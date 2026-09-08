// Copyright (c) 2026 Mirac Kutay Sereflisan. MIT lisansi - LICENSE dosyasina bakiniz.
// ProtocolVersion.cs
//
// What this file does: it states, in code, which version of the message contract
// this build speaks.
//
// Why it exists: the protocol spec is the single source of truth for what the two
// machines say to each other, but a document cannot stop a program from shipping
// with an outdated understanding of it. This constant is the one place where the
// document and the executable touch, and a test in Atm.Tests fails if the two ever
// disagree. That test is cheap; a terminal and a host that silently speak different
// dialects are not.
//
// Careful: when the contract changes, three things change together in one commit -
// the protocol spec, this constant, and the book chapter that explains it.

namespace Atm.Protocol;

/// <summary>Identifies the message contract this build implements.</summary>
public static class ProtocolVersion
{
    /// <summary>Must match the "Sürüm" line at the top of the protocol spec.</summary>
    public const string Current = "ATMSIM/0.1";
}
