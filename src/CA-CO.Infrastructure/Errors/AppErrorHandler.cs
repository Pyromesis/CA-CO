using CaCo.Application.Errors;
using CaCo.Core;
using Microsoft.Extensions.Logging;

namespace CaCo.Infrastructure.Errors;

/// <summary>
/// Traductor centralizado de errores técnicos a mensajes entendibles.
/// Los stack traces van al log; al usuario solo llega <see cref="UserFacingError"/>.
/// </summary>
public sealed class AppErrorHandler(ILogger<AppErrorHandler> logger) : IErrorHandler
{
    /// <inheritdoc/>
    public UserFacingError FromException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        logger.LogError(exception, "Error inesperado: {Message}", exception.Message);

        return exception switch
        {
            OperationCanceledException => new UserFacingError
            {
                Title = "Operación cancelada",
                Message = "La operación se canceló antes de terminar.",
                Severity = ErrorSeverity.Info,
            },
            UnauthorizedAccessException => new UserFacingError
            {
                Title = "Sin permiso",
                Message = "CA-CO no tiene permiso para acceder a ese archivo o carpeta. Revisa los permisos e inténtalo de nuevo.",
            },
            FileNotFoundException => new UserFacingError
            {
                Title = "Archivo no encontrado",
                Message = "El archivo ya no está donde se esperaba. Puede que se haya movido o eliminado fuera de CA-CO.",
            },
            DirectoryNotFoundException => new UserFacingError
            {
                Title = "Carpeta no encontrada",
                Message = "La carpeta de la biblioteca no está disponible. Comprueba que el disco siga conectado.",
            },
            IOException => new UserFacingError
            {
                Title = "Error de almacenamiento",
                Message = "No se pudo completar la operación de disco. Comprueba el espacio libre e inténtalo de nuevo.",
            },
            _ => new UserFacingError
            {
                Title = "Algo salió mal",
                Message = "Ocurrió un error inesperado. El detalle quedó registrado para su revisión.",
            },
        };
    }

    /// <inheritdoc/>
    public UserFacingError FromError(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        var code = error.Code;

        if (code.EndsWith(".NotFound", StringComparison.Ordinal) || code.EndsWith("NotFound", StringComparison.Ordinal))
        {
            return new UserFacingError
            {
                Title = "No encontrado",
                Message = "El elemento ya no existe. Actualiza la vista e inténtalo de nuevo.",
            };
        }

        if (code is "Import.Duplicate")
        {
            return new UserFacingError
            {
                Title = "Ya está en tu biblioteca",
                Message = "Ese documento ya había sido importado antes.",
                Severity = ErrorSeverity.Info,
            };
        }

        if (code is "Document.UnsupportedType")
        {
            return new UserFacingError
            {
                Title = "Tipo no soportado",
                Message = "CA-CO trabaja con PDF, PNG, JPG, JPEG, DOCX, XLSX y TXT.",
            };
        }

        if (code is "Language.Cancelled")
        {
            return new UserFacingError
            {
                Title = "Instalación cancelada",
                Message = "La instalación de voz y OCR se canceló. Puedes reintentarlo cuando quieras.",
                Severity = ErrorSeverity.Info,
            };
        }

        if (code is "Notebook.Cycle" or "Notebook.SelfParent")
        {
            return new UserFacingError
            {
                Title = "Movimiento no válido",
                Message = "Un cuaderno no puede moverse dentro de sí mismo ni de sus descendientes.",
            };
        }

        return new UserFacingError
        {
            Title = "No se pudo completar",
            Message = "No se pudo completar la operación. El detalle quedó registrado para su revisión.",
        };
    }
}
