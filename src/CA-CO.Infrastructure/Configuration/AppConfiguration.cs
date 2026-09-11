using System.Text.Json;
using CaCo.Application.Configuration;
using CaCo.Infrastructure.Persistence;

namespace CaCo.Infrastructure.Configuration;

/// <summary>
/// Rutas base de CA-CO fuera de la biblioteca (configuración y logs).
/// La biblioteca de documentos vive aparte y es reubicable.
/// </summary>
public static class AppPaths
{
    /// <summary>Carpeta de datos de aplicación: <c>%LOCALAPPDATA%\CA-CO</c>.</summary>
    public static string AppDataFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CA-CO");

    /// <summary>Archivo de ajustes.</summary>
    public static string SettingsFile => Path.Combine(AppDataFolder, "settings.json");

    /// <summary>Carpeta de logs.</summary>
    public static string LogsFolder => Path.Combine(AppDataFolder, "logs");

    /// <summary>
    /// Biblioteca por defecto: <c>%LOCALAPPDATA%\CA-CO\Library</c>.
    /// Funciona siempre en app empaquetada sin capacidades restringidas
    /// (las carpetas de usuario requieren carpeta elegida con token).
    /// Reubicable desde Configuración.
    /// </summary>
    public static string DefaultLibraryRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CA-CO", "Library");
}

/// <summary>
/// Resuelve la raíz efectiva de la biblioteca al arrancar:
/// ajuste guardado → defecto. La lectura es síncrona y tolerante a fallos
/// porque ocurre antes de construir el contenedor DI.
/// </summary>
public static class LibraryRootResolver
{
    /// <summary>Resuelve la raíz efectiva.</summary>
    /// <param name="overridePath">Ruta forzada (tests o argumentos).</param>
    public static string Resolve(string? overridePath = null)
    {
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return overridePath;
        }

        try
        {
            if (File.Exists(AppPaths.SettingsFile))
            {
                var json = File.ReadAllText(AppPaths.SettingsFile);
                var settings = JsonSerializer.Deserialize(json, CacoJsonContext.Indented.CacoSettings);
                if (!string.IsNullOrWhiteSpace(settings?.LibraryPath))
                {
                    return settings.LibraryPath;
                }
            }
        }
        catch
        {
            // Si los ajustes están corruptos se usa el defecto; FileAppConfiguration lo gestiona al cargar.
        }

        return AppPaths.DefaultLibraryRoot;
    }

    /// <summary>
    /// Raíz efectiva con sonda de escritura: si la ruta configurada no admite
    /// escritura (p. ej. carpeta de usuario sin token en app empaquetada),
    /// se usa <paramref name="defaultRoot"/>. Nunca lanza.
    /// </summary>
    /// <param name="configuredPath">Ruta de ajustes (puede ser nula o vacía).</param>
    /// <param name="defaultRoot">Raíz por defecto (la app pasa su LocalFolder real).</param>
    /// <param name="probe">Sonda opcional (tests).</param>
    public static string ResolveEffective(
        string? configuredPath,
        string? defaultRoot = null,
        Func<string, bool>? probe = null)
    {
        probe ??= StorageProbe.IsWritable;
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            try
            {
                if (probe(configuredPath))
                {
                    return configuredPath;
                }
            }
            catch
            {
                // Cae al defecto.
            }
        }

        return string.IsNullOrWhiteSpace(defaultRoot) ? AppPaths.DefaultLibraryRoot : defaultRoot;
    }
}

/// <summary>Persistencia de ajustes en <c>%LOCALAPPDATA%\CA-CO\settings.json</c>.</summary>
public sealed class FileAppConfiguration : IAppConfiguration
{
    private readonly string _settingsFile;

    /// <summary>Crea la configuración. Permite inyectar ruta (tests).</summary>
    public FileAppConfiguration(string? settingsFile = null)
    {
        _settingsFile = settingsFile ?? AppPaths.SettingsFile;
    }

    /// <inheritdoc/>
    public async Task<CacoSettings> LoadAsync(CancellationToken ct)
    {
        try
        {
            if (!File.Exists(_settingsFile))
            {
                return new CacoSettings();
            }

            await using var stream = File.OpenRead(_settingsFile);
            var settings = await JsonSerializer.DeserializeAsync(
                stream, CacoJsonContext.Indented.CacoSettings, ct).ConfigureAwait(false);
            return settings ?? new CacoSettings();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Ajustes corruptos: valores por defecto (se sobrescriben al guardar).
            return new CacoSettings();
        }
    }

    /// <inheritdoc/>
    public async Task SaveAsync(CacoSettings settings, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var folder = Path.GetDirectoryName(_settingsFile);
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }

        var temp = _settingsFile + ".tmp";
        await using (var stream = File.Create(temp))
        {
            await JsonSerializer.SerializeAsync(
                stream, settings, CacoJsonContext.Indented.CacoSettings, ct).ConfigureAwait(false);
        }

        File.Move(temp, _settingsFile, overwrite: true);
    }
}
