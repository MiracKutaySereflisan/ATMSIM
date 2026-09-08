// Copyright (c) 2026 Mirac Kutay Sereflisan. MIT lisansi - LICENSE dosyasina bakiniz.
// ConservationReport.cs
//
// What this file does: it holds what the checker found, and it draws the one line that
// makes a reconciliation useful - the line between a difference that is EXPLAINED and a
// difference that is NOT.
//
// Why that line is the whole point: every real reconciliation finds differences. Money is
// in flight; a report has been sent and not yet acknowledged; a machine was switched off
// mid-transaction. A tool that shouts at all of them is turned off by the second week. A
// tool that stays quiet about all of them is useless. The one that survives says: here
// are four differences, three of them have a name and a reason, and this fourth one does
// not - look at that one.
//
// An unexplained difference is the finding this project exists to produce. Instruction
// section 3 calls it a violation of money conservation; rule 6b requires it
// to fail loudly rather than be quietly repaired. Hence ThrowIfViolated: the callers that
// run unattended - the scenario runner in Phase 4, the end-of-day script - call it and
// stop. Callers that want to look at the whole picture read the lists instead.

namespace Atm.Audit;

/// <summary>One thing the checker noticed. Immutable.</summary>
/// <param name="Code">Short stable name, so a finding can be looked up and counted.</param>
/// <param name="Title">One line in plain Turkish: what does not add up.</param>
/// <param name="Detail">The numbers, so that a human can follow the arithmetic.</param>
/// <param name="Amount">How much money the difference is about, in kurus.</param>
/// <param name="Reason">
/// Empty when nothing explains this. When it is filled in, it names the thing that does -
/// "the advice for this transaction is still in the queue" - and the finding moves out of
/// the violations list.
/// </param>
public sealed record AuditFinding(
    string Code, string Title, string Detail, long Amount, string Reason = "")
{
    /// <summary>True when something names why this difference exists.</summary>
    public bool IsExplained => Reason.Length > 0;

    public override string ToString() => IsExplained
        ? $"[{Code}] {Title} — {Detail} (açıklanan: {Reason})"
        : $"[{Code}] {Title} — {Detail}";
}

/// <summary>The names of the things this checker can find. One per invariant.</summary>
public static class AuditCode
{
    /// <summary>The machine's buckets do not add up to what they started at.</summary>
    public const string CashTotal = "KASA-TOPLAMI";

    /// <summary>What customers hold is not what the accounts lost.</summary>
    public const string CustomerVersusLedger = "MUSTERI-DEFTER";

    /// <summary>The terminal's record does not match its own dispenser.</summary>
    public const string RecordVersusDispenser = "GUNLUK-KASA";

    /// <summary>An account was debited with no approved authorisation behind it.</summary>
    public const string DebitWithoutAuthorisation = "KARSILIKSIZ-BORC";

    /// <summary>A promise is still standing against an account.</summary>
    public const string OpenAuthorisation = "ACIK-SOZ";

    /// <summary>An account was credited with no agreed deposit behind it.</summary>
    public const string CreditWithoutDeposit = "KARSILIKSIZ-ALACAK";

    /// <summary>A deposit the host agreed to and never heard the end of.</summary>
    public const string OpenDeposit = "ACIK-YATIRMA";

    /// <summary>Money that is inside the machine and belongs to nobody right now.</summary>
    public const string StuckMoney = "SIKISAN-PARA";

    /// <summary>The two records disagree about one transaction.</summary>
    public const string Reconciliation = "MUTABAKAT-FARKI";
}

/// <summary>What the checker found, in two lists.</summary>
public sealed record ConservationReport
{
    /// <summary>Everything that was noticed, explained or not, in the order found.</summary>
    public required IReadOnlyList<AuditFinding> Findings { get; init; }

    /// <summary>Which moment this report was taken at.</summary>
    public required AuditMoment Moment { get; init; }

    /// <summary>Differences nothing accounts for. These are the findings.</summary>
    public IEnumerable<AuditFinding> Violations => Findings.Where(f => !f.IsExplained);

    /// <summary>Differences with a name and a reason. Real, and not alarming.</summary>
    public IEnumerable<AuditFinding> Explained => Findings.Where(f => f.IsExplained);

    /// <summary>True when nothing is unexplained.</summary>
    public bool IsClean => !Violations.Any();

    /// <summary>
    /// Stops the caller when money cannot be accounted for.
    /// </summary>
    /// <remarks>
    /// This is rule 6b in one method: an invariant that is broken has to
    /// be loud. A checker that returned a report nobody read would be a checker that
    /// lets a wrong balance reach a demonstration.
    /// </remarks>
    public void ThrowIfViolated()
    {
        if (IsClean)
        {
            return;
        }

        var lines = string.Join(Environment.NewLine, Violations.Select(v => "  " + v));

        throw new ConservationViolationException(
            $"Para korunumu ihlali ({Violations.Count()} adet):{Environment.NewLine}{lines}");
    }

    /// <summary>The whole report as text, for a log or a report file.</summary>
    public string ToText()
    {
        var header = Moment == AuditMoment.EndOfDay
            ? "GÜN SONU MUTABAKATI"
            : "ARA DENETİM";

        if (Findings.Count == 0)
        {
            return $"{header}: fark yok.";
        }

        var lines = Findings.Select(f => "  " + f);

        return $"{header}: {Violations.Count()} açıklanamayan, " +
               $"{Explained.Count()} açıklanan fark." + Environment.NewLine +
               string.Join(Environment.NewLine, lines);
    }
}

/// <summary>Thrown when the machine cannot account for money.</summary>
public sealed class ConservationViolationException : Exception
{
    public ConservationViolationException(string message) : base(message) { }
}
