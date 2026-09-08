// Copyright (c) 2026 Mirac Kutay Sereflisan. MIT lisansi - LICENSE dosyasina bakiniz.
// TerminalWithdrawalTests.cs
//
// What these tests are for: the screen side of a withdrawal - what the customer sees, in
// what order, and what the machine does when they walk away.
//
// The one thing worth understanding before reading them: the flow is driven by EVENTS,
// exactly as the browser drives it. A key press is an event, a hand taking the notes is
// an event, and a second passing is an event. Nothing here waits for anything. A thirty
// second timeout is tested by moving a virtual clock forward and sending one tick, which
// is why the whole file runs in milliseconds (KARAR-036).
//
// The second thing: every one of these tests ends by asking whether the money still adds
// up. A screen that shows the right words while the cash and the ledger drift apart is
// worse than one that shows the wrong words, because it is convincing.

using Atm.Audit;
using Atm.Host;
using Atm.Protocol;
using Atm.Terminal;
using Xunit;

namespace Atm.Tests;

public class TerminalWithdrawalTests
{
    private const string Card = "4111111111111111";

    /// <summary>A whole machine with a screen on the front of it.</summary>
    private sealed class Panel : SimulatedDay
    {
        public Panel()
        {
            Screen = new TerminalFlow(AskHost, Clock, () => Card, Flow);
        }

        public TerminalFlow Screen { get; }

        /// <summary>Sends one event and returns whatever the screen shows afterwards.</summary>
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

        /// <summary>Moves the clock past the current screen's patience and reports it.</summary>
        public ScreenView WaitOut()
        {
            Clock.Advance(TimeSpan.FromSeconds(60));
            return Send(ScreenEventName.Tick);
        }

        public void AssertMoneyAddsUp()
        {
            var report = ConservationChecker.Check(Snapshot());
            Assert.True(report.IsClean, report.ToText());
        }
    }

    [Fact]
    public void TheMenuOffersWithdrawalOnlyWhenThereIsOneBehindIt()
    {
        var panel = new Panel();
        Assert.Equal("Para çekme", panel.SignIn().SoftLeft[1]);

        // The same screen flow with nothing behind it does not offer a key that leads
        // nowhere: a menu that lies about what the machine does is worse than a short one.
        var day = new SimulatedDay();
        var bare = new TerminalFlow(day.AskHost, day.Clock, () => Card);
        bare.Handle(new ScreenEvent(ScreenEventName.Card, "inserted"));

        foreach (var digit in "1234")
        {
            bare.Handle(new ScreenEvent(ScreenEventName.Key, digit.ToString()));
        }

        bare.Handle(new ScreenEvent(ScreenEventName.Key, "enter"));

        Assert.Equal(null, bare.Current.SoftLeft[1]);
    }

    [Fact]
    public void AQuickAmountPutsTheCashAtTheMouthAndNothingIsSettledYet()
    {
        var panel = new Panel();
        panel.SignIn();
        panel.Send(ScreenEventName.Soft, "L2");

        var screen = panel.Send(ScreenEventName.Soft, "L3");

        Assert.Equal("cash", screen.Screen);
        Assert.Equal("presenting", screen.CashPort);
        Assert.Equal(TerminalStep.CashPresented, panel.Screen.Step);

        // The notes have left the cassettes and are at the mouth - and nobody has them.
        Assert.Equal(50_000, panel.Dispenser.Position.AtTheMouth);
        Assert.Equal(0, panel.Dispenser.Position.WithCustomers);

        // Nothing has been posted: the ledger only moves when the machine knows what
        // happened to the paper.
        Assert.Equal(250_000, panel.LedgerOf(Card));
        panel.AssertMoneyAddsUp();
    }

    [Fact]
    public void WhenAHandTakesTheNotesTheReceiptComesOutAndTheLedgerMoves()
    {
        var panel = new Panel();
        panel.SignIn();
        panel.Send(ScreenEventName.Soft, "L2");
        panel.Send(ScreenEventName.Soft, "L3");

        var screen = panel.Send(ScreenEventName.Cash, "taken");

        Assert.Equal("receipt", screen.Screen);
        Assert.Equal("taken", screen.CashPort);
        Assert.Equal("presented", screen.Receipt);
        Assert.Equal("returned", screen.Card);

        Assert.Equal(50_000, panel.Dispenser.Position.WithCustomers);
        Assert.Equal(200_000, panel.LedgerOf(Card));
        Assert.Equal(0, panel.Host.OpenAuthorisationCount);
        panel.AssertMoneyAddsUp();
    }

    [Fact]
    public void TheReceiptNeverCarriesAWholeCardNumber()
    {
        var panel = new Panel();
        panel.SignIn();
        panel.Send(ScreenEventName.Soft, "L2");
        panel.Send(ScreenEventName.Soft, "L3");

        var receipt = string.Join("\n", panel.Send(ScreenEventName.Cash, "taken").ReceiptLines);

        Assert.Contains("ŞEREFLİŞAN BANK", receipt);
        Assert.Contains("500,00 TL", receipt);
        Assert.False(receipt.Contains(Card),
            $"Makbuzda tam kart numarası var:\n{receipt}");
    }

    [Fact]
    public void CashThatNobodyTakesIsPulledBackInAndTheAccountIsLeftAlone()
    {
        var panel = new Panel();
        panel.SignIn();
        panel.Send(ScreenEventName.Soft, "L2");
        panel.Send(ScreenEventName.Soft, "L3");

        var screen = panel.WaitOut();

        Assert.Equal("retracted", screen.CashPort);
        Assert.Equal("PARANIZ GERİ ALINDI", screen.Title);

        // The notes are in the retract bin: neither the customer's nor the cassettes'.
        Assert.Equal(50_000, panel.Dispenser.Position.Retracted);
        Assert.Equal(0, panel.Dispenser.Position.WithCustomers);
        Assert.Equal(0, panel.Dispenser.Position.AtTheMouth);
        Assert.Equal(250_000, panel.LedgerOf(Card));
        Assert.Equal(0, panel.Host.OpenAuthorisationCount);
        panel.AssertMoneyAddsUp();
    }

    [Fact]
    public void AnAmountThisMachineCannotBuildNeverLeavesTheAmountScreen()
    {
        var panel = new Panel();
        panel.SignIn();
        panel.Send(ScreenEventName.Soft, "L2");

        // 30 lira: impossible out of 200, 100, 50 and 20 notes.
        panel.Send(ScreenEventName.Soft, "L4");
        panel.Send(ScreenEventName.Key, "3");
        panel.Send(ScreenEventName.Key, "0");
        var screen = panel.Send(ScreenEventName.Key, "enter");

        Assert.Equal("amount", screen.Screen);
        Assert.Contains("verilemiyor", string.Join(" ", screen.Lines));

        // The host was never asked. Nothing to refuse, nothing to take back.
        Assert.Equal(0, panel.Host.Journal.Entries
            .Count(e => e.Type == MessageType.WithdrawalAuthRequest));
        panel.AssertMoneyAddsUp();
    }

    [Fact]
    public void ATypedAmountGoesThroughLikeAnyOther()
    {
        var panel = new Panel();
        panel.SignIn();
        panel.Send(ScreenEventName.Soft, "L2");
        panel.Send(ScreenEventName.Soft, "L4");

        foreach (var digit in "350")
        {
            panel.Send(ScreenEventName.Key, digit.ToString());
        }

        var screen = panel.Send(ScreenEventName.Key, "enter");

        Assert.Equal("cash", screen.Screen);
        Assert.Equal(35_000, panel.Dispenser.Position.AtTheMouth);
        panel.AssertMoneyAddsUp();
    }

    [Fact]
    public void TheTypedAmountRefusesALeadingZeroAndStopsAtSixDigits()
    {
        var panel = new Panel();
        panel.SignIn();
        panel.Send(ScreenEventName.Soft, "L2");
        panel.Send(ScreenEventName.Soft, "L4");

        panel.Send(ScreenEventName.Key, "0");
        Assert.Contains("___", string.Join(" ", panel.Screen.Current.Lines));

        foreach (var digit in "12345678")
        {
            panel.Send(ScreenEventName.Key, digit.ToString());
        }

        Assert.Contains("123456 TL", string.Join(" ", panel.Screen.Current.Lines));
    }

    [Fact]
    public void AnAmountBiggerThanTheBalanceComesBackAsAWarningNotAsAReturnedCard()
    {
        var panel = new Panel();
        panel.SignIn();
        panel.Send(ScreenEventName.Soft, "L2");

        // 2000 lira against a 2500 lira balance is fine; ask twice and the second is not.
        panel.Send(ScreenEventName.Soft, "R2");
        panel.Send(ScreenEventName.Cash, "taken");
        panel.Send(ScreenEventName.Card, "taken");

        panel.SignIn();
        panel.Send(ScreenEventName.Soft, "L2");
        var screen = panel.Send(ScreenEventName.Soft, "R2");

        Assert.Equal("amount", screen.Screen);
        Assert.Contains("yeterli değil", string.Join(" ", screen.Lines));
        panel.AssertMoneyAddsUp();
    }

    [Fact]
    public void WhenTheHostDoesNotAnswerTheCustomerIsToldItCouldNotBeCompletedNotThatItDidNotHappen()
    {
        var panel = new Panel();
        panel.SignIn();
        panel.Send(ScreenEventName.Soft, "L2");

        panel.LoseAnswersForType = MessageType.WithdrawalAuthRequest;
        var screen = panel.Send(ScreenEventName.Soft, "L3");

        Assert.Equal("İŞLEMİNİZ TAMAMLANAMADI", screen.Title);

        // Not a single note moved. The host DID hold the money - the answer is what went
        // missing - and the reversal that released it went out immediately, because only
        // the authorisation's answer was being lost.
        Assert.Equal(0, panel.Dispenser.Position.AtTheMouth);
        Assert.Equal(0, panel.Pending.Count);
        Assert.Equal(0, panel.Host.OpenAuthorisationCount);
        Assert.Equal(1, panel.Host.Journal.Entries
            .Count(e => e.Type == MessageType.ReversalRequest && e.Rc == ResponseCode.Approved));
        Assert.Equal(250_000, panel.LedgerOf(Card));
        panel.AssertMoneyAddsUp();
    }

    [Fact]
    public void APartialDispenseSaysSoOnTheScreenAndPostsOnlyWhatCameOut()
    {
        var panel = new Panel();
        panel.SignIn();
        panel.Send(ScreenEventName.Soft, "L2");

        panel.Dispenser.Fault = DispenserFault.PresentAtMostThis(20_000);
        panel.Send(ScreenEventName.Soft, "L3");

        var screen = panel.Send(ScreenEventName.Cash, "taken");

        Assert.Equal("TUTARIN BİR KISMI VERİLDİ", screen.Title);
        Assert.Equal(20_000, panel.Dispenser.Position.WithCustomers);
        Assert.Equal(230_000, panel.LedgerOf(Card));
        panel.AssertMoneyAddsUp();
    }

    [Fact]
    public void AJammedDispenserSettlesImmediatelyAndTellsTheHost()
    {
        var panel = new Panel();
        panel.SignIn();
        panel.Send(ScreenEventName.Soft, "L2");

        panel.Dispenser.Fault = DispenserFault.Jam();
        var screen = panel.Send(ScreenEventName.Soft, "L3");

        Assert.Equal("İŞLEMİNİZ TAMAMLANAMADI", screen.Title);
        Assert.Equal("closed", screen.CashPort);

        // Nothing came out, the account is untouched, and no promise is left standing -
        // which is only true because the host was told rather than left guessing.
        Assert.Equal(250_000, panel.LedgerOf(Card));
        Assert.Equal(0, panel.Host.OpenAuthorisationCount);
        panel.AssertMoneyAddsUp();
    }

    [Fact]
    public void WalkingAwayAtTheAmountScreenReturnsTheCardAndMovesNothing()
    {
        var panel = new Panel();
        panel.SignIn();
        panel.Send(ScreenEventName.Soft, "L2");

        var screen = panel.WaitOut();

        Assert.Equal("İŞLEMİNİZ ZAMAN AŞIMINA UĞRADI", screen.Title);
        Assert.Equal("returned", screen.Card);
        Assert.Equal(0, panel.Host.Journal.Entries
            .Count(e => e.Type == MessageType.WithdrawalAuthRequest));
        panel.AssertMoneyAddsUp();
    }

    [Fact]
    public void ACardLeftHangingOutOfTheSlotIsTakenBackIn()
    {
        var panel = new Panel();
        panel.SignIn();
        panel.Send(ScreenEventName.Soft, "R4");

        var screen = panel.WaitOut();

        Assert.Equal("captured", screen.Card);
        Assert.Equal(TerminalStep.Idle, panel.Screen.Step);
    }

    [Fact]
    public void ATickThatArrivesBeforeThePatienceRunsOutChangesNothing()
    {
        // The browser sends one of these every second. If a tick could end a session by
        // itself, every screen in the machine would last exactly one second.
        var panel = new Panel();
        panel.SignIn();
        panel.Send(ScreenEventName.Soft, "L2");

        panel.Clock.Advance(TimeSpan.FromSeconds(5));
        var screen = panel.Send(ScreenEventName.Tick);

        Assert.Equal("amount", screen.Screen);
    }

    [Fact]
    public void EachNewScreenStartsItsOwnCountdown()
    {
        // Found by sabotage: the deadline was being written once and never refreshed, so
        // every later screen inherited the first one's. A session would end thirty seconds
        // after the card went in no matter what the customer was doing - including while
        // their cash was sitting at the mouth, which would retract money out of somebody's
        // hand. Patience belongs to the screen that is showing, not to the session.
        var panel = new Panel();
        panel.SignIn();

        panel.Clock.Advance(TimeSpan.FromSeconds(25));
        Assert.Equal("menu", panel.Send(ScreenEventName.Tick).Screen);

        panel.Send(ScreenEventName.Soft, "L2");
        panel.Clock.Advance(TimeSpan.FromSeconds(20));

        // Forty five seconds since the menu appeared, twenty since this screen did.
        Assert.Equal("amount", panel.Send(ScreenEventName.Tick).Screen);
    }

    [Fact]
    public void CashAtTheMouthGetsItsOwnThirtySecondsHoweverLongTheCustomerTookBeforeIt()
    {
        var panel = new Panel();
        panel.SignIn();
        panel.Send(ScreenEventName.Soft, "L2");

        // The customer dithers over the amount for twenty five seconds.
        panel.Clock.Advance(TimeSpan.FromSeconds(25));
        panel.Send(ScreenEventName.Tick);

        panel.Send(ScreenEventName.Soft, "L3");
        panel.Clock.Advance(TimeSpan.FromSeconds(20));

        // The notes are still there. A machine that retracted here would be pulling money
        // back before the customer had a chance to reach for it.
        Assert.Equal("cash", panel.Send(ScreenEventName.Tick).Screen);
        Assert.Equal(50_000, panel.Dispenser.Position.AtTheMouth);
        panel.AssertMoneyAddsUp();
    }

    [Fact]
    public void CancelDuringAmountEntryReturnsTheCardWithNothingAsked()
    {
        var panel = new Panel();
        panel.SignIn();
        panel.Send(ScreenEventName.Soft, "L2");

        var before = panel.MessagesDelivered;
        var screen = panel.Send(ScreenEventName.Key, "cancel");

        Assert.Equal("İŞLEM İPTAL EDİLDİ", screen.Title);
        Assert.Equal(before, panel.MessagesDelivered);
        panel.AssertMoneyAddsUp();
    }

    [Fact]
    public void ThePinDigitsNeverReachTheScreenDuringAWithdrawal()
    {
        var panel = new Panel();
        var seen = new List<string>();

        panel.Send(ScreenEventName.Card, "inserted");

        foreach (var digit in "1234")
        {
            seen.Add(ScreenCodec.ToJson(panel.Send(ScreenEventName.Key, digit.ToString())));
        }

        seen.Add(ScreenCodec.ToJson(panel.Send(ScreenEventName.Key, "enter")));
        seen.Add(ScreenCodec.ToJson(panel.Send(ScreenEventName.Soft, "L2")));
        seen.Add(ScreenCodec.ToJson(panel.Send(ScreenEventName.Soft, "L3")));
        seen.Add(ScreenCodec.ToJson(panel.Send(ScreenEventName.Cash, "taken")));

        var everything = string.Join("\n", seen);

        Assert.False(everything.Contains("1234"),
            $"PIN rakamları ekrana giden mesajlarda görünüyor:\n{everything}");
        Assert.False(everything.Contains(Card),
            $"Tam kart numarası ekrana giden mesajlarda görünüyor:\n{everything}");
    }
}
