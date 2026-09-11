using System.Collections.ObjectModel;
using CaCo.App.Services;
using CaCo.Application.Errors;
using CaCo.Application.Services;
using CaCo.Domain;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CaCo.App.ViewModels;

/// <summary>Fila del árbol de cuadernos con su nivel de indentación.</summary>
public sealed partial class NotebookRow : ObservableObject
{
    /// <summary>Crea la fila.</summary>
    public NotebookRow(Notebook notebook, int level, int documentCount)
    {
        Notebook = notebook;
        Level = level;
        DocumentCount = documentCount;
    }

    /// <summary>Cuaderno.</summary>
    [ObservableProperty]
    private Notebook _notebook = null!;

    /// <summary>Profundidad (0 = raíz).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IndentText))]
    private int _level;

    /// <summary>Documentos directos (punto de extensión: conteo eficiente en Fase 1).</summary>
    [ObservableProperty]
    private int _documentCount;

    /// <summary>Sangría visual (espacios) según profundidad. Solo presentación.</summary>
    public string IndentText => new string(' ', Level * 4);
}

/// <summary>ViewModel de cuadernos: árbol jerárquico + documentos del seleccionado.</summary>
public sealed partial class NotebooksViewModel : ViewModelBase
{
    private readonly INotebookService _notebooks;
    private readonly IDocumentService _documents;
    private readonly IDialogService _dialogs;
    private readonly INavigationService _navigation;

    /// <summary>Crea el ViewModel.</summary>
    public NotebooksViewModel(
        IErrorHandler errors,
        INotebookService notebooks,
        IDocumentService documents,
        IDialogService dialogs,
        INavigationService navigation)
        : base(errors)
    {
        _notebooks = notebooks;
        _documents = documents;
        _dialogs = dialogs;
        _navigation = navigation;
    }

    /// <summary>Filas del árbol (aplanado con nivel).</summary>
    public ObservableCollection<NotebookRow> Rows { get; } = [];

    /// <summary>Documentos del cuaderno seleccionado.</summary>
    public ObservableCollection<Document> SelectedDocuments { get; } = [];

    /// <summary>Fila seleccionada.</summary>
    [ObservableProperty]
    private NotebookRow? _selectedRow;

    /// <summary>Número de cuadernos.</summary>
    [ObservableProperty]
    private int _notebookCount;

    /// <summary>Nombre del cuaderno seleccionado (cabecera del panel derecho).</summary>
    [ObservableProperty]
    private string _selectedNotebookName = "—";

    /// <inheritdoc/>
    public override async Task OnNavigatedToAsync(CancellationToken ct)
    {
        await RefreshAsync(ct);
    }

    /// <summary>Recarga el árbol.</summary>
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
            var all = await _notebooks.ListAllActiveAsync(ct);
            if (all.IsFailure)
            {
                ShowError(all.Error);
                return;
            }

            var selectedId = SelectedRow?.Notebook.Id;
            Rows.Clear();
            foreach (var row in BuildRows(all.Value))
            {
                Rows.Add(row);
            }

            NotebookCount = all.Value.Count;
            SelectedRow = Rows.FirstOrDefault(r => r.Notebook.Id == selectedId) ?? Rows.FirstOrDefault();
            await LoadSelectedDocumentsAsync(CancellationToken.None);
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

    private Guid? _lastLoadedNotebookId;

    /// <summary>Selecciona un cuaderno y muestra sus documentos.</summary>
    [RelayCommand]
    private async Task SelectAsync(NotebookRow? row, CancellationToken ct)
    {
        if (row?.Notebook.Id == _lastLoadedNotebookId && SelectedRow?.Notebook.Id == row?.Notebook.Id)
        {
            SelectedRow = row;
            return;
        }

        SelectedRow = row;
        await LoadSelectedDocumentsAsync(ct);
    }

    /// <summary>Abre el detalle de un documento del cuaderno.</summary>
    [RelayCommand]
    private void OpenDocument(Document? document)
    {
        if (document is null)
        {
            return;
        }

        _navigation.NavigateTo<ReaderViewModel>(document.Id);
    }

    /// <summary>Crea un cuaderno raíz o hijo del seleccionado.</summary>
    [RelayCommand]
    private async Task CreateAsync(CancellationToken ct)
    {
        try
        {
            var hint = SelectedRow is null ? "raíz" : $"dentro de «{SelectedRow.Notebook.Name}»";
            var name = await _dialogs.PromptTextAsync("Crear cuaderno", $"Nombre del cuaderno ({hint})");
            if (name is null)
            {
                return;
            }

            var created = await _notebooks.CreateAsync(name, SelectedRow?.Notebook.Id, ct);
            if (created.IsFailure)
            {
                ShowError(created.Error);
                return;
            }

            await RefreshAsync(CancellationToken.None);
            SelectedRow = Rows.FirstOrDefault(r => r.Notebook.Id == created.Value.Id);
            await LoadSelectedDocumentsAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    /// <summary>Renombra el seleccionado.</summary>
    [RelayCommand]
    private async Task RenameAsync(CancellationToken ct)
    {
        if (SelectedRow is null)
        {
            return;
        }

        try
        {
            var name = await _dialogs.PromptTextAsync("Renombrar cuaderno", "Nuevo nombre", SelectedRow.Notebook.Name);
            if (name is null)
            {
                return;
            }

            var result = await _notebooks.RenameAsync(SelectedRow.Notebook.Id, name, ct);
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

    /// <summary>Elimina el seleccionado (hijos suben, documentos se conservan).</summary>
    [RelayCommand]
    private async Task DeleteAsync(CancellationToken ct)
    {
        if (SelectedRow is null)
        {
            return;
        }

        try
        {
            var confirmed = await _dialogs.ConfirmAsync(
                "Eliminar cuaderno",
                $"«{SelectedRow.Notebook.Name}» se eliminará. Sus subcuadernos subirán de nivel y sus documentos se conservarán sin clasificar.",
                "Eliminar");
            if (!confirmed)
            {
                return;
            }

            var result = await _notebooks.DeleteAsync(SelectedRow.Notebook.Id, ct);
            if (result.IsFailure)
            {
                ShowError(result.Error);
                return;
            }

            SelectedRow = null;
            await RefreshAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private async Task LoadSelectedDocumentsAsync(CancellationToken ct)
    {
        SelectedDocuments.Clear();
        SelectedNotebookName = SelectedRow?.Notebook.Name ?? "—";
        if (SelectedRow is null)
        {
            _lastLoadedNotebookId = null;
            return;
        }

        _lastLoadedNotebookId = SelectedRow.Notebook.Id;

        var docs = await _documents.ListAsync(
            new Application.Repositories.DocumentQuery { NotebookId = SelectedRow.Notebook.Id, Page = 1, PageSize = 100 },
            ct);
        if (docs.IsFailure)
        {
            ShowError(docs.Error);
            return;
        }

        foreach (var doc in docs.Value.Items)
        {
            SelectedDocuments.Add(doc);
        }
    }

    internal static List<NotebookRow> BuildRows(IReadOnlyList<Notebook> notebooks)
    {
        // Documentos por cuaderno no disponibles aquí sin repositorio: conteo en 0.
        // La vista muestra el conteo real cargando documentos del seleccionado.
        var byParent = notebooks.ToLookup(n => n.ParentId);
        var rows = new List<NotebookRow>();
        AddChildren(byParent, null, 0, rows);
        return rows;
    }

    private static void AddChildren(
        ILookup<Guid?, Notebook> byParent,
        Guid? parentId,
        int level,
        List<NotebookRow> rows)
    {
        foreach (var child in byParent[parentId].OrderBy(n => n.Name))
        {
            rows.Add(new NotebookRow(child, level, 0));
            AddChildren(byParent, child.Id, level + 1, rows);
        }
    }
}
