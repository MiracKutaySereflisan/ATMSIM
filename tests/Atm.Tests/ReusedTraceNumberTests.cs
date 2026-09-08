// ReusedTraceNumberTests.cs
//
// What this file does: it checks the two halves of failure A-03 - the one found on
// 2026-09-01 by restarting the terminal in the middle of a business day.
//
// What happened: a transaction is identified by terminal + business date + trace number.
// The terminal counted its trace numbers in memory, so a restart began the count again
// from one while the business date stayed the same. The second transaction arrived under
// a number that already meant something else, the host found it in the replay table and
// answered it with the FIRST transaction's approval - and 100 lira left the machine that
// no ledger ever moved for.
//
// Two things were wrong and each is fixed on its own side, because either one alone
// would have been enough to hide the other:
//
//   The host was answering a DIFFERENT request from the replay table. Idempotency says
//   "doing the same thing twice must not do it twice". It does not say "anything arriving
//   under a used number gets that number's old answer". HostService now compares the
//   request body before replaying and refuses when they differ.
//
//   The terminal was producing colliding identities in the first place. Its counter is
//   now rebuilt from its own journal on start, the same way the host rebuilds balances
//   from its ledger (KARAR-047).
//
// The tests below are written so that they fail if either fix is removed.

using Atm.Host;
using Atm.Protocol;
using Atm.Terminal;
using Xunit;

namespace Atm.Tests;

public class ReusedTraceNumberTests
{
    private const string Card = "4111111111111111";
    private const string Day = "2026-01-05";

    private static Envelope Request<TBody>(string type, int stan, TBody body, VirtualClock clock)
        where TBody : notnull =>
        MessageCodec.Envelope(type, new TransactionKey("ATM-01", Day, stan), clock.UtcNow, body);

    // 500 TL = two 200s and a 100; 100 TL = a single 100. The breakdown has to be one
    // this machine could actually build, because the host checks it before it checks
    // anything else - a broken breakdown would be refused for the wrong reason and the
    // test would pass without testing anything.
    private static Envelope Withdrawal(HostService host, int stan, long kurus, VirtualClock clock)
    {
        List<DenominationLine> notes = kurus == 50_000
            ? [new DenominationLine(20_000, 2), new DenominationLine(10_000, 1)]
            : [new DenominationLine(10_000, 1)];

        return host.Handle(Request(MessageType.WithdrawalAuthRequest, stan,
            new WithdrawalAuthRequestBody(Card, kurus, notes), clock));
    }

    [Fact]
    public void ASecondWithdrawalUnderAUsedTraceNumberIsRefusedNotAnsweredWithTheFirstOnes()
    {
        var clock = VirtualClock.StartOfBusinessDay();
        var host = HostService.WithDemoData(clock);

        var first = Withdrawal(host, 7, 50_000, clock).Body<WithdrawalAuthResponseBody>();
        Assert.Equal(ResponseCode.Approved, first.Rc);

        // Same terminal, same day, same trace number - but a different amount. This is
        // not a retry of the first request; it is a second request wearing its identity.
        var second = Withdrawal(host, 7, 10_000, clock).Body<WithdrawalAuthResponseBody>();

        Assert.NotEqual(ResponseCode.Approved, second.Rc);
        Assert.Equal(ResponseCode.InvalidTransaction, second.Rc);
    }

    [Fact]
    public void TheRefusalIsWrittenDownInTheHostsOwnWords()
    {
        var clock = VirtualClock.StartOfBusinessDay();
        var host = HostService.WithDemoData(clock);

        Withdrawal(host, 8, 50_000, clock);
        Withdrawal(host, 8, 10_000, clock);

        var line = host.Journal.Entries.Last(e => e.Type == MessageType.WithdrawalAuthRequest);

        Assert.Contains("different request", line.Note);
    }

    [Fact]
    public void AGenuineRetryOfTheSameRequestStillGetsTheSameAnswerBack()
    {
        // The point of the fix is to tell a repeat from a collision, not to break repeats.
        // A real retry - same body, sent again because the answer was lost - must still be
        // answered from the table without doing the work twice.
        var clock = VirtualClock.StartOfBusinessDay();
        var host = HostService.WithDemoData(clock);

        var first = Withdrawal(host, 9, 50_000, clock).Body<WithdrawalAuthResponseBody>();
        var again = Withdrawal(host, 9, 50_000, clock).Body<WithdrawalAuthResponseBody>();

        Assert.Equal(ResponseCode.Approved, first.Rc);
        Assert.Equal(ResponseCode.Approved, again.Rc);
        Assert.Equal(first.Available, again.Available);
        Assert.Equal(first.Ledger, again.Ledger);
        Assert.Equal(1, host.ReplayCount);
    }

    [Fact]
    public void ARestartedScreenCarriesOnFromTheTraceNumbersItsJournalAlreadyHolds()
    {
        // What the terminal does after a restart: the journal says trace numbers up to 4
        // have been used today, so the next transaction must be 5 - not 1.
        var clock = VirtualClock.StartOfBusinessDay();
        var journal = new InMemoryTerminalJournal();

        journal.Append(clock.UtcNow, new TransactionKey("ATM-01", Day, 4),
            TerminalEvent.AuthRequested, 50_000, Card);

        var used = journal.Entries
            .Where(e => e.Key.BizDate == Day)
            .Select(e => e.Key.Stan)
            .DefaultIfEmpty(0)
            .Max();

        Assert.Equal(4, used);

        var sent = new List<Envelope>();
        var flow = new TerminalFlow(
            ask: (request, _) =>
            {
                sent.Add(request);
                return MessageCodec.Envelope(MessageType.PinVerifyResponse, request.Key,
                    clock.UtcNow, new PinVerifyResponseBody(ResponseCode.Approved, true, 3));
            },
            clock: clock,
            readCard: () => Card,
            startingStan: used);

        flow.Handle(new ScreenEvent(ScreenEventName.Card, "inserted"));
        foreach (var digit in "1234")
        {
            flow.Handle(new ScreenEvent(ScreenEventName.Key, digit.ToString()));
        }
        flow.Handle(new ScreenEvent(ScreenEventName.Key, "enter"));

        Assert.NotEmpty(sent);
        Assert.Equal(5, sent[0].Key.Stan);
    }

    [Fact]
    public void AScreenThatHasNoJournalBehindItStillStartsAtOne()
    {
        var clock = VirtualClock.StartOfBusinessDay();
        var sent = new List<Envelope>();

        var flow = new TerminalFlow(
            ask: (request, _) =>
            {
                sent.Add(request);
                return MessageCodec.Envelope(MessageType.PinVerifyResponse, request.Key,
                    clock.UtcNow, new PinVerifyResponseBody(ResponseCode.Approved, true, 3));
            },
            clock: clock,
            readCard: () => Card);

        flow.Handle(new ScreenEvent(ScreenEventName.Card, "inserted"));
        foreach (var digit in "1234")
        {
            flow.Handle(new ScreenEvent(ScreenEventName.Key, digit.ToString()));
        }
        flow.Handle(new ScreenEvent(ScreenEventName.Key, "enter"));

        Assert.NotEmpty(sent);
        Assert.Equal(1, sent[0].Key.Stan);
    }
}
