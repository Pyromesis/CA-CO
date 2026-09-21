using CaCo.Domain;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace CaCo.App.Pages;

/// <summary>
/// Biblioteca de documentos con búsqueda, importación y arrastrar/soltar.
/// Solo enlaza UI con el ViewModel.
/// </summary>
public sealed partial class DocumentsPage : DocumentsPageBase
{
    /// <summary>Crea la página.</summary>
    public DocumentsPage()
    {
        InitializeComponent();
    }

    private void List_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args) =>
        DocumentListHelper.AttachDocument(ViewModel, args);

    private void List_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is CaCo.Domain.Document document)
        {
            ViewModel.OpenDetailsCommand.Execute(document);
        }
    }

    /// <summary>Buscar al pulsar Enter (más rápido que ir al botón).</summary>
    private void SearchBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter && ViewModel.SearchCommand.CanExecute(null))
        {
            ViewModel.SearchCommand.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>Quita todos los filtros y vuelve a la biblioteca completa.</summary>
    private void ClearSearch_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SearchText = string.Empty;
        ViewModel.SearchTypeIndex = 0;
        ViewModel.SearchFavoritesOnly = false;
        if (ViewModel.SearchCommand.CanExecute(null))
        {
            ViewModel.SearchCommand.Execute(null);
        }
    }

    private void DropArea_DragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = e.DataView.Contains(StandardDataFormats.StorageItems)
            ? DataPackageOperation.Copy
            : DataPackageOperation.None;
        e.Handled = true;
    }

    private async void DropArea_Drop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            var paths = items.OfType<StorageFile>().Select(f => f.Path).Where(p => SupportedFileTypes.IsSupported(p)).ToList();
            if (paths.Count > 0)
            {
                await ViewModel.ImportPathsAsync(paths, CancellationToken.None);
            }
            else
            {
                await new ContentDialog
                {
                    Title = "Nada que importar",
                    Content = "CA-CO trabaja con PDF, PNG, JPG, JPEG, DOCX, XLSX y TXT.",
                    CloseButtonText = "Aceptar",
                    XamlRoot = XamlRoot,
                }.ShowAsync();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Drop falló: {ex.Message}");
            // El ViewModel informa de los errores de importación vía InfoBar.
        }

        e.Handled = true;
    }
}
