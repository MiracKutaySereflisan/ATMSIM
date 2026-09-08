// CutoverFlow.cs
//
// What this file does: it closes a business day. It counts what this machine says moved,
// asks the host to agree, and moves the date only if the host does.
//
// Why this exists: until now the business date changed by itself at midnight UTC. Nothing
// compared the two sides' totals, nothing recorded a closing, and the day simply became a
// different day - in the middle of the night, in a timezone nobody in this project lives
// in. Instruction section 4.9 says the end of a day is a MOMENT; this file is that moment.
//
// The one rule that shapes everything here: THE DATE MOVES ONLY ON A YES. Not on silence,
// not on a mismatch, not on a refusal. It is the same rule the withdrawal flow follows
// when the host goes quiet, pointing at a different problem: a machine that acts on
// silence is a machine whose books are guesses.
//
// What this file does NOT do: decide when the day should end, repair a difference, or say
// which transaction caused one. A cutover can only report THAT the two records disagree -
// finding out where is the auditor's job (Atm.Audit), and it needs both records in front
// of it, which no machine in a real network ever has.

using Atm.Protocol;

namespace Atm.Terminal;

/// <summary>What came of trying to close the day.</summary>
/// <param name="Closed">True only when the host agreed and the date has moved.</param>
/// <param name="Rc">The host's response code, or "" when nothing was sent or nothing came back.</param>
/// <param name="Mine">What this machine counted for the day.</param>
/// <param name="Theirs">What the host counted, when an answer arrived.</param>
/// <param name="Reason">In words, why it did not close. Empty when it did.</param>
public sealed record CutoverResult(
    bool Closed, string Rc, DayTotals Mine, DayTotals? Theirs, string Reason)
{
    /// <summary>How far apart the two sides are, in kurus. Zero when they agree or nobody answered.</summary>
    public long Difference => Theirs is null
        ? 0
        : Math.Abs(Mine.Withdrawals - Theirs.Withdrawals) + Math.Abs(Mine.Deposits - Theirs.Deposits);
}

/// <summary>Closes one business day against the host. See docs/protocol.md section 4.7.</summary>
public sealed class CutoverFlow
{
    /// <summary>How long it waits for the host to agree.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private readonly Func<Envelope, TimeSpan, Envelope?> _ask;
    private readonly IClock _clock;
    private readonly IPendingHostMessages _pending;
    private readonly ITerminalJournal _journal;
    private readonly BusinessDay _day;
    private readonly string _terminalId;

    public CutoverFlow(
        Func<Envelope, TimeSpan, Envelope?> ask,
        IClock clock,
        IPendingHostMessages pending,
        ITerminalJournal journal,
        BusinessDay day,
        string terminalId = "ATM-01")
    {
        _ask = ask;
        _clock = clock;
        _pending = pending;
        _journal = journal;
        _day = day;
        _terminalId = terminalId;
    }

    /// <summary>The day being closed, until it closes.</summary>
    public string Closing => _day.Current;

    /// <summary>
    /// Tries to close the current business day.
    /// </summary>
    /// <remarks>
    /// The queue is checked first and the host is not asked at all when it is not empty.
    /// The reason is not efficiency: an advice still in the queue has not reached the
    /// host's ledger, so the totals are guaranteed not to match - and they would not match
    /// because of DELAY, not because of a DIFFERENCE. Letting the two arrive as the same
    /// answer is how a real difference gets waved away as "probably the queue" (KARAR-046).
    /// </remarks>
    public CutoverResult Close(int stan)
    {
        var closing = _day.Current;
        var mine = TerminalDayTotals.For(_journal.Entries, closing);
        var key = new TransactionKey(_terminalId, closing, stan);
        var opening = _day.Next;

        if (_pending.Count > 0)
        {
            var why = $"kuyrukta bekleyen {_pending.Count} bildirim var";
            _journal.Append(_clock.UtcNow, key, TerminalEvent.CutoverBlocked, note: why);
            return new CutoverResult(false, "", mine, null, why);
        }

        var request = MessageCodec.Envelope(MessageType.CutoverRequest, key, _clock.UtcNow,
            new CutoverRequestBody(opening, mine));

        _journal.Append(_clock.UtcNow, key, TerminalEvent.CutoverRequested,
            mine.Withdrawals + mine.Deposits,
            note: $"{closing}: {mine.Withdrawals} çekim, {mine.Deposits} yatırma, {mine.Count} işlem");

        var answer = _ask(request, Timeout);

        if (answer is null)
        {
            // Silence. The day stays open and the date stays where it is. A machine that
            // rolled the date here would be writing tomorrow's transactions into a day the
            // host still considers open - and yesterday's totals would never be agreed by
            // anyone.
            const string why = "hosttan cevap gelmedi; gün açık kaldı";
            _journal.Append(_clock.UtcNow, key, TerminalEvent.CutoverNoAnswer, note: why);
            return new CutoverResult(false, "", mine, null, why);
        }

        var body = answer.Body<CutoverResponseBody>();

        if (body.Rc != ResponseCode.Approved)
        {
            var difference =
                Math.Abs(mine.Withdrawals - body.Totals.Withdrawals)
                + Math.Abs(mine.Deposits - body.Totals.Deposits);

            _journal.Append(_clock.UtcNow, key, TerminalEvent.CutoverOutOfBalance, difference,
                note: $"rc={body.Rc}: {body.Reason}");

            return new CutoverResult(false, body.Rc, mine, body.Totals, body.Reason);
        }

        // The date the host agreed to, not a date recomputed here. If those two could
        // ever differ, the two sides would part company on the very message whose purpose
        // is to keep them together.
        _day.RollTo(opening);

        _journal.Append(_clock.UtcNow, key, TerminalEvent.CutoverBalanced,
            mine.Withdrawals + mine.Deposits,
            note: $"{closing} kapandı; yeni gün {_day.Current}");

        return new CutoverResult(true, body.Rc, mine, body.Totals, "");
    }
}
