using CaCo.Core;

namespace CaCo.Application.Updates;

/// <summary>
/// Actualizaciones desde GitHub Releases: comprobar, descargar y verificar
/// el instalador. El arranque de la instalación y el cierre de la app los
/// orquesta el ViewModel (capa UI).
/// </summary>
public interface IUpdateService
{
    /// <summary>Comprueba el último release contra la versión indicada.</summary>
    Task<UpdateCheckResult> CheckForUpdatesAsync(Version current, CancellationToken ct);

    /// <summary>Descarga el instalador a la carpeta temporal (progreso 0-100).</summary>
    /// <returns>Ruta local del instalador descargado.</returns>
    Task<Result<string>> DownloadAsync(UpdateRelease release, IProgress<double> progress, CancellationToken ct);

    /// <summary>Verifica que el instalador tenga firma Authenticode de CA-CO.</summary>
    Result VerifyInstaller(string path);

    /// <summary>Lanza el instalador en silencioso con reapertura al terminar.</summary>
    bool LaunchInstaller(string path);
}
