// Copyright (c) 2026 Mirac Kutay Sereflisan. MIT lisansi - LICENSE dosyasina bakiniz.
// TerminalDayTotals.cs
//
// What this file does: it adds up one business day out of the terminal's own record, in
// the three figures that go on the wire at cutover.
//
// Why it is a file of its own rather than three lines inside CutoverFlow: the same
// question - "how much moved today" - is asked in two places by two different machines,
// and the two answers are then compared. If either side counted differently the
// comparison would fail for a reason that has nothing to do with money. Putting the rule
// in a named place on each side makes the two rules readable side by side; the host's
// half is HostDayTotals.cs and the two files are meant to be read together.
//
// The rule, from the protocol spec section 4.7: count money that PHYSICALLY moved. Cash
// the customer took, notes that reached a drawer. Not what was authorised, not what was
// asked for, not what was handed back, not what a reversal released. Intent is not
// movement, and a day counted by intent would balance against nothing.
//
// Why the entries are filtered by the key's business date rather than by the wall clock:
// a transaction belongs to the day it was BORN in, and that day travels in its key
// (KARAR-009). An advice that arrives after midnight still belongs to yesterday. Counting
// by the clock would move it to today and leave both days wrong.

using Atm.Protocol;

namespace Atm.Terminal;

/// <summary>Adds up one business day out of the terminal's record.</summary>
public static class TerminalDayTotals
{
    /// <summary>What this machine says moved on that business day.</summary>
    public static DayTotals For(IEnumerable<TerminalJournalEntry> entries, string bizDate)
    {
        var moved = entries
            .Where(e => e.Key.BizDate == bizDate && e.Amount != 0)
            .Where(e => e.Event is TerminalEvent.CashTaken or TerminalEvent.DepositStacked)
            .ToList();

        return new DayTotals(
            Withdrawals: moved.Where(e => e.Event == TerminalEvent.CashTaken).Sum(e => e.Amount),
            Deposits: moved.Where(e => e.Event == TerminalEvent.DepositStacked).Sum(e => e.Amount),
            Count: moved.Select(e => e.Key).Distinct().Count());
    }
}
