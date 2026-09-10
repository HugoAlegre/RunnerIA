# RunnerIA

Proyecto **independiente** (workspace aparte) — cara del programa de automatización SOT.

## Ubicación

```text
C:\Repositorio\Proyecto\
  RunnerIA\                 ← ABRÍ ESTA CARPETA EN CURSOR
  AutomatizacionSOT\        ← repo de tests (hermano)
    AutomatizacionSOT\      ← SpecFlow / appsettings
```

## Abrir en Cursor

1. **File → Open Folder** → `C:\Repositorio\Proyecto\RunnerIA`  
   **o** doble clic en `RunnerIA.code-workspace` (abre RunnerIA + carpeta de tests).

2. Arrancar: doble clic en **`Iniciar-RunnerIA.bat`**

3. Navegador: **http://localhost:5050/** (título **RunnerIA**)

## Relación con AutomatizacionSOT

| Proyecto | Rol |
|----------|-----|
| **RunnerIA** (este) | UI + API del programa |
| **AutomatizacionSOT** (hermano) | Features, steps, scripts `run-*.ps1`, secrets |

No hace falta duplicar los tests aquí. RunnerIA apunta a `..\AutomatizacionSOT\AutomatizacionSOT`.

## OneDrive

Podés tener **ambas** carpetas en OneDrive (hermanas). El build de la API sale a `%LOCALAPPDATA%\RunnerIA\` (ruta corta).

## Programa portable (ventana de escritorio)

Genera un **programa con .exe** (no abre el navegador): ventana nativa con WebView2 + API interna.

```powershell
.\Publicar-Portable.ps1
.\Publicar-Portable.ps1 -Zip
```

Salida:

```text
dist\RunnerIA-win-x64\
  RunnerIA.exe                 ← DOBLE CLIC AQUÍ (ventana de programa)
  Iniciar-RunnerIA.bat         ← atajo que lanza el .exe
  LEEME.txt
  version.txt
  app\RunnerIA.Server.exe      ← API interna (la inicia el host)
  wwwroot\
dist\RunnerIA-win-x64.zip      (con -Zip)
```

### Cómo usarlo en otro PC

```text
Carpeta/
  RunnerIA-win-x64\          ← descomprimí el ZIP aquí
  AutomatizacionSOT\
    AutomatizacionSOT\       ← csproj + run-*.ps1 + secrets
```

1. Doble clic en **`RunnerIA.exe`**
2. Se abre la ventana del programa (UI embebida)
3. Al cerrar la ventana se detiene el servidor

Requisito: **WebView2 Runtime** (incluido en Windows 10/11 actualizado). Si falta: https://developer.microsoft.com/microsoft-edge/webview2/

| Qué querés | Requisito |
|------------|-----------|
| Abrir el programa (UI / ayuda) | Solo la carpeta portable + WebView2 |
| Correr pruebas SpecFlow | Portable **+** AutomatizacionSOT hermano + Playwright browsers + SDK .NET |

Sin la suite, la UI arranca en **modo ayuda** (banner aviso); las corridas responden con un mensaje claro (HTTP 503).

El ZIP **no** incluye: AutomatizacionSOT, browsers Playwright, secretos, ni instalador MSI.

### Etapa 2 (aún no)

Empaquetar AutomatizacionSOT + prerequisitos de ejecución en el mismo ZIP / instalador.
