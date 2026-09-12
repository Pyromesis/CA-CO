using Microsoft.Extensions.Logging;
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
}

/// <summary>Implementación con <c>SpeechRecognizer</c> del sistema.</summary>
public sealed class VoiceDictationService(ILogger<VoiceDictationService> logger) : IVoiceDictationService
{
    /// <inheritdoc/>
    public async Task<bool> IsAvailableAsync()
    {
        using var recognizer = await CreateWorkingRecognizerAsync();
        return recognizer is not null;
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
