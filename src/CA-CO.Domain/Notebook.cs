using CaCo.Core;

namespace CaCo.Domain;

/// <summary>
/// Cuaderno/carpeta de la jerarquía personal. Los cuadernos forman un árbol
/// (<see cref="ParentId"/> nulo = raíz). La invariante "sin ciclos" la garantiza
/// <c>INotebookService</c>; la entidad impide el auto-parentesco directo.
/// </summary>
public sealed class Notebook
{
    /// <summary>Máximo permitido para el nombre.</summary>
    public const int MaxNameLength = 128;

    /// <summary>Identificador único.</summary>
    public Guid Id { get; private set; }

    /// <summary>Nombre visible.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Cuaderno padre. Nulo = cuaderno raíz.</summary>
    public Guid? ParentId { get; private set; }

    /// <summary>Creación.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Última modificación.</summary>
    public DateTimeOffset ModifiedAt { get; private set; }

    /// <summary>Borrado lógico.</summary>
    public bool IsDeleted { get; private set; }

    private Notebook()
    {
    }

    /// <summary>Crea un cuaderno raíz o hijo.</summary>
    public static Result<Notebook> Create(
        string name,
        Guid? parentId = null,
        DateTimeOffset? now = null,
        Guid? id = null)
    {
        var clean = DomainErrors.TextSanitizer.Sanitize(name);
        if (string.IsNullOrWhiteSpace(clean) || clean.Length > MaxNameLength)
        {
            return Result.Failure<Notebook>(DomainErrors.Notebook.InvalidName("vacío o demasiado largo"));
        }

        var candidateId = id ?? Guid.NewGuid();
        if (parentId == candidateId)
        {
            return Result.Failure<Notebook>(DomainErrors.Notebook.SelfParent());
        }

        var timestamp = now ?? DateTimeOffset.UtcNow;
        return new Notebook
        {
            Id = candidateId,
            Name = clean,
            ParentId = parentId,
            CreatedAt = timestamp,
            ModifiedAt = timestamp,
        };
    }

    /// <summary>Cambia el nombre.</summary>
    public Result Rename(string newName, DateTimeOffset? now = null)
    {
        var clean = DomainErrors.TextSanitizer.Sanitize(newName);
        if (string.IsNullOrWhiteSpace(clean) || clean.Length > MaxNameLength)
        {
            return Result.Failure(DomainErrors.Notebook.InvalidName("vacío o demasiado largo"));
        }

        Name = clean;
        ModifiedAt = now ?? DateTimeOffset.UtcNow;
        return Result.Success();
    }

    /// <summary>Cambia el padre (mover en el árbol). Impide el auto-parentesco.</summary>
    public Result MoveTo(Guid? newParentId, DateTimeOffset? now = null)
    {
        if (newParentId == Id)
        {
            return Result.Failure(DomainErrors.Notebook.SelfParent());
        }

        if (newParentId == Guid.Empty)
        {
            return Result.Failure(DomainErrors.Notebook.InvalidName("el padre no puede ser Guid.Empty"));
        }

        ParentId = newParentId;
        ModifiedAt = now ?? DateTimeOffset.UtcNow;
        return Result.Success();
    }

    /// <summary>Marca borrado lógico (idempotente: no toca ModifiedAt si ya estaba borrado).</summary>
    public void MarkDeleted(DateTimeOffset? now = null)
    {
        if (IsDeleted)
        {
            return;
        }

        IsDeleted = true;
        ModifiedAt = now ?? DateTimeOffset.UtcNow;
    }

    /// <summary>Indica si es raíz.</summary>
    public bool IsRoot => ParentId is null;

    /// <summary>
    /// Reconstruye una entidad desde persistencia. Solo lo usa Infrastructure.
    /// </summary>
    internal static Notebook Rehydrate(
        Guid id,
        string name,
        Guid? parentId,
        DateTimeOffset createdAt,
        DateTimeOffset modifiedAt,
        bool isDeleted)
    {
        return new Notebook
        {
            Id = id == Guid.Empty ? Guid.NewGuid() : id,
            Name = name,
            ParentId = parentId == Guid.Empty ? null : parentId,
            CreatedAt = createdAt,
            ModifiedAt = modifiedAt,
            IsDeleted = isDeleted,
        };
    }
}
