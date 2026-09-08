// Copyright (c) 2026 Mirac Kutay Sereflisan. MIT lisansi - LICENSE dosyasina bakiniz.
// ClockTests.cs
//
// What this file does: it checks that the virtual clock behaves like a clock we own -
// it stands still until we move it, it moves by exactly what we ask, and it refuses
// to run backwards.
//
// Why bother testing something this small: every timeout rule in the protocol is
// measured on this clock. If it drifted by a second, every "the answer arrived just
// in time" test in the project would be measuring nothing in particular.

using Atm.Protocol;
using Xunit;

namespace Atm.Tests;

public class ClockTests
{
    [Fact]
    public void TheVirtualClockDoesNotMoveOnItsOwn()
    {
        var clock = VirtualClock.StartOfBusinessDay();
        var first = clock.UtcNow;

        // Do some work that takes real time. A real clock would have moved.
        for (var i = 0; i < 100_000; i++)
        {
            _ = i * 2;
        }

        Assert.Equal(first, clock.UtcNow);
    }

    [Fact]
    public void SleepingMovesTheClockByExactlyTheAmountAsked()
    {
        var clock = VirtualClock.StartOfBusinessDay();
        var before = clock.UtcNow;

        clock.Sleep(TimeSpan.FromSeconds(30));

        Assert.Equal(before.AddSeconds(30), clock.UtcNow);
    }

    [Fact]
    public void SeveralWaitsAddUp()
    {
        var clock = VirtualClock.StartOfBusinessDay();
        var before = clock.UtcNow;

        clock.Advance(TimeSpan.FromSeconds(15));
        clock.Advance(TimeSpan.FromSeconds(15));

        Assert.Equal(before.AddSeconds(30), clock.UtcNow);
    }

    [Fact]
    public void TheClockRefusesToRunBackwards()
    {
        var clock = VirtualClock.StartOfBusinessDay();

        // If this were allowed, a scenario could place a reversal before the
        // withdrawal it reverses and the ledger would still "balance".
        Assert.Throws<ArgumentOutOfRangeException>(
            () => clock.Sleep(TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void TheVirtualClockIsAlwaysUtc()
    {
        var clock = VirtualClock.StartOfBusinessDay();

        // Business date comes off this clock. An hour of offset would push a
        // late-evening transaction into the wrong business day - instruction 4.9.
        Assert.Equal(TimeSpan.Zero, clock.UtcNow.Offset);
    }
}
