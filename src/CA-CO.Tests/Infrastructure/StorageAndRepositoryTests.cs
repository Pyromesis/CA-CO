using CaCo.Application.Repositories;
using CaCo.Domain;
using CaCo.Infrastructure.Persistence;
using CaCo.Infrastructure.Storage;
using CaCo.Tests.Helpers;

namespace CaCo.Tests.Infrastructure;

/// <summary>Pruebas de la estructura de almacenamiento local.</summary>
public sealed class StorageTests
{
    [Fact]
    public async Task EnsureCreated_CreatesExpectedFolders()
    {
        using var temp = new TempDirectory();
        var root = Path.Combine(temp.Path, "Library");
        var paths = new LibraryPaths(root);
        var initializer = new LibraryInitializer(paths, NullLogger());
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var created = await initializer.EnsureCreatedAsync(cts.Token);

        Assert.True(created);
        foreach (var folder in new[]
                 {
                     paths.LibraryRoot, paths.Documents, paths.Originals, paths.Metadata,
                     paths.Database, paths.Cache, paths.Thumbnails, paths.Backups, paths.Temp,
                 })
        {
            Assert.True(Directory.Exists(folder));
        }

        // Segunda vez: idempotente.
        Assert.False(await initializer.EnsureCreatedAsync(cts.Token));
    }

    [Fact]
    public void LibraryPaths_Relocatable_RootDrivesEverything()
    {
        var root = Path.Combine(Path.GetTempPath(), "caco-reloc-test");
        var paths = new LibraryPaths(root);

        Assert.StartsWith(paths.LibraryRoot, paths.Documents);
        Assert.StartsWith(paths.LibraryRoot, paths.Database);
        Assert.EndsWith("documents.json", paths.DocumentsDb);
        Assert.EndsWith("notebooks.json", paths.NotebooksDb);
        Assert.Equal(Path.GetFullPath(root), paths.LibraryRoot);
    }

    private static Microsoft.Extensions.Logging.ILogger<LibraryInitializer> NullLogger() =>
        Microsoft.Extensions.Logging.Abstractions.NullLogger<LibraryInitializer>.Instance;
}

/// <summary>Pruebas de persistencia JSON (roundtrip + consultas).</summary>
public sealed class RepositoryTests
{
    [Fact]
    public async Task DocumentRepository_Roundtrip_Works()
    {
        using var temp = new TempDirectory();
        var paths = new LibraryPaths(Path.Combine(temp.Path, "Lib"));
        var repo = new JsonDocumentRepository(paths);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var doc = Document.Create("Informe", "informe.pdf", 100).Value;
        Assert.True((await repo.AddAsync(doc, cts.Token)).IsSuccess);

        var loaded = await repo.GetByIdAsync(doc.Id, false, cts.Token);
        Assert.True(loaded.IsSuccess);
        Assert.NotNull(loaded.Value);
        Assert.Equal("Informe", loaded.Value.Name);
        Assert.Equal(DocumentType.Pdf, loaded.Value.FileType);

        Assert.True(doc.Rename("Informe final").IsSuccess);
        Assert.True((await repo.UpdateAsync(doc, cts.Token)).IsSuccess);

        var listed = await repo.ListAsync(new DocumentQuery(), cts.Token);
        Assert.True(listed.IsSuccess);
        Assert.Equal(1, listed.Value.TotalCount);
        Assert.Equal("Informe final", listed.Value.Items[0].Name);

        Assert.True((await repo.DeleteAsync(doc.Id, cts.Token)).IsSuccess);
        Assert.Equal(0, (await repo.CountActiveAsync(cts.Token)).Value);
    }

    [Fact]
    public async Task DocumentRepository_FavoritesAndTrash_Filter()
    {
        using var temp = new TempDirectory();
        var paths = new LibraryPaths(Path.Combine(temp.Path, "Lib"));
        var repo = new JsonDocumentRepository(paths);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var fav = Document.Create("Fav", "f.pdf", 10).Value;
        fav.SetFavorite(true);
        var trash = Document.Create("Viejo", "v.txt", 10).Value;
        Assert.True(trash.MoveToTrash().IsSuccess);
        await repo.AddAsync(fav, cts.Token);
        await repo.AddAsync(trash, cts.Token);

        var favorites = await repo.ListAsync(new DocumentQuery { FavoritesOnly = true }, cts.Token);
        Assert.Single(favorites.Value.Items);

        var active = await repo.ListAsync(new DocumentQuery(), cts.Token);
        Assert.Single(active.Value.Items);

        var deleted = await repo.ListAsync(
            new DocumentQuery { DeletedOnly = true, IncludeDeleted = true }, cts.Token);
        Assert.Single(deleted.Value.Items);
    }

    [Fact]
    public async Task NotebookRepository_Roundtrip_Works()
    {
        using var temp = new TempDirectory();
        var paths = new LibraryPaths(Path.Combine(temp.Path, "Lib"));
        var repo = new JsonNotebookRepository(paths);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var root = Notebook.Create("Uni").Value;
        await repo.AddAsync(root, cts.Token);
        var child = Notebook.Create(" mates ", root.Id).Value;
        await repo.AddAsync(child, cts.Token);

        Assert.Equal("mates", child.Name); // La entidad recorta espacios.

        var children = await repo.ListAsync(new NotebookQuery { ParentId = root.Id }, cts.Token);
        Assert.Single(children.Value.Items);

        var all = await repo.ListAllActiveAsync(cts.Token);
        Assert.Equal(2, all.Value.Count);
    }
}
