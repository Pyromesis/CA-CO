using CaCo.Application.Storage;
using CaCo.Domain;
using Microsoft.Extensions.Logging;
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace CaCo.App.Services;

/// <summary>
/// Miniaturas de imágenes (PNG/JPG/JPEG) en <c>Thumbnails/{id}.png</c> (máx. 256 px).
/// Otros tipos no generan miniatura (la UI muestra icono).
/// </summary>
public sealed class ThumbnailService(ILibraryPaths paths, ILogger<ThumbnailService> logger) : IThumbnailService
{
    private const int MaxSize = 256;

    /// <inheritdoc/>
    public async Task EnsureThumbnailAsync(Guid documentId, string managedFilePath, DocumentType type, CancellationToken ct)
    {
        if (type is not (DocumentType.Png or DocumentType.Jpg or DocumentType.Jpeg))
        {
            return;
        }

        var target = PathFor(documentId);
        if (File.Exists(target))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(paths.Thumbnails);
            var file = await StorageFile.GetFileFromPathAsync(managedFilePath).AsTask(ct);
            using var stream = await file.OpenReadAsync().AsTask(ct);
            var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(ct);

            const long maxPixels = 50L * 1024 * 1024;
            if ((long)decoder.PixelWidth * decoder.PixelHeight > maxPixels)
            {
                logger.LogWarning("Imagen demasiado grande para miniatura: {W}x{H}.", decoder.PixelWidth, decoder.PixelHeight);
                return;
            }

            var scale = Math.Min(1.0, MaxSize / (double)Math.Max(decoder.PixelWidth, decoder.PixelHeight));
            var width = Math.Max(1, (uint)(decoder.PixelWidth * scale));
            var height = Math.Max(1, (uint)(decoder.PixelHeight * scale));

            var transform = new BitmapTransform { ScaledWidth = width, ScaledHeight = height };
            var pixels = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                transform, ExifOrientationMode.RespectExifOrientation,
                ColorManagementMode.DoNotColorManage).AsTask(ct);

            var targetFolder = await StorageFolder.GetFolderFromPathAsync(paths.Thumbnails).AsTask(ct);
            var targetFile = await targetFolder.CreateFileAsync(
                $"{documentId:N}.png", CreationCollisionOption.ReplaceExisting).AsTask(ct);
            using var outStream = await targetFile.OpenAsync(FileAccessMode.ReadWrite).AsTask(ct);
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, outStream).AsTask(ct);
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, width, height, 96, 96, pixels.DetachPixelData());
            await encoder.FlushAsync().AsTask(ct);
        }
        catch (Exception ex)
        {
            // Accesorio: se registra y se sigue sin miniatura.
            logger.LogWarning(ex, "No se pudo generar la miniatura de {DocumentId}", documentId);
        }
    }

    /// <inheritdoc/>
    public string? TryGetExistingPath(Guid documentId)
    {
        var target = PathFor(documentId);
        return File.Exists(target) ? target : null;
    }

    /// <inheritdoc/>
    public Task DeleteThumbnailAsync(Guid documentId, CancellationToken ct)
    {
        var target = PathFor(documentId);
        if (File.Exists(target))
        {
            File.Delete(target);
        }

        return Task.CompletedTask;
    }

    private string PathFor(Guid documentId) =>
        Path.Combine(paths.Thumbnails, $"{documentId:N}.png");
}
