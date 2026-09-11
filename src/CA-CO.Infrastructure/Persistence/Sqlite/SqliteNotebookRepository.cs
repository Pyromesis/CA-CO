using CaCo.Application.Repositories;
using CaCo.Application.Storage;
using CaCo.Core;
using CaCo.Domain;
using Microsoft.Data.Sqlite;

namespace CaCo.Infrastructure.Persistence.Sqlite;

/// <summary>Repositorio de cuadernos sobre SQLite (Fase 1).</summary>
public sealed class SqliteNotebookRepository : INotebookRepository
{
    private const string Columns = "id, name, parentId, createdAt, modifiedAt, isDeleted";

    private readonly SqliteDatabase _db;

    /// <summary>Crea el repositorio.</summary>
    public SqliteNotebookRepository(SqliteDatabase db)
    {
        ArgumentNullException.ThrowIfNull(db);
        _db = db;
    }

    /// <summary>Constructor para pruebas.</summary>
    internal SqliteNotebookRepository(string dbPath)
    {
        _db = new SqliteDatabase(dbPath);
    }

    /// <inheritdoc/>
    public async Task<Result<Notebook?>> GetByIdAsync(Guid id, bool includeDeleted, CancellationToken ct)
    {
        try
        {
            using var connection = _db.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = $"SELECT {Columns} FROM notebooks WHERE id = $id"
                + (includeDeleted ? "" : " AND isDeleted = 0");
            cmd.Parameters.AddWithValue("$id", id.ToString("D"));
            using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                return Result.Success<Notebook?>(null);
            }

            return Result.Success<Notebook?>(Read(reader));
        }
        catch (Exception ex)
        {
            return Result.Failure<Notebook?>(Error.Storage("NotebookRepository.ReadFailed", $"No se pudo leer el cuaderno: {ex.Message}"));
        }
    }

    /// <inheritdoc/>
    public async Task<Result<PagedResult<Notebook>>> ListAsync(NotebookQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        try
        {
            using var connection = _db.Open();
            using var cmd = connection.CreateCommand();
            var where = "1 = 1";
            if (!query.IncludeDeleted)
            {
                where += " AND isDeleted = 0";
            }

            if (query.ParentId.HasValue)
            {
                where += " AND parentId = $parent";
                cmd.Parameters.AddWithValue("$parent", query.ParentId.Value.ToString("D"));
            }
            else if (query.RootsOnly)
            {
                where += " AND parentId IS NULL";
            }

            if (!string.IsNullOrWhiteSpace(query.SearchText))
            {
                var search = query.SearchText.Trim();
                if (search.Length > 200)
                {
                    search = search[..200];
                }

                where += " AND name LIKE $search ESCAPE '\\'";
                cmd.Parameters.AddWithValue("$search", $"%{EscapeLike(search)}%");
            }

            cmd.CommandText = $"SELECT COUNT(*) FROM notebooks WHERE {where}";
            var total = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false));

            var page = Math.Clamp(query.Page, 1, 10_000);
            var pageSize = Math.Clamp(query.PageSize, 1, 200);
            cmd.CommandText = $"SELECT {Columns} FROM notebooks WHERE {where} ORDER BY name COLLATE NOCASE ASC LIMIT $limit OFFSET $offset";
            cmd.Parameters.AddWithValue("$limit", pageSize);
            cmd.Parameters.AddWithValue("$offset", checked((long)(page - 1) * pageSize));

            var items = new List<Notebook>();
            using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                items.Add(Read(reader));
            }

            return Result.Success(new PagedResult<Notebook>
            {
                Items = items,
                TotalCount = total,
                Page = page,
                PageSize = pageSize,
            });
        }
        catch (Exception ex)
        {
            return Result.Failure<PagedResult<Notebook>>(Error.Storage("NotebookRepository.ListFailed", $"No se pudo listar cuadernos: {ex.Message}"));
        }
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<Notebook>>> ListAllActiveAsync(CancellationToken ct)
    {
        try
        {
            using var connection = _db.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = $"SELECT {Columns} FROM notebooks WHERE isDeleted = 0 ORDER BY name COLLATE NOCASE ASC";
            var items = new List<Notebook>();
            using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                items.Add(Read(reader));
            }

            return Result.Success<IReadOnlyList<Notebook>>(items);
        }
        catch (Exception ex)
        {
            return Result.Failure<IReadOnlyList<Notebook>>(Error.Storage("NotebookRepository.ListFailed", $"No se pudo listar cuadernos: {ex.Message}"));
        }
    }

    /// <inheritdoc/>
    public async Task<Result<int>> CountActiveAsync(CancellationToken ct)
    {
        try
        {
            using var connection = _db.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM notebooks WHERE isDeleted = 0";
            return Result.Success(Convert.ToInt32(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false)));
        }
        catch (Exception ex)
        {
            return Result.Failure<int>(Error.Storage("NotebookRepository.CountFailed", $"No se pudo contar cuadernos: {ex.Message}"));
        }
    }

    /// <inheritdoc/>
    public async Task<Result> AddAsync(Notebook notebook, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(notebook);
        try
        {
            return await _db.WriteAsync(async (connection, token) =>
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = """
                    INSERT INTO notebooks (id, name, parentId, createdAt, modifiedAt, isDeleted)
                    VALUES ($id, $name, $parent, $created, $modified, $del)
                    """;
                Bind(cmd, notebook);
                try
                {
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
                catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
                {
                    return Result.Failure(Error.Conflict("NotebookRepository.Duplicate", "Ya existe un cuaderno con ese identificador."));
                }

                return Result.Success();
            }, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return Result.Failure(Error.Storage("NotebookRepository.AddFailed", $"No se pudo guardar el cuaderno: {ex.Message}"));
        }
    }

    /// <inheritdoc/>
    public async Task<Result> UpdateAsync(Notebook notebook, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(notebook);
        try
        {
            return await _db.WriteAsync(async (connection, token) =>
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = """
                    UPDATE notebooks SET name = $name, parentId = $parent, createdAt = $created,
                        modifiedAt = $modified, isDeleted = $del WHERE id = $id
                    """;
                Bind(cmd, notebook);
                var rows = await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                return rows == 0
                    ? Result.Failure(DomainErrors.Notebook.NotFound(notebook.Id))
                    : Result.Success();
            }, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return Result.Failure(Error.Storage("NotebookRepository.UpdateFailed", $"No se pudo actualizar el cuaderno: {ex.Message}"));
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
                cmd.CommandText = "DELETE FROM notebooks WHERE id = $id";
                cmd.Parameters.AddWithValue("$id", id.ToString("D"));
                var rows = await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                return rows == 0
                    ? Result.Failure(DomainErrors.Notebook.NotFound(id))
                    : Result.Success();
            }, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return Result.Failure(Error.Storage("NotebookRepository.DeleteFailed", $"No se pudo eliminar el cuaderno: {ex.Message}"));
        }
    }

    private static void Bind(SqliteCommand cmd, Notebook notebook)
    {
        cmd.Parameters.AddWithValue("$id", notebook.Id.ToString("D"));
        cmd.Parameters.AddWithValue("$name", notebook.Name);
        cmd.Parameters.AddWithValue("$parent", notebook.ParentId.HasValue ? notebook.ParentId.Value.ToString("D") : DBNull.Value);
        cmd.Parameters.AddWithValue("$created", notebook.CreatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("$modified", notebook.ModifiedAt.ToString("o"));
        cmd.Parameters.AddWithValue("$del", notebook.IsDeleted ? 1 : 0);
    }

    private static Notebook Read(SqliteDataReader r) => Notebook.Rehydrate(
        SqliteDatabase.ReadGuid(r, 0),
        r.GetString(1),
        SqliteDatabase.ReadNullableGuid(r, 2),
        SqliteDatabase.ReadDate(r, 3),
        SqliteDatabase.ReadDate(r, 4),
        r.GetInt32(5) == 1);

    private static string EscapeLike(string text) =>
        text.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
}
