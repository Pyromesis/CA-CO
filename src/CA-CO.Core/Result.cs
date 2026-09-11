namespace CaCo.Core;

/// <summary>
/// Resultado de una operación que puede fallar sin lanzar excepciones.
/// Patrón railway: los casos de uso devuelven <see cref="Result{T}"/> y la UI
/// traduce el <see cref="Error"/> a un mensaje entendible mediante <c>IErrorHandler</c>.
/// </summary>
public class Result
{
    /// <summary>Indica si la operación tuvo éxito.</summary>
    public bool IsSuccess { get; }

    /// <summary>Indica si la operación falló.</summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>Error producido, o <see cref="Error.None"/> si hubo éxito.</summary>
    public Error Error { get; }

    /// <summary>Construye un resultado. Usar <see cref="Success"/> / <see cref="Failure(Error)"/>.</summary>
    protected Result(bool isSuccess, Error error)
    {
        IsSuccess = isSuccess;
        Error = error;
    }

    /// <summary>Resultado exitoso.</summary>
    public static Result Success() => new(true, Error.None);

    /// <summary>Resultado exitoso con valor.</summary>
    public static Result<T> Success<T>(T value) => new(value, true, Error.None);

    /// <summary>Resultado fallido.</summary>
    public static Result Failure(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new(false, error);
    }

    /// <summary>Resultado fallido con valor tipado.</summary>
    public static Result<T> Failure<T>(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new(default, false, error);
    }
}

/// <summary>Resultado de una operación que devuelve un valor.</summary>
/// <typeparam name="T">Tipo del valor devuelto en caso de éxito.</typeparam>
public sealed class Result<T> : Result
{
    private readonly T? _value;

    /// <summary>Valor devuelto. Solo válido cuando <see cref="Result.IsSuccess"/> es <c>true</c>.</summary>
    /// <exception cref="InvalidOperationException">Si se accede tras un fallo.</exception>
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("No se puede obtener el valor de un resultado fallido.");

    /// <summary>Construye un resultado con valor.</summary>
    internal Result(T? value, bool isSuccess, Error error)
        : base(isSuccess, error)
    {
        _value = value;
    }

    /// <summary>Conversión implícita desde un valor (éxito).</summary>
    public static implicit operator Result<T>(T value) => Success(value);
}
