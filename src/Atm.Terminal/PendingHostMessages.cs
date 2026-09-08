// Copyright (c) 2026 Mirac Kutay Sereflisan. MIT lisansi - LICENSE dosyasina bakiniz.
// PendingHostMessages.cs
//
// What this file does: it holds the messages the terminal has sent and not yet had an
// answer to, and it decides when each one should be tried again.
//
// Why this exists at all: a reversal that is lost is worse than no reversal, because the
// machine believes it has corrected something it has not (rule 4.2). The
// same is true of a dispense advice: cash has already left the machine, and if the host
// never hears about it the money is out of a drawer and in nobody's ledger. Both messages
// therefore live here until the host says it heard them.
//
// The rule this file exists to enforce: A MESSAGE IN THIS QUEUE DOES NOT DIE UNTIL IT IS
// ACKNOWLEDGED. Not when the line drops, not when the customer walks away, not when the
// terminal is switched off and on again. The intervals come from the protocol spec section
// 4.5 and they get longer on purpose - a host that is down does not become less down by
// being asked more often.
//
// Where the disk is: FilePendingHostMessages, beside this file. This class is the
// in-memory form and it is what the tests use; the running terminal uses the file form,
// which is why a queued reversal survives the terminal being switched off and on again
// (KARAR-013). The interface exists so that the flow above never learns where the queue
// is kept - the two implementations differ in one place and nowhere else.
//
// What is NOT here, and is a deliberate limit: a background timer. The queue is drained
// on the next transaction, not on a clock of its own, so a machine that is switched on
// and left alone will not send its queued message until somebody uses it. A real ATM
// pumps its queue in the background; this one does not, and reports/assumptions.md says
// so rather than letting the omission look like an oversight.

using Atm.Protocol;

namespace Atm.Terminal;

/// <summary>One message waiting for an acknowledgement, and its history of attempts.</summary>
/// <param name="Message">The envelope to send again, unchanged apart from its retry count.</param>
/// <param name="Attempts">How many times it has been sent so far.</param>
/// <param name="DueAt">When it may be sent again.</param>
public sealed record PendingMessage(Envelope Message, int Attempts, DateTimeOffset DueAt)
{
    /// <summary>Which transaction and which message - the pair that identifies it (KARAR-031).</summary>
    public (TransactionKey Key, string Type) Identity => (Message.Key, Message.Type);
}

/// <summary>Messages the host has not acknowledged yet, seen from the flow's side.</summary>
public interface IPendingHostMessages
{
    /// <summary>How many messages are still waiting to be acknowledged.</summary>
    int Count { get; }

    /// <summary>
    /// Adds a message that must keep being sent until the host answers it. Adding the
    /// same message twice does not queue it twice.
    /// </summary>
    void Add(Envelope message, DateTimeOffset now);

    /// <summary>The messages whose next attempt is due at or before this moment.</summary>
    IReadOnlyList<PendingMessage> Due(DateTimeOffset now);

    /// <summary>Records that one was sent and not answered, so the next attempt is later.</summary>
    void Attempted(PendingMessage pending, DateTimeOffset now);

    /// <summary>The host answered this one. It stops being re-sent.</summary>
    void Acknowledge(TransactionKey key, string type);

    /// <summary>Everything still waiting, in the order it was queued.</summary>
    IReadOnlyList<PendingMessage> All { get; }
}

/// <summary>
/// The queue kept in memory. The disk arrives with the persistence step of Phase 2
/// (KARAR-013); nothing above this class has to change when it does.
/// </summary>
public sealed class PendingHostMessages : IPendingHostMessages
{
    /// <summary>
    /// How long to wait before each attempt, from the protocol spec section 4.5. After the
    /// list runs out the last interval repeats for ever.
    /// </summary>
    /// <remarks>
    /// The intervals grow on purpose. A host that is not answering does not start
    /// answering because it is asked more often, and a terminal that retries in a tight
    /// loop turns one unreachable host into a terminal that can do nothing else.
    /// </remarks>
    public static readonly IReadOnlyList<TimeSpan> Backoff =
    [
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(60),
        TimeSpan.FromMinutes(5),
    ];

    private readonly List<PendingMessage> _waiting = [];

    public int Count => _waiting.Count;

    public IReadOnlyList<PendingMessage> All => _waiting;

    public void Add(Envelope message, DateTimeOffset now)
    {
        var identity = (message.Key, message.Type);

        if (_waiting.Any(p => p.Identity == identity))
        {
            // Already waiting. Queuing it twice would double the traffic to a host that
            // is already not answering, and the second copy would be acknowledged by the
            // same reply as the first.
            return;
        }

        // Due immediately: the first attempt already happened or is happening now, and
        // this entry exists to make sure there is a second one.
        _waiting.Add(new PendingMessage(message, Attempts: 1, DueAt: now + Backoff[0]));
    }

    public IReadOnlyList<PendingMessage> Due(DateTimeOffset now) =>
        _waiting.Where(p => p.DueAt <= now).ToList();

    public void Attempted(PendingMessage pending, DateTimeOffset now)
    {
        var index = _waiting.FindIndex(p => p.Identity == pending.Identity);

        if (index < 0)
        {
            // Acknowledged while this attempt was in flight. Nothing to reschedule.
            return;
        }

        var attempts = _waiting[index].Attempts + 1;
        var wait = Backoff[Math.Min(attempts - 1, Backoff.Count - 1)];

        // The envelope carries its own attempt counter so that the host's record can show
        // how many times the terminal had to ask. It is diagnostic only: the host decides
        // on identity, never on this number (the protocol spec section 3).
        var message = _waiting[index].Message with { Retry = attempts - 1 };

        _waiting[index] = new PendingMessage(message, attempts, now + wait);
    }

    public void Acknowledge(TransactionKey key, string type) =>
        _waiting.RemoveAll(p => p.Identity == (key, type));

    /// <summary>
    /// Puts back a message read from storage, with the attempt count and due time it had
    /// when it was written down.
    /// </summary>
    /// <remarks>
    /// Not the same as Add: Add starts a message's life and gives it a first interval,
    /// while this continues one that already has a history. A restarted terminal that
    /// used Add would give every owed message a fresh ten-second wait, which would turn a
    /// restart loop into a way of hammering a host that is already not answering.
    /// </remarks>
    internal void Restore(PendingMessage pending)
    {
        if (_waiting.Any(p => p.Identity == pending.Identity))
        {
            return;
        }

        _waiting.Add(pending);
    }
}
