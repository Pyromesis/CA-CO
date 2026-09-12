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
    [NotifyPropertyChangedFor(nameof(IndentMargin))]
    private int _level;

    /// <summary>Documentos directos (punto de extensión: conteo eficiente en Fase 1).</summary>
    [ObservableProperty]
    private int _documentCount;

    /// <summary>Sangría visual según profundidad (los espacios los colapsa XAML).</summary>
    public Microsoft.UI.Xaml.Thickness IndentMargin => new(Level * 20, 0, 0, 0);

    /// <summary>Casilla de selección (lotes por columna).</summary>
    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>Fila de documento seleccionable (listas de Cuadernos).</summary>
public sealed partial class DocumentRow : ObservableObject
{
    /// <summary>Crea la fila.</summary>
    public DocumentRow(Document document)
    {
        Document = document;
    }

    /// <summary>Documento.</summary>
    public Document Document { get; }

    /// <summary>Casilla de selección.</summary>
    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>ViewModel de cuadernos: árbol jerárquico + documentos del seleccionado.</summary>
public sealed partial class NotebooksViewModel : ViewModelBase
{
    private readonly INotebookService _notebooks;
    private readonly IDocumentService _documents;
    private readonly IDialogService _dialogs;
    private readonly INavigationService _navigation;
    private readonly IBatchImportService _batch;
    private readonly IFolderScanner _scanner;
    private readonly IFilePickerService _picker;

    /// <summary>Crea el ViewModel.</summary>
    public NotebooksViewModel(
        IErrorHandler errors,
        INotebookService notebooks,
        IDocumentService documents,
        IDialogService dialogs,
        INavigationService navigation,
        IBatchImportService batch,
        IFolderScanner scanner,
        IFilePickerService picker)
        : base(errors)
    {
        _notebooks = notebooks;
        _documents = documents;
        _dialogs = dialogs;
        _navigation = navigation;
        _batch = batch;
        _scanner = scanner;
        _picker = picker;
    }

    /// <summary>Filas del árbol (aplanado con nivel).</summary>
    public ObservableCollection<NotebookRow> Rows { get; } = [];

    /// <summary>Documentos del cuaderno seleccionado.</summary>
    public ObservableCollection<DocumentRow> SelectedDocuments { get; } = [];

    /// <summary>Documentos sin cuaderno (origen para arrastrar).</summary>
    public ObservableCollection<DocumentRow> UnclassifiedDocuments { get; } = [];

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
            await LoadSelectedDocumentsAsync(ct);
            await LoadUnclassifiedAsync(ct);
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
        if (IsBusy)
        {
            return;
        }

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

            await RefreshAsync(ct);
            SelectedRow = Rows.FirstOrDefault(r => r.Notebook.Id == created.Value.Id);
            await LoadSelectedDocumentsAsync(ct);
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
        if (IsBusy)
        {
            return;
        }

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

            await RefreshAsync(ct);
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
        if (IsBusy)
        {
            return;
        }

        if (SelectedRow is null)
        {
            return;
        }

        try
        {
            var confirmed = await _dialogs.ConfirmAsync(
                "Eliminar cuaderno",
                $"¿Estás seguro de que quieres eliminar «{SelectedRow.Notebook.Name}»? Sus subcuadernos subirán de nivel y sus documentos quedarán sin clasificar.",
                "Eliminar");
            if (!confirmed)
            {
                return;
            }

            confirmed = await _dialogs.ConfirmAsync(
                "Eliminar cuaderno",
                $"«{SelectedRow.Notebook.Name}» se eliminará de forma permanente. Su contenido se conservará.",
                "Eliminar definitivamente");
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
            await RefreshAsync(ct);
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
            new DocumentQuery { NotebookId = SelectedRow.Notebook.Id, Page = 1, PageSize = 100 },
            ct);
        if (docs.IsFailure)
        {
            ShowError(docs.Error);
            return;
        }

        foreach (var doc in docs.Value.Items)
        {
            SelectedDocuments.Add(new DocumentRow(doc));
        }
    }

    private async Task LoadUnclassifiedAsync(CancellationToken ct)
    {
        UnclassifiedDocuments.Clear();
        try
        {
            var docs = await _documents.ListAsync(
                new DocumentQuery { UnclassifiedOnly = true, Page = 1, PageSize = 100 }, ct);
            if (docs.IsFailure)
            {
                ShowError(docs.Error);
                return;
            }

            foreach (var doc in docs.Value.Items)
            {
                UnclassifiedDocuments.Add(new DocumentRow(doc));
            }
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private async Task ReloadDocumentListsAsync(CancellationToken ct)
    {
        await LoadSelectedDocumentsAsync(ct);
        await LoadUnclassifiedAsync(ct);
    }

    /// <summary>
    /// Mueve documentos a un cuaderno (o los deja sin clasificar con <c>null</c>).
    /// Lo usa el arrastrar y soltar interno.
    /// </summary>
    public async Task MoveDocumentsToNotebookAsync(IReadOnlyList<Guid> documentIds, Guid? notebookId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(documentIds);
        if (documentIds.Count == 0)
        {
            return;
        }

        ClearMessages();
        IsBusy = true;
        try
        {
            var moved = 0;
            var failed = 0;
            foreach (var id in documentIds.Distinct())
            {
                ct.ThrowIfCancellationRequested();
                var result = await _documents.MoveToNotebookAsync(id, notebookId, ct);
                if (result.IsFailure)
                {
                    failed++;
                    continue;
                }

                moved++;
            }

            await ReloadDocumentListsAsync(ct);
            if (failed > 0)
            {
                ShowError(Core.Error.Storage("Notebooks.MoveFailed", $"{failed} documento(s) no se pudieron mover."));
            }

            if (moved > 0)
            {
                ShowInfo(notebookId.HasValue
                    ? $"{moved} documento(s) movido(s) al cuaderno."
                    : $"{moved} documento(s) sin clasificar.");
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

    /// <summary>
    /// Importa archivos y carpetas del Explorador en un cuaderno
    /// (o sin clasificar con <c>null</c>).
    /// </summary>
    public async Task ImportDroppedAsync(
        IReadOnlyList<string> filePaths,
        IReadOnlyList<string> folderPaths,
        Guid? notebookId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(filePaths);
        ArgumentNullException.ThrowIfNull(folderPaths);
        ClearMessages();
        IsBusy = true;
        try
        {
            var all = new List<string>(filePaths.Where(p => !string.IsNullOrWhiteSpace(p)));
            foreach (var folder in folderPaths.Where(p => !string.IsNullOrWhiteSpace(p)))
            {
                ct.ThrowIfCancellationRequested();
                FolderScanResult scan;
                try
                {
                    scan = await Task.Run(() => _scanner.Enumerate(folder, recursive: true, ct), ct);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    ShowError(ex);
                    return;
                }

                all.AddRange(scan.Files);
            }

            if (all.Count == 0)
            {
                ShowInfo("Nada que importar: no hay documentos soportados.");
                return;
            }

            var result = await _batch.ImportBatchAsync(all, notebookId, null, null, ct);
            if (result.IsFailure)
            {
                ShowError(result.Error);
                return;
            }

            var batch = result.Value;
            if (batch.RejectedByLimit)
            {
                ShowError(Core.Error.Validation("Import.BatchTooLarge", batch.LimitReason ?? "Lote demasiado grande."));
                return;
            }

            await ReloadDocumentListsAsync(ct);
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

            ShowInfo(parts.Count > 0 ? string.Join(" · ", parts) + "." : "Nada que importar.");
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
            IsBusy = false;
        }
    }

    /// <summary>Marca o desmarca todas las filas del árbol.</summary>
    [RelayCommand]
    private void SelectAllNotebooks()
    {
        var target = !Rows.All(r => r.IsSelected);
        foreach (var row in Rows)
        {
            row.IsSelected = target;
        }
    }

    /// <summary>Marca o desmarca los sin clasificar.</summary>
    [RelayCommand]
    private void SelectAllUnclassified()
    {
        var target = !UnclassifiedDocuments.All(d => d.IsSelected);
        foreach (var doc in UnclassifiedDocuments)
        {
            doc.IsSelected = target;
        }
    }

    /// <summary>Marca o desmarca los documentos del cuaderno.</summary>
    [RelayCommand]
    private void SelectAllSelected()
    {
        var target = !SelectedDocuments.All(d => d.IsSelected);
        foreach (var doc in SelectedDocuments)
        {
            doc.IsSelected = target;
        }
    }

    /// <summary>Importa archivos a Sin clasificar.</summary>
    [RelayCommand]
    private async Task AddUnclassifiedFilesAsync(CancellationToken ct)
    {
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

        await ImportPickedAsync(picked, null, ct);
    }

    /// <summary>Importa archivos al cuaderno seleccionado.</summary>
    [RelayCommand]
    private async Task AddToSelectedFilesAsync(CancellationToken ct)
    {
        if (SelectedRow is null)
        {
            ShowInfo("Elige primero un cuaderno.");
            return;
        }

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

        await ImportPickedAsync(picked, SelectedRow.Notebook.Id, ct);
    }

    /// <summary>Mueve los sin clasificar marcados al cuaderno seleccionado.</summary>
    [RelayCommand]
    private async Task MoveCheckedToSelectedAsync(CancellationToken ct)
    {
        if (SelectedRow is null)
        {
            ShowInfo("Elige primero un cuaderno.");
            return;
        }

        var ids = UnclassifiedDocuments.Where(d => d.IsSelected).Select(d => d.Document.Id).ToList();
        if (ids.Count == 0)
        {
            ShowInfo("Marca primero archivos en Sin clasificar.");
            return;
        }

        await MoveDocumentsToNotebookAsync(ids, SelectedRow.Notebook.Id, ct);
    }

    /// <summary>Manda a la papelera los sin clasificar marcados (una confirmación).</summary>
    [RelayCommand]
    private async Task DeleteCheckedUnclassifiedAsync(CancellationToken ct)
    {
        var checkedDocs = UnclassifiedDocuments.Where(d => d.IsSelected).ToList();
        if (checkedDocs.Count == 0)
        {
            ShowInfo("Marca primero archivos en Sin clasificar.");
            return;
        }

        await DeleteDocumentsAsync(checkedDocs, ct);
    }

    /// <summary>Manda a la papelera los documentos marcados del cuaderno (una confirmación).</summary>
    [RelayCommand]
    private async Task DeleteCheckedSelectedAsync(CancellationToken ct)
    {
        var checkedDocs = SelectedDocuments.Where(d => d.IsSelected).ToList();
        if (checkedDocs.Count == 0)
        {
            ShowInfo("Marca primero archivos del cuaderno.");
            return;
        }

        await DeleteDocumentsAsync(checkedDocs, ct);
    }

    private async Task DeleteDocumentsAsync(IReadOnlyList<DocumentRow> checkedDocs, CancellationToken ct)
    {
        ClearMessages();
        IsBusy = true;
        try
        {
            var confirmed = await _dialogs.ConfirmAsync(
                "Mover a la papelera",
                checkedDocs.Count == 1
                    ? $"¿Estás seguro de que quieres eliminar «{checkedDocs[0].Document.Name}»? Quedará en la papelera."
                    : $"¿Estás seguro de que quieres eliminar {checkedDocs.Count} archivos? Quedarán en la papelera.",
                "Mover a la papelera");
            if (!confirmed)
            {
                return;
            }

            var failed = 0;
            foreach (var row in checkedDocs)
            {
                ct.ThrowIfCancellationRequested();
                if ((await _documents.MoveToTrashAsync(row.Document.Id, ct)).IsFailure)
                {
                    failed++;
                }
            }

            await ReloadDocumentListsAsync(ct);
            ShowInfo(failed == 0
                ? $"{checkedDocs.Count} archivo(s) en la papelera."
                : $"{checkedDocs.Count - failed} en la papelera, {failed} con error.");
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

    /// <summary>Elimina los cuadernos marcados, de dentro hacia fuera (doble confirmación).</summary>
    [RelayCommand]
    private async Task DeleteCheckedNotebooksAsync(CancellationToken ct)
    {
        var checkedRows = Rows.Where(r => r.IsSelected).ToList();
        if (checkedRows.Count == 0)
        {
            ShowInfo("Marca primero cuadernos en el árbol.");
            return;
        }

        try
        {
            var confirmed = await _dialogs.ConfirmAsync(
                "Eliminar cuadernos",
                checkedRows.Count == 1
                    ? $"¿Estás seguro de que quieres eliminar «{checkedRows[0].Notebook.Name}»?"
                    : $"¿Estás seguro de que quieres eliminar {checkedRows.Count} cuadernos?",
                "Eliminar");
            if (!confirmed)
            {
                return;
            }

            confirmed = await _dialogs.ConfirmAsync(
                "Eliminar cuadernos",
                checkedRows.Count == 1
                    ? $"«{checkedRows[0].Notebook.Name}» se eliminará de forma permanente. Su contenido se conservará."
                    : $"{checkedRows.Count} cuadernos se eliminarán de forma permanente. Su contenido se conservará.",
                "Eliminar definitivamente");
            if (!confirmed)
            {
                return;
            }

            IsBusy = true;
            var failed = 0;
            // De dentro hacia fuera: los hijos antes que sus padres.
            foreach (var row in checkedRows.OrderByDescending(r => r.Level))
            {
                ct.ThrowIfCancellationRequested();
                if ((await _notebooks.DeleteAsync(row.Notebook.Id, ct)).IsFailure)
                {
                    failed++;
                }
            }

            SelectedRow = null;
            await RefreshAsync(ct);
            if (failed > 0)
            {
                ShowError(Core.Error.Storage("Notebooks.DeleteFailed", $"{failed} cuaderno(s) no se pudieron eliminar."));
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

    private async Task ImportPickedAsync(IReadOnlyList<string> picked, Guid? notebookId, CancellationToken ct)
    {
        ClearMessages();
        IsBusy = true;
        try
        {
            var result = await _batch.ImportBatchAsync(picked, notebookId, null, null, ct);
            if (result.IsFailure)
            {
                ShowError(result.Error);
                return;
            }

            var batch = result.Value;
            if (batch.RejectedByLimit)
            {
                ShowError(Core.Error.Validation("Import.BatchTooLarge", batch.LimitReason ?? "Lote demasiado grande."));
                return;
            }

            await ReloadDocumentListsAsync(ct);
            ShowInfo(batch.Imported > 0
                ? $"{batch.Imported} importado(s)."
                : "Nada que importar.");
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
