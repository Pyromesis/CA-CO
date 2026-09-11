using CaCo.Domain;

namespace CaCo.Tests.Domain;

/// <summary>Pruebas de tipos de archivo soportados.</summary>
public sealed class SupportedFileTypesTests
{
    [Theory]
    [InlineData(".pdf", DocumentType.Pdf)]
    [InlineData(".PDF", DocumentType.Pdf)]
    [InlineData(".png", DocumentType.Png)]
    [InlineData(".jpg", DocumentType.Jpg)]
    [InlineData(".jpeg", DocumentType.Jpeg)]
    [InlineData(".docx", DocumentType.Docx)]
    [InlineData(".xlsx", DocumentType.Xlsx)]
    [InlineData(".txt", DocumentType.Txt)]
    public void GetTypeOrUnknown_Supported_ReturnsType(string extension, DocumentType expected)
    {
        Assert.Equal(expected, SupportedFileTypes.GetTypeOrUnknown(extension));
        Assert.True(SupportedFileTypes.IsSupported(extension));
    }

    [Theory]
    [InlineData(".exe")]
    [InlineData(".zip")]
    [InlineData(".mp3")]
    [InlineData("")]
    [InlineData(null)]
    public void IsSupported_Unsupported_ReturnsFalse(string? extension)
    {
        Assert.False(SupportedFileTypes.IsSupported(extension));
        Assert.Equal(DocumentType.Unknown, SupportedFileTypes.GetTypeOrUnknown(extension));
    }

    [Fact]
    public void TryGetType_FullFileName_DetectsExtension()
    {
        Assert.True(SupportedFileTypes.TryGetType("mis apuntes.PDF", out var type));
        Assert.Equal(DocumentType.Pdf, type);
    }

    [Fact]
    public void AllExtensions_ContainsExpectedSet()
    {
        var all = SupportedFileTypes.AllExtensions;
        Assert.Equal(7, all.Count);
        Assert.Contains(".pdf", all);
        Assert.Contains(".txt", all);
    }
}
