using CaCo.Domain;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CaCo.App.Pages;

/// <summary>Página de notas sueltas (Fase 4). Solo enlaza UI con el ViewModel.</summary>
public sealed partial class NotesPage : NotesPageBase
{
    /// <summary>Crea la página.</summary>
    public NotesPage()
    {
        InitializeComponent();
    }

    private static Note? NoteOf(object sender) =>
        (sender as Button)?.DataContext as Note;

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (NoteOf(sender) is Note note)
        {
            ViewModel.EditCommand.Execute(note);
        }
    }

    private void Rename_Click(object sender, RoutedEventArgs e)
    {
        if (NoteOf(sender) is Note note)
        {
            ViewModel.RenameCommand.Execute(note);
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (NoteOf(sender) is Note note)
        {
            ViewModel.DeleteCommand.Execute(note);
        }
    }
}
