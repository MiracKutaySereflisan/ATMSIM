// Copyright (c) 2026 Mirac Kutay Sereflisan. MIT lisansi - LICENSE dosyasina bakiniz.
// Hardening.cs
//
// What this file does: it names, one by one, the things this system does BECAUSE of a fact
// about how ATMs really work - and lets each of them be switched off.
//
// Why that is worth building: the claim this whole project makes is not "our simulator
// works". It is "these particular rules are what keep the money straight, and here is what
// happens without them". A claim like that can only be measured by comparison, and a
// comparison is only readable when both sides are run under exactly the same conditions
// (rule 5): the same scenario files, the same seeds, the same steps.
//
// Why switches rather than a second, naive copy of the code: two copies drift. The naive
// copy would slowly stop being the same program with one rule removed and start being a
// different program that also happens to be wrong, and the comparison would quietly stop
// meaning anything. With switches there is one program, and the only difference between
// the two runs is the seven booleans below.
//
// Every flag maps to one numbered fact in the project instruction, and the mapping is the
// point - it is what makes "we turned off rule 4.3" a sentence about ATMs rather than a
// sentence about our code. The same comparison is printed by the scenario runner, with
// what each one costs when it is off.
//
// What this file does NOT do: make the naive mode reachable in the demo. It is a
// measurement instrument. The running host and terminal always use Full.

namespace Atm.Protocol;

/// <summary>Which of the domain's rules this run actually obeys.</summary>
/// <remarks>
/// Default is every rule on. A record with defaults means new(), and new() must be the
/// SAFE configuration: a flag that has to be remembered to be turned on is a flag that
/// will one day be forgotten.
/// </remarks>
public sealed record Hardening
{
    /// <summary>
    /// Instruction 4.1 - a timeout is not a refusal. Off: silence is treated as "it did
    /// not happen", and no reversal is produced.
    /// </summary>
    public bool TimeoutMakesReversal { get; init; } = true;

    /// <summary>
    /// Instruction 4.2 - a reversal can be lost too. Off: advices and reversals are sent
    /// once and forgotten if nobody answers.
    /// </summary>
    public bool RetryUntilAcknowledged { get; init; } = true;

    /// <summary>
    /// Instruction 4.3 - a partial dispense is real. Off: the machine reports the amount it
    /// was authorised for, whatever actually came out.
    /// </summary>
    public bool ReportPartialDispense { get; init; } = true;

    /// <summary>
    /// Instruction 4.4 - handed over is not taken. Off: cash the machine pulled back in is
    /// reported as if the customer had it.
    /// </summary>
    public bool RetractIsNotHandedOver { get; init; } = true;

    /// <summary>
    /// Instruction 4.5 - the same request can arrive twice. Off: the host's replay table is
    /// keyed on the transaction alone, and a reversed transaction can be authorised again.
    /// </summary>
    public bool RepeatImmunity { get; init; } = true;

    /// <summary>
    /// Instruction 4.6 - the note check comes BEFORE authorisation. Off: the machine asks
    /// first and finds out afterwards that it cannot build the amount.
    /// </summary>
    public bool NoteCheckBeforeAuthorisation { get; init; } = true;

    /// <summary>
    /// Instruction 4.7 - only what reached a drawer may be credited. Off: the account rises
    /// by what the customer put in, whatever the machine then did with it.
    /// </summary>
    public bool CreditWhatReachedADrawer { get; init; } = true;

    /// <summary>Everything on. This is what the running machine uses, always.</summary>
    public static readonly Hardening Full = new();

    /// <summary>Nothing on. A machine written by somebody who has not met an ATM.</summary>
    public static readonly Hardening None = new()
    {
        TimeoutMakesReversal = false,
        RetryUntilAcknowledged = false,
        ReportPartialDispense = false,
        RetractIsNotHandedOver = false,
        RepeatImmunity = false,
        NoteCheckBeforeAuthorisation = false,
        CreditWhatReachedADrawer = false,
    };

    /// <summary>True when every rule is on.</summary>
    public bool IsFull => Equals(Full);
}
