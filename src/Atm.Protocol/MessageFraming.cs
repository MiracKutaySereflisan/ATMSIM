// MessageFraming.cs
//
// What this file does: it puts a four-byte length in front of every message and reads
// it back, so that a stream of bytes can be cut into messages again.
//
// Why this is needed at all: TCP delivers a byte stream, not a message stream. Two
// messages sent one after another can arrive stuck together, and one message can
// arrive split in half. Saying where a message ends is the sender's job. See
// docs/protocol.md section 1.1.
//
// Why a length prefix and not a separator character: a separator only works while the
// separator never appears inside a message. The day it does - in an error text, in an
// address - the message is cut in the wrong place and the two sides get lost at
// different points in the stream. A length prefix cannot have that failure: the reader
// knows how many bytes to take before it takes any.
//
// Why big-endian: it is the traditional byte order for network protocols. The choice
// itself does not matter; having it WRITTEN DOWN does. If the two sides disagreed on
// the order, the number 1 would read as 16777216.
//
// Why the calls are synchronous: this simulator has one terminal and one connection.
// A blocking read on a dedicated thread is simpler to follow, simpler to explain and
// fully deterministic in tests, and none of those tests would be helped by
// concurrency. See KARAR-012.

using System.Buffers.Binary;

namespace Atm.Protocol;

/// <summary>Cuts a byte stream into messages, and writes messages into a byte stream.</summary>
public static class MessageFraming
{
    /// <summary>Size of the length prefix itself. Not counted in the length it holds.</summary>
    public const int PrefixBytes = 4;

    /// <summary>
    /// Largest payload we will read. A frame claiming more than this is not a big
    /// message - it means we no longer know where we are in the stream.
    /// </summary>
    public const int MaxPayloadBytes = 65536;

    /// <summary>Writes one payload as a framed message.</summary>
    public static void Write(Stream stream, ReadOnlySpan<byte> payload)
    {
        if (payload.Length > MaxPayloadBytes)
        {
            throw new ProtocolViolationException(
                $"Gövde {payload.Length} bayt; üst sınır {MaxPayloadBytes} bayt.");
        }

        Span<byte> prefix = stackalloc byte[PrefixBytes];
        BinaryPrimitives.WriteUInt32BigEndian(prefix, (uint)payload.Length);

        stream.Write(prefix);
        stream.Write(payload);
        stream.Flush();
    }

    /// <summary>
    /// Reads one framed message. Returns null when the stream ends cleanly between
    /// messages - that is a closed connection, not an error.
    /// </summary>
    /// <exception cref="ProtocolViolationException">
    /// The stream ended in the middle of a frame, or the frame claimed more than
    /// <see cref="MaxPayloadBytes"/>. Both mean the stream position is no longer
    /// known, and guessing from here would invent data.
    /// </exception>
    public static byte[]? Read(Stream stream)
    {
        var prefix = new byte[PrefixBytes];
        var prefixRead = ReadAtLeast(stream, prefix, PrefixBytes);

        if (prefixRead == 0)
        {
            return null; // Clean end of stream: nothing was in progress.
        }

        if (prefixRead < PrefixBytes)
        {
            throw new ProtocolViolationException(
                $"Uzunluk öneki yarıda kesildi: {prefixRead}/{PrefixBytes} bayt.");
        }

        var length = BinaryPrimitives.ReadUInt32BigEndian(prefix);

        if (length > MaxPayloadBytes)
        {
            throw new ProtocolViolationException(
                $"Çerçeve {length} bayt bildirdi; üst sınır {MaxPayloadBytes} bayt. Bağlantı kapatılmalı.");
        }

        var payload = new byte[length];
        var payloadRead = ReadAtLeast(stream, payload, (int)length);

        if (payloadRead < length)
        {
            throw new ProtocolViolationException(
                $"Gövde yarıda kesildi: {payloadRead}/{length} bayt.");
        }

        return payload;
    }

    /// <summary>
    /// Fills the buffer, looping until it is full or the stream ends. A single Read
    /// call is allowed to return fewer bytes than asked for, and treating one short
    /// read as the whole message is the classic way to lose the second half of it.
    /// </summary>
    private static int ReadAtLeast(Stream stream, byte[] buffer, int count)
    {
        var total = 0;

        while (total < count)
        {
            var read = stream.Read(buffer, total, count - total);

            if (read == 0)
            {
                break; // End of stream.
            }

            total += read;
        }

        return total;
    }
}
