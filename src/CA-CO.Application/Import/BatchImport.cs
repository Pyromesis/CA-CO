using CaCo.Core;

namespace CaCo.Application.Import;

/// <summary>Límites de un lote de importación (anti-DoS).</summary>
public sealed record ImportBatchOptions
{
    /// <summary>Máximo de archivos por lote.</summary>
    public int MaxFiles { get; init; } = 100;

    /// <summary>Tamaño total máximo del lote en bytes.</summary>
    public long MaxTotalBytes { get; init; } = 1L * 1024 * 1024 * 1024;

    /// <summary>Opciones por defecto.</summary>
    public static ImportBatchOptions Default => new();
}

/// <summary>Progreso de un lote (para barra + "3/12 · nombre").</summary>
/// <param name="Processed">Archivos ya procesados.</param>
/// <param name="Total">Total del lote.</param>
/// <param name="CurrentName">Nombre del archivo en curso (vacío al terminar).</param>
public sealed record ImportProgress(int Processed, int Total, string CurrentName);

/// <summary>Fallo individual dentro de un lote (no aborta el resto).</summary>
/// <param name="FileName">Nombre del archivo.</param>
/// <param name="Reason">Motivo entendible.</param>
public sealed record ImportFileFailure(string FileName, string Reason);

/// <summary>Informe final de un lote.</summary>
public sealed record ImportBatchResult
{
    /// <summary>Importados con éxito.</summary>
    public required int Imported { get; init; }

    /// <summary>Omitidos por duplicados.</summary>
    public required int Duplicates { get; init; }

    /// <summary>Fallidos (ver <see cref="Failures"/>).</summary>
    public required int Failed { get; init; }

    /// <summary>Detalle por archivo fallido.</summary>
    public required IReadOnlyList<ImportFileFailure> Failures { get; init; }

    /// <summary>Si se canceló a mitad (el resto no se intentó).</summary>
    public required bool WasCancelled { get; init; }

    /// <summary>Si se rechazó el lote entero por límites (nada importado).</summary>
    public required bool RejectedByLimit { get; init; }

    /// <summary>Motivo del rechazo por límites.</summary>
    public string? LimitReason { get; init; }

    /// <summary>Total intentado (importados + duplicados + fallidos).</summary>
    public int Attempted => Imported + Duplicates + Failed;
}

/// <summary>
/// Importación por lotes (Fase 2): valida límites ANTES de empezar, informa
/// progreso, nunca aborta ante un fallo individual y respeta cancelación
/// devolviendo el parcial con <c>WasCancelled</c>.
/// </summary>
public interface IBatchImportService
{
    /// <summary>Importa un lote de rutas al cuaderno indicado.</summary>
    Task<Result<ImportBatchResult>> ImportBatchAsync(
        IEnumerable<string> sourcePaths,
        Guid? notebookId,
        ImportBatchOptions? options = null,
        IProgress<ImportProgress>? progress = null,
        CancellationToken ct = default);
}
