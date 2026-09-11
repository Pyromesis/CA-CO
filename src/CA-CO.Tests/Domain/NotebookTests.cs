using CaCo.Domain;

namespace CaCo.Tests.Domain;

/// <summary>Pruebas de la entidad Cuaderno y su jerarquía.</summary>
public sealed class NotebookTests
{
    [Fact]
    public void Create_Root_HasNoParent()
    {
        var result = Notebook.Create("Universidad");

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsRoot);
        Assert.Null(result.Value.ParentId);
    }

    [Fact]
    public void Create_Child_KeepsParent()
    {
        var parent = Notebook.Create("Universidad").Value;

        var child = Notebook.Create("Programación", parent.Id);

        Assert.True(child.IsSuccess);
        Assert.Equal(parent.Id, child.Value.ParentId);
        Assert.False(child.Value.IsRoot);
    }

    [Fact]
    public void Create_SelfParent_Fails()
    {
        var id = Guid.NewGuid();

        var result = Notebook.Create("Raro", id, id: id);

        Assert.True(result.IsFailure);
        Assert.Equal("Notebook.SelfParent", result.Error.Code);
    }

    [Fact]
    public void MoveTo_Self_Fails()
    {
        var notebook = Notebook.Create("A").Value;

        var result = notebook.MoveTo(notebook.Id);

        Assert.True(result.IsFailure);
        Assert.Equal("Notebook.SelfParent", result.Error.Code);
    }

    [Fact]
    public void MoveTo_OtherParent_Succeeds()
    {
        var a = Notebook.Create("A").Value;
        var b = Notebook.Create("B").Value;

        Assert.True(a.MoveTo(b.Id).IsSuccess);
        Assert.Equal(b.Id, a.ParentId);
    }

    [Fact]
    public void Rename_Empty_Fails()
    {
        var notebook = Notebook.Create("A").Value;

        Assert.True(notebook.Rename("  ").IsFailure);
    }
}
