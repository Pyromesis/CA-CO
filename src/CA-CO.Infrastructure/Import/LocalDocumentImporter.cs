using System.Security.Cryptography;
using CaCo.Application.Configuration;
using CaCo.Application.Import;
using CaCo.Application.Repositories;
using CaCo.Application.Storage;
using CaCo.Core;
using CaCo.Domain;
using Microsoft.Extensions.Logging;

namespace CaCo.Infrastructure.Import;

/// <summary>Valida archivos candidatos a importar.</summary>
public sealed class FileTypeValidator : IFileTypeValidator
{
    /// <summary>Tamaño máximo importable (200 MB, evita DoS por disco).</summary>
    public const long MaxFileBytes = 200L * 1024 * 1024;

    /// <inheritdoc/>
    public FileValidationResult Validate(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return new FileValidationResult
            {
                IsValid = false,
                DetectedType = DocumentType.Unknown,
                Reason = "Ruta vacía.",
            };
        }

        FileInfo info;
        try
        {
            info = new FileInfo(path);
            if (!info.Exists)
            {
                return new FileValidationResult
                {
                    IsValid = false,
                    DetectedType = DocumentType.Unknown,
                    Reason = "El archivo no existe.",
                };
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new FileValidationResult
            {
                IsValid = false,
                DetectedType = DocumentType.Unknown,
                Reason = "Ruta no válida.",
            };
        }

        var type = SupportedFileTypes.GetTypeOrUnknown(path);
        if (type == DocumentType.Unknown)
        {
            return new FileValidationResult
            {
                IsValid = false,
                DetectedType = DocumentType.Unknown,
                Reason = $"Extensión no soportada: '{Path.GetExtension(path)}'.",
            };
        }

        if (info.Length == 0)
        {
            return new FileValidationResult
            {
                IsValid = false,
                DetectedType = type,
                Reason = "El archivo está vacío.",
            };
        }

        if (info.Length > MaxFileBytes)
        {
            return new FileValidationResult
            {
                IsValid = false,
                DetectedType = type,
                Reason = $"El archivo supera el máximo de {MaxFileBytes / (1024 * 1024)} MB.",
            };
        }

        if (!HasExpectedSignature(path, type))
        {
            return new FileValidationResult
            {
                IsValid = false,
                DetectedType = DocumentType.Unknown,
                Reason = "El contenido no coincide con su extensión.",
            };
        }

        return new FileValidationResult { IsValid = true, DetectedType = type };
    }

    private static bool HasExpectedSignature(string path, DocumentType type)
    {
        try
        {
            Span<byte> header = stackalloc byte[8];
            using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096,
                FileOptions.SequentialScan);
            var read = stream.Read(header);
            if (read < 1)
            {
                return false;
            }

            return type switch
            {
                DocumentType.Pdf => read >= 4 && header[0] == (byte)'%' && header[1] == (byte)'P'
                    && header[2] == (byte)'D' && header[3] == (byte)'F',
                DocumentType.Png => read >= 8 && header[0] == 0x89 && header[1] == 0x50
                    && header[2] == 0x4E && header[3] == 0x47,
                DocumentType.Jpg or DocumentType.Jpeg => read >= 2 && header[0] == 0xFF && header[1] == 0xD8,
                DocumentType.Docx or DocumentType.Xlsx => read >= 4 && header[0] == 0x50
                    && header[1] == 0x4B && header[2] == 0x03 && header[3] == 0x04,
                DocumentType.Txt => true,
                _ => false,
            };
        }
        catch (Exception)
        {
            return false;
        }
    }
}

/// <summary>
/// Importador local (Fase 0): valida → copia original → copia administrada → persiste.
/// Sin telemetría y sin red: todo ocurre en el equipo.
/// </summary>
public sealed class LocalDocumentImporter(
    IFileTypeValidator validator,
    IFileStorage storage,
    ILibraryPaths paths,
    IDocumentRepository documents,
    INotebookRepository notebooks,
    IThumbnailService thumbnails,
    IMediaInspector inspector,
    CacoSettings settings,
    IClock clock,
    ILogger<LocalDocumentImporter> logger) : IDocumentImporter
{
    /// <inheritdoc/>
    public async Task<Result<ImportResult>> ImportAsync(ImportRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var fileName = Path.GetFileName(request.SourcePath);

        var validation = validator.Validate(request.SourcePath);
        if (!validation.IsValid)
        {
            return Result.Success(new ImportResult
            {
                Succeeded = false,
                SourcePath = request.SourcePath,
                Error = DomainErrors.Document.UnsupportedType(validation.Reason ?? "?"),
            });
        }

        FileInfo info;
        string contentHash;
        try
        {
            info = new FileInfo(request.SourcePath);
            await using var hashStream = File.OpenRead(request.SourcePath);
            var hashBytes = await SHA256.HashDataAsync(hashStream, ct).ConfigureAwait(false);
            contentHash = Convert.ToHexString(hashBytes);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Result.Success(new ImportResult
            {
                Succeeded = false,
                SourcePath = request.SourcePath,
                Error = Error.Storage("Import.FileInfoFailed", $"No se pudo leer el archivo: {ex.Message}"),
            });
        }

        if (request.NotebookId.HasValue)
        {
            var parent = await notebooks.GetByIdAsync(request.NotebookId.Value, false, ct).ConfigureAwait(false);
            if (parent.IsFailure || parent.Value is null)
            {
                return Result.Success(new ImportResult
                {
                    Succeeded = false,
                    SourcePath = request.SourcePath,
                    Error = parent.IsFailure ? parent.Error : DomainErrors.Notebook.NotFound(request.NotebookId.Value),
                });
            }
        }

        // Detección de duplicados: hash SHA-256 exacto primero; nombre+tamaño
        // como herencia para archivos importados antes de Fase 1 (sin hash).
        var byHash = await documents.FindByHashAsync(contentHash, ct).ConfigureAwait(false);
        if (byHash.IsSuccess && byHash.Value.Any(d => d.NotebookId == request.NotebookId))
        {
            logger.LogInformation("Importación omitida por duplicada (hash): {FileName}", fileName);
            return Result.Success(new ImportResult
            {
                Succeeded = false,
                SourcePath = request.SourcePath,
                SkippedAsDuplicate = true,
                Error = Error.Conflict("Import.Duplicate", "El documento ya está en la biblioteca."),
            });
        }

        var existing = await documents.ListAsync(
            new DocumentQuery { Page = 1, PageSize = 200, SearchText = Path.GetFileNameWithoutExtension(fileName) },
            ct).ConfigureAwait(false);
        if (existing.IsSuccess && existing.Value.Items.Any(d =>
                !d.IsDeleted
                && d.NotebookId == request.NotebookId
                && d.ContentHash is null
                && string.Equals(d.OriginalFileName, fileName, StringComparison.OrdinalIgnoreCase)
                && d.SizeBytes == info.Length))
        {
            logger.LogInformation("Importación omitida por duplicada: {FileName}", fileName);
            return Result.Success(new ImportResult
            {
                Succeeded = false,
                SourcePath = request.SourcePath,
                SkippedAsDuplicate = true,
                Error = Error.Conflict("Import.Duplicate", "El documento ya está en la biblioteca."),
            });
        }

        var displayName = string.IsNullOrWhiteSpace(request.PreferredName)
            ? Path.GetFileNameWithoutExtension(fileName)
            : request.PreferredName.Trim();

        var created = Document.Create(displayName, fileName, info.Length, request.NotebookId, clock.UtcNow);
        if (created.IsFailure)
        {
            return Result.Success(new ImportResult
            {
                Succeeded = false,
                SourcePath = request.SourcePath,
                Error = created.Error,
            });
        }

        var document = created.Value;
        string? storedCopy = null;
        string? originalCopy = null;
        try
        {
            document.SetContentHash(contentHash, clock.UtcNow);

            if (settings.Storage.KeepOriginalCopy)
            {
                originalCopy = await storage.CopyIntoLibraryAsync(request.SourcePath, paths.Originals, ct).ConfigureAwait(false);
                document.AttachOriginalCopy(originalCopy, clock.UtcNow);
            }

            storedCopy = await storage.CopyIntoLibraryAsync(request.SourcePath, paths.Documents, ct).ConfigureAwait(false);
            document.AttachStoredCopy(storedCopy, clock.UtcNow);

            // Metadatos multimedia (Fase 3, best-effort: nunca tumban la importación).
            try
            {
                var media = await inspector.InspectAsync(
                    Path.Combine(paths.Documents, storedCopy), document.FileType, ct).ConfigureAwait(false);
                if (media.IsSuccess && media.Value is not null)
                {
                    ApplyMediaMetadata(document, media.Value);
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Sin metadatos multimedia para {FileName}.", fileName);
            }

            var added = await documents.AddAsync(document, ct).ConfigureAwait(false);
            if (added.IsFailure)
            {
                await TryCleanupAsync(storedCopy, originalCopy, ct).ConfigureAwait(false);
                return Result.Success(new ImportResult
                {
                    Succeeded = false,
                    SourcePath = request.SourcePath,
                    Error = added.Error,
                });
            }

            try
            {
                await thumbnails.EnsureThumbnailAsync(
                    document.Id,
                    Path.Combine(paths.Documents, storedCopy),
                    document.FileType,
                    ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // La miniatura es accesoria: nunca tumba una importación.
                logger.LogWarning(ex, "No se pudo generar la miniatura de {FileName}", fileName);
            }

            logger.LogInformation("Documento importado: {Name} ({FileType})", document.Name, document.FileType);
            return Result.Success(new ImportResult
            {
                Succeeded = true,
                Document = document,
                SourcePath = request.SourcePath,
            });
        }
        catch (OperationCanceledException)
        {
            await TryCleanupAsync(storedCopy, originalCopy, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            await TryCleanupAsync(storedCopy, originalCopy, CancellationToken.None).ConfigureAwait(false);
            logger.LogError(ex, "Fallo al importar {FileName}", fileName);
            return Result.Success(new ImportResult
            {
                Succeeded = false,
                SourcePath = request.SourcePath,
                Error = Error.Storage("Import.Failed", $"No se pudo importar el archivo: {ex.Message}"),
            });
        }
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<ImportResult> ImportManyAsync(
        IEnumerable<ImportRequest> requests,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var request in requests)
        {
            ct.ThrowIfCancellationRequested();
            var result = await ImportAsync(request, ct).ConfigureAwait(false);
            yield return result.IsSuccess
                ? result.Value
                : new ImportResult { Succeeded = false, SourcePath = request.SourcePath, Error = result.Error };
        }
    }

    private static void ApplyMediaMetadata(Document document, MediaInfo media)
    {
        try
        {
            if (document.FileType == DocumentType.Pdf && media.PageCount is > 0)
            {
                document.Metadata.Set(MediaMetadataKeys.PdfPageCount, media.PageCount.Value.ToString());
            }

            if (document.FileType is DocumentType.Png or DocumentType.Jpg or DocumentType.Jpeg)
            {
                if (media.Width is > 0)
                {
                    document.Metadata.Set(MediaMetadataKeys.ImageWidth, media.Width.Value.ToString());
                }

                if (media.Height is > 0)
                {
                    document.Metadata.Set(MediaMetadataKeys.ImageHeight, media.Height.Value.ToString());
                }
            }
        }
        catch (Exception)
        {
            // Metadatos accesorios: se omiten sin tumbar la importación.
        }
    }

    private async Task TryCleanupAsync(string? storedCopy, string? originalCopy, CancellationToken ct)
    {
        foreach (var (folder, name) in new[] { (paths.Documents, storedCopy), (paths.Originals, originalCopy) })
        {
            if (name is null)
            {
                continue;
            }

            try
            {
                await storage.DeleteAsync(folder, name, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "No se pudo limpiar la copia parcial {StoredCopy}", name);
            }
        }
    }
}
