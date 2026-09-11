using CaCo.Core;

namespace CaCo.Application.Future.Security;

/// <summary>Autenticación del usuario (Reservado para Fase 8: PIN, contraseña, Windows Hello).</summary>
public interface IAuthenticationService
{
    /// <summary>Indica si el bloqueo está configurado.</summary>
    bool IsLockConfigured { get; }

    /// <summary>Verifica la identidad del usuario.</summary>
    Task<Result<bool>> AuthenticateAsync(CancellationToken ct);
}

/// <summary>Cifrado opcional de la biblioteca (Reservado para Fase 8).</summary>
public interface IEncryptionService
{
    /// <summary>Indica si el cifrado está activo.</summary>
    bool IsEnabled { get; }

    /// <summary>Cifra bytes.</summary>
    Task<Result<byte[]>> EncryptAsync(byte[] plain, CancellationToken ct);

    /// <summary>Descifra bytes.</summary>
    Task<Result<byte[]>> DecryptAsync(byte[] cipher, CancellationToken ct);
}

/// <summary>Fachada de seguridad que combina autenticación y cifrado (Fase 8).</summary>
public interface ISecurityService
{
    /// <summary>Autenticación.</summary>
    IAuthenticationService Authentication { get; }

    /// <summary>Cifrado.</summary>
    IEncryptionService Encryption { get; }
}
