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

    /// <summary>Extrae el texto de un PDF, página a página (Fase 5).</summary>
    Task<Result<CaCo.Application.Ocr.PdfOcrResult>> RecognizePdfAsync(
        string pdfPath,
        CaCo.Application.Ocr.PdfOcrOptions? options,
        IProgress<CaCo.Application.Ocr.OcrProgress>? progress,
        CancellationToken ct);
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

    /// <inheritdoc/>
    public async Task<Result<CaCo.Application.Ocr.PdfOcrResult>> RecognizePdfAsync(
        string pdfPath,
        CaCo.Application.Ocr.PdfOcrOptions? options,
        IProgress<CaCo.Application.Ocr.OcrProgress>? progress,
        CancellationToken ct)
    {
        var settings = options ?? CaCo.Application.Ocr.PdfOcrOptions.Default;
        try
        {
            var engine = TryCreateEngine();
            if (engine is null)
            {
                return Result.Failure<CaCo.Application.Ocr.PdfOcrResult>(Error.Validation(
                    "Ocr.LanguageMissing",
                    "Falta el OCR en español. Pulsa «Instalar voz y OCR» en los comentarios (una vez, con Internet)."));
            }

            var info = new FileInfo(pdfPath);
            if (!info.Exists || info.Length == 0 || info.Length > 200L * 1024 * 1024)
            {
                return Result.Failure<CaCo.Application.Ocr.PdfOcrResult>(Error.Validation(
                    "Ocr.BadFile", "El PDF no existe o su tamaño no es válido."));
            }

            var storageFile = await Windows.Storage.StorageFile.GetFileFromPathAsync(pdfPath).AsTask(ct);
            var pdf = await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(storageFile).AsTask(ct);
            var totalPages = (int)pdf.PageCount;
            if (totalPages == 0)
            {
                return Result.Failure<CaCo.Application.Ocr.PdfOcrResult>(Error.Validation(
                    "Ocr.NoText", "El PDF no tiene páginas."));
            }

            var wanted = CaCo.Application.Ocr.OcrPages.TakePages(totalPages, settings);
            var pageTexts = new List<string?>(wanted.Count);
            var done = 0;
            foreach (var pageNumber in wanted)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    using var page = pdf.GetPage((uint)(pageNumber - 1));
                    using var rendered = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                    await page.RenderToStreamAsync(rendered).AsTask(ct);
                    var decoder = await BitmapDecoder.CreateAsync(rendered).AsTask(ct);
                    if ((long)decoder.PixelWidth * decoder.PixelHeight > 50L * 1024 * 1024)
                    {
                        pageTexts.Add(null);
                    }
                    else
                    {
                        var bitmap = await decoder.GetSoftwareBitmapAsync(
                            BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied).AsTask(ct);
                        var result = await engine.RecognizeAsync(bitmap).AsTask(ct);
                        pageTexts.Add(string.Join(Environment.NewLine,
                            result.Lines.Select(l => l.Text).Where(t => !string.IsNullOrWhiteSpace(t))));
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // Página ilegible: se salta y se sigue con el resto.
                    logger.LogDebug(ex, "OCR falló en la página {Page}.", pageNumber);
                    pageTexts.Add(null);
                }

                done++;
                progress?.Report(new CaCo.Application.Ocr.OcrProgress(done, wanted.Count));
            }

            var (text, charsTruncated) = CaCo.Application.Ocr.OcrPages.Combine(pageTexts, settings);
            if (string.IsNullOrWhiteSpace(text))
            {
                return Result.Failure<CaCo.Application.Ocr.PdfOcrResult>(Error.Validation(
                    "Ocr.NoText", "No se encontró texto en el PDF."));
            }

            var truncated = charsTruncated || wanted.Count < totalPages;
            logger.LogInformation("OCR PDF: {Chars} caracteres en {Pages}/{Total} páginas.", text.Length, done, totalPages);
            return Result.Success(new CaCo.Application.Ocr.PdfOcrResult(text, done, totalPages, truncated));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Fallo el OCR del PDF.");
            return Result.Failure<CaCo.Application.Ocr.PdfOcrResult>(Error.Storage("Ocr.Failed", "No se pudo extraer el texto. El detalle quedó registrado."));
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
