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

Write-Host "dotnet publish Desktop ($Runtime) -> host\RunnerIA.exe..." -ForegroundColor DarkGray
dotnet publish $desktopProj -c Release -r $Runtime --self-contained true -o $publishDesktopTmp `
    /p:PublishSingleFile=false /p:AssemblyName=RunnerIA /p:Product=RunnerIA
if ($LASTEXITCODE -ne 0) { Write-Error "publish Desktop fallo" }

# Layout limpio: runtime del host en host/ (la raiz solo tiene lanzadores + app + wwwroot).
# Asi en otra PC no hay que buscar RunnerIA.exe entre cientos de DLL.
$hostDir = Join-Path $outDir 'host'
New-Item -ItemType Directory -Force -Path $hostDir | Out-Null
Copy-Item (Join-Path $publishDesktopTmp '*') $hostDir -Recurse -Force
$exeDesktop = Join-Path $hostDir 'RunnerIA.exe'
if (-not (Test-Path $exeDesktop)) {
    Write-Error "No se genero host\RunnerIA.exe en $hostDir"
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
$versionLines += "Host: host\RunnerIA.exe (WebView2)"
$versionLines += "API: app\RunnerIA.Server.exe"
$versionLines += "Admin: NO (asInvoker)"
[IO.File]::WriteAllLines((Join-Path $outDir 'version.txt'), $versionLines, [Text.UTF8Encoding]::new($false))

# Launchers ASCII-only (CMD en PCs corporativas falla con UTF-8/PowerShell bloqueado).
$batPortable = @'
@echo off
setlocal EnableExtensions
title RunnerIA
cd /d "%~dp0"

REM Portable: no admin, no instalador.
REM Desbloqueo MotW solo del lanzador/host (sin PowerShell obligatorio para arrancar).
if exist "%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" (
  "%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -Command "Unblock-File -LiteralPath '%~f0' -EA SilentlyContinue; Unblock-File -LiteralPath '%~dp0host\RunnerIA.exe' -EA SilentlyContinue; Unblock-File -LiteralPath '%~dp0app\RunnerIA.Server.exe' -EA SilentlyContinue" >nul 2>&1
)

set "HOSTEXE=%~dp0host\RunnerIA.exe"
if not exist "%HOSTEXE%" set "HOSTEXE=%~dp0RunnerIA.exe"
if not exist "%HOSTEXE%" (
  echo.
  echo ERROR: No se encontro host\RunnerIA.exe
  echo.
  echo 1^) Baja RunnerIA-win-x64.zip desde GitHub Releases
  echo    https://github.com/HugoAlegre/RunnerIA/releases
  echo 2^) NO uses Code - Download ZIP ^(eso es codigo fuente^)
  echo 3^) Descomprimi TODO el ZIP y abre ESTA carpeta
  echo.
  echo Archivos .exe en esta carpeta:
  dir /b "%~dp0*.exe" 2>nul
  dir /b "%~dp0host\*.exe" 2>nul
  echo.
  pause
  exit /b 1
)

if not exist "%~dp0app\RunnerIA.Server.exe" (
  if not exist "%~dp0app\RunnerIA.exe" (
    echo ERROR: Falta app\RunnerIA.Server.exe ^(ZIP incompleto^)
    pause
    exit /b 1
  )
)

echo Iniciando RunnerIA ^(sin instalacion ni administrador^)...
start "" "%HOSTEXE%"
exit /b 0
'@
# CMD-friendly: ANSI/ASCII bytes
[IO.File]::WriteAllText((Join-Path $outDir 'Iniciar-RunnerIA.bat'), $batPortable, [Text.Encoding]::ASCII)
[IO.File]::WriteAllText((Join-Path $outDir '0-ABRIR-RunnerIA.bat'), $batPortable, [Text.Encoding]::ASCII)

# Copia visible del nombre que la gente busca (atajo .cmd a host)
$cmdShortcut = @"
@echo off
cd /d "%~dp0"
call "%~dp00-ABRIR-RunnerIA.bat"
"@
[IO.File]::WriteAllText((Join-Path $outDir 'RunnerIA.cmd'), $cmdShortcut, [Text.Encoding]::ASCII)

$leeme = @"
RunnerIA - programa portable (SIN administrador)
================================================

COMO ABRIR (cualquier PC Windows 10/11 x64)
------------------------------------------
1. Descomprimi RunnerIA-win-x64.zip completo.
2. Entra a la carpeta RunnerIA-win-x64.
3. Doble clic en:  0-ABRIR-RunnerIA.bat
   (tambien sirve Iniciar-RunnerIA.bat o RunnerIA.cmd)

NO hace falta instalar nada ni ser administrador.
NO uses Code > Download ZIP de GitHub (eso NO trae el programa).
Usa el ZIP del Release: RunnerIA-win-x64.zip

Donde esta el .exe
------------------
El programa esta en:  host\RunnerIA.exe
La raiz se dejo limpia a proposito (solo lanzadores + LEEME).
No busques el .exe entre DLLs: usa el .bat de arriba.

Si Windows muestra SmartScreen
------------------------------
Mas informacion -> Ejecutar de todas formas
Eso NO es instalacion ni admin; es aviso de archivo bajado de internet.

PIN por defecto: 1234

Requisito
---------
WebView2 Runtime (Windows 10/11 actualizado). Si falta, el programa
NO instala nada solo; ofrece abrir en el navegador.

Layout
------
  RunnerIA-win-x64\
    0-ABRIR-RunnerIA.bat   <-- ABRIR AQUI
    Iniciar-RunnerIA.bat
    RunnerIA.cmd
    LEEME.txt
    host\RunnerIA.exe      <-- programa
    app\RunnerIA.Server.exe
    wwwroot\
"@
[IO.File]::WriteAllText((Join-Path $outDir 'LEEME.txt'), $leeme.Replace("`r`n", "`n").Replace("`n", "`r`n"), [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText(
    (Join-Path $outDir 'README-PORTABLE.txt'),
    "ABRIR: doble clic en 0-ABRIR-RunnerIA.bat  |  EXE: host\RunnerIA.exe  |  SIN admin  |  PIN 1234  |  Baja desde Releases, no Code ZIP.",
    [Text.Encoding]::ASCII)

# Validacion post-publish
$checks = @(
    @{ Path = $exeDesktop; Label = 'host\RunnerIA.exe (escritorio)' },
    @{ Path = $exeServer; Label = 'app\RunnerIA.Server.exe' },
    @{ Path = (Join-Path $outDir 'wwwroot'); Label = 'wwwroot' },
    @{ Path = (Join-Path $outDir '0-ABRIR-RunnerIA.bat'); Label = '0-ABRIR-RunnerIA.bat' },
    @{ Path = (Join-Path $outDir 'Iniciar-RunnerIA.bat'); Label = 'Iniciar-RunnerIA.bat' },
    @{ Path = (Join-Path $outDir 'LEEME.txt'); Label = 'LEEME.txt' },
    @{ Path = (Join-Path $outDir 'version.txt'); Label = 'version.txt' }
)
foreach ($c in $checks) {
    if (-not (Test-Path $c.Path)) { Write-Error "Validacion fallida: falta $($c.Label)" }
}

# Raiz limpia: no debe haber coreclr.dll ni cientos de DLL mezclados con el lanzador
$rootDll = @(Get-ChildItem -LiteralPath $outDir -File -Filter '*.dll' -ErrorAction SilentlyContinue)
if ($rootDll.Count -gt 0) {
    Write-Error ("Validacion fallida: la raiz tiene DLL ({0}). Deben estar solo en host\ y app\." -f $rootDll[0].Name)
}
$rootExe = @(Get-ChildItem -LiteralPath $outDir -File -Filter '*.exe' -ErrorAction SilentlyContinue)
if ($rootExe.Count -gt 0) {
    Write-Error ("Validacion fallida: la raiz tiene .exe ({0}). El host debe vivir en host\." -f $rootExe[0].Name)
}

Write-Host 'Validacion post-publish: OK (raiz limpia, host\ + app\)' -ForegroundColor Green

Remove-Item $publishTmp -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item $publishDesktopTmp -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "Listo: $outDir" -ForegroundColor Green
Write-Host "Abri con: $(Join-Path $outDir '0-ABRIR-RunnerIA.bat')" -ForegroundColor Cyan
Write-Host "O directo: $exeDesktop" -ForegroundColor Cyan
if ($Zip) {
    $zipPath = Join-Path $runnerRoot "dist\RunnerIA-$Runtime.zip"
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Compress-Archive -Path $outDir -DestinationPath $zipPath -Force
    Write-Host "ZIP: $zipPath" -ForegroundColor Green
}
