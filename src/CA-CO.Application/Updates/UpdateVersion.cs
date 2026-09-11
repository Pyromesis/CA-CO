namespace CaCo.Application.Updates;

/// <summary>Lógica pura de versiones y selección de instalador (testeable sin red).</summary>
public static class UpdateVersion
{
    /// <summary>Normaliza una etiqueta ("v1.2.3", "1.2") a <see cref="Version"/>.</summary>
    public static bool TryParseTag(string? tag, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(tag))
        {
            return false;
        }

        var clean = tag.Trim().TrimStart('v', 'V');
        // System.Version exige 2-4 componentes numéricos.
        if (!Version.TryParse(clean, out var parsed))
        {
            return false;
        }

        version = parsed;
        return true;
    }

    /// <summary>Indica si la candidata es estrictamente mayor que la instalada.</summary>
    public static bool IsNewerThan(Version candidate, Version current)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(current);
        return candidate.CompareTo(current) > 0;
    }

    /// <summary>Elige el instalador x64 entre los assets de un release.</summary>
    /// <param name="assets">Tuplas (nombre, url, tamaño).</param>
    /// <returns>El asset elegido o null si no hay instalador.</returns>
    public static (string Name, string Url, long Size)? SelectSetupAsset(
        IEnumerable<(string Name, string Url, long Size)> assets)
    {
        ArgumentNullException.ThrowIfNull(assets);
        (string Name, string Url, long Size)? fallback = null;
        foreach (var asset in assets)
        {
            if (string.IsNullOrWhiteSpace(asset.Name) || string.IsNullOrWhiteSpace(asset.Url))
            {
                continue;
            }

            var name = asset.Name.Trim();
            if (!name.StartsWith("CA-CO-Setup-", StringComparison.OrdinalIgnoreCase)
                || !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            fallback ??= (name, asset.Url.Trim(), Math.Max(0, asset.Size));
            if (name.Contains("x64", StringComparison.OrdinalIgnoreCase))
            {
                return (name, asset.Url.Trim(), Math.Max(0, asset.Size));
            }
        }

        return fallback;
    }
}
