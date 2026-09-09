// Copyright (c) 2026 Mirac Kutay Sereflisan. MIT lisansi - LICENSE dosyasina bakiniz.
// HostServiceTests.cs
//
// What this file does: it checks the host's behaviour without a network anywhere in
// sight - a balance answered, an unknown card refused, a PIN counted, and above all a
// repeated request answered once and repeated rather than performed twice.
//
// The test to read first is TheSameRequestTwiceIsPerformedOnce. Nothing the host does
// today moves money, so it costs nothing today; in stage 2 the same rule is what
// stands between a lost answer and a customer debited twice for one withdrawal.

using Atm.Host;
using Atm.Protocol;
using Xunit;

namespace Atm.Tests;

public class HostServiceTests
{
    private const string Card = "4111111111111111";
    private const string DemoPin = "1234";

    private static (HostService host, VirtualClock clock) NewHost()
    {
        var clock = VirtualClock.StartOfBusinessDay();
        return (HostService.WithDemoData(clock), clock);
    }

    private static Envelope Request<TBody>(string type, int stan, TBody body, VirtualClock clock)
        where TBody : notnull =>
        MessageCodec.Envelope(type, new TransactionKey("ATM-01", "2026-01-05", stan),
            clock.UtcNow, body);

    [Fact]
    public void ABalanceEnquiryAnswersWhatTheAccountHolds()
    {
        var (host, clock) = NewHost();

        var response = host.Handle(
            Request(MessageType.BalanceRequest, 101, new BalanceRequestBody(Card), clock));

        var body = response.Body<BalanceResponseBody>();
        Assert.Equal(MessageType.BalanceResponse, response.Type);
        Assert.Equal(ResponseCode.Approved, body.Rc);
        Assert.Equal(250_000, body.Available);
        Assert.Equal(250_000, body.Ledger);
    }

    [Fact]
    public void TheAnswerCarriesTheTransactionKeyItWasAskedWith()
    {
        var (host, clock) = NewHost();

        var response = host.Handle(
            Request(MessageType.BalanceRequest, 101, new BalanceRequestBody(Card), clock));

        // The terminal matches answers to requests by this key, not by arrival order.
        Assert.Equal(new TransactionKey("ATM-01", "2026-01-05", 101), response.Key);
    }

    [Fact]
    public void AnUnknownCardIsRefusedWithCodeFourteen()
    {
        var (host, clock) = NewHost();

        var response = host.Handle(
            Request(MessageType.BalanceRequest, 102, new BalanceRequestBody("4000000000000002"), clock));

        Assert.Equal(ResponseCode.UnknownCard, response.Body<BalanceResponseBody>().Rc);
    }

    [Fact]
    public void TheSameRequestTwiceIsPerformedOnce()
    {
        var (host, clock) = NewHost();
        var request = Request(MessageType.PinVerifyRequest, 103,
            new PinVerifyRequestBody(Card, "0000"), clock);

        var first = host.Handle(request);
        clock.Advance(TimeSpan.FromSeconds(31));
        var second = host.Handle(request);

        // A retry must get the answer the host already gave. Counting the wrong PIN a
        // second time would take a try away from a customer who only guessed once.
        Assert.Equal(1, host.ReplayCount);
        Assert.Equal(first.Body<PinVerifyResponseBody>().RemainingTries,
                     second.Body<PinVerifyResponseBody>().RemainingTries);
        Assert.Equal(2, second.Body<PinVerifyResponseBody>().RemainingTries);
    }

    [Fact]
    public void ARepeatedRequestIsWrittenDownAsARepeat()
    {
        var (host, clock) = NewHost();
        var request = Request(MessageType.BalanceRequest, 104, new BalanceRequestBody(Card), clock);

        host.Handle(request);
        host.Handle(request);

        // Both the answer and the fact that it was asked twice belong in the record:
        // at end of day the terminal will show two requests and the host one movement,
        // and that difference has to be explainable from the record itself.
        Assert.Equal(2, host.Journal.Entries.Count);
        Assert.True(host.Journal.Entries[1].Note.Contains("replay"),
            "İkinci girdi tekrar olarak işaretlenmeliydi.");
    }

    [Fact]
    public void AnEchoIsAnsweredAndNotRemembered()
    {
        var (host, clock) = NewHost();
        var echo = Request(MessageType.EchoRequest, 1, new EchoBody(), clock);

        var first = host.Handle(echo);
        var second = host.Handle(echo);

        // Echo carries no money and arrives every thirty seconds; remembering each one
        // would grow the host's memory forever while protecting nothing.
        Assert.Equal(MessageType.EchoResponse, first.Type);
        Assert.Equal(MessageType.EchoResponse, second.Type);
        Assert.Equal(0, host.ReplayCount);
    }

    [Fact]
    public void AWrongPinIsRefusedAndCountedDown()
    {
        var (host, clock) = NewHost();

        var response = host.Handle(Request(MessageType.PinVerifyRequest, 105,
            new PinVerifyRequestBody(Card, "0000"), clock));

        var body = response.Body<PinVerifyResponseBody>();
        Assert.Equal(ResponseCode.WrongPin, body.Rc);
        Assert.False(body.Ok, "Yanlış PIN onaylanmamalı.");
        Assert.Equal(2, body.RemainingTries);
    }

    [Fact]
    public void TheRightPinIsAccepted()
    {
        var (host, clock) = NewHost();

        var response = host.Handle(Request(MessageType.PinVerifyRequest, 106,
            new PinVerifyRequestBody(Card, DemoPin), clock));

        Assert.True(response.Body<PinVerifyResponseBody>().Ok, "Doğru PIN kabul edilmeliydi.");
    }

    [Fact]
    public void ThePinNeverReachesTheRecord()
    {
        var (host, clock) = NewHost();
        const string WrongPin = "8642";

        // Both paths, on purpose. The first version of this test only tried a correct
        // PIN, and it stayed green while a deliberately sabotaged host wrote the PIN
        // into the refusal note - because the refusal path never ran. A test that only
        // walks the happy path cannot see what the unhappy path writes down.
        host.Handle(Request(MessageType.PinVerifyRequest, 107,
            new PinVerifyRequestBody(Card, WrongPin), clock));
        host.Handle(Request(MessageType.PinVerifyRequest, 108,
            new PinVerifyRequestBody(Card, DemoPin), clock));

        // Every field of every line, searched. Instruction section 2 forbids a PIN in
        // any record; this is that rule made checkable rather than remembered.
        foreach (var entry in host.Journal.Entries)
        {
            var line = $"{entry.Seq}|{entry.At:O}|{entry.Key}|{entry.Type}|{entry.Rc}|{entry.MaskedPan}|{entry.Note}";
            Assert.False(line.Contains(WrongPin), $"Kayıt satırında yanlış PIN geçiyor: {line}");
            Assert.False(line.Contains(DemoPin), $"Kayıt satırında doğru PIN geçiyor: {line}");
        }
    }

    [Fact]
    public void ThePinNeverComesBackInTheAnswer()
    {
        var (host, clock) = NewHost();
        const string WrongPin = "8642";

        var refused = host.Handle(Request(MessageType.PinVerifyRequest, 109,
            new PinVerifyRequestBody(Card, WrongPin), clock));
        var accepted = host.Handle(Request(MessageType.PinVerifyRequest, 110,
            new PinVerifyRequestBody(Card, DemoPin), clock));

        // The bytes that go on the wire, read as text. An "echo the request back so
        // the terminal can match it" convenience would be caught here.
        var refusedOnTheWire = MessageCodec.AsText(MessageCodec.Encode(refused));
        var acceptedOnTheWire = MessageCodec.AsText(MessageCodec.Encode(accepted));

        Assert.False(refusedOnTheWire.Contains(WrongPin), $"Cevapta PIN var: {refusedOnTheWire}");
        Assert.False(acceptedOnTheWire.Contains(DemoPin), $"Cevapta PIN var: {acceptedOnTheWire}");
    }

    [Fact]
    public void TheRecordKeepsTheCardNumberMasked()
    {
        var (host, clock) = NewHost();

        host.Handle(Request(MessageType.BalanceRequest, 108, new BalanceRequestBody(Card), clock));

        var entry = host.Journal.Entries[0];
        Assert.False(entry.MaskedPan == Card, "Kart numarası açık yazılmamalı.");
        Assert.Equal(MessageCodec.MaskPan(Card), entry.MaskedPan);
    }

    [Fact]
    public void TheRecordIsNumberedInOrder()
    {
        var (host, clock) = NewHost();

        host.Handle(Request(MessageType.BalanceRequest, 109, new BalanceRequestBody(Card), clock));
        host.Handle(Request(MessageType.BalanceRequest, 110, new BalanceRequestBody(Card), clock));
        host.Handle(Request(MessageType.BalanceRequest, 111, new BalanceRequestBody(Card), clock));

        Assert.Equal(1, host.Journal.Entries[0].Seq);
        Assert.Equal(2, host.Journal.Entries[1].Seq);
        Assert.Equal(3, host.Journal.Entries[2].Seq);
    }

    [Fact]
    public void TheRecordUsesTheClockItWasGivenRatherThanTheMachineClock()
    {
        var (host, clock) = NewHost();
        clock.Advance(TimeSpan.FromHours(9));

        host.Handle(Request(MessageType.BalanceRequest, 112, new BalanceRequestBody(Card), clock));

        // If this read the machine clock, every scenario's record would carry the day
        // it happened to be run on, and two runs of the same scenario would not be
        // comparable.
        Assert.Equal(clock.UtcNow, host.Journal.Entries[0].At);
    }

    [Fact]
    public void AMessageTypeTheHostDoesNotKnowIsRefusedRatherThanIgnored()
    {
        var (host, clock) = NewHost();

        var response = host.Handle(Request("SomethingFromTheFuture", 113,
            new BalanceRequestBody(Card), clock));

        // Silence would leave the terminal waiting out its timeout - and a timeout is
        // the one answer that costs the most to interpret (instruction 4.1).
        Assert.Equal(ResponseCode.InvalidTransaction, response.Body<BalanceResponseBody>().Rc);
    }
}
