using System.ComponentModel;
using CaCo.Domain;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CaCo.App.Views;

/// <summary>Fila de solo lectura para un comentario.</summary>
public sealed partial class CommentView : UserControl, INotifyPropertyChanged
{
    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Nota a mostrar.</summary>
    public Note? Note
    {
        get => (Note?)GetValue(NoteProperty);
        set => SetValue(NoteProperty, value);
    }

    /// <summary>DP de la nota.</summary>
    public static readonly DependencyProperty NoteProperty =
        DependencyProperty.Register(
            nameof(Note),
            typeof(Note),
            typeof(CommentView),
            new PropertyMetadata(null, static (d, _) => ((CommentView)d).OnNoteChanged()));

    /// <summary>Crea la vista.</summary>
    public CommentView()
    {
        InitializeComponent();
    }

    /// <summary>Contenido.</summary>
    public string Body => Note?.Content ?? string.Empty;

    /// <summary>Fecha legible.</summary>
    public string DateText => Note is null
        ? string.Empty
        : Note.ModifiedAt.LocalDateTime.ToString("dd/MM/yyyy HH:mm");

    private void OnNoteChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Body)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DateText)));
    }
}
