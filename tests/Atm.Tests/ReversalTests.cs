// Copyright (c) 2026 Mirac Kutay Sereflisan. MIT lisansi - LICENSE dosyasina bakiniz.
// ReversalTests.cs
//
// What this file does: it checks the message that exists because a timeout is not an
// answer - the one the machine sends when it cannot say whether the money moved.
//
// The three tests to read first:
//
//   AReversalForATransactionTheHostNeverSawIsStillAcknowledged - the authorisation was
//   lost on the way. The host has nothing to undo, and it says yes anyway. Saying no
//   would leave the machine re-sending this message for the rest of the day.
//
//   AReversalReleasesThePromiseAndLeavesTheLedgerAlone - the ledger never moved, so the
//   reversal must not move it either. What it takes back is the promise.
//
//   AReversalForAWithdrawalAlreadyPaidIsAcknowledgedAndCountedAsAContradiction - the
//   customer has the cash and the machine is asking for it back. The host cannot un-hand
//   a banknote. It acknowledges, undoes nothing, and writes the contradiction down where
//   Phase 4 will count it.

using Atm.Host;
using Atm.Protocol;
using Xunit;

namespace Atm.Tests;

public class ReversalTests
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

    private static ReversalResponseBody Reverse(
        HostService host, VirtualClock clock, long amount = 50_000,
        string reason = ReversalReason.Timeout, string authId = AuthId) =>
        host.Handle(Request(MessageType.ReversalRequest, Stan,
            new ReversalRequestBody(authId, amount, reason), clock))
            .Body<ReversalResponseBody>();

    private static BalanceResponseBody Balance(HostService host, VirtualClock clock, int stan) =>
        host.Handle(Request(MessageType.BalanceRequest, stan, new BalanceRequestBody(Card), clock))
            .Body<BalanceResponseBody>();

    private static string Why(HostService host) =>
        host.Journal.Entries.Last(e => e.Type == MessageType.ReversalRequest).Note;

    [Fact]
    public void AReversalReleasesThePromiseAndLeavesTheLedgerAlone()
    {
        var (host, clock) = AuthorisedFiveHundred();

        var body = Reverse(host, clock);

        Assert.Equal(ResponseCode.Approved, body.Rc);
        // The ledger never moved on the way in, so there is nothing here to move back.
        Assert.Equal(250_000, body.Ledger);
        Assert.Equal(250_000, body.Available);
        Assert.Equal(0, host.OpenAuthorisationCount);
    }

    [Fact]
    public void AReversalForATransactionTheHostNeverSawIsStillAcknowledged()
    {
        var (host, clock) = NewHost();

        // The authorisation never arrived. The machine heard nothing, waited, and is now
        // doing the only correct thing: assuming it MIGHT have happened.
        var body = Reverse(host, clock);

        Assert.Equal(ResponseCode.Approved, body.Rc);
        Assert.True(Why(host).Contains("never saw"), Why(host));
    }

    [Fact]
    public void AReversalForAWithdrawalAlreadyPaidIsAcknowledgedAndCountedAsAContradiction()
    {
        var (host, clock) = AuthorisedFiveHundred();
        host.Handle(Request(MessageType.DispenseAdvice, Stan,
            new DispenseAdviceBody(AuthId, DispenseOutcome.Full, 50_000, 0), clock));
        Assert.Equal(200_000, Balance(host, clock, 300).Ledger);

        var body = Reverse(host, clock);

        // Acknowledged, because a reversal that is refused is re-sent for ever.
        Assert.Equal(ResponseCode.Approved, body.Rc);
        // And nothing was undone: the customer has the cash and the ledger says so.
        Assert.Equal(200_000, Balance(host, clock, 301).Ledger);
        // The contradiction is counted rather than smoothed over.
        Assert.Equal(1, host.UnexpectedReversalCount);
        Assert.True(Why(host).Contains("UNEXPECTED"), Why(host));
    }

    [Fact]
    public void TheSameReversalTwiceReleasesThePromiseOnce()
    {
        var (host, clock) = AuthorisedFiveHundred();

        var first = Reverse(host, clock);
        // The acknowledgement was lost, so the machine sent it again.
        var second = Reverse(host, clock);

        Assert.Equal(ResponseCode.Approved, first.Rc);
        Assert.Equal(ResponseCode.Approved, second.Rc);
        Assert.Equal(1, host.ReplayCount);
        Assert.Equal(250_000, Balance(host, clock, 300).Available);
        // The second one did not turn into "a reversal for a transaction I never saw".
        Assert.Equal(0, host.UnexpectedReversalCount);
    }

    [Fact]
    public void AReversalForADifferentAmountThanIsHeldIsRefused()
    {
        var (host, clock) = AuthorisedFiveHundred();

        var body = Reverse(host, clock, amount: 30_000);

        Assert.Equal(ResponseCode.InvalidTransaction, body.Rc);
        Assert.True(Why(host).Contains("but 50000 is held"), Why(host));
        // The promise is still standing: a message that disagreed with the record did
        // not get to change it.
        Assert.Equal(1, host.OpenAuthorisationCount);
        Assert.Equal(200_000, Balance(host, clock, 300).Available);
    }

    [Fact]
    public void AReversalQuotingSomebodyElsesAuthorisationIsRefused()
    {
        var (host, clock) = AuthorisedFiveHundred();

        var body = Reverse(host, clock, authId: "ATM-01/2026-01-05/000999");

        Assert.Equal(ResponseCode.InvalidTransaction, body.Rc);
        Assert.Equal(1, host.OpenAuthorisationCount);
    }

    [Fact]
    public void ARefusedReversalCanBeFollowedByACorrectOne()
    {
        var (host, clock) = AuthorisedFiveHundred();

        Assert.Equal(ResponseCode.InvalidTransaction, Reverse(host, clock, amount: 30_000).Rc);

        // The refusal was not remembered as a verdict (KARAR-032), so the machine can
        // still take its promise back once it sends the right figure.
        Assert.Equal(ResponseCode.Approved, Reverse(host, clock).Rc);
        Assert.Equal(0, host.OpenAuthorisationCount);
    }

    [Fact]
    public void AReversalAndAnAdviceCannotBothCloseTheSameWithdrawal()
    {
        var (host, clock) = AuthorisedFiveHundred();

        Assert.Equal(ResponseCode.Approved, Reverse(host, clock).Rc);

        // The advice arrives after the reversal - the machine dispensed, then the link
        // came back and the queued reversal went first. There is nothing left to close.
        var advice = host.Handle(Request(MessageType.DispenseAdvice, Stan,
            new DispenseAdviceBody(AuthId, DispenseOutcome.Full, 50_000, 0), clock))
            .Body<DispenseAdviceResponseBody>();

        Assert.Equal(ResponseCode.InvalidTransaction, advice.Rc);
        // The ledger did not move twice, and it did not move at all: this is a cash
        // difference that only the end-of-day count can find, and Phase 4 will name it.
        Assert.Equal(250_000, Balance(host, clock, 300).Ledger);
    }

    [Fact]
    public void TheReasonIsWrittenDownEvenThoughItChangesNothing()
    {
        var (host, clock) = AuthorisedFiveHundred();

        Reverse(host, clock, reason: ReversalReason.Retracted);

        // The correction is the same whatever the reason. The report is not: "the line
        // was down" and "the customer never picked the money up" are different problems.
        Assert.True(Why(host).Contains(ReversalReason.Retracted), Why(host));
    }

    [Fact]
    public void AReversalIsNotMistakenForTheAuthorisationOrTheAdvice()
    {
        var (host, clock) = AuthorisedFiveHundred();

        var response = host.Handle(Request(MessageType.ReversalRequest, Stan,
            new ReversalRequestBody(AuthId, 50_000, ReversalReason.Timeout), clock));

        // Three messages, one trace number, three different answers (KARAR-031).
        Assert.Equal(MessageType.ReversalResponse, response.Type);
        Assert.Equal(0, host.ReplayCount);
    }

    [Fact]
    public void AReversedTransactionCannotBeAuthorisedAgain()
    {
        var (host, clock) = AuthorisedFiveHundred();

        Assert.Equal(ResponseCode.Approved, Reverse(host, clock).Rc);

        // The same trace number asks to be authorised a second time. Before KARAR-050 the
        // replay table answered it with the approval the reversal had just undone: the
        // machine believed it was authorised, handed over five hundred lira, and the advice
        // that followed found no open authorisation and was refused. Cash out, ledger
        // untouched - the shape of failure A-02.
        var again = host.Handle(Request(MessageType.WithdrawalAuthRequest, Stan,
            new WithdrawalAuthRequestBody(Card, 50_000,
                [new DenominationLine(20_000, 2), new DenominationLine(10_000, 1)]), clock))
            .Body<WithdrawalAuthResponseBody>();

        Assert.Equal(ResponseCode.InvalidTransaction, again.Rc);
        Assert.Equal("", again.AuthId);

        // Nothing is held and nothing is owed: the refusal changed no state at all.
        Assert.Equal(0, host.OpenAuthorisationCount);
        Assert.Equal(250_000, Balance(host, clock, stan: 900).Available);
    }

    [Fact]
    public void AReversalIsStillAcknowledgedAfterTheTransactionIsClosed()
    {
        var (host, clock) = AuthorisedFiveHundred();

        Assert.Equal(ResponseCode.Approved, Reverse(host, clock).Rc);

        // The second reversal is the same message arriving twice - which is what a terminal
        // does when an acknowledgement is lost. Closing a transaction must not stop it being
        // answered, or the terminal would send it for ever (rule 4.2).
        Assert.Equal(ResponseCode.Approved, Reverse(host, clock).Rc);
    }
}
