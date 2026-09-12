using CaCo.Application.Storage;
using CaCo.Domain;

namespace CaCo.App.Views;

/// <summary>
/// Formato de presentación de documentos (tamaños, tipos). Un solo lugar para
/// no duplicar lógica entre filas, tarjetas y ViewModels.
/// </summary>
internal static class DocumentFormat
{
    /// <summary>Subtítulo: tipo • tamaño • fecha (+ páginas o dimensiones si se conocen).</summary>
    public static string Subtitle(Document? document)
    {
        if (document is null)
        {
            return string.Empty;
        }

        var text = $"{TypeName(document.FileType)}  •  {Size(document.SizeBytes)}  •  {document.ImportedAt.LocalDateTime:dd/MM/yyyy}";
        var extra = MediaDetails(document);
        return string.IsNullOrEmpty(extra) ? text : $"{text}  •  {extra}";
    }

    /// <summary>Detalle multimedia: "N pág." o "WxH" según metadatos (vacío si no hay).</summary>
    public static string MediaDetails(Document? document)
    {
        if (document is null)
        {
            return string.Empty;
        }

        if (document.FileType == DocumentType.Pdf
            && document.Metadata.Get(MediaMetadataKeys.PdfPageCount) is string pages
            && int.TryParse(pages, out var count) && count > 0)
        {
            return count == 1 ? "1 pág." : $"{count} págs.";
        }

        if (document.FileType is DocumentType.Png or DocumentType.Jpg or DocumentType.Jpeg
            && document.Metadata.Get(MediaMetadataKeys.ImageWidth) is string w
            && document.Metadata.Get(MediaMetadataKeys.ImageHeight) is string h
            && int.TryParse(w, out var width) && int.TryParse(h, out var height)
            && width > 0 && height > 0)
        {
            return $"{width}×{height}";
        }

        return string.Empty;
    }

    /// <summary>Tamaño legible (B/KB/MB/GB).</summary>
    public static string Size(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.#} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):0.##} GB",
    };

    /// <summary>Nombre visible del tipo.</summary>
    public static string TypeName(DocumentType type) => type switch
    {
        DocumentType.Pdf => "PDF",
        DocumentType.Png => "PNG",
        DocumentType.Jpg => "JPG",
        DocumentType.Jpeg => "JPEG",
        DocumentType.Docx => "Word",
        DocumentType.Xlsx => "Excel",
        DocumentType.Txt => "Texto",
        _ => "Archivo",
    };

    /// <summary>Glifo Segoe MDL2 según tipo.</summary>
    public static string Glyph(DocumentType type) => type switch
    {
        DocumentType.Png or DocumentType.Jpg or DocumentType.Jpeg => "\uEB9F",
        DocumentType.Xlsx => "\uE9B9",
        _ => "\uE8A5",
    };
}
