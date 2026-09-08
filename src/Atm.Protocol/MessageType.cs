// Copyright (c) 2026 Mirac Kutay Sereflisan. MIT lisansi - LICENSE dosyasina bakiniz.
// MessageType.cs
//
// What this file does: it lists the names that may appear in an envelope's Type field,
// and the bodies that go with them.
//
// Why the names are constants rather than an enum: the name travels on the wire as
// text. An enum would be stored as a number, and a number means the two sides must
// agree on the ORDER of the list - so inserting a new type in the middle would
// silently change what every later type means. Text does not have that failure mode.
//
// Why the bodies are records: a record compares by value, so a test can say "this is
// the message I expected" in one line, and it cannot be modified after construction -
// a message that changed after being sent would be untraceable.
//
// What is implemented here is what the host actually answers today: echo, PIN
// verification, balance enquiry and - since Phase 2b - withdrawal authorisation.
// Dispense advice, reversal and the deposit messages are written in the protocol spec
// and arrive with the phases that perform them. Writing them now would be writing code
// for behaviour that does not exist yet.

using System.Text.Json.Serialization;

namespace Atm.Protocol;

/// <summary>The name that goes in <see cref="Envelope.Type"/>.</summary>
public static class MessageType
{
    public const string EchoRequest = "EchoRequest";
    public const string EchoResponse = "EchoResponse";
    public const string PinVerifyRequest = "PinVerifyRequest";
    public const string PinVerifyResponse = "PinVerifyResponse";
    public const string BalanceRequest = "BalanceRequest";
    public const string BalanceResponse = "BalanceResponse";
    public const string WithdrawalAuthRequest = "WithdrawalAuthRequest";
    public const string WithdrawalAuthResponse = "WithdrawalAuthResponse";
    public const string DispenseAdvice = "DispenseAdvice";
    public const string DispenseAdviceResponse = "DispenseAdviceResponse";
    public const string ReversalRequest = "ReversalRequest";
    public const string ReversalResponse = "ReversalResponse";
    public const string DepositAuthRequest = "DepositAuthRequest";
    public const string DepositAuthResponse = "DepositAuthResponse";
    public const string DepositCommitAdvice = "DepositCommitAdvice";
    public const string DepositCommitResponse = "DepositCommitResponse";
    public const string CutoverRequest = "CutoverRequest";
    public const string CutoverResponse = "CutoverResponse";
}

/// <summary>
/// What the machine says happened to the notes the customer put in.
/// See the protocol spec section 4.6.
/// </summary>
/// <remarks>
/// The mirror of DispenseOutcome, and the same distinction runs through it: money that
/// reached a drawer is the bank's, money that was handed back is the customer's, and money
/// stuck in the mechanism is neither. Only the first of the three moves a ledger.
/// </remarks>
public static class DepositOutcome
{
    /// <summary>All of it reached a recycler drawer. The account rises by that much.</summary>
    public const string Stacked = "STACKED";

    /// <summary>Some reached a drawer, the rest jammed. Only the stacked part is credited.</summary>
    public const string Partial = "PARTIAL";

    /// <summary>The customer changed their mind, or the host said no. Nothing is credited.</summary>
    public const string Returned = "RETURNED";

    /// <summary>Stuck in the mechanism. Nothing is credited and an engineer has to come.</summary>
    public const string Jammed = "JAMMED";
}

/// <summary>Why a withdrawal is being taken back. See the protocol spec section 4.5.</summary>
/// <remarks>
/// The reason never changes what the host does - a reversal releases the promise whatever
/// its reason. It is carried because the end-of-day report has to be able to say WHY the
/// machine gave money back, and "the line was down" and "the customer pressed cancel" are
/// different problems with the same correction.
/// </remarks>
public static class ReversalReason
{
    /// <summary>No answer came back. NOT "it did not happen" - see instruction 4.1.</summary>
    public const string Timeout = "TIMEOUT";

    /// <summary>The machine could not hand the cash over at all.</summary>
    public const string DispenseFailed = "DISPENSE_FAILED";

    /// <summary>Less came out than was authorised.</summary>
    public const string Partial = "PARTIAL";

    /// <summary>It came out, nobody took it, the machine pulled it back in.</summary>
    public const string Retracted = "RETRACTED";

    /// <summary>The customer stopped before the cash moved.</summary>
    public const string Cancelled = "CANCELLED";
}

/// <summary>
/// What the machine says happened to the cash. See the protocol spec section 4.4.
/// </summary>
/// <remarks>
/// These four words are the difference between money that left the bank and money that
/// only left a drawer. "Dispensed" is not "taken": cash can reach the mouth of the
/// machine and be pulled back in when nobody picks it up, and that cash is in neither
/// the customer's pocket nor the cassette (rule 4.4).
/// </remarks>
public static class DispenseOutcome
{
    /// <summary>All of it was handed over and the customer took it.</summary>
    public const string Full = "FULL";

    /// <summary>Less than the authorised amount came out. The difference is put back.</summary>
    public const string Partial = "PARTIAL";

    /// <summary>No note left the machine.</summary>
    public const string None = "NONE";

    /// <summary>It came out, nobody took it, the machine pulled it back in.</summary>
    public const string Retracted = "RETRACTED";
}

/// <summary>
/// The note values this protocol allows inside a denomination breakdown, in kurus.
/// Fixed by the domain model section 2.
/// </summary>
/// <remarks>
/// This list lives in the shared project rather than on either side, because both sides
/// have to agree on it: the terminal builds a breakdown out of these values and the host
/// refuses a breakdown that mentions anything else. A cassette holding a note that is not
/// in this list would be money the host would never authorise, so CassetteTests binds the
/// loaded cassettes to this list - the two cannot drift apart unnoticed.
/// </remarks>
public static class Denominations
{
    /// <summary>200, 100, 50 and 20 lira, in kurus, largest first.</summary>
    public static readonly IReadOnlyList<long> Valid = [20_000, 10_000, 5_000, 2_000];

    /// <summary>True when a note of this value exists in this machine's world.</summary>
    public static bool IsValid(long denomination) => Valid.Contains(denomination);
}

/// <summary>Response codes. See the protocol spec section 5.</summary>
public static class ResponseCode
{
    /// <summary>Approved.</summary>
    public const string Approved = "00";

    /// <summary>Card not recognised.</summary>
    public const string UnknownCard = "14";

    /// <summary>Insufficient funds.</summary>
    public const string InsufficientFunds = "51";

    /// <summary>Wrong PIN.</summary>
    public const string WrongPin = "55";

    /// <summary>PIN tries exhausted - the terminal keeps the card.</summary>
    public const string PinTriesExhausted = "75";

    /// <summary>Invalid transaction: unknown identity, missing field, inconsistent amount.</summary>
    public const string InvalidTransaction = "12";

    /// <summary>Host internal error - the terminal produces a reversal.</summary>
    public const string HostError = "96";

    /// <summary>Host temporarily unable to answer - the terminal produces a reversal.</summary>
    public const string HostUnavailable = "91";

    /// <summary>
    /// Reconcile error: the day's totals do not match, or business is still open on it.
    /// The day does not close. See the protocol spec section 4.7.
    /// </summary>
    public const string ReconcileError = "95";

    /// <summary>
    /// This day has already been closed. Nothing changes; the totals in the answer are
    /// the ones the first close agreed on.
    /// </summary>
    public const string AlreadyReconciled = "98";
}

/// <summary>Liveness check. Carries nothing; its arrival is the whole message.</summary>
public sealed record EchoBody;

/// <summary>
/// PIN verification request. The comparison happens inside the host and only true or
/// false leaves it.
/// </summary>
/// <remarks>
/// Careful: <see cref="Pin"/> must never reach a journal, a log line or an error
/// message. See KARAR-010. Encryption is deliberately out of scope and declared as a
/// known gap in reports/assumptions.md.
/// </remarks>
public sealed record PinVerifyRequestBody(string Pan, string Pin);

/// <summary>PIN verification result. Only the outcome, never the PIN.</summary>
public sealed record PinVerifyResponseBody(string Rc, bool Ok, int RemainingTries);

/// <summary>Balance enquiry request.</summary>
public sealed record BalanceRequestBody(string Pan);

/// <summary>
/// Balance enquiry result. Amounts are in kurus as whole numbers - see KARAR-008.
/// </summary>
/// <remarks>
/// Available and Ledger are equal in Phase 1 and stop being equal in Phase 2, when an
/// authorised withdrawal holds part of the balance before the ledger has moved.
/// </remarks>
public sealed record BalanceResponseBody(string Rc, long Available, long Ledger);

/// <summary>
/// One line of a denomination breakdown: so many notes of one value.
/// </summary>
/// <remarks>
/// The wire names are the short "d" and "n" that the protocol spec section 4.3 fixes, and
/// the C# names are the readable ones. The attributes are the only place those two
/// vocabularies meet; nothing else in the code has to know that "d" means anything.
/// </remarks>
public sealed record DenominationLine(
    [property: JsonPropertyName("d")] long Denomination,
    [property: JsonPropertyName("n")] int Count)
{
    /// <summary>What this line is worth, in kurus.</summary>
    [JsonIgnore]
    public long Value => Denomination * Count;
}

/// <summary>
/// Withdrawal authorisation request. Carries the breakdown the machine intends to hand
/// over, because the note check happens BEFORE the account is touched - instruction
/// section 4.6 and the protocol spec section 4.3.
/// </summary>
/// <remarks>
/// The breakdown is not a courtesy. The host re-adds it and refuses the request when the
/// total is not exactly Amount, so that the side that counts the notes and the side that
/// holds the money cannot believe two different numbers.
/// </remarks>
public sealed record WithdrawalAuthRequestBody(
    string Pan, long Amount, IReadOnlyList<DenominationLine> Denoms);

/// <summary>
/// Withdrawal authorisation result. On approval the account carries a hold of Amount and
/// its ledger has NOT moved - see KARAR-029.
/// </summary>
/// <remarks>
/// AuthId is the transaction key written out (ATM-01/2026-08-25/000104). It is not a
/// second identity: a reversal carries the same trace number, and two identities would
/// only make a day where they disagree possible. It is empty on a refusal, because there
/// is nothing to refer back to.
/// </remarks>
public sealed record WithdrawalAuthResponseBody(
    string Rc, string AuthId, long Available, long Ledger);

/// <summary>
/// What the machine reports after trying to hand cash over. Sent after the fact: the
/// notes have already moved, and this message is what lets the host stop guessing.
/// </summary>
/// <remarks>
/// Dispensed counts what left the cassettes. Retracted counts how much of that came back
/// because nobody took it. The money that actually reached the customer is the difference
/// between the two, and that is the only figure the ledger moves by.
/// </remarks>
public sealed record DispenseAdviceBody(
    string AuthId, string Outcome, long Dispensed, long Retracted);

/// <summary>
/// The host's acknowledgement, carrying the account as it now stands. Until this arrives
/// the machine keeps re-sending the advice (the protocol spec section 4.5).
/// </summary>
public sealed record DispenseAdviceResponseBody(string Rc, long Available, long Ledger);

/// <summary>
/// Take back an authorisation. Sent when the machine cannot say the money reached the
/// customer - including when it simply never heard an answer.
/// </summary>
/// <remarks>
/// A reversal is not a claim that the withdrawal did not happen. It is sent precisely
/// BECAUSE it may have happened and the machine cannot tell (rule 4.1).
/// It carries the same trace number as the authorisation it undoes, which is how the host
/// knows which promise to release, and it is re-sent until acknowledged, because a
/// reversal that is lost leaves the customer short (rule 4.2).
/// </remarks>
public sealed record ReversalRequestBody(string AuthId, long Amount, string Reason);

/// <summary>
/// The host's acknowledgement. Until this arrives the terminal keeps the reversal in its
/// queue - across a reconnection, and across being switched off and on again.
/// </summary>
public sealed record ReversalResponseBody(string Rc, long Available, long Ledger);

/// <summary>
/// The machine has counted the notes and is holding them in escrow; may it?
/// </summary>
/// <param name="Pan">The card. Masked everywhere it is written down.</param>
/// <param name="Counted">What the machine counted, in kurus.</param>
/// <param name="Denoms">The breakdown, so the host can see WHAT it counted.</param>
/// <remarks>
/// Nothing moves on either side because of this message. It exists so that a customer is
/// not asked to confirm a deposit into an account that will refuse it - a refusal AFTER
/// the notes are stacked is a refusal that cannot be honoured, because the paper is
/// already in a drawer (KARAR-038).
/// </remarks>
public sealed record DepositAuthRequestBody(
    string Pan, long Counted, IReadOnlyList<DenominationLine> Denoms);

/// <summary>May it. Or may it not, with a reason.</summary>
public sealed record DepositAuthResponseBody(string Rc, long Available, long Ledger);

/// <summary>
/// What actually happened to the paper. This is the message that moves the ledger.
/// </summary>
/// <param name="Txn">The transaction this is about, as text - for a human reading the record.</param>
/// <param name="Outcome">One of <see cref="DepositOutcome"/>.</param>
/// <param name="Stacked">What reached a recycler drawer, in kurus. This much is credited.</param>
/// <param name="Returned">What was handed back to the customer, in kurus. Credits nothing.</param>
/// <param name="Jammed">What is stuck in the mechanism, in kurus. Credits nothing.</param>
/// <remarks>
/// Sent AFTER the notes have physically moved, never before (KARAR-038). By the time this
/// message exists the outcome is already a fact, which is exactly what makes it safe to
/// send it again for ever until the host acknowledges it (KARAR-040).
/// </remarks>
public sealed record DepositCommitBody(
    string Txn, string Outcome, long Stacked, long Returned, long Jammed);

/// <summary>The host heard it, and says what the account looks like now.</summary>
public sealed record DepositCommitResponseBody(string Rc, long Available, long Ledger);

/// <summary>
/// One business day, added up: what left, what came in, and over how many transactions.
/// </summary>
/// <param name="Withdrawals">Money that reached customers, in kurus.</param>
/// <param name="Deposits">Money that reached a drawer, in kurus.</param>
/// <param name="Count">How many transactions those two figures come from.</param>
/// <remarks>
/// Both sides count the same thing: money that PHYSICALLY moved. An authorisation that
/// was approved and never used, a request that was refused, notes handed back, a reversal
/// - none of them appear here. The rule has to be identical on both sides, because the
/// whole point of exchanging these three numbers is that two independent records produce
/// them and are then compared (KARAR-045).
///
/// Count is carried because two days can share a total and not share a story: 500 lira in
/// one withdrawal and 500 lira in five are different days, and a difference that cancels
/// itself out in the sum would still show up in the count.
/// </remarks>
public sealed record DayTotals(long Withdrawals, long Deposits, int Count)
{
    /// <summary>A day on which nothing moved.</summary>
    public static readonly DayTotals Empty = new(0, 0, 0);
}

/// <summary>
/// Close today, open tomorrow - and here is what I counted. See the protocol spec 4.7.
/// </summary>
/// <param name="NewBizDate">The date the terminal will start using once this is agreed.</param>
/// <param name="Totals">The TERMINAL's own totals for the day being closed.</param>
/// <remarks>
/// The day being closed is not in this body: it is the envelope's BizDate. Writing it in
/// both places would make a day possible on which the two disagree.
/// </remarks>
public sealed record CutoverRequestBody(string NewBizDate, DayTotals Totals);

/// <summary>
/// The host's answer, carrying the HOST's own totals - never a copy of the terminal's.
/// </summary>
/// <param name="Rc">"00" closed, "95" not closed, "98" it was already closed.</param>
/// <param name="Totals">What the host counted for that day, out of its own journal.</param>
/// <param name="Reason">Why it was refused, in words, for a human reading the record.</param>
public sealed record CutoverResponseBody(string Rc, DayTotals Totals, string Reason);
