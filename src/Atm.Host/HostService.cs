// Copyright (c) 2026 Mirac Kutay Sereflisan. MIT lisansi - LICENSE dosyasina bakiniz.
// HostService.cs
//
// What this file does: it takes one message and returns the answer to it. That is the
// whole of the host's behaviour, and it is deliberately written with no socket in it.
//
// Why no socket: a flow that reads from a network cannot be tested without a network,
// and a test that needs a network is a test that fails for reasons that have nothing
// to do with the money (rule 6a). Phase 1e puts a TCP listener in front
// of this class; the listener's only job will be to carry bytes in and out. Every
// decision that matters happens here, where it can be run a thousand times in a
// millisecond.
//
// The one rule in this file that is easy to miss and expensive to add later: THE SAME
// REQUEST MAY ARRIVE TWICE. Not because anything is broken - because that is what a
// retry is. The terminal asked, heard nothing, and asked again; both copies may reach
// the host. If the host performs the work twice, the customer is debited twice.
//
// So the host remembers the transaction key (terminal + business date + trace number,
// KARAR-009) of everything it has answered, and when a key comes back it returns THE
// ANSWER IT GAVE BEFORE, without doing the work again. Instruction section 4.5 says
// this goes into the model at the start rather than being added later, and it is here
// on the day the host first answers anything - even though nothing it answers today
// moves money. Adding it in Phase 2, after the flows are written, would mean editing
// every one of them.
//
// Note what is stored: the previous ANSWER, not just "this was seen". Replying "you
// already asked" is not a substitute, because the terminal never got the first answer
// - that is why it asked again. It needs the answer, not a scolding.
//
// What is NOT stored: an answer that changed nothing. A refusal moved no money, took no
// try away and opened no promise, so there is nothing to protect from happening twice -
// and remembering it would turn one rejected message into a permanent verdict on that
// trace number. A wrong PIN is remembered, because counting it a second time takes a try
// away from a customer who only guessed once; a malformed advice is not (KARAR-032).
//
// The replay table is keyed by transaction key AND message type, not by transaction key
// alone. The two are not the same thing and the difference is not cosmetic: an
// authorisation, the dispense advice that follows it and the reversal that may follow
// that all carry the SAME trace number on purpose (the protocol spec section 2.1), so a
// table keyed by the transaction alone would greet the advice as "you already asked
// that" and hand back the authorisation's answer. See KARAR-031.
//
// The PIN passes through this class in one direction only. It arrives in the request
// body, goes into PinVerifier.Check, and never reaches the journal, the response or a
// message - see KARAR-010 and JournalEntry, where a PIN field does not exist.

using Atm.Protocol;

namespace Atm.Host;

/// <summary>The bank side's behaviour: one message in, one message out.</summary>
public sealed class HostService
{
    private readonly IAccountStore _accounts;
    private readonly PinVerifier _pins;
    private readonly IJournal _journal;
    private readonly IClock _clock;
    private readonly Dictionary<MessageKey, Envelope> _answered = [];

    // What the remembered answer was an answer TO. A repeat may only be answered from the
    // table when it is the SAME request; see the check in Handle. Kept as the body's raw
    // text because the envelope legitimately differs between retries (send time, retry
    // counter) while the body does not: PendingHostMessages re-sends a message "unchanged
    // apart from its retry count".
    private readonly Dictionary<MessageKey, string> _answeredRequests = [];
    private readonly Dictionary<TransactionKey, OpenAuthorisation> _open = [];
    private readonly Dictionary<TransactionKey, OpenDeposit> _deposits = [];
    private readonly Dictionary<string, DayTotals> _closedDays = [];
    private readonly HashSet<TransactionKey> _reversed = [];
    private readonly Hardening _hardening;

    /// <summary>
    /// Builds a host over the given record - and remembers whatever that record says was
    /// still unfinished.
    /// </summary>
    /// <remarks>
    /// The replay at the end is what makes a restart survivable (KARAR-047). Before it, a
    /// host that was stopped forgot every promise it had made: the hold stayed in the
    /// balance file with nothing left that could ever release it, and a settled business
    /// day became settleable again on different totals. With an empty journal the three
    /// lines do nothing, which is why every test that hands in a fresh record sees exactly
    /// the behaviour it saw before.
    /// </remarks>
    public HostService(IAccountStore accounts, PinVerifier pins, IJournal journal, IClock clock,
        Hardening? hardening = null)
    {
        _accounts = accounts;
        _pins = pins;
        _journal = journal;
        _clock = clock;
        _hardening = hardening ?? Hardening.Full;

        foreach (var item in JournalReplay.OpenAuthorisations(journal.Entries))
        {
            _open[item.Key] = new OpenAuthorisation(item.AccountId, item.Amount);
        }

        foreach (var item in JournalReplay.OpenDeposits(journal.Entries))
        {
            _deposits[item.Key] = new OpenDeposit(item.AccountId, item.Amount);
        }

        foreach (var key in JournalReplay.ReversedTransactions(journal.Entries))
        {
            _reversed.Add(key);
        }

        foreach (var day in JournalReplay.ClosedDays(journal.Entries))
        {
            // The totals are recomputed rather than read out of the line that closed the
            // day. The journal holds every movement of that day, so the same rule that
            // produced the figure the first time produces it again - and a figure stored
            // as text in a note could not be checked against anything.
            _closedDays[day] = HostDayTotals.For(journal.Entries, day);
        }
    }

    /// <summary>
    /// A host wired to the demo data of the domain model section 4.1. The record is kept in
    /// memory unless one is handed in - the running host hands in a file-backed one
    /// (KARAR-013), and every test uses the default.
    /// </summary>
    public static HostService WithDemoData(IClock clock, IJournal? journal = null) => new(
        InMemoryAccountStore.WithDemoAccounts(),
        PinVerifier.WithDemoCards(),
        journal ?? new InMemoryJournal(),
        clock);

    /// <summary>The host's record, for the end-of-day comparison.</summary>
    public IJournal Journal => _journal;

    /// <summary>How many requests were answered from memory instead of being redone.</summary>
    public int ReplayCount { get; private set; }

    /// <summary>
    /// Authorisations that were approved and never closed - money promised to a
    /// withdrawal nobody has reported the end of.
    /// </summary>
    /// <remarks>
    /// This is the number KARAR-029 predicted would exist. Each one of these is a hold
    /// standing against a customer's account with no cash movement to justify it, and the
    /// end-of-day reconciliation has to be able to name them rather than discover a
    /// difference it cannot explain. It is also what makes closing a transaction
    /// observable from outside: without it, "the advice closed this authorisation" and
    /// "the advice was remembered as already answered" look identical to every test.
    /// </remarks>
    public int OpenAuthorisationCount => _open.Count;

    /// <summary>
    /// Which transactions those open holds belong to, so that a reconciliation can name
    /// them instead of reporting a count.
    /// </summary>
    /// <remarks>
    /// A count says "three promises are standing". Only the keys say WHICH three, and
    /// only the keys can be matched against the terminal's record to find out whether a
    /// reversal for them is still on its way - which is the difference between a
    /// difference that is explained and one that is not (KARAR-035).
    /// </remarks>
    public IReadOnlyCollection<TransactionKey> OpenAuthorisations => _open.Keys;

    /// <summary>
    /// Deposits this host has agreed to but has not yet been told the end of.
    /// </summary>
    /// <remarks>
    /// The mirror of an open authorisation, and the difference between the two is worth
    /// saying out loud: an open AUTHORISATION is holding a customer's money, so leaving it
    /// open costs them. An open DEPOSIT holds nothing - the host agreed and then heard
    /// nothing more. What it costs is knowledge: somewhere out there a machine may be
    /// sitting on banknotes this account has not been credited for.
    /// </remarks>
    public IReadOnlyCollection<TransactionKey> OpenDeposits => _deposits.Keys;

    /// <summary>
    /// Deposits whose notes jammed in the machine, in kurus. Never credited; an engineer
    /// has to open the machine before anybody can say whose the money is.
    /// </summary>
    public long JammedInDeposits { get; private set; }

    /// <summary>
    /// Reversals that arrived for a withdrawal the host had already posted.
    /// </summary>
    /// <remarks>
    /// Each of these is a contradiction: the machine says take it back, the record says
    /// the customer already has it. The host acknowledges them - a reversal that is not
    /// acknowledged is re-sent for ever - but it undoes nothing and it counts them here,
    /// because a number that is never zero is a finding and a number that nobody keeps is
    /// a difference discovered at day end with nothing to explain it.
    /// </remarks>
    public int UnexpectedReversalCount { get; private set; }

    /// <summary>Answers one request.</summary>
    public Envelope Handle(Envelope request)
    {
        // A repeat of something already answered gets the same answer back. This is
        // checked before anything else: no counter moves, no account is touched, and
        // above all no work is done a second time.
        // Instruction section 4.5. Keyed on the transaction AND the message type
        // (KARAR-031); without that, an authorisation, its advice and its reversal all
        // look like the same message and the second of them is answered with the first's
        // answer - which is failure A-01, and is what this switch turns back on.
        var messageKey = _hardening.RepeatImmunity
            ? new MessageKey(request.Key, request.Type)
            : new MessageKey(request.Key, "");

        // A transaction that has been reversed is closed for good. Asking for it to be
        // authorised again - the same trace number, a second time - is refused before the
        // replay table is even consulted, because the replay table would answer it with
        // the approval that the reversal has since undone. See KARAR-050; this is the
        // finding scenario C-CI-YETKI was written for.
        if (_hardening.RepeatImmunity && _reversed.Contains(request.Key) && IsAuthorisation(request.Type))
        {
            _journal.Append(_clock.UtcNow, request.Key, request.Type,
                ResponseCode.InvalidTransaction,
                note: "this transaction was reversed; it cannot be authorised again");

            return request.Type == MessageType.WithdrawalAuthRequest
                ? MessageCodec.Envelope(MessageType.WithdrawalAuthResponse, request.Key,
                    _clock.UtcNow,
                    new WithdrawalAuthResponseBody(ResponseCode.InvalidTransaction, "", 0, 0))
                : MessageCodec.Envelope(MessageType.DepositAuthResponse, request.Key,
                    _clock.UtcNow,
                    new DepositAuthResponseBody(ResponseCode.InvalidTransaction, 0, 0));
        }

        if (_answered.TryGetValue(messageKey, out var previous))
        {
            // A repeat is only a repeat when it is the SAME request. Idempotency says
            // "doing this twice must not do it twice"; it does not say "anything arriving
            // under a used trace number gets that number's old answer". Those are two
            // different sentences and treating them as one is failure A-03: a terminal
            // that restarts inside a business day starts its trace numbers again from
            // one, the second transaction under a reused number was answered with the
            // FIRST one's approval, and cash left the machine that no ledger ever moved
            // for. Found on 2026-09-01 by restarting the terminal mid-day.
            //
            // Refusing is the only safe answer here. Performing it would be a second
            // transaction under an identity that already means something else, and
            // replaying would authorise an amount nobody asked for.
            if (_hardening.RepeatImmunity
                && _answeredRequests.TryGetValue(messageKey, out var answeredBody)
                && !string.Equals(answeredBody, RequestFingerprint(request), StringComparison.Ordinal))
            {
                _journal.Append(_clock.UtcNow, request.Key, request.Type,
                    ResponseCode.InvalidTransaction,
                    note: "same transaction id, different request: refused, not replayed");

                return Refuse(request, ResponseCode.InvalidTransaction);
            }

            ReplayCount++;
            _journal.Append(_clock.UtcNow, request.Key, request.Type, previous.Body<RcCarrier>().Rc,
                note: "replay: previous answer returned unchanged");
            return previous;
        }

        var answer = request.Type switch
        {
            // Echo is a liveness check, not a transaction: it carries no money, it is
            // sent every thirty seconds, and remembering each one would grow this
            // dictionary forever while protecting nothing.
            MessageType.EchoRequest => Answer.Forget(Echo(request)),
            MessageType.PinVerifyRequest => Answer.Keep(VerifyPin(request)),
            MessageType.BalanceRequest => Answer.Keep(Balance(request)),
            MessageType.WithdrawalAuthRequest => Authorise(request),
            MessageType.DispenseAdvice => TakeAdvice(request),
            MessageType.ReversalRequest => Reverse(request),
            MessageType.DepositAuthRequest => AuthoriseDeposit(request),
            MessageType.DepositCommitAdvice => CommitDeposit(request),
            MessageType.CutoverRequest => Cutover(request),
            _ => Answer.Forget(Invalid(request)),
        };

        if (answer.Remember)
        {
            _answered[messageKey] = answer.Response;
            _answeredRequests[messageKey] = RequestFingerprint(request);
        }

        return answer.Response;
    }

    /// <summary>The two messages that ASK for money to be committed to a transaction.</summary>
    /// <remarks>
    /// Only these two are refused for a reversed transaction. An advice about a reversed
    /// transaction is already refused for a better reason - there is no open authorisation
    /// to close - and a second reversal has to keep being acknowledged, because a reversal
    /// that stops being answered is a reversal the terminal will send for ever.
    /// </remarks>
    /// <summary>
    /// Marks a transaction as reversed: closed for good, and never authorisable again.
    /// </summary>
    /// <remarks>
    /// KARAR-050. The stored approval for this transaction was true when it was given and
    /// the reversal has made it false; without this mark the replay table would hand that
    /// approval out again, and cash would leave the machine with nothing standing behind
    /// it. The stale answer is deliberately NOT deleted here - deleting it would change
    /// nothing observable, because the guard in Handle refuses the request before the
    /// replay table is consulted, and code that changes nothing observable is code nobody
    /// can test (rule 6b).
    /// </remarks>
    private void MarkReversed(TransactionKey key) => _reversed.Add(key);

    private static bool IsAuthorisation(string type) =>
        type is MessageType.WithdrawalAuthRequest or MessageType.DepositAuthRequest;

    private Envelope Echo(Envelope request) =>
        MessageCodec.Envelope(MessageType.EchoResponse, request.Key, _clock.UtcNow, new EchoBody());

    private Envelope VerifyPin(Envelope request)
    {
        var body = request.Body<PinVerifyRequestBody>();
        var result = _pins.Check(body.Pan, body.Pin);

        // Only the outcome is written down. The PIN itself stops here.
        _journal.Append(_clock.UtcNow, request.Key, request.Type, result.Rc, body.Pan,
            result.Ok ? "pin verified" : $"pin refused, {result.RemainingTries} tries left");

        return MessageCodec.Envelope(MessageType.PinVerifyResponse, request.Key, _clock.UtcNow,
            new PinVerifyResponseBody(result.Rc, result.Ok, result.RemainingTries));
    }

    private Envelope Balance(Envelope request)
    {
        var body = request.Body<BalanceRequestBody>();
        var account = _accounts.FindByPan(body.Pan);

        if (account is null)
        {
            _journal.Append(_clock.UtcNow, request.Key, request.Type, ResponseCode.UnknownCard,
                body.Pan, "card not known to this host");
            return MessageCodec.Envelope(MessageType.BalanceResponse, request.Key, _clock.UtcNow,
                new BalanceResponseBody(ResponseCode.UnknownCard, 0, 0));
        }

        _journal.Append(_clock.UtcNow, request.Key, request.Type, ResponseCode.Approved,
            body.Pan, $"balance answered: {account.AvailableBalance} kurus available");

        return MessageCodec.Envelope(MessageType.BalanceResponse, request.Key, _clock.UtcNow,
            new BalanceResponseBody(ResponseCode.Approved,
                account.AvailableBalance, account.LedgerBalance));
    }

    private Answer Authorise(Envelope request)
    {
        var body = request.Body<WithdrawalAuthRequestBody>();

        // The breakdown is checked before any account is looked up. A request whose
        // notes do not add up to its amount is not a question about an account; it is a
        // broken message, and looking up the card first would only make the journal say
        // whose card it was that sent us nonsense.
        // The host re-adds the breakdown so that the side that counts the notes and the
        // side that holds the money cannot believe two different numbers. With the note
        // check off there is no breakdown to re-add - the machine asked for a number.
        var complaint = _hardening.NoteCheckBeforeAuthorisation ? Complain(body) : null;

        if (complaint is not null)
        {
            return RefuseAuth(request, body, ResponseCode.InvalidTransaction, complaint);
        }

        var account = _accounts.FindByPan(body.Pan);

        if (account is null)
        {
            return RefuseAuth(request, body, ResponseCode.UnknownCard, "card not known to this host");
        }

        // Available, not ledger. If this card already has an authorisation that has not
        // closed, that money has been promised once and cannot be promised again -
        // see KARAR-029.
        if (account.AvailableBalance < body.Amount)
        {
            return RefuseAuth(request, body, ResponseCode.InsufficientFunds,
                $"available {account.AvailableBalance} is short of {body.Amount}",
                account);
        }

        // Approved. The hold rises; the ledger does NOT move. It moves when the machine
        // reports what it actually handed over (the protocol spec section 4.4).
        var held = account with { HoldAmount = account.HoldAmount + body.Amount };
        _accounts.Save(held);

        // Remembered because the advice that closes this authorisation arrives as a
        // separate message and carries only its own numbers. Without this line the host
        // would have to trust the machine's word for how much was authorised - and the
        // machine is exactly the party whose report is in question.
        _open[request.Key] = new OpenAuthorisation(held.AccountId, body.Amount);

        _journal.Append(_clock.UtcNow, request.Key, request.Type, ResponseCode.Approved,
            body.Pan,
            $"authorised, hold now {held.HoldAmount}, ledger unchanged at {held.LedgerBalance}",
            body.Amount, held.AccountId);

        return Answer.Keep(MessageCodec.Envelope(
            MessageType.WithdrawalAuthResponse, request.Key, _clock.UtcNow,
            new WithdrawalAuthResponseBody(ResponseCode.Approved, request.Key.ToString(),
                held.AvailableBalance, held.LedgerBalance)));
    }

    /// <summary>
    /// Closes an authorisation with what the machine says actually happened to the cash.
    /// </summary>
    private Answer TakeAdvice(Envelope request)
    {
        var body = request.Body<DispenseAdviceBody>();

        if (!_open.TryGetValue(request.Key, out var open))
        {
            // Either this transaction was never authorised, or it was already closed -
            // by an earlier advice or by a reversal. Both are refused rather than
            // guessed at: an advice applied twice moves the ledger twice.
            return RefuseAdvice(request, body, ResponseCode.InvalidTransaction,
                "no open authorisation for this transaction");
        }

        var complaint = ComplainAboutAdvice(body, open, request.Key);

        if (complaint is not null)
        {
            return RefuseAdvice(request, body, ResponseCode.InvalidTransaction, complaint);
        }

        var account = _accounts.FindById(open.AccountId)
            ?? throw new InvalidOperationException(
                $"The account behind open authorisation {request.Key} has disappeared.");

        // The one line that moves money. What reached the customer is what left the
        // machine minus what the machine pulled back in - and nothing else. Cash that
        // was retracted is in the retract bin (the domain model section 1); it is neither
        // the customer's nor back in the cassette, and it must not move a ledger.
        var handedOver = body.Dispensed - body.Retracted;

        var closed = account with
        {
            LedgerBalance = account.LedgerBalance - handedOver,
            // The whole hold is released, not the amount handed over. A partial dispense
            // is corrected by giving the difference back, not by leaving it promised.
            HoldAmount = account.HoldAmount - open.Amount,
        };

        _accounts.Save(closed);
        _open.Remove(request.Key);

        _journal.Append(_clock.UtcNow, request.Key, request.Type, ResponseCode.Approved,
            account.Pan,
            $"{body.Outcome}: {handedOver} reached the customer, hold of {open.Amount} released",
            handedOver, closed.AccountId);

        return Answer.Keep(MessageCodec.Envelope(
            MessageType.DispenseAdviceResponse, request.Key, _clock.UtcNow,
            new DispenseAdviceResponseBody(ResponseCode.Approved,
                closed.AvailableBalance, closed.LedgerBalance)));
    }

    /// <summary>
    /// Takes back an authorisation. Always acknowledged, because a reversal that is not
    /// acknowledged is re-sent for ever (the protocol spec section 4.5).
    /// </summary>
    private Answer Reverse(Envelope request)
    {
        var body = request.Body<ReversalRequestBody>();

        if (body.AuthId != request.Key.ToString())
        {
            return RefuseReversal(request, body,
                $"reversal quotes {body.AuthId} but arrived under {request.Key}");
        }

        // A reversal can be taking back either kind of agreement. A deposit the machine
        // could not complete leaves the host holding an open deposit, and an open deposit
        // that is never closed is a difference at day end for money that is safely back in
        // a customer's pocket. Nothing is credited or debited here - a deposit that was
        // agreed and never committed never moved a ledger in the first place.
        if (_deposits.TryGetValue(request.Key, out var deposit))
        {
            _deposits.Remove(request.Key);
            MarkReversed(request.Key);

            var depositAccount = _accounts.FindById(deposit.AccountId);

            _journal.Append(_clock.UtcNow, request.Key, request.Type, ResponseCode.Approved,
                depositAccount?.Pan ?? "",
                $"reversed ({body.Reason}): deposit of {deposit.Counted} was never committed",
                deposit.Counted, deposit.AccountId);

            return Answer.Keep(ReversalAnswer(request, ResponseCode.Approved, depositAccount));
        }

        if (_open.TryGetValue(request.Key, out var open))
        {
            if (body.Amount != open.Amount)
            {
                // The machine and the host disagree about what was promised. Releasing
                // the host's figure would be right by luck; releasing the machine's would
                // leave a difference nobody can name.
                return RefuseReversal(request, body,
                    $"reversal is for {body.Amount} but {open.Amount} is held");
            }

            var account = _accounts.FindById(open.AccountId)
                ?? throw new InvalidOperationException(
                    $"The account behind open authorisation {request.Key} has disappeared.");

            // The promise is released and the ledger is left alone - it never moved.
            var released = account with { HoldAmount = account.HoldAmount - open.Amount };
            _accounts.Save(released);
            _open.Remove(request.Key);
            MarkReversed(request.Key);

            _journal.Append(_clock.UtcNow, request.Key, request.Type, ResponseCode.Approved,
                account.Pan, $"reversed ({body.Reason}): hold of {open.Amount} released",
                open.Amount, open.AccountId);

            return Answer.Keep(ReversalAnswer(request, ResponseCode.Approved, released));
        }

        // Nothing is open. Two very different situations look identical from here, and
        // the difference is what the record has to preserve.
        var alreadyPaid = _answered.TryGetValue(
            new MessageKey(request.Key, MessageType.DispenseAdvice), out var advice)
            && advice.Body<RcCarrier>().Rc == ResponseCode.Approved;

        if (alreadyPaid)
        {
            // The cash already reached the customer and was posted. The host cannot
            // un-hand a banknote, so it does not pretend to: it acknowledges - otherwise
            // the terminal re-sends this for ever - and writes the contradiction down
            // loudly. Phase 4 counts these; a silent acknowledgement here would be the
            // quiet repair rule 6b forbids.
            UnexpectedReversalCount++;

            _journal.Append(_clock.UtcNow, request.Key, request.Type, ResponseCode.Approved,
                note: $"UNEXPECTED reversal ({body.Reason}) for a withdrawal already " +
                      "posted from a dispense advice; nothing was undone",
                amount: body.Amount);

            return Answer.Forget(ReversalAnswer(request, ResponseCode.Approved, account: null));
        }

        // The host never saw this transaction: the authorisation was lost on its way here.
        // This is the ordinary case, not an error - the terminal is doing exactly what it
        // should, and it cannot know that nothing needs undoing.
        _journal.Append(_clock.UtcNow, request.Key, request.Type, ResponseCode.Approved,
            note: $"reversal ({body.Reason}) for a transaction this host never saw; " +
                  "nothing to undo",
            amount: body.Amount);

        return Answer.Forget(ReversalAnswer(request, ResponseCode.Approved, account: null));
    }

    /// <summary>
    /// Agrees to a deposit before the customer is asked to confirm it. Moves nothing.
    /// </summary>
    /// <remarks>
    /// Why this message exists at all, when it changes no balance: because a refusal after
    /// the notes are stacked cannot be honoured. The paper is in a drawer by then and this
    /// host cannot hand it back (KARAR-038). So the question is asked while the money is
    /// still in escrow and still the customer's.
    /// </remarks>
    private Answer AuthoriseDeposit(Envelope request)
    {
        var body = request.Body<DepositAuthRequestBody>();

        if (body.Counted <= 0)
        {
            return RefuseDeposit(request, body, ResponseCode.InvalidTransaction,
                "a deposit of nothing is not a deposit");
        }

        // The breakdown is re-added here, exactly as it is for a withdrawal. The machine
        // says it counted 500 lira; this checks that the notes it lists come to 500 lira.
        var listed = body.Denoms.Sum(d => d.Denomination * d.Count);

        if (listed != body.Counted)
        {
            return RefuseDeposit(request, body, ResponseCode.InvalidTransaction,
                $"the notes listed come to {listed}, not {body.Counted}");
        }

        var account = _accounts.FindByPan(body.Pan);

        if (account is null)
        {
            return RefuseDeposit(request, body, ResponseCode.UnknownCard,
                "card not known to this host");
        }

        // Nothing is held and nothing is credited. A deposit takes money from nobody, so
        // there is nothing to reserve - the only thing this answer decides is whether the
        // customer may be asked to confirm.
        _deposits[request.Key] = new OpenDeposit(account.AccountId, body.Counted);

        _journal.Append(_clock.UtcNow, request.Key, request.Type, ResponseCode.Approved,
            body.Pan,
            $"deposit agreed, {body.Counted} in escrow, ledger unchanged at {account.LedgerBalance}",
            body.Counted, account.AccountId);

        return Answer.Keep(MessageCodec.Envelope(
            MessageType.DepositAuthResponse, request.Key, _clock.UtcNow,
            new DepositAuthResponseBody(ResponseCode.Approved,
                account.AvailableBalance, account.LedgerBalance)));
    }

    /// <summary>
    /// Applies what actually happened to the paper. This is the message that credits.
    /// </summary>
    /// <remarks>
    /// One line does the work, and it is the mirror of the dispense advice:
    ///
    ///     credited = stacked
    ///
    /// Returned money went back to the customer and jammed money is in the mechanism;
    /// neither of them is in a drawer, so neither of them may become a balance. Writing
    /// "credited = counted" here would credit an account for banknotes that never reached
    /// the bank - which is the same mistake as paying out cash that never left the machine,
    /// pointing the other way.
    /// </remarks>
    private Answer CommitDeposit(Envelope request)
    {
        var body = request.Body<DepositCommitBody>();

        if (!_deposits.TryGetValue(request.Key, out var open))
        {
            // The host never agreed to this deposit - the authorisation was lost on its
            // way here, or this machine invented one. Either way the paper has already
            // moved and saying no would leave it in a drawer with nothing on the books.
            // It is acknowledged, refused, and written down loudly.
            return RefuseCommit(request, body, ResponseCode.InvalidTransaction,
                "no deposit was agreed for this transaction");
        }

        var moved = body.Stacked + body.Returned + body.Jammed;

        if (moved != open.Counted)
        {
            return RefuseCommit(request, body, ResponseCode.InvalidTransaction,
                $"the machine accounts for {moved} of a deposit of {open.Counted}");
        }

        var account = _accounts.FindById(open.AccountId)
            ?? throw new InvalidOperationException(
                $"The account behind open deposit {request.Key} has disappeared.");

        var credited = account with { LedgerBalance = account.LedgerBalance + body.Stacked };
        _accounts.Save(credited);
        _deposits.Remove(request.Key);
        JammedInDeposits += body.Jammed;

        _journal.Append(_clock.UtcNow, request.Key, request.Type, ResponseCode.Approved,
            account.Pan,
            $"{body.Outcome}: {body.Stacked} reached a drawer, {body.Returned} went back, " +
            $"{body.Jammed} jammed",
            body.Stacked, credited.AccountId);

        return Answer.Keep(MessageCodec.Envelope(
            MessageType.DepositCommitResponse, request.Key, _clock.UtcNow,
            new DepositCommitResponseBody(ResponseCode.Approved,
                credited.AvailableBalance, credited.LedgerBalance)));
    }

    private Answer RefuseDeposit(Envelope request, DepositAuthRequestBody body,
        string rc, string why)
    {
        _journal.Append(_clock.UtcNow, request.Key, request.Type, rc, body.Pan, why, body.Counted);

        return Answer.Forget(MessageCodec.Envelope(
            MessageType.DepositAuthResponse, request.Key, _clock.UtcNow,
            new DepositAuthResponseBody(rc, 0, 0)));
    }

    private Answer RefuseCommit(Envelope request, DepositCommitBody body, string rc, string why)
    {
        _journal.Append(_clock.UtcNow, request.Key, request.Type, rc, note: why,
            amount: body.Stacked);

        return Answer.Forget(MessageCodec.Envelope(
            MessageType.DepositCommitResponse, request.Key, _clock.UtcNow,
            new DepositCommitResponseBody(rc, 0, 0)));
    }

    private Answer RefuseReversal(Envelope request, ReversalRequestBody body, string why)
    {
        _journal.Append(_clock.UtcNow, request.Key, request.Type,
            ResponseCode.InvalidTransaction, note: why, amount: body.Amount);

        return Answer.Forget(ReversalAnswer(request, ResponseCode.InvalidTransaction, account: null));
    }

    private Envelope ReversalAnswer(Envelope request, string rc, Account? account) =>
        MessageCodec.Envelope(MessageType.ReversalResponse, request.Key, _clock.UtcNow,
            new ReversalResponseBody(rc,
                account?.AvailableBalance ?? 0, account?.LedgerBalance ?? 0));

    /// <summary>
    /// What is wrong with this advice, or null when nothing is. The machine reports what
    /// it did; the host checks that the report is internally possible before believing it.
    /// </summary>
    private static string? ComplainAboutAdvice(
        DispenseAdviceBody body, OpenAuthorisation open, TransactionKey key)
    {
        if (body.AuthId != key.ToString())
        {
            return $"advice quotes {body.AuthId} but arrived under {key}";
        }

        if (body.Dispensed < 0 || body.Retracted < 0)
        {
            return $"negative cash: dispensed {body.Dispensed}, retracted {body.Retracted}";
        }

        if (body.Dispensed > open.Amount)
        {
            // A machine cannot hand over more than it was allowed to. If it says it did,
            // the safe reading is that the message is wrong, not that the money is.
            return $"dispensed {body.Dispensed} is more than the authorised {open.Amount}";
        }

        if (body.Retracted > body.Dispensed)
        {
            return $"retracted {body.Retracted} is more than the dispensed {body.Dispensed}";
        }

        // The outcome word and the numbers have to say the same thing. They are two
        // descriptions of one event, and a machine that disagrees with itself is a
        // machine whose report cannot be used to move a ledger.
        var consistent = body.Outcome switch
        {
            DispenseOutcome.Full => body.Dispensed == open.Amount && body.Retracted == 0,
            DispenseOutcome.Partial => body.Dispensed > 0 && body.Dispensed < open.Amount
                                       && body.Retracted == 0,
            DispenseOutcome.None => body.Dispensed == 0,
            DispenseOutcome.Retracted => body.Dispensed > 0 && body.Retracted == body.Dispensed,
            _ => false,
        };

        return consistent
            ? null
            : $"outcome {body.Outcome} does not match dispensed {body.Dispensed} " +
              $"and retracted {body.Retracted} against an authorised {open.Amount}";
    }

    /// <summary>Refuses an advice: nothing moves, and the reason is written down.</summary>
    private Answer RefuseAdvice(
        Envelope request, DispenseAdviceBody body, string rc, string why)
    {
        _journal.Append(_clock.UtcNow, request.Key, request.Type, rc, note: why,
            amount: body.Dispensed);

        // Forgotten, not remembered: a refused advice moved nothing, and remembering it
        // would mean this transaction could never be advised again - the money would sit
        // in a hold that nothing can release (KARAR-032).
        return Answer.Forget(MessageCodec.Envelope(
            MessageType.DispenseAdviceResponse, request.Key, _clock.UtcNow,
            new DispenseAdviceResponseBody(rc, 0, 0)));
    }

    /// <summary>
    /// What is wrong with this breakdown, or null when nothing is. The order matters
    /// only for the message a human reads afterwards; any one of these refuses.
    /// </summary>
    private static string? Complain(WithdrawalAuthRequestBody body)
    {
        if (body.Amount <= 0)
        {
            return $"amount is not positive: {body.Amount}";
        }

        if (body.Denoms is null || body.Denoms.Count == 0)
        {
            return "no denomination breakdown was sent";
        }

        foreach (var line in body.Denoms)
        {
            if (line.Count <= 0)
            {
                return $"a bundle of {line.Count} notes is not a bundle";
            }

            if (!Denominations.IsValid(line.Denomination))
            {
                return $"no note of {line.Denomination} kurus exists in this machine";
            }
        }

        var bundled = body.Denoms.Sum(line => line.Value);

        // The line this whole method exists for. Two sides that add the same request up
        // differently would leave the difference in nobody's ledger.
        if (bundled != body.Amount)
        {
            return $"breakdown adds up to {bundled}, not {body.Amount}";
        }

        return null;
    }

    /// <summary>
    /// Refuses an authorisation: writes the reason down and answers with a response the
    /// terminal can read the same way as an approval. Nothing is held, nothing moves.
    /// </summary>
    private Answer RefuseAuth(
        Envelope request, WithdrawalAuthRequestBody body, string rc, string why, Account? account = null)
    {
        _journal.Append(_clock.UtcNow, request.Key, request.Type, rc, body.Pan, why, body.Amount);

        // A refusal carries no authId: there is nothing to refer back to, and an
        // identity handed out for a transaction that did not happen is an identity a
        // later reversal could quote. And it is forgotten rather than remembered:
        // nothing moved, so there is nothing to protect from moving twice.
        return Answer.Forget(MessageCodec.Envelope(
            MessageType.WithdrawalAuthResponse, request.Key, _clock.UtcNow,
            new WithdrawalAuthResponseBody(rc, "",
                account?.AvailableBalance ?? 0, account?.LedgerBalance ?? 0)));
    }

    /// <summary>The business days this host has agreed to close, and what they closed on.</summary>
    /// <remarks>
    /// Kept so that a second close of the same day can be told apart from the first. The
    /// replay table cannot do it: its key includes the trace number (KARAR-031), so a
    /// close arriving with a NEW trace number for a day already settled looks like a
    /// brand new request. It is not - and answering it as if it were would let a day be
    /// settled twice on two different sets of totals.
    /// </remarks>
    public IReadOnlyDictionary<string, DayTotals> ClosedDays => _closedDays;

    /// <summary>True when this host has agreed to close that business day.</summary>
    public bool IsClosed(string bizDate) => _closedDays.ContainsKey(bizDate);

    /// <summary>
    /// Close a business day: count this side's own totals, compare, and only then agree.
    /// </summary>
    /// <remarks>
    /// The terminal's totals arrive in the request and are never written down as the
    /// answer. This side counts its own journal and compares (KARAR-045); a host that
    /// stored what it was told would be running a check that cannot fail.
    ///
    /// Two things stop a day closing, and they are different things. Totals that do not
    /// match mean the two records disagree about what happened. Business still open on
    /// this side means the record is not finished yet - there is an authorisation the
    /// host has never heard the end of, and closing over it would settle a day that is
    /// still moving (KARAR-046). Both answer 95; the reason says which.
    /// </remarks>
    private Answer Cutover(Envelope request)
    {
        var body = request.Body<CutoverRequestBody>();
        var closing = request.Key.BizDate;

        if (_closedDays.TryGetValue(closing, out var settled))
        {
            // Not an error and not a repeat of work: a second machine-initiated close of
            // a day that is already settled. Nothing moves, and the totals that come back
            // are the ones the first close agreed on - so the terminal can see whether it
            // is looking at the same day it thinks it is.
            _journal.Append(_clock.UtcNow, request.Key, request.Type,
                ResponseCode.AlreadyReconciled, note: $"{closing} was already closed");

            return Answer.Forget(CutoverAnswer(request, ResponseCode.AlreadyReconciled, settled,
                $"gün {closing} zaten kapatılmış"));
        }

        var open = _open.Keys.Count(k => k.BizDate == closing)
            + _deposits.Keys.Count(k => k.BizDate == closing);

        if (open > 0)
        {
            var why = $"o güne ait {open} işlem hâlâ açık";
            _journal.Append(_clock.UtcNow, request.Key, request.Type,
                ResponseCode.ReconcileError, note: why);

            return Answer.Forget(CutoverAnswer(request, ResponseCode.ReconcileError,
                HostDayTotals.For(_journal.Entries, closing), why));
        }

        var mine = HostDayTotals.For(_journal.Entries, closing);

        if (mine != body.Totals)
        {
            var why =
                $"toplamlar tutmuyor - terminal {body.Totals.Withdrawals}/{body.Totals.Deposits}/" +
                $"{body.Totals.Count}, host {mine.Withdrawals}/{mine.Deposits}/{mine.Count}";

            _journal.Append(_clock.UtcNow, request.Key, request.Type,
                ResponseCode.ReconcileError, note: why);

            return Answer.Forget(CutoverAnswer(request, ResponseCode.ReconcileError, mine, why));
        }

        _closedDays[closing] = mine;

        _journal.Append(_clock.UtcNow, request.Key, request.Type, ResponseCode.Approved,
            note: $"{closing} kapandı: {mine.Withdrawals} çekildi, {mine.Deposits} yatırıldı, " +
                  $"{mine.Count} işlem; yeni gün {body.NewBizDate}");

        return Answer.Keep(CutoverAnswer(request, ResponseCode.Approved, mine, ""));
    }

    private Envelope CutoverAnswer(Envelope request, string rc, DayTotals totals, string reason) =>
        MessageCodec.Envelope(MessageType.CutoverResponse, request.Key, _clock.UtcNow,
            new CutoverResponseBody(rc, totals, reason));

    /// <summary>
    /// What makes two messages the same REQUEST rather than the same transaction: the
    /// body. The envelope is deliberately left out - send time and retry counter differ
    /// between a message and its own retry, and those differences must not make a genuine
    /// retry look like a new request.
    /// </summary>
    private static string RequestFingerprint(Envelope request) =>
        request.Type + "|" + request.Body.GetRawText();

    /// <summary>
    /// Refuses a request in its own language: every request type gets the response type
    /// the terminal is waiting for. Answering a withdrawal with a balance response would
    /// be refused by the terminal's reader and would look, from the outside, exactly like
    /// no answer at all - which is the one thing this project never lets a refusal be.
    /// </summary>
    private Envelope Refuse(Envelope request, string rc) => request.Type switch
    {
        MessageType.WithdrawalAuthRequest => MessageCodec.Envelope(
            MessageType.WithdrawalAuthResponse, request.Key, _clock.UtcNow,
            new WithdrawalAuthResponseBody(rc, "", 0, 0)),
        MessageType.DepositAuthRequest => MessageCodec.Envelope(
            MessageType.DepositAuthResponse, request.Key, _clock.UtcNow,
            new DepositAuthResponseBody(rc, 0, 0)),
        MessageType.DispenseAdvice => MessageCodec.Envelope(
            MessageType.DispenseAdviceResponse, request.Key, _clock.UtcNow,
            new DispenseAdviceResponseBody(rc, 0, 0)),
        MessageType.DepositCommitAdvice => MessageCodec.Envelope(
            MessageType.DepositCommitResponse, request.Key, _clock.UtcNow,
            new DepositCommitResponseBody(rc, 0, 0)),
        MessageType.ReversalRequest => MessageCodec.Envelope(
            MessageType.ReversalResponse, request.Key, _clock.UtcNow,
            new ReversalResponseBody(rc, 0, 0)),
        MessageType.PinVerifyRequest => MessageCodec.Envelope(
            MessageType.PinVerifyResponse, request.Key, _clock.UtcNow,
            new PinVerifyResponseBody(rc, false, 0)),
        MessageType.CutoverRequest => MessageCodec.Envelope(
            MessageType.CutoverResponse, request.Key, _clock.UtcNow,
            new CutoverResponseBody(rc, DayTotals.Empty, "same transaction id, different request")),
        _ => MessageCodec.Envelope(
            MessageType.BalanceResponse, request.Key, _clock.UtcNow,
            new BalanceResponseBody(rc, 0, 0)),
    };

    private Envelope Invalid(Envelope request)
    {
        // A message type this host does not know. It is refused rather than ignored:
        // silence would leave the terminal waiting out its timeout for no reason, and
        // a timeout is the one answer that costs the most to interpret.
        _journal.Append(_clock.UtcNow, request.Key, request.Type, ResponseCode.InvalidTransaction,
            note: $"unknown message type: {request.Type}");

        return MessageCodec.Envelope(MessageType.BalanceResponse, request.Key, _clock.UtcNow,
            new BalanceResponseBody(ResponseCode.InvalidTransaction, 0, 0));
    }

    /// <summary>
    /// Just enough of a body to read the response code out of a stored answer, whatever
    /// its type is. The journal line for a replay needs the code and nothing else.
    /// </summary>
    private sealed record RcCarrier(string Rc);

    /// <summary>A deposit the host has agreed to and not yet heard the end of.</summary>
    /// <summary>
    /// A deposit the host has agreed to and not yet heard the end of, named by ACCOUNT.
    /// </summary>
    /// <remarks>
    /// KARAR-048: it used to carry the card number. The account is the right identity -
    /// it is what the ledger moves - and it is also the only one that can be read back
    /// out of the journal, because the card in the journal is masked.
    /// </remarks>
    private sealed record OpenDeposit(string AccountId, long Counted);

    /// <summary>
    /// An answer, and whether the host should remember having given it.
    /// </summary>
    /// <remarks>
    /// KARAR-032. The replay table exists to stop work being done twice; an answer that
    /// did no work has nothing to protect. Every handler has to say which kind it
    /// produced, so that "should this be remembered" is answered where the work happens
    /// rather than guessed at afterwards from the response code.
    /// </remarks>
    private readonly record struct Answer(Envelope Response, bool Remember)
    {
        /// <summary>Something changed: remember this answer and repeat it on a retry.</summary>
        public static Answer Keep(Envelope response) => new(response, true);

        /// <summary>Nothing changed: answer a retry afresh.</summary>
        public static Answer Forget(Envelope response) => new(response, false);
    }

    /// <summary>
    /// An authorisation the host has approved and not yet closed: whose card, how much.
    /// </summary>
    /// <remarks>
    /// This is the host's memory of what it promised. The advice that closes it carries
    /// its own figures, and the whole point of keeping this record is to check those
    /// figures against something the machine did not supply.
    /// </remarks>
    private sealed record OpenAuthorisation(string AccountId, long Amount);

    /// <summary>
    /// What makes two arrivals the same message: the transaction AND the message type.
    /// </summary>
    /// <remarks>
    /// KARAR-031. The transaction alone is not enough, because an authorisation, its
    /// dispense advice and a reversal deliberately share one trace number - that shared
    /// number is how a reversal says which withdrawal it undoes. Keying the replay table
    /// on the transaction alone would make the advice look like a repeat of the
    /// authorisation, and the host would answer it with the authorisation's answer
    /// without ever moving the ledger.
    /// </remarks>
    private readonly record struct MessageKey(TransactionKey Key, string Type);
}
