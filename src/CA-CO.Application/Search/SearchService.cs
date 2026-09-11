using CaCo.Core;
using CaCo.Domain;

namespace CaCo.Application.Search;

/// <summary>Consulta de búsqueda (Fase 0: por nombre; Fase 6: texto completo, OCR, filtros).</summary>
public sealed record SearchQuery
{
    /// <summary>Texto a buscar.</summary>
    public required string Text { get; init; }

    /// <summary>Limitar a un cuaderno.</summary>
    public Guid? NotebookId { get; init; }

    /// <summary>Incluir papelera.</summary>
    public bool IncludeDeleted { get; init; }

    /// <summary>Máximo de resultados.</summary>
    public int MaxResults { get; init; } = 50;
}

/// <summary>Búsqueda local y offline sobre la biblioteca.</summary>
public interface ISearchService
{
    /// <summary>Busca documentos por nombre.</summary>
    Task<Result<IReadOnlyList<Document>>> SearchDocumentsAsync(SearchQuery query, CancellationToken ct);

    /// <summary>Busca cuadernos por nombre.</summary>
    Task<Result<IReadOnlyList<Notebook>>> SearchNotebooksAsync(string text, CancellationToken ct);
}
