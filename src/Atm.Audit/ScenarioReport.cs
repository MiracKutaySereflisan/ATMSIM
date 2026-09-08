// ScenarioReport.cs
//
// What this file does: it runs every scenario file and prints what happened - one line per
// scenario, then the coverage matrix, then the summary the report and the presentation are
// built on.
//
// Why the summary has three numbers rather than one: "how many passed" is the least
// interesting of them. A scenario passes when the system did what the scenario said it
// should, so a green run means the model behaved as designed. The numbers that say
// something about the SYSTEM are the other two: how many scenarios end with an unexplained
// difference, and - of those - where the difference was visible. A day with two differences
// caught at the moment and a day with two differences nobody would ever notice have the
// same arithmetic and are not the same day.
//
// What this file does NOT do: decide anything about the scenarios. It runs them and counts.

using Atm.Protocol;

namespace Atm.Audit;

/// <summary>Runs the scenario folder and prints the result.</summary>
public static class ScenarioReport
{
    /// <summary>Runs everything and returns the process exit code: 0 green, 1 red.</summary>
    public static int Run(string folder)
    {
        var scenarios = Scenario.ReadFolder(folder);

        Console.WriteLine($"ATMSIM — arıza senaryoları ({scenarios.Count} senaryo, {folder})");
        Console.WriteLine(new string('=', 78));

        if (scenarios.Count == 0)
        {
            Console.WriteLine("Hiç senaryo yok.");
            return 1;
        }

        var hardened = ScenarioRunner.RunAll(scenarios, Hardening.Full);

        foreach (var result in hardened)
        {
            Console.WriteLine(Line(result));

            foreach (var complaint in result.Complaints)
            {
                Console.WriteLine($"      ! {complaint}");
            }
        }

        Console.WriteLine();
        Console.WriteLine(CoverageMatrix.Of(scenarios).ToText());

        // The same files, the same seeds, the same steps - and every rule of the domain
        // switched off. Nothing else differs, which is the only condition under which the
        // two columns below can be read against each other (rule 5 (docs/proje-kurallari.md)).
        var naive = ScenarioRunner.RunAll(scenarios, Hardening.None);

        Console.WriteLine();
        Console.WriteLine("Naif akış — aynı senaryolar, alan kuralları kapalı:");
        Console.WriteLine(new string('-', 78));

        foreach (var (n, h) in naive.Zip(hardened).Where(p => p.First.Denetim == "ihlal"))
        {
            Console.WriteLine(
                $"  {n.Scenario.Id,-18} ihlal/{n.Tespit,-9} " +
                $"müşteride {n.MusteriyeGiden,7} (sert: {h.MusteriyeGiden}) · " +
                $"hesap {n.HesapFarki,8} (sert: {h.HesapFarki})");
        }

        Console.WriteLine();
        Console.WriteLine(Comparison(hardened, naive));
        Console.WriteLine();
        Console.WriteLine(PerRule(scenarios, hardened.Count, hardened));
        Console.WriteLine();
        Console.WriteLine(Summary(hardened));

        var failed = hardened.Count(r => !r.Passed);
        Console.WriteLine();

        if (failed == 0)
        {
            Console.WriteLine("SONUÇ: her senaryo, kendisinden beklenen şeyi yaptı.");
            return 0;
        }

        Console.WriteLine(
            $"SONUÇ: {failed} senaryo beklenenden farklı bitti. " +
            "Beklentiyi sonuca göre güncellemek yasak (docs/scenarios.md §7).");

        return 1;
    }

    /// <summary>
    /// The comparison the report and the presentation are built on.
    /// </summary>
    /// <remarks>
    /// Four rows, and the last one is the one to read out loud. A difference caught at the
    /// moment costs a phone call; a difference caught at the end of the day costs an
    /// investigation; a difference nobody catches is money that left the bank and was never
    /// missed. The three are not degrees of the same problem - they are different problems
    /// with the same arithmetic behind them.
    /// </remarks>
    private static string Comparison(
        IReadOnlyList<ScenarioResult> hardened, IReadOnlyList<ScenarioResult> naive)
    {
        static int Violations(IReadOnlyList<ScenarioResult> r) => r.Count(x => x.Denetim == "ihlal");
        static int Unseen(IReadOnlyList<ScenarioResult> r) =>
            r.Count(x => x.Denetim == "ihlal" && x.Tespit == "yok");
        static int AtDayEnd(IReadOnlyList<ScenarioResult> r) => r.Count(x => x.Tespit == "gun-sonu");
        var rows = new[]
        {
            ("açıklanamayan farkla biten senaryo", Violations(naive), Violations(hardened)),
            ("  bunlardan gün sonunda görülen", AtDayEnd(naive), AtDayEnd(hardened)),
            ("  bunlardan hiç görülmeyen", Unseen(naive), Unseen(hardened)),
        };

        var lines = new List<string>
        {
            $"KARŞILAŞTIRMA ({hardened.Count} senaryo, aynı seed'ler, aynı adımlar)",
            "",
            $"  {"",-36}{"naif",8}{"sertleştirilmiş",18}",
        };

        lines.AddRange(rows.Select(r => $"  {r.Item1,-36}{r.Item2,8}{r.Item3,18}"));

        lines.Add("");
        lines.Add(
            "  Sayılan şey senaryo adedidir, para değil: bir senaryoda kaybolan tutarı " +
            "tek bir sayıya");
        lines.Add(
            "  indirmek, aynı parayı birden fazla kontrolün adlandırdığı durumlarda " +
            "yanıltır (kural §9 (docs/proje-kurallari.md)).");

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// One rule off at a time: which of the seven actually earns its keep, and where.
    /// </summary>
    /// <remarks>
    /// The all-off comparison above answers "is any of this worth doing". It cannot answer
    /// "which part", and it is dominated by whichever rule fails on the most scenarios -
    /// switch off repeat immunity alone and nearly every scenario breaks, which makes the
    /// other six look free. This table is the honest version: each rule is removed on its
    /// own, everything else left in place, and the number beside it is what that ONE rule
    /// is holding up.
    ///
    /// A zero here is not a rule that does nothing. It is a rule that no scenario in this
    /// folder reaches - which is a statement about the coverage above, not about the rule,
    /// and it is written that way in the output.
    /// </remarks>
    private static string PerRule(
        IReadOnlyList<Scenario> scenarios, int total, IReadOnlyList<ScenarioResult> hardenedByRun)
    {
        (string Rule, string Fact, Hardening Off)[] rules =
        [
            ("tekrar bağışıklığı", "§4.5", Hardening.Full with { RepeatImmunity = false }),
            ("zaman aşımı ters kayıt üretir", "§4.1", Hardening.Full with { TimeoutMakesReversal = false }),
            ("onay gelene kadar tekrar dene", "§4.2", Hardening.Full with { RetryUntilAcknowledged = false }),
            ("kısmi dağıtım olduğu gibi bildirilir", "§4.3", Hardening.Full with { ReportPartialDispense = false }),
            ("geri alınan para verilmiş sayılmaz", "§4.4", Hardening.Full with { RetractIsNotHandedOver = false }),
            ("kupür kontrolü yetkilendirmeden önce", "§4.6", Hardening.Full with { NoteCheckBeforeAuthorisation = false }),
            ("yalnızca kasete gireni hesaba yaz", "§4.7", Hardening.Full with { CreditWhatReachedADrawer = false }),
        ];

        var lines = new List<string>
        {
            $"KURAL KURAL — her biri tek başına kapatıldığında ({total} senaryo)",
            "",
        };

        foreach (var (rule, fact, off) in rules)
        {
            var results = ScenarioRunner.RunAll(scenarios, off);
            var broken = results.Where(r => r.Denetim == "ihlal").ToList();
            var unseen = broken.Count(r => r.Tespit == "yok");

            // Whether the rule was REACHED at all is a different question from whether
            // removing it lost money, and the two must not be reported as one number. A
            // rule can change what the machine does - an authorisation asked for and then
            // taken back - without the books ever going out. Saying "no scenario tests
            // this" when a scenario does test it would be a lie about our own coverage.
            var touched = results.Zip(hardenedByRun)
                .Count(pair => !SameOutcome(pair.First, pair.Second));

            var note = broken.Count > 0
                ? $"  [{string.Join(", ", broken.Take(3).Select(r => r.Scenario.Id))}" +
                  (broken.Count > 3 ? ", …]" : "]") +
                  (unseen > 0 ? $" — {unseen} tanesi hiç fark edilmiyor" : "")
                : touched > 0
                    ? $"  dengesizlik yok; {touched} senaryonun davranışı yine de değişiyor"
                    : "  bu senaryo kümesinde gözlenebilir hiçbir farkı yok";

            lines.Add($"  {fact} {rule,-38} {broken.Count,3}/{total}{note}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>Whether two runs of the same scenario ended in the same observable place.</summary>
    /// <remarks>
    /// Only what an outsider could see: cash, ledger, the checker's verdict, the detection
    /// point, whether the day closed. Journal lines are deliberately not compared - a note
    /// that reads differently is not a different outcome, and comparing them would report
    /// every rule as "reached" whatever it did.
    /// </remarks>
    private static bool SameOutcome(ScenarioResult a, ScenarioResult b) =>
        a.MusteriyeGiden == b.MusteriyeGiden
        && a.HesapFarki == b.HesapFarki
        && a.Denetim == b.Denetim
        && a.Tespit == b.Tespit
        && a.GunKapandi == b.GunKapandi;

    private static string Summary(IReadOnlyList<ScenarioResult> results)
    {
        var failed = results.Count(r => !r.Passed);

        return string.Join(Environment.NewLine,
        [
            "Özet (sertleştirilmiş akış):",
            $"  senaryo            {results.Count}",
            $"  beklendiği gibi    {results.Count - failed}",
            $"  beklenmedik        {failed}",
            $"  açıklanamayan fark {results.Count(r => r.Denetim == "ihlal")}",
        ]);
    }

    private static string Line(ScenarioResult result)
    {
        var mark = result.Passed ? "TAMAM" : "FARKLI";
        var detection = result.Denetim == "temiz" ? "temiz" : $"ihlal/{result.Tespit}";

        return $"{mark,-7} {result.Scenario.Id,-18} {detection,-14} " +
               $"müşteride {result.MusteriyeGiden,7} · hesap {result.HesapFarki,8}  " +
               result.Scenario.Baslik;
    }
}
