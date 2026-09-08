// InMemoryLinkTests.cs
//
// What this file does: it checks the fake line - that a message sent arrives, that
// waiting for an answer that never comes costs exactly the timeout, and above all
// that a severed line behaves the way a pulled cable behaves rather than the way a
// polite error message behaves.
//
// The test that matters most in this file is BothEndsStillLookOpenAfterTheCableIsCut.
// If it ever turns green by accident - because someone "improved" the transport to
// report the break - then every later scenario about timeouts would be testing a
// world in which the terminal is told what happened. That world is not the one ATMs
// live in, and the results from it would be worthless.

using Atm.Protocol;
using Xunit;

namespace Atm.Tests;

public class InMemoryLinkTests
{
    private static readonly TransactionKey Key = new("ATM-01", "2026-01-05", 104);

    private static Envelope Balance(VirtualClock clock) =>
        MessageCodec.Envelope(
            MessageType.BalanceRequest, Key, clock.UtcNow,
            new BalanceRequestBody("4111111111111111"));

    [Fact]
    public void AMessageSentFromOneEndArrivesAtTheOther()
    {
        var clock = VirtualClock.StartOfBusinessDay();
        var link = new InMemoryLink(clock);

        link.TerminalEnd.Send(Balance(clock));
        var received = link.HostEnd.Receive(TimeSpan.FromSeconds(15));

        Assert.NotNull(received);
        Assert.Equal(MessageType.BalanceRequest, received!.Type);
        Assert.Equal(Key, received.Key);
    }

    [Fact]
    public void ReadingAWaitingMessageCostsNoTime()
    {
        var clock = VirtualClock.StartOfBusinessDay();
        var link = new InMemoryLink(clock);
        var before = clock.UtcNow;

        link.TerminalEnd.Send(Balance(clock));
        link.HostEnd.Receive(TimeSpan.FromSeconds(15));

        Assert.Equal(before, clock.UtcNow);
    }

    [Fact]
    public void WaitingForAnAnswerThatNeverComesCostsExactlyTheTimeout()
    {
        var clock = VirtualClock.StartOfBusinessDay();
        var link = new InMemoryLink(clock);
        var before = clock.UtcNow;

        var answer = link.TerminalEnd.Receive(TimeSpan.FromSeconds(15));

        Assert.True(answer is null, "Kimse bir şey göndermedi; cevap null olmalıydı.");
        Assert.Equal(before.AddSeconds(15), clock.UtcNow);
    }

    [Fact]
    public void BothEndsStillLookOpenAfterTheCableIsCut()
    {
        var clock = VirtualClock.StartOfBusinessDay();
        var link = new InMemoryLink(clock);

        link.Sever();

        // This is the trap, held in place by a test: nobody is told anything.
        Assert.True(link.TerminalEnd.IsOpen, "Kesilen hatta terminal ucu açık görünmeli.");
        Assert.True(link.HostEnd.IsOpen, "Kesilen hatta host ucu açık görünmeli.");
    }

    [Fact]
    public void ASeveredLineSwallowsMessagesWithoutComplaining()
    {
        var clock = VirtualClock.StartOfBusinessDay();
        var link = new InMemoryLink(clock);

        link.Sever();
        link.TerminalEnd.Send(Balance(clock));   // no exception, and no arrival

        Assert.Equal(1, link.SwallowedMessageCount);
        Assert.Equal(0, link.HostEnd.WaitingCount);
    }

    [Fact]
    public void AMessageAlreadyInFlightIsLostWhenTheCableIsCut()
    {
        var clock = VirtualClock.StartOfBusinessDay();
        var link = new InMemoryLink(clock);

        link.TerminalEnd.Send(Balance(clock));   // sent, not yet read by the host
        link.Sever();

        // It is in neither place now. Counting it as delivered would make a later
        // reconciliation balance when the money did not.
        Assert.Equal(0, link.HostEnd.WaitingCount);
        Assert.Equal(1, link.SwallowedMessageCount);
        Assert.True(link.HostEnd.Receive(TimeSpan.FromSeconds(15)) is null,
            "Kesilmiş hatta bekleyen mesaj okunmamalı.");
    }

    [Fact]
    public void ACleanlyClosedEndRefusesToBeUsed()
    {
        var clock = VirtualClock.StartOfBusinessDay();
        var link = new InMemoryLink(clock);

        link.TerminalEnd.Close();

        // A clean close is the one case where the code DOES know what happened,
        // so here silence would be wrong and an exception is right.
        Assert.False(link.TerminalEnd.IsOpen, "Kapatılan uç açık görünmemeli.");
        Assert.Throws<TransportClosedException>(() => link.TerminalEnd.Send(Balance(clock)));
        Assert.Throws<TransportClosedException>(
            () => link.TerminalEnd.Receive(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void SendingToAnEndThatHungUpIsLostRatherThanReported()
    {
        var clock = VirtualClock.StartOfBusinessDay();
        var link = new InMemoryLink(clock);

        link.HostEnd.Close();
        link.TerminalEnd.Send(Balance(clock));   // the terminal does not know yet

        Assert.Equal(1, link.SwallowedMessageCount);
    }

    [Fact]
    public void ClosingTwiceIsHarmless()
    {
        var clock = VirtualClock.StartOfBusinessDay();
        var link = new InMemoryLink(clock);

        link.TerminalEnd.Close();
        link.TerminalEnd.Close();

        Assert.False(link.TerminalEnd.IsOpen, "İki kez kapatmak durumu değiştirmemeli.");
    }

    [Fact]
    public void MessagesKeepTheirOrder()
    {
        var clock = VirtualClock.StartOfBusinessDay();
        var link = new InMemoryLink(clock);

        for (var stan = 1; stan <= 3; stan++)
        {
            link.TerminalEnd.Send(MessageCodec.Envelope(
                MessageType.BalanceRequest,
                new TransactionKey("ATM-01", "2026-01-05", stan),
                clock.UtcNow,
                new BalanceRequestBody("4111111111111111")));
        }

        Assert.Equal(1, link.HostEnd.Receive(TimeSpan.FromSeconds(1))!.Stan);
        Assert.Equal(2, link.HostEnd.Receive(TimeSpan.FromSeconds(1))!.Stan);
        Assert.Equal(3, link.HostEnd.Receive(TimeSpan.FromSeconds(1))!.Stan);
    }

    [Fact]
    public void AnEndLearnsThePeerHungUpButOnlyAfterReadingWhatIsWaiting()
    {
        var clock = VirtualClock.StartOfBusinessDay();
        var link = new InMemoryLink(clock);

        link.TerminalEnd.Send(Balance(clock));   // sent before hanging up
        link.TerminalEnd.Close();

        // A real connection delivers what was already in flight, and only then
        // reports the end of the stream. Dropping the message here would lose one
        // that genuinely arrived.
        Assert.NotNull(link.HostEnd.Receive(TimeSpan.FromSeconds(1)));
        Assert.True(link.HostEnd.IsOpen, "Bekleyen mesaj okunana kadar uç açık kalmalı.");

        Assert.True(link.HostEnd.Receive(TimeSpan.FromSeconds(1)) is null,
            "Karşı taraf kapattıktan sonra okuma boş dönmeli.");
        Assert.False(link.HostEnd.IsOpen, "Akış bittikten sonra uç açık görünmemeli.");
    }

    [Fact]
    public void ASeveredCableTellsTheOtherEndNothing()
    {
        var clock = VirtualClock.StartOfBusinessDay();
        var link = new InMemoryLink(clock);

        link.Sever();

        // The difference that matters: a clean close reports the end of the stream,
        // a pulled cable reports nothing at all. A loop reading from this end will
        // wait out its timeout and get silence, over and over.
        Assert.True(link.HostEnd.Receive(TimeSpan.FromSeconds(15)) is null,
            "Kesilen hatta okuma boş dönmeli.");
        Assert.True(link.HostEnd.IsOpen, "Kesilen hat kapanmış gibi görünmemeli.");
    }
}
