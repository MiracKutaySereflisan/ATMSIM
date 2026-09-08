// Copyright (c) 2026 Mirac Kutay Sereflisan. MIT lisansi - LICENSE dosyasina bakiniz.
// Cassette.cs
//
// What this file does: it holds one cassette - the drawer inside the machine that
// carries banknotes of a single value - and the two things that may be asked of it:
// how much it holds, and taking notes out of it.
//
// Why one denomination per cassette and not a mixed drawer: a real dispenser counts
// notes, not values. It moves a sheet of paper and increments a counter; it has no way
// to tell a 50 from a 200 on the way out. Mixing values in one drawer would mean the
// machine could not know what it had just handed over, which is the one thing it must
// never be unsure about.
//
// Why the count is guarded rather than a plain public field: a cassette that can go
// negative is a cassette that can invent money. the domain model's conservation equation
// counts notes across seven buckets, and a negative count in one of them makes the sum
// balance while the machine is wrong. So taking more than there is throws, loudly,
// rather than quietly clamping to zero (kural 6b).
//
// Amounts are whole kurus, never decimals - KARAR-008.

namespace Atm.Terminal;

/// <summary>What a cassette is allowed to do with notes (the domain model SS2).</summary>
public enum CassetteKind
{
    /// <summary>Notes leave this cassette and never come back to it.</summary>
    DispenseOnly,

    /// <summary>Deposited notes may be stored here and dispensed again later.</summary>
    Recycler,
}

/// <summary>One drawer of banknotes of a single denomination.</summary>
public sealed class Cassette
{
    public Cassette(string id, long denomination, CassetteKind kind, int noteCount)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("A cassette needs an id.", nameof(id));
        if (denomination <= 0)
            throw new ArgumentOutOfRangeException(nameof(denomination),
                $"Cassette {id}: a denomination must be a positive number of kurus.");
        if (noteCount < 0)
            throw new ArgumentOutOfRangeException(nameof(noteCount),
                $"Cassette {id}: a cassette cannot hold a negative number of notes.");

        Id = id;
        Denomination = denomination;
        Kind = kind;
        NoteCount = noteCount;
    }

    /// <summary>The name written on the drawer, e.g. K1. Used in journals and reports.</summary>
    public string Id { get; }

    /// <summary>The value of one note in this cassette, in kurus (KARAR-008).</summary>
    public long Denomination { get; }

    /// <summary>Whether deposited notes may be stored here (the domain model SS2).</summary>
    public CassetteKind Kind { get; }

    /// <summary>How many notes are in the drawer right now.</summary>
    public int NoteCount { get; private set; }

    /// <summary>What the drawer is worth right now, in kurus.</summary>
    public long Value => Denomination * NoteCount;

    /// <summary>
    /// Removes notes from the drawer. Throws when asked for more than there is, because
    /// a silently clamped count is money invented out of nothing.
    /// </summary>
    public void Take(int notes)
    {
        if (notes < 0)
            throw new ArgumentOutOfRangeException(nameof(notes),
                $"Cassette {Id}: cannot take a negative number of notes.");
        if (notes > NoteCount)
            throw new InvalidOperationException(
                $"Cassette {Id}: asked for {notes} notes but only {NoteCount} are loaded.");

        NoteCount -= notes;
    }

    /// <summary>
    /// Puts deposited notes into the drawer.
    /// </summary>
    /// <remarks>
    /// Refused on a dispense-only drawer, and that is not a formality: a machine that
    /// quietly dropped deposited notes into a cassette it can only pay out of would be
    /// handing a customer's banknote to the next customer with nothing in between. Which
    /// drawers may take notes back is a property of the hardware (the domain model section 2),
    /// so the type is where the rule belongs.
    /// </remarks>
    public void Put(int notes)
    {
        if (notes < 0)
            throw new ArgumentOutOfRangeException(nameof(notes),
                $"Cassette {Id}: cannot put a negative number of notes.");

        if (Kind != CassetteKind.Recycler)
            throw new InvalidOperationException(
                $"Cassette {Id}: notes cannot be put into a dispense-only drawer.");

        NoteCount += notes;
    }
}
