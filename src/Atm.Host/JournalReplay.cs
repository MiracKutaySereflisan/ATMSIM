// JournalReplay.cs
//
// What this file does: it reads the journal back and works out what was still unfinished
// when the host last stopped - which promises were never closed, which deposits were never
// committed, and which business days were settled.
//
// Why this exists: before it, all of that lived only in memory. A host that was restarted
// forgot every promise it had made. That is not a small gap. A promise the host has
// forgotten is money the customer cannot spend and the bank never took: the hold is still
// in the balance file, but nothing is left that could ever release it. And a business day
// the host had settled would become settleable again, on a different set of totals.
//
// The idea is the one this whole project is built on, applied to the host's own memory:
// THE JOURNAL IS THE TRUTH. Everything else - balances, open promises, closed days - is
// derived from it and can be derived again. That is what makes the journal being
// append-only worth the trouble; a record you can rebuild from is a record, and a record
// you cannot is a log.
//
// What this file does NOT rebuild: the replay table - the host's memory of the answers it
// gave (KARAR-032). It cannot, because the answers themselves are not in the journal, only
// what they decided. That sounds worse than it is: the messages that MOVE money are
// protected by something stronger than the replay table. A dispense advice arriving twice
// finds no open authorisation the second time and is refused; so is a second deposit
// commit. What a restart loses is the ability to give a byte-identical answer to a
// repeated PIN or balance question - and neither of those moves anything.

using Atm.Protocol;

namespace Atm.Host;

/// <summary>Something the host agreed to and had not finished when it last stopped.</summary>
/// <param name="Key">Which transaction.</param>
/// <param name="AccountId">Whose account - the identity that survives a journal (KARAR-048).</param>
/// <param name="Amount">How much was promised or counted, in kurus.</param>
public sealed record OpenItem(TransactionKey Key, string AccountId, long Amount);

/// <summary>Rebuilds the host's unfinished business out of its own journal.</summary>
public static class JournalReplay
{
    /// <summary>Authorisations that were approved and never closed by an advice or a reversal.</summary>
    public static IReadOnlyList<OpenItem> OpenAuthorisations(IEnumerable<JournalEntry> entries) =>
        StillOpen(entries, MessageType.WithdrawalAuthRequest,
            [MessageType.DispenseAdvice, MessageType.ReversalRequest]);

    /// <summary>Deposits that were agreed and never committed or reversed.</summary>
    public static IReadOnlyList<OpenItem> OpenDeposits(IEnumerable<JournalEntry> entries) =>
        StillOpen(entries, MessageType.DepositAuthRequest,
            [MessageType.DepositCommitAdvice, MessageType.ReversalRequest]);

    /// <summary>
    /// Transactions that were reversed. A reversed transaction is closed for good and may
    /// never be authorised again (KARAR-050).
    /// </summary>
    /// <remarks>
    /// Rebuilt like everything else, because a host that forgot this on restart would
    /// re-open a transaction whose promise had already been released - and the second
    /// authorisation would look perfectly ordinary in the record.
    /// </remarks>
    public static IReadOnlyList<TransactionKey> ReversedTransactions(
        IEnumerable<JournalEntry> entries) =>
        [.. entries
            .Where(e => e.Type == MessageType.ReversalRequest && e.Rc == ResponseCode.Approved)
            .Select(e => e.Key)
            .Distinct()];

    /// <summary>The business days this host agreed to close, oldest first.</summary>
    public static IReadOnlyList<string> ClosedDays(IEnumerable<JournalEntry> entries) =>
        [.. entries
            .Where(e => e.Type == MessageType.CutoverRequest && e.Rc == ResponseCode.Approved)
            .Select(e => e.Key.BizDate)
            .Distinct()];

    /// <summary>
    /// Everything opened by one kind of line and not closed by any of the others.
    /// </summary>
    /// <remarks>
    /// Only approved lines count on both sides. A refused authorisation opened nothing, and
    /// a refused reversal closed nothing - counting either would make the host forget a
    /// promise it is still holding, which is the one mistake this file exists to prevent.
    ///
    /// The order of the two passes matters and is deliberate: everything is opened first
    /// and closed afterwards, so a record whose lines were written out of order - a file
    /// merged by hand, a clock that went backwards - still produces the same answer.
    /// </remarks>
    private static IReadOnlyList<OpenItem> StillOpen(
        IEnumerable<JournalEntry> entries, string opens, string[] closes)
    {
        var lines = entries.Where(e => e.Rc == ResponseCode.Approved).ToList();
        var open = new Dictionary<TransactionKey, OpenItem>();

        foreach (var entry in lines.Where(e => e.Type == opens))
        {
            open[entry.Key] = new OpenItem(entry.Key, entry.AccountId, entry.Amount);
        }

        foreach (var entry in lines.Where(e => closes.Contains(e.Type)))
        {
            open.Remove(entry.Key);
        }

        return [.. open.Values.OrderBy(i => i.Key.ToString())];
    }
}
