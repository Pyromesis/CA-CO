using CaCo.Application.Configuration;
using CaCo.Application.Import;
using CaCo.Application.Storage;
using CaCo.Core;
using CaCo.Domain;
using CaCo.Infrastructure.DependencyInjection;
using CaCo.Infrastructure.Import;
using CaCo.Infrastructure.Persistence;
using CaCo.Infrastructure.Persistence.Sqlite;
using CaCo.Infrastructure.Storage;
using CaCo.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace CaCo.Tests.Application;

/// <summary>Inspector guionizado para probar metadatos sin WinRT.</summary>
internal sealed class ScriptedInspector : IMediaInspector
{
    private readonly Func<string, MediaInfo?> _script;
    public ScriptedInspector(Func<string, MediaInfo?> script) => _script = script;

    public Task<Result<MediaInfo?>> InspectAsync(string absolutePath, DocumentType type, CancellationToken ct)
    {
        var r = _script(absolutePath);
        return Task.FromResult(Result.Success(r));
    }
}

/// <summary>Inspector que siempre revienta (debe degradar con gracia).</summary>
internal sealed class ThrowingInspector : IMediaInspector
{
    public Task<Result<MediaInfo?>> InspectAsync(string absolutePath, DocumentType type, CancellationToken ct) =>
        throw new InvalidOperationException("Sin multimedia.");
}

/// <summary>Pruebas Fase 3: metadatos multimedia en la importación.</summary>
public sealed class MediaImportTests
{
    private static LocalDocumentImporter CreateImporter(
        TempDirectory temp, LibraryPaths paths, IMediaInspector inspector)
    {
        Directory.CreateDirectory(paths.Documents);
        Directory.CreateDirectory(paths.Originals);
        return new LocalDocumentImporter(
            new FileTypeValidator(),
            new PhysicalFileStorage(NullLogger<PhysicalFileStorage>.Instance),
            paths,
            new JsonDocumentRepository(paths),
            new JsonNotebookRepository(paths),
            new NullThumbnailService(),
            inspector,
            new CacoSettings(),
            new TestClock(DateTimeOffset.UtcNow),
            NullLogger<LocalDocumentImporter>.Instance);
    }

    [Fact]
    public async Task PdfImport_StoresPageCountOnly()
    {
        using var temp = new TempDirectory();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var paths = new LibraryPaths(Path.Combine(temp.Path, "Lib"));
        var importer = CreateImporter(temp, paths,
            new ScriptedInspector(_ => new MediaInfo(7, 800, 600)));

        var source = temp.CreateFile("doc.pdf", "%PDF-1.4 fake");
        var result = await importer.ImportAsync(new ImportRequest(source), cts.Token);

        Assert.True(result.Value.Succeeded);
        var doc = result.Value.Document!;
        Assert.Equal("7", doc.Metadata.Get(MediaMetadataKeys.PdfPageCount));
        Assert.Null(doc.Metadata.Get(MediaMetadataKeys.ImageWidth));
    }

    [Fact]
    public async Task TxtImport_IgnoresMediaInfo()
    {
        using var temp = new TempDirectory();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var paths = new LibraryPaths(Path.Combine(temp.Path, "Lib"));
        var importer = CreateImporter(temp, paths,
            new ScriptedInspector(_ => new MediaInfo(7, 800, 600)));

        var source = temp.CreateFile("nota.txt", "hola");
        var result = await importer.ImportAsync(new ImportRequest(source), cts.Token);

        Assert.True(result.Value.Succeeded);
        var doc = result.Value.Document!;
        Assert.Null(doc.Metadata.Get(MediaMetadataKeys.PdfPageCount));
        Assert.Null(doc.Metadata.Get(MediaMetadataKeys.ImageWidth));
    }

    [Fact]
    public async Task InspectorFailure_StillImports()
    {
        using var temp = new TempDirectory();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var paths = new LibraryPaths(Path.Combine(temp.Path, "Lib"));
        var importer = CreateImporter(temp, paths, new ThrowingInspector());

        var source = temp.CreateFile("nota.txt", "hola");
        var result = await importer.ImportAsync(new ImportRequest(source), cts.Token);

        Assert.True(result.Value.Succeeded);
        Assert.Null(result.Value.Document!.Metadata.Get(MediaMetadataKeys.PdfPageCount));
    }

    [Fact]
    public async Task NullMediaInspector_ReturnsNull()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var result = await NullMediaInspector.Instance.InspectAsync("x.pdf", DocumentType.Pdf, cts.Token);
        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
    }
}
