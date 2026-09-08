// ScenarioTests.cs
//
// What these tests are for: the scenario machinery itself - the reader that refuses what it
// does not understand, and the runner that reports WHERE a difference was visible.
//
// Why the machinery needs its own tests: everything else in Phase 4 is measured BY it. A
// reader that silently skipped a misspelled field would report a fault that was never
// injected as a scenario that passed; a runner whose detection point was always "nowhere"
// would report a system that catches nothing as a system with nothing to catch. Both
// failures make the whole phase greener, which is the direction nobody checks.
//
// The test to read first is TheDetectionPointSaysWhereADifferenceBecameVisible. It is the
// only place the three answers - at once, at day end, nowhere - are all produced and told
// apart, and the measurement rule 3 (docs/proje-kurallari.md) asks for rests on that distinction.

using Atm.Audit;
using Atm.Protocol;
using Xunit;

namespace Atm.Tests;

public class ScenarioTests
{
    private const string Card = "4111111111111111";

    private static string WriteScenario(string body)
    {
        var folder = Path.Combine(Path.GetTempPath(), "atmsim-sen-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "S.json"), body);
        return folder;
    }

    private const string Good = """
    {
      "id": "T-1",
      "baslik": "deneme",
      "seed": 1,
      "eksen": { "islem": "cekim", "ariza": "yok", "an": "bosta" },
      "adimlar": [ { "adim": "cek", "kart": "4111111111111111", "tutar": 35000, "stan": 701 } ],
      "beklenen": { "musteriye-giden": 35000, "hesap-farki": -35000,
                    "denetim": "temiz", "tespit": "yok" }
    }
    """;

    [Fact]
    public void AGoodScenarioIsReadAndRuns()
    {
        var folder = WriteScenario(Good);

        try
        {
            var scenario = Scenario.ReadFolder(folder).Single();
            Assert.Equal("T-1", scenario.Id);

            var result = ScenarioRunner.Run(scenario);
            Assert.True(result.Passed, string.Join(" · ", result.Complaints));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void AMisspelledFieldIsRefusedRatherThanIgnored()
    {
        // "tutarr" instead of "tutar". Ignored, this would run a withdrawal of nothing and
        // report a green scenario - a harness congratulating us for a fault we forgot to
        // cause (docs/scenarios.md section 2).
        var folder = WriteScenario(Good.Replace("\"tutar\"", "\"tutarr\""));

        try
        {
            var complaint = Assert.Throws<InvalidDataException>(() => Scenario.ReadFolder(folder));
            Assert.Contains("Tanınmayan bir alan", complaint.Message);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void AnAxisValueOutsideTheClosedListIsRefused()
    {
        // Inventing a fault name would add a whole row to the coverage matrix and report
        // one scenario as covering it (KARAR-049).
        var folder = WriteScenario(Good.Replace("\"ariza\": \"yok\"", "\"ariza\": \"kozmik-isin\""));

        try
        {
            var complaint = Assert.Throws<InvalidDataException>(() => Scenario.ReadFolder(folder));
            Assert.Contains("listede yok", complaint.Message);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void AnUnknownStepIsRefused()
    {
        var folder = WriteScenario(Good.Replace("\"adim\": \"cek\"", "\"adim\": \"zipla\""));

        try
        {
            var complaint = Assert.Throws<InvalidDataException>(() => Scenario.ReadFolder(folder));
            Assert.Contains("diye bir adım yok", complaint.Message);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void ACleanScenarioCannotClaimADetectionPoint()
    {
        var folder = WriteScenario(Good.Replace("\"tespit\": \"yok\"", "\"tespit\": \"aninda\""));

        try
        {
            var complaint = Assert.Throws<InvalidDataException>(() => Scenario.ReadFolder(folder));
            Assert.Contains("tespit edilecek bir şey yok", complaint.Message);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void TheDetectionPointSaysWhereADifferenceBecameVisible()
    {
        var folder = WriteScenario(Good);

        try
        {
            var scenario = Scenario.ReadFolder(folder).Single();

            // Every rule on: nothing to detect.
            var hardened = ScenarioRunner.Run(scenario, Hardening.Full);
            Assert.Equal("temiz", hardened.Denetim);
            Assert.Equal("yok", hardened.Tespit);

            // Repeat immunity off, and the same withdrawal loses its debit: the advice is
            // answered with the authorisation's remembered answer and the ledger never
            // moves. The difference is there the instant the scenario ends.
            var naive = ScenarioRunner.Run(scenario,
                Hardening.Full with { RepeatImmunity = false });

            Assert.Equal("ihlal", naive.Denetim);
            Assert.Equal("aninda", naive.Tespit);
            Assert.Equal(35_000, naive.MusteriyeGiden);
            Assert.Equal(0, naive.HesapFarki);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void ADifferenceThatOnlyTheEndOfTheDayShowsIsCalledThat()
    {
        // The advice never reaches the host and is never retried. While the queue could
        // still explain it, the checker says nothing; once the day is over and the queue is
        // empty, there is nothing left to explain it with.
        var folder = WriteScenario("""
        {
          "id": "T-2",
          "baslik": "bildirim kayboldu ve tekrar denenmedi",
          "seed": 2,
          "eksen": { "islem": "cekim", "ariza": "istek-kaybi", "an": "bildirim" },
          "adimlar": [
            { "adim": "istekleri-yut", "mesaj": "DispenseAdvice" },
            { "adim": "cek", "kart": "4111111111111111", "tutar": 35000, "stan": 702 }
          ],
          "beklenen": { "musteriye-giden": 35000, "hesap-farki": -35000,
                        "denetim": "temiz", "tespit": "yok" }
        }
        """);

        try
        {
            var scenario = Scenario.ReadFolder(folder).Single();

            var naive = ScenarioRunner.Run(scenario,
                Hardening.Full with { RetryUntilAcknowledged = false });

            Assert.Equal("ihlal", naive.Denetim);
            Assert.Equal("gun-sonu", naive.Tespit);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void ACassetteCannotBeRefilledOnceTheMachineHasDoneSomething()
    {
        var day = new SimulatedDay();
        day.Flow.Withdraw(Card, 20_000, stan: 703);

        // Re-taking the opening position mid-day would move the line a difference is
        // measured from, and erase a real one.
        Assert.Throws<InvalidOperationException>(day.RebaselineOpening);
    }

    [Fact]
    public void TheCoverageMatrixCountsOnlyCellsThatExist()
    {
        var matrix = CoverageMatrix.Of([]);

        // 4 transactions x 10 faults x 8 moments is 320; most of those combinations are not
        // things that can happen. The denominator is the ones that can.
        Assert.Equal(320, matrix.Total + matrix.Impossible);
        Assert.True(matrix.Total < 320);
        Assert.Equal(0, matrix.Covered);

        // And with nothing to go on, every cell it does count is reported as empty - by
        // name. A coverage report whose empty list could be shortened by looking away would
        // not be a coverage report.
        Assert.Equal(matrix.Total, matrix.Empty.Count);
    }
}
