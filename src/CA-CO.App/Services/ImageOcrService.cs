using System.Runtime.InteropServices.WindowsRuntime;
using CaCo.Core;
using Microsoft.Extensions.Logging;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace CaCo.App.Services;

/// <summary>
/// OCR local de imágenes (Fase 5, primera parte). Usa el motor del sistema:
/// funciona offline si el paquete del idioma está instalado.
/// </summary>
public interface IImageOcrService
{
    /// <summary>Indica si el motor está disponible.</summary>
    Task<bool> IsAvailableAsync();

    /// <summary>Extrae el texto de una imagen.</summary>
    Task<Result<string>> RecognizeAsync(string imagePath, CancellationToken ct);
}

/// <summary>Implementación con <c>Windows.Media.Ocr</c>.</summary>
public sealed class ImageOcrService(ILogger<ImageOcrService> logger) : IImageOcrService
{
    /// <inheritdoc/>
    public Task<bool> IsAvailableAsync()
    {
        try
        {
            return Task.FromResult(TryCreateEngine() is not null);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "OCR no disponible.");
            return Task.FromResult(false);
        }
    }

    /// <inheritdoc/>
    public async Task<Result<string>> RecognizeAsync(string imagePath, CancellationToken ct)
    {
        try
        {
            var engine = TryCreateEngine();
            if (engine is null)
            {
                return Result.Failure<string>(Error.Validation(
                    "Ocr.LanguageMissing",
                    "Falta el OCR en español. Pulsa «Instalar voz y OCR» en los comentarios (una vez, con Internet)."));
            }

            const long maxOcrBytes = 200L * 1024 * 1024;
            const long maxPixels = 50L * 1024 * 1024;
            var info = new FileInfo(imagePath);
            if (!info.Exists)
            {
                return Result.Failure<string>(Error.Validation("Ocr.MissingFile", "La imagen ya no existe."));
            }

            if (info.Length > maxOcrBytes)
            {
                return Result.Failure<string>(Error.Validation("Ocr.TooLarge", "La imagen supera el máximo para OCR (200 MB)."));
            }

            await using var fileStream = new FileStream(
                imagePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 20,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var decoder = await BitmapDecoder.CreateAsync(
                fileStream.AsRandomAccessStream()).AsTask(ct);
            if ((long)decoder.PixelWidth * decoder.PixelHeight > maxPixels)
            {
                return Result.Failure<string>(Error.Validation("Ocr.TooLarge", "La imagen es demasiado grande para OCR."));
            }

            var bitmap = await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied).AsTask(ct);

            var result = await engine.RecognizeAsync(bitmap).AsTask(ct);
            var text = string.Join(Environment.NewLine,
                result.Lines.Select(l => l.Text).Where(t => !string.IsNullOrWhiteSpace(t)));

            if (string.IsNullOrWhiteSpace(text))
            {
                return Result.Failure<string>(Error.Validation(
                    "Ocr.NoText", "No se encontró texto en la imagen."));
            }

            const int maxOcrChars = 100_000;
            if (text.Length > maxOcrChars)
            {
                text = text[..maxOcrChars];
            }

            logger.LogInformation("OCR completado: {Chars} caracteres.", text.Length);
            return Result.Success(text);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Fallo el OCR.");
            return Result.Failure<string>(Error.Storage("Ocr.Failed", "No se pudo extraer el texto. El detalle quedó registrado."));
        }
    }

    private static OcrEngine? TryCreateEngine()
    {
        try
        {
            return OcrEngine.TryCreateFromLanguage(new Language("es-ES"))
                ?? OcrEngine.TryCreateFromUserProfileLanguages();
        }
        catch
        {
            return null;
        }
    }
}
