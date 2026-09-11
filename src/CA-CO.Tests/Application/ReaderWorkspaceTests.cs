using CaCo.Application.Configuration;
using CaCo.Application.Services;
using CaCo.Core;
using CaCo.Domain;
using CaCo.Infrastructure.DependencyInjection;
using CaCo.Infrastructure.Import;
using CaCo.Infrastructure.Persistence.Sqlite;
using CaCo.Infrastructure.Storage;
using CaCo.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace CaCo.Tests.Application;

/// <summary>Pruebas del espacio de trabajo: edición de texto, notas y escritura.</summary>
public sealed class ReaderWorkspaceTests
{
    private sealed record Harness(
        DocumentService Documents,
        NoteService Notes,
        LibraryPaths Paths);

    private static async Task<Harness> CreateAsync(TempDirectory temp, CancellationToken ct)
    {
        var paths = new LibraryPaths(Path.Combine(temp.Path, "Lib"));
        Directory.CreateDirectory(paths.Documents);
        var db = new SqliteDatabase(Path.Combine(paths.Database, "caco.db"));
        await db.EnsureCreatedAsync(ct);

        var documents = new SqliteDocumentRepository(db);
        var notebooks = new SqliteNotebookRepository(db);
        var tags = new SqliteTagRepository(db);
        var noteRepo = new SqliteNoteRepository(db);
        var clock = new TestClock(DateTimeOffset.UtcNow);
        var storage = new PhysicalFileStorage(NullLogger<PhysicalFileStorage>.Instance);
        var service = new DocumentService(
            documents, notebooks, tags, storage, paths,
            new NullThumbnailService(), clock, NullLogger<DocumentService>.Instance);
        var notes = new NoteService(noteRepo, documents, clock, NullLogger<NoteService>.Instance);
        return new Harness(service, notes, paths);
    }

    private static async Task<Document> SeedTxtAsync(Harness h, TempDirectory temp, CancellationToken ct)
    {
        var source = temp.CreateFile("nota.txt", "línea uno\nlínea dos");
        var importer = new LocalDocumentImporter(
            new FileTypeValidator(),
            new PhysicalFileStorage(NullLogger<PhysicalFileStorage>.Instance),
            h.Paths,
            new SqliteDocumentRepository(new SqliteDatabase(Path.Combine(h.Paths.Database, "caco.db"))),
            new SqliteNotebookRepository(new SqliteDatabase(Path.Combine(h.Paths.Database, "caco.db"))),
            new NullThumbnailService(),
            new CacoSettings(),
            new TestClock(DateTimeOffset.UtcNow),
            NullLogger<LocalDocumentImporter>.Instance);
        var result = await importer.ImportAsync(new CaCo.Application.Import.ImportRequest(source), ct);
        Assert.True(result.Value.Succeeded);
        return result.Value.Document!;
    }

    [Fact]
    public async Task SaveTextContent_UpdatesFileSizeAndHash()
    {
        using var temp = new TempDirectory();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var h = await CreateAsync(temp, cts.Token);
        var doc = await SeedTxtAsync(h, temp, cts.Token);
        var oldHash = doc.ContentHash;

        Assert.True((await h.Documents.SaveTextContentAsync(doc.Id, "contenido nuevo", cts.Token)).IsSuccess);

        var reloaded = await h.Documents.GetByIdAsync(doc.Id, false, cts.Token);
        Assert.Equal("contenido nuevo", File.ReadAllText(Path.Combine(h.Paths.Documents, reloaded.Value!.StoredFileName!)));
        Assert.NotEqual(oldHash, reloaded.Value.ContentHash);
        Assert.True(reloaded.Value.SizeBytes > 0);
    }

    [Fact]
    public async Task SaveTextContent_NonTxt_Fails()
    {
        using var temp = new TempDirectory();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var h = await CreateAsync(temp, cts.Token);

        var pdf = Document.Create("Doc", "d.pdf", 10).Value;
        var db = new SqliteDatabase(Path.Combine(h.Paths.Database, "caco.db"));
        await new SqliteDocumentRepository(db).AddAsync(pdf, cts.Token);

        // Usa otro servicio atado a la misma base para no duplicar harness.
        var result = await h.Documents.SaveTextContentAsync(pdf.Id, "x", cts.Token);
        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task Notes_AddListDelete_Works()
    {
        using var temp = new TempDirectory();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var h = await CreateAsync(temp, cts.Token);
        var doc = await SeedTxtAsync(h, temp, cts.Token);

        var added = await h.Notes.AddToDocumentAsync(doc.Id, "revisar este párrafo", cts.Token);
        Assert.True(added.IsSuccess);

        var listed = await h.Notes.ListByDocumentAsync(doc.Id, cts.Token);
        Assert.Single(listed.Value);

        Assert.True((await h.Notes.DeleteAsync(added.Value.Id, cts.Token)).IsSuccess);
        listed = await h.Notes.ListByDocumentAsync(doc.Id, cts.Token);
        Assert.Empty(listed.Value);
    }

    [Fact]
    public async Task Notes_EmptyContent_Fails()
    {
        using var temp = new TempDirectory();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var h = await CreateAsync(temp, cts.Token);
        var doc = await SeedTxtAsync(h, temp, cts.Token);

        var result = await h.Notes.AddToDocumentAsync(doc.Id, "   ", cts.Token);
        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task Notes_Update_ChangesContent()
    {
        using var temp = new TempDirectory();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var h = await CreateAsync(temp, cts.Token);
        var doc = await SeedTxtAsync(h, temp, cts.Token);

        var added = await h.Notes.AddToDocumentAsync(doc.Id, "original", cts.Token);
        Assert.True((await h.Notes.UpdateAsync(added.Value.Id, "editado", cts.Token)).IsSuccess);

        var listed = await h.Notes.ListByDocumentAsync(doc.Id, cts.Token);
        Assert.Equal("editado", listed.Value[0].Content);

        Assert.True((await h.Notes.UpdateAsync(added.Value.Id, "  ", cts.Token)).IsFailure);
    }

    [Fact]
    public async Task Storage_WriteTextAndBytes_Roundtrip()
    {
        using var temp = new TempDirectory();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var storage = new PhysicalFileStorage(NullLogger<PhysicalFileStorage>.Instance);

        await storage.WriteAllTextAsync(temp.Path, "a.txt", "hola", cts.Token);
        Assert.Equal("hola", File.ReadAllText(Path.Combine(temp.Path, "a.txt")));

        await storage.WriteAllBytesAsync(temp.Path, "b.bin", [1, 2, 3], cts.Token);
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(temp.Path, "b.bin")));
    }

    [Fact]
    public void RefreshSizeAndHash_Validates()
    {
        var doc = Document.Create("D", "d.txt", 1).Value;

        Assert.True(doc.RefreshSizeAndHash(10, "ABC").IsSuccess);
        Assert.Equal(10, doc.SizeBytes);
        Assert.True(doc.RefreshSizeAndHash(-1, "ABC").IsFailure);
        Assert.True(doc.RefreshSizeAndHash(10, "  ").IsFailure);
    }

    [Fact]
    public async Task RefreshFileMetadata_DetectsExternalChanges()
    {
        using var temp = new TempDirectory();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var h = await CreateAsync(temp, cts.Token);
        var doc = await SeedTxtAsync(h, temp, cts.Token);

        // Cambio externo (p. ej. anotación guardada con Ctrl+S en el visor).
        File.WriteAllText(Path.Combine(h.Paths.Documents, doc.StoredFileName!), "modificado fuera");

        var result = await h.Documents.RefreshFileMetadataAsync(doc.Id, cts.Token);
        Assert.True(result.IsSuccess);
        Assert.True(result.Value);

        var reloaded = await h.Documents.GetByIdAsync(doc.Id, false, cts.Token);
        Assert.NotEqual(doc.ContentHash, reloaded.Value!.ContentHash);

        // Sin cambios: informa false.
        var again = await h.Documents.RefreshFileMetadataAsync(doc.Id, cts.Token);
        Assert.True(again.IsSuccess);
        Assert.False(again.Value);
    }
}
