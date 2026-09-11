using CaCo.Core;

namespace CaCo.Tests.Core;

/// <summary>Pruebas del tipo Result.</summary>
public sealed class ResultTests
{
    [Fact]
    public void Success_IsSuccess()
    {
        var result = Result.Success();

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal(Error.None, result.Error);
    }

    [Fact]
    public void Success_WithValue_ExposesValue()
    {
        var result = Result.Success(42);

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void Failure_Value_Throws()
    {
        var result = Result.Failure<int>(Error.Validation("X", "mal"));

        Assert.True(result.IsFailure);
        Assert.Throws<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void ImplicitConversion_FromValue_Succeeds()
    {
        Result<string> result = "hola";

        Assert.True(result.IsSuccess);
        Assert.Equal("hola", result.Value);
    }
}
