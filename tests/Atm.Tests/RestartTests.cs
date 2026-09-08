// RestartTests.cs
//
// What these tests are for: switching the HOST off and on again. Not the terminal - that
// is PersistenceTests, and it is about the queue. This file is about the bank side: the
// balances, the promises that were never closed, and the days that were already settled.
//
// Why it matters more than it sounds: before Phase 3f the host's accounts lived only in
// memory. A restart handed every customer their morning balance back - that is, it printed
// money, once per restart, silently, and a demonstration would have looked perfect. The
// promises were worse: the hold stayed in nobody's memory at all, so a machine coming back
// after a crash could be told "this withdrawal never finished" and the host would have no
// idea what it had promised.
//
// The pair to read side by side:
//
//   ABalanceSurvivesARestart               - the file remembers
//   AJournalThatDisagreesStopsTheHost      - and the file is not trusted for it
//
// Those two are KARAR-047 in one breath: the balance file is a convenience, the journal is
// the truth, and the two are compared before the port is ever opened.

using Atm.Host;
using Atm.Protocol;
using Xunit;

namespace Atm.Tests;

public class RestartTests
{
    private const string Card = "4111111111111111";

    /// <summary>A folder of its own for each test, removed at the end.</summary>
    private static string NewFolder()
    {
        var path = Path.Combine(Path.GetTempPath(), "atmsim-restart-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>A host over files in this folder - the shape the running host has.</summary>
    private static (HostService Host, FileAccountStore Accounts, FileJournal Journal)
        Open(string folder, IClock clock)
    {
        var accounts = new FileAccountStore(Path.Combine(folder, "accounts.jsonl"),
            InMemoryAccountStore.Opening);
        var journal = new FileJournal(Path.Combine(folder, "journal.jsonl"));
        return (new HostService(accounts, PinVerifier.WithDemoCards(), journal, clock),
            accounts, journal);
    }

    private static Envelope Auth(TransactionKey key, DateTimeOffset at, long amount) =>
        MessageCodec.Envelope(MessageType.WithdrawalAuthRequest, key, at,
            new WithdrawalAuthRequestBody(Card, amount, [new DenominationLine(20_000, (int)(amount / 20_000))]));

    private static Envelope Advice(TransactionKey key, DateTimeOffset at, long amount) =>
        MessageCodec.Envelope(MessageType.DispenseAdvice, key, at,
            new DispenseAdviceBody(key.ToString(), DispenseOutcome.Full, amount, 0));

    [Fact]
    public void ABalanceSurvivesARestart()
    {
        var folder = NewFolder();
        var clock = VirtualClock.StartOfBusinessDay();
        var key = new TransactionKey("ATM-01", "2026-01-05", 501);

        try
        {
            var before = Open(folder, clock).Host;
            before.Handle(Auth(key, clock.UtcNow, 20_000));
            before.Handle(Advice(key, clock.UtcNow, 20_000));

            var after = Open(folder, clock);

            // 2500,00 lira this morning, 200 lira handed over.
            Assert.Equal(230_000, after.Accounts.FindByPan(Card)!.LedgerBalance);
            Assert.Empty(LedgerRebuild.Differences(
                InMemoryAccountStore.Opening, after.Accounts.All, after.Journal.Entries));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void APromiseSurvivesARestartAndCanStillBeClosed()
    {
        var folder = NewFolder();
        var clock = VirtualClock.StartOfBusinessDay();
        var key = new TransactionKey("ATM-01", "2026-01-05", 502);

        try
        {
            Open(folder, clock).Host.Handle(Auth(key, clock.UtcNow, 20_000));

            // The host stops with a promise standing. The hold is in the file and the
            // authorisation is in the journal - and nothing is in memory any more.
            var after = Open(folder, clock);

            Assert.Equal(1, after.Host.OpenAuthorisationCount);
            Assert.Equal(20_000, after.Accounts.FindByPan(Card)!.HoldAmount);

            // The machine comes back and reports what it did. The restarted host knows
            // what it promised, so it can check the report against something.
            var answer = after.Host.Handle(Advice(key, clock.UtcNow, 20_000));

            Assert.Equal(ResponseCode.Approved, answer.Body<DispenseAdviceResponseBody>().Rc);
            Assert.Equal(230_000, after.Accounts.FindByPan(Card)!.LedgerBalance);
            Assert.Equal(0, after.Accounts.FindByPan(Card)!.HoldAmount);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void AnAdviceRepeatedAcrossARestartStillMovesTheLedgerOnce()
    {
        var folder = NewFolder();
        var clock = VirtualClock.StartOfBusinessDay();
        var key = new TransactionKey("ATM-01", "2026-01-05", 503);

        try
        {
            var before = Open(folder, clock).Host;
            before.Handle(Auth(key, clock.UtcNow, 20_000));
            before.Handle(Advice(key, clock.UtcNow, 20_000));

            // A restart loses the replay table - the answers themselves are not in the
            // journal, only what they decided. This is the test that says the loss is
            // survivable: the second advice finds no open authorisation and is refused,
            // which is a stronger protection than remembering the answer.
            var after = Open(folder, clock);
            var again = after.Host.Handle(Advice(key, clock.UtcNow, 20_000));

            Assert.Equal(ResponseCode.InvalidTransaction,
                again.Body<DispenseAdviceResponseBody>().Rc);
            Assert.Equal(230_000, after.Accounts.FindByPan(Card)!.LedgerBalance);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void AReversedTransactionIsStillClosedAfterARestart()
    {
        var folder = NewFolder();
        var clock = VirtualClock.StartOfBusinessDay();
        var key = new TransactionKey("ATM-01", "2026-01-05", 514);

        try
        {
            var before = Open(folder, clock).Host;
            before.Handle(Auth(key, clock.UtcNow, 20_000));
            before.Handle(MessageCodec.Envelope(MessageType.ReversalRequest, key, clock.UtcNow,
                new ReversalRequestBody(key.ToString(), 20_000, ReversalReason.Timeout)));

            // A host that forgot which transactions had been reversed would authorise this
            // one again - the promise released, the trace number free, and nothing in
            // memory to say otherwise (KARAR-050).
            var after = Open(folder, clock);
            var again = after.Host.Handle(Auth(key, clock.UtcNow, 20_000))
                .Body<WithdrawalAuthResponseBody>();

            Assert.Equal(ResponseCode.InvalidTransaction, again.Rc);
            Assert.Equal(0, after.Accounts.FindByPan(Card)!.HoldAmount);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void ASettledDayIsStillSettledAfterARestart()
    {
        var folder = NewFolder();
        var clock = VirtualClock.StartOfBusinessDay();
        var key = new TransactionKey("ATM-01", "2026-01-05", 504);

        try
        {
            var before = Open(folder, clock).Host;
            before.Handle(Auth(key, clock.UtcNow, 20_000));
            before.Handle(Advice(key, clock.UtcNow, 20_000));

            var closing = MessageCodec.Envelope(MessageType.CutoverRequest,
                new TransactionKey("ATM-01", "2026-01-05", 505), clock.UtcNow,
                new CutoverRequestBody("2026-01-06", new DayTotals(20_000, 0, 1)));

            Assert.Equal(ResponseCode.Approved,
                before.Handle(closing).Body<CutoverResponseBody>().Rc);

            var after = Open(folder, clock);

            Assert.True(after.Host.IsClosed("2026-01-05"));

            // And it cannot be settled a second time on different figures.
            var second = after.Host.Handle(MessageCodec.Envelope(MessageType.CutoverRequest,
                new TransactionKey("ATM-01", "2026-01-05", 506), clock.UtcNow,
                new CutoverRequestBody("2026-01-06", DayTotals.Empty)));

            var body = second.Body<CutoverResponseBody>();
            Assert.Equal(ResponseCode.AlreadyReconciled, body.Rc);
            Assert.Equal(20_000, body.Totals.Withdrawals);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void AReversedPromiseIsNotStandingAfterARestart()
    {
        var folder = NewFolder();
        var clock = VirtualClock.StartOfBusinessDay();
        var key = new TransactionKey("ATM-01", "2026-01-05", 510);

        try
        {
            var before = Open(folder, clock).Host;
            before.Handle(Auth(key, clock.UtcNow, 20_000));
            before.Handle(MessageCodec.Envelope(MessageType.ReversalRequest, key, clock.UtcNow,
                new ReversalRequestBody(key.ToString(), 20_000, ReversalReason.Timeout)));

            var after = Open(folder, clock);

            // A promise that was taken back is not a promise. If the replay only looked at
            // what OPENED authorisations, every reversed withdrawal in the record would
            // come back as a hold on somebody's money the morning after a restart.
            Assert.Equal(0, after.Host.OpenAuthorisationCount);
            Assert.Equal(0, after.Accounts.FindByPan(Card)!.HoldAmount);
            Assert.Equal(250_000, after.Accounts.FindByPan(Card)!.LedgerBalance);
            Assert.Empty(LedgerRebuild.Differences(
                InMemoryAccountStore.Opening, after.Accounts.All, after.Journal.Entries));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void OnlyApprovedLinesOpenAndCloseAnythingAcrossARestart()
    {
        var folder = NewFolder();
        var clock = VirtualClock.StartOfBusinessDay();
        var refused = new TransactionKey("ATM-01", "2026-01-05", 511);
        var standing = new TransactionKey("ATM-01", "2026-01-05", 512);

        try
        {
            var before = Open(folder, clock).Host;

            // Refused for want of money: TR-DEMO-002 holds 45,00 lira.
            before.Handle(MessageCodec.Envelope(MessageType.WithdrawalAuthRequest, refused,
                clock.UtcNow, new WithdrawalAuthRequestBody("4222222222222220", 20_000,
                    [new DenominationLine(20_000, 1)])));

            // Approved - and then a reversal the host refuses, because it quotes the
            // wrong amount. A refusal must not close what it was refused for.
            before.Handle(Auth(standing, clock.UtcNow, 20_000));
            before.Handle(MessageCodec.Envelope(MessageType.ReversalRequest, standing,
                clock.UtcNow,
                new ReversalRequestBody(standing.ToString(), 999_00, ReversalReason.Timeout)));

            var after = Open(folder, clock);

            // One promise standing: the refused request opened nothing, and the refused
            // reversal closed nothing.
            Assert.Equal(1, after.Host.OpenAuthorisationCount);
            Assert.Equal(standing, after.Host.OpenAuthorisations.Single());
            Assert.Equal(20_000, after.Accounts.FindByPan(Card)!.HoldAmount);
            Assert.Empty(LedgerRebuild.Differences(
                InMemoryAccountStore.Opening, after.Accounts.All, after.Journal.Entries));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void ADepositSurvivesARestartAndMovesTheBalanceTheOtherWay()
    {
        var folder = NewFolder();
        var clock = VirtualClock.StartOfBusinessDay();
        var key = new TransactionKey("ATM-01", "2026-01-05", 513);

        try
        {
            var before = Open(folder, clock).Host;

            before.Handle(MessageCodec.Envelope(MessageType.DepositAuthRequest, key,
                clock.UtcNow, new DepositAuthRequestBody(Card, 30_000,
                    [new DenominationLine(10_000, 3)])));

            before.Handle(MessageCodec.Envelope(MessageType.DepositCommitAdvice, key,
                clock.UtcNow, new DepositCommitBody(key.ToString(), DepositOutcome.Stacked,
                    30_000, 0, 0)));

            var after = Open(folder, clock);

            // Money in, not out. The rebuild has to know the difference: a deposit counted
            // with the wrong sign would report a balance 600 lira short of the truth on
            // every restart, and the file - which is right - would be the one blamed.
            Assert.Equal(280_000, after.Accounts.FindByPan(Card)!.LedgerBalance);
            Assert.Equal(0, after.Host.OpenDeposits.Count);
            Assert.Empty(LedgerRebuild.Differences(
                InMemoryAccountStore.Opening, after.Accounts.All, after.Journal.Entries));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void AJournalThatDisagreesWithTheBalanceFileIsReported()
    {
        var folder = NewFolder();
        var clock = VirtualClock.StartOfBusinessDay();
        var key = new TransactionKey("ATM-01", "2026-01-05", 507);
        var accountsPath = Path.Combine(folder, "accounts.jsonl");

        try
        {
            var before = Open(folder, clock).Host;
            before.Handle(Auth(key, clock.UtcNow, 20_000));
            before.Handle(Advice(key, clock.UtcNow, 20_000));

            // Somebody edits the balance file - or a half-finished write leaves an old
            // figure behind. Either way it now says something the journal does not.
            var lines = File.ReadAllLines(accountsPath);
            File.WriteAllLines(accountsPath,
                lines.Select(l => l.Replace("\"ledgerBalance\":230000", "\"ledgerBalance\":250000")));

            var after = Open(folder, clock);
            var problems = LedgerRebuild.Differences(
                InMemoryAccountStore.Opening, after.Accounts.All, after.Journal.Entries);

            Assert.NotEmpty(problems);
            Assert.Contains("TR-DEMO-001", problems.Single());
            Assert.Contains("20000", problems.Single());
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void AHoldWithNoPromiseBehindItIsReported()
    {
        var folder = NewFolder();
        var clock = VirtualClock.StartOfBusinessDay();
        var accountsPath = Path.Combine(folder, "accounts.jsonl");

        try
        {
            Open(folder, clock);

            // A hold appears in the file that no authorisation in the journal explains.
            // This is money the customer cannot spend and nothing will ever release.
            var lines = File.ReadAllLines(accountsPath);
            File.WriteAllLines(accountsPath,
                lines.Select(l => l.Contains("TR-DEMO-001")
                    ? l.Replace("\"holdAmount\":0", "\"holdAmount\":50000")
                    : l));

            var after = Open(folder, clock);
            var problems = LedgerRebuild.Differences(
                InMemoryAccountStore.Opening, after.Accounts.All, after.Journal.Entries);

            Assert.Contains("bloke", problems.Single());
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void ALedgerLineWithNoAccountOnItStopsTheCheckRatherThanBeingSkipped()
    {
        var folder = NewFolder();
        var clock = VirtualClock.StartOfBusinessDay();
        var journalPath = Path.Combine(folder, "journal.jsonl");

        try
        {
            var opened = Open(folder, clock);

            // A movement written by an older version of this host: real money, no account
            // named. The balances cannot be rebuilt from it, and the danger is that a
            // check which quietly skipped such lines would get GREENER the more damaged
            // the record was.
            opened.Journal.Append(clock.UtcNow, new TransactionKey("ATM-01", "2026-01-05", 509),
                MessageType.DispenseAdvice, ResponseCode.Approved, Card, "eski biçim", 20_000);

            var problems = LedgerRebuild.Differences(
                InMemoryAccountStore.Opening, opened.Accounts.All, opened.Journal.Entries);

            Assert.NotEmpty(problems);
            Assert.Contains("hesabı yazılmamış", problems[0]);
            Assert.Contains("--yeni-gun", problems[0]);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void AHostWithAnEmptyJournalOpensOnTheOpeningBalances()
    {
        var folder = NewFolder();
        var clock = VirtualClock.StartOfBusinessDay();

        try
        {
            var fresh = Open(folder, clock);

            Assert.Equal(250_000, fresh.Accounts.FindByPan(Card)!.LedgerBalance);
            Assert.Equal(0, fresh.Host.OpenAuthorisationCount);
            Assert.Empty(LedgerRebuild.Differences(
                InMemoryAccountStore.Opening, fresh.Accounts.All, fresh.Journal.Entries));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void ALedgerLineNamesTheAccountNotOnlyTheCard()
    {
        var folder = NewFolder();
        var clock = VirtualClock.StartOfBusinessDay();
        var key = new TransactionKey("ATM-01", "2026-01-05", 508);

        try
        {
            var opened = Open(folder, clock);
            opened.Host.Handle(Auth(key, clock.UtcNow, 20_000));
            opened.Host.Handle(Advice(key, clock.UtcNow, 20_000));

            var posted = opened.Journal.Entries
                .Single(e => e.Type == MessageType.DispenseAdvice && e.Rc == ResponseCode.Approved);

            Assert.Equal("TR-DEMO-001", posted.AccountId);

            // And the card beside it is still masked - the account is the identity, the
            // card is only how the customer got here (KARAR-048).
            Assert.Contains("*", posted.MaskedPan);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
