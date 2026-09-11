namespace CaCo.Domain;

/// <summary>
/// Tipos de documento soportados por CA-CO en Fase 0.
/// Ampliar este enum en fases futuras (p. ej. Markdown, EPUB) no rompe la arquitectura:
/// todo el código discrimina por <see cref="DocumentType"/> y no por cadenas.
/// </summary>
public enum DocumentType
{
    /// <summary>Tipo desconocido o aún no clasificado.</summary>
    Unknown = 0,

    /// <summary>Documento PDF (.pdf).</summary>
    Pdf = 1,

    /// <summary>Imagen PNG (.png).</summary>
    Png = 2,

    /// <summary>Imagen JPEG (.jpg).</summary>
    Jpg = 3,

    /// <summary>Imagen JPEG (.jpeg).</summary>
    Jpeg = 4,

    /// <summary>Documento Word (.docx).</summary>
    Docx = 5,

    /// <summary>Hoja de cálculo Excel (.xlsx).</summary>
    Xlsx = 6,

    /// <summary>Texto plano (.txt).</summary>
    Txt = 7,
}

/// <summary>
/// Conocimiento de dominio sobre qué extensiones acepta CA-CO.
/// Punto único de verdad usado por importadores, UI (pickers, drag &amp; drop) y validaciones.
/// </summary>
public static class SupportedFileTypes
{
    private static readonly Dictionary<string, DocumentType> ByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = DocumentType.Pdf,
        [".png"] = DocumentType.Png,
        [".jpg"] = DocumentType.Jpg,
        [".jpeg"] = DocumentType.Jpeg,
        [".docx"] = DocumentType.Docx,
        [".xlsx"] = DocumentType.Xlsx,
        [".txt"] = DocumentType.Txt,
    };

    /// <summary>Todas las extensiones soportadas, con punto y en minúsculas.</summary>
    public static IReadOnlyCollection<string> AllExtensions => ByExtension.Keys;

    /// <summary>Filtro para <c>FileOpenPicker</c>: extensión → descripción.</summary>
    public static IReadOnlyCollection<string> FilePickerFilter => ByExtension.Keys;

    /// <summary>Indica si la extensión (con o sin punto) está soportada.</summary>
    public static bool IsSupported(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return false;
        }

        return ByExtension.ContainsKey(Normalize(extension));
    }

    /// <summary>Obtiene el <see cref="DocumentType"/> de una extensión o nombre de archivo.</summary>
    public static bool TryGetType(string? extensionOrFileName, out DocumentType type)
    {
        type = DocumentType.Unknown;
        if (string.IsNullOrWhiteSpace(extensionOrFileName))
        {
            return false;
        }

        var ext = Normalize(Path.GetExtension(extensionOrFileName));
        if (string.IsNullOrEmpty(ext))
        {
            ext = Normalize(extensionOrFileName);
        }

        return ByExtension.TryGetValue(ext, out type);
    }

    /// <summary>Obtiene el tipo o <see cref="DocumentType.Unknown"/> si no está soportado.</summary>
    public static DocumentType GetTypeOrUnknown(string? extensionOrFileName) =>
        TryGetType(extensionOrFileName, out var type) ? type : DocumentType.Unknown;

    private static string Normalize(string extension)
    {
        var ext = extension.Trim().ToLowerInvariant();
        return ext.StartsWith('.') ? ext : "." + ext;
    }
}
