using CaCo.Core;
using Microsoft.Extensions.Logging;
using Windows.Security.Credentials.UI;

namespace CaCo.App.Services;

/// <summary>Windows Hello para desbloqueo (Fase 8). Sin PIN no hace nada.</summary>
public interface IHelloService
{
    /// <summary>Indica si Hello está disponible en este equipo.</summary>
    Task<bool> IsAvailableAsync();

    /// <summary>Pide verificación biométrica/PIN de Windows.</summary>
    Task<Result> VerifyAsync(string message, CancellationToken ct);
}

/// <summary>Implementación con <c>UserConsentVerifier</c> del sistema.</summary>
public sealed class HelloService(ILogger<HelloService> logger) : IHelloService
{
    /// <inheritdoc/>
    public async Task<bool> IsAvailableAsync()
    {
        try
        {
            return await UserConsentVerifier.CheckAvailabilityAsync() == UserConsentVerifierAvailability.Available;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Hello no disponible.");
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<Result> VerifyAsync(string message, CancellationToken ct)
    {
        try
        {
            var result = await UserConsentVerifier.RequestVerificationAsync(message).AsTask(ct);
            return result == UserConsentVerificationResult.Verified
                ? Result.Success()
                : Result.Failure(Error.Validation("Hello.Denied", "Verificación cancelada o no superada."));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Fallo la verificación Hello.");
            return Result.Failure(Error.Storage("Hello.Failed", "No se pudo verificar con Windows Hello."));
        }
    }
}
