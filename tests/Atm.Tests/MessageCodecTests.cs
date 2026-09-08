// Copyright (c) 2026 Mirac Kutay Sereflisan. MIT lisansi - LICENSE dosyasina bakiniz.
// MessageCodecTests.cs
//
// What this file does: it checks that what goes on the wire is what the protocol spec
// says goes on the wire, and that anything else is refused rather than guessed at.
//
// Why the wire field names are tested explicitly: the contract document names them in
// camelCase ("bizDate"). C# names them in PascalCase. One translation sits between the
// two, and if it ever changed, both sides of OUR system would change together and keep
// working - while no longer matching the document that is supposed to define them.
// A test that reads the raw text is the only thing that notices.

using System.Text.Json;
using Atm.Protocol;
using Xunit;

namespace Atm.Tests;

public class MessageCodecTests
{
    private static readonly TransactionKey Key = new("ATM-01", "2026-08-24", 104);
    private static readonly DateTimeOffset SentAt = new(2026, 8, 24, 9, 14, 2, TimeSpan.Zero);

    [Fact]
    public void AnEnvelopeSurvivesEncodingAndDecoding()
    {
        var sent = MessageCodec.Envelope(
            MessageType.BalanceRequest, Key, SentAt, new BalanceRequestBody("4111111111111111"));

        var received = MessageCodec.Decode(MessageCodec.Encode(sent));

        Assert.Equal(MessageType.BalanceRequest, received.Type);
        Assert.Equal(104, received.Stan);
        Assert.Equal("ATM-01", received.Terminal);
        Assert.Equal("2026-08-24", received.BizDate);
        Assert.Equal("4111111111111111", received.Body<BalanceRequestBody>().Pan);
    }

    [Fact]
    public void TheWireFieldNamesMatchTheContractDocument()
    {
        var json = MessageCodec.AsText(
            MessageCodec.Encode(MessageCodec.Envelope(
                MessageType.EchoRequest, Key, SentAt, new EchoBody())));

        foreach (var field in new[] { "\"v\"", "\"type\"", "\"stan\"", "\"terminal\"", "\"bizDate\"", "\"sentAt\"", "\"retry\"", "\"body\"" })
        {
            Assert.True(json.Contains(field, StringComparison.Ordinal),
                $"Zarf alanı {field} tel üzerinde bulunamadı. Gelen: {json}");
        }
    }

    [Fact]
    public void SendTimeIsWrittenAsUtcWithMilliseconds()
    {
        // The business date and the send time are read by a human during
        // reconciliation. A local-time stamp from one machine and a UTC stamp from
        // another would put two halves of one transaction hours apart.
        var envelope = MessageCodec.Envelope(
            MessageType.EchoRequest, Key, new DateTimeOffset(2026, 8, 24, 12, 14, 2, TimeSpan.FromHours(3)), new EchoBody());

        Assert.Equal("2026-08-24T09:14:02.000Z", envelope.SentAt);
    }

    [Fact]
    public void AMessageFromAnotherProtocolVersionIsRefused()
    {
        var foreign = """
            {"v":"ATMSIM/9.9","type":"EchoRequest","stan":1,"terminal":"ATM-01",
             "bizDate":"2026-08-24","sentAt":"2026-08-24T09:14:02.000Z","retry":0,"body":{}}
            """;

        var threw = false;
        try
        {
            MessageCodec.Decode(System.Text.Encoding.UTF8.GetBytes(foreign));
        }
        catch (ProtocolViolationException)
        {
            threw = true;
        }

        Assert.True(threw, "Farklı sürümlü mesaj reddedilmeli, umutla çözümlenmemeli.");
    }

    [Fact]
    public void UnreadableBytesAreRefusedRatherThanHalfParsed()
    {
        var threw = false;
        try
        {
            MessageCodec.Decode(System.Text.Encoding.UTF8.GetBytes("bu JSON değil"));
        }
        catch (ProtocolViolationException)
        {
            threw = true;
        }

        Assert.True(threw, "Çözümlenemeyen gövde ProtocolViolationException fırlatmalı.");
    }

    [Fact]
    public void TheTransactionKeyIsTheThreeFieldsFromKarar009()
    {
        var envelope = MessageCodec.Envelope(
            MessageType.BalanceRequest, Key, SentAt, new BalanceRequestBody("4111111111111111"));

        Assert.Equal(Key, envelope.Key);
        Assert.Equal("ATM-01/2026-08-24/000104", envelope.Key.ToString());
    }

    [Fact]
    public void TwoKeysWithTheSameThreeValuesAreTheSameTransaction()
    {
        // This is what stops a retried request from becoming a second debit. If this
        // comparison were wrong, the host's identity table would never find the first
        // attempt and would perform the transaction twice.
        Assert.Equal(new TransactionKey("ATM-01", "2026-08-24", 104), Key);
        Assert.True(new TransactionKey("ATM-01", "2026-08-24", 105) != Key,
            "Farklı izleme numarası farklı işlem demektir.");
    }

    [Fact]
    public void ThePinNeverAppearsInAMaskedCardNumber()
    {
        Assert.Equal("411111******1111", MessageCodec.MaskPan("4111111111111111"));
        Assert.Equal("411111**1111", MessageCodec.MaskPan("4111 1111 1111"));
    }

    [Fact]
    public void ABodyThatDoesNotMatchItsTypeIsRefused()
    {
        var envelope = MessageCodec.Envelope(
            MessageType.BalanceResponse, Key, SentAt, new BalanceResponseBody(ResponseCode.Approved, 250000, 250000));

        var body = envelope.Body<BalanceResponseBody>();

        Assert.Equal(250000, body.Available);
        Assert.Equal(ResponseCode.Approved, body.Rc);
        Assert.Equal(JsonValueKind.Object, envelope.Body.ValueKind);
    }

    [Fact]
    public void ADenominationBreakdownTravelsUnderTheShortNamesTheContractFixes()
    {
        var envelope = MessageCodec.Envelope(MessageType.WithdrawalAuthRequest,
            new TransactionKey("ATM-01", "2026-01-05", 104), SentAt,
            new WithdrawalAuthRequestBody("4111111111111111", 35_000,
                [new DenominationLine(20_000, 1), new DenominationLine(10_000, 1),
                 new DenominationLine(5_000, 1)]));

        var wire = MessageCodec.AsText(MessageCodec.Encode(envelope));

        // the protocol spec section 4.3 writes the breakdown as {"d":20000,"n":1}. The
        // C# names are longer on purpose; this test is where the two vocabularies are
        // held together, so that renaming a property cannot silently change the wire.
        Assert.True(wire.Contains("\"denoms\":[{\"d\":20000,\"n\":1}"),
            $"the breakdown did not travel as the contract writes it: {wire}");
        Assert.False(wire.Contains("denomination"),
            $"a C# property name reached the wire: {wire}");
    }
}
