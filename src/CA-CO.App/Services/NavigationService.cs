using CaCo.App.ViewModels;

namespace CaCo.App.Services;

/// <summary>Solicitud de navegación ViewModel → Página.</summary>
/// <param name="ViewModelType">ViewModel destino.</param>
/// <param name="PageType">Página WinUI a mostrar.</param>
/// <param name="Parameter">Parámetro opcional (p. ej. id del documento).</param>
public sealed record NavigationRequest(Type ViewModelType, Type PageType, object? Parameter = null);

/// <summary>
/// Navegación ViewModel-first: los ViewModels piden navegar por tipo de ViewModel
/// sin conocer <c>Frame</c> ni páginas. <c>MainWindow</c> escucha
/// <see cref="NavigationRequested"/> y mueve el <c>Frame</c>.
/// Añadir secciones futuras = registrar un par más, sin tocar el shell.
/// </summary>
public interface INavigationService
{
    /// <summary>Se eleva cuando hay que mostrar una página.</summary>
    event EventHandler<NavigationRequest>? NavigationRequested;

    /// <summary>Navega al ViewModel indicado.</summary>
    void NavigateTo<TViewModel>() where TViewModel : ViewModelBase;

    /// <summary>Navega al ViewModel indicado con un parámetro.</summary>
    void NavigateTo<TViewModel>(object? parameter) where TViewModel : ViewModelBase;

    /// <summary>Navega al ViewModel indicado (por tipo).</summary>
    void NavigateTo(Type viewModelType);

    /// <summary>Navega al ViewModel indicado (por tipo) con un parámetro.</summary>
    void NavigateTo(Type viewModelType, object? parameter);

    /// <summary>Resuelve la página registrada para un ViewModel.</summary>
    Type GetPageType(Type viewModelType);
}

/// <summary>Implementación de <see cref="INavigationService"/> con mapa VM → Page.</summary>
public sealed class NavigationService : INavigationService
{
    private readonly Dictionary<Type, Type> _map = [];

    /// <inheritdoc/>
    public event EventHandler<NavigationRequest>? NavigationRequested;

    /// <summary>Registra el par ViewModel → Página. Se llama una vez al arrancar.</summary>
    public NavigationService Register<TViewModel, TPage>()
        where TViewModel : ViewModelBase
        where TPage : Microsoft.UI.Xaml.Controls.Page
    {
        _map[typeof(TViewModel)] = typeof(TPage);
        return this;
    }

    /// <inheritdoc/>
    public void NavigateTo<TViewModel>()
        where TViewModel : ViewModelBase => NavigateTo(typeof(TViewModel), null);

    /// <inheritdoc/>
    public void NavigateTo<TViewModel>(object? parameter)
        where TViewModel : ViewModelBase => NavigateTo(typeof(TViewModel), parameter);

    /// <inheritdoc/>
    public void NavigateTo(Type viewModelType) => NavigateTo(viewModelType, null);

    /// <inheritdoc/>
    public void NavigateTo(Type viewModelType, object? parameter)
    {
        ArgumentNullException.ThrowIfNull(viewModelType);
        NavigationRequested?.Invoke(this, new NavigationRequest(viewModelType, GetPageType(viewModelType), parameter));
    }

    /// <inheritdoc/>
    public Type GetPageType(Type viewModelType)
    {
        ArgumentNullException.ThrowIfNull(viewModelType);
        if (!_map.TryGetValue(viewModelType, out var pageType))
        {
            throw new InvalidOperationException($"Sin página registrada para {viewModelType.Name}.");
        }

        return pageType;
    }
}
