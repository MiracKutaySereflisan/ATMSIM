// Copyright (c) 2026 Mirac Kutay Sereflisan. MIT lisansi - LICENSE dosyasina bakiniz.
// HostConnection.cs
//
// What this file does: it is the host's conversation loop. Read a message, hand it to
// HostService, write the answer back, repeat until the line ends.
//
// Why it is a separate file from the server: this loop is where the host's behaviour
// meets a line, and it must be testable on a line we control. HostServer knows about
// sockets and ports; this class knows only ITransport, so every test of the loop -
// including "the cable is pulled in the middle of a conversation" - runs in memory,
// deterministically, in microseconds.
//
// The loop deliberately does not end when a read comes back empty. A null from
// Receive means "no message within the timeout", and a quiet ATM is a normal ATM: at
// three in the morning nobody is using it and the terminal's echo is the only traffic.
// The loop ends when the line itself is finished - the peer hung up, or the transport
// reports it is no longer open.
//
// What this loop does NOT do is decide anything about money. It carries messages to
// HostService and answers back. If a decision ever appears in this file, it will be a
// decision that no test of HostService can see.

using Atm.Protocol;

namespace Atm.Host;

/// <summary>One conversation between the host and one terminal.</summary>
public sealed class HostConnection
{
    private readonly HostService _service;
    private readonly TimeSpan _readTimeout;

    /// <param name="service">The behaviour that answers messages.</param>
    /// <param name="readTimeout">
    /// How long one read waits before looking up. This is not a transaction timeout;
    /// it only decides how often the loop gets a chance to notice it should stop.
    /// </param>
    public HostConnection(HostService service, TimeSpan? readTimeout = null)
    {
        _service = service;
        _readTimeout = readTimeout ?? TimeSpan.FromSeconds(30);
    }

    /// <summary>How many messages this connection has answered.</summary>
    public int HandledCount { get; private set; }

    /// <summary>Why the conversation ended. Written into the record by the caller.</summary>
    public string EndReason { get; private set; } = "not started";

    /// <summary>
    /// Runs until the line is finished or <paramref name="stopAfter"/> messages have
    /// been answered. The limit exists so a test can run the real loop and still end.
    /// </summary>
    public void Serve(ITransport transport, int? stopAfter = null)
    {
        EndReason = "running";

        try
        {
            while (transport.IsOpen)
            {
                var request = transport.Receive(_readTimeout);

                if (request is null)
                {
                    if (!transport.IsOpen)
                    {
                        // The peer hung up, or the line is known to be finished.
                        EndReason = "line closed";
                        return;
                    }

                    // Just silence. A quiet ATM is a normal ATM.
                    continue;
                }

                transport.Send(_service.Handle(request));
                HandledCount++;

                if (stopAfter is not null && HandledCount >= stopAfter)
                {
                    EndReason = "stopped after limit";
                    return;
                }
            }

            EndReason = "line closed";
        }
        catch (TransportClosedException)
        {
            // This end was closed while the loop was inside a call. Not an error.
            EndReason = "end closed";
        }
        catch (ProtocolViolationException violation)
        {
            // Something arrived that is not one of our messages. The conversation
            // cannot continue safely: we no longer know where we are in the stream.
            EndReason = $"protocol violation: {violation.Message}";
        }
    }
}
