using System.Text;

namespace CaCo.Application.Speech;

/// <summary>Voz disponible para lectura (datos planos, sin WinRT).</summary>
/// <param name="Id">Identificador de la voz.</param>
/// <param name="Language">Etiqueta de idioma (p. ej. "es-ES").</param>
/// <param name="DisplayName">Nombre visible.</param>
public sealed record VoiceChoice(string Id, string Language, string DisplayName);

/// <summary>Lógica pura de lectura en voz alta (Fase 7, testeable sin motor).</summary>
public static class SpeechChunking
{
    /// <summary>Tamaño máximo por fragmento para el sintetizador.</summary>
    public const int MaxChunkChars = 4000;

    /// <summary>Elige voz: español de España, luego cualquier español, luego la primera.</summary>
    public static VoiceChoice? PickSpanishVoice(IEnumerable<VoiceChoice> voices)
    {
        ArgumentNullException.ThrowIfNull(voices);
        var list = voices.Where(v => !string.IsNullOrWhiteSpace(v.Id)).ToList();
        return list.FirstOrDefault(v => v.Language.StartsWith("es-ES", StringComparison.OrdinalIgnoreCase))
            ?? list.FirstOrDefault(v => v.Language.StartsWith("es", StringComparison.OrdinalIgnoreCase))
            ?? list.FirstOrDefault();
    }

    /// <summary>Trocea el texto en fragmentos por frases (sin partir palabras si cabe).</summary>
    public static IReadOnlyList<string> Split(string? text, int maxChars = MaxChunkChars)
    {
        var chunks = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return chunks;
        }

        maxChars = Math.Max(500, maxChars);
        var sentences = SplitSentences(text);
        var current = new StringBuilder();
        foreach (var sentence in sentences)
        {
            var piece = sentence.Trim();
            if (piece.Length == 0)
            {
                continue;
            }

            if (current.Length > 0 && current.Length + 1 + piece.Length > maxChars)
            {
                chunks.Add(current.ToString());
                current.Clear();
            }

            if (piece.Length > maxChars)
            {
                if (current.Length > 0)
                {
                    chunks.Add(current.ToString());
                    current.Clear();
                }

                // Frase gigante: corte duro por palabras y luego por caracteres.
                foreach (var hard in HardSplit(piece, maxChars))
                {
                    chunks.Add(hard);
                }

                continue;
            }

            if (current.Length > 0)
            {
                current.Append(' ');
            }

            current.Append(piece);
        }

        if (current.Length > 0)
        {
            chunks.Add(current.ToString());
        }

        return chunks;
    }

    private static List<string> SplitSentences(string text)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        foreach (var ch in text)
        {
            current.Append(ch);
            if (ch is '.' or '!' or '?' or '\n')
            {
                parts.Add(current.ToString());
                current.Clear();
            }
        }

        if (current.Length > 0)
        {
            parts.Add(current.ToString());
        }

        return parts;
    }

    private static IEnumerable<string> HardSplit(string text, int maxChars)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var current = new StringBuilder();
        foreach (var word in words)
        {
            if (current.Length > 0 && current.Length + 1 + word.Length > maxChars)
            {
                yield return current.ToString();
                current.Clear();
            }

            if (word.Length > maxChars)
            {
                if (current.Length > 0)
                {
                    yield return current.ToString();
                    current.Clear();
                }

                for (var i = 0; i < word.Length; i += maxChars)
                {
                    yield return word.Substring(i, Math.Min(maxChars, word.Length - i));
                }

                continue;
            }

            if (current.Length > 0)
            {
                current.Append(' ');
            }

            current.Append(word);
        }

        if (current.Length > 0)
        {
            yield return current.ToString();
        }
    }
}
