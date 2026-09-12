using System.Text;

namespace CaCo.Application.Ocr;

/// <summary>Opciones del OCR por páginas (Fase 5).</summary>
public sealed record PdfOcrOptions
{
    /// <summary>Máximo de páginas a procesar por documento.</summary>
    public int MaxPages { get; init; } = 20;

    /// <summary>Máximo de caracteres totales del texto unido.</summary>
    public int MaxChars { get; init; } = 100_000;

    /// <summary>Opciones por defecto.</summary>
    public static PdfOcrOptions Default => new();
}

/// <summary>Progreso del OCR de un PDF (para "página 3/12").</summary>
/// <param name="PagesDone">Páginas ya procesadas.</param>
/// <param name="TotalPages">Páginas a procesar (ya acotadas).</param>
public sealed record OcrProgress(int PagesDone, int TotalPages);

/// <summary>Texto extraído de un PDF.</summary>
/// <param name="Text">Texto unido de las páginas con texto.</param>
/// <param name="PagesProcessed">Páginas procesadas.</param>
/// <param name="TotalPages">Páginas totales del PDF.</param>
/// <param name="Truncated">Si se cortó por <see cref="PdfOcrOptions"/>.</param>
public sealed record PdfOcrResult(string Text, int PagesProcessed, int TotalPages, bool Truncated);

/// <summary>Claves de metadatos del resultado OCR (para Fase 6: búsqueda).</summary>
public static class OcrMetadataKeys
{
    /// <summary>"true" si el documento tiene OCR extraído.</summary>
    public const string Done = "ocr.done";

    /// <summary>Número de páginas procesadas.</summary>
    public const string Pages = "ocr.pages";

    /// <summary>Caracteres extraídos.</summary>
    public const string Chars = "ocr.chars";

    /// <summary>Extracto inicial (máx. 1000 caracteres).</summary>
    public const string Excerpt = "ocr.excerpt";

    /// <summary>Longitud máxima del extracto.</summary>
    public const int MaxExcerptLength = 1000;
}

/// <summary>Lógica pura del OCR multipágina (testeable sin WinRT).</summary>
public static class OcrPages
{
    /// <summary>Páginas a procesar (1-based), acotadas por <paramref name="options"/>.</summary>
    public static IReadOnlyList<int> TakePages(int totalPages, PdfOcrOptions? options = null)
    {
        var max = Math.Max(1, (options ?? PdfOcrOptions.Default).MaxPages);
        return Enumerable.Range(1, Math.Max(0, totalPages)).Take(max).ToList();
    }

    /// <summary>Une textos de páginas con separador y tope de caracteres.</summary>
    /// <param name="pageTexts">Texto por página (null/vacío = página sin texto).</param>
    /// <remarks>Separador determinista <c>"\n\n"</c>; el resultado nunca supera el tope.</remarks>
    public static (string Text, bool Truncated) Combine(IEnumerable<string?> pageTexts, PdfOcrOptions? options = null)
    {
        var max = Math.Max(1, (options ?? PdfOcrOptions.Default).MaxChars);
        var sb = new StringBuilder();
        var truncated = false;
        foreach (var page in pageTexts)
        {
            if (string.IsNullOrWhiteSpace(page))
            {
                continue;
            }

            var chunk = (sb.Length > 0 ? "\n\n" : string.Empty) + page.Trim();
            var room = max - sb.Length;
            if (room <= 0)
            {
                truncated = true;
                break;
            }

            if (chunk.Length > room)
            {
                sb.Append(chunk, 0, room);
                truncated = true;
                break;
            }

            sb.Append(chunk);
        }

        return (sb.ToString(), truncated);
    }
}
