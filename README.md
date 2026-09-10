# RunnerIA

Programa portable de automatización SOT (ventana de escritorio + API).

## Descargar el programa (otras máquinas)

**No uses** el botón verde **Code → Download ZIP** de GitHub: eso es solo el **código fuente** y **no trae** `RunnerIA.exe`.

### Lo correcto

1. Abrí **[Releases](https://github.com/HugoAlegre/RunnerIA/releases)**
2. Bajá el archivo **`RunnerIA-win-x64.zip`** (portable)
3. Descomprimí
4. Doble clic en **`RunnerIA.exe`**

PIN por defecto: **`1234`** (vencimiento desactivado).

**Sin administrador:** el portable no es un instalador. Descomprimí en Desktop/Documentos (no en `Program Files`). Si aparece SmartScreen (“Windows protegió tu PC”), usá **Más información → Ejecutar de todas formas** — no pide UAC de admin.

### Layout en otro PC

```text
Carpeta/
  RunnerIA-win-x64/
    RunnerIA.exe              ← programa
    Iniciar-RunnerIA.bat
    app\RunnerIA.Server.exe
    wwwroot\
  AutomatizacionSOT\          ← opcional, para correr pruebas
    AutomatizacionSOT\
```

| Qué querés | Qué necesitás |
|------------|----------------|
| Abrir el programa (UI / ayuda / config) | Solo el ZIP portable + WebView2 |
| Correr pruebas SpecFlow | Portable + carpeta hermana AutomatizacionSOT + Playwright + .NET SDK |

Requisito Windows: **WebView2 Runtime** (suele venir con Windows 10/11). Si falta: https://developer.microsoft.com/microsoft-edge/webview2/

## Desarrolladores (código fuente)

Repo: https://github.com/HugoAlegre/RunnerIA

```text
C:\Repositorio\Proyecto\
  RunnerIA\                 ← este proyecto
  AutomatizacionSOT\        ← tests (hermano)
```

Arranque en desarrollo:

```powershell
.\Iniciar-RunnerIA.bat
```

Generar portable localmente:

```powershell
.\Publicar-Portable.ps1 -Zip
```

Salida: `dist\RunnerIA-win-x64\` y `dist\RunnerIA-win-x64.zip` (el mismo que se sube al Release).

## Relación con AutomatizacionSOT

| Proyecto | Rol |
|----------|-----|
| **RunnerIA** | UI + API + Desktop |
| **AutomatizacionSOT** | Features, steps, scripts `run-*.ps1`, secrets |

El ZIP portable **no** incluye AutomatizacionSOT, browsers Playwright ni secretos.
