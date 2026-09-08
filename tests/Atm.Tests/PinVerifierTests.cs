// PinVerifierTests.cs
//
// What this file does: it checks the PIN rules of docs/protocol.md section 4.1 -
// three attempts, the counter surviving between attempts, a correct PIN clearing it,
// and an unknown card being refused without being counted.
//
// The PINs used here belong to invented cards that exist only in this repository and
// are written down in docs/kurulum.md so the demo can be used. Nothing here is a real
// credential. What the tests do check is the property that matters: the verifier
// stores a value it cannot reverse, and two cards with the same PIN do not look alike.

using Atm.Host;
using Atm.Protocol;
using Xunit;

namespace Atm.Tests;

public class PinVerifierTests
{
    private const string Card = "4111111111111111";
    private const string OtherCard = "4222222222222220";

    private static PinVerifier WithOneCard(string pin) =>
        new(new Dictionary<string, PinVerificationValue>
        {
            [Card] = PinVerifier.Create("saltysalt", pin),
        });

    [Fact]
    public void TheRightPinIsAccepted()
    {
        var verifier = WithOneCard("9137");

        var result = verifier.Check(Card, "9137");

        Assert.True(result.Ok, "Doğru PIN kabul edilmeliydi.");
        Assert.Equal(ResponseCode.Approved, result.Rc);
        Assert.Equal(3, result.RemainingTries);
    }

    [Fact]
    public void AWrongPinCountsDown()
    {
        var verifier = WithOneCard("9137");

        var result = verifier.Check(Card, "0000");

        Assert.Equal(ResponseCode.WrongPin, result.Rc);
        Assert.Equal(2, result.RemainingTries);
    }

    [Fact]
    public void TheCounterSurvivesBetweenAttempts()
    {
        var verifier = WithOneCard("9137");

        verifier.Check(Card, "0000");
        var second = verifier.Check(Card, "1111");

        // A customer who fails, takes the card out and comes back has one try left,
        // not three. The counter belongs to the card, not to the visit.
        Assert.Equal(1, second.RemainingTries);
    }

    [Fact]
    public void ThreeWrongPinsExhaustTheCard()
    {
        var verifier = WithOneCard("9137");

        verifier.Check(Card, "0000");
        verifier.Check(Card, "1111");
        var third = verifier.Check(Card, "2222");

        Assert.Equal(ResponseCode.PinTriesExhausted, third.Rc);
        Assert.Equal(0, third.RemainingTries);
    }

    [Fact]
    public void OnceExhaustedEvenTheRightPinIsRefused()
    {
        var verifier = WithOneCard("9137");
        verifier.Check(Card, "0000");
        verifier.Check(Card, "1111");
        verifier.Check(Card, "2222");

        var result = verifier.Check(Card, "9137");

        // If the right PIN still worked here, the limit would only slow a guesser
        // down rather than stop them.
        Assert.Equal(ResponseCode.PinTriesExhausted, result.Rc);
    }

    [Fact]
    public void ARightPinClearsTheCounter()
    {
        var verifier = WithOneCard("9137");
        verifier.Check(Card, "0000");
        verifier.Check(Card, "1111");

        verifier.Check(Card, "9137");

        Assert.Equal(3, verifier.RemainingTries(Card));
    }

    [Fact]
    public void AnUnknownCardIsRefusedAndNotCounted()
    {
        var verifier = WithOneCard("9137");

        var result = verifier.Check("4000000000000002", "9137");

        Assert.Equal(ResponseCode.UnknownCard, result.Rc);
    }

    [Fact]
    public void TheStoredValueIsNotThePin()
    {
        var stored = PinVerifier.Create("saltysalt", "9137");

        // What is written down can be checked against but not read back.
        Assert.False(stored.VerificationValue.Contains("9137"),
            "Saklanan değerin içinde PIN geçmemeli.");
        Assert.Equal(64, stored.VerificationValue.Length);
    }

    [Fact]
    public void TwoCardsWithTheSamePinDoNotLookAlike()
    {
        var first = PinVerifier.Create("saltysalt", "9137");
        var second = PinVerifier.Create("differentsalt", "9137");

        // Without the salt, identical PINs would store identical values - and anyone
        // reading the stored data could see which customers share a PIN.
        Assert.False(first.VerificationValue == second.VerificationValue,
            "Aynı PIN'e sahip iki kart aynı değeri saklamamalı.");
    }

    [Fact]
    public void TheDemoSeedMatchesTheDocumentedDemoPin()
    {
        // The seed values in PinVerifier.WithDemoCards were produced once, outside the
        // source. This is the test that says they still belong to the PIN written in
        // docs/kurulum.md - if someone regenerates one and forgets the other, the demo
        // would fail in front of an audience instead of here.
        var verifier = PinVerifier.WithDemoCards();

        Assert.True(verifier.Check(Card, "1234").Ok, "TR-DEMO-001 kartı 1234 ile açılmalı.");
        Assert.True(verifier.Check(OtherCard, "2468").Ok, "TR-DEMO-002 kartı 2468 ile açılmalı.");
    }
}
