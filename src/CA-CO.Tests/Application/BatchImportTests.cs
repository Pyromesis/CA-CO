using CaCo.Application.Errors;
using CaCo.Application.Import;
using CaCo.Core;
using CaCo.Domain;
using CaCo.Infrastructure.Errors;
using CaCo.Infrastructure.Import;
using CaCo.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace CaCo.Tests.Application;

/// <summary>Importador guionizado para probar lotes sin disco.</summary>
internal sealed class ScriptedImporter : IDocumentImporter
{
    private readonly Func<string, ImportResult> _script;
    public int Calls;

    public ScriptedImporter(Func<string, ImportResult> script) => _script = script;

    public Task<Result<ImportResult>> ImportAsync(ImportRequest request, CancellationToken ct)
    {
        Calls++;
        ct.ThrowIfCancellationRequested();
        var r = _script(request.SourcePath);
        if (r.SourcePath == "#throw-cancel")
        {
            throw new OperationCanceledException();
        }

        if (r.SourcePath == "#throw-failure")
        {
            return Task.FromResult(Result.Failure<ImportResult>(
                Error.Storage("Import.Boom", "Fallo técnico.")));
        }

        return Task.FromResult(Result.Success(r));
    }

    public async IAsyncEnumerable<ImportResult> ImportManyAsync(
        IEnumerable<ImportRequest> requests,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var request in requests)
        {
            var r = await ImportAsync(request, ct);
            yield return r.IsSuccess ? r.Value : new ImportResult { Succeeded = false, SourcePath = request.SourcePath, Error = r.Error };
        }
    }
}

/// <summary>Pruebas Fase 2: lotes con límites, informe y cancelación.</summary>
public sealed class BatchImportTests
{
    private static BatchImportService Create(Func<string, ImportResult> script, out ScriptedImporter stub)
    {
        stub = new ScriptedImporter(script);
        return new BatchImportService(
            stub,
            new AppErrorHandler(NullLogger<AppErrorHandler>.Instance),
            NullLogger<BatchImportService>.Instance);
    }

    private static ImportResult Ok(string path) => new()
    {
        Succeeded = true,
        SourcePath = path,
        Document = Document.Create("n", "a.txt", 10).Value,
    };

    private static ImportResult Dup(string path) => new()
    {
        Succeeded = false,
        SourcePath = path,
        SkippedAsDuplicate = true,
        Error = Error.Conflict("Import.Duplicate", "Duplicado."),
    };

    private static ImportResult Fail(string path) => new()
    {
        Succeeded = false,
        SourcePath = path,
        Error = Error.Validation("Document.UnsupportedType", "Tipo no soportado."),
    };

    [Fact]
    public async Task MixedBatch_CountsWithoutAborting()
    {
        var svc = Create(p => p.EndsWith("a.txt") ? Ok(p) : p.EndsWith("b.txt") ? Dup(p) : Fail(p), out _);
        var result = await svc.ImportBatchAsync(["a.txt", "b.txt", "c.txt"], null);

        Assert.True(result.IsSuccess);
        var batch = result.Value;
        Assert.Equal(1, batch.Imported);
        Assert.Equal(1, batch.Duplicates);
        Assert.Equal(1, batch.Failed);
        Assert.Equal(3, batch.Attempted);
        Assert.False(batch.WasCancelled);
        Assert.False(batch.RejectedByLimit);
        Assert.Single(batch.Failures);
        Assert.Equal("c.txt", batch.Failures[0].FileName);
        Assert.False(string.IsNullOrWhiteSpace(batch.Failures[0].Reason));
    }

    [Fact]
    public async Task OverFileLimit_RejectsBeforeStarting()
    {
        var svc = Create(Ok, out var stub);
        var paths = Enumerable.Range(0, 101).Select(i => $"f{i}.txt");
        var result = await svc.ImportBatchAsync(
            paths, null, new ImportBatchOptions { MaxFiles = 100 });

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.RejectedByLimit);
        Assert.Equal(0, result.Value.Attempted);
        Assert.False(string.IsNullOrWhiteSpace(result.Value.LimitReason));
        Assert.Equal(0, stub.Calls);
    }

    [Fact]
    public async Task OverBytesLimit_RejectsBeforeStarting()
    {
        using var temp = new TempDirectory();
        var f1 = temp.CreateFile("g1.txt", "0123456789");
        var f2 = temp.CreateFile("g2.txt", "0123456789");
        var svc = Create(Ok, out var stub);
        var result = await svc.ImportBatchAsync(
            [f1, f2], null, new ImportBatchOptions { MaxFiles = 100, MaxTotalBytes = 15 });

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.RejectedByLimit);
        Assert.Equal(0, stub.Calls);
    }

    [Fact]
    public async Task CancelMidBatch_ReturnsPartial()
    {
        // El stub devuelve el centinela que hace lanzar OperationCanceledException
        // al procesar b.txt: se conserva el parcial de a.txt.
        var stub = new ScriptedImporter(p =>
            p.EndsWith("b.txt")
                ? new ImportResult { Succeeded = false, SourcePath = "#throw-cancel" }
                : Ok(p));
        var svc = new BatchImportService(
            stub,
            new AppErrorHandler(NullLogger<AppErrorHandler>.Instance),
            NullLogger<BatchImportService>.Instance);
        var result = await svc.ImportBatchAsync(["a.txt", "b.txt", "c.txt"], null);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.WasCancelled);
        Assert.Equal(1, result.Value.Imported);
        Assert.Equal(1, result.Value.Attempted);
    }

    [Fact]
    public async Task ServiceFailure_CountsAsFailed()
    {
        var svc = Create(p => new ImportResult { Succeeded = false, SourcePath = "#throw-failure" }, out _);
        var result = await svc.ImportBatchAsync(["x.txt"], null);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Failed);
        Assert.Single(result.Value.Failures);
    }

    [Fact]
    public async Task Progress_ReachesTotal()
    {
        var svc = Create(Ok, out _);
        var seen = new List<ImportProgress>();
        var progress = new Progress<ImportProgress>(p => seen.Add(p));
        await svc.ImportBatchAsync(["a.txt", "b.txt"], null, null, progress);

        Assert.NotEmpty(seen);
        var last = seen[^1];
        Assert.Equal(2, last.Processed);
        Assert.Equal(2, last.Total);
    }

    [Fact]
    public void Scanner_FiltersSortsAndRecurses()
    {
        using var temp = new TempDirectory();
        temp.CreateFile("b.txt", "x");
        temp.CreateFile("a.txt", "x");
        temp.CreateFile("no.exe", "x");
        Directory.CreateDirectory(Path.Combine(temp.Path, "sub"));
        File.WriteAllText(Path.Combine(temp.Path, "sub", "c.txt"), "x");
        var hidden = Path.Combine(temp.Path, "h.txt");
        File.WriteAllText(hidden, "x");
        File.SetAttributes(hidden, FileAttributes.Hidden);

        var scanner = new FolderScanner();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var flat = scanner.Enumerate(temp.Path, recursive: false, cts.Token);
        Assert.Equal(2, flat.Files.Count);
        Assert.Equal("a.txt", Path.GetFileName(flat.Files[0]));
        Assert.Equal("b.txt", Path.GetFileName(flat.Files[1]));

        var deep = scanner.Enumerate(temp.Path, recursive: true, cts.Token);
        Assert.Equal(3, deep.Files.Count);
        // no.exe cuenta como omitido; h.txt lo filtra el SO y no se ve.
        Assert.True(deep.SkippedCount >= 1);
    }

    [Fact]
    public void Scanner_MissingFolder_ReturnsEmpty()
    {
        var scanner = new FolderScanner();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var result = scanner.Enumerate(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), true, cts.Token);
        Assert.Empty(result.Files);
    }
}
