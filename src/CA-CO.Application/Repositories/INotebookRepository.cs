using CaCo.Core;
using CaCo.Domain;

namespace CaCo.Application.Repositories;

/// <summary>Criterios de consulta de cuadernos.</summary>
public sealed record NotebookQuery
{
    /// <summary>Solo hijos directos de este padre (<c>null</c> + <see cref="RootsOnly"/> = raíces).</summary>
    public Guid? ParentId { get; init; }

    /// <summary>Solo cuadernos raíz.</summary>
    public bool RootsOnly { get; init; }

    /// <summary>Incluir eliminados.</summary>
    public bool IncludeDeleted { get; init; }

    /// <summary>Filtro por texto en el nombre.</summary>
    public string? SearchText { get; init; }

    /// <summary>Página base 1.</summary>
    public int Page { get; init; } = 1;

    /// <summary>Tamaño de página (1-200).</summary>
    public int PageSize { get; init; } = 100;
}

/// <summary>Puerto de persistencia de cuadernos.</summary>
public interface INotebookRepository
{
    /// <summary>Obtiene un cuaderno por id.</summary>
    Task<Result<Notebook?>> GetByIdAsync(Guid id, bool includeDeleted, CancellationToken ct);

    /// <summary>Lista paginada según criterios.</summary>
    Task<Result<PagedResult<Notebook>>> ListAsync(NotebookQuery query, CancellationToken ct);

    /// <summary>Todos los cuadernos activos (para construir el árbol; los cuadernos son pocos).</summary>
    Task<Result<IReadOnlyList<Notebook>>> ListAllActiveAsync(CancellationToken ct);

    /// <summary>Cuenta cuadernos no eliminados.</summary>
    Task<Result<int>> CountActiveAsync(CancellationToken ct);

    /// <summary>Añade un cuaderno nuevo.</summary>
    Task<Result> AddAsync(Notebook notebook, CancellationToken ct);

    /// <summary>Actualiza un cuaderno existente.</summary>
    Task<Result> UpdateAsync(Notebook notebook, CancellationToken ct);

    /// <summary>Elimina definitivamente.</summary>
    Task<Result> DeleteAsync(Guid id, CancellationToken ct);
}
