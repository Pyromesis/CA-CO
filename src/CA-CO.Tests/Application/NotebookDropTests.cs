using CaCo.Application.Configuration;
using CaCo.Application.Import;
using CaCo.Application.Services;
using CaCo.Core;
using CaCo.Domain;
using CaCo.Infrastructure.Configuration;
using CaCo.Infrastructure.DependencyInjection;
using CaCo.Infrastructure.Errors;
using CaCo.Infrastructure.Import;
using CaCo.Infrastructure.Persistence.Sqlite;
using CaCo.Infrastructure.Storage;
using CaCo.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace CaCo.Tests.Application;

/// <summary>Pruebas Cuadernos-DnD: clasificar por lote y scanner como origen.</summary>
public sealed class NotebookDropTests
{
    private sealed record Harness(
        BatchImportService Batch,
        DocumentService Documents,
        NotebookService Notebooks,
        FolderScanner Scanner,
        LibraryPaths Paths,
        TempDirectory Temp);

    private static async Task<Harness> CreateAsync(TempDirectory temp, CancellationToken ct)
    {
        var paths = new LibraryPaths(Path.Combine(temp.Path, "Lib"));
        Directory.CreateDirectory(paths.Documents);
        Directory.CreateDirectory(paths.Originals);
        var db = new SqliteDatabase(Path.Combine(paths.Database, "caco.db"));
        await db.EnsureCreatedAsync(ct);
        var clock = new TestClock(DateTimeOffset.UtcNow);
        var documents = new SqliteDocumentRepository(db);
        var notebooks = new SqliteNotebookRepository(db);
        var tags = new SqliteTagRepository(db);
        var storage = new PhysicalFileStorage(NullLogger<PhysicalFileStorage>.Instance);
        var errors = new AppErrorHandler(NullLogger<AppErrorHandler>.Instance);
        var importer = new LocalDocumentImporter(
            new FileTypeValidator(), storage, paths, documents, notebooks,
            new NullThumbnailService(), NullMediaInspector.Instance,
            new CacoSettings(), clock, NullLogger<LocalDocumentImporter>.Instance);
        var batch = new BatchImportService(
            importer, errors, NullLogger<BatchImportService>.Instance);
        var docService = new DocumentService(documents, notebooks, tags, storage, paths,
            new NullThumbnailService(), clock, NullLogger<DocumentService>.Instance);
        var nbService = new NotebookService(notebooks, documents, clock, NullLogger<NotebookService>.Instance);
        return new Harness(batch, docService, nbService, new FolderScanner(), paths, temp);
    }

    [Fact]
    public async Task DropFolder_ImportsIntoNotebook()
    {
        using var temp = new TempDirectory();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var h = await CreateAsync(temp, cts.Token);

        var drop = Path.Combine(temp.Path, "Arrastre");
        Directory.CreateDirectory(drop);
        Directory.CreateDirectory(Path.Combine(drop, "Sub"));
        File.WriteAllText(Path.Combine(drop, "a.txt"), "uno");
        File.WriteAllText(Path.Combine(drop, "Sub", "b.txt"), "dos");
        File.WriteAllText(Path.Combine(drop, "no.exe"), "x");

        var created = await h.Notebooks.CreateAsync("Destino", null, cts.Token);
        Assert.True(created.IsSuccess);

        var scan = h.Scanner.Enumerate(drop, recursive: true, cts.Token);
        Assert.Equal(2, scan.Files.Count);

        var progress = new Progress<ImportProgress>(_ => { });
        var result = await h.Batch.ImportBatchAsync(scan.Files, created.Value.Id, null, progress, cts.Token);
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Imported);

        var inNotebook = await h.Documents.ListAsync(
            new CaCo.Application.Repositories.DocumentQuery { NotebookId = created.Value.Id, Page = 1, PageSize = 50 },
            cts.Token);
        Assert.Equal(2, inNotebook.Value.Items.Count);

        // Mover uno fuera (soltar en Sin clasificar).
        var move = await h.Documents.MoveToNotebookAsync(inNotebook.Value.Items[0].Id, null, cts.Token);
        Assert.True(move.IsSuccess);
        var unclassified = await h.Documents.ListAsync(
            new CaCo.Application.Repositories.DocumentQuery { UnclassifiedOnly = true, Page = 1, PageSize = 50 },
            cts.Token);
        Assert.Single(unclassified.Value.Items);
    }
}
