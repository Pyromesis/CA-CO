using CaCo.App.Pages;
using CaCo.App.Services;
using CaCo.App.ViewModels;
using CaCo.Application.Configuration;
using CaCo.Infrastructure.Configuration;
using CaCo.Infrastructure.DependencyInjection;
using CaCo.Infrastructure.Persistence.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;

namespace CaCo.App;

/// <summary>
/// Punto de entrada de CA-CO. Construye el contenedor de dependencias una sola vez
/// (Composition Root de la UI), inicializa la biblioteca local y muestra la ventana.
/// </summary>
public partial class App : Microsoft.UI.Xaml.Application
{
    private Window? _window;
    private ILogger<App>? _logger;

    /// <summary>Proveedor de servicios raíz (resolución de ViewModels desde las páginas).</summary>
    public static IServiceProvider Services { get; private set; } = null!;

    /// <summary>Aplica el tema a la ventana principal (llamado al guardar ajustes).</summary>
    public static void ApplyTheme(string theme)
    {
        if (Current is App app && app._window is MainWindow window)
        {
            window.ApplyTheme(theme);
        }
    }

    /// <summary>Inicializa la aplicación.</summary>
    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    /// <summary>Arranque: configura DI, garantiza la biblioteca y abre la ventana principal.</summary>
    protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        // 1. Ajustes (antes de DI: determinan la raíz de la biblioteca).
        var configuration = new FileAppConfiguration();
        CacoSettings settings;
        try
        {
            settings = await configuration.LoadAsync(CancellationToken.None);
        }
        catch
        {
            settings = new CacoSettings();
        }

        // 2. Raíz efectiva: carpeta brokered (token) → configurada si escribible → defecto.
        var libraryRoot = await ResolveLibraryRootAsync(settings);

        // 3. Contenedor de dependencias.
        var services = new ServiceCollection();
        services.AddCacoLogging();
        services.AddCaco(libraryRoot, o => o.Settings = settings);
        services.AddCacoUi();
        Services = services.BuildServiceProvider();

        _logger = Services.GetRequiredService<ILogger<App>>();
        _logger.LogInformation("CA-CO iniciando. Biblioteca: {LibraryRoot}", libraryRoot);

        // 4. Primera ejecución: crear estructura si no existe (offline, sin configuración).
        try
        {
            var legacy = Services.GetRequiredService<LegacyVfsMigrator>();
            await legacy.MigrateIfNeededAsync(AppPaths.DefaultLibraryRoot, libraryRoot, CancellationToken.None);

            var library = Services.GetRequiredService<Application.Services.ILibraryService>();
            var initialized = await library.EnsureInitializedAsync(CancellationToken.None);
            if (initialized.IsSuccess && initialized.Value)
            {
                _logger.LogInformation("Biblioteca creada por primera vez.");
            }
            else if (initialized.IsFailure)
            {
                _logger.LogError("No se pudo inicializar la biblioteca: {Error}", initialized.Error);
            }

            // 5. Migración única JSON (Fase 0) → SQLite (Fase 1).
            var migrator = Services.GetRequiredService<JsonToSqliteMigrator>();
            var migration = await migrator.MigrateIfNeededAsync(CancellationToken.None);
            if (migration.Migrated)
            {
                _logger.LogInformation(
                    "Migración completada: {Documents} documentos, {Notebooks} cuadernos.",
                    migration.Documents, migration.Notebooks);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Fallo al inicializar la biblioteca.");
        }

        // 6. Ventana principal.
        _window = Services.GetRequiredService<MainWindow>();
        _window.Activate();
    }

    private static async Task<string> ResolveLibraryRootAsync(CacoSettings settings)
    {
        try
        {
            // Carpeta elegida previamente con token de acceso (app empaquetada).
            var brokered = await BrokeredFolder.TryResolveAsync(settings.LibraryToken, CancellationToken.None);
            if (!string.IsNullOrWhiteSpace(brokered))
            {
                settings.LibraryPath = brokered;
            }
        }
        catch
        {
            // Sin acceso brokered (app desatendida): se sigue con la ruta guardada o el defecto.
        }

        // App desatendida (sin identidad de paquete): %LOCALAPPDATA% directo.
        // ApplicationData.Current exige paquete registrado y cuelga/revienta aquí.
        return LibraryRootResolver.ResolveEffective(settings.LibraryPath, AppPaths.DefaultLibraryRoot);
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        // Nunca cerrar de golpe si puede evitarse: registrar y mostrar mensaje entendible.
        try
        {
            _logger?.LogError(e.Exception, "Excepción no controlada.");
            e.Handled = true;
        }
        catch
        {
            // Último recurso: dejar el comportamiento por defecto.
        }
    }
}
