// IJournal.cs
//
// What this file does: it is the host's own record of what it was asked and what it
// answered - one line per message, in order, never edited.
//
// Why this exists: at the end of the day the host's record and the terminal's record
// are compared, and every difference has to be explainable. That comparison is only
// possible if both sides wrote down what they saw AT THE TIME. A record written
// afterwards from memory is not evidence, and a record that can be edited is not
// evidence either. So entries are appended and never changed - rule 12 (docs/proje-kurallari.md)
// and the ledger rule of docs/model.md section 4: a wrong movement is corrected by a
// second movement, never by rubbing out the first.
//
// The journal is not the ledger. The ledger is what the account's money did; the
// journal is what the host was asked and what it said. A balance enquiry moves no
// money and still belongs in the journal, because "the customer asked at 14:02 and we
// answered 2500,00" is exactly the kind of question a dispute starts with.
//
// One decision is enforced by the type rather than by discipline: JournalEntry takes
// a card number and stores it MASKED. There is no way to put a full card number into
// a journal entry, and there is no PIN field at all - so "let me just log the request
// while I debug this" cannot write one. Instruction section 2 forbids logging a PIN;
// a rule that depends on nobody forgetting is not a rule, so it is made unsayable.
//
// Behind the interface today: a list in memory. KARAR-013 requires the ledger and the
// pending-reversal queue to reach disk, and that arrives in Phase 2 with the first
// movement that has money in it - a durable file with nothing to make durable would
// be a file written for its own sake.

using Atm.Protocol;

namespace Atm.Host;

/// <summary>One line in the host's record. Immutable once created.</summary>
public sealed record JournalEntry
{
    /// <summary>Position in the record. Starts at 1 and never skips.</summary>
    public required long Seq { get; init; }

    /// <summary>When the host handled it, taken from IClock - never DateTime.Now.</summary>
    public required DateTimeOffset At { get; init; }

    /// <summary>Which transaction (KARAR-009): terminal + business date + trace number.</summary>
    public required TransactionKey Key { get; init; }

    /// <summary>Which message, by the names in MessageType.</summary>
    public required string Type { get; init; }

    /// <summary>Response code the host returned.</summary>
    public required string Rc { get; init; }

    /// <summary>
    /// The amount this line is about, in kurus. Zero when the line is about no money -
    /// an echo, a PIN check, a refused request.
    /// </summary>
    /// <remarks>
    /// Note what this is NOT: it is not "how much the ledger moved". An authorisation
    /// is about an amount without moving the ledger at all (KARAR-029), and writing 0
    /// there would lose the one number the line exists to record. The end-of-day
    /// comparison therefore adds this column up PER MESSAGE TYPE - which it has to do
    /// anyway, since an authorisation and a dispense advice mean different things about
    /// the same money. Keeping the amount inside the note text instead would mean
    /// reconciling by reading sentences (KARAR-030).
    /// </remarks>
    public long Amount { get; init; }

    /// <summary>Short note for a human reading the record later.</summary>
    public string Note { get; init; } = "";

    /// <summary>
    /// Which ACCOUNT this line is about, e.g. TR-DEMO-001. Empty when it is about no
    /// account - an echo, a cutover, a message that was refused before any card was
    /// looked up.
    /// </summary>
    /// <remarks>
    /// KARAR-048. A ledger posts to an account, not to a card: one account can be
    /// reached by several cards, and the card is only how the customer got here. The
    /// masked card number is kept beside it for a human reading the record, but it is
    /// not an identity - masking is deliberately lossy, and two different cards can mask
    /// to the same string. Balances are rebuilt from this field, so a record identified
    /// by the masked card would be a record that can add two customers together.
    /// </remarks>
    public string AccountId { get; init; } = "";

    /// <summary>Card number, masked. See <see cref="Pan"/> - the full one cannot be stored.</summary>
    public string MaskedPan { get; private init; } = "";

    /// <summary>
    /// Write-only door for the card number: whatever is put in is masked on the way.
    /// This is why no journal entry can hold a full card number.
    /// </summary>
    public string Pan
    {
        init => MaskedPan = string.IsNullOrEmpty(value) ? "" : MessageCodec.MaskPan(value);
    }

    /// <summary>
    /// Rebuilds an entry that was read back from storage, whose card number is already
    /// masked. Refuses a value that does not look masked.
    /// </summary>
    /// <remarks>
    /// This is the only other door into <see cref="MaskedPan"/>, and it is guarded:
    /// MaskPan always leaves at least one star in anything it produces, so a value with
    /// no star in it never came from there. Without the check, a stored record would be
    /// a way to put a whole card number into a journal entry - the one thing this record
    /// is built to make impossible.
    /// </remarks>
    public static JournalEntry Restored(JournalEntry withoutPan, string maskedPan)
    {
        if (!string.IsNullOrEmpty(maskedPan) && !maskedPan.Contains('*'))
        {
            throw new InvalidDataException(
                "Kayıttan okunan kart numarası maskeli değil; defter satırı kabul edilmedi.");
        }

        return withoutPan with { MaskedPan = maskedPan };
    }
}

/// <summary>The host's record, seen from the flow's side. Append only.</summary>
public interface IJournal
{
    /// <summary>Adds one line. The line's sequence number is assigned here.</summary>
    JournalEntry Append(DateTimeOffset at, TransactionKey key, string type, string rc,
        string pan = "", string note = "", long amount = 0, string accountId = "");

    /// <summary>Everything written so far, in the order it was written.</summary>
    IReadOnlyList<JournalEntry> Entries { get; }
}

/// <summary>The record kept in memory. Disk arrives in Phase 2 (KARAR-013).</summary>
public sealed class InMemoryJournal : IJournal
{
    private readonly List<JournalEntry> _entries = [];

    public IReadOnlyList<JournalEntry> Entries => _entries;

    public JournalEntry Append(DateTimeOffset at, TransactionKey key, string type, string rc,
        string pan = "", string note = "", long amount = 0, string accountId = "")
    {
        var entry = new JournalEntry
        {
            Seq = _entries.Count + 1,
            At = at,
            Key = key,
            Type = type,
            Rc = rc,
            Pan = pan,
            Note = note,
            Amount = amount,
            AccountId = accountId,
        };

        _entries.Add(entry);
        return entry;
    }
}
