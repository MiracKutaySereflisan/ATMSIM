// Copyright (c) 2026 Mirac Kutay Sereflisan. MIT lisansi - LICENSE dosyasina bakiniz.
// HostLink.cs
//
// What this file does: it makes the real connection to the host - one TCP socket to
// a host and port - and hands it back as an ITransport.
//
// Why it is separate from TerminalClient: the client contains the rules (match the
// answer to the request, three missed echoes and the line is dead). Those rules must
// be testable on a line we can cut. So the client is handed a WAY TO CONNECT rather
// than a connection, and this file is what that way looks like in the demo.
//
// Why connecting can fail and why that is not exceptional: a terminal that starts
// before the host, or after the host restarts, will not connect. That is an ordinary
// morning at an ATM, not a crash, so the caller decides what to do about it - wait
// and try again - and this file only reports what happened.

using System.Net.Sockets;
using Atm.Protocol;

namespace Atm.Terminal;

/// <summary>Makes real connections to the host.</summary>
public sealed class HostLink
{
    private readonly string _host;
    private readonly int _port;

    public HostLink(string host = "127.0.0.1", int port = 9099)
    {
        _host = host;
        _port = port;
    }

    /// <summary>Opens one connection. Throws <see cref="SocketException"/> if the host is not there.</summary>
    public ITransport Connect()
    {
        var client = new TcpClient();
        client.Connect(_host, _port);

        // A short answer should leave immediately rather than wait in a buffer for
        // company - see KARAR-020.
        client.NoDelay = true;

        return new StreamTransport(client.GetStream(), client);
    }

    /// <summary>
    /// Tries to connect, waiting between attempts, and gives up after
    /// <paramref name="attempts"/> tries. Waiting is measured on the injected clock,
    /// so a test spends no real seconds proving that retrying works.
    /// </summary>
    public ITransport? ConnectWithRetry(IClock clock, int attempts = 5, TimeSpan? between = null)
    {
        var wait = between ?? TimeSpan.FromSeconds(2);

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                return Connect();
            }
            catch (SocketException)
            {
                if (attempt == attempts)
                {
                    return null;
                }

                clock.Sleep(wait);
            }
        }

        return null;
    }
}
