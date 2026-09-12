using System.Collections.ObjectModel;
using CaCo.App.Services;
using CaCo.Application.Errors;
using CaCo.Application.Import;
using CaCo.Application.Services;
using CaCo.Application.Storage;
using CaCo.Core;
using CaCo.Domain;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Media.Imaging;

namespace CaCo.App.ViewModels;

/// <summary>Modo del espacio de trabajo segÃºn el tipo de documento.</summary>
public enum ReaderKind
{
    /// <summary>PDF (visor integrado).</summary>
    Pdf = 0,

    /// <summary>Imagen (zoom + tinta).</summary>
    Image = 1,

    /// <summary>Texto editable.</summary>
    Text = 2,

    /// <summary>Otros (Word/Excel: metadatos + apertura externa).</summary>
    Other = 3,
}

/// <summary>
/// Espacio de trabajo del documento: visor con zoom a la izquierda,
/// comentarios y dictado a la derecha. La ediciÃ³n guarda en la biblioteca.
/// </summary>
public sealed partial class ReaderViewModel : ViewModelBase
{
    private const int MaxTextChars = 500_000;

    private readonly IDocumentService _documents;
    private readonly INoteService _notes;
    private readonly IFileStorage _storage;
    private readonly ILibraryPaths _paths;
    private readonly IDocumentImporter _importer;
    private readonly IDialogService _dialogs;
    private readonly IFileLauncherService _launcher;
    private readonly IVoiceDictationService _voice;
    private readonly IVoiceMessageSessionFactory _voiceFactory;
    private readonly IImageOcrService _ocr;
    private readonly ILanguageFeatureInstaller _language;
    private readonly INavigationService _navigation;
    private readonly Microsoft.UI.Dispatching.DispatcherQueue? _dispatcher;

    private IVoiceMessageSession? _voiceSession;

    private Guid _documentId = Guid.Empty;
    private Document? _document;

    /// <summary>Crea el ViewModel.</summary>
    public ReaderViewModel(
        IErrorHandler errors,
        IDocumentService documents,
        INoteService notes,
        IFileStorage storage,
        ILibraryPaths paths,
        IDocumentImporter importer,
        IDialogService dialogs,
        IFileLauncherService launcher,
        IVoiceDictationService voice,
        IVoiceMessageSessionFactory voiceFactory,
        IImageOcrService ocr,
        ILanguageFeatureInstaller language,
        INavigationService navigation)
        : base(errors)
    {
        _documents = documents;
        _notes = notes;
        _storage = storage;
        _paths = paths;
        _importer = importer;
        _dialogs = dialogs;
        _launcher = launcher;
        _voice = voice;
        _voiceFactory = voiceFactory;
        _ocr = ocr;
        _language = language;
        _navigation = navigation;
        _dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
    }

    /// <summary>TÃ­tulo del documento.</summary>
    [ObservableProperty]
    private string _title = "â€¦";

    /// <summary>SubtÃ­tulo (tipo â€¢ tamaÃ±o â€¢ fecha).</summary>
    [ObservableProperty]
    private string _subtitle = string.Empty;

    /// <summary>Modo de lectura.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPdf))]
    [NotifyPropertyChangedFor(nameof(IsImage))]
    [NotifyPropertyChangedFor(nameof(IsText))]
    [NotifyPropertyChangedFor(nameof(IsOther))]
    private ReaderKind _kind = ReaderKind.Other;

    /// <summary>Indica si es PDF.</summary>
    public bool IsPdf => Kind == ReaderKind.Pdf;

    /// <summary>Indica si es imagen.</summary>
    public bool IsImage => Kind == ReaderKind.Image;

    /// <summary>Indica si es texto.</summary>
    public bool IsText => Kind == ReaderKind.Text;

    /// <summary>Indica si es otro tipo.</summary>
    public bool IsOther => Kind == ReaderKind.Other;

    /// <summary>URI del PDF para el visor integrado.</summary>
    [ObservableProperty]
    private Uri? _pdfUri;

    /// <summary>Imagen a mostrar.</summary>
    [ObservableProperty]
    private BitmapImage? _image;

    /// <summary>Texto editable.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTextDirty))]
    private string _textContent = string.Empty;

    /// <summary>Texto original cargado (para detectar cambios).</summary>
    private string _originalText = string.Empty;

    /// <summary>Indica si el texto cambiÃ³ sin guardar.</summary>
    public bool IsTextDirty => IsText && TextContent != _originalText;

    /// <summary>Comentarios del documento.</summary>
    public ObservableCollection<Note> Comments { get; } = [];

    /// <summary>Nuevo comentario.</summary>
    [ObservableProperty]
    private string _newComment = string.Empty;

    /// <summary>Indica si se estÃ¡ grabando voz.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsActivelyRecording))]
    [NotifyPropertyChangedFor(nameof(ShowVoicePanel))]
    [NotifyPropertyChangedFor(nameof(IsVoiceIdle))]
    [NotifyPropertyChangedFor(nameof(VoiceStatus))]
    private bool _isRecording;

    /// <summary>Indica si la grabaciÃ³n estÃ¡ en pausa.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsActivelyRecording))]
    [NotifyPropertyChangedFor(nameof(ShowVoicePanel))]
    [NotifyPropertyChangedFor(nameof(IsVoiceIdle))]
    [NotifyPropertyChangedFor(nameof(VoiceStatus))]
    private bool _isPaused;

    /// <summary>Borrador del mensaje de voz.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasVoiceDraft))]
    [NotifyPropertyChangedFor(nameof(ShowVoicePanel))]
    [NotifyPropertyChangedFor(nameof(IsVoiceIdle))]
    [NotifyPropertyChangedFor(nameof(VoiceStatus))]
    private string _voiceDraft = string.Empty;

    /// <summary>HipÃ³tesis en vivo (no definitiva).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VoiceLiveDisplay))]
    private string _voiceLive = string.Empty;

    /// <summary>HipÃ³tesis visible (con espacio separador).</summary>
    public string VoiceLiveDisplay =>
        string.IsNullOrWhiteSpace(VoiceLive) ? string.Empty : " " + VoiceLive.Trim();

    /// <summary>Indica si hay grabaciÃ³n activa (sin pausa).</summary>
    public bool IsActivelyRecording => IsRecording && !IsPaused;

    /// <summary>Indica si hay borrador.</summary>
    public bool HasVoiceDraft => !string.IsNullOrWhiteSpace(VoiceDraft);

    /// <summary>Indica si se muestra el panel de voz.</summary>
    public bool ShowVoicePanel => IsRecording || IsPaused || HasVoiceDraft;

    /// <summary>Indica si la voz estÃ¡ inactiva (botÃ³n Dictar visible).</summary>
    public bool IsVoiceIdle => !ShowVoicePanel;

    /// <summary>Estado textual del panel de voz.</summary>
    public string VoiceStatus => IsPaused
        ? "En pausa. Sigue, termina o elimina."
        : IsRecording ? "Grabandoâ€¦ habla ahora." : "Borrador listo: termina o elimina.";

    /// <summary>Indica si el dictado estÃ¡ disponible.</summary>
    [ObservableProperty]
    private bool _voiceAvailable;

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

    /// <inheritdoc/>
    public override async Task OnNavigatedFromAsync(CancellationToken ct)
    {
        try
        {
            if (_voiceSession is not null)
            {
                try
                {
                    await _voiceSession.StopAsync();
                }
                catch
                {
                }

                CleanupVoiceSession();
            }
        }
        catch
        {
        }

        IsRecording = false;
        IsPaused = false;
        await Task.CompletedTask;
    }

    /// <summary>Carga documento, vista y comentarios.</summary>
    [RelayCommand]
    private async Task LoadAsync(CancellationToken ct)
    {
        if (_documentId == Guid.Empty || IsBusy)
        {
            return;
        }

        IsBusy = true;
        ClearMessages();
        CleanupVoiceSession();
        IsRecording = false;
        IsPaused = false;
        VoiceDraft = string.Empty;
        VoiceLive = string.Empty;
        try
        {
            var found = await _documents.GetByIdAsync(_documentId, true, ct);
            if (found.IsFailure || found.Value is null)
            {
                ShowError(found.IsFailure ? found.Error : DomainErrors.Document.NotFound(_documentId));
                return;
            }

            _document = found.Value;
            Title = _document.Name;
            Subtitle = Views.DocumentFormat.Subtitle(_document);

            Kind = _document.FileType switch
            {
                DocumentType.Pdf => ReaderKind.Pdf,
                DocumentType.Png or DocumentType.Jpg or DocumentType.Jpeg => ReaderKind.Image,
                DocumentType.Txt => ReaderKind.Text,
                _ => ReaderKind.Other,
            };

            PdfUri = null;
            Image = null;
            TextContent = string.Empty;
            _originalText = string.Empty;

            var path = ManagedPath();
            if (path is not null && File.Exists(path))
            {
                if (Kind == ReaderKind.Pdf)
                {
                    PdfUri = new Uri(new FileInfo(path).FullName, UriKind.Absolute);
                }
                else if (Kind == ReaderKind.Image)
                {
                    Image = new BitmapImage(new Uri(new FileInfo(path).FullName, UriKind.Absolute));
                }
                else if (Kind == ReaderKind.Text && _document.StoredFileName is not null)
                {
                    await using var stream = await _storage.OpenReadAsync(
                        _paths.Documents, _document.StoredFileName, ct).ConfigureAwait(false);
                    using var reader = new StreamReader(stream);
                    var buffer = new char[MaxTextChars + 1];
                    var read = await reader.ReadAsync(buffer, ct).ConfigureAwait(false);
                    var text = new string(buffer, 0, Math.Min(read, MaxTextChars));
                    if (read > MaxTextChars)
                    {
                        text += "\nâ€¦ (truncado: el archivo es mayor de lo editable aquÃ­)";
                    }

                    TextContent = text;
                    _originalText = text;
                    OnPropertyChanged(nameof(IsTextDirty));
                }
            }

            VoiceAvailable = await _voice.IsAvailableAsync();
            await LoadCommentsAsync(ct);
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

    /// <summary>Guarda el texto editado en la biblioteca.</summary>
    [RelayCommand]
    private async Task SaveTextAsync(CancellationToken ct)
    {
        if (_document is null || !IsTextDirty)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _documents.SaveTextContentAsync(_document.Id, TextContent, ct);
            if (result.IsFailure)
            {
                ShowError(result.Error);
                return;
            }

            _originalText = TextContent;
            OnPropertyChanged(nameof(IsTextDirty));
            IsBusy = false;
            await LoadAsync(CancellationToken.None);
            if (!HasError)
            {
                ShowInfo("Cambios guardados en tu biblioteca.");
            }
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

    /// <summary>
    /// Relee la copia administrada (adopta anotaciones guardadas con Ctrl+S en el visor).
    /// </summary>
    [RelayCommand]
    private async Task SyncExternalChangesAsync(CancellationToken ct)
    {
        if (_document is null)
        {
            return;
        }

        IsBusy = true;
        ClearMessages();
        try
        {
            var result = await _documents.RefreshFileMetadataAsync(_document.Id, ct);
            if (result.IsFailure)
            {
                ShowError(result.Error);
                return;
            }

            var changed = result.Value;
            IsBusy = false;
            await LoadAsync(CancellationToken.None);
            if (!HasError)
            {
                ShowInfo(changed
                    ? "Cambios detectados y guardados en tu biblioteca."
                    : "Sin cambios: la biblioteca ya estÃ¡ al dÃ­a.");
            }
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

    /// <summary>Guarda la imagen anotada como documento nuevo.</summary>
    public async Task SaveAnnotatedImageAsync(byte[] pngBytes, CancellationToken ct)
    {
        const int maxPngBytes = 50 * 1024 * 1024;
        if (_document is null || pngBytes.Length == 0)
        {
            return;
        }

        if (pngBytes.Length > maxPngBytes)
        {
            ShowError(Error.Validation("Reader.ImageTooLarge", "La anotaciÃ³n es demasiado grande (mÃ¡x. 50 MB)."));
            return;
        }

        IsBusy = true;
        ClearMessages();
        try
        {
            var tempName = $"anotado-{Guid.NewGuid():N}.png";
            var tempPath = Path.Combine(_paths.Temp, tempName);
            Directory.CreateDirectory(_paths.Temp);
            await File.WriteAllBytesAsync(tempPath, pngBytes, ct).ConfigureAwait(false);

            var imported = await _importer.ImportAsync(
                new ImportRequest(
                    tempPath, _document.NotebookId, $"{_document.Name} (anotado)"),
                ct).ConfigureAwait(false);
            try
            {
                File.Delete(tempPath);
            }
            catch
            {
                // Limpieza best-effort.
            }

            if (imported.IsFailure)
            {
                ShowError(imported.Error);
                return;
            }

            if (!imported.Value.Succeeded || imported.Value.Document is null)
            {
                ShowError(imported.Value.Error
                    ?? Error.Storage("Reader.AnnotateFailed", "No se pudo guardar la anotaciÃ³n."));
                return;
            }

            ShowInfo("AnotaciÃ³n guardada como documento nuevo.");
            _navigation.NavigateTo<ReaderViewModel>(imported.Value.Document.Id);
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

    /// <summary>Estado del OCR en curso (vacÃ­o si no hay).</summary>
    [ObservableProperty]
    private string _ocrStatus = string.Empty;

    /// <summary>Progreso del OCR 0-100.</summary>
    [ObservableProperty]
    private double _ocrProgress;

    /// <summary>Si hay un OCR en curso.</summary>
    [ObservableProperty]
    private bool _isOcrRunning;

    /// <summary>Extrae el texto de la imagen o del PDF (OCR local).</summary>
    [RelayCommand]
    private async Task ExtractTextAsync(CancellationToken ct)
    {
        var path = ManagedPath();
        if (_document is null || path is null || !File.Exists(path))
        {
            ShowError(Error.Storage("Detail.MissingFile", "El archivo ya no estÃ¡ en la biblioteca."));
            return;
        }

        IsBusy = true;
        ClearMessages();
        try
        {
            string text;
            int? ocrPages = null;
            if (Kind == ReaderKind.Pdf)
            {
                IsOcrRunning = true;
                OcrStatus = "Extrayendo texto del PDFâ€¦";
                try
                {
                    var progress = new Progress<CaCo.Application.Ocr.OcrProgress>(p =>
                    {
                        OcrProgress = p.TotalPages > 0 ? p.PagesDone * 100.0 / p.TotalPages : 0;
                        OcrStatus = $"PÃ¡gina {p.PagesDone}/{p.TotalPages}â€¦";
                    });
                    var pdf = await _ocr.RecognizePdfAsync(path, null, progress, ct);
                    if (pdf.IsFailure)
                    {
                        ShowError(pdf.Error);
                        return;
                    }

                    text = pdf.Value.Text;
                    ocrPages = pdf.Value.PagesProcessed;
                    if (pdf.Value.Truncated)
                    {
                        ShowInfo("Texto parcial: el PDF supera los lÃ­mites del OCR.");
                    }
                }
                finally
                {
                    IsOcrRunning = false;
                    OcrProgress = 0;
                    OcrStatus = string.Empty;
                }
            }
            else
            {
                var recognized = await _ocr.RecognizeAsync(path, ct);
                if (recognized.IsFailure)
                {
                    ShowError(recognized.Error);
                    return;
                }

                text = recognized.Value;
            }

            if (ocrPages.HasValue)
            {
                var saved = await _documents.SetOcrResultAsync(_document.Id, ocrPages.Value, text, ct);
                if (saved.IsFailure)
                {
                    ShowError(saved.Error);
                }
            }

            IsBusy = false;
            var action = await _dialogs.ShowOcrResultAsync(text);
            switch (action)
            {
                case OcrAction.Copy:
                {
                    var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
                    package.SetText(text);
                    Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
                    ShowInfo("Texto copiado al portapapeles.");
                    break;
                }

                case OcrAction.SaveComment:
                {
                    var added = await _notes.AddToDocumentAsync(_document.Id, text, ct);
                    if (added.IsFailure)
                    {
                        ShowError(added.Error);
                        return;
                    }

                    await LoadCommentsAsync(CancellationToken.None);
                    ShowInfo("Texto guardado como comentario.");
                    break;
                }

                case OcrAction.SaveTxt:
                {
                    var tempPath = Path.Combine(_paths.Temp, $"ocr-{Guid.NewGuid():N}.txt");
                    Directory.CreateDirectory(_paths.Temp);
                    await File.WriteAllTextAsync(tempPath, text, ct).ConfigureAwait(false);
                    var imported = await _importer.ImportAsync(
                        new ImportRequest(tempPath, _document.NotebookId, $"{_document.Name} (OCR)"),
                        ct).ConfigureAwait(false);
                    try
                    {
                        File.Delete(tempPath);
                    }
                    catch
                    {
                        // Limpieza best-effort.
                    }

                    if (imported.IsFailure)
                    {
                        ShowError(imported.Error);
                    }
                    else if (imported.Value.Succeeded)
                    {
                        ShowInfo("Texto guardado como documento nuevo.");
                    }

                    break;
                }

                default:
                    break;
            }
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

    /// <summary>AÃ±ade el comentario escrito.</summary>
    [RelayCommand]
    private async Task AddCommentAsync(CancellationToken ct)
    {
        if (_document is null || string.IsNullOrWhiteSpace(NewComment))
        {
            return;
        }

        try
        {
            var result = await _notes.AddToDocumentAsync(_document.Id, NewComment.Trim(), ct);
            if (result.IsFailure)
            {
                ShowError(result.Error);
                return;
            }

            NewComment = string.Empty;
            await LoadCommentsAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    /// <summary>Inserta fecha y hora en el comentario (campo rÃ¡pido).</summary>
    [RelayCommand]
    private void InsertDate()
    {
        var stamp = DateTime.Now.ToString("dd/MM/yyyy HH:mm");
        NewComment = string.IsNullOrWhiteSpace(NewComment) ? stamp : NewComment.TrimEnd() + " " + stamp;
    }

    /// <summary>Edita un comentario.</summary>
    [RelayCommand]
    private async Task EditCommentAsync(Note? note, CancellationToken ct)
    {
        if (note is null)
        {
            return;
        }

        string? text;
        try
        {
            text = await _dialogs.PromptMultilineTextAsync("Editar comentario", "Comentario", note.Content);
        }
        catch (Exception ex)
        {
            ShowError(ex);
            return;
        }

        if (text is null)
        {
            return;
        }

        var result = await _notes.UpdateAsync(note.Id, text, ct);
        if (result.IsFailure)
        {
            ShowError(result.Error);
            return;
        }

        await LoadCommentsAsync(CancellationToken.None);
    }

    /// <summary>Abre los ajustes de idioma de Windows (instalar voz/OCR).</summary>
    [RelayCommand]
    private async Task OpenLanguageSettingsAsync(CancellationToken ct)
    {
        if (!await _launcher.LaunchUriAsync(new Uri("ms-settings:regionlanguage")))
        {
            ShowError(Error.Storage("Reader.SettingsFailed", "No se pudo abrir la ConfiguraciÃ³n de Windows."));
        }
    }

    /// <summary>Abre la privacidad de voz de Windows (aceptar el dictado).</summary>
    [RelayCommand]
    private async Task OpenSpeechPrivacyAsync(CancellationToken ct)
    {
        if (!await _launcher.LaunchUriAsync(new Uri("ms-settings:privacy-speech")))
        {
            ShowError(Error.Storage("Reader.SettingsFailed", "No se pudo abrir la ConfiguraciÃ³n de Windows."));
        }
    }

    /// <summary>Instala voz y OCR (pide permiso de administrador una vez).</summary>
    [RelayCommand]
    private async Task InstallLanguageFeaturesAsync(CancellationToken ct)
    {
        if (IsBusy)
        {
            return;
        }

        ClearMessages();
        IsBusy = true;
        try
        {
            ShowInfo("Instalando voz y OCR: acepta el permiso y NO cierres la ventana azul hasta que diga Â«terminadoÂ» (varios minutos, usa Internet)â€¦");
            var result = await _language.InstallSpeechAndOcrAsync(ct);
            if (result.IsFailure)
            {
                ShowError(result.Error);
                return;
            }

            VoiceAvailable = await _voice.IsAvailableAsync();
            ShowInfo(result.Value + (VoiceAvailable
                ? " Prueba a dictar de nuevo."
                : " Si sigue sin ir, reinicia la app y vuelve a probar."));
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

    /// <summary>AÃ±ade texto al cuadro de comentario (dictado, pegado, selecciÃ³n).</summary>
    public void AppendToComposer(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var clean = text.Trim();
        NewComment = string.IsNullOrWhiteSpace(NewComment) ? clean : NewComment.TrimEnd() + " " + clean;
    }

    /// <summary>Pega el portapapeles en el cuadro de comentario.</summary>
    [RelayCommand]
    private async Task PasteAsCommentAsync(CancellationToken ct)
    {
        ClearMessages();
        try
        {
            var content = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
            if (!content.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text))
            {
                ShowInfo("El portapapeles no tiene texto. CÃ³pialo antes (Ctrl+C).");
                return;
            }

            var text = await content.GetTextAsync().AsTask(ct);
            if (string.IsNullOrWhiteSpace(text))
            {
                ShowInfo("El portapapeles no tiene texto. CÃ³pialo antes (Ctrl+C).");
                return;
            }

            AppendToComposer(text.Trim());
            ShowInfo("Texto pegado: revÃ­salo y pulsa AÃ±adir.");
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    /// <summary>Elimina un comentario (con confirmaciÃ³n).</summary>
    [RelayCommand]
    private async Task DeleteCommentAsync(Note? note, CancellationToken ct)
    {
        if (note is null)
        {
            return;
        }

        var confirmed = await _dialogs.ConfirmAsync(
            "Eliminar comentario", "Se eliminarÃ¡ definitivamente.", "Eliminar");
        if (!confirmed)
        {
            return;
        }

        var result = await _notes.DeleteAsync(note.Id, ct);
        if (result.IsFailure)
        {
            ShowError(result.Error);
            return;
        }

        await LoadCommentsAsync(CancellationToken.None);
    }

    /// <summary>Inicia un mensaje de voz (sin lÃ­mite de tiempo).</summary>
    [RelayCommand]
    private async Task StartVoiceAsync(CancellationToken ct)
    {
        if (IsRecording || IsPaused)
        {
            return;
        }

        ClearMessages();
        if (!await BeginListeningAsync(ct))
        {
            return;
        }

        IsRecording = true;
        IsPaused = false;
    }

    /// <summary>Pausa la grabaciÃ³n (conserva el borrador).</summary>
    [RelayCommand]
    private async Task PauseVoiceAsync(CancellationToken ct)
    {
        if (_voiceSession is null || !IsActivelyRecording)
        {
            return;
        }

        await _voiceSession.StopAsync();
        CleanupVoiceSession();
        IsPaused = true;
    }

    /// <summary>Sigue grabando tras pausar (continÃºa el borrador).</summary>
    [RelayCommand]
    private async Task ResumeVoiceAsync(CancellationToken ct)
    {
        if (!IsPaused)
        {
            return;
        }

        ClearMessages();
        if (!await BeginListeningAsync(ct))
        {
            return;
        }

        IsPaused = false;
    }

    /// <summary>Crea la sesiÃ³n, la enlaza y la arranca.</summary>
    /// <returns><c>true</c> si quedÃ³ escuchando.</returns>
    private async Task<bool> BeginListeningAsync(CancellationToken ct)
    {
        CleanupVoiceSession();

        var session = await _voiceFactory.CreateAsync(ct);
        if (session is null)
        {
            ShowError(Error.Validation(
                "Voice.LanguageMissing",
                "Falta la voz en espaÃ±ol. Pulsa Â«Instalar voz y OCRÂ» aquÃ­ abajo (una vez, con Internet)."));
            return false;
        }

        _voiceSession = session;
        _voiceSession.FinalRecognized += OnVoiceFinal;
        _voiceSession.HypothesisChanged += OnVoiceHypothesis;
        _voiceSession.SessionCompleted += OnVoiceCompleted;

        var started = await _voiceSession.StartAsync(ct);
        if (started.IsFailure)
        {
            ShowError(started.Error);
            CleanupVoiceSession();
            return false;
        }

        return true;
    }

    /// <summary>Termina y lleva el mensaje al cuadro de comentario.</summary>
    [RelayCommand]
    private async Task FinishVoiceAsync(CancellationToken ct)
    {
        if (_voiceSession is not null)
        {
            await _voiceSession.StopAsync();
            CleanupVoiceSession();
        }

        IsRecording = false;
        IsPaused = false;
        if (!string.IsNullOrWhiteSpace(VoiceDraft))
        {
            AppendToComposer(VoiceDraft.Trim());
            VoiceDraft = string.Empty;
            VoiceLive = string.Empty;
            ShowInfo("Mensaje aÃ±adido: revÃ­salo y pulsa AÃ±adir.");
        }
    }

    /// <summary>Elimina el mensaje de voz sin usarlo.</summary>
    [RelayCommand]
    private async Task DiscardVoiceAsync(CancellationToken ct)
    {
        if (_voiceSession is not null)
        {
            await _voiceSession.StopAsync();
            CleanupVoiceSession();
        }

        IsRecording = false;
        IsPaused = false;
        VoiceDraft = string.Empty;
        VoiceLive = string.Empty;
        ClearMessages();
    }

    private void OnVoiceFinal(object? sender, string text)
    {
        var dispatcher = _dispatcher ?? Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        if (dispatcher is null)
        {
            return;
        }

        dispatcher.TryEnqueue(() =>
        {
            VoiceDraft = string.IsNullOrWhiteSpace(VoiceDraft) ? text : VoiceDraft.TrimEnd() + " " + text;
            VoiceLive = string.Empty;
        });
    }

    private void OnVoiceHypothesis(object? sender, string text)
    {
        var dispatcher = _dispatcher ?? Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        if (dispatcher is null)
        {
            return;
        }

        dispatcher.TryEnqueue(() => VoiceLive = text);
    }

    private void OnVoiceCompleted(object? sender, VoiceSessionEndReason reason)
    {
        var dispatcher = _dispatcher ?? Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        if (dispatcher is null)
        {
            return;
        }

        dispatcher.TryEnqueue(() =>
        {
            CleanupVoiceSession();
            IsRecording = false;
            IsPaused = false;
            if (reason == VoiceSessionEndReason.Timeout && !string.IsNullOrWhiteSpace(VoiceDraft))
            {
                ShowInfo("Se detuvo por silencio. El borrador se conserva: termina o elimina.");
            }
        });
    }

    private void CleanupVoiceSession()
    {
        if (_voiceSession is null)
        {
            return;
        }

        _voiceSession.FinalRecognized -= OnVoiceFinal;
        _voiceSession.HypothesisChanged -= OnVoiceHypothesis;
        _voiceSession.SessionCompleted -= OnVoiceCompleted;
        _voiceSession.Dispose();
        _voiceSession = null;
    }

    /// <summary>Abre con la app externa.</summary>
    [RelayCommand]
    private async Task OpenExternalAsync(CancellationToken ct)
    {
        var path = ManagedPath();
        if (path is null || !File.Exists(path))
        {
            ShowError(Error.Storage("Detail.MissingFile", "El archivo ya no estÃ¡ en la biblioteca."));
            return;
        }

        try
        {
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(path).AsTask(ct);
            if (!await Windows.System.Launcher.LaunchFileAsync(file))
            {
                ShowError(Error.Storage("Detail.OpenFailed", "No se pudo abrir con la app externa."));
            }
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    /// <summary>Va a la ficha del documento.</summary>
    [RelayCommand]
    private void GoToDetails()
    {
        if (_document is not null)
        {
            _navigation.NavigateTo<DocumentDetailViewModel>(_document.Id);
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

    private async Task LoadCommentsAsync(CancellationToken ct)
    {
        Comments.Clear();
        if (_document is null)
        {
            return;
        }

        var result = await _notes.ListByDocumentAsync(_document.Id, ct);
        if (result.IsFailure)
        {
            ShowError(result.Error);
            return;
        }

        foreach (var note in result.Value)
        {
            Comments.Add(note);
        }
    }
}
