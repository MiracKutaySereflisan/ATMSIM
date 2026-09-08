// IAccountStore.cs
//
// What this file does: it is the door between the transaction flow and wherever
// accounts happen to be kept.
//
// Why this exists (KARAR-013): the flow must never contain the sentence "read this
// out of a file" or "run this query". If it did, changing where accounts live would
// mean opening the withdrawal logic, the reversal logic and the reconciliation - the
// three places in this project that must not be touched for an unrelated reason.
// Behind this interface today there is a dictionary in memory. Behind it tomorrow
// there could be PostgreSQL (KARAR-015), and the flow would not notice.
//
// What this interface deliberately does NOT offer: a way to list every account, or a
// way to change a balance by an arbitrary amount. Balances move through the ledger,
// one recorded movement at a time, because a balance that can be set directly is a
// balance that can be set wrongly with nothing left to explain how.

namespace Atm.Host;

/// <summary>Where accounts are kept, seen from the transaction flow's side.</summary>
public interface IAccountStore
{
    /// <summary>Finds the account a card reaches, or null when the card is unknown.</summary>
    Account? FindByPan(string pan);

    /// <summary>
    /// Finds an account by its own identity, or null when there is no such account.
    /// </summary>
    /// <remarks>
    /// KARAR-048. Everything the host remembers between two messages - an open
    /// authorisation, an open deposit - names the ACCOUNT, because that is what a ledger
    /// moves and it is the only identity that survives being written to a journal where
    /// the card is masked.
    /// </remarks>
    Account? FindById(string accountId);

    /// <summary>Replaces an account with an updated copy. Used by later phases.</summary>
    void Save(Account account);
}

/// <summary>
/// The accounts of docs/model.md section 4.1, held in memory. Every one of them is
/// invented and Luhn-valid; none corresponds to a real card, account or person.
/// </summary>
public sealed class InMemoryAccountStore : IAccountStore
{
    private readonly Dictionary<string, Account> _byPan;

    public InMemoryAccountStore(IEnumerable<Account> accounts) =>
        _byPan = accounts.ToDictionary(a => a.Pan);

    /// <summary>
    /// The accounts as the world starts: the balances docs/model.md section 4.1 fixes.
    /// </summary>
    /// <remarks>
    /// This list is the OPENING position, and it is used twice: to create a machine that
    /// has never run, and - at start-up - as the point the journal is replayed from when
    /// the stored balances are checked (KARAR-047). Those two uses have to read the same
    /// numbers, which is why the list is here rather than written out twice.
    /// </remarks>
    public static readonly IReadOnlyList<Account> Opening =
    [
        // Normal flow.
        new Account { AccountId = "TR-DEMO-001", Pan = "4111111111111111", LedgerBalance = 250_000 },
        // 45,00 TL - too little to withdraw, and not a multiple of any note.
        new Account { AccountId = "TR-DEMO-002", Pan = "4222222222222220", LedgerBalance = 4_500 },
        // Rich enough to empty a cassette.
        new Account { AccountId = "TR-DEMO-003", Pan = "4333333333333339", LedgerBalance = 10_000_000 },
    ];

    /// <summary>The three demo accounts, with the balances the model document fixes.</summary>
    public static InMemoryAccountStore WithDemoAccounts() => new(Opening);

    public Account? FindByPan(string pan) =>
        _byPan.TryGetValue(pan, out var account) ? account : null;

    public Account? FindById(string accountId) =>
        _byPan.Values.FirstOrDefault(a => a.AccountId == accountId);

    public void Save(Account account) => _byPan[account.Pan] = account;
}
