using CaCo.Domain;
using Microsoft.UI.Xaml;
using Windows.Storage.Pickers;

namespace CaCo.App.Services;

/// <summary>Selector de archivos del sistema para importar documentos.</summary>
public interface IFilePickerService
{
    /// <summary>Fija la ventana propietaria (lo llama <c>MainWindow</c> al arrancar).</summary>
    void Initialize(Window window);

    /// <summary>Permite elegir uno o varios documentos soportados. Vacío si se cancela.</summary>
    Task<IReadOnlyList<string>> PickDocumentsAsync();
}

/// <summary>Implementación con <c>FileOpenPicker</c> nativo.</summary>
public sealed class FilePickerService : IFilePickerService
{
    private Window? _window;

    /// <inheritdoc/>
    public void Initialize(Window window) => _window = window;

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> PickDocumentsAsync()
    {
        if (_window is null)
        {
            throw new InvalidOperationException("FilePickerService no inicializado.");
        }

        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            ViewMode = PickerViewMode.List,
        };
        foreach (var extension in SupportedFileTypes.AllExtensions)
        {
            picker.FileTypeFilter.Add(extension);
        }

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_window);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var files = await picker.PickMultipleFilesAsync();
        if (files is null)
        {
            return [];
        }

        return files.Select(f => f.Path).Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
    }
}
