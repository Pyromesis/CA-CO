namespace CaCo.Domain;

/// <summary>
/// Metadatos extensibles de un documento (pares clave/valor).
/// Es el punto de extensión natural para OCR (Fase 5), IA (Fase 11) o nube (Fase 10):
/// esos módulos guardarán aquí sus claves (<c>ocr.pageCount</c>, <c>ai.summary</c>...)
/// sin cambiar el esquema de persistencia.
/// </summary>
public sealed class DocumentMetadata
{
    /// <summary>Valores almacenados.</summary>
    public Dictionary<string, string> Values { get; private set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Obtiene un valor o <c>null</c> si no existe.</summary>
    public string? Get(string key) =>
        key is not null && Values.TryGetValue(key, out var value) ? value : null;

    /// <summary>Máximo de claves para evitar DoS por metadatos.</summary>
    public const int MaxKeys = 50;

    /// <summary>Longitud máxima por clave / valor.</summary>
    public const int MaxKeyLength = 64;

    /// <summary>Longitud máxima por valor.</summary>
    public const int MaxValueLength = 4096;

    /// <summary>Establece un valor (añade o reemplaza).</summary>
    public void Set(string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);
        var trimmed = key.Trim();
        if (trimmed.Length > MaxKeyLength)
        {
            throw new ArgumentException($"La clave no puede superar {MaxKeyLength} caracteres.", nameof(key));
        }

        if (value.Length > MaxValueLength)
        {
            throw new ArgumentException($"El valor no puede superar {MaxValueLength} caracteres.", nameof(value));
        }

        if (!Values.ContainsKey(trimmed) && Values.Count >= MaxKeys)
        {
            throw new InvalidOperationException($"No se admiten más de {MaxKeys} metadatos.");
        }

        Values[trimmed] = value;
    }

    /// <summary>Elimina una clave.</summary>
    public bool Remove(string key) => key is not null && Values.Remove(key);
}
