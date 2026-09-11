using CaCo.Domain;

namespace CaCo.Infrastructure.Persistence;

/// <summary>
/// DTOs de persistencia JSON (Fase 0). Las entidades de dominio no conocen este formato:
/// el mapeo vive aquí y se valida con pruebas. Fase 1 los sustituye SQLite sin tocar el dominio.
/// </summary>
internal sealed class DocumentDto
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string OriginalFileName { get; set; } = string.Empty;

    public string? StoredFileName { get; set; }

    public string? OriginalStoredFileName { get; set; }

    public DocumentType FileType { get; set; }

    public long SizeBytes { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ModifiedAt { get; set; }

    public DateTimeOffset ImportedAt { get; set; }

    public bool IsFavorite { get; set; }

    public bool IsDeleted { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public Guid? NotebookId { get; set; }

    public List<Guid> TagIds { get; set; } = [];

    public Dictionary<string, string> Metadata { get; set; } = new();

    public string? ContentHash { get; set; }

    public bool IsEncrypted { get; set; }

    public static DocumentDto FromEntity(Document doc) => new()
    {
        Id = doc.Id,
        Name = doc.Name,
        OriginalFileName = doc.OriginalFileName,
        StoredFileName = doc.StoredFileName,
        OriginalStoredFileName = doc.OriginalStoredFileName,
        FileType = doc.FileType,
        SizeBytes = doc.SizeBytes,
        CreatedAt = doc.CreatedAt,
        ModifiedAt = doc.ModifiedAt,
        ImportedAt = doc.ImportedAt,
        IsFavorite = doc.IsFavorite,
        IsDeleted = doc.IsDeleted,
        DeletedAt = doc.DeletedAt,
        NotebookId = doc.NotebookId,
        TagIds = [.. doc.TagIds],
        Metadata = new Dictionary<string, string>(doc.Metadata.Values),
        ContentHash = doc.ContentHash,
        IsEncrypted = doc.IsEncrypted,
    };

    public Document ToEntity() => Document.Rehydrate(
        Id, Name, OriginalFileName, StoredFileName, OriginalStoredFileName, FileType, SizeBytes,
        CreatedAt, ModifiedAt, ImportedAt, IsFavorite, IsDeleted, DeletedAt,
        NotebookId, [.. TagIds], new Dictionary<string, string>(Metadata),
        ContentHash, IsEncrypted);
}

/// <summary>DTO de cuaderno.</summary>
internal sealed class NotebookDto
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public Guid? ParentId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ModifiedAt { get; set; }

    public bool IsDeleted { get; set; }

    public static NotebookDto FromEntity(Notebook notebook) => new()
    {
        Id = notebook.Id,
        Name = notebook.Name,
        ParentId = notebook.ParentId,
        CreatedAt = notebook.CreatedAt,
        ModifiedAt = notebook.ModifiedAt,
        IsDeleted = notebook.IsDeleted,
    };

    public Notebook ToEntity() => Notebook.Rehydrate(Id, Name, ParentId, CreatedAt, ModifiedAt, IsDeleted);
}

/// <summary>DTO de nota.</summary>
internal sealed class NoteDto
{
    public Guid Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public Guid? DocumentId { get; set; }

    public Guid? NotebookId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ModifiedAt { get; set; }

    public bool IsDeleted { get; set; }

    public static NoteDto FromEntity(Note note) => new()
    {
        Id = note.Id,
        Title = note.Title,
        Content = note.Content,
        DocumentId = note.DocumentId,
        NotebookId = note.NotebookId,
        CreatedAt = note.CreatedAt,
        ModifiedAt = note.ModifiedAt,
        IsDeleted = note.IsDeleted,
    };

    public Note ToEntity() => Note.Rehydrate(Id, Title, Content, DocumentId, NotebookId, CreatedAt, ModifiedAt, IsDeleted);
}

/// <summary>DTO de etiqueta.</summary>
internal sealed class TagDto
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public static TagDto FromEntity(Tag tag) => new()
    {
        Id = tag.Id,
        Name = tag.Name,
        CreatedAt = tag.CreatedAt,
    };

    public Tag ToEntity() => Tag.Rehydrate(Id, Name, CreatedAt);
}
