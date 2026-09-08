// ConservationTests.cs
//
// What these tests are for: the conservation checker is the one piece of this project
// whose job is to notice that something is wrong. A checker that quietly passes
// everything looks exactly like a system with no faults in it, and it would make every
// other test in this repository meaningless - they all end by asking it whether the books
// balance.
//
// So these tests are written the other way round from most of the others. Instead of
// running the machine and asking whether the answer is clean, they hand the checker a
// picture that is KNOWN to be broken and insist that it says so, one breakage at a time.
// If a check is ever weakened or deleted, exactly one of these goes red and names it.
//
// The snapshots here are built by hand rather than produced by a run. That is deliberate:
// some of them are pictures the real machine cannot currently produce - money appearing
// out of nowhere, a debit with no authorisation behind it. Those are the situations the
// checker exists for, and they cannot be reached by driving the flow.

using Atm.Audit;
using Atm.Host;
using Atm.Protocol;
using Atm.Terminal;
using Xunit;

namespace Atm.Tests;

public class ConservationTests
{
    private const string Card = "4111111111111111";
    private static readonly DateTimeOffset At = new(2026, 1, 5, 9, 0, 0, TimeSpan.Zero);
    private static readonly TransactionKey Key = new("ATM-01", "2026-01-05", 101);
    private static readonly TransactionKey OtherKey = new("ATM-01", "2026-01-05", 102);

    /// <summary>A machine holding 1000 lira in its drawers and nothing anywhere else.</summary>
    private static readonly CashPosition Loaded = new(100_000, 0, 0, 0);

    private static AuditSnapshot Snapshot(
        CashPosition? now = null,
        long ledgerNow = 250_000,
        long holdNow = 0,
        IReadOnlyList<JournalEntry>? host = null,
        IReadOnlyList<TerminalJournalEntry>? terminal = null,
        IReadOnlyList<string>? open = null,
        AuditMoment moment = AuditMoment.AtRest) => new()
        {
            Moment = moment,
            CashAtStart = Loaded,
            CashNow = now ?? Loaded,
            LedgerAtStart = [new AccountBalance("TR-DEMO-001", 250_000, 0)],
            LedgerNow = [new AccountBalance("TR-DEMO-001", ledgerNow, holdNow)],
            HostJournal = host ?? [],
            TerminalJournal = terminal ?? [],
            OpenAuthorisations = open ?? [],
        };

    private static JournalEntry HostLine(string type, string rc, long amount, TransactionKey? key = null, long seq = 1)
        => new() { Seq = seq, At = At, Key = key ?? Key, Type = type, Rc = rc, Amount = amount, Pan = Card };

    private static TerminalJournalEntry TerminalLine(string @event, long amount, TransactionKey? key = null, long seq = 1)
        => new() { Seq = seq, At = At, Key = key ?? Key, Event = @event, Amount = amount, Pan = Card };

    /// <summary>An approved authorisation and the advice that posted it, for one amount.</summary>
    private static JournalEntry[] AWithdrawalOnTheHost(long amount, TransactionKey? key = null) =>
    [
        HostLine(MessageType.WithdrawalAuthRequest, ResponseCode.Approved, amount, key, 1),
        HostLine(MessageType.DispenseAdvice, ResponseCode.Approved, amount, key, 2),
    ];

    /// <summary>The same withdrawal, seen from the machine: notes out, notes taken.</summary>
    private static TerminalJournalEntry[] AWithdrawalOnTheTerminal(long amount, TransactionKey? key = null) =>
    [
        TerminalLine(TerminalEvent.CashPresented, amount, key, 1),
        TerminalLine(TerminalEvent.CashTaken, amount, key, 2),
    ];

    [Fact]
    public void AMachineThatHasDoneNothingHasNothingToExplain()
    {
        var report = ConservationChecker.Check(Snapshot());

        Assert.True(report.IsClean, report.ToText());
        Assert.Empty(report.Findings);
    }

    [Fact]
    public void AWithdrawalThatWentRightIsClean()
    {
        // 350 lira left the drawers, a hand took it, and the account fell by the same.
        var report = ConservationChecker.Check(Snapshot(
            now: new CashPosition(65_000, 0, 35_000, 0),
            ledgerNow: 215_000,
            host: AWithdrawalOnTheHost(35_000),
            terminal: AWithdrawalOnTheTerminal(35_000)));

        Assert.True(report.IsClean, report.ToText());
    }

    [Fact]
    public void MoneyThatAppearedInsideTheMachineIsReported()
    {
        // The four buckets add up to more than the machine was loaded with.
        var report = ConservationChecker.Check(Snapshot(
            now: new CashPosition(100_000, 0, 5_000, 0)));

        var finding = Assert.Single(report.Violations, f => f.Code == AuditCode.CashTotal);
        Assert.Equal(5_000, finding.Amount);
    }

    [Fact]
    public void CashHandedOverWithNoDebitBehindItIsReported()
    {
        // The customer is holding 350 lira and no account is any lighter.
        var report = ConservationChecker.Check(Snapshot(
            now: new CashPosition(65_000, 0, 35_000, 0),
            ledgerNow: 250_000,
            terminal: AWithdrawalOnTheTerminal(35_000)));

        Assert.False(report.IsClean);
        Assert.Contains(report.Violations, f => f.Code == AuditCode.CustomerVersusLedger);
    }

    [Fact]
    public void TheSameDifferenceIsExplainedWhileTheReportIsStillOnItsWay()
    {
        // Identical picture to the test above, with one line added: the terminal knows its
        // report has not been acknowledged. This is what every withdrawal looks like for a
        // moment, and it is exactly what must NOT be alarmed about.
        var report = ConservationChecker.Check(Snapshot(
            now: new CashPosition(65_000, 0, 35_000, 0),
            ledgerNow: 250_000,
            terminal:
            [
                .. AWithdrawalOnTheTerminal(35_000),
                TerminalLine(TerminalEvent.AdviceQueued, 35_000, seq: 3),
            ]));

        Assert.True(report.IsClean, report.ToText());
        Assert.NotEmpty(report.Explained);
    }

    [Fact]
    public void OnceTheReportIsAcknowledgedTheDifferenceStopsBeingExplained()
    {
        // The host said it heard the report - so the debit should be there, and it is not.
        // This is the test that keeps the "explained" door from being propped open: a
        // checker that treats any queued line as a permanent excuse passes the test above
        // and fails this one.
        var report = ConservationChecker.Check(Snapshot(
            now: new CashPosition(65_000, 0, 35_000, 0),
            ledgerNow: 250_000,
            terminal:
            [
                .. AWithdrawalOnTheTerminal(35_000),
                TerminalLine(TerminalEvent.AdviceQueued, 35_000, seq: 3),
                TerminalLine(TerminalEvent.AdviceAcknowledged, 35_000, seq: 4),
            ]));

        Assert.False(report.IsClean);
        Assert.Contains(report.Violations, f => f.Code == AuditCode.CustomerVersusLedger);
    }

    [Fact]
    public void ADebitWithNoApprovedAuthorisationBehindItIsReported()
    {
        // The ledger moved and the record holds no promise that allowed it.
        var report = ConservationChecker.Check(Snapshot(
            now: new CashPosition(65_000, 0, 35_000, 0),
            ledgerNow: 215_000,
            host: [HostLine(MessageType.DispenseAdvice, ResponseCode.Approved, 35_000)],
            terminal: AWithdrawalOnTheTerminal(35_000)));

        var finding = Assert.Single(report.Violations,
            f => f.Code == AuditCode.DebitWithoutAuthorisation);
        Assert.Equal(35_000, finding.Amount);
    }

    [Fact]
    public void ARefusedAuthorisationDoesNotCountAsAPromise()
    {
        // Same as above with an authorisation line present - but a refused one. A checker
        // that matched on the message type without reading the response code would pass.
        var report = ConservationChecker.Check(Snapshot(
            now: new CashPosition(65_000, 0, 35_000, 0),
            ledgerNow: 215_000,
            host:
            [
                HostLine(MessageType.WithdrawalAuthRequest, ResponseCode.InsufficientFunds, 35_000, seq: 1),
                HostLine(MessageType.DispenseAdvice, ResponseCode.Approved, 35_000, seq: 2),
            ],
            terminal: AWithdrawalOnTheTerminal(35_000)));

        Assert.Contains(report.Violations, f => f.Code == AuditCode.DebitWithoutAuthorisation);
    }

    [Fact]
    public void TheTwoRecordsDisagreeingAboutOneTransactionIsReported()
    {
        // The host posted 350; the machine says only 200 was taken. Both books balance
        // against themselves - only putting them side by side shows the 150.
        var report = ConservationChecker.Check(Snapshot(
            now: new CashPosition(65_000, 0, 20_000, 15_000),
            ledgerNow: 215_000,
            host: AWithdrawalOnTheHost(35_000),
            terminal:
            [
                TerminalLine(TerminalEvent.CashPresented, 35_000, seq: 1),
                TerminalLine(TerminalEvent.CashTaken, 20_000, seq: 2),
                TerminalLine(TerminalEvent.CashRetracted, 15_000, seq: 3),
            ]));

        var finding = Assert.Single(report.Violations, f => f.Code == AuditCode.Reconciliation);
        Assert.Equal(15_000, finding.Amount);
    }

    [Fact]
    public void TheRecordAndTheDispenserDisagreeingIsReported()
    {
        // The dispenser says a hand took 350 lira; the terminal's own record says nothing
        // about it. One of the two is lying and the machine cannot tell which.
        var report = ConservationChecker.Check(Snapshot(
            now: new CashPosition(65_000, 0, 35_000, 0),
            ledgerNow: 215_000,
            host: AWithdrawalOnTheHost(35_000)));

        Assert.Contains(report.Violations, f => f.Code == AuditCode.RecordVersusDispenser);
    }

    [Fact]
    public void RetractedCashThatTheRecordDoesNotMentionIsReported()
    {
        var report = ConservationChecker.Check(Snapshot(
            now: new CashPosition(65_000, 0, 0, 35_000),
            terminal: [TerminalLine(TerminalEvent.CashPresented, 35_000)]));

        Assert.Contains(report.Violations,
            f => f.Code == AuditCode.RecordVersusDispenser && f.Amount == 35_000);
    }

    [Fact]
    public void APromiseStillStandingIsOrdinaryDuringTheDay()
    {
        var report = ConservationChecker.Check(Snapshot(
            holdNow: 35_000,
            open: [Key.ToString()],
            moment: AuditMoment.AtRest));

        Assert.True(report.IsClean, report.ToText());
    }

    [Fact]
    public void APromiseStillStandingAtDayEndIsAFinding()
    {
        var report = ConservationChecker.Check(Snapshot(
            holdNow: 35_000,
            open: [Key.ToString()],
            host: [HostLine(MessageType.WithdrawalAuthRequest, ResponseCode.Approved, 35_000)],
            moment: AuditMoment.EndOfDay));

        var finding = Assert.Single(report.Violations, f => f.Code == AuditCode.OpenAuthorisation);
        Assert.Equal(35_000, finding.Amount);
    }

    [Fact]
    public void APromiseIsExplainedWhileItsReversalIsStillTrying()
    {
        var report = ConservationChecker.Check(Snapshot(
            holdNow: 35_000,
            open: [Key.ToString()],
            terminal: [TerminalLine(TerminalEvent.ReversalQueued, 35_000)],
            moment: AuditMoment.EndOfDay));

        Assert.True(report.IsClean, report.ToText());
        Assert.Single(report.Explained, f => f.Code == AuditCode.OpenAuthorisation);
    }

    [Fact]
    public void TwoPromisesAtDayEndAreReportedWithTheirOwnAmountsNotTheTotal()
    {
        // A checker that printed the total hold on every line would show 700 lira twice
        // and turn a 700 lira problem into a 1400 lira one.
        var report = ConservationChecker.Check(Snapshot(
            holdNow: 70_000,
            open: [Key.ToString(), OtherKey.ToString()],
            host:
            [
                HostLine(MessageType.WithdrawalAuthRequest, ResponseCode.Approved, 35_000, Key, 1),
                HostLine(MessageType.WithdrawalAuthRequest, ResponseCode.Approved, 35_000, OtherKey, 2),
            ],
            moment: AuditMoment.EndOfDay));

        var open = report.Violations.Where(f => f.Code == AuditCode.OpenAuthorisation).ToList();

        Assert.Equal(2, open.Count);
        Assert.Equal(70_000, open.Sum(f => f.Amount));
    }

    [Fact]
    public void AReversalThatWasAcknowledgedNoLongerExplainsAStandingPromise()
    {
        var report = ConservationChecker.Check(Snapshot(
            holdNow: 35_000,
            open: [Key.ToString()],
            terminal:
            [
                TerminalLine(TerminalEvent.ReversalQueued, 35_000, seq: 1),
                TerminalLine(TerminalEvent.ReversalAcknowledged, 35_000, seq: 2),
            ],
            moment: AuditMoment.EndOfDay));

        Assert.False(report.IsClean);
        Assert.Contains(report.Violations, f => f.Code == AuditCode.OpenAuthorisation);
    }

    [Fact]
    public void AnExplanationBelongingToAnotherTransactionDoesNotCoverThisOne()
    {
        // A queued advice exists - for a different transaction. A checker that asked
        // "is anything in flight" instead of "is THIS in flight" would pass.
        var report = ConservationChecker.Check(Snapshot(
            now: new CashPosition(65_000, 0, 35_000, 0),
            ledgerNow: 250_000,
            terminal:
            [
                .. AWithdrawalOnTheTerminal(35_000),
                TerminalLine(TerminalEvent.AdviceQueued, 20_000, OtherKey, seq: 3),
            ]));

        Assert.False(report.IsClean);
        Assert.Contains(report.Violations, f => f.Code == AuditCode.Reconciliation);
    }

    // ---------- money coming IN ----------

    /// <summary>An agreed deposit and the commit that credited it, on the host's side.</summary>
    private static JournalEntry[] ADepositOnTheHost(long amount, TransactionKey? key = null) =>
    [
        HostLine(MessageType.DepositAuthRequest, ResponseCode.Approved, amount, key, 1),
        HostLine(MessageType.DepositCommitAdvice, ResponseCode.Approved, amount, key, 2),
    ];

    /// <summary>The same deposit seen from the machine: counted in, stacked away.</summary>
    private static TerminalJournalEntry[] ADepositOnTheTerminal(long amount, TransactionKey? key = null) =>
    [
        TerminalLine(TerminalEvent.DepositCounted, amount, key, 1),
        TerminalLine(TerminalEvent.DepositStacking, amount, key, 2),
        TerminalLine(TerminalEvent.DepositStacked, amount, key, 3),
    ];

    [Fact]
    public void ADepositThatWentRightIsClean()
    {
        // 500 lira came in through the mouth, went into a drawer, and the account rose.
        var report = ConservationChecker.Check(Snapshot(
            now: new CashPosition(150_000, 0, 0, 0, TakenFromCustomers: 50_000),
            ledgerNow: 300_000,
            host: ADepositOnTheHost(50_000),
            terminal: ADepositOnTheTerminal(50_000)));

        Assert.True(report.IsClean, report.ToText());
    }

    [Fact]
    public void MoneyThatArrivedWithoutComingThroughTheMouthIsReported()
    {
        // The drawers hold 500 lira more than they did, and nothing says a customer put it
        // there. Before deposits existed this was simply impossible; now it is the shape
        // that money-out-of-nowhere takes.
        var report = ConservationChecker.Check(Snapshot(
            now: new CashPosition(150_000, 0, 0, 0),
            ledgerNow: 300_000,
            host: ADepositOnTheHost(50_000),
            terminal: ADepositOnTheTerminal(50_000)));

        Assert.False(report.IsClean);
        Assert.Contains(report.Violations, f => f.Code == AuditCode.CashTotal);
    }

    [Fact]
    public void MoneyTheMachineTookInButNeverWroteDownIsReported()
    {
        // Found by sabotage: nothing was comparing the machine's incoming counter with the
        // record. Both were right, so they always agreed - and a check that cannot
        // disagree is not a check. This is what it looks like when they do: the mouth
        // counted 500 lira in, and the record says nothing came in at all.
        var report = ConservationChecker.Check(Snapshot(
            now: new CashPosition(150_000, 0, 0, 0, TakenFromCustomers: 50_000),
            ledgerNow: 300_000,
            host: ADepositOnTheHost(50_000),
            terminal:
            [
                TerminalLine(TerminalEvent.DepositStacking, 50_000, seq: 1),
                TerminalLine(TerminalEvent.DepositStacked, 50_000, seq: 2),
            ]));

        Assert.Contains(report.Violations,
            f => f.Code == AuditCode.RecordVersusDispenser && f.Amount == 50_000);
    }

    [Fact]
    public void ACreditWithNoAgreedDepositBehindItIsReported()
    {
        var report = ConservationChecker.Check(Snapshot(
            now: new CashPosition(150_000, 0, 0, 0, TakenFromCustomers: 50_000),
            ledgerNow: 300_000,
            host: [HostLine(MessageType.DepositCommitAdvice, ResponseCode.Approved, 50_000)],
            terminal: ADepositOnTheTerminal(50_000)));

        var finding = Assert.Single(report.Violations, f => f.Code == AuditCode.CreditWithoutDeposit);
        Assert.Equal(50_000, finding.Amount);
    }

    [Fact]
    public void TheTwoRecordsDisagreeingAboutADepositIsReported()
    {
        // The machine says 300 reached a drawer; the host credited 500.
        var report = ConservationChecker.Check(Snapshot(
            now: new CashPosition(130_000, 0, 0, 0, Jammed: 20_000, TakenFromCustomers: 50_000),
            ledgerNow: 300_000,
            host: ADepositOnTheHost(50_000),
            terminal:
            [
                TerminalLine(TerminalEvent.DepositCounted, 50_000, seq: 1),
                TerminalLine(TerminalEvent.DepositStacking, 50_000, seq: 2),
                TerminalLine(TerminalEvent.DepositStacked, 30_000, seq: 3),
                TerminalLine(TerminalEvent.DepositJammed, 20_000, seq: 4),
            ]));

        Assert.False(report.IsClean);
        Assert.Contains(report.Violations, f => f.Code == AuditCode.Reconciliation);
    }

    [Fact]
    public void MoneyStuckInTheMechanismIsNamedButNotAlarmedAbout()
    {
        var report = ConservationChecker.Check(Snapshot(
            now: new CashPosition(100_000, 0, 0, 0, Jammed: 50_000, TakenFromCustomers: 50_000),
            ledgerNow: 250_000,
            host:
            [
                HostLine(MessageType.DepositAuthRequest, ResponseCode.Approved, 50_000, seq: 1),
                HostLine(MessageType.DepositCommitAdvice, ResponseCode.Approved, 0, seq: 2),
            ],
            terminal:
            [
                TerminalLine(TerminalEvent.DepositCounted, 50_000, seq: 1),
                TerminalLine(TerminalEvent.DepositStacking, 50_000, seq: 2),
                TerminalLine(TerminalEvent.DepositJammed, 50_000, seq: 3),
            ],
            moment: AuditMoment.EndOfDay));

        // A jam is a real, named, physically explainable event. It is a difference and it
        // is reported - but it is not a violation, because the machine can say exactly
        // where the money is and why nobody can have it yet.
        Assert.True(report.IsClean, report.ToText());
        Assert.Single(report.Explained, f => f.Code == AuditCode.StuckMoney);
    }

    [Fact]
    public void AStackThatWasStartedAndNeverFinishedIsAViolation()
    {
        // KARAR-041. The machine wrote down that it was about to move the notes and then
        // said nothing more - a power cut in the one place it cannot be ruled out. Unlike
        // a jam, this one is NOT explainable: the machine does not know where the money is.
        var report = ConservationChecker.Check(Snapshot(
            now: new CashPosition(100_000, 0, 0, 0, InEscrow: 50_000, TakenFromCustomers: 50_000),
            terminal:
            [
                TerminalLine(TerminalEvent.DepositCounted, 50_000, seq: 1),
                TerminalLine(TerminalEvent.DepositStacking, 50_000, seq: 2),
            ]));

        var finding = Assert.Single(report.Violations, f => f.Code == AuditCode.StuckMoney);
        Assert.Equal(50_000, finding.Amount);
    }

    [Fact]
    public void MoneyLeftInEscrowAtDayEndIsAFinding()
    {
        var report = ConservationChecker.Check(Snapshot(
            now: new CashPosition(100_000, 0, 0, 0, InEscrow: 50_000, TakenFromCustomers: 50_000),
            terminal: [TerminalLine(TerminalEvent.DepositCounted, 50_000)],
            moment: AuditMoment.EndOfDay));

        Assert.Contains(report.Violations,
            f => f.Code == AuditCode.StuckMoney && f.Amount == 50_000);
    }

    [Fact]
    public void ADepositAgreedAndNeverFinishedIsAFindingAtDayEnd()
    {
        var report = ConservationChecker.Check(Snapshot(
            host: [HostLine(MessageType.DepositAuthRequest, ResponseCode.Approved, 50_000)],
            open: [],
            moment: AuditMoment.EndOfDay) with { OpenDeposits = [Key.ToString()] });

        var finding = Assert.Single(report.Violations, f => f.Code == AuditCode.OpenDeposit);
        Assert.Equal(50_000, finding.Amount);
    }

    [Fact]
    public void ThatSameDepositIsExplainedWhileItsReportIsStillQueued()
    {
        var report = ConservationChecker.Check((Snapshot(
            host: [HostLine(MessageType.DepositAuthRequest, ResponseCode.Approved, 50_000)],
            terminal:
            [
                TerminalLine(TerminalEvent.DepositCounted, 50_000, seq: 1),
                TerminalLine(TerminalEvent.DepositStacking, 50_000, seq: 2),
                TerminalLine(TerminalEvent.DepositStacked, 50_000, seq: 3),
                TerminalLine(TerminalEvent.DepositCommitQueued, 50_000, seq: 4),
            ],
            moment: AuditMoment.EndOfDay) with { OpenDeposits = [Key.ToString()] }));

        Assert.Empty(report.Violations.Where(f => f.Code == AuditCode.OpenDeposit));
    }

    [Fact]
    public void ACleanReportSaysNothingAndABrokenOneStopsTheCaller()
    {
        ConservationChecker.Check(Snapshot()).ThrowIfViolated();

        var broken = ConservationChecker.Check(Snapshot(
            now: new CashPosition(100_000, 0, 5_000, 0)));

        var error = Assert.Throws<ConservationViolationException>(() => broken.ThrowIfViolated());
        Assert.Contains(AuditCode.CashTotal, error.Message);
    }
}
