// DepositFlow.cs
//
// What this file does: it performs one deposit from the terminal's side, from "the
// customer pushed some notes in" to "the host knows where those notes ended up".
//
// It is the mirror of WithdrawalFlow, and reading the two side by side is the fastest way
// to understand why this project exists. The shapes match: count first, ask the host, move
// the paper, report what happened. But one thing is reversed, and everything else follows
// from it:
//
//   In a withdrawal, the money leaves the machine LAST. Until the notes are at the mouth,
//   nothing has happened that cannot be undone - so an unanswered authorisation is undone
//   with a reversal.
//
//   In a deposit, the money is inside the machine FIRST. The customer's banknotes are in
//   the escrow before the host is even asked. And once they are stacked into a drawer they
//   cannot come back out - so an unanswered commit is not undone, it is REPEATED
//   (KARAR-040).
//
// Getting that backwards is the most expensive mistake available here: treating an
// unanswered commit as "it did not happen" and handing the money back means handing the
// customer a second bundle of banknotes for money that is already in a drawer.
//
// The other decision this file rests on is the ORDER inside Complete: the escrow is emptied
// into the drawers BEFORE the host is told (KARAR-038). A note in a drawer cannot be
// un-stacked, but a message can be sent again for ever, so the uncertainty is put on the
// side that can be repeated.
//
// What this file does NOT do: draw anything, and never ask a function whether the customer
// confirmed. Confirmation arrives as an event, later - which is why this flow is split in
// two the same way the withdrawal is (KARAR-036).

using Atm.Protocol;

namespace Atm.Terminal;

/// <summary>How one deposit ended, in terms the screen can use.</summary>
public enum DepositEnding
{
    /// <summary>Everything reached a drawer and the account rose by all of it.</summary>
    Credited,

    /// <summary>Some reached a drawer; the rest is stuck in the machine.</summary>
    PartlyCredited,

    /// <summary>The notes went back out. Nothing was credited, and nothing was lost.</summary>
    Returned,

    /// <summary>Nothing reached a drawer and nothing came back out. An engineer is needed.</summary>
    Jammed,

    /// <summary>This machine could not take any of the notes it was given.</summary>
    NothingAccepted,

    /// <summary>The host said no. The notes went back out.</summary>
    RefusedByHost,

    /// <summary>The host did not answer. The notes went back out - they were never the bank's.</summary>
    NoAnswerFromHost,
}

/// <summary>A deposit whose notes are counted and waiting in escrow for a confirmation.</summary>
/// <param name="Key">Which transaction this is.</param>
/// <param name="Pan">The card, so the record can carry it masked.</param>
/// <param name="Counted">What is in escrow, in kurus.</param>
public sealed record DepositHandle(TransactionKey Key, string Pan, long Counted);

/// <summary>What happened, and what the screen has to say about it.</summary>
/// <param name="Ending">Which of the seven endings this was.</param>
/// <param name="Credited">What the account rose by, in kurus.</param>
/// <param name="Returned">What went back out to the customer, in kurus.</param>
/// <param name="Jammed">What is stuck in the mechanism, in kurus.</param>
/// <param name="Rc">The host's response code, or empty when the host was never asked.</param>
/// <param name="CommitQueued">True when the report is still waiting to be acknowledged.</param>
public sealed record DepositResult(
    DepositEnding Ending, long Credited, long Returned, long Jammed, string Rc, bool CommitQueued);

/// <summary>
/// How the first half of a deposit ended: either it is over already, or notes are in escrow
/// and the customer has to be asked.
/// </summary>
/// <param name="Finished">Set when the deposit ended before anything was confirmed.</param>
/// <param name="InEscrow">Set when the host agreed and the notes are waiting.</param>
/// <param name="Refused">Notes the machine pushed straight back out, in kurus.</param>
public sealed record DepositStart(
    DepositResult? Finished, DepositHandle? InEscrow, long Refused);

/// <summary>One deposit, from the terminal's side.</summary>
public sealed class DepositFlow
{
    /// <summary>How long the terminal waits for a deposit authorisation.</summary>
    public static readonly TimeSpan AuthTimeout = TimeSpan.FromSeconds(30);

    /// <summary>How long it waits for a commit to be acknowledged.</summary>
    public static readonly TimeSpan CommitTimeout = TimeSpan.FromSeconds(30);

    private readonly Func<Envelope, TimeSpan, Envelope?> _ask;
    private readonly ICashAcceptor _acceptor;
    private readonly IClock _clock;
    private readonly IPendingHostMessages _pending;
    private readonly ITerminalJournal _journal;
    private readonly string _terminalId;
    private readonly BusinessDay _day;
    private readonly Hardening _hardening;

    public DepositFlow(
        Func<Envelope, TimeSpan, Envelope?> ask,
        ICashAcceptor acceptor,
        IClock clock,
        IPendingHostMessages pending,
        ITerminalJournal? journal = null,
        string terminalId = "ATM-01",
        BusinessDay? day = null,
        Hardening? hardening = null)
    {
        _ask = ask;
        _acceptor = acceptor;
        _clock = clock;
        _pending = pending;
        _journal = journal ?? new InMemoryTerminalJournal();
        _terminalId = terminalId;
        _day = day ?? new BusinessDay(clock);
        _hardening = hardening ?? Hardening.Full;
    }

    /// <summary>This machine's own record of what it did. Append only.</summary>
    public ITerminalJournal Journal => _journal;

    /// <summary>
    /// The first half: count the notes into escrow and ask the host whether this account
    /// may have them. Nothing is credited and nothing is stacked.
    /// </summary>
    public DepositStart Begin(string pan, IReadOnlyList<NoteBundle> notes, int stan)
    {
        var key = new TransactionKey(_terminalId, BusinessDate(), stan);

        // 1. Count. Notes this machine has no drawer for are pushed straight back out -
        // asked here, before the host, so that a customer is never asked to confirm a
        // deposit that includes a note the machine cannot keep.
        var counted = _acceptor.AcceptIntoEscrow(notes);

        _journal.Append(_clock.UtcNow, key, TerminalEvent.DepositCounted, counted.Counted, pan,
            counted.Refused > 0 ? $"{counted.Refused} kabul edilmedi" : "");

        if (counted.Counted == 0)
        {
            return new DepositStart(
                new DepositResult(DepositEnding.NothingAccepted, 0, 0, 0, Rc: "", CommitQueued: false),
                InEscrow: null, counted.Refused);
        }

        // 2. Ask. This message moves nothing on either side; it exists so that a refusal
        // happens while the notes can still be handed back (KARAR-038).
        var request = MessageCodec.Envelope(MessageType.DepositAuthRequest, key, _clock.UtcNow,
            new DepositAuthRequestBody(pan, counted.Counted,
                counted.Accepted.Select(b => new DenominationLine(b.Denomination, b.Count)).ToList()));

        _journal.Append(_clock.UtcNow, key, TerminalEvent.DepositAuthRequested,
            counted.Counted, pan);

        var answer = _ask(request, AuthTimeout);

        if (answer is null)
        {
            // No answer. Unlike a withdrawal, this is NOT the dangerous branch: the notes
            // are in escrow and escrow is the customer's. They go back out. A reversal is
            // sent so that the host does not sit on a deposit it agreed to and never heard
            // the end of.
            if (!_hardening.TimeoutMakesReversal)
            {
                // Silence taken for a yes - the mirror of the withdrawal mistake, pointing
                // the other way. The notes go on into a drawer on the strength of an answer
                // that never came, and the commit that follows will be refused by a host
                // that never agreed to this deposit at all.
                _journal.Append(_clock.UtcNow, key, TerminalEvent.DepositAuthNoAnswer,
                    counted.Counted, pan, "cevap yok: onay sayıldı");

                return new DepositStart(Finished: null,
                    new DepositHandle(key, pan, counted.Counted), counted.Refused);
            }

            _journal.Append(_clock.UtcNow, key, TerminalEvent.DepositAuthNoAnswer,
                counted.Counted, pan, "cevap yok: para iade ediliyor");

            var returned = GiveBack(key, pan, "cevap gelmedi");
            QueueReversal(key, counted.Counted, ReversalReason.Timeout);

            return new DepositStart(
                new DepositResult(DepositEnding.NoAnswerFromHost, 0, returned, 0,
                    Rc: "", CommitQueued: false),
                InEscrow: null, counted.Refused);
        }

        var agreed = answer.Body<DepositAuthResponseBody>();

        if (agreed.Rc != ResponseCode.Approved)
        {
            _journal.Append(_clock.UtcNow, key, TerminalEvent.DepositAuthRefused,
                counted.Counted, pan, agreed.Rc);

            var returned = GiveBack(key, pan, $"host reddetti ({agreed.Rc})");

            return new DepositStart(
                new DepositResult(DepositEnding.RefusedByHost, 0, returned, 0,
                    agreed.Rc, CommitQueued: false),
                InEscrow: null, counted.Refused);
        }

        _journal.Append(_clock.UtcNow, key, TerminalEvent.DepositAuthApproved,
            counted.Counted, pan);

        return new DepositStart(Finished: null,
            new DepositHandle(key, pan, counted.Counted), counted.Refused);
    }

    /// <summary>
    /// The second half: do what the customer decided, and make sure the host hears it.
    /// </summary>
    /// <param name="handle">What <see cref="Begin"/> returned.</param>
    /// <param name="confirmed">
    /// True when the customer confirmed. False means they changed their mind, and their
    /// notes come back out - which is only possible because nothing has been stacked yet.
    /// </param>
    public DepositResult Complete(DepositHandle handle, bool confirmed)
    {
        var (key, pan, counted) = handle;

        if (!confirmed)
        {
            var back = GiveBack(key, pan, "müşteri vazgeçti");

            // Instruction section 4.7 in the negative: a machine that credits what it
            // COUNTED rather than what reached a drawer tells the host this money is the
            // bank's - while the notes are going back into the customer's hand.
            var told = _hardening.CreditWhatReachedADrawer
                ? Tell(key, pan, DepositOutcome.Returned, stacked: 0, returned: back, jammed: 0)
                : Tell(key, pan, DepositOutcome.Stacked, stacked: back, returned: 0, jammed: 0);

            return new DepositResult(DepositEnding.Returned, 0, back, 0,
                ResponseCode.Approved, told);
        }

        // The intention is written down BEFORE the paper moves (KARAR-041). If the power
        // goes here, the record says a stack was started and never finished - which is a
        // known uncertainty, and a known uncertainty can be reported.
        _journal.Append(_clock.UtcNow, key, TerminalEvent.DepositStacking, counted, pan,
            "escrow kasete alınıyor");

        // The point of no return. AFTER this line the notes are the bank's; before it they
        // were the customer's. Nothing between the two is negotiable, which is why the
        // host is told afterwards and not before (KARAR-038).
        var stacked = _acceptor.Stack();

        if (stacked.Stacked > 0)
        {
            _journal.Append(_clock.UtcNow, key, TerminalEvent.DepositStacked,
                stacked.Stacked, pan);
        }

        if (stacked.Jammed > 0)
        {
            _journal.Append(_clock.UtcNow, key, TerminalEvent.DepositJammed,
                stacked.Jammed, pan, "mekanizmada sıkıştı");
        }

        var outcome = Describe(stacked);

        var queued = _hardening.CreditWhatReachedADrawer
            ? Tell(key, pan, outcome, stacked.Stacked, returned: 0, jammed: stacked.Jammed)
            : Tell(key, pan, DepositOutcome.Stacked, stacked: counted, returned: 0, jammed: 0);

        return new DepositResult(
            Ending(stacked), stacked.Stacked, 0, stacked.Jammed, ResponseCode.Approved, queued);
    }

    /// <summary>This machine's business date, as the envelope carries it.</summary>
    /// <summary>The day this machine is working in. Moved only by a cutover (KARAR-044).</summary>
    public BusinessDay Day => _day;

    /// <summary>The business date every new transaction key gets.</summary>
    public string BusinessDate() => _day.Current;

    /// <summary>Hands the escrow back and writes it down.</summary>
    private long GiveBack(TransactionKey key, string pan, string why)
    {
        var back = _acceptor.ReturnFromEscrow();

        if (back > 0)
        {
            _journal.Append(_clock.UtcNow, key, TerminalEvent.DepositReturned, back, pan, why);
        }

        return back;
    }

    /// <summary>
    /// Reports what happened to the paper, and keeps the message until the host answers.
    /// </summary>
    /// <remarks>
    /// Queued whatever the outcome, including RETURNED. A deposit the host agreed to and
    /// never heard the end of leaves it holding an open deposit for ever - and at day end
    /// that is a difference nobody can explain, for money that is safely back in a
    /// customer's pocket.
    /// </remarks>
    private bool Tell(TransactionKey key, string pan, string outcome,
        long stacked, long returned, long jammed)
    {
        var advice = MessageCodec.Envelope(MessageType.DepositCommitAdvice, key, _clock.UtcNow,
            new DepositCommitBody(key.ToString(), outcome, stacked, returned, jammed));

        _journal.Append(_clock.UtcNow, key, TerminalEvent.DepositCommitSent, stacked, pan, outcome);

        var acknowledged = _ask(advice, CommitTimeout);

        if (acknowledged is null && _hardening.RetryUntilAcknowledged)
        {
            _pending.Add(advice, _clock.UtcNow);
            _journal.Append(_clock.UtcNow, key, TerminalEvent.DepositCommitQueued, stacked, pan,
                "onay gelmedi, kuyrukta");
            return true;
        }

        if (acknowledged is null)
        {
            _journal.Append(_clock.UtcNow, key, TerminalEvent.DepositCommitQueued, stacked, pan,
                "onay gelmedi - kesinleştirme unutuldu");
            return false;
        }

        _journal.Append(_clock.UtcNow, key, TerminalEvent.DepositCommitAcknowledged, stacked, pan);
        return false;
    }

    private void QueueReversal(TransactionKey key, long amount, string reason)
    {
        var reversal = MessageCodec.Envelope(MessageType.ReversalRequest, key, _clock.UtcNow,
            new ReversalRequestBody(key.ToString(), amount, reason));

        var answer = _ask(reversal, AuthTimeout);

        if (answer is null && _hardening.RetryUntilAcknowledged)
        {
            _pending.Add(reversal, _clock.UtcNow);
            _journal.Append(_clock.UtcNow, key, TerminalEvent.ReversalQueued, amount, note: reason);
        }
        else if (answer is null)
        {
            _journal.Append(_clock.UtcNow, key, TerminalEvent.ReversalQueued, amount,
                note: reason + " - ters kayıt unutuldu");
        }
        else
        {
            _journal.Append(_clock.UtcNow, key, TerminalEvent.ReversalAcknowledged, amount,
                note: reason);
        }
    }

    /// <summary>The word for the host: what the machine says happened to the paper.</summary>
    private static string Describe(StackResult stacked)
    {
        if (stacked.Partial)
        {
            return DepositOutcome.Partial;
        }

        return stacked.NothingStacked ? DepositOutcome.Jammed : DepositOutcome.Stacked;
    }

    /// <summary>The word for the screen: what the customer experienced.</summary>
    private static DepositEnding Ending(StackResult stacked)
    {
        if (stacked.Partial)
        {
            return DepositEnding.PartlyCredited;
        }

        return stacked.NothingStacked ? DepositEnding.Jammed : DepositEnding.Credited;
    }
}
