// BusinessDay.cs
//
// What this file does: it holds the business date this machine is currently working in,
// and it is the only thing in the terminal allowed to say what that date is.
//
// Why this exists: the date used to be read off the calendar - clock.UtcNow formatted as
// a date - in three separate places. That looks harmless and is not. A date read off the
// calendar changes by itself at midnight UTC: no total is compared, nobody says "closed",
// the machine simply starts writing a different day into its transaction keys. Instruction
// section 4.9 is about exactly this: the end of a business day is a MOMENT, not a date,
// and the two transactions that fall either side of it land in different days for no
// reason anybody recorded.
//
// So the date became a value that is HELD. Nothing moves it except a cutover the host has
// agreed to (KARAR-044). If the host never answers, the date stays where it was - which is
// the same rule the rest of this machine follows: silence is never a yes.
//
// Why one object shared by the three flows rather than a field in each: a machine that
// believes two different dates at once writes two different days into its own journal, and
// nothing about that produces an error message. One object cannot disagree with itself.
//
// What this file does NOT do: decide WHEN to close. That is CutoverFlow's job, and it only
// gets to call RollTo after the host has said yes.

using Atm.Protocol;

namespace Atm.Terminal;

/// <summary>The business date this terminal is working in. Changed only by a cutover.</summary>
public sealed class BusinessDay
{
    /// <summary>The one format that goes on the wire and into every transaction key.</summary>
    public const string Format = "yyyy-MM-dd";

    /// <summary>
    /// Starts on the day the clock is showing. This is the only moment the calendar is
    /// consulted at all - at start-up a machine has to begin somewhere.
    /// </summary>
    public BusinessDay(IClock clock) : this(clock.UtcNow.ToString(Format))
    {
    }

    /// <summary>Starts on a given day. Used by tests and by a machine resuming a day.</summary>
    public BusinessDay(string date)
    {
        Current = Parse(date).ToString(Format);
    }

    /// <summary>The day every new transaction is written into.</summary>
    public string Current { get; private set; }

    /// <summary>The day after the current one - what a cutover would move to.</summary>
    public string Next => Parse(Current).AddDays(1).ToString(Format);

    /// <summary>
    /// Moves to a new day. Refuses to move backwards or to stand still.
    /// </summary>
    /// <remarks>
    /// The refusal is loud on purpose (rule 6b (docs/proje-kurallari.md)). Rolling to a day that has
    /// already been closed would let the machine write new money into a settled day, and
    /// the difference would surface a day later as a total nobody can explain. A caller
    /// that gets this wrong should find out immediately, not in tomorrow's reconciliation.
    /// </remarks>
    public void RollTo(string date)
    {
        var next = Parse(date);

        if (next <= Parse(Current))
        {
            throw new InvalidOperationException(
                $"İş günü geriye alınamaz: {Current} -> {date}. Gün yalnızca ileri gider.");
        }

        Current = next.ToString(Format);
    }

    private static DateOnly Parse(string date) =>
        DateOnly.TryParseExact(date, Format, out var parsed)
            ? parsed
            : throw new ArgumentException($"İş günü '{Format}' biçiminde olmalı: '{date}'", nameof(date));
}
