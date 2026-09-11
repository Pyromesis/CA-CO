using CaCo.Application.Repositories;
using CaCo.Application.Storage;
using CaCo.Core;
using CaCo.Domain;

namespace CaCo.Infrastructure.Persistence;

/// <summary>Repositorio de cuadernos sobre <c>Database/notebooks.json</c> (Fase 0).</summary>
public sealed class JsonNotebookRepository : INotebookRepository
{
    private readonly JsonFileStore<NotebookDto> _store;

    /// <summary>Crea el repositorio.</summary>
    public JsonNotebookRepository(ILibraryPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _store = new JsonFileStore<NotebookDto>(paths.NotebooksDb, CacoJsonContext.Indented.ListNotebookDto);
    }

    /// <summary>Constructor para pruebas (ruta directa).</summary>
    internal JsonNotebookRepository(string filePath)
    {
        _store = new JsonFileStore<NotebookDto>(filePath, CacoJsonContext.Indented.ListNotebookDto);
    }

    /// <inheritdoc/>
    public async Task<Result<Notebook?>> GetByIdAsync(Guid id, bool includeDeleted, CancellationToken ct)
    {
        try
        {
            var items = await _store.LoadAsync(ct).ConfigureAwait(false);
            var dto = items.FirstOrDefault(n => n.Id == id && (includeDeleted || !n.IsDeleted));
            return Result.Success(dto?.ToEntity());
        }
        catch (Exception ex)
        {
            return Result.Failure<Notebook?>(Error.Storage("NotebookRepository.ReadFailed", $"No se pudo leer el cuaderno: {ex.Message}"));
        }
    }

    /// <inheritdoc/>
    public async Task<Result<PagedResult<Notebook>>> ListAsync(NotebookQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        try
        {
            var items = await _store.LoadAsync(ct).ConfigureAwait(false);
            IEnumerable<NotebookDto> filtered = query.IncludeDeleted
                ? items
                : items.Where(n => !n.IsDeleted);

            if (query.ParentId.HasValue)
            {
                filtered = filtered.Where(n => n.ParentId == query.ParentId.Value);
            }
            else if (query.RootsOnly)
            {
                filtered = filtered.Where(n => n.ParentId == null);
            }

            if (!string.IsNullOrWhiteSpace(query.SearchText))
            {
                filtered = filtered.Where(n =>
                    n.Name.Contains(query.SearchText, StringComparison.OrdinalIgnoreCase));
            }

            var ordered = filtered
                .OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var total = ordered.Count;
            var page = Math.Max(1, query.Page);
            var pageSize = Math.Clamp(query.PageSize, 1, 200);

            return Result.Success(new PagedResult<Notebook>
            {
                Items = ordered.Skip((page - 1) * pageSize).Take(pageSize).Select(n => n.ToEntity()).ToList(),
                TotalCount = total,
                Page = page,
                PageSize = pageSize,
            });
        }
        catch (Exception ex)
        {
            return Result.Failure<PagedResult<Notebook>>(Error.Storage("NotebookRepository.ListFailed", $"No se pudo listar cuadernos: {ex.Message}"));
        }
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<Notebook>>> ListAllActiveAsync(CancellationToken ct)
    {
        try
        {
            var items = await _store.LoadAsync(ct).ConfigureAwait(false);
            IReadOnlyList<Notebook> result = items
                .Where(n => !n.IsDeleted)
                .OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
                .Select(n => n.ToEntity())
                .ToList();
            return Result.Success(result);
        }
        catch (Exception ex)
        {
            return Result.Failure<IReadOnlyList<Notebook>>(Error.Storage("NotebookRepository.ListFailed", $"No se pudo listar cuadernos: {ex.Message}"));
        }
    }

    /// <inheritdoc/>
    public async Task<Result<int>> CountActiveAsync(CancellationToken ct)
    {
        try
        {
            var items = await _store.LoadAsync(ct).ConfigureAwait(false);
            return Result.Success(items.Count(n => !n.IsDeleted));
        }
        catch (Exception ex)
        {
            return Result.Failure<int>(Error.Storage("NotebookRepository.CountFailed", $"No se pudo contar cuadernos: {ex.Message}"));
        }
    }

    /// <inheritdoc/>
    public async Task<Result> AddAsync(Notebook notebook, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(notebook);
        try
        {
            var items = await _store.LoadAsync(ct).ConfigureAwait(false);
            if (items.Any(n => n.Id == notebook.Id))
            {
                return Result.Failure(Error.Conflict("NotebookRepository.Duplicate", "Ya existe un cuaderno con ese identificador."));
            }

            items.Add(NotebookDto.FromEntity(notebook));
            await _store.SaveAsync(items, ct).ConfigureAwait(false);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(Error.Storage("NotebookRepository.AddFailed", $"No se pudo guardar el cuaderno: {ex.Message}"));
        }
    }

    /// <inheritdoc/>
    public async Task<Result> UpdateAsync(Notebook notebook, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(notebook);
        try
        {
            var items = await _store.LoadAsync(ct).ConfigureAwait(false);
            var index = items.FindIndex(n => n.Id == notebook.Id);
            if (index < 0)
            {
                return Result.Failure(DomainErrors.Notebook.NotFound(notebook.Id));
            }

            items[index] = NotebookDto.FromEntity(notebook);
            await _store.SaveAsync(items, ct).ConfigureAwait(false);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(Error.Storage("NotebookRepository.UpdateFailed", $"No se pudo actualizar el cuaderno: {ex.Message}"));
        }
    }

    /// <inheritdoc/>
    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct)
    {
        try
        {
            var items = await _store.LoadAsync(ct).ConfigureAwait(false);
            var removed = items.RemoveAll(n => n.Id == id);
            if (removed == 0)
            {
                return Result.Failure(DomainErrors.Notebook.NotFound(id));
            }

            await _store.SaveAsync(items, ct).ConfigureAwait(false);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(Error.Storage("NotebookRepository.DeleteFailed", $"No se pudo eliminar el cuaderno: {ex.Message}"));
        }
    }
}
