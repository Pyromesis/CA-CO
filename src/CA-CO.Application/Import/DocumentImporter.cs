using CaCo.Core;
using CaCo.Domain;

namespace CaCo.Application.Import;

/// <summary>Solicitud de importación de un archivo a la biblioteca.</summary>
/// <param name="SourcePath">Ruta del archivo a importar.</param>
/// <param name="NotebookId">Cuaderno destino (opcional).</param>
/// <param name="PreferredName">Nombre visible preferido (por defecto, el del archivo sin extensión).</param>
public sealed record ImportRequest(string SourcePath, Guid? NotebookId = null, string? PreferredName = null);

/// <summary>Resultado de una importación individual.</summary>
public sealed record ImportResult
{
    /// <summary><c>true</c> si el documento quedó en la biblioteca.</summary>
    public required bool Succeeded { get; init; }

    /// <summary>Documento creado (si <see cref="Succeeded"/>).</summary>
    public Document? Document { get; init; }

    /// <summary>Archivo de origen.</summary>
    public required string SourcePath { get; init; }

    /// <summary>Error si falló.</summary>
    public Error? Error { get; init; }

    /// <summary>Indica si se omitió por estar duplicado (Fase 1: hash; Fase 0: mismo nombre+tamaño).</summary>
    public bool SkippedAsDuplicate { get; init; }
}

/// <summary>Validación de un archivo candidato a importar.</summary>
public sealed record FileValidationResult
{
    /// <summary><c>true</c> si puede importarse.</summary>
    public required bool IsValid { get; init; }

    /// <summary>Tipo detectado.</summary>
    public DocumentType DetectedType { get; init; }

    /// <summary>Motivo si no es válido.</summary>
    public string? Reason { get; init; }
}

/// <summary>Valida archivos antes de importar (existencia, extensión soportada, tamaño...).</summary>
public interface IFileTypeValidator
{
    /// <summary>Valida un archivo candidato.</summary>
    FileValidationResult Validate(string path);
}

/// <summary>
/// Punto de entrada para incorporar archivos a la biblioteca.
/// Fase 0: archivo individual y múltiples. Fases futuras: carpetas, drag &amp; drop,
/// cámara (misma interfaz, nuevos métodos).
/// Flujo: validar → copiar original → copiar administrada → crear entidad → persistir.
/// </summary>
public interface IDocumentImporter
{
    /// <summary>Importa un archivo.</summary>
    Task<Result<ImportResult>> ImportAsync(ImportRequest request, CancellationToken ct);

    /// <summary>Importa varios archivos, uno por uno, sin abortar ante fallos individuales.</summary>
    IAsyncEnumerable<ImportResult> ImportManyAsync(
        IEnumerable<ImportRequest> requests,
        CancellationToken ct);
}
