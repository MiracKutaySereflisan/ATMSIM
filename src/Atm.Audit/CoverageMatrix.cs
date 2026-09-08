// CoverageMatrix.cs
//
// What this file does: it works out which cells of the "transaction x fault x moment"
// matrix the scenario files actually cover, and names the ones they do not.
//
// Why coverage needs a denominator that is not 4 x 10 x 8 = 320: most of those 320 cells
// do not exist. There is no such thing as a partial dispense during a balance enquiry, and
// no such thing as an escrow moment in a withdrawal. Reporting 15 out of 320 would be
// arithmetically honest and practically useless - a number that cannot go up is a number
// nobody reads.
//
// But narrowing the denominator is also the easiest way to cheat, and rule 9 (docs/proje-kurallari.md)
// says to assume somebody will ask. So the narrowing is not done cell by cell and it is not
// done by judgement at report time. It is done by two small tables below - which moments a
// TRANSACTION has, and which moments a FAULT can strike at - and a cell exists when both
// tables allow it. Both tables are printed in docs/scenarios.md section 5.1, and making
// coverage look better means editing one of them, in a commit, where it can be seen.
//
// What this file does NOT do: judge whether a covered cell is covered WELL. One scenario in
// a cell marks it covered. That is a real limit of this measure and it is written in the
// report rather than hidden: coverage says where we have looked, not how hard.

namespace Atm.Audit;

/// <summary>One cell of the matrix and how many scenarios landed in it.</summary>
public sealed record CoverageCell(string Islem, string Ariza, string An, int Scenarios)
{
    /// <summary>True when no scenario covers this cell.</summary>
    public bool IsEmpty => Scenarios == 0;

    public override string ToString() => $"{Islem} × {Ariza} × {An}";
}

/// <summary>Which cells of the matrix exist, and which of them are covered.</summary>
public sealed class CoverageMatrix
{
    /// <summary>Which moments each transaction actually has.</summary>
    /// <remarks>
    /// A withdrawal has no escrow: nothing waits to be confirmed, the notes either come out
    /// or they do not. A deposit has no dispense moment for the same reason in reverse. A
    /// balance enquiry has an authorisation moment and nothing else - it moves no money, so
    /// there is nothing to advise, reverse or hand over.
    /// </remarks>
    public static readonly IReadOnlyDictionary<string, string[]> MomentsOf =
        new Dictionary<string, string[]>
        {
            ["cekim"] = ["yetkilendirme", "dagitim", "bildirim", "ters-kayit", "bosta"],
            ["yatirma"] = ["yetkilendirme", "escrow", "onay", "bildirim", "ters-kayit", "bosta"],
            ["bakiye"] = ["yetkilendirme", "bosta"],
            ["gun-sonu"] = ["kesim", "bosta"],
        };

    /// <summary>Which moments each fault can strike at.</summary>
    /// <remarks>
    /// A partial dispense can only happen while cash is being handed over. An empty cassette
    /// can only bite before authorisation, because that is where this machine checks whether
    /// it can build the amount at all (rule 4.6 (docs/proje-kurallari.md)) - a machine that found out
    /// later would be a machine with a different bug. A duplicate request only exists where
    /// a message is sent, so it has no dispense moment.
    /// </remarks>
    public static readonly IReadOnlyDictionary<string, string[]> MomentsFor =
        new Dictionary<string, string[]>
        {
            ["cevap-kaybi"] = ["yetkilendirme", "bildirim", "ters-kayit", "onay", "kesim"],
            ["istek-kaybi"] = ["yetkilendirme", "bildirim", "ters-kayit", "onay", "kesim"],
            ["hat-kopmasi"] =
                ["yetkilendirme", "dagitim", "bildirim", "ters-kayit", "escrow", "onay", "kesim"],
            ["cift-istek"] = ["yetkilendirme", "bildirim", "ters-kayit"],
            ["kismi-dagitim"] = ["dagitim"],
            ["alinmayan-para"] = ["dagitim"],
            ["bos-kaset"] = ["yetkilendirme"],
            ["sikisma"] = ["dagitim", "escrow"],
            ["host-yeniden-baslatma"] =
                ["yetkilendirme", "bildirim", "ters-kayit", "escrow", "onay", "kesim", "bosta"],
            ["yok"] = ["bosta"],
        };

    private CoverageMatrix(IReadOnlyList<CoverageCell> cells) => Cells = cells;

    /// <summary>Every cell that exists, covered or not.</summary>
    public IReadOnlyList<CoverageCell> Cells { get; }

    /// <summary>How many cells exist at all.</summary>
    public int Total => Cells.Count;

    /// <summary>How many of them at least one scenario reaches.</summary>
    public int Covered => Cells.Count(c => !c.IsEmpty);

    /// <summary>The cells nobody has written a scenario for, by name.</summary>
    public IReadOnlyList<CoverageCell> Empty => [.. Cells.Where(c => c.IsEmpty)];

    /// <summary>How many of the 4x10x8 combinations are ruled out as impossible.</summary>
    public int Impossible =>
        (ScenarioAxis.Transactions.Count * ScenarioAxis.Faults.Count * ScenarioAxis.Moments.Count)
        - Total;

    /// <summary>Builds the matrix from a set of scenarios.</summary>
    public static CoverageMatrix Of(IEnumerable<Scenario> scenarios)
    {
        var counted = scenarios
            .GroupBy(s => (s.Eksen.Islem, s.Eksen.Ariza, s.Eksen.An))
            .ToDictionary(g => g.Key, g => g.Count());

        var cells = new List<CoverageCell>();

        foreach (var islem in ScenarioAxis.Transactions)
        {
            foreach (var ariza in ScenarioAxis.Faults)
            {
                foreach (var an in MomentsOf[islem].Intersect(MomentsFor[ariza]))
                {
                    cells.Add(new CoverageCell(islem, ariza, an,
                        counted.GetValueOrDefault((islem, ariza, an))));
                }
            }
        }

        return new CoverageMatrix(cells);
    }

    /// <summary>
    /// The report, as text. Empty cells are listed by name - that list is the point.
    /// </summary>
    public string ToText()
    {
        var lines = new List<string>
        {
            $"Kapsama: {Covered}/{Total} hücre koşuldu " +
            $"({(Total == 0 ? 0 : 100.0 * Covered / Total):F0}%). " +
            $"{Impossible} kombinasyon anlamsız olduğu için hiç sayılmadı " +
            "(docs/scenarios.md §5.1).",
        };

        if (Empty.Count == 0)
        {
            lines.Add("Boş hücre yok.");
            return string.Join(Environment.NewLine, lines);
        }

        lines.Add($"Koşulmayan {Empty.Count} hücre:");
        lines.AddRange(Empty.Select(c => "  - " + c));

        return string.Join(Environment.NewLine, lines);
    }
}
