using Microsoft.UI.Xaml.Controls;

namespace CaCo.App.Pages;

/// <summary>Página de inicio.</summary>
public sealed partial class HomePage : HomePageBase
{
    /// <summary>Crea la página.</summary>
    public HomePage()
    {
        InitializeComponent();
    }

    private void List_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args) =>
        DocumentListHelper.AttachRecent(args);

    private void List_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is CaCo.Domain.Document document)
        {
            ViewModel.OpenDetailsCommand.Execute(document);
        }
    }
}
