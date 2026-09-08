// ITransport.cs
//
// What this file does: it describes "a line I can send a message down and receive a
// message from", without saying whether that line is a real TCP socket or something
// we made up for a test.
//
// Why this exists: the whole point of this project is what happens when the line
// misbehaves - it goes quiet, it dies mid-message, it swallows an answer that the
// host already acted on. A real socket cannot be told to die at an exact moment;
// the operating system decides, and it decides differently every run. A line we
// wrote ourselves can be cut on the exact message we choose, every time.
//
// Two failures that look alike from the outside and are not the same thing:
//
//   Close()  - a clean goodbye. Both sides know the conversation is over.
//   Sever()  - the cable is pulled (see InMemoryLink). Nobody is told anything.
//              Both ends still LOOK open, sending still "succeeds", and the answer
//              simply never comes. This is the realistic one, and it is the one
//              that costs money: protocol section 4.0 exists because of it.
//
// Receive returns null on timeout rather than throwing. A timeout is not an error
// and it is certainly not a rejection - the host may well have done the work.
// Instruction section 4.1 calls treating a timeout as "it did not happen" the number
// one source of mistakes in this field, so the type system is not going to help
// anyone make it: null means "no answer yet", and the caller must decide what that
// means for the money.

namespace Atm.Protocol;

/// <summary>A message-level connection between the terminal and the host.</summary>
public interface ITransport : IDisposable
{
    /// <summary>
    /// Whether this end believes the line is usable. A severed line still reports
    /// true - that is not a bug, that is the trap being modelled faithfully.
    /// </summary>
    bool IsOpen { get; }

    /// <summary>Sends one message. On a severed line the message is silently lost.</summary>
    /// <exception cref="TransportClosedException">The end was closed cleanly.</exception>
    void Send(Envelope message);

    /// <summary>
    /// Waits up to <paramref name="timeout"/> for one message.
    /// Returns null when the time runs out - which means "no answer", never "no".
    /// </summary>
    /// <exception cref="TransportClosedException">The end was closed cleanly.</exception>
    Envelope? Receive(TimeSpan timeout);

    /// <summary>Ends the conversation politely. Idempotent.</summary>
    void Close();
}

/// <summary>Thrown when code tries to use an end that was closed cleanly.</summary>
public sealed class TransportClosedException(string message) : Exception(message);
