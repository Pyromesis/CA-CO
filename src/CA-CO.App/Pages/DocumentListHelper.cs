using CaCo.App.ViewModels;
using CaCo.App.Views;
using CaCo.Domain;
using Microsoft.UI.Xaml.Controls;

namespace CaCo.App.Pages;

/// <summary>
/// Enlaza las filas con sus datos dentro de <c>DataTemplate</c>, donde <c>x:Bind</c>
/// solo alcanza al elemento y las entidades de dominio no notifican cambios.
/// Se invoca desde <c>ContainerContentChanging</c>, que cubre creación y reciclaje.
/// </summary>
internal static class DocumentListHelper
{
    /// <summary>Asigna ViewModel y documento a una fila <see cref="DocumentItemView"/>.</summary>
    public static void AttachDocument(DocumentListViewModel? viewModel, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue)
        {
            if (args.ItemContainer.ContentTemplateRoot is DocumentItemView recycled)
            {
                recycled.Document = null;
            }

            return;
        }

        if (args.ItemContainer.ContentTemplateRoot is DocumentItemView view)
        {
            view.ViewModel = viewModel;
            view.Document = args.Item as Document;
        }
    }

    /// <summary>Asigna el documento a una fila <see cref="RecentDocumentView"/>.</summary>
    public static void AttachRecent(ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue)
        {
            if (args.ItemContainer.ContentTemplateRoot is RecentDocumentView recycled)
            {
                recycled.Document = null;
            }

            return;
        }

        if (args.ItemContainer.ContentTemplateRoot is RecentDocumentView view)
        {
            view.Document = args.Item as Document;
        }
    }
}
