using CaCo.Application.Import;
using CaCo.Domain;

namespace CaCo.Infrastructure.Import;

/// <summary>Implementación de <see cref="IFolderScanner"/> sobre el sistema de archivos.</summary>
public sealed class FolderScanner : IFolderScanner
{
    /// <summary>Tope de entradas exploradas (el resto cuenta como omitido).</summary>
    public const int MaxScannedFiles = 5000;

    /// <inheritdoc/>
    public FolderScanResult Enumerate(string folder, bool recursive, CancellationToken ct)
    {
        var files = new List<string>();
        var skipped = 0;
        if (string.IsNullOrWhiteSpace(folder))
        {
            return new FolderScanResult(files, skipped);
        }

        IEnumerable<string> candidates;
        try
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = recursive,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
                MatchCasing = MatchCasing.CaseInsensitive,
            };
            candidates = Directory.EnumerateFiles(folder, "*", options);
        }
        catch (Exception)
        {
            return new FolderScanResult(files, skipped);
        }

        var scanned = 0;
        try
        {
            foreach (var path in candidates)
            {
                ct.ThrowIfCancellationRequested();
                if (scanned >= MaxScannedFiles)
                {
                    skipped++;
                    continue;
                }

                scanned++;
                if (SupportedFileTypes.IsSupported(path))
                {
                    files.Add(path);
                }
                else
                {
                    skipped++;
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // A mitad de la exploración: se devuelve lo reunido hasta ahora.
        }

        files.Sort(StringComparer.OrdinalIgnoreCase);
        return new FolderScanResult(files, skipped);
    }
}
