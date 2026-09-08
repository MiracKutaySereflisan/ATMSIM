// DenominationPlannerTests.cs
//
// These tests are the reason KARAR-011 chose an exact solution over the greedy one. The
// first test is the whole argument in six lines: a machine loaded with 50s and 20s can
// hand over 60 TL, and the algorithm most people would write says it cannot.
//
// Amounts here are written in kurus (KARAR-008): 60 TL is 6_000.

using Atm.Terminal;
using Xunit;

namespace Atm.Tests;

public class DenominationPlannerTests
{
    private static CassetteSet Loaded(params (string Id, long Denomination, int Notes)[] drawers)
        => new(drawers.Select(d =>
            new Cassette(d.Id, d.Denomination, CassetteKind.DispenseOnly, d.Notes)));

    // Written out as text rather than compared as arrays: the test kit's Equal compares
    // objects, and two arrays holding the same numbers are two different objects. Text
    // also means a failure prints "1x20000 + 1x10000" instead of a type name.
    private static string Notes(DenominationPlan plan)
        => string.Join(" + ", plan.Bundles.Select(b => $"{b.Count}x{b.Denomination}"));

    [Fact]
    public void SixtyLiraComesOutOfThreeTwentiesWhenTheGreedyAnswerWouldHaveGivenUp()
    {
        // Greedy lays down one 50, is left with 10, finds no 10 note and refuses.
        var plan = DenominationPlanner.Plan(Loaded(("K3", 5_000, 10), ("K4", 2_000, 10)), 6_000);

        Assert.True(plan.CanDispense, "60 TL is three 20s; the planner refused it.");
        Assert.Equal("3x2000", Notes(plan));
        Assert.Equal(3, plan.NoteCount);
    }

    [Fact]
    public void TheFewestNotesWins()
    {
        // 350 TL out of the standard load: 200 + 100 + 50 is three notes; seven 50s is seven.
        var plan = DenominationPlanner.Plan(CassetteSet.Standard(), 35_000);

        Assert.True(plan.CanDispense, "350 TL out of the standard load was refused.");
        Assert.Equal("1x20000 + 1x10000 + 1x5000", Notes(plan));
    }

    [Fact]
    public void AnAmountThatIsNotAMultipleOfTheSmallestNoteCanStillBeDispensed()
    {
        // 70 TL is not a multiple of 20, and the machine hands it over as 50 + 20.
        // This is why "the amount must be a multiple of the smallest note" is a wrong
        // shortcut: the planner is the only thing that knows.
        var plan = DenominationPlanner.Plan(CassetteSet.Standard(), 7_000);

        Assert.True(plan.CanDispense, "70 TL is 50 + 20; the planner refused it.");
        Assert.Equal("1x5000 + 1x2000", Notes(plan));
    }

    [Fact]
    public void AnAmountNoCombinationReachesIsRefused()
    {
        // 30 TL: there is no 10 note, and 20 + 20 overshoots.
        var plan = DenominationPlanner.Plan(CassetteSet.Standard(), 3_000);

        Assert.False(plan.CanDispense, "30 TL cannot be made from 200, 100, 50 and 20.");
        Assert.Equal(PlanRefusal.NoCombination, plan.Refusal);
        Assert.Equal(0, plan.Bundles.Count);
    }

    [Fact]
    public void AnAmountBetweenTwoStepsIsRefused()
    {
        // 55 TL. Every loaded note is a whole number of 10 TL, so 55 is unreachable.
        var plan = DenominationPlanner.Plan(CassetteSet.Standard(), 5_500);

        Assert.False(plan.CanDispense, "55 TL is not a whole number of 10 TL notes.");
        Assert.Equal(PlanRefusal.NoCombination, plan.Refusal);
    }

    [Fact]
    public void ADrawerThatHasRunEmptyIsNotUsed()
    {
        var set = Loaded(("K1", 20_000, 0), ("K2", 10_000, 5));
        var plan = DenominationPlanner.Plan(set, 20_000);

        Assert.True(plan.CanDispense, "200 TL is two 100s once the 200 drawer is empty.");
        Assert.Equal("2x10000", Notes(plan));
    }

    [Fact]
    public void MoreThanTheMachineHoldsIsRefused()
    {
        var set = Loaded(("K4", 2_000, 3));
        var plan = DenominationPlanner.Plan(set, 8_000);

        Assert.False(plan.CanDispense, "The machine holds 60 TL and was asked for 80.");
        Assert.Equal(PlanRefusal.NoCombination, plan.Refusal);
    }

    [Fact]
    public void ADrawerWithTooFewNotesForcesTheLongerAnswer()
    {
        // 200 TL with only one 200 note left would be one note - but there are none,
        // so the answer has to be built out of the 100s.
        var set = Loaded(("K1", 20_000, 0), ("K2", 10_000, 2), ("K4", 2_000, 100));
        var plan = DenominationPlanner.Plan(set, 20_000);

        Assert.Equal("2x10000", Notes(plan));
    }

    [Fact]
    public void ZeroAndNegativeAmountsAreRefusedWithTheirOwnReason()
    {
        var set = CassetteSet.Standard();

        Assert.Equal(PlanRefusal.AmountNotPositive, DenominationPlanner.Plan(set, 0).Refusal);
        Assert.Equal(PlanRefusal.AmountNotPositive, DenominationPlanner.Plan(set, -2_000).Refusal);
    }

    [Fact]
    public void WhenTwoAnswersAreTheSameLengthTheNotesComeFromTheFullerDrawer()
    {
        // A machine loaded with 30, 20 and 10 notes. 40 can be 20 + 20 or 30 + 10;
        // both are two notes. The 20 drawer is the full one, so it is the one spent from,
        // and the near-empty drawers are left with something in them.
        var set = Loaded(("A", 3_000, 5), ("B", 2_000, 400), ("C", 1_000, 5));
        var plan = DenominationPlanner.Plan(set, 4_000);

        Assert.Equal(2, plan.NoteCount);
        Assert.Equal("2x2000", Notes(plan));
    }

    [Fact]
    public void ThePlanAlwaysAddsUpToExactlyWhatWasAskedFor()
    {
        var set = CassetteSet.Standard();

        for (var amount = 1_000L; amount <= 500_000L; amount += 1_000L)
        {
            var plan = DenominationPlanner.Plan(set, amount);
            if (plan.CanDispense) Assert.Equal(amount, plan.BundledValue);
        }
    }

    [Fact]
    public void TheSameQuestionAskedTwiceGivesTheSameAnswer()
    {
        var first = DenominationPlanner.Plan(CassetteSet.Standard(), 33_000);
        var second = DenominationPlanner.Plan(CassetteSet.Standard(), 33_000);

        Assert.Equal(Notes(first), Notes(second));
    }

    [Fact]
    public void AnAbsurdAmountIsRefusedCleanlyRatherThanThrowing()
    {
        // Ten million lira. What this pins down is that a wildly large request comes back
        // as a refusal with a reason, not as an exception out of the table-building code.
        //
        // What it does NOT pin down, and what was measured rather than assumed: the
        // "more than the machine holds" check in front of the table is a speed guard, not
        // a correctness one. With the check removed this test still passes; the suite goes
        // from 0.5 s to 6.9 s because the table is built for ten million lira and then
        // answers no anyway. Nothing here can tell the two versions apart.
        var plan = DenominationPlanner.Plan(CassetteSet.Standard(), 1_000_000_000L);

        Assert.False(plan.CanDispense, "No machine holds ten million lira.");
        Assert.Equal(PlanRefusal.NoCombination, plan.Refusal);
    }

    [Fact]
    public void PlanningDoesNotTakeAnyNotesOutOfTheCassettes()
    {
        // The plan is made before the host is asked. Nothing has moved yet.
        var set = CassetteSet.Standard();
        var before = set.TotalNotes;

        DenominationPlanner.Plan(set, 35_000);

        Assert.Equal(before, set.TotalNotes);
    }
}
