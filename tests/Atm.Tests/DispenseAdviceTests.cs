// Copyright (c) 2026 Mirac Kutay Sereflisan. MIT lisansi - LICENSE dosyasina bakiniz.
// DispenseAdviceTests.cs
//
// What this file does: it checks the message that finally moves the ledger - the
// machine's report of what happened to the cash - and the four outcomes it can carry.
//
// The three tests to read first:
//
//   AnAdviceIsNotMistakenForARepeatOfTheAuthorisation - the authorisation and its advice
//   carry the SAME trace number on purpose. A host that keys its replay table on the
//   transaction alone answers the advice with the authorisation's answer, the ledger
//   never moves, and nothing anywhere says so (KARAR-031).
//
//   APartialDispenseMovesTheLedgerByWhatCameOut - 500 authorised, 300 handed over. The
//   ledger moves by 300, not 500 and not 0. Both of the wrong answers leave a hole in
//   somebody's balance sheet.
//
//   RetractedCashNeverReachesTheLedger - the notes left the cassette, so the machine is
//   lighter, but nobody took them. Money that came back is in the retract bin: not the
//   customer's, not the cassette's, and not the ledger's business.
//
// Every test asks for the balance afterwards rather than reading the account directly,
// so what is being checked is what a screen would actually be told.

using Atm.Host;
using Atm.Protocol;
using Xunit;

namespace Atm.Tests;

public class DispenseAdviceTests
{
    private const string Card = "4111111111111111";
    private const int Stan = 104;
    private const string AuthId = "ATM-01/2026-01-05/000104";

    private static (HostService host, VirtualClock clock) NewHost()
    {
        var clock = VirtualClock.StartOfBusinessDay();
        return (HostService.WithDemoData(clock), clock);
    }

    private static Envelope Request<TBody>(string type, int stan, TBody body, VirtualClock clock)
        where TBody : notnull =>
        MessageCodec.Envelope(type, new TransactionKey("ATM-01", "2026-01-05", stan),
            clock.UtcNow, body);

    /// <summary>Authorises 500,00 TL out of the 2.500,00 TL account and returns the host.</summary>
    private static (HostService host, VirtualClock clock) AuthorisedFiveHundred()
    {
        var (host, clock) = NewHost();

        var response = host.Handle(Request(MessageType.WithdrawalAuthRequest, Stan,
            new WithdrawalAuthRequestBody(Card, 50_000,
                [new DenominationLine(20_000, 2), new DenominationLine(10_000, 1)]), clock))
            .Body<WithdrawalAuthResponseBody>();

        Assert.Equal(ResponseCode.Approved, response.Rc);
        return (host, clock);
    }

    private static DispenseAdviceResponseBody Advise(
        HostService host, VirtualClock clock, string outcome, long dispensed, long retracted = 0,
        string authId = AuthId) =>
        host.Handle(Request(MessageType.DispenseAdvice, Stan,
            new DispenseAdviceBody(authId, outcome, dispensed, retracted), clock))
            .Body<DispenseAdviceResponseBody>();

    private static string Why(HostService host) =>
        host.Journal.Entries.Last(e => e.Type == MessageType.DispenseAdvice).Note;

    [Fact]
    public void AnAdviceIsNotMistakenForARepeatOfTheAuthorisation()
    {
        var (host, clock) = AuthorisedFiveHundred();

        var response = host.Handle(Request(MessageType.DispenseAdvice, Stan,
            new DispenseAdviceBody(AuthId, DispenseOutcome.Full, 50_000, 0), clock));

        // Same trace number as the authorisation, different message. If the host greeted
        // it as a repeat, this would come back as a WithdrawalAuthResponse and the ledger
        // would still be sitting at 250.000 with a hold nobody ever releases.
        Assert.Equal(MessageType.DispenseAdviceResponse, response.Type);
        Assert.Equal(0, host.ReplayCount);
        Assert.Equal(200_000, response.Body<DispenseAdviceResponseBody>().Ledger);
    }

    [Fact]
    public void AFullDispenseMovesTheLedgerAndReleasesTheHold()
    {
        var (host, clock) = AuthorisedFiveHundred();

        var body = Advise(host, clock, DispenseOutcome.Full, 50_000);

        Assert.Equal(ResponseCode.Approved, body.Rc);
        Assert.Equal(200_000, body.Ledger);
        // Available equals ledger again: nothing is promised any more.
        Assert.Equal(200_000, body.Available);
    }

    [Fact]
    public void APartialDispenseMovesTheLedgerByWhatCameOut()
    {
        var (host, clock) = AuthorisedFiveHundred();

        // 500 authorised, 300 actually handed over.
        var body = Advise(host, clock, DispenseOutcome.Partial, 30_000);

        // Not 200.000 - that would debit money the customer never got.
        // Not 250.000 - that would leave 300 lira in a pocket and in the account at once.
        Assert.Equal(220_000, body.Ledger);
        Assert.Equal(220_000, body.Available);
    }

    [Fact]
    public void ADispenseThatNeverHappenedLeavesTheAccountWhereItWas()
    {
        var (host, clock) = AuthorisedFiveHundred();

        var body = Advise(host, clock, DispenseOutcome.None, 0);

        Assert.Equal(250_000, body.Ledger);
        Assert.Equal(250_000, body.Available);
    }

    [Fact]
    public void RetractedCashNeverReachesTheLedger()
    {
        var (host, clock) = AuthorisedFiveHundred();

        // The notes came out and went back in: the cassette is lighter by 500 lira and
        // the customer is richer by nothing.
        var body = Advise(host, clock, DispenseOutcome.Retracted, 50_000, retracted: 50_000);

        Assert.Equal(250_000, body.Ledger);
        Assert.Equal(250_000, body.Available);
        Assert.True(Why(host).Contains("0 reached the customer"), Why(host));
    }

    [Fact]
    public void AnAdviceForATransactionTheHostNeverAuthorisedIsRefused()
    {
        var (host, clock) = NewHost();

        var body = Advise(host, clock, DispenseOutcome.Full, 50_000);

        Assert.Equal(ResponseCode.InvalidTransaction, body.Rc);
        Assert.True(Why(host).Contains("no open authorisation"), Why(host));
    }

    [Fact]
    public void TheSameAdviceTwiceMovesTheLedgerOnce()
    {
        var (host, clock) = AuthorisedFiveHundred();

        var first = Advise(host, clock, DispenseOutcome.Full, 50_000);
        // The machine heard no acknowledgement and sent it again.
        var second = Advise(host, clock, DispenseOutcome.Full, 50_000);

        Assert.Equal(ResponseCode.Approved, first.Rc);
        Assert.Equal(ResponseCode.Approved, second.Rc);
        Assert.Equal(first.Ledger, second.Ledger);
        Assert.Equal(200_000, second.Ledger);
        Assert.Equal(1, host.ReplayCount);
    }

    [Fact]
    public void AnAdviceQuotingSomebodyElsesAuthorisationIsRefused()
    {
        var (host, clock) = AuthorisedFiveHundred();

        var body = Advise(host, clock, DispenseOutcome.Full, 50_000,
            authId: "ATM-01/2026-01-05/000999");

        Assert.Equal(ResponseCode.InvalidTransaction, body.Rc);
        Assert.True(Why(host).Contains("but arrived under"), Why(host));

        // The account is where the authorisation left it: 500 lira held, nothing posted.
        var balance = host.Handle(Request(MessageType.BalanceRequest, 200,
            new BalanceRequestBody(Card), clock)).Body<BalanceResponseBody>();
        Assert.Equal(250_000, balance.Ledger);
        Assert.Equal(200_000, balance.Available);
    }

    [Fact]
    public void MoreCashThanWasAuthorisedIsRefusedRatherThanBelieved()
    {
        var (host, clock) = AuthorisedFiveHundred();

        var body = Advise(host, clock, DispenseOutcome.Full, 60_000);

        // The safe reading is that the message is wrong, not that the money is.
        Assert.Equal(ResponseCode.InvalidTransaction, body.Rc);
        Assert.True(Why(host).Contains("more than the authorised"), Why(host));
    }

    [Fact]
    public void AnOutcomeThatDisagreesWithItsOwnNumbersIsRefused()
    {
        var (host, clock) = AuthorisedFiveHundred();

        // "All of it came out" and "none of it came out", in one message.
        var body = Advise(host, clock, DispenseOutcome.Full, 0);

        Assert.Equal(ResponseCode.InvalidTransaction, body.Rc);
        Assert.True(Why(host).Contains("does not match"), Why(host));
    }

    [Fact]
    public void AnOutcomeWordTheContractDoesNotKnowIsRefused()
    {
        var (host, clock) = AuthorisedFiveHundred();

        var body = Advise(host, clock, "MOSTLY_FINE", 50_000);

        Assert.Equal(ResponseCode.InvalidTransaction, body.Rc);
    }

    [Fact]
    public void MoreRetractedThanDispensedIsRefused()
    {
        var (host, clock) = AuthorisedFiveHundred();

        var body = Advise(host, clock, DispenseOutcome.Retracted, 30_000, retracted: 50_000);

        Assert.Equal(ResponseCode.InvalidTransaction, body.Rc);
        Assert.True(Why(host).Contains("more than the dispensed"), Why(host));
    }

    [Fact]
    public void ARefusedAdviceLeavesTheAuthorisationOpen()
    {
        var (host, clock) = AuthorisedFiveHundred();

        // A nonsense advice arrives...
        Assert.Equal(ResponseCode.InvalidTransaction,
            Advise(host, clock, DispenseOutcome.Full, 60_000).Rc);

        // ...and a correct one after it still closes the transaction. Refusing a message
        // must not throw away the promise it was about.
        var body = Advise(host, clock, DispenseOutcome.Full, 50_000);
        Assert.Equal(ResponseCode.Approved, body.Rc);
        Assert.Equal(200_000, body.Ledger);
    }

    [Fact]
    public void TheJournalRecordsWhatReachedTheCustomerNotWhatLeftTheCassette()
    {
        var (host, clock) = AuthorisedFiveHundred();

        Advise(host, clock, DispenseOutcome.Retracted, 50_000, retracted: 50_000);

        var line = host.Journal.Entries.Last(e => e.Type == MessageType.DispenseAdvice);
        // The end-of-day comparison adds this column up. Counting retracted cash here
        // would show money leaving the bank that never left the building.
        Assert.Equal(0, line.Amount);
        Assert.Equal(ResponseCode.Approved, line.Rc);
    }

    [Fact]
    public void AnAppliedAdviceClosesTheAuthorisationRatherThanLeavingItStanding()
    {
        var (host, clock) = AuthorisedFiveHundred();
        Assert.Equal(1, host.OpenAuthorisationCount);

        Advise(host, clock, DispenseOutcome.Full, 50_000);

        // Nothing is promised any more. An authorisation left standing here would be a
        // hold on somebody's account that no message can release - the failure class
        // KARAR-029 warned this design would produce, arriving by accident instead of by
        // a lost message.
        Assert.Equal(0, host.OpenAuthorisationCount);
    }

    [Fact]
    public void ARefusedAdviceLeavesTheAuthorisationStanding()
    {
        var (host, clock) = AuthorisedFiveHundred();

        Advise(host, clock, DispenseOutcome.Full, 60_000);

        // Still open, deliberately: the cash question is unanswered, and forgetting the
        // promise would be worse than keeping it.
        Assert.Equal(1, host.OpenAuthorisationCount);
    }

    [Fact]
    public void NegativeCashIsRefusedAsNegativeCashAndNotAsSomethingElse()
    {
        var (host, clock) = AuthorisedFiveHundred();

        var body = Advise(host, clock, DispenseOutcome.None, -10_000);

        Assert.Equal(ResponseCode.InvalidTransaction, body.Rc);
        // Without this check the message is still refused - a negative amount does not
        // match any outcome either - but the record would say the outcome word was wrong
        // when what was wrong was a machine reporting that money flowed backwards. The
        // difference matters to whoever reads the journal the next morning.
        Assert.True(Why(host).Contains("negative cash"), Why(host));
    }
}
