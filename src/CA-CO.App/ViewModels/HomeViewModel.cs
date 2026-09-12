using System.Collections.ObjectModel;
using CaCo.App.Services;
using CaCo.Application.Errors;
using CaCo.Application.Import;
using CaCo.Application.Services;
using CaCo.Domain;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CaCo.App.ViewModels;

/// <summary>ViewModel de la página de inicio: bienvenida, cifras, recientes y accesos rápidos.</summary>
public sealed partial class HomeViewModel : ViewModelBase
{
    private readonly ILibraryService _library;
    private readonly IBatchImportService _batch;
    private readonly INotebookService _notebooks;
    private readonly IFilePickerService _picker;
    private readonly IDialogService _dialogs;
    private readonly INavigationService _navigation;

    /// <summary>Crea el ViewModel.</summary>
    public HomeViewModel(
        IErrorHandler errors,
        ILibraryService library,
        IBatchImportService batch,
        INotebookService notebooks,
        IFilePickerService picker,
        IDialogService dialogs,
        INavigationService navigation)
        : base(errors)
    {
        _library = library;
        _batch = batch;
        _notebooks = notebooks;
        _picker = picker;
        _dialogs = dialogs;
        _navigation = navigation;
    }

    /// <summary>Documentos recientes.</summary>
    public ObservableCollection<Document> RecentDocuments { get; } = [];

    /// <summary>Número de documentos.</summary>
    [ObservableProperty]
    private int _documentCount;

    /// <summary>Número de cuadernos.</summary>
    [ObservableProperty]
    private int _notebookCount;

    /// <summary>Número de favoritos.</summary>
    [ObservableProperty]
    private int _favoriteCount;

    /// <summary>Tamaño total legible (p. ej. "12,4 MB").</summary>
    [ObservableProperty]
    private string _totalSizeText = "—";

    /// <summary>Indica si la biblioteca está vacía (primera experiencia).</summary>
    [ObservableProperty]
    private bool _isEmpty = true;

    /// <inheritdoc/>
    public override async Task OnNavigatedToAsync(CancellationToken ct)
    {
        await RefreshAsync(ct);
    }

    /// <summary>Recarga cifras y recientes.</summary>
    [RelayCommand]
    private async Task RefreshAsync(CancellationToken ct)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        ClearMessages();
        try
        {
            var stats = await _library.GetStatsAsync(ct);
            if (stats.IsFailure)
            {
                ShowError(stats.Error);
                return;
            }

            DocumentCount = stats.Value.DocumentCount;
            NotebookCount = stats.Value.NotebookCount;
            FavoriteCount = stats.Value.FavoriteCount;
            TotalSizeText = Views.DocumentFormat.Size(stats.Value.TotalBytes);
            IsEmpty = stats.Value.DocumentCount == 0 && stats.Value.NotebookCount == 0;

            var recent = await _library.GetRecentDocumentsAsync(5, ct);
            RecentDocuments.Clear();
            if (recent.IsFailure)
            {
                ShowError(recent.Error);
                return;
            }

            foreach (var doc in recent.Value)
            {
                RecentDocuments.Add(doc);
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

    /// <summary>Importa documentos con el selector del sistema.</summary>
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
            var result = await _batch.ImportBatchAsync(picked, null, null, null, ct);
            if (result.IsFailure)
            {
                ShowError(result.Error);
                return;
            }

            var batch = result.Value;
            if (batch.RejectedByLimit)
            {
                ShowError(CaCo.Core.Error.Validation("Import.BatchTooLarge", batch.LimitReason ?? "Lote demasiado grande."));
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
                parts.Add($"{batch.Duplicates} ya estaban en tu biblioteca");
            }

            if (batch.Failed > 0)
            {
                parts.Add($"{batch.Failed} con error");
            }

            ShowInfo(parts.Count > 0 ? string.Join(" · ", parts) + "." : "Nada que importar.");
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

    /// <summary>Refresca aunque la importación mantenga <see cref="ViewModelBase.IsBusy"/>.</summary>
    private async Task RefreshNowAsync()
    {
        IsBusy = false;
        await RefreshAsync(CancellationToken.None);
    }

    /// <summary>Crea un cuaderno (pide el nombre).</summary>
    [RelayCommand]
    private async Task CreateNotebookAsync(CancellationToken ct)
    {
        ClearMessages();
        string? name;
        try
        {
            name = await _dialogs.PromptTextAsync("Crear cuaderno", "Nombre del cuaderno");
        }
        catch (Exception ex)
        {
            ShowError(ex);
            return;
        }

        if (name is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var created = await _notebooks.CreateAsync(name, null, ct);
            if (created.IsFailure)
            {
                ShowError(created.Error);
                return;
            }

            await RefreshNowAsync();
            ShowInfo($"Cuaderno «{created.Value.Name}» creado.");
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

    /// <summary>Va a la biblioteca de documentos.</summary>
    [RelayCommand]
    private void GoToDocuments() => _navigation.NavigateTo<DocumentsViewModel>();

    /// <summary>Va a los cuadernos.</summary>
    [RelayCommand]
    private void GoToNotebooks() => _navigation.NavigateTo<NotebooksViewModel>();

    /// <summary>Abre el detalle de un documento reciente.</summary>
    [RelayCommand]
    private void OpenDetails(Document? document)
    {
        if (document is null)
        {
            return;
        }

        _navigation.NavigateTo<ReaderViewModel>(document.Id);
    }
}
