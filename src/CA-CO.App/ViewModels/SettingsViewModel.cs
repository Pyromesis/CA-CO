using CaCo.App.Services;
using CaCo.Application.Configuration;
using CaCo.Application.Errors;
using CaCo.Application.Services;
using CaCo.Application.Security;
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
    private readonly IPinLockService _pin;
    private readonly IHelloService _hello;
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
        IDialogService dialogs,
        IPinLockService pin,
        IHelloService hello)
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
        _pin = pin;
        _hello = hello;
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
        await RefreshSecurityAsync(ct);
        try
        {
            var stats = await _library.GetStatsAsync(ct);
            if (stats.IsFailure)
            {
                StorageSummary = "No disponible";
                ShowError(stats.Error);
                return;
            }

            StorageSummary = $"{stats.Value.DocumentCount} documentos • {stats.Value.NotebookCount} cuadernos • {Views.DocumentFormat.Size(stats.Value.TotalBytes)}";
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

                // Sonda de escritura: evita guardar una ruta donde la biblioteca
                // no podrá crearse al reiniciar (USB sin permiso, solo lectura...).
                try
                {
                    Directory.CreateDirectory(full);
                    var probe = Path.Combine(full, ".caco-write-test");
                    await File.WriteAllTextAsync(probe, "ok", ct);
                    File.Delete(probe);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    ShowError(Error.Validation("Settings.NotWritable", "No se puede escribir en esa carpeta. Elige otra."));
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

    /// <summary>Estado de protección visible.</summary>
    [ObservableProperty]
    private string _securityStatus = "Sin protección";

    /// <summary>Si hay PIN configurado.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoPin))]
    private bool _hasPin;

    /// <summary>Si NO hay PIN (para mostrar "Establecer").</summary>
    public bool HasNoPin => !HasPin;

    /// <summary>Si Hello está disponible en este equipo.</summary>
    [ObservableProperty]
    private bool _helloAvailable;

    /// <summary>Si Hello está activado para desbloquear.</summary>
    [ObservableProperty]
    private bool _helloEnabled;

    private async Task RefreshSecurityAsync(CancellationToken ct)
    {
        HasPin = _pin.IsPinSet;
        HelloEnabled = _settings.Security.HelloEnabled;
        try
        {
            HelloAvailable = await _hello.IsAvailableAsync();
        }
        catch
        {
            HelloAvailable = false;
        }

        SecurityStatus = !HasPin
            ? "Sin protección: cualquiera que abra el equipo entra."
            : HelloEnabled && HelloAvailable
                ? "Protegido con PIN + Windows Hello."
                : "Protegido con PIN.";
    }

    private async Task SaveSecurityAsync(CancellationToken ct)
    {
        try
        {
            await _configuration.SaveAsync(_settings, ct);
        }
        catch (Exception ex)
        {
            ShowError(ex);
            return;
        }

        await RefreshSecurityAsync(CancellationToken.None);
    }

    /// <summary>Establece el PIN (pide repetir).</summary>
    [RelayCommand]
    private async Task SetupPinAsync(CancellationToken ct)
    {
        try
        {
            var first = await _dialogs.PromptPasswordAsync("Establecer PIN", "PIN (4-64 caracteres)");
            if (first is null)
            {
                return;
            }

            var second = await _dialogs.PromptPasswordAsync("Repite el PIN", "Otra vez");
            if (second is null)
            {
                return;
            }

            if (!string.Equals(first, second, StringComparison.Ordinal))
            {
                ShowError(Error.Validation("Pin.Mismatch", "Los PIN no coinciden. Inténtalo de nuevo."));
                return;
            }

            var set = _pin.SetPin(first);
            if (set.IsFailure)
            {
                ShowError(set.Error);
                return;
            }

            await SaveSecurityAsync(ct);
            ShowInfo("PIN activado. Te lo pedirá al abrir CA-CO.");
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    /// <summary>Cambia el PIN (pide el actual).</summary>
    [RelayCommand]
    private async Task ChangePinAsync(CancellationToken ct)
    {
        try
        {
            if (!await VerifyCurrentPinAsync())
            {
                return;
            }

            var first = await _dialogs.PromptPasswordAsync("Nuevo PIN", "PIN (4-64 caracteres)");
            if (first is null)
            {
                return;
            }

            var second = await _dialogs.PromptPasswordAsync("Repite el PIN", "Otra vez");
            if (second is null)
            {
                return;
            }

            if (!string.Equals(first, second, StringComparison.Ordinal))
            {
                ShowError(Error.Validation("Pin.Mismatch", "Los PIN no coinciden. Inténtalo de nuevo."));
                return;
            }

            var set = _pin.SetPin(first);
            if (set.IsFailure)
            {
                ShowError(set.Error);
                return;
            }

            await SaveSecurityAsync(ct);
            ShowInfo("PIN cambiado.");
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    /// <summary>Desactiva el bloqueo (pide el actual).</summary>
    [RelayCommand]
    private async Task RemovePinAsync(CancellationToken ct)
    {
        try
        {
            if (!await VerifyCurrentPinAsync())
            {
                return;
            }

            _pin.RemovePin();
            await SaveSecurityAsync(ct);
            ShowInfo("Protección desactivada.");
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    /// <summary>Activa el desbloqueo con Windows Hello.</summary>
    [RelayCommand]
    private async Task EnableHelloAsync(CancellationToken ct)
    {
        try
        {
            if (!HasPin)
            {
                ShowError(Error.Validation("Pin.NotConfigured", "Activa primero un PIN."));
                return;
            }

            if (!await _hello.IsAvailableAsync())
            {
                ShowError(Error.Validation("Hello.Unavailable", "Windows Hello no está disponible en este equipo."));
                return;
            }

            var verified = await _hello.VerifyAsync("Activar desbloqueo con Hello en CA-CO", ct);
            if (verified.IsFailure)
            {
                ShowError(verified.Error);
                return;
            }

            _settings.Security.HelloEnabled = true;
            await SaveSecurityAsync(ct);
            ShowInfo("Hello activado.");
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    /// <summary>Desactiva el desbloqueo con Windows Hello.</summary>
    [RelayCommand]
    private async Task DisableHelloAsync(CancellationToken ct)
    {
        _settings.Security.HelloEnabled = false;
        await SaveSecurityAsync(ct);
        ShowInfo("Hello desactivado. Sigue el PIN.");
    }

    private async Task<bool> VerifyCurrentPinAsync()
    {
        var current = await _dialogs.PromptPasswordAsync("PIN actual", "Tu PIN");
        if (current is null)
        {
            return false;
        }

        var checkedPin = _pin.VerifyPin(current);
        if (checkedPin.IsFailure)
        {
            ShowError(checkedPin.Error);
            return false;
        }

        return true;
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
            else if (result.Error is not null)
            {
                ShowError(result.Error);
                UpdateStatus = "No se pudo comprobar; seguirás con tu versión local.";
            }
            else
            {
                UpdateStatus = "No se pudo comprobar; seguirás con tu versión local.";
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
