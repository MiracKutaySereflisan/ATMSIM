// Copyright (c) 2026 Mirac Kutay Sereflisan. MIT lisansi - LICENSE dosyasina bakiniz.
// CassetteTests.cs
//
// These tests are about the drawer itself, not about choosing notes. They pin down the
// three things a cassette must never do: hold a negative number of notes, hand out more
// than it has, or let two drawers claim the same denomination or the same name.

using Atm.Protocol;
using Atm.Terminal;
using Xunit;

namespace Atm.Tests;

public class CassetteTests
{
    [Fact]
    public void ACassetteIsWorthItsDenominationTimesItsNotes()
    {
        var cassette = new Cassette("K1", 20_000, CassetteKind.DispenseOnly, 500);
        Assert.Equal(10_000_000L, cassette.Value);
    }

    [Fact]
    public void TakingNotesLowersTheCount()
    {
        var cassette = new Cassette("K1", 20_000, CassetteKind.DispenseOnly, 500);
        cassette.Take(3);
        Assert.Equal(497, cassette.NoteCount);
        Assert.Equal(9_940_000L, cassette.Value);
    }

    [Fact]
    public void TakingMoreNotesThanTheDrawerHoldsThrowsInsteadOfClampingToZero()
    {
        var cassette = new Cassette("K4", 2_000, CassetteKind.DispenseOnly, 2);
        Assert.Throws<InvalidOperationException>(() => cassette.Take(3));
        Assert.Equal(2, cassette.NoteCount);
    }

    [Fact]
    public void TakingANegativeNumberOfNotesThrows()
    {
        var cassette = new Cassette("K4", 2_000, CassetteKind.DispenseOnly, 2);
        Assert.Throws<ArgumentOutOfRangeException>(() => cassette.Take(-1));
    }

    [Fact]
    public void ACassetteCannotBeLoadedWithANegativeNumberOfNotes()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Cassette("K1", 20_000, CassetteKind.DispenseOnly, -1));
    }

    [Fact]
    public void ACassetteCannotCarryANoteWorthNothing()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Cassette("K1", 0, CassetteKind.DispenseOnly, 10));
    }

    [Fact]
    public void TwoCassettesCannotCarryTheSameDenomination()
    {
        Assert.Throws<ArgumentException>(() => new CassetteSet(
        [
            new Cassette("K1", 5_000, CassetteKind.DispenseOnly, 10),
            new Cassette("K2", 5_000, CassetteKind.DispenseOnly, 10),
        ]));
    }

    [Fact]
    public void TwoCassettesCannotShareAName()
    {
        Assert.Throws<ArgumentException>(() => new CassetteSet(
        [
            new Cassette("K1", 5_000, CassetteKind.DispenseOnly, 10),
            new Cassette("K1", 2_000, CassetteKind.DispenseOnly, 10),
        ]));
    }

    [Fact]
    public void AMachineWithNoCassettesCannotExist()
    {
        Assert.Throws<ArgumentException>(() => new CassetteSet([]));
    }

    [Fact]
    public void TheStandardLoadMatchesTheOneWrittenInTheModel()
    {
        // the domain model SS2: K1 200 TL x500 recycler, K2 100 TL x1000 recycler,
        // K3 50 TL x1000 recycler, K4 20 TL x500 dispense-only.
        var set = CassetteSet.Standard();

        Assert.Equal(4, set.Cassettes.Count);
        Assert.Equal(3000, set.TotalNotes);
        Assert.Equal(26_000_000L, set.TotalValue);

        Assert.Equal("K1", set.Cassettes[0].Id);
        Assert.Equal(20_000L, set.Cassettes[0].Denomination);
        Assert.Equal(CassetteKind.Recycler, set.Cassettes[0].Kind);
        Assert.Equal(500, set.Cassettes[0].NoteCount);

        Assert.Equal(CassetteKind.Recycler, set.ByDenomination(10_000)!.Kind);
        Assert.Equal(CassetteKind.Recycler, set.ByDenomination(5_000)!.Kind);
        Assert.Equal(CassetteKind.DispenseOnly, set.ByDenomination(2_000)!.Kind);
        Assert.True(set.ByDenomination(1_000) is null, "The machine has no 10 TL cassette.");
    }

    [Fact]
    public void TheCassettesComeBackLargestDenominationFirstWhateverOrderTheyWereGivenIn()
    {
        var set = new CassetteSet(
        [
            new Cassette("K4", 2_000, CassetteKind.DispenseOnly, 10),
            new Cassette("K1", 20_000, CassetteKind.DispenseOnly, 10),
            new Cassette("K3", 5_000, CassetteKind.Recycler, 10),
        ]);

        Assert.Equal("20000,5000,2000",
            string.Join(",", set.Cassettes.Select(c => c.Denomination)));
    }

    [Fact]
    public void EveryLoadedCassetteHoldsANoteTheProtocolAllows()
    {
        var set = CassetteSet.Standard();

        // Two lists say what a note is worth: Denominations.Valid, which the host checks
        // a breakdown against, and this loading, which is what the machine physically
        // holds. If they drifted apart, the machine would be carrying money the host
        // would never authorise - and nobody would notice until a customer was refused
        // for a reason nobody could explain.
        foreach (var cassette in set.Cassettes)
        {
            Assert.True(Denominations.IsValid(cassette.Denomination),
                $"cassette {cassette.Id} holds {cassette.Denomination} kurus, " +
                "which the protocol spec does not allow in a breakdown");
        }
    }
}
