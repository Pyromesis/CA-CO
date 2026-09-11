using CaCo.Domain;

namespace CaCo.Application.Storage;

/// <summary>
/// Miniaturas de documentos (Fase 1: imágenes; otros tipos usan icono).
/// La implementación vive en la capa UI (APIs de imagen de Windows);
/// el importador la invoca tras copiar para no bloquear la importación si falla.
/// </summary>
public interface IThumbnailService
{
    /// <summary>Genera la miniatura si aplica (idempotente; nunca lanza).</summary>
    /// <param name="documentId">Documento propietario.</param>
    /// <param name="managedFilePath">Ruta absoluta de la copia administrada.</param>
    /// <param name="type">Tipo de documento.</param>
    Task EnsureThumbnailAsync(Guid documentId, string managedFilePath, DocumentType type, CancellationToken ct);

    /// <summary>Ruta de una miniatura ya generada, o <c>null</c>.</summary>
    string? TryGetExistingPath(Guid documentId);

    /// <summary>Elimina la miniatura si existe (borrado definitivo).</summary>
    Task DeleteThumbnailAsync(Guid documentId, CancellationToken ct);
}
