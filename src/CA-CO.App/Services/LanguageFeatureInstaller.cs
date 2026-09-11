using System.Diagnostics;
using CaCo.Core;
using Microsoft.Extensions.Logging;

namespace CaCo.App.Services;

/// <summary>
/// Instala las características de idioma de Windows (voz y OCR en español)
/// mediante un proceso elevado. Requiere conexión a Internet (Windows Update)
/// y que el usuario acepte el permiso una sola vez.
/// </summary>
public interface ILanguageFeatureInstaller
{
    /// <summary>Instala voz y OCR. Devuelve el resumen de lo instalado.</summary>
    Task<Result<string>> InstallSpeechAndOcrAsync(CancellationToken ct);
}

/// <summary>Implementación con Add-WindowsCapability en PowerShell elevado.</summary>
public sealed class LanguageFeatureInstaller(ILogger<LanguageFeatureInstaller> logger) : ILanguageFeatureInstaller
{
    // es-US no publica estos componentes: solo es-ES (cubre todo el español).
    private static readonly string[] Capabilities =
    [
        "Language.Speech~~~es-ES~0.0.1.0",
        "Language.OCR~~~es-ES~0.0.1.0",
    ];

    /// <inheritdoc/>
    public async Task<Result<string>> InstallSpeechAndOcrAsync(CancellationToken ct)
    {
        var folder = Path.Combine(Path.GetTempPath(), "CA-CO-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var scriptPath = Path.Combine(folder, "install-idioma.ps1");
        var resultPath = Path.Combine(folder, "install-idioma.resultado.txt");
        try
        {
            File.Delete(resultPath);
        }
        catch
        {
            // No existía.
        }

        var names = string.Join("','", Capabilities);
        var script =
            "$ErrorActionPreference='Continue'; $WarningPreference='SilentlyContinue'" + Environment.NewLine +
            "$resultFile = '" + resultPath.Replace("'", "''") + "'" + Environment.NewLine +
            "Write-Host 'CA-CO: preparando instalacion de voz y OCR...'" + Environment.NewLine +
            "if (Test-Path 'HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Component Based Servicing\\RebootPending') {" + Environment.NewLine +
            "  Write-Host '  Windows tiene un reinicio pendiente: reinicia el equipo primero.'" + Environment.NewLine +
            "  'REBOOT-REQUIRED' | Set-Content -LiteralPath $resultFile; exit }" + Environment.NewLine +
            "function Stop-Svc([string]$n) {" + Environment.NewLine +
            "  try {" + Environment.NewLine +
            "    if ((Get-Service -Name $n -ErrorAction Stop).Status -eq 'Stopped') { return $true }" + Environment.NewLine +
            "    Write-Host \"  deteniendo $n ...\"" + Environment.NewLine +
            "    Stop-Service -Name $n -Force -ErrorAction SilentlyContinue" + Environment.NewLine +
            "    for ($w = 0; $w -lt 10; $w++) {" + Environment.NewLine +
            "      try { if ((Get-Service -Name $n).Status -eq 'Stopped') { return $true } } catch { return $true }" + Environment.NewLine +
            "      Start-Sleep -Seconds 2 }" + Environment.NewLine +
            "    try {" + Environment.NewLine +
            "      $line = sc.exe queryex $n | Select-String 'PID' | Select-Object -First 1" + Environment.NewLine +
            "      $id = ($line -replace '.*:\\s*','').Trim()" + Environment.NewLine +
            "      if ($id -match '^\\d+$' -and [int]$id -gt 4) {" + Environment.NewLine +
            "        Write-Host \"  $n colgado: terminando proceso $id ...\"" + Environment.NewLine +
            "        taskkill.exe /F /PID $id 2>$null | Out-Null; Start-Sleep -Seconds 5 } }" + Environment.NewLine +
            "    catch { }" + Environment.NewLine +
            "    try { return ((Get-Service -Name $n).Status -eq 'Stopped') } catch { return $true }" + Environment.NewLine +
            "  } catch { return $true } }" + Environment.NewLine +
            "foreach ($s in @('wuauserv','BITS')) {" + Environment.NewLine +
            "  try { if ((Get-Service -Name $s).Status -ne 'Running') { Start-Service -Name $s; Write-Host \"  servicio $s iniciado.\" } }" + Environment.NewLine +
            "  catch { Write-Host \"  aviso: no se pudo iniciar $s\" } }" + Environment.NewLine +
            "$dlFails = @(Get-WinEvent -FilterHashtable @{LogName='Microsoft-Windows-WindowsUpdateClient/Operational'; ID=31; StartTime=(Get-Date).AddDays(-7)} -ErrorAction SilentlyContinue)" + Environment.NewLine +
            "$repaired = $false" + Environment.NewLine +
            "$marker = Join-Path (Split-Path $resultFile -Parent) 'install-idioma.reparado'" + Environment.NewLine +
            "if ((Test-Path $marker) -and (((Get-Date) - (Get-Item $marker).LastWriteTime).TotalHours -lt 24)) { Write-Host '  Reparacion reciente: se omite y se reintenta la descarga.'; $repaired = $true }" + Environment.NewLine +
            "elseif ($dlFails.Count -gt 0) {" + Environment.NewLine +
            "  Write-Host '  Descargas rotas detectadas: reparando almacen...' " + Environment.NewLine +
            "  try {" + Environment.NewLine +
            "    $orig = @{}" + Environment.NewLine +
            "    foreach ($s in @('wuauserv','BITS','cryptsvc','dosvc','UsoSvc')) {" + Environment.NewLine +
            "      try {" + Environment.NewLine +
            "        $svc = Get-Service -Name $s -ErrorAction Stop" + Environment.NewLine +
            "        $orig[$s] = $svc.StartType" + Environment.NewLine +
            "        if ($svc.Status -ne 'Stopped') {" + Environment.NewLine +
            "          try { Set-Service -Name $s -StartupType Disabled -ErrorAction Stop } catch { }" + Environment.NewLine +
            "          [void](Stop-Svc $s) } }" + Environment.NewLine +
            "      catch { } }" + Environment.NewLine +
            "    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'" + Environment.NewLine +
            "    $movedSd = $false; $movedCat = $false" + Environment.NewLine +
            "    for ($r = 0; $r -lt 3 -and -not ($movedSd -and $movedCat); $r++) {" + Environment.NewLine +
            "      if ($r -gt 0) { Write-Host '  reintentando apartar carpetas...'; Start-Sleep -Seconds 4 }" + Environment.NewLine +
            "      if (Test-Path \"$env:SystemRoot\\SoftwareDistribution\") { try { Rename-Item \"$env:SystemRoot\\SoftwareDistribution\" \"SoftwareDistribution.caco-$stamp\" -ErrorAction Stop; $movedSd = -not (Test-Path \"$env:SystemRoot\\SoftwareDistribution\") } catch { } } else { $movedSd = $true }" + Environment.NewLine +
            "      if (Test-Path \"$env:SystemRoot\\System32\\catroot2\") { try { Rename-Item \"$env:SystemRoot\\System32\\catroot2\" \"catroot2.caco-$stamp\" -ErrorAction Stop; $movedCat = -not (Test-Path \"$env:SystemRoot\\System32\\catroot2\") } catch { } } else { $movedCat = $true } }" + Environment.NewLine +
            "    foreach ($kv in $orig.GetEnumerator()) { try { Set-Service -Name $kv.Key -StartupType $kv.Value -ErrorAction Stop } catch { } }" + Environment.NewLine +
            "    Start-Service -Name wuauserv,BITS,cryptsvc -ErrorAction SilentlyContinue" + Environment.NewLine +
            "    if ($movedSd -and $movedCat) { $repaired = $true; Write-Host '  almacen reparado OK.'; try { Set-Content -LiteralPath $marker -Value (Get-Date -Format 'o') } catch { } }" + Environment.NewLine +
            "    else {" + Environment.NewLine +
            "      Write-Host '  Apartado bloqueado por el sistema: restaurando imagen (hasta 25 min, veras su %)...'" + Environment.NewLine +
            "      try {" + Environment.NewLine +
            "        $d = Start-Process -FilePath \"$env:SystemRoot\\System32\\DISM.exe\" -ArgumentList '/Online /Cleanup-Image /RestoreHealth' -PassThru" + Environment.NewLine +
            "        if (-not $d.WaitForExit(25*60*1000)) { Write-Host '  DISM tardo demasiado: se continua igualmente...'; try { Stop-Process -Id $d.Id -Force } catch { } }" + Environment.NewLine +
            "        else { Write-Host (\"  DISM termino con codigo \" + $d.ExitCode + \".\"); if ($d.ExitCode -eq 0) { $repaired = $true } } }" + Environment.NewLine +
            "      catch { Write-Host '  aviso: la restauracion tambien fallo.' } }" + Environment.NewLine +
            "  } catch { Write-Host '  aviso: no se pudo reparar.' } }" + Environment.NewLine +
            "else { Write-Host '  (sin fallos previos de descarga: no hizo falta reparar.)'; $repaired = $true }" + Environment.NewLine +
            "$ProgressPreference='SilentlyContinue'" + Environment.NewLine +
            "$worker = Join-Path (Split-Path $resultFile -Parent) 'install-una.ps1'" + Environment.NewLine +
            "\"param([string]$Name)`n`$ProgressPreference='SilentlyContinue'`nAdd-WindowsCapability -Online -Name `$Name\" | Set-Content -LiteralPath $worker" + Environment.NewLine +
            "Write-Host 'CA-CO: descargando e instalando (veras el % real, no cierres esta ventana)...'" + Environment.NewLine +
            "$ok=@(); $fail=@(); $i=0" + Environment.NewLine +
            $"$caps=@('{names}'); $total=$caps.Count" + Environment.NewLine +
            "foreach ($n in $caps) {" + Environment.NewLine +
            "  $i++; Write-Host \"  [$i/$total] $n ...\"" + Environment.NewLine +
            "  try { if ((Get-WindowsCapability -Online -Name $n).State -eq 'Installed') { $ok+=\"$n (ya estaba)\"; Write-Host '    ya estaba instalado.'; continue } }" + Environment.NewLine +
            "  catch { }" + Environment.NewLine +
            "  $t0 = Get-Date; $lastPct = -1; $stuckSince = Get-Date" + Environment.NewLine +
            "  $p = Start-Process -FilePath 'powershell.exe' -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File',$worker,$n) -PassThru -WindowStyle Hidden" + Environment.NewLine +
            "  $done = $false; $stuck = $false" + Environment.NewLine +
            "  for ($w = 0; $w -lt 240 -and -not $done; $w++) {" + Environment.NewLine +
            "    Start-Sleep -Seconds 10" + Environment.NewLine +
            "    try {" + Environment.NewLine +
            "      $hit = Get-Content \"$env:SystemRoot\\Logs\\CBS\\CBS.log\" -Tail 600 -ErrorAction Stop |" + Environment.NewLine +
            "        Where-Object { $_ -match '^(\\d{4}-\\d{2}-\\d{2} \\d{2}:\\d{2}:\\d{2}),.*DownloadProgress: \\[(\\d+)' } |" + Environment.NewLine +
            "        ForEach-Object { [pscustomobject]@{ T = [datetime]$matches[1]; P = [int]$matches[2] } } |" + Environment.NewLine +
            "        Where-Object { $_.T -ge $t0 } | Select-Object -Last 1" + Environment.NewLine +
            "      if ($hit -and $hit.P -ne $lastPct) { $lastPct = $hit.P; $stuckSince = Get-Date; Write-Host \"    descargando... $lastPct%\" } }" + Environment.NewLine +
            "    catch { }" + Environment.NewLine +
            "    if (((Get-Date) - $stuckSince).TotalMinutes -ge 15 -and $lastPct -ge 0) { $stuck = $true; $done = $true; Write-Host \"    atascado en $lastPct% mas de 15 min: se pasa al siguiente...\" }" + Environment.NewLine +
            "    try { $p.Refresh(); if ($p.HasExited) { $done = $true } } catch { $done = $true } }" + Environment.NewLine +
            "  if (-not $done) { Write-Host '    tardo demasiado (40 min): se pasa al siguiente...'; try { Stop-Process -Id $p.Id -Force } catch { } ; $fail += \"$n :: tiempo agotado\" }" + Environment.NewLine +
            "  elseif ($stuck) { try { Stop-Process -Id $p.Id -Force } catch { }; $fail += \"$n :: descarga atascada en $lastPct%\" }" + Environment.NewLine +
            "  else {" + Environment.NewLine +
            "    Start-Sleep -Seconds 3" + Environment.NewLine +
            "    try { if ((Get-WindowsCapability -Online -Name $n).State -eq 'Installed') { $ok += $n; Write-Host '    listo.' } else { $fail += \"$n :: no quedo instalado\"; Write-Host '    FALLO: no quedo instalado.' } }" + Environment.NewLine +
            "    catch { $fail += \"$n :: no se pudo verificar\"; Write-Host '    FALLO: no se pudo verificar.' } } }" + Environment.NewLine +
            "Write-Host 'CA-CO: terminado.'" + Environment.NewLine +
            "@('OK:'+$ok.Count; $ok; 'FAIL:'+$fail.Count; $fail) | Set-Content -LiteralPath '"
            + resultPath.Replace("'", "''") + "'";

        try
        {
            await File.WriteAllTextAsync(scriptPath, script, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return Result.Failure<string>(Error.Storage("Language.ScriptFailed", $"No se pudo preparar la instalación: {ex.Message}"));
        }

        Process? process;
        try
        {
            process = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
                UseShellExecute = true,
                Verb = "runas",
            });
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return Result.Failure<string>(Error.Validation(
                "Language.Cancelled", "Instalación cancelada. Pulsa el botón y acepta el permiso para continuar."));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "No se pudo elevar para instalar idioma.");
            return Result.Failure<string>(Error.Storage("Language.ElevateFailed", $"No se pudo pedir el permiso: {ex.Message}"));
        }

        if (process is null)
        {
            return Result.Failure<string>(Error.Storage("Language.LaunchFailed", "No se pudo iniciar la instalación."));
        }

        var startTime = DateTime.UtcNow;
        var maxDuration = TimeSpan.FromMinutes(45);
        try
        {
            while (!process.HasExited)
            {
                ct.ThrowIfCancellationRequested();
                if (DateTime.UtcNow - startTime > maxDuration)
                {
                    try { process.Kill(); } catch { }
                    return Result.Failure<string>(Error.Storage(
                        "Language.Timeout", "La instalación tardó demasiado (45 min). Vuelve a intentarlo."));
                }

                await Task.Delay(1000, ct).ConfigureAwait(false);
                process.Refresh();
            }
        }
        catch (OperationCanceledException)
        {
            return Result.Success("La instalación sigue en segundo plano. Vuelve en unos minutos.");
        }

        try
        {
            if (!File.Exists(resultPath))
            {
                return Result.Failure<string>(Error.Storage(
                    "Language.NoResult", "Terminó sin confirmar. Revisa tu conexión y vuelve a intentarlo."));
            }

            var lines = await File.ReadAllLinesAsync(resultPath, ct).ConfigureAwait(false);
            if (lines.Any(l => l.Trim() == "REBOOT-REQUIRED"))
            {
                return Result.Failure<string>(Error.Validation(
                    "Language.RebootRequired",
                    "Windows tiene un reinicio pendiente: reinicia el equipo y vuelve a pulsar «Instalar voz y OCR»."));
            }

            var fails = lines.Where(l => l.StartsWith("Language.", StringComparison.Ordinal) && l.Contains("::"))
                .ToList();
            var oks = lines.Count(l => l.StartsWith("Language.", StringComparison.Ordinal) && !l.Contains("::"));
            if (fails.Count > 0)
            {
                logger.LogWarning("Idioma parcial: {Fails}", string.Join("; ", fails));
                return Result.Failure<string>(Error.Storage(
                    "Language.Partial",
                    $"Se instalaron {oks}, fallaron {fails.Count} (¿hay Internet?). Detalle en {resultPath}"));
            }

            return Result.Success($"Voz y OCR instalados ({oks} componentes).");
        }
        catch (Exception ex)
        {
            return Result.Failure<string>(Error.Storage("Language.ResultFailed", $"No se pudo leer el resultado: {ex.Message}"));
        }
    }
}
