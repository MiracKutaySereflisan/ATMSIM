// Copyright (c) 2026 Mirac Kutay Sereflisan. MIT lisansi - LICENSE dosyasina bakiniz.
// ScreenMessage.cs
//
// What this file does: it defines the entire vocabulary the browser screen and the
// terminal may use with each other. Two directions, and nothing else exists.
//
//   terminal -> screen : ScreenView  - "draw exactly this"
//   screen -> terminal : ScreenEvent - "this physical thing happened"
//
// Why this is a SEPARATE contract from the protocol spec: the browser must never see
// a host message. If the screen were handed the host's answer, then somewhere in the
// browser a line of code would have to look at a response code and decide what it
// means - and that decision is business logic. The moment business logic can be
// edited with the browser's developer tools, the machine that decides about money is
// the customer's browser. So the two protocols are deliberately unrelated: one talks
// about accounts, the other talks about pixels and keys.
//
// Why the screen reports PHYSICAL events rather than intentions: a real ATM's panel
// does not know what a transaction is. A key was pressed; a card was inserted; the
// cash was taken from the mouth. That is all it can honestly report. If the browser
// were allowed to send "withdraw 200 lira", it would have had to decide that the
// customer wants a withdrawal - a decision that does not belong to it.
//
// Consequence worth noticing: the terminal, not the browser, counts the PIN digits.
// The browser is told "draw three asterisks"; it never keeps the digits and never
// decides how many to draw. Every asterisk on the screen came from the terminal.
//
// Simplification, recorded in the assumptions list: on a real ATM the PIN is typed
// into a sealed keypad (an encrypting PIN pad) that releases only an encrypted block,
// and the digits never travel as digits. Here they travel as key events on a loopback
// connection. Encryption is out of scope for the whole project - KARAR-010.

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;

namespace Atm.Terminal;

/// <summary>
/// One complete picture of the screen. The browser draws this and holds no other
/// state of its own.
/// </summary>
public sealed record ScreenView
{
    /// <summary>
    /// Which layout to draw: "idle", "pin", "menu", "balance", "amount", "cash",
    /// "receipt", "message", "error", "wait".
    /// </summary>
    public required string Screen { get; init; }

    /// <summary>The heading line, in ATM language.</summary>
    public required string Title { get; init; }

    /// <summary>Body lines, already in the wording the customer should read.</summary>
    public IReadOnlyList<string> Lines { get; init; } = Array.Empty<string>();

    /// <summary>Labels for the four keys down the left of the screen. Null means blank.</summary>
    public IReadOnlyList<string?> SoftLeft { get; init; } = new string?[4];

    /// <summary>Labels for the four keys down the right of the screen. Null means blank.</summary>
    public IReadOnlyList<string?> SoftRight { get; init; } = new string?[4];

    /// <summary>How many asterisks to draw. Counted by the terminal, never by the browser.</summary>
    public int MaskedLength { get; init; }

    /// <summary>Card slot: "out", "reading", "in", "returned", "captured".</summary>
    public string Card { get; init; } = "out";

    /// <summary>Cash mouth: "closed", "presenting", "taken", "retracted".</summary>
    public string CashPort { get; init; } = "closed";

    /// <summary>
    /// Deposit slot: "closed", "open", "counting", "returned", "swallowed".
    /// </summary>
    /// <remarks>
    /// A separate port from <see cref="CashPort"/> because on the machine they are two
    /// separate holes (rule 7), and because they mean opposite things: the
    /// cash mouth is where the bank's money becomes the customer's, the deposit slot is
    /// where the customer's money becomes the bank's. Drawing them as one hole would hide
    /// the exact distinction this phase exists to show.
    /// </remarks>
    public string DepositPort { get; init; } = "closed";

    /// <summary>Receipt slot: "none", "printing", "presented", "taken".</summary>
    public string Receipt { get; init; } = "none";

    /// <summary>Seconds left before this screen times out. Null means no countdown.</summary>
    public int? CountdownSeconds { get; init; }

    /// <summary>
    /// The receipt, line by line, when there is one to draw. Empty otherwise.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Lines"/> because it is a different piece of paper: the
    /// screen's lines are read now and gone, the receipt is taken away and kept. The
    /// browser draws it in the receipt slot and it has to stay readable and printable
    /// (rule 7). Composed by the terminal, like everything else - a
    /// browser that could write its own receipt could write any amount on it.
    /// </remarks>
    public IReadOnlyList<string> ReceiptLines { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Which demonstration faults are switched on, in words. Empty when none are.
    /// </summary>
    /// <remarks>
    /// Carried on EVERY picture rather than on the one that switched it, because a machine
    /// with a fault silently on is a machine somebody will one day show to a user as
    /// if it were working normally (KARAR-053). The browser only displays this; it is not
    /// the browser's memory of what it pressed.
    /// </remarks>
    public string Demo { get; init; } = "";
}

/// <summary>
/// One physical thing that happened at the panel. Never an intention, never an amount,
/// never a decision.
/// </summary>
/// <param name="Event">One of <see cref="ScreenEventName"/>.</param>
/// <param name="Value">
/// What was touched: a digit "0".."9", a keypad key, a soft key position "L1".."R4",
/// or an empty string for events that carry nothing.
/// </param>
public sealed record ScreenEvent(
    [property: JsonPropertyName("event")] string Event,
    [property: JsonPropertyName("value")] string Value);

/// <summary>The complete list of things the screen may report.</summary>
public static class ScreenEventName
{
    /// <summary>The browser has just connected and wants the current picture.</summary>
    public const string Hello = "hello";

    /// <summary>A keypad key: a digit, or "enter", "cancel", "correct".</summary>
    public const string Key = "key";

    /// <summary>A key beside the screen: "L1".."L4", "R1".."R4".</summary>
    public const string Soft = "soft";

    /// <summary>The card slot: "inserted" or "taken".</summary>
    public const string Card = "card";

    /// <summary>The cash mouth: "taken".</summary>
    public const string Cash = "cash";

    /// <summary>
    /// Banknotes were pushed into the deposit slot. Carries which notes, as
    /// "200x2,50x1" - denomination in lira, then how many of them.
    /// </summary>
    /// <remarks>
    /// This is a physical fact, not a decision, and that is why it is allowed to come
    /// from the browser at all (KARAR-024). It is the same kind of event as a card being
    /// inserted: the panel reports what was physically put in, and the terminal decides
    /// what that is worth, whether the machine has a drawer for it, and what happens
    /// next. Note what it does NOT carry: an amount. The customer does not type what
    /// they are depositing - the machine counts it. A deposit where the amount comes
    /// from the customer rather than from the counter is a deposit nobody can audit.
    /// </remarks>
    public const string Notes = "notes";

    /// <summary>The receipt slot: "taken".</summary>
    public const string Receipt = "receipt";

    /// <summary>
    /// A second of real time passed at the panel. Carries nothing.
    /// </summary>
    /// <remarks>
    /// Why the browser reports time rather than deciding about it: a countdown that runs
    /// out is a DECISION - give the card back, pull the cash in - and decisions do not
    /// live in the browser (KARAR-024). So the screen reports the one thing it can
    /// honestly know, that a second went by, and the terminal reads its own clock and
    /// decides. In a test the clock is virtual and the ticks are written by hand, which
    /// is why a timeout can be tested without waiting for one (KARAR-036).
    /// </remarks>
    public const string Tick = "tick";

    /// <summary>
    /// The operator flipped a demonstration switch. The value names the switch and
    /// nothing else - what it MEANS is decided on this side (KARAR-053).
    /// </summary>
    public const string Demo = "demo";
}

/// <summary>Turns screen messages into JSON text and back.</summary>
public static class ScreenCodec
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,

        // Turkish letters are written as themselves rather than as escape sequences.
        // By default anything outside plain ASCII is escaped, so "HOŞ GELDİNİZ" would
        // travel as "HO\u015E GELD\u0130N\u0130Z". A browser reads that correctly,
        // so nothing would break - but every message on the screen link would be
        // unreadable to a person watching it, and this project's whole method is
        // looking at what goes over the wire.
        //
        // The wider "relaxed" encoder was not used: it also stops escaping < > and &,
        // and this text ends up inside a web page. Allowing all letters while keeping
        // those three escaped costs nothing and removes a question.
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    /// <summary>Renders a picture as the JSON text that goes down the WebSocket.</summary>
    public static string ToJson(ScreenView view) => JsonSerializer.Serialize(view, Options);

    /// <summary>
    /// Reads an event sent by the browser. Anything that is not in the vocabulary is
    /// refused loudly rather than ignored: a message we do not understand coming from
    /// the screen means the screen and the terminal no longer agree on the contract,
    /// and continuing would mean guessing what the customer did.
    /// </summary>
    public static ScreenEvent ReadEvent(string json)
    {
        ScreenEvent? parsed;

        try
        {
            parsed = JsonSerializer.Deserialize<ScreenEvent>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Ekrandan gelen mesaj okunamadı: {ex.Message}", ex);
        }

        if (parsed is null)
        {
            throw new InvalidOperationException("Ekrandan boş mesaj geldi.");
        }

        if (!IsKnown(parsed.Event))
        {
            throw new InvalidOperationException($"Ekrandan tanınmayan olay geldi: '{parsed.Event}'.");
        }

        return parsed;
    }

    private static bool IsKnown(string name) => name is
        ScreenEventName.Hello or ScreenEventName.Key or ScreenEventName.Soft or
        ScreenEventName.Card or ScreenEventName.Cash or ScreenEventName.Notes or
        ScreenEventName.Receipt or ScreenEventName.Tick or ScreenEventName.Demo;
}
