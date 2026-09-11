using CaCo.Core;

namespace CaCo.Application.Errors;

/// <summary>Gravedad de un error de cara al usuario.</summary>
public enum ErrorSeverity
{
    /// <summary>Informativo (p. ej. duplicado omitido).</summary>
    Info = 0,

    /// <summary>Advertencia recuperable.</summary>
    Warning = 1,

    /// <summary>Error que impidió la operación.</summary>
    Error = 2,
}

/// <summary>Error listo para mostrar: sin tecnicismos ni stack traces.</summary>
public sealed record UserFacingError
{
    /// <summary>Título corto.</summary>
    public required string Title { get; init; }

    /// <summary>Explicación entendible y, si aplica, qué puede hacer el usuario.</summary>
    public required string Message { get; init; }

    /// <summary>Gravedad.</summary>
    public ErrorSeverity Severity { get; init; } = ErrorSeverity.Error;
}

/// <summary>
/// Punto centralizado de manejo de errores:
/// <c>Excepción técnica → logging → mensaje entendible</c>.
/// Los <see cref="Error"/> de dominio ya son estables; este servicio cubre
/// excepciones inesperadas (E/S, permisos, etc.).
/// </summary>
public interface IErrorHandler
{
    /// <summary>Traduce una excepción a un error mostrable.</summary>
    UserFacingError FromException(Exception exception);

    /// <summary>Traduce un <see cref="Error"/> de dominio a un error mostrable.</summary>
    UserFacingError FromError(Error error);
}
