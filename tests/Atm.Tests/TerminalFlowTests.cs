// TerminalFlowTests.cs
//
// What these tests check: the whole behaviour of the machine, from the card going in
// to the balance appearing, without a socket, a browser or a real clock anywhere.
//
// The host is a function written inside each test. That is what makes "the host did
// not answer" a line of code rather than a twenty-second wait, and it is why every
// one of these tests gives the same answer every time it runs.
//
// Two of these tests are about money not moving rather than about money moving, and
// they are the ones worth reading twice: an unanswered balance enquiry is safe to
// shrug at, and Phase 2 will have to stop shrugging.

using Atm.Protocol;
using Atm.Terminal;
using Xunit;

namespace Atm.Tests;

public class TerminalFlowTests
{
    private const string DemoPan = "4111111111111111";

    private readonly VirtualClock _clock = new(new DateTimeOffset(2026, 8, 25, 9, 0, 0, TimeSpan.Zero));
    private readonly List<Envelope> _asked = new();

    // Builds a flow whose host answers with whatever the test says.
    private TerminalFlow FlowWith(Func<Envelope, Envelope?> host, string pan = DemoPan) =>
        new(
            ask: (request, _) =>
            {
                _asked.Add(request);
                return host(request);
            },
            clock: _clock,
            readCard: () => pan);

    private Envelope Answer<TBody>(Envelope request, string type, TBody body) where TBody : notnull =>
        MessageCodec.Envelope(type, request.Key, _clock.UtcNow, body);

    // A host that approves the PIN and reports 2.500,00 TL.
    private Envelope HappyHost(Envelope request) => request.Type switch
    {
        MessageType.PinVerifyRequest => Answer(request, MessageType.PinVerifyResponse,
            new PinVerifyResponseBody(ResponseCode.Approved, true, 3)),
        MessageType.BalanceRequest => Answer(request, MessageType.BalanceResponse,
            new BalanceResponseBody(ResponseCode.Approved, 250_000, 250_000)),
        _ => throw new InvalidOperationException("Beklenmeyen mesaj: " + request.Type),
    };

    private static ScreenView Press(TerminalFlow flow, string key) =>
        flow.Handle(new ScreenEvent(ScreenEventName.Key, key))
        ?? throw new InvalidOperationException($"'{key}' tuşu ekranı değiştirmedi.");

    private static ScreenView TypePin(TerminalFlow flow, string pin)
    {
        ScreenView? last = null;

        foreach (var digit in pin)
        {
            last = Press(flow, digit.ToString());
        }

        return last!;
    }

    // ---------- the happy path, end to end ----------

    [Fact]
    public void ACardAPinAndABalance()
    {
        var flow = FlowWith(HappyHost);

        var pinScreen = flow.Handle(new ScreenEvent(ScreenEventName.Card, "inserted"))!;
        Assert.Equal("pin", pinScreen.Screen);
        Assert.Equal(TerminalStep.PinEntry, flow.Step);

        TypePin(flow, "1234");
        var menu = Press(flow, "enter");
        Assert.Equal("menu", menu.Screen);
        Assert.Equal("Bakiye sorgu", menu.SoftLeft[0]);

        var balance = flow.Handle(new ScreenEvent(ScreenEventName.Soft, "L1"))!;
        Assert.Equal("balance", balance.Screen);
        Assert.True(balance.Lines.Any(l => l.Contains("2.500,00 TL")),
            "Bakiye ekranda yok: " + string.Join(" | ", balance.Lines));

        var bye = flow.Handle(new ScreenEvent(ScreenEventName.Soft, "R4"))!;
        Assert.Equal("returned", bye.Card);

        var idle = flow.Handle(new ScreenEvent(ScreenEventName.Card, "taken"))!;
        Assert.Equal("idle", idle.Screen);
        Assert.Equal(TerminalStep.Idle, flow.Step);
    }

    // ---------- the PIN ----------

    [Fact]
    public void TheTerminalCountsTheDigitsAndTheScreenIsOnlyToldHowMany()
    {
        var flow = FlowWith(HappyHost);
        flow.Handle(new ScreenEvent(ScreenEventName.Card, "inserted"));

        Assert.Equal(1, Press(flow, "1").MaskedLength);
        Assert.Equal(2, Press(flow, "9").MaskedLength);
        Assert.Equal(3, Press(flow, "3").MaskedLength);
        Assert.Equal(2, Press(flow, "correct").MaskedLength);
    }

    [Fact]
    public void ThePinDigitsNeverAppearInAnythingSentToTheScreen()
    {
        // This is the test the Phase 1g note in ScreenContractTests promised: it walks
        // the real PIN-entry path and scans every picture the flow produced, including
        // the ones on the error paths.
        var pictures = new List<ScreenView>();
        var flow = FlowWith(request => request.Type == MessageType.PinVerifyRequest
            ? Answer(request, MessageType.PinVerifyResponse,
                new PinVerifyResponseBody(ResponseCode.WrongPin, false, 2))
            : HappyHost(request));

        pictures.Add(flow.Handle(new ScreenEvent(ScreenEventName.Card, "inserted"))!);

        foreach (var digit in "4913")
        {
            pictures.Add(Press(flow, digit.ToString()));
        }

        pictures.Add(Press(flow, "enter"));

        foreach (var picture in pictures)
        {
            var json = ScreenCodec.ToJson(picture);

            Assert.False(json.Contains("4913"), "PIN ekrana gitti: " + json);

            foreach (var digit in "4913")
            {
                Assert.False(json.Contains($"\"{digit}\""), $"PIN hanesi '{digit}' ekrana gitti: {json}");
            }
        }
    }

    [Fact]
    public void ThePinDoesReachTheHostAndOnlyOnTheVerifyMessage()
    {
        // The other half of the same claim: the PIN goes exactly one place, and the
        // balance message that follows does not carry it.
        var flow = FlowWith(HappyHost);
        flow.Handle(new ScreenEvent(ScreenEventName.Card, "inserted"));
        TypePin(flow, "4913");
        Press(flow, "enter");
        flow.Handle(new ScreenEvent(ScreenEventName.Soft, "L1"));

        Assert.Equal(2, _asked.Count);
        Assert.Equal("4913", _asked[0].Body<PinVerifyRequestBody>().Pin);
        Assert.Equal(MessageType.BalanceRequest, _asked[1].Type);
        Assert.False(MessageCodec.AsText(MessageCodec.Encode(_asked[1])).Contains("4913"),
            "PIN bakiye mesajına sızmış.");
    }

    [Fact]
    public void AShortPinIsNotSentToTheHostAtAll()
    {
        var flow = FlowWith(HappyHost);
        flow.Handle(new ScreenEvent(ScreenEventName.Card, "inserted"));
        TypePin(flow, "12");

        var stillPin = Press(flow, "enter");

        Assert.Equal("pin", stillPin.Screen);
        Assert.Equal(0, _asked.Count);
    }

    [Fact]
    public void AWrongPinLeavesTheCustomerOnThePinScreenWithTheTriesLeft()
    {
        var flow = FlowWith(request =>
            Answer(request, MessageType.PinVerifyResponse,
                new PinVerifyResponseBody(ResponseCode.WrongPin, false, 2)));

        flow.Handle(new ScreenEvent(ScreenEventName.Card, "inserted"));
        TypePin(flow, "0000");
        var again = Press(flow, "enter");

        Assert.Equal("pin", again.Screen);
        Assert.Equal(0, again.MaskedLength);
        Assert.Equal(2, flow.RemainingTries);
        Assert.True(again.Lines.Any(l => l.Contains("Kalan hakkınız: 2")),
            "Kalan hak yazılmamış: " + string.Join(" | ", again.Lines));
    }

    [Fact]
    public void WhenTheTriesAreGoneTheCardIsKept()
    {
        var flow = FlowWith(request =>
            Answer(request, MessageType.PinVerifyResponse,
                new PinVerifyResponseBody(ResponseCode.PinTriesExhausted, false, 0)));

        flow.Handle(new ScreenEvent(ScreenEventName.Card, "inserted"));
        TypePin(flow, "0000");
        var kept = Press(flow, "enter");

        // "captured" and "returned" are different words on purpose. A card that is
        // kept and a card that is handed back are different events for the customer
        // and different lines in the terminal's journal.
        Assert.Equal("captured", kept.Card);
    }

    // ---------- when the host says nothing ----------

    [Fact]
    public void AnUnansweredBalanceEnquiryEndsTheSessionAndMovesNoMoney()
    {
        // A balance enquiry reads. Nothing was done, so nothing needs undoing, and the
        // honest thing to tell the customer is that it could not be completed.
        //
        // Phase 2 breaks this. An unanswered WITHDRAWAL cannot be treated this way:
        // the host may have debited the account already and the terminal does not
        // know. The answer there is a reversal (rule 4.1 (docs/proje-kurallari.md)). This test
        // exists partly to mark the place where that difference begins.
        var flow = FlowWith(request => request.Type == MessageType.BalanceRequest
            ? null
            : HappyHost(request));

        flow.Handle(new ScreenEvent(ScreenEventName.Card, "inserted"));
        TypePin(flow, "1234");
        Press(flow, "enter");

        var failed = flow.Handle(new ScreenEvent(ScreenEventName.Soft, "L1"))!;

        Assert.Equal("İŞLEMİNİZ TAMAMLANAMADI", failed.Title);
        Assert.Equal("returned", failed.Card);
        Assert.Equal(TerminalStep.Message, flow.Step);
    }

    [Fact]
    public void AnUnansweredPinCheckDoesNotSayThePinWasWrong()
    {
        // Silence is not a refusal. Telling the customer their PIN was wrong when the
        // host simply did not answer would be inventing an answer, and it would burn
        // one of their three tries in their own mind while burning none on the host.
        var flow = FlowWith(_ => null);

        flow.Handle(new ScreenEvent(ScreenEventName.Card, "inserted"));
        TypePin(flow, "1234");
        var quiet = Press(flow, "enter");

        Assert.Equal("İŞLEMİNİZ TAMAMLANAMADI", quiet.Title);
        Assert.False(string.Join(" ", quiet.Lines).Contains("hatalı"),
            "Cevapsızlık yanlış PIN gibi anlatılmış.");
        Assert.Equal(3, flow.RemainingTries);
    }

    // ---------- identity and housekeeping ----------

    [Fact]
    public void EveryMessageCarriesItsOwnTransactionKey()
    {
        // Two different transactions must never share a key: the host refuses to do
        // the same transaction twice, so a repeated key would make the second one
        // silently return the first one's answer.
        var flow = FlowWith(HappyHost);
        flow.Handle(new ScreenEvent(ScreenEventName.Card, "inserted"));
        TypePin(flow, "1234");
        Press(flow, "enter");
        flow.Handle(new ScreenEvent(ScreenEventName.Soft, "L1"));

        Assert.Equal(2, _asked.Count);
        Assert.False(_asked[0].Key == _asked[1].Key, "İki mesaj aynı işlem kimliğini taşıyor.");
        Assert.Equal("ATM-01", _asked[0].Key.Terminal);
        Assert.Equal("2026-08-25", _asked[0].Key.BizDate);
    }

    [Fact]
    public void CancelAlwaysReturnsTheCard()
    {
        var flow = FlowWith(HappyHost);
        flow.Handle(new ScreenEvent(ScreenEventName.Card, "inserted"));
        TypePin(flow, "12");

        var cancelled = Press(flow, "cancel");

        Assert.Equal("returned", cancelled.Card);
        Assert.Equal(0, _asked.Count);
    }

    [Fact]
    public void NothingSurvivesFromOneCustomerToTheNext()
    {
        var flow = FlowWith(HappyHost);

        flow.Handle(new ScreenEvent(ScreenEventName.Card, "inserted"));
        TypePin(flow, "1234");
        Press(flow, "enter");
        flow.Handle(new ScreenEvent(ScreenEventName.Soft, "L1"));
        flow.Handle(new ScreenEvent(ScreenEventName.Soft, "R4"));
        flow.Handle(new ScreenEvent(ScreenEventName.Card, "taken"));

        // The next customer walks up to a welcome screen with no asterisks and no
        // balance on it.
        Assert.Equal("idle", flow.Current.Screen);
        Assert.Equal(0, flow.Current.MaskedLength);
        Assert.False(ScreenCodec.ToJson(flow.Current).Contains("2.500"),
            "Önceki müşterinin bakiyesi ekranda kalmış.");
    }

    [Fact]
    public void AKeyPressedWhenNobodyIsAtTheMachineChangesNothing()
    {
        var flow = FlowWith(HappyHost);

        Assert.True(flow.Handle(new ScreenEvent(ScreenEventName.Key, "5")) is null,
            "Boştaki makine tuşa tepki verdi.");
    }

    [Fact]
    public void AReconnectingScreenIsSentThePictureThatIsAlreadyShowing()
    {
        // A browser reload must not restart the customer's session. The screen asks
        // "what is on you", and gets what is on it.
        var flow = FlowWith(HappyHost);
        flow.Handle(new ScreenEvent(ScreenEventName.Card, "inserted"));
        TypePin(flow, "12");

        var afterReload = flow.Handle(new ScreenEvent(ScreenEventName.Hello, ""))!;

        Assert.Equal("pin", afterReload.Screen);
        Assert.Equal(2, afterReload.MaskedLength);
        Assert.Equal(TerminalStep.PinEntry, flow.Step);
    }

    // ---------- money on the screen ----------

    [Fact]
    public void AmountsAreWrittenTheWayTurkishMoneyIsWritten()
    {
        Assert.Equal("2.500,00 TL", TerminalFlow.Money(250_000));
        Assert.Equal("45,00 TL", TerminalFlow.Money(4_500));
        Assert.Equal("100.000,00 TL", TerminalFlow.Money(10_000_000));
        Assert.Equal("0,05 TL", TerminalFlow.Money(5));
        Assert.Equal("-20,00 TL", TerminalFlow.Money(-2_000));
    }
}
