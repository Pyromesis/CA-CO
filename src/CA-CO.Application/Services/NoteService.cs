using CaCo.Application.Repositories;
using CaCo.Core;
using CaCo.Domain;
using Microsoft.Extensions.Logging;

namespace CaCo.Application.Services;

/// <summary>Caso de uso: comentarios/notas vinculadas a documentos (espacio de trabajo).</summary>
public interface INoteService
{
    /// <summary>Comentarios activos de un documento (recientes primero).</summary>
    Task<Result<IReadOnlyList<Note>>> ListByDocumentAsync(Guid documentId, CancellationToken ct);

    /// <summary>Añade un comentario a un documento.</summary>
    Task<Result<Note>> AddToDocumentAsync(Guid documentId, string content, CancellationToken ct);

    /// <summary>Edita el contenido de un comentario.</summary>
    Task<Result> UpdateAsync(Guid noteId, string content, CancellationToken ct);

    /// <summary>Elimina definitivamente un comentario.</summary>
    Task<Result> DeleteAsync(Guid noteId, CancellationToken ct);
}

/// <summary>Implementación de <see cref="INoteService"/>.</summary>
public sealed class NoteService(
    INoteRepository notes,
    IDocumentRepository documents,
    IClock clock,
    ILogger<NoteService> logger) : INoteService
{
    /// <inheritdoc/>
    public Task<Result<IReadOnlyList<Note>>> ListByDocumentAsync(Guid documentId, CancellationToken ct) =>
        notes.ListByDocumentAsync(documentId, ct);

    /// <inheritdoc/>
    public async Task<Result<Note>> AddToDocumentAsync(Guid documentId, string content, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return Result.Failure<Note>(
                Error.Validation("Note.EmptyContent", "El comentario no puede estar vacío."));
        }

        var found = await documents.GetByIdAsync(documentId, false, ct).ConfigureAwait(false);
        if (found.IsFailure || found.Value is null)
        {
            return Result.Failure<Note>(
                found.IsFailure ? found.Error : DomainErrors.Document.NotFound(documentId));
        }

        var title = content.Trim().Length <= 60
            ? content.Trim()
            : content.Trim()[..60] + "…";
        var created = Note.Create(title, content.Trim(), documentId, null, clock.UtcNow);
        if (created.IsFailure)
        {
            return created;
        }

        var added = await notes.AddAsync(created.Value, ct).ConfigureAwait(false);
        if (added.IsFailure)
        {
            return Result.Failure<Note>(added.Error);
        }

        logger.LogInformation("Comentario añadido a {DocumentId}", documentId);
        return created;
    }

    /// <inheritdoc/>
    public async Task<Result> UpdateAsync(Guid noteId, string content, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return Result.Failure(
                Error.Validation("Note.EmptyContent", "El comentario no puede estar vacío."));
        }

        var found = await notes.GetByIdAsync(noteId, false, ct).ConfigureAwait(false);
        if (found.IsFailure || found.Value is null)
        {
            return Result.Failure(found.IsFailure ? found.Error : DomainErrors.Note.NotFound(noteId));
        }

        var trimmed = content.Trim();
        var title = trimmed.Length <= 60 ? trimmed : trimmed[..60] + "…";
        var updated = found.Value.Update(title, trimmed, clock.UtcNow);
        if (updated.IsFailure)
        {
            return updated;
        }

        return await notes.UpdateAsync(found.Value, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task<Result> DeleteAsync(Guid noteId, CancellationToken ct) =>
        notes.DeleteAsync(noteId, ct);
}
