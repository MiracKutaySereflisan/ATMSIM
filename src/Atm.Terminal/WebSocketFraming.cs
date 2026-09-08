// Copyright (c) 2026 Mirac Kutay Sereflisan. MIT lisansi - LICENSE dosyasina bakiniz.
// WebSocketFraming.cs
//
// What this file does: it turns a message into the bytes a WebSocket connection
// carries, and turns arriving bytes back into messages.
//
// Why this exists even though we already have MessageFraming: the two links have
// different rules. The terminal-to-host link is ours, so we chose the simplest thing
// that works - four bytes of length, then the message (the protocol spec section 2.2).
// The browser link is not ours: the browser was written before this project and will
// only speak the framing written into RFC 6455. So on that side we follow their
// rules, and the shape below is theirs, not ours.
//
// The problem being solved is the same one, and it is worth saying once more: a
// connection delivers a stream of bytes, not messages. Two messages sent one after
// another may arrive stuck together, and one message may arrive in five pieces. Any
// code that assumes "what I read is one message" is wrong on a busy line, and wrong
// in a way that only shows up under load.
//
// The one rule that surprises people: a message from a browser is always MASKED -
// every byte is combined with a four-byte key the browser picks at random - and a
// message from a server must never be. This is not encryption and hides nothing; the
// key travels in the open, right next to the data. It exists because of an attack on
// old web caching machines that could be tricked into treating a WebSocket message as
// a new HTTP request. Masking makes the bytes unpredictable, so they cannot be
// steered into looking like a request. We must unmask what arrives and must not mask
// what we send.
//
// This file touches no socket: it works on byte arrays, so the "one message arrived
// in five pieces" case is a test, not an accident we wait for.

namespace Atm.Terminal;

/// <summary>The kinds of frame this project uses. Values are fixed by RFC 6455.</summary>
public static class WebSocketOpcode
{
    /// <summary>A continuation of the previous frame.</summary>
    public const int Continuation = 0x0;

    /// <summary>Text, always UTF-8. Everything this project sends is text.</summary>
    public const int Text = 0x1;

    /// <summary>Binary. Not used here, but recognised so it can be refused clearly.</summary>
    public const int Binary = 0x2;

    /// <summary>"I am closing." Should be answered with the same.</summary>
    public const int Close = 0x8;

    /// <summary>"Are you there?"</summary>
    public const int Ping = 0x9;

    /// <summary>"I am here." The answer to a ping.</summary>
    public const int Pong = 0xA;
}

/// <summary>One frame read off the wire.</summary>
/// <param name="Fin">True when this frame completes a message.</param>
/// <param name="Opcode">See <see cref="WebSocketOpcode"/>.</param>
/// <param name="Payload">The frame's content, already unmasked.</param>
public sealed record WebSocketFrame(bool Fin, int Opcode, byte[] Payload);

/// <summary>Reads and writes WebSocket frames.</summary>
public static class WebSocketFraming
{
    /// <summary>
    /// The largest frame this terminal will accept. A screen event is a few dozen
    /// bytes; anything approaching this size is a mistake or an attempt to exhaust
    /// memory, and refusing early is cheaper than finding out later.
    /// </summary>
    public const int MaxPayloadBytes = 32 * 1024;

    /// <summary>Builds an unmasked frame, the way a server must send it.</summary>
    public static byte[] Encode(int opcode, ReadOnlySpan<byte> payload)
    {
        if (payload.Length > MaxPayloadBytes)
        {
            throw new InvalidOperationException(
                $"Çerçeve sınırı aşıldı: {payload.Length} bayt, sınır {MaxPayloadBytes}.");
        }

        // A short message spends two bytes on its header; a longer one spends four.
        // The standard has a third, eight-byte form for very large messages, which
        // this project never produces because of the limit above.
        var headerLength = payload.Length <= 125 ? 2 : 4;
        var frame = new byte[headerLength + payload.Length];

        // 0x80 sets the FIN bit: "this frame is the whole message". This project
        // never splits an outgoing message, so it is always set.
        frame[0] = (byte)(0x80 | opcode);

        if (payload.Length <= 125)
        {
            // The high bit of the second byte is the mask flag. A server leaves it
            // clear; a browser that saw it set would close the connection.
            frame[1] = (byte)payload.Length;
        }
        else
        {
            frame[1] = 126;
            frame[2] = (byte)(payload.Length >> 8);
            frame[3] = (byte)payload.Length;
        }

        payload.CopyTo(frame.AsSpan(headerLength));
        return frame;
    }

    /// <summary>Builds a text frame from a string.</summary>
    public static byte[] EncodeText(string text) =>
        Encode(WebSocketOpcode.Text, System.Text.Encoding.UTF8.GetBytes(text));

    /// <summary>Builds a close frame carrying the normal-closure code, 1000.</summary>
    public static byte[] EncodeClose() =>
        Encode(WebSocketOpcode.Close, new byte[] { 0x03, 0xE8 });

    /// <summary>
    /// Tries to read one frame from the front of a buffer.
    /// Returns false when the buffer does not yet hold a complete frame - which is
    /// the normal case on a real connection, not an error.
    /// </summary>
    /// <param name="buffer">Bytes received so far.</param>
    /// <param name="frame">The frame, when one was complete.</param>
    /// <param name="consumed">How many bytes the frame used.</param>
    public static bool TryDecode(ReadOnlySpan<byte> buffer, out WebSocketFrame? frame, out int consumed)
    {
        frame = null;
        consumed = 0;

        if (buffer.Length < 2)
        {
            return false;
        }

        var fin = (buffer[0] & 0x80) != 0;
        var opcode = buffer[0] & 0x0F;
        var masked = (buffer[1] & 0x80) != 0;
        var length = buffer[1] & 0x7F;
        var offset = 2;

        if (length == 126)
        {
            if (buffer.Length < offset + 2) return false;
            length = (buffer[offset] << 8) | buffer[offset + 1];
            offset += 2;
        }
        else if (length == 127)
        {
            // The eight-byte form only ever describes a message far larger than this
            // terminal accepts, so it is refused without being read.
            throw new InvalidOperationException(
                $"Çerçeve sınırı aşıldı: sekiz baytlık uzunluk biçimi, sınır {MaxPayloadBytes}.");
        }

        if (length > MaxPayloadBytes)
        {
            throw new InvalidOperationException(
                $"Çerçeve sınırı aşıldı: {length} bayt, sınır {MaxPayloadBytes}.");
        }

        if (!masked)
        {
            // Every frame from a browser is masked. An unmasked one did not come from
            // a browser following the standard, and reading it as if it did would mean
            // guessing.
            throw new InvalidOperationException("Tarayıcıdan maskesiz çerçeve geldi.");
        }

        if (buffer.Length < offset + 4) return false;

        var mask = buffer.Slice(offset, 4);
        offset += 4;

        if (buffer.Length < offset + length) return false;

        var payload = new byte[length];

        for (var i = 0; i < length; i++)
        {
            // Undoing the mask is the same operation as applying it: each byte is
            // combined with one of the four key bytes, in turn.
            payload[i] = (byte)(buffer[offset + i] ^ mask[i % 4]);
        }

        frame = new WebSocketFrame(fin, opcode, payload);
        consumed = offset + length;
        return true;
    }
}
