# =============================================================
# CA-CO — pipeline del instalador (publicar -> firmar -> compilar)
# Uso: powershell -ExecutionPolicy Bypass -File installer\Build-Installer.ps1
# Requisitos: .NET 10 SDK, Inno Setup 6 (portable en %LOCALAPPDATA%\Programs
#   o instalado), signtool (viene con el SDK de build del repo).
# =============================================================
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
Set-Location $repo

$version = '1.2.0'
$leafThumb = 'F9EFDA059DEAD9D684A7EBDE8F24B6E915E861D2'
$timestamp = 'http://timestamp.digicert.com'

$signtool = Get-ChildItem "$HOME\.nuget\packages\microsoft.windows.sdk.buildtools" -Recurse -Filter signtool.exe |
    Where-Object { $_.FullName -match 'x64' } | Select-Object -First 1 -ExpandProperty FullName
if (-not $signtool) { throw 'No se encontro signtool.exe en la cache NuGet.' }

$iscc = Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'
if (-not (Test-Path $iscc)) {
    $pf = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'
    if (Test-Path $pf) { $iscc = $pf } else { throw 'No se encontro ISCC.exe. Instala Inno Setup 6.' }
}

Write-Host '[1/3] Publicando Release autocontenida...'
& dotnet publish src/CA-CO.App/CaCo.App.csproj -c Release -p:Platform=x64 -r win-x64 `
    --self-contained true -p:WindowsAppSDKSelfContained=true -p:PublishTrimmed=false `
    -o installer/staging/CA-CO --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Fallo dotnet publish.' }

Write-Host '[2/3] Firmando binarios...'
$exes = Get-ChildItem installer/staging/CA-CO -Filter *.exe | Select-Object -ExpandProperty FullName
& $signtool sign /fd SHA256 /sha1 $leafThumb /tr $timestamp /td SHA256 $exes
if ($LASTEXITCODE -ne 0) { throw 'Fallo la firma de binarios.' }

Write-Host '[3/3] Compilando instalador (firma setup + desinstalador)...'
$signSpec = '/S' + "casign=$signtool sign /fd SHA256 /sha1 $leafThumb /tr $timestamp /td SHA256 `$f"
& $iscc $signSpec 'installer\CA-CO.iss'
if ($LASTEXITCODE -ne 0) { throw 'Fallo ISCC.' }

Write-Host "OK: installer\dist\CA-CO-Setup-$version-x64.exe"
