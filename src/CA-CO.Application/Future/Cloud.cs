using CaCo.Core;

namespace CaCo.Application.Future.Cloud;

/// <summary>
/// Proveedor de almacenamiento en la nube (Reservado para Fase 10).
/// Los proveedores (OneDrive, Google Drive, Dropbox) se añaden sin tocar el núcleo:
/// basta una nueva implementación registrada en DI.
/// </summary>
public interface ICloudStorageProvider
{
    /// <summary>Nombre del proveedor (p. ej. "OneDrive").</summary>
    string Name { get; }

    /// <summary>Indica si el proveedor está configurado y disponible.</summary>
    bool IsConfigured { get; }

    /// <summary>Sube un archivo de la biblioteca.</summary>
    Task<Result> UploadAsync(string localPath, string remotePath, CancellationToken ct);

    /// <summary>Descarga un archivo a la biblioteca.</summary>
    Task<Result> DownloadAsync(string remotePath, string localPath, CancellationToken ct);
}
