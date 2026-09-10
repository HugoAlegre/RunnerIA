# Manual interno — Presentación RunnerIA / Runner Operador

**Público:** equipo / stakeholders  
**Duración sugerida:** 10–15 min (+ 5 min Q&A)  
**Objetivo:** mostrar que el Runner es un producto usable (no solo scripts), con flujo claro: configurar → generar → ejecutar → evidencia.

---

## 1. Antes de empezar (checklist 5 min)

| # | Chequeo | OK |
|---|---------|----|
| 1 | Doble clic en `dist\RunnerIA-win-x64\RunnerIA.exe` (ventana de programa) **o** en desarrollo `RunnerIA\Iniciar-RunnerIA.bat` | ☐ |
| 2 | Ver la UI dentro de la ventana (no hace falta Chrome/Edge aparte) | ☐ |
| 3 | Login con PIN (si pide) | ☐ |
| 4 | Pill del brandbar: **Listo** / **OK** (verde) | ☐ |
| 5 | **Configuración**: ambiente DEV, usuario SOT y al menos una contraseña guardada | ☐ |
| 6 | Tener un ticket Jira de demo **o** un texto de Detalle listo (ej. “Verificar login y apertura de caja”) | ☐ |
| 7 | Tema claro listo (o oscuro si preferís); probar el toggle una vez | ☐ |

**Demo con portable:** generá antes `.\Publicar-Portable.ps1` y dejá `AutomatizacionSOT` como carpeta hermana del `RunnerIA-win-x64`. Abrí **`RunnerIA.exe`** (no el navegador). Si falta la suite verás el banner amarillo de modo ayuda (UI sí, corridas no). Requisito: WebView2 Runtime.

**Si algo falla en vivo**

- Ventana no abre / pide WebView2 → instalar runtime Edge WebView2.
- Sin conexión al servidor → el host no pudo levantar `app\RunnerIA.Server.exe`; regenerar portable.
- Banner “Falta AutomatizacionSOT” → verificar layout hermano o `AutomatizacionSOT_ROOT`.
- QA sin Auth → usar **DEV** en la demo.
- Analizar lento → decir “puede tardar con Jira/docs”; mostrar las **fases** (Jira → Mapa → Gherkin).

---

## 2. Guion de presentación (orden recomendado)

### Slide mental 0 — Apertura (30 s)

> “Esto es **RunnerIA**: la cara del Runner Operador. Automatización SOT E2E (Playwright + SpecFlow) con una UI para operar sin tocar la consola.”

**Mostrar:** brandbar (logo Accusys, proyecto, estado Conectado, tema, Salir).

---

### Pantalla 1 — Shell / navegación (1 min)

**Qué destacar**

- Nav tipo **tabs**: Runner · Generar · Configuración · Ayuda.
- Estado del servidor siempre visible.
- Tema claro/oscuro en un botón.

**Frase**

> “La idea es que cualquier tester del equipo se ubique en menos de 10 segundos: dónde configuro, dónde genero y dónde corro.”

---

### Pantalla 2 — Configuración (2–3 min)

**Recorrido**

1. Ir a **Configuración**.
2. Señalar los **chips de salud** arriba (Jira / Acceso SOT / SQL / COBIS).
3. Índice izquierdo: saltar a **Ambiente SOT** y **Acceso**.
4. Mostrar **Ver / Ocultar** en una contraseña.
5. En Repos: card compacta → **Más** (detalle) → **Menos**.
6. Señalar la barra sticky **Guardar configuración**.

**Frase**

> “Antes era un formulario infinito. Ahora tenés estado de un vistazo, saltás por sección y el Guardar no se pierde al scrollear.”

**No hace falta** sincronizar repos en vivo (puede tardar). Solo mostrar la sección.

---

### Pantalla 3 — Generar pruebas (4–5 min) ★ núcleo demo

**Recorrido**

1. Tab **Crear**.
2. Abrir solapa **RunnerIA** (proyecto activo) — “asistente acotado a crear casos, no chat genérico”.
3. Pegar **Link Jira** *o* escribir **Detalle** en lenguaje natural.
4. Opcional: 1 archivo de documentación.
5. **Analizar** → señalar fases + toast “Borrador listo”.
6. Split: izquierda inputs / derecha **Gherkin** sticky.
7. **Guardar prueba** (botón primario cuando hay preview).
8. Tab **Módulos**: listar / editar; mencionar menú `⋯` en pantallas chicas.
9. “La **regresión** se corre desde Runner, no desde acá.”

**Frase**

> “Flujo: intención en claro → Analizar → revisar Gherkin → Guardar. Menos texto de ayuda, una acción primaria por momento.”

**Plan B (sin Jira):** solo Detalle → Analizar → mostrar Gherkin generado.

---

### Pantalla 4 — Runner (4–5 min) ★ wow factor

**Recorrido**

1. **Runner**: elegir módulo en el árbol (o buscador).
2. **Buscar** una prueba (tipear 2–3 letras).
3. Badges **OK / Falló / —** en la lista.
4. Detalle: **título del caso** (corto + descripción), ambiente, resultado esperado + **Ejecutar** (confirmación).
5. Mencionar **último ítem del módulo** = regresión del módulo (distinto de Correr todo).
6. Mientras corre: progreso + pill “En curso”.
7. Al terminar: toast + informe embebido (o “Ver informe”).
8. Acciones: Ver informe · Abrir en pestaña · Descargar ZIP.
9. Pie del sidebar: **Correr todo (N)** = regresión completa · **Comprobar instalación** = verificar build sin abrir SOT.

**Frase**

> “Ejecutar y ver evidencia sin salir de la pantalla. Regresión del módulo al final de cada lista; regresión global abajo.”

**Si no querés correr una prueba larga:** mostrar última corrida guardada + iframe del informe (si existe).

---

### Pantalla 5 — Ayuda (1–2 min, recomendado para demo)

Recorrido: **Empezar en 5 min** → **Mapa de la app** → **Ejecutar** (tipos de corrida) → **Generar** (tabla de criterios) → **Problemas frecuentes**.  
Manual técnico MANUAL.md queda **colapsado** al final.

> “Onboarding en la misma app; el manual de scripts no compite con la guía de pantalla.”

---

### Cierre (1 min)

| Mensaje | Detalle |
|---------|---------|
| Producto | UI Angular sobre la API del Runner |
| Valor | Generar casos + ejecutar + evidencia en un solo lugar |
| Próximo | Más badges históricos, toasts en más acciones, etc. (si preguntan) |

> “Preguntas: ¿lo usan más para crear casos o para correr suite/regresión?”

---

## 3. Mapa “qué mostrar” por pantalla

| Pantalla | Highlight visual | Beneficio (decir esto) |
|----------|------------------|------------------------|
| Shell | Tabs + pill Conectado | Orientación inmediata |
| Config | Chips salud + sticky Guardar | Menos errores de setup |
| Generar | Tabs + split Gherkin + fases | Flujo crear sin scroll infinito |
| Runner | Buscador, badges, informe on-demand | Operación diaria clara |
| Runner | Regresión piloto | Un click para @Piloto |

---

## 4. Timing de bolsillo (10 min)

| Min | Bloque |
|-----|--------|
| 0:00–0:30 | Apertura + brandbar |
| 0:30–2:30 | Config (chips + saltos + Guardar) |
| 2:30–7:00 | Generar (Analizar → Gherkin → Guardar) |
| 7:00–9:30 | Runner (lista → Ejecutar o última corrida → informe) |
| 9:30–10:00 | Cierre + preguntas |

---

## 5. Preguntas frecuentes (respuestas cortas)

**¿Reemplaza a Cursor / SpecFlow?**  
No. Es la consola operativa. Cursor/IA pueden armar Gherkin; acá se analiza, guarda y ejecuta.

**¿Dónde viven las pruebas?**  
`.feature` en el proyecto de automatización (piloto / módulos). El Runner las lista y corre.

**¿Las contraseñas van al navegador?**  
SOT/SQL/COBIS: secrets del **servidor**. Jira/LLM “Recordar”: opcional en el navegador.

**¿Sirve en QA?**  
Sí, con Auth OpenID permitido en Config. Para demo preferir DEV.

**¿Qué es RunnerIA?**  
Asistente **solo** para crear/mejorar casos E2E del proyecto activo (mapa + OCR; LLM opcional).

---

## 6. Frases listadas (copy rápido)

1. “De scripts en consola a una app que el equipo puede operar.”
2. “Configurar → Generar → Ejecutar → Evidencia.”
3. “Una acción primaria por momento: Analizar, después Guardar.”
4. “El informe aparece cuando hay corrida; no ensucia la pantalla.”
5. “Regresión piloto desde Runner: todas las @Piloto en un click.”

---

## 7. Riesgos de demo y mitigación

| Riesgo | Mitigación |
|--------|------------|
| Analizar falla (Jira/token) | Usar solo Detalle texto |
| Ejecutar tarda mucho | Mostrar última corrida + ZIP/informe |
| Ambiente caído | Screenshots de respaldo (opcional) |
| Lista vacía | Tener 1 módulo piloto guardado de antemano |
| Red lenta | Hablar sobre UX mientras carga; mostrar fases |

---

## 8. Preparación “día anterior”

1. Correr **una** prueba corta en DEV y dejar la última corrida visible.
2. Guardar un borrador Gherkin de demo en Generar (o un módulo con 2–3 pruebas).
3. Completar chips en verde (o al menos Acceso SOT + SQL).
4. Probar el bat en la misma máquina de la presentación.
5. Cerrar pestañas innecesarias; zoom del browser ~100–110 %.

---

*Documento interno — Runner Operador / frontend Angular. Actualizado con la UI post-mejoras (tabs, toasts, badges, split Generar, Config sticky).*
