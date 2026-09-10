# Publica este proyecto RunnerIA como carpeta portable self-contained (win-x64).
# Incluye RunnerIA.exe (ventana de escritorio WebView2) + app/RunnerIA.Server.exe (API).
param(
    [switch]$Zip,
    [switch]$SinBuildFrontend,
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'

if ((Get-ExecutionPolicy -Scope Process) -ne 'Bypass') {
    $argList = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $PSCommandPath)
    if ($Zip) { $argList += '-Zip' }
    if ($SinBuildFrontend) { $argList += '-SinBuildFrontend' }
    $argList += @('-Runtime', $Runtime)
    & powershell.exe @argList
    exit $LASTEXITCODE
}

$runnerRoot = $PSScriptRoot
$apiProj = Join-Path $runnerRoot 'RunnerOperadorApi\RunnerOperadorApi.csproj'
$desktopProj = Join-Path $runnerRoot 'RunnerIA.Desktop\RunnerIA.Desktop.csproj'
$frontendDir = Join-Path $runnerRoot 'frontend'
$wwwRootSrc = Join-Path $runnerRoot 'wwwroot'
$outDir = Join-Path $runnerRoot "dist\RunnerIA-$Runtime"
$autoRoot = Join-Path (Split-Path $runnerRoot -Parent) 'AutomatizacionSOT\AutomatizacionSOT'

if (-not (Test-Path $apiProj)) { Write-Error "No se encontro $apiProj" }
if (-not (Test-Path $desktopProj)) { Write-Error "No se encontro $desktopProj" }
if (-not (Test-Path (Join-Path $autoRoot 'AutomatizacionSOT.csproj'))) {
    Write-Warning "No se ve AutomatizacionSOT hermano. El portable se genera igual (modo ayuda sin suite)."
}

Write-Host '=== Publicar RunnerIA portable (programa de escritorio) ===' -ForegroundColor Cyan
Write-Host "Salida: $outDir" -ForegroundColor DarkGray

$portableNode = Get-ChildItem (Join-Path $runnerRoot '.tools\node') -Directory -ErrorAction SilentlyContinue |
    Where-Object { Test-Path (Join-Path $_.FullName 'node.exe') } |
    Select-Object -First 1
if ($portableNode) { $env:Path = "$($portableNode.FullName);" + $env:Path }

$builtFrontend = $false
if (-not $SinBuildFrontend -and (Test-Path $frontendDir) -and (Get-Command npm -ErrorAction SilentlyContinue)) {
    Write-Host 'Compilando Frontend Angular...' -ForegroundColor DarkGray
    Push-Location $frontendDir
    try {
        if (-not (Test-Path 'node_modules')) {
            $env:npm_config_loglevel = 'error'
            npm install --no-fund --no-audit
            if ($LASTEXITCODE -ne 0) { throw "npm install fallo" }
        }
        npm run build
        if ($LASTEXITCODE -ne 0) { throw "ng build fallo" }
        $builtFrontend = $true
    } finally { Pop-Location }
}
elseif ($SinBuildFrontend) {
    Write-Warning 'SinBuildFrontend: se usara wwwroot existente (si hay Angular compilado).'
}

if (Test-Path $outDir) { Remove-Item -LiteralPath $outDir -Recurse -Force }
$appDir = Join-Path $outDir 'app'
New-Item -ItemType Directory -Force -Path $appDir | Out-Null
$publishTmp = Join-Path $runnerRoot 'dist\_publish-tmp'
$publishDesktopTmp = Join-Path $runnerRoot 'dist\_publish-desktop-tmp'
if (Test-Path $publishTmp) { Remove-Item -LiteralPath $publishTmp -Recurse -Force }
if (Test-Path $publishDesktopTmp) { Remove-Item -LiteralPath $publishDesktopTmp -Recurse -Force }

Write-Host "dotnet publish API ($Runtime) -> RunnerIA.Server..." -ForegroundColor DarkGray
dotnet publish $apiProj -c Release -r $Runtime --self-contained true -o $publishTmp `
    /p:PublishSingleFile=false /p:AssemblyName=RunnerIA.Server /p:Product=RunnerIA
if ($LASTEXITCODE -ne 0) { Write-Error "publish API fallo" }

Copy-Item (Join-Path $publishTmp '*') $appDir -Recurse -Force
if (Test-Path $wwwRootSrc) {
    Copy-Item $wwwRootSrc (Join-Path $outDir 'wwwroot') -Recurse -Force
}

$exeServer = Join-Path $appDir 'RunnerIA.Server.exe'
$exeLegacyApi = Join-Path $appDir 'RunnerOperadorApi.exe'
$exeOldName = Join-Path $appDir 'RunnerIA.exe'
if ((Test-Path $exeLegacyApi) -and -not (Test-Path $exeServer)) {
    Rename-Item $exeLegacyApi 'RunnerIA.Server.exe'
}
if ((Test-Path $exeOldName) -and -not (Test-Path $exeServer)) {
    Rename-Item $exeOldName 'RunnerIA.Server.exe'
}

$angularIndex = Join-Path $outDir 'wwwroot\app\index.html'
if (-not (Test-Path $angularIndex)) {
    if ($SinBuildFrontend) {
        Write-Warning "No esta wwwroot\app\index.html. La UI puede caer al HTML legacy."
    } else {
        Write-Error "Falta wwwroot\app\index.html tras el publish. Compila el frontend (npm run build) o usa -SinBuildFrontend solo si ya existe."
    }
}

if (-not (Test-Path $exeServer)) {
    Write-Error "No se genero app\RunnerIA.Server.exe en $appDir"
}

Write-Host "dotnet publish Desktop ($Runtime) -> RunnerIA.exe..." -ForegroundColor DarkGray
dotnet publish $desktopProj -c Release -r $Runtime --self-contained true -o $publishDesktopTmp `
    /p:PublishSingleFile=false /p:AssemblyName=RunnerIA /p:Product=RunnerIA
if ($LASTEXITCODE -ne 0) { Write-Error "publish Desktop fallo" }

# Copiar host a la raiz del portable (lo que el usuario ejecuta)
Copy-Item (Join-Path $publishDesktopTmp '*') $outDir -Recurse -Force
$exeDesktop = Join-Path $outDir 'RunnerIA.exe'
if (-not (Test-Path $exeDesktop)) {
    Write-Error "No se genero RunnerIA.exe (host de escritorio) en $outDir"
}

$gitHash = ''
try {
    Push-Location $runnerRoot
    $gitHash = (& git rev-parse --short HEAD 2>$null)
    if ($LASTEXITCODE -ne 0) { $gitHash = '' }
} catch { $gitHash = '' } finally { Pop-Location }

$stamp = Get-Date -Format 'yyyy-MM-dd HH:mm'
$versionLines = @(
    "RunnerIA portable ($Runtime) - programa de escritorio"
    "Generado: $stamp"
)
if ($gitHash) { $versionLines += "Git: $gitHash" }
$versionLines += "Frontend build: $(if ($builtFrontend) { 'si' } else { 'omitido/cache' })"
$versionLines += "Host: RunnerIA.exe (WebView2)"
$versionLines += "API: app\RunnerIA.Server.exe"
[IO.File]::WriteAllLines((Join-Path $outDir 'version.txt'), $versionLines, [Text.UTF8Encoding]::new($false))

$batPortable = @'
@echo off
setlocal EnableExtensions
title RunnerIA
cd /d "%~dp0"

REM Portable: NO requiere administrador.
REM Quita "Mark of the Web" del ZIP de GitHub (SmartScreen), sin elevar privilegios.
powershell -NoProfile -ExecutionPolicy Bypass -Command "Get-ChildItem -LiteralPath '%~dp0' -Recurse -File -ErrorAction SilentlyContinue | Unblock-File -ErrorAction SilentlyContinue" >nul 2>&1

if not exist "%~dp0RunnerIA.exe" (
  echo ERROR: No se encontro RunnerIA.exe
  echo Baja el ZIP del Release de GitHub ^(no Code - Download ZIP^).
  pause
  exit /b 1
)

if not exist "%~dp0app\RunnerIA.Server.exe" (
  if not exist "%~dp0app\RunnerIA.exe" (
    echo ERROR: No se encontro app\RunnerIA.Server.exe
    pause
    exit /b 1
  )
)

echo RunnerIA portable - sin instalacion ni admin
start "" "%~dp0RunnerIA.exe"
exit /b 0
'@
[IO.File]::WriteAllText((Join-Path $outDir 'Iniciar-RunnerIA.bat'), $batPortable, [Text.UTF8Encoding]::new($false))

$leeme = @"
RunnerIA - programa portable (SIN administrador)
================================================

IMPORTANTE
----------
- NO pide permisos de administrador.
- NO es un instalador: descomprimis y listo.
- NO uses Code > Download ZIP de GitHub (eso es codigo fuente sin .exe).
- Usa el ZIP del Release: RunnerIA-win-x64.zip

Arranque
--------
1. Descomprimi en una carpeta de USUARIO (Desktop, Documentos, C:\Repositorio\...).
   Evita Program Files (ahi Windows puede pedir admin al escribir).
2. Doble clic en RunnerIA.exe
   o Iniciar-RunnerIA.bat (tambien desbloquea el aviso SmartScreen).
3. Si Windows muestra SmartScreen ("Windows protegio tu PC"):
   Mas informacion -> Ejecutar de todas formas
   Eso NO es instalacion ni admin; es aviso de archivo descargado.
4. PIN por defecto: 1234

Requisito
---------
WebView2 Runtime (Windows 10/11 actualizado). Si falta, el programa
NO instala nada solo; te ofrece abrir en el navegador o el link
oficial de WebView2.

Layout con pruebas
------------------
  Carpeta/
    RunnerIA-win-x64/     <- esta carpeta
    AutomatizacionSOT/
      AutomatizacionSOT/

Que incluye
-----------
- RunnerIA.exe (escritorio, asInvoker = usuario normal)
- app/RunnerIA.Server.exe (API, asInvoker)
- wwwroot/, LEEME.txt, Iniciar-RunnerIA.bat
"@
[IO.File]::WriteAllText((Join-Path $outDir 'LEEME.txt'), $leeme.Replace("`r`n", "`n").Replace("`n", "`r`n"), [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText(
    (Join-Path $outDir 'README-PORTABLE.txt'),
    "RunnerIA portable SIN admin. Doble clic RunnerIA.exe. Si SmartScreen: Mas info -> Ejecutar de todas formas. PIN 1234.",
    [Text.UTF8Encoding]::new($false))

# Validacion post-publish
$checks = @(
    @{ Path = $exeDesktop; Label = 'RunnerIA.exe (escritorio)' },
    @{ Path = $exeServer; Label = 'app\RunnerIA.Server.exe' },
    @{ Path = (Join-Path $outDir 'wwwroot'); Label = 'wwwroot' },
    @{ Path = (Join-Path $outDir 'Iniciar-RunnerIA.bat'); Label = 'Iniciar-RunnerIA.bat' },
    @{ Path = (Join-Path $outDir 'LEEME.txt'); Label = 'LEEME.txt' },
    @{ Path = (Join-Path $outDir 'version.txt'); Label = 'version.txt' }
)
foreach ($c in $checks) {
    if (-not (Test-Path $c.Path)) { Write-Error "Validacion fallida: falta $($c.Label)" }
}
Write-Host 'Validacion post-publish: OK' -ForegroundColor Green

Remove-Item $publishTmp -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item $publishDesktopTmp -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "Listo: $outDir" -ForegroundColor Green
Write-Host "Abri el programa con: $exeDesktop" -ForegroundColor Cyan
if ($Zip) {
    $zipPath = Join-Path $runnerRoot "dist\RunnerIA-$Runtime.zip"
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Compress-Archive -Path $outDir -DestinationPath $zipPath -Force
    Write-Host "ZIP: $zipPath" -ForegroundColor Green
}
