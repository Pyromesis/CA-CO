using CaCo.App.Services;
using CaCo.Application.Configuration;
using CaCo.Application.Errors;
using CaCo.Application.Services;
using CaCo.Application.Storage;
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
    private CacoSettings _settings;

    /// <summary>Crea el ViewModel.</summary>
    public SettingsViewModel(
        IErrorHandler errors,
        IAppConfiguration configuration,
        CacoSettings settings,
        IFolderPickerService folderPicker,
        IFileLauncherService launcher,
        ILibraryService library,
        ILibraryPaths paths)
        : base(errors)
    {
        _configuration = configuration;
        _settings = settings;
        _folderPicker = folderPicker;
        _launcher = launcher;
        _library = library;
        _paths = paths;
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
