using CaCo.Core;
using CaCo.Domain;

namespace CaCo.Application.Repositories;

/// <summary>
/// Criterios de consulta de documentos. Toda consulta está paginada:
/// la biblioteca puede crecer a decenas de miles de elementos.
/// </summary>
public sealed record DocumentQuery
{
    /// <summary>Solo documentos de este cuaderno (<c>null</c> = todos; ver <see cref="UnclassifiedOnly"/>).</summary>
    public Guid? NotebookId { get; init; }

    /// <summary>Solo documentos sin clasificar.</summary>
    public bool UnclassifiedOnly { get; init; }

    /// <summary>Solo favoritos.</summary>
    public bool FavoritesOnly { get; init; }

    /// <summary>Incluir papelera (por defecto se excluye).</summary>
    public bool IncludeDeleted { get; init; }

    /// <summary>Solo papelera.</summary>
    public bool DeletedOnly { get; init; }

    /// <summary>Filtro por texto en el nombre.</summary>
    public string? SearchText { get; init; }

    /// <summary>Orden.</summary>
    public DocumentOrder OrderBy { get; init; } = DocumentOrder.ImportedDescending;

    /// <summary>Página base 1.</summary>
    public int Page { get; init; } = 1;

    /// <summary>Tamaño de página (1-200).</summary>
    public int PageSize { get; init; } = 50;
}

/// <summary>Órdenes soportados al listar documentos.</summary>
public enum DocumentOrder
{
    /// <summary>Más recientemente importados primero.</summary>
    ImportedDescending = 0,

    /// <summary>Modificados recientemente primero.</summary>
    ModifiedDescending = 1,

    /// <summary>Nombre ascendente.</summary>
    NameAscending = 2,

    /// <summary>Tamaño descendente.</summary>
    SizeDescending = 3,
}

/// <summary>
/// Puerto de persistencia de documentos. La UI nunca accede a archivos o BD directamente.
/// Implementación Fase 0: JSON local (<c>Database/documents.json</c>). Fase 1: SQLite.
/// </summary>
public interface IDocumentRepository
{
    /// <summary>Obtiene un documento por id (<c>null</c> si está en papelera y no se incluye).</summary>
    Task<Result<Document?>> GetByIdAsync(Guid id, bool includeDeleted, CancellationToken ct);

    /// <summary>Lista paginada según criterios.</summary>
    Task<Result<PagedResult<Document>>> ListAsync(DocumentQuery query, CancellationToken ct);

    /// <summary>Cuenta documentos no eliminados.</summary>
    Task<Result<int>> CountActiveAsync(CancellationToken ct);

    /// <summary>Documentos no eliminados con un hash SHA-256 exacto (deduplicación).</summary>
    Task<Result<IReadOnlyList<Document>>> FindByHashAsync(string hash, CancellationToken ct);

    /// <summary>Suma de bytes de documentos no eliminados.</summary>
    Task<Result<long>> SumActiveBytesAsync(CancellationToken ct);

    /// <summary>Añade un documento nuevo.</summary>
    Task<Result> AddAsync(Document document, CancellationToken ct);

    /// <summary>Actualiza un documento existente.</summary>
    Task<Result> UpdateAsync(Document document, CancellationToken ct);

    /// <summary>Elimina definitivamente (la papelera lógica la gestiona la entidad).</summary>
    Task<Result> DeleteAsync(Guid id, CancellationToken ct);
}
