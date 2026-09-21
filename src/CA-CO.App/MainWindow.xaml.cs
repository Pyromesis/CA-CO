using CaCo.App.Models;
using CaCo.App.Pages;
using CaCo.App.Services;
using CaCo.App.ViewModels;
using CaCo.Application.Configuration;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
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
    private readonly CaCo.Application.Future.Security.IAuthenticationService _auth;
    private bool _navigating;
    private NavigationRequest? _lastRequest;

    /// <summary>Crea la ventana resolviendo dependencias del contenedor.</summary>
    public MainWindow(
        INavigationService navigation,
        IDialogService dialogs,
        IFilePickerService picker,
        IFolderPickerService folderPicker,
        CacoSettings settings,
        CaCo.Application.Future.Security.IAuthenticationService auth)
    {
        _navigation = navigation;
        _auth = auth;
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
        ApplyTheme(settings.General.Theme);

        // Tamaño inicial cómodo para una app de escritorio (luego el usuario ajusta).
        try
        {
            AppWindow.Resize(new Windows.Graphics.SizeInt32 { Width = 1120, Height = 760 });
        }
        catch
        {
            // Algunos modos (tablet, contenedor) no permiten redimensionar.
        }

        dialogs.Initialize(this);
        picker.Initialize(this);
        folderPicker.Initialize(this);

        _navigation.NavigationRequested += OnNavigationRequested;
        Closed += (_, _) => _navigation.NavigationRequested -= OnNavigationRequested;

        _navigation.NavigateTo<HomeViewModel>();
        RegisterPaletteCommands();
        Activated += OnFirstActivated;
    }

    /// <summary>Registra los comandos de la paleta (Ctrl+K).</summary>
    private void RegisterPaletteCommands()
    {
        Palette.RegisterCommands(new[]
        {
            new PaletteCommand("Ir a Inicio", "Resumen de tu biblioteca", "\uE80F", "", () => _navigation.NavigateTo<HomeViewModel>()),
            new PaletteCommand("Ir a Documentos", "Biblioteca completa y búsqueda", "\uE8A5", "Ctrl+D", () => _navigation.NavigateTo<DocumentsViewModel>()),
            new PaletteCommand("Ir a Cuadernos", "Organiza en carpetas", "\uE8B7", "", () => _navigation.NavigateTo<NotebooksViewModel>()),
            new PaletteCommand("Ir a Notas", "Notas sueltas con markdown", "\uE77B", "", () => _navigation.NavigateTo<NotesViewModel>()),
            new PaletteCommand("Ir a Favoritos", "Documentos marcados", "\uE734", "", () => _navigation.NavigateTo<FavoritesViewModel>()),
            new PaletteCommand("Ir a Recientes", "Últimos importados", "\uE81C", "", () => _navigation.NavigateTo<RecentsViewModel>()),
            new PaletteCommand("Ir a la Papelera", "Restaurar o vaciar", "\uE74D", "", () => _navigation.NavigateTo<TrashViewModel>()),
            new PaletteCommand("Ir a Configuración", "Tema, almacenamiento y seguridad", "\uE771", "", () => _navigation.NavigateTo<SettingsViewModel>()),
            new PaletteCommand("Importar documento", "Elige archivos con el selector", "\uE710", "Ctrl+I", () =>
                InvokeOnPage<DocumentsViewModel>(vm => vm.ImportCommand.Execute(null))),
            new PaletteCommand("Importar carpeta", "Importa una carpeta entera (recursivo)", "\uE8B7", "", () =>
                InvokeOnPage<DocumentsViewModel>(vm => vm.ImportFolderCommand.Execute(null))),
            new PaletteCommand("Nueva nota", "Crea una nota suelta", "\uE77B", "Ctrl+N", () =>
                InvokeOnPage<NotesViewModel>(vm => vm.CreateCommand.Execute(null))),
            new PaletteCommand("Crear cuaderno", "Pide el nombre y lo crea", "\uE710", "", () =>
                InvokeOnPage<NotebooksViewModel>(vm => vm.CreateCommand.Execute(null))),
            new PaletteCommand("Tema oscuro", "Cambie a modo oscuro", "\uE708", "", () => App.ApplyTheme("Dark")),
            new PaletteCommand("Tema claro", "Cambia a modo claro", "\uE709", "", () => App.ApplyTheme("Light")),
        });
    }

    /// <summary>Navega a la página de <typeparamref name="TViewModel" /> y ejecuta una acción sobre SU instancia.</summary>
    private void InvokeOnPage<TViewModel>(Action<TViewModel> action)
        where TViewModel : ViewModelBase
    {
        _navigation.NavigateTo<TViewModel>();
        if (NavFrame.Content is IPageViewModelHolder holder && holder.ViewModel is TViewModel viewModel)
        {
            action(viewModel);
        }
    }

    /// <summary>Abre la paleta de comandos con Ctrl+K.</summary>
    private void CommandPalette_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (!Palette.IsOpen)
        {
            Palette.Open();
        }

        args.Handled = true;
    }

    /// <summary>Importar documentos con Ctrl+I.</summary>
    private void Import_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        InvokeOnPage<DocumentsViewModel>(vm => vm.ImportCommand.Execute(null));
        args.Handled = true;
    }

    /// <summary>Nueva nota con Ctrl+N.</summary>
    private void NewNote_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        InvokeOnPage<NotesViewModel>(vm => vm.CreateCommand.Execute(null));
        args.Handled = true;
    }

    /// <summary>Ir a Documentos con Ctrl+D.</summary>
    private void Documents_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        _navigation.NavigateTo<DocumentsViewModel>();
        args.Handled = true;
    }

    /// <summary>Bloqueo con PIN/Hello al arrancar (Fase 8): sin desbloqueo no hay app.</summary>
    private async void OnFirstActivated(object sender, WindowActivatedEventArgs args)
    {
        Activated -= OnFirstActivated;

        // Aquí el tema real del sistema ya está resuelto: repintar la barra de título.
        ApplyCaptionColors();

        if (!_auth.IsLockConfigured)
        {
            return;
        }

        try
        {
            var result = await _auth.AuthenticateAsync(CancellationToken.None);
            if (result.IsFailure || !result.Value)
            {
                Close();
            }
        }
        catch
        {
            Close();
        }
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

        // Los botones de la barra de título deben contrastar con el tema activo.
        ApplyCaptionColors();
    }

    /// <summary>
    /// Colores explícitos de los botones de ventana, adaptados al tema
    /// (el marrón del tema claro sería invisible sobre el Mica oscuro).
    /// </summary>
    private void ApplyCaptionColors()
    {
        var dark = Content is FrameworkElement root && root.ActualTheme == ElementTheme.Dark;
        var titleBar = AppWindow.TitleBar;
        titleBar.ButtonBackgroundColor = Color.FromArgb(0, 0, 0, 0);
        titleBar.ButtonInactiveBackgroundColor = Color.FromArgb(0, 0, 0, 0);
        if (dark)
        {
            titleBar.ButtonForegroundColor = Color.FromArgb(255, 245, 235, 221);
            titleBar.ButtonHoverBackgroundColor = Color.FromArgb(255, 74, 44, 18);
            titleBar.ButtonHoverForegroundColor = Color.FromArgb(255, 255, 255, 255);
            titleBar.ButtonPressedBackgroundColor = Color.FromArgb(255, 94, 58, 23);
            titleBar.ButtonPressedForegroundColor = Color.FromArgb(255, 255, 255, 255);
            titleBar.ButtonInactiveForegroundColor = Color.FromArgb(255, 148, 132, 108);
            return;
        }

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
