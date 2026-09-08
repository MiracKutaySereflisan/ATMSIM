// Copyright (c) 2026 Mirac Kutay Sereflisan. MIT lisansi - LICENSE dosyasina bakiniz.
// TerminalClientTests.cs
//
// What this file does: it runs the terminal's real rules on lines that die exactly
// when the test says so - the answer that never comes, the answer that belongs to
// somebody else, and the three unanswered echoes that end a connection.
//
// The helper below is worth reading first. Switchboard hands out a new in-memory line
// every time the terminal asks to connect, and keeps the current one where the test
// can cut it. That is the whole apparatus: no sockets, no waiting, no luck.

using Atm.Protocol;
using Atm.Terminal;
using Xunit;

namespace Atm.Tests;

public class TerminalClientTests
{
    /// <summary>Hands out a fresh line on every connect and keeps the current one.</summary>
    private sealed class Switchboard(VirtualClock clock)
    {
        public InMemoryLink? Current { get; private set; }
        public int LinksHandedOut { get; private set; }

        public ITransport Connect()
        {
            Current = new InMemoryLink(clock);
            LinksHandedOut++;
            return Current.TerminalEnd;
        }
    }

    private static Envelope Balance(int stan, VirtualClock clock) =>
        MessageCodec.Envelope(MessageType.BalanceRequest,
            new TransactionKey("ATM-01", "2026-01-05", stan), clock.UtcNow,
            new BalanceRequestBody("4111111111111111"));

    private static Envelope AnswerTo(Envelope request, VirtualClock clock) =>
        MessageCodec.Envelope(MessageType.BalanceResponse, request.Key, clock.UtcNow,
            new BalanceResponseBody(ResponseCode.Approved, 250_000, 250_000));

    [Fact]
    public void AnAnswerThatBelongsToTheRequestIsReturned()
    {
        var clock = VirtualClock.StartOfBusinessDay();
        var board = new Switchboard(clock);
        using var terminal = new TerminalClient(board.Connect, clock);
        terminal.Connect();

        var request = Balance(401, clock);
        // The host side of the line answers before the terminal asks for it, which is
        // all "the host was quick" means on a line with no real time in it.
        board.Current!.HostEnd.Send(AnswerTo(request, clock));

        var answer = terminal.Exchange(request, TimeSpan.FromSeconds(15));

        Assert.NotNull(answer);
        Assert.Equal(request.Key, answer!.Key);
        Assert.Equal(250_000, answer.Body<BalanceResponseBody>().Available);
    }

    [Fact]
    public void AnAnswerForSomebodyElseIsNotTakenAsOurs()
    {
        var clock = VirtualClock.StartOfBusinessDay();
        var board = new Switchboard(clock);
        using var terminal = new TerminalClient(board.Connect, clock);
        terminal.Connect();

        var mine = Balance(402, clock);
        var somebodyElses = Balance(999, clock);      // a late answer to an older request
        board.Current!.HostEnd.Send(AnswerTo(somebodyElses, clock));
        board.Current!.HostEnd.Send(AnswerTo(mine, clock));

        var answer = terminal.Exchange(mine, TimeSpan.FromSeconds(15));

        // Taking the first message off the line would have shown this customer another
        // transaction's result. Answers are matched by key, not by arrival order.
        Assert.Equal(mine.Key, answer!.Key);
        Assert.Equal(1, terminal.LateAnswerCount);
    }

    [Fact]
    public void NoAnswerComesBackAsNullAndCostsExactlyTheTimeout()
    {
        var clock = VirtualClock.StartOfBusinessDay();
        var board = new Switchboard(clock);
        using var terminal = new TerminalClient(board.Connect, clock);
        terminal.Connect();
        var before = clock.UtcNow;

        var answer = terminal.Exchange(Balance(403, clock), TimeSpan.FromSeconds(15));

        // Null means "no answer". It does not mean the transaction did not happen -
        // the host may have done the work and lost the answer on the way back.
        Assert.True(answer is null, "Cevap gelmediğinde null dönmeliydi.");
        Assert.Equal(before.AddSeconds(15), clock.UtcNow);
    }

    [Fact]
    public void ARequestSentIntoASeveredLineGetsNoAnswer()
    {
        var clock = VirtualClock.StartOfBusinessDay();
        var board = new Switchboard(clock);
        using var terminal = new TerminalClient(board.Connect, clock);
        terminal.Connect();

        board.Current!.Sever();
        var answer = terminal.Exchange(Balance(404, clock), TimeSpan.FromSeconds(15));

        Assert.True(answer is null, "Kesik hatta cevap gelmemeliydi.");
        Assert.True(terminal.IsConnected, "Kesik hat hâlâ bağlı görünmeli - kimse haber vermedi.");
    }

    [Fact]
    public void AnAnsweredEchoKeepsTheLine()
    {
        var clock = VirtualClock.StartOfBusinessDay();
        var board = new Switchboard(clock);
        using var terminal = new TerminalClient(board.Connect, clock);
        terminal.Connect();

        PutEchoAnswerOnTheLine(board, clock, echoStan: 1);
        var alive = terminal.CheckLine();

        Assert.True(alive, "Cevaplanan echo hattı canlı saymalı.");
        Assert.Equal(0, terminal.MissedEchoes);
        Assert.Equal(1, board.LinksHandedOut);
    }

    [Fact]
    public void ThreeUnansweredEchoesEndTheConnectionAndANewOneIsMade()
    {
        var clock = VirtualClock.StartOfBusinessDay();
        var board = new Switchboard(clock);
        using var terminal = new TerminalClient(board.Connect, clock);
        terminal.Connect();
        board.Current!.Sever();

        Assert.False(terminal.CheckLine(), "Birinci echo cevapsız kalmalı.");
        Assert.Equal(1, terminal.MissedEchoes);
        Assert.False(terminal.CheckLine(), "İkinci echo cevapsız kalmalı.");
        Assert.Equal(2, terminal.MissedEchoes);
        Assert.False(terminal.CheckLine(), "Üçüncü echo cevapsız kalmalı.");

        // Three in a row is the rule of protocol section 4.0. Waiting longer only
        // delays the moment the terminal starts again.
        Assert.Equal(2, board.LinksHandedOut);
        Assert.Equal(0, terminal.MissedEchoes);
    }

    [Fact]
    public void TheNewLineWorksAfterAReconnect()
    {
        var clock = VirtualClock.StartOfBusinessDay();
        var board = new Switchboard(clock);
        using var terminal = new TerminalClient(board.Connect, clock);
        terminal.Connect();
        board.Current!.Sever();
        terminal.CheckLine();
        terminal.CheckLine();
        terminal.CheckLine();               // reconnected here

        var request = Balance(405, clock);
        board.Current!.HostEnd.Send(AnswerTo(request, clock));
        var answer = terminal.Exchange(request, TimeSpan.FromSeconds(15));

        Assert.NotNull(answer);
        Assert.Equal(2, board.LinksHandedOut);
    }

    [Fact]
    public void OneAnsweredEchoClearsTheCount()
    {
        var clock = VirtualClock.StartOfBusinessDay();
        var board = new Switchboard(clock);
        using var terminal = new TerminalClient(board.Connect, clock);
        terminal.Connect();

        // Two echoes nobody answers - the line is quiet but not given up on yet.
        terminal.CheckLine();
        terminal.CheckLine();
        Assert.Equal(2, terminal.MissedEchoes);

        // The third one is answered. A line that answers again is alive again: the
        // count is about the line's state now, not about its history. Without this
        // reset, a terminal would reconnect after three quiet moments spread over a
        // whole day, dropping a line that was working.
        PutEchoAnswerOnTheLine(board, clock, echoStan: 3);

        Assert.True(terminal.CheckLine(), "Cevaplanan echo hattı canlı saymalı.");
        Assert.Equal(0, terminal.MissedEchoes);
        Assert.Equal(1, board.LinksHandedOut);
    }

    [Fact]
    public void AskingBeforeConnectingIsRefusedLoudly()
    {
        var clock = VirtualClock.StartOfBusinessDay();
        var board = new Switchboard(clock);
        using var terminal = new TerminalClient(board.Connect, clock);

        // Silently connecting here would hide a terminal that never called Connect,
        // and the first sign of it would be in the demo.
        Assert.Throws<InvalidOperationException>(
            () => terminal.Exchange(Balance(406, clock), TimeSpan.FromSeconds(1)));
    }

    /// <summary>
    /// Puts the answer to a not-yet-sent echo on the line. The terminal numbers its
    /// echoes 1, 2, 3..., so the key is known in advance - which is what lets this
    /// test suite answer an echo without a second thread anywhere in it. Threads
    /// would bring timing, and timing would bring tests that pass four times out of
    /// five (rule 5).
    /// </summary>
    private static void PutEchoAnswerOnTheLine(Switchboard board, VirtualClock clock, int echoStan) =>
        board.Current!.HostEnd.Send(MessageCodec.Envelope(MessageType.EchoResponse,
            new TransactionKey("ATM-01", clock.UtcNow.ToString("yyyy-MM-dd"), echoStan),
            clock.UtcNow, new EchoBody()));
}
