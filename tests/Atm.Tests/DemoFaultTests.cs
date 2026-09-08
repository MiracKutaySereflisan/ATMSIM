// DemoFaultTests.cs
//
// What these tests are for: the switches a presenter flips on stage - "now I am cutting the
// line" - and the two promises around them.
//
// The first promise is that flipping a switch is NOT a transaction. It must not move the
// customer's screen, must not give them more time, and must not decide anything. The screen
// says only "the operator pressed this"; what it means is decided here (KARAR-053).
//
// The second is louder: every picture admits that a fault is on. A demonstration machine
// with a fault quietly switched on is a machine somebody will one day show to an audience
// as if it were working normally - and that is a worse outcome than the demo failing,
// because it is a claim about the system that nobody can see is false.

using Atm.Protocol;
using Atm.Terminal;
using Xunit;

namespace Atm.Tests;

public class DemoFaultTests
{
    private const string Card = "4111111111111111";

    private readonly VirtualClock _clock = new(new DateTimeOffset(2026, 8, 25, 9, 0, 0, TimeSpan.Zero));
    private readonly DemoFaults _demo = new();

    private TerminalFlow Flow() => new(
        ask: (request, _) => request.Type switch
        {
            MessageType.PinVerifyRequest => MessageCodec.Envelope(
                MessageType.PinVerifyResponse, request.Key, _clock.UtcNow,
                new PinVerifyResponseBody(ResponseCode.Approved, true, 3)),
            _ => null,
        },
        clock: _clock,
        readCard: () => Card,
        demo: _demo);

    private static ScreenView Send(TerminalFlow flow, string @event, string value) =>
        flow.Handle(new ScreenEvent(@event, value))
        ?? throw new InvalidOperationException($"'{@event}:{value}' ekranı değiştirmedi.");

    [Fact]
    public void EveryScreenAdmitsThatAFaultIsSwitchedOn()
    {
        var flow = Flow();

        Assert.Equal("", Send(flow, ScreenEventName.Card, "inserted").Demo);

        Send(flow, ScreenEventName.Demo, "hat-kes");

        // Not the screen that switched it on - the NEXT one, and the one after that. The
        // admission is stamped on the way out of every picture, so no screen anywhere in
        // the flow can be written that forgets it.
        Assert.Contains("hat kesik", Send(flow, ScreenEventName.Key, "1").Demo);
        Assert.Contains("hat kesik", Send(flow, ScreenEventName.Key, "2").Demo);
    }

    [Fact]
    public void AnOperatorSwitchDoesNotMoveTheCustomersTransaction()
    {
        var flow = Flow();

        Send(flow, ScreenEventName.Card, "inserted");
        var before = Send(flow, ScreenEventName.Key, "1");

        var after = Send(flow, ScreenEventName.Demo, "cevap-yut");

        // Same screen, same title, same number of asterisks. A flipped switch is not an
        // event in the customer's transaction.
        Assert.Equal(before.Screen, after.Screen);
        Assert.Equal(before.Title, after.Title);
        Assert.Equal(before.MaskedLength, after.MaskedLength);
    }

    [Fact]
    public void AnOperatorSwitchDoesNotGiveTheCustomerMoreTime()
    {
        var flow = Flow();

        Send(flow, ScreenEventName.Card, "inserted");
        var pin = Send(flow, ScreenEventName.Key, "1");

        var patience = pin.CountdownSeconds
            ?? throw new InvalidOperationException("PIN ekranının geri sayımı yok.");

        // Most of the patience is spent, then a switch is flipped, then the rest.
        _clock.Advance(TimeSpan.FromSeconds(patience - 2));
        Send(flow, ScreenEventName.Demo, "hat-kes");
        _clock.Advance(TimeSpan.FromSeconds(4));

        var next = Send(flow, ScreenEventName.Tick, "");

        // The transaction has run out of time. Repainting the same picture restarts its
        // countdown unless something stops it - and the screen where that would matter
        // most is the deposit confirm screen, where silence means no (KARAR-042).
        Assert.NotEqual("pin", next.Screen);
    }

    [Fact]
    public void TheLineComingBackClearsBothWaysOfLosingAMessage()
    {
        _demo.Apply("hat-kes");
        _demo.Apply("cevap-yut");

        Assert.True(_demo.LineIsDown);
        Assert.True(_demo.AnswersAreSwallowed);

        _demo.Apply("hat-gelsin");

        // One button, both faults. On stage the presenter has to be able to put everything
        // back with a single press; a switch that needs two presses to undo is a switch
        // that will be left half on.
        Assert.False(_demo.LineIsDown);
        Assert.False(_demo.AnswersAreSwallowed);
        Assert.False(_demo.Any);
    }

    [Fact]
    public void TheTwoWaysOfLosingAMessageAreSeparateSwitches()
    {
        // They look identical from the machine's side and they are opposites in fact: in
        // one the host never heard, in the other it heard and did the work. A panel with
        // one button for both could not demonstrate rule 4.1 (docs/proje-kurallari.md) at all.
        _demo.Apply("cevap-yut");

        Assert.False(_demo.LineIsDown);
        Assert.True(_demo.AnswersAreSwallowed);
        Assert.Contains("cevaplar yutuluyor", _demo.Summary);
    }

    [Fact]
    public void AnUnknownSwitchIsRefusedRatherThanIgnored()
    {
        // The presenter says "now I am cutting the line" while the line stays up. Silence
        // here would be a demonstration that lies about itself.
        var complaint = Assert.Throws<ArgumentException>(() => _demo.Apply("hatti-kes"));
        Assert.Contains("Bilinmeyen demo komutu", complaint.Message);
    }

    [Fact]
    public void AMachineFaultSaysWhatItIsInWords()
    {
        _demo.Apply("eksik-ver");
        Assert.Contains("200 TL", _demo.Summary);

        _demo.Apply("sikisma");
        Assert.Contains("sıkışık", _demo.Summary);

        _demo.Apply("makine-duzelsin");
        Assert.Equal("", _demo.Summary);
        Assert.False(_demo.Any);
    }

    [Fact]
    public void AFlowWithNoPanelBehindItIgnoresTheSwitchEntirely()
    {
        // The tests of every earlier phase build a flow without a panel, and the demo event
        // must be nothing at all to them - not an exception, not a screen.
        var plain = new TerminalFlow(
            ask: (_, _) => null, clock: _clock, readCard: () => Card);

        Assert.Null(plain.Handle(new ScreenEvent(ScreenEventName.Demo, "hat-kes")));
    }
}
