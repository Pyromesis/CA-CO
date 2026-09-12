using CaCo.Application.Notes;
using CaCo.Application.Services;
using CaCo.Core;
using CaCo.Domain;
using CaCo.Infrastructure.Persistence.Sqlite;
using CaCo.Infrastructure.Storage;
using CaCo.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace CaCo.Tests.Application;

/// <summary>Pruebas Fase 4: markdown ligero y notas sueltas.</summary>
public sealed class NotesPhaseTests
{
    [Fact]
    public void Markdown_BoldItalicCode()
    {
        var spans = MarkdownLite.ParseInline("hola **fuerte** y *suave* y `codigo` fin");
        Assert.Equal(7, spans.Count);
        Assert.Equal(new MdSpan("hola ", false, false, false), spans[0]);
        Assert.Equal(new MdSpan("fuerte", true, false, false), spans[1]);
        Assert.Equal(new MdSpan(" y ", false, false, false), spans[2]);
        Assert.Equal(new MdSpan("suave", false, true, false), spans[3]);
        Assert.Equal(new MdSpan(" y ", false, false, false), spans[4]);
        Assert.Equal(new MdSpan("codigo", false, false, true), spans[5]);
        Assert.Equal(new MdSpan(" fin", false, false, false), spans[6]);
    }

    [Fact]
    public void Markdown_UnclosedMarkers_StayLiteral()
    {
        var spans = MarkdownLite.ParseInline("a **sin cerrar y *tampoco y `nada");
        Assert.Single(spans);
        Assert.Equal("a **sin cerrar y *tampoco y `nada", spans[0].Text);
    }

    [Fact]
    public void Markdown_Blocks_HeadersBulletsParagraphs()
    {
        var blocks = MarkdownLite.Parse("# Titulo\n\ntexto **x**\n- uno\n* dos\n#### profundo");
        Assert.Equal(5, blocks.Count);
        Assert.Equal(MdBlockKind.Header, blocks[0].Kind);
        Assert.Equal(1, blocks[0].Level);
        Assert.Equal("Titulo", Assert.Single(blocks[0].Spans).Text);
        Assert.Equal(MdBlockKind.Paragraph, blocks[1].Kind);
        Assert.Equal(MdBlockKind.Bullet, blocks[2].Kind);
        Assert.Equal(MdBlockKind.Bullet, blocks[3].Kind);
        Assert.Equal(MdBlockKind.Paragraph, blocks[4].Kind);
    }

    [Fact]
    public void Markdown_EmptyAndCap()
    {
        Assert.Empty(MarkdownLite.Parse(null));
        Assert.Empty(MarkdownLite.Parse("   \n  "));
        var many = string.Join("\n", Enumerable.Repeat("linea", 600));
        Assert.Equal(MarkdownLite.MaxBlocks, MarkdownLite.Parse(many).Count);
    }

    private static (NoteService Service, TempDirectory Temp) Create()
    {
        var temp = new TempDirectory();
        var paths = new LibraryPaths(Path.Combine(temp.Path, "Lib"));
        Directory.CreateDirectory(paths.Database);
        var db = new SqliteDatabase(Path.Combine(paths.Database, "caco.db"));
        db.EnsureCreatedAsync(CancellationToken.None).GetAwaiter().GetResult();
        var service = new NoteService(
            new SqliteNoteRepository(db),
            new SqliteDocumentRepository(db),
            new TestClock(DateTimeOffset.UtcNow),
            NullLogger<NoteService>.Instance);
        return (service, temp);
    }

    [Fact]
    public async Task Standalone_CreateListRenameDelete_Flow()
    {
        var (service, temp) = Create();
        using (temp)
        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
        {
            Assert.True((await service.AddStandaloneAsync("Compra", "pan y **leche**", cts.Token)).IsSuccess);

            var list = await service.ListStandaloneAsync(10, cts.Token);
            Assert.True(list.IsSuccess);
            var note = Assert.Single(list.Value);
            Assert.Equal("Compra", note.Title);

            Assert.True((await service.RenameAsync(note.Id, "Compra semanal", cts.Token)).IsSuccess);
            list = await service.ListStandaloneAsync(10, cts.Token);
            Assert.Equal("Compra semanal", list.Value[0].Title);
            Assert.Equal("pan y **leche**", list.Value[0].Content);

            Assert.True((await service.DeleteAsync(note.Id, cts.Token)).IsSuccess);
            list = await service.ListStandaloneAsync(10, cts.Token);
            Assert.Empty(list.Value);
        }
    }

    [Fact]
    public async Task Standalone_EmptyTitle_Fails()
    {
        var (service, temp) = Create();
        using (temp)
        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
        {
            Assert.True((await service.AddStandaloneAsync("  ", "x", cts.Token)).IsFailure);
        }
    }

    [Fact]
    public async Task Standalone_ExcludesDocumentNotes()
    {
        var (service, temp) = Create();
        using (temp)
        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
        {
            // Nota atada a documento (vía repositorio): no debe salir en sueltas.
            var paths = new LibraryPaths(Path.Combine(temp.Path, "Lib"));
            var db = new SqliteDatabase(Path.Combine(paths.Database, "caco.db"));
            var repo = new SqliteNoteRepository(db);
            var linked = Note.Create("C", "hola", documentId: Guid.NewGuid(), now: DateTimeOffset.UtcNow).Value;
            Assert.True((await repo.AddAsync(linked, cts.Token)).IsSuccess);

            Assert.True((await service.AddStandaloneAsync("Suelta", "yo", cts.Token)).IsSuccess);
            var list = await service.ListStandaloneAsync(10, cts.Token);
            var single = Assert.Single(list.Value);
            Assert.Equal("Suelta", single.Title);
        }
    }
}
