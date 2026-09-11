namespace CaCo.Core;

/// <summary>
/// Abstracción del reloj del sistema. Permite congelar/controlar el tiempo en pruebas
/// y evita <see cref="DateTimeOffset.UtcNow"/> disperso por el código.
/// </summary>
public interface IClock
{
    /// <summary>Momento actual en UTC.</summary>
    DateTimeOffset UtcNow { get; }
}

/// <summary>Implementación de <see cref="IClock"/> basada en el reloj del sistema.</summary>
public sealed class SystemClock : IClock
{
    /// <summary>Instancia compartida.</summary>
    public static readonly SystemClock Instance = new();

    private SystemClock()
    {
    }

    /// <inheritdoc/>
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
