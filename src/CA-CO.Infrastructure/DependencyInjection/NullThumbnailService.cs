using CaCo.Application.Storage;
using CaCo.Domain;

namespace CaCo.Infrastructure.DependencyInjection;

/// <summary>
/// Miniaturas desactivadas (se usa cuando la UI no registra la implementación real,
/// p. ej. en tests). Nunca falla ni hace nada.
/// </summary>
public sealed class NullThumbnailService : IThumbnailService
{
    /// <inheritdoc/>
    public Task EnsureThumbnailAsync(Guid documentId, string managedFilePath, DocumentType type, CancellationToken ct) =>
        Task.CompletedTask;

    /// <inheritdoc/>
    public string? TryGetExistingPath(Guid documentId) => null;

    /// <inheritdoc/>
    public Task DeleteThumbnailAsync(Guid documentId, CancellationToken ct) => Task.CompletedTask;
}
