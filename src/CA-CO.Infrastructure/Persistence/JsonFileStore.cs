using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace CaCo.Infrastructure.Persistence;

/// <summary>
/// Almacén genérico sobre un archivo JSON con escritura atómica (temporal + mover).
/// Hilo-seguro mediante semáforo. Usa serialización generada (trim-safe).
/// Fase 0: la colección completa vive en memoria; suficiente para el arranque
/// y sustituible por SQLite en Fase 1 sin cambiar repositorios.
/// </summary>
/// <typeparam name="T">Tipo DTO serializable.</typeparam>
internal sealed class JsonFileStore<T>
{
    private readonly string _filePath;
    private readonly JsonTypeInfo<List<T>> _listTypeInfo;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private List<T>? _cache;

    /// <summary>Crea un almacén sobre <paramref name="filePath"/>.</summary>
    public JsonFileStore(string filePath, JsonTypeInfo<List<T>> listTypeInfo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(listTypeInfo);
        _filePath = filePath;
        _listTypeInfo = listTypeInfo;
    }

    /// <summary>Lee todos los elementos (con caché en memoria).</summary>
    public async Task<List<T>> LoadAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_cache is not null)
            {
                return [.. _cache];
            }

            if (!File.Exists(_filePath))
            {
                _cache = [];
                return [];
            }

            await using var stream = File.OpenRead(_filePath);
            _cache = await JsonSerializer.DeserializeAsync(stream, _listTypeInfo, ct).ConfigureAwait(false) ?? [];
            return [.. _cache];
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Reemplaza el contenido completo de forma atómica.</summary>
    public async Task SaveAsync(List<T> items, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(items);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var folder = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }

            var temp = _filePath + ".tmp";
            await using (var stream = File.Create(temp))
            {
                await JsonSerializer.SerializeAsync(stream, items, _listTypeInfo, ct).ConfigureAwait(false);
            }

            File.Move(temp, _filePath, overwrite: true);
            _cache = [.. items];
        }
        finally
        {
            _gate.Release();
        }
    }
}
