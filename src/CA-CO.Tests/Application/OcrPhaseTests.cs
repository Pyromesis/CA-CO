using CaCo.Application.Ocr;
using CaCo.Application.Services;
using CaCo.Core;
using CaCo.Domain;
using CaCo.Infrastructure.Persistence.Sqlite;
using CaCo.Infrastructure.Storage;
using CaCo.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace CaCo.Tests.Application;

/// <summary>Pruebas Fase 5: OCR multipágina y persistencia del resultado.</summary>
public sealed class OcrPhaseTests
{
    [Fact]
    public void TakePages_CapsAndRanges()
    {
        Assert.Empty(OcrPages.TakePages(0));
        Assert.Equal([1, 2, 3], OcrPages.TakePages(3).ToList());
        var capped = OcrPages.TakePages(100);
        Assert.Equal(20, capped.Count);
        Assert.Equal(1, capped[0]);
        Assert.Equal(20, capped[^1]);
        Assert.Equal([1, 2], OcrPages.TakePages(5, new PdfOcrOptions { MaxPages = 2 }).ToList());
    }

    [Fact]
    public void Combine_SkipsBlanksAndJoins()
    {
        var (text, truncated) = OcrPages.Combine(["  ", null, "uno", "", "dos"]);
        Assert.False(truncated);
        Assert.Equal("uno\n\ndos", text);
    }

    [Fact]
    public void Combine_TruncatesAtMaxChars()
    {
        var (text, truncated) = OcrPages.Combine(
            ["abcdefghij", "klmnopqrst"],
            new PdfOcrOptions { MaxChars = 12 });
        Assert.True(truncated);
        Assert.True(text.Length <= 12);
        Assert.StartsWith("abcdefghij", text);
    }

    [Fact]
    public void Combine_Empty_ReturnsEmpty()
    {
        var (text, truncated) = OcrPages.Combine([null, "  "]);
        Assert.False(truncated);
        Assert.Equal(string.Empty, text);
    }

    private static (DocumentService Service, TempDirectory Temp) Create()
    {
        var temp = new TempDirectory();
        var paths = new LibraryPaths(Path.Combine(temp.Path, "Lib"));
        Directory.CreateDirectory(paths.Database);
        var db = new SqliteDatabase(Path.Combine(paths.Database, "caco.db"));
        db.EnsureCreatedAsync(CancellationToken.None).GetAwaiter().GetResult();
        var clock = new TestClock(DateTimeOffset.UtcNow);
        var storage = new PhysicalFileStorage(NullLogger<PhysicalFileStorage>.Instance);
        var service = new DocumentService(
            new SqliteDocumentRepository(db),
            new SqliteNotebookRepository(db),
            new SqliteTagRepository(db),
            storage, paths,
            new CaCo.Infrastructure.DependencyInjection.NullThumbnailService(),
            clock, NullLogger<DocumentService>.Instance);
        return (service, temp);
    }

    private static async Task<Guid> SeedDocAsync(DocumentService service, TempDirectory temp, CancellationToken ct)
    {
        var paths = new LibraryPaths(Path.Combine(temp.Path, "Lib"));
        var db = new SqliteDatabase(Path.Combine(paths.Database, "caco.db"));
        var repo = new SqliteDocumentRepository(db);
        var doc = Document.Create("Manual", "manual.pdf", 100).Value;
        Assert.True((await repo.AddAsync(doc, ct)).IsSuccess);
        return doc.Id;
    }

    [Fact]
    public async Task SetOcrResult_StoresMetadata()
    {
        var (service, temp) = Create();
        using (temp)
        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
        {
            var id = await SeedDocAsync(service, temp, cts.Token);
            Assert.True((await service.SetOcrResultAsync(id, 3, "hola mundo", cts.Token)).IsSuccess);

            var paths = new LibraryPaths(Path.Combine(temp.Path, "Lib"));
            var db = new SqliteDatabase(Path.Combine(paths.Database, "caco.db"));
            var found = await new SqliteDocumentRepository(db).GetByIdAsync(id, false, cts.Token);
            Assert.Equal("true", found.Value!.Metadata.Get(OcrMetadataKeys.Done));
            Assert.Equal("3", found.Value.Metadata.Get(OcrMetadataKeys.Pages));
            Assert.Equal("10", found.Value.Metadata.Get(OcrMetadataKeys.Chars));
            Assert.Equal("hola mundo", found.Value.Metadata.Get(OcrMetadataKeys.Excerpt));
        }
    }

    [Fact]
    public async Task SetOcrResult_TruncatesExcerpt()
    {
        var (service, temp) = Create();
        using (temp)
        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
        {
            var id = await SeedDocAsync(service, temp, cts.Token);
            var big = new string('x', 2500);
            Assert.True((await service.SetOcrResultAsync(id, 1, big, cts.Token)).IsSuccess);

            var paths = new LibraryPaths(Path.Combine(temp.Path, "Lib"));
            var db = new SqliteDatabase(Path.Combine(paths.Database, "caco.db"));
            var found = await new SqliteDocumentRepository(db).GetByIdAsync(id, false, cts.Token);
            Assert.Equal("2500", found.Value!.Metadata.Get(OcrMetadataKeys.Chars));
            Assert.Equal(OcrMetadataKeys.MaxExcerptLength, found.Value.Metadata.Get(OcrMetadataKeys.Excerpt)!.Length);
        }
    }

    [Fact]
    public async Task SetOcrResult_Invalid_Fails()
    {
        var (service, temp) = Create();
        using (temp)
        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
        {
            var id = await SeedDocAsync(service, temp, cts.Token);
            Assert.True((await service.SetOcrResultAsync(id, -1, "x", cts.Token)).IsFailure);
            Assert.True((await service.SetOcrResultAsync(id, 1, "   ", cts.Token)).IsFailure);
            Assert.True((await service.SetOcrResultAsync(Guid.NewGuid(), 1, "x", cts.Token)).IsFailure);
        }
    }
}
