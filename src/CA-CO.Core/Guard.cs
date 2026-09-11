namespace CaCo.Core;

/// <summary>
/// Guardas de validación para fallar rápido con mensajes claros en los límites del dominio.
/// Solo para errores de programación (argumentos nulos, cadenas vacías); las reglas de
/// negocio devuelven <see cref="Result"/> en lugar de lanzar.
/// </summary>
public static class Guard
{
    /// <summary>Lanza si el valor es nulo, vacío o solo espacios.</summary>
    public static string NotNullOrWhiteSpace(string? value, string paramName, int maxLength = int.MaxValue)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("No puede estar vacío.", paramName);
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentException($"No puede superar los {maxLength} caracteres.", paramName);
        }

        return trimmed;
    }

    /// <summary>Lanza si el valor es nulo.</summary>
    public static T NotNull<T>(T? value, string paramName)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(value, paramName);
        return value;
    }

    /// <summary>Lanza si el número es negativo.</summary>
    public static long NotNegative(long value, string paramName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(paramName, "No puede ser negativo.");
        }

        return value;
    }
}
