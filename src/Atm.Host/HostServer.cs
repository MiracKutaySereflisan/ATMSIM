// HostServer.cs
//
// What this file does: it listens on a TCP port, accepts a terminal, and runs one
// conversation at a time.
//
// Why one at a time: this simulator has one ATM (docs/model.md, rule 3 (docs/proje-kurallari.md)
// scope). Accepting several terminals would mean deciding what happens when two of
// them ask about the same account in the same millisecond - a real and interesting
// question, and one this project has declared out of scope rather than answered
// badly. A second connection is accepted only after the first has ended.
//
// Why the connection is kept open rather than opened per request: this is the shape
// real ATM-to-host links have, and it is the shape that makes this project's subject
// visible. A connection that is opened and closed around every request hides the
// interesting state - what happens to a message that was in flight when the line
// died - because there is never anything in flight for long.
//
// This file contains no decision about money. It carries bytes to HostConnection and
// nothing else; every rule that matters lives in HostService, where no socket can
// make a test flaky.

using System.Net;
using System.Net.Sockets;
using Atm.Protocol;

namespace Atm.Host;

/// <summary>Accepts terminals on a TCP port and serves them one at a time.</summary>
public sealed class HostServer : IDisposable
{
    private readonly TcpListener _listener;
    private readonly HostService _service;
    private readonly TimeSpan _readTimeout;

    /// <param name="service">The behaviour that answers messages.</param>
    /// <param name="port">TCP port. Zero asks the operating system for a free one.</param>
    /// <param name="readTimeout">How long one read waits before the loop looks up.</param>
    public HostServer(HostService service, int port = 9099, TimeSpan? readTimeout = null)
    {
        _service = service;
        _readTimeout = readTimeout ?? TimeSpan.FromSeconds(30);
        _listener = new TcpListener(IPAddress.Loopback, port);
    }

    /// <summary>The port actually in use. Meaningful after <see cref="Start"/>.</summary>
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <summary>How many terminals have been served since the listener started.</summary>
    public int ConnectionCount { get; private set; }

    /// <summary>Opens the port. Nothing is accepted until <see cref="ServeOne"/> is called.</summary>
    public void Start() => _listener.Start();

    /// <summary>
    /// Waits for one terminal, serves it until its line ends, and returns why it
    /// ended. Called in a loop by the host process; called once by a test.
    /// </summary>
    public string ServeOne(int? stopAfter = null)
    {
        using var client = _listener.AcceptTcpClient();
        ConnectionCount++;

        // The connection stays open for the whole conversation, so a small write is
        // sent immediately rather than being held back waiting for company. Without
        // this, a short answer can sit in the operating system's buffer for tens of
        // milliseconds - and a delay we did not ask for is a delay we cannot explain.
        client.NoDelay = true;

        using var transport = new StreamTransport(client.GetStream());
        var conversation = new HostConnection(_service, _readTimeout);
        conversation.Serve(transport, stopAfter);

        return conversation.EndReason;
    }

    /// <summary>Serves terminals one after another until stopped.</summary>
    public void ServeForever(CancellationToken cancel)
    {
        while (!cancel.IsCancellationRequested)
        {
            ServeOne();
        }
    }

    public void Dispose() => _listener.Stop();
}
