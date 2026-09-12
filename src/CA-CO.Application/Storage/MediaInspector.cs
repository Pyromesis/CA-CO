using CaCo.Core;
using CaCo.Domain;

namespace CaCo.Application.Storage;

/// <summary>Datos multimedia extraídos de la copia administrada (Fase 3).</summary>
/// <param name="PageCount">Páginas del PDF (null si no aplica).</param>
/// <param name="Width">Ancho de imagen en píxeles (null si no aplica).</param>
/// <param name="Height">Alto de imagen en píxeles (null si no aplica).</param>
public sealed record MediaInfo(int? PageCount, int? Width, int? Height);

/// <summary>Claves de <see cref="DocumentMetadata"/> para datos multimedia.</summary>
public static class MediaMetadataKeys
{
    /// <summary>Número de páginas del PDF.</summary>
    public const string PdfPageCount = "pdf.pageCount";

    /// <summary>Ancho de imagen en píxeles.</summary>
    public const string ImageWidth = "image.width";

    /// <summary>Alto de imagen en píxeles.</summary>
    public const string ImageHeight = "image.height";
}

/// <summary>
/// Inspecciona la copia administrada para extraer metadatos (páginas PDF,
/// dimensiones de imagen). Best-effort: el importador nunca falla por esto.
/// </summary>
public interface IMediaInspector
{
    /// <summary>Inspecciona el archivo (<c>null</c> si el tipo no aporta nada).</summary>
    Task<Result<MediaInfo?>> InspectAsync(string absolutePath, DocumentType type, CancellationToken ct);
}
