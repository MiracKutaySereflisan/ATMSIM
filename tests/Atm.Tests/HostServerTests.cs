// HostServerTests.cs
//
// What this file does: it is the one place in this repository where a test uses a
// real socket. Everything else runs on the in-memory line, on purpose.
//
// Why one such test exists anyway: the in-memory line proves the loop is right; it
// cannot prove that the bytes survive a real socket - that the length prefix works
// against a real stream, that a real read timeout comes back as silence rather than
// as a crash. That is a small, specific claim, and it needs a small, specific test.
//
// Why exactly one: a socket test depends on the machine it runs on. It can be slow
// under load and it can fail for reasons that have nothing to do with the money.
// Instruction section 5 says an unstable test is not a test, so this one is kept
// deliberately narrow: loopback only, a port the operating system picks (so two runs
// never collide), one request, one answer, and it stops itself.

using System.Net.Sockets;
using Atm.Host;
using Atm.Protocol;
using Xunit;

namespace Atm.Tests;

public class HostServerTests
{
    [Fact]
    public void ARealSocketCarriesARequestAndBringsBackAnAnswer()
    {
        var clock = VirtualClock.StartOfBusinessDay();
        using var server = new HostServer(HostService.WithDemoData(clock), port: 0,
            readTimeout: TimeSpan.FromSeconds(5));
        server.Start();

        // The server blocks while waiting for a terminal, so it is served on another
        // thread and the test plays the terminal.
        var serving = Task.Run(() => server.ServeOne(stopAfter: 1));

        using var client = new TcpClient();
        client.Connect("127.0.0.1", server.Port);
        client.NoDelay = true;
        using var transport = new StreamTransport(client.GetStream());

        transport.Send(MessageCodec.Envelope(MessageType.BalanceRequest,
            new TransactionKey("ATM-01", "2026-01-05", 301), clock.UtcNow,
            new BalanceRequestBody("4111111111111111")));

        var answer = transport.Receive(TimeSpan.FromSeconds(5));
        serving.GetAwaiter().GetResult();

        Assert.NotNull(answer);
        Assert.Equal(MessageType.BalanceResponse, answer!.Type);
        Assert.Equal(250_000, answer.Body<BalanceResponseBody>().Available);
        Assert.Equal(1, server.ConnectionCount);
    }

    [Fact]
    public void AReadThatTimesOutOnARealSocketComesBackAsSilenceNotAsACrash()
    {
        var clock = VirtualClock.StartOfBusinessDay();
        using var server = new HostServer(HostService.WithDemoData(clock), port: 0,
            readTimeout: TimeSpan.FromSeconds(5));
        server.Start();

        var serving = Task.Run(() => server.ServeOne(stopAfter: 1));

        using var client = new TcpClient();
        client.Connect("127.0.0.1", server.Port);
        using var transport = new StreamTransport(client.GetStream());

        // Ask for an answer nobody is going to send. The operating system phrases this
        // as an exception; the transport must turn it back into "no answer yet",
        // because the flow above is not allowed to care how the line phrases silence.
        var nothing = transport.Receive(TimeSpan.FromMilliseconds(200));

        Assert.True(nothing is null, "Zaman aşımı sessizlik olarak dönmeliydi.");
        Assert.True(transport.IsOpen, "Zaman aşımı hattı kapatmamalı.");

        // Let the server's ServeOne finish rather than leaving a thread blocked.
        transport.Send(MessageCodec.Envelope(MessageType.EchoRequest,
            new TransactionKey("ATM-01", "2026-01-05", 302), clock.UtcNow, new EchoBody()));
        transport.Receive(TimeSpan.FromSeconds(5));
        serving.GetAwaiter().GetResult();
    }
}
