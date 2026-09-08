// Copyright (c) 2026 Mirac Kutay Sereflisan. MIT lisansi - LICENSE dosyasina bakiniz.
// IClock.cs
//
// What this file does: it hides "what time is it" and "wait a while" behind an
// interface, so the transaction flow never reads the machine clock directly.
//
// Why this exists: this project is about the moments when things go wrong, and most
// of those moments are defined by time - "the answer did not arrive within 30
// seconds". A test that proved that by really waiting 30 seconds would be slow, and
// worse, it would be unstable: a loaded machine turns 30.0 into 30.4 and the test
// starts failing for a reason that has nothing to do with the code. Instruction
// section 5 is blunt about it - an unstable test is not a test.
//
// With the clock behind an interface, a test moves time forward by exactly 30
// seconds and the flow believes it. Same input, same result, every run.
//
// Why VirtualClock sits here in production code rather than in the test project:
// the scenario runner of Phase 4 will run the REAL transaction flow on the virtual
// clock - that is how "cut the line at second 12" becomes reproducible. It is not a
// test double; it is the clock the simulator runs on. See KARAR-016.

namespace Atm.Protocol;

/// <summary>The only way any code in this project is allowed to ask for the time.</summary>
public interface IClock
{
    /// <summary>Current instant, always UTC. Wall-clock time zones never enter the flow.</summary>
    DateTimeOffset UtcNow { get; }

    /// <summary>Waits for the given duration. Zero or negative durations return at once.</summary>
    void Sleep(TimeSpan duration);
}

/// <summary>The real clock. Used when the simulator runs for a human, in the demo.</summary>
public sealed class SystemClock : IClock
{
    /// <summary>There is only one machine clock, so there is only one of these.</summary>
    public static readonly SystemClock Instance = new();

    private SystemClock() { }

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public void Sleep(TimeSpan duration)
    {
        if (duration > TimeSpan.Zero)
        {
            Thread.Sleep(duration);
        }
    }
}

/// <summary>
/// A clock that only moves when it is told to. Time here is a value we control,
/// not something that happens to us.
/// </summary>
public sealed class VirtualClock : IClock
{
    private DateTimeOffset _now;

    public VirtualClock(DateTimeOffset start) => _now = start;

    /// <summary>A fixed, boring starting point so every scenario log reads the same.</summary>
    public static VirtualClock StartOfBusinessDay() =>
        new(new DateTimeOffset(2026, 1, 5, 6, 0, 0, TimeSpan.Zero));

    public DateTimeOffset UtcNow => _now;

    /// <summary>
    /// Moving forward is the only thing this clock does. A negative duration is
    /// refused loudly instead of quietly rewinding time: a test that could run the
    /// clock backwards could "prove" that a reversal happened before the withdrawal
    /// it reverses, and nobody would notice the proof was nonsense.
    /// </summary>
    public void Sleep(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(duration), duration, "Time does not run backwards.");
        }

        _now = _now.Add(duration);
    }

    /// <summary>Same thing as Sleep, named for what a test means when it uses it.</summary>
    public void Advance(TimeSpan duration) => Sleep(duration);
}
