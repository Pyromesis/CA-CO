using System.Collections.ObjectModel;
using CaCo.App.Services;
using CaCo.Application.Errors;
using CaCo.Application.Import;
using CaCo.Application.Repositories;
using CaCo.Application.Search;
using CaCo.Application.Services;
using CaCo.Core;
using CaCo.Domain;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CaCo.App.ViewModels;

/// <summary>
/// Base de las páginas con listas de documentos (Documentos, Favoritos, Recientes, Papelera).
/// Carga paginada ("Cargar más") para no retener la biblioteca completa en memoria.
/// </summary>
public abstract partial class DocumentListViewModel : ViewModelBase
{
    private readonly IDocumentService _documents;
    private readonly INavigationService _navigation;

    /// <summary>Servicios UI compartidos.</summary>
    protected readonly IDialogService Dialogs;

    /// <summary>Crea la base.</summary>
    protected DocumentListViewModel(
        IErrorHandler errors,
        IDocumentService documents,
        IDialogService dialogs,
        INavigationService navigation)
        : base(errors)
    {
        _documents = documents;
        Dialogs = dialogs;
        _navigation = navigation;
    }

    /// <summary>Elementos cargados.</summary>
    public ObservableCollection<Document> Items { get; } = [];

    /// <summary>Total (sin paginar).</summary>
    [ObservableProperty]
    private int _totalCount;

    /// <summary>Indica si hay más páginas.</summary>
    [ObservableProperty]
    private bool _hasMore;

    /// <summary>Indica si la lista está vacía (estado vacío de la vista).</summary>
    [ObservableProperty]
    private bool _isEmptyList = true;

    /// <summary>Título de la sección (para la vista).</summary>
    public abstract string Title { get; }

    /// <summary>Texto cuando la lista está vacía.</summary>
    public abstract string EmptyText { get; }

    /// <summary>Indica si la papelera admite restaurar (solo Papelera).</summary>
    public virtual bool CanRestore => false;

    /// <summary>Construye la consulta para la página indicada.</summary>
    protected abstract DocumentQuery BuildQuery(int page);

    /// <summary>Tamaño de página.</summary>
    protected virtual int PageSize => 50;

    private int _currentPage = 1;

    /// <inheritdoc/>
    public override async Task OnNavigatedToAsync(CancellationToken ct)
    {
        await RefreshAsync(ct);
    }

    /// <summary>Recarga desde la primera página.</summary>
    [RelayCommand]
    protected async Task RefreshAsync(CancellationToken ct)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        ClearMessages();
        try
        {
            var result = await _documents.ListAsync(BuildQuery(1), ct);
            if (result.IsFailure)
            {
                ShowError(result.Error);
                return;
            }

            Items.Clear();
            foreach (var doc in result.Value.Items)
            {
                Items.Add(doc);
            }

            TotalCount = result.Value.TotalCount;
            HasMore = result.Value.HasNextPage;
            IsEmptyList = Items.Count == 0;
            _currentPage = 1;
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Carga la siguiente página.</summary>
    [RelayCommand]
    protected async Task LoadMoreAsync(CancellationToken ct)
    {
        if (IsBusy || !HasMore)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var nextPage = _currentPage + 1;
            var result = await _documents.ListAsync(BuildQuery(nextPage), ct);
            if (result.IsFailure)
            {
                ShowError(result.Error);
                return;
            }

            foreach (var doc in result.Value.Items)
            {
                Items.Add(doc);
            }

            TotalCount = result.Value.TotalCount;
            HasMore = result.Value.HasNextPage;
            IsEmptyList = Items.Count == 0;
            _currentPage = nextPage;
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Marca o desmarca favorito.</summary>
    [RelayCommand]
    protected async Task ToggleFavoriteAsync(Document? document, CancellationToken ct)
    {
        if (document is null)
        {
            return;
        }

        var newValue = !document.IsFavorite;
        try
        {
            var result = await _documents.SetFavoriteAsync(document.Id, newValue, ct);
            if (result.IsFailure)
            {
                ShowError(result.Error);
                return;
            }

            // Sincroniza la fila (la entidad del servicio es otra instancia).
            document.SetFavorite(newValue);
            AfterToggleFavorite(document);
            await RefreshAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    /// <summary>Ajuste inmediato tras cambiar favorito (p. ej. quitar de la lista).</summary>
    protected virtual void AfterToggleFavorite(Document document)
    {
    }

    /// <summary>Mueve a la papelera (una confirmación: queda en la papelera).</summary>
    [RelayCommand]
    protected async Task MoveToTrashAsync(Document? document, CancellationToken ct)
    {
        if (document is null)
        {
            return;
        }

        try
        {
            var confirmed = await Dialogs.ConfirmAsync(
                "Mover a la papelera",
                $"¿Estás seguro de que quieres eliminar «{document.Name}»? Quedará en la papelera.",
                "Mover a la papelera");
            if (!confirmed)
            {
                return;
            }

            var result = await _documents.MoveToTrashAsync(document.Id, ct);
            if (result.IsFailure)
            {
                ShowError(result.Error);
                return;
            }

            await RefreshAsync(CancellationToken.None);
            ShowInfo($"«{document.Name}» se movió a la papelera.");
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    /// <summary>Restaura desde la papelera.</summary>
    [RelayCommand]
    protected async Task RestoreAsync(Document? document, CancellationToken ct)
    {
        if (document is null)
        {
            return;
        }

        try
        {
            var result = await _documents.RestoreAsync(document.Id, ct);
            if (result.IsFailure)
            {
                ShowError(result.Error);
                return;
            }

            await RefreshAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    /// <summary>Elimina definitivamente (doble confirmación: es permanente).</summary>
    [RelayCommand]
    protected async Task DeletePermanentlyAsync(Document? document, CancellationToken ct)
    {
        if (document is null)
        {
            return;
        }

        try
        {
            var confirmed = await Dialogs.ConfirmAsync(
                "Eliminar definitivamente",
                $"¿Estás seguro de que quieres eliminar «{document.Name}»?",
                "Eliminar");
            if (!confirmed)
            {
                return;
            }

            confirmed = await Dialogs.ConfirmAsync(
                "Eliminar definitivamente",
                $"«{document.Name}» se eliminará de forma permanente. Esta acción no se puede deshacer.",
                "Eliminar definitivamente");
            if (!confirmed)
            {
                return;
            }

            var result = await _documents.DeletePermanentlyAsync(document.Id, ct);
            if (result.IsFailure)
            {
                ShowError(result.Error);
                return;
            }

            await RefreshAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    /// <summary>Renombra (pide el nuevo nombre).</summary>
    [RelayCommand]
    protected async Task RenameAsync(Document? document, CancellationToken ct)
    {
        if (document is null)
        {
            return;
        }

        try
        {
            var name = await Dialogs.PromptTextAsync("Renombrar", "Nuevo nombre", document.Name);
            if (name is null)
            {
                return;
            }

            var result = await _documents.RenameAsync(document.Id, name, ct);
            if (result.IsFailure)
            {
                ShowError(result.Error);
                return;
            }

            await RefreshAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    /// <summary>Quita el aviso de error/info.</summary>
    [RelayCommand]
    private void DismissMessages() => ClearMessages();

    /// <summary>Abre el detalle del documento.</summary>
    [RelayCommand]
    protected void OpenDetails(Document? document)
    {
        if (document is null)
        {
            return;
        }

        _navigation.NavigateTo<ReaderViewModel>(document.Id);
    }
}

/// <summary>Biblioteca completa + búsqueda + importación.</summary>
public sealed partial class DocumentsViewModel : DocumentListViewModel
{
    private readonly IBatchImportService _batch;
    private readonly IFolderScanner _scanner;
    private readonly IFilePickerService _picker;
    private readonly IFolderPickerService _folderPicker;
    private readonly ISearchService _search;
    private CancellationTokenSource? _importCts;

    /// <summary>Crea el ViewModel.</summary>
    public DocumentsViewModel(
        IErrorHandler errors,
        IDocumentService documents,
        IDialogService dialogs,
        INavigationService navigation,
        IBatchImportService batch,
        IFolderScanner scanner,
        IFilePickerService picker,
        IFolderPickerService folderPicker,
        ISearchService search)
        : base(errors, documents, dialogs, navigation)
    {
        _batch = batch;
        _scanner = scanner;
        _picker = picker;
        _folderPicker = folderPicker;
        _search = search;
    }

    /// <summary>Si hay una importación en curso.</summary>
    [ObservableProperty]
    private bool _isImporting;

    /// <summary>Progreso de importación 0-100.</summary>
    [ObservableProperty]
    private double _importProgress;

    /// <summary>Texto de progreso ("3/12 · nombre").</summary>
    [ObservableProperty]
    private string _importStatus = string.Empty;

    /// <inheritdoc/>
    public override string Title => "Documentos";

    /// <inheritdoc/>
    public override string EmptyText => "Aún no hay documentos. Importa tu primer archivo para empezar.";

    /// <summary>Filtro de búsqueda (nombres, etiquetas, notas, OCR).</summary>
    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>Tipos para el filtro (índice 0 = todos).</summary>
    public string[] SearchTypeOptions { get; } =
        ["Todos", "PDF", "Imagen", "Word", "Excel", "Texto"];

    /// <summary>Índice del tipo elegido.</summary>
    [ObservableProperty]
    private int _searchTypeIndex;

    /// <summary>Solo favoritos.</summary>
    [ObservableProperty]
    private bool _searchFavoritesOnly;

    /// <summary>Explicación de la última búsqueda ("7 resultados · nombre, OCR").</summary>
    [ObservableProperty]
    private string _searchExplanation = string.Empty;

    /// <summary>Si la lista muestra resultados de búsqueda (sin "cargar más").</summary>
    [ObservableProperty]
    private bool _isSearchResult;

    partial void OnSearchTextChanged(string value)
    {
        IsSearchResult = false;
        SearchExplanation = string.Empty;
    }

    partial void OnSearchTypeIndexChanged(int value)
    {
        IsSearchResult = false;
        SearchExplanation = string.Empty;
    }

    partial void OnSearchFavoritesOnlyChanged(bool value)
    {
        IsSearchResult = false;
        SearchExplanation = string.Empty;
    }

    /// <inheritdoc/>
    protected override DocumentQuery BuildQuery(int page) => new()
    {
        SearchText = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim(),
        Page = page,
        PageSize = PageSize,
    };

    /// <summary>Aplica la búsqueda avanzada (o vuelve al explorado si no hay criterios).</summary>
    [RelayCommand]
    private async Task SearchAsync(CancellationToken ct)
    {
        var text = SearchText?.Trim() ?? string.Empty;
        if (text.Length == 0 && SearchTypeIndex == 0 && !SearchFavoritesOnly)
        {
            IsSearchResult = false;
            SearchExplanation = string.Empty;
            await RefreshAsync(ct);
            return;
        }

        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        ClearMessages();
        try
        {
            var query = new SearchQuery
            {
                Text = text,
                FileType = SearchTypeIndex switch
                {
                    1 => DocumentType.Pdf,
                    // 2 = Imagen (PNG/JPG/JPEG): se filtra en memoria abajo.
                    3 => DocumentType.Docx,
                    4 => DocumentType.Xlsx,
                    5 => DocumentType.Txt,
                    _ => null,
                },
                FavoritesOnly = SearchFavoritesOnly,
                MaxResults = 100,
            };
            // JPG/JPEG comparten filtro "Imagen".
            var hits = await _search.SearchAdvancedAsync(query, ct);
            if (hits.IsFailure)
            {
                ShowError(hits.Error);
                return;
            }

            var list = hits.Value;
            if (SearchTypeIndex == 2)
            {
                list = list.Where(h =>
                    h.Document.FileType is DocumentType.Png or DocumentType.Jpg or DocumentType.Jpeg).ToList();
            }

            Items.Clear();
            foreach (var hit in list)
            {
                Items.Add(hit.Document);
            }

            TotalCount = list.Count;
            HasMore = false;
            IsEmptyList = Items.Count == 0;
            IsSearchResult = true;
            var fields = list.SelectMany(h => h.MatchedIn).Distinct().ToList();
            SearchExplanation = list.Count == 0
                ? "Sin resultados. Prueba con menos filtros."
                : $"{list.Count} resultado(s)" + (fields.Count > 0 ? $" · coincide en: {string.Join(", ", fields)}." : ".");
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Importa con el selector del sistema.</summary>
    [RelayCommand]
    private async Task ImportAsync(CancellationToken ct)
    {
        ClearMessages();
        IReadOnlyList<string> picked;
        try
        {
            picked = await _picker.PickDocumentsAsync();
        }
        catch (Exception ex)
        {
            ShowError(ex);
            return;
        }

        if (picked.Count == 0)
        {
            return;
        }

        await RunBatchAsync(picked, ct);
    }

    /// <summary>Importa una carpeta completa (recursiva) con el selector del sistema.</summary>
    [RelayCommand]
    private async Task ImportFolderAsync(CancellationToken ct)
    {
        ClearMessages();
        PickedFolder? folder;
        try
        {
            folder = await _folderPicker.PickFolderAsync();
        }
        catch (Exception ex)
        {
            ShowError(ex);
            return;
        }

        if (folder is null)
        {
            return;
        }

        FolderScanResult scan;
        try
        {
            scan = await Task.Run(() => _scanner.Enumerate(folder.Path, recursive: true, ct), ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            ShowError(ex);
            return;
        }

        if (scan.Files.Count == 0)
        {
            ShowInfo("No hay documentos importables en esa carpeta.");
            return;
        }

        await RunBatchAsync(scan.Files, ct);
    }

    /// <summary>Cancela la importación en curso.</summary>
    [RelayCommand]
    private void CancelImport()
    {
        _importCts?.Cancel();
    }

    /// <summary>Ejecuta un lote con progreso, cancelación e informe final.</summary>
    private async Task RunBatchAsync(IEnumerable<string> paths, CancellationToken ct)
    {
        if (IsImporting)
        {
            return;
        }

        IsImporting = true;
        IsBusy = true;
        ImportProgress = 0;
        ImportStatus = "Preparando…";
        _importCts?.Dispose();
        _importCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var batchToken = _importCts.Token;
        try
        {
            var progress = new Progress<ImportProgress>(p =>
            {
                ImportProgress = p.Total > 0 ? p.Processed * 100.0 / p.Total : 0;
                ImportStatus = string.IsNullOrEmpty(p.CurrentName)
                    ? $"{p.Processed}/{p.Total}"
                    : $"{p.Processed + 1}/{p.Total} · {p.CurrentName}";
            });
            var result = await _batch.ImportBatchAsync(paths, null, null, progress, batchToken);
            if (result.IsFailure)
            {
                ShowError(result.Error);
                return;
            }

            var batch = result.Value;
            if (batch.RejectedByLimit)
            {
                ShowError(Error.Validation("Import.BatchTooLarge", batch.LimitReason ?? "Lote demasiado grande."));
                return;
            }

            await RefreshNowAsync();
            var parts = new List<string>();
            if (batch.Imported > 0)
            {
                parts.Add($"{batch.Imported} importado(s)");
            }

            if (batch.Duplicates > 0)
            {
                parts.Add($"{batch.Duplicates} ya estaban");
            }

            if (batch.Failed > 0)
            {
                parts.Add($"{batch.Failed} con error");
            }

            var summary = parts.Count > 0 ? string.Join(" · ", parts) + "." : "Nada que importar.";
            if (batch.WasCancelled)
            {
                summary = "Cancelado: " + char.ToLowerInvariant(summary[0]) + summary[1..];
            }

            ShowInfo(summary);
            if (batch.Failures.Count > 0)
            {
                var details = string.Join(
                    Environment.NewLine,
                    batch.Failures.Take(10).Select(f => $"• {f.FileName}: {f.Reason}"));
                if (batch.Failures.Count > 10)
                {
                    details += $"{Environment.NewLine}… y {batch.Failures.Count - 10} más.";
                }

                await Dialogs.ShowMessageAsync("Detalles de importación", details);
            }
        }
        catch (OperationCanceledException)
        {
            ShowInfo("Importación cancelada.");
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
        finally
        {
            IsImporting = false;
            IsBusy = false;
            ImportProgress = 0;
            ImportStatus = string.Empty;
        }
    }

    /// <summary>Refresca aunque la operación actual mantenga <see cref="ViewModelBase.IsBusy"/>.</summary>
    private async Task RefreshNowAsync()
    {
        IsBusy = false;
        await RefreshAsync(CancellationToken.None);
    }

    /// <summary>Importa rutas ya elegidas (drag &amp; drop desde la vista).</summary>
    public async Task ImportPathsAsync(IEnumerable<string> paths, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ClearMessages();
        await RunBatchAsync(paths, ct);
    }
}

/// <summary>Solo favoritos.</summary>
public sealed class FavoritesViewModel : DocumentListViewModel
{
    /// <summary>Crea el ViewModel.</summary>
    public FavoritesViewModel(
        IErrorHandler errors,
        IDocumentService documents,
        IDialogService dialogs,
        INavigationService navigation)
        : base(errors, documents, dialogs, navigation)
    {
    }

    /// <inheritdoc/>
    public override string Title => "Favoritos";

    /// <inheritdoc/>
    public override string EmptyText => "Aún no tienes favoritos. Marca documentos con la estrella para verlos aquí.";

    /// <inheritdoc/>
    protected override DocumentQuery BuildQuery(int page) => new()
    {
        FavoritesOnly = true,
        Page = page,
        PageSize = PageSize,
    };

    /// <summary>Al quitar la estrella, la fila sale al instante (luego se reconcilia).</summary>
    protected override void AfterToggleFavorite(Document document)
    {
        if (!document.IsFavorite && Items.Remove(document))
        {
            TotalCount = Math.Max(0, TotalCount - 1);
            IsEmptyList = Items.Count == 0;
        }
    }
}

/// <summary>Importados recientemente.</summary>
public sealed class RecentsViewModel : DocumentListViewModel
{
    /// <summary>Crea el ViewModel.</summary>
    public RecentsViewModel(
        IErrorHandler errors,
        IDocumentService documents,
        IDialogService dialogs,
        INavigationService navigation)
        : base(errors, documents, dialogs, navigation)
    {
    }

    /// <inheritdoc/>
    public override string Title => "Recientes";

    /// <inheritdoc/>
    public override string EmptyText => "Nada por aquí todavía. Los documentos que importes aparecerán aquí.";

    /// <inheritdoc/>
    protected override int PageSize => 20;

    /// <inheritdoc/>
    protected override DocumentQuery BuildQuery(int page) => new()
    {
        Page = page,
        PageSize = PageSize,
    };
}

/// <summary>Papelera: restaurar, eliminar o vaciar.</summary>
public sealed partial class TrashViewModel : DocumentListViewModel
{
    private readonly IDocumentService _documents;

    /// <summary>Crea el ViewModel.</summary>
    public TrashViewModel(
        IErrorHandler errors,
        IDocumentService documents,
        IDialogService dialogs,
        INavigationService navigation)
        : base(errors, documents, dialogs, navigation)
    {
        _documents = documents;
    }

    /// <inheritdoc/>
    public override string Title => "Papelera";

    /// <inheritdoc/>
    public override string EmptyText => "La papelera está vacía.";

    /// <inheritdoc/>
    public override bool CanRestore => true;

    /// <inheritdoc/>
    protected override DocumentQuery BuildQuery(int page) => new()
    {
        DeletedOnly = true,
        IncludeDeleted = true,
        Page = page,
        PageSize = PageSize,
    };

    /// <summary>Vacía la papelera (con confirmación).</summary>
    [RelayCommand]
    private async Task EmptyTrashAsync(CancellationToken ct)
    {
        try
        {
            var confirmed = await Dialogs.ConfirmAsync(
                "Vaciar papelera",
                "¿Estás seguro de que quieres vaciar la papelera?",
                "Vaciar");
            if (!confirmed)
            {
                return;
            }

            confirmed = await Dialogs.ConfirmAsync(
                "Vaciar papelera",
                "Todo el contenido de la papelera se eliminará de forma permanente.",
                "Vaciar definitivamente");
            if (!confirmed)
            {
                return;
            }

            IsBusy = true;
            var result = await _documents.EmptyTrashAsync(ct);
            if (result.IsFailure)
            {
                ShowError(result.Error);
                return;
            }

            IsBusy = false;
            await RefreshAsync(CancellationToken.None);
            ShowInfo($"Se eliminaron {result.Value} documento(s).");
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
