// Copyright (c) 2026 Mirac Kutay Sereflisan. MIT lisansi - LICENSE dosyasina bakiniz.
// MessageFramingTests.cs
//
// What this file does: it checks that a byte stream really can be cut back into the
// messages that were written into it - including in the three ways a real network
// makes that hard.
//
// Why these particular cases: they are not imagined. Two messages arriving stuck
// together, and one message arriving in pieces, are the normal behaviour of TCP, not
// its failure mode. Code that only ever sees one whole message at a time works in
// testing and loses half a message in the demo.

using Atm.Protocol;
using Xunit;

namespace Atm.Tests;

public class MessageFramingTests
{
    private static byte[] Payload(string text) => System.Text.Encoding.UTF8.GetBytes(text);

    [Fact]
    public void OneMessageSurvivesTheRoundTrip()
    {
        var stream = new MemoryStream();
        MessageFraming.Write(stream, Payload("merhaba"));
        stream.Position = 0;

        var read = MessageFraming.Read(stream);

        Assert.NotNull(read);
        Assert.Equal("merhaba", MessageCodec.AsText(read!));
    }

    [Fact]
    public void TwoMessagesArrivingBackToBackAreSeparated()
    {
        // TCP is allowed to hand both messages over in one lump. The reader must take
        // exactly the first message's bytes and leave the rest alone.
        var stream = new MemoryStream();
        MessageFraming.Write(stream, Payload("birinci"));
        MessageFraming.Write(stream, Payload("ikinci"));
        stream.Position = 0;

        var first = MessageFraming.Read(stream);
        var second = MessageFraming.Read(stream);

        Assert.Equal("birinci", MessageCodec.AsText(first!));
        Assert.Equal("ikinci", MessageCodec.AsText(second!));
    }

    [Fact]
    public void AMessageDeliveredOneByteAtATimeIsStillOneMessage()
    {
        // The other half of the same coin: a single Read call may return fewer bytes
        // than asked for. Treating one short read as the whole message is the classic
        // way to lose the tail of it.
        var whole = new MemoryStream();
        MessageFraming.Write(whole, Payload("parça parça gelen mesaj"));

        var trickle = new TrickleStream(whole.ToArray(), bytesPerRead: 1);

        var read = MessageFraming.Read(trickle);

        Assert.Equal("parça parça gelen mesaj", MessageCodec.AsText(read!));
    }

    [Fact]
    public void AClosedConnectionBetweenMessagesIsNotAnError()
    {
        // Nothing was in progress, so this is a hang-up, not a broken frame.
        var read = MessageFraming.Read(new MemoryStream());

        Assert.True(read is null, "Boş akıştan null beklenir; bu temiz bir kapanmadır.");
    }

    [Fact]
    public void AFrameLargerThanTheLimitIsRefused()
    {
        // A length this large does not mean a large message. It means we no longer
        // know where we are in the stream, and reading on would invent data.
        var stream = new MemoryStream();
        stream.Write([0xFF, 0xFF, 0xFF, 0xFF]);
        stream.Position = 0;

        var threw = false;
        try
        {
            MessageFraming.Read(stream);
        }
        catch (ProtocolViolationException)
        {
            threw = true;
        }

        Assert.True(threw, "Sınırı aşan çerçeve ProtocolViolationException fırlatmalı.");
    }

    [Fact]
    public void AConnectionCutInTheMiddleOfAMessageIsAnError()
    {
        // The prefix promised more bytes than arrived. Silently returning what came is
        // how a truncated amount becomes a wrong amount.
        var whole = new MemoryStream();
        MessageFraming.Write(whole, Payload("yarıda kesilecek mesaj"));

        var cut = whole.ToArray()[..10];

        var threw = false;
        try
        {
            MessageFraming.Read(new MemoryStream(cut));
        }
        catch (ProtocolViolationException)
        {
            threw = true;
        }

        Assert.True(threw, "Yarıda kesilen gövde ProtocolViolationException fırlatmalı.");
    }

    /// <summary>
    /// A stream that hands over a fixed, small number of bytes per Read call, the way a
    /// real socket may. Nothing else in the repository needs it, so it lives here.
    /// </summary>
    private sealed class TrickleStream(byte[] data, int bytesPerRead) : Stream
    {
        private int position;

        public override int Read(byte[] buffer, int offset, int count)
        {
            var take = Math.Min(Math.Min(bytesPerRead, count), data.Length - position);
            Array.Copy(data, position, buffer, offset, take);
            position += take;
            return take;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => data.Length;
        public override long Position { get => position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
