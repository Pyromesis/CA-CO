using CaCo.Core;
using Microsoft.Extensions.Logging;
using Windows.Devices.Enumeration;
using Windows.Media.SpeechRecognition;

namespace CaCo.App.Services;

/// <summary>Motivo de fin de una sesión de dictado.</summary>
public enum VoiceSessionEndReason
{
    /// <summary>Terminada por el usuario.</summary>
    Stopped = 0,

    /// <summary>Terminada por silencio prolongado.</summary>
    Timeout = 1,

    /// <summary>Terminada por error.</summary>
    Error = 2,
}

/// <summary>
/// Sesión de dictado continuo estilo mensaje de voz: iniciar, pausar, seguir,
/// terminar o eliminar. Emite fragmentos finales e hipótesis en vivo.
/// </summary>
public interface IVoiceMessageSession : IDisposable
{
    /// <summary>Fragmento definitivo reconocido (se acumula).</summary>
    event EventHandler<string>? FinalRecognized;

    /// <summary>Hipótesis en vivo (no definitiva).</summary>
    event EventHandler<string>? HypothesisChanged;

    /// <summary>Sesión terminada por el sistema (silencio, error).</summary>
    event EventHandler<VoiceSessionEndReason>? SessionCompleted;

    /// <summary>Inicia la escucha.</summary>
    Task<Result> StartAsync(CancellationToken ct);

    /// <summary>Detiene (dispara <see cref="SessionCompleted"/> con <c>Stopped</c>).</summary>
    Task<Result> StopAsync();
}

/// <summary>Crea sesiones de dictado con un idioma funcional.</summary>
public interface IVoiceMessageSessionFactory
{
    /// <summary>Crea la sesión (<c>null</c> si no hay idioma con voz).</summary>
    Task<IVoiceMessageSession?> CreateAsync(CancellationToken ct);
}

/// <summary>Sesión sobre <c>SpeechRecognizer</c> en modo continuo.</summary>
public sealed class VoiceMessageSession : IVoiceMessageSession
{
    private readonly SpeechRecognizer _recognizer;
    private readonly ILogger _logger;
    private bool _disposed;

    /// <inheritdoc/>
    public event EventHandler<string>? FinalRecognized;

    /// <inheritdoc/>
    public event EventHandler<string>? HypothesisChanged;

    /// <inheritdoc/>
    public event EventHandler<VoiceSessionEndReason>? SessionCompleted;

    internal VoiceMessageSession(SpeechRecognizer recognizer, ILogger logger)
    {
        _recognizer = recognizer;
        _logger = logger;
        _recognizer.ContinuousRecognitionSession.ResultGenerated += OnResultGenerated;
        _recognizer.HypothesisGenerated += OnHypothesisGenerated;
        _recognizer.ContinuousRecognitionSession.Completed += OnCompleted;
        _recognizer.Timeouts.InitialSilenceTimeout = TimeSpan.FromSeconds(30);
        _recognizer.Timeouts.EndSilenceTimeout = TimeSpan.FromSeconds(10);
        _recognizer.Timeouts.BabbleTimeout = TimeSpan.FromMinutes(5);
    }

    /// <inheritdoc/>
    public async Task<Result> StartAsync(CancellationToken ct)
    {
        try
        {
            var capture = await DeviceInformation.FindAllAsync(DeviceClass.AudioCapture).AsTask(ct);
            if (capture.Count == 0)
            {
                return Result.Failure(Error.Validation(
                    "Voice.NoMicrophone",
                    "No hay ningún micrófono disponible. Conecta uno y vuelve a intentarlo."));
            }

            _recognizer.Constraints.Clear();
            _recognizer.Constraints.Add(new SpeechRecognitionTopicConstraint(
                SpeechRecognitionScenario.Dictation, "dictado"));
            var compiled = await _recognizer.CompileConstraintsAsync();
            if (compiled.Status != SpeechRecognitionResultStatus.Success)
            {
                return Result.Failure(Error.Storage(
                    "Voice.ConstraintsFailed", "No se pudo preparar el dictado."));
            }

            await _recognizer.ContinuousRecognitionSession.StartAsync();
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo iniciar la sesión de voz.");
            return Result.Failure(Error.Storage("Voice.StartFailed", $"No se pudo iniciar: {ex.Message}"));
        }
    }

    /// <inheritdoc/>
    public async Task<Result> StopAsync()
    {
        try
        {
            await _recognizer.ContinuousRecognitionSession.StopAsync();
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo detener.");
            return Result.Failure(Error.Storage("Voice.StopFailed", "No se pudo detener la grabación."));
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _recognizer.ContinuousRecognitionSession.ResultGenerated -= OnResultGenerated;
        _recognizer.HypothesisGenerated -= OnHypothesisGenerated;
        _recognizer.ContinuousRecognitionSession.Completed -= OnCompleted;
        _recognizer.Dispose();
    }

    private void OnResultGenerated(
        SpeechContinuousRecognitionSession sender,
        SpeechContinuousRecognitionResultGeneratedEventArgs args)
    {
        try
        {
            if (args.Result.Status != SpeechRecognitionResultStatus.Success)
            {
                return;
            }

            var text = args.Result.Text?.Trim();
            if (!string.IsNullOrEmpty(text))
            {
                FinalRecognized?.Invoke(this, text);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Fallo al procesar resultado.");
        }
    }

    private void OnHypothesisGenerated(
        SpeechRecognizer sender,
        SpeechRecognitionHypothesisGeneratedEventArgs args)
    {
        try
        {
            var text = args.Hypothesis?.Text?.Trim();
            if (!string.IsNullOrEmpty(text))
            {
                HypothesisChanged?.Invoke(this, text);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Fallo al procesar hipótesis.");
        }
    }

    private void OnCompleted(
        SpeechContinuousRecognitionSession sender,
        SpeechContinuousRecognitionCompletedEventArgs args)
    {
        var reason = args.Status switch
        {
            SpeechRecognitionResultStatus.Success => VoiceSessionEndReason.Stopped,
            SpeechRecognitionResultStatus.UserCanceled => VoiceSessionEndReason.Stopped,
            SpeechRecognitionResultStatus.TimeoutExceeded => VoiceSessionEndReason.Timeout,
            _ => VoiceSessionEndReason.Error,
        };
        SessionCompleted?.Invoke(this, reason);
    }
}

/// <summary>Crea sesiones con el primer idioma funcional (sistema, luego español).</summary>
public sealed class VoiceMessageSessionFactory(ILogger<VoiceMessageSessionFactory> logger) : IVoiceMessageSessionFactory
{
    /// <inheritdoc/>
    public async Task<IVoiceMessageSession?> CreateAsync(CancellationToken ct)
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
                ct.ThrowIfCancellationRequested();
                recognizer = new SpeechRecognizer(language);
                await recognizer.CompileConstraintsAsync().AsTask(ct);
                return new VoiceMessageSession(recognizer, logger);
            }
            catch (Exception ex)
            {
                recognizer?.Dispose();
                logger.LogWarning(ex, "Voz no disponible para {Language}.", language.LanguageTag);
            }
        }

        return null;
    }
}
