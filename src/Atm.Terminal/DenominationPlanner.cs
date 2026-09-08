// Copyright (c) 2026 Mirac Kutay Sereflisan. MIT lisansi - LICENSE dosyasina bakiniz.
// DenominationPlanner.cs
//
// What this file does: given the cassettes a machine is loaded with and an amount, it
// works out exactly which notes would be handed over - or says that they cannot be.
//
// Why this runs BEFORE the authorisation and not after (kural 4.6): asking the host
// first and counting notes afterwards produces a debit for money the machine then cannot
// pay. That debit has to be reversed, and a reversal can itself be lost (kural 4.2).
// Running the count first turns a two-step failure into a screen message: the machine
// simply never asks for money it cannot hand over.
//
// Why an exact solution and not the greedy "start from the largest note" (KARAR-011):
// greedy declares dispensable amounts undispensable. Loaded with 50s and 20s only, asked
// for 60, greedy lays down one 50, is left with 10, has no 10 note and gives up - while
// three 20s were sitting right there. That is a wrong answer, and it reaches the customer
// as "the machine would not give me my money".
//
// How the exact answer is found: this is the bounded coin change problem. Amounts are
// walked in steps of the greatest common divisor of the loaded denominations, because no
// amount between two steps is reachable at all, and each cassette is folded in one at a
// time with the number of notes taken from it bounded by what it holds.
//
// The choice between equally short answers is fixed, not arbitrary, because a plan has
// to be reproducible from its inputs alone (kural 5): fewest notes first, and where
// two answers use the same number of notes, the one that takes from the fuller cassettes.
// Cassettes emptying one after another leaves a machine that still dispenses something;
// cassettes emptying together leaves a machine that is simply out of service.
//
// Cost: the table has (amount / step) cells per cassette, and the amount is bounded by
// what the cassettes are worth, which is checked before any table is built.

namespace Atm.Terminal;

/// <summary>Works out which notes make up an amount, exactly (KARAR-011).</summary>
public static class DenominationPlanner
{
    private const int Unreachable = int.MaxValue;

    /// <summary>
    /// The notes the machine would hand over for this amount, or a refusal with a reason.
    /// </summary>
    public static DenominationPlan Plan(CassetteSet cassettes, long amount)
    {
        ArgumentNullException.ThrowIfNull(cassettes);

        if (amount <= 0)
            return DenominationPlan.Refused(amount, PlanRefusal.AmountNotPositive);

        // Only a drawer with notes in it can take part. Skipping the empty ones does not
        // change any answer - a drawer with no notes contributes none - it only keeps the
        // table small, so no test can tell the difference and none pretends to.
        var usable = cassettes.Cassettes.Where(c => c.NoteCount > 0).ToList();

        // Refusing an amount larger than the machine holds is a speed guard and nothing
        // more, and it is written down here as one. The table has a cell per step of the
        // amount, so a ten-million-lira request builds a million cells and then answers no
        // anyway - measured: the whole test suite goes from 0.5 s to 6.9 s without this
        // line, and not one test changes its verdict.
        if (usable.Count == 0 || amount > usable.Sum(c => c.Value))
            return DenominationPlan.Refused(amount, PlanRefusal.NoCombination);

        var step = usable.Select(c => c.Denomination).Aggregate(GreatestCommonDivisor);
        if (amount % step != 0)
            return DenominationPlan.Refused(amount, PlanRefusal.NoCombination);

        var target = (int)(amount / step);

        // notes[c][a]: fewest notes that make amount a out of the first c cassettes.
        // score[c][a]: among those, the largest "took it from a full drawer" score.
        // taken[c][a]: how many notes cassette c-1 contributed to that answer.
        var notes = NewTable<int>(usable.Count + 1, target + 1);
        var score = NewTable<long>(usable.Count + 1, target + 1);
        var taken = NewTable<int>(usable.Count + 1, target + 1);

        for (var a = 0; a <= target; a++) notes[0][a] = Unreachable;
        notes[0][0] = 0;

        for (var c = 0; c < usable.Count; c++)
        {
            var cassette = usable[c];
            var stepsPerNote = (int)(cassette.Denomination / step);

            for (var a = 0; a <= target; a++)
            {
                var bestNotes = Unreachable;
                var bestScore = 0L;
                var bestTaken = 0;
                var most = Math.Min(cassette.NoteCount, a / stepsPerNote);

                for (var k = 0; k <= most; k++)
                {
                    var rest = a - (k * stepsPerNote);
                    if (notes[c][rest] == Unreachable) continue;

                    var candidateNotes = notes[c][rest] + k;
                    var candidateScore = score[c][rest] + ((long)k * cassette.NoteCount);

                    var better = candidateNotes < bestNotes
                        || (candidateNotes == bestNotes && candidateScore > bestScore);
                    if (!better) continue;

                    bestNotes = candidateNotes;
                    bestScore = candidateScore;
                    bestTaken = k;
                }

                notes[c + 1][a] = bestNotes;
                score[c + 1][a] = bestNotes == Unreachable ? 0 : bestScore;
                taken[c + 1][a] = bestNotes == Unreachable ? 0 : bestTaken;
            }
        }

        if (notes[usable.Count][target] == Unreachable)
            return DenominationPlan.Refused(amount, PlanRefusal.NoCombination);

        // Walk back through the table and read off what each cassette contributed.
        var bundles = new List<NoteBundle>();
        var remaining = target;
        for (var c = usable.Count; c > 0; c--)
        {
            var count = taken[c][remaining];
            if (count > 0) bundles.Add(new NoteBundle(usable[c - 1].Denomination, count));
            remaining -= count * (int)(usable[c - 1].Denomination / step);
        }

        bundles.Sort((left, right) => right.Denomination.CompareTo(left.Denomination));
        return DenominationPlan.Dispensable(amount, bundles);
    }

    private static T[][] NewTable<T>(int rows, int columns)
    {
        var table = new T[rows][];
        for (var r = 0; r < rows; r++) table[r] = new T[columns];
        return table;
    }

    private static long GreatestCommonDivisor(long a, long b)
    {
        while (b != 0) (a, b) = (b, a % b);
        return a;
    }
}
