using CaCo.Core;

namespace CaCo.Application.Updates;

/// <summary>Versión publicada en un release de GitHub.</summary>
/// <param name="Tag">Etiqueta del release (p. ej. "v1.1.0").</param>
/// <param name="Version">Versión parseada para comparar.</param>
/// <param name="AssetName">Nombre del instalador (p. ej. "CA-CO-Setup-1.1.0-x64.exe").</param>
/// <param name="DownloadUrl">URL directa del instalador.</param>
/// <param name="SizeBytes">Tamaño declarado (0 si se desconoce).</param>
/// <param name="Notes">Notas del release (pueden ir vacías).</param>
public sealed record UpdateRelease(
    string Tag,
    Version Version,
    string AssetName,
    Uri DownloadUrl,
    long SizeBytes,
    string Notes);

/// <summary>Resultado de comprobar actualizaciones (nunca lanza).</summary>
/// <param name="Succeeded">Si la comprobación llegó a GitHub.</param>
/// <param name="UpdateAvailable">Si hay versión strictly mayor instalada.</param>
/// <param name="Release">Release encontrado (null si no hay o falló).</param>
/// <param name="CurrentVersion">Versión en ejecución.</param>
/// <param name="Error">Error si <c>Succeeded</c> es falso.</param>
public sealed record UpdateCheckResult(
    bool Succeeded,
    bool UpdateAvailable,
    UpdateRelease? Release,
    Version CurrentVersion,
    Error? Error);
