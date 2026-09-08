// Copyright (c) 2026 Mirac Kutay Sereflisan. MIT lisansi - LICENSE dosyasina bakiniz.
// ScreenServer.cs
//
// What this file does: it listens on a local port, hands the browser the screen page,
// and then keeps one WebSocket connection open with it - one picture down, one event
// up, for as long as the screen is there.
//
// Why the terminal serves the page itself instead of the file being opened from disk:
// a page opened as a file has no host and no port, so it has nothing to connect back
// to. Serving the page from the same place the connection goes to removes the
// question entirely - the screen connects to wherever it came from.
//
// Why loopback only: this is a simulator standing on one machine. Listening on every
// network interface would put an ATM screen on whatever network the laptop is on,
// which is not something to do by accident.
//
// Why one screen at a time: an ATM has one panel. A second browser is accepted only
// after the first one is gone, the same rule and the same reason as HostServer.
//
// What this file must NOT do: decide anything. It carries bytes between the browser
// and whatever the terminal's flow is. The vocabulary is in ScreenMessage.cs and the
// decisions are in the flow, where no socket can make a test unstable.

using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Atm.Terminal;

/// <summary>Serves the ATM screen page and keeps one WebSocket open with it.</summary>
public sealed class ScreenServer : IDisposable
{
    private readonly TcpListener _listener;
    private readonly string _pagePath;
    private readonly Func<ScreenEvent, ScreenView?> _onEvent;

    /// <param name="pagePath">Path to wwwroot/index.html.</param>
    /// <param name="onEvent">
    /// What the terminal does with a screen event. Returns the next picture, or null
    /// when nothing on the screen changes.
    /// </param>
    /// <param name="port">TCP port. Zero asks the operating system for a free one.</param>
    public ScreenServer(string pagePath, Func<ScreenEvent, ScreenView?> onEvent, int port = 8080)
    {
        _pagePath = pagePath;
        _onEvent = onEvent;
        _listener = new TcpListener(IPAddress.Loopback, port);
    }

    /// <summary>The port actually in use. Meaningful after <see cref="Start"/>.</summary>
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <summary>The address to open in a browser.</summary>
    public string Address => $"http://127.0.0.1:{Port}/";

    /// <summary>How many browser requests have been served since the listener started.</summary>
    public int RequestCount { get; private set; }

    /// <summary>Opens the port.</summary>
    public void Start() => _listener.Start();

    /// <summary>
    /// Serves one browser request: either the page, or an upgrade to a WebSocket that
    /// is then kept open until the screen goes away.
    /// </summary>
    /// <param name="stopAfterEvents">
    /// Stop the WebSocket loop after this many events. Used by tests so that a test
    /// ends on its own rather than on a timeout; null means "until the screen leaves".
    /// </param>
    public void ServeOne(int? stopAfterEvents = null)
    {
        using var client = _listener.AcceptTcpClient();
        client.NoDelay = true;
        RequestCount++;

        using var stream = client.GetStream();
        var head = ReadHead(stream);

        if (!head.IsUpgrade)
        {
            ServePage(stream, head);
            return;
        }

        var reply = Encoding.ASCII.GetBytes(WebSocketHandshake.UpgradeResponse(head.Key!));
        stream.Write(reply, 0, reply.Length);

        PumpScreen(stream, stopAfterEvents);
    }

    /// <summary>Serves browsers one after another until stopped.</summary>
    public void ServeForever(CancellationToken cancel)
    {
        while (!cancel.IsCancellationRequested)
        {
            try
            {
                ServeOne();
            }
            catch (IOException)
            {
                // A browser tab closed mid-message. Not an event worth stopping for;
                // the next tab gets a fresh connection.
            }
        }
    }

    /// <summary>Sends a picture to a screen that is already connected.</summary>
    public static void Send(Stream stream, ScreenView view)
    {
        var frame = WebSocketFraming.EncodeText(ScreenCodec.ToJson(view));
        stream.Write(frame, 0, frame.Length);
        stream.Flush();
    }

    // Reads bytes until the blank line that ends an HTTP request head. Reading a fixed
    // number of bytes instead would work almost always and fail on the request that
    // happened to be split differently - the same trap as message framing.
    private static HttpRequestHead ReadHead(Stream stream)
    {
        var buffer = new byte[8192];
        var got = 0;

        while (got < buffer.Length)
        {
            var read = stream.Read(buffer, got, buffer.Length - got);

            if (read == 0)
            {
                throw new IOException("Tarayıcı isteği yarıda kesildi.");
            }

            got += read;
            var text = Encoding.ASCII.GetString(buffer, 0, got);

            if (text.Contains("\r\n\r\n"))
            {
                return WebSocketHandshake.ReadHead(text);
            }
        }

        throw new InvalidOperationException("HTTP istek başlığı sınırı aştı.");
    }

    private void ServePage(Stream stream, HttpRequestHead head)
    {
        var wanted = head.Path is "/" or "/index.html";
        var exists = wanted && File.Exists(_pagePath);

        if (!exists)
        {
            var missing = Encoding.ASCII.GetBytes(
                "HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
            stream.Write(missing, 0, missing.Length);
            return;
        }

        var body = File.ReadAllBytes(_pagePath);
        var header = Encoding.ASCII.GetBytes(
            "HTTP/1.1 200 OK\r\n" +
            "Content-Type: text/html; charset=utf-8\r\n" +
            $"Content-Length: {body.Length}\r\n" +
            // The page is read from disk on every request on purpose: during a demo
            // the screen can be changed and reloaded without restarting the terminal.
            "Cache-Control: no-store\r\n" +
            "Connection: close\r\n\r\n");

        stream.Write(header, 0, header.Length);
        stream.Write(body, 0, body.Length);
        stream.Flush();
    }

    // The loop that keeps the screen alive: read frames, hand events to the terminal,
    // send back whatever picture it produced.
    private void PumpScreen(Stream stream, int? stopAfterEvents)
    {
        var buffer = new byte[WebSocketFraming.MaxPayloadBytes + 64];
        var got = 0;
        var handled = 0;

        while (true)
        {
            var read = stream.Read(buffer, got, buffer.Length - got);

            if (read == 0)
            {
                // The screen closed its side. Nothing is wrong; a customer walked away
                // or a tab was closed.
                return;
            }

            got += read;

            // Several frames can arrive stuck together, so every complete one in the
            // buffer is taken before waiting for more bytes.
            while (WebSocketFraming.TryDecode(buffer.AsSpan(0, got), out var frame, out var used))
            {
                Buffer.BlockCopy(buffer, used, buffer, 0, got - used);
                got -= used;

                if (frame!.Opcode == WebSocketOpcode.Close)
                {
                    var bye = WebSocketFraming.EncodeClose();
                    stream.Write(bye, 0, bye.Length);
                    return;
                }

                if (frame.Opcode == WebSocketOpcode.Ping)
                {
                    var pong = WebSocketFraming.Encode(WebSocketOpcode.Pong, frame.Payload);
                    stream.Write(pong, 0, pong.Length);
                    continue;
                }

                if (frame.Opcode != WebSocketOpcode.Text)
                {
                    continue;
                }

                var text = Encoding.UTF8.GetString(frame.Payload);
                var next = _onEvent(ScreenCodec.ReadEvent(text));

                if (next is not null)
                {
                    Send(stream, next);
                }

                handled++;

                if (stopAfterEvents is not null && handled >= stopAfterEvents)
                {
                    return;
                }
            }
        }
    }

    public void Dispose() => _listener.Stop();
}
