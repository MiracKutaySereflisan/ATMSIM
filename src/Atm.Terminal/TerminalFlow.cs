// Copyright (c) 2026 Mirac Kutay Sereflisan. MIT lisansi - LICENSE dosyasina bakiniz.
// TerminalFlow.cs
//
// What this file does: it is the part of the terminal that decides. A physical thing
// happened at the panel; this file works out what it means, asks the host when it has
// to, and produces the next picture for the screen.
//
// Why every decision is here and nowhere else: the browser cannot decide (KARAR-024)
// and the host does not know what is on the screen. This is the only place that knows
// both. Keeping it in one file means the whole behaviour of the machine can be read in
// one sitting - and, more importantly, tested without a socket, a browser or a clock.
//
// What this file does NOT know: sockets, WebSockets, HTTP, files. It is handed a way
// to ask the host a question and a way to read a card, and it uses whatever comes
// back. In the demo those are a real TCP connection and a simulated card reader; in a
// test they are functions written in the test itself. That is why "the host did not
// answer" is a line of test code here rather than something to wait for.
//
// The rule that matters most in this phase, and it is the rule stage 2 will break:
// a balance enquiry MOVES NO MONEY. It reads. So when the host does not answer one,
// the terminal may simply say so and return the card. Nothing needs undoing, because
// nothing was done. The moment a withdrawal exists that stops being true - an
// unanswered withdrawal may already have debited the account, and the right response
// is a reversal, not a shrug (rule 4.1). This file is written so that
// the difference is visible rather than assumed: see SendAndRead, where the null
// answer is handled, and note that the comment there says "read-only".

using Atm.Protocol;

namespace Atm.Terminal;

/// <summary>Where the customer currently is.</summary>
public enum TerminalStep
{
    /// <summary>Nobody is at the machine.</summary>
    Idle,

    /// <summary>A card is in and a PIN is being typed.</summary>
    PinEntry,

    /// <summary>The PIN was accepted and the menu is showing.</summary>
    Menu,

    /// <summary>A balance is on the screen.</summary>
    Balance,

    /// <summary>An amount is being chosen or typed.</summary>
    AmountEntry,

    /// <summary>
    /// Cash is at the mouth and the machine is waiting to find out whether a hand takes
    /// it. Nothing about this transaction is settled until it leaves this step.
    /// </summary>
    CashPresented,

    /// <summary>
    /// The deposit slot is open and the machine is waiting for banknotes. Nothing has
    /// been counted yet, so nothing has to be undone if the customer walks away.
    /// </summary>
    DepositCounting,

    /// <summary>
    /// The notes are counted and sitting in the escrow, and the machine is waiting to
    /// find out whether the customer means it. This is the deposit's mirror of
    /// <see cref="CashPresented"/>: the money is inside the machine but it is still the
    /// customer's, and nothing about this transaction is settled until it leaves this
    /// step. The difference is which way it will go - out of the mouth, or into a drawer.
    /// </summary>
    DepositConfirm,

    /// <summary>Something is being told to the customer; the card is on its way out.</summary>
    Message,
}

/// <summary>The terminal's decisions: one physical event in, one picture out.</summary>
public sealed class TerminalFlow
{
    /// <summary>Digits in a PIN on this machine.</summary>
    public const int PinLength = 4;

    /// <summary>How long the terminal waits for the host before giving up on one message.</summary>
    public static readonly TimeSpan HostTimeout = TimeSpan.FromSeconds(20);

    private readonly Func<Envelope, TimeSpan, Envelope?> _ask;
    private readonly IClock _clock;
    private readonly Func<string> _readCard;
    private readonly WithdrawalFlow? _withdrawals;
    private readonly DepositFlow? _deposits;
    private readonly string _terminalId;
    private readonly BusinessDay _day;
    private readonly DemoFaults? _demo;
    private readonly IReadOnlyList<long> _depositDenominations;

    private string _pan = "";
    private string _pin = "";
    private int _stan;
    private string _typedLira = "";
    private bool _typing;
    private WithdrawalHandle? _atTheMouth;
    private DepositHandle? _inEscrow;
    private long _refusedAtTheSlot;
    private DateTimeOffset? _deadline;

    /// <param name="ask">
    /// Asks the host one question and waits for the answer that belongs to it.
    /// Returns null when no answer came - which means "no answer", never "no".
    /// </param>
    /// <param name="clock">The clock. Virtual in tests, real in the demo.</param>
    /// <param name="readCard">The card reader: returns the card number of whatever was inserted.</param>
    /// <param name="withdrawals">
    /// The withdrawal flow this screen drives. When it is left out the menu does not offer
    /// withdrawal at all - deliberately, because a menu key that leads nowhere is a
    /// machine lying about what it can do. The balance-enquiry tests of stage 1 leave it
    /// out and still describe a complete, honest machine.
    /// </param>
    /// <param name="deposits">
    /// The deposit flow this screen drives. Left out, the menu does not offer deposit -
    /// for the same reason withdrawal is hidden when there is no dispenser behind it.
    /// </param>
    /// <param name="terminalId">This machine's identity; part of every transaction key.</param>
    public TerminalFlow(
        Func<Envelope, TimeSpan, Envelope?> ask,
        IClock clock,
        Func<string> readCard,
        WithdrawalFlow? withdrawals = null,
        string terminalId = "ATM-01",
        DepositFlow? deposits = null,
        BusinessDay? day = null,
        DemoFaults? demo = null,
        int startingStan = 0,
        IReadOnlyList<long>? depositDenominations = null)
    {
        _ask = ask;
        _clock = clock;
        _readCard = readCard;
        _withdrawals = withdrawals;
        _deposits = deposits;
        _terminalId = terminalId;

        // Which notes this machine's mouth will take. It is told, not worked out here:
        // the drawers are the terminal's business and the screen only prints what it is
        // given. Empty means "do not say anything", which is what every test that does
        // not care about deposits gets.
        _depositDenominations = depositDenominations ?? [];

        // Where the trace numbers carry on from. It matters only when this machine has
        // run before TODAY: a transaction is identified by terminal + business date +
        // trace number, so a terminal that restarts inside a business day and begins
        // counting from one again produces identities that already mean something else
        // on the host. That was failure A-03 (2026-09-01): the second withdrawal under a
        // reused number was answered with the first one's approval and cash left the
        // machine that no ledger moved for.
        //
        // The number is not stored in a file of its own. It is rebuilt from the journal,
        // for the same reason the balances are (KARAR-047): a counter kept beside the
        // record can disagree with the record, and then there are two answers to "which
        // numbers have been used" and no way to tell which is right.
        _stan = startingStan;

        // The screen must be in the same day as the flows behind it. Taking it from
        // whichever flow is present rather than making the caller pass it three times is
        // deliberate: a caller who passed it twice could pass two different days, and a
        // machine that believes two dates at once writes two days into one journal.
        _day = day ?? withdrawals?.Day ?? deposits?.Day ?? new BusinessDay(clock);
        _demo = demo;
    }

    /// <summary>Where the customer is right now.</summary>
    public TerminalStep Step { get; private set; } = TerminalStep.Idle;

    /// <summary>PIN attempts left on the card that is in the machine.</summary>
    public int RemainingTries { get; private set; } = PinVerifierTries;

    /// <summary>The default number of tries, before the host has said otherwise.</summary>
    public const int PinVerifierTries = 3;

    /// <summary>The picture to show when the machine is switched on.</summary>
    public ScreenView Current { get; private set; } = Welcome();

    /// <summary>
    /// Works out what one physical event means and returns the next picture, or null
    /// when the picture does not change.
    /// </summary>
    public ScreenView? Handle(ScreenEvent happened)
    {
        var wasWaitingUntil = _deadline;
        var next = Decide(happened);

        if (next is null)
        {
            return null;
        }

        // Every picture carries its own patience. Recorded here, in one place, so that no
        // screen can be produced that forgets to start its own countdown.
        _deadline = next.CountdownSeconds is int seconds
            ? _clock.UtcNow.AddSeconds(seconds)
            : null;

        // An operator switch must not give the customer more time. Repainting the same
        // picture would otherwise restart its countdown, and the one screen where that
        // matters is the confirm screen - where silence means no (KARAR-042).
        if (happened.Event == ScreenEventName.Demo)
        {
            _deadline = wasWaitingUntil;
        }

        // Stamped here, on the way out, so that no picture anywhere in this file can be
        // written that forgets to admit a fault is switched on.
        next = next with { Demo = _demo?.Summary ?? "" };

        Current = next;
        return next;
    }

    private ScreenView? Decide(ScreenEvent happened) => happened.Event switch
    {
        // The screen has just connected and has nothing on it. Whatever is going on,
        // the current picture is the answer.
        ScreenEventName.Hello => Current,

        ScreenEventName.Card => OnCard(happened.Value),
        ScreenEventName.Key => OnKey(happened.Value),
        ScreenEventName.Soft => OnSoftKey(happened.Value),
        ScreenEventName.Cash => OnCash(happened.Value),
        ScreenEventName.Notes => OnNotes(happened.Value),
        ScreenEventName.Tick => OnTick(),

        // The operator's switch. The flow does not carry it out - Program.cs applies it to
        // the real line and the real hands - it only records it and repaints, so that the
        // fault appears on the screen instead of living in somebody's memory.
        ScreenEventName.Demo => OnDemo(happened.Value),

        // The receipt slot reports nothing that changes a decision: a receipt that is
        // taken or left behind moves no money. It stays in the vocabulary because the
        // screen still reports it and a contract that hides events is a contract nobody
        // can debug against.
        _ => null,
    };

    /// <summary>
    /// Records an operator switch and repaints whatever is on the screen.
    /// </summary>
    /// <remarks>
    /// Returning the current picture rather than a new one is the point: flipping a switch
    /// must not move the customer's transaction. What changes is only the fault line at the
    /// bottom of the screen, which Handle stamps onto every picture anyway.
    /// </remarks>
    private ScreenView? OnDemo(string command)
    {
        if (_demo is null)
        {
            return null;
        }

        _demo.Apply(command);
        return Current;
    }

    // ---------- the card ----------

    private ScreenView? OnCard(string what)
    {
        if (what == "inserted" && Step == TerminalStep.Idle)
        {
            // The reader tells us which card. The screen never says - it cannot know
            // what is on a magnetic stripe, and a browser that could name a card
            // number would be a browser that could name somebody else's.
            _pan = _readCard();
            _pin = "";
            RemainingTries = PinVerifierTries;
            Step = TerminalStep.PinEntry;
            return PinScreen();
        }

        if (what == "taken" && Step == TerminalStep.Message)
        {
            return EndSession();
        }

        return null;
    }

    // ---------- the keypad ----------

    private ScreenView? OnKey(string key)
    {
        if (key == "cancel")
        {
            // Cancel works everywhere and normally ends the same way: the card comes
            // back. A machine that keeps a card because the customer changed their
            // mind is a machine people stop using.
            //
            // The one place it cannot just do that is the escrow. There are banknotes
            // inside the machine that belong to the person standing in front of it, and
            // "give the card back" would leave them there. So cancel at that step means
            // the same thing the "Vazgeç" key means: give the notes back, then end.
            if (Step == TerminalStep.DepositConfirm)
            {
                return FinishDeposit(confirmed: false);
            }

            return Step == TerminalStep.Idle ? null : ReturnCard("İŞLEM İPTAL EDİLDİ", "Kartınızı alınız.");
        }

        if (Step == TerminalStep.AmountEntry && _typing)
        {
            return OnAmountKey(key);
        }

        if (Step != TerminalStep.PinEntry)
        {
            return null;
        }

        if (key == "correct")
        {
            _pin = _pin.Length > 0 ? _pin[..^1] : "";
            return PinScreen();
        }

        if (key == "enter")
        {
            return _pin.Length == PinLength ? VerifyPin() : PinScreen("Şifreniz dört haneli olmalıdır.");
        }

        if (key.Length == 1 && key[0] >= '0' && key[0] <= '9')
        {
            if (_pin.Length >= PinLength)
            {
                return null;
            }

            _pin += key;
            return PinScreen();
        }

        return null;
    }

    // ---------- the keys beside the screen ----------

    private ScreenView? OnSoftKey(string where) => (Step, where) switch
    {
        (TerminalStep.Menu, "L1") => AskBalance(),
        (TerminalStep.Menu, "L2") => _withdrawals is null ? null : AmountScreen(),
        (TerminalStep.Menu, "L3") => _deposits is null ? null : DepositSlotScreen(),
        (TerminalStep.Menu, "R4") => ReturnCard("İŞLEMİNİZ TAMAMLANDI", "Kartınızı alınız."),

        (TerminalStep.Balance, "L1") => MenuScreen(),
        (TerminalStep.Balance, "R4") => ReturnCard("İŞLEMİNİZ TAMAMLANDI", "Kartınızı alınız."),

        // The quick amounts. They are written here as kurus, because every amount in this
        // project is kurus (KARAR-008) and the one place a lira slips in is the place a
        // hundredfold error is born.
        (TerminalStep.AmountEntry, "L1") => Withdraw(10_000),
        (TerminalStep.AmountEntry, "L2") => Withdraw(20_000),
        (TerminalStep.AmountEntry, "L3") => Withdraw(50_000),
        (TerminalStep.AmountEntry, "R1") => Withdraw(100_000),
        (TerminalStep.AmountEntry, "R2") => Withdraw(200_000),
        (TerminalStep.AmountEntry, "L4") => StartTyping(),
        (TerminalStep.AmountEntry, "R4") => MenuScreen(),

        // The slot is open and empty. Giving up here costs nothing, because nothing has
        // been counted: the machine simply closes the slot and returns the card.
        (TerminalStep.DepositCounting, "R4") => ReturnCard("İŞLEM İPTAL EDİLDİ", "Kartınızı alınız."),

        // The two answers to "is this right?". They are the same call with one word
        // changed, and that word decides whose money the notes in the escrow are.
        (TerminalStep.DepositConfirm, "L1") => FinishDeposit(confirmed: true),
        (TerminalStep.DepositConfirm, "R4") => FinishDeposit(confirmed: false),

        _ => null,
    };

    // ---------- the cash mouth ----------

    private ScreenView? OnCash(string what)
    {
        if (what != "taken" || Step != TerminalStep.CashPresented || _atTheMouth is null)
        {
            return null;
        }

        // A hand took the notes. This is the only event in the whole project that means
        // money has left the machine for good.
        return Settle(takenByCustomer: true);
    }

    // ---------- time passing ----------

    // A second went by at the panel. Whether that MATTERS is decided here, against this
    // machine's own clock - not by the browser that reported it (KARAR-036).
    private ScreenView? OnTick()
    {
        if (_deadline is null || _clock.UtcNow < _deadline)
        {
            return null;
        }

        return Step switch
        {
            // Nobody took the cash. The machine pulls it in, and the money goes into the
            // retract bin - not back into a cassette (rule 4.4).
            TerminalStep.CashPresented => Settle(takenByCustomer: false),

            // The card was returned and left hanging out of the slot. A real machine takes
            // it back rather than leaving it there for the next person.
            TerminalStep.Message => CaptureCard(),

            // Nobody answered the question about the notes in the escrow. Silence is not
            // consent: an unanswered confirmation is treated as "no" and the notes go
            // back out (KARAR-042). The direction matters, and it is the opposite of the
            // withdrawal timeout above - there, unclaimed cash is pulled IN because it is
            // the bank's; here, unconfirmed cash is pushed OUT because it is still the
            // customer's. Reading the two lines side by side is the whole lesson.
            TerminalStep.DepositConfirm => FinishDeposit(confirmed: false),

            // Idle already, but the glass still shows the last thing that happened. The
            // machine is free; the picture just has not caught up. Putting the welcome
            // screen back is the whole of it - no state changes here, because there is
            // no state left to change.
            TerminalStep.Idle => Current.Screen == "idle" ? null : Welcome(),

            // Everything else: the customer walked away mid-session. Nothing has moved, so
            // the card simply comes back.
            _ => ReturnCard("İŞLEMİNİZ ZAMAN AŞIMINA UĞRADI", "Kartınızı alınız."),
        };
    }

    // ---------- talking to the host ----------

    private ScreenView VerifyPin()
    {
        var pin = _pin;

        // The digits are cleared here, before anything can go wrong further down. A
        // PIN that is still in a field when an error path is taken is a PIN that ends
        // up in a log line somebody adds later.
        _pin = "";

        var answer = SendAndRead(MessageType.PinVerifyRequest, new PinVerifyRequestBody(_pan, pin));

        if (answer is null)
        {
            return ReturnCard("İŞLEMİNİZ TAMAMLANAMADI", "Lütfen daha sonra tekrar deneyiniz.");
        }

        var body = answer.Body<PinVerifyResponseBody>();
        RemainingTries = body.RemainingTries;

        if (body.Rc == ResponseCode.Approved)
        {
            Step = TerminalStep.Menu;
            return MenuScreen();
        }

        if (body.Rc == ResponseCode.PinTriesExhausted)
        {
            Step = TerminalStep.Message;
            return new ScreenView
            {
                Screen = "error",
                Title = "KARTINIZA EL KONULDU",
                Lines = new[]
                {
                    "Şifreniz üç kez hatalı girildi.",
                    "Lütfen bankanızla görüşünüz.",
                },
                Card = "captured",
            };
        }

        if (body.Rc == ResponseCode.UnknownCard)
        {
            return ReturnCard("KARTINIZ OKUNAMADI", "Kartınızı alınız.");
        }

        return PinScreen($"Şifreniz hatalı. Kalan hakkınız: {RemainingTries}.");
    }

    private ScreenView AskBalance()
    {
        var answer = SendAndRead(MessageType.BalanceRequest, new BalanceRequestBody(_pan));

        if (answer is null)
        {
            return ReturnCard("İŞLEMİNİZ TAMAMLANAMADI", "Lütfen daha sonra tekrar deneyiniz.");
        }

        var body = answer.Body<BalanceResponseBody>();

        if (body.Rc != ResponseCode.Approved)
        {
            return ReturnCard("İŞLEMİNİZ TAMAMLANAMADI", "Lütfen daha sonra tekrar deneyiniz.");
        }

        Step = TerminalStep.Balance;

        return new ScreenView
        {
            Screen = "balance",
            Title = "BAKİYENİZ",
            Lines = new[]
            {
                "",
                $"Kullanılabilir bakiye : {Money(body.Available)}",
                $"Hesap bakiyesi        : {Money(body.Ledger)}",
            },
            SoftLeft = new string?[] { "Başka işlem", null, null, null },
            SoftRight = new string?[] { null, null, null, "İşlem sonu" },
            Card = "in",
        };
    }

    // Sends one message and waits for the answer that belongs to it.
    //
    // Both messages in this phase are READ-ONLY: they ask the host something and move
    // no money. That is the only reason a null answer can be handled by simply telling
    // the customer and returning the card. From stage 2 a null answer to a withdrawal
    // means the opposite - the host may have debited the account already - and the
    // right response there is a reversal, not a message.
    private Envelope? SendAndRead<TBody>(string type, TBody body) where TBody : notnull
    {
        var key = new TransactionKey(_terminalId, BusinessDate(), NextStan());
        var request = MessageCodec.Envelope(type, key, _clock.UtcNow, body);

        return _ask(request, HostTimeout);
    }

    // ---------- the withdrawal ----------

    /// <summary>Offers the keypad for an amount that is not one of the quick ones.</summary>
    private ScreenView StartTyping()
    {
        _typing = true;
        _typedLira = "";
        return AmountScreen();
    }

    private ScreenView? OnAmountKey(string key)
    {
        if (key == "correct")
        {
            _typedLira = _typedLira.Length > 0 ? _typedLira[..^1] : "";
            return AmountScreen();
        }

        if (key == "enter")
        {
            if (_typedLira.Length == 0)
            {
                return AmountScreen("Tutar giriniz.");
            }

            // Typed in lira, converted here and only here. The rest of the machine has
            // never seen a lira and never will.
            return Withdraw(long.Parse(_typedLira) * 100);
        }

        if (key.Length == 1 && key[0] >= '0' && key[0] <= '9')
        {
            // Six digits of lira is 999.999 TL, more than any cassette set holds. The cap
            // is here so that a leaning finger cannot produce a number long enough to
            // overflow the arithmetic further down.
            if (_typedLira.Length >= 6)
            {
                return null;
            }

            if (_typedLira.Length == 0 && key == "0")
            {
                return null;
            }

            _typedLira += key;
            return AmountScreen();
        }

        return null;
    }

    /// <summary>
    /// Starts one withdrawal and draws whatever it produced.
    /// </summary>
    /// <remarks>
    /// Everything that decides anything happens inside WithdrawalFlow.Begin - the note
    /// count, the authorisation, the reversal when no answer comes. This method only turns
    /// the result into a picture. That separation is what lets the same withdrawal be run
    /// a thousand times from a scenario file with no screen anywhere near it.
    /// </remarks>
    private ScreenView Withdraw(long kurus)
    {
        _typing = false;
        _typedLira = "";

        var started = _withdrawals!.Begin(_pan, kurus, NextStan());

        if (started.Finished is { } finished)
        {
            return finished.Outcome switch
            {
                WithdrawalOutcome.AmountCannotBeDispensed => AmountScreen(
                    "Bu tutar bu makinedeki banknotlarla verilemiyor."),

                WithdrawalOutcome.RefusedByHost => RefusedScreen(finished.Rc),

                // No answer. The customer is told the truth - that it could not be
                // completed - and NOT that it did not happen, because it may have. The
                // reversal is already queued (rule 4.1).
                _ => ReturnCard("İŞLEMİNİZ TAMAMLANAMADI", "Kartınızı alınız."),
            };
        }

        _atTheMouth = started.AtTheMouth;

        if (_atTheMouth!.Presented == 0)
        {
            // The dispenser jammed: the host approved, no note left a cassette. Nothing is
            // waiting to be taken, so this settles immediately - and the host still has to
            // be told, which is what Settle does.
            return Settle(takenByCustomer: false);
        }

        Step = TerminalStep.CashPresented;

        return new ScreenView
        {
            Screen = "cash",
            Title = "PARANIZI ALINIZ",
            Lines = new[]
            {
                "",
                $"Paranız hazırlandı: {Money(_atTheMouth.Presented)}",
                "Lütfen nakit ağzından alınız.",
            },
            Card = "in",
            CashPort = "presenting",
            CountdownSeconds = 30,
        };
    }

    /// <summary>Finishes a withdrawal whose cash is at the mouth, and draws the ending.</summary>
    private ScreenView Settle(bool takenByCustomer)
    {
        var handle = _atTheMouth!;
        var result = _withdrawals!.Complete(handle, takenByCustomer);
        _atTheMouth = null;

        return result.Outcome switch
        {
            WithdrawalOutcome.CashTaken or WithdrawalOutcome.PartialCashTaken =>
                ReceiptScreen(handle, result),

            WithdrawalOutcome.CashNotTaken => Ending(
                "PARANIZ GERİ ALINDI",
                "Paranızı almadığınız için işlem geri alınmıştır.",
                cashPort: "retracted"),

            // No note came out. The host has been told; nothing was posted.
            _ => Ending(
                "İŞLEMİNİZ TAMAMLANAMADI",
                "Nakit verilemedi. Kartınızı alınız.",
                cashPort: "closed"),
        };
    }

    // ---------- the deposit slot ----------

    /// <summary>
    /// Banknotes went into the slot. This is where a deposit actually begins - not at the
    /// menu key, which only opened a hole in the machine.
    /// </summary>
    /// <remarks>
    /// Read this next to <see cref="Withdraw"/> and the reversal of the whole project is
    /// visible in about ten lines. In a withdrawal the machine asks the host FIRST and
    /// moves paper afterwards, so an unanswered question can be undone. Here the paper
    /// moved first - the notes are already inside - so the question the host is asked is
    /// no longer "may I?", it is "will you take these?", and the answer arrives while the
    /// notes can still be pushed back out. That is the whole reason the authorisation
    /// exists in a deposit at all (KARAR-038).
    /// </remarks>
    private ScreenView? OnNotes(string what)
    {
        if (Step != TerminalStep.DepositCounting || _deposits is null)
        {
            // Notes pushed in when no deposit is running. A real machine's shutter is
            // closed, so this cannot physically happen; ignoring it is safer than acting
            // on it, because acting on it would mean crediting money to whoever happens
            // to be at the machine.
            return null;
        }

        var notes = ParseNotes(what);

        if (notes.Count == 0)
        {
            return null;
        }

        var started = _deposits.Begin(_pan, notes, NextStan());
        _refusedAtTheSlot = started.Refused;

        if (started.Finished is { } finished)
        {
            return finished.Ending switch
            {
                // Not one note had a drawer to go to. Everything came straight back out;
                // the host was never even asked.
                DepositEnding.NothingAccepted => DepositEndingScreen(
                    "PARANIZ İADE EDİLDİ",
                    ["Bu makine yatırdığınız banknotları kabul edemiyor.",
                     "Paranızı yatırma ağzından alınız."],
                    depositPort: "returned"),

                DepositEnding.RefusedByHost => DepositEndingScreen(
                    "İŞLEMİNİZ TAMAMLANAMADI",
                    [$"İade edilen tutar: {Money(finished.Returned)}",
                     "Paranızı ve kartınızı alınız."],
                    depositPort: "returned"),

                // No answer. The notes are back out, and unlike an unanswered withdrawal
                // there is nothing hanging: escrow was the customer's throughout.
                _ => DepositEndingScreen(
                    "İŞLEMİNİZ TAMAMLANAMADI",
                    [$"İade edilen tutar: {Money(finished.Returned)}",
                     "Paranızı ve kartınızı alınız."],
                    depositPort: "returned"),
            };
        }

        _inEscrow = started.InEscrow;
        Step = TerminalStep.DepositConfirm;

        return DepositConfirmScreen();
    }

    /// <summary>
    /// Does what the customer decided about the notes in the escrow, and draws the ending.
    /// </summary>
    private ScreenView FinishDeposit(bool confirmed)
    {
        var handle = _inEscrow!;
        var result = _deposits!.Complete(handle, confirmed);
        _inEscrow = null;

        return result.Ending switch
        {
            DepositEnding.Credited => DepositReceiptScreen(handle, result),

            // Some of it reached a drawer and some of it is stuck in the mechanism. The
            // customer is credited for what got there and told plainly about the rest -
            // not given a number the machine cannot back up.
            DepositEnding.PartlyCredited => DepositReceiptScreen(handle, result),

            DepositEnding.Returned => DepositEndingScreen(
                "PARANIZ İADE EDİLDİ",
                [$"İade edilen tutar: {Money(result.Returned)}",
                 "Paranızı ve kartınızı alınız."],
                depositPort: "returned"),

            // Nothing reached a drawer and nothing came back out. This is the one ending
            // where the machine cannot make the customer whole by itself, and it says so
            // instead of pretending otherwise.
            _ => DepositEndingScreen(
                "İŞLEMİNİZ TAMAMLANAMADI",
                ["Paranız makinede kaldı, hesabınıza geçmedi.",
                 $"Söz konusu tutar: {Money(result.Jammed)}",
                 "Lütfen bankanızla görüşünüz."],
                depositPort: "swallowed"),
        };
    }

    /// <summary>
    /// Reads what the panel says was pushed in: "200x2,50x1" means two two-hundreds and
    /// one fifty. Denominations are written in lira here because that is what is printed
    /// on a banknote; they become kurus before they go any further (KARAR-008).
    /// </summary>
    private static IReadOnlyList<NoteBundle> ParseNotes(string what)
    {
        var bundles = new List<NoteBundle>();

        foreach (var part in what.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var halves = part.Split('x');

            if (halves.Length != 2 ||
                !long.TryParse(halves[0], out var lira) ||
                !int.TryParse(halves[1], out var count) ||
                lira <= 0 || count <= 0)
            {
                // A message from the screen that we cannot read means the screen and the
                // terminal no longer agree on the contract. Guessing what the customer
                // pushed in would be guessing with somebody's money.
                throw new InvalidOperationException(
                    $"Yatırma ağzından okunamayan banknot listesi geldi: '{what}'.");
            }

            bundles.Add(new NoteBundle(lira * 100, count));
        }

        return bundles;
    }

    private ScreenView RefusedScreen(string rc) => rc switch
    {
        ResponseCode.InsufficientFunds => AmountScreen("Bakiyeniz bu işlem için yeterli değil."),
        _ => ReturnCard("İŞLEMİNİZ TAMAMLANAMADI", "Kartınızı alınız."),
    };

    /// <summary>Takes back a card that was returned and left in the slot.</summary>
    private ScreenView CaptureCard()
    {
        Step = TerminalStep.Idle;
        _pan = "";
        _pin = "";
        _atTheMouth = null;
        _inEscrow = null;
        _refusedAtTheSlot = 0;
        RemainingTries = PinVerifierTries;

        return new ScreenView
        {
            Screen = "message",
            Title = "KARTINIZ MAKİNEDE KALDI",
            Lines = new[] { "", "Kartınızı almadığınız için makine kartı geri aldı.",
                            "Lütfen bankanızla görüşünüz." },
            Card = "captured",

            // This picture gets a countdown of its own so the machine can put the welcome
            // screen back by itself. Without it the last customer's message stays on the
            // glass until somebody inserts a card - the machine is READY (Step is Idle
            // above) but does not look ready, and an ATM that looks out of order is out of
            // order as far as the next person in the queue is concerned.
            CountdownSeconds = 10,
        };
    }

    // ---------- pictures ----------

    private static ScreenView Welcome() => new()
    {
        Screen = "idle",
        Title = "HOŞ GELDİNİZ",
        Lines = new[] { "", "İşlem yapmak için kartınızı takınız." },
        Card = "out",
    };

    private ScreenView PinScreen(string? warning = null) => new()
    {
        Screen = "pin",
        Title = "ŞİFRENİZİ GİRİNİZ",
        Lines = warning is null
            ? new[] { "Dört haneli şifrenizi giriniz", "ve GİRİŞ tuşuna basınız." }
            : new[] { warning, "", "Şifrenizi giriniz ve GİRİŞ tuşuna basınız." },

        // The count, never the digits. This is the whole of KARAR-024 in one line.
        MaskedLength = _pin.Length,
        Card = "in",
        CountdownSeconds = 30,
    };

    private ScreenView MenuScreen()
    {
        Step = TerminalStep.Menu;

        return new ScreenView
        {
            Screen = "menu",
            Title = "İŞLEM SEÇİNİZ",

            // Only what exists is offered. A key that leads nowhere is a machine lying
            // about what it can do, so each of these appears only when there is something
            // behind it.
            SoftLeft = new string?[]
            {
                "Bakiye sorgu",
                _withdrawals is null ? null : "Para çekme",
                _deposits is null ? null : "Para yatırma",
                null,
            },
            SoftRight = new string?[] { null, null, null, "İşlem sonu" },
            Card = "in",
            CountdownSeconds = 30,
        };
    }

    private ScreenView AmountScreen(string? warning = null)
    {
        Step = TerminalStep.AmountEntry;

        var lines = _typing
            ? new[]
            {
                warning ?? "Tutarı yazınız ve GİRİŞ tuşuna basınız.",
                "",
                $"Tutar: {(_typedLira.Length == 0 ? "___" : _typedLira)} TL",
            }
            : warning is null
                ? new[] { "", "Çekmek istediğiniz tutarı seçiniz." }
                : new[] { warning, "", "Başka bir tutar seçiniz." };

        return new ScreenView
        {
            Screen = "amount",
            Title = "TUTAR SEÇİNİZ",
            Lines = lines,
            SoftLeft = new string?[] { "100 TL", "200 TL", "500 TL", "Diğer tutar" },
            SoftRight = new string?[] { "1.000 TL", "2.000 TL", null, "Vazgeç" },
            Card = "in",
            CountdownSeconds = 30,
        };
    }

    /// <summary>The ending of a withdrawal where a hand took the notes.</summary>
    private ScreenView ReceiptScreen(WithdrawalHandle handle, WithdrawalResult result)
    {
        Step = TerminalStep.Message;

        var partial = result.Outcome == WithdrawalOutcome.PartialCashTaken;

        return new ScreenView
        {
            Screen = "receipt",
            Title = partial ? "TUTARIN BİR KISMI VERİLDİ" : "PARANIZI ALINIZ",
            Lines = partial
                ? new[]
                {
                    "",
                    $"Talep ettiğiniz tutar: {Money(handle.Amount)}",
                    $"Verilen tutar        : {Money(result.CashToCustomer)}",
                    "Aradaki fark hesabınıza yansıtılmayacaktır.",
                }
                : new[] { "", $"Verilen tutar: {Money(result.CashToCustomer)}",
                          "Makbuzunuzu ve kartınızı alınız." },
            ReceiptLines = Receipt(handle, result),
            Card = "returned",
            CashPort = "taken",
            Receipt = "presented",

            // The card is hanging out of the slot. If nobody takes it, the machine takes
            // it back rather than leaving it there for the next person.
            CountdownSeconds = 30,
        };
    }

    /// <summary>
    /// The piece of paper. Composed here because a receipt is a record, and a record the
    /// browser could write is a record that proves nothing.
    /// </summary>
    /// <remarks>
    /// ASSUMPTION: the remaining balance is not printed. A real receipt usually carries
    /// it, and the host does send it back on the dispense advice - but printing it would
    /// mean printing a balance read at a moment that is not the moment the paper says.
    /// Recorded in the assumptions list rather than guessed at.
    /// </remarks>
    private IReadOnlyList<string> Receipt(WithdrawalHandle handle, WithdrawalResult result) =>
    [
        "ŞEREFLİŞAN BANK",
        "PARA ÇEKME",
        "",
        $"Tarih  : {_clock.UtcNow:yyyy-MM-dd HH:mm}",
        $"Terminal: {_terminalId}",
        $"İşlem no: {handle.Key.Stan:D6}",
        $"Kart    : {MessageCodec.MaskPan(handle.Pan)}",
        "",
        $"Talep edilen : {Money(handle.Amount)}",
        $"Verilen      : {Money(result.CashToCustomer)}",
        "",
        "Bu bir simülasyon makbuzudur.",
    ];

    /// <summary>The slot is open and the machine is waiting for banknotes.</summary>
    private ScreenView DepositSlotScreen()
    {
        Step = TerminalStep.DepositCounting;
        _refusedAtTheSlot = 0;

        var lines = new List<string>
        {
            "",
            "Banknotlarınızı yatırma ağzına koyunuz.",
            "Tutarı siz yazmazsınız; makine sayar.",
        };

        // Which notes the mouth will take, said BEFORE the customer pushes them in. A
        // machine can only put a deposited note into a drawer that recycles that
        // denomination; the others come straight back out (CashAcceptor.AcceptIntoEscrow).
        // That refusal is correct, but a customer who is told about it only afterwards
        // reads it as a fault. So the machine says it up front.
        if (_depositDenominations.Count > 0)
        {
            var notes = string.Join(", ", _depositDenominations
                .OrderBy(d => d)
                .Select(d => (d / 100).ToString()));

            lines.Add($"Kabul edilen banknotlar: {notes} TL. Diğerleri iade edilir.");
        }

        return new ScreenView
        {
            Screen = "deposit",
            Title = "PARANIZI YERLEŞTİRİNİZ",
            Lines = lines,
            SoftRight = new string?[] { null, null, null, "Vazgeç" },
            Card = "in",
            DepositPort = "open",
            CountdownSeconds = 60,
        };
    }

    /// <summary>
    /// The notes are counted and waiting in the escrow. The customer has to say yes.
    /// </summary>
    /// <remarks>
    /// Why this screen exists at all, and why it is not a formality: until a key is
    /// pressed here the banknotes are still the customer's property, sitting in a holding
    /// area inside the machine. Skipping this step - counting the notes and crediting them
    /// at once - is the classic deposit mistake (rule 4.7). It looks
    /// friendlier and it removes the customer's last chance to change their mind about
    /// money that is already out of their hand.
    /// </remarks>
    private ScreenView DepositConfirmScreen()
    {
        var lines = new List<string> { "", $"Sayılan tutar: {Money(_inEscrow!.Counted)}" };

        if (_refusedAtTheSlot > 0)
        {
            lines.Add($"Kabul edilmeyen: {Money(_refusedAtTheSlot)} - iade edildi.");
        }

        lines.Add("Hesabınıza geçmesi için ONAYLA tuşuna basınız.");

        return new ScreenView
        {
            Screen = "deposit",
            Title = "TUTARI ONAYLAYINIZ",
            Lines = lines,
            SoftLeft = new string?[] { "Onayla", null, null, null },
            SoftRight = new string?[] { null, null, null, "Vazgeç" },
            Card = "in",
            DepositPort = "counting",
            CountdownSeconds = 30,
        };
    }

    /// <summary>The ending of a deposit where money reached a drawer.</summary>
    private ScreenView DepositReceiptScreen(DepositHandle handle, DepositResult result)
    {
        Step = TerminalStep.Message;

        var partial = result.Ending == DepositEnding.PartlyCredited;

        return new ScreenView
        {
            Screen = "receipt",
            Title = partial ? "TUTARIN BİR KISMI YATIRILDI" : "İŞLEMİNİZ TAMAMLANDI",
            Lines = partial
                ?
                [
                    "",
                    $"Sayılan tutar        : {Money(handle.Counted)}",
                    $"Hesabınıza geçen     : {Money(result.Credited)}",
                    "Kalan tutar makinede kaldı; bankanızla görüşünüz.",
                ]
                :
                [
                    "",
                    $"Hesabınıza geçen tutar: {Money(result.Credited)}",
                    "Makbuzunuzu ve kartınızı alınız.",
                ],
            ReceiptLines = DepositReceipt(handle, result),
            Card = "returned",
            DepositPort = "swallowed",
            Receipt = "presented",
            CountdownSeconds = 30,
        };
    }

    /// <summary>
    /// The deposit receipt. It prints what was COUNTED and what was CREDITED as two
    /// separate lines, even when they are the same number.
    /// </summary>
    /// <remarks>
    /// Printing one number for both would be printing a claim the machine cannot always
    /// make. They come apart whenever the mechanism jams part-way, and the customer's only
    /// evidence that some of their money went in and some of it did not is this piece of
    /// paper. A receipt is worth having exactly to the extent that it can disagree with
    /// itself.
    /// </remarks>
    private IReadOnlyList<string> DepositReceipt(DepositHandle handle, DepositResult result) =>
    [
        "ŞEREFLİŞAN BANK",
        "PARA YATIRMA",
        "",
        $"Tarih  : {_clock.UtcNow:yyyy-MM-dd HH:mm}",
        $"Terminal: {_terminalId}",
        $"İşlem no: {handle.Key.Stan:D6}",
        $"Kart    : {MessageCodec.MaskPan(handle.Pan)}",
        "",
        $"Sayılan      : {Money(handle.Counted)}",
        $"Hesaba geçen : {Money(result.Credited)}",
        "",
        "Bu bir simülasyon makbuzudur.",
    ];

    /// <summary>An ending of a deposit: the card comes back and the deposit slot says why.</summary>
    private ScreenView DepositEndingScreen(string title, string[] lines, string depositPort)
    {
        Step = TerminalStep.Message;
        _pin = "";
        _inEscrow = null;

        return new ScreenView
        {
            Screen = "message",
            Title = title,
            Lines = [.. new[] { "" }.Concat(lines)],
            Card = "returned",
            DepositPort = depositPort,
            CountdownSeconds = 30,
        };
    }

    /// <summary>An ending that is not a receipt: the card comes back and the session closes.</summary>
    private ScreenView Ending(string title, string line, string cashPort)
    {
        Step = TerminalStep.Message;
        _pin = "";

        return new ScreenView
        {
            Screen = "message",
            Title = title,
            Lines = new[] { "", line },
            Card = "returned",
            CashPort = cashPort,
            CountdownSeconds = 30,
        };
    }

    private ScreenView ReturnCard(string title, string line)
    {
        Step = TerminalStep.Message;
        _pin = "";
        _typedLira = "";
        _typing = false;

        return new ScreenView
        {
            Screen = "message",
            Title = title,
            Lines = new[] { "", line },
            Card = "returned",
            CountdownSeconds = 30,
        };
    }

    private ScreenView EndSession()
    {
        // Everything about this customer is dropped here. Nothing is kept between two
        // customers, because the only thing keeping it could do is turn up in the next
        // customer's session.
        Step = TerminalStep.Idle;
        _pan = "";
        _pin = "";
        _typedLira = "";
        _typing = false;
        _atTheMouth = null;
        _inEscrow = null;
        _refusedAtTheSlot = 0;
        RemainingTries = PinVerifierTries;
        return Welcome();
    }

    // ---------- small things ----------

    /// <summary>The business date this terminal stamps messages with.</summary>
    /// <summary>The day this machine is working in. Moved only by a cutover (KARAR-044).</summary>
    public BusinessDay Day => _day;

    /// <summary>The business date every new transaction key gets.</summary>
    public string BusinessDate() => _day.Current;

    private int NextStan() => _stan = _stan >= 999_999 ? 1 : _stan + 1;

    /// <summary>
    /// Writes an amount held in kurus as Turkish money: 250000 becomes "2.500,00 TL".
    /// </summary>
    /// <remarks>
    /// Written by hand rather than left to a culture setting. The build sets
    /// InvariantGlobalization, so asking the runtime for Turkish formatting would give
    /// a different answer on a machine where that setting differed - and an amount
    /// that reads differently on two machines is exactly the kind of thing nobody
    /// notices until it is on a receipt.
    /// </remarks>
    public static string Money(long kurus)
    {
        var negative = kurus < 0;
        var value = Math.Abs(kurus);
        var lira = (value / 100).ToString();
        var kurusPart = (value % 100).ToString("D2");

        var grouped = "";

        for (var i = 0; i < lira.Length; i++)
        {
            if (i > 0 && (lira.Length - i) % 3 == 0)
            {
                grouped += ".";
            }

            grouped += lira[i];
        }

        return $"{(negative ? "-" : "")}{grouped},{kurusPart} TL";
    }
}
