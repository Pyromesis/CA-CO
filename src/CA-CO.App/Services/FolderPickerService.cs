using Microsoft.UI.Xaml;
using Windows.Storage;
using Windows.Storage.AccessCache;
using Windows.Storage.Pickers;

namespace CaCo.App.Services;

/// <summary>Carpeta elegida por el usuario con token de acceso persistente.</summary>
/// <param name="Path">Ruta de la carpeta.</param>
/// <param name="Token">Token de FutureAccessList.</param>
public sealed record PickedFolder(string Path, string Token);

/// <summary>Selector de carpeta para reubicar la biblioteca (acceso brokered, sin capacidades restringidas).</summary>
public interface IFolderPickerService
{
    /// <summary>Fija la ventana propietaria.</summary>
    void Initialize(Window window);

    /// <summary>Pide una carpeta. <c>null</c> si se cancela.</summary>
    Task<PickedFolder?> PickFolderAsync();
}

/// <summary>Implementación con <c>FolderPicker</c> + FutureAccessList.</summary>
public sealed class FolderPickerService : IFolderPickerService
{
    private Window? _window;

    /// <inheritdoc/>
    public void Initialize(Window window) => _window = window;

    /// <inheritdoc/>
    public async Task<PickedFolder?> PickFolderAsync()
    {
        if (_window is null)
        {
            throw new InvalidOperationException("FolderPickerService no inicializado.");
        }

        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
        };
        picker.FileTypeFilter.Add("*");

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_window);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var folder = await picker.PickSingleFolderAsync();
        if (folder is null)
        {
            return null;
        }

        // AddOrReplace: un solo token fijo (Add crearía uno por vez, tope 1000).
        const string token = "CaCoLibrary";
        StorageApplicationPermissions.FutureAccessList.AddOrReplace(token, folder);
        return new PickedFolder(folder.Path, token);
    }
}

/// <summary>Resuelve un token de carpeta brokered a su ruta (arranque de la app).</summary>
public static class BrokeredFolder
{
    /// <summary>Devuelve la ruta del token, o <c>null</c> si ya no es válido.</summary>
    public static async Task<string?> TryResolveAsync(string? token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        try
        {
            if (!StorageApplicationPermissions.FutureAccessList.ContainsItem(token))
            {
                return null;
            }

            var folder = await StorageApplicationPermissions.FutureAccessList
                .GetFolderAsync(token).AsTask(ct);
            return folder.Path;
        }
        catch
        {
            return null;
        }
    }
}
