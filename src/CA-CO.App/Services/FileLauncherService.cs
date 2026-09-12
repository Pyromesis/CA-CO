using Microsoft.Extensions.Logging;
using Windows.Storage;
using Windows.System;

namespace CaCo.App.Services;

/// <summary>Apertura de archivos y carpetas con las apps del sistema.</summary>
public interface IFileLauncherService
{
    /// <summary>Abre un archivo con su app predeterminada.</summary>
    Task<bool> OpenFileAsync(string path, CancellationToken ct);

    /// <summary>Muestra un archivo o carpeta en el Explorador.</summary>
    Task<bool> ShowInFolderAsync(string path, CancellationToken ct);

    /// <summary>Abre una URI del sistema (p. ej. ms-settings:regionlanguage).</summary>
    Task<bool> LaunchUriAsync(Uri uri);
}

/// <summary>Implementación con <c>Windows.System.Launcher</c> (respeta al usuario, sin rutas cableadas).</summary>
public sealed class FileLauncherService(ILogger<FileLauncherService> logger) : IFileLauncherService
{
    /// <inheritdoc/>
    public async Task<bool> OpenFileAsync(string path, CancellationToken ct)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path).AsTask(ct);
            return await Launcher.LaunchFileAsync(file);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "No se pudo abrir el archivo.");
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> ShowInFolderAsync(string path, CancellationToken ct)
    {
        try
        {
            if (File.Exists(path))
            {
                var file = await StorageFile.GetFileFromPathAsync(path).AsTask(ct);
                var options = new FolderLauncherOptions();
                options.ItemsToSelect.Add(file);
                var folder = await file.GetParentAsync().AsTask(ct);
                return await Launcher.LaunchFolderAsync(folder, options);
            }

            if (Directory.Exists(path))
            {
                var folder = await StorageFolder.GetFolderFromPathAsync(path).AsTask(ct);
                return await Launcher.LaunchFolderAsync(folder);
            }

            return false;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "No se pudo mostrar en el Explorador.");
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> LaunchUriAsync(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        // Allowlist: solo URIs del sistema. Nunca http(s) con contenido de usuario.
        if (!string.Equals(uri.Scheme, "ms-settings", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            return await Launcher.LaunchUriAsync(uri);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "No se pudo abrir la URI del sistema.");
            return false;
        }
    }
}
