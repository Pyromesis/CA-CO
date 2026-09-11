using Microsoft.UI.Xaml.Controls;

namespace CaCo.App.Pages;

/// <summary>Página de papelera.</summary>
public sealed partial class TrashPage : TrashPageBase
{
    /// <summary>Crea la página.</summary>
    public TrashPage()
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
