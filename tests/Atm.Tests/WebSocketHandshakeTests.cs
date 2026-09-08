// WebSocketHandshakeTests.cs
//
// What these tests check: that the terminal recognises a real upgrade request, refuses
// the ones it must refuse, and computes the answer the standard requires.
//
// The accept-key test uses the example written into RFC 6455 itself. That matters:
// the expected value is not something this project decided, so a test that agrees with
// it is agreeing with the standard rather than with our own code.

using Atm.Terminal;
using Xunit;

namespace Atm.Tests;

public class WebSocketHandshakeTests
{
    private const string UpgradeRequest =
        "GET /ws HTTP/1.1\r\n" +
        "Host: 127.0.0.1:8080\r\n" +
        "Upgrade: websocket\r\n" +
        "Connection: Upgrade\r\n" +
        "Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\n" +
        "Sec-WebSocket-Version: 13\r\n\r\n";

    [Fact]
    public void TheAcceptValueMatchesTheOneWrittenIntoTheStandard()
    {
        // RFC 6455 section 1.3 gives this exact pair as its worked example.
        Assert.Equal("s3pPLMBiTxaQ9kYGzzhZRbK+xOo=",
            WebSocketHandshake.AcceptFor("dGhlIHNhbXBsZSBub25jZQ=="));
    }

    [Fact]
    public void AnUpgradeRequestIsRecognised()
    {
        var head = WebSocketHandshake.ReadHead(UpgradeRequest);

        Assert.Equal("GET", head.Method);
        Assert.Equal("/ws", head.Path);
        Assert.True(head.IsUpgrade, "Yükseltme isteği tanınmadı.");
        Assert.Equal("dGhlIHNhbXBsZSBub25jZQ==", head.Key);
    }

    [Fact]
    public void HeaderNamesAreReadWithoutCaringAboutCapitalisation()
    {
        // Browsers do not agree on capitalisation. A server that compares literally
        // works in one browser and fails in another, for a reason nobody can see.
        var head = WebSocketHandshake.ReadHead(UpgradeRequest.Replace("Upgrade:", "upgrade:"));

        Assert.True(head.IsUpgrade, "Küçük harfli başlık tanınmadı.");
    }

    [Fact]
    public void APlainPageRequestIsNotAnUpgrade()
    {
        var head = WebSocketHandshake.ReadHead("GET / HTTP/1.1\r\nHost: x\r\n\r\n");

        Assert.False(head.IsUpgrade, "Sıradan sayfa isteği yükseltme sanıldı.");
        Assert.Equal("/", head.Path);
    }

    [Fact]
    public void TheQueryStringIsNotPartOfThePath()
    {
        var head = WebSocketHandshake.ReadHead("GET /index.html?x=1 HTTP/1.1\r\nHost: x\r\n\r\n");

        Assert.Equal("/index.html", head.Path);
    }

    [Fact]
    public void AnUpgradeWithoutAKeyIsRefused()
    {
        // Without the key there is nothing to answer with, and answering anyway would
        // leave the browser holding a connection it thinks is a WebSocket.
        var noKey = UpgradeRequest.Replace("Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\n", "");

        Assert.Throws<InvalidOperationException>(() => WebSocketHandshake.ReadHead(noKey));
    }

    [Fact]
    public void AVersionWeDoNotSpeakIsRefused()
    {
        // Every frame after the handshake would be read with the wrong rules.
        var otherVersion = UpgradeRequest.Replace("Version: 13", "Version: 8");

        Assert.Throws<InvalidOperationException>(() => WebSocketHandshake.ReadHead(otherVersion));
    }

    [Fact]
    public void TheUpgradeReplyCarriesTheComputedAcceptValue()
    {
        var reply = WebSocketHandshake.UpgradeResponse("dGhlIHNhbXBsZSBub25jZQ==");

        Assert.True(reply.StartsWith("HTTP/1.1 101 ", StringComparison.Ordinal),
            "Cevap 101 ile başlamıyor.");
        Assert.True(reply.Contains("Sec-WebSocket-Accept: s3pPLMBiTxaQ9kYGzzhZRbK+xOo="),
            "Cevapta hesaplanmış kabul değeri yok.");
        Assert.True(reply.EndsWith("\r\n\r\n", StringComparison.Ordinal),
            "Cevap boş satırla bitmiyor.");
    }
}
