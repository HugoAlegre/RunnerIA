# Cómo revertir el piloto «Asistente QA»

Si el piloto no convence, se puede eliminar sin afectar escenarios existentes (01–31, SC-161, etc.).

## Qué agregó el piloto

1. Vista en el Runner: pestaña **Asistente QA (piloto)** en `mi-runner-operador.html`
2. API: clase `RunnerOperadorApi/AsistenteCasos.cs` + llamada `AsistenteCasos.MapEndpoints(...)` en `Program.cs`
3. Salida de borradores: `AutomatizacionSOT/Features/_pruebas/` (compilables; Ejecutar genera informe + capturas)
4. Script: `AutomatizacionSOT/run-PruebaBorrador.ps1`
5. Esta carpeta: `RunnerOperador/wwwroot/pruebas/`

## Pasos para revertir

1. Borrar `RunnerOperador/RunnerOperadorApi/AsistenteCasos.cs` y `EscenarioDef.cs` solo si no se usa en otro lado (EscenarioDef sí se usa en Program — **no borrar**)
2. Quitar la línea `AsistenteCasos.MapEndpoints(...)` de `Program.cs`
3. Quitar del HTML la pestaña, `#view-piloto`, toast y el JS del piloto
4. Borrar `AutomatizacionSOT/Features/_pruebas/` y `run-PruebaBorrador.ps1`
5. Borrar `RunnerOperador/wwwroot/pruebas/`
6. Reiniciar el Runner (`Iniciar-Runner.bat`)

Los escenarios ya productivos no se tocan.
