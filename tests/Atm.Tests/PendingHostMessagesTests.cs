// Copyright (c) 2026 Mirac Kutay Sereflisan. MIT lisansi - LICENSE dosyasina bakiniz.
// PendingHostMessagesTests.cs
//
// What this file does: it pins down when a message that was not acknowledged is tried
// again, and that it is never queued twice or forgotten early.
//
// Why the intervals are worth a test of their own: they are the difference between a
// terminal that keeps asking a silent host politely and one that spends the whole day
// asking it as fast as it can. The second one cannot serve anybody else while it does
// that, so a host that is down would take the machine down with it.

using Atm.Protocol;
using Atm.Terminal;
using Xunit;

namespace Atm.Tests;

public class PendingHostMessagesTests
{
    private static readonly TransactionKey Key = new("ATM-01", "2026-01-05", 104);

    private static Envelope Reversal(DateTimeOffset at) =>
        MessageCodec.Envelope(MessageType.ReversalRequest, Key, at,
            new ReversalRequestBody(Key.ToString(), 35_000, ReversalReason.Timeout));

    [Fact]
    public void AQueuedMessageIsNotDueImmediatelyAndIsDueAfterTenSeconds()
    {
        var clock = VirtualClock.StartOfBusinessDay();
        var queue = new PendingHostMessages();
        queue.Add(Reversal(clock.UtcNow), clock.UtcNow);

        Assert.Equal(0, queue.Due(clock.UtcNow).Count);

        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(1, queue.Due(clock.UtcNow).Count);
    }

    [Fact]
    public void TheWaitGrowsWithEveryUnansweredAttemptAndThenStopsGrowing()
    {
        var clock = VirtualClock.StartOfBusinessDay();
        var queue = new PendingHostMessages();
        queue.Add(Reversal(clock.UtcNow), clock.UtcNow);

        var expected = new[] { 30, 60, 300, 300, 300 };

        foreach (var seconds in expected)
        {
            var pending = queue.All.Single();
            queue.Attempted(pending, clock.UtcNow);

            // One second short of the interval: still not due.
            clock.Advance(TimeSpan.FromSeconds(seconds - 1));
            Assert.Equal(0, queue.Due(clock.UtcNow).Count);

            clock.Advance(TimeSpan.FromSeconds(1));
            Assert.Equal(1, queue.Due(clock.UtcNow).Count);
        }
    }

    [Fact]
    public void EachAttemptIsCountedOnTheEnvelopeItself()
    {
        var clock = VirtualClock.StartOfBusinessDay();
        var queue = new PendingHostMessages();
        queue.Add(Reversal(clock.UtcNow), clock.UtcNow);

        queue.Attempted(queue.All.Single(), clock.UtcNow);
        queue.Attempted(queue.All.Single(), clock.UtcNow);

        // Diagnostic only - the host decides on identity, never on this number - but the
        // record has to be able to show how many times the terminal had to ask.
        Assert.Equal(2, queue.All.Single().Message.Retry);
        Assert.Equal(3, queue.All.Single().Attempts);
    }

    [Fact]
    public void TheSameMessageIsNeverQueuedTwice()
    {
        var clock = VirtualClock.StartOfBusinessDay();
        var queue = new PendingHostMessages();

        queue.Add(Reversal(clock.UtcNow), clock.UtcNow);
        queue.Add(Reversal(clock.UtcNow), clock.UtcNow);

        // Two copies would double the traffic to a host that is already not answering,
        // and one reply would acknowledge both anyway.
        Assert.Equal(1, queue.Count);
    }

    [Fact]
    public void TwoDifferentMessagesAboutOneTransactionAreBothKept()
    {
        var clock = VirtualClock.StartOfBusinessDay();
        var queue = new PendingHostMessages();

        queue.Add(Reversal(clock.UtcNow), clock.UtcNow);
        queue.Add(MessageCodec.Envelope(MessageType.DispenseAdvice, Key, clock.UtcNow,
            new DispenseAdviceBody(Key.ToString(), DispenseOutcome.Full, 35_000, 0)),
            clock.UtcNow);

        // Same trace number, different messages - and the host answers them separately
        // (KARAR-031), so the queue has to keep them separately too.
        Assert.Equal(2, queue.Count);
    }

    [Fact]
    public void AnAcknowledgedMessageStopsBeingSent()
    {
        var clock = VirtualClock.StartOfBusinessDay();
        var queue = new PendingHostMessages();
        queue.Add(Reversal(clock.UtcNow), clock.UtcNow);

        queue.Acknowledge(Key, MessageType.ReversalRequest);

        Assert.Equal(0, queue.Count);
        clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(0, queue.Due(clock.UtcNow).Count);
    }

    [Fact]
    public void AcknowledgingSomethingElseDoesNotRemoveThisOne()
    {
        var clock = VirtualClock.StartOfBusinessDay();
        var queue = new PendingHostMessages();
        queue.Add(Reversal(clock.UtcNow), clock.UtcNow);

        queue.Acknowledge(Key, MessageType.DispenseAdvice);

        Assert.Equal(1, queue.Count);
    }
}
