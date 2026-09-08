// SimulatedDay.cs
//
// What this file does: it puts a whole ATM in one process - a host, a machine, cassettes,
// a dispenser and a line between them that can be cut on demand - and lets a caller run a
// day through it and then ask whether the books balance.
//
// Why this belongs here and not in the tests: it did live in a test file, and that was the
// wrong home for it. The same harness is needed by three callers - the end-to-end tests,
// scripts/check.sh, and the scenario runner of Phase 4 - and a copy per caller is three
// slightly different ATMs that drift apart. It sits in Atm.Audit because everything here
// is about looking at the system from outside it.
//
// Two properties this harness must have, and both are rule 5 (docs/proje-kurallari.md):
//
//   It is DETERMINISTIC. The clock is virtual, nothing sleeps, and the same sequence of
//   calls produces the same journals byte for byte. A run that cannot be repeated cannot
//   be a finding.
//
//   The line can be cut PRECISELY. LineIsDown, AnswersAreLost, DropRequestsOfType and
//   LoseAnswersForType aim a failure at one message rather than at the whole conversation.
//   That is the only way to ask "what happens when exactly the advice is lost" and get an
//   answer about that and nothing else.
//
// What it is NOT: a network. The host and the terminal talk through a method call here,
// not through a socket. That is on purpose - the socket has its own tests, and putting it
// in the middle of a conservation run would add a source of failure that has nothing to
// do with the question being asked.

using Atm.Host;
using Atm.Protocol;
using Atm.Terminal;

namespace Atm.Audit;

/// <summary>One host, one machine, and a line between them that can be cut.</summary>
public class SimulatedDay
{
    /// <summary>The demo accounts, as a balance file would name them.</summary>
    public static readonly IReadOnlyList<(string AccountId, string Pan)> DemoAccounts =
    [
        ("TR-DEMO-001", "4111111111111111"),
        ("TR-DEMO-002", "4222222222222220"),
        ("TR-DEMO-003", "4333333333333339"),
    ];

    private int _queryStan;

    public SimulatedDay(bool customerTakesCash = true, Hardening? hardening = null)
    {
        Hardening = hardening ?? Hardening.Full;
        Clock = VirtualClock.StartOfBusinessDay();

        // The host's two durable things are built here and kept, so that the host itself
        // can be thrown away and rebuilt over them. That is what a restart IS: the files
        // survive, the memory does not (KARAR-047).
        Accounts = InMemoryAccountStore.WithDemoAccounts();
        Pins = PinVerifier.WithDemoCards();
        HostJournal = new InMemoryJournal();
        Host = new HostService(Accounts, Pins, HostJournal, Clock, Hardening);
        Cassettes = CassetteSet.Standard();
        Dispenser = new FakeCashDispenser(Cassettes);
        Pending = new PendingHostMessages();
        CustomerTakesCash = customerTakesCash;
        StartingCash = Dispenser.Position.Total;
        StartingPosition = Dispenser.Position;
        StartingBalances = AllBalances();

        // ONE record for the whole machine, shared by both flows. Two journals would be
        // two machines as far as the reconciliation is concerned, and a withdrawal and a
        // deposit that happened at the same ATM would reconcile against different books.
        Journal = new InMemoryTerminalJournal();

        // ONE business day for the whole machine, for the same reason as one journal: a
        // withdrawal and a deposit that believed different dates would be reconciled
        // against different days, and nothing in the machine would say so (KARAR-044).
        Day = new BusinessDay(Clock);

        Flow = new WithdrawalFlow(Ask, Dispenser, Cassettes, Clock, Pending,
            () => CustomerTakesCash, Journal, day: Day, hardening: Hardening);

        Deposits = new DepositFlow(Ask, Dispenser, Clock, Pending, Journal, day: Day,
            hardening: Hardening);

        Cutover = new CutoverFlow(Ask, Clock, Pending, Journal, Day);
    }

    public VirtualClock Clock { get; }

    /// <summary>Which of the domain's rules this day obeys. Full unless a caller says otherwise.</summary>
    public Hardening Hardening { get; } = Hardening.Full;

    /// <summary>The host. Replaced, not mutated, when the day restarts it.</summary>
    public HostService Host { get; private set; }

    /// <summary>The accounts - the part of the host that would be a file in the real one.</summary>
    public IAccountStore Accounts { get; }

    /// <summary>The PIN store. Survives a restart the same way.</summary>
    public PinVerifier Pins { get; }

    /// <summary>The host's record. Append-only, and the only thing a restart rebuilds from.</summary>
    public IJournal HostJournal { get; }
    public CassetteSet Cassettes { get; }
    public FakeCashDispenser Dispenser { get; }
    public PendingHostMessages Pending { get; }
    public WithdrawalFlow Flow { get; }
    public DepositFlow Deposits { get; }

    /// <summary>The day this machine is working in. Moves only when the cutover succeeds.</summary>
    public BusinessDay Day { get; }

    /// <summary>Closes the day against the host. See docs/protocol.md section 4.7.</summary>
    public CutoverFlow Cutover { get; }

    /// <summary>The machine's own record - one for the whole machine, both flows write here.</summary>
    public ITerminalJournal Journal { get; }

    /// <summary>What the machine held when the day started, in kurus.</summary>
    public long StartingCash { get; private set; }

    /// <summary>Where every note was when the day started.</summary>
    public CashPosition StartingPosition { get; private set; } = new(0, 0, 0, 0);

    /// <summary>The accounts as the day started - the balance file, cut at open.</summary>
    public IReadOnlyList<AccountBalance> StartingBalances { get; private set; } = [];

    /// <summary>Whether somebody picks the notes up before the machine gives up on them.</summary>
    public bool CustomerTakesCash { get; set; }

    /// <summary>The message never reaches the host at all.</summary>
    public bool LineIsDown { get; set; }

    /// <summary>The host hears it and does the work; the answer never comes back.</summary>
    public bool AnswersAreLost { get; set; }

    /// <summary>Lose the answer to one kind of message only.</summary>
    public string LoseAnswersForType { get; set; } = "";

    /// <summary>Drop one kind of message before it reaches the host at all.</summary>
    public string DropRequestsOfType { get; set; } = "";

    /// <summary>How many messages actually reached the host.</summary>
    public int MessagesDelivered { get; private set; }

    /// <summary>Puts the line back and lets everything through again.</summary>
    public void LineComesBack()
    {
        LineIsDown = false;
        AnswersAreLost = false;
        LoseAnswersForType = "";
        DropRequestsOfType = "";
    }

    /// <summary>
    /// Switches the host off and on again: same accounts, same journal, no memory.
    /// </summary>
    /// <remarks>
    /// The terminal is deliberately NOT restarted here. A scenario that restarted both at
    /// once could not tell which side recovered - and the interesting failures are exactly
    /// the ones where one side remembers something the other has forgotten.
    /// </remarks>
    public void RestartHost() =>
        Host = new HostService(Accounts, Pins, HostJournal, Clock, Hardening);

    /// <summary>
    /// Takes the opening position again, as it stands now. Setup only.
    /// </summary>
    /// <remarks>
    /// A scenario that starts with an almost-empty cassette has to say so before anything
    /// happens, and the opening position has to be taken AFTER that - otherwise the notes
    /// removed during setup look exactly like notes that went missing during the day.
    ///
    /// It refuses once the machine has done anything, and that refusal is the whole safety
    /// of the method: re-baselining mid-day would erase a real difference by moving the
    /// line it is measured from.
    /// </remarks>
    public void RebaselineOpening()
    {
        if (Journal.Entries.Count > 0)
        {
            throw new InvalidOperationException(
                "Açılış konumu yalnızca ilk işlemden önce yeniden alınabilir; " +
                $"makine zaten {Journal.Entries.Count} satır yazmış.");
        }

        StartingCash = Dispenser.Position.Total;
        StartingPosition = Dispenser.Position;
        StartingBalances = AllBalances();
    }

    /// <summary>The three demo accounts as they stand right now.</summary>
    public IReadOnlyList<AccountBalance> AllBalances() =>
        DemoAccounts.Select(a =>
            new AccountBalance(a.AccountId, LedgerOf(a.Pan), LedgerOf(a.Pan) - AvailableOf(a.Pan)))
        .ToList();

    /// <summary>Everything the checker is allowed to look at, read at this instant.</summary>
    public AuditSnapshot Snapshot(AuditMoment moment = AuditMoment.AtRest) => new()
    {
        Moment = moment,
        CashAtStart = StartingPosition,
        CashNow = Dispenser.Position,
        LedgerAtStart = StartingBalances,
        LedgerNow = AllBalances(),
        HostJournal = Host.Journal.Entries,
        TerminalJournal = Journal.Entries,
        OpenAuthorisations = Host.OpenAuthorisations.Select(k => k.ToString()).ToList(),
        OpenDeposits = Host.OpenDeposits.Select(k => k.ToString()).ToList(),
    };

    /// <summary>What this account's history adds up to, in kurus.</summary>
    public long LedgerOf(string pan) => Balance(pan).Ledger;

    /// <summary>What this account may actually spend right now, in kurus.</summary>
    public long AvailableOf(string pan) => Balance(pan).Available;

    /// <summary>
    /// Asks the host one question, through a line that may be cut.
    /// </summary>
    /// <remarks>
    /// The two failures below are deliberately indistinguishable from the terminal's side,
    /// because they are indistinguishable in life: in one the host never heard the
    /// question, in the other it heard it and did the work. Both look like silence. Every
    /// rule about reversals exists because of that pair.
    /// </remarks>
    protected Envelope? Ask(Envelope request, TimeSpan timeout)
    {
        if (LineIsDown || request.Type == DropRequestsOfType)
        {
            Clock.Advance(timeout);
            return null;
        }

        MessagesDelivered++;
        var answer = Host.Handle(request);

        if (AnswersAreLost || request.Type == LoseAnswersForType)
        {
            Clock.Advance(timeout);
            return null;
        }

        return answer;
    }

    /// <summary>
    /// The same line, offered to callers that build their own flow on top of this day -
    /// the screen flow, for instance, which needs a host it can lose messages to.
    /// </summary>
    public Envelope? AskHost(Envelope request, TimeSpan timeout) => Ask(request, timeout);

    private BalanceResponseBody Balance(string pan) =>
        Host.Handle(MessageCodec.Envelope(MessageType.BalanceRequest,
            new TransactionKey("QUERY", "2026-01-05", ++_queryStan),
            Clock.UtcNow, new BalanceRequestBody(pan)))
            .Body<BalanceResponseBody>();
}
