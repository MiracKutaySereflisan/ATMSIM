// WebSocketFramingTests.cs
//
// What these tests check: that a message survives the trip in both directions, and
// that the three ways a stream can hand us bytes - stuck together, split apart, or
// too large - are all handled on purpose rather than by luck.
//
// None of these tests opens a socket. The "arrived in pieces" case is the one that
// only shows up on a busy real connection, which is exactly why it is made to happen
// here on demand instead of being waited for.

using System.Text;
using Atm.Terminal;
using Xunit;

namespace Atm.Tests;

public class WebSocketFramingTests
{
    // Builds the kind of frame a browser sends: masked, with a chosen key.
    private static byte[] MaskedTextFrame(string text, byte[] mask)
    {
        var payload = Encoding.UTF8.GetBytes(text);
        var header = payload.Length <= 125 ? 2 : 4;
        var frame = new byte[header + 4 + payload.Length];

        frame[0] = 0x80 | WebSocketOpcode.Text;

        if (payload.Length <= 125)
        {
            frame[1] = (byte)(0x80 | payload.Length);
        }
        else
        {
            frame[1] = 0x80 | 126;
            frame[2] = (byte)(payload.Length >> 8);
            frame[3] = (byte)payload.Length;
        }

        Array.Copy(mask, 0, frame, header, 4);

        for (var i = 0; i < payload.Length; i++)
        {
            frame[header + 4 + i] = (byte)(payload[i] ^ mask[i % 4]);
        }

        return frame;
    }

    [Fact]
    public void AMaskedMessageFromTheBrowserComesBackAsItWasWritten()
    {
        var frame = MaskedTextFrame("{\"event\":\"key\",\"value\":\"7\"}", new byte[] { 1, 2, 3, 4 });

        Assert.True(WebSocketFraming.TryDecode(frame, out var read, out var used), "Çerçeve okunamadı.");
        Assert.Equal("{\"event\":\"key\",\"value\":\"7\"}", Encoding.UTF8.GetString(read!.Payload));
        Assert.Equal(frame.Length, used);
        Assert.True(read.Fin, "FIN biti okunmadı.");
    }

    [Fact]
    public void WhatTheServerSendsIsNeverMasked()
    {
        // A browser that receives a masked frame from a server closes the connection.
        var frame = WebSocketFraming.EncodeText("merhaba");

        Assert.Equal(0, frame[1] & 0x80);
        Assert.Equal("merhaba", Encoding.UTF8.GetString(frame, 2, frame.Length - 2));
    }

    [Fact]
    public void TwoMessagesThatArriveStuckTogetherAreStillTwoMessages()
    {
        var first = MaskedTextFrame("bir", new byte[] { 9, 8, 7, 6 });
        var second = MaskedTextFrame("iki", new byte[] { 5, 4, 3, 2 });
        var together = first.Concat(second).ToArray();

        Assert.True(WebSocketFraming.TryDecode(together, out var a, out var usedA), "Birinci okunamadı.");
        Assert.Equal("bir", Encoding.UTF8.GetString(a!.Payload));

        Assert.True(WebSocketFraming.TryDecode(together.AsSpan(usedA), out var b, out _), "İkinci okunamadı.");
        Assert.Equal("iki", Encoding.UTF8.GetString(b!.Payload));
    }

    [Fact]
    public void AMessageDeliveredOneByteAtATimeIsStillOneMessage()
    {
        var frame = MaskedTextFrame("parça parça", new byte[] { 11, 22, 33, 44 });

        // Every prefix short of the whole frame must say "not yet" rather than
        // guessing, and none of them may throw.
        for (var i = 0; i < frame.Length; i++)
        {
            Assert.False(WebSocketFraming.TryDecode(frame.AsSpan(0, i), out _, out _),
                $"{i} bayt yeterli sanıldı.");
        }

        Assert.True(WebSocketFraming.TryDecode(frame, out var read, out _), "Tam çerçeve okunamadı.");
        Assert.Equal("parça parça", Encoding.UTF8.GetString(read!.Payload));
    }

    [Fact]
    public void AMessageLongerThanOneHundredAndTwentyFiveBytesUsesTheLongerHeader()
    {
        var uzun = new string('x', 400);
        var frame = MaskedTextFrame(uzun, new byte[] { 3, 1, 4, 1 });

        Assert.Equal(126, frame[1] & 0x7F);
        Assert.True(WebSocketFraming.TryDecode(frame, out var read, out _), "Uzun çerçeve okunamadı.");
        Assert.Equal(uzun, Encoding.UTF8.GetString(read!.Payload));
    }

    [Fact]
    public void AnUnmaskedFrameFromTheBrowserIsRefusedLoudly()
    {
        // Every frame a browser sends is masked. An unmasked one did not come from a
        // browser following the standard, and reading it would mean guessing.
        var wrong = WebSocketFraming.EncodeText("maskesiz");

        Assert.Throws<InvalidOperationException>(() =>
            WebSocketFraming.TryDecode(wrong, out _, out _));
    }

    [Fact]
    public void AFrameLargerThanTheLimitIsRefusedBeforeItIsRead()
    {
        // The length is refused on sight; the bytes it describes are never allocated.
        var header = new byte[] { 0x81, 0x80 | 126, 0xFF, 0xFF, 1, 2, 3, 4 };

        Assert.Throws<InvalidOperationException>(() =>
            WebSocketFraming.TryDecode(header, out _, out _));
    }

    [Fact]
    public void TheEightByteLengthFormIsRefused()
    {
        var header = new byte[] { 0x81, 0x80 | 127, 0, 0, 0, 0, 0, 0, 0, 0, 1, 2, 3, 4 };

        Assert.Throws<InvalidOperationException>(() =>
            WebSocketFraming.TryDecode(header, out _, out _));
    }

    [Fact]
    public void SendingMoreThanTheLimitIsRefused()
    {
        var tooBig = new byte[WebSocketFraming.MaxPayloadBytes + 1];

        Assert.Throws<InvalidOperationException>(() =>
            WebSocketFraming.Encode(WebSocketOpcode.Text, tooBig));
    }

    [Fact]
    public void ACloseFrameCarriesTheNormalClosureCode()
    {
        var frame = WebSocketFraming.EncodeClose();

        Assert.Equal(WebSocketOpcode.Close, frame[0] & 0x0F);
        Assert.Equal(1000, (frame[2] << 8) | frame[3]);
    }
}
