using CaCo.Core;

namespace CaCo.Domain;

/// <summary>Etiqueta reutilizable para clasificar documentos.</summary>
public sealed class Tag
{
    /// <summary>Máximo permitido para el nombre.</summary>
    public const int MaxNameLength = 64;

    /// <summary>Identificador único.</summary>
    public Guid Id { get; private set; }

    /// <summary>Nombre normalizado (minúsculas, sin espacios externos).</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Creación.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    private Tag()
    {
    }

    /// <summary>Crea una etiqueta normalizando el nombre.</summary>
    public static Result<Tag> Create(string name, DateTimeOffset? now = null, Guid? id = null)
    {
        var clean = DomainErrors.TextSanitizer.Sanitize(name);
        if (string.IsNullOrWhiteSpace(clean) || clean.Length > MaxNameLength)
        {
            return Result.Failure<Tag>(DomainErrors.Tag.InvalidName());
        }

        return new Tag
        {
            Id = id ?? Guid.NewGuid(),
            Name = clean.ToLowerInvariant(),
            CreatedAt = now ?? DateTimeOffset.UtcNow,
        };
    }

    /// <summary>
    /// Reconstruye una entidad desde persistencia. Solo lo usa Infrastructure.
    /// </summary>
    internal static Tag Rehydrate(Guid id, string name, DateTimeOffset createdAt)
    {
        return new Tag
        {
            Id = id == Guid.Empty ? Guid.NewGuid() : id,
            Name = name,
            CreatedAt = createdAt,
        };
    }
}
