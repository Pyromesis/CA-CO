using CaCo.Application.Storage;
using Microsoft.Extensions.Logging;

namespace CaCo.Infrastructure.Storage;

/// <summary>Implementación de <see cref="ILibraryPaths"/> sobre una raíz configurable.</summary>
public sealed class LibraryPaths : ILibraryPaths
{
    /// <summary>Crea las rutas colgando de <paramref name="libraryRoot"/>.</summary>
    public LibraryPaths(string libraryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryRoot);
        LibraryRoot = Path.GetFullPath(libraryRoot);
        Documents = Path.Combine(LibraryRoot, "Documents");
        Originals = Path.Combine(LibraryRoot, "Originals");
        Metadata = Path.Combine(LibraryRoot, "Metadata");
        Database = Path.Combine(LibraryRoot, "Database");
        Cache = Path.Combine(LibraryRoot, "Cache");
        Thumbnails = Path.Combine(LibraryRoot, "Thumbnails");
        Backups = Path.Combine(LibraryRoot, "Backups");
        Temp = Path.Combine(LibraryRoot, "Temp");
        DocumentsDb = Path.Combine(Database, "documents.json");
        NotebooksDb = Path.Combine(Database, "notebooks.json");
        NotesDb = Path.Combine(Database, "notes.json");
        TagsDb = Path.Combine(Database, "tags.json");
    }

    /// <inheritdoc/>
    public string LibraryRoot { get; }

    /// <inheritdoc/>
    public string Documents { get; }

    /// <inheritdoc/>
    public string Originals { get; }

    /// <inheritdoc/>
    public string Metadata { get; }

    /// <inheritdoc/>
    public string Database { get; }

    /// <inheritdoc/>
    public string Cache { get; }

    /// <inheritdoc/>
    public string Thumbnails { get; }

    /// <inheritdoc/>
    public string Backups { get; }

    /// <inheritdoc/>
    public string Temp { get; }

    /// <inheritdoc/>
    public string DocumentsDb { get; }

    /// <inheritdoc/>
    public string NotebooksDb { get; }

    /// <inheritdoc/>
    public string NotesDb { get; }

    /// <inheritdoc/>
    public string TagsDb { get; }
}

/// <summary>Crea la estructura inicial de la biblioteca (idempotente).</summary>
public sealed class LibraryInitializer(
    ILibraryPaths paths,
    ILogger<LibraryInitializer> logger) : ILibraryInitializer
{
    private const int TempRetentionDays = 7;
    /// <inheritdoc/>
    public Task<bool> EnsureCreatedAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var created = false;

        foreach (var folder in new[]
                 {
                     paths.LibraryRoot, paths.Documents, paths.Originals, paths.Metadata,
                     paths.Database, paths.Cache, paths.Thumbnails, paths.Backups, paths.Temp,
                 })
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (!Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder);
                    created = true;
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or NotSupportedException)
            {
                logger.LogWarning(ex, "No se pudo crear la carpeta de biblioteca.");
                throw;
            }
        }

        PurgeTemp();

        if (created)
        {
            logger.LogInformation("Biblioteca inicializada.");
        }

        return Task.FromResult(created);
    }

    private void PurgeTemp()
    {
        const long maxTempBytes = 500L * 1024 * 1024;
        try
        {
            if (!Directory.Exists(paths.Temp))
            {
                return;
            }

            var cutoff = DateTime.UtcNow.AddDays(-TempRetentionDays);
            foreach (var file in Directory.EnumerateFiles(paths.Temp, "*", SearchOption.AllDirectories))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(file) < cutoff)
                    {
                        File.Delete(file);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "No se pudo purgar temporal.");
                }
            }

            foreach (var dir in Directory.EnumerateDirectories(paths.Temp, "*", SearchOption.AllDirectories))
            {
                try
                {
                    if (Directory.Exists(dir) && Directory.GetFileSystemEntries(dir).Length == 0
                        && Directory.GetLastWriteTimeUtc(dir) < cutoff)
                    {
                        Directory.Delete(dir);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "No se pudo purgar directorio temporal.");
                }
            }

            // Cuota total LRU: si Temp supera 500 MB, borrar los más viejos.
            try
            {
                var files = Directory.EnumerateFiles(paths.Temp, "*", SearchOption.AllDirectories)
                    .Select(f => new FileInfo(f))
                    .Where(f => f.Exists)
                    .OrderBy(f => f.LastWriteTimeUtc)
                    .ToList();
                var total = files.Sum(f => f.Length);
                foreach (var f in files)
                {
                    if (total <= maxTempBytes)
                    {
                        break;
                    }

                    try
                    {
                        total -= f.Length;
                        f.Delete();
                    }
                    catch (Exception ex)
                    {
                        logger.LogDebug(ex, "No se pudo purgar temporal por cuota.");
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "No se pudo aplicar cuota temporal.");
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "No se pudo purgar la carpeta temporal.");
        }
    }
}

/// <summary>Almacenamiento binario sobre el sistema de archivos local.</summary>
public sealed class PhysicalFileStorage(ILogger<PhysicalFileStorage> logger) : IFileStorage
{
    /// <inheritdoc/>
    public async Task<string> CopyIntoLibraryAsync(string sourcePath, string targetFolder, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetFolder);
        Directory.CreateDirectory(targetFolder);

        // Nombre único e imposible de colisionar; la extensión original se conserva.
        var storedFileName = $"{Guid.NewGuid():N}{Path.GetExtension(sourcePath).ToLowerInvariant()}";
        var destination = Path.Combine(targetFolder, storedFileName);

        await using (var source = new FileStream(
            sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 20,
            FileOptions.Asynchronous | FileOptions.SequentialScan))
        await using (var dest = new FileStream(
            destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 20,
            FileOptions.Asynchronous))
        {
            await source.CopyToAsync(dest, 1 << 20, ct).ConfigureAwait(false);
            await dest.FlushAsync(ct).ConfigureAwait(false);
        }

        logger.LogDebug("Archivo copiado a la biblioteca: {FileName}", storedFileName);
        return storedFileName;
    }

    /// <inheritdoc/>
    public Task DeleteAsync(string folder, string storedFileName, CancellationToken ct)
    {
        var path = SafeCombine(folder, storedFileName);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<bool> ExistsAsync(string folder, string storedFileName, CancellationToken ct) =>
        Task.FromResult(File.Exists(SafeCombine(folder, storedFileName)));

    /// <inheritdoc/>
    public Task<Stream> OpenReadAsync(string folder, string storedFileName, CancellationToken ct)
    {
        Stream stream = File.OpenRead(SafeCombine(folder, storedFileName));
        return Task.FromResult(stream);
    }

    /// <inheritdoc/>
    public async Task WriteAllTextAsync(string folder, string storedFileName, string content, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(content);
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(SafeCombine(folder, storedFileName), content, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task WriteAllBytesAsync(string folder, string storedFileName, byte[] content, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(content);
        Directory.CreateDirectory(folder);
        await File.WriteAllBytesAsync(SafeCombine(folder, storedFileName), content, ct).ConfigureAwait(false);
    }

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    internal static string SafeCombine(string folder, string storedFileName)
    {
        // Evita path traversal: solo se admite un nombre de archivo, sin directorios.
        if (string.IsNullOrWhiteSpace(storedFileName)
            || storedFileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || storedFileName.Contains("..", StringComparison.Ordinal)
            || storedFileName.Contains('/') || storedFileName.Contains('\\')
            || Path.GetFileName(storedFileName) != storedFileName
            || storedFileName.EndsWith('.') || storedFileName.EndsWith(' '))
        {
            throw new ArgumentException("Nombre de archivo no válido.", nameof(storedFileName));
        }

        var baseName = storedFileName.Split('.')[0];
        if (ReservedNames.Contains(baseName))
        {
            throw new ArgumentException("Nombre de archivo reservado.", nameof(storedFileName));
        }

        var combined = Path.Combine(folder, storedFileName);
        var fullFolder = Path.GetFullPath(folder);
        var fullPath = Path.GetFullPath(combined);
        if (!fullPath.StartsWith(fullFolder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("El archivo queda fuera de la biblioteca.", nameof(storedFileName));
        }

        return fullPath;
    }
}
