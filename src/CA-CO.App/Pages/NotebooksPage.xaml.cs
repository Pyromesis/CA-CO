using CaCo.App.ViewModels;
using CaCo.Domain;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace CaCo.App.Pages;

/// <summary>Página de cuadernos: árbol + documentos del seleccionado.</summary>
public sealed partial class NotebooksPage : NotebooksPageBase
{
    /// <summary>Crea la página.</summary>
    public NotebooksPage()
    {
        InitializeComponent();
    }

    private void List_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.FirstOrDefault() is NotebookRow row)
        {
            ViewModel.SelectCommand.Execute(row);
        }
    }

    /// <summary>Prefijo del arrastre interno de documentos (ids separados por ';').</summary>
    private const string DragPrefix = "CA-CO-DOCS:";

    private void Unclassified_DragStarting(object sender, DragItemsStartingEventArgs args)
    {
        if (sender is not ListView list)
        {
            args.Cancel = true;
            return;
        }

        var ids = list.SelectedItems.OfType<Document>().Select(d => d.Id.ToString("D")).ToList();
        if (ids.Count == 0)
        {
            args.Cancel = true;
            return;
        }

        args.Data.SetText(DragPrefix + string.Join(";", ids));
        args.Data.RequestedOperation = DataPackageOperation.Move;
    }

    private void Any_DragOver(object sender, DragEventArgs e)
    {
        try
        {
            if (e.DataView.Contains(StandardDataFormats.Text)
                || e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                e.AcceptedOperation = e.DataView.Contains(StandardDataFormats.StorageItems)
                    ? DataPackageOperation.Copy
                    : DataPackageOperation.Move;
                e.DragUIOverride.Caption = "Mover al cuaderno";
                e.DragUIOverride.IsContentVisible = true;
                e.Handled = true;
            }
            else
            {
                e.AcceptedOperation = DataPackageOperation.None;
            }
        }
        catch
        {
            e.AcceptedOperation = DataPackageOperation.None;
        }
    }

    private async void Notebook_Drop(object sender, DragEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not NotebookRow row)
        {
            return;
        }

        await DropIntoNotebookAsync(e, row.Notebook.Id);
    }

    private async void SelectedDocs_Drop(object sender, DragEventArgs e)
    {
        await DropIntoNotebookAsync(e, ViewModel.SelectedRow?.Notebook.Id);
    }

    private async void Unclassified_Drop(object sender, DragEventArgs e)
    {
        await DropIntoNotebookAsync(e, null);
    }

    private async Task DropIntoNotebookAsync(DragEventArgs e, Guid? notebookId)
    {
        e.Handled = true;
        try
        {
            if (e.DataView.Contains(StandardDataFormats.Text))
            {
                var text = await e.DataView.GetTextAsync();
                if (text.StartsWith(DragPrefix, StringComparison.Ordinal))
                {
                    var ids = text[DragPrefix.Length..]
                        .Split(';', StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => Guid.TryParse(s, out var id) ? id : Guid.Empty)
                        .Where(id => id != Guid.Empty)
                        .ToList();
                    await ViewModel.MoveDocumentsToNotebookAsync(ids, notebookId, CancellationToken.None);
                    return;
                }
            }

            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                var items = await e.DataView.GetStorageItemsAsync();
                var files = items.OfType<StorageFile>().Select(f => f.Path).ToList();
                var folders = items.OfType<StorageFolder>().Select(f => f.Path).ToList();
                if (files.Count > 0 || folders.Count > 0)
                {
                    await ViewModel.ImportDroppedAsync(files, folders, notebookId, CancellationToken.None);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Drop en cuaderno falló: {ex.Message}");
        }
    }

    private void Docs_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args) =>
        DocumentListHelper.AttachRecent(args);

    private void Docs_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is CaCo.Domain.Document document)
        {
            ViewModel.OpenDocumentCommand.Execute(document);
        }
    }
}
