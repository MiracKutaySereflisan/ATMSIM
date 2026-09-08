// Copyright (c) 2026 Mirac Kutay Sereflisan. MIT lisansi - LICENSE dosyasina bakiniz.
// DepositTests.cs
//
// What these tests are for: the host's side of a deposit - what it agrees to, what it
// credits, and above all what it refuses to credit.
//
// One line is the whole of it:
//
//     credited = stacked
//
// Money handed back to the customer and money stuck in the mechanism are not in a drawer,
// so neither may become a balance. Most of the tests below are that sentence, asked from a
// different direction each time.
//
// The other thing being nailed down here is repetition. A deposit commit is sent again and
// again until the host answers (KARAR-040), because the paper has already moved and the
// only unknown is whether the ledger heard about it. That is only safe if a second commit
// credits nothing - so there is a test for exactly that, and it is the one that would make
// this a double-credit machine if it ever went red.

using Atm.Host;
using Atm.Protocol;
using Xunit;

namespace Atm.Tests;

public class DepositTests
{
    private const string Card = "4111111111111111";
    private const string UnknownCard = "4999999999999996";

    private sealed class Bank
    {
        public Bank()
        {
            Clock = VirtualClock.StartOfBusinessDay();
            Host = HostService.WithDemoData(Clock);
        }

        public VirtualClock Clock { get; }
        public HostService Host { get; }

        private int _stan = 100;
        public int NextStan() => ++_stan;

        public TransactionKey Key(int stan) => new("ATM-01", "2026-01-05", stan);

        public DepositAuthResponseBody Agree(int stan, long counted,
            IReadOnlyList<DenominationLine>? denoms = null, string pan = Card) =>
            Host.Handle(MessageCodec.Envelope(MessageType.DepositAuthRequest, Key(stan),
                Clock.UtcNow,
                new DepositAuthRequestBody(pan, counted,
                    denoms ?? [new DenominationLine(10_000, (int)(counted / 10_000))])))
                .Body<DepositAuthResponseBody>();

        public DepositCommitResponseBody Commit(int stan, string outcome,
            long stacked, long returned = 0, long jammed = 0) =>
            Host.Handle(MessageCodec.Envelope(MessageType.DepositCommitAdvice, Key(stan),
                Clock.UtcNow,
                new DepositCommitBody(Key(stan).ToString(), outcome, stacked, returned, jammed)))
                .Body<DepositCommitResponseBody>();

        public long LedgerOf(string pan) =>
            Host.Handle(MessageCodec.Envelope(MessageType.BalanceRequest,
                new TransactionKey("QUERY", "2026-01-05", ++_queryStan),
                Clock.UtcNow, new BalanceRequestBody(pan)))
                .Body<BalanceResponseBody>().Ledger;

        /// <summary>The reason the host wrote down for the last thing it did.</summary>
        public string LastNote => Host.Journal.Entries[^1].Note;

        private int _queryStan;
    }

    [Fact]
    public void AgreeingToADepositMovesNothing()
    {
        var bank = new Bank();
        var stan = bank.NextStan();

        var answer = bank.Agree(stan, 50_000);

        Assert.Equal(ResponseCode.Approved, answer.Rc);
        Assert.Equal(250_000, answer.Ledger);
        Assert.Equal(250_000, bank.LedgerOf(Card));

        // The host is now waiting to hear what happened to the paper.
        Assert.Equal(1, bank.Host.OpenDeposits.Count);
    }

    [Fact]
    public void ADepositWhoseNotesDoNotAddUpIsRefusedBeforeAnyAccountIsTouched()
    {
        var bank = new Bank();

        // Says 500 lira, lists 300.
        var answer = bank.Agree(bank.NextStan(), 50_000,
            [new DenominationLine(10_000, 3)]);

        Assert.Equal(ResponseCode.InvalidTransaction, answer.Rc);
        Assert.Contains("30000", bank.LastNote);
        Assert.Equal(0, bank.Host.OpenDeposits.Count);
    }

    [Fact]
    public void ADepositOfNothingIsRefused()
    {
        var bank = new Bank();

        Assert.Equal(ResponseCode.InvalidTransaction, bank.Agree(bank.NextStan(), 0, []).Rc);
        Assert.Contains("not a deposit", bank.LastNote);
    }

    [Fact]
    public void ADepositToACardThisHostDoesNotKnowIsRefused()
    {
        var bank = new Bank();

        Assert.Equal(ResponseCode.UnknownCard,
            bank.Agree(bank.NextStan(), 50_000, pan: UnknownCard).Rc);
    }

    [Fact]
    public void OnlyWhatReachedADrawerIsCredited()
    {
        var bank = new Bank();
        var stan = bank.NextStan();
        bank.Agree(stan, 50_000);

        var answer = bank.Commit(stan, DepositOutcome.Stacked, stacked: 50_000);

        Assert.Equal(ResponseCode.Approved, answer.Rc);
        Assert.Equal(300_000, answer.Ledger);
        Assert.Equal(0, bank.Host.OpenDeposits.Count);
    }

    [Fact]
    public void MoneyHandedBackToTheCustomerCreditsNothing()
    {
        var bank = new Bank();
        var stan = bank.NextStan();
        bank.Agree(stan, 50_000);

        var answer = bank.Commit(stan, DepositOutcome.Returned, stacked: 0, returned: 50_000);

        Assert.Equal(ResponseCode.Approved, answer.Rc);
        Assert.Equal(250_000, answer.Ledger);
    }

    [Fact]
    public void MoneyStuckInTheMechanismCreditsNothingAndIsCounted()
    {
        var bank = new Bank();
        var stan = bank.NextStan();
        bank.Agree(stan, 50_000);

        var answer = bank.Commit(stan, DepositOutcome.Jammed, stacked: 0, jammed: 50_000);

        Assert.Equal(ResponseCode.Approved, answer.Rc);
        Assert.Equal(250_000, answer.Ledger);

        // Counted, because a number that is never zero is a finding and a number nobody
        // keeps is a difference discovered at day end with nothing to explain it.
        Assert.Equal(50_000, bank.Host.JammedInDeposits);
    }

    [Fact]
    public void APartialStackCreditsOnlyTheStackedPart()
    {
        var bank = new Bank();
        var stan = bank.NextStan();
        bank.Agree(stan, 50_000);

        var answer = bank.Commit(stan, DepositOutcome.Partial, stacked: 30_000, jammed: 20_000);

        Assert.Equal(280_000, answer.Ledger);
        Assert.Equal(20_000, bank.Host.JammedInDeposits);
    }

    [Fact]
    public void ACommitThatDoesNotAccountForEveryNoteIsRefused()
    {
        var bank = new Bank();
        var stan = bank.NextStan();
        bank.Agree(stan, 50_000);

        // 300 stacked, 100 back, and 100 lira nobody mentions.
        var answer = bank.Commit(stan, DepositOutcome.Partial, stacked: 30_000, returned: 10_000);

        Assert.Equal(ResponseCode.InvalidTransaction, answer.Rc);
        Assert.Contains("40000", bank.LastNote);
        Assert.Equal(250_000, bank.LedgerOf(Card));
    }

    [Fact]
    public void ACommitForADepositThisHostNeverAgreedToIsRefused()
    {
        var bank = new Bank();

        var answer = bank.Commit(bank.NextStan(), DepositOutcome.Stacked, stacked: 50_000);

        Assert.Equal(ResponseCode.InvalidTransaction, answer.Rc);
        Assert.Contains("no deposit was agreed", bank.LastNote);
        Assert.Equal(250_000, bank.LedgerOf(Card));
    }

    [Fact]
    public void TheSameCommitSentTwiceCreditsOnce()
    {
        // This is the test that makes KARAR-040 safe. A commit is re-sent until the host
        // answers, because the paper has already moved - and if the second one credited,
        // every lost acknowledgement would become free money.
        var bank = new Bank();
        var stan = bank.NextStan();
        bank.Agree(stan, 50_000);

        var first = bank.Commit(stan, DepositOutcome.Stacked, stacked: 50_000);
        var second = bank.Commit(stan, DepositOutcome.Stacked, stacked: 50_000);

        Assert.Equal(300_000, first.Ledger);
        Assert.Equal(300_000, second.Ledger);
        Assert.Equal(300_000, bank.LedgerOf(Card));
        Assert.Equal(1, bank.Host.ReplayCount);
    }

    [Fact]
    public void ARefusedCommitIsNotRememberedSoTheCorrectOneStillWorks()
    {
        // KARAR-032, asked of the deposit side: a broken commit followed by a good one has
        // to end with the account credited. If the refusal were remembered, one malformed
        // message would leave the customer's banknotes in a drawer for ever with nothing
        // on the books.
        var bank = new Bank();
        var stan = bank.NextStan();
        bank.Agree(stan, 50_000);

        bank.Commit(stan, DepositOutcome.Partial, stacked: 30_000);
        var good = bank.Commit(stan, DepositOutcome.Stacked, stacked: 50_000);

        Assert.Equal(ResponseCode.Approved, good.Rc);
        Assert.Equal(300_000, good.Ledger);
    }

    [Fact]
    public void ADepositAndAWithdrawalSharingATraceNumberDoNotCollide()
    {
        // KARAR-031 again, from the deposit side. The two messages carry the same
        // transaction key and differ only by type; a replay table keyed on the key alone
        // would answer the deposit with the withdrawal's answer.
        var bank = new Bank();
        var stan = bank.NextStan();

        bank.Host.Handle(MessageCodec.Envelope(MessageType.WithdrawalAuthRequest,
            bank.Key(stan), bank.Clock.UtcNow,
            new WithdrawalAuthRequestBody(Card, 20_000, [new DenominationLine(10_000, 2)])));

        var deposit = bank.Agree(stan, 50_000);

        Assert.Equal(ResponseCode.Approved, deposit.Rc);
        Assert.Equal(0, bank.Host.ReplayCount);
    }
}
