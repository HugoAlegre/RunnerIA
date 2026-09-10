/// <summary>
/// Reglas operativas fijas que RunnerIA inyecta al LLM / memoria al generar Gherkin.
/// Mantener alineado con la suite SpecFlow y el Runner (Excel, Detener, Config SC-416).
/// </summary>
public static class ReglasGeneracion
{
    /// <summary>Texto corto para system prompts (LLM).</summary>
    public const string ReglasSystem = """
REGLAS OBLIGATORIAS DE GENERACIÓN (SOT / SpecFlow / Playwright):

1) UTF-8 y Antecedentes de login en español (canónico; ñ real en «contraseña»). Keywords Gherkin en español (# language: es):
  Antecedentes:
    Dado el usuario abre la aplicacion SOT
    Cuando ingresa el usuario configurado en el formulario de login
    Cuando ingresa la contraseña y confirma el acceso al sistema
    Entonces se muestra el popup para elegir sucursal
    Cuando busca y selecciona la sucursal configurada en el listado
    Cuando confirma la seleccion de sucursal
    Entonces el dialogo de sucursal se cierra
    Entonces se muestra el cartel de bienvenida en la pagina de inicio
Usá Característica / Escenario / Dado / Cuando / Entonces / Y (no Feature/Given/When/Then).
Nunca escribas «contraseÃ±a» ni variantes sin ñ. Si el encoding se rompe, SpecFlow no matchea LoginSteps.

2) Diálogo de sucursal (mat-autocomplete):
- Tras filtrar/elegir la opción, el botón Confirmar solo se habilita si selectedBranch quedó seteado.
- Usá siempre los steps canónicos de arriba; no inventes «elige sucursal 131» sueltos.
- No asumas que ver la opción en pantalla alcanza: el flujo real espera Confirmar habilitado y cierre del diálogo.

3) Suite vs generada:
- Borradores del asistente van a Features/_pruebas con @Prueba (y @PruebaRun_…).
- Casos ya estabilizados (p. ej. SC-416 Nota en cierre forzado) viven en Features/ (suite) con tags @SC-416 @CierreForzadoNota @SC416_CA_0N — SIN @Prueba.
- Si el ticket es SC-416 / Nota billetaje / Forzar cierre Compensada: preferí steps de CierreForzadoNota y cajas «configurada».

4) SC-416 — Nota en cierre forzado (Procesos de Sucursal → Cierre de Sucursal):
- Criterio: Compensada (tipo 10) SIN sección Nota; Normal y MiniBóveda CON Nota (efectivo).
- Steps preferidos:
  When navega a Cierre de Sucursal desde Procesos de Sucursal
  When en Cierre de Sucursal localiza la caja Compensada configurada abierta en Pesos
  When en Cierre de Sucursal localiza la caja Normal configurada abierta en Pesos
  When en Cierre de Sucursal localiza la caja MiniBoveda configurada abierta en Pesos
  When en Cierre de Sucursal abre el dialogo de Forzar cierre de esa caja
  Then el dialogo de cierre forzado no muestra la seccion Nota de billetaje
  Then el dialogo de cierre forzado muestra la seccion Nota de billetaje
  When cancela el dialogo de cierre forzado
- Correlativos editables: appsettings CierreForzadoNota (Compensada/Normal/MiniBoveda) o Runner → Config → SC-416.
- Los escenarios cancelan el diálogo (no confirman el cierre forzado).

5) Excel de casos de prueba (entrega QA; Runner «Ejecutar todos + Excel»):
Columnas exactas: ID | Título | Precondiciones | Pasos | Resultado esperado | Resultado obtenido | Estado | Observaciones
- Estado: PASS o FAIL (verde/rojo).
- Observaciones: NO obligatorias; solo si FAIL o si hubo una nota/aviso durante la ejecución.

6) Runner operativo (contexto para redactar prerrequisitos/evidencias):
- Hay botón «Ejecutar todos del módulo» y «Detener pruebas» (cancela PowerShell/dotnet en curso y corta el lote).
- Prerrequisitos críticos se resaltan si empiezan con ★ / CRITERIO: / CAJA: / OBLIGATORIO:.

7) Estilo Gherkin:
- Comentarios con # (nunca «When # comentario»).
- Preferí reutilizar steps existentes del proyecto.
- Un Feature por archivo o bloques separados por «---» en lotes.
- Tags: @SC-NNN más tags de categoría; en pruebas generadas además @Prueba (técnico, no va en el título ni en el nombre de archivo).
- Nombre de archivo: usá SC-NNN_Descripcion.feature (sin prefijo «Prueba_»). Título Feature sin «Prueba —».
""";

    /// <summary>Bloque para inyectar en el user prompt junto a ejemplos.</summary>
    public static string BloqueUserPrompt() =>
        "=== REGLAS OPERATIVAS RUNNERIA (cumplir al armar el .feature) ===\n" + ReglasSystem + "\n";
}
