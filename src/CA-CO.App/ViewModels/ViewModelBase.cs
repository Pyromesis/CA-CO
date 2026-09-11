using CaCo.Application.Errors;
using CaCo.Core;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CaCo.App.ViewModels;

/// <summary>
/// Base de todos los ViewModels: estado ocupado + mensajes para <c>InfoBar</c>.
/// Los errores técnicos se traducen con <see cref="IErrorHandler"/>; la vista
/// nunca ve excepciones ni stack traces.
/// </summary>
public abstract partial class ViewModelBase : ObservableObject
{
    private readonly IErrorHandler _errors;

    /// <summary>Crea la base con el manejador de errores.</summary>
    protected ViewModelBase(IErrorHandler errors)
    {
        _errors = errors;
    }

    /// <summary>Operación en curso (spinners, botones deshabilitados).</summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Mensaje de error visible (InfoBar). Nulo = oculto.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    /// <summary>Mensaje informativo/de éxito (InfoBar). Nulo = oculto.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInfo))]
    private string? _infoMessage;

    /// <summary>Indica si hay error visible (para <c>InfoBar.IsOpen</c>).</summary>
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    /// <summary>Indica si hay mensaje informativo visible.</summary>
    public bool HasInfo => !string.IsNullOrEmpty(InfoMessage);

    /// <summary>Llamado al mostrar la página: las páginas lo invocan desde <c>OnNavigatedTo</c>.</summary>
    public virtual Task OnNavigatedToAsync(CancellationToken ct) => Task.CompletedTask;

    /// <summary>Llamado al salir de la página (liberar voz, cancelar, etc.).</summary>
    public virtual Task OnNavigatedFromAsync(CancellationToken ct) => Task.CompletedTask;

    /// <summary>Recibe el parámetro de navegación (p. ej. id). Por defecto se ignora.</summary>
    public virtual void ReceiveParameter(object? parameter)
    {
    }

    /// <summary>Muestra un error de dominio.</summary>
    protected void ShowError(Error error) => ShowUserError(_errors.FromError(error));

    /// <summary>Muestra una excepción inesperada (ya quedó en el log).</summary>
    protected void ShowError(Exception exception) => ShowUserError(_errors.FromException(exception));

    /// <summary>Muestra un mensaje informativo.</summary>
    protected void ShowInfo(string message)
    {
        ErrorMessage = null;
        InfoMessage = message;
    }

    /// <summary>Limpia los mensajes.</summary>
    protected void ClearMessages()
    {
        ErrorMessage = null;
        InfoMessage = null;
    }

    private void ShowUserError(UserFacingError error)
    {
        InfoMessage = null;
        ErrorMessage = error.Severity == ErrorSeverity.Info ? null : error.Message;
        if (error.Severity == ErrorSeverity.Info)
        {
            InfoMessage = $"{error.Title}: {error.Message}";
        }
    }
}
