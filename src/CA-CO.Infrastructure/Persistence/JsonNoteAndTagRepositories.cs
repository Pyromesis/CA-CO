using CaCo.Application.Repositories;
using CaCo.Application.Storage;
using CaCo.Core;
using CaCo.Domain;

namespace CaCo.Infrastructure.Persistence;

/// <summary>Repositorio de notas sobre <c>Database/notes.json</c> (Fase 0).</summary>
public sealed class JsonNoteRepository : INoteRepository
{
    private readonly JsonFileStore<NoteDto> _store;

    /// <summary>Crea el repositorio.</summary>
    public JsonNoteRepository(ILibraryPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _store = new JsonFileStore<NoteDto>(paths.NotesDb, CacoJsonContext.Indented.ListNoteDto);
    }

    /// <inheritdoc/>
    public async Task<Result<Note?>> GetByIdAsync(Guid id, bool includeDeleted, CancellationToken ct)
    {
        try
        {
            var items = await _store.LoadAsync(ct).ConfigureAwait(false);
            var dto = items.FirstOrDefault(n => n.Id == id && (includeDeleted || !n.IsDeleted));
            return Result.Success(dto?.ToEntity());
        }
        catch (Exception ex)
        {
            return Result.Failure<Note?>(Error.Storage("NoteRepository.ReadFailed", $"No se pudo leer la nota: {ex.Message}"));
        }
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<Note>>> ListByDocumentAsync(Guid documentId, CancellationToken ct)
    {
        try
        {
            var items = await _store.LoadAsync(ct).ConfigureAwait(false);
            IReadOnlyList<Note> result = items
                .Where(n => !n.IsDeleted && n.DocumentId == documentId)
                .OrderByDescending(n => n.ModifiedAt)
                .Select(n => n.ToEntity())
                .ToList();
            return Result.Success(result);
        }
        catch (Exception ex)
        {
            return Result.Failure<IReadOnlyList<Note>>(Error.Storage("NoteRepository.ListFailed", $"No se pudieron listar notas: {ex.Message}"));
        }
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<Note>>> ListByNotebookAsync(Guid notebookId, CancellationToken ct)
    {
        try
        {
            var items = await _store.LoadAsync(ct).ConfigureAwait(false);
            IReadOnlyList<Note> result = items
                .Where(n => !n.IsDeleted && n.NotebookId == notebookId)
                .OrderByDescending(n => n.ModifiedAt)
                .Select(n => n.ToEntity())
                .ToList();
            return Result.Success(result);
        }
        catch (Exception ex)
        {
            return Result.Failure<IReadOnlyList<Note>>(Error.Storage("NoteRepository.ListFailed", $"No se pudieron listar notas: {ex.Message}"));
        }
    }

    /// <inheritdoc/>
    public async Task<Result> AddAsync(Note note, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(note);
        try
        {
            var items = await _store.LoadAsync(ct).ConfigureAwait(false);
            if (items.Any(n => n.Id == note.Id))
            {
                return Result.Failure(Error.Conflict("NoteRepository.Duplicate", "Ya existe una nota con ese identificador."));
            }

            items.Add(NoteDto.FromEntity(note));
            await _store.SaveAsync(items, ct).ConfigureAwait(false);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(Error.Storage("NoteRepository.AddFailed", $"No se pudo guardar la nota: {ex.Message}"));
        }
    }

    /// <inheritdoc/>
    public async Task<Result> UpdateAsync(Note note, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(note);
        try
        {
            var items = await _store.LoadAsync(ct).ConfigureAwait(false);
            var index = items.FindIndex(n => n.Id == note.Id);
            if (index < 0)
            {
                return Result.Failure(DomainErrors.Note.NotFound(note.Id));
            }

            items[index] = NoteDto.FromEntity(note);
            await _store.SaveAsync(items, ct).ConfigureAwait(false);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(Error.Storage("NoteRepository.UpdateFailed", $"No se pudo actualizar la nota: {ex.Message}"));
        }
    }

    /// <inheritdoc/>
    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct)
    {
        try
        {
            var items = await _store.LoadAsync(ct).ConfigureAwait(false);
            var removed = items.RemoveAll(n => n.Id == id);
            if (removed == 0)
            {
                return Result.Failure(DomainErrors.Note.NotFound(id));
            }

            await _store.SaveAsync(items, ct).ConfigureAwait(false);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(Error.Storage("NoteRepository.DeleteFailed", $"No se pudo eliminar la nota: {ex.Message}"));
        }
    }
}

/// <summary>Repositorio de etiquetas sobre <c>Database/tags.json</c> (Fase 0).</summary>
public sealed class JsonTagRepository : ITagRepository
{
    private readonly JsonFileStore<TagDto> _store;

    /// <summary>Crea el repositorio.</summary>
    public JsonTagRepository(ILibraryPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _store = new JsonFileStore<TagDto>(paths.TagsDb, CacoJsonContext.Indented.ListTagDto);
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<Tag>>> ListAllAsync(CancellationToken ct)
    {
        try
        {
            var items = await _store.LoadAsync(ct).ConfigureAwait(false);
            IReadOnlyList<Tag> result = items
                .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .Select(t => t.ToEntity())
                .ToList();
            return Result.Success(result);
        }
        catch (Exception ex)
        {
            return Result.Failure<IReadOnlyList<Tag>>(Error.Storage("TagRepository.ListFailed", $"No se pudieron listar etiquetas: {ex.Message}"));
        }
    }

    /// <inheritdoc/>
    public async Task<Result<Tag?>> GetByNameAsync(string name, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure<Tag?>(DomainErrors.Tag.InvalidName());
        }

        try
        {
            var normalized = name.Trim().ToLowerInvariant();
            var items = await _store.LoadAsync(ct).ConfigureAwait(false);
            var dto = items.FirstOrDefault(t =>
                string.Equals(t.Name, normalized, StringComparison.OrdinalIgnoreCase));
            return Result.Success(dto?.ToEntity());
        }
        catch (Exception ex)
        {
            return Result.Failure<Tag?>(Error.Storage("TagRepository.ReadFailed", $"No se pudo leer la etiqueta: {ex.Message}"));
        }
    }

    /// <inheritdoc/>
    public async Task<Result> AddAsync(Tag tag, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tag);
        try
        {
            var items = await _store.LoadAsync(ct).ConfigureAwait(false);
            if (items.Any(t => t.Id == tag.Id))
            {
                return Result.Failure(Error.Conflict("TagRepository.Duplicate", "Ya existe una etiqueta con ese identificador."));
            }

            items.Add(TagDto.FromEntity(tag));
            await _store.SaveAsync(items, ct).ConfigureAwait(false);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(Error.Storage("TagRepository.AddFailed", $"No se pudo guardar la etiqueta: {ex.Message}"));
        }
    }
}
