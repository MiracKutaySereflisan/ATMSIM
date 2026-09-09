// Copyright (c) 2026 Mirac Kutay Sereflisan. MIT lisansi - LICENSE dosyasina bakiniz.
// PinVerifier.cs
//
// What this file does: it answers one question - "does this PIN belong to this card"
// - and it counts how many times the answer has been no.
//
// What it does NOT do, and this is the point: it never stores a PIN. What it holds
// per card is a salt (a short random string) and the SHA-256 of salt + PIN. Given
// those two values you cannot read the PIN back out; you can only check a PIN you
// already have. So the running host could have its whole memory printed and the PIN
// would not be in it.
//
// An honest statement of what that claim is worth here. The cards in this project
// are invented, so there is no real secret to protect; the discipline is kept because
// a habit of storing PINs is the thing that gets carried into a system where there IS
// one. The demo PINs are written down in the setup notes, because a demo needs them,
// exactly as the test PINs of a test environment are written down. The property being
// enforced is narrower and worth stating precisely: THE SYSTEM does not store or log
// the PIN, and no code path can print it.
//
// How a real ATM network does it, and where we are not the same: the keypad encrypts
// the PIN in hardware and only an encrypted "PIN block" ever leaves it; the bank
// stores a verification value derived with keys that live inside a tamper-resistant
// box called an HSM. A salted hash is the same IDEA - keep something you can check
// against but cannot reverse - and it is not the same MECHANISM. Encryption and key
// management are declared out of scope in the assumptions list (V-02) rather than
// half-implemented, because a half-implemented one would look like the real thing.
//
// Try counting lives here rather than in the account, because it is not a property of
// the money. It resets on a correct PIN and it is per card, not per session: a
// customer who fails twice, takes the card out and comes back has one try left, not
// three. Instruction section 4 has no rule for this; the protocol spec section 4.1 and
// response code 75 do.

using System.Security.Cryptography;
using System.Text;
using Atm.Protocol;

namespace Atm.Host;

/// <summary>The stored, non-reversible form of one card's PIN.</summary>
/// <param name="Salt">Random text mixed in before hashing, different per card.</param>
/// <param name="VerificationValue">Lower-case hex SHA-256 of salt + PIN.</param>
public readonly record struct PinVerificationValue(string Salt, string VerificationValue);

/// <summary>The outcome of one PIN check, in the terms the protocol spec uses.</summary>
/// <param name="Rc">Response code: 00 correct, 55 wrong, 75 no tries left, 14 unknown card.</param>
/// <param name="RemainingTries">How many attempts are left for this card.</param>
public readonly record struct PinCheckResult(string Rc, int RemainingTries)
{
    public bool Ok => Rc == ResponseCode.Approved;
}

/// <summary>Checks PINs and counts failures. Holds no PIN.</summary>
public sealed class PinVerifier
{
    /// <summary>Attempts allowed before the card is retained (protocol section 4.1).</summary>
    public const int MaxTries = 3;

    private readonly Dictionary<string, PinVerificationValue> _byPan;
    private readonly Dictionary<string, int> _failuresByPan = [];

    public PinVerifier(IReadOnlyDictionary<string, PinVerificationValue> byPan) =>
        _byPan = new Dictionary<string, PinVerificationValue>(byPan);

    /// <summary>
    /// The demo cards' verification values. These were produced once from the demo
    /// PINs and written here; the PINs themselves appear nowhere in the source.
    /// </summary>
    public static PinVerifier WithDemoCards() => new(new Dictionary<string, PinVerificationValue>
    {
        ["4111111111111111"] = new("6fd68d75e0390f12",
            "704c8849f2199cad1adba6b8059f31e7e5917c3d783e66faaae587917a7ce17c"),
        ["4222222222222220"] = new("ee058469da40eed8",
            "c49f663a07570471e8bbbe061d2a78efc4974483745899b1b504c30f23140888"),
        ["4333333333333339"] = new("e6fd558f4765a934",
            "d5365f873ded6ca76d50d1807a651f17757a9dabd72aa747f9d44bde91e41082"),
    });

    /// <summary>How many attempts this card has left before it is retained.</summary>
    public int RemainingTries(string pan) =>
        Math.Max(0, MaxTries - (_failuresByPan.TryGetValue(pan, out var n) ? n : 0));

    /// <summary>
    /// Checks one PIN. The PIN is used inside this method and nowhere else: it is not
    /// returned, not stored, not attached to the result and not written to any record.
    /// </summary>
    public PinCheckResult Check(string pan, string pin)
    {
        if (!_byPan.TryGetValue(pan, out var stored))
        {
            // Unknown card. No counter is kept for a card we do not know, otherwise a
            // stream of invented card numbers would fill this dictionary for free.
            return new PinCheckResult(ResponseCode.UnknownCard, 0);
        }

        if (RemainingTries(pan) == 0)
        {
            // The card was already exhausted before this attempt. Checking anyway
            // would let someone keep guessing past the limit.
            return new PinCheckResult(ResponseCode.PinTriesExhausted, 0);
        }

        if (Matches(stored, pin))
        {
            _failuresByPan.Remove(pan);
            return new PinCheckResult(ResponseCode.Approved, MaxTries);
        }

        var failures = (_failuresByPan.TryGetValue(pan, out var n) ? n : 0) + 1;
        _failuresByPan[pan] = failures;
        var left = Math.Max(0, MaxTries - failures);

        return left == 0
            ? new PinCheckResult(ResponseCode.PinTriesExhausted, 0)
            : new PinCheckResult(ResponseCode.WrongPin, left);
    }

    /// <summary>
    /// Builds the stored form of a PIN. This is how the demo seed above was produced
    /// and how a test builds a card of its own; the value it returns can be written
    /// down, the PIN that went into it cannot be read back out.
    /// </summary>
    public static PinVerificationValue Create(string salt, string pin) =>
        new(salt, Hash(salt, pin));

    private static bool Matches(PinVerificationValue stored, string pin)
    {
        var computed = Hash(stored.Salt, pin);

        // Compared byte by byte in constant time. On a system with real cards, a
        // comparison that returns early on the first differing character leaks how
        // much of a guess was right, one measurement at a time. It costs nothing to
        // do it properly, so it is done properly.
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(computed),
            Encoding.ASCII.GetBytes(stored.VerificationValue));
    }

    private static string Hash(string salt, string pin) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(salt + pin)));
}
