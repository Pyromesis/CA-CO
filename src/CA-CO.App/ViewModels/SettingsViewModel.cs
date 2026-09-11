using CaCo.App.Services;
using CaCo.Application.Configuration;
using CaCo.Application.Errors;
using CaCo.Application.Services;
using CaCo.Application.Storage;
using CaCo.Application.Updates;
using CaCo.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CaCo.App.ViewModels;

/// <summary>ViewModel de configuración: ajustes reales + secciones futuras como "próximamente".</summary>
public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly IAppConfiguration _configuration;
    private readonly IFolderPickerService _folderPicker;
    private readonly IFileLauncherService _launcher;
    private readonly ILibraryService _library;
    private readonly ILibraryPaths _paths;
    private readonly IUpdateService _updates;
    private readonly IDialogService _dialogs;
    private CacoSettings _settings;
    private UpdateRelease? _pendingRelease;

    /// <summary>Crea el ViewModel.</summary>
    public SettingsViewModel(
        IErrorHandler errors,
        IAppConfiguration configuration,
        CacoSettings settings,
        IFolderPickerService folderPicker,
        IFileLauncherService launcher,
        ILibraryService library,
        ILibraryPaths paths,
        IUpdateService updates,
        IDialogService dialogs)
        : base(errors)
    {
        _configuration = configuration;
        _settings = settings;
        _folderPicker = folderPicker;
        _launcher = launcher;
        _library = library;
        _paths = paths;
        _updates = updates;
        _dialogs = dialogs;
        _initializing = true;
        try
        {
            LibraryPath = settings.LibraryPath;
            Theme = settings.General.Theme;
            KeepOriginalCopy = settings.Storage.KeepOriginalCopy;
            EffectivePath = paths.LibraryRoot;
        }
        finally
        {
            _initializing = false;
        }

        HasChanges = false;
    }

    private bool _initializing;

    /// <summary>Ruta personalizada de la biblioteca (vacía = defecto).</summary>
    [ObservableProperty]
    private string _libraryPath = string.Empty;

    /// <summary>Ruta efectiva en uso (tras sonda de escritura).</summary>
    [ObservableProperty]
    private string _effectivePath = string.Empty;

    /// <summary>Resumen de uso (documentos • cuadernos • tamaño).</summary>
    [ObservableProperty]
    private string _storageSummary = "…";

    /// <summary>Tema: System, Light, Dark.</summary>
    [ObservableProperty]
    private string _theme = "System";

    /// <summary>Temas disponibles.</summary>
    public string[] Themes { get; } = ["System", "Light", "Dark"];

    /// <summary>Conservar copia del original.</summary>
    [ObservableProperty]
    private bool _keepOriginalCopy = true;

    /// <summary>Indica si hay cambios sin guardar.</summary>
    [ObservableProperty]
    private bool _hasChanges;

    /// <summary>Aviso de que cambiar la biblioteca requiere reiniciar.</summary>
    public string RestartNotice => "Cambiar la ubicación de la biblioteca requiere reiniciar CA-CO.";

    /// <summary>Versión instalada (la del ensamblado).</summary>
    public string CurrentVersionText => InstalledVersion.ToString();

    /// <summary>Estado de las actualizaciones (texto para la vista).</summary>
    [ObservableProperty]
    private string _updateStatus = "Aún no se ha comprobado.";

    /// <summary>Notas del release encontrado (vacío si no hay).</summary>
    [ObservableProperty]
    private string _updateNotes = string.Empty;

    /// <summary>Progreso de descarga 0-100.</summary>
    [ObservableProperty]
    private double _updateProgress;

    /// <summary>Si hay comprobación o descarga en curso.</summary>
    [ObservableProperty]
    private bool _isUpdateBusy;

    /// <summary>Si hay una versión lista para descargar e instalar.</summary>
    [ObservableProperty]
    private bool _hasUpdate;

    private static Version InstalledVersion =>
        typeof(SettingsViewModel).Assembly.GetName().Version ?? new Version(1, 0, 0);

    /// <inheritdoc/>
    public override async Task OnNavigatedToAsync(CancellationToken ct)
    {
        ClearMessages();
        EffectivePath = _paths.LibraryRoot;
        try
        {
            var stats = await _library.GetStatsAsync(ct);
            StorageSummary = stats.IsSuccess
                ? $"{stats.Value.DocumentCount} documentos • {stats.Value.NotebookCount} cuadernos • {Views.DocumentFormat.Size(stats.Value.TotalBytes)}"
                : "No disponible";
            if (stats.IsFailure)
            {
                System.Diagnostics.Debug.WriteLine($"Stats falló: {stats.Error.Code}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Stats falló: {ex.Message}");
            StorageSummary = "No disponible";
        }
    }

    /// <summary>Elige la carpeta de biblioteca con el selector del sistema.</summary>
    [RelayCommand]
    private async Task PickFolderAsync(CancellationToken ct)
    {
        ClearMessages();
        try
        {
            var picked = await _folderPicker.PickFolderAsync();
            if (picked is null)
            {
                return;
            }

            LibraryPath = picked.Path;
            _settings.LibraryToken = picked.Token;
            HasChanges = true;
            ShowInfo("Carpeta elegida. Guarda y reinicia para aplicarla.");
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    /// <summary>Abre la biblioteca en el Explorador.</summary>
    [RelayCommand]
    private async Task OpenLibraryFolderAsync(CancellationToken ct)
    {
        if (!await _launcher.ShowInFolderAsync(_paths.LibraryRoot, ct))
        {
            ShowInfo("No se pudo abrir la carpeta en el Explorador.");
        }
    }

    /// <summary>Guarda los ajustes.</summary>
    [RelayCommand]
    private async Task SaveAsync(CancellationToken ct)
    {
        ClearMessages();
        IsBusy = true;
        try
        {
            var trimmed = LibraryPath.Trim();
            if (!string.IsNullOrEmpty(trimmed))
            {
                string full;
                try
                {
                    full = Path.GetFullPath(trimmed);
                }
                catch (Exception)
                {
                    ShowError(Error.Validation("Settings.InvalidPath", "La ruta de biblioteca no es válida."));
                    return;
                }

                if (!Path.IsPathRooted(full))
                {
                    ShowError(Error.Validation("Settings.InvalidPath", "La ruta de biblioteca debe ser absoluta."));
                    return;
                }

                var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                if (!string.IsNullOrEmpty(windowsDir)
                    && full.StartsWith(Path.GetFullPath(windowsDir) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    ShowError(Error.Validation("Settings.InvalidPath", "Elige una carpeta fuera de Windows."));
                    return;
                }
            }

            _settings.LibraryPath = trimmed;
            _settings.General.Theme = Theme;
            _settings.Storage.KeepOriginalCopy = KeepOriginalCopy;
            await _configuration.SaveAsync(_settings, ct);
            HasChanges = false;
            App.ApplyTheme(Theme);
            ShowInfo("Ajustes guardados. Los cambios de ubicación se aplican al reiniciar.");
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Restablece la ruta por defecto.</summary>
    [RelayCommand]
    private void UseDefaultLibrary()
    {
        LibraryPath = string.Empty;
        _settings.LibraryToken = string.Empty;
        HasChanges = true;
    }

    /// <summary>Comprueba si hay una versión nueva en GitHub Releases.</summary>
    [RelayCommand]
    private async Task CheckForUpdatesAsync(CancellationToken ct)
    {
        if (IsUpdateBusy)
        {
            return;
        }

        IsUpdateBusy = true;
        HasUpdate = false;
        _pendingRelease = null;
        UpdateNotes = string.Empty;
        UpdateStatus = "Buscando actualizaciones…";
        try
        {
            var current = InstalledVersion;
            var result = await _updates.CheckForUpdatesAsync(current, ct);
            if (result.Succeeded && result is { UpdateAvailable: true, Release: not null })
            {
                _pendingRelease = result.Release;
                HasUpdate = true;
                var size = result.Release.SizeBytes > 0
                    ? $" ({Views.DocumentFormat.Size(result.Release.SizeBytes)})"
                    : string.Empty;
                UpdateStatus = $"Disponible {result.Release.Tag}{size}. Se descargará el instalador.";
                UpdateNotes = result.Release.Notes;
            }
            else if (result.Succeeded)
            {
                UpdateStatus = $"Estás al día (versión {current}).";
            }
            else
            {
                UpdateStatus = "No se pudo comprobar. Revisa tu conexión; seguirás con tu versión local.";
            }
        }
        catch (OperationCanceledException)
        {
            UpdateStatus = "Comprobación cancelada.";
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
        finally
        {
            IsUpdateBusy = false;
        }
    }

    /// <summary>Descarga el instalador, lo verifica y lo lanza (la app se cierra y reabre).</summary>
    [RelayCommand]
    private async Task DownloadAndInstallAsync(CancellationToken ct)
    {
        if (IsUpdateBusy || _pendingRelease is null)
        {
            return;
        }

        var release = _pendingRelease;
        var confirmed = await _dialogs.ConfirmAsync(
            "Actualizar CA-CO",
            $"Se descargará {release.AssetName} y se instalará. La app se cerrará y se abrirá actualizada.",
            "Actualizar");
        if (!confirmed)
        {
            return;
        }

        IsUpdateBusy = true;
        UpdateProgress = 0;
        UpdateStatus = $"Descargando {release.Tag}…";
        try
        {
            var progress = new Progress<double>(value => UpdateProgress = value);
            var downloaded = await _updates.DownloadAsync(release, progress, ct);
            if (downloaded.IsFailure)
            {
                ShowError(downloaded.Error);
                UpdateStatus = "La descarga falló. Inténtalo de nuevo.";
                return;
            }

            UpdateStatus = "Instalando… La app se cerrará ahora.";
            if (!_updates.LaunchInstaller(downloaded.Value))
            {
                ShowError(Error.Storage("Update.LaunchFailed", "No se pudo iniciar el instalador."));
                UpdateStatus = "No se pudo iniciar el instalador.";
                return;
            }

            Microsoft.UI.Xaml.Application.Current.Exit();
        }
        catch (OperationCanceledException)
        {
            UpdateStatus = "Descarga cancelada.";
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
        finally
        {
            IsUpdateBusy = false;
        }
    }

    partial void OnLibraryPathChanged(string value)
    {
        if (!_initializing)
        {
            HasChanges = true;
        }
    }

    partial void OnThemeChanged(string value)
    {
        if (!_initializing)
        {
            HasChanges = true;
        }
    }

    partial void OnKeepOriginalCopyChanged(bool value)
    {
        if (!_initializing)
        {
            HasChanges = true;
        }
    }
}
