using CaCo.Application.Storage;
using CaCo.Core;
using CaCo.Domain;

namespace CaCo.Infrastructure.DependencyInjection;

/// <summary>
/// Inspector desactivado (tests / contextos sin APIs multimedia). Nunca falla.
/// </summary>
public sealed class NullMediaInspector : IMediaInspector
{
    /// <summary>Instancia compartida.</summary>
    public static NullMediaInspector Instance { get; } = new();

    /// <inheritdoc/>
    public Task<Result<MediaInfo?>> InspectAsync(string absolutePath, DocumentType type, CancellationToken ct) =>
        Task.FromResult(Result.Success<MediaInfo?>(null));
}
