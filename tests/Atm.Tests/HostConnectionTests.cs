// HostConnectionTests.cs
//
// What this file does: it runs the host's real conversation loop on the in-memory
// line, so that "the cable is pulled in the middle of a conversation" is a test that
// takes microseconds and gives the same answer every time.
//
// Note what is NOT faked here: HostConnection and HostService are the real ones, the
// same code the demo runs. Only the line underneath them is ours. That is the whole
// argument for putting the line behind an interface - the thing being tested is the
// thing that ships.

using Atm.Host;
using Atm.Protocol;
using Xunit;

namespace Atm.Tests;

public class HostConnectionTests
{
    private const string Card = "4111111111111111";

    private static Envelope Balance(int stan, VirtualClock clock) =>
        MessageCodec.Envelope(MessageType.BalanceRequest,
            new TransactionKey("ATM-01", "2026-01-05", stan), clock.UtcNow,
            new BalanceRequestBody(Card));

    [Fact]
    public void ARequestOnTheLineIsAnsweredOnTheLine()
    {
        var clock = VirtualClock.StartOfBusinessDay();
        var link = new InMemoryLink(clock);
        var connection = new HostConnection(HostService.WithDemoData(clock));

        link.TerminalEnd.Send(Balance(201, clock));
        connection.Serve(link.HostEnd, stopAfter: 1);

        var answer = link.TerminalEnd.Receive(TimeSpan.FromSeconds(15));
        Assert.NotNull(answer);
        Assert.Equal(MessageType.BalanceResponse, answer!.Type);
        Assert.Equal(250_000, answer.Body<BalanceResponseBody>().Available);
    }

    [Fact]
    public void SeveralRequestsAreAnsweredInOrderOnOneConnection()
    {
        var clock = VirtualClock.StartOfBusinessDay();
        var link = new InMemoryLink(clock);
        var connection = new HostConnection(HostService.WithDemoData(clock));

        link.TerminalEnd.Send(Balance(202, clock));
        link.TerminalEnd.Send(Balance(203, clock));
        connection.Serve(link.HostEnd, stopAfter: 2);

        Assert.Equal(2, connection.HandledCount);
        Assert.Equal(202, link.TerminalEnd.Receive(TimeSpan.FromSeconds(1))!.Stan);
        Assert.Equal(203, link.TerminalEnd.Receive(TimeSpan.FromSeconds(1))!.Stan);
    }

    [Fact]
    public void SilenceDoesNotEndTheConversation()
    {
        var clock = VirtualClock.StartOfBusinessDay();
        var link = new InMemoryLink(clock);
        var connection = new HostConnection(HostService.WithDemoData(clock),
            readTimeout: TimeSpan.FromSeconds(30));

        // Nothing is waiting. The loop must wait, not hang up: at three in the morning
        // a working ATM is silent, and a host that hung up on silence would drop the
        // line every night.
        link.TerminalEnd.Send(Balance(204, clock));
        connection.Serve(link.HostEnd, stopAfter: 1);

        Assert.Equal("stopped after limit", connection.EndReason);
        Assert.Equal(1, connection.HandledCount);
    }

    [Fact]
    public void AConversationEndsWhenTheTerminalHangsUp()
    {
        var clock = VirtualClock.StartOfBusinessDay();
        var link = new InMemoryLink(clock);
        var connection = new HostConnection(HostService.WithDemoData(clock));

        link.TerminalEnd.Close();
        connection.Serve(link.HostEnd);

        // A clean goodbye is not an error, and the loop must not treat it as one.
        Assert.Equal("line closed", connection.EndReason);
        Assert.Equal(0, connection.HandledCount);
    }

    [Fact]
    public void TheHostAnswersIntoTheVoidWhenTheCableIsPulledMidConversation()
    {
        var clock = VirtualClock.StartOfBusinessDay();
        var link = new InMemoryLink(clock);
        var host = HostService.WithDemoData(clock);
        var connection = new HostConnection(host);

        // The request arrives, and the cable is pulled before the answer goes out.
        link.TerminalEnd.Send(Balance(205, clock));
        var request = link.HostEnd.Receive(TimeSpan.FromSeconds(1))!;
        link.Sever();
        link.HostEnd.Send(host.Handle(request));

        // This is the shape of the whole project in five lines: the host did the work
        // and wrote it down, the terminal heard nothing, and nobody was told. A
        // balance enquiry moves no money, so here it costs nothing - the same five
        // lines around a withdrawal are what a reversal exists for.
        Assert.Equal(1, host.Journal.Entries.Count);
        Assert.Equal(0, link.TerminalEnd.WaitingCount);
        Assert.True(link.SwallowedMessageCount >= 1, "Kesilen hat cevabı yutmalıydı.");
        Assert.True(link.TerminalEnd.IsOpen, "Terminal ucu hâlâ açık görünmeli.");
    }

    [Fact]
    public void ARepeatedRequestOnTheLineGetsTheSameAnswerBack()
    {
        var clock = VirtualClock.StartOfBusinessDay();
        var link = new InMemoryLink(clock);
        var host = HostService.WithDemoData(clock);
        var connection = new HostConnection(host);
        var request = Balance(206, clock);

        link.TerminalEnd.Send(request);
        link.TerminalEnd.Send(request);       // the terminal heard nothing and asked again
        connection.Serve(link.HostEnd, stopAfter: 2);

        var first = link.TerminalEnd.Receive(TimeSpan.FromSeconds(1))!;
        var second = link.TerminalEnd.Receive(TimeSpan.FromSeconds(1))!;

        Assert.Equal(1, host.ReplayCount);
        Assert.Equal(first.Body<BalanceResponseBody>().Available,
                     second.Body<BalanceResponseBody>().Available);
    }
}
