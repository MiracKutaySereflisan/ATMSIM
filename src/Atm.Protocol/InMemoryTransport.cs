// Copyright (c) 2026 Mirac Kutay Sereflisan. MIT lisansi - LICENSE dosyasina bakiniz.
// InMemoryTransport.cs
//
// What this file does: one end of an InMemoryLink. It implements ITransport, so the
// code above it - the withdrawal flow, the balance flow - cannot tell it apart from
// a real socket. That is the point: the flow is written once and tested on a line we
// control.
//
// Reading this file, notice what Receive does with time. If a message is already
// waiting it returns immediately and the clock does not move. If nothing is waiting
// it hands the whole timeout to the clock and then reports null. On the virtual
// clock that costs nothing in real seconds, and it means a test can assert something
// stronger than "it returned null": it can assert that exactly thirty seconds were
// spent waiting, which is what the protocol promised.
//
// One behaviour was missing from the first version of this file and was found by the
// host's conversation loop hanging in a test: when one end closed, the other end was
// never told, so a loop reading from it waited forever for a message that could not
// come. A real connection does tell - a clean close ends the stream and the reader
// sees end-of-stream after everything already in flight. That is now modelled here,
// and it is deliberately the ONLY thing a close reports. A cable that is pulled
// (InMemoryLink.Sever) still tells nobody anything.
//
// Nothing here is thread-safe, on purpose. One terminal, one host, one line, one
// thread of control per side - KARAR-012. Locks would add timing that we would then
// have to prove does not affect the result.

namespace Atm.Protocol;

/// <summary>One end of an in-memory line. Created by <see cref="InMemoryLink"/>.</summary>
public sealed class InMemoryTransport : ITransport
{
    private readonly InMemoryLink _link;
    private readonly Queue<Envelope> _inbox = new();
    private bool _closed;
    private bool _peerHungUp;

    internal InMemoryTransport(InMemoryLink link, string name)
    {
        _link = link;
        Name = name;
    }

    /// <summary>"terminal" or "host". Only used in messages a human reads.</summary>
    public string Name { get; }

    /// <summary>
    /// True until this end is closed cleanly. Note what this does NOT tell you:
    /// whether the line still carries anything. A severed line reports true here,
    /// exactly as a real socket does when the cable is pulled.
    /// </summary>
    public bool IsOpen => !_closed;

    /// <summary>How many messages are waiting to be read at this end.</summary>
    public int WaitingCount => _inbox.Count;

    public void Send(Envelope message)
    {
        ThrowIfClosed();

        if (_link.IsSevered)
        {
            // The bytes leave and go nowhere. The sender is told nothing, because in
            // reality it is told nothing. Everything the terminal does after this
            // point is a decision made without information.
            _link.CountSwallowed();
            return;
        }

        var other = _link.OtherEndOf(this);

        if (!other.IsOpen)
        {
            // The far end hung up cleanly. We have no way to know that yet either -
            // the message is simply lost, like sending into a closed socket before
            // the close is noticed.
            _link.CountSwallowed();
            return;
        }

        other._inbox.Enqueue(message);
    }

    public Envelope? Receive(TimeSpan timeout)
    {
        ThrowIfClosed();

        if (_inbox.Count > 0)
        {
            // Anything the peer sent before hanging up is still readable. A real
            // connection delivers what was already in flight and only then reports
            // the end - dropping it here would lose messages that did arrive.
            return _inbox.Dequeue();
        }

        if (_peerHungUp)
        {
            // Nothing waiting and the peer said goodbye: this is the end of the
            // stream, which is how a real socket reports a clean close. Not an error.
            _closed = true;
            return null;
        }

        // Nothing is waiting, and on this line nothing can arrive while we are the
        // only ones running. So the answer is already known: we will wait out the
        // whole timeout and come back empty-handed. We still spend the time, because
        // the flow above must live through the same delay a real one would.
        _link.Clock.Sleep(timeout);
        return null;
    }

    public void Close()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        _inbox.Clear();

        // Tell the other end - but only that we hung up, not that anything is wrong.
        // This is the polite goodbye a real connection sends; the cable being pulled
        // (InMemoryLink.Sever) tells nobody anything, and that difference is the
        // whole point of having both.
        _link.OtherEndOf(this).NotePeerHungUp();
    }

    public void Dispose() => Close();

    internal void NotePeerHungUp() => _peerHungUp = true;

    internal int DiscardInbox()
    {
        var lost = _inbox.Count;
        _inbox.Clear();
        return lost;
    }

    private void ThrowIfClosed()
    {
        if (_closed)
        {
            throw new TransportClosedException($"The {Name} end was closed.");
        }
    }
}
