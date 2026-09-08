// HostDayTotals.cs
//
// What this file does: it adds up one business day out of the host's own journal, in the
// three figures the cutover compares.
//
// Why it is a file of its own: it is one half of a pair. The other half is
// TerminalDayTotals.cs on the machine's side, and the whole value of a reconciliation is
// that these two files never see each other's numbers - they read two records kept by two
// programs that do not share memory, and then the two results are compared over the wire
// (KARAR-045). Read them side by side: if the two rules ever drift apart, every day will
// fail to balance and the failure will have nothing to do with money.
//
// The rule: count money that MOVED. On this side that means the lines that actually moved
// the ledger - an approved dispense advice, an approved deposit commit. An authorisation
// is a promise and moves nothing. A reversal releases a promise and moves nothing. A
// refusal moves nothing. Those lines exist in the journal, and none of them is counted
// here.
//
// The same "amount != 0" guard the auditor uses is applied here, and for the same reason:
// an advice that says nothing came out is a real line about a real event, and adding zero
// transactions to the count would make two records disagree about how many things
// happened on a day when nothing did.

using Atm.Protocol;

namespace Atm.Host;

/// <summary>Adds up one business day out of the host's journal.</summary>
public static class HostDayTotals
{
    /// <summary>What the host's record says moved on that business day.</summary>
    public static DayTotals For(IEnumerable<JournalEntry> entries, string bizDate)
    {
        var moved = entries
            .Where(e => e.Key.BizDate == bizDate && e.Rc == ResponseCode.Approved && e.Amount != 0)
            .Where(e => e.Type is MessageType.DispenseAdvice or MessageType.DepositCommitAdvice)
            .ToList();

        return new DayTotals(
            Withdrawals: moved.Where(e => e.Type == MessageType.DispenseAdvice).Sum(e => e.Amount),
            Deposits: moved.Where(e => e.Type == MessageType.DepositCommitAdvice).Sum(e => e.Amount),
            Count: moved.Select(e => e.Key).Distinct().Count());
    }
}
