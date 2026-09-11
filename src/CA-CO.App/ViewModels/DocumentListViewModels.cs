using System.Collections.ObjectModel;
using CaCo.App.Services;
using CaCo.Application.Errors;
using CaCo.Application.Import;
using CaCo.Application.Repositories;
using CaCo.Application.Services;
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

    /// <summary>Mueve a la papelera.</summary>
    [RelayCommand]
    protected async Task MoveToTrashAsync(Document? document, CancellationToken ct)
    {
        if (document is null)
        {
            return;
        }

        try
        {
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

    /// <summary>Elimina definitivamente (con confirmación).</summary>
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
                $"«{document.Name}» se eliminará para siempre. Esta acción no se puede deshacer.",
                "Eliminar");
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
    private readonly IDocumentImporter _importer;
    private readonly IFilePickerService _picker;

    /// <summary>Crea el ViewModel.</summary>
    public DocumentsViewModel(
        IErrorHandler errors,
        IDocumentService documents,
        IDialogService dialogs,
        INavigationService navigation,
        IDocumentImporter importer,
        IFilePickerService picker)
        : base(errors, documents, dialogs, navigation)
    {
        _importer = importer;
        _picker = picker;
    }

    /// <inheritdoc/>
    public override string Title => "Documentos";

    /// <inheritdoc/>
    public override string EmptyText => "Aún no hay documentos. Importa tu primer archivo para empezar.";

    /// <summary>Filtro de búsqueda por nombre.</summary>
    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <inheritdoc/>
    protected override DocumentQuery BuildQuery(int page) => new()
    {
        SearchText = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim(),
        Page = page,
        PageSize = PageSize,
    };

    /// <summary>Aplica la búsqueda.</summary>
    [RelayCommand]
    private async Task SearchAsync(CancellationToken ct) => await RefreshAsync(ct);

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

        IsBusy = true;
        try
        {
            var ok = 0;
            foreach (var path in picked)
            {
                ct.ThrowIfCancellationRequested();
                var result = await _importer.ImportAsync(new ImportRequest(path), ct);
                if (result.IsFailure)
                {
                    ShowError(result.Error);
                    break;
                }

                if (result.Value.Succeeded)
                {
                    ok++;
                }
                else if (result.Value.Error is not null && !result.Value.SkippedAsDuplicate)
                {
                    ShowError(result.Value.Error);
                    break;
                }
            }

            await RefreshNowAsync();
            if (ok > 0)
            {
                ShowInfo($"{ok} documento(s) importado(s).");
            }
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
        IsBusy = true;
        try
        {
            var ok = 0;
            var rejected = 0;
            foreach (var path in paths)
            {
                ct.ThrowIfCancellationRequested();
                var result = await _importer.ImportAsync(new ImportRequest(path), ct);
                if (result.IsSuccess && result.Value.Succeeded)
                {
                    ok++;
                }
                else
                {
                    rejected++;
                }
            }

            await RefreshNowAsync();
            ShowInfo(rejected == 0
                ? $"{ok} documento(s) importado(s)."
                : $"{ok} importado(s), {rejected} no soportado(s) u omitido(s).");
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
                "Se eliminarán definitivamente todos los documentos de la papelera.",
                "Vaciar");
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
