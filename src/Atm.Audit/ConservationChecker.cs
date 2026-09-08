// Copyright (c) 2026 Mirac Kutay Sereflisan. MIT lisansi - LICENSE dosyasina bakiniz.
// ConservationChecker.cs
//
// What this file does: it is the central claim of this project, written as something that
// can be run rather than something that can be said.
//
// The claim, from rule 3:
//
//   an account was debited  -> either the cash reached the customer, or a reversal exists
//   an account was credited -> the notes behind it reached a drawer
//   cash left the machine   -> an approved authorisation exists for it
//   what left the cassettes  = handed over + retracted + waiting at the mouth
//   the host's record        = the terminal's record  (at the end-of-day reconciliation)
//
// Deposits made the second line necessary and changed the shape of the third. Until money
// could come IN, the machine's buckets could only be rearranged, so their sum could not
// change. Now a banknote can arrive from outside - and an arrival looks exactly like money
// being created unless the machine counts what came in through its mouth. That counter is
// CashPosition.TakenFromCustomers, and it is why Total is still a straight line.
//
// Nine checks below. Each one is written so that it can only pass for the right reason:
// none of them reads a number that the thing being checked also wrote. Check 3 compares
// the terminal's record against the terminal's own dispenser - two things that CAN
// disagree, which is what makes agreement mean something. Check 6 compares two records
// kept by two machines that never see each other's.
//
// The trap this file is built to avoid: a checker that reports a difference every time
// money is legitimately in flight. Cash is handed over a moment before the host is told
// about it, and between those two moments the books genuinely do not balance. A checker
// that shouted there would be shouting on every single withdrawal. So every difference is
// first asked one question - is there a message in the queue that explains you? - and
// only an unexplained difference is a violation. That is also exactly how a real
// reconciliation reads: items in transit are listed, not alarmed about.
//
// What this file does NOT do: repair anything. It counts, names and stops. A checker that
// corrected what it found would be the silent repair rule 6b forbids, and
// the next run would show a clean book with the evidence removed.

using Atm.Host;
using Atm.Protocol;
using Atm.Terminal;

namespace Atm.Audit;

/// <summary>Runs the money-conservation checks over one snapshot.</summary>
public static class ConservationChecker
{
    /// <summary>Checks everything and reports what does not add up.</summary>
    public static ConservationReport Check(AuditSnapshot snapshot)
    {
        var findings = new List<AuditFinding>();

        CheckCashTotal(snapshot, findings);
        CheckRecordAgainstDispenser(snapshot, findings);
        CheckCustomerAgainstLedger(snapshot, findings);
        CheckDebitsHaveAuthorisations(snapshot, findings);
        CheckCreditsReachedADrawer(snapshot, findings);
        CheckReconciliation(snapshot, findings);
        CheckOpenAuthorisations(snapshot, findings);
        CheckOpenDeposits(snapshot, findings);
        CheckStuckMoney(snapshot, findings);

        return new ConservationReport { Findings = findings, Moment = snapshot.Moment };
    }

    // 1. Nothing appears and nothing vanishes INSIDE the machine. Notes move between the
    // cassettes, the mouth, the customer and the retract bin; the four together are the
    // whole of what the machine can account for, so their sum cannot change by itself.
    private static void CheckCashTotal(AuditSnapshot s, List<AuditFinding> findings)
    {
        if (s.CashNow.Total == s.CashAtStart.Total)
        {
            return;
        }

        var difference = s.CashNow.Total - s.CashAtStart.Total;

        findings.Add(new AuditFinding(
            AuditCode.CashTotal,
            difference > 0 ? "Makinede olması gerekenden fazla para var"
                           : "Makinede olması gerekenden az para var",
            $"başlangıç {s.CashAtStart.Total}, şimdi {s.CashNow.Total}, fark {difference} kuruş",
            Math.Abs(difference)));
    }

    // 2. The machine's own record has to match the machine's own hands. These are two
    // separate things: the dispenser moves notes, the journal writes lines, and nothing
    // forces them to agree. A dispenser that hands over cash without a line, or a line
    // written for cash that never came out, both show up here.
    private static void CheckRecordAgainstDispenser(AuditSnapshot s, List<AuditFinding> findings)
    {
        // Everything that left the machine, whichever mouth it left by: cash paid out, and
        // deposited notes handed back. Both are money in a customer's hand and neither is
        // in the machine any more.
        var takenInRecord = SumOf(s, TerminalEvent.CashTaken) + SumOf(s, TerminalEvent.DepositReturned);
        var takenInMachine = s.CashNow.WithCustomers - s.CashAtStart.WithCustomers;

        if (takenInRecord != takenInMachine)
        {
            findings.Add(new AuditFinding(
                AuditCode.RecordVersusDispenser,
                "Terminal günlüğü ile dağıtıcı, müşteriye verilen para konusunda ayrı şey söylüyor",
                $"günlükte {takenInRecord}, dağıtıcıda {takenInMachine} kuruş",
                Math.Abs(takenInRecord - takenInMachine)));
        }

        var retractedInRecord = SumOf(s, TerminalEvent.CashRetracted);
        var retractedInMachine = s.CashNow.Retracted - s.CashAtStart.Retracted;

        if (retractedInRecord != retractedInMachine)
        {
            findings.Add(new AuditFinding(
                AuditCode.RecordVersusDispenser,
                "Terminal günlüğü ile dağıtıcı, geri alınan para konusunda ayrı şey söylüyor",
                $"günlükte {retractedInRecord}, dağıtıcıda {retractedInMachine} kuruş",
                Math.Abs(retractedInRecord - retractedInMachine)));
        }

        // What came IN through the deposit mouth. Without this line an arriving banknote
        // would be indistinguishable from money appearing out of nowhere.
        var countedInRecord = SumOf(s, TerminalEvent.DepositCounted);
        var countedInMachine = s.CashNow.TakenFromCustomers - s.CashAtStart.TakenFromCustomers;

        if (countedInRecord != countedInMachine)
        {
            findings.Add(new AuditFinding(
                AuditCode.RecordVersusDispenser,
                "Terminal günlüğü ile makine, içeri alınan para konusunda ayrı şey söylüyor",
                $"günlükte {countedInRecord}, makinede {countedInMachine} kuruş",
                Math.Abs(countedInRecord - countedInMachine)));
        }

        var jammedInRecord = SumOf(s, TerminalEvent.DepositJammed);
        var jammedInMachine = s.CashNow.Jammed - s.CashAtStart.Jammed;

        if (jammedInRecord != jammedInMachine)
        {
            findings.Add(new AuditFinding(
                AuditCode.RecordVersusDispenser,
                "Terminal günlüğü ile makine, sıkışan para konusunda ayrı şey söylüyor",
                $"günlükte {jammedInRecord}, makinede {jammedInMachine} kuruş",
                Math.Abs(jammedInRecord - jammedInMachine)));
        }
    }

    // 3. The central claim in one line: what the customers are holding is what the
    // accounts lost. The difference between the two is money that exists in one place and
    // not in the other - which is either a customer who was given money nobody charged
    // for, or a customer who was charged for money they never received.
    private static void CheckCustomerAgainstLedger(AuditSnapshot s, List<AuditFinding> findings)
    {
        // The claim, with deposits in it: an account falls by exactly the cash a customer
        // walked away with, and rises by exactly the notes that reached a drawer. Money
        // handed back out of escrow moves no ledger at all - it was never the bank's - and
        // money stuck in the mechanism moves none either, because it is in no drawer.
        var handedOver = SumOf(s, TerminalEvent.CashTaken);
        var stacked = SumOf(s, TerminalEvent.DepositStacked);
        var ledgerFall = s.LedgerFall;
        var expectedFall = handedOver - stacked;

        if (expectedFall == ledgerFall)
        {
            return;
        }

        // Before calling it a violation: is there a report on its way that would close
        // exactly this gap? Cash leaves the machine before the host hears about it, and
        // during that window these two numbers are SUPPOSED to differ.
        var inFlight = AdvicesInFlight(s).Sum(a => a.Amount);
        var difference = expectedFall - ledgerFall;

        var reason = difference == inFlight && inFlight != 0
            ? $"{AdvicesInFlight(s).Count} bildirim hâlâ kuyrukta"
            : "";

        findings.Add(new AuditFinding(
            AuditCode.CustomerVersusLedger,
            difference > 0
                ? "Müşteride, hesaplara yazılandan fazla para var"
                : "Hesaplara, müşteriden alınandan fazla para yazılmış",
            $"müşteriye giden {handedOver}, kasete giren {stacked}, " +
            $"hesaplardan net düşen {ledgerFall} kuruş",
            Math.Abs(difference),
            reason));
    }

    // 4. Cash that left the machine has to point back at a promise. This walks the host's
    // record: every line that moved a ledger must have an approved authorisation with the
    // same key written EARLIER in the same record.
    private static void CheckDebitsHaveAuthorisations(AuditSnapshot s, List<AuditFinding> findings)
    {
        var approvedAuths = s.HostJournal
            .Where(e => e.Type == MessageType.WithdrawalAuthRequest && e.Rc == ResponseCode.Approved)
            .Select(e => e.Key.ToString())
            .ToHashSet();

        foreach (var debit in s.HostJournal.Where(IsLedgerMovement))
        {
            if (approvedAuths.Contains(debit.Key.ToString()))
            {
                continue;
            }

            findings.Add(new AuditFinding(
                AuditCode.DebitWithoutAuthorisation,
                "Onaylanmış bir yetkilendirmesi olmayan bir borçlanma var",
                $"işlem {debit.Key}, {debit.Amount} kuruş, defter satırı {debit.Seq}",
                debit.Amount));
        }
    }

    // 4b. The mirror of check 4: an account that rose has to point at notes that reached a
    // drawer. Getting this one wrong is how a machine credits a customer for banknotes it
    // handed straight back to them.
    private static void CheckCreditsReachedADrawer(AuditSnapshot s, List<AuditFinding> findings)
    {
        var agreed = s.HostJournal
            .Where(e => e.Type == MessageType.DepositAuthRequest && e.Rc == ResponseCode.Approved)
            .Select(e => e.Key.ToString())
            .ToHashSet();

        foreach (var credit in s.HostJournal.Where(IsCredit))
        {
            if (agreed.Contains(credit.Key.ToString()))
            {
                continue;
            }

            findings.Add(new AuditFinding(
                AuditCode.CreditWithoutDeposit,
                "Kabul edilmiş bir yatırması olmayan bir alacak var",
                $"işlem {credit.Key}, {credit.Amount} kuruş, defter satırı {credit.Seq}",
                credit.Amount));
        }
    }

    // 7. A deposit the host agreed to and never heard the end of. It holds nothing, so it
    // costs nobody money today - what it costs is knowledge: somewhere a machine may be
    // sitting on banknotes this account has not been credited for.
    private static void CheckOpenDeposits(AuditSnapshot s, List<AuditFinding> findings)
    {
        if (s.Moment != AuditMoment.EndOfDay)
        {
            return;
        }

        foreach (var key in s.OpenDeposits)
        {
            var commitQueued = InFlight(s, key,
                TerminalEvent.DepositCommitQueued, TerminalEvent.DepositCommitAcknowledged);

            var counted = s.HostJournal
                .Where(e => e.Key.ToString() == key
                         && e.Type == MessageType.DepositAuthRequest
                         && e.Rc == ResponseCode.Approved)
                .Sum(e => e.Amount);

            findings.Add(new AuditFinding(
                AuditCode.OpenDeposit,
                "Gün sonunda sonu bildirilmemiş bir yatırma var",
                $"işlem {key}, {counted} kuruş kabul edilmiş, sonucu bilinmiyor",
                counted,
                commitQueued ? "bu yatırmanın bildirimi hâlâ kuyrukta" : ""));
        }
    }

    // 8. Money that is inside the machine and belongs to nobody: notes stuck in the
    // mechanism, and a stack that was started and never finished.
    //
    // Both are reported with a reason rather than as violations, and the difference
    // matters: a jam is a real, named, physically explainable event and an engineer closes
    // it. An unfinished stack is NOT explainable - the machine does not know whether the
    // notes reached a drawer - so it stays a violation (KARAR-041).
    private static void CheckStuckMoney(AuditSnapshot s, List<AuditFinding> findings)
    {
        var jammed = s.CashNow.Jammed - s.CashAtStart.Jammed;

        if (jammed > 0)
        {
            findings.Add(new AuditFinding(
                AuditCode.StuckMoney,
                "Mekanizmada sıkışmış para var",
                $"{jammed} kuruş, ne kasette ne müşteride",
                jammed,
                "sıkışma: makinenin açılması gerekiyor"));
        }

        foreach (var key in s.TerminalJournal
                     .Where(e => e.Event == TerminalEvent.DepositStacking)
                     .Select(e => e.Key.ToString())
                     .Distinct())
        {
            var lines = s.TerminalJournal.Where(e => e.Key.ToString() == key).ToList();

            var finished = lines.Any(e => e.Event is TerminalEvent.DepositStacked
                or TerminalEvent.DepositJammed or TerminalEvent.DepositReturned);

            if (finished)
            {
                continue;
            }

            findings.Add(new AuditFinding(
                AuditCode.StuckMoney,
                "Başlanmış ve bitmemiş bir kasete alma var; paranın nerede olduğu bilinmiyor",
                $"işlem {key}: 'alıyorum' yazılmış, sonucu yazılmamış",
                lines.Where(e => e.Event == TerminalEvent.DepositStacking).Sum(e => e.Amount)));
        }

        if (s.Moment == AuditMoment.EndOfDay && s.CashNow.InEscrow > 0)
        {
            findings.Add(new AuditFinding(
                AuditCode.StuckMoney,
                "Gün sonunda ara kasada para kalmış",
                $"{s.CashNow.InEscrow} kuruş escrow'da; ne müşteriye verildi ne kasete girdi",
                s.CashNow.InEscrow));
        }
    }

    // 5. The end-of-day reconciliation: the host's record and the terminal's record, one
    // transaction at a time. The host says what it posted; the terminal says what a hand
    // took. Neither can see the other, and that is the point - two records that agree
    // were kept independently, and two that disagree name the transaction to look at.
    private static void CheckReconciliation(AuditSnapshot s, List<AuditFinding> findings)
    {
        var hostPosted = s.HostJournal
            .Where(IsLedgerMovement)
            .GroupBy(e => e.Key.ToString())
            .ToDictionary(g => g.Key, g => g.Sum(e => e.Amount));

        var terminalHandedOver = s.TerminalJournal
            .Where(e => e.Event == TerminalEvent.CashTaken)
            .GroupBy(e => e.Key.ToString())
            .ToDictionary(g => g.Key, g => g.Sum(e => e.Amount));

        var hostCredited = s.HostJournal
            .Where(IsCredit)
            .GroupBy(e => e.Key.ToString())
            .ToDictionary(g => g.Key, g => g.Sum(e => e.Amount));

        var terminalStacked = s.TerminalJournal
            .Where(e => e.Event == TerminalEvent.DepositStacked)
            .GroupBy(e => e.Key.ToString())
            .ToDictionary(g => g.Key, g => g.Sum(e => e.Amount));

        foreach (var key in hostPosted.Keys.Union(terminalHandedOver.Keys).OrderBy(k => k))
        {
            var posted = hostPosted.GetValueOrDefault(key);
            var handed = terminalHandedOver.GetValueOrDefault(key);

            if (posted == handed)
            {
                continue;
            }

            var queued = AdvicesInFlight(s).Any(a => a.Key == key);

            findings.Add(new AuditFinding(
                AuditCode.Reconciliation,
                posted < handed
                    ? "Terminal para verdiğini söylüyor, host o parayı hesaptan düşmemiş"
                    : "Host hesaptan düşmüş, terminal o parayı verdiğini söylemiyor",
                $"işlem {key}: host {posted}, terminal {handed} kuruş",
                Math.Abs(posted - handed),
                queued ? "bu işlemin dağıtım bildirimi hâlâ kuyrukta" : ""));
        }

        // The same question for the money going the other way.
        foreach (var key in hostCredited.Keys.Union(terminalStacked.Keys).OrderBy(k => k))
        {
            var credited = hostCredited.GetValueOrDefault(key);
            var stacked = terminalStacked.GetValueOrDefault(key);

            if (credited == stacked)
            {
                continue;
            }

            var queued = InFlight(s, key,
                TerminalEvent.DepositCommitQueued, TerminalEvent.DepositCommitAcknowledged);

            findings.Add(new AuditFinding(
                AuditCode.Reconciliation,
                credited < stacked
                    ? "Terminal parayı kasete aldığını söylüyor, host o parayı hesaba yazmamış"
                    : "Host hesaba yazmış, terminal o parayı kasete aldığını söylemiyor",
                $"işlem {key}: host {credited}, terminal {stacked} kuruş",
                Math.Abs(credited - stacked),
                queued ? "bu yatırmanın bildirimi hâlâ kuyrukta" : ""));
        }
    }

    // 6. A promise still standing at the end of the day is money the customer cannot
    // spend and the bank has not taken. Mid-day it is ordinary; at day end it is either
    // explained by a reversal still trying to get through, or it is a finding.
    private static void CheckOpenAuthorisations(AuditSnapshot s, List<AuditFinding> findings)
    {
        if (s.Moment != AuditMoment.EndOfDay)
        {
            return;
        }

        foreach (var key in s.OpenAuthorisations)
        {
            var reversalQueued = InFlight(s, key,
                TerminalEvent.ReversalQueued, TerminalEvent.ReversalAcknowledged);

            // How much THIS promise is holding, not how much every promise together is
            // holding. The total would be repeated once per line and read as that much
            // money per transaction - a report that overstates by a factor of its own
            // length is a report nobody trusts twice.
            var held = s.HostJournal
                .Where(e => e.Key.ToString() == key
                         && e.Type == MessageType.WithdrawalAuthRequest
                         && e.Rc == ResponseCode.Approved)
                .Sum(e => e.Amount);

            findings.Add(new AuditFinding(
                AuditCode.OpenAuthorisation,
                "Gün sonunda kapanmamış bir yetkilendirme var",
                $"işlem {key}, {held} kuruş bloke duruyor",
                held,
                reversalQueued ? "bu işlemin ters kaydı hâlâ kuyrukta" : ""));
        }
    }

    /// <summary>A host journal line that actually moved an account's balance.</summary>
    /// <remarks>
    /// Only the dispense advice moves the ledger (KARAR-029). An authorisation carries an
    /// amount and moves nothing; a reversal releases a hold and moves nothing. Counting
    /// any of those here would show debits that never happened.
    /// </remarks>
    private static bool IsLedgerMovement(JournalEntry entry) =>
        entry.Type == MessageType.DispenseAdvice
        && entry.Rc == ResponseCode.Approved
        && entry.Amount != 0;

    /// <summary>A host journal line that credited an account.</summary>
    /// <remarks>
    /// Only the deposit commit credits, and it credits only what it says was STACKED - the
    /// amount on the line is already that number (the protocol spec section 4.6). An agreed
    /// deposit carries an amount and credits nothing.
    /// </remarks>
    private static bool IsCredit(JournalEntry entry) =>
        entry.Type == MessageType.DepositCommitAdvice
        && entry.Rc == ResponseCode.Approved
        && entry.Amount != 0;

    /// <summary>How much the terminal's record says moved, for one kind of event.</summary>
    private static long SumOf(AuditSnapshot s, string @event) =>
        s.TerminalJournal.Where(e => e.Event == @event).Sum(e => e.Amount);

    /// <summary>
    /// The advices the terminal has sent and the host has not acknowledged. Read out of
    /// the terminal's own record rather than out of the live queue, so that the same
    /// answer comes back from a snapshot taken an hour ago.
    /// </summary>
    private static List<(string Key, long Amount)> AdvicesInFlight(AuditSnapshot s)
    {
        // Both kinds of report, because both close a gap between what the paper did and
        // what the ledger says. A dispense advice explains money the customer has and the
        // account has not lost; a deposit commit explains money in a drawer that the
        // account has not gained. The sign is opposite, which is why the deposit side is
        // subtracted here.
        var withdrawals = InFlightOf(s, TerminalEvent.AdviceQueued,
            TerminalEvent.AdviceAcknowledged, sign: 1);

        var deposits = InFlightOf(s, TerminalEvent.DepositCommitQueued,
            TerminalEvent.DepositCommitAcknowledged, sign: -1);

        return [.. withdrawals, .. deposits];
    }

    private static List<(string Key, long Amount)> InFlightOf(
        AuditSnapshot s, string queued, string acknowledged, int sign) =>
        s.TerminalJournal
            .Where(e => e.Event == queued)
            .Select(e => e.Key.ToString())
            .Distinct()
            .Where(key => InFlight(s, key, queued, acknowledged))
            .Select(key => (Key: key, Amount: sign * s.TerminalJournal
                .Where(e => e.Key.ToString() == key && e.Event == queued)
                .Sum(e => e.Amount)))
            .ToList();

    /// <summary>
    /// True when this transaction has more of one event than of the other - i.e. something
    /// was queued and never acknowledged.
    /// </summary>
    private static bool InFlight(AuditSnapshot s, string key, string queued, string acknowledged)
    {
        var lines = s.TerminalJournal.Where(e => e.Key.ToString() == key).ToList();

        return lines.Count(e => e.Event == queued) > lines.Count(e => e.Event == acknowledged);
    }
}
