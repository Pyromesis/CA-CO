namespace CaCo.Application.Storage;

/// <summary>
/// Resuelve las rutas de la biblioteca CA-CO. Toda la persistencia cuelga de
/// <see cref="LibraryRoot"/> para que la biblioteca sea reubicable:
/// ningún documento guarda rutas absolutas.
/// <para>Estructura:</para>
/// <code>
/// {LibraryRoot}
/// ├── Documents   (copias administradas)
/// ├── Originals   (copias exactas de lo importado)
/// ├── Metadata    (metadatos por documento, fases futuras)
/// ├── Database    (persistencia: documents.json, notebooks.json...)
/// ├── Cache       (datos derivados regenerables)
/// ├── Thumbnails  (miniaturas bajo demanda, Fase 3)
/// ├── Backups     (copias locales, Fase 9)
/// └── Temp        (operaciones en curso)
/// </code>
/// </summary>
public interface ILibraryPaths
{
    /// <summary>Raíz de la biblioteca (configurable).</summary>
    string LibraryRoot { get; }

    /// <summary>Copias administradas de los documentos.</summary>
    string Documents { get; }

    /// <summary>Copias exactas de los archivos importados.</summary>
    string Originals { get; }

    /// <summary>Metadatos por documento.</summary>
    string Metadata { get; }

    /// <summary>Persistencia (JSON en Fase 0).</summary>
    string Database { get; }

    /// <summary>Caché regenerable.</summary>
    string Cache { get; }

    /// <summary>Miniaturas.</summary>
    string Thumbnails { get; }

    /// <summary>Copias de seguridad locales.</summary>
    string Backups { get; }

    /// <summary>Temporal de operaciones.</summary>
    string Temp { get; }

    /// <summary>Ruta del archivo de persistencia de documentos.</summary>
    string DocumentsDb { get; }

    /// <summary>Ruta del archivo de persistencia de cuadernos.</summary>
    string NotebooksDb { get; }

    /// <summary>Ruta del archivo de persistencia de notas.</summary>
    string NotesDb { get; }

    /// <summary>Ruta del archivo de persistencia de etiquetas.</summary>
    string TagsDb { get; }
}

/// <summary>
/// Crea la estructura inicial de la biblioteca si no existe.
/// Idempotente: abrir la app por primera vez no requiere configuración.
/// </summary>
public interface ILibraryInitializer
{
    /// <summary>Crea carpetas y archivos base si faltan. Devuelve <c>true</c> si creó algo nuevo.</summary>
    Task<bool> EnsureCreatedAsync(CancellationToken ct);
}

/// <summary>
/// Almacenamiento binario dentro de la biblioteca (copias administradas y originales).
/// Abstrae <c>System.IO</c> para poder sustituirlo (cifrado en Fase 8, nube en Fase 10).
/// </summary>
public interface IFileStorage
{
    /// <summary>Copia un archivo externo a la biblioteca y devuelve el nombre almacenado.</summary>
    /// <param name="sourcePath">Archivo de origen (fuera de la biblioteca).</param>
    /// <param name="targetFolder">Carpeta destino dentro de la biblioteca.</param>
    /// <param name="ct">Cancelación (importaciones grandes).</param>
    /// <returns>Nombre del archivo dentro de <paramref name="targetFolder"/>.</returns>
    Task<string> CopyIntoLibraryAsync(string sourcePath, string targetFolder, CancellationToken ct);

    /// <summary>Elimina un archivo de la biblioteca si existe.</summary>
    Task DeleteAsync(string folder, string storedFileName, CancellationToken ct);

    /// <summary>Indica si existe un archivo en la biblioteca.</summary>
    Task<bool> ExistsAsync(string folder, string storedFileName, CancellationToken ct);

    /// <summary>Abre un archivo de la biblioteca para lectura.</summary>
    Task<Stream> OpenReadAsync(string folder, string storedFileName, CancellationToken ct);

    /// <summary>Escribe texto en un archivo de la biblioteca (crea o reemplaza).</summary>
    Task WriteAllTextAsync(string folder, string storedFileName, string content, CancellationToken ct);

    /// <summary>Escribe bytes en un archivo de la biblioteca (crea o reemplaza).</summary>
    Task WriteAllBytesAsync(string folder, string storedFileName, byte[] content, CancellationToken ct);
}
