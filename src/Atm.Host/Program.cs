// Copyright (c) 2026 Mirac Kutay Sereflisan. MIT lisansi - LICENSE dosyasina bakiniz.
// Program.cs (Atm.Host)
//
// What this file does: it starts the bank side. It opens a TCP port, waits for the
// terminal, and serves it for as long as the line lasts. When the line ends it goes
// back to waiting, because a terminal that reconnects is the normal case, not a fault.
//
// Why the process is this thin: everything that decides anything is in HostService,
// which knows nothing about sockets. This file exists to hold a port open and to
// print enough that a person watching a demo can see what is happening.
//
// The account data is the demo data from the domain model section 4.1 - three invented
// accounts and three invented card numbers, corresponding to nothing real.
//
// Both the record and the accounts go to files rather than to memory (KARAR-013,
// KARAR-047), so that a host which is stopped and started again knows both what it
// answered and what everybody's balance is. Before stage 3f the balances did not survive
// a restart, which meant a restart handed every customer their morning balance back -
// silently, and looking perfect on screen.
//
// The check between the two files runs BEFORE the port opens. The balance file is a
// snapshot; the journal is the truth; if they disagree the host does not start. A host
// that started anyway would be serving a balance nobody can account for, and the first
// person to find out would be a customer.

using Atm.Host;
using Atm.Protocol;

var port = args.Length > 0 && int.TryParse(args[0], out var chosen) ? chosen : 9099;

var clock = SystemClock.Instance;

var dataFolder = Path.Combine(Directory.GetCurrentDirectory(), "_veri");
Directory.CreateDirectory(dataFolder);
var journalPath = Path.Combine(dataFolder, "host-journal.jsonl");
var accountsPath = Path.Combine(dataFolder, "host-accounts.jsonl");

var journal = new FileJournal(journalPath);
var accounts = new FileAccountStore(accountsPath, InMemoryAccountStore.Opening);

var problems = LedgerRebuild.Differences(
    InMemoryAccountStore.Opening, accounts.All, journal.Entries);

if (problems.Count > 0)
{
    // A violated invariant fails loudly with everything it knows (instruction 6b). This
    // one is not a running condition and there is no "carry on anyway": the two records of
    // the same money disagree, and only a person can decide which one is right.
    Console.Error.WriteLine("HESAP DOSYASI DEFTERLE TUTMUYOR - host baslatilmadi.");
    Console.Error.WriteLine($"  hesap dosyasi: {accountsPath}");
    Console.Error.WriteLine($"  defter       : {journalPath} ({journal.Entries.Count} satir)");

    foreach (var problem in problems)
    {
        Console.Error.WriteLine("  - " + problem);
    }

    Console.Error.WriteLine();
    Console.Error.WriteLine("Defter dogru kabul edilir (KARAR-047). Hesap dosyasi elle");
    Console.Error.WriteLine("duzeltilmeden ya da silinip defterden yeniden uretilmeden");
    Console.Error.WriteLine("host acilmaz.");
    return 4;
}

var service = new HostService(accounts, PinVerifier.WithDemoCards(), journal, clock);
using var server = new HostServer(service, port);

try
{
    server.Start();
}
catch (System.Net.Sockets.SocketException ex) when (ex.SocketErrorCode == System.Net.Sockets.SocketError.AddressAlreadyInUse)
{
    // A port clash is an operating condition, not a broken invariant, and the two deserve
    // opposite treatment. A violated invariant must fail loudly with everything it knows,
    // because somebody has to debug it (rule 6b). "Somebody else is already on
    // this port" needs no debugging - it needs one sentence and a way out. Printing a stack
    // trace here would mean a wall of English exception text in front of a user for a
    // problem whose whole answer fits on two lines.
    Console.Error.WriteLine($"Port {port} zaten kullaniliyor - host baslatilamadi.");
    Console.Error.WriteLine("Muhtemelen onceki bir calistirmadan kalmis. Su komut temizler:");
    Console.Error.WriteLine("  ./scripts/demo.sh --kapat");
    return 2;
}

Console.WriteLine($"Atm.Host - protokol {ProtocolVersion.Current} - 127.0.0.1:{server.Port} dinleniyor.");
Console.WriteLine($"Defter: {journalPath} ({service.Journal.Entries.Count} satir okundu).");
Console.WriteLine($"Hesaplar: {accountsPath} - defterle tutuyor.");

if (service.OpenAuthorisationCount > 0 || service.OpenDeposits.Count > 0)
{
    Console.WriteLine(
        $"Kapanmamis is: {service.OpenAuthorisationCount} yetkilendirme, " +
        $"{service.OpenDeposits.Count} yatirma - defterden geri kuruldu.");
}
Console.WriteLine("Durdurmak icin Ctrl+C.");

using var stopping = new CancellationTokenSource();

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    stopping.Cancel();
};

while (!stopping.IsCancellationRequested)
{
    try
    {
        var reason = server.ServeOne();
        Console.WriteLine($"Terminal ayrildi ({reason}). Toplam baglanti: {server.ConnectionCount}.");
    }
    catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException)
    {
        // A line that died mid-message. The terminal will come back; the host does
        // not stop because one connection ended badly.
        Console.WriteLine($"Hat koptu: {ex.GetType().Name}.");
    }
}

// The normal ending. Written out because the port-clash branch above returns 2, and a
// program with one explicit exit code must give every path one.
return 0;
