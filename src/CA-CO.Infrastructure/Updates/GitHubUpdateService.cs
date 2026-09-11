using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using CaCo.Application.Updates;
using CaCo.Core;
using Microsoft.Extensions.Logging;

namespace CaCo.Infrastructure.Updates;

/// <summary>
/// Actualizaciones contra GitHub Releases (sin dependencias nuevas:
/// <c>HttpClient</c> + <c>JsonDocument</c>, apto para recorte).
/// Offline-first: cualquier fallo de red se devuelve como <see cref="Result"/>
/// con mensaje genérico; al usuario nunca le llegan detalles técnicos.
/// </summary>
public sealed class GitHubUpdateService : IUpdateService
{
    private const string ExpectedPublisher = "CN=CA-CO";
    private const long MinInstallerBytes = 10L * 1024 * 1024;

    private readonly ILogger<GitHubUpdateService>? _logger;
    private readonly HttpMessageHandler? _handler;
    private readonly string _owner;
    private readonly string _repo;

    /// <summary>Crea el servicio (DI resuelve solo el logger).</summary>
    public GitHubUpdateService(
        ILogger<GitHubUpdateService>? logger = null,
        HttpMessageHandler? handler = null,
        string owner = "Pyromesis",
        string repo = "CA-CO")
    {
        _logger = logger;
        _handler = handler;
        _owner = string.IsNullOrWhiteSpace(owner) ? "Pyromesis" : owner.Trim();
        _repo = string.IsNullOrWhiteSpace(repo) ? "CA-CO" : repo.Trim();
    }

    /// <inheritdoc/>
    public async Task<UpdateCheckResult> CheckForUpdatesAsync(Version current, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(current);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        var token = timeout.Token;

        try
        {
            using var http = CreateClient();
            using var response = await http.GetAsync(
                new Uri($"https://api.github.com/repos/{_owner}/{_repo}/releases/latest"),
                HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger?.LogWarning("GitHub devolvió {Status} al comprobar actualizaciones.", (int)response.StatusCode);
                return Fail(current, "Update.CheckFailed", "No se pudo comprobar. Revisa tu conexión.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: token).ConfigureAwait(false);
            var root = json.RootElement;

            if (!root.TryGetProperty("tag_name", out var tagProp)
                || !UpdateVersion.TryParseTag(tagProp.GetString(), out var latest))
            {
                return Fail(current, "Update.BadResponse", "GitHub devolvió una respuesta inesperada.");
            }

            if (!UpdateVersion.IsNewerThan(latest, current))
            {
                return new UpdateCheckResult(true, false, null, current, null);
            }

            var assets = new List<(string Name, string Url, long Size)>();
            if (root.TryGetProperty("assets", out var assetsProp)
                && assetsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assetsProp.EnumerateArray())
                {
                    var name = asset.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty;
                    var url = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() ?? string.Empty : string.Empty;
                    var size = asset.TryGetProperty("size", out var s) && s.TryGetInt64(out var v) ? v : 0;
                    assets.Add((name, url, size));
                }
            }

            var picked = UpdateVersion.SelectSetupAsset(assets);
            if (picked is null)
            {
                return Fail(current, "Update.NoInstaller", "El release no trae instalador para este equipo.");
            }

            var notes = root.TryGetProperty("body", out var body) ? body.GetString() ?? string.Empty : string.Empty;
            if (notes.Length > 2000)
            {
                notes = notes[..2000] + "…";
            }

            var release = new UpdateRelease(
                root.GetProperty("tag_name").GetString() ?? latest.ToString(),
                latest, picked.Value.Name, new Uri(picked.Value.Url), picked.Value.Size, notes.Trim());
            return new UpdateCheckResult(true, true, release, current, null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Fail(current, "Update.Timeout", "GitHub tardó demasiado. Inténtalo de nuevo.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Fallo al comprobar actualizaciones.");
            return Fail(current, "Update.CheckFailed", "No se pudo comprobar. Revisa tu conexión.");
        }
    }

    /// <inheritdoc/>
    public async Task<Result<string>> DownloadAsync(
        UpdateRelease release, IProgress<double> progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(progress);

        var folder = Path.Combine(Path.GetTempPath(), "CA-CO", "updates");
        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception ex)
        {
            return Result.Failure<string>(Error.Storage("Update.TempFailed", $"No se pudo preparar la descarga: {ex.Message}"));
        }

        var target = Path.Combine(folder, release.AssetName);
        try
        {
            // Un reintento ante cortes de red; si el proxy trunca de forma
            // determinista, el segundo intento falla igual con error claro.
            IOException? lastIncomplete = null;
            for (var attempt = 0; attempt < 2; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    await DownloadOnceAsync(release, target, progress, ct).ConfigureAwait(false);
                    lastIncomplete = null;
                    break;
                }
                catch (IOException ex) when (attempt == 0)
                {
                    lastIncomplete = ex;
                    _logger?.LogWarning(ex, "Descarga incompleta, reintentando.");
                    try { if (File.Exists(target)) { File.Delete(target); } } catch { }
                    progress.Report(0);
                }
            }

            if (lastIncomplete is not null)
            {
                throw lastIncomplete;
            }

            progress.Report(100);
            var check = VerifyInstaller(target);
            if (check.IsFailure)
            {
                return Result.Failure<string>(check.Error);
            }

            return Result.Success(target);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Fallo la descarga de la actualización.");
            try { if (File.Exists(target)) { File.Delete(target); } } catch { }
            return Result.Failure<string>(Error.Storage("Update.DownloadFailed", "No se pudo descargar. Inténtalo de nuevo."));
        }
    }

    /// <inheritdoc/>
    public Result VerifyInstaller(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return Result.Failure(Error.Validation("Update.MissingFile", "El instalador descargado no existe."));
        }

        try
        {
            var info = new FileInfo(path);
            if (info.Length < MinInstallerBytes)
            {
                throw new InvalidDataException($"Tamaño sospechoso ({info.Length} bytes).");
            }

            // SYSLIB0057: no hay alternativa recortable para leer Authenticode
            // de un fichero; esta API sigue siendo la vía soportada.
#pragma warning disable SYSLIB0057
            using var cert = X509Certificate.CreateFromSignedFile(path);
#pragma warning restore SYSLIB0057
            if (cert.Subject is null
                || !cert.Subject.Contains(ExpectedPublisher, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Firma de editor inesperada.");
            }

            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "El instalador descargado no pasó la verificación.");
            try { File.Delete(path); } catch { }
            return Result.Failure(Error.Validation("Update.Untrusted", "El instalador no pasó la verificación. Se ha descartado."));
        }
    }

    private static async Task DownloadOnceAsync(
        UpdateRelease release, string target, IProgress<double> progress, CancellationToken ct)
    {
        using var http = CreateClientStatic();
        using var response = await http.GetAsync(
            release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new IOException($"GitHub devolvió {(int)response.StatusCode}.");
        }

        // La verdad es el máximo entre cabecera y metadatos del release:
        // una cabecera corta no debe enmascarar un truncado.
        var declared = response.Content.Headers.ContentLength ?? 0;
        var expected = Math.Max(declared, release.SizeBytes);
        await using var remote = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var local = new FileStream(
            target, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, useAsync: true);
        var buffer = new byte[1 << 20];
        long done = 0;
        int read;
        while ((read = await remote.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false)) > 0)
        {
            await local.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            done += read;
            if (expected > 0)
            {
                progress.Report(Math.Clamp(done * 100.0 / expected, 0, 100));
            }
        }

        await local.FlushAsync(ct).ConfigureAwait(false);
        if (expected > 0 && done != expected)
        {
            throw new IOException($"Descarga incompleta ({done}/{expected} bytes).");
        }
    }

    private HttpClient CreateClient() => CreateClientStatic(_handler);

    private static HttpClient CreateClientStatic(HttpMessageHandler? handler = null)
    {
        HttpClient http = handler is null
            ? new HttpClient()
            : new HttpClient(handler, disposeHandler: false);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("CA-CO-Updater");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return http;
    }

    /// <inheritdoc/>
    public bool LaunchInstaller(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = path,
                Arguments = "/SILENT /NORESTART /relaunch=1",
                UseShellExecute = true,
            });
            return process is not null;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "No se pudo lanzar el instalador.");
            return false;
        }
    }

    private static UpdateCheckResult Fail(Version current, string code, string message) =>
        new(false, false, null, current, Error.Storage(code, message));
}
