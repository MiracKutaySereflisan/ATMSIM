// CutoverTests.cs
//
// What these tests are for: the end of a business day - the moment the machine and the
// host agree on what happened and only then start writing into a new day.
//
// The claim under test is one sentence: THE DATE MOVES ONLY ON A YES. Most of the file is
// that sentence taken apart. No answer is not a yes. A difference is not a yes. Business
// still open on either side is not a yes. A day already closed is not a second yes.
//
// The pair to read side by side:
//
//   ADayThatWentRightClosesAndTheDateMoves    - the whole thing working
//   TheCalendarAloneNeverMovesTheBusinessDay  - why it exists at all
//
// The second one is the test the old code would have failed. Before KARAR-044 the business
// date was read off the calendar, so it changed by itself at midnight UTC: no totals were
// compared, nothing was recorded, and the machine simply began writing a different day
// into its transaction keys - at three in the morning, local time, with nobody watching.
//
// One test plants a difference by hand (ADifferenceRefusesTheCloseAndSaysHowBig). That is
// deliberate and it is the only honest way to test a detector: a detector that has never
// been shown a difference is a detector nobody has tested. The planted line has the exact
// shape of failure A-01 in reports/failure-catalog.md - cash the machine handed over that
// the host never debited, with an empty queue behind it.

using Atm.Audit;
using Atm.Protocol;
using Atm.Terminal;
using Xunit;

namespace Atm.Tests;

public class CutoverTests
{
    private const string Card = "4111111111111111";

    /// <summary>Two hundred lira as one note - the smallest thing that moves both books.</summary>
    private static Envelope AuthRequest(SimulatedDay day, int stan) =>
        MessageCodec.Envelope(MessageType.WithdrawalAuthRequest,
            new TransactionKey("ATM-01", day.Day.Current, stan), day.Clock.UtcNow,
            new WithdrawalAuthRequestBody(Card, 20_000, [new DenominationLine(20_000, 1)]));

    [Fact]
    public void ADayThatWentRightClosesAndTheDateMoves()
    {
        var day = new SimulatedDay();
        var opened = day.Day.Current;

        day.Flow.Withdraw(Card, 20_000, stan: 401);
        day.Deposits.Complete(
            day.Deposits.Begin(Card, [new NoteBundle(10_000, 3)], stan: 402).InEscrow!,
            confirmed: true);

        var result = day.Cutover.Close(stan: 900);

        Assert.True(result.Closed, result.Reason);
        Assert.Equal(ResponseCode.Approved, result.Rc);

        // The two sides counted separately and got the same three numbers. That is the
        // whole claim; everything else in this file is a way of it failing.
        Assert.Equal(result.Mine, result.Theirs);
        Assert.Equal(20_000, result.Mine.Withdrawals);
        Assert.Equal(30_000, result.Mine.Deposits);
        Assert.Equal(2, result.Mine.Count);

        Assert.NotEqual(opened, day.Day.Current);
        Assert.True(day.Host.IsClosed(opened));
    }

    [Fact]
    public void TheCalendarAloneNeverMovesTheBusinessDay()
    {
        var day = new SimulatedDay();
        var opened = day.Day.Current;

        // A day and a half of wall clock passes without anybody closing anything.
        day.Clock.Sleep(TimeSpan.FromHours(36));

        day.Flow.Withdraw(Card, 20_000, stan: 403);

        // The transaction is still in the day the machine was told it was in. A machine
        // that read the calendar here would have started a new day on its own - and
        // yesterday's totals would never be agreed by anyone, because nobody would ever
        // ask about a day the machine had already walked out of.
        Assert.Equal(opened, day.Day.Current);
        Assert.Equal(opened, day.Journal.Entries[^1].Key.BizDate);
    }

    [Fact]
    public void AfterACloseTheNextTransactionIsInTheNewDay()
    {
        var day = new SimulatedDay();
        var first = day.Day.Current;

        day.Flow.Withdraw(Card, 20_000, stan: 404);
        Assert.True(day.Cutover.Close(stan: 901).Closed);

        var second = day.Day.Current;
        day.Flow.Withdraw(Card, 20_000, stan: 405);

        Assert.Equal(second, day.Journal.Entries[^1].Key.BizDate);
        Assert.NotEqual(first, second);

        // And the new day closes on its own totals, not on yesterday's.
        var again = day.Cutover.Close(stan: 902);
        Assert.True(again.Closed, again.Reason);
        Assert.Equal(20_000, again.Mine.Withdrawals);
        Assert.Equal(1, again.Mine.Count);
    }

    [Fact]
    public void SilenceDoesNotCloseTheDay()
    {
        var day = new SimulatedDay();
        var opened = day.Day.Current;

        day.Flow.Withdraw(Card, 20_000, stan: 406);
        day.LineIsDown = true;

        var result = day.Cutover.Close(stan: 903);

        Assert.False(result.Closed);
        Assert.Equal("", result.Rc);
        Assert.Null(result.Theirs);
        Assert.Equal(opened, day.Day.Current);
        Assert.False(day.Host.IsClosed(opened));
        Assert.Equal(TerminalEvent.CutoverNoAnswer, day.Journal.Entries[^1].Event);
    }

    [Fact]
    public void AQueuedAdviceStopsTheCloseBeforeTheHostIsEvenAsked()
    {
        var day = new SimulatedDay();
        day.LoseAnswersForType = MessageType.DispenseAdvice;

        day.Flow.Withdraw(Card, 20_000, stan: 407);
        Assert.True(day.Pending.Count > 0);

        var delivered = day.MessagesDelivered;
        var result = day.Cutover.Close(stan: 904);

        Assert.False(result.Closed);
        Assert.Equal("", result.Rc);

        // Nothing was sent. The difference would have been real and the reason for it
        // would have been delay, not disagreement - and answering both with one code is
        // how a genuine difference gets waved away as "probably the queue" (KARAR-046).
        Assert.Equal(delivered, day.MessagesDelivered);
        Assert.Equal(TerminalEvent.CutoverBlocked, day.Journal.Entries[^1].Event);
    }

    [Fact]
    public void AnOpenPromiseOnTheHostSideRefusesTheClose()
    {
        var day = new SimulatedDay();

        // An authorisation the machine asked for and never finished - the shape a crash
        // between "yes" and the notes leaves behind. The terminal's queue is empty,
        // because the terminal never got as far as having anything to send.
        day.Host.Handle(AuthRequest(day, stan: 408));
        Assert.Equal(0, day.Pending.Count);
        Assert.Equal(1, day.Host.OpenAuthorisationCount);

        var result = day.Cutover.Close(stan: 905);

        Assert.False(result.Closed);
        Assert.Equal(ResponseCode.ReconcileError, result.Rc);
        Assert.Contains("açık", result.Reason);
        Assert.False(day.Host.IsClosed(day.Day.Current));
    }

    [Fact]
    public void ADifferenceRefusesTheCloseAndSaysHowBig()
    {
        var day = new SimulatedDay();
        day.Flow.Withdraw(Card, 20_000, stan: 409);

        // Planted: the machine's record says a hundred lira reached a customer that the
        // host was never told about, and there is nothing in the queue to explain it.
        day.Journal.Append(day.Clock.UtcNow,
            new TransactionKey("ATM-01", day.Day.Current, 999),
            TerminalEvent.CashTaken, 10_000, Card, "planted difference");

        var result = day.Cutover.Close(stan: 906);

        Assert.False(result.Closed);
        Assert.Equal(ResponseCode.ReconcileError, result.Rc);
        Assert.Equal(10_000, result.Difference);
        Assert.Equal(30_000, result.Mine.Withdrawals);
        Assert.Equal(20_000, result.Theirs!.Withdrawals);
        Assert.False(day.Host.IsClosed(day.Day.Current));

        // The difference is in the machine's own record, where tomorrow's operator can
        // find it. A refusal nobody wrote down would be a day that simply never closed.
        Assert.Equal(TerminalEvent.CutoverOutOfBalance, day.Journal.Entries[^1].Event);
    }

    [Fact]
    public void ClosingADayTwiceChangesNothingAndAdmitsIt()
    {
        var day = new SimulatedDay();
        day.Flow.Withdraw(Card, 20_000, stan: 410);

        var closed = day.Day.Current;
        Assert.True(day.Cutover.Close(stan: 907).Closed);
        var after = day.Day.Current;

        // A second close arriving with a NEW trace number: the replay table cannot catch
        // it, because its key includes the trace number (KARAR-031).
        var second = day.Host.Handle(MessageCodec.Envelope(MessageType.CutoverRequest,
            new TransactionKey("ATM-01", closed, 908), day.Clock.UtcNow,
            new CutoverRequestBody("2099-01-01", DayTotals.Empty)));

        var body = second.Body<CutoverResponseBody>();

        Assert.Equal(ResponseCode.AlreadyReconciled, body.Rc);
        Assert.Equal(20_000, body.Totals.Withdrawals);   // the first close's figures
        Assert.Equal(after, day.Day.Current);            // and nothing moved
    }

    [Fact]
    public void ACloseCountsMovementNotIntent()
    {
        var day = new SimulatedDay();

        // A withdrawal the host refuses, and a deposit the customer takes back. Both are
        // real transactions with real journal lines behind them, and neither moved a
        // banknote out of or into the bank.
        day.Flow.Withdraw(Card, 500_000_00, stan: 411);
        day.Deposits.Complete(
            day.Deposits.Begin(Card, [new NoteBundle(10_000, 2)], stan: 412).InEscrow!,
            confirmed: false);

        var result = day.Cutover.Close(stan: 909);

        Assert.True(result.Closed, result.Reason);
        Assert.Equal(DayTotals.Empty, result.Mine);
        Assert.Equal(DayTotals.Empty, result.Theirs);
    }

    [Fact]
    public void TheBusinessDayNeverGoesBackwards()
    {
        var business = new BusinessDay("2026-01-05");

        Assert.Equal("2026-01-06", business.Next);
        Assert.Throws<InvalidOperationException>(() => business.RollTo("2026-01-04"));
        Assert.Throws<InvalidOperationException>(() => business.RollTo("2026-01-05"));

        business.RollTo("2026-01-06");
        Assert.Equal("2026-01-06", business.Current);
    }

    [Fact]
    public void TheTwoSidesCountTheSameDayAndOnlyThatDay()
    {
        var day = new SimulatedDay();
        var first = day.Day.Current;

        day.Flow.Withdraw(Card, 20_000, stan: 413);
        Assert.True(day.Cutover.Close(stan: 910).Closed);

        day.Flow.Withdraw(Card, 40_000, stan: 414);

        // Yesterday's total is still yesterday's. A total that grew after the day was
        // settled would mean the books could be rewritten behind a closed day.
        Assert.Equal(20_000, day.Host.ClosedDays[first].Withdrawals);
        Assert.Equal(20_000,
            TerminalDayTotals.For(day.Journal.Entries, first).Withdrawals);
        Assert.Equal(40_000,
            TerminalDayTotals.For(day.Journal.Entries, day.Day.Current).Withdrawals);
    }
}
