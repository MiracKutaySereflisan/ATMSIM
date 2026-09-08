// Copyright (c) 2026 Mirac Kutay Sereflisan. MIT lisansi - LICENSE dosyasina bakiniz.
// CashDispenserTests.cs
//
// What this file does: it checks that money only ever MOVES. Every test here ends by
// adding the four buckets up and finding the same total it started with, because the
// one thing a cash dispenser must never do is create or destroy a banknote.
//
// The two tests to read first:
//
//   RetractedCashGoesToItsOwnBucketAndNotBackIntoTheCassette - the notes came out and
//   went back in, and they are now in neither the customer's hands nor the drawer. Any
//   design that puts them back in the drawer shows more money there than the drawer
//   holds, and the difference turns up at day end with nothing to explain it.
//
//   APartialDispenseLeavesTheNotesItNeverReachedInTheirDrawer - a jam half way through
//   is not "the whole amount left and some came back". The notes that were never picked
//   up never moved at all.

using Atm.Terminal;
using Xunit;

namespace Atm.Tests;

public class CashDispenserTests
{
    private static (FakeCashDispenser dispenser, CassetteSet cassettes, long startingTotal) NewMachine()
    {
        var cassettes = CassetteSet.Standard();
        var dispenser = new FakeCashDispenser(cassettes);
        return (dispenser, cassettes, dispenser.Position.Total);
    }

    private static DenominationPlan PlanFor(CassetteSet cassettes, long amount) =>
        DenominationPlanner.Plan(cassettes, amount);

    [Fact]
    public void MoneyHandedOverLeavesTheCassettesAndWaitsAtTheMouth()
    {
        var (dispenser, cassettes, total) = NewMachine();

        var handedOver = dispenser.Present(PlanFor(cassettes, 35_000));

        Assert.Equal(35_000, handedOver);
        Assert.Equal(35_000, dispenser.Position.AtTheMouth);
        // Not with the customer yet. Nobody has touched it.
        Assert.Equal(0, dispenser.Position.WithCustomers);
        Assert.Equal(total, dispenser.Position.Total);
    }

    [Fact]
    public void MoneyTheCustomerTakesLeavesTheMachineForGood()
    {
        var (dispenser, cassettes, total) = NewMachine();
        dispenser.Present(PlanFor(cassettes, 35_000));

        var taken = dispenser.CustomerTakes();

        Assert.Equal(35_000, taken);
        Assert.Equal(0, dispenser.Position.AtTheMouth);
        Assert.Equal(35_000, dispenser.Position.WithCustomers);
        Assert.Equal(total, dispenser.Position.Total);
    }

    [Fact]
    public void RetractedCashGoesToItsOwnBucketAndNotBackIntoTheCassette()
    {
        var (dispenser, cassettes, total) = NewMachine();
        var cassettesAfterPresenting = 0L;

        dispenser.Present(PlanFor(cassettes, 35_000));
        cassettesAfterPresenting = dispenser.Position.InCassettes;

        var pulledBack = dispenser.Retract();

        Assert.Equal(35_000, pulledBack);
        Assert.Equal(35_000, dispenser.Position.Retracted);
        Assert.Equal(0, dispenser.Position.WithCustomers);
        // The drawers did NOT get their notes back. This is the whole point.
        Assert.Equal(cassettesAfterPresenting, dispenser.Position.InCassettes);
        Assert.Equal(total, dispenser.Position.Total);
    }

    [Fact]
    public void APartialDispenseLeavesTheNotesItNeverReachedInTheirDrawer()
    {
        var (dispenser, cassettes, total) = NewMachine();
        // 350 lira planned as 200 + 100 + 50; the machine stops after 300.
        dispenser.Fault = DispenserFault.PresentAtMostThis(30_000);

        var handedOver = dispenser.Present(PlanFor(cassettes, 35_000));

        Assert.Equal(30_000, handedOver);
        Assert.Equal(30_000, dispenser.Position.AtTheMouth);
        // The 50 lira note is still in its drawer, not "dispensed and returned".
        Assert.Equal(total - 30_000, dispenser.Position.InCassettes);
        Assert.Equal(total, dispenser.Position.Total);
    }

    [Fact]
    public void AJamBeforeTheFirstNoteMovesNothingAtAll()
    {
        var (dispenser, cassettes, total) = NewMachine();
        dispenser.Fault = DispenserFault.Jam();

        var handedOver = dispenser.Present(PlanFor(cassettes, 35_000));

        Assert.Equal(0, handedOver);
        Assert.Equal(0, dispenser.Position.AtTheMouth);
        Assert.Equal(total, dispenser.Position.InCassettes);
    }

    [Fact]
    public void APartialDispenseThatIsThenRetractedLeavesNobodyHoldingAnything()
    {
        var (dispenser, cassettes, total) = NewMachine();
        dispenser.Fault = DispenserFault.PresentAtMostThis(30_000);

        dispenser.Present(PlanFor(cassettes, 35_000));
        dispenser.Retract();

        // 300 in the retract bin, 50 never left its drawer, nothing with the customer.
        Assert.Equal(30_000, dispenser.Position.Retracted);
        Assert.Equal(0, dispenser.Position.WithCustomers);
        Assert.Equal(0, dispenser.Position.AtTheMouth);
        Assert.Equal(total, dispenser.Position.Total);
    }

    [Fact]
    public void TheMachineRefusesToHandOverWhileMoneyIsStillWaitingAtTheMouth()
    {
        var (dispenser, cassettes, _) = NewMachine();
        dispenser.Present(PlanFor(cassettes, 35_000));

        // A second customer cannot be served while the first one's cash is still in the
        // mouth. Allowing it would put two people's money in one bucket with no way to
        // say afterwards whose was whose.
        var thrown = Assert.Throws<InvalidOperationException>(
            () => dispenser.Present(PlanFor(cassettes, 20_000)));

        Assert.True(thrown.Message.Contains("still waiting at the mouth"), thrown.Message);
    }

    [Fact]
    public void ARefusedPlanCannotBeHandedOver()
    {
        var (dispenser, cassettes, total) = NewMachine();
        // 30 lira: a multiple of ten, and there is no way to build it out of 200, 100,
        // 50 and 20 notes.
        var refused = PlanFor(cassettes, 3_000);
        Assert.False(refused.CanDispense, "30 lira should not be dispensable from these cassettes");

        Assert.Throws<InvalidOperationException>(() => dispenser.Present(refused));
        Assert.Equal(total, dispenser.Position.InCassettes);
    }

    [Fact]
    public void TakingNothingFromAnEmptyMouthIsNotAnError()
    {
        var (dispenser, _, total) = NewMachine();

        // The flow asks after a jam: nothing came out, so nothing is taken. This has to
        // be an ordinary zero rather than an exception, because "no cash appeared" is a
        // normal outcome of a withdrawal, not a programming mistake.
        Assert.Equal(0, dispenser.CustomerTakes());
        Assert.Equal(0, dispenser.Retract());
        Assert.Equal(total, dispenser.Position.Total);
    }

    [Fact]
    public void ManyWithdrawalsInARowNeverChangeTheTotal()
    {
        var (dispenser, cassettes, total) = NewMachine();

        for (var i = 0; i < 20; i++)
        {
            dispenser.Fault = (i % 3) switch
            {
                0 => DispenserFault.None,
                1 => DispenserFault.PresentAtMostThis(10_000),
                _ => DispenserFault.Jam(),
            };

            dispenser.Present(PlanFor(cassettes, 35_000));

            if (i % 2 == 0)
            {
                dispenser.CustomerTakes();
            }
            else
            {
                dispenser.Retract();
            }
        }

        // Twenty withdrawals, three kinds of fault, two kinds of ending - and the same
        // amount of money in the world as when the machine was loaded.
        Assert.Equal(total, dispenser.Position.Total);
    }
}
