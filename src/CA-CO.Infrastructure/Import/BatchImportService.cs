using CaCo.Application.Errors;
using CaCo.Application.Import;
using CaCo.Core;
using Microsoft.Extensions.Logging;

namespace CaCo.Infrastructure.Import;

/// <summary>Implementación de <see cref="IBatchImportService"/> sobre <see cref="IDocumentImporter"/>.</summary>
public sealed class BatchImportService(
    IDocumentImporter importer,
    IErrorHandler errors,
    ILogger<BatchImportService> logger) : IBatchImportService
{
    /// <inheritdoc/>
    public async Task<Result<ImportBatchResult>> ImportBatchAsync(
        IEnumerable<string> sourcePaths,
        Guid? notebookId,
        ImportBatchOptions? options = null,
        IProgress<ImportProgress>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sourcePaths);
        var settings = options ?? ImportBatchOptions.Default;

        var paths = sourcePaths
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (paths.Count == 0)
        {
            return Result.Success(EmptyResult());
        }

        if (paths.Count > settings.MaxFiles)
        {
            return Result.Success(EmptyResult(
                rejected: true,
                reason: $"Demasiados archivos ({paths.Count}, máximo {settings.MaxFiles}). Importa en tandas más pequeñas o por carpetas."));
        }

        long totalBytes = 0;
        foreach (var path in paths)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                totalBytes += new FileInfo(path).Exists ? new FileInfo(path).Length : 0;
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "No se pudo medir {Path} para el límite del lote.", Path.GetFileName(path));
            }

            if (totalBytes > settings.MaxTotalBytes)
            {
                return Result.Success(EmptyResult(
                    rejected: true,
                    reason: $"El lote supera el máximo de {settings.MaxTotalBytes / (1024 * 1024)} MB."));
            }
        }

        var imported = 0;
        var duplicates = 0;
        var failures = new List<ImportFileFailure>();
        var cancelled = false;
        var processed = 0;

        foreach (var path in paths)
        {
            if (ct.IsCancellationRequested)
            {
                cancelled = true;
                break;
            }

            var name = Path.GetFileName(path);
            progress?.Report(new ImportProgress(processed, paths.Count, name));
            try
            {
                var result = await importer.ImportAsync(new ImportRequest(path, notebookId), ct).ConfigureAwait(false);
                if (result.IsFailure)
                {
                    failures.Add(new ImportFileFailure(name, errors.FromError(result.Error).Message));
                }
                else if (result.Value.Succeeded)
                {
                    imported++;
                }
                else if (result.Value.SkippedAsDuplicate)
                {
                    duplicates++;
                }
                else if (result.Value.Error is not null)
                {
                    failures.Add(new ImportFileFailure(name, errors.FromError(result.Value.Error).Message));
                }
                else
                {
                    failures.Add(new ImportFileFailure(name, "No se pudo importar."));
                }
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Fallo inesperado importando {Path}.", name);
                failures.Add(new ImportFileFailure(name, errors.FromException(ex).Message));
            }

            processed++;
            progress?.Report(new ImportProgress(processed, paths.Count, string.Empty));
        }

        logger.LogInformation(
            "Lote: {Imported} importados, {Duplicates} duplicados, {Failed} fallidos, cancelado={Cancelled}.",
            imported, duplicates, failures.Count, cancelled);
        return Result.Success(new ImportBatchResult
        {
            Imported = imported,
            Duplicates = duplicates,
            Failed = failures.Count,
            Failures = failures,
            WasCancelled = cancelled,
            RejectedByLimit = false,
        });
    }

    private static ImportBatchResult EmptyResult(bool rejected = false, string? reason = null) =>
        new()
        {
            Imported = 0,
            Duplicates = 0,
            Failed = 0,
            Failures = [],
            WasCancelled = false,
            RejectedByLimit = rejected,
            LimitReason = reason,
        };
}
