using CaCo.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace CaCo.App.Pages;

/// <summary>
/// Base de las páginas: resuelve su ViewModel del contenedor DI y lo notifica
/// al navegar. Elimina el boilerplate de las 7 páginas.
/// </summary>
/// <typeparam name="TViewModel">ViewModel de la página.</typeparam>
public abstract class PageBase<TViewModel> : Page
    where TViewModel : ViewModelBase
{
    /// <summary>ViewModel resuelto por DI.</summary>
    protected TViewModel ViewModel { get; }

    /// <summary>Crea la página.</summary>
    protected PageBase()
    {
        ViewModel = App.Services.GetRequiredService<TViewModel>();
    }

    private CancellationTokenSource? _navCts;

    /// <inheritdoc/>
    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _navCts?.Cancel();
        _navCts?.Dispose();
        _navCts = new CancellationTokenSource();
        try
        {
            ViewModel.ReceiveParameter(e.Parameter);
            await ViewModel.OnNavigatedToAsync(_navCts.Token);
        }
        catch (OperationCanceledException)
        {
            // Navegación rápida: carga anterior cancelada, sin ruido.
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"OnNavigatedTo falló: {ex.Message}");
            // El ViewModel ya muestra sus errores vía InfoBar.
        }
    }

    /// <inheritdoc/>
    protected override async void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        try
        {
            _navCts?.Cancel();
        }
        catch
        {
        }

        try
        {
            await ViewModel.OnNavigatedFromAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"OnNavigatedFrom falló: {ex.Message}");
        }
    }
}

/// <summary>Bases no genéricas para usar como raíz en XAML (WinUI no admite genéricos en XAML).</summary>
public abstract class HomePageBase : PageBase<HomeViewModel>
{
}

/// <summary>Base de la página de documentos.</summary>
public abstract class DocumentsPageBase : PageBase<DocumentsViewModel>
{
}

/// <summary>Base de la página de detalle de documento.</summary>
public abstract class DocumentDetailPageBase : PageBase<DocumentDetailViewModel>
{
}

/// <summary>Base del espacio de trabajo (lector).</summary>
public abstract class ReaderPageBase : PageBase<ReaderViewModel>
{
}

/// <summary>Base de la página de cuadernos.</summary>
public abstract class NotebooksPageBase : PageBase<NotebooksViewModel>
{
}

/// <summary>Base de la página de favoritos.</summary>
public abstract class FavoritesPageBase : PageBase<FavoritesViewModel>
{
}

/// <summary>Base de la página de recientes.</summary>
public abstract class RecentsPageBase : PageBase<RecentsViewModel>
{
}

/// <summary>Base de la página de papelera.</summary>
public abstract class TrashPageBase : PageBase<TrashViewModel>
{
}

/// <summary>Base de la página de configuración.</summary>
public abstract class SettingsPageBase : PageBase<SettingsViewModel>
{
}
