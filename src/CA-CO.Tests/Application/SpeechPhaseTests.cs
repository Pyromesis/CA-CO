using CaCo.Application.Speech;

namespace CaCo.Tests.Application;

/// <summary>Pruebas Fase 7: troceado y elección de voz (sin motor).</summary>
public sealed class SpeechPhaseTests
{
    [Fact]
    public void PickSpanishVoice_PrefersSpainThenSpanishThenFirst()
    {
        var voices = new[]
        {
            new VoiceChoice("en", "en-US", "English"),
            new VoiceChoice("mx", "es-MX", "Español (México)"),
            new VoiceChoice("es", "es-ES", "Español (España)"),
        };

        Assert.Equal("es", PickSpanishVoice(voices)!.Id);
        Assert.Equal("mx", PickSpanishVoice(voices[..2])!.Id);
        Assert.Equal("en", PickSpanishVoice(voices[..1])!.Id);
        Assert.Null(PickSpanishVoice([]));
        Assert.Null(PickSpanishVoice([new VoiceChoice("", "es-ES", "X")]));
    }

    private static VoiceChoice? PickSpanishVoice(VoiceChoice[] voices) =>
        SpeechChunking.PickSpanishVoice(voices);

    [Fact]
    public void Split_SentencesAndCaps()
    {
        Assert.Empty(SpeechChunking.Split(null));
        Assert.Empty(SpeechChunking.Split("   "));

        var single = SpeechChunking.Split("Hola mundo. Adiós mundo! Otra vez?");
        Assert.Single(single);
        Assert.Equal("Hola mundo. Adiós mundo! Otra vez?", single[0]);

        var s1 = new string('a', 300) + ".";
        var s2 = new string('b', 300) + "!";
        var s3 = new string('c', 10) + "?";
        var chunks = SpeechChunking.Split($"{s1} {s2} {s3}", 500);
        Assert.Equal(2, chunks.Count);
        Assert.StartsWith("a", chunks[0]);
        Assert.EndsWith("?", chunks[1]);

        var big = string.Join(" ", Enumerable.Repeat("palabra", 2000));
        var capped = SpeechChunking.Split(big, 1000);
        Assert.True(capped.Count > 1);
        Assert.All(capped, c => Assert.True(c.Length <= 1000));
        Assert.Equal(
            big.Replace("  ", " ").Trim(),
            string.Join(" ", capped).Trim());
    }

    [Fact]
    public void Split_LongWord_HardCuts()
    {
        var word = new string('a', 2500);
        var chunks = SpeechChunking.Split(word, 1000);
        Assert.Equal(3, chunks.Count);
        Assert.All(chunks, c => Assert.True(c.Length <= 1000));
        Assert.Equal(word, string.Concat(chunks));
    }
}
