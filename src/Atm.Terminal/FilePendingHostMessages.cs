// FilePendingHostMessages.cs
//
// What this file does: it keeps the queue of unacknowledged messages in a file, so that a
// terminal which is switched off and on again still owes the host the same messages it
// owed before.
//
// Why this matters more than it looks: the messages in this queue are about money that has
// already moved. A reversal that disappears when the machine restarts leaves a hold on a
// customer's account with nothing to release it; a dispense advice that disappears leaves
// cash out of a drawer and in nobody's ledger. docs/protocol.md section 4.5 states the
// rule plainly - the queue is written to disk - and this is where that promise is kept
// (KARAR-013).
//
// Why the whole file is rewritten rather than appended to, unlike the journal: this is not
// a record of what happened, it is a list of what is still owed, and things leave it. A
// list that only grows would have to be replayed and filtered to be understood, and the
// filtering would be the part that goes wrong. The file is small by nature - a queue with
// many entries in it is already a problem that a file format cannot fix.
//
// The write happens through a temporary file and a rename, so that a crash in the middle
// leaves either the old list or the new one, never half of either.

using System.Text;
using System.Text.Json;
using Atm.Protocol;

namespace Atm.Terminal;

/// <summary>The unacknowledged-message queue, kept in a file (KARAR-013).</summary>
public sealed class FilePendingHostMessages : IPendingHostMessages
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
    private readonly PendingHostMessages _inMemory = new();

    /// <summary>Opens the queue at this path, reading back anything still owed.</summary>
    public FilePendingHostMessages(string path)
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

            var stored = JsonSerializer.Deserialize<StoredPending>(line, Options)
                ?? throw new InvalidDataException(
                    $"{path}: bekleyen mesaj okunamadı. Satır: {line}");

            _inMemory.Restore(stored.ToPending());
        }
    }

    public int Count => _inMemory.Count;

    public IReadOnlyList<PendingMessage> All => _inMemory.All;

    public void Add(Envelope message, DateTimeOffset now)
    {
        _inMemory.Add(message, now);
        Save();
    }

    public IReadOnlyList<PendingMessage> Due(DateTimeOffset now) => _inMemory.Due(now);

    public void Attempted(PendingMessage pending, DateTimeOffset now)
    {
        _inMemory.Attempted(pending, now);
        Save();
    }

    public void Acknowledge(TransactionKey key, string type)
    {
        _inMemory.Acknowledge(key, type);
        Save();
    }

    private void Save()
    {
        var lines = _inMemory.All
            .Select(p => JsonSerializer.Serialize(StoredPending.From(p), Options));

        // Write beside the real file, then move it into place. A crash before the move
        // leaves the previous list intact; a crash after it leaves the new one. There is
        // no moment at which the file on disk is half a list.
        var temporary = _path + ".writing";
        File.WriteAllLines(temporary, lines, Utf8NoBom);
        File.Move(temporary, _path, overwrite: true);
    }

    /// <summary>
    /// One line of the file: the message as it goes on the wire, plus what we know about
    /// trying to send it.
    /// </summary>
    /// <remarks>
    /// The envelope is stored as the very bytes that would be sent. Storing a parsed form
    /// and rebuilding it later would mean the message that finally reaches the host is one
    /// this file assembled, not the one the flow decided to send.
    /// </remarks>
    private sealed record StoredPending(string Wire, int Attempts, DateTimeOffset DueAt)
    {
        public static StoredPending From(PendingMessage pending) => new(
            MessageCodec.AsText(MessageCodec.Encode(pending.Message)),
            pending.Attempts, pending.DueAt);

        public PendingMessage ToPending() =>
            new(MessageCodec.Decode(Encoding.UTF8.GetBytes(Wire)), Attempts, DueAt);
    }
}
