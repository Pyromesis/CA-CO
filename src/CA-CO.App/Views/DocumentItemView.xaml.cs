using System.ComponentModel;
using CaCo.App.ViewModels;
using CaCo.Application.Storage;
using CaCo.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace CaCo.App.Views;

/// <summary>
/// Fila reutilizable de documento (lista de Documentos, Favoritos, Recientes, Papelera e Inicio).
/// Solo formato y enlace a comandos del ViewModel; sin lógica de negocio.
/// </summary>
public sealed partial class DocumentItemView : UserControl, INotifyPropertyChanged
{
    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Documento a mostrar.</summary>
    public Document? Document
    {
        get => (Document?)GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    /// <summary>DP del documento.</summary>
    public static readonly DependencyProperty DocumentProperty =
        DependencyProperty.Register(
            nameof(Document),
            typeof(Document),
            typeof(DocumentItemView),
            new PropertyMetadata(null, static (d, _) => ((DocumentItemView)d).OnDocumentChanged()));

    /// <summary>ViewModel con los comandos (hereda de <see cref="DocumentListViewModel"/>).</summary>
    public DocumentListViewModel? ViewModel
    {
        get => (DocumentListViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    /// <summary>DP del ViewModel.</summary>
    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(
            nameof(ViewModel),
            typeof(DocumentListViewModel),
            typeof(DocumentItemView),
            new PropertyMetadata(null, static (d, _) => ((DocumentItemView)d).OnViewModelChanged()));

    /// <summary>Si es <c>true</c>, muestra Restaurar/Eliminar en lugar de Papelera (vista Papelera).</summary>
    public bool ShowRestore
    {
        get => (bool)GetValue(ShowRestoreProperty);
        set => SetValue(ShowRestoreProperty, value);
    }

    /// <summary>DP de modo papelera.</summary>
    public static readonly DependencyProperty ShowRestoreProperty =
        DependencyProperty.Register(
            nameof(ShowRestore),
            typeof(bool),
            typeof(DocumentItemView),
            new PropertyMetadata(false, static (d, _) => ((DocumentItemView)d).OnDocumentChanged()));

    /// <summary>Crea la vista.</summary>
    public DocumentItemView()
    {
        InitializeComponent();
    }

    /// <summary>Visibilidad del botón Papelera (oculto en modo restauración).</summary>
    public Visibility TrashButtonVisibility => ShowRestore ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Visibilidad de Restaurar/Eliminar (solo en papelera).</summary>
    public Visibility RestoreButtonVisibility => ShowRestore ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Miniatura (si existe; tapa al icono).</summary>
    public BitmapImage? ThumbnailImage { get; private set; }

    /// <summary>Nombre o cadena vacía si aún no hay documento (evita NRE en x:Bind con reciclaje).</summary>
    public string Title => Document?.Name ?? string.Empty;

    /// <summary>Subtítulo: tipo • tamaño • fecha.</summary>
    public string Subtitle => DocumentFormat.Subtitle(Document);

    private void OnViewModelChanged()
    {
        // x:Bind a ViewModel.* no se refresca solo con el DP: avisar explícitamente.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ViewModel)));
    }

    private void OnDocumentChanged()
    {
        // Glifos por código (determinista ante reciclaje, sin depender del binding).
        if (TypeIcon is not null)
        {
            TypeIcon.Glyph = DocumentFormat.Glyph(Document?.FileType ?? DocumentType.Unknown);
        }

        if (FavIcon is not null)
        {
            FavIcon.Glyph = Document?.IsFavorite == true ? "\uE734" : "\uE735";
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Title)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Subtitle)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TrashButtonVisibility)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RestoreButtonVisibility)));
        LoadThumbnail();
    }

    private void LoadThumbnail()
    {
        ThumbnailImage = null;
        try
        {
            if (Document is not null)
            {
                var path = App.Services.GetService<IThumbnailService>()?.TryGetExistingPath(Document.Id);
                if (path is not null)
                {
                    ThumbnailImage = new BitmapImage
                    {
                        UriSource = new Uri(path, UriKind.Absolute),
                        DecodePixelWidth = 80,
                        DecodePixelType = DecodePixelType.Logical,
                    };
                }
            }
        }
        catch
        {
            ThumbnailImage = null;
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ThumbnailImage)));
    }
}
