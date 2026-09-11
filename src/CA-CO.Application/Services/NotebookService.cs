using CaCo.Application.Repositories;
using CaCo.Core;
using CaCo.Domain;
using Microsoft.Extensions.Logging;

namespace CaCo.Application.Services;

/// <summary>Caso de uso: operaciones sobre cuadernos y su jerarquía.</summary>
public interface INotebookService
{
    /// <summary>Crea un cuaderno (valida que el padre exista).</summary>
    Task<Result<Notebook>> CreateAsync(string name, Guid? parentId, CancellationToken ct);

    /// <summary>Renombra un cuaderno.</summary>
    Task<Result> RenameAsync(Guid id, string newName, CancellationToken ct);

    /// <summary>Mueve un cuaderno (valida ciclos recorriendo ancestros).</summary>
    Task<Result> MoveAsync(Guid id, Guid? newParentId, CancellationToken ct);

    /// <summary>
    /// Elimina un cuaderno: los hijos suben al abuelo y los documentos se desclasifican.
    /// Nunca se pierde contenido por reorganizar.
    /// </summary>
    Task<Result> DeleteAsync(Guid id, CancellationToken ct);

    /// <summary>Todos los cuadernos activos para construir el árbol.</summary>
    Task<Result<IReadOnlyList<Notebook>>> ListAllActiveAsync(CancellationToken ct);
}

/// <summary>Implementación de <see cref="INotebookService"/>.</summary>
public sealed class NotebookService(
    INotebookRepository notebooks,
    IDocumentRepository documents,
    IClock clock,
    ILogger<NotebookService> logger) : INotebookService
{
    /// <inheritdoc/>
    public async Task<Result<Notebook>> CreateAsync(string name, Guid? parentId, CancellationToken ct)
    {
        if (parentId.HasValue)
        {
            var parent = await notebooks.GetByIdAsync(parentId.Value, false, ct).ConfigureAwait(false);
            if (parent.IsFailure || parent.Value is null)
            {
                return Result.Failure<Notebook>(
                    parent.IsFailure ? parent.Error : DomainErrors.Notebook.ParentNotFound(parentId.Value));
            }
        }

        var created = Notebook.Create(name, parentId, clock.UtcNow);
        if (created.IsFailure)
        {
            return created;
        }

        var added = await notebooks.AddAsync(created.Value, ct).ConfigureAwait(false);
        if (added.IsFailure)
        {
            return Result.Failure<Notebook>(added.Error);
        }

        logger.LogInformation("Cuaderno creado: {NotebookId} ({Name})", created.Value.Id, created.Value.Name);
        return created;
    }

    /// <inheritdoc/>
    public async Task<Result> RenameAsync(Guid id, string newName, CancellationToken ct)
    {
        var found = await notebooks.GetByIdAsync(id, false, ct).ConfigureAwait(false);
        if (found.IsFailure || found.Value is null)
        {
            return Result.Failure(found.IsFailure ? found.Error : DomainErrors.Notebook.NotFound(id));
        }

        var renamed = found.Value.Rename(newName, clock.UtcNow);
        if (renamed.IsFailure)
        {
            return renamed;
        }

        return await notebooks.UpdateAsync(found.Value, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<Result> MoveAsync(Guid id, Guid? newParentId, CancellationToken ct)
    {
        var found = await notebooks.GetByIdAsync(id, false, ct).ConfigureAwait(false);
        if (found.IsFailure || found.Value is null)
        {
            return Result.Failure(found.IsFailure ? found.Error : DomainErrors.Notebook.NotFound(id));
        }

        if (newParentId.HasValue)
        {
            var parent = await notebooks.GetByIdAsync(newParentId.Value, false, ct).ConfigureAwait(false);
            if (parent.IsFailure || parent.Value is null)
            {
                return Result.Failure(parent.IsFailure ? parent.Error : DomainErrors.Notebook.ParentNotFound(newParentId.Value));
            }

            if (await WouldCreateCycleAsync(id, newParentId.Value, ct).ConfigureAwait(false))
            {
                return Result.Failure(DomainErrors.Notebook.Cycle());
            }
        }

        var moved = found.Value.MoveTo(newParentId, clock.UtcNow);
        if (moved.IsFailure)
        {
            return moved;
        }

        return await notebooks.UpdateAsync(found.Value, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct)
    {
        var found = await notebooks.GetByIdAsync(id, false, ct).ConfigureAwait(false);
        if (found.IsFailure || found.Value is null)
        {
            return Result.Failure(found.IsFailure ? found.Error : DomainErrors.Notebook.NotFound(id));
        }

        var notebook = found.Value;

        // 1. Los hijos suben al abuelo (paginado: puede haber más de 200).
        var children = await notebooks.ListAsync(
            new NotebookQuery { ParentId = id, Page = 1, PageSize = 200 }, ct).ConfigureAwait(false);
        if (children.IsFailure)
        {
            return Result.Failure(children.Error);
        }

        var iterations = 0;
        const int maxIterations = 50;
        while (children.Value.Items.Count > 0 && iterations < maxIterations)
        {
            iterations++;
            var movedThisRound = 0;
            foreach (var child in children.Value.Items)
            {
                ct.ThrowIfCancellationRequested();
                var moved = child.MoveTo(notebook.ParentId, clock.UtcNow);
                if (moved.IsFailure)
                {
                    return moved;
                }

                var updated = await notebooks.UpdateAsync(child, ct).ConfigureAwait(false);
                if (updated.IsFailure)
                {
                    return updated;
                }

                movedThisRound++;
            }

            if (movedThisRound == 0)
            {
                break;
            }

            children = await notebooks.ListAsync(
                new NotebookQuery { ParentId = id, Page = 1, PageSize = 200 }, ct).ConfigureAwait(false);
            if (children.IsFailure)
            {
                return Result.Failure(children.Error);
            }
        }

        // 2. Los documentos se desclasifican (siguen visibles en Documentos).
        var docs = await documents.ListAsync(
            new DocumentQuery { NotebookId = id, Page = 1, PageSize = 200 }, ct).ConfigureAwait(false);
        if (docs.IsFailure)
        {
            return Result.Failure(docs.Error);
        }

        var docIterations = 0;
        while (docs.Value.Items.Count > 0 && docIterations < maxIterations)
        {
            docIterations++;
            var updatedThisRound = 0;
            foreach (var doc in docs.Value.Items)
            {
                ct.ThrowIfCancellationRequested();
                doc.MoveToNotebook(null, clock.UtcNow);
                var updated = await documents.UpdateAsync(doc, ct).ConfigureAwait(false);
                if (updated.IsFailure)
                {
                    return updated;
                }

                updatedThisRound++;
            }

            if (updatedThisRound == 0)
            {
                break;
            }

            docs = await documents.ListAsync(
                new DocumentQuery { NotebookId = id, Page = 1, PageSize = 200 }, ct).ConfigureAwait(false);
            if (docs.IsFailure)
            {
                return Result.Failure(docs.Error);
            }
        }

        // 3. Borrado lógico del cuaderno.
        notebook.MarkDeleted(clock.UtcNow);
        var result = await notebooks.UpdateAsync(notebook, ct).ConfigureAwait(false);
        if (result.IsSuccess)
        {
            logger.LogInformation("Cuaderno eliminado: {NotebookId}", id);
        }

        return result;
    }

    /// <inheritdoc/>
    public Task<Result<IReadOnlyList<Notebook>>> ListAllActiveAsync(CancellationToken ct) =>
        notebooks.ListAllActiveAsync(ct);

    private async Task<bool> WouldCreateCycleAsync(Guid movingId, Guid newParentId, CancellationToken ct)
    {
        var currentId = (Guid?)newParentId;
        while (currentId.HasValue)
        {
            if (currentId.Value == movingId)
            {
                return true;
            }

            var current = await notebooks.GetByIdAsync(currentId.Value, false, ct).ConfigureAwait(false);
            if (current.IsFailure || current.Value is null)
            {
                // Fail-closed: ante un fallo de lectura no se puede garantizar
                // que no haya ciclo, así que se bloquea el movimiento.
                logger.LogWarning("No se pudo verificar ciclo para {MovingId}: lectura de ancestro falló.", movingId);
                return true;
            }

            currentId = current.Value.ParentId;
        }

        return false;
    }
}
