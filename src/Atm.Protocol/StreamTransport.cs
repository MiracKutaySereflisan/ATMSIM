// Copyright (c) 2026 Mirac Kutay Sereflisan. MIT lisansi - LICENSE dosyasina bakiniz.
// StreamTransport.cs
//
// What this file does: it is the real line. It carries the same ITransport that the
// in-memory one does, but over a byte stream - in practice the stream of a TCP
// socket - using the length prefix of MessageFraming and the JSON of MessageCodec.
//
// Why it is thin on purpose: everything above it is written against ITransport, so
// this class must add no behaviour of its own. Whatever it decides here, it decides
// for the demo and not for the tests, and a decision only the demo sees is a decision
// nobody has tested.
//
// Where the real world stops matching InMemoryLink, and it matters:
//
//   - A read timeout on a socket arrives as an exception, not as a polite "nothing
//     yet". It is turned back into null here, because the flow above must not care
//     how the line phrases silence.
//   - A peer that closes cleanly ends the stream. That is not an error, so Receive
//     returns null and IsOpen turns false - the caller can tell the two apart by
//     asking, rather than by catching.
//   - A line that dies badly (cable pulled, machine gone) may raise an IOException
//     here, or may raise nothing at all for a very long time. Both happen. This is
//     exactly why the protocol has an echo every thirty seconds (section 4.0):
//     the only dependable way to know a line is alive is to keep asking it.
//
// So a broken line is treated the same way the in-memory one treats a severed cable:
// nothing is thrown at the flow. What the flow sees is that answers stopped arriving,
// and it decides what that means for the money.

using System.Net.Sockets;

namespace Atm.Protocol;

/// <summary>ITransport over a byte stream. The real line, used by the demo.</summary>
public sealed class StreamTransport : ITransport
{
    private readonly Stream _stream;
    private readonly IDisposable? _owner;
    private bool _closed;

    /// <param name="stream">The stream to read and write.</param>
    /// <param name="owner">Optional thing to dispose when this end closes, e.g. the TcpClient.</param>
    public StreamTransport(Stream stream, IDisposable? owner = null)
    {
        _stream = stream;
        _owner = owner;
    }

    /// <summary>True until this end is closed or the stream is known to be finished.</summary>
    public bool IsOpen => !_closed;

    /// <summary>How many messages this end could not deliver because the line was gone.</summary>
    public int LostOnSendCount { get; private set; }

    public void Send(Envelope message)
    {
        if (_closed)
        {
            throw new TransportClosedException("This end was closed.");
        }

        try
        {
            MessageFraming.Write(_stream, MessageCodec.Encode(message));
        }
        catch (IOException)
        {
            // The line is gone. Nobody is told, because in reality nobody is told:
            // the message left and went nowhere.
            LostOnSendCount++;
            _closed = true;
        }
        catch (ObjectDisposedException)
        {
            LostOnSendCount++;
            _closed = true;
        }
    }

    public Envelope? Receive(TimeSpan timeout)
    {
        if (_closed)
        {
            throw new TransportClosedException("This end was closed.");
        }

        if (_stream.CanTimeout)
        {
            _stream.ReadTimeout = (int)Math.Max(1, timeout.TotalMilliseconds);
        }

        try
        {
            var payload = MessageFraming.Read(_stream);

            if (payload is null)
            {
                // The peer hung up cleanly between messages. Not an error.
                _closed = true;
                return null;
            }

            return MessageCodec.Decode(payload);
        }
        catch (IOException io) when (io.InnerException is SocketException
                                     { SocketErrorCode: SocketError.TimedOut })
        {
            // Silence, phrased as an exception by the operating system. The flow above
            // gets the same null the in-memory line gives, and decides what it means.
            return null;
        }
        catch (IOException)
        {
            // The line died in a way this machine happened to notice. Many deaths are
            // not noticed at all - which is why the echo exists.
            _closed = true;
            return null;
        }
        catch (ObjectDisposedException)
        {
            _closed = true;
            return null;
        }
    }

    public void Close()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;

        try
        {
            _stream.Dispose();
        }
        catch (IOException)
        {
            // Closing a line that is already gone is not news.
        }

        _owner?.Dispose();
    }

    public void Dispose() => Close();
}
