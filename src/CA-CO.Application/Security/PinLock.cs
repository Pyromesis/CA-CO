using System.Security.Cryptography;
using CaCo.Application.Configuration;
using CaCo.Core;

namespace CaCo.Application.Security;

/// <summary>Credenciales PIN con PBKDF2-SHA256 (Fase 8, sin dependencias).</summary>
public static class PinCredentials
{
    /// <summary>Iteraciones de derivación.</summary>
    public const int Iterations = 210_000;

    /// <summary>Validación del formato: 4-64 caracteres.</summary>
    public static bool IsValidFormat(string? pin) =>
        !string.IsNullOrEmpty(pin) && pin.Length is >= 4 and <= 64;

    /// <summary>Genera hash y sal para guardar.</summary>
    public static (string Hash, string Salt) Hash(string pin)
    {
        ArgumentException.ThrowIfNullOrEmpty(pin);
        var salt = RandomNumberGenerator.GetBytes(16);
        var subkey = Rfc2898DeriveBytes.Pbkdf2(pin, salt, Iterations, HashAlgorithmName.SHA256, 32);
        return (Convert.ToBase64String(subkey), Convert.ToBase64String(salt));
    }

    /// <summary>Verifica un PIN contra hash y sal (tiempo constante).</summary>
    public static bool Verify(string? pin, string hash, string salt)
    {
        if (string.IsNullOrEmpty(pin) || string.IsNullOrEmpty(hash) || string.IsNullOrEmpty(salt))
        {
            return false;
        }

        try
        {
            var expected = Convert.FromBase64String(hash);
            var saltBytes = Convert.FromBase64String(salt);
            var actual = Rfc2898DeriveBytes.Pbkdf2(pin, saltBytes, Iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch (Exception)
        {
            return false;
        }
    }
}

/// <summary>Bloqueo con PIN y periodos de espera (Fase 8).</summary>
public interface IPinLockService
{
    /// <summary>Si hay PIN configurado.</summary>
    bool IsPinSet { get; }

    /// <summary>Configura un PIN nuevo.</summary>
    Result SetPin(string pin);

    /// <summary>Verifica el PIN (aplica espera tras fallos).</summary>
    Result VerifyPin(string? pin);

    /// <summary>Quita el PIN (requiere verificarlo antes desde la UI).</summary>
    void RemovePin();

    /// <summary>Si está en periodo de espera.</summary>
    bool IsLockedOut { get; }

    /// <summary>Segundos restantes de espera.</summary>
    int LockoutRemainingSeconds { get; }
}

/// <summary>Implementación sobre <see cref="CacoSettings"/> con reloj inyectable.</summary>
public sealed class PinLockService(CacoSettings settings, IClock clock) : IPinLockService
{
    /// <summary>Fallos seguidos antes de la espera.</summary>
    public const int MaxAttempts = 5;

    /// <summary>Segundos de espera tras agotar intentos.</summary>
    public const int LockoutSeconds = 30;

    private int _failures;
    private DateTimeOffset _lockedUntil = DateTimeOffset.MinValue;

    /// <inheritdoc/>
    public bool IsPinSet => !string.IsNullOrEmpty(settings.Security.PinHash);

    /// <inheritdoc/>
    public bool IsLockedOut => clock.UtcNow < _lockedUntil;

    /// <inheritdoc/>
    public int LockoutRemainingSeconds =>
        IsLockedOut ? Math.Max(1, (int)(_lockedUntil - clock.UtcNow).TotalSeconds) : 0;

    /// <inheritdoc/>
    public Result SetPin(string pin)
    {
        if (!PinCredentials.IsValidFormat(pin))
        {
            return Result.Failure(Error.Validation("Pin.InvalidFormat", "El PIN debe tener entre 4 y 64 caracteres."));
        }

        var (hash, salt) = PinCredentials.Hash(pin);
        settings.Security.PinHash = hash;
        settings.Security.PinSalt = salt;
        settings.Security.LockEnabled = true;
        Reset();
        return Result.Success();
    }

    /// <inheritdoc/>
    public Result VerifyPin(string? pin)
    {
        if (!IsPinSet)
        {
            return Result.Failure(Error.Validation("Pin.NotConfigured", "No hay PIN configurado."));
        }

        if (IsLockedOut)
        {
            return Result.Failure(Error.Validation(
                "Pin.LockedOut", $"Demasiados intentos. Espera {LockoutRemainingSeconds} segundos."));
        }

        if (PinCredentials.Verify(pin, settings.Security.PinHash, settings.Security.PinSalt))
        {
            Reset();
            return Result.Success();
        }

        _failures++;
        if (_failures >= MaxAttempts)
        {
            _lockedUntil = clock.UtcNow.AddSeconds(LockoutSeconds);
            _failures = 0;
        }

        return Result.Failure(Error.Validation("Pin.Wrong", "PIN incorrecto."));
    }

    /// <inheritdoc/>
    public void RemovePin()
    {
        settings.Security.PinHash = string.Empty;
        settings.Security.PinSalt = string.Empty;
        settings.Security.LockEnabled = false;
        settings.Security.HelloEnabled = false;
        Reset();
    }

    private void Reset()
    {
        _failures = 0;
        _lockedUntil = DateTimeOffset.MinValue;
    }
}
