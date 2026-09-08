// Copyright (c) 2026 Mirac Kutay Sereflisan. MIT lisansi - LICENSE dosyasina bakiniz.
// Program.cs (Atm.Terminal)
//
// What this file does: it starts the machine the customer stands in front of. Three
// things are wired together here and nowhere else:
//
//   HostLink      - the persistent TCP connection to the host
//   TerminalFlow  - everything that decides
//   ScreenServer  - the page the browser loads and the link that carries it
//
// Why the wiring is in the entry point: each of those three can be built with
// something else in its place - a fake line, a test host, no browser at all - which is
// what every test in this repository does. The one place that must name the real
// socket, the real clock and the real file is this one.
//
// The card reader is simulated: this process is told which card is in the wallet, and
// hands that number over when the panel reports a card going in. A real reader would
// read a stripe or a chip; where the number comes from is the only difference.

using Atm.Protocol;
using Atm.Terminal;

var hostPort = args.Length > 0 && int.TryParse(args[0], out var chosen) ? chosen : 9099;
var screenPort = args.Length > 1 && int.TryParse(args[1], out var chosenScreen) ? chosenScreen : 8080;
var card = args.Length > 2 ? args[2] : "4111111111111111";

var clock = SystemClock.Instance;
var link = new HostLink("127.0.0.1", hostPort);

Console.WriteLine($"Atm.Terminal - protokol {ProtocolVersion.Current}");
Console.WriteLine($"Host: 127.0.0.1:{hostPort} - kart: {MessageCodec.MaskPan(card)}");

// TerminalClient is handed a way to MAKE a connection rather than a connection, so it
// can replace a dead line with a new one on its own (KARAR-021). Here that way is a
// real socket; in every test it is the in-memory line.
using var client = new TerminalClient(() => link.Connect(), clock);

// The first connection is ATTEMPTED, not required. A machine that refuses to start
// because the centre is not answering is a machine that cannot be switched on before the
// centre - and on the morning of a demonstration that is exactly the order things happen
// in. A real ATM in this position shows "temporarily out of service" and keeps trying;
// it does not fall over.
//
// Found by rehearsing the demo (Faz 5a): with no host listening, this line threw and the
// terminal died with a stack trace where a sentence belonged.
try
{
    client.Connect();
    Console.WriteLine("Host baglantisi kuruldu.");
}
catch (Exception ex) when (ex is System.Net.Sockets.SocketException or IOException)
{
    Console.WriteLine($"Host baglantisi yok ({ex.GetType().Name}). Ekran acilacak;");
    Console.WriteLine("hat gelince ilk istekte kendiliginden baglanilir.");
}

// The machine's own things: what it holds, what its hands do, what it owes the host and
// what it writes down. The last two reach disk (KARAR-013, KARAR-034): a terminal that
// forgot its queue would leave a hold standing against somebody's account for ever, and a
// terminal that forgot its journal would leave cash it handed over in nobody's record.
var dataDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "_veri");
Directory.CreateDirectory(dataDir);

var cassettes = CassetteSet.Standard();
var dispenser = new FakeCashDispenser(cassettes);

// The demonstration switches (KARAR-053). They live here, at the edge, because this is
// where the real line and the real hands are - and nowhere in the flow does anything ask
// whether it is in a demonstration.
var demo = new DemoFaults();

// The line, with the operator's switches in front of it. "hat-kes" answers nothing at all;
// "cevap-yut" lets the request through, lets the host do the work, and throws the answer
// away. The second is the one worth showing: it is rule 4.1 in the flesh,
// and the machine cannot tell it apart from the first.
//
// A cut line answers immediately rather than after the full timeout. A dead socket answers
// immediately in life too, and thirty seconds of nothing in front of an audience teaches
// nobody anything. Written down in reports/assumptions.md.
Envelope? Ask(Envelope request, TimeSpan timeout)
{
    if (demo.LineIsDown)
    {
        return null;
    }

    var answer = client.Exchange(request, timeout);
    return demo.AnswersAreSwallowed ? null : answer;
}
var pending = new FilePendingHostMessages(Path.Combine(dataDir, "terminal-pending.json"));
var journal = new FileTerminalJournal(Path.Combine(dataDir, "terminal-journal.jsonl"));

Console.WriteLine($"Kasetler: {cassettes.TotalNotes} banknot, {TerminalFlow.Money(cassettes.TotalValue)}");
Console.WriteLine($"Bekleyen mesaj: {pending.Count}");

var withdrawals = new WithdrawalFlow(
    ask: Ask,
    dispenser: dispenser,
    cassettes: cassettes,
    clock: clock,
    pending: pending,

    // In the demo the machine's mouth is watched by the screen, not by this function:
    // TerminalFlow drives Begin and Complete around the real event. This callback is only
    // reached by the scripted form of a withdrawal, which the demo never uses.
    customerTakesTheCash: () => false,
    journal: journal);

// The deposit side. It shares the dispenser object with the withdrawal flow on purpose:
// on a recycling machine the drawer a hundred-lira note is taken FROM and the drawer it
// is put INTO are the same drawer, and modelling them as two objects would let the
// machine hand out money it never took in.
var deposits = new DepositFlow(
    ask: Ask,
    acceptor: dispenser,
    clock: clock,
    pending: pending,
    journal: journal);

// Which trace numbers this machine has already used TODAY. Read back from its own
// journal rather than kept in a counter file: the journal is the record, and a counter
// that can disagree with the record is a second, quieter record (KARAR-047, A-03).
var usedStan = journal.Entries
    .Where(e => e.Key.BizDate == withdrawals.Day.Current)
    .Select(e => e.Key.Stan)
    .DefaultIfEmpty(0)
    .Max();

if (usedStan > 0)
{
    Console.WriteLine($"Bugun kullanilmis islem numarasi: {usedStan} - sayac oradan devam ediyor.");
}

var flow = new TerminalFlow(
    ask: Ask,
    clock: clock,
    readCard: () => card,
    withdrawals: withdrawals,
    deposits: deposits,
    demo: demo,
    startingStan: usedStan,
    depositDenominations: cassettes.AcceptedForDeposit);

var pagePath = Path.Combine(AppContext.BaseDirectory, "wwwroot", "index.html");

using var screen = new ScreenServer(
    pagePath,
    happened =>
    {
        // One line of tracing, because a demo where nothing is visible in the terminal
        // window is a demo where a broken link looks the same as a quiet customer. Ticks
        // are not traced: one line a second would bury everything else.
        if (happened.Event != ScreenEventName.Tick)
        {
            Console.WriteLine($"  ekran -> {happened.Event}:{happened.Value}");
        }

        ScreenView? next;

        try
        {
            next = flow.Handle(happened);
        }
        catch (ArgumentException ex)
        {
            // A word the machine does not know, arriving from the screen. The flow refuses
            // it by throwing, and inside a test that is exactly right - an unknown command
            // is a bug and should be loud. Out here it must not be fatal: this process is
            // the running ATM, and a stray or mistyped message from a browser tab is an
            // operating condition, not a reason for the machine to die in front of an
            // audience. It is reported at full volume and the machine stays up, showing
            // whatever it was showing.
            Console.Error.WriteLine($"  EKRAN HATASI -> {happened.Event}:{happened.Value} - {ex.Message}");
            return null;
        }

        // The hands are set here rather than inside the flow, for the same reason the line
        // is: a dispenser that jams is a fact about the machine, not a decision about a
        // transaction.
        if (happened.Event == ScreenEventName.Demo)
        {
            dispenser.Fault = demo.PresentAtMost switch
            {
                null => DispenserFault.None,
                0 => DispenserFault.Jam(),
                long limit => DispenserFault.PresentAtMostThis(limit),
            };

            Console.WriteLine($"  DEMO -> {(demo.Any ? demo.Summary : "bütün arızalar kapalı")}");
        }

        if (happened.Event == ScreenEventName.Tick)
        {
            // Whatever this machine still owes the host gets another chance, on its own
            // schedule (10, 30, 60, 300 seconds). This is where a late reversal actually
            // goes out in the demo: the line comes back and the queue empties itself.
            var sent = withdrawals.SendPending();

            if (sent > 0)
            {
                Console.WriteLine($"  kuyruk -> {sent} mesaj tekrar gonderildi");
            }
        }

        if (next is not null)
        {
            Console.WriteLine($"  ekran <- {next.Screen}");
        }

        return next;
    },
    port: screenPort);

try
{
    screen.Start();
}
catch (System.Net.Sockets.SocketException ex) when (ex.SocketErrorCode == System.Net.Sockets.SocketError.AddressAlreadyInUse)
{
    // Same reasoning as in Atm.Host/Program.cs: an occupied port is an operating
    // condition with a one-line answer, not an invariant worth a stack trace.
    Console.Error.WriteLine($"Ekran portu {screenPort} zaten kullaniliyor - terminal baslatilamadi.");
    Console.Error.WriteLine("Su komut onceki calistirmadan kalanlari temizler:");
    Console.Error.WriteLine("  ./scripts/demo.sh --kapat");
    return 2;
}

Console.WriteLine();
Console.WriteLine($"ATM ekrani hazir:  {screen.Address}");
Console.WriteLine("Bu adresi tarayicida acin. Durdurmak icin Ctrl+C.");
Console.WriteLine();

using var stopping = new CancellationTokenSource();

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    stopping.Cancel();
};

screen.ServeForever(stopping.Token);

// The normal ending. Written out because the port-clash branch above returns 2, and a
// program with one explicit exit code must give every path one.
return 0;
