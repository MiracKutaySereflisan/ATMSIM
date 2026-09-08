// Copyright (c) 2026 Mirac Kutay Sereflisan. MIT lisansi - LICENSE dosyasina bakiniz.
// Scenario.cs
//
// What this file does: it reads one scenario file and turns it into something that can be
// run - and refuses anything it does not fully understand.
//
// Why the refusal matters more than the reading: a scenario file describes a FAULT. If a
// field name is misspelled and the reader shrugs, the fault is never injected, everything
// goes fine, and the scenario reports GREEN. A silently ignored field in a test harness is
// not a small bug; it is a machine that congratulates you for the failures you forgot to
// cause. So unknown members are an error (JsonUnmappedMemberHandling.Disallow), unknown
// step names are an error, and axis values outside the closed list are an error.
//
// Why the axis values are a closed list (KARAR-049): the coverage matrix is built from
// these files. An open list would let coverage be watered down by accident - invent a new
// fault name, write one scenario for it, and the matrix reports a whole new area as
// covered. A closed list makes adding a fault type a deliberate act whose cost is visible:
// every empty cell in the new row appears in the report.
//
// What this file does NOT do: run anything. It parses and validates. ScenarioRunner runs.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Atm.Audit;

/// <summary>The three coordinates of the coverage matrix. See the scenario format section 5.</summary>
public sealed record ScenarioAxis(string Islem, string Ariza, string An)
{
    /// <summary>The transactions a scenario can be about.</summary>
    public static readonly IReadOnlyList<string> Transactions =
        ["cekim", "yatirma", "bakiye", "gun-sonu"];

    /// <summary>The faults a scenario can inject.</summary>
    public static readonly IReadOnlyList<string> Faults =
    [
        "cevap-kaybi", "istek-kaybi", "hat-kopmasi", "cift-istek", "kismi-dagitim",
        "alinmayan-para", "bos-kaset", "sikisma", "host-yeniden-baslatma", "yok",
    ];

    /// <summary>The moments a fault can be aimed at.</summary>
    public static readonly IReadOnlyList<string> Moments =
    [
        "yetkilendirme", "dagitim", "bildirim", "ters-kayit", "escrow", "onay",
        "kesim", "bosta",
    ];

    /// <summary>The cell of the matrix this scenario fills.</summary>
    public override string ToString() => $"{Islem} × {Ariza} × {An}";
}

/// <summary>One step of a scenario. Which fields matter depends on <see cref="Adim"/>.</summary>
public sealed record ScenarioStep
{
    /// <summary>Which step. The names are in the scenario format section 4.</summary>
    public required string Adim { get; init; }

    public string Kart { get; init; } = "";
    public long Tutar { get; init; }
    public int Stan { get; init; }
    public string Banknotlar { get; init; } = "";
    public bool Onay { get; init; } = true;
    public string Mesaj { get; init; } = "";
    public long EnFazla { get; init; }
    public int Saniye { get; init; }
    public long Kupur { get; init; }
    public int Adet { get; init; }
}

/// <summary>What the scenario says should be true when it is over. Written BEFORE the run.</summary>
public sealed record ScenarioExpectation
{
    /// <summary>Total cash in customers' hands at the end, in kurus.</summary>
    public required long MusteriyeGiden { get; init; }

    /// <summary>Net change of the acting card's ledger balance, in kurus.</summary>
    public required long HesapFarki { get; init; }

    /// <summary>"temiz" or "ihlal".</summary>
    public required string Denetim { get; init; }

    /// <summary>Where a violation shows up: "aninda", "gun-sonu" or "yok".</summary>
    public required string Tespit { get; init; }

    /// <summary>How many unexplained differences are expected. Null means "do not check".</summary>
    public int? IhlalSayisi { get; init; }

    /// <summary>Whether the day was expected to close. Null means "no cutover in this scenario".</summary>
    public bool? GunKapandi { get; init; }
}

/// <summary>One scenario, as read from a file.</summary>
public sealed record Scenario
{
    public required string Id { get; init; }
    public required string Baslik { get; init; }
    public string Aciklama { get; init; } = "";
    public required int Seed { get; init; }
    public required ScenarioAxis Eksen { get; init; }
    public required IReadOnlyList<ScenarioStep> Adimlar { get; init; }
    public required ScenarioExpectation Beklenen { get; init; }

    /// <summary>Where this scenario was read from - so a complaint can name the file.</summary>
    [JsonIgnore]
    public string Path { get; init; } = "";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.KebabCaseLower,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Reads and validates every scenario in a folder, sorted by id.</summary>
    public static IReadOnlyList<Scenario> ReadFolder(string folder)
    {
        if (!Directory.Exists(folder))
        {
            throw new DirectoryNotFoundException($"Senaryo klasörü yok: {folder}");
        }

        var scenarios = Directory.GetFiles(folder, "*.json")
            .OrderBy(f => f)
            .Select(Read)
            .ToList();

        var duplicate = scenarios.GroupBy(s => s.Id).FirstOrDefault(g => g.Count() > 1);

        if (duplicate is not null)
        {
            throw new InvalidDataException(
                $"Aynı kimlikte iki senaryo var: {duplicate.Key}. Kimlik, raporun ve hata " +
                "kataloğunun bağlandığı addır; iki senaryo aynı adı taşıyamaz.");
        }

        return scenarios;
    }

    /// <summary>Reads and validates one scenario file.</summary>
    public static Scenario Read(string path)
    {
        Scenario scenario;

        try
        {
            scenario = JsonSerializer.Deserialize<Scenario>(File.ReadAllText(path), Options)
                ?? throw new InvalidDataException($"{path}: dosya boş.");
        }
        catch (JsonException ex)
        {
            // The message is deliberately long. A scenario file is written by a person and
            // the commonest mistake is a misspelled field - which, if it were ignored,
            // would produce a fault that never happens and a scenario that passes.
            throw new InvalidDataException(
                $"{path}: okunamadı. Tanınmayan bir alan ya da yanlış bir tip olabilir - " +
                $"bilinmeyen alanlar bilerek hata sayılıyor (the scenario format §2). " +
                $"Ayrıntı: {ex.Message}", ex);
        }

        scenario = scenario with { Path = path };
        Validate(scenario);
        return scenario;
    }

    private static void Validate(Scenario scenario)
    {
        void Refuse(string why) =>
            throw new InvalidDataException($"{scenario.Path} ({scenario.Id}): {why}");

        if (scenario.Id.Length == 0)
        {
            Refuse("kimlik boş.");
        }

        if (!ScenarioAxis.Transactions.Contains(scenario.Eksen.Islem))
        {
            Refuse($"eksen.islem '{scenario.Eksen.Islem}' listede yok. " +
                   $"Geçerli: {string.Join(", ", ScenarioAxis.Transactions)}.");
        }

        if (!ScenarioAxis.Faults.Contains(scenario.Eksen.Ariza))
        {
            Refuse($"eksen.ariza '{scenario.Eksen.Ariza}' listede yok. " +
                   $"Geçerli: {string.Join(", ", ScenarioAxis.Faults)}.");
        }

        if (!ScenarioAxis.Moments.Contains(scenario.Eksen.An))
        {
            Refuse($"eksen.an '{scenario.Eksen.An}' listede yok. " +
                   $"Geçerli: {string.Join(", ", ScenarioAxis.Moments)}.");
        }

        if (scenario.Adimlar.Count == 0)
        {
            Refuse("hiç adım yok.");
        }

        foreach (var step in scenario.Adimlar.Where(s => !ScenarioRunner.KnownSteps.Contains(s.Adim)))
        {
            Refuse($"'{step.Adim}' diye bir adım yok. " +
                   $"Geçerli adımlar: {string.Join(", ", ScenarioRunner.KnownSteps)}.");
        }

        if (scenario.Beklenen.Denetim is not ("temiz" or "ihlal"))
        {
            Refuse($"beklenen.denetim '{scenario.Beklenen.Denetim}' olamaz; 'temiz' veya 'ihlal'.");
        }

        if (scenario.Beklenen.Tespit is not ("aninda" or "gun-sonu" or "yok"))
        {
            Refuse($"beklenen.tespit '{scenario.Beklenen.Tespit}' olamaz; " +
                   "'aninda', 'gun-sonu' veya 'yok'.");
        }

        if (scenario.Beklenen.Denetim == "temiz" && scenario.Beklenen.Tespit != "yok")
        {
            Refuse("temiz bir senaryoda tespit noktası olamaz: ortada tespit edilecek bir şey yok.");
        }

        if (scenario.Beklenen.Denetim == "ihlal" && scenario.Beklenen.Tespit == "yok")
        {
            // Allowed, and deliberately allowed: an undetected violation is the worst
            // result this project can produce, and a format that could not express it
            // would be a format that hides its own bad news.
        }
    }
}
