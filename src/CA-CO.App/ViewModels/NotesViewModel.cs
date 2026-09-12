using CaCo.App.Services;
using CaCo.Application.Errors;
using CaCo.Application.Services;
using CaCo.Domain;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace CaCo.App.ViewModels;

/// <summary>ViewModel de notas sueltas: lista, crear, renombrar, editar y eliminar (Fase 4).</summary>
public sealed partial class NotesViewModel : ViewModelBase
{
    private readonly INoteService _notes;
    private readonly IDialogService _dialogs;

    /// <summary>Crea el ViewModel.</summary>
    public NotesViewModel(IErrorHandler errors, INoteService notes, IDialogService dialogs)
        : base(errors)
    {
        _notes = notes;
        _dialogs = dialogs;
    }

    /// <summary>Notas sueltas.</summary>
    public ObservableCollection<Note> Items { get; } = [];

    private readonly List<Note> _all = [];

    /// <summary>Filtro de búsqueda (título y contenido).</summary>
    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>Indica si no hay notas.</summary>
    [ObservableProperty]
    private bool _isEmpty = true;

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var text = SearchText?.Trim() ?? string.Empty;
        Items.Clear();
        foreach (var note in _all.Where(n =>
                     text.Length == 0
                     || n.Title.Contains(text, StringComparison.OrdinalIgnoreCase)
                     || n.Content.Contains(text, StringComparison.OrdinalIgnoreCase)))
        {
            Items.Add(note);
        }

        IsEmpty = Items.Count == 0;
    }

    /// <inheritdoc/>
    public override async Task OnNavigatedToAsync(CancellationToken ct)
    {
        await RefreshAsync(ct);
    }

    /// <summary>Recarga las notas sueltas.</summary>
    [RelayCommand]
    private async Task RefreshAsync(CancellationToken ct)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        ClearMessages();
        try
        {
            var result = await _notes.ListStandaloneAsync(100, ct);
            if (result.IsFailure)
            {
                ShowError(result.Error);
                return;
            }

            _all.Clear();
            _all.AddRange(result.Value);
            ApplyFilter();
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

    /// <summary>Crea una nota suelta (título + contenido markdown).</summary>
    [RelayCommand]
    private async Task CreateAsync(CancellationToken ct)
    {
        try
        {
            var title = await _dialogs.PromptTextAsync("Nueva nota", "Título");
            if (title is null)
            {
                return;
            }

            var content = await _dialogs.PromptMultilineTextAsync(
                "Nueva nota", "Contenido (markdown: **negrita**, *cursiva*, `código`)");
            if (content is null)
            {
                return;
            }

            var created = await _notes.AddStandaloneAsync(title, content, ct);
            if (created.IsFailure)
            {
                ShowError(created.Error);
                return;
            }

            await RefreshAsync(ct);
            ShowInfo("Nota creada.");
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    /// <summary>Renombra una nota.</summary>
    [RelayCommand]
    private async Task RenameAsync(Note? note, CancellationToken ct)
    {
        if (note is null)
        {
            return;
        }

        try
        {
            var title = await _dialogs.PromptTextAsync("Renombrar nota", "Título", note.Title);
            if (title is null)
            {
                return;
            }

            var renamed = await _notes.RenameAsync(note.Id, title, ct);
            if (renamed.IsFailure)
            {
                ShowError(renamed.Error);
                return;
            }

            await RefreshAsync(ct);
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    /// <summary>Edita el contenido de una nota.</summary>
    [RelayCommand]
    private async Task EditAsync(Note? note, CancellationToken ct)
    {
        if (note is null)
        {
            return;
        }

        try
        {
            var content = await _dialogs.PromptMultilineTextAsync("Editar nota", "Contenido", note.Content);
            if (content is null)
            {
                return;
            }

            var updated = await _notes.UpdateAsync(note.Id, content, ct);
            if (updated.IsFailure)
            {
                ShowError(updated.Error);
                return;
            }

            await RefreshAsync(ct);
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    /// <summary>Elimina definitivamente una nota.</summary>
    [RelayCommand]
    private async Task DeleteAsync(Note? note, CancellationToken ct)
    {
        if (note is null)
        {
            return;
        }

        try
        {
            var confirmed = await _dialogs.ConfirmAsync(
                "Eliminar nota", $"¿Estás seguro de que quieres eliminar «{note.Title}»?", "Eliminar");
            if (!confirmed)
            {
                return;
            }

            confirmed = await _dialogs.ConfirmAsync(
                "Eliminar nota", $"«{note.Title}» se eliminará de forma permanente.", "Eliminar definitivamente");
            if (!confirmed)
            {
                return;
            }

            var deleted = await _notes.DeleteAsync(note.Id, ct);
            if (deleted.IsFailure)
            {
                ShowError(deleted.Error);
                return;
            }

            await RefreshAsync(ct);
            ShowInfo("Nota eliminada.");
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }
}
