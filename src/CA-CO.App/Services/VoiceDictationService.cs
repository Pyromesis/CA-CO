using CaCo.Core;
using Microsoft.Extensions.Logging;
using Windows.Devices.Enumeration;
using Windows.Media.SpeechRecognition;

namespace CaCo.App.Services;

/// <summary>
/// Dictado local voz → texto para comentarios (Fase 7, primera parte).
/// Usa el reconocedor del sistema: funciona offline si el paquete de voz del
/// idioma está instalado; si no, devuelve un error entendible.
/// </summary>
public interface IVoiceDictationService
{
    /// <summary>Indica si el dictado puede intentarse (idioma con voz instalado).</summary>
    Task<bool> IsAvailableAsync();

    /// <summary>Abre el dictado del sistema y devuelve el texto (<c>null</c> si se cancela).</summary>
    Task<Result<string?>> DictateAsync();
}

/// <summary>Implementación con <c>SpeechRecognizer</c> del sistema.</summary>
public sealed class VoiceDictationService(ILogger<VoiceDictationService> logger) : IVoiceDictationService
{
    /// <inheritdoc/>
    public async Task<bool> IsAvailableAsync()
    {
        using var recognizer = await CreateWorkingRecognizerAsync().ConfigureAwait(false);
        return recognizer is not null;
    }

    /// <inheritdoc/>
    public async Task<Result<string?>> DictateAsync()
    {
        try
        {
            var capture = await DeviceInformation.FindAllAsync(DeviceClass.AudioCapture);
            if (capture.Count == 0)
            {
                return Result.Failure<string?>(Error.Validation(
                    "Voice.NoMicrophone",
                    "No hay ningún micrófono disponible. Conecta uno y vuelve a intentarlo."));
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "No se pudo enumerar micrófonos.");
        }

        using var recognizer = await CreateWorkingRecognizerAsync();
        if (recognizer is null)
        {
            return Result.Failure<string?>(Error.Validation(
                "Voice.LanguageMissing",
                "Falta la voz en español. Pulsa «Instalar voz y OCR» aquí abajo (una vez, con Internet) o instálala en Configuración de Windows."));
        }

        try
        {
            var result = await recognizer.RecognizeWithUIAsync();
            if (result.Status == SpeechRecognitionResultStatus.UserCanceled)
            {
                return Result.Success<string?>(null);
            }

            if (result.Status == SpeechRecognitionResultStatus.TimeoutExceeded)
            {
                return Result.Failure<string?>(Error.Validation(
                    "Voice.NoSpeech",
                    "No se detectó voz. Revisa que el micrófono esté conectado y seleccionado, habla en cuanto aparezca la ventana y vuelve a intentarlo."));
            }

            if (result.Status != SpeechRecognitionResultStatus.Success)
            {
                return Result.Failure<string?>(Error.Validation(
                    "Voice.NotRecognized",
                    "No se entendió el dictado. Inténtalo de nuevo en un lugar silencioso."));
            }

            var text = result.Text?.Trim();
            return Result.Success<string?>(string.IsNullOrEmpty(text) ? null : text);
        }
        catch (Exception ex) when ((uint)ex.HResult == 0x800705AA)
        {
            return Result.Success<string?>(null); // Permiso denegado / cancelado.
        }
        catch (Exception ex) when (ex.Message.Contains("speech privacy policy", StringComparison.OrdinalIgnoreCase))
        {
            // Windows exige aceptar la directiva de privacidad de voz.
            logger.LogWarning("Directiva de privacidad de voz sin aceptar.");
            return Result.Failure<string?>(Error.Validation(
                "Voice.PrivacyNotAccepted",
                "Windows bloqueó el dictado por privacidad. Pulsa «Permitir voz en Privacidad», actívalo y vuelve a intentarlo."));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Fallo el dictado.");
            return Result.Failure<string?>(Error.Storage(
                "Voice.Failed", $"No se pudo dictar: {ex.Message}"));
        }
    }

    /// <summary>Crea un reconocedor funcional: idioma del sistema y, si falla, español.</summary>
    private async Task<SpeechRecognizer?> CreateWorkingRecognizerAsync()
    {
        foreach (var language in new[]
                 {
                     SpeechRecognizer.SystemSpeechLanguage,
                     new Windows.Globalization.Language("es-ES"),
                 })
        {
            SpeechRecognizer? recognizer = null;
            try
            {
                recognizer = new SpeechRecognizer(language);
                await recognizer.CompileConstraintsAsync();
                return recognizer;
            }
            catch (Exception ex)
            {
                recognizer?.Dispose();
                logger.LogWarning(ex, "Reconocedor no disponible para {Language}.", language.LanguageTag);
            }
        }

        return null;
    }
}
