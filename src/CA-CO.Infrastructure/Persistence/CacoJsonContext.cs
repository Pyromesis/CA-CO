using System.Text.Json;
using System.Text.Json.Serialization;
using CaCo.Application.Configuration;

namespace CaCo.Infrastructure.Persistence;

/// <summary>
/// Contexto de serialización por generación de código (trim-safe).
/// La app WinUI se publica con trimming en Release: la serialización por
/// reflexión rompería en ese modo. Todos los JSON de CA-CO pasan por aquí.
/// </summary>
[JsonSerializable(typeof(DocumentDto))]
[JsonSerializable(typeof(List<DocumentDto>))]
[JsonSerializable(typeof(NotebookDto))]
[JsonSerializable(typeof(List<NotebookDto>))]
[JsonSerializable(typeof(NoteDto))]
[JsonSerializable(typeof(List<NoteDto>))]
[JsonSerializable(typeof(TagDto))]
[JsonSerializable(typeof(List<TagDto>))]
[JsonSerializable(typeof(CacoSettings))]
[JsonSerializable(typeof(List<Guid>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class CacoJsonContext : JsonSerializerContext
{
    /// <summary>Instancia con JSON indentado (archivos legibles y depurables).</summary>
    public static CacoJsonContext Indented { get; } = new(
        new JsonSerializerOptions { WriteIndented = true });
}
