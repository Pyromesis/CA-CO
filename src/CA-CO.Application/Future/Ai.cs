using CaCo.Core;

namespace CaCo.Application.Future.Ai;

/// <summary>
/// IA sobre documentos (Reservado para Fase 11: resumen, preguntas, clasificación,
/// extracción). Diseñado para admitir IA local o por API sin cambiar consumidores:
/// todo cuelga de esta abstracción.
/// </summary>
public interface IAiService
{
    /// <summary>Indica si hay un modelo disponible (local o remoto).</summary>
    bool IsAvailable { get; }

    /// <summary>Resume el texto dado.</summary>
    Task<Result<string>> SummarizeAsync(string text, CancellationToken ct);

    /// <summary>Responde una pregunta sobre el texto dado.</summary>
    Task<Result<string>> AskAsync(string text, string question, CancellationToken ct);
}
