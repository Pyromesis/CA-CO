namespace CaCo.Application.Import;

/// <summary>Resultado de explorar una carpeta para importar.</summary>
/// <param name="Files">Rutas candidatas (ordenadas, solo extensiones soportadas).</param>
/// <param name="SkippedCount">Entradas omitidas (no soportadas, ocultas, inaccesibles).</param>
public sealed record FolderScanResult(IReadOnlyList<string> Files, int SkippedCount);

/// <summary>
/// Explora carpetas para importar (Fase 2): solo extensiones soportadas,
/// salta ocultos/sistema, orden determinista y tolera carpetas inaccesibles
/// (las cuenta como omitidas en vez de lanzar).
/// </summary>
public interface IFolderScanner
{
    /// <summary>Enumera archivos importables bajo <paramref name="folder"/>.</summary>
    FolderScanResult Enumerate(string folder, bool recursive, CancellationToken ct);
}
