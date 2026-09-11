namespace CaCo.Infrastructure.Configuration;

/// <summary>
/// Sonda de escritura: verifica que una carpeta admite crear archivos.
/// Se usa para validar la ruta de biblioteca configurada antes de adoptarla
/// (una app empaquetada no puede escribir en carpetas de usuario arbitrarias
/// sin carpeta elegida con token).
/// </summary>
public static class StorageProbe
{
    /// <summary><c>true</c> si se puede crear y borrar un archivo en la carpeta.</summary>
    public static bool IsWritable(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            var probe = Path.Combine(folder, $".caco-write-test-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
