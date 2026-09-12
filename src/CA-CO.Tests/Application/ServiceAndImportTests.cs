using CaCo.Application.Configuration;
using CaCo.Application.Import;
using CaCo.Application.Services;
using CaCo.Core;
using CaCo.Domain;
using CaCo.Infrastructure.DependencyInjection;
using CaCo.Infrastructure.Import;
using CaCo.Infrastructure.Persistence;
using CaCo.Infrastructure.Storage;
using CaCo.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace CaCo.Tests.Application;

/// <summary>Pruebas de jerarquía de cuadernos a nivel de servicio (anti-ciclos, borrado seguro).</summary>
public sealed class NotebookHierarchyTests
{
    private sealed record Harness(
        NotebookService Notebooks,
        JsonNotebookRepository NotebookRepo,
        JsonDocumentRepository DocumentRepo);

    private static Harness Create(TempDirectory temp, IClock? clock = null)
    {
        var paths = new LibraryPaths(Path.Combine(temp.Path, "Lib"));
        var notebooks = new JsonNotebookRepository(paths);
        var documents = new JsonDocumentRepository(paths);
        var service = new NotebookService(
            notebooks, documents, clock ?? new TestClock(DateTimeOffset.UtcNow),
            NullLogger<NotebookService>.Instance);
        return new Harness(service, notebooks, documents);
    }

    [Fact]
    public async Task Move_IntoDescendant_IsRejected()
    {
        using var temp = new TempDirectory();
        var h = Create(temp);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var root = (await h.Notebooks.CreateAsync("Raíz", null, cts.Token)).Value;
        var child = (await h.Notebooks.CreateAsync("Hija", root.Id, cts.Token)).Value;
        var grandchild = (await h.Notebooks.CreateAsync("Nieta", child.Id, cts.Token)).Value;

        // Mover la raíz dentro de su nieta crearía un ciclo.
        var result = await h.Notebooks.MoveAsync(root.Id, grandchild.Id, cts.Token);

        Assert.True(result.IsFailure);
        Assert.Equal("Notebook.Cycle", result.Error.Code);
    }

    [Fact]
    public async Task Delete_ReparentsChildren_AndKeepsDocuments()
    {
        using var temp = new TempDirectory();
        var h = Create(temp);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var root = (await h.Notebooks.CreateAsync("Raíz", null, cts.Token)).Value;
        var child = (await h.Notebooks.CreateAsync("Hija", root.Id, cts.Token)).Value;

        var doc = Document.Create("Doc", "d.pdf", 10, root.Id).Value;
        Assert.True((await h.DocumentRepo.AddAsync(doc, cts.Token)).IsSuccess);

        Assert.True((await h.Notebooks.DeleteAsync(root.Id, cts.Token)).IsSuccess);

        // La hija sube a raíz.
        var reloadedChild = await h.NotebookRepo.GetByIdAsync(child.Id, false, cts.Token);
        Assert.Null(reloadedChild.Value!.ParentId);

        // El documento se conserva, desclasificado.
        var reloadedDoc = await h.DocumentRepo.GetByIdAsync(doc.Id, false, cts.Token);
        Assert.NotNull(reloadedDoc.Value);
        Assert.Null(reloadedDoc.Value.NotebookId);
    }

    [Fact]
    public async Task Create_WithMissingParent_Fails()
    {
        using var temp = new TempDirectory();
        var h = Create(temp);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var result = await h.Notebooks.CreateAsync("Huérfano", Guid.NewGuid(), cts.Token);

        Assert.True(result.IsFailure);
        Assert.Equal("Notebook.ParentNotFound", result.Error.Code);
    }
}

/// <summary>Pruebas del importador local.</summary>
public sealed class ImportTests
{
    private sealed record Harness(
        LocalDocumentImporter Importer,
        JsonDocumentRepository Documents,
        LibraryPaths Paths);

    private static Harness Create(TempDirectory temp)
    {
        var paths = new LibraryPaths(Path.Combine(temp.Path, "Lib"));
        Directory.CreateDirectory(paths.Documents);
        Directory.CreateDirectory(paths.Originals);
        var documents = new JsonDocumentRepository(paths);
        var notebooks = new JsonNotebookRepository(paths);
        var importer = new LocalDocumentImporter(
            new FileTypeValidator(),
            new PhysicalFileStorage(NullLogger<PhysicalFileStorage>.Instance),
            paths,
            documents,
            notebooks,
            new NullThumbnailService(),
            NullMediaInspector.Instance,
            new CacoSettings(),
            new TestClock(DateTimeOffset.UtcNow),
            NullLogger<LocalDocumentImporter>.Instance);
        return new Harness(importer, documents, paths);
    }

    [Fact]
    public async Task Import_SupportedFile_CopiesAndPersists()
    {
        using var temp = new TempDirectory();
        var h = Create(temp);
        var source = temp.CreateFile("origen.txt", "hola mundo");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var result = await h.Importer.ImportAsync(new ImportRequest(source), cts.Token);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Succeeded);
        Assert.NotNull(result.Value.Document);
        Assert.Equal("origen", result.Value.Document.Name);
        Assert.NotNull(result.Value.Document.StoredFileName);
        Assert.True(File.Exists(Path.Combine(h.Paths.Documents, result.Value.Document.StoredFileName)));
        Assert.True(File.Exists(Path.Combine(h.Paths.Originals, Directory.GetFiles(h.Paths.Originals)[0])));
    }

    [Fact]
    public async Task Import_SameFileTwice_SecondIsSkippedAsDuplicate()
    {
        using var temp = new TempDirectory();
        var h = Create(temp);
        var source = temp.CreateFile("repe.txt", "mismo contenido");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var first = await h.Importer.ImportAsync(new ImportRequest(source), cts.Token);
        var second = await h.Importer.ImportAsync(new ImportRequest(source), cts.Token);

        Assert.True(first.Value.Succeeded);
        Assert.False(second.Value.Succeeded);
        Assert.True(second.Value.SkippedAsDuplicate);
    }

    [Fact]
    public async Task Import_UnsupportedFile_IsRejected()
    {
        using var temp = new TempDirectory();
        var h = Create(temp);
        var source = temp.CreateFile("programa.exe", "binario");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var result = await h.Importer.ImportAsync(new ImportRequest(source), cts.Token);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.Succeeded);
        Assert.NotNull(result.Value.Error);
    }
}
