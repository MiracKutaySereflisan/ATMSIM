// CassetteSet.cs
//
// What this file does: it holds the cassettes a machine is loaded with, and answers
// the questions that are about the set rather than about one drawer - what is in the
// machine altogether, and which drawer carries a given note.
//
// Why a type of its own instead of a plain list: two rules have to hold for the whole
// set and there is nowhere else to put them. No two cassettes may carry the same
// denomination, because then "the 50 cassette" would be an ambiguous phrase and the
// dispense plan could not say where a note came from. And no two cassettes may share
// an id, because the id is what a journal line and a reconciliation report point at.
// A list cannot refuse either mistake; this type refuses both at construction.
//
// Why the cassettes come back sorted from the largest denomination down: the planner
// walks them in a fixed order and a plan has to be reproducible from its inputs alone
// (kural 5 (docs/proje-kurallari.md), determinism). Sorting here means no caller has to remember to.

namespace Atm.Terminal;

/// <summary>The cassettes this machine is loaded with.</summary>
public sealed class CassetteSet
{
    private readonly List<Cassette> _cassettes;

    public CassetteSet(IEnumerable<Cassette> cassettes)
    {
        _cassettes = cassettes.OrderByDescending(c => c.Denomination).ToList();

        if (_cassettes.Count == 0)
            throw new ArgumentException("A machine with no cassettes cannot exist.", nameof(cassettes));

        var duplicateDenomination = _cassettes
            .GroupBy(c => c.Denomination).FirstOrDefault(g => g.Count() > 1);
        if (duplicateDenomination is not null)
            throw new ArgumentException(
                $"Two cassettes carry {duplicateDenomination.Key} kurus notes; one denomination lives in one cassette.",
                nameof(cassettes));

        var duplicateId = _cassettes.GroupBy(c => c.Id).FirstOrDefault(g => g.Count() > 1);
        if (duplicateId is not null)
            throw new ArgumentException(
                $"Two cassettes are both called {duplicateId.Key}; an id has to point at one drawer.",
                nameof(cassettes));
    }

    /// <summary>The cassettes, largest denomination first.</summary>
    public IReadOnlyList<Cassette> Cassettes => _cassettes;

    /// <summary>Every note in the machine's cassettes, counted.</summary>
    public int TotalNotes => _cassettes.Sum(c => c.NoteCount);

    /// <summary>What the cassettes are worth altogether, in kurus.</summary>
    public long TotalValue => _cassettes.Sum(c => c.Value);

    /// <summary>
    /// The denominations this machine can take back in, largest first. A note whose
    /// denomination is not in here has nowhere to go: the machine can pay it out but has
    /// no drawer that will accept it, so it is refused at the mouth rather than counted
    /// into a deposit that cannot be completed.
    /// </summary>
    public IReadOnlyList<long> AcceptedForDeposit =>
        _cassettes.Where(c => c.Kind == CassetteKind.Recycler)
                  .Select(c => c.Denomination).ToList();

    /// <summary>The drawer that carries this denomination, or null if the machine has none.</summary>
    public Cassette? ByDenomination(long denomination)
        => _cassettes.FirstOrDefault(c => c.Denomination == denomination);

    /// <summary>
    /// The load this simulator starts from, taken from docs/model.md SS2.
    /// ASSUMPTION: the four denominations and their counts are our own choice. They are
    /// not the loading of any real machine and no real bank's configuration was consulted.
    /// </summary>
    public static CassetteSet Standard() => new(
    [
        new Cassette("K1", 20_000, CassetteKind.Recycler, 500),
        new Cassette("K2", 10_000, CassetteKind.Recycler, 1000),
        new Cassette("K3", 5_000, CassetteKind.Recycler, 1000),
        new Cassette("K4", 2_000, CassetteKind.DispenseOnly, 500),
    ]);
}
