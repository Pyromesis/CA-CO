using CaCo.Application.Repositories;
using CaCo.Core;
using CaCo.Domain;
using Microsoft.Data.Sqlite;

namespace CaCo.Infrastructure.Persistence.Sqlite;

/// <summary>Repositorio de notas sobre SQLite (Fase 1).</summary>
public sealed class SqliteNoteRepository : INoteRepository
{
    private const string Columns = "id, title, content, documentId, notebookId, createdAt, modifiedAt, isDeleted";

    private readonly SqliteDatabase _db;

    /// <summary>Crea el repositorio.</summary>
    public SqliteNoteRepository(SqliteDatabase db)
    {
        ArgumentNullException.ThrowIfNull(db);
        _db = db;
    }

    /// <inheritdoc/>
    public async Task<Result<Note?>> GetByIdAsync(Guid id, bool includeDeleted, CancellationToken ct)
    {
        try
        {
            using var connection = _db.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = $"SELECT {Columns} FROM notes WHERE id = $id"
                + (includeDeleted ? "" : " AND isDeleted = 0");
            cmd.Parameters.AddWithValue("$id", id.ToString("D"));
            using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                return Result.Success<Note?>(null);
            }

            return Result.Success<Note?>(Read(reader));
        }
        catch (Exception ex)
        {
            return Result.Failure<Note?>(Error.Storage("NoteRepository.ReadFailed", $"No se pudo leer la nota: {ex.Message}"));
        }
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<Note>>> ListByDocumentAsync(Guid documentId, CancellationToken ct) =>
        await ListByAsync("documentId", documentId, ct).ConfigureAwait(false);

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<Note>>> ListByNotebookAsync(Guid notebookId, CancellationToken ct) =>
        await ListByAsync("notebookId", notebookId, ct).ConfigureAwait(false);

    private async Task<Result<IReadOnlyList<Note>>> ListByAsync(string column, Guid id, CancellationToken ct)
    {
        var safeColumn = column switch
        {
            "documentId" => "documentId",
            "notebookId" => "notebookId",
            _ => throw new ArgumentException("Columna no permitida.", nameof(column)),
        };

        try
        {
            using var connection = _db.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = $"SELECT {Columns} FROM notes WHERE isDeleted = 0 AND {safeColumn} = $id ORDER BY modifiedAt DESC";
            cmd.Parameters.AddWithValue("$id", id.ToString("D"));
            var items = new List<Note>();
            using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                items.Add(Read(reader));
            }

            return Result.Success<IReadOnlyList<Note>>(items);
        }
        catch (Exception ex)
        {
            return Result.Failure<IReadOnlyList<Note>>(Error.Storage("NoteRepository.ListFailed", $"No se pudieron listar notas: {ex.Message}"));
        }
    }

    /// <inheritdoc/>
    public async Task<Result> AddAsync(Note note, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(note);
        try
        {
            return await _db.WriteAsync(async (connection, token) =>
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = """
                    INSERT INTO notes (id, title, content, documentId, notebookId, createdAt, modifiedAt, isDeleted)
                    VALUES ($id, $title, $content, $doc, $notebook, $created, $modified, $del)
                    """;
                Bind(cmd, note);
                try
                {
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
                catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
                {
                    return Result.Failure(Error.Conflict("NoteRepository.Duplicate", "Ya existe una nota con ese identificador."));
                }

                return Result.Success();
            }, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return Result.Failure(Error.Storage("NoteRepository.AddFailed", $"No se pudo guardar la nota: {ex.Message}"));
        }
    }

    /// <inheritdoc/>
    public async Task<Result> UpdateAsync(Note note, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(note);
        try
        {
            return await _db.WriteAsync(async (connection, token) =>
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = """
                    UPDATE notes SET title = $title, content = $content, documentId = $doc,
                        notebookId = $notebook, createdAt = $created, modifiedAt = $modified,
                        isDeleted = $del WHERE id = $id
                    """;
                Bind(cmd, note);
                var rows = await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                return rows == 0
                    ? Result.Failure(DomainErrors.Note.NotFound(note.Id))
                    : Result.Success();
            }, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return Result.Failure(Error.Storage("NoteRepository.UpdateFailed", $"No se pudo actualizar la nota: {ex.Message}"));
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
                cmd.CommandText = "DELETE FROM notes WHERE id = $id";
                cmd.Parameters.AddWithValue("$id", id.ToString("D"));
                var rows = await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                return rows == 0
                    ? Result.Failure(DomainErrors.Note.NotFound(id))
                    : Result.Success();
            }, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return Result.Failure(Error.Storage("NoteRepository.DeleteFailed", $"No se pudo eliminar la nota: {ex.Message}"));
        }
    }

    private static void Bind(SqliteCommand cmd, Note note)
    {
        cmd.Parameters.AddWithValue("$id", note.Id.ToString("D"));
        cmd.Parameters.AddWithValue("$title", note.Title);
        cmd.Parameters.AddWithValue("$content", note.Content);
        cmd.Parameters.AddWithValue("$doc", note.DocumentId.HasValue ? note.DocumentId.Value.ToString("D") : DBNull.Value);
        cmd.Parameters.AddWithValue("$notebook", note.NotebookId.HasValue ? note.NotebookId.Value.ToString("D") : DBNull.Value);
        cmd.Parameters.AddWithValue("$created", note.CreatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("$modified", note.ModifiedAt.ToString("o"));
        cmd.Parameters.AddWithValue("$del", note.IsDeleted ? 1 : 0);
    }

    private static Note Read(SqliteDataReader r) => Note.Rehydrate(
        SqliteDatabase.ReadGuid(r, 0),
        r.GetString(1),
        r.GetString(2),
        SqliteDatabase.ReadNullableGuid(r, 3),
        SqliteDatabase.ReadNullableGuid(r, 4),
        SqliteDatabase.ReadDate(r, 5),
        SqliteDatabase.ReadDate(r, 6),
        r.GetInt32(7) == 1);
}

/// <summary>Repositorio de etiquetas sobre SQLite (Fase 1).</summary>
public sealed class SqliteTagRepository : ITagRepository
{
    private readonly SqliteDatabase _db;

    /// <summary>Crea el repositorio.</summary>
    public SqliteTagRepository(SqliteDatabase db)
    {
        ArgumentNullException.ThrowIfNull(db);
        _db = db;
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<Tag>>> ListAllAsync(CancellationToken ct)
    {
        try
        {
            using var connection = _db.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT id, name, createdAt FROM tags ORDER BY name COLLATE NOCASE ASC";
            var items = new List<Tag>();
            using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                items.Add(Tag.Rehydrate(
                    SqliteDatabase.ReadGuid(reader, 0),
                    reader.GetString(1),
                    SqliteDatabase.ReadDate(reader, 2)));
            }

            return Result.Success<IReadOnlyList<Tag>>(items);
        }
        catch (Exception ex)
        {
            return Result.Failure<IReadOnlyList<Tag>>(Error.Storage("TagRepository.ListFailed", $"No se pudieron listar etiquetas: {ex.Message}"));
        }
    }

    /// <inheritdoc/>
    public async Task<Result<Tag?>> GetByNameAsync(string name, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure<Tag?>(DomainErrors.Tag.InvalidName());
        }

        try
        {
            var normalized = name.Trim().ToLowerInvariant();
            using var connection = _db.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT id, name, createdAt FROM tags WHERE name = $name COLLATE NOCASE";
            cmd.Parameters.AddWithValue("$name", normalized);
            using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                return Result.Success<Tag?>(null);
            }

            return Result.Success<Tag?>(Tag.Rehydrate(
                SqliteDatabase.ReadGuid(reader, 0),
                reader.GetString(1),
                SqliteDatabase.ReadDate(reader, 2)));
        }
        catch (Exception ex)
        {
            return Result.Failure<Tag?>(Error.Storage("TagRepository.ReadFailed", $"No se pudo leer la etiqueta: {ex.Message}"));
        }
    }

    /// <inheritdoc/>
    public async Task<Result> AddAsync(Tag tag, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tag);
        try
        {
            return await _db.WriteAsync(async (connection, token) =>
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "INSERT INTO tags (id, name, createdAt) VALUES ($id, $name, $created)";
                cmd.Parameters.AddWithValue("$id", tag.Id.ToString("D"));
                cmd.Parameters.AddWithValue("$name", tag.Name);
                cmd.Parameters.AddWithValue("$created", tag.CreatedAt.ToString("o"));
                try
                {
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
                catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
                {
                    return Result.Failure(Error.Conflict("TagRepository.Duplicate", "Ya existe una etiqueta con ese identificador o nombre."));
                }

                return Result.Success();
            }, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return Result.Failure(Error.Storage("TagRepository.AddFailed", $"No se pudo guardar la etiqueta: {ex.Message}"));
        }
    }
}
