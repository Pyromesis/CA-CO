using System.Text;

namespace CaCo.Application.Notes;

/// <summary>Tramo de texto con formato en línea.</summary>
/// <param name="Text">Texto literal.</param>
/// <param name="Bold">Negrita (<c>**texto**</c>).</param>
/// <param name="Italic">Cursiva (<c>*texto*</c>).</param>
/// <param name="Code">Código (<c>`texto`</c>, monoespaciado).</param>
public sealed record MdSpan(string Text, bool Bold = false, bool Italic = false, bool Code = false);

/// <summary>Tipo de bloque markdown.</summary>
public enum MdBlockKind
{
    /// <summary>Párrafo normal.</summary>
    Paragraph = 0,

    /// <summary>Encabezado (<c>#</c>, <c>##</c>, <c>###</c>).</summary>
    Header = 1,

    /// <summary>Elemento de lista (<c>- </c> o <c>* </c>).</summary>
    Bullet = 2,
}

/// <summary>Bloque markdown (Fase 4: subconjunto intencional).</summary>
/// <param name="Kind">Tipo de bloque.</param>
/// <param name="Level">Nivel de encabezado (1-3, 0 si no aplica).</param>
/// <param name="Spans">Contenido en línea.</param>
public sealed record MdBlock(MdBlockKind Kind, int Level, IReadOnlyList<MdSpan> Spans);

/// <summary>
/// Markdown ligero para notas y comentarios (Fase 4): encabezados, negrita,
/// cursiva, código en línea y listas simples. Sin anidado ni escapes:
/// los marcadores sin cerrar se muestran tal cual. Puro y testeable; la UI
/// lo dibuja con <c>RichTextBlock</c>.
/// </summary>
public static class MarkdownLite
{
    /// <summary>Máximo de bloques por documento (anti-DoS).</summary>
    public const int MaxBlocks = 500;

    /// <summary>Convierte texto en bloques.</summary>
    public static IReadOnlyList<MdBlock> Parse(string? text)
    {
        var blocks = new List<MdBlock>();
        if (string.IsNullOrEmpty(text))
        {
            return blocks;
        }

        foreach (var rawLine in text.Replace("\r\n", "\n").Split('\n'))
        {
            if (blocks.Count >= MaxBlocks)
            {
                break;
            }

            var line = rawLine.TrimEnd();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var trimmed = line.TrimStart();
            if (trimmed.StartsWith('#'))
            {
                var level = 0;
                while (level < trimmed.Length && trimmed[level] == '#')
                {
                    level++;
                }

                var rest = trimmed[level..].Trim();
                if (level is >= 1 and <= 3 && rest.Length > 0)
                {
                    blocks.Add(new MdBlock(MdBlockKind.Header, level, ParseInline(rest)));
                    continue;
                }
            }

            if ((trimmed.StartsWith("- ") || trimmed.StartsWith("* "))
                && trimmed.Length > 2)
            {
                blocks.Add(new MdBlock(MdBlockKind.Bullet, 0, ParseInline(trimmed[2..].TrimStart())));
                continue;
            }

            blocks.Add(new MdBlock(MdBlockKind.Paragraph, 0, ParseInline(trimmed)));
        }

        return blocks;
    }

    /// <summary>Trocea una línea en tramos con formato (sin anidado).</summary>
    public static IReadOnlyList<MdSpan> ParseInline(string text)
    {
        var spans = new List<MdSpan>();
        if (string.IsNullOrEmpty(text))
        {
            return spans;
        }

        var current = new StringBuilder();
        var i = 0;

        void Flush(bool bold = false, bool italic = false, bool code = false)
        {
            if (current.Length > 0)
            {
                spans.Add(new MdSpan(current.ToString(), bold, italic, code));
                current.Clear();
            }
        }

        while (i < text.Length)
        {
            // Código: `...` (tiene prioridad, no se interpreta dentro).
            if (text[i] == '`')
            {
                var end = text.IndexOf('`', i + 1);
                if (end > i + 1)
                {
                    Flush();
                    spans.Add(new MdSpan(text[(i + 1)..end], Code: true));
                    i = end + 1;
                    continue;
                }

                current.Append(text[i]);
                i++;
                continue;
            }

            // Negrita: **...** (el par es atómico: sin cierre sale tal cual).
            if (text[i] == '*' && i + 1 < text.Length && text[i + 1] == '*')
            {
                var end = text.IndexOf("**", i + 2, StringComparison.Ordinal);
                if (end > i + 2)
                {
                    Flush();
                    spans.Add(new MdSpan(text[(i + 2)..end], Bold: true));
                    i = end + 2;
                    continue;
                }

                current.Append("**");
                i += 2;
                continue;
            }

            // Cursiva: *...* (un asterisco simple a cada lado).
            if (text[i] == '*')
            {
                var end = text.IndexOf('*', i + 1);
                if (end > i + 1 && (end + 1 >= text.Length || text[end + 1] != '*'))
                {
                    Flush();
                    spans.Add(new MdSpan(text[(i + 1)..end], Italic: true));
                    i = end + 1;
                    continue;
                }

                current.Append(text[i]);
                i++;
                continue;
            }

            current.Append(text[i]);
            i++;
        }

        Flush();
        return spans;
    }
}
