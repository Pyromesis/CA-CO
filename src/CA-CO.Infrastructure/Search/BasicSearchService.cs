using CaCo.Application.Repositories;
using CaCo.Application.Search;
using CaCo.Core;
using CaCo.Domain;

namespace CaCo.Infrastructure.Search;

/// <summary>Búsqueda básica por nombre (Fase 0). Fase 6: texto completo, OCR y filtros.</summary>
public sealed class BasicSearchService(
    IDocumentRepository documents,
    INotebookRepository notebooks) : ISearchService
{
    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<Document>>> SearchDocumentsAsync(SearchQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (string.IsNullOrWhiteSpace(query.Text))
        {
            return Result.Success<IReadOnlyList<Document>>([]);
        }

        var page = await documents.ListAsync(new DocumentQuery
        {
            SearchText = query.Text.Trim(),
            NotebookId = query.NotebookId,
            IncludeDeleted = query.IncludeDeleted,
            Page = 1,
            PageSize = Math.Clamp(query.MaxResults, 1, 200),
        }, ct).ConfigureAwait(false);

        return page.IsFailure
            ? Result.Failure<IReadOnlyList<Document>>(page.Error)
            : Result.Success<IReadOnlyList<Document>>(page.Value.Items);
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<Notebook>>> SearchNotebooksAsync(string text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Result.Success<IReadOnlyList<Notebook>>([]);
        }

        var page = await notebooks.ListAsync(new NotebookQuery
        {
            SearchText = text.Trim(),
            Page = 1,
            PageSize = 50,
        }, ct).ConfigureAwait(false);

        return page.IsFailure
            ? Result.Failure<IReadOnlyList<Notebook>>(page.Error)
            : Result.Success<IReadOnlyList<Notebook>>(page.Value.Items);
    }
}
