using System.Collections.ObjectModel;
using CaCo.App.Services;
using CaCo.Application.Errors;
using CaCo.Application.Services;
using CaCo.Application.Storage;
using CaCo.Core;
using CaCo.Domain;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Media.Imaging;

namespace CaCo.App.ViewModels;

/// <summary>Tipo de vista previa disponible.</summary>
public enum PreviewKind
{
    /// <summary>Sin vista previa (abrir con app externa).</summary>
    None = 0,

    /// <summary>Imagen incrustada.</summary>
    Image = 1,

    /// <summary>Texto incrustado.</summary>
    Text = 2,
}

/// <summary>Etiqueta visible con su id (para quitar).</summary>
public sealed partial class TagItem : ObservableObject
{
    /// <summary>Crea el item.</summary>
    public TagItem(Guid id, string name)
    {
        Id = id;
        Name = name;
    }

    /// <summary>Identificador.</summary>
    public Guid Id { get; }

    /// <summary>Nombre.</summary>
    [ObservableProperty]
    private string _name = string.Empty;
}

/// <summary>Opción del diálogo de mover (incluye "sin clasificar").</summary>
/// <param name="Notebook">Cuaderno destino (<c>null</c> = sin clasificar).</param>
/// <param name="Label">Texto visible.</param>
public sealed record NotebookChoice(Notebook? Notebook, string Label);

/// <summary>ViewModel del detalle de documento: vista previa, metadatos, cuaderno y etiquetas.</summary>
public sealed partial class DocumentDetailViewModel : ViewModelBase
{
    private const int MaxPreviewChars = 200_000;

    private readonly IDocumentService _documents;
    private readonly INotebookService _notebooks;
    private readonly IFileStorage _storage;
    private readonly ILibraryPaths _paths;
    private readonly IDialogService _dialogs;
    private readonly IFileLauncherService _launcher;
    private readonly INavigationService _navigation;

    private Guid _documentId = Guid.Empty;
    private Document? _document;

    /// <summary>Crea el ViewModel.</summary>
    public DocumentDetailViewModel(
        IErrorHandler errors,
        IDocumentService documents,
        INotebookService notebooks,
        IFileStorage storage,
        ILibraryPaths paths,
        IDialogService dialogs,
        IFileLauncherService launcher,
        INavigationService navigation)
        : base(errors)
    {
        _documents = documents;
        _notebooks = notebooks;
        _storage = storage;
        _paths = paths;
        _dialogs = dialogs;
        _launcher = launcher;
        _navigation = navigation;
    }

    /// <summary>Documento cargado.</summary>
    [ObservableProperty]
    private Document? _documentView;

    /// <summary>Título visible (evita nulos en el binding).</summary>
    [ObservableProperty]
    private string _documentTitle = "…";

    /// <summary>Nombre del cuaderno actual (o "Sin clasificar").</summary>
    [ObservableProperty]
    private string _notebookName = "Sin clasificar";

    /// <summary>Etiquetas del documento.</summary>
    public ObservableCollection<TagItem> Tags { get; } = [];

    /// <summary>Tipo de vista previa.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsImagePreview))]
    [NotifyPropertyChangedFor(nameof(IsTextPreview))]
    private PreviewKind _preview = PreviewKind.None;

    /// <summary>Indica si hay vista previa de imagen.</summary>
    public bool IsImagePreview => Preview == PreviewKind.Image;

    /// <summary>Indica si hay vista previa de texto.</summary>
    public bool IsTextPreview => Preview == PreviewKind.Text;

    /// <summary>Imagen de vista previa.</summary>
    [ObservableProperty]
    private BitmapImage? _previewImage;

    /// <summary>Texto de vista previa.</summary>
    [ObservableProperty]
    private string _previewText = string.Empty;

    /// <summary>Subtítulo de metadatos (tamaño • fechas).</summary>
    [ObservableProperty]
    private string _metaText = string.Empty;

    /// <summary>Hash corto para identificar duplicados.</summary>
    [ObservableProperty]
    private string _hashText = "…”";

    /// <summary>Indica si está en papelera (muestra restaurar/eliminar).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotInTrash))]
    private bool _isInTrash;

    /// <summary>Indica si NO está en papelera.</summary>
    public bool IsNotInTrash => !IsInTrash;

    /// <inheritdoc/>
    public override void ReceiveParameter(object? parameter)
    {
        if (parameter is Guid id)
        {
            _documentId = id;
        }
    }

    /// <inheritdoc/>
    public override async Task OnNavigatedToAsync(CancellationToken ct)
    {
        await LoadAsync(ct);
    }

    /// <summary>Recarga el documento.</summary>
    [RelayCommand]
    private async Task LoadAsync(CancellationToken ct)
    {
        if (_documentId == Guid.Empty || IsBusy)
        {
            return;
        }

        IsBusy = true;
        ClearMessages();
        try
        {
            var found = await _documents.GetByIdAsync(_documentId, true, ct);
            if (found.IsFailure || found.Value is null)
            {
                ShowError(found.IsFailure ? found.Error : DomainErrors.Document.NotFound(_documentId));
                return;
            }

            _document = found.Value;
            DocumentView = _document;
            DocumentTitle = _document.Name;
            IsInTrash = _document.IsDeleted;
            MetaText = $"{Views.DocumentFormat.TypeName(_document.FileType)}  •  " +
                $"{Views.DocumentFormat.Size(_document.SizeBytes)}  •  " +
                $"Importado el {_document.ImportedAt.LocalDateTime:dd/MM/yyyy}";
            HashText = _document.ContentHash is null
                ? "—”"
                : _document.ContentHash[..Math.Min(12, _document.ContentHash.Length)];

            if (_document.NotebookId.HasValue)
            {
                var notebook = await _notebooks.ListAllActiveAsync(ct);
                NotebookName = notebook.IsSuccess
                    ? notebook.Value.FirstOrDefault(n => n.Id == _document.NotebookId)?.Name ?? "Sin clasificar"
                    : "Sin clasificar";
            }
            else
            {
                NotebookName = "Sin clasificar";
            }

            await LoadTagsAsync(ct);
            await LoadPreviewAsync(ct);
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

    /// <summary>Abre con la app predeterminada.</summary>
    [RelayCommand]
    private async Task OpenAsync(CancellationToken ct)
    {
        var path = ManagedPath();
        if (path is null || !File.Exists(path))
        {
            ShowError(Error.Storage("Detail.MissingFile", "El archivo ya no está en la biblioteca. Puede que se haya movido fuera de CA-CO."));
            return;
        }

        if (!await _launcher.OpenFileAsync(path, ct))
        {
            ShowError(Error.Storage("Detail.OpenFailed", "No se pudo abrir el archivo con su app predeterminada."));
        }
    }

    /// <summary>Muestra en el Explorador.</summary>
    [RelayCommand]
    private async Task ShowInFolderAsync(CancellationToken ct)
    {
        var path = ManagedPath();
        if (path is null || !File.Exists(path))
        {
            ShowError(Error.Storage("Detail.MissingFile", "El archivo ya no está en la biblioteca. Puede que se haya movido fuera de CA-CO."));
            return;
        }

        if (!await _launcher.ShowInFolderAsync(path, ct))
        {
            ShowError(Error.Storage("Detail.FolderFailed", "No se pudo mostrar la carpeta en el Explorador."));
        }
    }

    /// <summary>Marca o desmarca favorito.</summary>
    [RelayCommand]
    private async Task ToggleFavoriteAsync(CancellationToken ct)
    {
        if (_document is null)
        {
            return;
        }

        var result = await _documents.SetFavoriteAsync(_document.Id, !_document.IsFavorite, ct);
        if (result.IsFailure)
        {
            ShowError(result.Error);
            return;
        }

        await LoadAsync(ct);
    }

    /// <summary>Renombra.</summary>
    [RelayCommand]
    private async Task RenameAsync(CancellationToken ct)
    {
        if (_document is null)
        {
            return;
        }

        try
        {
            var name = await _dialogs.PromptTextAsync("Renombrar", "Nuevo nombre", _document.Name);
            if (name is null)
            {
                return;
            }

            var result = await _documents.RenameAsync(_document.Id, name, ct);
            if (result.IsFailure)
            {
                ShowError(result.Error);
                return;
            }

            await LoadAsync(ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    /// <summary>Mueve a otro cuaderno (o lo deja sin clasificar).</summary>
    [RelayCommand]
    private async Task MoveToNotebookAsync(CancellationToken ct)
    {
        if (_document is null)
        {
            return;
        }

        var all = await _notebooks.ListAllActiveAsync(ct);
        if (all.IsFailure)
        {
            ShowError(all.Error);
            return;
        }

        try
        {
            var options = new List<NotebookChoice> { new(null, "(Sin clasificar)") };
            options.AddRange(all.Value.Select(n => new NotebookChoice(n, IndentedName(n, all.Value))));

            var choice = await _dialogs.PromptChoiceAsync("Mover a cuaderno", options, c => c.Label);
            if (choice is null)
            {
                return;
            }

            var result = await _documents.MoveToNotebookAsync(_document.Id, choice.Notebook?.Id, ct);
            if (result.IsFailure)
            {
                ShowError(result.Error);
                return;
            }

            await LoadAsync(ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    /// <summary>Añade una etiqueta (la crea si no existe).</summary>
    [RelayCommand]
    private async Task AddTagAsync(CancellationToken ct)
    {
        try
        {
            if (_document is null)
            {
                return;
            }

            var name = await _dialogs.PromptTextAsync("Añadir etiqueta", "Nombre de la etiqueta");
            if (name is null)
            {
                return;
            }

            var result = await _documents.AddTagAsync(_document.Id, name, ct);
            if (result.IsFailure)
            {
                ShowError(result.Error);
                return;
            }

            await LoadAsync(ct);
    
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }
    /// <summary>Quita una etiqueta.</summary>
    [RelayCommand]
    private async Task RemoveTagAsync(TagItem? tag, CancellationToken ct)
    {
        try
        {
            if (_document is null || tag is null)
            {
                return;
            }

            var result = await _documents.RemoveTagAsync(_document.Id, tag.Id, ct);
            if (result.IsFailure)
            {
                ShowError(result.Error);
                return;
            }

            await LoadAsync(ct);
    
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }
    /// <summary>Mueve a la papelera y vuelve a Documentos (una confirmación).</summary>
    [RelayCommand]
    private async Task MoveToTrashAsync(CancellationToken ct)
    {
        try
        {
            if (_document is null)
            {
                return;
            }

            var confirmed = await _dialogs.ConfirmAsync(
                "Mover a la papelera",
                $"¿Estás seguro de que quieres eliminar «{_document.Name}»? Quedará en la papelera.",
                "Mover a la papelera");
            if (!confirmed)
            {
                return;
            }

            var result = await _documents.MoveToTrashAsync(_document.Id, ct);
            if (result.IsFailure)
            {
                ShowError(result.Error);
                return;
            }

            _navigation.NavigateTo<DocumentsViewModel>();
    
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }
    /// <summary>Restaura desde la papelera.</summary>
    [RelayCommand]
    private async Task RestoreAsync(CancellationToken ct)
    {
        if (_document is null)
        {
            return;
        }

        var result = await _documents.RestoreAsync(_document.Id, ct);
        if (result.IsFailure)
        {
            ShowError(result.Error);
            return;
        }

        await LoadAsync(ct);
    }

    /// <summary>Elimina definitivamente (con confirmación) y vuelve.</summary>
    [RelayCommand]
    private async Task DeletePermanentlyAsync(CancellationToken ct)
    {
        if (_document is null)
        {
            return;
        }

        var confirmed = await _dialogs.ConfirmAsync(
            "Eliminar definitivamente",
            $"«{_document.Name}» se eliminará para siempre, con sus copias y su miniatura.",
            "Eliminar");
        if (!confirmed)
        {
            return;
        }

        var result = await _documents.DeletePermanentlyAsync(_document.Id, ct);
        if (result.IsFailure)
        {
            ShowError(result.Error);
            return;
        }

        _navigation.NavigateTo<DocumentsViewModel>();
    }

    /// <summary>Abre el espacio de trabajo (visor, anotaciones y comentarios).</summary>
    [RelayCommand]
    private void OpenReader()
    {
        if (_document is not null)
        {
            _navigation.NavigateTo<ReaderViewModel>(_document.Id);
        }
    }

    /// <summary>Vuelve a la biblioteca.</summary>
    [RelayCommand]
    private void GoBack() => _navigation.NavigateTo<DocumentsViewModel>();

    private string? ManagedPath()
    {
        var name = _document?.StoredFileName;
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || name.Contains("..", StringComparison.Ordinal)
            || name.Contains('/') || name.Contains('\\')
            || Path.GetFileName(name) != name)
        {
            return null;
        }

        var fullFolder = Path.GetFullPath(_paths.Documents);
        var fullPath = Path.GetFullPath(Path.Combine(_paths.Documents, name));
        if (!fullPath.StartsWith(fullFolder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return fullPath;
    }

    private async Task LoadTagsAsync(CancellationToken ct)
    {
        Tags.Clear();
        if (_document is null || _document.TagIds.Count == 0)
        {
            return;
        }

        var all = await _documents.ListAllTagsAsync(ct);
        if (all.IsFailure)
        {
            ShowError(all.Error);
            return;
        }

        var byId = all.Value.ToDictionary(t => t.Id);
        foreach (var id in _document.TagIds)
        {
            if (byId.TryGetValue(id, out var tag))
            {
                Tags.Add(new TagItem(tag.Id, tag.Name));
            }
        }
    }

    private async Task LoadPreviewAsync(CancellationToken ct)
    {
        Preview = PreviewKind.None;
        PreviewImage = null;
        PreviewText = string.Empty;

        if (_document?.StoredFileName is null)
        {
            return;
        }

        try
        {
            if (_document.FileType is DocumentType.Png or DocumentType.Jpg or DocumentType.Jpeg)
            {
                var path = ManagedPath();
                if (path is not null && File.Exists(path))
                {
                    PreviewImage = new BitmapImage(new Uri(new FileInfo(path).FullName, UriKind.Absolute));
                    Preview = PreviewKind.Image;
                }
            }
            else if (_document.FileType == DocumentType.Txt)
            {
                await using var stream = await _storage.OpenReadAsync(
                    _paths.Documents, _document.StoredFileName, ct);
                using var reader = new StreamReader(stream);
                var buffer = new char[MaxPreviewChars + 1];
                var read = 0;
                int chunk;
                while (read < buffer.Length
                    && (chunk = await reader.ReadAsync(buffer.AsMemory(read), ct)) > 0)
                {
                    read += chunk;
                }

                PreviewText = new string(buffer, 0, Math.Min(read, MaxPreviewChars));
                if (read > MaxPreviewChars)
                {
                    PreviewText += "\n… (vista previa truncada)";
                }

                Preview = PreviewKind.Text;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Vista previa falló: {ex.Message}");
            Preview = PreviewKind.None;
        }
    }

    private static string IndentedName(Notebook notebook, IReadOnlyList<Notebook> all)
    {
        var level = 0;
        var parentId = notebook.ParentId;
        while (parentId.HasValue && level < 10)
        {
            level++;
            parentId = all.FirstOrDefault(n => n.Id == parentId.Value)?.ParentId;
        }

        return new string(' ', level * 3) + notebook.Name;
    }
}
