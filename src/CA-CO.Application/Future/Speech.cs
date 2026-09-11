using CaCo.Core;

namespace CaCo.Application.Future.Speech;

/// <summary>
/// Dictado local voz → texto (Reservado para Fase 7).
/// Flujo futuro: <c>Voz → Transcripción → Texto editable</c>, todo offline.
/// </summary>
public interface ISpeechToTextService
{
    /// <summary>Indica si hay un reconocedor local disponible.</summary>
    bool IsAvailable { get; }

    /// <summary>Transcribe audio a texto editable.</summary>
    Task<Result<TranscriptionResult>> TranscribeAsync(Stream audio, string language, CancellationToken ct);
}

/// <summary>Resultado de una transcripción.</summary>
/// <param name="Text">Texto transcrito y editable.</param>
/// <param name="Confidence">Confianza media 0-1.</param>
public sealed record TranscriptionResult(string Text, double Confidence);
