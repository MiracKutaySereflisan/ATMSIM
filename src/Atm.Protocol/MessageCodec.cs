// Copyright (c) 2026 Mirac Kutay Sereflisan. MIT lisansi - LICENSE dosyasina bakiniz.
// MessageCodec.cs
//
// What this file does: it turns an envelope into the bytes that go on the wire, and
// those bytes back into an envelope.
//
// Why it is one file: the translation between C# names (PascalCase) and wire names
// (camelCase, as the protocol spec specifies) has to happen in exactly one place. Two
// places would eventually disagree, and the disagreement would only show up on the
// wire - the most expensive place to find it.
//
// Why System.Text.Json: it ships with .NET. This repository has no external packages
// (KARAR-006), and JSON was chosen over a binary format for readability, with the cost
// recorded in reports/assumptions.md as V-01.
//
// Careful: Decode does NOT accept a version it does not speak. A message from a
// different protocol version is refused loudly rather than parsed hopefully - a field
// that moved between versions would otherwise be read as if nothing had changed.

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Atm.Protocol;

/// <summary>Thrown when bytes on the wire do not obey the protocol spec.</summary>
public sealed class ProtocolViolationException(string message) : Exception(message);

/// <summary>Converts between <see cref="Envelope"/> and the UTF-8 JSON on the wire.</summary>
public static class MessageCodec
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = false,
    };

    /// <summary>
    /// Builds an envelope around a typed body. The body is serialised immediately so
    /// that the caller cannot hand over an object and then keep changing it.
    /// </summary>
    public static Envelope Envelope<TBody>(
        string type, TransactionKey key, DateTimeOffset sentAt, TBody body, int retry = 0)
        where TBody : notnull
    {
        return new Envelope
        {
            Type = type,
            Stan = key.Stan,
            Terminal = key.Terminal,
            BizDate = key.BizDate,
            SentAt = sentAt.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'"),
            Retry = retry,
            Body = JsonSerializer.SerializeToElement(body, Options),
        };
    }

    /// <summary>Serialises an envelope to the UTF-8 bytes that go inside one frame.</summary>
    public static byte[] Encode(Envelope envelope) =>
        JsonSerializer.SerializeToUtf8Bytes(envelope, Options);

    /// <summary>
    /// Parses one frame's payload back into an envelope. Refuses anything that is not
    /// this protocol version, or that is missing a required field.
    /// </summary>
    public static Envelope Decode(ReadOnlySpan<byte> payload)
    {
        Envelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<Envelope>(payload, Options);
        }
        catch (JsonException ex)
        {
            // The wire content is not our business to guess at. Say what arrived was
            // unreadable and stop; a half-parsed money message is worse than none.
            throw new ProtocolViolationException($"Mesaj çözümlenemedi: {ex.Message}");
        }

        if (envelope is null)
        {
            throw new ProtocolViolationException("Mesaj boş.");
        }

        if (envelope.V != ProtocolVersion.Current)
        {
            throw new ProtocolViolationException(
                $"Protokol sürümü uyuşmuyor. Beklenen: {ProtocolVersion.Current}, gelen: {envelope.V}");
        }

        return envelope;
    }

    /// <summary>
    /// Reads the body as the record that belongs to this message type. Called only
    /// after <see cref="Envelope.Type"/> has been looked at.
    /// </summary>
    public static TBody Body<TBody>(this Envelope envelope) where TBody : notnull
    {
        var body = envelope.Body.Deserialize<TBody>(Options);

        return body ?? throw new ProtocolViolationException(
            $"{envelope.Type} mesajının gövdesi {typeof(TBody).Name} olarak okunamadı.");
    }

    /// <summary>
    /// The card number as it may appear in a journal: first six digits, then stars,
    /// then the last four. Never the whole number - see KARAR-010.
    /// </summary>
    public static string MaskPan(string pan)
    {
        var digits = pan.Where(char.IsDigit).ToArray();

        if (digits.Length < 10)
        {
            // Too short to mask meaningfully. Showing part of it would be worse than
            // showing none, because the reader could not tell how much was hidden.
            return new string('*', digits.Length);
        }

        return string.Concat(
            new string(digits, 0, 6),
            new string('*', digits.Length - 10),
            new string(digits, digits.Length - 4, 4));
    }

    /// <summary>UTF-8 text of a payload, for journals and test failure messages.</summary>
    public static string AsText(ReadOnlySpan<byte> payload) => Encoding.UTF8.GetString(payload);
}
