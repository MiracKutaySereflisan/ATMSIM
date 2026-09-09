// Copyright (c) 2026 Mirac Kutay Sereflisan. MIT lisansi - LICENSE dosyasina bakiniz.
// DemoFaults.cs
//
// What this file does: it remembers which faults the person running a demonstration has
// switched on. Nothing else. It does not cause a fault, it does not know what a socket is,
// and it makes no decision about a transaction.
//
// Why it exists: the demo needs a mode in which the operator can
// say "now I am cutting the line" and cut it. Without that, a live demo can only show the
// happy path - and a simulator that only shows the happy path is a demo, not a finding.
//
// Why it is a separate object rather than a few booleans in the flow: the flow decides what
// happens to money. The faults are an operating condition applied at the EDGE - the line,
// the dispenser - exactly where the real thing would break. Mixing the two would put "am I
// in a demonstration" inside the withdrawal logic, which is the one place it must never be.
//
// The division of labour, and it is the whole point:
//
//   the browser  says only "the operator pressed this button"   (no logic)
//   this object  remembers which switches are on                (no effect)
//   Program.cs   applies them to the real line and the real hands (the effect)
//   TerminalFlow shows them on the screen                       (so nobody forgets)
//
// The last line matters more than it looks. A demonstration machine with a fault silently
// switched on is a machine that will one day be shown to somebody as if it were working
// normally. Every screen carries the list.

namespace Atm.Terminal;

/// <summary>Which faults the operator has switched on for a demonstration.</summary>
public sealed class DemoFaults
{
    /// <summary>The commands the screen may send. Anything else is refused loudly.</summary>
    public static readonly IReadOnlyList<string> Commands =
        ["hat-kes", "hat-gelsin", "cevap-yut", "cevap-birak", "eksik-ver", "sikisma", "makine-duzelsin"];

    /// <summary>Nothing reaches the host at all.</summary>
    public bool LineIsDown { get; private set; }

    /// <summary>
    /// The request reaches the host and does its work; the answer is thrown away.
    /// </summary>
    /// <remarks>
    /// The dangerous one, and the one worth showing to a user: this is instruction
    /// section 4.1 in the flesh. The machine cannot tell it apart from a line that was
    /// never there, and the money may already be held.
    /// </remarks>
    public bool AnswersAreSwallowed { get; private set; }

    /// <summary>The dispenser hands over at most this much, in kurus. Zero means it jams.</summary>
    public long? PresentAtMost { get; private set; }

    /// <summary>True when anything at all is switched on.</summary>
    public bool Any => LineIsDown || AnswersAreSwallowed || PresentAtMost is not null;

    /// <summary>What is on, in words, for the screen to show.</summary>
    public string Summary
    {
        get
        {
            var on = new List<string>();

            if (LineIsDown)
            {
                on.Add("hat kesik");
            }

            if (AnswersAreSwallowed)
            {
                on.Add("cevaplar yutuluyor");
            }

            if (PresentAtMost == 0)
            {
                on.Add("dağıtıcı sıkışık");
            }
            else if (PresentAtMost is long limit)
            {
                on.Add($"en fazla {limit / 100} TL veriliyor");
            }

            return string.Join(" · ", on);
        }
    }

    /// <summary>Flips one switch. Refuses a command it does not know.</summary>
    /// <remarks>
    /// The refusal is deliberate and matches the scenario reader (KARAR-049): a mistyped
    /// command that was quietly ignored would leave the presenter saying "now I am cutting
    /// the line" to a user watching a line that is still up.
    /// </remarks>
    public void Apply(string command)
    {
        switch (command)
        {
            case "hat-kes":
                LineIsDown = true;
                break;

            case "hat-gelsin":
                LineIsDown = false;
                AnswersAreSwallowed = false;
                break;

            case "cevap-yut":
                AnswersAreSwallowed = true;
                break;

            case "cevap-birak":
                AnswersAreSwallowed = false;
                break;

            case "eksik-ver":
                PresentAtMost = 20_000;
                break;

            case "sikisma":
                PresentAtMost = 0;
                break;

            case "makine-duzelsin":
                PresentAtMost = null;
                break;

            default:
                throw new ArgumentException(
                    $"Bilinmeyen demo komutu: '{command}'. Geçerli olanlar: " +
                    string.Join(", ", Commands), nameof(command));
        }
    }
}
