// Copyright (c) 2026 Mirac Kutay Sereflisan. MIT lisansi - LICENSE dosyasina bakiniz.
// LedgerRebuild.cs
//
// What this file does: it works out what every account's balance SHOULD be by reading the
// journal from the first line, and says where that disagrees with the balances on file.
//
// Why this exists (KARAR-047): the balance file and the journal are two records of the
// same thing, kept by two different mechanisms - one rewritten on every movement, one only
// ever appended to. Two records that are never compared are not two records; they are one
// record and a rumour. This file is the comparison, and it runs before the host opens its
// port.
//
// The direction of trust is fixed and it is not a preference: the JOURNAL is right. It is
// append-only, every line was written at the moment the money moved, and no later event
// can change what an earlier line says. The balance file is a current value that is
// overwritten constantly. When they disagree, the balance file is the one that has been
// damaged - by a half-finished write, by an edit, by a restore from the wrong day.
//
// What this file does NOT do: repair the balance file. It reports and the host refuses to
// start. A host that silently rewrote balances to match its own journal would be a host
// that cannot be caught being wrong - and if the journal were the damaged one, the repair
// would destroy the only evidence of what really happened.

using Atm.Protocol;

namespace Atm.Host;

/// <summary>Rebuilds balances from the journal and compares them with the file.</summary>
public static class LedgerRebuild
{
    /// <summary>
    /// The net movement of each account according to the journal, in kurus. Positive means
    /// the account grew.
    /// </summary>
    /// <remarks>
    /// Only two kinds of line move a ledger, and they are the same two the end-of-day
    /// totals count (HostDayTotals): an approved dispense advice takes money out, an
    /// approved deposit commit puts money in. An authorisation is a promise; a reversal
    /// releases a promise; a refusal does nothing. None of those may appear here, and the
    /// fact that this list and the day-totals list have to stay identical is why they are
    /// written in the same words in both places.
    /// </remarks>
    public static IReadOnlyDictionary<string, long> Movements(IEnumerable<JournalEntry> entries)
    {
        var moved = new Dictionary<string, long>();

        foreach (var entry in entries)
        {
            if (entry.Rc != ResponseCode.Approved || entry.Amount == 0 || entry.AccountId.Length == 0)
            {
                continue;
            }

            var change = entry.Type switch
            {
                MessageType.DispenseAdvice => -entry.Amount,
                MessageType.DepositCommitAdvice => entry.Amount,
                _ => 0L,
            };

            if (change != 0)
            {
                moved[entry.AccountId] = moved.GetValueOrDefault(entry.AccountId) + change;
            }
        }

        return moved;
    }

    /// <summary>
    /// What the balances would be if the journal were replayed from the opening position.
    /// </summary>
    public static IReadOnlyDictionary<string, long> Expected(
        IEnumerable<Account> opening, IEnumerable<JournalEntry> entries)
    {
        var movements = Movements(entries);

        return opening.ToDictionary(
            a => a.AccountId,
            a => a.LedgerBalance + movements.GetValueOrDefault(a.AccountId));
    }

    /// <summary>
    /// Every disagreement between the balances on file and the journal, in plain Turkish.
    /// Empty means the two records tell the same story.
    /// </summary>
    public static IReadOnlyList<string> Differences(
        IEnumerable<Account> opening, IEnumerable<Account> stored, IEnumerable<JournalEntry> entries)
    {
        var expected = Expected(opening, entries);
        var found = stored.ToDictionary(a => a.AccountId, a => a);
        var problems = new List<string>();

        // A ledger line with no account on it cannot be replayed, so a record containing
        // one cannot be checked at all. That is worse than a mismatch and it is reported
        // first: a check that quietly skips the lines it does not understand is a check
        // that gets greener the more damaged the record is. In practice this means a
        // journal written before KARAR-048 - and the honest answer to an old record is to
        // archive it and start a clean day, not to half-believe it.
        var nameless = entries.Count(e =>
            e.Rc == ResponseCode.Approved
            && e.Amount != 0
            && e.AccountId.Length == 0
            && e.Type is MessageType.DispenseAdvice or MessageType.DepositCommitAdvice);

        if (nameless > 0)
        {
            problems.Add(
                $"defterde hesabı yazılmamış {nameless} para hareketi var (KARAR-048 " +
                "öncesi biçim). Bu defter bakiyelerle karşılaştırılamaz; arşivleyip temiz " +
                "bir günle başlayın: ./scripts/demo.sh --yeni-gun");
        }

        foreach (var (accountId, shouldBe) in expected.OrderBy(p => p.Key))
        {
            if (!found.TryGetValue(accountId, out var account))
            {
                problems.Add($"{accountId}: hesap dosyada yok, defterde var.");
                continue;
            }

            if (account.LedgerBalance != shouldBe)
            {
                problems.Add(
                    $"{accountId}: dosyada {account.LedgerBalance} kuruş, defterden çıkan " +
                    $"{shouldBe} kuruş (fark {account.LedgerBalance - shouldBe}).");
            }
        }

        foreach (var accountId in found.Keys.Except(expected.Keys).OrderBy(k => k))
        {
            problems.Add($"{accountId}: hesap dosyada var, açılış listesinde yok.");
        }

        // The holds are checked the same way and for the same reason. A hold is a promise,
        // and the journal knows exactly which promises were never closed (JournalReplay).
        // A balance file carrying a hold with no promise behind it is money the customer
        // cannot spend and nothing will ever give back; a promise with no hold behind it is
        // money that can be promised twice.
        var promised = JournalReplay.OpenAuthorisations(entries)
            .GroupBy(i => i.AccountId)
            .ToDictionary(g => g.Key, g => g.Sum(i => i.Amount));

        foreach (var account in found.Values.OrderBy(a => a.AccountId))
        {
            var shouldHold = promised.GetValueOrDefault(account.AccountId);

            if (account.HoldAmount != shouldHold)
            {
                problems.Add(
                    $"{account.AccountId}: dosyada {account.HoldAmount} kuruş bloke, defterde " +
                    $"kapanmamış {shouldHold} kuruşluk söz var.");
            }
        }

        return problems;
    }
}
