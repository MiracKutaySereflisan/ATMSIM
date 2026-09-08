// InMemoryLink.cs
//
// What this file does: it is a line between two ends that exists only in memory -
// no network, no operating system, no timing luck. One end is handed to the
// terminal, the other to the host, and everything that happens on it is decided by
// this file.
//
// Why this exists: it is the fake cable we can pull at a moment of our choosing.
// Without it, "the line dies exactly after the host has debited the account but
// before the terminal hears about it" is a scenario we could only hope to hit; with
// it, that scenario is one method call and it happens the same way every run.
//
// The two ways a line stops working, and why they are modelled differently:
//
//   Close()  - one end hangs up. Anyone who then tries to use that end gets an
//              exception, because that end knows what happened.
//   Sever()  - the cable is cut. Neither end is told. Send() still returns without
//              complaint (the bytes leave and go nowhere) and Receive() waits out
//              its timeout and returns null. IsOpen stays true on both ends.
//
// That second behaviour is the whole reason this project exists. A terminal whose
// line has been severed does not see an error; it sees silence, and silence is
// ambiguous: the host may have done the work, or it may never have heard the
// request. The terminal cannot tell them apart, so it must not guess.
//
// Delivery is a plain queue: messages arrive in the order they were sent, and
// nothing arrives by itself while nobody is sending. This is deliberate. Real
// networks reorder and duplicate; ours does not, because a scenario that only
// reproduces one run in fifty is not a scenario. Duplicates in this project are
// produced on purpose (the retry paths of protocol section 3), not by luck.
//
// KARAR-016 records why this lives in Atm.Protocol next to the interface.

namespace Atm.Protocol;

/// <summary>
/// One in-memory line with two ends. Create it, hand out the ends, and cut it when
/// the scenario says so.
/// </summary>
public sealed class InMemoryLink
{
    private readonly IClock _clock;

    public InMemoryLink(IClock clock)
    {
        _clock = clock;
        TerminalEnd = new InMemoryTransport(this, "terminal");
        HostEnd = new InMemoryTransport(this, "host");
    }

    /// <summary>The end the terminal holds.</summary>
    public InMemoryTransport TerminalEnd { get; }

    /// <summary>The end the host holds.</summary>
    public InMemoryTransport HostEnd { get; }

    /// <summary>True once the cable has been pulled. Neither end is told.</summary>
    public bool IsSevered { get; private set; }

    /// <summary>How many messages the severed line has swallowed. Used by reports.</summary>
    public int SwallowedMessageCount { get; private set; }

    /// <summary>
    /// Pulls the cable. Anything already sent but not yet read is lost with it:
    /// a message that has left one end and not reached the other is in neither
    /// place, and pretending otherwise would make the money add up when it should
    /// not.
    /// </summary>
    public void Sever()
    {
        if (IsSevered)
        {
            return;
        }

        IsSevered = true;
        SwallowedMessageCount += TerminalEnd.DiscardInbox() + HostEnd.DiscardInbox();
    }

    internal IClock Clock => _clock;

    internal void CountSwallowed() => SwallowedMessageCount++;

    internal InMemoryTransport OtherEndOf(InMemoryTransport end) =>
        ReferenceEquals(end, TerminalEnd) ? HostEnd : TerminalEnd;
}
