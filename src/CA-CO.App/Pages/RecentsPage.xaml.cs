using Microsoft.UI.Xaml.Controls;

namespace CaCo.App.Pages;

/// <summary>Página de recientes.</summary>
public sealed partial class RecentsPage : RecentsPageBase
{
    /// <summary>Crea la página.</summary>
    public RecentsPage()
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
}
