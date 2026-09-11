using CaCo.Application.Repositories;
using CaCo.Domain;
using CaCo.Infrastructure.Persistence.Sqlite;
using CaCo.Tests.Helpers;

namespace CaCo.Tests.Infrastructure;

/// <summary>Pruebas de persistencia SQLite (Fase 1).</summary>
public sealed class SqliteRepositoryTests
{
    private static async Task<(SqliteDocumentRepository Docs, SqliteNotebookRepository Notebooks)> CreateAsync(
        TempDirectory temp, CancellationToken ct)
    {
        var dbPath = Path.Combine(temp.Path, "Lib", "Database", "caco.db");
        var db = new SqliteDatabase(dbPath);
        await db.EnsureCreatedAsync(ct);
        return (new SqliteDocumentRepository(db), new SqliteNotebookRepository(db));
    }

    [Fact]
    public async Task Documents_Roundtrip_WithHashAndOriginal_Correct()
    {
        using var temp = new TempDirectory();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var (docs, _) = await CreateAsync(temp, cts.Token);

        var doc = Document.Create("Informe", "informe.pdf", 100).Value;
        doc.AttachStoredCopy("abc123.pdf");
        doc.AttachOriginalCopy("orig456.pdf");
        doc.SetContentHash("DEADBEEF");
        doc.SetFavorite(true);
        Assert.True((await docs.AddAsync(doc, cts.Token)).IsSuccess);

        var loaded = await docs.GetByIdAsync(doc.Id, false, cts.Token);
        Assert.NotNull(loaded.Value);
        Assert.Equal("abc123.pdf", loaded.Value.StoredFileName);
        Assert.Equal("orig456.pdf", loaded.Value.OriginalStoredFileName);
        Assert.Equal("DEADBEEF", loaded.Value.ContentHash);
        Assert.True(loaded.Value.IsFavorite);

        Assert.Equal(1, (await docs.CountActiveAsync(cts.Token)).Value);
        Assert.Equal(100, (await docs.SumActiveBytesAsync(cts.Token)).Value);

        var page = await docs.ListAsync(new DocumentQuery { FavoritesOnly = true }, cts.Token);
        Assert.Single(page.Value.Items);

        Assert.True((await docs.DeleteAsync(doc.Id, cts.Token)).IsSuccess);
        Assert.Equal(0, (await docs.CountActiveAsync(cts.Token)).Value);
    }

    [Fact]
    public async Task Documents_SearchAndPagination_Work()
    {
        using var temp = new TempDirectory();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var (docs, _) = await CreateAsync(temp, cts.Token);

        for (var i = 0; i < 5; i++)
        {
            var doc = Document.Create($"Apunte {i}", $"a{i}.txt", 10 + i).Value;
            Assert.True((await docs.AddAsync(doc, cts.Token)).IsSuccess);
        }

        var search = await docs.ListAsync(new DocumentQuery { SearchText = "apunte 1" }, cts.Token);
        Assert.Single(search.Value.Items);

        var p1 = await docs.ListAsync(new DocumentQuery { Page = 1, PageSize = 2 }, cts.Token);
        Assert.Equal(2, p1.Value.Items.Count);
        Assert.Equal(5, p1.Value.TotalCount);
        Assert.True(p1.Value.HasNextPage);
    }

    [Fact]
    public async Task Notebooks_Hierarchy_Persists()
    {
        using var temp = new TempDirectory();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var (_, notebooks) = await CreateAsync(temp, cts.Token);

        var root = Notebook.Create("Uni").Value;
        var child = Notebook.Create("Física", root.Id).Value;
        Assert.True((await notebooks.AddAsync(root, cts.Token)).IsSuccess);
        Assert.True((await notebooks.AddAsync(child, cts.Token)).IsSuccess);

        var children = await notebooks.ListAsync(new NotebookQuery { ParentId = root.Id }, cts.Token);
        Assert.Single(children.Value.Items);

        var roots = await notebooks.ListAsync(new NotebookQuery { RootsOnly = true }, cts.Token);
        Assert.Single(roots.Value.Items);

        Assert.Equal(2, (await notebooks.CountActiveAsync(cts.Token)).Value);
    }
}
