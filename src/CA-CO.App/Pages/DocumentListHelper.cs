using CaCo.App.ViewModels;
using CaCo.App.Views;
using CaCo.Domain;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

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

    /// <summary>Asigna el documento a una fila <see cref="RecentDocumentView"/>
    /// (directa o envuelta con casilla).</summary>
    public static void AttachRecent(ContainerContentChangingEventArgs args)
    {
        var root = args.ItemContainer.ContentTemplateRoot;
        var view = root as RecentDocumentView ?? FindChild<RecentDocumentView>(root as DependencyObject);
        if (view is null)
        {
            return;
        }

        if (args.InRecycleQueue)
        {
            view.Document = null;
            return;
        }

        view.Document = args.Item as Document ?? (args.Item as DocumentRow)?.Document;
    }

    private static T? FindChild<T>(DependencyObject? root)
        where T : DependencyObject
    {
        if (root is null)
        {
            return null;
        }

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed)
            {
                return typed;
            }

            var nested = FindChild<T>(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }
}
