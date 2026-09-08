// Copyright (c) 2026 Mirac Kutay Sereflisan. MIT lisansi - LICENSE dosyasina bakiniz.
// FileAccountStore.cs
//
// What this file does: it keeps the accounts in a file, so that a host which is stopped
// and started again does not hand every customer their morning balance back.
//
// Why this was left until now: until Phase 2 the accounts never changed, so losing them
// cost nothing. From the moment a withdrawal moves a ledger, a restart that resets the
// balances is a machine that PRINTS MONEY - every restart returns whatever was spent. It
// was written down as an open item rather than quietly tolerated, and this file closes it.
//
// The important decision is NOT that this file exists. It is what this file IS
// (KARAR-047): a SNAPSHOT, not the truth. The truth is the journal, which is append-only
// and never rewritten. This file is a convenience so the host does not have to replay its
// whole history on every start - and because it is only a convenience, losing it is
// survivable and a disagreement between it and the journal is a reason to STOP.
//
// Why a whole-file rewrite here when the journal appends: a balance is a current value,
// not a history; there is nothing to append to. The rewrite is made safe the only way a
// rewrite can be - written beside the real file and then moved onto it, so that a crash
// half way leaves either the old file or the new one and never half of either.
//
// What this file does NOT do: let anybody set a balance. Balances arrive here only as the
// result of a movement the flow already recorded (IAccountStore's comment says why).
//
// One thing that IS written here in full and is masked everywhere else: the card number.
// The store has to find an account by the card the customer inserted, and a masked number
// cannot be looked up. In a real bank that lookup goes through an encrypted or tokenised
// store; encryption is out of scope here and declared as a known gap
// (reports/assumptions.md). The card numbers in this file are invented, Luhn-valid and
// correspond to nothing real - which is exactly why this simplification is affordable HERE
// and would not be affordable anywhere else.

using System.Text;
using System.Text.Json;

namespace Atm.Host;

/// <summary>The accounts, kept in a file. A snapshot of the journal's truth (KARAR-047).</summary>
public sealed class FileAccountStore : IAccountStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    /// <summary>UTF-8 with no byte-order mark - the record exists to be read by others.</summary>
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly string _path;
    private readonly Dictionary<string, Account> _byPan = [];

    /// <summary>
    /// Opens the file at this path. If there is nothing there yet, the opening position
    /// is written out; if there is, it is read back and the opening position is ignored.
    /// </summary>
    public FileAccountStore(string path, IEnumerable<Account> opening)
    {
        _path = path;

        if (File.Exists(path))
        {
            foreach (var line in File.ReadAllLines(path, Utf8NoBom))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var account = JsonSerializer.Deserialize<Account>(line, Options)
                    ?? throw new InvalidDataException(
                        $"{path}: bir hesap satırı okunamadı. Satır: {line}");

                _byPan[account.Pan] = account;
            }

            return;
        }

        foreach (var account in opening)
        {
            _byPan[account.Pan] = account;
        }

        Write();
    }

    /// <summary>Every account, for the start-up check. Not on the interface, deliberately.</summary>
    /// <remarks>
    /// IAccountStore offers no way to list accounts, because a transaction flow has no
    /// business walking the customer base. The start-up check is not a transaction flow -
    /// it runs once, before the port is open, and it needs every balance to compare them
    /// against the journal.
    /// </remarks>
    public IReadOnlyList<Account> All => [.. _byPan.Values.OrderBy(a => a.AccountId)];

    public Account? FindByPan(string pan) =>
        _byPan.TryGetValue(pan, out var account) ? account : null;

    public Account? FindById(string accountId) =>
        _byPan.Values.FirstOrDefault(a => a.AccountId == accountId);

    public void Save(Account account)
    {
        _byPan[account.Pan] = account;
        Write();
    }

    /// <summary>
    /// Writes the whole snapshot beside the real file and then moves it into place.
    /// </summary>
    /// <remarks>
    /// The move is the point. Writing straight over the file means that a process killed
    /// half way through leaves a file that is half old and half new - and a balance file
    /// like that is worse than no balance file, because nothing about it looks wrong. A
    /// move replaces the whole thing at once or not at all.
    /// </remarks>
    private void Write()
    {
        var temporary = _path + ".tmp";

        File.WriteAllLines(temporary,
            _byPan.Values.OrderBy(a => a.AccountId)
                .Select(a => JsonSerializer.Serialize(a, Options)),
            Utf8NoBom);

        File.Move(temporary, _path, overwrite: true);
    }
}
