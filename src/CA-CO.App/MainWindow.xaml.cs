using CaCo.App.Services;
using CaCo.App.ViewModels;
using CaCo.Application.Configuration;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.UI;

namespace CaCo.App;

/// <summary>
/// Ventana principal (shell): TitleBar + NavigationView + Frame.
/// Menú estático por Tag (el camino más robusto de WinUI); sin lógica de negocio.
/// </summary>
public sealed partial class MainWindow : Window
{
    private static readonly Dictionary<string, Type> MenuMap = new()
    {
        ["home"] = typeof(HomeViewModel),
        ["documents"] = typeof(DocumentsViewModel),
        ["notebooks"] = typeof(NotebooksViewModel),
        ["notes"] = typeof(NotesViewModel),
        ["favorites"] = typeof(FavoritesViewModel),
        ["recents"] = typeof(RecentsViewModel),
        ["trash"] = typeof(TrashViewModel),
    };

    private readonly INavigationService _navigation;
    private bool _navigating;
    private NavigationRequest? _lastRequest;

    /// <summary>Crea la ventana resolviendo dependencias del contenedor.</summary>
    public MainWindow(
        INavigationService navigation,
        IDialogService dialogs,
        IFilePickerService picker,
        IFolderPickerService folderPicker,
        CacoSettings settings)
    {
        _navigation = navigation;
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        try
        {
            // Ruta absoluta: la relativa depende del WorkingDirectory del acceso directo.
            AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        }
        catch
        {
            // Sin icono de ventana: se sigue con el icono incrustado del exe.
        }
        ApplyCaptionColors();
        ApplyTheme(settings.General.Theme);

        dialogs.Initialize(this);
        picker.Initialize(this);
        folderPicker.Initialize(this);

        _navigation.NavigationRequested += OnNavigationRequested;
        Closed += (_, _) => _navigation.NavigationRequested -= OnNavigationRequested;

        _navigation.NavigateTo<HomeViewModel>();
    }

    /// <summary>Aplica el tema a la ventana (Claro / Oscuro / Sistema).</summary>
    public void ApplyTheme(string theme)
    {
        if (Content is FrameworkElement root)
        {
            root.RequestedTheme = theme switch
            {
                "Dark" => ElementTheme.Dark,
                "Light" => ElementTheme.Light,
                _ => ElementTheme.Default,
            };
        }
    }

    /// <summary>
    /// Colores explícitos de los botones de ventana (visibles sobre el beige
    /// en cualquier tema del sistema).
    /// </summary>
    private void ApplyCaptionColors()
    {
        var titleBar = AppWindow.TitleBar;
        titleBar.ButtonBackgroundColor = Color.FromArgb(0, 0, 0, 0);
        titleBar.ButtonInactiveBackgroundColor = Color.FromArgb(0, 0, 0, 0);
        titleBar.ButtonForegroundColor = Color.FromArgb(255, 74, 44, 18);
        titleBar.ButtonHoverBackgroundColor = Color.FromArgb(255, 138, 90, 43);
        titleBar.ButtonHoverForegroundColor = Color.FromArgb(255, 255, 255, 255);
        titleBar.ButtonPressedBackgroundColor = Color.FromArgb(255, 94, 58, 23);
        titleBar.ButtonPressedForegroundColor = Color.FromArgb(255, 255, 255, 255);
        titleBar.ButtonInactiveForegroundColor = Color.FromArgb(255, 168, 152, 128);
    }

    private void OnNavigationRequested(object? sender, NavigationRequest request)
    {
        // Evita recrear la página si ya estamos en ella (clic en la pestaña activa).
        // Con parámetro (documento concreto) siempre se navega para permitir recarga.
        if (request.Parameter is null
            && _lastRequest is not null
            && _lastRequest.PageType == request.PageType
            && Equals(_lastRequest.Parameter, request.Parameter))
        {
            SelectMenu(request.ViewModelType);
            return;
        }

        if (_navigating)
        {
            return;
        }

        _navigating = true;
        try
        {
            var navigated = NavFrame.Navigate(request.PageType, request.Parameter);
            if (!navigated)
            {
                return;
            }

            _lastRequest = request;
            // Conservar historial en Reader/Detail para no perder origen;
            // en secciones raíz se limpia para que el TitleBar no se mueva.
            var isDetail = request.ViewModelType == typeof(ReaderViewModel)
                || request.ViewModelType == typeof(DocumentDetailViewModel);
            if (!isDetail)
            {
                NavFrame.BackStack.Clear();
            }

            SelectMenu(request.ViewModelType);
        }
        finally
        {
            _navigating = false;
        }
    }

    private void SelectMenu(Type viewModelType)
    {
        if (viewModelType == typeof(SettingsViewModel))
        {
            NavView.SelectedItem = NavView.SettingsItem;
            return;
        }

        foreach (var menuItem in NavView.MenuItems.OfType<NavigationViewItem>())
        {
            if (menuItem.Tag is string tag
                && MenuMap.TryGetValue(tag, out var vm)
                && vm == viewModelType)
            {
                NavView.SelectedItem = menuItem;
                return;
            }
        }
    }

    private void TitleBar_PaneToggleRequested(TitleBar sender, object args)
    {
        NavView.IsPaneOpen = !NavView.IsPaneOpen;
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_navigating)
        {
            return;
        }

        if (args.IsSettingsSelected)
        {
            _navigation.NavigateTo<SettingsViewModel>();
            return;
        }

        if (args.SelectedItem is NavigationViewItem item
            && item.Tag is string tag
            && MenuMap.TryGetValue(tag, out var viewModelType))
        {
            _navigation.NavigateTo(viewModelType);
        }
    }
}
