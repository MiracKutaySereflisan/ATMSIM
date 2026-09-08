// Copyright (c) 2026 Mirac Kutay Sereflisan. MIT lisansi - LICENSE dosyasina bakiniz.
// Account.cs
//
// What this file does: it holds one account as the host sees it - an identity, the
// card that reaches it, and two different balances.
//
// Why two balances and not one: the ledger balance is what the account's history adds
// up to. The available balance is that minus the money already promised to a
// withdrawal that has been authorised but not yet closed. Since Phase 2b they stop
// being equal the moment an authorisation is approved: the hold rises, the ledger does
// not move, and the ledger only moves when the machine reports what it actually handed
// over (KARAR-029). A system with a single number cannot say "promised but not yet
// posted" - it must either pretend the money is gone or leave it free to be promised
// twice, and the second of those is where double spending lives.
//
// Why the amounts are whole numbers of kurus rather than decimals: KARAR-008.
// A decimal type would let 0.1 + 0.2 quietly become 0.30000000000000004. Money is
// counted, not measured, so it is counted here - in the smallest unit that exists.
//
// This record carries no PIN and no PIN-derived value. Those live in PinVerifier,
// separately, so that a future "let me just log the account to debug this" cannot
// print one.

using System.Text.Json.Serialization;

namespace Atm.Host;

/// <summary>One customer account. Amounts are whole kurus (KARAR-008).</summary>
public sealed record Account
{
    /// <summary>Invented identity, e.g. TR-DEMO-001. Matches nothing real.</summary>
    public required string AccountId { get; init; }

    /// <summary>Invented card number, Luhn-valid. Masked everywhere it is written down.</summary>
    public required string Pan { get; init; }

    /// <summary>What the account's history adds up to, in kurus.</summary>
    public required long LedgerBalance { get; init; }

    /// <summary>Money promised to an authorisation that has not closed yet, in kurus.</summary>
    public long HoldAmount { get; init; }

    /// <summary>What the customer may actually spend right now, in kurus.</summary>
    /// <remarks>
    /// Deliberately kept out of anything written to disk. It is a DERIVED value - the two
    /// numbers above are the record - and a derived value stored beside the numbers it
    /// comes from is a value that can one day disagree with them. The same reasoning runs
    /// through KARAR-047 at a larger scale: what can be recomputed is not stored, and what
    /// is stored is compared against what can be recomputed.
    /// </remarks>
    [JsonIgnore]
    public long AvailableBalance => LedgerBalance - HoldAmount;
}
