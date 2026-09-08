// Copyright (c) 2026 Mirac Kutay Sereflisan. MIT lisansi - LICENSE dosyasina bakiniz.
// FileTerminalJournal.cs
//
// What this file does: it writes the terminal's record to a file, one line per event, and
// reads back whatever was already there when the terminal starts.
//
// Why this matters more here than on the host side: a terminal is switched off. It loses
// power, it is restarted by an engineer, it is moved. The events this record holds are
// about paper that has already left the machine - and paper does not come back when the
// process does. A record that lived only in memory would forget exactly the cash whose
// whereabouts nobody else knows (KARAR-013, KARAR-035).
//
// The shape is the host's FileJournal on purpose: one JSON object per line, appended,
// never rewritten, and a line that cannot be read stops the terminal rather than being
// skipped. The reasoning for each of those is written out in FileJournal.cs and is not
// repeated here - if that reasoning changes, it changes in both places.

using System.Text;
using System.Text.Json;
using Atm.Protocol;

namespace Atm.Terminal;

/// <summary>The terminal's record, kept in a file so that it survives the process.</summary>
public sealed class FileTerminalJournal : ITerminalJournal
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };


    /// <summary>
    /// UTF-8 with no byte-order mark.
    /// </summary>
    /// <remarks>
    /// Encoding.UTF8 writes three extra bytes at the front of a file it creates. Our own
    /// reader strips them, so nothing here ever noticed - but this file exists precisely
    /// so that OTHER tools can read it, and a strict JSON reader stops at those three
    /// bytes. Found by reading a journal with a script that was not ours (2026-08-25).
    /// </remarks>
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly string _path;
    private readonly List<TerminalJournalEntry> _entries = [];

    /// <summary>Opens the record at this path, reading back anything already written.</summary>
    public FileTerminalJournal(string path)
    {
        _path = path;

        if (!File.Exists(path))
        {
            return;
        }

        foreach (var line in File.ReadAllLines(path, Utf8NoBom))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var stored = JsonSerializer.Deserialize<StoredEntry>(line, Options)
                ?? throw new InvalidDataException(
                    $"{path}: bir terminal günlüğü satırı okunamadı. Satır: {line}");

            _entries.Add(stored.ToEntry());
        }
    }

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

        File.AppendAllText(_path,
            JsonSerializer.Serialize(StoredEntry.From(entry), Options) + Environment.NewLine,
            Utf8NoBom);

        return entry;
    }

    /// <summary>
    /// One line of the file. Separate from the entry for the same reason as on the host
    /// side: the entry masks the card number through a write-only property, which a
    /// serialiser cannot round-trip.
    /// </summary>
    private sealed record StoredEntry(
        long Seq, DateTimeOffset At, string Terminal, string BizDate, int Stan,
        string Event, long Amount, string MaskedPan, string Note)
    {
        public static StoredEntry From(TerminalJournalEntry entry) => new(
            entry.Seq, entry.At, entry.Key.Terminal, entry.Key.BizDate, entry.Key.Stan,
            entry.Event, entry.Amount, entry.MaskedPan, entry.Note);

        public TerminalJournalEntry ToEntry()
        {
            var entry = new TerminalJournalEntry
            {
                Seq = Seq,
                At = At,
                Key = new TransactionKey(Terminal, BizDate, Stan),
                Event = Event,
                Amount = Amount,
                Note = Note,
            };

            return TerminalJournalEntry.Restored(entry, MaskedPan);
        }
    }
}
