// FileJournal.cs
//
// What this file does: it writes the host's record to a file, one line per entry, and
// reads back whatever was already there when the host starts.
//
// Why a file and not a database: KARAR-013. A database would be a second thing to install,
// a second thing to keep running and a second thing that can be down on the morning of a
// demonstration. What the record actually needs is that it survives the process and that
// a human can read it - and a text file does both.
//
// Why one line per entry rather than one document rewritten each time: the record is
// append-only by design (KARAR-018), and appending is the one file operation that cannot
// destroy what is already there. A rewrite that is interrupted half way loses everything
// written before it; an append that is interrupted half way loses the line being written
// and nothing else. That difference matters here, because the lines before it are the
// evidence that money moved.
//
// What this file deliberately does NOT do: repair a damaged line. A line that cannot be
// read is reported and the host refuses to start. A record that quietly drops the entries
// it could not understand is a record that reconciles to zero by forgetting.

using System.Text;
using System.Text.Json;
using Atm.Protocol;

namespace Atm.Host;

/// <summary>The host's record, kept in a file so that it survives the process.</summary>
public sealed class FileJournal : IJournal
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
    private readonly List<JournalEntry> _entries = [];

    /// <summary>Opens the record at this path, reading back anything already written.</summary>
    public FileJournal(string path)
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
                    $"{path}: bir defter satırı okunamadı. Satır: {line}");

            _entries.Add(stored.ToEntry());
        }
    }

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

        // The line reaches the disk before this method returns. A record that is written
        // "soon" is a record that is missing exactly the entries a crash was about.
        File.AppendAllText(_path,
            JsonSerializer.Serialize(StoredEntry.From(entry), Options) + Environment.NewLine,
            Utf8NoBom);

        return entry;
    }

    /// <summary>
    /// One line of the file. Separate from <see cref="JournalEntry"/> on purpose: the
    /// entry masks the card number on the way in through a write-only property, which is
    /// exactly the shape a serialiser cannot round-trip. Writing the masked value here
    /// keeps that protection - a full card number cannot reach this file even by mistake.
    /// </summary>
    private sealed record StoredEntry(
        long Seq, DateTimeOffset At, string Terminal, string BizDate, int Stan,
        string Type, string Rc, string MaskedPan, string Note, long Amount,
        string AccountId = "")
    {
        public static StoredEntry From(JournalEntry entry) => new(
            entry.Seq, entry.At, entry.Key.Terminal, entry.Key.BizDate, entry.Key.Stan,
            entry.Type, entry.Rc, entry.MaskedPan, entry.Note, entry.Amount,
            entry.AccountId);

        public JournalEntry ToEntry()
        {
            var entry = new JournalEntry
            {
                Seq = Seq,
                At = At,
                Key = new TransactionKey(Terminal, BizDate, Stan),
                Type = Type,
                Rc = Rc,
                Note = Note,
                Amount = Amount,
                AccountId = AccountId,
            };

            // The stored value is already masked. Putting it back through Pan would mask
            // the stars themselves; Restored refuses anything that is not masked already.
            return JournalEntry.Restored(entry, MaskedPan);
        }
    }
}
