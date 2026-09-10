@echo off
title RunnerIA
cd /d "%~dp0"

echo.
echo  ============================================
echo   RunnerIA
echo   Proyecto independiente (workspace aparte)
echo  ============================================
echo.
echo  Tests: ..\AutomatizacionSOT\AutomatizacionSOT
echo  URL:   http://localhost:5050/
echo.
echo  NO cierres esta ventana. Detener: Ctrl+C
echo.

powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "Get-ChildItem -LiteralPath '%~dp0' -Filter '*.ps1' -ErrorAction SilentlyContinue | Unblock-File -ErrorAction SilentlyContinue"

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Start-RunnerIA.ps1" %*
if errorlevel 1 (
    echo.
    echo  ERROR al iniciar RunnerIA. Revisa los mensajes de arriba.
    pause
    exit /b 1
)
