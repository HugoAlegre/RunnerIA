# Mini manual — cómo generar pruebas

Podés armar una prueba de **dos formas**. En ambos casos el resultado es un escenario **Gherkin** (`.feature`) que corre con **SpecFlow** + **Playwright** contra SOT.

## 1) Automático (recomendado si hay ticket)

1. En **Configuración → Jira**, cargá una vez el **email** y el **API token** de Atlassian (se guardan solo en este navegador). Podés usar **Probar conexión Jira**.
2. En **Generar pruebas**, pegá solo el **Link Jira** del ticket.
3. Opcional: adjuntá **Documentación**.
4. Tocá **Analizar y proponer feature**: lee el ticket (descripción, adjuntos de texto, comentarios **funcionales**), sugiere **módulo** y arma uno o más **escenarios**.
5. Al **Ejecutar**, cada paso de UI genera **captura PNG + informe Extent** (igual que la suite SOT).
6. Revisá el Gherkin y **Guardá**.

Token Atlassian: https://id.atlassian.com/manage-profile/security/api-tokens (no es el de GitHub).

## 2) Manual

1. Elegí o creá el **módulo**.
2. Escribí el **nombre del feature** y el **detalle**.
3. Describí en claro qué se prueba (pasos, datos, resultado esperado).
4. **Analizar** → revisar Gherkin → **Guardar**.

## Orden de los campos

| Campo | Significado |
|-------|-------------|
| **Módulo** | Carpeta (ej. Alta de caja) |
| **Nombre del feature** | Cómo se llama la prueba en la lista |
| **Detalle** | Qué se quiere lograr |

## Tecnología que usa esta pantalla

| Tecnología | Para qué |
|------------|----------|
| **Jira REST API** | Lee el ticket con el link (credenciales en Configuración) |
| **Gherkin / SpecFlow** | Formato del escenario (`.feature`) |
| **Playwright** | Ejecuta la prueba en el navegador contra SOT |
| **Runner Operador** (API local) | Analiza, guarda y dispara la corrida |

## Ejemplo manual (si no usás Jira)

```
Abrir una caja Normal en pesos.
Usuario: cajero.
Fondo inicial: 1000.
Al finalizar, la caja debe quedar abierta y operativa.
```
