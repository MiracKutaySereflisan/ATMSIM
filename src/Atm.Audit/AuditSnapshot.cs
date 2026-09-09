// Copyright (c) 2026 Mirac Kutay Sereflisan. MIT lisansi - LICENSE dosyasina bakiniz.
// AuditSnapshot.cs
//
// What this file does: it is everything the conservation checker is allowed to look at,
// gathered in one place, at one moment.
//
// Why a snapshot rather than live objects: a reconciliation compares two states, and the
// two have to be read at the same instant. If the checker held a live dispenser and a
// live host and asked them questions one after another, a withdrawal happening in between
// would make the two halves disagree - and the checker would report a violation that was
// really its own reading being late. That failure mode is the one that turns an
// invariant layer into a source of noise, and noise is how a real finding gets ignored.
//
// The second reason is stage 4. A scenario run has to be reproducible from a seed
// (rule 5), and a checker that reads live objects can only run while the
// system is up. A snapshot can be written to a file, kept, and checked again later - by
// the scenario runner, by the end-of-day script, and by a human who wants to see why.
//
// The account balances arrive as a plain list rather than being read out of the host's
// account store, and that is not laziness: IAccountStore deliberately offers no way to
// list every account (its own file explains why), and a real reconciliation does not
// query the live production database either - it is given a balance file cut at a
// point in time. This mirrors that.

using Atm.Host;
using Atm.Terminal;

namespace Atm.Audit;

/// <summary>Which question is being asked: the mid-day one or the end-of-day one.</summary>
public enum AuditMoment
{
    /// <summary>
    /// Between customers. Money may legitimately be in flight: an advice can be sitting
    /// in the queue and a hold can be standing against an account.
    /// </summary>
    AtRest,

    /// <summary>
    /// The day is closed. Anything still in flight is now a difference somebody has to
    /// explain, and a hold with no cash behind it is money nobody can spend.
    /// </summary>
    EndOfDay,
}

/// <summary>One account's balance at a moment, as a balance file would carry it.</summary>
/// <param name="AccountId">The invented account identity, e.g. TR-DEMO-001.</param>
/// <param name="LedgerBalance">What its history adds up to, in kurus.</param>
/// <param name="HoldAmount">What is promised to an authorisation that has not closed, in kurus.</param>
public sealed record AccountBalance(string AccountId, long LedgerBalance, long HoldAmount);

/// <summary>Everything the checker reads, taken at one instant.</summary>
public sealed record AuditSnapshot
{
    /// <summary>Which of the two questions is being asked.</summary>
    public required AuditMoment Moment { get; init; }

    /// <summary>Where every note in the machine was when the day started.</summary>
    public required CashPosition CashAtStart { get; init; }

    /// <summary>Where every note in the machine is now.</summary>
    public required CashPosition CashNow { get; init; }

    /// <summary>The accounts as the day started.</summary>
    public required IReadOnlyList<AccountBalance> LedgerAtStart { get; init; }

    /// <summary>The accounts now.</summary>
    public required IReadOnlyList<AccountBalance> LedgerNow { get; init; }

    /// <summary>The host's record: what it was asked and what it answered.</summary>
    public required IReadOnlyList<JournalEntry> HostJournal { get; init; }

    /// <summary>The terminal's record: what happened to the paper.</summary>
    public required IReadOnlyList<TerminalJournalEntry> TerminalJournal { get; init; }

    /// <summary>Transactions with a promise still standing against them.</summary>
    public IReadOnlyList<string> OpenAuthorisations { get; init; } = [];

    /// <summary>Deposits the host agreed to and has not been told the end of.</summary>
    public IReadOnlyList<string> OpenDeposits { get; init; } = [];

    /// <summary>How much money left the accounts, all of them together, in kurus.</summary>
    public long LedgerFall =>
        LedgerAtStart.Sum(a => a.LedgerBalance) - LedgerNow.Sum(a => a.LedgerBalance);

    /// <summary>How much more money the customers are holding than when the day started.</summary>
    public long CashHandedToCustomers => CashNow.WithCustomers - CashAtStart.WithCustomers;

    /// <summary>How much money came in through the deposit mouth since the day started.</summary>
    public long CashTakenFromCustomers =>
        CashNow.TakenFromCustomers - CashAtStart.TakenFromCustomers;
}
