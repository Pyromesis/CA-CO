using CaCo.Application.Configuration;
using CaCo.Application.Import;
using CaCo.Application.Services;
using CaCo.Core;
using CaCo.Domain;
using CaCo.Infrastructure.Configuration;
using CaCo.Infrastructure.DependencyInjection;
using CaCo.Infrastructure.Import;
using CaCo.Infrastructure.Persistence;
using CaCo.Infrastructure.Persistence.Sqlite;
using CaCo.Infrastructure.Storage;
using CaCo.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace CaCo.Tests.Application;

/// <summary>Pruebas Fase 1: hash/duplicados, copias originales, tags y migración.</summary>
public sealed class Phase1Tests
{
    private sealed record Harness(
        LocalDocumentImporter Importer,
        DocumentService Documents,
        LibraryPaths Paths,
        SqliteDatabase Db);

    private static async Task<Harness> CreateAsync(TempDirectory temp, CancellationToken ct)
    {
        var paths = new LibraryPaths(Path.Combine(temp.Path, "Lib"));
        Directory.CreateDirectory(paths.Documents);
        Directory.CreateDirectory(paths.Originals);
        var db = new SqliteDatabase(Path.Combine(paths.Database, "caco.db"));
        await db.EnsureCreatedAsync(ct);

        var documents = new SqliteDocumentRepository(db);
        var notebooks = new SqliteNotebookRepository(db);
        var tags = new SqliteTagRepository(db);
        var clock = new TestClock(DateTimeOffset.UtcNow);
        var importer = new LocalDocumentImporter(
            new FileTypeValidator(),
            new PhysicalFileStorage(NullLogger<PhysicalFileStorage>.Instance),
            paths,
            documents,
            notebooks,
            new NullThumbnailService(),
            NullMediaInspector.Instance,
            new CacoSettings(),
            clock,
            NullLogger<LocalDocumentImporter>.Instance);
        var service = new DocumentService(
            documents, notebooks, tags,
            new PhysicalFileStorage(NullLogger<PhysicalFileStorage>.Instance),
            paths, new NullThumbnailService(), clock,
            NullLogger<DocumentService>.Instance);
        return new Harness(importer, service, paths, db);
    }

    [Fact]
    public async Task Import_SetsHash_AndDetectsSameContentWithDifferentName()
    {
        using var temp = new TempDirectory();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var h = await CreateAsync(temp, cts.Token);

        var first = temp.CreateFile("uno.txt", "contenido idéntico");
        var second = temp.CreateFile("dos.txt", "contenido idéntico");

        var r1 = await h.Importer.ImportAsync(new ImportRequest(first), cts.Token);
        var r2 = await h.Importer.ImportAsync(new ImportRequest(second), cts.Token);

        Assert.True(r1.Value.Succeeded);
        Assert.NotNull(r1.Value.Document!.ContentHash);
        Assert.False(r2.Value.Succeeded);
        Assert.True(r2.Value.SkippedAsDuplicate);
    }

    [Fact]
    public async Task Import_KeepsOriginal_AndDeleteRemovesBothCopies()
    {
        using var temp = new TempDirectory();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var h = await CreateAsync(temp, cts.Token);

        var source = temp.CreateFile("orig.txt", "algo");
        var result = await h.Importer.ImportAsync(new ImportRequest(source), cts.Token);
        var doc = result.Value.Document!;
        Assert.NotNull(doc.OriginalStoredFileName);
        Assert.True(File.Exists(Path.Combine(h.Paths.Originals, doc.OriginalStoredFileName)));
        Assert.True(File.Exists(Path.Combine(h.Paths.Documents, doc.StoredFileName!)));

        Assert.True((await h.Documents.DeletePermanentlyAsync(doc.Id, cts.Token)).IsSuccess);
        Assert.False(File.Exists(Path.Combine(h.Paths.Originals, doc.OriginalStoredFileName)));
        Assert.False(File.Exists(Path.Combine(h.Paths.Documents, doc.StoredFileName!)));
    }

    [Fact]
    public async Task Tags_AddListRemove_Works()
    {
        using var temp = new TempDirectory();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var h = await CreateAsync(temp, cts.Token);

        var source = temp.CreateFile("t.txt", "x");
        var doc = (await h.Importer.ImportAsync(new ImportRequest(source), cts.Token)).Value.Document!;

        Assert.True((await h.Documents.AddTagAsync(doc.Id, "Importante", cts.Token)).IsSuccess);
        // Idempotente a nivel de nombre (misma etiqueta).
        Assert.True((await h.Documents.AddTagAsync(doc.Id, "importante", cts.Token)).IsSuccess);

        var all = await h.Documents.ListAllTagsAsync(cts.Token);
        Assert.Single(all.Value);

        var reloaded = await h.Documents.GetByIdAsync(doc.Id, false, cts.Token);
        Assert.Single(reloaded.Value!.TagIds);

        Assert.True((await h.Documents.RemoveTagAsync(doc.Id, all.Value[0].Id, cts.Token)).IsSuccess);
        reloaded = await h.Documents.GetByIdAsync(doc.Id, false, cts.Token);
        Assert.Empty(reloaded.Value!.TagIds);
    }

    [Fact]
    public async Task Migration_JsonToSqlite_MovesDataOnce()
    {
        using var temp = new TempDirectory();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var root = Path.Combine(temp.Path, "Lib");
        var paths = new LibraryPaths(root);

        // Siembra JSON (Fase 0).
        var jsonDocs = new JsonDocumentRepository(paths);
        var jsonNotebooks = new JsonNotebookRepository(paths);
        var notebook = Notebook.Create("Migrado").Value;
        Assert.True((await jsonNotebooks.AddAsync(notebook, cts.Token)).IsSuccess);
        var doc = Document.Create("Viejo", "v.pdf", 50, notebook.Id).Value;
        Assert.True((await jsonDocs.AddAsync(doc, cts.Token)).IsSuccess);

        // Migra.
        var db = new SqliteDatabase(Path.Combine(paths.Database, "caco.db"));
        var migrator = new JsonToSqliteMigrator(
            paths, db,
            new SqliteDocumentRepository(db),
            new SqliteNotebookRepository(db),
            new SqliteNoteRepository(db),
            new SqliteTagRepository(db),
            NullLogger<JsonToSqliteMigrator>.Instance);

        var first = await migrator.MigrateIfNeededAsync(cts.Token);
        Assert.True(first.Migrated);
        Assert.Equal(1, first.Documents);
        Assert.Equal(1, first.Notebooks);

        // Segunda vez: no duplica.
        var second = await migrator.MigrateIfNeededAsync(cts.Token);
        Assert.False(second.Migrated);

        var sqliteDocs = new SqliteDocumentRepository(db);
        Assert.Equal(1, (await sqliteDocs.CountActiveAsync(cts.Token)).Value);
    }

    [Fact]
    public void StorageProbe_WritableAndInvalid_Behaves()
    {
        using var temp = new TempDirectory();

        Assert.True(StorageProbe.IsWritable(Path.Combine(temp.Path, "nueva")));
        Assert.False(StorageProbe.IsWritable(string.Empty));
    }

    [Fact]
    public void ResolveEffective_FallsBack_WhenNotWritable()
    {
        var fallback = LibraryRootResolver.ResolveEffective(
            Path.Combine("Z:", "no-existe-seguro", "x"),
            null,
            _ => false);

        Assert.Equal(AppPaths.DefaultLibraryRoot, fallback);
    }

    [Fact]
    public void ResolveEffective_HonorsDefaultRoot()
    {
        var custom = Path.Combine(Path.GetTempPath(), "caco-custom-default");
        var result = LibraryRootResolver.ResolveEffective(null, custom, _ => false);

        Assert.Equal(custom, result);
    }

    [Fact]
    public async Task LegacyMigrator_MovesContentOnce()
    {
        using var temp = new TempDirectory();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var legacy = Path.Combine(temp.Path, "legacy");
        var fresh = Path.Combine(temp.Path, "fresh");
        Directory.CreateDirectory(Path.Combine(legacy, "Documents"));
        File.WriteAllText(Path.Combine(legacy, "Documents", "a.txt"), "x");

        var migrator = new LegacyVfsMigrator(NullLogger<LegacyVfsMigrator>.Instance);
        Assert.True(await migrator.MigrateIfNeededAsync(legacy, fresh, cts.Token));
        Assert.True(File.Exists(Path.Combine(fresh, "Documents", "a.txt")));

        // Segunda vez: ya hay Database? No: sin Database en destino y legado vacío → false.
        Assert.False(await migrator.MigrateIfNeededAsync(legacy, fresh, cts.Token));
    }
}
