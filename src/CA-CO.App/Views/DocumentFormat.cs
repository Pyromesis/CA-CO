using CaCo.Domain;

namespace CaCo.App.Views;

/// <summary>
/// Formato de presentación de documentos (tamaños, tipos). Un solo lugar para
/// no duplicar lógica entre filas, tarjetas y ViewModels.
/// </summary>
internal static class DocumentFormat
{
    /// <summary>Subtítulo: tipo • tamaño • fecha de importación.</summary>
    public static string Subtitle(Document? document) => document is null
        ? string.Empty
        : $"{TypeName(document.FileType)}  •  {Size(document.SizeBytes)}  •  {document.ImportedAt.LocalDateTime:dd/MM/yyyy}";

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
