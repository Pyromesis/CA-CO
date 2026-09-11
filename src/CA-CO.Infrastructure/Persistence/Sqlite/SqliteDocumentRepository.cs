using System.Text;
using CaCo.Application.Repositories;
using CaCo.Application.Storage;
using CaCo.Core;
using CaCo.Domain;
using Microsoft.Data.Sqlite;

namespace CaCo.Infrastructure.Persistence.Sqlite;

/// <summary>Repositorio de documentos sobre SQLite (Fase 1).</summary>
public sealed class SqliteDocumentRepository : IDocumentRepository
{
    private const string Columns = """
        id, name, originalFileName, storedFileName, originalStoredFileName, fileType,
        sizeBytes, createdAt, modifiedAt, importedAt, isFavorite, isDeleted, deletedAt,
        notebookId, tagIds, metadata, contentHash, isEncrypted
        """;

    private readonly SqliteDatabase _db;

    /// <summary>Crea el repositorio.</summary>
    public SqliteDocumentRepository(SqliteDatabase db)
    {
        ArgumentNullException.ThrowIfNull(db);
        _db = db;
    }

    /// <summary>Constructor para pruebas.</summary>
    internal SqliteDocumentRepository(string dbPath)
    {
        _db = new SqliteDatabase(dbPath);
    }

    /// <inheritdoc/>
    public async Task<Result<Document?>> GetByIdAsync(Guid id, bool includeDeleted, CancellationToken ct)
    {
        try
        {
            using var connection = _db.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = $"SELECT {Columns} FROM documents WHERE id = $id"
                + (includeDeleted ? "" : " AND isDeleted = 0");
            cmd.Parameters.AddWithValue("$id", id.ToString("D"));
            using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                return Result.Success<Document?>(null);
            }

            return Result.Success<Document?>(Read(reader));
        }
        catch (Exception ex)
        {
            return Result.Failure<Document?>(Error.Storage("DocumentRepository.ReadFailed", $"No se pudo leer el documento: {ex.Message}"));
        }
    }

    /// <inheritdoc/>
    public async Task<Result<PagedResult<Document>>> ListAsync(DocumentQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        try
        {
            var where = new StringBuilder("1 = 1");
            using var connection = _db.Open();
            using var cmd = connection.CreateCommand();

            if (query.DeletedOnly)
            {
                where.Append(" AND isDeleted = 1");
            }
            else if (!query.IncludeDeleted)
            {
                where.Append(" AND isDeleted = 0");
            }

            if (query.NotebookId.HasValue)
            {
                where.Append(" AND notebookId = $notebook");
                cmd.Parameters.AddWithValue("$notebook", query.NotebookId.Value.ToString("D"));
            }
            else if (query.UnclassifiedOnly)
            {
                where.Append(" AND notebookId IS NULL");
            }

            if (query.FavoritesOnly)
            {
                where.Append(" AND isFavorite = 1");
            }

            if (!string.IsNullOrWhiteSpace(query.SearchText))
            {
                var search = query.SearchText.Trim();
                if (search.Length > 200)
                {
                    search = search[..200];
                }

                where.Append(" AND name LIKE $search ESCAPE '\\'");
                cmd.Parameters.AddWithValue("$search", $"%{EscapeLike(search)}%");
            }

            var order = query.OrderBy switch
            {
                DocumentOrder.ModifiedDescending => "modifiedAt DESC",
                DocumentOrder.NameAscending => "name COLLATE NOCASE ASC",
                DocumentOrder.SizeDescending => "sizeBytes DESC",
                _ => "importedAt DESC",
            };

            var page = Math.Clamp(query.Page, 1, 10_000);
            var pageSize = Math.Clamp(query.PageSize, 1, 200);

            cmd.CommandText = $"SELECT COUNT(*) FROM documents WHERE {where}";
            var total = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false));

            cmd.CommandText = $"SELECT {Columns} FROM documents WHERE {where} ORDER BY {order} LIMIT $limit OFFSET $offset";
            cmd.Parameters.AddWithValue("$limit", pageSize);
            cmd.Parameters.AddWithValue("$offset", checked((long)(page - 1) * pageSize));

            var items = new List<Document>();
            using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                items.Add(Read(reader));
            }

            return Result.Success(new PagedResult<Document>
            {
                Items = items,
                TotalCount = total,
                Page = page,
                PageSize = pageSize,
            });
        }
        catch (Exception ex)
        {
            return Result.Failure<PagedResult<Document>>(Error.Storage("DocumentRepository.ListFailed", $"No se pudo listar documentos: {ex.Message}"));
        }
    }

    /// <inheritdoc/>
    public async Task<Result<int>> CountActiveAsync(CancellationToken ct)
    {
        try
        {
            using var connection = _db.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM documents WHERE isDeleted = 0";
            return Result.Success(Convert.ToInt32(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false)));
        }
        catch (Exception ex)
        {
            return Result.Failure<int>(Error.Storage("DocumentRepository.CountFailed", $"No se pudo contar documentos: {ex.Message}"));
        }
    }

    /// <inheritdoc/>
    public async Task<Result<long>> SumActiveBytesAsync(CancellationToken ct)
    {
        try
        {
            using var connection = _db.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT COALESCE(SUM(sizeBytes), 0) FROM documents WHERE isDeleted = 0";
            return Result.Success(Convert.ToInt64(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false)));
        }
        catch (Exception ex)
        {
            return Result.Failure<long>(Error.Storage("DocumentRepository.SumFailed", $"No se pudo calcular el tamaño: {ex.Message}"));
        }
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<Document>>> FindByHashAsync(string hash, CancellationToken ct)
    {
        try
        {
            using var connection = _db.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = $"SELECT {Columns} FROM documents WHERE isDeleted = 0 AND contentHash = $hash";
            cmd.Parameters.AddWithValue("$hash", hash);
            var items = new List<Document>();
            using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                items.Add(Read(reader));
            }

            return Result.Success<IReadOnlyList<Document>>(items);
        }
        catch (Exception ex)
        {
            return Result.Failure<IReadOnlyList<Document>>(Error.Storage("DocumentRepository.HashFailed", $"No se pudo buscar por hash: {ex.Message}"));
        }
    }

    /// <inheritdoc/>
    public async Task<Result> AddAsync(Document document, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(document);
        try
        {
            return await _db.WriteAsync(async (connection, token) =>
            {
                using var check = connection.CreateCommand();
                check.CommandText = "SELECT COUNT(*) FROM documents WHERE id = $id";
                check.Parameters.AddWithValue("$id", document.Id.ToString("D"));
                if (Convert.ToInt32(await check.ExecuteScalarAsync(token).ConfigureAwait(false)) > 0)
                {
                    return Result.Failure(Error.Conflict("DocumentRepository.Duplicate", "Ya existe un documento con ese identificador."));
                }

                using var cmd = connection.CreateCommand();
                cmd.CommandText = """
                    INSERT INTO documents (id, name, originalFileName, storedFileName, originalStoredFileName,
                        fileType, sizeBytes, createdAt, modifiedAt, importedAt, isFavorite, isDeleted, deletedAt,
                        notebookId, tagIds, metadata, contentHash, isEncrypted)
                    VALUES ($id, $name, $original, $stored, $originalStored, $type, $size, $created, $modified,
                        $imported, $fav, $del, $deletedAt, $notebook, $tags, $meta, $hash, $enc)
                    """;
                Bind(cmd, document);
                await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                return Result.Success();
            }, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return Result.Failure(Error.Storage("DocumentRepository.AddFailed", $"No se pudo guardar el documento: {ex.Message}"));
        }
    }

    /// <inheritdoc/>
    public async Task<Result> UpdateAsync(Document document, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(document);
        try
        {
            return await _db.WriteAsync(async (connection, token) =>
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = """
                    UPDATE documents SET name = $name, originalFileName = $original, storedFileName = $stored,
                        originalStoredFileName = $originalStored, fileType = $type, sizeBytes = $size,
                        createdAt = $created, modifiedAt = $modified, importedAt = $imported, isFavorite = $fav,
                        isDeleted = $del, deletedAt = $deletedAt, notebookId = $notebook, tagIds = $tags,
                        metadata = $meta, contentHash = $hash, isEncrypted = $enc
                    WHERE id = $id
                    """;
                Bind(cmd, document);
                var rows = await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                return rows == 0
                    ? Result.Failure(DomainErrors.Document.NotFound(document.Id))
                    : Result.Success();
            }, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return Result.Failure(Error.Storage("DocumentRepository.UpdateFailed", $"No se pudo actualizar el documento: {ex.Message}"));
        }
    }

    /// <inheritdoc/>
    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct)
    {
        try
        {
            return await _db.WriteAsync(async (connection, token) =>
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "DELETE FROM documents WHERE id = $id";
                cmd.Parameters.AddWithValue("$id", id.ToString("D"));
                var rows = await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                return rows == 0
                    ? Result.Failure(DomainErrors.Document.NotFound(id))
                    : Result.Success();
            }, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return Result.Failure(Error.Storage("DocumentRepository.DeleteFailed", $"No se pudo eliminar el documento: {ex.Message}"));
        }
    }

    private static void Bind(SqliteCommand cmd, Document doc)
    {
        cmd.Parameters.AddWithValue("$id", doc.Id.ToString("D"));
        cmd.Parameters.AddWithValue("$name", doc.Name);
        cmd.Parameters.AddWithValue("$original", doc.OriginalFileName);
        cmd.Parameters.AddWithValue("$stored", (object?)doc.StoredFileName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$originalStored", (object?)doc.OriginalStoredFileName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$type", (int)doc.FileType);
        cmd.Parameters.AddWithValue("$size", doc.SizeBytes);
        cmd.Parameters.AddWithValue("$created", doc.CreatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("$modified", doc.ModifiedAt.ToString("o"));
        cmd.Parameters.AddWithValue("$imported", doc.ImportedAt.ToString("o"));
        cmd.Parameters.AddWithValue("$fav", doc.IsFavorite ? 1 : 0);
        cmd.Parameters.AddWithValue("$del", doc.IsDeleted ? 1 : 0);
        cmd.Parameters.AddWithValue("$deletedAt", doc.DeletedAt.HasValue ? doc.DeletedAt.Value.ToString("o") : DBNull.Value);
        cmd.Parameters.AddWithValue("$notebook", doc.NotebookId.HasValue ? doc.NotebookId.Value.ToString("D") : DBNull.Value);
        cmd.Parameters.AddWithValue("$tags", SqliteDatabase.WriteGuidList(doc.TagIds));
        cmd.Parameters.AddWithValue("$meta", SqliteDatabase.WriteMetadata(doc.Metadata.Values));
        cmd.Parameters.AddWithValue("$hash", (object?)doc.ContentHash ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$enc", doc.IsEncrypted ? 1 : 0);
    }

    private static Document Read(SqliteDataReader r) => Document.Rehydrate(
        SqliteDatabase.ReadGuid(r, 0),
        r.GetString(1),
        r.GetString(2),
        r.IsDBNull(3) ? null : r.GetString(3),
        r.IsDBNull(4) ? null : r.GetString(4),
        (DocumentType)r.GetInt32(5),
        r.GetInt64(6),
        SqliteDatabase.ReadDate(r, 7),
        SqliteDatabase.ReadDate(r, 8),
        SqliteDatabase.ReadDate(r, 9),
        r.GetInt32(10) == 1,
        r.GetInt32(11) == 1,
        SqliteDatabase.ReadNullableDate(r, 12),
        SqliteDatabase.ReadNullableGuid(r, 13),
        SqliteDatabase.ReadGuidList(r.GetString(14)),
        SqliteDatabase.ReadMetadata(r.GetString(15)),
        r.IsDBNull(16) ? null : r.GetString(16),
        r.GetInt32(17) == 1);

    private static string EscapeLike(string text) =>
        text.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
}
