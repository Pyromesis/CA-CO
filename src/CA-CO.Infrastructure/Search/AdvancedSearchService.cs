using CaCo.Application.Ocr;
using CaCo.Application.Repositories;
using CaCo.Application.Search;
using CaCo.Core;
using CaCo.Domain;
using Microsoft.Extensions.Logging;

namespace CaCo.Infrastructure.Search;

/// <summary>Búsqueda avanzada local (Fase 6): multi-campo con relevancia y filtros.</summary>
public sealed class AdvancedSearchService(
    IDocumentRepository documents,
    INotebookRepository notebooks,
    INoteRepository notes,
    ITagRepository tags,
    ILogger<AdvancedSearchService> logger) : ISearchService
{
    private const int ScanPageSize = 200;
    private const int MaxScannedDocs = 2000;

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<Document>>> SearchDocumentsAsync(SearchQuery query, CancellationToken ct)
    {
        var hits = await SearchAdvancedAsync(query, ct).ConfigureAwait(false);
        return hits.IsFailure
            ? Result.Failure<IReadOnlyList<Document>>(hits.Error)
            : Result.Success<IReadOnlyList<Document>>(hits.Value.Select(h => h.Document).ToList());
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<SearchHit>>> SearchAdvancedAsync(SearchQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var text = query.Text?.Trim() ?? string.Empty;
        var hasFilters = query.NotebookId.HasValue || query.FileTypes is { Count: > 0 }
            || query.FavoritesOnly || !string.IsNullOrWhiteSpace(query.Tag)
            || query.FromUtc.HasValue || query.ToUtc.HasValue;
        if (text.Length == 0 && !hasFilters)
        {
            return Result.Success<IReadOnlyList<SearchHit>>([]);
        }

        var tagFilter = query.Tag?.Trim() ?? string.Empty;
        var take = Math.Clamp(query.MaxResults, 1, 200);

        var allTags = await tags.ListAllAsync(ct).ConfigureAwait(false);
        if (allTags.IsFailure)
        {
            return Result.Failure<IReadOnlyList<SearchHit>>(allTags.Error);
        }

        var tagNames = allTags.Value.ToDictionary(t => t.Id, t => t.Name);
        var allNotes = await notes.ListAllAsync(ct).ConfigureAwait(false);
        if (allNotes.IsFailure)
        {
            return Result.Failure<IReadOnlyList<SearchHit>>(allNotes.Error);
        }

        var notesByDoc = allNotes.Value
            .Where(n => n.DocumentId.HasValue)
            .GroupBy(n => n.DocumentId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var hits = new List<SearchHit>();
        var page = 1;
        var scanned = 0;
        while (scanned < MaxScannedDocs)
        {
            ct.ThrowIfCancellationRequested();
            var candidates = await documents.ListAsync(new DocumentQuery
            {
                NotebookId = query.NotebookId,
                FavoritesOnly = query.FavoritesOnly,
                IncludeDeleted = query.IncludeDeleted,
                Page = page,
                PageSize = ScanPageSize,
                OrderBy = DocumentOrder.ImportedDescending,
            }, ct).ConfigureAwait(false);
            if (candidates.IsFailure)
            {
                return Result.Failure<IReadOnlyList<SearchHit>>(candidates.Error);
            }

            if (candidates.Value.Items.Count == 0)
            {
                break;
            }

            foreach (var doc in candidates.Value.Items)
            {
                ct.ThrowIfCancellationRequested();
                if (!PassesFilters(doc, query, tagFilter, tagNames))
                {
                    continue;
                }

                var hit = Score(doc, text, tagNames, notesByDoc);
                if (hit is not null)
                {
                    hits.Add(hit);
                }
            }

            scanned += candidates.Value.Items.Count;
            if (!candidates.Value.HasNextPage)
            {
                break;
            }

            page++;
        }

        var ordered = hits
            .OrderByDescending(h => h.Score)
            .ThenByDescending(h => h.Document.ImportedAt)
            .Take(take)
            .ToList();
        logger.LogDebug("Búsqueda '{Text}': {Count} resultados.", text, ordered.Count);
        return Result.Success<IReadOnlyList<SearchHit>>(ordered);
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

    private static bool PassesFilters(
        Document doc, SearchQuery query, string tagFilter, Dictionary<Guid, string> tagNames)
    {
        if (query.FileTypes is { Count: > 0 } types && !types.Contains(doc.FileType))
        {
            return false;
        }

        if (query.FromUtc.HasValue && doc.ImportedAt < query.FromUtc.Value)
        {
            return false;
        }

        if (query.ToUtc.HasValue && doc.ImportedAt > query.ToUtc.Value)
        {
            return false;
        }

        if (tagFilter.Length > 0 && !doc.TagIds.Any(id =>
                tagNames.TryGetValue(id, out var name)
                && name.Contains(tagFilter, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return true;
    }

    private static SearchHit? Score(
        Document doc,
        string text,
        Dictionary<Guid, string> tagNames,
        Dictionary<Guid, List<Note>> notesByDoc)
    {
        if (text.Length == 0)
        {
            return new SearchHit(doc, 10, ["filtro"]);
        }

        var score = 0;
        var matched = new List<string>();
        void Add(int points, string field)
        {
            if (points > score)
            {
                score = points;
            }

            if (!matched.Contains(field))
            {
                matched.Add(field);
            }
        }

        if (doc.Name.Contains(text, StringComparison.OrdinalIgnoreCase))
        {
            Add(doc.Name.StartsWith(text, StringComparison.OrdinalIgnoreCase) ? 100 : 70, "nombre");
        }

        if (doc.OriginalFileName.Contains(text, StringComparison.OrdinalIgnoreCase))
        {
            Add(65, "archivo");
        }

        if (doc.TagIds.Any(id =>
                tagNames.TryGetValue(id, out var name)
                && name.Contains(text, StringComparison.OrdinalIgnoreCase)))
        {
            Add(60, "etiqueta");
        }

        if (notesByDoc.TryGetValue(doc.Id, out var docNotes) && docNotes.Any(n =>
                n.Title.Contains(text, StringComparison.OrdinalIgnoreCase)
                || n.Content.Contains(text, StringComparison.OrdinalIgnoreCase)))
        {
            Add(50, "nota");
        }

        var excerpt = doc.Metadata.Get(OcrMetadataKeys.Excerpt);
        if (!string.IsNullOrEmpty(excerpt)
            && excerpt.Contains(text, StringComparison.OrdinalIgnoreCase))
        {
            Add(40, "OCR");
        }

        return score == 0 ? null : new SearchHit(doc, score, matched);
    }
}
