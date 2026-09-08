// Copyright (c) 2026 Mirac Kutay Sereflisan. MIT lisansi - LICENSE dosyasina bakiniz.
// DenominationPlan.cs
//
// What this file does: it carries the answer to "can this amount be handed over, and
// with which notes" - either a list of bundles that add up to the amount exactly, or
// a refusal with a reason.
//
// Why the refusal carries a reason rather than being an empty plan: an empty list and
// a refusal look the same to a caller that does not check, and the caller that does not
// check is the one that authorises a withdrawal it cannot pay out. A plan that has to be
// asked "did this work" before it can be read is a plan that cannot be misread.
//
// Why the amount is repeated inside the plan: a plan travels into the authorisation
// request (the protocol spec SS4.3) and the host re-adds the bundles and compares them to
// the amount. Two sides reading the same number differently is the most expensive kind
// of mistake, so the number and its breakdown stay together.

namespace Atm.Terminal;

/// <summary>Why an amount cannot be handed over.</summary>
public enum PlanRefusal
{
    /// <summary>Not a refusal; the plan can be dispensed.</summary>
    None,

    /// <summary>Zero or a negative amount was asked for.</summary>
    AmountNotPositive,

    /// <summary>
    /// No combination of the notes actually loaded adds up to the amount. This covers
    /// both "these denominations cannot make that number" and "the drawer ran out".
    /// </summary>
    NoCombination,
}

/// <summary>So many notes of one denomination.</summary>
/// <param name="Denomination">Value of one note, in kurus.</param>
/// <param name="Count">How many of them.</param>
public sealed record NoteBundle(long Denomination, int Count)
{
    /// <summary>What this bundle is worth, in kurus.</summary>
    public long Value => Denomination * Count;
}

/// <summary>What the machine would hand over for an amount, or why it would not.</summary>
public sealed record DenominationPlan
{
    private DenominationPlan(long amount, IReadOnlyList<NoteBundle> bundles, PlanRefusal refusal)
    {
        Amount = amount;
        Bundles = bundles;
        Refusal = refusal;
    }

    /// <summary>The amount that was asked for, in kurus.</summary>
    public long Amount { get; }

    /// <summary>The notes to hand over, largest denomination first. Empty when refused.</summary>
    public IReadOnlyList<NoteBundle> Bundles { get; }

    /// <summary>Why this cannot be dispensed, or None.</summary>
    public PlanRefusal Refusal { get; }

    /// <summary>True when the machine can hand this amount over exactly.</summary>
    public bool CanDispense => Refusal == PlanRefusal.None;

    /// <summary>How many pieces of paper leave the machine.</summary>
    public int NoteCount => Bundles.Sum(b => b.Count);

    /// <summary>What the bundles add up to, in kurus. Equals Amount for a dispensable plan.</summary>
    public long BundledValue => Bundles.Sum(b => b.Value);

    internal static DenominationPlan Dispensable(long amount, IReadOnlyList<NoteBundle> bundles)
    {
        if (bundles.Sum(b => b.Value) != amount)
            throw new InvalidOperationException(
                $"A plan for {amount} kurus was built out of bundles worth {bundles.Sum(b => b.Value)}.");

        return new DenominationPlan(amount, bundles, PlanRefusal.None);
    }

    internal static DenominationPlan Refused(long amount, PlanRefusal refusal)
    {
        if (refusal == PlanRefusal.None)
            throw new ArgumentException("A refusal needs a reason.", nameof(refusal));

        return new DenominationPlan(amount, [], refusal);
    }
}
