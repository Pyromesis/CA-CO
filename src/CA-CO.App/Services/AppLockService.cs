using CaCo.Application.Configuration;
using CaCo.Application.Security;
using CaCo.Core;
using Microsoft.Extensions.Logging;

namespace CaCo.App.Services;

/// <summary>Bloqueo de la app con PIN y Windows Hello (Fase 8).</summary>
public sealed class AppLockService(
    CacoSettings settings,
    IPinLockService pin,
    IHelloService hello,
    IDialogService dialogs,
    ILogger<AppLockService> logger) : CaCo.Application.Future.Security.IAuthenticationService
{
    /// <inheritdoc/>
    public bool IsLockConfigured => pin.IsPinSet;

    /// <inheritdoc/>
    public async Task<Result<bool>> AuthenticateAsync(CancellationToken ct)
    {
        if (!pin.IsPinSet)
        {
            return Result.Success(true);
        }

        // Hello primero si está activado y disponible (una oportunidad).
        if (settings.Security.HelloEnabled)
        {
            try
            {
                if (await hello.IsAvailableAsync())
                {
                    var verified = await hello.VerifyAsync("Desbloquear CA-CO", ct);
                    if (verified.IsSuccess)
                    {
                        return Result.Success(true);
                    }

                    logger.LogInformation("Hello no superado; se pide PIN.");
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Hello falló; se pide PIN.");
            }
        }

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (pin.IsLockedOut)
            {
                return Result.Failure<bool>(Error.Validation(
                    "Pin.LockedOut", $"Demasiados intentos. Espera {pin.LockoutRemainingSeconds} segundos."));
            }

            var entered = await dialogs.PromptPasswordAsync("CA-CO bloqueado", "Introduce tu PIN");
            if (entered is null)
            {
                return Result.Success(false);
            }

            var checkedPin = pin.VerifyPin(entered);
            if (checkedPin.IsSuccess)
            {
                return Result.Success(true);
            }

            await dialogs.ShowMessageAsync("PIN incorrecto", checkedPin.Error.Message);
        }
    }
}
