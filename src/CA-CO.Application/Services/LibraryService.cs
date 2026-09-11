using CaCo.Application.Repositories;
using CaCo.Application.Storage;
using CaCo.Core;
using CaCo.Domain;

namespace CaCo.Application.Services;

/// <summary>Estadísticas globales de la biblioteca (página de inicio).</summary>
public sealed record LibraryStats
{
    /// <summary>Documentos activos.</summary>
    public required int DocumentCount { get; init; }

    /// <summary>Cuadernos activos.</summary>
    public required int NotebookCount { get; init; }

    /// <summary>Documentos marcados como favoritos.</summary>
    public required int FavoriteCount { get; init; }

    /// <summary>Documentos en papelera.</summary>
    public required int TrashCount { get; init; }

    /// <summary>Tamaño total de documentos activos en bytes.</summary>
    public required long TotalBytes { get; init; }
}

/// <summary>Caso de uso: estado global e inicialización de la biblioteca.</summary>
public interface ILibraryService
{
    /// <summary>Crea la estructura inicial si no existe (primera ejecución).</summary>
    Task<Result<bool>> EnsureInitializedAsync(CancellationToken ct);

    /// <summary>Estadísticas para la página de inicio.</summary>
    Task<Result<LibraryStats>> GetStatsAsync(CancellationToken ct);

    /// <summary>Documentos recientes para accesos rápidos.</summary>
    Task<Result<IReadOnlyList<Document>>> GetRecentDocumentsAsync(int count, CancellationToken ct);
}

/// <summary>Implementación de <see cref="ILibraryService"/>.</summary>
public sealed class LibraryService(
    ILibraryInitializer initializer,
    IDocumentRepository documents,
    INotebookRepository notebooks) : ILibraryService
{
    /// <inheritdoc/>
    public async Task<Result<bool>> EnsureInitializedAsync(CancellationToken ct)
    {
        try
        {
            return Result.Success(await initializer.EnsureCreatedAsync(ct).ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            return Result.Failure<bool>(Error.Storage("Library.InitFailed", $"No se pudo inicializar la biblioteca: {ex.Message}"));
        }
    }

    /// <inheritdoc/>
    public async Task<Result<LibraryStats>> GetStatsAsync(CancellationToken ct)
    {
        var docCountTask = documents.CountActiveAsync(ct);
        var notebookCountTask = notebooks.CountActiveAsync(ct);
        var totalBytesTask = documents.SumActiveBytesAsync(ct);
        var favoritesTask = documents.ListAsync(
            new DocumentQuery { FavoritesOnly = true, Page = 1, PageSize = 1 }, ct);
        var trashTask = documents.ListAsync(
            new DocumentQuery { DeletedOnly = true, IncludeDeleted = true, Page = 1, PageSize = 1 }, ct);

        await Task.WhenAll(docCountTask, notebookCountTask, totalBytesTask, favoritesTask, trashTask)
            .ConfigureAwait(false);

        var docCount = await docCountTask.ConfigureAwait(false);
        if (docCount.IsFailure)
        {
            return Result.Failure<LibraryStats>(docCount.Error);
        }

        var notebookCount = await notebookCountTask.ConfigureAwait(false);
        if (notebookCount.IsFailure)
        {
            return Result.Failure<LibraryStats>(notebookCount.Error);
        }

        var totalBytes = await totalBytesTask.ConfigureAwait(false);
        if (totalBytes.IsFailure)
        {
            return Result.Failure<LibraryStats>(totalBytes.Error);
        }

        var favorites = await favoritesTask.ConfigureAwait(false);
        if (favorites.IsFailure)
        {
            return Result.Failure<LibraryStats>(favorites.Error);
        }

        var trash = await trashTask.ConfigureAwait(false);
        if (trash.IsFailure)
        {
            return Result.Failure<LibraryStats>(trash.Error);
        }

        return Result.Success(new LibraryStats
        {
            DocumentCount = docCount.Value,
            NotebookCount = notebookCount.Value,
            FavoriteCount = favorites.Value.TotalCount,
            TrashCount = trash.Value.TotalCount,
            TotalBytes = totalBytes.Value,
        });
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<Document>>> GetRecentDocumentsAsync(int count, CancellationToken ct)
    {
        var page = await documents.ListAsync(
            new DocumentQuery { Page = 1, PageSize = Math.Clamp(count, 1, 100) },
            ct).ConfigureAwait(false);
        return page.IsFailure
            ? Result.Failure<IReadOnlyList<Document>>(page.Error)
            : Result.Success<IReadOnlyList<Document>>(page.Value.Items);
    }
}
