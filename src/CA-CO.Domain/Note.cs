using CaCo.Core;

namespace CaCo.Domain;

/// <summary>
/// Nota del usuario (Fase 0: modelo base; edición enriquecida en Fase 4).
/// Puede vivir suelta, vinculada a un documento o a un cuaderno.
/// </summary>
public sealed class Note
{
    /// <summary>Máximo permitido para el título.</summary>
    public const int MaxTitleLength = 200;

    /// <summary>Máximo permitido para el contenido (evita DoS en disco/RAM).</summary>
    public const int MaxContentLength = 100_000;

    /// <summary>Identificador único.</summary>
    public Guid Id { get; private set; }

    /// <summary>Título.</summary>
    public string Title { get; private set; } = string.Empty;

    /// <summary>Contenido en texto plano (Markdown en Fase 4).</summary>
    public string Content { get; private set; } = string.Empty;

    /// <summary>Documento asociado, si aplica.</summary>
    public Guid? DocumentId { get; private set; }

    /// <summary>Cuaderno asociado, si aplica.</summary>
    public Guid? NotebookId { get; private set; }

    /// <summary>Creación.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Última modificación.</summary>
    public DateTimeOffset ModifiedAt { get; private set; }

    /// <summary>Borrado lógico.</summary>
    public bool IsDeleted { get; private set; }

    private Note()
    {
    }

    /// <summary>Crea una nota.</summary>
    public static Result<Note> Create(
        string title,
        string content = "",
        Guid? documentId = null,
        Guid? notebookId = null,
        DateTimeOffset? now = null,
        Guid? id = null)
    {
        var cleanTitle = DomainErrors.TextSanitizer.Sanitize(title);
        if (string.IsNullOrWhiteSpace(cleanTitle) || cleanTitle.Length > MaxTitleLength)
        {
            return Result.Failure<Note>(
                Error.Validation("Note.InvalidTitle", "El título de la nota no es válido."));
        }

        if ((content?.Length ?? 0) > MaxContentLength)
        {
            return Result.Failure<Note>(
                Error.Validation("Note.ContentTooLong", $"El contenido no puede superar {MaxContentLength} caracteres."));
        }

        var timestamp = now ?? DateTimeOffset.UtcNow;
        return new Note
        {
            Id = id ?? Guid.NewGuid(),
            Title = cleanTitle,
            Content = content ?? string.Empty,
            DocumentId = documentId == Guid.Empty ? null : documentId,
            NotebookId = notebookId == Guid.Empty ? null : notebookId,
            CreatedAt = timestamp,
            ModifiedAt = timestamp,
        };
    }

    /// <summary>Actualiza título y/o contenido.</summary>
    public Result Update(string title, string content, DateTimeOffset? now = null)
    {
        var cleanTitle = DomainErrors.TextSanitizer.Sanitize(title);
        if (string.IsNullOrWhiteSpace(cleanTitle) || cleanTitle.Length > MaxTitleLength)
        {
            return Result.Failure(
                Error.Validation("Note.InvalidTitle", "El título de la nota no es válido."));
        }

        if ((content?.Length ?? 0) > MaxContentLength)
        {
            return Result.Failure(
                Error.Validation("Note.ContentTooLong", $"El contenido no puede superar {MaxContentLength} caracteres."));
        }

        Title = cleanTitle;
        Content = content ?? string.Empty;
        ModifiedAt = now ?? DateTimeOffset.UtcNow;
        return Result.Success();
    }

    /// <summary>Marca borrado lógico (idempotente).</summary>
    public void MarkDeleted(DateTimeOffset? now = null)
    {
        if (IsDeleted)
        {
            return;
        }

        IsDeleted = true;
        ModifiedAt = now ?? DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Reconstruye una entidad desde persistencia. Solo lo usa Infrastructure.
    /// </summary>
    internal static Note Rehydrate(
        Guid id,
        string title,
        string content,
        Guid? documentId,
        Guid? notebookId,
        DateTimeOffset createdAt,
        DateTimeOffset modifiedAt,
        bool isDeleted)
    {
        return new Note
        {
            Id = id,
            Title = title,
            Content = content,
            DocumentId = documentId,
            NotebookId = notebookId,
            CreatedAt = createdAt,
            ModifiedAt = modifiedAt,
            IsDeleted = isDeleted,
        };
    }
}
