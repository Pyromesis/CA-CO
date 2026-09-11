using System.ComponentModel;
using CaCo.Domain;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CaCo.App.Views;

/// <summary>Fila de solo lectura para documentos recientes (página de inicio).</summary>
public sealed partial class RecentDocumentView : UserControl, INotifyPropertyChanged
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
            typeof(RecentDocumentView),
            new PropertyMetadata(null, static (d, _) => ((RecentDocumentView)d).OnDocumentChanged()));

    /// <summary>Crea la vista.</summary>
    public RecentDocumentView()
    {
        InitializeComponent();
    }

    /// <summary>Nombre o marcador si aún no hay documento.</summary>
    public string Title => Document?.Name ?? string.Empty;

    /// <summary>Subtítulo con tipo, tamaño y fecha.</summary>
    public string Subtitle => DocumentFormat.Subtitle(Document);

    /// <summary>Glifo según tipo.</summary>
    public string Glyph => DocumentFormat.Glyph(Document?.FileType ?? DocumentType.Unknown);

    private void OnDocumentChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Title)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Subtitle)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Glyph)));
    }
}
