// Copyright (c) 2026 Mirac Kutay Sereflisan. MIT lisansi - LICENSE dosyasina bakiniz.
// WithdrawalFlow.cs
//
// What this file does: it performs one withdrawal from the terminal's side, from "the
// customer asked for 350 lira" to "the host knows what happened to the cash".
//
// This is the file the whole project points at. Every rule that has been written down
// somewhere else meets here, in one order that cannot be rearranged:
//
//   1. Count the notes BEFORE asking the host (instruction 4.6). An amount this machine
//      cannot build is refused here, with nothing sent and no account touched.
//   2. Ask for authorisation, and treat NO ANSWER as "it may have happened" rather than
//      "it did not" (instruction 4.1). That branch produces a reversal, not a message
//      on the screen.
//   3. Hand the cash over - and then find out separately whether it was taken
//      (instruction 4.4). Presenting is not handing over.
//   4. Tell the host what actually happened, and keep telling it until it answers
//      (instruction 4.2). Cash has already left the machine at this point; a report that
//      is lost leaves money out of a drawer and in nobody's ledger.
//
// The one thing this file must never do is decide that something did not happen because
// it did not hear back. Every "null" from the host in here means "I do not know", and
// every one of them is answered by writing something down rather than by assuming.
//
// What this file does NOT do: draw anything. It returns what happened; the screen flow
// turns that into a picture (Phase 2h). A withdrawal that could only be run by pressing
// buttons could not be run a thousand times in a scenario file.

using Atm.Protocol;

namespace Atm.Terminal;

/// <summary>How one withdrawal ended, in terms the screen can use.</summary>
public enum WithdrawalOutcome
{
    /// <summary>The customer has all of the money they asked for.</summary>
    CashTaken,

    /// <summary>The customer has some of it; the host was told the difference.</summary>
    PartialCashTaken,

    /// <summary>Cash came out and nobody took it. The machine pulled it back in.</summary>
    CashNotTaken,

    /// <summary>No note left the machine. Nothing was posted.</summary>
    NoCashCameOut,

    /// <summary>This machine cannot build this amount out of the notes it holds.</summary>
    AmountCannotBeDispensed,

    /// <summary>The host said no. The reason is in <see cref="WithdrawalResult.Rc"/>.</summary>
    RefusedByHost,

    /// <summary>The host did not answer. A reversal is queued and will not stop.</summary>
    NoAnswerFromHost,
}

/// <summary>What happened, and what the screen has to say about it.</summary>
/// <param name="Outcome">Which of the seven endings this was.</param>
/// <param name="CashToCustomer">What the customer is actually holding, in kurus.</param>
/// <param name="Rc">The host's response code, or empty when the host was never asked.</param>
/// <param name="ReversalQueued">True when a reversal is waiting to be acknowledged.</param>
public sealed record WithdrawalResult(
    WithdrawalOutcome Outcome, long CashToCustomer, string Rc, bool ReversalQueued);

/// <summary>
/// A withdrawal that has been authorised and whose cash is at the mouth, waiting to find
/// out whether a hand takes it.
/// </summary>
/// <param name="Key">Which transaction this is.</param>
/// <param name="Pan">The card, so that the record can carry it masked.</param>
/// <param name="Amount">What the host authorised, in kurus.</param>
/// <param name="Presented">What actually came out of the cassettes, in kurus.</param>
public sealed record WithdrawalHandle(
    TransactionKey Key, string Pan, long Amount, long Presented);

/// <summary>
/// How the first half of a withdrawal ended: either it is over already, or there is cash
/// at the mouth and somebody has to find out whether it gets taken.
/// </summary>
/// <remarks>
/// The two halves exist because presenting cash and having it taken are separated in TIME,
/// not only in name (rule 4.4). A single call that asks a function "did the
/// customer take it?" can only be driven by something that already knows the answer - a
/// test or a scenario file. A screen cannot answer it, because the answer arrives later,
/// as an event, and the machine has to keep drawing and listening in the meantime.
/// So the flow is split exactly at that moment (KARAR-036).
/// </remarks>
/// <param name="Finished">Set when the withdrawal ended before any cash was promised.</param>
/// <param name="AtTheMouth">Set when the host approved; the cash movement is not settled yet.</param>
public sealed record WithdrawalStart(WithdrawalResult? Finished, WithdrawalHandle? AtTheMouth);

/// <summary>One withdrawal, from the terminal's side.</summary>
public sealed class WithdrawalFlow
{
    /// <summary>How long the terminal waits for an authorisation (the protocol spec section 6).</summary>
    public static readonly TimeSpan AuthTimeout = TimeSpan.FromSeconds(30);

    /// <summary>How long it waits for an advice to be acknowledged.</summary>
    public static readonly TimeSpan AdviceTimeout = TimeSpan.FromSeconds(30);

    private readonly Func<Envelope, TimeSpan, Envelope?> _ask;
    private readonly ICashDispenser _dispenser;
    private readonly CassetteSet _cassettes;
    private readonly IClock _clock;
    private readonly IPendingHostMessages _pending;
    private readonly Func<bool> _customerTakesTheCash;
    private readonly ITerminalJournal _journal;
    private readonly string _terminalId;
    private readonly BusinessDay _day;
    private readonly Hardening _hardening;

    /// <param name="ask">
    /// Asks the host one question and waits. Returns null when no answer came - which
    /// means "I do not know", never "no".
    /// </param>
    /// <param name="customerTakesTheCash">
    /// The machine watching its own mouth: true when somebody picked the notes up before
    /// the machine gave up on them. This is a separate question from whether the notes
    /// came out, because they are separate events (rule 4.4).
    /// </param>
    public WithdrawalFlow(
        Func<Envelope, TimeSpan, Envelope?> ask,
        ICashDispenser dispenser,
        CassetteSet cassettes,
        IClock clock,
        IPendingHostMessages pending,
        Func<bool> customerTakesTheCash,
        ITerminalJournal? journal = null,
        string terminalId = "ATM-01",
        BusinessDay? day = null,
        Hardening? hardening = null)
    {
        _ask = ask;
        _dispenser = dispenser;
        _cassettes = cassettes;
        _clock = clock;
        _pending = pending;
        _customerTakesTheCash = customerTakesTheCash;
        _journal = journal ?? new InMemoryTerminalJournal();
        _terminalId = terminalId;
        _day = day ?? new BusinessDay(clock);
        _hardening = hardening ?? Hardening.Full;
    }

    /// <summary>This machine's own record of what it did. Append only.</summary>
    public ITerminalJournal Journal => _journal;

    /// <summary>
    /// Runs one withdrawal from beginning to end, asking the given function whether the
    /// customer took the cash. This is the form a scenario file drives.
    /// </summary>
    public WithdrawalResult Withdraw(string pan, long amount, int stan)
    {
        var started = Begin(pan, amount, stan);

        if (started.Finished is not null)
        {
            return started.Finished;
        }

        var handle = started.AtTheMouth!;

        return Complete(handle, handle.Presented > 0 && _customerTakesTheCash());
    }

    /// <summary>
    /// The first half: count the notes, ask the host, and put the cash at the mouth.
    /// </summary>
    /// <remarks>
    /// Everything in here happens before anybody can have taken anything, and the order is
    /// the one that cannot be rearranged - see the file header.
    /// </remarks>
    public WithdrawalStart Begin(string pan, long amount, int stan)
    {
        var key = new TransactionKey(_terminalId, BusinessDate(), stan);

        // 1. Can this machine even build this amount? Asked here, before the host, so
        // that an amount we cannot pay out never becomes a promise we have to take back.
        var plan = DenominationPlanner.Plan(_cassettes, amount);

        if (!plan.CanDispense && _hardening.NoteCheckBeforeAuthorisation)
        {
            _journal.Append(_clock.UtcNow, key, TerminalEvent.AmountRefusedLocally,
                amount, pan, plan.Refusal.ToString());

            return new WithdrawalStart(new WithdrawalResult(
                WithdrawalOutcome.AmountCannotBeDispensed, 0, Rc: "", ReversalQueued: false),
                AtTheMouth: null);
        }

        // 2. Ask for authorisation.
        var request = MessageCodec.Envelope(MessageType.WithdrawalAuthRequest, key,
            _clock.UtcNow,
            new WithdrawalAuthRequestBody(pan, amount,
                // Without the note check there is nothing to send: a machine that has not
                // worked out which notes it would use has no breakdown to declare, and
                // asks for a number instead (rule 4.6).
                _hardening.NoteCheckBeforeAuthorisation
                    ? plan.Bundles.Select(b => new DenominationLine(b.Denomination, b.Count)).ToList()
                    : []));

        _journal.Append(_clock.UtcNow, key, TerminalEvent.AuthRequested, amount, pan);

        var answer = _ask(request, AuthTimeout);

        if (answer is null)
        {
            // NOT a refusal. The host may have this money held right now, and the only
            // safe thing to do is assume it does. No cash moves and a reversal is queued.
            _journal.Append(_clock.UtcNow, key, TerminalEvent.AuthNoAnswer, amount, pan,
                "cevap yok: gerçekleşmiş olabilir");

            if (_hardening.TimeoutMakesReversal)
            {
                QueueReversal(key, amount, ReversalReason.Timeout);
            }

            return new WithdrawalStart(new WithdrawalResult(
                WithdrawalOutcome.NoAnswerFromHost, 0, Rc: "",
                ReversalQueued: _hardening.TimeoutMakesReversal),
                AtTheMouth: null);
        }

        var authorised = answer.Body<WithdrawalAuthResponseBody>();

        if (authorised.Rc != ResponseCode.Approved)
        {
            _journal.Append(_clock.UtcNow, key, TerminalEvent.AuthRefused, amount, pan,
                authorised.Rc);

            // A real answer, and the answer is no. Nothing was promised, so there is
            // nothing to take back.
            return new WithdrawalStart(new WithdrawalResult(
                WithdrawalOutcome.RefusedByHost, 0, authorised.Rc, ReversalQueued: false),
                AtTheMouth: null);
        }

        // 3a. The money is promised. Hand it over. Whether it is TAKEN is a separate
        // question with a separate answer, and it is asked in Complete.
        _journal.Append(_clock.UtcNow, key, TerminalEvent.AuthApproved, amount, pan);

        // A machine that never checked whether it could build the amount arrives here with
        // a plan that was refused. It still tries, and nothing comes out - which is the
        // whole cost of asking the host first (rule 4.6): the promise is
        // already made when the machine finds out.
        var presented = plan.CanDispense ? _dispenser.Present(plan) : 0;

        if (presented > 0)
        {
            _journal.Append(_clock.UtcNow, key, TerminalEvent.CashPresented, presented, pan);
        }

        return new WithdrawalStart(Finished: null,
            new WithdrawalHandle(key, pan, amount, presented));
    }

    /// <summary>
    /// The second half: settle what happened to the cash and make sure the host hears it.
    /// </summary>
    /// <param name="handle">What <see cref="Begin"/> returned.</param>
    /// <param name="takenByCustomer">
    /// True when a hand picked the notes up. False means nobody did - and the machine
    /// pulls them into the retract bin, where they are neither the customer's nor the
    /// cassettes' again.
    /// </param>
    public WithdrawalResult Complete(WithdrawalHandle handle, bool takenByCustomer)
    {
        var (key, pan, amount, presented) = handle;

        long retracted = 0;

        if (presented > 0 && takenByCustomer)
        {
            var taken = _dispenser.CustomerTakes();
            _journal.Append(_clock.UtcNow, key, TerminalEvent.CashTaken, taken, pan);
        }
        else if (presented > 0)
        {
            retracted = _dispenser.Retract();
            _journal.Append(_clock.UtcNow, key, TerminalEvent.CashRetracted, retracted, pan);
        }

        var outcome = Describe(presented, retracted, amount);

        // 4. Tell the host. From here the cash has already moved, so this message cannot
        // be allowed to get lost - it goes into the queue before it is even sent.
        // What the machine TELLS the host. The hardened answer is what actually happened;
        // the two switches below are the two ways of getting it wrong, and both of them
        // report MORE money as having reached the customer than really did.
        var claimed = _hardening.ReportPartialDispense ? presented : amount;
        var admitted = _hardening.RetractIsNotHandedOver ? retracted : 0;

        var advice = MessageCodec.Envelope(MessageType.DispenseAdvice, key, _clock.UtcNow,
            new DispenseAdviceBody(key.ToString(), outcome, claimed, admitted));

        _journal.Append(_clock.UtcNow, key, TerminalEvent.AdviceSent,
            presented - retracted, pan, outcome);

        var acknowledged = _ask(advice, AdviceTimeout);

        if (acknowledged is null && _hardening.RetryUntilAcknowledged)
        {
            _pending.Add(advice, _clock.UtcNow);
            _journal.Append(_clock.UtcNow, key, TerminalEvent.AdviceQueued,
                presented - retracted, pan, "onay gelmedi, kuyrukta");
        }
        else if (acknowledged is null)
        {
            _journal.Append(_clock.UtcNow, key, TerminalEvent.AdviceQueued,
                presented - retracted, pan, "onay gelmedi - bildirim unutuldu");
        }
        else
        {
            _journal.Append(_clock.UtcNow, key, TerminalEvent.AdviceAcknowledged,
                presented - retracted, pan);
        }

        return new WithdrawalResult(
            Ending(presented, retracted, amount),
            presented - retracted,
            ResponseCode.Approved,
            ReversalQueued: false);
    }

    /// <summary>
    /// Sends everything whose next attempt is due. Called whenever the terminal has a
    /// moment - between customers, and immediately after the line comes back.
    /// </summary>
    /// <remarks>
    /// The order matters: queued messages go BEFORE the next customer's, because they
    /// are about money that has already moved and the next customer's money has not.
    /// This is what the protocol spec section 4.5 calls a late reversal.
    /// </remarks>
    public int SendPending()
    {
        var sent = 0;

        foreach (var pending in _pending.Due(_clock.UtcNow))
        {
            var answer = _ask(pending.Message, AdviceTimeout);
            sent++;

            if (answer is not null)
            {
                _pending.Acknowledge(pending.Message.Key, pending.Message.Type);

                _journal.Append(_clock.UtcNow, pending.Message.Key,
                    pending.Message.Type == MessageType.ReversalRequest
                        ? TerminalEvent.ReversalAcknowledged
                        : TerminalEvent.AdviceAcknowledged,
                    note: $"{pending.Attempts + 1}. denemede onaylandı");
            }
            else
            {
                _pending.Attempted(pending, _clock.UtcNow);
            }
        }

        return sent;
    }

    /// <summary>This machine's business date, as the envelope carries it.</summary>
    /// <summary>The day this machine is working in. Moved only by a cutover (KARAR-044).</summary>
    public BusinessDay Day => _day;

    /// <summary>The business date every new transaction key gets.</summary>
    public string BusinessDate() => _day.Current;

    private void QueueReversal(TransactionKey key, long amount, string reason)
    {
        var reversal = MessageCodec.Envelope(MessageType.ReversalRequest, key, _clock.UtcNow,
            new ReversalRequestBody(key.ToString(), amount, reason));

        // Sent once now, and queued whatever happens to that first attempt: an answer
        // that does not arrive and an answer that arrives look the same from here until
        // it is asked for. SendPending takes it out of the queue when the host answers.
        var answer = _ask(reversal, AuthTimeout);

        if (answer is null && _hardening.RetryUntilAcknowledged)
        {
            _pending.Add(reversal, _clock.UtcNow);
            _journal.Append(_clock.UtcNow, key, TerminalEvent.ReversalQueued, amount,
                note: reason);
        }
        else if (answer is null)
        {
            // Sent, unanswered, forgotten. Instruction section 4.2 in the negative: a
            // reversal that is not retried is a reversal that did not happen, and the hold
            // it was meant to release stays on somebody's money.
            _journal.Append(_clock.UtcNow, key, TerminalEvent.ReversalQueued, amount,
                note: reason + " - ters kayıt unutuldu");
        }
        else
        {
            _journal.Append(_clock.UtcNow, key, TerminalEvent.ReversalAcknowledged, amount,
                note: reason);
        }
    }

    /// <summary>The word for the host: what the machine says happened to the cash.</summary>
    private static string Describe(long presented, long retracted, long authorised)
    {
        if (presented == 0)
        {
            return DispenseOutcome.None;
        }

        if (retracted > 0)
        {
            return DispenseOutcome.Retracted;
        }

        return presented == authorised ? DispenseOutcome.Full : DispenseOutcome.Partial;
    }

    /// <summary>The word for the screen: what the customer experienced.</summary>
    private static WithdrawalOutcome Ending(long presented, long retracted, long authorised)
    {
        if (presented == 0)
        {
            return WithdrawalOutcome.NoCashCameOut;
        }

        if (retracted > 0)
        {
            return WithdrawalOutcome.CashNotTaken;
        }

        return presented == authorised
            ? WithdrawalOutcome.CashTaken
            : WithdrawalOutcome.PartialCashTaken;
    }
}
