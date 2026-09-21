using CaCo.Core;
using CaCo.Domain;

namespace CaCo.Application.Search;

/// <summary>Consulta de búsqueda (Fase 6: multi-campo, relevancia y filtros).</summary>
public sealed record SearchQuery
{
    /// <summary>Texto a buscar (nombres, etiquetas, notas, OCR).</summary>
    public required string Text { get; init; }

    /// <summary>Limitar a un cuaderno.</summary>
    public Guid? NotebookId { get; init; }

    /// <summary>Limitar a una serie de tipos de documento (p. ej. imágenes = PNG/JPG/JPEG).</summary>
    public IReadOnlyList<DocumentType>? FileTypes { get; init; }

    /// <summary>Solo favoritos.</summary>
    public bool FavoritesOnly { get; init; }

    /// <summary>Filtrar por etiqueta (nombre, insensible a mayúsculas).</summary>
    public string? Tag { get; init; }

    /// <summary>Importados desde (inclusive).</summary>
    public DateTimeOffset? FromUtc { get; init; }

    /// <summary>Importados hasta (inclusive).</summary>
    public DateTimeOffset? ToUtc { get; init; }

    /// <summary>Incluir papelera.</summary>
    public bool IncludeDeleted { get; init; }

    /// <summary>Máximo de resultados.</summary>
    public int MaxResults { get; init; } = 50;
}

/// <summary>Documento encontrado con su relevancia (Fase 6).</summary>
/// <param name="Document">Documento coincidente.</param>
/// <param name="Score">Puntuación (nombre 100/70, archivo 65, etiqueta 60, nota 50, OCR 40, filtro 10).</param>
/// <param name="MatchedIn">Dónde coincide ("nombre", "archivo", "etiqueta", "nota", "OCR", "filtro").</param>
public sealed record SearchHit(Document Document, int Score, IReadOnlyList<string> MatchedIn);

/// <summary>Búsqueda local y offline sobre la biblioteca.</summary>
public interface ISearchService
{
    /// <summary>Busca documentos (ordenados por relevancia).</summary>
    Task<Result<IReadOnlyList<Document>>> SearchDocumentsAsync(SearchQuery query, CancellationToken ct);

    /// <summary>Busca documentos con relevancia y campos coincidentes.</summary>
    Task<Result<IReadOnlyList<SearchHit>>> SearchAdvancedAsync(SearchQuery query, CancellationToken ct);

    /// <summary>Busca cuadernos por nombre.</summary>
    Task<Result<IReadOnlyList<Notebook>>> SearchNotebooksAsync(string text, CancellationToken ct);
}
