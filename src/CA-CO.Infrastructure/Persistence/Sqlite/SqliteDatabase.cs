using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace CaCo.Infrastructure.Persistence.Sqlite;

/// <summary>
/// Acceso a SQLite (<c>Database/caco.db</c>, Fase 1).
/// Crea el esquema si falta, usa WAL para lecturas concurrentes y serializa
/// escrituras con semáforo. Trim-safe (sin reflexión).
/// </summary>
public sealed class SqliteDatabase
{
    private const string Schema = """
        CREATE TABLE IF NOT EXISTS documents (
            id TEXT PRIMARY KEY,
            name TEXT NOT NULL,
            originalFileName TEXT NOT NULL,
            storedFileName TEXT NULL,
            originalStoredFileName TEXT NULL,
            fileType INTEGER NOT NULL,
            sizeBytes INTEGER NOT NULL,
            createdAt TEXT NOT NULL,
            modifiedAt TEXT NOT NULL,
            importedAt TEXT NOT NULL,
            isFavorite INTEGER NOT NULL DEFAULT 0,
            isDeleted INTEGER NOT NULL DEFAULT 0,
            deletedAt TEXT NULL,
            notebookId TEXT NULL,
            tagIds TEXT NOT NULL DEFAULT '[]',
            metadata TEXT NOT NULL DEFAULT '{}',
            contentHash TEXT NULL,
            isEncrypted INTEGER NOT NULL DEFAULT 0
        );
        CREATE INDEX IF NOT EXISTS idx_documents_notebook ON documents(notebookId);
        CREATE INDEX IF NOT EXISTS idx_documents_imported ON documents(importedAt DESC);
        CREATE INDEX IF NOT EXISTS idx_documents_name ON documents(name COLLATE NOCASE);
        CREATE INDEX IF NOT EXISTS idx_documents_hash ON documents(contentHash);
        CREATE INDEX IF NOT EXISTS idx_docs_recent ON documents(isDeleted, importedAt DESC);
        CREATE INDEX IF NOT EXISTS idx_docs_fav ON documents(isDeleted, isFavorite);
        CREATE INDEX IF NOT EXISTS idx_docs_trash ON documents(isDeleted, deletedAt);
        CREATE INDEX IF NOT EXISTS idx_docs_nb_active ON documents(isDeleted, notebookId);
        CREATE TABLE IF NOT EXISTS notebooks (
            id TEXT PRIMARY KEY,
            name TEXT NOT NULL,
            parentId TEXT NULL,
            createdAt TEXT NOT NULL,
            modifiedAt TEXT NOT NULL,
            isDeleted INTEGER NOT NULL DEFAULT 0
        );
        CREATE INDEX IF NOT EXISTS idx_notebooks_parent ON notebooks(parentId);
        CREATE TABLE IF NOT EXISTS notes (
            id TEXT PRIMARY KEY,
            title TEXT NOT NULL,
            content TEXT NOT NULL DEFAULT '',
            documentId TEXT NULL,
            notebookId TEXT NULL,
            createdAt TEXT NOT NULL,
            modifiedAt TEXT NOT NULL,
            isDeleted INTEGER NOT NULL DEFAULT 0
        );
        CREATE INDEX IF NOT EXISTS idx_notes_document ON notes(documentId);
        CREATE INDEX IF NOT EXISTS idx_notes_notebook ON notes(notebookId);
        CREATE TABLE IF NOT EXISTS tags (
            id TEXT PRIMARY KEY,
            name TEXT NOT NULL UNIQUE,
            createdAt TEXT NOT NULL
        );
        """;

    private readonly string _dbPath;
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    /// <summary>Crea el acceso sobre el archivo indicado.</summary>
    public SqliteDatabase(string dbPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dbPath);
        _dbPath = dbPath;
    }

    /// <summary>Ruta del archivo.</summary>
    public string DbPath => _dbPath;

    /// <summary>Crea carpeta, esquema e índices si faltan (idempotente).</summary>
    public async Task EnsureCreatedAsync(CancellationToken ct)
    {
        var folder = Path.GetDirectoryName(_dbPath);
        if (!string.IsNullOrEmpty(folder))
        {
            try
            {
                Directory.CreateDirectory(folder);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or NotSupportedException)
            {
                throw new InvalidOperationException($"No se pudo crear la carpeta de base de datos.", ex);
            }
        }

        using var connection = Open();
        using var pragmas = connection.CreateCommand();
        pragmas.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA synchronous=NORMAL;
            PRAGMA foreign_keys=ON;
            PRAGMA temp_store=MEMORY;
            PRAGMA busy_timeout=5000;
            """;
        await pragmas.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

        using var schema = connection.CreateCommand();
        schema.CommandText = Schema;
        await schema.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Abre una conexión (el llamador la dispone).</summary>
    public SqliteConnection Open()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true,
        };
        var connection = new SqliteConnection(builder.ConnectionString);
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";
        pragma.ExecuteNonQuery();
        return connection;
    }

    /// <summary>Ejecuta una operación de escritura serializada.</summary>
    public async Task<T> WriteAsync<T>(Func<SqliteConnection, CancellationToken, Task<T>> action, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(action);
        await _writeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var connection = Open();
            return await action(connection, ct).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    /// <summary>Ejecuta una operación de escritura serializada sin resultado.</summary>
    public Task WriteAsync(Func<SqliteConnection, CancellationToken, Task> action, CancellationToken ct) =>
        WriteAsync(async (c, token) =>
        {
            await action(c, token).ConfigureAwait(false);
            return true;
        }, ct);

    /// <summary>Lectura ISO-8601 → <see cref="DateTimeOffset"/>.</summary>
    public static DateTimeOffset ReadDate(SqliteDataReader reader, int ordinal) =>
        DateTimeOffset.Parse(reader.GetString(ordinal), null, System.Globalization.DateTimeStyles.RoundtripKind);

    /// <summary>Lectura nulable de fecha.</summary>
    public static DateTimeOffset? ReadNullableDate(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : ReadDate(reader, ordinal);

    /// <summary>Lectura de Guid en texto.</summary>
    public static Guid ReadGuid(SqliteDataReader reader, int ordinal) => Guid.Parse(reader.GetString(ordinal));

    /// <summary>Lectura nulable de Guid.</summary>
    public static Guid? ReadNullableGuid(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : ReadGuid(reader, ordinal);

    /// <summary>Serializa una lista de Guid (columna tagIds).</summary>
    public static string WriteGuidList(IEnumerable<Guid> ids) =>
        JsonSerializer.Serialize(ids.ToList(), CacoJsonContext.Indented.ListGuid);

    /// <summary>Deserializa una lista de Guid (tolerante a filas corruptas).</summary>
    public static List<Guid> ReadGuidList(string json)
    {
        try
        {
            return JsonSerializer.Deserialize(json, CacoJsonContext.Indented.ListGuid) ?? [];
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>Serializa metadatos.</summary>
    public static string WriteMetadata(IReadOnlyDictionary<string, string> values) =>
        JsonSerializer.Serialize(new Dictionary<string, string>(values), CacoJsonContext.Indented.DictionaryStringString);

    /// <summary>Deserializa metadatos (tolerante a filas corruptas).</summary>
    public static Dictionary<string, string> ReadMetadata(string json)
    {
        try
        {
            return JsonSerializer.Deserialize(json, CacoJsonContext.Indented.DictionaryStringString) ?? new();
        }
        catch (Exception)
        {
            return new();
        }
    }
}
