// CashAcceptorTests.cs
//
// What these tests are for: the physical side of a deposit - what happens to the paper,
// and only that. No host, no account, no screen.
//
// Every test here ends the same way, and it is the point of the file: the machine's total
// does not change. Money moves between buckets - the mouth, escrow, a drawer, the jam -
// and each move leaves the sum alone. A deposit is the first thing in this project that
// brings a banknote in from outside, so the sum is the thing most likely to go wrong here.

using Atm.Terminal;
using Xunit;

namespace Atm.Tests;

public class CashAcceptorTests
{
    /// <summary>The standard load: only the 100 and 50 lira drawers are recyclers.</summary>
    private static (FakeCashDispenser Machine, CassetteSet Cassettes) NewMachine()
    {
        var cassettes = CassetteSet.Standard();
        return (new FakeCashDispenser(cassettes), cassettes);
    }

    private static NoteBundle[] Hundreds(int count) => [new NoteBundle(10_000, count)];

    [Fact]
    public void CountedNotesWaitInEscrowAndNoDrawerChanges()
    {
        var (machine, cassettes) = NewMachine();
        var before = machine.Position;
        var drawerBefore = cassettes.TotalValue;

        var counted = machine.AcceptIntoEscrow(Hundreds(5));

        Assert.Equal(50_000, counted.Counted);
        Assert.Equal(0, counted.Refused);
        Assert.Equal(50_000, machine.Position.InEscrow);

        // Nothing has reached a drawer: the money is inside the machine and still the
        // customer's.
        Assert.Equal(drawerBefore, cassettes.TotalValue);
        Assert.Equal(before.Total, machine.Position.Total);
    }

    [Fact]
    public void ANoteThisMachineHasNoDrawerForIsPushedStraightBackOut()
    {
        // The standard load recycles 200, 100 and 50 lira. A 20 lira note can be paid out
        // of this machine and cannot be taken back into it.
        var (machine, _) = NewMachine();

        var counted = machine.AcceptIntoEscrow([new NoteBundle(2_000, 2), new NoteBundle(10_000, 1)]);

        Assert.Equal(10_000, counted.Counted);
        Assert.Equal(4_000, counted.Refused);
        Assert.Equal(10_000, machine.Position.InEscrow);

        // The refused notes never entered: they are not counted as having arrived, so the
        // machine's total is unchanged by them.
        Assert.Equal(10_000, machine.Position.TakenFromCustomers);
    }

    [Fact]
    public void StackingPutsTheEscrowIntoTheRecyclerDrawers()
    {
        var (machine, cassettes) = NewMachine();
        var before = machine.Position;
        var drawerBefore = cassettes.TotalValue;

        machine.AcceptIntoEscrow(Hundreds(5));
        var stacked = machine.Stack();

        Assert.Equal(50_000, stacked.Stacked);
        Assert.Equal(0, stacked.Jammed);
        Assert.Equal(0, machine.Position.InEscrow);
        Assert.Equal(drawerBefore + 50_000, cassettes.TotalValue);
        Assert.Equal(before.Total, machine.Position.Total);
    }

    [Fact]
    public void MoneyPutInCanBePaidOutAgain()
    {
        // This is what "recycler" means, and it is the whole reason the deposit and the
        // withdrawal share one set of drawers rather than two.
        var (machine, cassettes) = NewMachine();
        var hundreds = cassettes.ByDenomination(10_000)!;
        var before = hundreds.NoteCount;

        machine.AcceptIntoEscrow(Hundreds(5));
        machine.Stack();

        Assert.Equal(before + 5, hundreds.NoteCount);

        var plan = DenominationPlanner.Plan(cassettes, 50_000);
        Assert.True(plan.CanDispense, "Yatırılan paranın çekilebilir olması gerekiyordu.");
    }

    [Fact]
    public void AReturnedDepositGoesBackToTheCustomerAndTheTotalIsUnchanged()
    {
        var (machine, cassettes) = NewMachine();
        var before = machine.Position;
        var drawerBefore = cassettes.TotalValue;

        machine.AcceptIntoEscrow(Hundreds(5));
        var back = machine.ReturnFromEscrow();

        Assert.Equal(50_000, back);
        Assert.Equal(0, machine.Position.InEscrow);
        Assert.Equal(drawerBefore, cassettes.TotalValue);

        // It came in and it went out again. Both are counted, and they cancel.
        Assert.Equal(50_000, machine.Position.TakenFromCustomers);
        Assert.Equal(50_000, machine.Position.WithCustomers);
        Assert.Equal(before.Total, machine.Position.Total);
    }

    [Fact]
    public void AJamLeavesTheMoneyInItsOwnBucketAndNotInADrawer()
    {
        var (machine, cassettes) = NewMachine();
        var before = machine.Position;
        var drawerBefore = cassettes.TotalValue;

        machine.AcceptIntoEscrow(Hundreds(5));
        machine.AcceptorFault = AcceptorFault.Jam();
        var stacked = machine.Stack();

        Assert.True(stacked.NothingStacked, "Sıkışmada hiçbir banknot kasete girmemeliydi.");
        Assert.Equal(50_000, stacked.Jammed);
        Assert.Equal(50_000, machine.Position.Jammed);

        // Not in a drawer, not in escrow, not with the customer - and still counted.
        Assert.Equal(drawerBefore, cassettes.TotalValue);
        Assert.Equal(0, machine.Position.InEscrow);
        Assert.Equal(before.Total, machine.Position.Total);
    }

    [Fact]
    public void APartialStackPutsSomeAwayAndJamsTheRest()
    {
        var (machine, cassettes) = NewMachine();
        var before = machine.Position;
        var drawerBefore = cassettes.TotalValue;

        machine.AcceptIntoEscrow(Hundreds(5));
        machine.AcceptorFault = AcceptorFault.StackAtMostThis(30_000);
        var stacked = machine.Stack();

        Assert.True(stacked.Partial, "Kısmi sıkışma bekleniyordu.");
        Assert.Equal(30_000, stacked.Stacked);
        Assert.Equal(20_000, stacked.Jammed);
        Assert.Equal(drawerBefore + 30_000, cassettes.TotalValue);
        Assert.Equal(before.Total, machine.Position.Total);
    }

    [Fact]
    public void ASecondDepositCannotBeCountedWhileTheFirstIsStillInEscrow()
    {
        // One mouth, one escrow. Letting a second deposit in would mix two customers'
        // banknotes into one bundle that could then only be credited to one of them.
        var (machine, _) = NewMachine();
        machine.AcceptIntoEscrow(Hundreds(5));

        Assert.Throws<InvalidOperationException>(() => machine.AcceptIntoEscrow(Hundreds(1)));
    }

    [Fact]
    public void ADepositAndAWithdrawalInTheSameMachineStillAddUp()
    {
        var (machine, cassettes) = NewMachine();
        var before = machine.Position;

        machine.AcceptIntoEscrow(Hundreds(5));
        machine.Stack();

        var plan = DenominationPlanner.Plan(cassettes, 35_000);
        machine.Present(plan);
        machine.CustomerTakes();

        Assert.Equal(50_000, machine.Position.TakenFromCustomers);
        Assert.Equal(35_000, machine.Position.WithCustomers);
        Assert.Equal(before.Total, machine.Position.Total);
    }
}
