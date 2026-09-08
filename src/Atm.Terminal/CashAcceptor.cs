// Copyright (c) 2026 Mirac Kutay Sereflisan. MIT lisansi - LICENSE dosyasina bakiniz.
// CashAcceptor.cs
//
// What this file does: it is the machine's mouth working the other way round - the part
// that takes banknotes IN, holds them where the customer can still get them back, and
// then either puts them away for good or hands them back.
//
// Why it is separate from the dispenser interface even though one machine does both: they
// are two different sets of physical events with two different failure modes. A dispenser
// fails by handing over less than it was asked for; an acceptor fails by jamming with the
// notes half way in. Behind them, though, is ONE machine with one set of drawers, which is
// why the fake implements both and there is only ever one CashPosition.
//
// The three calls are three separate moments on purpose, and the order between them is the
// decision the whole of Phase 3 rests on (KARAR-038):
//
//   AcceptIntoEscrow - the notes are in the machine and still the CUSTOMER'S.
//   Stack            - the notes go into the recycler drawers. From here they are the
//                      BANK'S, and they cannot come back out to this customer.
//   ReturnFromEscrow - the customer changed their mind, or the host said no.
//
// Stack happens BEFORE the host is told. A note in a drawer cannot be un-stacked, but a
// message can be sent again for ever - so the uncertainty is put on the side that can be
// repeated. See the protocol spec section 4.6.
//
// What this file does NOT do: decide whether a deposit is allowed. It counts and moves
// paper. Whether the account may have it is the host's answer.

namespace Atm.Terminal;

/// <summary>What the machine made of the notes the customer pushed in.</summary>
/// <param name="Accepted">The notes it took, by denomination. These are in escrow.</param>
/// <param name="Counted">What those notes are worth, in kurus.</param>
/// <param name="Refused">
/// What it pushed straight back out, in kurus: notes of a denomination this machine has no
/// drawer for. Refused at the mouth rather than counted into a deposit that could not be
/// completed later.
/// </param>
public sealed record DepositCount(
    IReadOnlyList<NoteBundle> Accepted, long Counted, long Refused);

/// <summary>What happened when the escrow was emptied into the drawers.</summary>
/// <param name="Stacked">Reached a drawer, in kurus. Only this much may be credited.</param>
/// <param name="Jammed">Stuck in the mechanism, in kurus. Nobody's, until an engineer comes.</param>
public sealed record StackResult(long Stacked, long Jammed)
{
    /// <summary>True when nothing reached a drawer.</summary>
    public bool NothingStacked => Stacked == 0;

    /// <summary>True when some of it reached a drawer and some of it did not.</summary>
    public bool Partial => Stacked > 0 && Jammed > 0;
}

/// <summary>The machine's mouth, taking notes in.</summary>
public interface ICashAcceptor
{
    /// <summary>
    /// Counts the notes the customer pushed in and holds them in escrow, pushing back the
    /// ones this machine has no drawer for.
    /// </summary>
    DepositCount AcceptIntoEscrow(IReadOnlyList<NoteBundle> notes);

    /// <summary>
    /// Empties the escrow into the recycler drawers. This is the point of no return
    /// (KARAR-038) - after it, the notes are the bank's and cannot be handed back.
    /// </summary>
    StackResult Stack();

    /// <summary>Gives the escrow back to the customer. Returns how much went back out.</summary>
    long ReturnFromEscrow();

    /// <summary>Where every note in this machine is right now.</summary>
    CashPosition Position { get; }
}

/// <summary>
/// What the acceptor's hands will do this time. The default does what it was asked; the
/// others are the ways a real acceptor fails, made reproducible.
/// </summary>
/// <remarks>
/// Like DispenserFault, this is not a test helper that leaked into production code: fault
/// injection is what this project is for, and Phase 4 drives these settings from
/// scenarios/*.json rather than from C#.
/// </remarks>
public sealed record AcceptorFault
{
    private AcceptorFault(long stackAtMost) => StackAtMost = stackAtMost;

    /// <summary>The most this acceptor will get into a drawer, in kurus.</summary>
    public long StackAtMost { get; }

    /// <summary>Put everything away.</summary>
    public static AcceptorFault None { get; } = new(long.MaxValue);

    /// <summary>
    /// Get no more than this much into the drawers; the rest jams in the mechanism. This
    /// is what a note that fails to feed looks like from outside.
    /// </summary>
    public static AcceptorFault StackAtMostThis(long kurus)
    {
        if (kurus < 0)
            throw new ArgumentOutOfRangeException(nameof(kurus),
                "An acceptor cannot stack a negative amount.");

        return new AcceptorFault(kurus);
    }

    /// <summary>Jam before a single note reaches a drawer.</summary>
    public static AcceptorFault Jam() => new(0);
}
