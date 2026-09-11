using CaCo.Core;

namespace CaCo.Domain;

/// <summary>
/// Documento gestionado por CA-CO. Es la entidad central del sistema.
///
/// Distingue entre el <b>archivo original</b> importado por el usuario
/// (<see cref="OriginalFileName"/>) y la <b>copia administrada</b> por CA-CO
/// (<see cref="StoredFileName"/> dentro de la biblioteca).
/// Campos futuros (OCR, hash, cifrado, versiones, nube) se añaden como
/// propiedades opcionales sin romper la API existente.
/// </summary>
public sealed class Document
{
    /// <summary>Máximo permitido para el nombre visible.</summary>
    public const int MaxNameLength = 255;

    /// <summary>Identificador único.</summary>
    public Guid Id { get; private set; }

    /// <summary>Nombre visible en la biblioteca (sin necesidad de incluir extensión).</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Nombre del archivo tal como lo importó el usuario (con extensión).</summary>
    public string OriginalFileName { get; private set; } = string.Empty;

    /// <summary>
    /// Nombre del archivo dentro de la biblioteca (<c>Documents/</c>). Nulo hasta que
    /// el almacenamiento confirme la copia. La ruta absoluta nunca se persiste:
    /// se reconstruye desde <see cref="CaCo.Application.Storage.ILibraryPaths"/>
    /// para que la biblioteca sea reubicable.
    /// </summary>
    public string? StoredFileName { get; private set; }

    /// <summary>
    /// Nombre de la copia exacta del original dentro de <c>Originals/</c>.
    /// Nulo si no se conserva copia (ajuste) o en registros anteriores a Fase 1.
    /// </summary>
    public string? OriginalStoredFileName { get; private set; }

    /// <summary>Tipo de documento inferido de la extensión.</summary>
    public DocumentType FileType { get; private set; } = DocumentType.Unknown;

    /// <summary>Tamaño en bytes del archivo importado.</summary>
    public long SizeBytes { get; private set; }

    /// <summary>Cuándo se creó el archivo original (si se conoce).</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Última modificación conocida.</summary>
    public DateTimeOffset ModifiedAt { get; private set; }

    /// <summary>Cuándo entró en la biblioteca CA-CO.</summary>
    public DateTimeOffset ImportedAt { get; private set; }

    /// <summary>Marcado como favorito.</summary>
    public bool IsFavorite { get; private set; }

    /// <summary>En papelera (borrado lógico).</summary>
    public bool IsDeleted { get; private set; }

    /// <summary>Cuándo se movió a la papelera, si aplica.</summary>
    public DateTimeOffset? DeletedAt { get; private set; }

    /// <summary>Cuaderno contenedor, si está clasificado.</summary>
    public Guid? NotebookId { get; private set; }

    /// <summary>Etiquetas asignadas.</summary>
    public List<Guid> TagIds { get; private set; } = [];

    /// <summary>Metadatos extensibles (punto de extensión para OCR, IA, nube...).</summary>
    public DocumentMetadata Metadata { get; private set; } = new();

    // --- Puntos de extensión para fases futuras (opcionales, no rompen persistencia) ---

    /// <summary>Hash SHA-256 del contenido. Reservado para Fase 1 (deduplicación).</summary>
    public string? ContentHash { get; private set; }

    /// <summary>Si el contenido está cifrado. Reservado para Fase 8.</summary>
    public bool IsEncrypted { get; private set; }

    private Document()
    {
    }

    /// <summary>Crea un documento pendiente de almacenar su copia administrada.</summary>
    public static Result<Document> Create(
        string name,
        string originalFileName,
        long sizeBytes,
        Guid? notebookId = null,
        DateTimeOffset? now = null,
        Guid? id = null)
    {
        var cleanName = DomainErrors.TextSanitizer.Sanitize(name);
        if (string.IsNullOrWhiteSpace(cleanName) || cleanName.Length > MaxNameLength)
        {
            return Result.Failure<Document>(DomainErrors.Document.InvalidName("vacío o demasiado largo"));
        }

        if (string.IsNullOrWhiteSpace(originalFileName))
        {
            return Result.Failure<Document>(DomainErrors.Document.InvalidName("falta el nombre de archivo original"));
        }

        if (sizeBytes < 0)
        {
            return Result.Failure<Document>(DomainErrors.Document.InvalidSize("el tamaño no puede ser negativo"));
        }

        var type = SupportedFileTypes.GetTypeOrUnknown(originalFileName);
        if (type == DocumentType.Unknown)
        {
            return Result.Failure<Document>(
                DomainErrors.Document.UnsupportedType(Path.GetExtension(originalFileName)));
        }

        var timestamp = now ?? DateTimeOffset.UtcNow;
        return new Document
        {
            Id = id ?? Guid.NewGuid(),
            Name = cleanName,
            OriginalFileName = originalFileName.Trim(),
            FileType = type,
            SizeBytes = sizeBytes,
            CreatedAt = timestamp,
            ModifiedAt = timestamp,
            ImportedAt = timestamp,
            NotebookId = notebookId,
        };
    }

    /// <summary>Registra la copia administrada una vez copiada a la biblioteca.</summary>
    public void AttachStoredCopy(string storedFileName, DateTimeOffset? now = null)
    {
        StoredFileName = Guard.NotNullOrWhiteSpace(storedFileName, nameof(storedFileName));
        ModifiedAt = now ?? DateTimeOffset.UtcNow;
    }

    /// <summary>Registra la copia exacta del original (si el ajuste la conserva).</summary>
    public void AttachOriginalCopy(string storedFileName, DateTimeOffset? now = null)
    {
        OriginalStoredFileName = Guard.NotNullOrWhiteSpace(storedFileName, nameof(storedFileName));
        ModifiedAt = now ?? DateTimeOffset.UtcNow;
    }

    /// <summary>Registra el hash SHA-256 del contenido (deduplicación desde Fase 1).</summary>
    public void SetContentHash(string hash, DateTimeOffset? now = null)
    {
        ContentHash = Guard.NotNullOrWhiteSpace(hash, nameof(hash));
        ModifiedAt = now ?? DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Actualiza tamaño y hash tras modificar el contenido administrado
    /// (edición de texto, Fase 3). Mantiene la entidad coherente con el archivo.
    /// </summary>
    public Result RefreshSizeAndHash(long sizeBytes, string contentHash, DateTimeOffset? now = null)
    {
        if (sizeBytes < 0)
        {
            return Result.Failure(DomainErrors.Document.InvalidSize("el tamaño no puede ser negativo"));
        }

        if (string.IsNullOrWhiteSpace(contentHash))
        {
            return Result.Failure(DomainErrors.Document.InvalidSize("falta el hash del contenido"));
        }

        SizeBytes = sizeBytes;
        ContentHash = contentHash.Trim();
        ModifiedAt = now ?? DateTimeOffset.UtcNow;
        return Result.Success();
    }

    /// <summary>Cambia el nombre visible.</summary>
    public Result Rename(string newName, DateTimeOffset? now = null)
    {
        var clean = DomainErrors.TextSanitizer.Sanitize(newName);
        if (string.IsNullOrWhiteSpace(clean) || clean.Length > MaxNameLength)
        {
            return Result.Failure(DomainErrors.Document.InvalidName("vacío o demasiado largo"));
        }

        Name = clean;
        ModifiedAt = now ?? DateTimeOffset.UtcNow;
        return Result.Success();
    }

    /// <summary>Marca o desmarca como favorito.</summary>
    public void SetFavorite(bool isFavorite, DateTimeOffset? now = null)
    {
        IsFavorite = isFavorite;
        ModifiedAt = now ?? DateTimeOffset.UtcNow;
    }

    /// <summary>Mueve a la papelera (borrado lógico).</summary>
    public Result MoveToTrash(DateTimeOffset? now = null)
    {
        if (IsDeleted)
        {
            return Result.Failure(DomainErrors.Document.AlreadyInState("papelera"));
        }

        IsDeleted = true;
        DeletedAt = now ?? DateTimeOffset.UtcNow;
        ModifiedAt = DeletedAt.Value;
        return Result.Success();
    }

    /// <summary>Restaura desde la papelera.</summary>
    public Result Restore(DateTimeOffset? now = null)
    {
        if (!IsDeleted)
        {
            return Result.Failure(DomainErrors.Document.AlreadyInState("activo"));
        }

        IsDeleted = false;
        DeletedAt = null;
        ModifiedAt = now ?? DateTimeOffset.UtcNow;
        return Result.Success();
    }

    /// <summary>Clasifica en un cuaderno (o lo deja sin clasificar con <c>null</c>).</summary>
    public void MoveToNotebook(Guid? notebookId, DateTimeOffset? now = null)
    {
        if (notebookId == Guid.Empty)
        {
            throw new ArgumentException("El cuaderno no puede ser Guid.Empty; usa null para desclasificar.", nameof(notebookId));
        }

        NotebookId = notebookId;
        ModifiedAt = now ?? DateTimeOffset.UtcNow;
    }

    /// <summary>Asigna una etiqueta (idempotente).</summary>
    public void AddTag(Guid tagId)
    {
        if (tagId == Guid.Empty)
        {
            throw new ArgumentException("La etiqueta no puede ser Guid.Empty.", nameof(tagId));
        }

        if (!TagIds.Contains(tagId))
        {
            TagIds.Add(tagId);
        }
    }

    /// <summary>Quita una etiqueta.</summary>
    public void RemoveTag(Guid tagId) => TagIds.Remove(tagId);

    /// <summary>
    /// Reconstruye una entidad desde persistencia. Solo lo usa Infrastructure;
    /// el resto del código crea documentos con <see cref="Create"/>.
    /// </summary>
    internal static Document Rehydrate(
        Guid id,
        string name,
        string originalFileName,
        string? storedFileName,
        string? originalStoredFileName,
        DocumentType fileType,
        long sizeBytes,
        DateTimeOffset createdAt,
        DateTimeOffset modifiedAt,
        DateTimeOffset importedAt,
        bool isFavorite,
        bool isDeleted,
        DateTimeOffset? deletedAt,
        Guid? notebookId,
        List<Guid> tagIds,
        Dictionary<string, string> metadata,
        string? contentHash,
        bool isEncrypted)
    {
        var safeTags = (tagIds ?? []).Where(t => t != Guid.Empty).Distinct().ToList();
        return new Document
        {
            Id = id == Guid.Empty ? Guid.NewGuid() : id,
            Name = name,
            OriginalFileName = originalFileName,
            StoredFileName = storedFileName,
            OriginalStoredFileName = originalStoredFileName,
            FileType = Enum.IsDefined(fileType) ? fileType : DocumentType.Unknown,
            SizeBytes = Math.Max(0, sizeBytes),
            CreatedAt = createdAt,
            ModifiedAt = modifiedAt,
            ImportedAt = importedAt,
            IsFavorite = isFavorite,
            IsDeleted = isDeleted,
            DeletedAt = isDeleted ? deletedAt : null,
            NotebookId = notebookId == Guid.Empty ? null : notebookId,
            TagIds = safeTags,
            Metadata = RehydrateMetadata(metadata),
            ContentHash = contentHash,
            IsEncrypted = isEncrypted,
        };
    }

    private static DocumentMetadata RehydrateMetadata(Dictionary<string, string>? values)
    {
        var metadata = new DocumentMetadata();
        if (values is null)
        {
            return metadata;
        }

        foreach (var (key, value) in values)
        {
            try
            {
                metadata.Set(key, value);
            }
            catch (Exception)
            {
                // Fila corrupta: se omite la clave inválida en vez de tumbar la lectura.
            }
        }

        return metadata;
    }
}
