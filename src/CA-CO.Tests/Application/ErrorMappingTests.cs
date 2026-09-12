using CaCo.Application.Errors;
using CaCo.Core;
using CaCo.Infrastructure.Errors;
using Microsoft.Extensions.Logging.Abstractions;

namespace CaCo.Tests.Application;

/// <summary>El manejador nunca filtra detalles técnicos y marca lo informativo.</summary>
public sealed class ErrorMappingTests
{
    private static AppErrorHandler Create() =>
        new(NullLogger<AppErrorHandler>.Instance);

    [Fact]
    public void LanguageCancelled_IsInfo()
    {
        var handler = Create();
        var shown = handler.FromError(Error.Validation("Language.Cancelled", "x"));
        Assert.Equal(ErrorSeverity.Info, shown.Severity);
    }

    [Fact]
    public void UnknownError_HidesTechnicalMessage()
    {
        var handler = Create();
        var shown = handler.FromError(Error.Storage("Db.Secret", @"C:\Users\pepe\clave"));
        Assert.NotEqual(ErrorSeverity.Info, shown.Severity);
        Assert.DoesNotContain(@"C:\Users\pepe", shown.Message);
        Assert.DoesNotContain("clave", shown.Message);
    }

    [Fact]
    public void FromException_OperationCanceled_IsInfo()
    {
        var handler = Create();
        var shown = handler.FromException(new OperationCanceledException());
        Assert.Equal(ErrorSeverity.Info, shown.Severity);
    }
}
