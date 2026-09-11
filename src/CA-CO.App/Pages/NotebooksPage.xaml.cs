using CaCo.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

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
