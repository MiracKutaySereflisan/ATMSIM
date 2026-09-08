// Copyright (c) 2026 Mirac Kutay Sereflisan. MIT lisansi - LICENSE dosyasina bakiniz.
// PersistenceTests.cs
//
// What this file does: it switches the machine off and on again, and checks that what the
// host and the terminal owed before is still owed afterwards.
//
// Why this is not a nice-to-have: every message in the terminal's queue is about money
// that has already moved. A reversal that disappears on restart leaves a hold on somebody's
// account with nothing left to release it. A record that disappears leaves an end-of-day
// reconciliation with nothing to reconcile against.
//
// The test to read first is ARestartedTerminalStillOwesWhatItOwed - the whole point of
// putting the queue on disk in one sentence.

using Atm.Host;
using Atm.Protocol;
using Atm.Terminal;
using Xunit;

namespace Atm.Tests;

public class PersistenceTests
{
    private const string Card = "4111111111111111";
    private static readonly TransactionKey Key = new("ATM-01", "2026-01-05", 104);

    /// <summary>A folder of its own for each test, removed at the end.</summary>
    private static string NewFolder()
    {
        var path = Path.Combine(Path.GetTempPath(), "atmsim-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static Envelope Reversal(DateTimeOffset at) =>
        MessageCodec.Envelope(MessageType.ReversalRequest, Key, at,
            new ReversalRequestBody(Key.ToString(), 35_000, ReversalReason.Timeout));

    [Fact]
    public void ARestartedTerminalStillOwesWhatItOwed()
    {
        var folder = NewFolder();
        var path = Path.Combine(folder, "pending.jsonl");
        var clock = VirtualClock.StartOfBusinessDay();

        try
        {
            var before = new FilePendingHostMessages(path);
            before.Add(Reversal(clock.UtcNow), clock.UtcNow);

            // The terminal is switched off and on again.
            var after = new FilePendingHostMessages(path);

            Assert.Equal(1, after.Count);
            Assert.Equal(MessageType.ReversalRequest, after.All.Single().Message.Type);
            Assert.Equal(Key, after.All.Single().Message.Key);
            Assert.Equal(35_000,
                after.All.Single().Message.Body<ReversalRequestBody>().Amount);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void ARestartDoesNotGiveAnOwedMessageAFreshTenSecondWait()
    {
        var folder = NewFolder();
        var path = Path.Combine(folder, "pending.jsonl");
        var clock = VirtualClock.StartOfBusinessDay();

        try
        {
            var before = new FilePendingHostMessages(path);
            before.Add(Reversal(clock.UtcNow), clock.UtcNow);
            before.Attempted(before.All.Single(), clock.UtcNow);
            before.Attempted(before.All.Single(), clock.UtcNow);

            var after = new FilePendingHostMessages(path);
            var restored = after.All.Single();

            // Three attempts so far, so the next wait is sixty seconds, not ten. A
            // restart loop that reset this would hammer a host that is already silent.
            Assert.Equal(3, restored.Attempts);
            Assert.Equal(clock.UtcNow + TimeSpan.FromSeconds(60), restored.DueAt);
            Assert.Equal(0, after.Due(clock.UtcNow).Count);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void AnAcknowledgedMessageDoesNotComeBackAfterARestart()
    {
        var folder = NewFolder();
        var path = Path.Combine(folder, "pending.jsonl");
        var clock = VirtualClock.StartOfBusinessDay();

        try
        {
            var before = new FilePendingHostMessages(path);
            before.Add(Reversal(clock.UtcNow), clock.UtcNow);
            before.Acknowledge(Key, MessageType.ReversalRequest);

            Assert.Equal(0, new FilePendingHostMessages(path).Count);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void TwoOwedMessagesAboutOneTransactionBothSurvive()
    {
        var folder = NewFolder();
        var path = Path.Combine(folder, "pending.jsonl");
        var clock = VirtualClock.StartOfBusinessDay();

        try
        {
            var before = new FilePendingHostMessages(path);
            before.Add(Reversal(clock.UtcNow), clock.UtcNow);
            before.Add(MessageCodec.Envelope(MessageType.DispenseAdvice, Key, clock.UtcNow,
                new DispenseAdviceBody(Key.ToString(), DispenseOutcome.Full, 35_000, 0)),
                clock.UtcNow);

            var after = new FilePendingHostMessages(path);

            // One trace number, two messages, two separate debts (KARAR-031).
            Assert.Equal(2, after.Count);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void ARestartedHostStillHasItsRecord()
    {
        var folder = NewFolder();
        var path = Path.Combine(folder, "journal.jsonl");
        var clock = VirtualClock.StartOfBusinessDay();

        try
        {
            var before = new FileJournal(path);
            before.Append(clock.UtcNow, Key, MessageType.WithdrawalAuthRequest,
                ResponseCode.Approved, Card, "authorised", 35_000);
            before.Append(clock.UtcNow, Key, MessageType.DispenseAdvice,
                ResponseCode.Approved, Card, "FULL", 35_000);

            var after = new FileJournal(path);

            Assert.Equal(2, after.Entries.Count);
            Assert.Equal(1, after.Entries[0].Seq);
            Assert.Equal(35_000, after.Entries[1].Amount);
            Assert.Equal(MessageType.DispenseAdvice, after.Entries[1].Type);
            Assert.Equal(Key, after.Entries[1].Key);
            // And the next line carries on from where the file left off.
            Assert.Equal(3, after.Append(clock.UtcNow, Key, MessageType.EchoRequest,
                ResponseCode.Approved).Seq);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void AWholeCardNumberNeverReachesTheFile()
    {
        var folder = NewFolder();
        var path = Path.Combine(folder, "journal.jsonl");
        var clock = VirtualClock.StartOfBusinessDay();

        try
        {
            var journal = new FileJournal(path);
            journal.Append(clock.UtcNow, Key, MessageType.BalanceRequest,
                ResponseCode.Approved, Card, "balance answered");

            var text = File.ReadAllText(path);

            // The masking is not a formatting choice; it is the reason this record cannot
            // become a list of card numbers on somebody's disk.
            Assert.False(text.Contains(Card), $"the whole card number reached the file: {text}");
            Assert.True(text.Contains("411111******1111"), text);
            // And it survives the trip back without being masked a second time.
            Assert.Equal("411111******1111", new FileJournal(path).Entries.Single().MaskedPan);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void ARecordLineCarryingAnUnmaskedCardNumberIsRefused()
    {
        var folder = NewFolder();
        var path = Path.Combine(folder, "journal.jsonl");
        var clock = VirtualClock.StartOfBusinessDay();

        try
        {
            new FileJournal(path).Append(clock.UtcNow, Key, MessageType.BalanceRequest,
                ResponseCode.Approved, Card, "balance answered");

            // Somebody edits the file - or an older version of this program wrote it
            // without masking. Reading it back must not be a way in.
            File.WriteAllText(path, File.ReadAllText(path).Replace("411111******1111", Card));

            Assert.Throws<InvalidDataException>(() => new FileJournal(path));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void ADamagedLineStopsTheHostRatherThanBeingSkipped()
    {
        var folder = NewFolder();
        var path = Path.Combine(folder, "journal.jsonl");

        try
        {
            File.WriteAllText(path, "{this is not a record}" + Environment.NewLine);

            // A record that quietly drops what it cannot read is a record that reconciles
            // to zero by forgetting.
            Assert.Throws<System.Text.Json.JsonException>(() => new FileJournal(path));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void AQueueThatWasEmptiedLeavesAnEmptyFileRatherThanAnOldOne()
    {
        var folder = NewFolder();
        var path = Path.Combine(folder, "pending.jsonl");
        var clock = VirtualClock.StartOfBusinessDay();

        try
        {
            var queue = new FilePendingHostMessages(path);
            queue.Add(Reversal(clock.UtcNow), clock.UtcNow);
            queue.Acknowledge(Key, MessageType.ReversalRequest);

            Assert.Equal("", File.ReadAllText(path).Trim());
            Assert.False(File.Exists(path + ".writing"),
                "the temporary file should have been moved into place, not left behind");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void TheRecordsOnDiskAreReadableByAToolThatIsNotOurs()
    {
        // Found by reading a journal with a plain Python script (2026-08-25): .NET's
        // Encoding.UTF8 puts a three-byte mark at the front of a file it creates. Our own
        // reader strips it, so every test here passed - and a strict JSON reader stopped
        // on the very first line. These files exist so that OTHER tools can read them; a
        // record only we can read is not evidence, it is a private note.
        var folder = Path.Combine(Path.GetTempPath(), "atmsim-bom-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        try
        {
            var journalPath = Path.Combine(folder, "host.jsonl");
            var terminalPath = Path.Combine(folder, "terminal.jsonl");
            var queuePath = Path.Combine(folder, "pending.json");
            var clock = VirtualClock.StartOfBusinessDay();
            var key = new TransactionKey("ATM-01", "2026-01-05", 1);

            new FileJournal(journalPath).Append(clock.UtcNow, key,
                MessageType.BalanceRequest, ResponseCode.Approved, pan: "4111111111111111");

            new FileTerminalJournal(terminalPath).Append(clock.UtcNow, key,
                TerminalEvent.CashTaken, 35_000, pan: "4111111111111111");

            new FilePendingHostMessages(queuePath).Add(
                MessageCodec.Envelope(MessageType.DispenseAdvice, key, clock.UtcNow,
                    new DispenseAdviceBody(key.ToString(), DispenseOutcome.Full, 35_000, 0)),
                clock.UtcNow);

            foreach (var path in new[] { journalPath, terminalPath, queuePath })
            {
                var first = File.ReadAllBytes(path).Take(3).ToArray();

                Assert.False(first.Length == 3 && first[0] == 0xEF && first[1] == 0xBB && first[2] == 0xBF,
                    $"{Path.GetFileName(path)} bayt sırası işaretiyle (BOM) başlıyor; " +
                    "bizim dışımızdaki bir okuyucu ilk satırda durur.");
            }
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
