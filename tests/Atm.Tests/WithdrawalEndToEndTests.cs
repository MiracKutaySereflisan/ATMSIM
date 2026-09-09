// Copyright (c) 2026 Mirac Kutay Sereflisan. MIT lisansi - LICENSE dosyasina bakiniz.
// WithdrawalEndToEndTests.cs
//
// What this file does: it runs whole withdrawals with a real host on one side and a real
// (fake-hardware) dispenser on the other, and after each one it adds up all the money in
// the world and checks that none of it appeared or disappeared.
//
// There is no socket here and no waiting: the line is a delegate that can be cut between
// any two messages, and the clock is virtual. That is what makes "the answer was lost
// exactly here" a test rather than an anecdote.
//
// The three tests to read first:
//
//   TheAnswerIsLostAfterTheHostHasAlreadyHeldTheMoney - the worst case in the whole
//   project. The host has taken the money out of reach, the machine does not know it, and
//   nobody has any cash. What saves it is that "no answer" produces a reversal instead of
//   a message on the screen.
//
//   CashThatComesOutAndIsNotTakenLeavesTheLedgerAlone - the notes left the drawer, so
//   the machine is lighter, and the customer still has nothing. The ledger must agree
//   with the customer, not with the drawer.
//
//   MoneyIsNeverCreatedOrDestroyedAcrossAMixedRun - twelve withdrawals, four kinds of
//   failure, and the sum of every bucket plus every account is what it was at the start.

using Atm.Audit;
using Atm.Host;
using Atm.Protocol;
using Atm.Terminal;
using Xunit;

namespace Atm.Tests;

public class WithdrawalEndToEndTests
{
    private const string Card = "4111111111111111";

    /// <summary>
    /// One machine, one host and a line between them that can be cut on demand.
    /// </summary>
    private sealed class Machine : SimulatedDay
    {
        public Machine(bool customerTakesCash = true) : base(customerTakesCash)
            => StartingLedger = LedgerOf(Card);

        /// <summary>The demo card's balance when the day started, in kurus.</summary>
        public long StartingLedger { get; }

        /// <summary>
        /// Every banknote in this world, wherever it is, plus what the account still says
        /// it has. Withdrawing money moves it from the account to the customer, so the sum
        /// of the two only stays constant when the ledger agrees with the cash.
        /// </summary>
        public void AssertNothingWasCreatedOrDestroyed()
        {
            var position = Dispenser.Position;

            Assert.Equal(StartingCash, position.Total);

            // What the customer holds must be exactly what the ledger says has gone.
            var goneFromTheAccount = StartingLedger - LedgerOf(Card);
            Assert.Equal(goneFromTheAccount, position.WithCustomers);

            // The same claim again, this time through the runnable layer (stage 2g). Both
            // are kept on purpose: the two lines above are written out by hand and cannot
            // be wrong in the same way the checker can, so a checker that starts passing
            // everything is caught here rather than agreeing with itself.
            var report = ConservationChecker.Check(Snapshot());
            Assert.True(report.IsClean, report.ToText());
        }
    }

    [Fact]
    public void AWithdrawalThatGoesRightMovesTheMoneyExactlyOnce()
    {
        var machine = new Machine();

        var result = machine.Flow.Withdraw(Card, 35_000, stan: 101);

        Assert.Equal(WithdrawalOutcome.CashTaken, result.Outcome);
        Assert.Equal(35_000, result.CashToCustomer);
        Assert.Equal(35_000, machine.Dispenser.Position.WithCustomers);
        Assert.Equal(215_000, machine.LedgerOf(Card));
        Assert.Equal(0, machine.Host.OpenAuthorisationCount);
        Assert.Equal(0, machine.Pending.Count);
        machine.AssertNothingWasCreatedOrDestroyed();
    }

    [Fact]
    public void AnAmountThisMachineCannotBuildNeverReachesTheHost()
    {
        var machine = new Machine();

        // 30 lira: a multiple of ten, and impossible out of 200, 100, 50 and 20 notes.
        var result = machine.Flow.Withdraw(Card, 3_000, stan: 101);

        Assert.Equal(WithdrawalOutcome.AmountCannotBeDispensed, result.Outcome);
        // The whole point: no promise was made, so there is nothing to take back. The
        // host was not asked anything at all - not even a refused question.
        Assert.Equal(0, machine.MessagesDelivered);
        Assert.Equal(0, machine.Host.Journal.Entries
            .Count(e => e.Type != MessageType.BalanceRequest));
        machine.AssertNothingWasCreatedOrDestroyed();
    }

    [Fact]
    public void TheAnswerIsLostAfterTheHostHasAlreadyHeldTheMoney()
    {
        var machine = new Machine { AnswersAreLost = true };

        var result = machine.Flow.Withdraw(Card, 35_000, stan: 101);

        // The terminal does not know whether the money was held. It does NOT say "your
        // transaction did not happen" - it says nothing about the account at all and
        // starts trying to take the promise back.
        Assert.Equal(WithdrawalOutcome.NoAnswerFromHost, result.Outcome);
        Assert.Equal(0, result.CashToCustomer);
        Assert.Equal(0, machine.Dispenser.Position.WithCustomers);

        // The reversal reached the host - its answer was lost, not the message - so the
        // hold is already gone, and the reversal stays queued until an answer arrives.
        Assert.Equal(0, machine.Host.OpenAuthorisationCount);
        Assert.Equal(1, machine.Pending.Count);
        Assert.Equal(250_000, machine.LedgerOf(Card));
        machine.AssertNothingWasCreatedOrDestroyed();
    }

    [Fact]
    public void AQueuedReversalStopsOnlyWhenTheHostFinallyAnswers()
    {
        var machine = new Machine { AnswersAreLost = true };
        machine.Flow.Withdraw(Card, 35_000, stan: 101);
        Assert.Equal(1, machine.Pending.Count);

        // Ten seconds later the first retry is due, and the answer is still being lost.
        machine.Clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(1, machine.Flow.SendPending());
        Assert.Equal(1, machine.Pending.Count);

        // The line recovers. The next attempt is due after thirty seconds.
        machine.AnswersAreLost = false;
        machine.Clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(1, machine.Flow.SendPending());

        Assert.Equal(0, machine.Pending.Count);
        Assert.Equal(250_000, machine.AvailableOf(Card));
        machine.AssertNothingWasCreatedOrDestroyed();
    }

    [Fact]
    public void AReversalIsSentEvenWhenTheHostNeverHeardOfTheWithdrawal()
    {
        var machine = new Machine { LineIsDown = true };

        var result = machine.Flow.Withdraw(Card, 35_000, stan: 101);

        Assert.Equal(WithdrawalOutcome.NoAnswerFromHost, result.Outcome);
        Assert.Equal(0, machine.MessagesDelivered);
        // Queued, because the terminal cannot tell this case from the one above.
        Assert.Equal(1, machine.Pending.Count);

        machine.LineIsDown = false;
        machine.Clock.Advance(TimeSpan.FromSeconds(10));
        machine.Flow.SendPending();

        // The host accepts a reversal for a transaction it never saw, and says so.
        Assert.Equal(0, machine.Pending.Count);
        Assert.True(machine.Host.Journal.Entries.Any(e => e.Note.Contains("never saw")),
            "the host should have recorded a reversal for a transaction it never saw");
        machine.AssertNothingWasCreatedOrDestroyed();
    }

    [Fact]
    public void APartialDispenseDebitsOnlyWhatCameOut()
    {
        var machine = new Machine();
        // 350 planned as 200 + 100 + 50; the machine stops after 300.
        machine.Dispenser.Fault = DispenserFault.PresentAtMostThis(30_000);

        var result = machine.Flow.Withdraw(Card, 35_000, stan: 101);

        Assert.Equal(WithdrawalOutcome.PartialCashTaken, result.Outcome);
        Assert.Equal(30_000, result.CashToCustomer);
        Assert.Equal(220_000, machine.LedgerOf(Card));
        // And no promise is left standing over the fifty lira that never came out.
        Assert.Equal(220_000, machine.AvailableOf(Card));
        machine.AssertNothingWasCreatedOrDestroyed();
    }

    [Fact]
    public void CashThatComesOutAndIsNotTakenLeavesTheLedgerAlone()
    {
        var machine = new Machine(customerTakesCash: false);

        var result = machine.Flow.Withdraw(Card, 35_000, stan: 101);

        Assert.Equal(WithdrawalOutcome.CashNotTaken, result.Outcome);
        Assert.Equal(0, result.CashToCustomer);
        // The drawer is lighter and the customer has nothing.
        Assert.Equal(35_000, machine.Dispenser.Position.Retracted);
        Assert.Equal(0, machine.Dispenser.Position.WithCustomers);
        // So the account must be untouched. The ledger agrees with the customer.
        Assert.Equal(250_000, machine.LedgerOf(Card));
        Assert.Equal(250_000, machine.AvailableOf(Card));
        machine.AssertNothingWasCreatedOrDestroyed();
    }

    [Fact]
    public void AJammedDispenserPostsNothingAndLeavesNoPromiseStanding()
    {
        var machine = new Machine();
        machine.Dispenser.Fault = DispenserFault.Jam();

        var result = machine.Flow.Withdraw(Card, 35_000, stan: 101);

        Assert.Equal(WithdrawalOutcome.NoCashCameOut, result.Outcome);
        Assert.Equal(250_000, machine.LedgerOf(Card));
        Assert.Equal(250_000, machine.AvailableOf(Card));
        Assert.Equal(0, machine.Host.OpenAuthorisationCount);
        machine.AssertNothingWasCreatedOrDestroyed();
    }

    [Fact]
    public void AnAdviceWhoseAnswerIsLostKeepsBeingSentUntilItLands()
    {
        var machine = new Machine();
        // The authorisation goes through normally; only the advice loses its answer.
        machine.LoseAnswersForType = MessageType.DispenseAdvice;

        var result = machine.Flow.Withdraw(Card, 35_000, stan: 101);

        Assert.Equal(WithdrawalOutcome.CashTaken, result.Outcome);
        Assert.Equal(1, machine.Pending.Count);

        // The customer has the cash. The host has POSTED it - the advice arrived, only
        // its acknowledgement was lost - so the two sides already agree. What is still
        // open is the terminal's knowledge of that, and the queue is what closes it.
        Assert.Equal(35_000, machine.Dispenser.Position.WithCustomers);
        Assert.Equal(215_000, machine.LedgerOf(Card));

        machine.LoseAnswersForType = "";
        machine.Clock.Advance(TimeSpan.FromSeconds(10));
        machine.Flow.SendPending();

        // The repeat is recognised as a repeat, not applied a second time.
        Assert.Equal(0, machine.Pending.Count);
        Assert.Equal(215_000, machine.LedgerOf(Card));
        machine.AssertNothingWasCreatedOrDestroyed();
    }

    [Fact]
    public void AnAdviceThatNeverArrivesLeavesThePromiseStandingUntilItDoes()
    {
        var machine = new Machine();
        // The authorisation goes through; the advice never reaches the host at all.
        machine.DropRequestsOfType = MessageType.DispenseAdvice;

        var result = machine.Flow.Withdraw(Card, 35_000, stan: 101);

        Assert.Equal(WithdrawalOutcome.CashTaken, result.Outcome);
        // The customer has the money. The host does not know it and is still holding
        // the promise: this is the hanging hold KARAR-029 said this design would produce.
        Assert.Equal(35_000, machine.Dispenser.Position.WithCustomers);
        Assert.Equal(250_000, machine.LedgerOf(Card));
        Assert.Equal(215_000, machine.AvailableOf(Card));
        Assert.Equal(1, machine.Host.OpenAuthorisationCount);
        Assert.Equal(1, machine.Pending.Count);

        // In THIS window the two sides disagree, and only the end-of-day count would find
        // it if the queue gave up. The queue does not give up.
        machine.DropRequestsOfType = "";
        machine.Clock.Advance(TimeSpan.FromSeconds(10));
        machine.Flow.SendPending();

        Assert.Equal(0, machine.Pending.Count);
        Assert.Equal(0, machine.Host.OpenAuthorisationCount);
        Assert.Equal(215_000, machine.LedgerOf(Card));
        machine.AssertNothingWasCreatedOrDestroyed();
    }

    [Fact]
    public void AHostThatSaysNoCostsNothingAndLeavesNothingBehind()
    {
        var machine = new Machine();

        // 4.000 lira out of an account holding 2.500.
        var result = machine.Flow.Withdraw(Card, 400_000, stan: 101);

        Assert.Equal(WithdrawalOutcome.RefusedByHost, result.Outcome);
        Assert.Equal(ResponseCode.InsufficientFunds, result.Rc);
        Assert.Equal(0, machine.Pending.Count);
        Assert.Equal(0, machine.Host.OpenAuthorisationCount);
        machine.AssertNothingWasCreatedOrDestroyed();
    }

    [Fact]
    public void MoneyIsNeverCreatedOrDestroyedAcrossAMixedRun()
    {
        var machine = new Machine();

        for (var i = 0; i < 12; i++)
        {
            machine.Dispenser.Fault = (i % 4) switch
            {
                0 => DispenserFault.None,
                1 => DispenserFault.PresentAtMostThis(20_000),
                2 => DispenserFault.Jam(),
                _ => DispenserFault.None,
            };
            machine.CustomerTakesCash = i % 3 != 0;
            machine.AnswersAreLost = i % 5 == 0;

            machine.Flow.Withdraw(Card, 35_000, stan: 200 + i);

            machine.AnswersAreLost = false;
            machine.Clock.Advance(TimeSpan.FromSeconds(60));
            machine.Flow.SendPending();

            machine.AssertNothingWasCreatedOrDestroyed();
        }

        // Nothing is left waiting and no promise is left standing.
        Assert.Equal(0, machine.Pending.Count);
        Assert.Equal(0, machine.Host.OpenAuthorisationCount);
        machine.AssertNothingWasCreatedOrDestroyed();
    }
}
