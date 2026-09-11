using CaCo.Core;
using CaCo.Domain;

namespace CaCo.Application.Repositories;

/// <summary>Puerto de persistencia de notas (base Fase 0; edición enriquecida en Fase 4).</summary>
public interface INoteRepository
{
    /// <summary>Obtiene una nota por id.</summary>
    Task<Result<Note?>> GetByIdAsync(Guid id, bool includeDeleted, CancellationToken ct);

    /// <summary>Notas de un documento (activas).</summary>
    Task<Result<IReadOnlyList<Note>>> ListByDocumentAsync(Guid documentId, CancellationToken ct);

    /// <summary>Notas de un cuaderno (activas).</summary>
    Task<Result<IReadOnlyList<Note>>> ListByNotebookAsync(Guid notebookId, CancellationToken ct);

    /// <summary>Añade una nota.</summary>
    Task<Result> AddAsync(Note note, CancellationToken ct);

    /// <summary>Actualiza una nota.</summary>
    Task<Result> UpdateAsync(Note note, CancellationToken ct);

    /// <summary>Elimina definitivamente.</summary>
    Task<Result> DeleteAsync(Guid id, CancellationToken ct);
}

/// <summary>Puerto de persistencia de etiquetas.</summary>
public interface ITagRepository
{
    /// <summary>Todas las etiquetas ordenadas por nombre.</summary>
    Task<Result<IReadOnlyList<Tag>>> ListAllAsync(CancellationToken ct);

    /// <summary>Busca por nombre normalizado.</summary>
    Task<Result<Tag?>> GetByNameAsync(string name, CancellationToken ct);

    /// <summary>Añade una etiqueta.</summary>
    Task<Result> AddAsync(Tag tag, CancellationToken ct);
}
