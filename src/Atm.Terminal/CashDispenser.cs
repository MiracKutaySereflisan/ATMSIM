// Copyright (c) 2026 Mirac Kutay Sereflisan. MIT lisansi - LICENSE dosyasina bakiniz.
// CashDispenser.cs
//
// What this file does: it is the machine's hands. It takes notes out of the cassettes,
// puts them where the customer can reach them, and then finds out whether the customer
// actually took them.
//
// Why it is an interface with a fake behind it (rule 6a): there is no
// cash drawer in this project and there never will be. Everything this project measures
// happens in the moments when the hands do not do what they were asked - fewer notes
// come out than were planned, nothing comes out at all, or the money comes out and
// nobody picks it up. A real device cannot be told to fail on command at a chosen
// millisecond. A fake one can, and it is the only way those moments become repeatable.
//
// THE DISTINCTION THIS FILE EXISTS FOR: handing over is not the same as being taken.
// The two are separate calls here, not one, because they are separate events in the
// world and the money is in a different place after each of them (instruction section
// 4.4). Code that treats "dispensed" and "the customer has it" as one word cannot even
// describe the case where the machine pulls the money back in.
//
// What this file does NOT decide: which word (FULL, PARTIAL, NONE, RETRACTED) goes into
// the message to the host. That is a report about what happened, and it is assembled by
// the flow that watched it happen. A device that names its own outcome is a device whose
// report cannot be checked against its own movements.

namespace Atm.Terminal;

/// <summary>The machine's hands, seen from the flow's side.</summary>
public interface ICashDispenser
{
    /// <summary>
    /// Takes notes out of the cassettes and puts them where the customer can reach them.
    /// Returns what actually came out, which may be less than the plan asked for.
    /// </summary>
    long Present(DenominationPlan plan);

    /// <summary>
    /// The customer took what was waiting. Returns how much left the machine for good.
    /// </summary>
    long CustomerTakes();

    /// <summary>
    /// Nobody took it. The machine pulls the notes back in. Returns how much went into
    /// the retract bin - which is neither the customer's money nor the cassettes' again.
    /// </summary>
    long Retract();

    /// <summary>Where every note in this machine is right now.</summary>
    CashPosition Position { get; }
}

/// <summary>
/// How much money is sitting in each of the machine's buckets, in kurus.
/// </summary>
/// <remarks>
/// The bucket names are the ones the domain model section 1 fixes, and the list is closed:
/// a note that is not in one of these is a note this project cannot account for. The
/// money-conservation checker of Phase 2f reads exactly these numbers.
/// </remarks>
/// <param name="InCassettes">Still in the drawers.</param>
/// <param name="AtTheMouth">Handed out, not yet taken - the Transit bucket.</param>
/// <param name="WithCustomers">Gone: taken and never coming back.</param>
/// <param name="Retracted">Pulled back in after nobody took it. Counted by hand at day end.</param>
/// <param name="InEscrow">
/// Deposited and counted, not yet confirmed. Inside the machine and still the CUSTOMER'S -
/// if they change their mind it comes back out (KARAR-039).
/// </param>
/// <param name="Jammed">
/// Stuck in the mechanism while being stacked: neither in escrow nor in a drawer, and not
/// countable without opening the machine. This bucket exists because the alternative is
/// worse - without it a jam would be reported as money vanishing, or worse, quietly
/// counted into a drawer that does not hold it (KARAR-039).
/// </param>
/// <param name="TakenFromCustomers">
/// Everything that has come IN through the deposit mouth since the day started, in kurus.
/// </param>
/// <remarks>
/// Why an incoming counter had to exist at all: until deposits, money only ever MOVED
/// between buckets, so their sum could not change. A deposit breaks that - a banknote
/// appears inside the machine that was not there when the day started, and it did not come
/// out of a drawer. Without this counter the arrival would look exactly like money being
/// created, which is the one thing the whole project is built to notice.
/// </remarks>
public sealed record CashPosition(
    long InCassettes, long AtTheMouth, long WithCustomers, long Retracted,
    long InEscrow = 0, long Jammed = 0, long TakenFromCustomers = 0)
{
    /// <summary>What is physically inside the machine right now, in kurus.</summary>
    public long Inside => InCassettes + AtTheMouth + Retracted + InEscrow + Jammed;

    /// <summary>
    /// Everything the machine can account for. Should never change by itself: money moves
    /// between buckets, it does not appear or vanish.
    /// </summary>
    /// <remarks>
    /// Reading the formula: what is inside, plus what has gone out to customers, minus what
    /// has come in from them. The last term is what keeps the number still when somebody
    /// pushes a banknote into the mouth - the note is now inside, and it was counted as
    /// arriving, and the two cancel. Handing that same note back cancels the other way.
    /// </remarks>
    public long Total => Inside + WithCustomers - TakenFromCustomers;
}

/// <summary>
/// What the hands should do this time. The default does what it was asked; the others
/// are the ways a real machine fails, made reproducible.
/// </summary>
/// <remarks>
/// This is not a test helper living in production code by accident. Fault injection is
/// what this project is FOR (rule 3), and Phase 4 drives these same
/// settings from scenarios/*.json rather than from C#.
/// </remarks>
public sealed record DispenserFault
{
    private DispenserFault(long presentAtMost) => PresentAtMost = presentAtMost;

    /// <summary>The most this dispenser will hand over, in kurus.</summary>
    public long PresentAtMost { get; }

    /// <summary>Hand over everything that was planned.</summary>
    public static DispenserFault None { get; } = new(long.MaxValue);

    /// <summary>
    /// Hand over no more than this much, and keep the rest in the cassettes. This is what
    /// a note that fails to pick up looks like from outside.
    /// </summary>
    public static DispenserFault PresentAtMostThis(long kurus)
    {
        if (kurus < 0)
            throw new ArgumentOutOfRangeException(nameof(kurus),
                "A dispenser cannot hand over a negative amount.");

        return new DispenserFault(kurus);
    }

    /// <summary>Jam before a single note leaves the cassettes.</summary>
    public static DispenserFault Jam() => new(0);
}

/// <summary>
/// A dispenser with no hardware behind it: it moves numbers between buckets exactly the
/// way notes move between places.
/// </summary>
public sealed class FakeCashDispenser : ICashDispenser, ICashAcceptor
{
    private readonly CassetteSet _cassettes;
    private readonly List<NoteBundle> _escrow = [];
    private long _atTheMouth;
    private long _withCustomers;
    private long _retracted;
    private long _jammed;
    private long _takenFromCustomers;

    public FakeCashDispenser(CassetteSet cassettes) => _cassettes = cassettes;

    /// <summary>What the hands will do on the next Present. Reset it after using it.</summary>
    public DispenserFault Fault { get; set; } = DispenserFault.None;

    /// <summary>What the hands will do on the next Stack. Reset it after using it.</summary>
    public AcceptorFault AcceptorFault { get; set; } = AcceptorFault.None;

    public CashPosition Position => new(
        _cassettes.TotalValue, _atTheMouth, _withCustomers, _retracted,
        InEscrow: _escrow.Sum(b => b.Value), Jammed: _jammed,
        TakenFromCustomers: _takenFromCustomers);

    public long Present(DenominationPlan plan)
    {
        if (!plan.CanDispense)
            throw new InvalidOperationException(
                $"A plan that was refused ({plan.Refusal}) cannot be handed over.");

        if (_atTheMouth > 0)
            throw new InvalidOperationException(
                $"{_atTheMouth} kurus is still waiting at the mouth; it has to be taken " +
                "or retracted before more can be handed over.");

        var handedOver = 0L;

        // Bundle by bundle, largest note first - the order the planner already chose.
        // Stopping part way through is what a partial dispense IS: the notes that were
        // not reached never left their drawer, and the drawer must still know that.
        foreach (var bundle in plan.Bundles)
        {
            var cassette = _cassettes.ByDenomination(bundle.Denomination)
                ?? throw new InvalidOperationException(
                    $"The plan asks for {bundle.Denomination} kurus notes, which this " +
                    "machine does not carry.");

            for (var note = 0; note < bundle.Count; note++)
            {
                if (handedOver + bundle.Denomination > Fault.PresentAtMost)
                {
                    // The fault stops the count here. Every note counted so far has
                    // already left its cassette and is at the mouth.
                    _atTheMouth += handedOver;
                    return handedOver;
                }

                cassette.Take(1);
                handedOver += bundle.Denomination;
            }
        }

        _atTheMouth += handedOver;
        return handedOver;
    }

    public long CustomerTakes()
    {
        var taken = _atTheMouth;
        _atTheMouth = 0;
        _withCustomers += taken;
        return taken;
    }

    public long Retract()
    {
        var pulledBack = _atTheMouth;
        _atTheMouth = 0;

        // Into its own bucket, NOT back into the cassettes. The machine took these notes
        // in an order it did not record and cannot hand them out again; counting them
        // back into a drawer would show more money in that drawer than it holds, and the
        // difference would surface at day end as an unexplained surplus (the domain model).
        _retracted += pulledBack;
        return pulledBack;
    }

    // ---------- taking notes in ----------

    public DepositCount AcceptIntoEscrow(IReadOnlyList<NoteBundle> notes)
    {
        if (_escrow.Count > 0)
            throw new InvalidOperationException(
                "The escrow already holds a deposit; it has to be stacked or returned " +
                "before another one can be counted.");

        var takeable = _cassettes.AcceptedForDeposit.ToHashSet();
        var accepted = new List<NoteBundle>();
        var refused = 0L;

        foreach (var bundle in notes)
        {
            if (bundle.Count <= 0)
            {
                continue;
            }

            // A note this machine has no drawer for is pushed straight back out. Counting
            // it into the escrow would mean promising a customer a deposit that could not
            // be completed - and discovering that AFTER they confirmed.
            if (takeable.Contains(bundle.Denomination))
            {
                accepted.Add(bundle);
            }
            else
            {
                refused += bundle.Value;
            }
        }

        _escrow.AddRange(accepted);

        // The refused notes never entered the machine, so they are not counted as having
        // arrived: they were pushed back out of the same mouth they came in at.
        var counted = accepted.Sum(b => b.Value);
        _takenFromCustomers += counted;

        return new DepositCount(accepted, counted, refused);
    }

    public StackResult Stack()
    {
        var stacked = 0L;
        var jammed = 0L;

        // Bundle by bundle. Stopping part way through is what a partial stack IS: the notes
        // that were reached are in a drawer and the rest are in the mechanism.
        foreach (var bundle in _escrow)
        {
            var cassette = _cassettes.ByDenomination(bundle.Denomination)
                ?? throw new InvalidOperationException(
                    $"The escrow holds {bundle.Denomination} kurus notes, which this " +
                    "machine has no drawer for.");

            for (var note = 0; note < bundle.Count; note++)
            {
                if (stacked + bundle.Denomination > AcceptorFault.StackAtMost)
                {
                    jammed += bundle.Denomination;
                    continue;
                }

                cassette.Put(1);
                stacked += bundle.Denomination;
            }
        }

        _escrow.Clear();
        _jammed += jammed;

        return new StackResult(stacked, jammed);
    }

    public long ReturnFromEscrow()
    {
        var back = _escrow.Sum(b => b.Value);
        _escrow.Clear();

        // Straight out to the customer: it was theirs the whole time it sat in escrow, and
        // handing it back is not a payment - no ledger anywhere moves.
        _withCustomers += back;
        return back;
    }
}
