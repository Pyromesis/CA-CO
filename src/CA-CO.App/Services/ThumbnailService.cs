using CaCo.Application.Storage;
using CaCo.Domain;
using Microsoft.Extensions.Logging;
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace CaCo.App.Services;

/// <summary>
/// Miniaturas en <c>Thumbnails/{id}.png</c> (máx. 256 px): imágenes directas y
/// primera página de PDFs (Fase 3). Otros tipos usan icono.
/// </summary>
public sealed class ThumbnailService(ILibraryPaths paths, ILogger<ThumbnailService> logger) : IThumbnailService
{
    private const int MaxSize = 256;
    private const long MaxPixels = 50L * 1024 * 1024;

    /// <inheritdoc/>
    public async Task EnsureThumbnailAsync(Guid documentId, string managedFilePath, DocumentType type, CancellationToken ct)
    {
        if (type is not (DocumentType.Png or DocumentType.Jpg or DocumentType.Jpeg or DocumentType.Pdf))
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
            if (type == DocumentType.Pdf)
            {
                await EnsurePdfThumbnailAsync(documentId, managedFilePath, ct).ConfigureAwait(false);
                return;
            }

            var file = await StorageFile.GetFileFromPathAsync(managedFilePath).AsTask(ct);
            using var stream = await file.OpenReadAsync().AsTask(ct);
            var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(ct);
            await DecodeAndSaveAsync(documentId, decoder, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Accesorio: se registra y se sigue sin miniatura.
            logger.LogWarning(ex, "No se pudo generar la miniatura de {DocumentId}", documentId);
        }
    }

    private async Task EnsurePdfThumbnailAsync(Guid documentId, string managedFilePath, CancellationToken ct)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(managedFilePath).AsTask(ct);
            var pdf = await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(file).AsTask(ct);
            if (pdf.PageCount == 0)
            {
                return;
            }

            using var page = pdf.GetPage(0);
            using var rendered = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            await page.RenderToStreamAsync(rendered).AsTask(ct);
            var decoder = await BitmapDecoder.CreateAsync(rendered).AsTask(ct);
            await DecodeAndSaveAsync(documentId, decoder, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "No se pudo generar la miniatura PDF de {DocumentId}", documentId);
        }
    }

    private async Task DecodeAndSaveAsync(Guid documentId, BitmapDecoder decoder, CancellationToken ct)
    {
        if ((long)decoder.PixelWidth * decoder.PixelHeight > MaxPixels)
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
