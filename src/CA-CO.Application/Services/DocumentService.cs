using CaCo.Application.Repositories;
using CaCo.Application.Storage;
using CaCo.Core;
using CaCo.Domain;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;

namespace CaCo.Application.Services;

/// <summary>Caso de uso: operaciones sobre documentos.</summary>
public interface IDocumentService
{
    /// <summary>Obtiene un documento por id.</summary>
    Task<Result<Document?>> GetByIdAsync(Guid id, bool includeDeleted, CancellationToken ct);

    /// <summary>Lista documentos según criterios.</summary>
    Task<Result<PagedResult<Document>>> ListAsync(DocumentQuery query, CancellationToken ct);

    /// <summary>Documentos recientes (no eliminados, por fecha de importación).</summary>
    Task<Result<IReadOnlyList<Document>>> GetRecentAsync(int count, CancellationToken ct);

    /// <summary>Renombra un documento.</summary>
    Task<Result> RenameAsync(Guid id, string newName, CancellationToken ct);

    /// <summary>Marca o desmarca favorito.</summary>
    Task<Result> SetFavoriteAsync(Guid id, bool isFavorite, CancellationToken ct);

    /// <summary>Mueve a la papelera.</summary>
    Task<Result> MoveToTrashAsync(Guid id, CancellationToken ct);

    /// <summary>Restaura desde la papelera.</summary>
    Task<Result> RestoreAsync(Guid id, CancellationToken ct);

    /// <summary>Clasifica en un cuaderno.</summary>
    Task<Result> MoveToNotebookAsync(Guid id, Guid? notebookId, CancellationToken ct);

    /// <summary>Vacía la papelera (borrado definitivo de entidades; archivos en Fase 1).</summary>
    Task<Result<int>> EmptyTrashAsync(CancellationToken ct);

    /// <summary>Elimina definitivamente un documento.</summary>
    Task<Result> DeletePermanentlyAsync(Guid id, CancellationToken ct);

    /// <summary>Todas las etiquetas (para sugerencias).</summary>
    Task<Result<IReadOnlyList<Tag>>> ListAllTagsAsync(CancellationToken ct);

    /// <summary>Crea la etiqueta si no existe y la asigna al documento.</summary>
    Task<Result> AddTagAsync(Guid documentId, string tagName, CancellationToken ct);

    /// <summary>Quita una etiqueta de un documento.</summary>
    Task<Result> RemoveTagAsync(Guid documentId, Guid tagId, CancellationToken ct);

    /// <summary>
    /// Guarda el contenido editado de un TXT en su copia administrada y actualiza
    /// tamaño y hash (espacio de trabajo, Fase 3).
    /// </summary>
    Task<Result> SaveTextContentAsync(Guid documentId, string text, CancellationToken ct);

    /// <summary>
    /// Relee la copia administrada y actualiza tamaño y hash (adopta anotaciones
    /// guardadas con Ctrl+S en el visor externo/integrado).
    /// </summary>
    Task<Result<bool>> RefreshFileMetadataAsync(Guid documentId, CancellationToken ct);
}

/// <summary>Implementación de <see cref="IDocumentService"/> sobre repositorios.</summary>
public sealed class DocumentService(
    IDocumentRepository documents,
    INotebookRepository notebooks,
    ITagRepository tags,
    IFileStorage storage,
    ILibraryPaths paths,
    IThumbnailService thumbnails,
    IClock clock,
    ILogger<DocumentService> logger) : IDocumentService
{
    /// <inheritdoc/>
    public Task<Result<Document?>> GetByIdAsync(Guid id, bool includeDeleted, CancellationToken ct) =>
        documents.GetByIdAsync(id, includeDeleted, ct);

    /// <inheritdoc/>
    public Task<Result<PagedResult<Document>>> ListAsync(DocumentQuery query, CancellationToken ct) =>
        documents.ListAsync(query, ct);

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<Document>>> GetRecentAsync(int count, CancellationToken ct)
    {
        var page = await documents.ListAsync(
            new DocumentQuery { Page = 1, PageSize = Math.Clamp(count, 1, 100) },
            ct).ConfigureAwait(false);
        return page.IsFailure
            ? Result.Failure<IReadOnlyList<Document>>(page.Error)
            : Result.Success<IReadOnlyList<Document>>(page.Value.Items);
    }

    /// <inheritdoc/>
    public async Task<Result> RenameAsync(Guid id, string newName, CancellationToken ct)
    {
        var found = await documents.GetByIdAsync(id, false, ct).ConfigureAwait(false);
        if (found.IsFailure || found.Value is null)
        {
            return Result.Failure(found.IsFailure ? found.Error : DomainErrors.Document.NotFound(id));
        }

        var renamed = found.Value.Rename(newName, clock.UtcNow);
        if (renamed.IsFailure)
        {
            return renamed;
        }

        return await documents.UpdateAsync(found.Value, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<Result> SetFavoriteAsync(Guid id, bool isFavorite, CancellationToken ct)
    {
        var found = await documents.GetByIdAsync(id, true, ct).ConfigureAwait(false);
        if (found.IsFailure || found.Value is null)
        {
            return Result.Failure(found.IsFailure ? found.Error : DomainErrors.Document.NotFound(id));
        }

        if (found.Value.IsDeleted && isFavorite)
        {
            return Result.Failure(Error.Conflict(
                "Document.FavoriteInTrash", "No se puede marcar como favorito un documento en papelera."));
        }

        found.Value.SetFavorite(isFavorite, clock.UtcNow);
        var updated = await documents.UpdateAsync(found.Value, ct).ConfigureAwait(false);
        if (updated.IsSuccess)
        {
            logger.LogInformation("Favorito de {DocumentId} = {IsFavorite}", id, isFavorite);
        }

        return updated;
    }

    /// <inheritdoc/>
    public async Task<Result> MoveToTrashAsync(Guid id, CancellationToken ct)
    {
        var found = await documents.GetByIdAsync(id, false, ct).ConfigureAwait(false);
        if (found.IsFailure || found.Value is null)
        {
            return Result.Failure(found.IsFailure ? found.Error : DomainErrors.Document.NotFound(id));
        }

        var trashed = found.Value.MoveToTrash(clock.UtcNow);
        if (trashed.IsFailure)
        {
            return trashed;
        }

        return await documents.UpdateAsync(found.Value, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<Result> RestoreAsync(Guid id, CancellationToken ct)
    {
        var found = await documents.GetByIdAsync(id, true, ct).ConfigureAwait(false);
        if (found.IsFailure || found.Value is null)
        {
            return Result.Failure(found.IsFailure ? found.Error : DomainErrors.Document.NotFound(id));
        }

        var restored = found.Value.Restore(clock.UtcNow);
        if (restored.IsFailure)
        {
            return restored;
        }

        return await documents.UpdateAsync(found.Value, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<Result> MoveToNotebookAsync(Guid id, Guid? notebookId, CancellationToken ct)
    {
        var found = await documents.GetByIdAsync(id, false, ct).ConfigureAwait(false);
        if (found.IsFailure || found.Value is null)
        {
            return Result.Failure(found.IsFailure ? found.Error : DomainErrors.Document.NotFound(id));
        }

        if (notebookId.HasValue)
        {
            var parent = await notebooks.GetByIdAsync(notebookId.Value, false, ct).ConfigureAwait(false);
            if (parent.IsFailure || parent.Value is null)
            {
                return Result.Failure(parent.IsFailure ? parent.Error : DomainErrors.Notebook.NotFound(notebookId.Value));
            }
        }

        found.Value.MoveToNotebook(notebookId, clock.UtcNow);
        return await documents.UpdateAsync(found.Value, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<Result<int>> EmptyTrashAsync(CancellationToken ct)
    {
        var trash = await documents.ListAsync(
            new DocumentQuery { DeletedOnly = true, IncludeDeleted = true, Page = 1, PageSize = 200 },
            ct).ConfigureAwait(false);
        if (trash.IsFailure)
        {
            return Result.Failure<int>(trash.Error);
        }

        var removed = 0;
        var iterations = 0;
        const int maxIterations = 50;
        while (trash.Value.Items.Count > 0 && iterations < maxIterations)
        {
            iterations++;
            var removedThisRound = 0;
            foreach (var doc in trash.Value.Items)
            {
                ct.ThrowIfCancellationRequested();
                var deleted = await DeletePermanentlyAsync(doc.Id, ct).ConfigureAwait(false);
                if (deleted.IsSuccess)
                {
                    removed++;
                    removedThisRound++;
                }
                else
                {
                    logger.LogWarning("No se pudo eliminar definitivamente {DocumentId}: {Error}", doc.Id, deleted.Error);
                }
            }

            // Si en una vuelta completa no se pudo borrar nada, salir para no
            // girar en bucle infinito sobre el mismo fallo persistente.
            if (removedThisRound == 0)
            {
                break;
            }

            trash = await documents.ListAsync(
                new DocumentQuery { DeletedOnly = true, IncludeDeleted = true, Page = 1, PageSize = 200 },
                ct).ConfigureAwait(false);
            if (trash.IsFailure)
            {
                return Result.Failure<int>(trash.Error);
            }
        }

        return Result.Success(removed);
    }

    /// <inheritdoc/>
    public async Task<Result> DeletePermanentlyAsync(Guid id, CancellationToken ct)
    {
        var found = await documents.GetByIdAsync(id, true, ct).ConfigureAwait(false);
        if (found.IsFailure || found.Value is null)
        {
            return Result.Failure(found.IsFailure ? found.Error : DomainErrors.Document.NotFound(id));
        }

        if (found.Value.StoredFileName is not null)
        {
            try
            {
                await storage.DeleteAsync(paths.Documents, found.Value.StoredFileName, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "No se pudo borrar el archivo administrado de {DocumentId}", id);
            }
        }

        if (found.Value.OriginalStoredFileName is not null)
        {
            try
            {
                await storage.DeleteAsync(paths.Originals, found.Value.OriginalStoredFileName, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "No se pudo borrar la copia original de {DocumentId}", id);
            }
        }

        try
        {
            await thumbnails.DeleteThumbnailAsync(found.Value.Id, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "No se pudo borrar la miniatura de {DocumentId}", id);
        }

        return await documents.DeleteAsync(id, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task<Result<IReadOnlyList<Tag>>> ListAllTagsAsync(CancellationToken ct) =>
        tags.ListAllAsync(ct);

    /// <inheritdoc/>
    public async Task<Result> AddTagAsync(Guid documentId, string tagName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(tagName))
        {
            return Result.Failure(DomainErrors.Tag.InvalidName());
        }

        var found = await documents.GetByIdAsync(documentId, false, ct).ConfigureAwait(false);
        if (found.IsFailure || found.Value is null)
        {
            return Result.Failure(found.IsFailure ? found.Error : DomainErrors.Document.NotFound(documentId));
        }

        var existing = await tags.GetByNameAsync(tagName, ct).ConfigureAwait(false);
        if (existing.IsFailure)
        {
            return Result.Failure(existing.Error);
        }

        Tag tag;
        if (existing.Value is null)
        {
            var created = Tag.Create(tagName, clock.UtcNow);
            if (created.IsFailure)
            {
                return created;
            }

            var added = await tags.AddAsync(created.Value, ct).ConfigureAwait(false);
            if (added.IsFailure)
            {
                return added;
            }

            tag = created.Value;
        }
        else
        {
            tag = existing.Value;
        }

        found.Value.AddTag(tag.Id);
        return await documents.UpdateAsync(found.Value, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<Result> RemoveTagAsync(Guid documentId, Guid tagId, CancellationToken ct)
    {
        var found = await documents.GetByIdAsync(documentId, false, ct).ConfigureAwait(false);
        if (found.IsFailure || found.Value is null)
        {
            return Result.Failure(found.IsFailure ? found.Error : DomainErrors.Document.NotFound(documentId));
        }

        found.Value.RemoveTag(tagId);
        return await documents.UpdateAsync(found.Value, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<Result> SaveTextContentAsync(Guid documentId, string text, CancellationToken ct)
    {
        if (text is null)
        {
            return Result.Failure(Error.Validation("Document.InvalidContent", "El contenido no puede ser nulo."));
        }
        var found = await documents.GetByIdAsync(documentId, false, ct).ConfigureAwait(false);
        if (found.IsFailure || found.Value is null)
        {
            return Result.Failure(found.IsFailure ? found.Error : DomainErrors.Document.NotFound(documentId));
        }

        var document = found.Value;
        if (document.FileType != DocumentType.Txt || document.StoredFileName is null)
        {
            return Result.Failure(Error.Validation("Document.NotEditable", "Solo los TXT se editan dentro de CA-CO."));
        }

        const int maxTextChars = 1_000_000;
        if (text.Length > maxTextChars)
        {
            return Result.Failure(Error.Validation(
                "Document.ContentTooLong", $"El texto no puede superar {maxTextChars:N0} caracteres."));
        }

        try
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            await storage.WriteAllBytesAsync(paths.Documents, document.StoredFileName, bytes, ct).ConfigureAwait(false);
            var refreshed = document.RefreshSizeAndHash(bytes.Length, hash, clock.UtcNow);
            if (refreshed.IsFailure)
            {
                return refreshed;
            }

            return await documents.UpdateAsync(document, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "No se pudo guardar el contenido de {DocumentId}", documentId);
            return Result.Failure(Error.Storage("Document.SaveFailed", $"No se pudo guardar: {ex.Message}"));
        }
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> RefreshFileMetadataAsync(Guid documentId, CancellationToken ct)
    {
        var found = await documents.GetByIdAsync(documentId, false, ct).ConfigureAwait(false);
        if (found.IsFailure || found.Value is null)
        {
            return Result.Failure<bool>(found.IsFailure ? found.Error : DomainErrors.Document.NotFound(documentId));
        }

        var document = found.Value;
        if (document.StoredFileName is null)
        {
            return Result.Failure<bool>(Error.Validation("Document.NoCopy", "Aún no hay copia administrada."));
        }

        if (!IsSafeStoredFileName(document.StoredFileName))
        {
            return Result.Failure<bool>(Error.Validation("Document.InvalidStoredName", "El nombre almacenado no es válido."));
        }

        const long maxRefreshBytes = 200L * 1024 * 1024;
        try
        {
            var fullPath = Path.Combine(paths.Documents, document.StoredFileName);
            var info = new FileInfo(fullPath);
            if (!info.Exists)
            {
                return Result.Failure<bool>(Error.Storage("Document.MissingFile", "El archivo ya no está en la biblioteca."));
            }

            if (info.Length > maxRefreshBytes)
            {
                return Result.Failure<bool>(Error.Validation(
                    "Document.TooLarge", "El archivo supera el máximo re-legible (200 MB)."));
            }

            byte[] hashBytes;
            await using (var stream = new FileStream(
                fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 20,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                hashBytes = await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);
            }

            // Re-stat tras el hash: el archivo pudo cambiar durante la lectura.
            info.Refresh();
            if (!info.Exists)
            {
                return Result.Failure<bool>(Error.Storage("Document.MissingFile", "El archivo ya no está en la biblioteca."));
            }

            if (info.Length > maxRefreshBytes)
            {
                return Result.Failure<bool>(Error.Validation(
                    "Document.TooLarge", "El archivo supera el máximo re-legible (200 MB)."));
            }

            var hash = Convert.ToHexString(hashBytes);
            if (info.Length == document.SizeBytes
                && string.Equals(hash, document.ContentHash, StringComparison.OrdinalIgnoreCase))
            {
                return Result.Success(false);
            }

            var refreshed = document.RefreshSizeAndHash(info.Length, hash, clock.UtcNow);
            if (refreshed.IsFailure)
            {
                return Result.Failure<bool>(refreshed.Error);
            }

            var updated = await documents.UpdateAsync(document, ct).ConfigureAwait(false);
            return updated.IsFailure ? Result.Failure<bool>(updated.Error) : Result.Success(true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "No se pudo releer el archivo de {DocumentId}", documentId);
            return Result.Failure<bool>(Error.Storage("Document.RefreshFailed", "No se pudo releer. El detalle quedó registrado."));
        }
    }

    private static bool IsSafeStoredFileName(string storedFileName) =>
        !string.IsNullOrWhiteSpace(storedFileName)
        && storedFileName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
        && !storedFileName.Contains("..", StringComparison.Ordinal)
        && Path.GetFileName(storedFileName) == storedFileName;
}
