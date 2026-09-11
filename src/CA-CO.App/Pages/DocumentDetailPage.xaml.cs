using CaCo.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CaCo.App.Pages;

/// <summary>Página de detalle de documento.</summary>
public sealed partial class DocumentDetailPage : DocumentDetailPageBase
{
    /// <summary>Crea la página.</summary>
    public DocumentDetailPage()
    {
        InitializeComponent();
    }

    private void TagRemove_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is TagItem tag)
        {
            ViewModel.RemoveTagCommand.Execute(tag);
        }
    }
}
