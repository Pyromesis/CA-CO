using CaCo.Application.Storage;
using CaCo.Core;
using CaCo.Domain;
using Microsoft.Extensions.Logging;
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace CaCo.App.Services;

/// <summary>Inspector multimedia real con APIs de Windows (Fase 3).</summary>
public sealed class MediaInspector(ILogger<MediaInspector> logger) : IMediaInspector
{
    private const long MaxBytes = 200L * 1024 * 1024;

    /// <inheritdoc/>
    public async Task<Result<MediaInfo?>> InspectAsync(string absolutePath, DocumentType type, CancellationToken ct)
    {
        if (type is not (DocumentType.Pdf or DocumentType.Png or DocumentType.Jpg or DocumentType.Jpeg))
        {
            return Result.Success<MediaInfo?>(null);
        }

        try
        {
            var info = new FileInfo(absolutePath);
            if (!info.Exists)
            {
                return Result.Failure<MediaInfo?>(Error.Storage("Media.MissingFile", "El archivo ya no existe."));
            }

            if (info.Length == 0 || info.Length > MaxBytes)
            {
                return Result.Failure<MediaInfo?>(Error.Validation("Media.BadSize", "Tamaño fuera de rango para inspeccionar."));
            }

            if (type == DocumentType.Pdf)
            {
                var file = await StorageFile.GetFileFromPathAsync(absolutePath).AsTask(ct);
                var pdf = await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(file).AsTask(ct);
                return Result.Success<MediaInfo?>(new MediaInfo((int)pdf.PageCount, null, null));
            }

            await using var stream = new FileStream(
                absolutePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var decoder = await BitmapDecoder.CreateAsync(stream.AsRandomAccessStream()).AsTask(ct);
            return Result.Success<MediaInfo?>(
                new MediaInfo(null, (int)decoder.PixelWidth, (int)decoder.PixelHeight));
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "No se pudo inspeccionar el archivo multimedia.");
            return Result.Failure<MediaInfo?>(Error.Storage("Media.InspectFailed", "No se pudo inspeccionar."));
        }
    }
}
