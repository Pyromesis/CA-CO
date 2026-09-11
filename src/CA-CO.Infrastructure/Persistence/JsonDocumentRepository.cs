using CaCo.Application.Repositories;
using CaCo.Application.Storage;
using CaCo.Core;
using CaCo.Domain;

namespace CaCo.Infrastructure.Persistence;

/// <summary>Repositorio de documentos sobre <c>Database/documents.json</c> (Fase 0).</summary>
public sealed class JsonDocumentRepository : IDocumentRepository
{
    private readonly JsonFileStore<DocumentDto> _store;

    /// <summary>Crea el repositorio.</summary>
    public JsonDocumentRepository(ILibraryPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _store = new JsonFileStore<DocumentDto>(paths.DocumentsDb, CacoJsonContext.Indented.ListDocumentDto);
    }

    /// <summary>Constructor para pruebas (ruta directa).</summary>
    internal JsonDocumentRepository(string filePath)
    {
        _store = new JsonFileStore<DocumentDto>(filePath, CacoJsonContext.Indented.ListDocumentDto);
    }

    /// <inheritdoc/>
    public async Task<Result<Document?>> GetByIdAsync(Guid id, bool includeDeleted, CancellationToken ct)
    {
        try
        {
            var items = await _store.LoadAsync(ct).ConfigureAwait(false);
            var dto = items.FirstOrDefault(d => d.Id == id && (includeDeleted || !d.IsDeleted));
            return Result.Success(dto?.ToEntity());
        }
        catch (Exception ex)
        {
            return Result.Failure<Document?>(Error.Storage("DocumentRepository.ReadFailed", $"No se pudo leer el documento: {ex.Message}"));
        }
    }

    /// <inheritdoc/>
    public async Task<Result<PagedResult<Document>>> ListAsync(DocumentQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        try
        {
            var items = await _store.LoadAsync(ct).ConfigureAwait(false);
            IEnumerable<DocumentDto> filtered = items;

            if (query.DeletedOnly)
            {
                filtered = filtered.Where(d => d.IsDeleted);
            }
            else if (!query.IncludeDeleted)
            {
                filtered = filtered.Where(d => !d.IsDeleted);
            }

            if (query.NotebookId.HasValue)
            {
                filtered = filtered.Where(d => d.NotebookId == query.NotebookId.Value);
            }
            else if (query.UnclassifiedOnly)
            {
                filtered = filtered.Where(d => d.NotebookId == null);
            }

            if (query.FavoritesOnly)
            {
                filtered = filtered.Where(d => d.IsFavorite);
            }

            if (!string.IsNullOrWhiteSpace(query.SearchText))
            {
                filtered = filtered.Where(d =>
                    d.Name.Contains(query.SearchText, StringComparison.OrdinalIgnoreCase));
            }

            filtered = query.OrderBy switch
            {
                DocumentOrder.ModifiedDescending => filtered.OrderByDescending(d => d.ModifiedAt),
                DocumentOrder.NameAscending => filtered.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase),
                DocumentOrder.SizeDescending => filtered.OrderByDescending(d => d.SizeBytes),
                _ => filtered.OrderByDescending(d => d.ImportedAt),
            };

            var total = filtered.Count();
            var page = Math.Max(1, query.Page);
            var pageSize = Math.Clamp(query.PageSize, 1, 200);
            var pageItems = filtered
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(d => d.ToEntity())
                .ToList();

            return Result.Success(new PagedResult<Document>
            {
                Items = pageItems,
                TotalCount = total,
                Page = page,
                PageSize = pageSize,
            });
        }
        catch (Exception ex)
        {
            return Result.Failure<PagedResult<Document>>(Error.Storage("DocumentRepository.ListFailed", $"No se pudo listar documentos: {ex.Message}"));
        }
    }

    /// <inheritdoc/>
    public async Task<Result<int>> CountActiveAsync(CancellationToken ct)
    {
        try
        {
            var items = await _store.LoadAsync(ct).ConfigureAwait(false);
            return Result.Success(items.Count(d => !d.IsDeleted));
        }
        catch (Exception ex)
        {
            return Result.Failure<int>(Error.Storage("DocumentRepository.CountFailed", $"No se pudo contar documentos: {ex.Message}"));
        }
    }

    /// <inheritdoc/>
    public async Task<Result<long>> SumActiveBytesAsync(CancellationToken ct)
    {
        try
        {
            var items = await _store.LoadAsync(ct).ConfigureAwait(false);
            return Result.Success(items.Where(d => !d.IsDeleted).Sum(d => d.SizeBytes));
        }
        catch (Exception ex)
        {
            return Result.Failure<long>(Error.Storage("DocumentRepository.SumFailed", $"No se pudo calcular el tamaño: {ex.Message}"));
        }
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<Document>>> FindByHashAsync(string hash, CancellationToken ct)
    {
        try
        {
            var items = await _store.LoadAsync(ct).ConfigureAwait(false);
            IReadOnlyList<Document> result = items
                .Where(d => !d.IsDeleted && string.Equals(d.ContentHash, hash, StringComparison.OrdinalIgnoreCase))
                .Select(d => d.ToEntity())
                .ToList();
            return Result.Success(result);
        }
        catch (Exception ex)
        {
            return Result.Failure<IReadOnlyList<Document>>(Error.Storage("DocumentRepository.HashFailed", $"No se pudo buscar por hash: {ex.Message}"));
        }
    }

    /// <inheritdoc/>
    public async Task<Result> AddAsync(Document document, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(document);
        try
        {
            var items = await _store.LoadAsync(ct).ConfigureAwait(false);
            if (items.Any(d => d.Id == document.Id))
            {
                return Result.Failure(Error.Conflict("DocumentRepository.Duplicate", "Ya existe un documento con ese identificador."));
            }

            items.Add(DocumentDto.FromEntity(document));
            await _store.SaveAsync(items, ct).ConfigureAwait(false);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(Error.Storage("DocumentRepository.AddFailed", $"No se pudo guardar el documento: {ex.Message}"));
        }
    }

    /// <inheritdoc/>
    public async Task<Result> UpdateAsync(Document document, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(document);
        try
        {
            var items = await _store.LoadAsync(ct).ConfigureAwait(false);
            var index = items.FindIndex(d => d.Id == document.Id);
            if (index < 0)
            {
                return Result.Failure(DomainErrors.Document.NotFound(document.Id));
            }

            items[index] = DocumentDto.FromEntity(document);
            await _store.SaveAsync(items, ct).ConfigureAwait(false);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(Error.Storage("DocumentRepository.UpdateFailed", $"No se pudo actualizar el documento: {ex.Message}"));
        }
    }

    /// <inheritdoc/>
    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct)
    {
        try
        {
            var items = await _store.LoadAsync(ct).ConfigureAwait(false);
            var removed = items.RemoveAll(d => d.Id == id);
            if (removed == 0)
            {
                return Result.Failure(DomainErrors.Document.NotFound(id));
            }

            await _store.SaveAsync(items, ct).ConfigureAwait(false);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(Error.Storage("DocumentRepository.DeleteFailed", $"No se pudo eliminar el documento: {ex.Message}"));
        }
    }
}
