// ScenarioRunner.cs
//
// What this file does: it takes one scenario, plays it through a simulated day, and says
// whether what happened is what the scenario said should happen.
//
// The one rule that shapes this file: THE SCENARIO IS NOT ALLOWED TO LEARN FROM THE RUN.
// Nothing here writes back into a scenario, softens a comparison, or reports "close
// enough". A runner that adjusted its expectations to the result would turn this whole
// phase into an expensive way of asserting that the code does what the code does
// (rule 5 (docs/proje-kurallari.md)).
//
// The three questions asked at the end, in this order, because they answer different
// things:
//
//   1. Where is the money? Cash in customers' hands, and the ledger of the card that acted.
//      These are the facts a person could check by counting.
//   2. Do the books balance? The conservation checker, run twice - once with the queue
//      still full (AtRest) and once after it has drained (EndOfDay).
//   3. WHERE would anybody have noticed? That is the detection point, and it is the
//      measurement rule 3 (docs/proje-kurallari.md) actually asks for: a difference caught at the
//      moment, a difference caught at the end of the day and a difference caught by nobody
//      are three very different results with the same arithmetic behind them.
//
// Why the checker is run at two moments rather than one: between cash leaving the machine
// and the host being told, the books genuinely do not balance, and a runner that only
// looked at the end would call that "fine" without ever seeing it. Looking twice is what
// lets a scenario say WHEN the difference was visible.

using Atm.Protocol;
using Atm.Terminal;

namespace Atm.Audit;

/// <summary>What one scenario did.</summary>
/// <param name="Scenario">The scenario that was run.</param>
/// <param name="Passed">True when everything the scenario expected actually happened.</param>
/// <param name="Complaints">What did not match, in plain Turkish. Empty when it passed.</param>
/// <param name="MusteriyeGiden">Cash in customers' hands at the end, in kurus.</param>
/// <param name="HesapFarki">Net ledger change of the acting card, in kurus.</param>
/// <param name="Denetim">"temiz" or "ihlal" - what the checker actually said.</param>
/// <param name="Tespit">Where the violation was visible: "aninda", "gun-sonu" or "yok".</param>
/// <param name="IhlalSayisi">How many unexplained differences were found.</param>
/// <param name="GunKapandi">Whether a cutover in this scenario closed the day.</param>
public sealed record ScenarioResult(
    Scenario Scenario,
    bool Passed,
    IReadOnlyList<string> Complaints,
    long MusteriyeGiden,
    long HesapFarki,
    string Denetim,
    string Tespit,
    int IhlalSayisi,
    bool? GunKapandi);

/// <summary>Plays scenarios through a simulated day. See docs/scenarios.md.</summary>
public sealed class ScenarioRunner
{
    /// <summary>Every step name this runner understands. Anything else is an error.</summary>
    public static readonly IReadOnlyList<string> KnownSteps =
    [
        "cek", "yatir", "yatir-basla", "yatir-bitir", "bakiye", "gun-kapat",
        "hat-kes", "hat-gelsin", "istekleri-yut", "cevaplari-yut",
        "eksik-ver", "sikisma", "dagitici-duzelsin", "para-alinmasin", "para-alinsin",
        "yatirma-sikismasi", "yatirma-eksik-alsin", "yatirma-duzelsin",
        "kaset-ayarla", "zaman-gecir", "kuyrugu-bosalt", "host-yeniden-baslat",
    ];

    /// <summary>Runs every scenario and returns one result each, in the order given.</summary>
    public static IReadOnlyList<ScenarioResult> RunAll(
        IEnumerable<Scenario> scenarios, Hardening? hardening = null) =>
        [.. scenarios.Select(s => Run(s, hardening))];

    /// <summary>
    /// Runs one scenario from beginning to end, under the given set of rules.
    /// </summary>
    /// <remarks>
    /// The hardening is the ONLY thing that differs between the two runs the report
    /// compares. Same files, same seeds, same steps - because a comparison read under
    /// unmatched conditions is not a comparison (rule 5 (docs/proje-kurallari.md)).
    /// </remarks>
    public static ScenarioResult Run(Scenario scenario, Hardening? hardening = null)
    {
        var day = new SimulatedDay(hardening: hardening);
        var actingCard = "";
        var ledgerBefore = 0L;
        bool? dayClosed = null;

        // A deposit that was begun and not yet confirmed. Held here rather than inside the
        // step, because the whole point of splitting a deposit into two steps is that a
        // fault can be injected BETWEEN them - which is rule 4.8 (docs/proje-kurallari.md): the line
        // can drop between the customer saying yes and the account being touched.
        DepositHandle? inEscrow = null;

        foreach (var step in scenario.Adimlar)
        {
            // The first card the scenario touches is the one "hesap-farki" is about. Taken
            // from the steps rather than declared separately, so the two cannot disagree.
            if (step.Kart.Length > 0 && actingCard.Length == 0)
            {
                actingCard = step.Kart;
                ledgerBefore = day.LedgerOf(actingCard);
            }

            Apply(day, step, scenario, ref dayClosed, ref inEscrow);
        }

        // Read the books BEFORE draining anything. A difference visible here is a
        // difference a person watching the machine could have seen.
        var atRest = ConservationChecker.Check(day.Snapshot(AuditMoment.AtRest));

        // Then let the machine say everything it still owes the host, and look again. What
        // survives this is a difference nothing explains.
        day.Clock.Advance(TimeSpan.FromMinutes(10));
        day.Flow.SendPending();
        var endOfDay = ConservationChecker.Check(day.Snapshot(AuditMoment.EndOfDay));

        var ihlalSayisi = endOfDay.Violations.Count();
        var denetim = endOfDay.IsClean ? "temiz" : "ihlal";

        var tespit = endOfDay.IsClean
            ? "yok"
            : atRest.Violations.Any() ? "aninda" : "gun-sonu";

        var musteriyeGiden = day.Dispenser.Position.WithCustomers;
        var hesapFarki = actingCard.Length == 0 ? 0 : day.LedgerOf(actingCard) - ledgerBefore;

        var complaints = Compare(scenario.Beklenen,
            musteriyeGiden, hesapFarki, denetim, tespit, ihlalSayisi, dayClosed);

        return new ScenarioResult(scenario, complaints.Count == 0, complaints,
            musteriyeGiden, hesapFarki, denetim, tespit, ihlalSayisi, dayClosed);
    }

    private static IReadOnlyList<string> Compare(
        ScenarioExpectation expected, long musteriyeGiden, long hesapFarki,
        string denetim, string tespit, int ihlalSayisi, bool? dayClosed)
    {
        var complaints = new List<string>();

        if (expected.MusteriyeGiden != musteriyeGiden)
        {
            complaints.Add(
                $"müşteriye giden: beklenen {expected.MusteriyeGiden}, çıkan {musteriyeGiden} kuruş");
        }

        if (expected.HesapFarki != hesapFarki)
        {
            complaints.Add(
                $"hesap farkı: beklenen {expected.HesapFarki}, çıkan {hesapFarki} kuruş");
        }

        if (expected.Denetim != denetim)
        {
            complaints.Add($"denetim: beklenen '{expected.Denetim}', çıkan '{denetim}'");
        }

        if (expected.Tespit != tespit)
        {
            complaints.Add($"tespit noktası: beklenen '{expected.Tespit}', çıkan '{tespit}'");
        }

        if (expected.IhlalSayisi is int wanted && wanted != ihlalSayisi)
        {
            complaints.Add($"ihlal sayısı: beklenen {wanted}, çıkan {ihlalSayisi}");
        }

        if (expected.GunKapandi is bool shouldClose && shouldClose != (dayClosed ?? false))
        {
            complaints.Add(
                $"gün kapandı mı: beklenen {shouldClose}, çıkan {dayClosed?.ToString() ?? "kesim hiç denenmedi"}");
        }

        return complaints;
    }

    private static void Apply(SimulatedDay day, ScenarioStep step, Scenario scenario,
        ref bool? dayClosed, ref DepositHandle? inEscrow)
    {
        switch (step.Adim)
        {
            case "cek":
                day.Flow.Withdraw(step.Kart, step.Tutar, step.Stan);
                break;

            case "yatir":
                var began = day.Deposits.Begin(step.Kart, Notes(step.Banknotlar, scenario), step.Stan);

                if (began.InEscrow is not null)
                {
                    day.Deposits.Complete(began.InEscrow, step.Onay);
                }

                break;

            case "yatir-basla":
                inEscrow = day.Deposits
                    .Begin(step.Kart, Notes(step.Banknotlar, scenario), step.Stan).InEscrow;
                break;

            case "yatir-bitir":
                if (inEscrow is null)
                {
                    throw new InvalidDataException(
                        $"{scenario.Id}: 'yatir-bitir' adımından önce ara kasada para yok. " +
                        "Ya 'yatir-basla' eksik, ya da o adım parayı zaten geri vermiş.");
                }

                day.Deposits.Complete(inEscrow, step.Onay);
                inEscrow = null;
                break;

            case "yatirma-sikismasi":
                day.Dispenser.AcceptorFault = AcceptorFault.Jam();
                break;

            case "yatirma-eksik-alsin":
                day.Dispenser.AcceptorFault = AcceptorFault.StackAtMostThis(step.EnFazla);
                break;

            case "yatirma-duzelsin":
                day.Dispenser.AcceptorFault = AcceptorFault.None;
                break;

            case "bakiye":
                day.AskHost(MessageCodec.Envelope(MessageType.BalanceRequest,
                    new TransactionKey("ATM-01", day.Day.Current, step.Stan), day.Clock.UtcNow,
                    new BalanceRequestBody(step.Kart)), TimeSpan.FromSeconds(15));
                break;

            case "gun-kapat":
                dayClosed = day.Cutover.Close(step.Stan).Closed;
                break;

            case "hat-kes":
                day.LineIsDown = true;
                break;

            case "hat-gelsin":
                day.LineComesBack();
                break;

            case "istekleri-yut":
                day.DropRequestsOfType = step.Mesaj;
                break;

            case "cevaplari-yut":
                day.LoseAnswersForType = step.Mesaj;
                break;

            case "eksik-ver":
                day.Dispenser.Fault = DispenserFault.PresentAtMostThis(step.EnFazla);
                break;

            case "sikisma":
                day.Dispenser.Fault = DispenserFault.Jam();
                break;

            case "dagitici-duzelsin":
                day.Dispenser.Fault = DispenserFault.None;
                break;

            case "para-alinmasin":
                day.CustomerTakesCash = false;
                break;

            case "para-alinsin":
                day.CustomerTakesCash = true;
                break;

            case "kaset-ayarla":
                SetCassette(day, step, scenario);
                break;

            case "zaman-gecir":
                day.Clock.Advance(TimeSpan.FromSeconds(step.Saniye));
                break;

            case "kuyrugu-bosalt":
                day.Flow.SendPending();
                break;

            case "host-yeniden-baslat":
                day.RestartHost();
                break;

            default:
                // Unreachable: Scenario.Validate refuses unknown steps before we get here.
                // Kept anyway, because "unreachable" is a claim about today's code and this
                // is a switch somebody will add a case to.
                throw new InvalidDataException(
                    $"{scenario.Id}: '{step.Adim}' adımı koşucuda karşılıksız.");
        }
    }

    /// <summary>
    /// Sets how many notes of one denomination the machine starts the day with.
    /// </summary>
    /// <remarks>
    /// Setup only, and the refusal below is the point. Changing a cassette in the middle of
    /// the day would take money out of the conservation equation without it going anywhere,
    /// and the checker would rightly report that money had vanished. In life the person who
    /// empties a cassette is doing something OUTSIDE the transactions - so an empty-cassette
    /// scenario is a scenario where the machine STARTED the morning that way.
    /// </remarks>
    private static void SetCassette(SimulatedDay day, ScenarioStep step, Scenario scenario)
    {
        var cassette = day.Cassettes.ByDenomination(step.Kupur)
            ?? throw new InvalidDataException(
                $"{scenario.Id}: {step.Kupur} kuruşluk kaset yok.");

        if (step.Adet < cassette.NoteCount)
        {
            cassette.Take(cassette.NoteCount - step.Adet);
        }
        else if (step.Adet > cassette.NoteCount)
        {
            cassette.Put(step.Adet - cassette.NoteCount);
        }

        day.RebaselineOpening();
    }

    /// <summary>Reads "100x5,50x2" as note bundles. Values are in lira on the way in.</summary>
    /// <remarks>
    /// Lira in the file, kurus in the code. The file is written by a person and a person
    /// writes "100 lira"; everything inside this project counts in kurus (KARAR-008). The
    /// conversion happens here, in one place, and nowhere else.
    /// </remarks>
    private static IReadOnlyList<NoteBundle> Notes(string text, Scenario scenario)
    {
        if (text.Length == 0)
        {
            throw new InvalidDataException($"{scenario.Id}: 'yatir' adımında banknot listesi yok.");
        }

        var bundles = new List<NoteBundle>();

        foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var halves = part.Trim().Split('x');

            if (halves.Length != 2
                || !long.TryParse(halves[0], out var lira)
                || !int.TryParse(halves[1], out var count))
            {
                throw new InvalidDataException(
                    $"{scenario.Id}: banknot listesi okunamadı: '{part}'. Biçim: \"100x5,50x2\".");
            }

            bundles.Add(new NoteBundle(lira * 100, count));
        }

        return bundles;
    }
}
