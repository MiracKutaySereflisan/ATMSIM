// Copyright (c) 2026 Mirac Kutay Sereflisan. MIT lisansi - LICENSE dosyasina bakiniz.
// DepositEndToEndTests.cs
//
// What these tests are for: a whole deposit, from the notes going in to the account
// changing - with a real host, a real acceptor and a line that can be cut at any point.
//
// Every one of them ends by asking the conservation checker whether the books balance. In
// a deposit that question is harder than it looks, because a deposit is the first thing in
// this project that brings money in from OUTSIDE the machine. Until now the machine could
// only rearrange what it had; now a banknote can arrive that was not there this morning,
// and "a banknote arrived" and "money was created" look identical unless the machine
// counts what came in through its mouth.
//
// The pairs to read side by side:
//
//   AnUnansweredAuthorisationGivesTheMoneyBack   - escrow is the customer's
//   AnUnansweredCommitIsSentAgainNotUndone       - a drawer is not
//
// Those two are the whole of KARAR-040. Swapping them is the most expensive mistake in
// this file's subject: handing back money that is already in a drawer means the customer
// leaves with two bundles for one deposit.

using Atm.Audit;
using Atm.Protocol;
using Atm.Terminal;
using Xunit;

namespace Atm.Tests;

public class DepositEndToEndTests
{
    private const string Card = "4111111111111111";

    /// <summary>Five hundred lira in hundreds - notes this machine can take back in.</summary>
    private static NoteBundle[] FiveHundred() => [new NoteBundle(10_000, 5)];

    private static void AssertBooksBalance(SimulatedDay day,
        AuditMoment moment = AuditMoment.AtRest)
    {
        var report = ConservationChecker.Check(day.Snapshot(moment));
        Assert.True(report.IsClean, report.ToText());
    }

    [Fact]
    public void ADepositThatGoesRightMovesTheMoneyExactlyOnce()
    {
        var day = new SimulatedDay();
        var start = day.LedgerOf(Card);

        var began = day.Deposits.Begin(Card, FiveHundred(), stan: 301);
        Assert.NotNull(began.InEscrow);

        // The notes are inside the machine and the account has not moved. This is the
        // whole point of escrow.
        Assert.Equal(50_000, day.Dispenser.Position.InEscrow);
        Assert.Equal(start, day.LedgerOf(Card));
        AssertBooksBalance(day);

        var result = day.Deposits.Complete(began.InEscrow!, confirmed: true);

        Assert.Equal(DepositEnding.Credited, result.Ending);
        Assert.Equal(50_000, result.Credited);
        Assert.Equal(start + 50_000, day.LedgerOf(Card));
        Assert.Equal(0, day.Dispenser.Position.InEscrow);
        Assert.Equal(0, day.Host.OpenDeposits.Count);
        AssertBooksBalance(day, AuditMoment.EndOfDay);
    }

    [Fact]
    public void ACustomerWhoChangesTheirMindGetsTheirNotesBack()
    {
        var day = new SimulatedDay();
        var start = day.LedgerOf(Card);

        var began = day.Deposits.Begin(Card, FiveHundred(), stan: 302);
        var result = day.Deposits.Complete(began.InEscrow!, confirmed: false);

        Assert.Equal(DepositEnding.Returned, result.Ending);
        Assert.Equal(50_000, result.Returned);
        Assert.Equal(0, result.Credited);
        Assert.Equal(start, day.LedgerOf(Card));

        // Out of the machine and back in a pocket - and the host was told, so it is not
        // left waiting for the end of a deposit that is already over.
        Assert.Equal(50_000, day.Dispenser.Position.WithCustomers);
        Assert.Equal(0, day.Host.OpenDeposits.Count);
        AssertBooksBalance(day, AuditMoment.EndOfDay);
    }

    [Fact]
    public void AnUnansweredAuthorisationGivesTheMoneyBack()
    {
        // The mirror image of the withdrawal rule, and the reason it is safe here: the
        // notes are in escrow, escrow is the customer's, and nothing has been credited.
        // Giving them back cannot create money.
        var day = new SimulatedDay { LoseAnswersForType = MessageType.DepositAuthRequest };
        var start = day.LedgerOf(Card);

        var began = day.Deposits.Begin(Card, FiveHundred(), stan: 303);

        Assert.Null(began.InEscrow);
        Assert.Equal(DepositEnding.NoAnswerFromHost, began.Finished!.Ending);
        Assert.Equal(50_000, began.Finished.Returned);
        Assert.Equal(start, day.LedgerOf(Card));
        Assert.Equal(0, day.Dispenser.Position.InEscrow);

        day.LineComesBack();
        day.Clock.Advance(TimeSpan.FromSeconds(60));
        day.Flow.SendPending();

        Assert.Equal(0, day.Host.OpenDeposits.Count);
        AssertBooksBalance(day, AuditMoment.EndOfDay);
    }

    [Fact]
    public void AnUnansweredCommitIsSentAgainNotUndone()
    {
        // The notes are in a drawer. They cannot come back out, so the only thing left to
        // do is keep telling the host until it says it heard.
        var day = new SimulatedDay();
        var start = day.LedgerOf(Card);

        var began = day.Deposits.Begin(Card, FiveHundred(), stan: 304);

        // The report never reaches the host at all: the notes are in a drawer and the
        // ledger knows nothing about them. This is the gap the queue exists to close.
        day.DropRequestsOfType = MessageType.DepositCommitAdvice;
        var result = day.Deposits.Complete(began.InEscrow!, confirmed: true);

        Assert.True(result.CommitQueued, "Cevapsız commit kuyruğa girmeliydi.");
        Assert.Equal(1, day.Pending.Count);

        // The money is in the drawer already; the account has not caught up yet. That gap
        // is real, and the checker knows it is explained by the queue.
        Assert.Equal(start, day.LedgerOf(Card));
        AssertBooksBalance(day);

        day.LineComesBack();
        day.Clock.Advance(TimeSpan.FromSeconds(60));
        day.Flow.SendPending();

        Assert.Equal(start + 50_000, day.LedgerOf(Card));
        Assert.Equal(0, day.Pending.Count);
        AssertBooksBalance(day, AuditMoment.EndOfDay);
    }

    [Fact]
    public void ACommitWhoseAnswerIsLostIsSentAgainAndStillCreditsOnce()
    {
        // The worst case of the two: the host DID hear the report and DID credit the
        // account, and the terminal never found out. It will send the report again - and
        // it has to be free to do so, because the alternative is a machine that owes the
        // host a message it dare not send.
        var day = new SimulatedDay();
        var start = day.LedgerOf(Card);

        var began = day.Deposits.Begin(Card, FiveHundred(), stan: 306);

        day.LoseAnswersForType = MessageType.DepositCommitAdvice;
        day.Deposits.Complete(began.InEscrow!, confirmed: true);

        // Already credited: the host did the work, the answer went missing.
        Assert.Equal(start + 50_000, day.LedgerOf(Card));
        Assert.Equal(1, day.Pending.Count);

        day.LineComesBack();

        for (var attempt = 0; attempt < 3; attempt++)
        {
            day.Clock.Advance(TimeSpan.FromSeconds(300));
            day.Flow.SendPending();
        }

        // Sent again, acknowledged, and credited exactly once.
        Assert.Equal(start + 50_000, day.LedgerOf(Card));
        Assert.Equal(0, day.Pending.Count);
        AssertBooksBalance(day, AuditMoment.EndOfDay);
    }

    [Fact]
    public void NotesThatJamAreCreditedToNobodyAndAreNamed()
    {
        var day = new SimulatedDay();
        var start = day.LedgerOf(Card);

        var began = day.Deposits.Begin(Card, FiveHundred(), stan: 307);
        day.Dispenser.AcceptorFault = AcceptorFault.Jam();
        var result = day.Deposits.Complete(began.InEscrow!, confirmed: true);

        Assert.Equal(DepositEnding.Jammed, result.Ending);
        Assert.Equal(0, result.Credited);
        Assert.Equal(start, day.LedgerOf(Card));
        Assert.Equal(50_000, day.Dispenser.Position.Jammed);
        Assert.Equal(50_000, day.Host.JammedInDeposits);

        // The books still balance - but the report says out loud where the money is, and
        // that it is nobody's until an engineer opens the machine.
        var report = ConservationChecker.Check(day.Snapshot(AuditMoment.EndOfDay));
        Assert.True(report.IsClean, report.ToText());
        Assert.Contains(report.Explained, f => f.Code == AuditCode.StuckMoney && f.Amount == 50_000);
    }

    [Fact]
    public void APartialStackCreditsOnlyWhatReachedADrawer()
    {
        var day = new SimulatedDay();
        var start = day.LedgerOf(Card);

        var began = day.Deposits.Begin(Card, FiveHundred(), stan: 308);
        day.Dispenser.AcceptorFault = AcceptorFault.StackAtMostThis(30_000);
        var result = day.Deposits.Complete(began.InEscrow!, confirmed: true);

        Assert.Equal(DepositEnding.PartlyCredited, result.Ending);
        Assert.Equal(30_000, result.Credited);
        Assert.Equal(20_000, result.Jammed);
        Assert.Equal(start + 30_000, day.LedgerOf(Card));
        AssertBooksBalance(day, AuditMoment.EndOfDay);
    }

    [Fact]
    public void NotesThisMachineCannotKeepAreRefusedBeforeTheHostIsAsked()
    {
        var day = new SimulatedDay();

        // 20 lira notes: this machine pays them out and has no drawer that takes them in.
        var began = day.Deposits.Begin(Card, [new NoteBundle(2_000, 2)], stan: 309);

        Assert.Equal(DepositEnding.NothingAccepted, began.Finished!.Ending);
        Assert.Equal(4_000, began.Refused);
        Assert.Equal(0, day.Host.Journal.Entries
            .Count(e => e.Type == MessageType.DepositAuthRequest));
        AssertBooksBalance(day, AuditMoment.EndOfDay);
    }

    [Fact]
    public void MoneyPutInCanBeTakenOutAgainAndTheBooksStillBalance()
    {
        var day = new SimulatedDay();
        var start = day.LedgerOf(Card);

        var began = day.Deposits.Begin(Card, FiveHundred(), stan: 310);
        day.Deposits.Complete(began.InEscrow!, confirmed: true);

        var result = day.Flow.Withdraw(Card, 35_000, stan: 311);

        Assert.Equal(WithdrawalOutcome.CashTaken, result.Outcome);
        Assert.Equal(start + 50_000 - 35_000, day.LedgerOf(Card));
        AssertBooksBalance(day, AuditMoment.EndOfDay);
    }

    [Fact]
    public void TheIntentionToStackIsWrittenDownBeforeTheNotesMove()
    {
        // KARAR-041, and found by sabotage: deleting the "about to stack" line left every
        // test green. It is the only thing that makes a power cut in the one unclosable
        // window VISIBLE afterwards, so its absence has to fail loudly.
        var day = new SimulatedDay();

        var began = day.Deposits.Begin(Card, FiveHundred(), stan: 320);
        day.Deposits.Complete(began.InEscrow!, confirmed: true);

        var events = day.Journal.Entries.Select(e => e.Event).ToList();
        var intention = events.IndexOf(TerminalEvent.DepositStacking);
        var done = events.IndexOf(TerminalEvent.DepositStacked);

        Assert.True(intention >= 0, "'Kasete alıyorum' satırı yazılmamış.");
        Assert.True(done >= 0, "'Kasete aldım' satırı yazılmamış.");

        // The order is the whole point: written first, so that a machine that stops
        // between the two lines leaves a record saying it was in the middle of something.
        Assert.True(intention < done,
            $"'alıyorum' satırı 'aldım'dan sonra yazılmış (sıra: {intention}, {done}).");
    }

    [Fact]
    public void AConfirmationThatEndsInAJamStillLeavesBothLinesInTheRecord()
    {
        var day = new SimulatedDay();

        var began = day.Deposits.Begin(Card, FiveHundred(), stan: 321);
        day.Dispenser.AcceptorFault = AcceptorFault.Jam();
        day.Deposits.Complete(began.InEscrow!, confirmed: true);

        var events = day.Journal.Entries.Select(e => e.Event).ToList();

        Assert.Contains(events, e => e == TerminalEvent.DepositStacking);
        Assert.Contains(events, e => e == TerminalEvent.DepositJammed);
    }

    [Fact]
    public void MoneyIsNeverCreatedOrDestroyedAcrossAMixedRunOfBothDirections()
    {
        var day = new SimulatedDay();
        var stan = 400;

        for (var i = 0; i < 8; i++)
        {
            // A deposit, sometimes confirmed, sometimes jammed, sometimes abandoned.
            var began = day.Deposits.Begin(Card, FiveHundred(), stan: ++stan);

            if (began.InEscrow is not null)
            {
                day.Dispenser.AcceptorFault = i % 4 == 1
                    ? AcceptorFault.StackAtMostThis(20_000)
                    : AcceptorFault.None;

                day.Deposits.Complete(began.InEscrow, confirmed: i % 3 != 0);
                day.Dispenser.AcceptorFault = AcceptorFault.None;
            }

            // And a withdrawal, sometimes with a fault of its own.
            day.Dispenser.Fault = i % 5 == 2 ? DispenserFault.Jam() : DispenserFault.None;
            day.CustomerTakesCash = i % 4 != 3;
            day.LoseAnswersForType = i % 3 == 1 ? MessageType.DispenseAdvice : "";

            day.Flow.Withdraw(Card, 20_000, stan: ++stan);

            day.LineComesBack();
            day.Clock.Advance(TimeSpan.FromSeconds(300));
            day.Flow.SendPending();

            AssertBooksBalance(day);
        }

        day.Clock.Advance(TimeSpan.FromSeconds(300));
        day.Flow.SendPending();

        Assert.Equal(0, day.Pending.Count);
        Assert.Equal(0, day.Host.OpenAuthorisationCount);
        Assert.Equal(0, day.Host.OpenDeposits.Count);
        AssertBooksBalance(day, AuditMoment.EndOfDay);
    }
}
