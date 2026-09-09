// Copyright (c) 2026 Mirac Kutay Sereflisan. MIT lisansi - LICENSE dosyasina bakiniz.
// Program.cs (Atm.Audit)
//
// What this file does: it plays one whole day through the simulator and prints whether
// the money adds up. This is stage 2g's deliverable - the central claim of the project
// turned from a sentence into a command anybody can run.
//
// Why a fixed day rather than a random one: rule 5 makes determinism
// absolute. The day below is built from a seed, and the same seed produces the same
// twelve withdrawals, the same faults at the same moments and the same journals. A run
// that cannot be repeated cannot be shown to anybody, because the first question asked
// about a surprising number is "run it again".
//
// The faults are deliberately ordinary: a dispenser that hands over less than it was
// asked for, a customer who walks away, an answer that never comes back. None of them is
// exotic and every one of them happens to real machines every day. If the books only
// balance when nothing goes wrong, they do not balance.
//
// What this program is NOT: the scenario runner. That arrives in stage 4 and reads its
// failures from scenarios/*.json instead of from the switch below (rule 5:
// failure scenarios live in data, not in code). This file is its first, small form, and
// it exists now because the checker it drives had to be provable before the scenarios
// that will lean on it are written.

using Atm.Audit;
using Atm.Protocol;
using Atm.Terminal;

// Two deliverables, one binary. "senaryolar" runs the scenario files of stage 4;
// anything else runs the fixed conservation day of stage 2g. They share the simulator,
// the checker and the day harness, and a second executable would have shared all three
// and differed only in its Main - which is not a layer that solves a problem
// (rule 6a). The two scripts in scripts/ are what a person sees.
if (args.Length > 0 && args[0] == "senaryolar")
{
    return ScenarioReport.Run(args.Length > 1 ? args[1] : "scenarios");
}

var seed = args.Length > 0 && int.TryParse(args[0], out var given) ? given : 20260825;
var dice = new Random(seed);

Console.WriteLine($"ATMSIM — para korunumu denetimi (seed {seed})");
Console.WriteLine(new string('=', 60));

var day = new SimulatedDay();
const string card = "4111111111111111";

for (var i = 0; i < 12; i++)
{
    // One of four things goes wrong with the hands, chosen by the seed.
    day.Dispenser.Fault = dice.Next(4) switch
    {
        1 => DispenserFault.PresentAtMostThis(20_000),
        2 => DispenserFault.Jam(),
        _ => DispenserFault.None,
    };

    // Sometimes nobody picks the notes up.
    day.CustomerTakesCash = dice.Next(3) != 0;

    // Sometimes the answer to one kind of message goes missing on the way back.
    day.LoseAnswersForType = dice.Next(4) switch
    {
        1 => MessageType.WithdrawalAuthRequest,
        2 => MessageType.DispenseAdvice,
        _ => "",
    };

    var result = day.Flow.Withdraw(card, 35_000, stan: 200 + i);

    Console.WriteLine(
        $"{i + 1,2}. çekim: {result.Outcome,-24} müşteride {result.CashToCustomer,6} kuruş" +
        (day.LoseAnswersForType.Length > 0 ? $"  [kayıp cevap: {day.LoseAnswersForType}]" : ""));

    // The line comes back and the machine works off whatever it still owes the host. This
    // is the part a demonstration skips and a real day cannot: the queue is what turns an
    // unexplained difference back into an explained one.
    day.LineComesBack();
    day.Clock.Advance(TimeSpan.FromSeconds(300));
    day.Flow.SendPending();
}

// Everything that was still owed has now been said. Anything left is a finding.
day.Clock.Advance(TimeSpan.FromSeconds(300));
day.Flow.SendPending();

Console.WriteLine();
Console.WriteLine("Nakit nerede:");

var position = day.Dispenser.Position;
Console.WriteLine($"  kasetlerde     {position.InCassettes,9} kuruş");
Console.WriteLine($"  ağızda         {position.AtTheMouth,9} kuruş");
Console.WriteLine($"  müşterilerde   {position.WithCustomers,9} kuruş");
Console.WriteLine($"  geri alınan    {position.Retracted,9} kuruş");
Console.WriteLine($"  toplam         {position.Total,9} kuruş  (gün başı {day.StartingCash})");

Console.WriteLine();
Console.WriteLine($"Host defteri: {day.Host.Journal.Entries.Count} satır · " +
                  $"terminal günlüğü: {day.Flow.Journal.Entries.Count} satır · " +
                  $"kapanmamış yetkilendirme: {day.Host.OpenAuthorisationCount} · " +
                  $"beklenmeyen ters kayıt: {day.Host.UnexpectedReversalCount}");

Console.WriteLine();

var report = ConservationChecker.Check(day.Snapshot(AuditMoment.EndOfDay));
Console.WriteLine(report.ToText());

// The day is now actually closed - the two machines compare their own totals over the
// wire and only agree if they match (the protocol spec section 4.7). This is a different
// question from the one the checker just answered, and both have to be asked. The checker
// looks at both records at once, which no machine in a real network can do; the cutover is
// what the two sides can prove to each other with only their own books in hand.
Console.WriteLine();
Console.WriteLine("Gün sonu kesimi:");

var closing = day.Day.Current;
var cutover = day.Cutover.Close(stan: 999);

Console.WriteLine($"  kapatılan gün  {closing}");
Console.WriteLine($"  terminal       {cutover.Mine.Withdrawals,9} çekim · " +
                  $"{cutover.Mine.Deposits} yatırma · {cutover.Mine.Count} işlem");
Console.WriteLine(cutover.Theirs is null
    ? "  host           cevap yok"
    : $"  host           {cutover.Theirs.Withdrawals,9} çekim · " +
      $"{cutover.Theirs.Deposits} yatırma · {cutover.Theirs.Count} işlem");
Console.WriteLine(cutover.Closed
    ? $"  sonuç          gün kapandı, fark yok; yeni gün {day.Day.Current}"
    : $"  sonuç          GÜN KAPANMADI — {cutover.Reason} (fark {cutover.Difference} kuruş)");

Console.WriteLine();

if (report.IsClean && cutover.Closed)
{
    Console.WriteLine("SONUÇ: para korunumu tuttu, gün sıfır farkla kapandı.");
    return 0;
}

if (!report.IsClean)
{
    Console.WriteLine($"SONUÇ: {report.Violations.Count()} açıklanamayan fark var.");
    return 1;
}

Console.WriteLine("SONUÇ: kayıtlar tutuyor ama gün kapanmadı — yukarıdaki sebebe bakın.");
return 1;
