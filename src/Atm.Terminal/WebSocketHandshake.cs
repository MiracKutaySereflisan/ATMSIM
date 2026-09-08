// WebSocketHandshake.cs
//
// What this file does: it reads the browser's request to switch a plain HTTP
// connection over to a WebSocket connection, checks that the request is a real one,
// and produces the reply that completes the switch.
//
// Why a switch is needed at all: an ordinary web request is one question and one
// answer, and then it is over. The screen needs the opposite - a line that stays open
// so the terminal can speak first, without being asked. WebSocket is the standard way
// to get that: the browser opens a normal HTTP request, asks to be upgraded, and if
// the server agrees the same connection becomes a two-way message channel.
//
// Why the reply contains a computed value rather than "yes": the browser sends a
// random key and expects it back, hashed together with a fixed text written into the
// standard (RFC 6455). Only a server that actually knows the WebSocket rules can
// produce it. This stops a plain web server, or something that caches web responses,
// from accidentally answering "fine" to an upgrade it does not understand - the
// browser would then be holding a connection that looks like a WebSocket and is not.
//
// This file touches no socket. It is given the request text and returns the response
// text, so every rule below is tested on a string.

using System.Security.Cryptography;
using System.Text;

namespace Atm.Terminal;

/// <summary>The result of reading one HTTP request from the browser.</summary>
/// <param name="Method">"GET", or whatever else was asked for.</param>
/// <param name="Path">The path asked for, without the query string.</param>
/// <param name="IsUpgrade">True when this request asks to become a WebSocket.</param>
/// <param name="Key">The browser's random key, when this is an upgrade request.</param>
public sealed record HttpRequestHead(string Method, string Path, bool IsUpgrade, string? Key);

/// <summary>Reads upgrade requests and writes the reply that completes the switch.</summary>
public static class WebSocketHandshake
{
    /// <summary>
    /// The fixed text every WebSocket server appends to the browser's key before
    /// hashing it. It is written into RFC 6455 and is the same everywhere; it is not
    /// a secret and not a key. Its only job is to make the answer impossible to
    /// produce by accident.
    /// </summary>
    public const string MagicGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

    /// <summary>The version of the WebSocket protocol browsers speak.</summary>
    public const string SupportedVersion = "13";

    /// <summary>Reads the request line and headers of one HTTP request.</summary>
    /// <param name="requestText">Everything up to and including the blank line.</param>
    public static HttpRequestHead ReadHead(string requestText)
    {
        var lines = requestText.Replace("\r\n", "\n").Split('\n');

        if (lines.Length == 0 || lines[0].Length == 0)
        {
            throw new InvalidOperationException("Boş HTTP isteği.");
        }

        var parts = lines[0].Split(' ');

        if (parts.Length < 2)
        {
            throw new InvalidOperationException($"Anlaşılmayan istek satırı: '{lines[0]}'.");
        }

        var method = parts[0];
        var path = parts[1].Split('?')[0];

        string? upgrade = null, connection = null, key = null, version = null;

        for (var i = 1; i < lines.Length; i++)
        {
            var colon = lines[i].IndexOf(':');

            if (colon <= 0)
            {
                continue;
            }

            // Header names are case-insensitive in HTTP, and browsers do not all use
            // the same capitalisation. Comparing them literally would make the server
            // work in one browser and fail in another for no visible reason.
            var name = lines[i][..colon].Trim().ToLowerInvariant();
            var value = lines[i][(colon + 1)..].Trim();

            switch (name)
            {
                case "upgrade": upgrade = value; break;
                case "connection": connection = value; break;
                case "sec-websocket-key": key = value; break;
                case "sec-websocket-version": version = value; break;
            }
        }

        var asksToUpgrade =
            string.Equals(upgrade, "websocket", StringComparison.OrdinalIgnoreCase) &&
            connection is not null &&
            connection.Contains("upgrade", StringComparison.OrdinalIgnoreCase);

        if (!asksToUpgrade)
        {
            return new HttpRequestHead(method, path, IsUpgrade: false, Key: null);
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException("Yükseltme isteğinde Sec-WebSocket-Key yok.");
        }

        if (version is not null && version != SupportedVersion)
        {
            // Refusing loudly is the point. A version we do not speak means every
            // frame after this would be read with the wrong rules.
            throw new InvalidOperationException($"Desteklenmeyen WebSocket sürümü: '{version}'.");
        }

        return new HttpRequestHead(method, path, IsUpgrade: true, Key: key);
    }

    /// <summary>
    /// Computes the value the browser is waiting for: SHA-1 of the browser's key
    /// followed by the fixed text, written in Base64.
    /// </summary>
    /// <remarks>
    /// SHA-1 is used here because the standard says so, not because anything is being
    /// protected. Nothing about this value is secret and nothing depends on it being
    /// hard to reverse; it exists so that only a server that knows the rules can
    /// produce it.
    /// </remarks>
    public static string AcceptFor(string browserKey)
    {
        var bytes = Encoding.ASCII.GetBytes(browserKey + MagicGuid);
        return Convert.ToBase64String(SHA1.HashData(bytes));
    }

    /// <summary>The complete reply that turns the connection into a WebSocket.</summary>
    public static string UpgradeResponse(string browserKey) =>
        "HTTP/1.1 101 Switching Protocols\r\n" +
        "Upgrade: websocket\r\n" +
        "Connection: Upgrade\r\n" +
        $"Sec-WebSocket-Accept: {AcceptFor(browserKey)}\r\n" +
        "\r\n";
}
