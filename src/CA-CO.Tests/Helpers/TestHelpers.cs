using CaCo.Core;

namespace CaCo.Tests.Helpers;

/// <summary>Reloj fijo para pruebas deterministas.</summary>
public sealed class TestClock(DateTimeOffset now) : IClock
{
    /// <inheritdoc/>
    public DateTimeOffset UtcNow => now;
}

/// <summary>Carpeta temporal que se limpia al terminar.</summary>
public sealed class TempDirectory : IDisposable
{
    /// <summary>Ruta creada.</summary>
    public string Path { get; } = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), "caco-tests", Guid.NewGuid().ToString("N"));

    /// <summary>Crea la carpeta.</summary>
    public TempDirectory()
    {
        Directory.CreateDirectory(Path);
    }

    /// <summary>Crea un archivo con contenido.</summary>
    public string CreateFile(string name, string content = "contenido de prueba")
    {
        var path = System.IO.Path.Combine(Path, name);
        File.WriteAllText(path, content);
        return path;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch
        {
            // Limpieza best-effort.
        }
    }
}
