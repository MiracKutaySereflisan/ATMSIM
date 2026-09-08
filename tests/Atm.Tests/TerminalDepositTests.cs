// Copyright (c) 2026 Mirac Kutay Sereflisan. MIT lisansi - LICENSE dosyasina bakiniz.
// TerminalDepositTests.cs
//
// What these tests are for: the screen side of a deposit - what the customer sees, in what
// order, and, above all, WHOSE money the banknotes are at each step.
//
// Read them next to TerminalWithdrawalTests.cs. The two files describe the same machine
// doing opposite things, and every difference between them is a decision somebody had to
// make. The one that matters most is what happens when nobody answers:
//
//   Withdrawal, nobody takes the cash  -> the machine pulls it IN. It is the bank's.
//   Deposit,    nobody confirms        -> the machine pushes it OUT. It is the customer's.
//
// Both are "the customer walked away", both are one tick of a virtual clock, and getting
// them the wrong way round hands somebody else's money to whoever is standing there next.
//
// As in the withdrawal tests, nothing here waits: the clock is virtual, the host is real
// code, and a thirty second timeout is one line (KARAR-036).

using Atm.Audit;
using Atm.Protocol;
using Atm.Terminal;
using Xunit;

namespace Atm.Tests;

public class TerminalDepositTests
{
    private const string Card = "4111111111111111";

    /// <summary>Five hundred lira in hundreds. This machine has a recycler for these.</summary>
    private const string FiveHundred = "100x5";

    /// <summary>A whole machine with a screen on the front of it, deposit included.</summary>
    private sealed class Panel : SimulatedDay
    {
        public Panel()
        {
            Screen = new TerminalFlow(AskHost, Clock, () => Card, Flow, deposits: Deposits);
        }

        public TerminalFlow Screen { get; }

        public ScreenView Send(string @event, string value = "")
        {
            Screen.Handle(new ScreenEvent(@event, value));
            return Screen.Current;
        }

        /// <summary>Card in, PIN typed, PIN accepted. The menu is showing afterwards.</summary>
        public ScreenView SignIn()
        {
            Send(ScreenEventName.Card, "inserted");

            foreach (var digit in "1234")
            {
                Send(ScreenEventName.Key, digit.ToString());
            }

            return Send(ScreenEventName.Key, "enter");
        }

        /// <summary>Signs in and opens the deposit slot.</summary>
        public ScreenView OpenSlot()
        {
            SignIn();
            return Send(ScreenEventName.Soft, "L3");
        }

        /// <summary>Opens the slot and pushes these notes in.</summary>
        public ScreenView PushIn(string notes = FiveHundred)
        {
            OpenSlot();
            return Send(ScreenEventName.Notes, notes);
        }

        /// <summary>Moves the clock past the current screen's patience and reports it.</summary>
        public ScreenView WaitOut()
        {
            Clock.Advance(TimeSpan.FromSeconds(120));
            return Send(ScreenEventName.Tick);
        }

        public void AssertMoneyAddsUp()
        {
            var report = ConservationChecker.Check(Snapshot());
            Assert.True(report.IsClean, report.ToText());
        }
    }

    // ---------- the menu ----------

    [Fact]
    public void TheMenuOffersDepositOnlyWhenThereIsAnAcceptorBehindIt()
    {
        var withSlot = new Panel();
        Assert.Contains(withSlot.SignIn().SoftLeft!, key => key == "Para yatırma");

        // The same screen class, built without a deposit flow: the key is blank rather
        // than present-and-dead. A menu key that does nothing is the machine lying.
        var day = new SimulatedDay();
        var withoutSlot = new TerminalFlow(day.AskHost, day.Clock, () => Card, day.Flow);
        withoutSlot.Handle(new ScreenEvent(ScreenEventName.Card, "inserted"));

        foreach (var digit in "1234")
        {
            withoutSlot.Handle(new ScreenEvent(ScreenEventName.Key, digit.ToString()));
        }

        withoutSlot.Handle(new ScreenEvent(ScreenEventName.Key, "enter"));

        Assert.False(withoutSlot.Current.SoftLeft!.Any(key => key == "Para yatırma"),
            "Arkasında kabul edici olmayan bir makine menüde para yatırma göstermemeli.");
    }

    // ---------- the ordinary deposit ----------

    [Fact]
    public void TheSlotOpensBeforeAnythingIsCounted()
    {
        var atm = new Panel();
        var view = atm.OpenSlot();

        Assert.Equal("deposit", view.Screen);
        Assert.Equal("open", view.DepositPort);

        // Nothing has been counted, so nothing is inside the machine yet.
        Assert.Equal(0, atm.Dispenser.Position.InEscrow);
        atm.AssertMoneyAddsUp();
    }

    [Fact]
    public void CountedNotesWaitInEscrowAndTheAccountHasNotMoved()
    {
        var atm = new Panel();
        var before = atm.LedgerOf(Card);

        var view = atm.PushIn();

        Assert.Equal(TerminalStep.DepositConfirm, atm.Screen.Step);
        Assert.Contains(view.Lines, line => line.Contains("500,00 TL"));
        Assert.Contains(view.SoftLeft!, key => key == "Onayla");

        // The whole reason this screen exists: the notes are inside the machine and the
        // account has not moved. The customer can still walk out with their money.
        Assert.Equal(50_000, atm.Dispenser.Position.InEscrow);
        Assert.Equal(before, atm.LedgerOf(Card));
        atm.AssertMoneyAddsUp();
    }

    [Fact]
    public void ConfirmingCreditsTheAccountAndPrintsWhatWasCountedAndWhatWasCredited()
    {
        var atm = new Panel();
        var before = atm.LedgerOf(Card);

        atm.PushIn();
        var view = atm.Send(ScreenEventName.Soft, "L1");

        Assert.Equal("receipt", view.Screen);
        Assert.Equal(before + 50_000, atm.LedgerOf(Card));
        Assert.Equal(0, atm.Dispenser.Position.InEscrow);

        // Two separate lines on the paper, even though they agree here. They stop
        // agreeing the moment the mechanism jams, and that is the case the receipt is for.
        Assert.Contains(view.ReceiptLines, line => line.StartsWith("Sayılan"));
        Assert.Contains(view.ReceiptLines, line => line.StartsWith("Hesaba geçen"));
        Assert.Contains(view.ReceiptLines, line => line.Contains("PARA YATIRMA"));

        // The card number on the paper is masked, like everywhere else it is written down.
        Assert.False(view.ReceiptLines.Any(line => line.Contains(Card)),
            "Makbuza kart numarası açık yazılmış.");
        atm.AssertMoneyAddsUp();
    }

    // ---------- the three ways a customer says no ----------

    [Fact]
    public void GivingUpAtTheConfirmScreenHandsTheNotesBack()
    {
        var atm = new Panel();
        var before = atm.LedgerOf(Card);

        atm.PushIn();
        var view = atm.Send(ScreenEventName.Soft, "R4");

        Assert.Equal("returned", view.DepositPort);
        Assert.Equal(before, atm.LedgerOf(Card));
        Assert.Equal(0, atm.Dispenser.Position.InEscrow);
        atm.AssertMoneyAddsUp();
    }

    [Fact]
    public void TheCancelKeyAtTheConfirmScreenReturnsTheNotesRatherThanJustTheCard()
    {
        var atm = new Panel();
        var before = atm.LedgerOf(Card);

        atm.PushIn();
        var view = atm.Send(ScreenEventName.Key, "cancel");

        // The trap this test exists for: cancel is handled in one place for the whole
        // machine, and that one place used to mean "give the card back". At this step
        // that would have left the customer's banknotes inside the machine.
        Assert.Equal("returned", view.DepositPort);
        Assert.Equal(before, atm.LedgerOf(Card));
        Assert.Equal(0, atm.Dispenser.Position.InEscrow);
        atm.AssertMoneyAddsUp();
    }

    [Fact]
    public void SilenceAtTheConfirmScreenIsNotConsent()
    {
        var atm = new Panel();
        var before = atm.LedgerOf(Card);

        atm.PushIn();
        var view = atm.WaitOut();

        // The customer walked away without answering. Their notes go back OUT - the
        // opposite direction from a withdrawal nobody collects (KARAR-042).
        Assert.Equal("returned", view.DepositPort);
        Assert.Equal(before, atm.LedgerOf(Card));
        Assert.Equal(0, atm.Dispenser.Position.InEscrow);
        atm.AssertMoneyAddsUp();
    }

    [Fact]
    public void WalkingAwayFromAnEmptySlotCostsNothing()
    {
        var atm = new Panel();
        var before = atm.LedgerOf(Card);

        atm.OpenSlot();
        var view = atm.WaitOut();

        Assert.Equal(TerminalStep.Message, atm.Screen.Step);
        Assert.Equal("returned", view.Card);
        Assert.Equal(before, atm.LedgerOf(Card));
        atm.AssertMoneyAddsUp();
    }

    // ---------- notes this machine cannot keep ----------

    [Fact]
    public void NotesWithNoDrawerAreHandedBackAndTheCustomerIsToldSo()
    {
        var atm = new Panel();

        // Two twenties and a hundred. The twenty cassette on this machine only pays
        // out, so those notes come straight back; the hundred stays.
        var view = atm.PushIn("20x2,100x1");

        Assert.Equal(TerminalStep.DepositConfirm, atm.Screen.Step);
        Assert.Contains(view.Lines, line => line.Contains("Kabul edilmeyen"));
        Assert.Contains(view.Lines, line => line.Contains("40,00 TL"));
        Assert.Equal(10_000, atm.Dispenser.Position.InEscrow);
        atm.AssertMoneyAddsUp();
    }

    [Fact]
    public void AHandfulOfNotesThisMachineCannotTakeAtAllEndsBeforeTheHostIsAsked()
    {
        var atm = new Panel();
        atm.OpenSlot();

        // Counted AFTER signing in, so that the PIN message is not mistaken for a deposit
        // message. The first version of this test counted from zero and "proved" that a
        // deposit talks to the host when what it had actually counted was the PIN check.
        var before = atm.MessagesDelivered;

        var view = atm.Send(ScreenEventName.Notes, "20x3");

        Assert.Equal(TerminalStep.Message, atm.Screen.Step);
        Assert.Equal("returned", view.DepositPort);

        // Not one message went to the host. There was nothing to ask about.
        Assert.Equal(before, atm.MessagesDelivered);
        atm.AssertMoneyAddsUp();
    }

    // ---------- the host ----------

    // NOT WRITTEN, DELIBERATELY: "the host refuses the deposit and the notes come back".
    // With the demo host there is no way to reach that answer FROM THE SCREEN - the only
    // refusal it produces is an unknown card, and an unknown card never gets past the PIN
    // to the menu. The path itself is covered at flow level in DepositTests.cs; driving it
    // through the screen needs fault injection, which arrives in Phase 4. Recorded here
    // rather than faked with a weakened test (rule 9).

    [Fact]
    public void AHostThatNeverAnswersMeansTheNotesComeBackOut()
    {
        var atm = new Panel { LoseAnswersForType = MessageType.DepositAuthRequest };
        var before = atm.LedgerOf(Card);

        var view = atm.PushIn();

        // Unlike an unanswered withdrawal, this is the safe direction: the money never
        // left the customer, so there is nothing to reverse on their side of the glass.
        Assert.Equal(TerminalStep.Message, atm.Screen.Step);
        Assert.Equal("returned", view.DepositPort);
        Assert.Contains(view.Lines, line => line.Contains("500,00 TL"));
        Assert.Equal(before, atm.LedgerOf(Card));
        Assert.Equal(0, atm.Dispenser.Position.InEscrow);
        atm.AssertMoneyAddsUp();
    }

    // ---------- the mechanism ----------

    [Fact]
    public void NotesStuckInTheMechanismAreReportedAsStuckAndNotAsCredited()
    {
        var atm = new Panel();
        var before = atm.LedgerOf(Card);

        atm.PushIn();
        atm.Dispenser.AcceptorFault = AcceptorFault.Jam();
        var view = atm.Send(ScreenEventName.Soft, "L1");

        Assert.Equal("swallowed", view.DepositPort);
        Assert.Contains(view.Lines, line => line.Contains("makinede kaldı"));
        Assert.Contains(view.Lines, line => line.Contains("500,00 TL"));

        // Nothing reached a drawer, so nothing may be credited. The customer is told the
        // truth rather than given a number the machine cannot back up.
        Assert.Equal(before, atm.LedgerOf(Card));
        atm.AssertMoneyAddsUp();
    }

    [Fact]
    public void WhenHalfOfItJamsTheReceiptSaysWhatWentInAndWhatDidNot()
    {
        var atm = new Panel();
        var before = atm.LedgerOf(Card);

        atm.PushIn();
        atm.Dispenser.AcceptorFault = AcceptorFault.StackAtMostThis(30_000);
        var view = atm.Send(ScreenEventName.Soft, "L1");

        Assert.Equal("receipt", view.Screen);
        Assert.Equal(before + 30_000, atm.LedgerOf(Card));

        // The two lines that agreed on a good day now disagree, and that is the point.
        Assert.Contains(view.ReceiptLines, line => line.StartsWith("Sayılan") && line.Contains("500,00"));
        Assert.Contains(view.ReceiptLines, line => line.StartsWith("Hesaba geçen") && line.Contains("300,00"));
        atm.AssertMoneyAddsUp();
    }

    // ---------- what the screen may not do ----------

    [Fact]
    public void NotesPushedInWhenNoDepositIsRunningAreIgnored()
    {
        var atm = new Panel();
        atm.SignIn();

        var before = atm.LedgerOf(Card);
        atm.Send(ScreenEventName.Notes, FiveHundred);

        // The shutter is shut on a real machine, so this cannot physically happen. If it
        // arrives anyway, acting on it would mean crediting money to whoever is at the
        // machine right now.
        Assert.Equal(TerminalStep.Menu, atm.Screen.Step);
        Assert.Equal(before, atm.LedgerOf(Card));
        Assert.Equal(0, atm.Dispenser.Position.InEscrow);
    }

    [Fact]
    public void ANoteListTheTerminalCannotReadIsRefusedLoudly()
    {
        var atm = new Panel();
        atm.OpenSlot();

        // Not a quiet zero. A screen and a terminal that no longer agree on the contract
        // must not carry on guessing with somebody's money.
        Assert.Throws<InvalidOperationException>(
            () => atm.Send(ScreenEventName.Notes, "yüz lira falan"));
    }

    [Fact]
    public void TheAmountOnTheScreenIsTheOneTheMachineCountedNotOneTheCustomerTyped()
    {
        var atm = new Panel();
        atm.OpenSlot();

        // The deposit event carries banknotes, never an amount. There is no path in the
        // vocabulary by which a customer states what they are depositing, and this test
        // fails the day somebody adds one.
        var view = atm.Send(ScreenEventName.Notes, "50x3");

        Assert.Contains(view.Lines, line => line.Contains("150,00 TL"));
        Assert.Equal(15_000, atm.Dispenser.Position.InEscrow);
    }
}
