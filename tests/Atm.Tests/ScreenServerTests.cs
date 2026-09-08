// Copyright (c) 2026 Mirac Kutay Sereflisan. MIT lisansi - LICENSE dosyasina bakiniz.
// ScreenServerTests.cs
//
// What these tests check: that a real browser could actually talk to this server.
//
// These are the only tests in the repository besides HostServerTests that open a real
// socket, and for the same reason: the handshake and the framing are already tested on
// strings and byte arrays, but "the page is served, the upgrade is accepted, an event
// goes up and a picture comes back" is a claim about the socket itself. It cannot be
// made anywhere else.
//
// They stay reproducible by asking the operating system for a free port (port 0) and
// by ending on a counted number of events rather than on a timeout.

using System.Net.Sockets;
using System.Text;
using Atm.Protocol;
using Atm.Terminal;
using Xunit;

namespace Atm.Tests;

public class ScreenServerTests
{
    private static readonly ScreenView Idle = new()
    {
        Screen = "idle",
        Title = "HOŞ GELDİNİZ",
        Lines = new[] { "Kartınızı takınız." },
    };

    private static string PagePath()
    {
        // The test binary runs from bin/, so the page is found by walking up to the
        // repository root. Hard-coding a path would break the day anybody moved the
        // output folder.
        var dir = AppContext.BaseDirectory;

        while (dir is not null && !File.Exists(Path.Combine(dir, "ATMSIM.slnx")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        return Path.Combine(dir!, "src", "Atm.Terminal", "wwwroot", "index.html");
    }

    // Reads a frame the way a BROWSER would: unmasked, because that is what a server
    // must send. WebSocketFraming.TryDecode deliberately refuses unmasked frames - it
    // only ever reads the browser's direction - so the test plays the browser here.
    private static string ReadServerText(byte[] buffer, int length)
    {
        Assert.True(length >= 2, "Sunucudan gelen çerçeve çok kısa.");
        Assert.Equal(WebSocketOpcode.Text, buffer[0] & 0x0F);
        Assert.Equal(0, buffer[1] & 0x80);

        var payloadLength = buffer[1] & 0x7F;
        var offset = 2;

        if (payloadLength == 126)
        {
            payloadLength = (buffer[2] << 8) | buffer[3];
            offset = 4;
        }

        Assert.Equal(offset + payloadLength, length);
        return Encoding.UTF8.GetString(buffer, offset, payloadLength);
    }

    private static byte[] MaskedText(string text)
    {
        var payload = Encoding.UTF8.GetBytes(text);
        var mask = new byte[] { 0x37, 0xFA, 0x21, 0x3D };
        var frame = new byte[2 + 4 + payload.Length];

        frame[0] = 0x80 | WebSocketOpcode.Text;
        frame[1] = (byte)(0x80 | payload.Length);
        Array.Copy(mask, 0, frame, 2, 4);

        for (var i = 0; i < payload.Length; i++)
        {
            frame[6 + i] = (byte)(payload[i] ^ mask[i % 4]);
        }

        return frame;
    }

    [Fact]
    public void TheBrowserIsHandedTheScreenPage()
    {
        using var server = new ScreenServer(PagePath(), _ => Idle, port: 0);
        server.Start();

        var serving = Task.Run(() => server.ServeOne());

        using var client = new TcpClient();
        client.Connect("127.0.0.1", server.Port);
        var stream = client.GetStream();

        var request = Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\nHost: x\r\n\r\n");
        stream.Write(request, 0, request.Length);

        using var reader = new StreamReader(stream, Encoding.UTF8);
        var page = reader.ReadToEnd();
        serving.GetAwaiter().GetResult();

        Assert.True(page.StartsWith("HTTP/1.1 200 ", StringComparison.Ordinal), "Sayfa 200 dönmedi.");
        Assert.True(page.Contains("ŞEREFLİŞAN"), "Sayfada banka adı yok.");
        Assert.True(page.Contains("<!DOCTYPE html>"), "Sayfa HTML değil.");
    }

    [Fact]
    public void AnUnknownPathIsRefusedRatherThanAnsweredWithTheScreen()
    {
        using var server = new ScreenServer(PagePath(), _ => Idle, port: 0);
        server.Start();

        var serving = Task.Run(() => server.ServeOne());

        using var client = new TcpClient();
        client.Connect("127.0.0.1", server.Port);
        var stream = client.GetStream();

        var request = Encoding.ASCII.GetBytes("GET /gizli HTTP/1.1\r\nHost: x\r\n\r\n");
        stream.Write(request, 0, request.Length);

        using var reader = new StreamReader(stream, Encoding.UTF8);
        var answer = reader.ReadToEnd();
        serving.GetAwaiter().GetResult();

        Assert.True(answer.StartsWith("HTTP/1.1 404 ", StringComparison.Ordinal), "404 dönmedi: " + answer);
    }

    [Fact]
    public void AnEventGoesUpAndAPictureComesBack()
    {
        ScreenEvent? seen = null;

        using var server = new ScreenServer(PagePath(), e => { seen = e; return Idle; }, port: 0);
        server.Start();

        var serving = Task.Run(() => server.ServeOne(stopAfterEvents: 1));

        using var client = new TcpClient();
        client.Connect("127.0.0.1", server.Port);
        var stream = client.GetStream();

        var upgrade = Encoding.ASCII.GetBytes(
            "GET /ws HTTP/1.1\r\nHost: x\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n" +
            "Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\nSec-WebSocket-Version: 13\r\n\r\n");
        stream.Write(upgrade, 0, upgrade.Length);

        var head = new byte[256];
        var headLength = stream.Read(head, 0, head.Length);
        var headText = Encoding.ASCII.GetString(head, 0, headLength);

        Assert.True(headText.Contains("101 Switching Protocols"), "Yükseltme kabul edilmedi: " + headText);
        Assert.True(headText.Contains("Sec-WebSocket-Accept: s3pPLMBiTxaQ9kYGzzhZRbK+xOo="),
            "Kabul değeri yanlış: " + headText);

        var hello = MaskedText("{\"event\":\"hello\",\"value\":\"\"}");
        stream.Write(hello, 0, hello.Length);

        var answer = new byte[4096];
        var got = stream.Read(answer, 0, answer.Length);
        serving.GetAwaiter().GetResult();

        var picture = ReadServerText(answer, got);
        Assert.True(picture.Contains("HOŞ GELDİNİZ"), "Beklenen ekran gelmedi: " + picture);

        Assert.NotNull(seen);
        Assert.Equal(ScreenEventName.Hello, seen!.Event);
    }

    [Fact]
    public void AnOperatorSwitchTravelsOverTheRealSocketAndComesBackOnThePicture()
    {
        // The rehearsal, in one test. Everything on the way is the real thing: a real
        // socket, the real handshake, the real framing, the real codec's list of allowed
        // events, the real flow, the real panel.
        //
        // It is here because rehearsing the demo by hand found a bug this test would have
        // caught: the codec's allow-list did not know about "demo", so the very first press
        // of the service panel closed the connection - in front of an audience, silently
        // and permanently. A vocabulary added on one side and not the other is invisible
        // until somebody uses the word.
        var demo = new DemoFaults();

        var flow = new TerminalFlow(
            ask: (_, _) => null,
            clock: new VirtualClock(new DateTimeOffset(2026, 8, 25, 9, 0, 0, TimeSpan.Zero)),
            readCard: () => "4111111111111111",
            demo: demo);

        // A card first, so there is a picture to repaint.
        flow.Handle(new ScreenEvent(ScreenEventName.Card, "inserted"));

        using var server = new ScreenServer(PagePath(), flow.Handle, port: 0);
        server.Start();

        var serving = Task.Run(() => server.ServeOne(stopAfterEvents: 1));

        using var client = new TcpClient();
        client.Connect("127.0.0.1", server.Port);
        var stream = client.GetStream();

        var upgrade = Encoding.ASCII.GetBytes(
            "GET /ws HTTP/1.1\r\nHost: x\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n" +
            "Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\nSec-WebSocket-Version: 13\r\n\r\n");
        stream.Write(upgrade, 0, upgrade.Length);

        var head = new byte[256];
        var headLength = stream.Read(head, 0, head.Length);

        Assert.True(Encoding.ASCII.GetString(head, 0, headLength).Contains("101 Switching Protocols"),
            "Yükseltme kabul edilmedi.");

        var cut = MaskedText("{\"event\":\"demo\",\"value\":\"hat-kes\"}");
        stream.Write(cut, 0, cut.Length);

        var answer = new byte[4096];
        var got = stream.Read(answer, 0, answer.Length);
        serving.GetAwaiter().GetResult();

        var picture = ReadServerText(answer, got);

        Assert.True(picture.Contains("hat kesik"),
            "Arıza ekrana damgalanmadı: " + picture);

        Assert.True(demo.LineIsDown, "Anahtar terminale ulaşmadı.");
    }

    [Fact]
    public void TheTerminalMaySayNothingChangedAndTheScreenGetsNoMessage()
    {
        // Returning null means "the picture did not change". Sending a picture anyway
        // would make the screen redraw for nothing, and would hide the difference
        // between "nothing happened" and "here is the same thing again".
        using var server = new ScreenServer(PagePath(), _ => null, port: 0);
        server.Start();

        var serving = Task.Run(() => server.ServeOne(stopAfterEvents: 1));

        using var client = new TcpClient();
        client.Connect("127.0.0.1", server.Port);
        var stream = client.GetStream();

        var upgrade = Encoding.ASCII.GetBytes(
            "GET /ws HTTP/1.1\r\nHost: x\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n" +
            "Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\nSec-WebSocket-Version: 13\r\n\r\n");
        stream.Write(upgrade, 0, upgrade.Length);

        var head = new byte[256];
        var headLength = stream.Read(head, 0, head.Length);
        Assert.True(headLength > 0, "Yükseltme cevabı gelmedi.");

        var key = MaskedText("{\"event\":\"key\",\"value\":\"5\"}");
        stream.Write(key, 0, key.Length);

        serving.GetAwaiter().GetResult();

        // The server has finished and closed; nothing was sent down.
        client.Client.ReceiveTimeout = 500;
        var answer = new byte[64];
        var got = 0;

        try
        {
            got = stream.Read(answer, 0, answer.Length);
        }
        catch (IOException)
        {
            got = 0;
        }

        Assert.Equal(0, got);
    }
}
