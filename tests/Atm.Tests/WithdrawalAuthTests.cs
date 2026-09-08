// WithdrawalAuthTests.cs
//
// What this file does: it checks the first message in this project that touches money -
// the withdrawal authorisation - and it checks it from the outside, by asking the host
// for a balance afterwards rather than by reaching into the account store.
//
// The two tests to read first:
//
//   TheBreakdownIsAddedUpAgainByTheHost - the terminal says "350 lira" and hands over
//   the notes it means to use. If those two numbers disagree, the difference would end
//   up in nobody's ledger. The host adds them up again rather than trusting the sender.
//
//   MoneyPromisedOnceCannotBePromisedTwice - the second authorisation is refused
//   because the first one is still holding the money, even though the ledger balance
//   alone would cover both. This is the whole reason an account carries two numbers.
//
// What is NOT tested here, because it does not exist yet: what happens after the cash
// is handed over. The dispense advice, the reversal and the ledger movement they cause
// arrive in Phase 2c. Until then an approved authorisation leaves a hold standing, and
// that is the honest state of the system rather than an oversight.

using Atm.Host;
using Atm.Protocol;
using Xunit;

namespace Atm.Tests;

public class WithdrawalAuthTests
{
    // TR-DEMO-001, 2.500,00 TL. docs/model.md section 4.1.
    private const string Card = "4111111111111111";
    private const string UnknownCard = "4999999999999999";

    private static (HostService host, VirtualClock clock) NewHost()
    {
        var clock = VirtualClock.StartOfBusinessDay();
        return (HostService.WithDemoData(clock), clock);
    }

    private static Envelope Request<TBody>(string type, int stan, TBody body, VirtualClock clock)
        where TBody : notnull =>
        MessageCodec.Envelope(type, new TransactionKey("ATM-01", "2026-01-05", stan),
            clock.UtcNow, body);

    /// <summary>350,00 TL as the machine would hand it over: one 200, one 100, one 50.</summary>
    private static WithdrawalAuthRequestBody ThreeHundredAndFifty(string pan = Card) =>
        new(pan, 35_000, [new DenominationLine(20_000, 1),
                          new DenominationLine(10_000, 1),
                          new DenominationLine(5_000, 1)]);

    private static WithdrawalAuthResponseBody Authorise(
        HostService host, VirtualClock clock, int stan, WithdrawalAuthRequestBody body) =>
        host.Handle(Request(MessageType.WithdrawalAuthRequest, stan, body, clock))
            .Body<WithdrawalAuthResponseBody>();

    /// <summary>
    /// The reason the host wrote down for the only authorisation line in the journal.
    /// </summary>
    /// <remarks>
    /// Why the tests read this and not just the response code: every refusal answers 12,
    /// so the code alone cannot tell one guard from another. Nine sabotage runs made that
    /// concrete - two guards were removed and every test still passed, because a later
    /// guard caught the same request and returned the same code. The refusal REASON is
    /// what the journal exists to preserve, and it is the only thing that tells these
    /// cases apart. A fragment is matched rather than the whole sentence, so that
    /// rewording a message does not turn into a failing test.
    /// </remarks>
    private static string Why(HostService host) =>
        host.Journal.Entries.Last(e => e.Type == MessageType.WithdrawalAuthRequest).Note;

    private static BalanceResponseBody Balance(HostService host, VirtualClock clock, int stan) =>
        host.Handle(Request(MessageType.BalanceRequest, stan, new BalanceRequestBody(Card), clock))
            .Body<BalanceResponseBody>();

    [Fact]
    public void AnApprovedAuthorisationHoldsTheMoneyAndLeavesTheLedgerAlone()
    {
        var (host, clock) = NewHost();

        var body = Authorise(host, clock, 101, ThreeHundredAndFifty());

        Assert.Equal(ResponseCode.Approved, body.Rc);
        // The customer may now spend 350 lira less...
        Assert.Equal(215_000, body.Available);
        // ...but nothing has left the account: the cash is still inside the machine.
        Assert.Equal(250_000, body.Ledger);
    }

    [Fact]
    public void TheHoldIsStillThereWhenTheNextMessageAsksForTheBalance()
    {
        var (host, clock) = NewHost();

        Authorise(host, clock, 101, ThreeHundredAndFifty());
        var balance = Balance(host, clock, 102);

        // Asked from the outside, through the same door the screen uses.
        Assert.Equal(215_000, balance.Available);
        Assert.Equal(250_000, balance.Ledger);
    }

    [Fact]
    public void TheAnswerIsAWithdrawalAuthResponseCarryingTheKeyItWasAskedWith()
    {
        var (host, clock) = NewHost();

        var response = host.Handle(
            Request(MessageType.WithdrawalAuthRequest, 101, ThreeHundredAndFifty(), clock));

        Assert.Equal(MessageType.WithdrawalAuthResponse, response.Type);
        Assert.Equal(new TransactionKey("ATM-01", "2026-01-05", 101), response.Key);
    }

    [Fact]
    public void TheAuthIdIsTheTransactionKeyWrittenOutAndNothingNewer()
    {
        var (host, clock) = NewHost();

        var body = Authorise(host, clock, 104, ThreeHundredAndFifty());

        // A second identity would only make a day where the two disagree possible.
        Assert.Equal("ATM-01/2026-01-05/000104", body.AuthId);
    }

    [Fact]
    public void TheBreakdownIsAddedUpAgainByTheHost()
    {
        var (host, clock) = NewHost();

        // The terminal asks for 350 lira but sends notes worth 300. A software fault, a
        // half-finished change to the cassettes, a request someone edited by hand - the
        // host cannot tell which, and does not need to.
        var wrong = new WithdrawalAuthRequestBody(Card, 35_000,
            [new DenominationLine(20_000, 1), new DenominationLine(10_000, 1)]);

        var body = Authorise(host, clock, 101, wrong);

        Assert.Equal(ResponseCode.InvalidTransaction, body.Rc);
        Assert.True(Why(host).Contains("adds up to 30000"), Why(host));
        Assert.Equal(250_000, Balance(host, clock, 102).Available);
    }

    [Fact]
    public void ANoteThisMachineDoesNotHaveIsRefused()
    {
        var (host, clock) = NewHost();

        // 500 lira notes exist in the country and not in this machine. The breakdown
        // adds up perfectly, which is exactly why the value has to be checked too.
        var body = Authorise(host, clock, 101,
            new WithdrawalAuthRequestBody(Card, 50_000, [new DenominationLine(50_000, 1)]));

        Assert.Equal(ResponseCode.InvalidTransaction, body.Rc);
        Assert.True(Why(host).Contains("no note of 50000"), Why(host));
        Assert.Equal(250_000, Balance(host, clock, 102).Available);
    }

    [Fact]
    public void ARequestWithoutABreakdownIsRefused()
    {
        var (host, clock) = NewHost();

        var body = Authorise(host, clock, 101, new WithdrawalAuthRequestBody(Card, 35_000, []));

        Assert.Equal(ResponseCode.InvalidTransaction, body.Rc);
        // Not "adds up to 0, not 35000". A request that never said which notes it meant
        // to use is a different mistake from one that counted them wrongly, and the
        // record has to be able to tell a human which of the two happened.
        Assert.True(Why(host).Contains("no denomination breakdown"), Why(host));
    }

    [Fact]
    public void ABundleOfNoNotesIsRefused()
    {
        var (host, clock) = NewHost();

        var body = Authorise(host, clock, 101,
            new WithdrawalAuthRequestBody(Card, 20_000,
                [new DenominationLine(20_000, 1), new DenominationLine(10_000, 0)]));

        Assert.Equal(ResponseCode.InvalidTransaction, body.Rc);
        Assert.True(Why(host).Contains("is not a bundle"), Why(host));
    }

    [Fact]
    public void AnAmountThatIsNotPositiveIsRefused()
    {
        var (host, clock) = NewHost();

        var body = Authorise(host, clock, 101,
            new WithdrawalAuthRequestBody(Card, 0, [new DenominationLine(20_000, 1)]));

        Assert.Equal(ResponseCode.InvalidTransaction, body.Rc);
        // The amount is checked on its own. Without this line the request is still
        // refused - the notes do not add up to zero - but the record would say the
        // breakdown was wrong when what was wrong was the request.
        Assert.True(Why(host).Contains("amount is not positive"), Why(host));
    }

    [Fact]
    public void AnUnknownCardIsRefusedWithoutSayingAnythingAboutBalances()
    {
        var (host, clock) = NewHost();

        var body = Authorise(host, clock, 101, ThreeHundredAndFifty(UnknownCard));

        Assert.Equal(ResponseCode.UnknownCard, body.Rc);
        Assert.Equal(0, body.Available);
        Assert.Equal(0, body.Ledger);
    }

    [Fact]
    public void MoreThanTheBalanceIsRefused()
    {
        var (host, clock) = NewHost();

        // 3.000 TL out of an account holding 2.500 TL, in notes that add up exactly.
        var body = Authorise(host, clock, 101,
            new WithdrawalAuthRequestBody(Card, 300_000, [new DenominationLine(20_000, 15)]));

        Assert.Equal(ResponseCode.InsufficientFunds, body.Rc);
        Assert.Equal(250_000, Balance(host, clock, 102).Available);
    }

    [Fact]
    public void MoneyPromisedOnceCannotBePromisedTwice()
    {
        var (host, clock) = NewHost();

        // 2.000 TL authorised: available falls to 500 TL, the ledger stays at 2.500.
        var first = Authorise(host, clock, 101,
            new WithdrawalAuthRequestBody(Card, 200_000, [new DenominationLine(20_000, 10)]));
        Assert.Equal(ResponseCode.Approved, first.Rc);

        // 1.000 TL more. The LEDGER would cover it - 2.500 is more than 1.000 - and a
        // host that checked the ledger would approve it. The money is already promised.
        var second = Authorise(host, clock, 102,
            new WithdrawalAuthRequestBody(Card, 100_000, [new DenominationLine(20_000, 5)]));

        Assert.Equal(ResponseCode.InsufficientFunds, second.Rc);
        Assert.Equal(50_000, Balance(host, clock, 103).Available);
        Assert.Equal(250_000, Balance(host, clock, 104).Ledger);
    }

    [Fact]
    public void TheSameAuthorisationArrivingTwiceHoldsTheMoneyOnce()
    {
        var (host, clock) = NewHost();
        var request = Request(MessageType.WithdrawalAuthRequest, 101, ThreeHundredAndFifty(), clock);

        var first = host.Handle(request).Body<WithdrawalAuthResponseBody>();
        // The same trace number again: the terminal heard nothing and asked once more.
        var second = host.Handle(request).Body<WithdrawalAuthResponseBody>();

        Assert.Equal(ResponseCode.Approved, first.Rc);
        Assert.Equal(first.AuthId, second.AuthId);
        Assert.Equal(first.Available, second.Available);
        Assert.Equal(1, host.ReplayCount);
        // Held once, not twice. This is the line that stands between a lost answer and
        // a customer whose money is promised away twice over.
        Assert.Equal(215_000, Balance(host, clock, 102).Available);
    }

    [Fact]
    public void TheJournalWritesTheAmountAndNeverTheWholeCard()
    {
        var (host, clock) = NewHost();

        Authorise(host, clock, 101, ThreeHundredAndFifty());

        var line = host.Journal.Entries.Single(e => e.Type == MessageType.WithdrawalAuthRequest);
        Assert.Equal(35_000, line.Amount);
        Assert.Equal(ResponseCode.Approved, line.Rc);
        Assert.Equal("411111******1111", line.MaskedPan);
        Assert.False(line.Note.Contains(Card), "the journal line quoted the full card number");
    }

    [Fact]
    public void ARefusalCarriesNoAuthIdAndStillGetsWrittenDown()
    {
        var (host, clock) = NewHost();

        var body = Authorise(host, clock, 101, ThreeHundredAndFifty(UnknownCard));

        // Nothing happened, so there is nothing for a later reversal to quote.
        Assert.Equal("", body.AuthId);
        var line = host.Journal.Entries.Single(e => e.Type == MessageType.WithdrawalAuthRequest);
        Assert.Equal(ResponseCode.UnknownCard, line.Rc);
        Assert.Equal(35_000, line.Amount);
    }

    [Fact]
    public void ARefusalIsAnsweredAfreshRatherThanRememberedAsAVerdict()
    {
        var (host, clock) = NewHost();
        var broken = Request(MessageType.WithdrawalAuthRequest, 101,
            new WithdrawalAuthRequestBody(Card, 35_000, [new DenominationLine(20_000, 1)]), clock);

        host.Handle(broken);
        host.Handle(broken);

        // Both were real refusals, not one refusal and one echo of it. A refusal moved
        // no money and took nothing away, so there is nothing for the replay table to
        // protect - and remembering it would make one bad message a permanent verdict on
        // this trace number (KARAR-032).
        Assert.Equal(0, host.ReplayCount);
        Assert.Equal(2, host.Journal.Entries.Count(e => e.Type == MessageType.WithdrawalAuthRequest));
        Assert.Equal(0, host.Journal.Entries.Count(e => e.Note.Contains("replay")));
    }
}
