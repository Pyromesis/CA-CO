using CaCo.Domain;

namespace CaCo.Tests.Domain;

/// <summary>Pruebas de la entidad Documento.</summary>
public sealed class DocumentTests
{
    [Fact]
    public void Create_ValidData_Succeeds()
    {
        var result = Document.Create("Apuntes", "apuntes.pdf", 1024);

        Assert.True(result.IsSuccess);
        Assert.Equal("Apuntes", result.Value.Name);
        Assert.Equal("apuntes.pdf", result.Value.OriginalFileName);
        Assert.Equal(DocumentType.Pdf, result.Value.FileType);
        Assert.Equal(1024, result.Value.SizeBytes);
        Assert.False(result.Value.IsFavorite);
        Assert.False(result.Value.IsDeleted);
        Assert.NotEqual(Guid.Empty, result.Value.Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_EmptyName_Fails(string name)
    {
        var result = Document.Create(name, "a.pdf", 10);

        Assert.True(result.IsFailure);
        Assert.Equal("Document.InvalidName", result.Error.Code);
    }

    [Fact]
    public void Create_UnsupportedExtension_Fails()
    {
        var result = Document.Create("Binario", "app.exe", 10);

        Assert.True(result.IsFailure);
        Assert.Equal("Document.UnsupportedType", result.Error.Code);
    }

    [Fact]
    public void FavoriteTrashRestore_Flow_Works()
    {
        var doc = Document.Create("Foto", "foto.png", 2048).Value;

        doc.SetFavorite(true);
        Assert.True(doc.IsFavorite);

        Assert.True(doc.MoveToTrash().IsSuccess);
        Assert.True(doc.IsDeleted);
        Assert.NotNull(doc.DeletedAt);

        // Doble papelera falla.
        Assert.True(doc.MoveToTrash().IsFailure);

        Assert.True(doc.Restore().IsSuccess);
        Assert.False(doc.IsDeleted);
        Assert.Null(doc.DeletedAt);
    }

    [Fact]
    public void Rename_ValidName_UpdatesAndTouchesModified()
    {
        var before = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var after = before.AddHours(1);
        var doc = Document.Create("Viejo", "v.pdf", 10, now: before).Value;

        Assert.True(doc.Rename("Nuevo", after).IsSuccess);
        Assert.Equal("Nuevo", doc.Name);
        Assert.Equal(after, doc.ModifiedAt);
    }

    [Fact]
    public void MoveToNotebook_AssignsAndClears()
    {
        var doc = Document.Create("Doc", "d.txt", 5).Value;
        var notebookId = Guid.NewGuid();

        doc.MoveToNotebook(notebookId);
        Assert.Equal(notebookId, doc.NotebookId);

        doc.MoveToNotebook(null);
        Assert.Null(doc.NotebookId);
    }

    [Fact]
    public void Tags_AddIsIdempotent_RemoveWorks()
    {
        var doc = Document.Create("Doc", "d.txt", 5).Value;
        var tag = Guid.NewGuid();

        doc.AddTag(tag);
        doc.AddTag(tag);
        Assert.Single(doc.TagIds);

        doc.RemoveTag(tag);
        Assert.Empty(doc.TagIds);
    }
}
