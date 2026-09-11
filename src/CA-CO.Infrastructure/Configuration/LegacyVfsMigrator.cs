using Microsoft.Extensions.Logging;

namespace CaCo.Infrastructure.Configuration;

/// <summary>
/// Migra la biblioteca legada (ruta virtualizada por el paquete) a la nueva raíz real.
/// Solo actúa una vez: si la raíz nueva está vacía y la legada tiene contenido,
/// mueve cada carpeta superior y deja constancia en el log. Nunca lanza.
/// </summary>
public sealed class LegacyVfsMigrator(ILogger<LegacyVfsMigrator> logger)
{
    /// <summary>Mueve la biblioteca legada si corresponde.</summary>
    /// <param name="legacyRoot">Ruta legada (%LOCALAPPDATA%\CA-CO\Library).</param>
    /// <param name="newRoot">Raíz efectiva actual.</param>
    /// <returns><c>true</c> si movió algo.</returns>
    public Task<bool> MigrateIfNeededAsync(string legacyRoot, string newRoot, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(legacyRoot)
                || string.IsNullOrWhiteSpace(newRoot)
                || string.Equals(
                    Path.GetFullPath(legacyRoot).TrimEnd(Path.DirectorySeparatorChar),
                    Path.GetFullPath(newRoot).TrimEnd(Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(false);
            }

            if (!Directory.Exists(legacyRoot))
            {
                return Task.FromResult(false);
            }

            var entries = Directory.GetFileSystemEntries(legacyRoot);
            if (entries.Length == 0)
            {
                return Task.FromResult(false);
            }

            Directory.CreateDirectory(newRoot);
            var movedAny = false;
            foreach (var entry in entries)
            {
                ct.ThrowIfCancellationRequested();
                var target = Path.Combine(newRoot, Path.GetFileName(entry));
                try
                {
                    if (Directory.Exists(entry))
                    {
                        if (!Directory.Exists(target))
                        {
                            Directory.Move(entry, target);
                            movedAny = true;
                        }
                    }
                    else if (File.Exists(entry) && !File.Exists(target))
                    {
                        File.Move(entry, target);
                        movedAny = true;
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "No se pudo mover la entrada legada.");
                }
            }

            if (movedAny)
            {
                logger.LogInformation("Biblioteca legada migrada a {NewRoot}", newRoot);
            }

            return Task.FromResult(movedAny);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "No se pudo migrar la biblioteca legada; se usa la nueva ubicación.");
            return Task.FromResult(false);
        }
    }
}
