// TerminalClient.cs
//
// What this file does: it is the terminal's side of the line. It keeps one connection
// to the host open, sends a request and waits for the matching answer, checks that
// the line is still alive, and reconnects when it is not.
//
// Why "the matching answer" and not "the next message": answers are matched by the
// transaction key, never by arrival order (docs/protocol.md section 2.1). A late
// answer to an earlier request can arrive while we are waiting for this one, and
// taking it would mean showing one customer another transaction's result.
//
// Why this file knows nothing about sockets: it is handed a way to make a connection
// and it uses whatever comes back. In the demo that is a TCP socket; in a test it is
// the in-memory line, which can be cut at a chosen moment. Every reconnection rule
// below is therefore tested on a line that dies exactly when we say so, the same way
// every run.
//
// The rule that matters most here is what happens when an answer does not come.
// Exchange returns null. It does NOT report a failure, and it does not decide that
// the transaction did not happen - because it does not know. The host may have done
// the work and lost the answer on the way back. Instruction section 4.1 calls
// treating that as "it did not happen" the number one mistake in this field. What
// happens next is the caller's decision, and from Phase 2 onwards that decision is
// usually to produce a reversal.
//
// Liveness is a separate matter from any transaction. A socket that looks open
// proves nothing: if the cable is pulled or the far machine freezes, this end can
// keep believing the line is fine for minutes. The only dependable way to know is to
// keep asking, which is what the echo is for - three unanswered in a row and the line
// is treated as dead (protocol section 4.0).

using Atm.Protocol;

namespace Atm.Terminal;

/// <summary>The terminal's end of the link to the host.</summary>
public sealed class TerminalClient : IDisposable
{
    /// <summary>Unanswered echoes in a row before the line is given up on.</summary>
    public const int MissedEchoesBeforeDead = 3;

    private readonly Func<ITransport> _connect;
    private readonly IClock _clock;
    private readonly string _terminalId;
    private ITransport? _line;
    private int _echoStan;

    public TerminalClient(Func<ITransport> connect, IClock clock, string terminalId = "ATM-01")
    {
        _connect = connect;
        _clock = clock;
        _terminalId = terminalId;
    }

    /// <summary>Whether this end currently believes it has a usable line.</summary>
    public bool IsConnected => _line?.IsOpen == true;

    /// <summary>How many times a new connection has been made, including the first.</summary>
    public int ConnectCount { get; private set; }

    /// <summary>Echoes sent with no answer, in a row. Reset by any answered echo.</summary>
    public int MissedEchoes { get; private set; }

    /// <summary>
    /// Answers that arrived for a transaction we were no longer waiting for. Not an
    /// error - it is the normal shape of a late answer - but worth counting, because
    /// a rising number means the host is answering slower than the timeouts allow.
    /// </summary>
    public int LateAnswerCount { get; private set; }

    /// <summary>Opens a connection, replacing any existing one.</summary>
    public void Connect()
    {
        _line?.Close();
        _line = _connect();
        MissedEchoes = 0;
        ConnectCount++;
    }

    /// <summary>
    /// Sends a request and waits for the answer that belongs to it.
    /// Returns null when no such answer arrives in time - which means "no answer",
    /// never "no".
    /// </summary>
    public Envelope? Exchange(Envelope request, TimeSpan timeout)
    {
        var line = _line ?? throw new InvalidOperationException("Connect() önce çağrılmalı.");
        var deadline = _clock.UtcNow + timeout;

        line.Send(request);

        while (true)
        {
            var remaining = deadline - _clock.UtcNow;

            if (remaining <= TimeSpan.Zero)
            {
                return null;
            }

            var answer = line.Receive(remaining);

            if (answer is null)
            {
                if (!line.IsOpen)
                {
                    // The line ended. Still not an answer, and still not a refusal.
                    return null;
                }

                continue;
            }

            if (answer.Key == request.Key)
            {
                return answer;
            }

            // Somebody else's answer - a late one for a transaction we have already
            // given up on. Taking it would show this customer another transaction's
            // result, so it is counted and dropped.
            LateAnswerCount++;
        }
    }

    /// <summary>
    /// Sends one echo and waits for it. Returns true when the line answered.
    /// After <see cref="MissedEchoesBeforeDead"/> unanswered echoes in a row the line
    /// is given up on and a new connection is made.
    /// </summary>
    public bool CheckLine(TimeSpan? echoTimeout = null)
    {
        var line = _line ?? throw new InvalidOperationException("Connect() önce çağrılmalı.");
        var timeout = echoTimeout ?? TimeSpan.FromSeconds(10);

        var echo = MessageCodec.Envelope(MessageType.EchoRequest,
            new TransactionKey(_terminalId, BusinessDate(), NextEchoStan()),
            _clock.UtcNow, new EchoBody());

        var answer = Exchange(echo, timeout);

        if (answer is not null)
        {
            MissedEchoes = 0;
            return true;
        }

        MissedEchoes++;

        if (MissedEchoes >= MissedEchoesBeforeDead)
        {
            // Three in a row. Whatever this line is, it is not carrying messages, and
            // waiting longer only delays the moment we start again.
            Connect();
        }

        return false;
    }

    /// <summary>The business date this terminal is stamping messages with.</summary>
    public string BusinessDate() => _clock.UtcNow.ToString("yyyy-MM-dd");

    /// <summary>Trace numbers for echoes. Kept apart from transaction trace numbers.</summary>
    private int NextEchoStan() => _echoStan = _echoStan >= 999_999 ? 1 : _echoStan + 1;

    public void Dispose()
    {
        _line?.Close();
        _line = null;
    }
}
