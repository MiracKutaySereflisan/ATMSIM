// Copyright (c) 2026 Mirac Kutay Sereflisan. MIT lisansi - LICENSE dosyasina bakiniz.
// ITerminalJournal.cs
//
// What this file does: it is the terminal's own record of what it did - one line per
// event, in order, never edited.
//
// Why this exists, when the host already keeps a record: because the two records are
// about different things, and the difference between them is the finding this project
// exists to produce. The host's record says what it was ASKED and what it ANSWERED. The
// terminal's record says what physically HAPPENED to the notes: they came out, they were
// taken, they were pulled back in. Neither side can see the other's half.
//
// Instruction section 4.10 puts it plainly: terminal state and host state break
// separately, and there is no single source of truth. That is why reconciliation exists
// at all. A machine that kept no record of its own would have nothing to reconcile
// against - the host's record would be the only account of what happened, and it would
// be right by definition, including when it is wrong.
//
// The concrete case: the terminal hands over 350 lira and its report to the host is lost.
// The host's record shows an authorisation and no debit. The terminal's record shows cash
// gone. Money is out of the machine and in nobody's ledger. Without this file, that
// situation is invisible - the host's books balance perfectly.
//
// The same two protections as the host's record, for the same reasons (instruction
// section 2): a card number can only enter MASKED, through a write-only property, and
// there is no PIN field at all - so it cannot be logged even by accident.
//
// What this file does NOT do: decide anything. It records. The auditing lives in
// Atm.Audit, which reads this record and the host's and compares them (KARAR-035).

using Atm.Protocol;

namespace Atm.Terminal;

/// <summary>
/// The events a terminal writes down. These are not message types: some of them are
/// things that happened to paper, which no message describes.
/// </summary>
/// <remarks>
/// The list separates events a real machine can tell apart. "Cash presented" and "cash
/// taken" are two lines because they are two events (rule 4.4); collapsing
/// them would make the machine unable to say that money came out and nobody took it -
/// which is precisely the state the retract bin exists for.
/// </remarks>
public static class TerminalEvent
{
    /// <summary>The amount could not be built from the notes in this machine. Nothing was sent.</summary>
    public const string AmountRefusedLocally = "AMOUNT_REFUSED_LOCALLY";

    /// <summary>An authorisation was asked for.</summary>
    public const string AuthRequested = "AUTH_REQUESTED";

    /// <summary>The host approved it. The money is promised but has not moved.</summary>
    public const string AuthApproved = "AUTH_APPROVED";

    /// <summary>The host said no, and said why. Nothing was promised.</summary>
    public const string AuthRefused = "AUTH_REFUSED";

    /// <summary>No answer came. This is not a refusal - it is "I do not know".</summary>
    public const string AuthNoAnswer = "AUTH_NO_ANSWER";

    /// <summary>Notes left the cassettes and are at the mouth, where a hand can reach them.</summary>
    public const string CashPresented = "CASH_PRESENTED";

    /// <summary>A hand took them. This is the only event that means money left the machine.</summary>
    public const string CashTaken = "CASH_TAKEN";

    /// <summary>Nobody took them; the machine pulled them into the retract bin.</summary>
    public const string CashRetracted = "CASH_RETRACTED";

    /// <summary>The report of what happened to the cash was sent to the host.</summary>
    public const string AdviceSent = "ADVICE_SENT";

    /// <summary>The host acknowledged the report. This transaction is now closed on both sides.</summary>
    public const string AdviceAcknowledged = "ADVICE_ACKNOWLEDGED";

    /// <summary>The report was not acknowledged and went into the queue. It will be sent again.</summary>
    public const string AdviceQueued = "ADVICE_QUEUED";

    /// <summary>A reversal was queued because the authorisation may have happened.</summary>
    public const string ReversalQueued = "REVERSAL_QUEUED";

    /// <summary>The host acknowledged the reversal. The promise, if there was one, is released.</summary>
    public const string ReversalAcknowledged = "REVERSAL_ACKNOWLEDGED";

    // ---------- taking money in ----------

    /// <summary>Notes were counted into the escrow. They are inside the machine, and the customer's.</summary>
    public const string DepositCounted = "DEPOSIT_COUNTED";

    /// <summary>Permission for a deposit was asked for. Nothing has moved on either side.</summary>
    public const string DepositAuthRequested = "DEPOSIT_AUTH_REQUESTED";

    /// <summary>The host agreed. The customer may now be asked to confirm.</summary>
    public const string DepositAuthApproved = "DEPOSIT_AUTH_APPROVED";

    /// <summary>The host said no. The notes go back out.</summary>
    public const string DepositAuthRefused = "DEPOSIT_AUTH_REFUSED";

    /// <summary>No answer came. The notes go back out - they are still the customer's.</summary>
    public const string DepositAuthNoAnswer = "DEPOSIT_AUTH_NO_ANSWER";

    /// <summary>
    /// The machine is ABOUT TO move the escrow into the drawers. Written BEFORE the
    /// physical move, not after (KARAR-041).
    /// </summary>
    /// <remarks>
    /// A line written before an irreversible action cannot make that action safe - a
    /// power cut between the two is always possible. What it does is make the gap
    /// VISIBLE: a record with this line and no ending is a deposit whose fate is unknown,
    /// and a known uncertainty can be reported. An unknown one is discovered at day end as
    /// a difference nobody can explain.
    /// </remarks>
    public const string DepositStacking = "DEPOSIT_STACKING";

    /// <summary>The notes reached a recycler drawer. From here they are the bank's.</summary>
    public const string DepositStacked = "DEPOSIT_STACKED";

    /// <summary>Notes stuck in the mechanism: not in escrow, not in a drawer, nobody's.</summary>
    public const string DepositJammed = "DEPOSIT_JAMMED";

    /// <summary>The notes went back out to the customer. Nothing is credited.</summary>
    public const string DepositReturned = "DEPOSIT_RETURNED";

    /// <summary>What happened to the paper was reported to the host.</summary>
    public const string DepositCommitSent = "DEPOSIT_COMMIT_SENT";

    /// <summary>The host acknowledged it. The deposit is now closed on both sides.</summary>
    public const string DepositCommitAcknowledged = "DEPOSIT_COMMIT_ACKNOWLEDGED";

    /// <summary>No acknowledgement. It goes into the queue and will be sent again.</summary>
    public const string DepositCommitQueued = "DEPOSIT_COMMIT_QUEUED";

    // --- End of day (the protocol spec section 4.7) ---

    /// <summary>
    /// A cutover was wanted and not even attempted, because the queue was not empty.
    /// </summary>
    /// <remarks>
    /// This line matters more than it looks. A machine that quietly skipped the close
    /// would look identical, in its own record, to a machine nobody asked to close.
    /// </remarks>
    public const string CutoverBlocked = "CUTOVER_BLOCKED";

    /// <summary>The close was asked for. Carries this side's own total, so the record shows what was claimed.</summary>
    public const string CutoverRequested = "CUTOVER_REQUESTED";

    /// <summary>Both sides counted the same thing. The day is closed and the date has moved.</summary>
    public const string CutoverBalanced = "CUTOVER_BALANCED";

    /// <summary>The two sides did not agree. The day did NOT close; the amount is the difference.</summary>
    public const string CutoverOutOfBalance = "CUTOVER_OUT_OF_BALANCE";

    /// <summary>No answer came. The day did NOT close and the date did not move.</summary>
    public const string CutoverNoAnswer = "CUTOVER_NO_ANSWER";
}

/// <summary>One line in the terminal's record. Immutable once created.</summary>
public sealed record TerminalJournalEntry
{
    /// <summary>Position in the record. Starts at 1 and never skips.</summary>
    public required long Seq { get; init; }

    /// <summary>When it happened, taken from IClock - never DateTime.Now.</summary>
    public required DateTimeOffset At { get; init; }

    /// <summary>Which transaction (KARAR-009): terminal + business date + trace number.</summary>
    public required TransactionKey Key { get; init; }

    /// <summary>Which event, by the names in <see cref="TerminalEvent"/>.</summary>
    public required string Event { get; init; }

    /// <summary>
    /// The amount this line is about, in kurus. What it MEANS depends on the event, and
    /// that is deliberate: on CASH_TAKEN it is money that left the machine, on
    /// AUTH_REQUESTED it is money that was asked for and may never move. Adding the
    /// column up without looking at the event would produce a number that means nothing.
    /// </summary>
    public long Amount { get; init; }

    /// <summary>Short note for a human reading the record later.</summary>
    public string Note { get; init; } = "";

    /// <summary>Card number, masked. See <see cref="Pan"/> - the full one cannot be stored.</summary>
    public string MaskedPan { get; private init; } = "";

    /// <summary>Write-only door for the card number: whatever is put in is masked on the way.</summary>
    public string Pan
    {
        init => MaskedPan = string.IsNullOrEmpty(value) ? "" : MessageCodec.MaskPan(value);
    }

    /// <summary>
    /// Rebuilds an entry read back from storage, whose card number is already masked.
    /// Refuses a value that does not look masked - see JournalEntry.Restored for why.
    /// </summary>
    public static TerminalJournalEntry Restored(TerminalJournalEntry withoutPan, string maskedPan)
    {
        if (!string.IsNullOrEmpty(maskedPan) && !maskedPan.Contains('*'))
        {
            throw new InvalidDataException(
                "Kayıttan okunan kart numarası maskeli değil; terminal günlüğü satırı kabul edilmedi.");
        }

        return withoutPan with { MaskedPan = maskedPan };
    }
}

/// <summary>The terminal's record, seen from the flow's side. Append only.</summary>
public interface ITerminalJournal
{
    /// <summary>Adds one line. The line's sequence number is assigned here.</summary>
    TerminalJournalEntry Append(DateTimeOffset at, TransactionKey key, string @event,
        long amount = 0, string pan = "", string note = "");

    /// <summary>Everything written so far, in the order it was written.</summary>
    IReadOnlyList<TerminalJournalEntry> Entries { get; }
}

/// <summary>The record kept in memory. The file version is FileTerminalJournal.</summary>
public sealed class InMemoryTerminalJournal : ITerminalJournal
{
    private readonly List<TerminalJournalEntry> _entries = [];

    public IReadOnlyList<TerminalJournalEntry> Entries => _entries;

    public TerminalJournalEntry Append(DateTimeOffset at, TransactionKey key, string @event,
        long amount = 0, string pan = "", string note = "")
    {
        var entry = new TerminalJournalEntry
        {
            Seq = _entries.Count + 1,
            At = at,
            Key = key,
            Event = @event,
            Amount = amount,
            Pan = pan,
            Note = note,
        };

        _entries.Add(entry);
        return entry;
    }
}
