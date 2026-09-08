// Copyright (c) 2026 Mirac Kutay Sereflisan. MIT lisansi - LICENSE dosyasina bakiniz.
// ScreenContractTests.cs
//
// What these tests check: the boundary between the terminal and the browser. Two
// claims are being defended here, and both are claims about where decisions live.
//
//   1. Anything the screen says that is not in the vocabulary is refused, not guessed.
//   2. A PIN never appears in a picture sent to the screen - only a count of asterisks.
//
// The second is the one worth being strict about. Instruction section 2 forbids a PIN
// from being stored, logged or carried anywhere it is not needed, and the screen is
// the place where a careless line of code would put it, because "just show the digits"
// looks harmless while it is being written.

using Atm.Terminal;
using Xunit;

namespace Atm.Tests;

public class ScreenContractTests
{
    [Fact]
    public void APictureTravelsAsJsonWithTheNamesTheBrowserExpects()
    {
        var view = new ScreenView
        {
            Screen = "pin",
            Title = "PIN GİRİNİZ",
            Lines = new[] { "Dört haneli şifrenizi giriniz." },
            MaskedLength = 3,
            CountdownSeconds = 22,
        };

        var json = ScreenCodec.ToJson(view);

        Assert.True(json.Contains("\"screen\":\"pin\""), "screen alanı yok: " + json);
        Assert.True(json.Contains("\"maskedLength\":3"), "maskedLength alanı yok: " + json);
        Assert.True(json.Contains("\"countdownSeconds\":22"), "countdownSeconds alanı yok: " + json);
    }

    [Fact]
    public void AKeyPressIsReadAsAKeyPress()
    {
        var read = ScreenCodec.ReadEvent("{\"event\":\"key\",\"value\":\"7\"}");

        Assert.Equal(ScreenEventName.Key, read.Event);
        Assert.Equal("7", read.Value);
    }

    [Fact]
    public void EverySlotEventInTheVocabularyIsAccepted()
    {
        foreach (var (name, value) in new[]
        {
            (ScreenEventName.Hello, ""),
            (ScreenEventName.Soft, "L2"),
            (ScreenEventName.Card, "inserted"),
            (ScreenEventName.Cash, "taken"),
            (ScreenEventName.Receipt, "taken"),
        })
        {
            var read = ScreenCodec.ReadEvent($"{{\"event\":\"{name}\",\"value\":\"{value}\"}}");
            Assert.Equal(name, read.Event);
        }
    }

    [Fact]
    public void AnEventOutsideTheVocabularyIsRefusedLoudly()
    {
        // "withdraw" is exactly the message this contract exists to make impossible:
        // an intention, decided in the browser. Ignoring it quietly would leave the
        // door closed today and open the day somebody adds a handler for it.
        Assert.Throws<InvalidOperationException>(() =>
            ScreenCodec.ReadEvent("{\"event\":\"withdraw\",\"value\":\"20000\"}"));
    }

    [Fact]
    public void RubbishFromTheScreenIsRefusedRatherThanGuessedAt()
    {
        Assert.Throws<InvalidOperationException>(() => ScreenCodec.ReadEvent("bu json değil"));
    }

    // WHAT THIS TEST DOES NOT PROVE, said out loud because the last time this project
    // wrote a test with this name it was weak and a sabotage walked past it.
    //
    // This test builds the picture itself, so all it can show is that the codec does
    // not invent a field and leak something into it. It cannot show that the terminal's
    // flow will not one day put a PIN into Title or Lines, because in Phase 1g there is
    // no flow yet. The test that walks the real PIN-entry path belongs to Phase 1h and
    // is written down as a known gap in reports/assumptions.md. Until then this is a guard on the codec,
    // not on the terminal.
    [Fact]
    public void ThePinNeverReachesTheScreen()
    {
        // Four digits are entered. The picture the terminal would send carries the
        // COUNT and nothing else. This test scans the whole JSON text, so it fails
        // whichever field a PIN were put into - including one added later.
        const string pin = "4913";
        var view = new ScreenView
        {
            Screen = "pin",
            Title = "PIN GİRİNİZ",
            Lines = new[] { "Şifrenizi giriniz ve GİRİŞ tuşuna basınız." },
            MaskedLength = pin.Length,
        };

        var json = ScreenCodec.ToJson(view);

        Assert.True(json.Contains("\"maskedLength\":4"), "Yıldız sayısı gitmemiş: " + json);
        Assert.False(json.Contains(pin), "PIN ekrana giden mesajda görünüyor: " + json);

        foreach (var digit in pin)
        {
            Assert.False(json.Contains($"\"{digit}\""), $"PIN hanesi '{digit}' mesajda görünüyor.");
        }
    }

    [Fact]
    public void APictureWithNoOptionsLeavesAllEightKeysBlank()
    {
        var view = new ScreenView { Screen = "wait", Title = "LÜTFEN BEKLEYİNİZ" };
        var json = ScreenCodec.ToJson(view);

        Assert.True(json.Contains("\"softLeft\":[null,null,null,null]"), "Sol tuşlar boş değil: " + json);
        Assert.True(json.Contains("\"softRight\":[null,null,null,null]"), "Sağ tuşlar boş değil: " + json);
    }
}
