using CaCo.Application.Repositories;
using CaCo.Application.Storage;
using Microsoft.Extensions.Logging;

namespace CaCo.Infrastructure.Persistence.Sqlite;

/// <summary>Resultado de la migración única JSON → SQLite.</summary>
public sealed record MigrationResult
{
    /// <summary><c>true</c> si se migraron datos.</summary>
    public required bool Migrated { get; init; }

    /// <summary>Documentos migrados.</summary>
    public int Documents { get; init; }

    /// <summary>Cuadernos migrados.</summary>
    public int Notebooks { get; init; }

    /// <summary>Notas migradas.</summary>
    public int Notes { get; init; }

    /// <summary>Etiquetas migradas.</summary>
    public int Tags { get; init; }
}

/// <summary>
/// Migración única de Fase 0 (JSON) a Fase 1 (SQLite).
/// Solo actúa si la base SQLite está vacía y existen JSON con datos;
/// es idempotente y conserva los JSON como respaldo (no los borra).
/// </summary>
public sealed class JsonToSqliteMigrator(
    ILibraryPaths paths,
    SqliteDatabase db,
    IDocumentRepository documents,
    INotebookRepository notebooks,
    INoteRepository notes,
    ITagRepository tags,
    ILogger<JsonToSqliteMigrator> logger)
{
    /// <summary>Migra si corresponde. Nunca lanza (devuelve conteos en cero ante fallos).</summary>
    public async Task<MigrationResult> MigrateIfNeededAsync(CancellationToken ct)
    {
        try
        {
            await db.EnsureCreatedAsync(ct).ConfigureAwait(false);

            var existing = await documents.CountActiveAsync(ct).ConfigureAwait(false);
            if (existing.IsSuccess && existing.Value > 0)
            {
                return new MigrationResult { Migrated = false };
            }

            if (!File.Exists(paths.DocumentsDb) && !File.Exists(paths.NotebooksDb))
            {
                return new MigrationResult { Migrated = false };
            }

            var jsonDocs = new JsonDocumentRepository(paths);
            var jsonNotebooks = new JsonNotebookRepository(paths);
            var jsonNotes = new JsonNoteRepository(paths);
            var jsonTags = new JsonTagRepository(paths);

            var migrated = new MigrationResult
            {
                Migrated = true,
                Documents = await MigrateDocumentsAsync(jsonDocs, documents, ct).ConfigureAwait(false),
                Notebooks = await MigrateAllAsync(jsonNotebooks, notebooks, ct).ConfigureAwait(false),
                Notes = await MigrateNotesAsync(jsonNotes, notes, ct).ConfigureAwait(false),
                Tags = await MigrateTagsAsync(jsonTags, tags, ct).ConfigureAwait(false),
            };

            logger.LogInformation(
                "Migración JSON→SQLite: {Documents} documentos, {Notebooks} cuadernos.",
                migrated.Documents, migrated.Notebooks);
            return migrated;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Fallo la migración JSON→SQLite.");
            return new MigrationResult { Migrated = false };
        }
    }

    private static async Task<int> MigrateDocumentsAsync(
        JsonDocumentRepository source,
        IDocumentRepository target,
        CancellationToken ct)
    {
        var count = 0;
        var page = await source.ListAsync(
            new Application.Repositories.DocumentQuery
            {
                IncludeDeleted = true,
                Page = 1,
                PageSize = 200,
            }, ct).ConfigureAwait(false);
        if (page.IsFailure)
        {
            return 0;
        }

        while (page.Value.Items.Count > 0)
        {
            foreach (var item in page.Value.Items)
            {
                ct.ThrowIfCancellationRequested();
                if ((await target.AddAsync(item, ct).ConfigureAwait(false)).IsSuccess)
                {
                    count++;
                }
            }

            if (!page.Value.HasNextPage)
            {
                break;
            }

            page = await source.ListAsync(
                new Application.Repositories.DocumentQuery
                {
                    IncludeDeleted = true,
                    Page = page.Value.Page + 1,
                    PageSize = 200,
                }, ct).ConfigureAwait(false);
            if (page.IsFailure)
            {
                break;
            }
        }

        return count;
    }

    private async Task<int> MigrateAllAsync(
        JsonNotebookRepository source,
        INotebookRepository target,
        CancellationToken ct)
    {
        // Incluir papelera: ListAllActive perdía cuadernos eliminados.
        var count = 0;
        var page = 1;
        while (true)
        {
            var result = await source.ListAsync(
                new Application.Repositories.NotebookQuery
                {
                    IncludeDeleted = true,
                    Page = page,
                    PageSize = 200,
                }, ct).ConfigureAwait(false);
            if (result.IsFailure || result.Value.Items.Count == 0)
            {
                break;
            }

            foreach (var item in result.Value.Items)
            {
                ct.ThrowIfCancellationRequested();
                if ((await target.AddAsync(item, ct).ConfigureAwait(false)).IsSuccess)
                {
                    count++;
                }
            }

            if (!result.Value.HasNextPage)
            {
                break;
            }

            page++;
        }

        return count;
    }

    private async Task<int> MigrateNotesAsync(
        JsonNoteRepository source,
        INoteRepository target,
        CancellationToken ct)
    {
        // Las notas JSON solo se listan por documento/cuaderno: se enumeran
        // todos los documentos y cuadernos ya migrados y se recogen sus notas.
        // Las huérfanas puras (sin doc ni cuaderno) quedan en el JSON respaldo.
        try
        {
            var seen = new HashSet<Guid>();
            var count = 0;

            var docPage = await documents.ListAsync(
                new Application.Repositories.DocumentQuery
                {
                    IncludeDeleted = true,
                    Page = 1,
                    PageSize = 200,
                }, ct).ConfigureAwait(false);
            var docIds = new List<Guid>();
            while (docPage.IsSuccess && docPage.Value.Items.Count > 0)
            {
                docIds.AddRange(docPage.Value.Items.Select(d => d.Id));
                if (!docPage.Value.HasNextPage)
                {
                    break;
                }

                docPage = await documents.ListAsync(
                    new Application.Repositories.DocumentQuery
                    {
                        IncludeDeleted = true,
                        Page = docPage.Value.Page + 1,
                        PageSize = 200,
                    }, ct).ConfigureAwait(false);
            }

            foreach (var docId in docIds)
            {
                ct.ThrowIfCancellationRequested();
                var list = await source.ListByDocumentAsync(docId, ct).ConfigureAwait(false);
                if (list.IsFailure)
                {
                    continue;
                }

                foreach (var note in list.Value)
                {
                    if (seen.Add(note.Id) && (await target.AddAsync(note, ct).ConfigureAwait(false)).IsSuccess)
                    {
                        count++;
                    }
                }
            }

            var nbPage = await notebooks.ListAsync(
                new Application.Repositories.NotebookQuery
                {
                    IncludeDeleted = true,
                    Page = 1,
                    PageSize = 200,
                }, ct).ConfigureAwait(false);
            while (nbPage.IsSuccess && nbPage.Value.Items.Count > 0)
            {
                foreach (var nb in nbPage.Value.Items)
                {
                    ct.ThrowIfCancellationRequested();
                    var list = await source.ListByNotebookAsync(nb.Id, ct).ConfigureAwait(false);
                    if (list.IsFailure)
                    {
                        continue;
                    }

                    foreach (var note in list.Value)
                    {
                        if (seen.Add(note.Id) && (await target.AddAsync(note, ct).ConfigureAwait(false)).IsSuccess)
                        {
                            count++;
                        }
                    }
                }

                if (!nbPage.Value.HasNextPage)
                {
                    break;
                }

                nbPage = await notebooks.ListAsync(
                    new Application.Repositories.NotebookQuery
                    {
                        IncludeDeleted = true,
                        Page = nbPage.Value.Page + 1,
                        PageSize = 200,
                    }, ct).ConfigureAwait(false);
            }

            return count;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Migración de notas incompleta; el JSON queda como respaldo.");
            return 0;
        }
    }

    private async Task<int> MigrateTagsAsync(
        JsonTagRepository source,
        ITagRepository target,
        CancellationToken ct)
    {
        var all = await source.ListAllAsync(ct).ConfigureAwait(false);
        if (all.IsFailure)
        {
            return 0;
        }

        var count = 0;
        foreach (var item in all.Value)
        {
            ct.ThrowIfCancellationRequested();
            if ((await target.AddAsync(item, ct).ConfigureAwait(false)).IsSuccess)
            {
                count++;
            }
        }

        return count;
    }
}
