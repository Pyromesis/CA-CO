using CaCo.Application.Ocr;
using CaCo.Application.Search;
using CaCo.Application.Services;
using CaCo.Core;
using CaCo.Domain;
using CaCo.Infrastructure.Persistence;
using CaCo.Infrastructure.Persistence.Sqlite;
using CaCo.Infrastructure.Search;
using CaCo.Infrastructure.Storage;
using CaCo.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace CaCo.Tests.Application;

/// <summary>Pruebas Fase 6: búsqueda multi-campo con relevancia y filtros.</summary>
public sealed class SearchPhaseTests
{
    private sealed record Harness(
        AdvancedSearchService Search,
        SqliteDocumentRepository Documents,
        SqliteTagRepository Tags,
        SqliteNoteRepository Notes,
        TempDirectory Temp);

    private static Harness Create()
    {
        var temp = new TempDirectory();
        var paths = new LibraryPaths(Path.Combine(temp.Path, "Lib"));
        Directory.CreateDirectory(paths.Database);
        var db = new SqliteDatabase(Path.Combine(paths.Database, "caco.db"));
        db.EnsureCreatedAsync(CancellationToken.None).GetAwaiter().GetResult();
        var documents = new SqliteDocumentRepository(db);
        var notebooks = new SqliteNotebookRepository(db);
        var tags = new SqliteTagRepository(db);
        var notes = new SqliteNoteRepository(db);
        var search = new AdvancedSearchService(
            documents, notebooks, notes, tags, NullLogger<AdvancedSearchService>.Instance);
        return new Harness(search, documents, tags, notes, temp);
    }

    private static async Task SeedAsync(Harness h, CancellationToken ct)
    {
        var manual = Document.Create("Manual de cocina", "manual.pdf", 100).Value;
        var foto = Document.Create("Foto playa", "foto.png", 100).Value;
        foto.SetFavorite(true, DateTimeOffset.UtcNow);
        var apuntes = Document.Create("Apuntes varios", "a.txt", 100).Value;
        apuntes.Metadata.Set(OcrMetadataKeys.Excerpt, "texto extraído del sombrero");
        foreach (var d in new[] { manual, foto, apuntes })
        {
            Assert.True((await h.Documents.AddAsync(d, ct)).IsSuccess);
        }

        var tag = Tag.Create("Importante").Value;
        Assert.True((await h.Tags.AddAsync(tag, ct)).IsSuccess);
        manual.AddTag(tag.Id);
        Assert.True((await h.Documents.UpdateAsync(manual, ct)).IsSuccess);

        var note = Note.Create("N", "llevamos la sombrilla a la playa", documentId: foto.Id).Value;
        Assert.True((await h.Notes.AddAsync(note, ct)).IsSuccess);
    }

    [Fact]
    public async Task NameMatch_BeatsNoteMatch()
    {
        var h = Create();
        using (h.Temp)
        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
        {
            await SeedAsync(h, cts.Token);
            // "playa": Foto por nombre (70) y por nota (50); Manual no coincide.
            var result = await h.Search.SearchAdvancedAsync(
                new SearchQuery { Text = "playa" }, cts.Token);
            Assert.True(result.IsSuccess);
            var first = Assert.Single(result.Value);
            Assert.Equal("Foto playa", first.Document.Name);
            Assert.Equal(70, first.Score);
            Assert.Contains("nombre", first.MatchedIn);
            Assert.Contains("nota", first.MatchedIn);
        }
    }

    [Fact]
    public async Task TagAndOcr_Match()
    {
        var h = Create();
        using (h.Temp)
        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
        {
            await SeedAsync(h, cts.Token);
            var byTag = await h.Search.SearchAdvancedAsync(
                new SearchQuery { Text = "importante" }, cts.Token);
            Assert.Equal("Manual de cocina", Assert.Single(byTag.Value).Document.Name);
            Assert.Contains("etiqueta", byTag.Value[0].MatchedIn);

            var byOcr = await h.Search.SearchAdvancedAsync(
                new SearchQuery { Text = "sombrero" }, cts.Token);
            Assert.Equal("Apuntes varios", Assert.Single(byOcr.Value).Document.Name);
            Assert.Contains("OCR", byOcr.Value[0].MatchedIn);
        }
    }

    [Fact]
    public async Task Filters_TypeFavoritesDates()
    {
        var h = Create();
        using (h.Temp)
        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
        {
            await SeedAsync(h, cts.Token);
            var pdfs = await h.Search.SearchAdvancedAsync(
                new SearchQuery { Text = "a", FileTypes = [DocumentType.Pdf] }, cts.Token);
            Assert.All(pdfs.Value, x => Assert.Equal(DocumentType.Pdf, x.Document.FileType));

            var favs = await h.Search.SearchAdvancedAsync(
                new SearchQuery { Text = "a", FavoritesOnly = true }, cts.Token);
            Assert.Equal("Foto playa", Assert.Single(favs.Value).Document.Name);

            var future = await h.Search.SearchAdvancedAsync(
                new SearchQuery { Text = "a", FromUtc = DateTimeOffset.UtcNow.AddDays(1) }, cts.Token);
            Assert.Empty(future.Value);

            var byTag = await h.Search.SearchAdvancedAsync(
                new SearchQuery { Text = "", Tag = "IMPORTANTE" }, cts.Token);
            Assert.Equal("Manual de cocina", Assert.Single(byTag.Value).Document.Name);
        }
    }

    [Fact]
    public async Task EmptyQuery_ReturnsEmpty()
    {
        var h = Create();
        using (h.Temp)
        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
        {
            await SeedAsync(h, cts.Token);
            var result = await h.Search.SearchAdvancedAsync(
                new SearchQuery { Text = "   " }, cts.Token);
            Assert.True(result.IsSuccess);
            Assert.Empty(result.Value);
        }
    }

    [Fact]
    public async Task ListAllNotes_SqliteAndJson()
    {
        var h = Create();
        using (h.Temp)
        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
        {
            var linked = Note.Create("L", "x", documentId: Guid.NewGuid()).Value;
            var loose = Note.Create("S", "y").Value;
            Assert.True((await h.Notes.AddAsync(linked, cts.Token)).IsSuccess);
            Assert.True((await h.Notes.AddAsync(loose, cts.Token)).IsSuccess);
            var all = await h.Notes.ListAllAsync(cts.Token);
            Assert.Equal(2, all.Value.Count);

            var paths = new LibraryPaths(Path.Combine(h.Temp.Path, "Json"));
            var json = new JsonNoteRepository(paths);
            Assert.True((await json.AddAsync(linked, cts.Token)).IsSuccess);
            var allJson = await json.ListAllAsync(cts.Token);
            Assert.Single(allJson.Value);
        }
    }
}
