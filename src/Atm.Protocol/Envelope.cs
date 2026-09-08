// Copyright (c) 2026 Mirac Kutay Sereflisan. MIT lisansi - LICENSE dosyasina bakiniz.
// Envelope.cs
//
// What this file does: it is the outer part every message carries - version, type,
// trace number, terminal, business date, send time, retry counter - plus the
// type-specific body.
//
// Why the envelope is separate from the body: every message needs the same seven
// answers ("which conversation is this, which transaction, which business day"),
// and only the body differs. If each message type carried its own copy of those
// fields, a new message type could silently forget one - and the one it forgot
// would be the one the reconciliation needed.
//
// Why the body is a JsonElement rather than a typed object: the envelope must be
// readable BEFORE we know which type we are holding. The reader parses the outer
// part, looks at Type, and only then asks for the body as the matching record.
// Reversing that order would mean guessing the type from its shape.
//
// The field names here are PascalCase because that is C#'s convention; on the wire
// they become camelCase ("bizDate"), which is what the protocol spec section 2
// specifies. MessageCodec does that translation in one place.

using System.Text.Json;

namespace Atm.Protocol;

/// <summary>The outer part of every message on the wire. See the protocol spec section 2.</summary>
public sealed record Envelope
{
    /// <summary>Protocol version. Always the version this build speaks.</summary>
    public string V { get; init; } = ProtocolVersion.Current;

    /// <summary>Message type name, from <see cref="MessageType"/>.</summary>
    public required string Type { get; init; }

    /// <summary>Trace number. Same value on the request, the response and any reversal.</summary>
    public required int Stan { get; init; }

    /// <summary>Terminal identity. Part of the transaction identity - see KARAR-009.</summary>
    public required string Terminal { get; init; }

    /// <summary>Business date, "yyyy-MM-dd". Declared by the terminal, accepted by the host.</summary>
    public required string BizDate { get; init; }

    /// <summary>Send time, read from the virtual clock. Never from the machine clock.</summary>
    public required string SentAt { get; init; }

    /// <summary>0 on the first attempt. Diagnostic only - the host decides on identity, not on this.</summary>
    public int Retry { get; init; }

    /// <summary>Type-specific content, still unparsed.</summary>
    public JsonElement Body { get; init; }

    /// <summary>
    /// The three fields that identify a transaction (KARAR-009). Two messages with the
    /// same key are the same transaction, however many times they arrive.
    /// </summary>
    public TransactionKey Key => new(Terminal, BizDate, Stan);
}

/// <summary>
/// Terminal + business date + trace number. The host stores this and refuses to perform
/// the same transaction twice - see the protocol spec section 3.
/// </summary>
/// <remarks>
/// This is a record struct so that two keys with the same three values are equal without
/// anybody having to remember to write a comparison. Getting that comparison wrong would
/// mean a double debit, so it is not left to be written by hand.
/// </remarks>
public readonly record struct TransactionKey(string Terminal, string BizDate, int Stan)
{
    public override string ToString() => $"{Terminal}/{BizDate}/{Stan:D6}";
}
