using CaCo.Application.Configuration;
using CaCo.Application.Security;
using CaCo.Core;
using CaCo.Tests.Helpers;

namespace CaCo.Tests.Application;

/// <summary>Pruebas Fase 8: PIN con PBKDF2 y espera tras fallos.</summary>
public sealed class SecurityPhaseTests
{
    [Fact]
    public void PinHash_Verify_Roundtrip()
    {
        var (hash, salt) = PinCredentials.Hash("1234");
        Assert.True(PinCredentials.Verify("1234", hash, salt));
        Assert.False(PinCredentials.Verify("4321", hash, salt));
        Assert.False(PinCredentials.Verify(null, hash, salt));
        Assert.False(PinCredentials.Verify("1234", "!!!", salt));
    }

    [Fact]
    public void PinHash_SaltsAreUnique()
    {
        var first = PinCredentials.Hash("1234");
        var second = PinCredentials.Hash("1234");
        Assert.NotEqual(first.Hash, second.Hash);
        Assert.NotEqual(first.Salt, second.Salt);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("123", false)]
    [InlineData("1234", true)]
    [InlineData("una-clave-muy-larga-pero-valida-0123456789", true)]
    public void PinFormat_Boundaries(string? pin, bool expected)
    {
        Assert.Equal(expected, PinCredentials.IsValidFormat(pin));
        Assert.Equal(64, new string('x', 64).Length);
        Assert.True(PinCredentials.IsValidFormat(new string('x', 64)));
        Assert.False(PinCredentials.IsValidFormat(new string('x', 65)));
    }

    [Fact]
    public void PinLock_SetVerifyRemove_Flow()
    {
        var settings = new CacoSettings();
        var clock = new TestClock(DateTimeOffset.UtcNow);
        var pin = new PinLockService(settings, clock);

        Assert.False(pin.IsPinSet);
        Assert.True(pin.VerifyPin("1234").IsFailure);
        Assert.True(pin.SetPin("12").IsFailure);
        Assert.True(pin.SetPin("1234").IsSuccess);
        Assert.True(pin.IsPinSet);
        Assert.True(settings.Security.LockEnabled);
        Assert.True(pin.VerifyPin("1234").IsSuccess);

        pin.RemovePin();
        Assert.False(pin.IsPinSet);
        Assert.False(settings.Security.HelloEnabled);
    }

    [Fact]
    public async Task PinSettings_SurviveSaveLoad()
    {
        using var temp = new CaCo.Tests.Helpers.TempDirectory();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var file = Path.Combine(temp.Path, "settings.json");
        var config = new CaCo.Infrastructure.Configuration.FileAppConfiguration(file);

        var settings = new CacoSettings();
        var pin = new PinLockService(settings, new TestClock(DateTimeOffset.UtcNow));
        Assert.True(pin.SetPin("1234").IsSuccess);
        settings.Security.HelloEnabled = true;
        await config.SaveAsync(settings, cts.Token);

        var loaded = await config.LoadAsync(cts.Token);
        Assert.True(loaded.Security.LockEnabled);
        Assert.True(loaded.Security.HelloEnabled);
        Assert.False(string.IsNullOrEmpty(loaded.Security.PinHash));
        var pin2 = new PinLockService(loaded, new TestClock(DateTimeOffset.UtcNow));
        Assert.True(pin2.IsPinSet);
        Assert.True(pin2.VerifyPin("1234").IsSuccess);
        Assert.True(pin2.VerifyPin("mala").IsFailure);
    }

    [Fact]
    public void PinLock_FiveFailures_LocksOutThenRecovers()
    {
        var now = DateTimeOffset.UtcNow;
        var clock = new TestClock(now);
        var settings = new CacoSettings();
        var pin = new PinLockService(settings, clock);
        Assert.True(pin.SetPin("1234").IsSuccess);

        for (var i = 0; i < 5; i++)
        {
            var result = pin.VerifyPin("mala");
            Assert.True(result.IsFailure);
            Assert.Equal("Pin.Wrong", result.Error.Code);
        }

        Assert.True(pin.IsLockedOut);
        var locked = pin.VerifyPin("1234");
        Assert.True(locked.IsFailure);
        Assert.Equal("Pin.LockedOut", locked.Error.Code);
        Assert.True(pin.LockoutRemainingSeconds is >= 1 and <= 30);

        clock.Advance(TimeSpan.FromSeconds(31));
        Assert.False(pin.IsLockedOut);
        Assert.True(pin.VerifyPin("1234").IsSuccess);
    }
}
