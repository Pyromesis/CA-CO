using CaCo.Application.Speech;
using CaCo.Core;
using Microsoft.Extensions.Logging;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.SpeechSynthesis;

namespace CaCo.App.Services;

/// <summary>Lectura en voz alta de textos (Fase 7): sintetizador del sistema
/// con voz española preferida, offline si el paquete está instalado.</summary>
public interface IVoiceReaderService
{
    /// <summary>Indica si hay alguna voz disponible.</summary>
    Task<bool> IsAvailableAsync();

    /// <summary>Nombre de la voz elegida (vacío si no hay).</summary>
    string VoiceDisplayName { get; }

    /// <summary>Se eleva al terminar (o detenerse) la lectura.</summary>
    event EventHandler? ReadingFinished;

    /// <summary>Lee el texto en voz alta (corta la anterior).</summary>
    Task<Result> SpeakAsync(string text, CancellationToken ct);

    /// <summary>Detiene la lectura en curso.</summary>
    void Stop();
}

/// <summary>Implementación con <c>SpeechSynthesizer</c> + <c>MediaPlayer</c>.</summary>
public sealed class VoiceReaderService(ILogger<VoiceReaderService> logger) : IVoiceReaderService
{
    private readonly List<Windows.Storage.Streams.IRandomAccessStream> _streams = [];
    private MediaPlayer? _player;
    private SpeechSynthesizer? _synth;
    private CancellationTokenSource? _speakCts;

    /// <inheritdoc/>
    public string VoiceDisplayName { get; private set; } = string.Empty;

    /// <inheritdoc/>
    public event EventHandler? ReadingFinished;

    /// <inheritdoc/>
    public Task<bool> IsAvailableAsync()
    {
        try
        {
            return Task.FromResult(EnsureEngine() is not null);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Voz de lectura no disponible.");
            return Task.FromResult(false);
        }
    }

    /// <inheritdoc/>
    public async Task<Result> SpeakAsync(string text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Result.Failure(Error.Validation("Voice.EmptyText", "No hay texto que leer."));
        }

        Stop();
        var synth = EnsureEngine();
        if (synth is null)
        {
            return Result.Failure(Error.Validation(
                "Voice.LanguageMissing",
                "Falta la voz en español. Pulsa «Instalar voz y OCR» en los comentarios (una vez, con Internet)."));
        }

        var chunks = SpeechChunking.Split(text);
        if (chunks.Count == 0)
        {
            return Result.Failure(Error.Validation("Voice.NoText", "No hay texto que leer."));
        }

        _speakCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _speakCts.Token;
        try
        {
            EnsurePlayer();
            foreach (var chunk in chunks)
            {
                token.ThrowIfCancellationRequested();
                var stream = await synth.SynthesizeTextToStreamAsync(chunk).AsTask(token);
                if (stream.Size == 0)
                {
                    continue;
                }

                _streams.Add(stream);
                var played = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                void OnEnded(MediaPlayer sender, object args) => played.TrySetResult(true);
                void OnFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
                {
                    logger.LogWarning("Fallo la reproducción de voz: {Error}", args.Error);
                    played.TrySetResult(false);
                }

                _player!.MediaEnded += OnEnded;
                _player.MediaFailed += OnFailed;
                try
                {
                    _player.Source = MediaSource.CreateFromStream(stream, "audio/wav");
                    _player.Play();
                    using (token.Register(() => played.TrySetCanceled()))
                    {
                        await played.Task.ConfigureAwait(false);
                    }
                }
                finally
                {
                    _player.MediaEnded -= OnEnded;
                    _player.MediaFailed -= OnFailed;
                }

                token.ThrowIfCancellationRequested();
            }

            return Result.Success();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Fallo la lectura en voz alta.");
            return Result.Failure(Error.Storage("Voice.SpeakFailed", "No se pudo leer en voz alta."));
        }
        finally
        {
            Stop();
            ReadingFinished?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <inheritdoc/>
    public void Stop()
    {
        try
        {
            _speakCts?.Cancel();
        }
        catch
        {
        }

        _speakCts?.Dispose();
        _speakCts = null;
        try
        {
            _player?.Pause();
        }
        catch
        {
        }

        foreach (var stream in _streams)
        {
            try
            {
                stream.Dispose();
            }
            catch
            {
            }
        }

        _streams.Clear();
    }

    private SpeechSynthesizer? EnsureEngine()
    {
        if (_synth is not null)
        {
            return _synth;
        }

        var choice = SpeechChunking.PickSpanishVoice(
            SpeechSynthesizer.AllVoices.Select(v =>
                new VoiceChoice(v.Id, v.Language, v.DisplayName)));
        if (choice is null)
        {
            return null;
        }

        var synth = new SpeechSynthesizer();
        var voice = SpeechSynthesizer.AllVoices.FirstOrDefault(v => v.Id == choice.Id);
        if (voice is not null)
        {
            synth.Voice = voice;
        }

        VoiceDisplayName = voice?.DisplayName ?? choice.DisplayName;
        _synth = synth;
        return _synth;
    }

    private void EnsurePlayer()
    {
        _player ??= new MediaPlayer();
    }
}
