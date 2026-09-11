using CaCo.Application.Updates;

namespace CaCo.Tests.Application;

/// <summary>Pruebas de versiones y selección de instalador (sin red).</summary>
public sealed class UpdateVersionTests
{
    [Theory]
    [InlineData("v1.2.3", 1, 2, 3)]
    [InlineData("1.0.0", 1, 0, 0)]
    [InlineData("V2.0", 2, 0, -1)]
    public void TryParseTag_Valid_Parses(string tag, int major, int minor, int build)
    {
        Assert.True(UpdateVersion.TryParseTag(tag, out var version));
        Assert.Equal(major, version.Major);
        Assert.Equal(minor, version.Minor);
        if (build >= 0)
        {
            Assert.Equal(build, version.Build);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("nonsense")]
    [InlineData("v")]
    public void TryParseTag_Invalid_Fails(string? tag)
    {
        Assert.False(UpdateVersion.TryParseTag(tag, out _));
    }

    [Fact]
    public void IsNewerThan_StrictComparison()
    {
        Assert.True(UpdateVersion.IsNewerThan(new Version(1, 1, 0), new Version(1, 0, 0)));
        Assert.False(UpdateVersion.IsNewerThan(new Version(1, 0, 0), new Version(1, 0, 0)));
        Assert.False(UpdateVersion.IsNewerThan(new Version(1, 0, 0), new Version(1, 1, 0)));
    }

    [Fact]
    public void SelectSetupAsset_PicksX64Exe()
    {
        var assets = new (string Name, string Url, long Size)[]
        {
            ("CA-CO-CA.crt", "https://example.com/CA-CO-CA.crt", 1000),
            ("CA-CO-Setup-1.1.0-x64.exe", "https://example.com/setup.exe", 72000000),
            ("readme.txt", "https://example.com/readme.txt", 10),
        };

        var picked = UpdateVersion.SelectSetupAsset(assets);
        Assert.NotNull(picked);
        Assert.Equal("CA-CO-Setup-1.1.0-x64.exe", picked.Value.Name);
        Assert.Equal("https://example.com/setup.exe", picked.Value.Url);
    }

    [Fact]
    public void SelectSetupAsset_WithoutInstaller_ReturnsNull()
    {
        var assets = new (string Name, string Url, long Size)[]
        {
            ("CA-CO-CA.crt", "https://example.com/CA-CO-CA.crt", 1000),
        };

        Assert.Null(UpdateVersion.SelectSetupAsset(assets));
    }
}
