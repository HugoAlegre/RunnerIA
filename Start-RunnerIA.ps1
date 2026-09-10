# Inicia RunnerIA (proyecto aparte) y abre la UI en el navegador.
# Estructura esperada (hermanos):
#   <Proyecto>/RunnerIA/                 ← ESTE proyecto (abrir en Cursor)
#   <Proyecto>/AutomatizacionSOT/        ← repo de tests
#     AutomatizacionSOT/                 ← csproj SpecFlow
#
# Entrada: Iniciar-RunnerIA.bat
param(
    [switch]$SinAbrirNavegador,
    [switch]$SinBuildFrontend,
    [int]$Puerto = 5050
)

if ((Get-ExecutionPolicy -Scope Process) -ne 'Bypass') {
    $argList = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $PSCommandPath)
    foreach ($a in $args) { $argList += $a }
    & powershell.exe @argList
    exit $LASTEXITCODE
}

$ErrorActionPreference = 'Stop'

# Consola UTF-8 (evita "fallÃ³" y tildes rotas en cmd.exe)
try {
    [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
    $OutputEncoding = [Console]::OutputEncoding
    if ($env:OS -match 'Windows') { chcp 65001 | Out-Null }
} catch { }

$runnerRoot = $PSScriptRoot
$apiDir = Join-Path $runnerRoot 'RunnerOperadorApi'
$proj = Join-Path $apiDir 'RunnerOperadorApi.csproj'
$frontendDir = Join-Path $runnerRoot 'frontend'
# Repo hermano: ..\AutomatizacionSOT\AutomatizacionSOT
$autoRoot = Join-Path (Split-Path $runnerRoot -Parent) 'AutomatizacionSOT\AutomatizacionSOT'
$nodeVersion = 'v24.18.0'
$dotnetChannel = '9.0'

function Test-DotNetSdk9Ready {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        return $false
    }

    try {
        $list = & dotnet --list-sdks 2>$null
        if (-not $list) {
            $ver = (& dotnet --version 2>$null).Trim()
            return $ver -match '^9\.'
        }
        return [bool]($list | Where-Object { $_ -match '^9\.' })
    } catch {
        return $false
    }
}

function Ensure-PortableDotNet([string]$Root, [string]$Channel) {
    $installDir = Join-Path $Root '.tools\dotnet'
    $dotnetExe = Join-Path $installDir 'dotnet.exe'
    if (Test-Path $dotnetExe) {
        return $installDir
    }

    if (Test-DotNetSdk9Ready) {
        return $null
    }

    Write-Host ".NET 9 SDK no encontrado. Instalando SDK portable (solo la primera vez, ~200 MB)..." -ForegroundColor Yellow
    $toolsDir = Join-Path $Root '.tools'
    New-Item -ItemType Directory -Force -Path $toolsDir | Out-Null
    $installScript = Join-Path $toolsDir 'dotnet-install.ps1'
    try {
        if (-not (Test-Path $installScript)) {
            Invoke-WebRequest -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installScript -UseBasicParsing
        }

        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $installScript `
            -Channel $Channel `
            -Quality 'ga' `
            -InstallDir $installDir `
            -Architecture 'x64'
    } catch {
        Write-Error @"
No se pudo instalar .NET SDK portable en $installDir.
Instalá .NET 9 SDK desde https://dotnet.microsoft.com/download o verificá red/proxy.
Detalle: $($_.Exception.Message)
"@
    }

    if (-not (Test-Path $dotnetExe)) {
        Write-Error "Instalación .NET portable incompleta en $installDir"
    }

    Write-Host ".NET SDK portable listo en $installDir" -ForegroundColor Green
    return $installDir
}

function Use-DotNetFrom([string]$Dir) {
    if ([string]::IsNullOrWhiteSpace($Dir)) {
        return
    }

    $env:DOTNET_ROOT = $Dir
    $env:DOTNET_MULTILEVEL_LOOKUP = '0'
    $env:Path = "$Dir;" + $env:Path
}

function Ensure-PortableNode([string]$Root, [string]$Version) {
    $portableNode = Join-Path $Root ".tools\node\node-$Version-win-x64"
    $nodeExe = Join-Path $portableNode 'node.exe'
    if (Test-Path $nodeExe) {
        return $portableNode
    }

    if (Get-Command npm -ErrorAction SilentlyContinue) {
        return $null
    }

    Write-Host "Node/npm no encontrado. Descargando Node.js portable $Version (solo la primera vez)..." -ForegroundColor Yellow
    $toolsDir = Join-Path $Root '.tools\node'
    New-Item -ItemType Directory -Force -Path $toolsDir | Out-Null
    $zipName = "node-$Version-win-x64.zip"
    $zipPath = Join-Path $toolsDir $zipName
    $zipUrl = "https://nodejs.org/dist/$Version/$zipName"
    try {
        Invoke-WebRequest -Uri $zipUrl -OutFile $zipPath -UseBasicParsing
        Expand-Archive -LiteralPath $zipPath -DestinationPath $toolsDir -Force
        Remove-Item $zipPath -Force -ErrorAction SilentlyContinue
    } catch {
        Write-Error @"
No se pudo descargar Node portable ($zipUrl).
Instalá Node.js 20+ LTS desde https://nodejs.org o verificá red/proxy.
Detalle: $($_.Exception.Message)
"@
    }

    if (-not (Test-Path $nodeExe)) {
        Write-Error "Node portable incompleto en $portableNode"
    }

    Write-Host "Node portable listo en $portableNode" -ForegroundColor Green
    return $portableNode
}

if (-not (Test-Path $proj)) {
    Write-Error "No se encontró RunnerOperadorApi en: $proj"
}
if (-not (Test-Path (Join-Path $autoRoot 'AutomatizacionSOT.csproj'))) {
    Write-Error "No se encontró la automatización en: $autoRoot. ¿Moviste las carpetas?"
}

$portableDotnet = Ensure-PortableDotNet -Root $runnerRoot -Channel $dotnetChannel
Use-DotNetFrom $portableDotnet

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error "No se encontró 'dotnet'. Reintentá con internet o instalá .NET 9 SDK desde https://dotnet.microsoft.com/download"
}

# Node portable del repo (descarga automática si falta) o Node del sistema para build Angular
$portableNode = Ensure-PortableNode -Root $runnerRoot -Version $nodeVersion
if ($portableNode) {
    $env:Path = "$portableNode;" + $env:Path
}

function Test-RunnerHealth {
    try {
        $r = Invoke-RestMethod "http://localhost:$Puerto/health" -TimeoutSec 2
        return [bool]$r.ok
    } catch {
        return $false
    }
}

function Get-ListenerPidOnPort([int]$port) {
    try {
        $c = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($c) { return $c.OwningProcess }
    } catch { }
    return $null
}

if (Test-RunnerHealth) {
    Write-Host "El servidor YA está corriendo en http://localhost:$Puerto/" -ForegroundColor Yellow
    if (-not $SinAbrirNavegador) {
        Start-Process "http://localhost:$Puerto/"
    }
    Write-Host "Si querés reiniciarlo, cerrá la otra ventana del servidor (Ctrl+C) y volvé a ejecutar." -ForegroundColor DarkGray
    Write-Host ""
    Read-Host "Enter para cerrar esta ventana (el servidor sigue en la otra)"
    exit 0
}

$pidEnPuerto = Get-ListenerPidOnPort $Puerto
if ($pidEnPuerto) {
    $proc = Get-Process -Id $pidEnPuerto -ErrorAction SilentlyContinue
    if ($proc -and $proc.ProcessName) {
        $procName = $proc.ProcessName
    } else {
        $procName = "pid $pidEnPuerto"
    }
    Write-Warning "Puerto $Puerto ocupado por $procName pero /health no responde."
    if ($procName -match 'RunnerIA|RunnerOperadorApi|dotnet') {
        Write-Host "Deteniendo proceso anterior ($procName, PID $pidEnPuerto)..." -ForegroundColor Yellow
        Stop-Process -Id $pidEnPuerto -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 2
    } else {
        Write-Host "Cierra manualmente el proceso que usa el puerto $Puerto o usa otro puerto." -ForegroundColor Red
        Read-Host "Enter para cerrar"
        exit 1
    }
}

Write-Host '=== RunnerIA (Angular + API) ===' -ForegroundColor Cyan
Write-Host "Proyecto:       $runnerRoot" -ForegroundColor DarkGray
Write-Host "Automatizacion: $autoRoot" -ForegroundColor DarkGray

if ($runnerRoot -match 'OneDrive' -or $runnerRoot.Length -gt 90) {
    Write-Warning "Ruta larga/OneDrive. El build usa %LOCALAPPDATA%\RunnerIA\ (sin tocar tu PC)."
}

$buildRoot = Join-Path $env:LOCALAPPDATA 'RunnerIA'
$apiOutDir = Join-Path $buildRoot 'bin\Debug\net9.0-windows10.0.19041.0'
# Limpiar obj/bin viejos del proyecto (TFM anterior o rutas largas) para no mezclar AssemblyInfo.
foreach ($legacy in @(
        (Join-Path $apiDir 'bin'),
        (Join-Path $apiDir 'obj')
    )) {
    if (Test-Path $legacy) {
        Remove-Item -LiteralPath $legacy -Recurse -Force -ErrorAction SilentlyContinue
    }
}

$angularIndex = Join-Path $runnerRoot 'wwwroot\app\index.html'
if (-not $SinBuildFrontend -and (Test-Path $frontendDir)) {
    if (-not (Get-Command npm -ErrorAction SilentlyContinue)) {
        Write-Warning "No se encontró npm. Se usará el Frontend ya compilado (si existe) o el HTML legacy."
    } else {
        Write-Host "Compilando Frontend Angular..." -ForegroundColor DarkGray
        Push-Location $frontendDir
        try {
            if (-not (Test-Path 'node_modules')) {
                $prevNpmLog = $env:npm_config_loglevel
                $env:npm_config_loglevel = 'error'
                try {
                    npm install --no-fund --no-audit
                } finally {
                    if ($null -eq $prevNpmLog) {
                        Remove-Item Env:npm_config_loglevel -ErrorAction SilentlyContinue
                    } else {
                        $env:npm_config_loglevel = $prevNpmLog
                    }
                }
                if ($LASTEXITCODE -ne 0) { throw "npm install fallo (codigo $LASTEXITCODE)" }
            }
            npm run build
            if ($LASTEXITCODE -ne 0) { throw "ng build fallo (codigo $LASTEXITCODE)" }
        } finally {
            Pop-Location
        }
    }
}

if (-not (Test-Path $angularIndex)) {
    Write-Warning "No está wwwroot/app/index.html. La API puede caer al HTML legacy en wwwroot/_legacy."
}

Write-Host "Compilando API local (salida: $apiOutDir)..." -ForegroundColor DarkGray
dotnet build $proj -c Debug
if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "Fallo el build. Si ves MSB3021 / ruta > 260 caracteres:" -ForegroundColor Red
    Write-Host "  1) git clone a C:\Repositorio\AutomatizacionSOT (evitar OneDrive + ZIP)" -ForegroundColor Yellow
    Write-Host "  2) Volvé a ejecutar Iniciar-RunnerIA.bat" -ForegroundColor Yellow
    Read-Host "Enter para cerrar"
    exit $LASTEXITCODE
}

$apiDll = Join-Path $apiOutDir 'RunnerIA.dll'
if (-not (Test-Path $apiDll)) {
    # Compat si el AssemblyName aun no se regenero
    $apiDllAlt = Join-Path $apiOutDir 'RunnerOperadorApi.dll'
    if (Test-Path $apiDllAlt) { $apiDll = $apiDllAlt }
}
if (-not (Test-Path $apiDll)) {
    Write-Error "No se generó RunnerIA.dll en $apiOutDir"
}

# --- Workaround: runtime .NET desparejo (p. ej. ASP.NET Core 9.0.18 instalado sin
#     Microsoft.NETCore.App 9.0.18). En ese estado la API muere al arrancar con
#     "You must install or update .NET". Detectamos el desfase y fijamos la versión
#     más alta que exista en AMBOS runtimes editando el runtimeconfig.json del build.
#     Se soluciona de raíz instalando (como admin): winget install Microsoft.DotNet.Runtime.9
try {
    $sharedRoot = Join-Path $env:ProgramFiles 'dotnet\shared'
    $netcoreVers = @(Get-ChildItem (Join-Path $sharedRoot 'Microsoft.NETCore.App') -Directory -ErrorAction Stop | ForEach-Object Name)
    $aspnetVers  = @(Get-ChildItem (Join-Path $sharedRoot 'Microsoft.AspNetCore.App') -Directory -ErrorAction Stop | ForEach-Object Name)
    $aspnet9     = @($aspnetVers | Where-Object { $_ -like '9.*' } | Sort-Object { [version]$_ })
    $comunes9    = @($aspnet9 | Where-Object { $netcoreVers -contains $_ })
    if ($aspnet9.Count -gt 0 -and $comunes9.Count -gt 0 -and ($netcoreVers -notcontains $aspnet9[-1])) {
        $pin = $comunes9[-1]
        Write-Warning "Runtime .NET desparejo (ASP.NET $($aspnet9[-1]) sin NETCore igual). Se fija la API a $pin."
        Get-ChildItem $apiOutDir -Filter 'RunnerIA.runtimeconfig.json' -ErrorAction SilentlyContinue | ForEach-Object {
            $json = Get-Content $_.FullName -Raw | ConvertFrom-Json
            foreach ($fw in $json.runtimeOptions.frameworks) {
                $fw.version = $pin
                $fw | Add-Member -NotePropertyName rollForward -NotePropertyValue 'Disable' -Force
            }
            $json | ConvertTo-Json -Depth 10 | Set-Content $_.FullName -Encoding UTF8
        }
    }
} catch {
    Write-Warning "No se pudo verificar runtimes .NET: $($_.Exception.Message)"
}

$url = "http://localhost:$Puerto/"
Write-Host ""
Write-Host "Listo. Abrí en el navegador:" -ForegroundColor Green
Write-Host "  $url" -ForegroundColor White
Write-Host ""
Write-Host "Doble clic en: Iniciar-RunnerIA.bat" -ForegroundColor DarkGray
Write-Host "Detené el servidor con Ctrl+C en esta ventana." -ForegroundColor DarkGray
Write-Host ""

if (-not $SinAbrirNavegador) {
    Start-Process $url
}

$env:ASPNETCORE_URLS = "http://localhost:$Puerto"
$env:ASPNETCORE_CONTENTROOT = $apiDir
$env:AutomatizacionSOT_ROOT = $autoRoot
try {
    Push-Location $apiDir
    try {
        & dotnet $apiDll
    } finally {
        Pop-Location
    }
    if ($LASTEXITCODE -ne 0) {
        Write-Host ""
        Write-Host "El servidor terminó con error (código $LASTEXITCODE)." -ForegroundColor Red
        Read-Host "Enter para cerrar"
        exit $LASTEXITCODE
    }
} catch {
    Write-Host ""
    Write-Host "ERROR al iniciar la API: $($_.Exception.Message)" -ForegroundColor Red
    Read-Host "Enter para cerrar"
    exit 1
}
