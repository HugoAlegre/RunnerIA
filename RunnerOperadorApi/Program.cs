using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AdoNetCore.AseClient;
using AutomatizacionSOT.Config;
using Microsoft.Data.SqlClient;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    // Solo localhost: el Runner es intranet; AllowAnyOrigin permitía abuso desde cualquier origen.
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins(
                "http://localhost:5050",
                "http://127.0.0.1:5050",
                "http://localhost:4200",
                "http://127.0.0.1:4200")
            .AllowAnyMethod()
            .AllowAnyHeader());
});

// Varios docs por ticket (Jira + adjuntos locales) al Analizar.
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o =>
{
    o.MultipartBodyLengthLimit = 80 * 1024 * 1024; // 80 MB
    o.ValueLengthLimit = 80 * 1024 * 1024;
    o.MultipartHeadersLengthLimit = 64 * 1024;
    o.MemoryBufferThreshold = 2 * 1024 * 1024;
});
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 80 * 1024 * 1024);

builder.WebHost.UseUrls("http://localhost:5050");

var app = builder.Build();

app.UseCors();

// Estructura:
//   <repo>/AutomatizacionSOT/          ← tests, scripts, appsettings
//   <repo>/RunnerOperador/
//     RunnerOperadorApi/               ← este proyecto (ContentRoot)
//     wwwroot/                         ← HTML, logo, manuals frontend
// Se puede overridear con la variable de entorno AutomatizacionSOT_ROOT.
var contentRoot = app.Environment.ContentRootPath;
var runnerRoot = Path.GetFullPath(Path.Combine(contentRoot, ".."));
var wwwRoot = Path.Combine(runnerRoot, "wwwroot");
var (automatizacionRoot, automatizacionOk, automatizacionTried) = ResolveAutomatizacionRoot(contentRoot);
var automatizacionBuscado = automatizacionRoot;
if (!automatizacionOk)
{
    // Modo ayuda: UI y auth locales sin suite SpecFlow al lado.
    var fallback = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RunnerIA",
        "modo-ayuda");
    Directory.CreateDirectory(fallback);
    automatizacionRoot = fallback;
}

var runStore = new RunStore();
var runLock = new SemaphoreSlim(1, 1);

var escenarios = new Dictionary<string, EscenarioDef>(StringComparer.Ordinal)
{
    // Regresión estándar: Alta + Intercaja (01–05) en una sola corrida.
    ["00"] = new("Regresión", "run-TodosLosFeatures.ps1", []),
    ["01"] = new("Alta sin COE", "run-AltaSinEquivalencia.ps1", []),
    ["02"] = new("Alta con apertura", "run-AltaCajaConEquivalencia.ps1", []),
    ["02-USD"] = new("Alta con apertura USD", "run-AltaCajaConEquivalencia.ps1", ["-Moneda", "Usd"]),
    ["02-EUR"] = new("Alta con apertura EUR", "run-AltaCajaConEquivalencia.ps1", ["-Moneda", "Eur"]),
    ["03"] = new("Interpase aceptado", "run-Intercaja.ps1", ["-Solo", "Aceptado"]),
    ["04"] = new("Interpase anulado", "run-AnulacionPaseIntercaja.ps1", []),
    ["05"] = new("Limpieza interpases", "run-LimpiezaPasesIntercaja.ps1", []),
    // Opcional: solo con caja bóveda asignada (no entra en run-TodosLosFeatures.ps1 por defecto).
    ["06"] = new("Pases Caja-Bóveda error COE", "run-PasesCajaBovedaNegativo.ps1", []),
    ["03-USD"] = new("Interpase aceptado USD", "run-Intercaja.ps1", ["-Solo", "UsdAceptado"]),
    ["04-USD"] = new("Interpase anulado USD", "run-Intercaja.ps1", ["-Solo", "UsdAnulado"]),
    ["03-EUR"] = new("Interpase aceptado EUR", "run-Intercaja.ps1", ["-Solo", "EurAceptado"]),
    ["04-EUR"] = new("Interpase anulado EUR", "run-Intercaja.ps1", ["-Solo", "EurAnulado"]),
    ["06-USD"] = new("Pases Caja-Bóveda error COE (USD)", "run-PasesCajaBovedaNegativo.ps1", ["-Solo", "Usd"]),

    // Cierre / cuadre · sucursal configurada por el usuario.
    ["30"] = new("Parametría — listado de cajas", "run-ParametriaCajasSmoke.ps1", []),
    ["31"] = new("Cuadre y cierre completo (smoke)", "run-CierreCuadreSmoke.ps1", []),
    ["31-CA01"] = new("Cierre sin diferencia COBIS", "run-CierreCuadreModulo.ps1", ["-Solo", "CA-01"]),
    ["31-CA02"] = new("Cierre con diferencia COBIS — aceptar", "run-CierreCuadreModulo.ps1", ["-Solo", "CA-02"]),
    ["31-CA03"] = new("Eliminar Cierre de Caja", "run-CierreCuadreModulo.ps1", ["-Solo", "CA-03"]),
    ["31-CA04"] = new("Cierre con sobrante — sot1", "run-CierreCuadreModulo.ps1", ["-Solo", "CA-04"]),
    ["31-CA05"] = new("Cierre con faltante — sot1", "run-CierreCuadreModulo.ps1", ["-Solo", "CA-05"]),
    ["31-CN01"] = new("Cierre con diferencia COBIS — cancelar", "run-CierreCuadreModulo.ps1", ["-Solo", "CN-01"]),

    // SC-416 — Nota en cierre forzado (suite mejoras, suite).
    ["32"] = new("SC-416 — suite cierre forzado (todos)", "run-SC416-CierreForzadoNota.ps1", []),
    ["32-CA01"] = new("SC-416 CA-01 Compensada sin Nota", "run-SC416-CierreForzadoNota.ps1", ["-Solo", "CA-01"]),
    ["32-CA02"] = new("SC-416 CA-02 Normal con Nota", "run-SC416-CierreForzadoNota.ps1", ["-Solo", "CA-02"]),
    ["32-CA03"] = new("SC-416 CA-03 MiniBóveda con Nota", "run-SC416-CierreForzadoNota.ps1", ["-Solo", "CA-03"]),

    // Categoría: Depósito e Interdepósito de cheques
    ["08"] = new("Conexión COBIS", "run-DepositoInterdepositoCheques.ps1", ["-Solo", "ConexionCobis"]),
    ["09"] = new("Interdepósito ahorros otra sucursal", "run-DepositoInterdepositoCheques.ps1", ["-Solo", "Interdeposito"]),
    ["10"] = new("Depósito/Interdepósito completo", "run-DepositoInterdepositoCheques.ps1", ["-Solo", "Completo"]),
    ["11"] = new("Interdepósito judicial", "run-DepositoChequesFiltro.ps1", ["-Categoria", "DepositoInterdepositoJudicial"]),
    ["12"] = new("Interdepósito corriente", "run-DepositoInterdepositoCheques.ps1", ["-Solo", "Corriente"]),
    ["13"] = new("Cuenta bloqueada (negativo)", "run-DepositoChequesFiltro.ps1", ["-Categoria", "DepositoInterdepositoBloqueado"]),
    ["14"] = new("Sin depósito de cheques (negativo)", "run-DepositoChequesFiltro.ps1", ["-Categoria", "DepositoInterdepositoSinDepositoCheques"]),
    ["15"] = new("Cancelar judicial (negativo)", "run-DepositoChequesFiltro.ps1", ["-Categoria", "DepositoInterdepositoCancelarJudicial"]),
    ["16"] = new("Boleta inválida (negativo)", "run-DepositoChequesFiltro.ps1", ["-Categoria", "DepositoInterdepositoBoletaInvalida"]),
    ["17"] = new("Boleta no numérica (negativo)", "run-DepositoChequesFiltro.ps1", ["-Categoria", "DepositoInterdepositoBoletaNoNumerica"]),
    ["18-USD"] = new("Depósito USD misma sucursal", "run-DepositoChequesFiltro.ps1", ["-Categoria", "DepositoUsdMismaSucursal", "-Usd"]),
    ["19-USD"] = new("Interdepósito USD otra sucursal", "run-DepositoChequesFiltro.ps1", ["-Categoria", "DepositoInterdepositoUsd", "-Usd"]),

    // Retiro de efectivo — 12 escenarios (CA/CC/Judicial × titularidad)
    ["retiro-r01"] = new("R01 CA pesos individual", "run-RetiroEfectivo.ps1", ["-Filtro", "Category=RetiroCaIndividual"]),
    ["retiro-r02"] = new("R02 CA pesos conjunta", "run-RetiroEfectivo.ps1", ["-Filtro", "Category=RetiroCaConjunta"]),
    ["retiro-r03"] = new("R03 CA pesos indistinta", "run-RetiroEfectivo.ps1", ["-Filtro", "Category=RetiroCaIndistinta"]),
    ["retiro-r04"] = new("R04 CA pesos categorizada", "run-RetiroEfectivo.ps1", ["-Filtro", "Category=RetiroCaCategorizada"]),
    ["retiro-r05"] = new("R05 CC pesos individual", "run-RetiroEfectivo.ps1", ["-Filtro", "Category=RetiroCcIndividual"]),
    ["retiro-r06"] = new("R06 CC pesos conjunta", "run-RetiroEfectivo.ps1", ["-Filtro", "Category=RetiroCcConjunta"]),
    ["retiro-r07"] = new("R07 CC pesos indistinta", "run-RetiroEfectivo.ps1", ["-Filtro", "Category=RetiroCcIndistinta"]),
    ["retiro-r08"] = new("R08 CC pesos categorizada", "run-RetiroEfectivo.ps1", ["-Filtro", "Category=RetiroCcCategorizada"]),
    ["retiro-r09"] = new("R09 CA judicial individual", "run-RetiroEfectivo.ps1", ["-Filtro", "Category=RetiroJudicialIndividual"]),
    ["retiro-r10"] = new("R10 CA judicial conjunta", "run-RetiroEfectivo.ps1", ["-Filtro", "Category=RetiroJudicialConjunta"]),
    ["retiro-r11"] = new("R11 CA judicial indistinta", "run-RetiroEfectivo.ps1", ["-Filtro", "Category=RetiroJudicialIndistinta"]),
    ["retiro-r12"] = new("R12 CA judicial categorizada", "run-RetiroEfectivo.ps1", ["-Filtro", "Category=RetiroJudicialCategorizada"]),
    ["retiro-r13"] = new("R13 CA USD individual", "run-RetiroEfectivo.ps1", ["-Filtro", "Category=RetiroCaUsdIndividual"]),
    ["retiro-r14"] = new("R14 CA USD conjunta", "run-RetiroEfectivo.ps1", ["-Filtro", "Category=RetiroCaUsdConjunta"]),
    ["retiro-r15"] = new("R15 CA USD indistinta", "run-RetiroEfectivo.ps1", ["-Filtro", "Category=RetiroCaUsdIndistinta"]),
    ["retiro-r16"] = new("R16 CA USD categorizada", "run-RetiroEfectivo.ps1", ["-Filtro", "Category=RetiroCaUsdCategorizada"]),
    ["retiro-r17"] = new("R17 CC USD individual", "run-RetiroEfectivo.ps1", ["-Filtro", "Category=RetiroCcUsdIndividual"]),
    ["retiro-r18"] = new("R18 CC USD conjunta", "run-RetiroEfectivo.ps1", ["-Filtro", "Category=RetiroCcUsdConjunta"]),
    ["retiro-r19"] = new("R19 CC USD indistinta", "run-RetiroEfectivo.ps1", ["-Filtro", "Category=RetiroCcUsdIndistinta"]),
    ["retiro-r20"] = new("R20 CC USD categorizada", "run-RetiroEfectivo.ps1", ["-Filtro", "Category=RetiroCcUsdCategorizada"]),
    ["retiro-r21"] = new("R21 CA EUR individual", "run-RetiroEfectivo.ps1", ["-Filtro", "Category=RetiroCaEurIndividual"]),
    ["retiro-r22"] = new("R22 CA EUR conjunta", "run-RetiroEfectivo.ps1", ["-Filtro", "Category=RetiroCaEurConjunta"]),
    ["retiro-r23"] = new("R23 CA EUR indistinta", "run-RetiroEfectivo.ps1", ["-Filtro", "Category=RetiroCaEurIndistinta"]),
    ["retiro-r24"] = new("R24 CA EUR categorizada", "run-RetiroEfectivo.ps1", ["-Filtro", "Category=RetiroCaEurCategorizada"]),
    ["retiro-r25"] = new("R25 CC EUR individual", "run-RetiroEfectivo.ps1", ["-Filtro", "Category=RetiroCcEurIndividual"]),
    ["retiro-r26"] = new("R26 CC EUR conjunta", "run-RetiroEfectivo.ps1", ["-Filtro", "Category=RetiroCcEurConjunta"]),
    ["retiro-r27"] = new("R27 CC EUR indistinta", "run-RetiroEfectivo.ps1", ["-Filtro", "Category=RetiroCcEurIndistinta"]),
    ["retiro-r28"] = new("R28 CC EUR categorizada", "run-RetiroEfectivo.ps1", ["-Filtro", "Category=RetiroCcEurCategorizada"]),
    ["retiro-todo"] = new("Todo retiro efectivo", "run-RetiroEfectivo.ps1", []),

    ["retiro-cf01"] = new("CF01 Conformidad CA indistinta", "run-RetiroEfectivo.ps1", ["-Filtro", "Category=RetiroConfIndistinta"]),
    ["retiro-cf02"] = new("CF02 Conformidad CA conjunta", "run-RetiroEfectivo.ps1", ["-Filtro", "Category=RetiroConfConjunta"]),
    ["retiro-cf03"] = new("CF03 Conformidad CA categorizada", "run-RetiroEfectivo.ps1", ["-Filtro", "Category=RetiroConfCategorizada"]),
    ["retiro-cf04"] = new("CF04 Conformidad CA unipersonal", "run-RetiroEfectivo.ps1", ["-Filtro", "Category=RetiroConfUnipersonal"]),
    ["retiro-cf05"] = new("CF05 Conformidad CC conjunta", "run-RetiroEfectivo.ps1", ["-Filtro", "Category=RetiroConfCcConjunta"]),
    ["retiro-cf06"] = new("CF06 Conformidad CC categorizada", "run-RetiroEfectivo.ps1", ["-Filtro", "Category=RetiroConfCcCategorizada"]),
    ["retiro-cf07"] = new("CF07 Conformidad Validación con Firma", "run-RetiroEfectivo.ps1", ["-Filtro", "Category=RetiroConfValidacionFirma"]),
    ["retiro-conf-todo"] = new("Todo conformidad titularidad", "run-RetiroEfectivo.ps1", ["-Filtro", "Category=Conformidad"]),

    ["retiro-rc01"] = new("RC01 sin condiciones vigentes", "run-RetiroEfectivoCuentas.ps1", ["-Filtro", "Category=RetiroCuentaSinCondiciones01"]),
    ["retiro-rc02"] = new("RC02 CA empresa CUIT", "run-RetiroEfectivoCuentas.ps1", ["-Filtro", "Category=RetiroCuentaEmpresaCa01"]),
    ["retiro-rc03"] = new("RC03 CC empresa CUIT", "run-RetiroEfectivoCuentas.ps1", ["-Filtro", "Category=RetiroCuentaEmpresaCc01"]),
    ["retiro-cuentas-todo"] = new("Todo retiro por cuenta", "run-RetiroEfectivoCuentas.ps1", []),

    // SC-161 — Parametría Relación Transacción y Perfil Contable (Excel: 20 casos)
    ["18"] = new("Suite automatica P1-P4 SC-161", "run-SC161-RelacionTransaccionPerfil.ps1", []),
    ["18-CA01"] = new("P1 Acceso - Abrir pantalla TRN-Perfil", "run-SC161-RelacionTransaccionPerfil.ps1", ["-Solo", "CA-01"]),
    ["18-CA02"] = new("P1 Listado - Filtrar relaciones", "run-SC161-RelacionTransaccionPerfil.ps1", ["-Solo", "CA-02"]),
    ["18-CA03"] = new("P2 Autocomplete - Descripcion TRN", "run-SC161-RelacionTransaccionPerfil.ps1", ["-Solo", "CA-03"]),
    ["18-CA04"] = new("P2 Autocomplete - Descripcion causa", "run-SC161-RelacionTransaccionPerfil.ps1", ["-Solo", "CA-04"]),
    ["18-CA05"] = new("P3 Alta - Causa vacia", "run-SC161-RelacionTransaccionPerfil.ps1", ["-Solo", "CA-05"]),
    ["18-CA06"] = new("P3 Alta - Causa informada", "run-SC161-RelacionTransaccionPerfil.ps1", ["-Solo", "CA-06"]),
    ["18-CA07"] = new("P3 Perfil - Autocompletar descripcion", "run-SC161-RelacionTransaccionPerfil.ps1", ["-Solo", "CA-07"]),
    ["18-CA08"] = new("P5 Manual - Catalogo F5 perfiles", "run-SC161-RelacionTransaccionPerfil.ps1", ["-Solo", "CA-08"]),
    ["18-CA09"] = new("P3 UI - No Contabiliza", "run-SC161-RelacionTransaccionPerfil.ps1", ["-Solo", "CA-09"]),
    ["18-CA10"] = new("P4 Edicion - Modificar relacion", "run-SC161-RelacionTransaccionPerfil.ps1", ["-Solo", "CA-10"]),
    ["18-CA11"] = new("P5 Manual - Eliminar relacion", "run-SC161-RelacionTransaccionPerfil.ps1", ["-Solo", "CA-11"]),
    ["18-CA12"] = new("P5 Manual - TA alta", "run-SC161-RelacionTransaccionPerfil.ps1", ["-Solo", "CA-12"]),
    ["18-CA13"] = new("P5 Manual - TA modificacion", "run-SC161-RelacionTransaccionPerfil.ps1", ["-Solo", "CA-13"]),
    ["18-CA14"] = new("P5 Manual - E2E deprecated", "run-SC161-RelacionTransaccionPerfil.ps1", ["-Solo", "CA-14"]),
    ["18-CA15"] = new("P5 Manual - PDF listado", "run-SC161-RelacionTransaccionPerfil.ps1", ["-Solo", "CA-15"]),
    ["18-CN01"] = new("P6 Negativo - TRN invalida", "run-SC161-RelacionTransaccionPerfil.ps1", ["-Solo", "CN-01"]),
    ["18-CN02"] = new("P6 Negativo - Perfil obligatorio", "run-SC161-RelacionTransaccionPerfil.ps1", ["-Solo", "CN-02"]),
    ["18-CN03"] = new("P6 Negativo - Perfil inexistente", "run-SC161-RelacionTransaccionPerfil.ps1", ["-Solo", "CN-03"]),
    ["18-CN04"] = new("P6 Negativo - Alta duplicada", "run-SC161-RelacionTransaccionPerfil.ps1", ["-Solo", "CN-04"]),
    ["18-CN05"] = new("P6 Negativo - Cancelar eliminacion", "run-SC161-RelacionTransaccionPerfil.ps1", ["-Solo", "CN-05"]),
    ["18-CN06"] = new("P6 Negativo - Sin permiso", "run-SC161-RelacionTransaccionPerfil.ps1", ["-Solo", "CN-06"]),

    // Release 9 — automatización en Runner; T.O./TimeOut QA manual (ver .cursor/rules/release-9-runner-qa.mdc).
    ["r9-SC-161"] = new("SC-161 — Parametría Relación Transacción y Perfil Contable", "run-SC161-RelacionTransaccionPerfil.ps1", []),
    ["r9-SC-402"] = new("SC-402 — Correcciones en dar alta Equivalencia Unidad COE", "run-PruebaFeature.ps1", ["-Feature", "Release-9/SC-402_Equivalencia_Unidad_COE.feature"]),
    ["r9-SC-414"] = new("SC-414 — En cierre caja al ir a Ver Minitesoro no se posiciona en la caja correspondiente", "run-PruebaFeature.ps1", ["-Feature", "Release-9/SC-414_Ver_minitesoro_posiciona_caja.feature"]),
    ["r9-SC-461"] = new("SC-461 — Mensaje en supervisiones que requieren rol de nivel mayor a 1", "run-PruebaFeature.ps1", ["-Feature", "Release-9/SC-461_Supervision_nivel_insuficiente.feature"]),
    ["r9-SC-471"] = new("SC-471 — En pase intercaja no muestra saldo actualizado al pasar de bandeja", "run-PruebaFeature.ps1", ["-Feature", "Release-9/SC-471_Pase_intercaja_saldo_bandeja.feature"]),
    ["r9-SC-472"] = new("SC-472 — Reporte Historico cierres de caja", "run-PruebaFeature.ps1", ["-Feature", "Release-9/SC-472_Historial_cierres_caja.feature"]),
    ["r9-SC-476"] = new("SC-476 — Nuevo mensaje de confirmacion en Desasignación de Caja", "run-PruebaFeature.ps1", ["-Feature", "Release-9/SC-476_Mensaje_desasignacion_caja.feature"]),
    ["r9-SC-476-qa"] = new("SC-476 — Entrega QA (12 casos Xray)", "run-SC476-QaEntrega.ps1", []),
    ["r9-SC-484"] = new("SC-484 — Modificar label Saldo Actual por Saldo de caja en Pases Intercaja y con COE", "run-PruebaFeature.ps1", ["-Feature", "Release-9/SC-484_Saldo_de_caja_pases.feature"]),
    ["r9-SC-485"] = new("SC-485 — Modificar label Saldo Actual por Saldo de caja en Transacciones Monetarias y Resumen Cierre de Caja", "run-PruebaFeature.ps1", ["-Feature", "Release-9/SC-485_Saldo_de_caja_TM_resumen.feature"]),
    ["r9-SC-492"] = new("SC-492 — Después de generar nuevo pase siempre posicionarse en bandeja Enviados", "run-PruebaFeature.ps1", ["-Feature", "Release-9/SC-492_Posicion_bandeja_Enviados.feature"]),
    ["r9-SC-493"] = new("SC-493 — Mensaje en cierre de caja cuando no se realiza el cierre en COE", "run-PruebaFeature.ps1", ["-Feature", "Release-9/SC-493_Mensaje_cierre_sin_COE.feature"]),
    ["r9-todo"] = new("Correr todo Release 9", "run-Release9-Todos.ps1", []),

    // Regresión de pruebas: @Prueba sin Release 9 (R9 corre en módulo release-9).
    ["regresion-pruebas-todo"] = new("Correr todo (regresión completa)", "run-PruebasTodos.ps1", []),

    // Diagnóstico: build + list-tests (validar pipeline sin corrida UI larga).
    ["smoke-pipeline"] = new("Comprobar build y detección de tests", "run-SmokePipeline.ps1", [])
};

if (!Directory.Exists(wwwRoot))
    throw new DirectoryNotFoundException("No se encontró wwwroot en: " + wwwRoot);

IResult? RequireSuite()
{
    if (automatizacionOk)
        return null;
    var tried = string.Join(" | ", automatizacionTried);
    return Results.Json(new
    {
        ok = false,
        error =
            "Falta AutomatizacionSOT junto al portable. Colocá la carpeta hermana AutomatizacionSOT (con AutomatizacionSOT.csproj) "
            + "o definí AutomatizacionSOT_ROOT. Rutas intentadas: " + tried,
        automatizacionOk = false,
        automatizacionBuscado,
        rutasIntentadas = automatizacionTried
    }, statusCode: 503);
}

// Frontend Angular (build → wwwroot/app). Legacy HTML queda en wwwroot/_legacy.
var angularRoot = Path.Combine(wwwRoot, "app");
var serveRoot = File.Exists(Path.Combine(angularRoot, "index.html"))
    ? angularRoot
    : wwwRoot;
var fileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(serveRoot);
var htmlPath = File.Exists(Path.Combine(serveRoot, "index.html"))
    ? Path.Combine(serveRoot, "index.html")
    : Path.Combine(wwwRoot, "_legacy", "mi-runner-operador.html");

app.UseDefaultFiles(new DefaultFilesOptions
{
    FileProvider = fileProvider,
    DefaultFileNames = ["index.html", "mi-runner-operador.html"]
});

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = fileProvider,
    RequestPath = "",
    ContentTypeProvider = CreateContentTypeProvider()
});

// Assets legacy (logo, manuales, backup HTML) siguen disponibles desde wwwroot.
var wwwProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(wwwRoot);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = wwwProvider,
    RequestPath = "",
    ContentTypeProvider = CreateContentTypeProvider()
});

// Auth PIN + rate limit (después de estáticos; protege APIs).
RunnerSecurity.UseAuthGate(app, automatizacionRoot);
RunnerSecurity.MapAuthEndpoints(app, automatizacionRoot);

// También sirve la carpeta fuente casos-prueba del repo (Excel, SQL, CSV).
var casosPruebaRoot = Path.Combine(automatizacionRoot, "..", "casos-prueba");
casosPruebaRoot = Path.GetFullPath(casosPruebaRoot);
if (Directory.Exists(casosPruebaRoot))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(casosPruebaRoot),
        RequestPath = "/casos-prueba-repo",
        ContentTypeProvider = CreateContentTypeProvider()
    });
}

app.MapGet("/casos-prueba", () =>
{
    var wwwCasos = Path.Combine(wwwRoot, "casos-prueba");
    var tickets = new List<object>();
    if (Directory.Exists(wwwCasos))
    {
        foreach (var dir in Directory.GetDirectories(wwwCasos).OrderBy(d => d))
        {
            var id = Path.GetFileName(dir);
            var jsonPath = Path.Combine(dir, "casos.json");
            if (!File.Exists(jsonPath))
            {
                tickets.Add(new { id, url = $"/casos-prueba/{id}/", tieneJson = false });
                continue;
            }

            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
                var root = doc.RootElement;
                tickets.Add(new
                {
                    id,
                    titulo = root.TryGetProperty("titulo", out var t) ? t.GetString() : id,
                    jira = root.TryGetProperty("jira", out var j) ? j.GetString() : null,
                    total = root.TryGetProperty("total", out var n) ? n.GetInt32() : 0,
                    feliz = root.TryGetProperty("feliz", out var f) ? f.GetInt32() : 0,
                    negativo = root.TryGetProperty("negativo", out var g) ? g.GetInt32() : 0,
                    excel = root.TryGetProperty("excel", out var e) ? e.GetString() : null,
                    pagina = id.Equals("SC-161", StringComparison.OrdinalIgnoreCase)
                        ? "/SC-161-casos-prueba.html"
                        : $"/casos-prueba/{id}/",
                    tieneJson = true
                });
            }
            catch
            {
                tickets.Add(new { id, url = $"/casos-prueba/{id}/", tieneJson = false });
            }
        }
    }

    return Results.Ok(new { ok = true, tickets });
});

app.MapGet("/pruebas/release9-info", () =>
{
    if (!automatizacionOk)
    {
        return Results.Ok(new
        {
            ok = false,
            error = "No se encontró AutomatizacionSOT (modo ayuda). Colocá la carpeta hermana junto al portable.",
            automatizacionOk = false,
            rutasIntentadas = automatizacionTried
        });
    }

    var manifestPath = Path.GetFullPath(Path.Combine(automatizacionRoot, "..", "casos-prueba", "Release-9", "manifest.json"));
    if (!File.Exists(manifestPath))
    {
        return Results.Ok(new
        {
            ok = false,
            error = "No se encontró casos-prueba/Release-9/manifest.json"
        });
    }

    try
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = doc.RootElement;
        var auto = new List<object>();
        if (root.TryGetProperty("ticketsAutomatizacion", out var ta) && ta.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in ta.EnumerateArray())
            {
                auto.Add(new
                {
                    id = item.TryGetProperty("id", out var id) ? id.GetString() : null,
                    titulo = item.TryGetProperty("titulo", out var t) ? t.GetString() : null
                });
            }
        }

        var manual = new List<object>();
        if (root.TryGetProperty("ticketsSoloExcel", out var te) && te.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in te.EnumerateArray())
            {
                manual.Add(new
                {
                    id = item.TryGetProperty("id", out var id) ? id.GetString() : null,
                    tipo = item.TryGetProperty("tipo", out var tp) ? tp.GetString() : null,
                    titulo = item.TryGetProperty("titulo", out var t) ? t.GetString() : null
                });
            }
        }

        var nota = root.TryGetProperty("nota", out var n) ? n.GetString() : null;
        return Results.Ok(new
        {
            ok = true,
            release = root.TryGetProperty("release", out var r) ? r.GetInt32() : 9,
            nota,
            lotePrincipal = "r9-todo",
            automatizados = auto.Count,
            manuales = manual.Count,
            ticketsAutomatizacion = auto,
            ticketsSoloExcel = manual
        });
    }
    catch (Exception ex)
    {
        return Results.Ok(new { ok = false, error = ex.Message });
    }
});

app.MapGet("/", () =>
{
    return File.Exists(htmlPath)
        ? Results.Content(File.ReadAllText(htmlPath), "text/html; charset=utf-8")
        : Results.NotFound("No se encontró el Frontend Angular (wwwroot/app/index.html). Ejecutá npm run build en RunnerOperador/frontend.");
});

app.MapGet("/health", () => Results.Ok(new
{
    ok = true,
    service = "RunnerIA",
    version = RunnerVersion.Version,
    channel = RunnerVersion.Channel,
    build = RunnerVersion.Build,
    versionLabel = RunnerVersion.Label,
    wwwRoot,
    wwwrootOk = Directory.Exists(wwwRoot),
    angularOk = File.Exists(Path.Combine(wwwRoot, "app", "index.html")),
    automatizacionOk,
    automatizacionRoot = automatizacionOk ? automatizacionRoot : automatizacionBuscado,
    automatizacionBuscado,
    rutasIntentadas = automatizacionTried,
    modoAyuda = !automatizacionOk,
    mensaje = automatizacionOk
        ? null
        : "Suite AutomatizacionSOT no encontrada. La UI funciona en modo ayuda; las corridas requieren la carpeta hermana.",
    escenarios = escenarios.Keys.OrderBy(k => k).ToArray()
}));

app.MapGet("/preflight", () =>
{
    var secretsPath = Path.Combine(automatizacionRoot, "appsettings.secrets.json");
    var secretsPresent = File.Exists(secretsPath);
    var csproj = Path.Combine(automatizacionOk ? automatizacionRoot : automatizacionBuscado, "AutomatizacionSOT.csproj");
    var csprojOk = File.Exists(csproj);
    var playwrightPath = Environment.GetEnvironmentVariable("PLAYWRIGHT_BROWSERS_PATH")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ms-playwright");
    var playwrightOk = Directory.Exists(playwrightPath);

    var (catalogSyncOk, uiOnly, apiOnly) = EvaluarSyncCatalogo(runnerRoot, escenarios.Keys);

    var ultima = automatizacionOk
        ? AsegurarUltimaCorridaEnMemoria(runStore, runnerRoot, automatizacionRoot)
        : null;

    return Results.Ok(new
    {
        ok = csprojOk && catalogSyncOk,
        service = "RunnerIA",
        version = RunnerVersion.Version,
        channel = RunnerVersion.Channel,
        build = RunnerVersion.Build,
        versionLabel = RunnerVersion.Label,
        automatizacionOk,
        automatizacionRoot = automatizacionOk ? automatizacionRoot : automatizacionBuscado,
        automatizacionBuscado,
        rutasIntentadas = automatizacionTried,
        modoAyuda = !automatizacionOk,
        mensaje = automatizacionOk
            ? null
            : "Falta AutomatizacionSOT. Colocá la carpeta hermana junto al portable o definí AutomatizacionSOT_ROOT.",
        dotnetOk = csprojOk,
        secretsPresent,
        playwrightPath,
        playwrightOk,
        escenariosCount = escenarios.Count,
        catalogSyncOk,
        catalogUiOnly = uiOnly,
        catalogApiOnly = apiOnly,
        ultimaCorrida = ultima is null
            ? null
            : new
            {
                runId = ultima.Id,
                escenarioId = ultima.EscenarioId,
                titulo = ultima.Titulo,
                estado = ultima.Estado,
                exitCode = ultima.ExitCode,
                corridaVacia = ultima.CorridaVacia
            }
    });
});

app.MapGet("/escenarios", () => escenarios.Select(kv => new
{
    id = kv.Key,
    titulo = kv.Value.Titulo,
    script = kv.Value.Script,
    args = kv.Value.Args
}));

app.MapGet("/run/ultima", () =>
{
    var run = AsegurarUltimaCorridaEnMemoria(runStore, runnerRoot, automatizacionRoot);
    if (run is null)
        return Results.Ok(new { ok = true, hayUltima = false });
    return Results.Ok(new { ok = true, hayUltima = true, corrida = ToRunResponse(run) });
});

// Última corrida por escenario (persistida en data/ultimas-por-escenario.json).
app.MapGet("/run/escenario/{escenarioId}/ultima", (string escenarioId) =>
{
    var run = AsegurarCorridaEscenarioEnMemoria(runStore, runnerRoot, automatizacionRoot, escenarioId);
    if (run is null)
        return Results.Ok(new { ok = true, hayUltima = false, escenarioId });
    return Results.Ok(new { ok = true, hayUltima = true, escenarioId, corrida = ToRunResponse(run) });
});

app.MapGet("/run/escenario/{escenarioId}/evidencia.zip", (string escenarioId) =>
{
    var run = AsegurarCorridaEscenarioEnMemoria(runStore, runnerRoot, automatizacionRoot, escenarioId);
    if (run is null)
        return Results.NotFound(new { ok = false, error = "No hay última corrida guardada para este escenario." });
    return CrearZipEvidencia(run, automatizacionRoot);
});

app.MapGet("/run/escenario/{escenarioId}/informe", (string escenarioId) =>
{
    var run = AsegurarCorridaEscenarioEnMemoria(runStore, runnerRoot, automatizacionRoot, escenarioId);
    if (run is null)
        return Results.NotFound(new { ok = false, error = "No hay última corrida guardada para este escenario." });
    return ServirInformeHtml(run, automatizacionRoot);
});

app.MapGet("/run/{runId}", (string runId) =>
{
    if (string.Equals(runId, "ultima", StringComparison.OrdinalIgnoreCase))
    {
        var u = AsegurarUltimaCorridaEnMemoria(runStore, runnerRoot, automatizacionRoot);
        if (u is null)
            return Results.NotFound(new { ok = false, error = "No hay última corrida guardada." });
        return Results.Ok(ToRunResponse(u));
    }

    if (!runStore.TryGet(runId, out var run))
    {
        // Tras reiniciar la API: rehidratar si coincide con la última global o por escenario.
        var ultima = AsegurarUltimaCorridaEnMemoria(runStore, runnerRoot, automatizacionRoot);
        if (ultima is null || !string.Equals(ultima.Id, runId, StringComparison.OrdinalIgnoreCase))
        {
            run = BuscarCorridaPersistidaPorRunId(runStore, runnerRoot, automatizacionRoot, runId);
            if (run is null)
                return Results.NotFound(new { ok = false, error = "Corrida no encontrada." });
        }
        else
            run = ultima;
    }

    return Results.Ok(ToRunResponse(run));
});

app.MapPost("/run/{id}", async (string id, CancellationToken ct) =>
{
    if (RequireSuite() is { } suiteMissing)
        return suiteMissing;

    if (!escenarios.TryGetValue(id, out var escenario))
        return Results.NotFound(new { ok = false, error = $"Escenario '{id}' no configurado." });

    if (!await runLock.WaitAsync(0, ct))
    {
        return Results.Conflict(new
        {
            ok = false,
            error = "Ya hay una corrida en curso. Esperá a que termine o consultá su estado."
        });
    }

  try
  {
    var scriptPath = Path.Combine(automatizacionRoot, escenario.Script);
    if (!File.Exists(scriptPath))
    {
        runLock.Release();
        return Results.BadRequest(new { ok = false, error = $"No se encontró el script: {escenario.Script}" });
    }

    var run = runStore.Create(id, escenario, scriptPath);
    _ = Task.Run(() => ExecuteRunAsync(run, automatizacionRoot, runnerRoot, runLock), CancellationToken.None);
    return Results.Accepted($"/run/{run.Id}", ToRunResponse(run));
  }
  catch
  {
    runLock.Release();
    throw;
  }
});

app.MapPost("/run/{runId}/cancel", (string runId) =>
{
    if (!runStore.TryGet(runId, out var run))
        return Results.NotFound(new { ok = false, error = "Corrida no encontrada." });

    if (string.Equals(run.Estado, "finalizado", StringComparison.OrdinalIgnoreCase))
        return Results.Ok(new { ok = true, mensaje = "La corrida ya había finalizado.", runId });

    var killed = run.RequestCancel();
    return Results.Ok(new
    {
        ok = true,
        mensaje = killed
            ? "Se pidió detener la corrida (proceso PowerShell/dotnet)."
            : "Corrida marcada para detener (el proceso aún no había arrancado o ya terminó).",
        runId
    });
});

// Paquete QA: genera Excel+ZIP una sola vez; descargas GET rápidas por token.
app.MapPost("/run/modulo/paquete-qa", (ModuloEvidenciasRequest body) =>
{
    if (RequireSuite() is { } suiteMissing)
        return suiteMissing;

    if (body?.Casos is null || body.Casos.Count == 0)
        return Results.BadRequest(new { ok = false, error = "Sin casos para armar el paquete QA." });

    try
    {
        ResolverCorridasEvidenciaModulo(body, runStore, runnerRoot, automatizacionRoot);
        var pack = ModuloEvidenciasZip.PrepararPaquete(body, automatizacionRoot, casosPruebaRoot);
        var token = ModuloPaqueteCache.Guardar(pack.ZipBytes, pack.ZipNombre, pack.ExcelBytes, pack.ExcelNombre);
        return Results.Json(new
        {
            ok = true,
            token,
            zipNombre = pack.ZipNombre,
            excelNombre = pack.ExcelNombre,
            pasosEvidencia = pack.PasosEvidencia
        });
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = "No se pudo preparar el paquete QA: " + ex.Message }, statusCode: 500);
    }
});

app.MapGet("/run/modulo/paquete-qa/{token}/excel", (string token) =>
{
    var entrada = ModuloPaqueteCache.Obtener(token);
    if (entrada is null)
        return Results.NotFound(new { ok = false, error = "Paquete QA no encontrado o expirado." });

    var path = Path.Combine(entrada.Directorio, entrada.ExcelNombre);
    if (!File.Exists(path))
        return Results.NotFound(new { ok = false, error = "Excel no disponible." });

    return Results.File(
        path,
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        entrada.ExcelNombre);
});

app.MapGet("/run/modulo/paquete-qa/{token}/zip", (string token) =>
{
    var entrada = ModuloPaqueteCache.Obtener(token);
    if (entrada is null)
        return Results.NotFound(new { ok = false, error = "Paquete QA no encontrado o expirado." });

    var path = Path.Combine(entrada.Directorio, entrada.ZipNombre);
    if (!File.Exists(path))
        return Results.NotFound(new { ok = false, error = "ZIP no disponible." });

    return Results.File(path, "application/zip", entrada.ZipNombre);
});

// Paquete QA desde casos.json completo (SC-476: 12 casos Xray + evidencias embebidas).
app.MapPost("/run/ticket/{ticket}/paquete-json", (string ticket) =>
{
    if (RequireSuite() is { } suiteMissing)
        return suiteMissing;

    var tk = CasosPruebaExcel.NormalizarTicketParaArchivo(ticket);
    if (string.IsNullOrWhiteSpace(tk) || tk == "Modulo")
        return Results.BadRequest(new { ok = false, error = "Ticket inválido." });

    try
    {
        var pack = ModuloEvidenciasZip.PrepararPaqueteDesdeCasosJson(tk, automatizacionRoot, casosPruebaRoot);
        var token = ModuloPaqueteCache.Guardar(pack.ZipBytes, pack.ZipNombre, pack.ExcelBytes, pack.ExcelNombre);
        return Results.Json(new
        {
            ok = true,
            token,
            ticket = tk,
            zipNombre = pack.ZipNombre,
            excelNombre = pack.ExcelNombre,
            pasosEvidencia = pack.PasosEvidencia,
            casos = CasosPruebaJsonLoader.TryLoad(casosPruebaRoot, tk, "Release 9")?.Casos?.Count ?? 0
        });
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = "No se pudo armar el paquete desde casos.json: " + ex.Message }, statusCode: 500);
    }
});

// ZIP QA tras «Ejecutar todos del módulo» (Excel de casos ejecutados + PNG embebidos).
app.MapPost("/run/modulo/evidencias.zip", (ModuloEvidenciasRequest body, HttpContext http) =>
{
    if (RequireSuite() is { } suiteMissing)
        return suiteMissing;

    if (body?.Casos is null || body.Casos.Count == 0)
        return Results.BadRequest(new { ok = false, error = "Sin casos para armar el paquete QA." });

    try
    {
        ResolverCorridasEvidenciaModulo(body, runStore, runnerRoot, automatizacionRoot);
        var pack = ModuloEvidenciasZip.Empaquetar(body, automatizacionRoot, casosPruebaRoot);
        http.Response.Headers["X-Runner-Excel-Filename"] = pack.ExcelNombre;
        http.Response.Headers["X-Runner-Evidencia-Pasos"] = pack.PasosEvidencia.ToString(CultureInfo.InvariantCulture);
        return Results.File(pack.Bytes!, "application/zip", pack.ZipNombre);
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = "No se pudo crear el ZIP QA: " + ex.Message }, statusCode: 500);
    }
});

// Excel QA con evidencias embebidas (mismo cuerpo que evidencias.zip).
app.MapPost("/run/modulo/excel", (ModuloEvidenciasRequest body, HttpContext http) =>
{
    if (RequireSuite() is { } suiteMissing)
        return suiteMissing;

    if (body?.Casos is null || body.Casos.Count == 0)
        return Results.BadRequest(new { ok = false, error = "Sin casos para armar el Excel." });

    try
    {
        ResolverCorridasEvidenciaModulo(body, runStore, runnerRoot, automatizacionRoot);
        var pack = ModuloEvidenciasZip.GenerarExcel(body, automatizacionRoot, casosPruebaRoot);
        http.Response.Headers["X-Runner-Evidencia-Pasos"] = pack.PasosEvidencia.ToString(CultureInfo.InvariantCulture);
        return Results.File(
            pack.Bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            pack.ExcelNombre);
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = "No se pudo crear el Excel QA: " + ex.Message }, statusCode: 500);
    }
});

// ---- Evidencias: descarga ZIP e informe HTML (carpeta se mantiene en disco) ----
app.MapGet("/run/{runId}/evidencia.zip", (string runId) =>
{
    if (!runStore.TryGet(runId, out var run))
    {
        run = BuscarCorridaPersistidaPorRunId(runStore, runnerRoot, automatizacionRoot, runId);
        if (run is null)
            return Results.NotFound(new { ok = false, error = "Corrida no encontrada." });
    }

    return CrearZipEvidencia(run, automatizacionRoot);
});

app.MapGet("/run/{runId}/informe", (string runId) =>
{
    if (!runStore.TryGet(runId, out var run))
    {
        run = BuscarCorridaPersistidaPorRunId(runStore, runnerRoot, automatizacionRoot, runId);
        if (run is null)
            return Results.NotFound(new { ok = false, error = "Corrida no encontrada." });
    }

    return ServirInformeHtml(run, automatizacionRoot);
});

app.MapGet("/run/{runId}/evidencia-file/{*relPath}", (string runId, string relPath) =>
{
    if (!runStore.TryGet(runId, out var run))
    {
        run = BuscarCorridaPersistidaPorRunId(runStore, runnerRoot, automatizacionRoot, runId);
        if (run is null)
            return Results.NotFound();
    }

    ResolverEvidenciaSiFalta(run, automatizacionRoot);
    var carpeta = run.EvidenciaCarpeta;
    if (string.IsNullOrEmpty(carpeta) || string.IsNullOrWhiteSpace(relPath))
        return Results.NotFound();

    if (!EsRutaEvidenciaSegura(carpeta, automatizacionRoot))
        return Results.BadRequest(new { ok = false, error = "Ruta de evidencia no permitida." });

    var decoded = Uri.UnescapeDataString(relPath);
    if (decoded.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(decoded))
        return Results.NotFound();

    var full = Path.GetFullPath(Path.Combine(carpeta, decoded.Replace('/', Path.DirectorySeparatorChar)));
    var rootFull = Path.GetFullPath(carpeta).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                   + Path.DirectorySeparatorChar;
    if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
        return Results.NotFound();

    if (EsNombreArchivoSensible(Path.GetFileName(full)))
        return Results.NotFound();

    var contentType = full.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ? "text/html; charset=utf-8"
        : full.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png"
        : full.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || full.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ? "image/jpeg"
        : "application/octet-stream";
    return Results.File(full, contentType);
});

// ---- Configuración: editar appsettings desde la web ----
// Lista blanca de archivos editables. Los secretos (appsettings.secrets.json) quedan
// EXCLUIDOS a propósito para no exponer ni permitir editar contraseñas desde la web.
var archivosConfig = new (string Name, string Desc)[]
{
    ("appsettings.json", "Configuración principal (login, sucursal, timeouts)"),
    ("appsettings.intercaja.json", "Overlay pases intercaja (cajas origen/destino)"),
    ("appsettings.deposito-interdeposito-cheques.json", "Overlay depósito de cheques (Pesos)"),
    ("appsettings.deposito-interdeposito-cheques-usd.json", "Overlay depósito de cheques (USD)"),
    ("appsettings.perfiles/banfield.json", "Perfil sucursal Banfield"),
    ("appsettings.perfiles/sanmartin.json", "Perfil sucursal San Martín"),
    ("appsettings.perfiles/aguaray.json", "Perfil sucursal Aguaray")
};

string? RutaConfigSegura(string name)
{
    var match = archivosConfig.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
    if (match.Name is null) return null;
    var full = Path.GetFullPath(Path.Combine(automatizacionRoot, match.Name.Replace('/', Path.DirectorySeparatorChar)));
    // Defensa extra contra path traversal: debe quedar dentro de automatizacionRoot.
    if (!full.StartsWith(automatizacionRoot, StringComparison.OrdinalIgnoreCase)) return null;
    return full;
}

app.MapGet("/config/files", () => archivosConfig
    .Select(a => new
    {
        name = a.Name,
        descripcion = a.Desc,
        existe = File.Exists(RutaConfigSegura(a.Name) ?? "")
    }));

app.MapGet("/config/file", (string name) =>
{
    var ruta = RutaConfigSegura(name);
    if (ruta is null) return Results.BadRequest(new { ok = false, error = "Archivo no permitido." });
    if (!File.Exists(ruta)) return Results.NotFound(new { ok = false, error = "El archivo no existe." });
    return Results.Ok(new { ok = true, name, content = File.ReadAllText(ruta) });
});

app.MapPost("/config/file", (ConfigGuardar body) =>
{
    if (body is null || string.IsNullOrWhiteSpace(body.Name))
        return Results.BadRequest(new { ok = false, error = "Falta el nombre del archivo." });

    var ruta = RutaConfigSegura(body.Name);
    if (ruta is null) return Results.BadRequest(new { ok = false, error = "Archivo no permitido." });

    var contenido = body.Content ?? "";
    try
    {
        using var _ = JsonDocument.Parse(contenido);
    }
    catch (JsonException ex)
    {
        return Results.BadRequest(new { ok = false, error = "JSON inválido: " + ex.Message });
    }

    try
    {
        if (File.Exists(ruta))
        {
            var backup = ruta + ".bak";
            File.Copy(ruta, backup, overwrite: true);
        }
        File.WriteAllText(ruta, contenido, new UTF8Encoding(false));
        return Results.Ok(new { ok = true, name = body.Name, guardado = DateTimeOffset.Now });
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = "No se pudo guardar: " + ex.Message }, statusCode: 500);
    }
});

// ---- Manual: texto markdown para la sección de Ayuda ----
app.MapGet("/MANUAL.md", () =>
{
    var manualPath = Path.Combine(automatizacionRoot, "MANUAL.md");
    return File.Exists(manualPath)
        ? Results.File(manualPath, "text/markdown; charset=utf-8", "MANUAL.md")
        : Results.NotFound(new { ok = false, error = "No se encontró MANUAL.md" });
});

app.MapGet("/manual", () =>
{
    var manualPath = Path.Combine(automatizacionRoot, "MANUAL.md");
    return File.Exists(manualPath)
        ? Results.Ok(new { ok = true, content = File.ReadAllText(manualPath) })
        : Results.NotFound(new { ok = false, error = "No se encontró MANUAL.md" });
});

// ---- Configuración simplificada para el cliente ----
// Solo expone lo esencial (sucursal, usuario, contraseñas, puerto COBIS). El resto se
// maneja por código interno y queda en la "Configuración avanzada" (endpoints /config/file).
string RutaAppsettings() => Path.Combine(automatizacionRoot, "appsettings.json");
string RutaSecrets() => Path.Combine(automatizacionRoot, "appsettings.secrets.json");

JsonObject LeerObjeto(string path)
{
    if (!File.Exists(path)) return new JsonObject();
    try { return JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject(); }
    catch { return new JsonObject(); }
}

static string? LeerValor(JsonObject root, string seccion, string clave)
    => (root[seccion] as JsonObject)?[clave]?.ToString();

static string Efectivo(string? valor, string porDefecto)
    => string.IsNullOrWhiteSpace(valor) ? porDefecto : valor;

static string InferirAmbienteDesdeUrl(string? url)
{
    var u = (url ?? "").ToLowerInvariant();
    if (u.Contains("accusys-qa") || u.Contains("-qa.") || u.Contains(".qa.")) return "QA";
    if (u.Contains("accusys-dev") || u.Contains("-dev.") || u.Contains(".dev.")) return "Dev";
    if (string.IsNullOrWhiteSpace(u)) return "Dev";
    return "Custom";
}

static bool TienePassword(JsonObject root, string seccion, string clave)
{
    var v = LeerValor(root, seccion, clave);
    return !string.IsNullOrWhiteSpace(v) && v != "***";
}

static void SetValor(JsonObject root, string seccion, string clave, string valor)
{
    if (root[seccion] is not JsonObject s) { s = new JsonObject(); root[seccion] = s; }
    s[clave] = valor;
}

static string ResolverPassword(string? valorFormulario, JsonObject sec, JsonObject app, string seccion, string clave)
{
    if (!string.IsNullOrWhiteSpace(valorFormulario))
        return DecodificarSiCorresponde(valorFormulario.Trim());

    var claveConfig = $"{seccion}:{clave}";
    var envName = "AutomatizacionSOT_" + claveConfig.Replace(':', '_');
    var envValue = Environment.GetEnvironmentVariable(envName);
    if (!string.IsNullOrWhiteSpace(envValue))
        return DecodificarSiCorresponde(envValue.Trim());

    var secret = LeerValor(sec, seccion, clave);
    if (!string.IsNullOrWhiteSpace(secret) && !EsMascara(secret))
        return DecodificarSiCorresponde(secret.Trim());

    var visible = LeerValor(app, seccion, clave);
    if (!string.IsNullOrWhiteSpace(visible) && !EsMascara(visible))
        return DecodificarSiCorresponde(visible.Trim());

    return "";
}

static bool EsMascara(string? valor) => RunnerSecrets.EsMascara(valor);

static string DecodificarSiCorresponde(string valor) =>
    SecretProtector.UnprotectFromStorage(valor);

static void GuardarConBackup(string path, JsonObject obj)
{
    if (File.Exists(path)) File.Copy(path, path + ".bak", overwrite: true);
    var json = obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(path, json, new UTF8Encoding(false));
}

static void GuardarSecretsConBackup(string path, JsonObject obj)
{
    SecretProtector.EncryptSensitiveFieldsInPlace(obj);
    GuardarConBackup(path, obj);
}

// ¿Ese usuario SOT tiene contraseña guardada? Busca en secrets:UsuariosSot y, si es el
// supervisor principal, también en Supervision:Password (compatibilidad).
static bool TienePasswordUsuarioSot(JsonObject sec, JsonObject app, string usuario)
{
    if (string.IsNullOrWhiteSpace(usuario)) return false;
    var v = LeerValor(sec, "UsuariosSot", usuario);
    if (!string.IsNullOrWhiteSpace(v)) return true;
    var principal = LeerValor(app, "Supervision", "Usuario");
    if (!string.IsNullOrWhiteSpace(principal) && string.Equals(principal, usuario, StringComparison.OrdinalIgnoreCase))
        return TienePassword(sec, "Supervision", "Password");
    return false;
}

app.MapGet("/config/cliente", () =>
{
    var appObj = LeerObjeto(RutaAppsettings());
    var secObj = LeerObjeto(RutaSecrets());

    // Lista de usuarios SOT (roles). Si no hay lista configurada, se arma una con el
    // supervisor principal actual para no perder compatibilidad.
    var usuariosSot = new List<object>();
    if (appObj["UsuariosSot"] is JsonArray arr)
    {
        foreach (var node in arr)
        {
            if (node is not JsonObject o) continue;
            var usuario = o["Usuario"]?.ToString() ?? "";
            if (string.IsNullOrWhiteSpace(usuario)) continue;
            usuariosSot.Add(new
            {
                rol = string.IsNullOrWhiteSpace(o["Rol"]?.ToString()) ? "SOT Supervisor" : o["Rol"]!.ToString(),
                usuario,
                tienePassword = TienePasswordUsuarioSot(secObj, appObj, usuario)
            });
        }
    }
    if (usuariosSot.Count == 0)
    {
        var principal = LeerValor(appObj, "Supervision", "Usuario") ?? "sot1";
        usuariosSot.Add(new
        {
            rol = "SOT Supervisor",
            usuario = principal,
            tienePassword = TienePassword(secObj, "Supervision", "Password")
        });
    }

    return Results.Ok(new
    {
        ok = true,
        ambiente = LeerValor(appObj, "Aplicacion", "Ambiente")
            ?? InferirAmbienteDesdeUrl(LeerValor(appObj, "Aplicacion", "UrlInicio")),
        urlInicio = LeerValor(appObj, "Aplicacion", "UrlInicio") ?? "",
        authOpenIdUrl = LeerValor(appObj, "Aplicacion", "AuthOpenIdUrl") ?? "",
        permitirAuthQa = string.Equals(LeerValor(appObj, "Aplicacion", "PermitirAuthQa"), "true", StringComparison.OrdinalIgnoreCase),
        sucursal = LeerValor(appObj, "DialogoSucursal", "SucursalObjetivo") ?? "",
        codigoSucursal = LeerValor(appObj, "DialogoSucursal", "TextoFiltroSucursal") ?? "",
        usuario = LeerValor(appObj, "Aplicacion", "Usuario") ?? "",
        usuariosSot,
        // COBIS: endpoint configurable por banco. Si está vacío, se muestra el valor efectivo (default).
        cobisServidor = Efectivo(LeerValor(appObj, "Cobis", "Servidor"), "SYBSRV2"),
        cobisHost = Efectivo(LeerValor(appObj, "Cobis", "Host"), "192.168.50.121"),
        puertoSybase = Efectivo(LeerValor(appObj, "Cobis", "Puerto"), "7410"),
        cobisUsuario = Efectivo(LeerValor(appObj, "Cobis", "Usuario"), "portiz"),
        cobisBaseDatos = Efectivo(LeerValor(appObj, "Cobis", "BaseDatos"), "cobis"),
        // SQL SOT: endpoint configurable por banco
        sqlSotServidor = Efectivo(LeerValor(appObj, "SqlSot", "Servidor"), "sqlsot.accusys-dev.io"),
        sqlSotPuerto = Efectivo(LeerValor(appObj, "SqlSot", "Puerto"), "1433"),
        sqlSotUsuario = Efectivo(LeerValor(appObj, "SqlSot", "Usuario"), "accusys"),
        sqlSotBaseDatos = Efectivo(LeerValor(appObj, "SqlSot", "BaseDatos"), "UW_CASHIER"),
        secretsExiste = File.Exists(RutaSecrets()),
        tienePasswordLogin = TienePassword(secObj, "Aplicacion", "Contrasena"),
        tienePasswordCobis = TienePassword(secObj, "Cobis", "Contrasena"),
        tienePasswordSqlSot = TienePassword(secObj, "SqlSot", "Contrasena"),
        // SC-416 — cajas del diálogo de cierre forzado
        cierreForzadoNotaCompensada = LeerValor(appObj, "CierreForzadoNota", "CorrelativoCompensada") ?? "4161",
        cierreForzadoNotaNormal = LeerValor(appObj, "CierreForzadoNota", "CorrelativoNormal") ?? "4162",
        cierreForzadoNotaMiniBoveda = LeerValor(appObj, "CierreForzadoNota", "CorrelativoMiniBoveda") ?? "",
        // Retiro de efectivo — consulta COBIS + importe aleatorio
        cobisConsultaMaxCuentas = LeerValor(appObj, "CobisConsultaCuentas", "MaxCuentasCandidatas") ?? "50",
        cobisConsultaModoRotacion = LeerValor(appObj, "CobisConsultaCuentas", "ModoRotacion") ?? "Aleatoria",
        cobisConsultaRotacionPorCliente = LeerValor(appObj, "CobisConsultaCuentas", "RotacionPorCliente") ?? "true",
        cobisConsultaUnaCuentaPorCliente = LeerValor(appObj, "CobisConsultaCuentas", "UnaCuentaPorCliente") ?? "true",
        cobisConsultaSaldoMinimoPesos = LeerValor(appObj, "CobisConsultaCuentas", "SaldoMinimoPesos") ?? "1000",
        cobisConsultaSaldoMinimoExtranjera = LeerValor(appObj, "CobisConsultaCuentas", "SaldoMinimoExtranjera") ?? "100",
        cobisConsultaSaldoMinimoCategorizada = LeerValor(appObj, "CobisConsultaCuentas", "SaldoMinimoCategorizada") ?? "100",
        cobisConsultaTitularidadDefault = LeerValor(appObj, "CobisConsultaCuentas", "TitularidadRetiroDefault") ?? "Individual",
        retiroImporteMinimo = LeerValor(appObj, "RetiroEfectivo", "ImporteMinimo") ?? "100",
        retiroImporteMaximoPractico = LeerValor(appObj, "RetiroEfectivo", "ImporteMaximoPractico") ?? "500",
        retiroMargenSaldoResiduo = LeerValor(appObj, "RetiroEfectivo", "MargenSaldoResiduo") ?? "1",
        retiroPorcentajeMinimo = LeerValor(appObj, "RetiroEfectivo", "PorcentajeMinimoRetiro") ?? "0.05",
        retiroPorcentajeMaximo = LeerValor(appObj, "RetiroEfectivo", "PorcentajeMaximoRetiro") ?? "0.40"
    });
});

app.MapPost("/config/cliente", (ConfigCliente body) =>
{
    if (body is null) return Results.BadRequest(new { ok = false, error = "Sin datos." });

    try
    {
        var appPath = RutaAppsettings();
        var appObj = LeerObjeto(appPath);

        if (body.Usuario is not null) SetValor(appObj, "Aplicacion", "Usuario", body.Usuario.Trim());
        if (body.UrlInicio is not null) SetValor(appObj, "Aplicacion", "UrlInicio", body.UrlInicio.Trim());
        if (body.Ambiente is not null) SetValor(appObj, "Aplicacion", "Ambiente", body.Ambiente.Trim());
        if (body.AuthOpenIdUrl is not null) SetValor(appObj, "Aplicacion", "AuthOpenIdUrl", body.AuthOpenIdUrl.Trim());
        if (body.PermitirAuthQa is not null)
            SetValor(appObj, "Aplicacion", "PermitirAuthQa", body.PermitirAuthQa == true ? "true" : "false");
        if (body.Sucursal is not null) SetValor(appObj, "DialogoSucursal", "SucursalObjetivo", body.Sucursal.Trim());
        if (body.CodigoSucursal is not null) SetValor(appObj, "DialogoSucursal", "TextoFiltroSucursal", body.CodigoSucursal.Trim());

        // Usuarios SOT (roles). La primera fila es la "principal": la automatización la usa
        // en los diálogos de supervisión y en la apertura (Supervision:* y Caja:*).
        var usuariosValidos = (body.UsuariosSot ?? Array.Empty<UsuarioSotDto>())
            .Where(u => !string.IsNullOrWhiteSpace(u.Usuario))
            .ToList();
        UsuarioSotDto? principal = null;
        if (usuariosValidos.Count > 0)
        {
            var arr = new JsonArray();
            foreach (var u in usuariosValidos)
            {
                arr.Add(new JsonObject
                {
                    ["Rol"] = string.IsNullOrWhiteSpace(u.Rol) ? "SOT Supervisor" : u.Rol!.Trim(),
                    ["Usuario"] = u.Usuario!.Trim()
                });
            }
            appObj["UsuariosSot"] = arr;

            principal = usuariosValidos[0];
            SetValor(appObj, "Supervision", "Usuario", principal.Usuario!.Trim());
            SetValor(appObj, "Caja", "UsuarioSupervisorApertura", principal.Usuario!.Trim());
        }

        // COBIS: endpoint configurable por banco
        if (body.CobisServidor is not null) SetValor(appObj, "Cobis", "Servidor", body.CobisServidor.Trim());
        if (body.CobisHost is not null) SetValor(appObj, "Cobis", "Host", body.CobisHost.Trim());
        if (!string.IsNullOrWhiteSpace(body.PuertoSybase)) SetValor(appObj, "Cobis", "Puerto", body.PuertoSybase.Trim());
        if (body.CobisUsuario is not null) SetValor(appObj, "Cobis", "Usuario", body.CobisUsuario.Trim());
        if (body.CobisBaseDatos is not null) SetValor(appObj, "Cobis", "BaseDatos", body.CobisBaseDatos.Trim());

        // SQL SOT: endpoint configurable por banco
        if (body.SqlSotServidor is not null) SetValor(appObj, "SqlSot", "Servidor", body.SqlSotServidor.Trim());
        if (!string.IsNullOrWhiteSpace(body.SqlSotPuerto)) SetValor(appObj, "SqlSot", "Puerto", body.SqlSotPuerto.Trim());
        if (body.SqlSotUsuario is not null) SetValor(appObj, "SqlSot", "Usuario", body.SqlSotUsuario.Trim());
        if (body.SqlSotBaseDatos is not null) SetValor(appObj, "SqlSot", "BaseDatos", body.SqlSotBaseDatos.Trim());

        // SC-416 — correlativos editables
        if (body.CierreForzadoNotaCompensada is not null)
            SetValor(appObj, "CierreForzadoNota", "CorrelativoCompensada", body.CierreForzadoNotaCompensada.Trim());
        if (body.CierreForzadoNotaNormal is not null)
            SetValor(appObj, "CierreForzadoNota", "CorrelativoNormal", body.CierreForzadoNotaNormal.Trim());
        if (body.CierreForzadoNotaMiniBoveda is not null)
            SetValor(appObj, "CierreForzadoNota", "CorrelativoMiniBoveda", body.CierreForzadoNotaMiniBoveda.Trim());

        // Retiro de efectivo — COBIS cuentas + importe aleatorio
        if (body.CobisConsultaMaxCuentas is not null)
            SetValor(appObj, "CobisConsultaCuentas", "MaxCuentasCandidatas", body.CobisConsultaMaxCuentas.Trim());
        if (body.CobisConsultaModoRotacion is not null)
            SetValor(appObj, "CobisConsultaCuentas", "ModoRotacion", body.CobisConsultaModoRotacion.Trim());
        if (body.CobisConsultaRotacionPorCliente is not null)
            SetValor(appObj, "CobisConsultaCuentas", "RotacionPorCliente", body.CobisConsultaRotacionPorCliente.Trim());
        if (body.CobisConsultaUnaCuentaPorCliente is not null)
            SetValor(appObj, "CobisConsultaCuentas", "UnaCuentaPorCliente", body.CobisConsultaUnaCuentaPorCliente.Trim());
        if (body.CobisConsultaSaldoMinimoPesos is not null)
            SetValor(appObj, "CobisConsultaCuentas", "SaldoMinimoPesos", body.CobisConsultaSaldoMinimoPesos.Trim());
        if (body.CobisConsultaSaldoMinimoExtranjera is not null)
            SetValor(appObj, "CobisConsultaCuentas", "SaldoMinimoExtranjera", body.CobisConsultaSaldoMinimoExtranjera.Trim());
        if (body.CobisConsultaSaldoMinimoCategorizada is not null)
            SetValor(appObj, "CobisConsultaCuentas", "SaldoMinimoCategorizada", body.CobisConsultaSaldoMinimoCategorizada.Trim());
        if (body.CobisConsultaTitularidadDefault is not null)
            SetValor(appObj, "CobisConsultaCuentas", "TitularidadRetiroDefault", body.CobisConsultaTitularidadDefault.Trim());
        if (body.RetiroImporteMinimo is not null)
            SetValor(appObj, "RetiroEfectivo", "ImporteMinimo", body.RetiroImporteMinimo.Trim());
        if (body.RetiroImporteMaximoPractico is not null)
            SetValor(appObj, "RetiroEfectivo", "ImporteMaximoPractico", body.RetiroImporteMaximoPractico.Trim());
        if (body.RetiroMargenSaldoResiduo is not null)
            SetValor(appObj, "RetiroEfectivo", "MargenSaldoResiduo", body.RetiroMargenSaldoResiduo.Trim());
        if (body.RetiroPorcentajeMinimo is not null)
            SetValor(appObj, "RetiroEfectivo", "PorcentajeMinimoRetiro", body.RetiroPorcentajeMinimo.Trim());
        if (body.RetiroPorcentajeMaximo is not null)
            SetValor(appObj, "RetiroEfectivo", "PorcentajeMaximoRetiro", body.RetiroPorcentajeMaximo.Trim());

        GuardarConBackup(appPath, appObj);

        // Las contraseñas van a secrets.json; solo se escriben las que llegan no vacías
        // (dejar el campo en blanco mantiene la contraseña actual).
        var usuariosConPassword = usuariosValidos
            .Where(u => !string.IsNullOrEmpty(u.Password))
            .ToList();
        if (!string.IsNullOrEmpty(body.PasswordLogin)
            || !string.IsNullOrEmpty(body.PasswordCobis)
            || !string.IsNullOrEmpty(body.PasswordSqlSot)
            || usuariosConPassword.Count > 0)
        {
            var secPath = RutaSecrets();
            var secObj = LeerObjeto(secPath);
            if (!string.IsNullOrEmpty(body.PasswordLogin))
                SetValor(secObj, "Aplicacion", "Contrasena", body.PasswordLogin);
            if (!string.IsNullOrEmpty(body.PasswordCobis))
                SetValor(secObj, "Cobis", "Contrasena", body.PasswordCobis);
            if (!string.IsNullOrEmpty(body.PasswordSqlSot))
                SetValor(secObj, "SqlSot", "Contrasena", body.PasswordSqlSot);

            // Contraseña de cada usuario SOT en secrets:UsuariosSot
            foreach (var u in usuariosConPassword)
                SetValor(secObj, "UsuariosSot", u.Usuario!.Trim(), u.Password!);

            // El principal también sincroniza Supervision/Caja (compatibilidad con el flujo actual)
            if (principal is not null && !string.IsNullOrEmpty(principal.Password))
            {
                SetValor(secObj, "Supervision", "Password", principal.Password!);
                SetValor(secObj, "Caja", "ContrasenaSupervisorApertura", principal.Password!);
            }
            GuardarSecretsConBackup(secPath, secObj);
        }

        return Results.Ok(new { ok = true, guardado = DateTimeOffset.Now });
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = "No se pudo guardar: " + ex.Message }, statusCode: 500);
    }
});

app.MapPost("/config/cliente/probar-sql-sot", async (ConfigCliente body) =>
{
    try
    {
        var appObj = LeerObjeto(RutaAppsettings());
        var secObj = LeerObjeto(RutaSecrets());

        var servidor = Efectivo(body.SqlSotServidor, Efectivo(LeerValor(appObj, "SqlSot", "Servidor"), "sqlsot.accusys-dev.io")).Trim();
        var puerto = Efectivo(body.SqlSotPuerto, Efectivo(LeerValor(appObj, "SqlSot", "Puerto"), "1433")).Trim();
        var usuario = Efectivo(body.SqlSotUsuario, Efectivo(LeerValor(appObj, "SqlSot", "Usuario"), "accusys")).Trim();
        var baseDatos = Efectivo(body.SqlSotBaseDatos, Efectivo(LeerValor(appObj, "SqlSot", "BaseDatos"), "UW_CASHIER")).Trim();
        var password = ResolverPassword(body.PasswordSqlSot, secObj, appObj, "SqlSot", "Contrasena");

        if (string.IsNullOrWhiteSpace(servidor)) throw new InvalidOperationException("Falta servidor SQL SOT.");
        if (string.IsNullOrWhiteSpace(usuario)) throw new InvalidOperationException("Falta usuario SQL SOT.");
        if (string.IsNullOrWhiteSpace(baseDatos)) throw new InvalidOperationException("Falta base SQL SOT.");
        if (string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException(
                "No hay contraseña SQL SOT guardada. Escribila en el campo Contraseña SQL SOT y pulsá «Guardar configuración» (o completala antes de probar).");

        var server = puerto == "1433" || string.IsNullOrWhiteSpace(puerto) ? servidor : $"{servidor},{puerto}";
        var cs = $"Server={server};Database={baseDatos};User Id={usuario};Password={password};TrustServerCertificate=True;Connect Timeout=10;";

        await using var conn = new SqlConnection(cs);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT DB_NAME() AS BaseDatos, @@SERVERNAME AS Servidor";
        await using var reader = await cmd.ExecuteReaderAsync();
        string? serverName = null;
        string? dbName = null;
        if (await reader.ReadAsync())
        {
            serverName = reader["Servidor"]?.ToString();
            dbName = reader["BaseDatos"]?.ToString();
        }

        return Results.Ok(new
        {
            ok = true,
            motor = "SQL Server",
            servidor = serverName ?? servidor,
            baseDatos = dbName ?? baseDatos,
            mensaje = "Conectado correctamente a la base SQL SOT."
        });
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = "No se pudo conectar a SQL SOT: " + ex.Message }, statusCode: 400);
    }
});

// Asistente de pruebas: asistente QA para borradores .feature (Features/_pruebas).
AsistenteCasos.MapEndpoints(app, automatizacionRoot, escenarios);
// Catálogo célula SOT (4 repos locales DEV → índice para Analizar).
CatalogoSotCelula.MapEndpoints(app, automatizacionRoot);
// Mapeo UI suspendido (grabación no viable por ahora). Código en UiMapeo*.cs para retomar después.
// UiMapeoEndpoints.Map(app, automatizacionRoot);
// Proyectos + nombre IA (RunnerIA).
RunnerIaProyectos.MapEndpoints(app, automatizacionRoot);
// Edición de .feature nativos de la suite (Features/**, sin _pruebas).
SuiteFeatures.MapEndpoints(app, automatizacionRoot);

app.MapPost("/config/cliente/probar-cobis", async (ConfigCliente body) =>
{
    try
    {
        var appObj = LeerObjeto(RutaAppsettings());
        var secObj = LeerObjeto(RutaSecrets());

        var host = Efectivo(body.CobisHost, Efectivo(LeerValor(appObj, "Cobis", "Host"), "192.168.50.121")).Trim();
        var puerto = Efectivo(body.PuertoSybase, Efectivo(LeerValor(appObj, "Cobis", "Puerto"), "7410")).Trim();
        var usuario = Efectivo(body.CobisUsuario, Efectivo(LeerValor(appObj, "Cobis", "Usuario"), "portiz")).Trim();
        var baseDatos = Efectivo(body.CobisBaseDatos, Efectivo(LeerValor(appObj, "Cobis", "BaseDatos"), "cobis")).Trim();
        var password = ResolverPassword(body.PasswordCobis, secObj, appObj, "Cobis", "Contrasena");

        if (string.IsNullOrWhiteSpace(host)) throw new InvalidOperationException("Falta host COBIS.");
        if (string.IsNullOrWhiteSpace(usuario)) throw new InvalidOperationException("Falta usuario COBIS.");
        if (string.IsNullOrWhiteSpace(baseDatos)) throw new InvalidOperationException("Falta base COBIS.");
        if (string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException(
                "No hay contraseña COBIS guardada. Escribila en el campo Contraseña COBIS/Sybase y pulsá «Guardar configuración» (o completala antes de probar).");

        var cs = $"Data Source={host};Port={puerto};Database={baseDatos};Uid={usuario};Pwd={password};Connection Timeout=10;";
        await using var conn = new AseConnection(cs);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT db_name() AS BaseDatos";
        var db = (await cmd.ExecuteScalarAsync())?.ToString();

        return Results.Ok(new
        {
            ok = true,
            motor = "Sybase ASE",
            servidor = host,
            baseDatos = db ?? baseDatos,
            mensaje = "Conectado correctamente a la base COBIS (Sybase)."
        });
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = "No se pudo conectar a COBIS: " + ex.Message }, statusCode: 400);
    }
});

// SPA Angular: rutas de UI (/runner, /config, …) → index.html (debe ir al final).
app.MapFallback(async (HttpContext ctx) =>
{
    var path = ctx.Request.Path.Value ?? "";
    if (path.StartsWith("/api", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/run/", StringComparison.OrdinalIgnoreCase)
        || path.Equals("/run", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/config/", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/pruebas", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/catalogo-sot", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/runner-ia", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/auth", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/suite", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/health", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/manual", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/escenarios", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/casos-prueba", StringComparison.OrdinalIgnoreCase)
        || Path.HasExtension(path))
    {
        ctx.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    if (!File.Exists(htmlPath))
    {
        ctx.Response.StatusCode = StatusCodes.Status404NotFound;
        await ctx.Response.WriteAsync("Frontend Angular no compilado.");
        return;
    }

    ctx.Response.ContentType = "text/html; charset=utf-8";
    await ctx.Response.SendFileAsync(htmlPath);
});

app.Run();

static (bool syncOk, string[] uiOnly, string[] apiOnly) EvaluarSyncCatalogo(
    string runnerRoot,
    IEnumerable<string> apiIds)
{
    var apiSet = new HashSet<string>(apiIds, StringComparer.Ordinal);
    var uiSet = ExtraerIdsCatalogoUi(runnerRoot);
    if (uiSet.Count == 0)
        return (true, [], []);

    var uiOnly = uiSet
        .Where(id => !apiSet.Contains(id) && !EsEscenarioRegresionModuloUi(id))
        .OrderBy(x => x)
        .ToArray();
    var apiOnly = apiSet.Where(id => !uiSet.Contains(id) && !id.StartsWith("prueba-", StringComparison.Ordinal))
        .OrderBy(x => x)
        .Take(50)
        .ToArray();
    return (uiOnly.Length == 0, uiOnly, apiOnly);
}

/// <summary>Regresión del módulo: orquestada en UI (lote + Excel), sin script propio en API.</summary>
static bool EsEscenarioRegresionModuloUi(string id) =>
    id.EndsWith("-regresion", StringComparison.Ordinal);

static HashSet<string> ExtraerIdsCatalogoUi(string runnerRoot)
{
    var path = Path.Combine(
        runnerRoot,
        "frontend",
        "src",
        "app",
        "core",
        "catalog-data.ts");
    if (!File.Exists(path)) return new HashSet<string>(StringComparer.Ordinal);

    var text = File.ReadAllText(path);
    var idx = text.IndexOf("export const ESCENARIOS", StringComparison.Ordinal);
    if (idx < 0) return new HashSet<string>(StringComparer.Ordinal);
    var slice = text[idx..];
    var end = slice.IndexOf("};", StringComparison.Ordinal);
    if (end > 0) slice = slice[..end];

    var rx = new Regex(@"^\s*""([^""]+)""\s*:", RegexOptions.Multiline);
    var set = new HashSet<string>(StringComparer.Ordinal);
    foreach (Match m in rx.Matches(slice))
    {
        var id = m.Groups[1].Value.Trim();
        if (!string.IsNullOrEmpty(id)) set.Add(id);
    }

    return set;
}

static (string Root, bool Ok, string[] Tried) ResolveAutomatizacionRoot(string contentRoot)
{
    var tried = new List<string>();

    bool EsSuite(string full)
    {
        tried.Add(full);
        return Directory.Exists(full) && File.Exists(Path.Combine(full, "AutomatizacionSOT.csproj"));
    }

    var desdeEnv = Environment.GetEnvironmentVariable("AutomatizacionSOT_ROOT");
    if (!string.IsNullOrWhiteSpace(desdeEnv))
    {
        var envFull = Path.GetFullPath(desdeEnv.Trim());
        if (EsSuite(envFull))
            return (envFull, true, tried.ToArray());
    }

    // Portable: ContentRoot = .../RunnerIA-win-x64/app
    var contentName = new DirectoryInfo(contentRoot).Name;
    if (string.Equals(contentName, "app", StringComparison.OrdinalIgnoreCase))
    {
        var portableRoot = Path.GetFullPath(Path.Combine(contentRoot, ".."));
        var portableCandidates = new[]
        {
            Path.Combine(portableRoot, "..", "AutomatizacionSOT", "AutomatizacionSOT"),
            Path.Combine(portableRoot, "..", "AutomatizacionSOT"),
            Path.Combine(portableRoot, "AutomatizacionSOT", "AutomatizacionSOT"),
            Path.Combine(portableRoot, "AutomatizacionSOT")
        };
        foreach (var c in portableCandidates)
        {
            var full = Path.GetFullPath(c);
            if (EsSuite(full))
                return (full, true, tried.ToArray());
        }
    }

    // Proyecto RunnerIA (hermano): ..\AutomatizacionSOT\AutomatizacionSOT
    var hermano = Path.GetFullPath(Path.Combine(contentRoot, "..", "..", "AutomatizacionSOT", "AutomatizacionSOT"));
    if (EsSuite(hermano))
        return (hermano, true, tried.ToArray());

    // Monorepo clásico: <repo>/RunnerOperador/Api → <repo>/AutomatizacionSOT
    var candidato = Path.GetFullPath(Path.Combine(contentRoot, "..", "..", "AutomatizacionSOT"));
    if (EsSuite(candidato))
        return (candidato, true, tried.ToArray());

    // Fallback: buscar hacia arriba un directorio con AutomatizacionSOT.csproj
    var dir = new DirectoryInfo(contentRoot);
    while (dir is not null)
    {
        var hitNested = Path.Combine(dir.FullName, "AutomatizacionSOT", "AutomatizacionSOT");
        if (EsSuite(hitNested))
            return (hitNested, true, tried.ToArray());
        var hit = Path.Combine(dir.FullName, "AutomatizacionSOT");
        if (EsSuite(hit))
            return (hit, true, tried.ToArray());
        if (EsSuite(dir.FullName))
            return (dir.FullName, true, tried.ToArray());
        dir = dir.Parent;
    }

    // Mejor candidato para mensajes de soporte (aunque no exista).
    var preferido = string.IsNullOrWhiteSpace(desdeEnv)
        ? hermano
        : Path.GetFullPath(desdeEnv.Trim());
    if (!tried.Contains(preferido, StringComparer.OrdinalIgnoreCase))
        tried.Add(preferido);
    return (preferido, false, tried.ToArray());
}

static Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider CreateContentTypeProvider()
{
    var provider = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
    provider.Mappings[".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    provider.Mappings[".xls"] = "application/vnd.ms-excel";
    provider.Mappings[".xlsb"] = "application/vnd.ms-excel.sheet.binary.macroEnabled.12";
    provider.Mappings[".sql"] = "application/sql";
    provider.Mappings[".csv"] = "text/csv";
    return provider;
}

static async Task ExecuteRunAsync(RunEntry run, string workingDir, string runnerRoot, SemaphoreSlim runLock)
{
    run.MarkRunning();

    var psi = new ProcessStartInfo
    {
        FileName = "powershell.exe",
        WorkingDirectory = workingDir,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
        StandardOutputEncoding = Encoding.UTF8,
        StandardErrorEncoding = Encoding.UTF8
    };

    psi.ArgumentList.Add("-NoProfile");
    psi.ArgumentList.Add("-ExecutionPolicy");
    psi.ArgumentList.Add("Bypass");
    psi.ArgumentList.Add("-File");
    psi.ArgumentList.Add(run.ScriptPath);

    foreach (var arg in run.ScriptArgs)
        psi.ArgumentList.Add(arg);

    try
    {
        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var output = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null) run.AppendLog(e.Data, output);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null) run.AppendLog(e.Data, output);
        };

        if (!process.Start())
        {
            run.MarkFailed(-1, "No se pudo iniciar PowerShell.");
            PersistirUltimaCorrida(run, runnerRoot);
            return;
        }

        run.AttachProcess(process);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // Poll cancelación mientras corre.
        while (!process.HasExited)
        {
            if (run.CancelRequested)
            {
                try
                {
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                }
                catch
                {
                    try { process.Kill(); } catch { /* ignore */ }
                }

                try { await process.WaitForExitAsync(); } catch { /* ignore */ }
                run.AppendLog("[Runner] Corrida detenida por el operador.", output);
                run.MarkFailed(-2, "Corrida detenida por el operador.");
                ResolverEvidenciaSiFalta(run, workingDir);
                PersistirUltimaCorrida(run, runnerRoot);
                return;
            }

            await Task.Delay(400);
        }

        run.MarkFinished(process.ExitCode);
        ResolverEvidenciaSiFalta(run, workingDir);
        run.ApplyOutcomeMessage();
        PersistirUltimaCorrida(run, runnerRoot);
    }
    catch (Exception ex)
    {
        run.MarkFailed(-1, ex.Message);
        ResolverEvidenciaSiFalta(run, workingDir);
        PersistirUltimaCorrida(run, runnerRoot);
    }
    finally
    {
        run.DetachProcess();
        runLock.Release();
    }
}

static string RutaUltimaCorridaJson(string runnerRoot) =>
    Path.Combine(Path.GetFullPath(runnerRoot), "data", "ultima-corrida.json");

static string RutaUltimasPorEscenarioJson(string runnerRoot) =>
    Path.Combine(Path.GetFullPath(runnerRoot), "data", "ultimas-por-escenario.json");

static void PersistirUltimaCorrida(RunEntry run, string runnerRoot)
{
    try
    {
        var path = RutaUltimaCorridaJson(runnerRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var payload = PayloadPersistenciaCorrida(run);
        File.WriteAllText(path, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
        PersistirUltimaPorEscenario(run, runnerRoot);
    }
    catch
    {
        /* no bloquear la corrida por fallo de persistencia */
    }
}

static object PayloadPersistenciaCorrida(RunEntry run) => new
{
    runId = run.Id,
    escenarioId = run.EscenarioId,
    titulo = run.Titulo,
    estado = run.Estado,
    exitCode = run.ExitCode,
    error = run.Error,
    iniciado = run.IniciadoUtc,
    finalizado = run.FinalizadoUtc,
    logTail = RunnerSecurity.RedactarParaLlm(run.GetLogTail(80)),
    evidenciaCarpeta = run.EvidenciaCarpeta,
    evidenciaInforme = run.EvidenciaInforme
};

static void PersistirUltimaPorEscenario(RunEntry run, string runnerRoot)
{
    if (string.IsNullOrWhiteSpace(run.EscenarioId)) return;
    try
    {
        var path = RutaUltimasPorEscenarioJson(runnerRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        JsonObject root;
        if (File.Exists(path))
        {
            root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject();
        }
        else
            root = new JsonObject();

        root[run.EscenarioId] = JsonSerializer.SerializeToNode(PayloadPersistenciaCorrida(run));
        File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
    catch
    {
        /* ignore */
    }
}

static RunEntry? RehidratarDesdeJsonElement(
    RunStore store,
    JsonElement root,
    string automatizacionRoot)
{
    var id = root.TryGetProperty("runId", out var rid) ? rid.GetString() : null;
    if (string.IsNullOrWhiteSpace(id)) return null;

    if (store.TryGet(id, out var existing))
        return existing;

    var escId = root.TryGetProperty("escenarioId", out var e) ? e.GetString() ?? "" : "";
    var titulo = root.TryGetProperty("titulo", out var t) ? t.GetString() ?? escId : escId;
    int? exitCode = root.TryGetProperty("exitCode", out var xc) && xc.ValueKind == JsonValueKind.Number
        ? xc.GetInt32()
        : null;
    var error = root.TryGetProperty("error", out var er) ? er.GetString() : null;
    var iniciado = root.TryGetProperty("iniciado", out var ini) && ini.TryGetDateTimeOffset(out var iniDto)
        ? iniDto
        : DateTimeOffset.UtcNow;
    DateTimeOffset? finalizado = root.TryGetProperty("finalizado", out var fin) && fin.TryGetDateTimeOffset(out var finDto)
        ? finDto
        : null;
    var carpeta = root.TryGetProperty("evidenciaCarpeta", out var c) ? c.GetString() : null;
    var informe = root.TryGetProperty("evidenciaInforme", out var i) ? i.GetString() : null;
    var logTail = root.TryGetProperty("logTail", out var l) ? l.GetString() : null;

    if (!string.IsNullOrEmpty(carpeta) && (!Directory.Exists(carpeta) || !EsRutaEvidenciaSegura(carpeta, automatizacionRoot)))
        carpeta = null;
    if (!string.IsNullOrEmpty(informe) && (!File.Exists(informe) || !EsRutaEvidenciaSegura(informe, automatizacionRoot)))
        informe = null;

    var run = RunEntry.FromPersisted(
        id, escId, titulo, exitCode, error, iniciado, finalizado, carpeta, informe, logTail);
    store.Register(run);
    return run;
}

static RunEntry? AsegurarCorridaEscenarioEnMemoria(
    RunStore store,
    string runnerRoot,
    string automatizacionRoot,
    string escenarioId)
{
    if (string.IsNullOrWhiteSpace(escenarioId)) return null;
    var path = RutaUltimasPorEscenarioJson(runnerRoot);
    if (!File.Exists(path)) return null;
    try
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        if (!doc.RootElement.TryGetProperty(escenarioId, out var node))
            return null;
        return RehidratarDesdeJsonElement(store, node, automatizacionRoot);
    }
    catch
    {
        return null;
    }
}

static RunEntry? BuscarCorridaPersistidaPorRunId(
    RunStore store,
    string runnerRoot,
    string automatizacionRoot,
    string runId)
{
    var ultima = AsegurarUltimaCorridaEnMemoria(store, runnerRoot, automatizacionRoot);
    if (ultima is not null && string.Equals(ultima.Id, runId, StringComparison.OrdinalIgnoreCase))
        return ultima;

    var path = RutaUltimasPorEscenarioJson(runnerRoot);
    if (!File.Exists(path)) return null;
    try
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            if (prop.Value.TryGetProperty("runId", out var rid)
                && string.Equals(rid.GetString(), runId, StringComparison.OrdinalIgnoreCase))
                return RehidratarDesdeJsonElement(store, prop.Value, automatizacionRoot);
        }
    }
    catch
    {
        /* ignore */
    }

    return null;
}

static IResult CrearZipEvidencia(RunEntry run, string automatizacionRoot)
{
    ResolverEvidenciaSiFalta(run, automatizacionRoot);
    var carpeta = run.EvidenciaCarpeta;
    if (string.IsNullOrEmpty(carpeta) || !Directory.Exists(carpeta))
        return Results.NotFound(new { ok = false, error = "No hay carpeta de evidencia para esta corrida." });

    if (!EsRutaEvidenciaSegura(carpeta, automatizacionRoot))
        return Results.BadRequest(new { ok = false, error = "Ruta de evidencia no permitida." });

    try
    {
        var zipDir = Path.Combine(Path.GetTempPath(), "RunnerOperadorEvidencias");
        Directory.CreateDirectory(zipDir);
        var zipName = NombreZipEvidencia(run);
        var zipPath = Path.Combine(zipDir, run.Id + "_" + zipName);
        if (File.Exists(zipPath)) File.Delete(zipPath);

        var staging = Path.Combine(zipDir, "staging_" + run.Id);
        if (Directory.Exists(staging))
            Directory.Delete(staging, recursive: true);
        CopiarCarpetaEvidenciaLocal(carpeta, staging);
        AdjuntarLogCorridaAlStaging(run, staging);
        // Garantiza informe HTML en el ZIP si existe en la carpeta.
        ZipFile.CreateFromDirectory(staging, zipPath, CompressionLevel.Fastest, includeBaseDirectory: true);
        try { Directory.Delete(staging, recursive: true); } catch { /* ignore */ }

        var bytes = File.ReadAllBytes(zipPath);
        try { File.Delete(zipPath); } catch { /* ignore */ }
        return Results.File(bytes, "application/zip", zipName);
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = "No se pudo crear el ZIP: " + ex.Message }, statusCode: 500);
    }
}

static IResult ServirInformeHtml(RunEntry run, string automatizacionRoot)
{
    ResolverEvidenciaSiFalta(run, automatizacionRoot);
    var informe = run.EvidenciaInforme;
    var carpeta = run.EvidenciaCarpeta;
    if (string.IsNullOrEmpty(informe) || !File.Exists(informe))
        return Results.NotFound(new { ok = false, error = "No se encontró el informe HTML." });

    if (!EsRutaEvidenciaSegura(informe, automatizacionRoot))
        return Results.BadRequest(new { ok = false, error = "Ruta de informe no permitida." });

    var html = File.ReadAllText(informe);
    if (!string.IsNullOrEmpty(carpeta))
        html = ReescribirInformeParaWeb(html, carpeta, run.Id);

    return Results.Content(html, "text/html; charset=utf-8");
}

static RunEntry? AsegurarUltimaCorridaEnMemoria(RunStore store, string runnerRoot, string automatizacionRoot)
{
    var path = RutaUltimaCorridaJson(runnerRoot);
    if (!File.Exists(path)) return null;
    try
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return RehidratarDesdeJsonElement(store, doc.RootElement, automatizacionRoot);
    }
    catch
    {
        return null;
    }
}

static object ToRunResponse(RunEntry run)
{
    var logLines = run.GetLogLinesForInterpretation();
    var corridaVacia = run.CorridaVacia
        || (run.Estado == "finalizado"
            && run.ExitCode == 0
            && RunExitInterpretation.IsEmptyRun(logLines, out _));

    var error = run.Estado == "finalizado" && run.ExitCode is int ec && ec >= 0 && !corridaVacia
        ? RunExitInterpretation.InterpretError(
            ec,
            run.EvidenciaCarpeta,
            run.EvidenciaInforme,
            logLines)
        : run.Error;

  if (corridaVacia && string.IsNullOrWhiteSpace(error))
      error = "No se ejecutó ningún escenario — revisar filtro/tag.";

    return new
    {
        runId = run.Id,
        escenarioId = run.EscenarioId,
        titulo = run.Titulo,
        estado = run.Estado,
        ok = run.Estado == "finalizado" && RunExitInterpretation.IsRunOk(run.ExitCode, corridaVacia),
        corridaVacia,
        fallasEscenarios = run.Estado == "finalizado" && RunExitInterpretation.IsTestFailureExitCode(run.ExitCode, logLines),
        errorProceso = run.Estado == "finalizado" && RunExitInterpretation.IsProcessErrorExitCode(run.ExitCode, logLines),
        exitCode = run.ExitCode,
        error,
        iniciado = run.IniciadoUtc,
        finalizado = run.FinalizadoUtc,
        log = RunnerSecurity.RedactarParaLlm(run.GetLogTail(120)),
        tieneEvidencia = !string.IsNullOrEmpty(run.EvidenciaCarpeta) && Directory.Exists(run.EvidenciaCarpeta),
        // No exponer rutas absolutas de disco al cliente (solo URLs relativas).
        evidenciaCarpeta = (string?)null,
        evidenciaInforme = (string?)null,
        evidenciaZipUrl = string.IsNullOrEmpty(run.EvidenciaCarpeta) ? null : $"/run/{run.Id}/evidencia.zip",
        evidenciaZipNombre = string.IsNullOrEmpty(run.EvidenciaCarpeta) ? null : NombreZipEvidencia(run),
        evidenciaInformeUrl = string.IsNullOrEmpty(run.EvidenciaInforme) ? null : $"/run/{run.Id}/informe",
        evidenciaZipEscenarioUrl = string.IsNullOrEmpty(run.EscenarioId) || string.IsNullOrEmpty(run.EvidenciaCarpeta)
            ? null
            : $"/run/escenario/{Uri.EscapeDataString(run.EscenarioId)}/evidencia.zip",
        evidenciaInformeEscenarioUrl = string.IsNullOrEmpty(run.EscenarioId) || string.IsNullOrEmpty(run.EvidenciaInforme)
            ? null
            : $"/run/escenario/{Uri.EscapeDataString(run.EscenarioId)}/informe"
    };
}
static string NombreZipEvidencia(RunEntry run)
{
    var titulo = SanitizarNombreArchivo(string.IsNullOrWhiteSpace(run.Titulo) ? (run.EscenarioId ?? "corrida") : run.Titulo);
    if (titulo.Length > 80) titulo = titulo[..80].TrimEnd('_', '-', ' ');
    var cuando = (run.FinalizadoUtc ?? run.IniciadoUtc).ToLocalTime();
    return $"{titulo}_{cuando:yyyy-MM-dd_HH-mm-ss}.zip";
}

static void ResolverCorridasEvidenciaModulo(
    ModuloEvidenciasRequest body,
    RunStore store,
    string runnerRoot,
    string automatizacionRoot)
{
    if (body.Corridas is null || body.Corridas.Count == 0)
        return;

    foreach (var corrida in body.Corridas)
    {
        RunEntry? run = null;
        if (!string.IsNullOrWhiteSpace(corrida.RunId))
        {
            if (!store.TryGet(corrida.RunId, out run))
                run = BuscarCorridaPersistidaPorRunId(store, runnerRoot, automatizacionRoot, corrida.RunId);
        }

        if (run is null && !string.IsNullOrWhiteSpace(corrida.EscenarioId))
            run = AsegurarCorridaEscenarioEnMemoria(store, runnerRoot, automatizacionRoot, corrida.EscenarioId);

        if (run is null)
            continue;

        ResolverEvidenciaSiFalta(run, automatizacionRoot);
        if (string.IsNullOrWhiteSpace(corrida.EvidenciaCarpeta))
            corrida.EvidenciaCarpeta = run.EvidenciaCarpeta;
    }
}

static void ResolverEvidenciaSiFalta(RunEntry run, string automatizacionRoot)
{
    if (!string.IsNullOrEmpty(run.EvidenciaCarpeta) && Directory.Exists(run.EvidenciaCarpeta))
    {
        if (!EsRutaEvidenciaSegura(run.EvidenciaCarpeta, automatizacionRoot))
            run.SetEvidencia("", null);
        return;
    }

    // 1) Marcadores en el log de la corrida
    var (fromLogCarpeta, fromLogInforme) = run.ExtraerEvidenciaDelLog();
    if (!string.IsNullOrEmpty(fromLogCarpeta) && Directory.Exists(fromLogCarpeta)
        && EsRutaEvidenciaSegura(fromLogCarpeta, automatizacionRoot))
    {
        run.SetEvidencia(fromLogCarpeta, fromLogInforme);
        return;
    }

    // 2) Marcador JSON escrito por Hooks AfterTestRun
    var carpetaBase = ResolverCarpetaBaseEvidencia(automatizacionRoot);
    var marker = Path.Combine(carpetaBase, ".ultima-ejecucion-runner.json");
    if (File.Exists(marker))
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(marker));
            var carpeta = doc.RootElement.TryGetProperty("carpeta", out var c) ? c.GetString() : null;
            var informe = doc.RootElement.TryGetProperty("informe", out var i) ? i.GetString() : null;
            if (!string.IsNullOrEmpty(carpeta) && Directory.Exists(carpeta)
                && EsRutaEvidenciaSegura(carpeta, automatizacionRoot))
            {
                var creado = Directory.GetCreationTimeUtc(carpeta);
                if (creado >= run.IniciadoUtc.UtcDateTime.AddMinutes(-2))
                {
                    run.SetEvidencia(carpeta, informe);
                    return;
                }
            }
        }
        catch { /* ignore */ }
    }

    // 3) Primera carpeta Ejecucion_* creada tras el inicio y no reclamada por otra corrida
    if (Directory.Exists(carpetaBase))
    {
        var candidata = Directory.GetDirectories(carpetaBase, "Ejecucion_*")
            .Select(d => new DirectoryInfo(d))
            .Where(d => !d.FullName.Contains($"{Path.DirectorySeparatorChar}_archivo{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(d => d.CreationTimeUtc >= run.IniciadoUtc.UtcDateTime.AddMinutes(-2))
            .Where(d => !RunEntry.CarpetaAsignadaAOtraCorrida(d.FullName, run.Id))
            .Where(d => EsRutaEvidenciaSegura(d.FullName, automatizacionRoot))
            .OrderBy(d => d.CreationTimeUtc)
            .FirstOrDefault();
        if (candidata is not null)
        {
            var informe = Path.Combine(candidata.FullName, "Informes", "ReporteEjecucion.html");
            run.SetEvidencia(candidata.FullName, File.Exists(informe) ? informe : null);
        }
    }
}

static string ResolverCarpetaBaseEvidencia(string automatizacionRoot)
{
    try
    {
        var appsettings = Path.Combine(automatizacionRoot, "appsettings.json");
        if (File.Exists(appsettings))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(appsettings));
            if (doc.RootElement.TryGetProperty("Evidencia", out var ev)
                && ev.TryGetProperty("CarpetaBase", out var cb))
            {
                var v = cb.GetString()?.Trim();
                if (!string.IsNullOrEmpty(v))
                    return Path.GetFullPath(v);
            }
        }
    }
    catch { /* ignore */ }

    return Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        "Evidencia y reportes");
}

static bool EsRutaEvidenciaSegura(string ruta, string automatizacionRoot)
{
    try
    {
        var full = Path.GetFullPath(ruta);
        var baseEv = Path.GetFullPath(ResolverCarpetaBaseEvidencia(automatizacionRoot))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var fullNorm = full.EndsWith(Path.DirectorySeparatorChar) || Directory.Exists(full)
            ? full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar
            : full;
        // Prefijo con separador: evita que C:\ev\EjecucionX matchee C:\ev\Ejecucion
        return fullNorm.StartsWith(baseEv, StringComparison.OrdinalIgnoreCase)
               || string.Equals(
                   full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                   baseEv.TrimEnd(Path.DirectorySeparatorChar),
                   StringComparison.OrdinalIgnoreCase);
    }
    catch
    {
        return false;
    }
}

static bool EsNombreArchivoSensible(string? nombre) =>
    SecretRedactor.EsNombreArchivoSensible(nombre);

static string SanitizarNombreArchivo(string nombre)
{
    foreach (var c in Path.GetInvalidFileNameChars())
        nombre = nombre.Replace(c, '_');
    return string.IsNullOrWhiteSpace(nombre) ? "evidencia" : nombre;
}

/// <summary>
/// Convierte rutas absolutas del informe Extent (p. ej. C:\...\Capturas\01.png) en URLs
/// servidas por el runner: /run/{id}/evidencia-file/...
/// </summary>
static string ReescribirInformeParaWeb(string html, string carpetaEjecucion, string runId)
{
    var root = Path.GetFullPath(carpetaEjecucion).TrimEnd('\\', '/');
    var prefix = $"/run/{runId}/evidencia-file/";
    var informesRoot = Path.Combine(root, "Informes");

    // Variantes típicas que deja Extent / Windows
    var roots = new[]
    {
        root,
        root.Replace('\\', '/'),
        "file:///" + root.Replace('\\', '/'),
        "file://" + root.Replace('\\', '/')
    };

    foreach (var r in roots.Distinct(StringComparer.OrdinalIgnoreCase))
    {
        if (string.IsNullOrEmpty(r)) continue;
        var idx = 0;
        while (true)
        {
            idx = html.IndexOf(r, idx, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) break;

            var end = idx + r.Length;
            // Tomar el resto de la ruta hasta comilla, espacio o >
            var pathEnd = end;
            while (pathEnd < html.Length)
            {
                var ch = html[pathEnd];
                if (ch is '"' or '\'' or '<' or '>' or ')' or ' ') break;
                pathEnd++;
            }

            var absOrSuffix = html[idx..pathEnd];
            string? rel;
            try
            {
                // absOrSuffix empieza con root; obtener relatividad
                var absNorm = absOrSuffix
                    .Replace("file:///", "", StringComparison.OrdinalIgnoreCase)
                    .Replace("file://", "", StringComparison.OrdinalIgnoreCase)
                    .Replace('/', Path.DirectorySeparatorChar);
                if (!Path.IsPathRooted(absNorm))
                    absNorm = Path.Combine(root, absNorm.TrimStart('\\', '/'));
                absNorm = Path.GetFullPath(absNorm);
                rel = Path.GetRelativePath(root, absNorm);
            }
            catch
            {
                idx = pathEnd;
                continue;
            }

            if (rel.StartsWith("..", StringComparison.Ordinal))
            {
                idx = pathEnd;
                continue;
            }

            var parts = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var encoded = string.Join('/', parts.Select(Uri.EscapeDataString));
            var replacement = prefix + encoded;
            html = html[..idx] + replacement + html[pathEnd..];
            idx += replacement.Length;
        }
    }

    // Links relativos del informe (../Escenario/Capturas/x.png) → endpoint web.
    html = ReescribirHrefsRelativosInforme(html, informesRoot, root, prefix);
    html = ReescribirLinksBloquesInforme(html, prefix);

    return html;
}

/// <summary>
/// Enlaces del índice de corrida a los informes por bloque (<c>Informes/Reporte_*.html</c>) →
/// endpoint web. Abren en pestaña nueva: el índice se sirve dentro del iframe de la SPA y volver
/// atrás sacaría a la SPA de su ruta (pantalla en blanco).
/// </summary>
static string ReescribirLinksBloquesInforme(string html, string prefix)
{
    return System.Text.RegularExpressions.Regex.Replace(
        html,
        """(?i)<a\s+href=(["'])(Reporte_[^"'>/\\]+\.html)\1""",
        m =>
        {
            var quote = m.Groups[1].Value;
            var archivo = Uri.EscapeDataString(m.Groups[2].Value);
            return $"<a target=\"_blank\" rel=\"noopener\" href={quote}{prefix}Informes/{archivo}{quote}";
        });
}

/// <summary>
/// Reescribe href="../…/Capturas/….png" del HTML (válidos al abrir el ZIP) a /run/…/evidencia-file/….
/// </summary>
static string ReescribirHrefsRelativosInforme(string html, string carpetaInformes, string carpetaEjecucion, string prefix)
{
    // href="../algo/Capturas/archivo.png" o src= lo mismo
    return System.Text.RegularExpressions.Regex.Replace(
        html,
        """(?i)\b(href|src)=(["'])(\.\./[^"'>\s]+\.(?:png|jpe?g|gif|webp))\2""",
        m =>
        {
            var attr = m.Groups[1].Value;
            var quote = m.Groups[2].Value;
            var relFromInformes = m.Groups[3].Value.Replace('/', Path.DirectorySeparatorChar);
            try
            {
                var abs = Path.GetFullPath(Path.Combine(carpetaInformes, relFromInformes));
                var rootFull = Path.GetFullPath(carpetaEjecucion);
                if (!abs.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase) || !File.Exists(abs))
                    return m.Value;
                var rel = Path.GetRelativePath(rootFull, abs);
                var parts = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var encoded = string.Join('/', parts.Select(Uri.EscapeDataString));
                return $"{attr}={quote}{prefix}{encoded}{quote}";
            }
            catch
            {
                return m.Value;
            }
        });
}

/// <summary>
/// Incluye el log de la corrida en el ZIP (Informes/log-corrida.txt).
/// </summary>
static void AdjuntarLogCorridaAlStaging(RunEntry run, string staging)
{
    try
    {
        var informes = Path.Combine(staging, "Informes");
        Directory.CreateDirectory(informes);
        var logPath = Path.Combine(informes, "log-corrida.txt");
        var contenido = string.Join(Environment.NewLine, run.GetLogLinesForInterpretation());
        if (string.IsNullOrWhiteSpace(contenido))
            contenido = run.GetLogTail(500);
        File.WriteAllText(logPath, RunnerSecurity.RedactarParaLlm(contenido));
    }
    catch
    {
        // No bloquear la descarga del ZIP si falla adjuntar el log.
    }
}

/// <summary>
/// Copia evidencia a disco local (hidrata OneDrive) antes de generar el ZIP.
/// Excluye archivos sensibles (secrets, auth, passwords).
/// </summary>
static void CopiarCarpetaEvidenciaLocal(string origen, string destino)
{
    Directory.CreateDirectory(destino);
    foreach (var dir in Directory.GetDirectories(origen, "*", SearchOption.AllDirectories))
    {
        var rel = Path.GetRelativePath(origen, dir);
        Directory.CreateDirectory(Path.Combine(destino, rel));
    }

    foreach (var file in Directory.GetFiles(origen, "*", SearchOption.AllDirectories))
    {
        if (EsNombreArchivoSensible(Path.GetFileName(file)))
            continue;
        var rel = Path.GetRelativePath(origen, file);
        var target = Path.Combine(destino, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        using var src = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var dst = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None);
        src.CopyTo(dst);
    }
}

sealed record ConfigGuardar(string Name, string Content);

sealed record ConfigCliente(
    string? Sucursal,
    string? CodigoSucursal,
    string? Usuario,
    UsuarioSotDto[]? UsuariosSot,
    string? CobisServidor,
    string? CobisHost,
    string? PuertoSybase,
    string? CobisUsuario,
    string? CobisBaseDatos,
    string? PasswordLogin,
    string? PasswordCobis,
    string? SqlSotServidor = null,
    string? SqlSotPuerto = null,
    string? SqlSotUsuario = null,
    string? SqlSotBaseDatos = null,
    string? PasswordSqlSot = null,
    string? UrlInicio = null,
    string? Ambiente = null,
    string? AuthOpenIdUrl = null,
    bool? PermitirAuthQa = null,
    string? CierreForzadoNotaCompensada = null,
    string? CierreForzadoNotaNormal = null,
    string? CierreForzadoNotaMiniBoveda = null,
    string? CobisConsultaMaxCuentas = null,
    string? CobisConsultaModoRotacion = null,
    string? CobisConsultaRotacionPorCliente = null,
    string? CobisConsultaUnaCuentaPorCliente = null,
    string? CobisConsultaSaldoMinimoPesos = null,
    string? CobisConsultaSaldoMinimoExtranjera = null,
    string? CobisConsultaSaldoMinimoCategorizada = null,
    string? CobisConsultaTitularidadDefault = null,
    string? RetiroImporteMinimo = null,
    string? RetiroImporteMaximoPractico = null,
    string? RetiroMargenSaldoResiduo = null,
    string? RetiroPorcentajeMinimo = null,
    string? RetiroPorcentajeMaximo = null);

sealed record UsuarioSotDto(string? Rol, string? Usuario, string? Password);

sealed class RunStore
{
    private readonly ConcurrentDictionary<string, RunEntry> _runs = new();

    public RunEntry Create(string escenarioId, EscenarioDef escenario, string scriptPath)
    {
        var run = new RunEntry(escenarioId, escenario, scriptPath);
        _runs[run.Id] = run;
        return run;
    }

    public void Register(RunEntry run) => _runs[run.Id] = run;

    public bool TryGet(string runId, out RunEntry run) => _runs.TryGetValue(runId, out run!);
}

sealed class RunEntry
{
    private readonly object _sync = new();
    private readonly List<string> _lines = [];
    private static readonly Regex RxCarpeta = new(@"\[Evidencia\]\s+RUNNER_CARPETA=(.+)$", RegexOptions.Compiled);
    private static readonly Regex RxInforme = new(@"\[Evidencia\]\s+RUNNER_INFORME=(.+)$", RegexOptions.Compiled);
    private static readonly Regex RxInformeLegacy = new(@"\[Evidencia\]\s+Informe HTML:\s*(.+)$", RegexOptions.Compiled);
    private static readonly Regex RxCorrida = new(@"\[Evidencia\]\s+Corrida(?:\s*\([^)]*\))?:\s*(.+)$", RegexOptions.Compiled);

    public RunEntry(string escenarioId, EscenarioDef escenario, string scriptPath)
    {
        Id = Guid.NewGuid().ToString("N")[..12];
        EscenarioId = escenarioId;
        Titulo = escenario.Titulo;
        ScriptPath = scriptPath;
        ScriptArgs = escenario.Args;
        Estado = "en_cola";
        IniciadoUtc = DateTimeOffset.UtcNow;
    }

    private RunEntry(
        string id,
        string escenarioId,
        string titulo,
        int? exitCode,
        string? error,
        DateTimeOffset iniciado,
        DateTimeOffset? finalizado,
        string? carpeta,
        string? informe,
        string? logTail)
    {
        Id = id;
        EscenarioId = escenarioId;
        Titulo = titulo;
        ScriptPath = "";
        ScriptArgs = [];
        Estado = "finalizado";
        ExitCode = exitCode;
        Error = error;
        IniciadoUtc = iniciado;
        FinalizadoUtc = finalizado;
        EvidenciaCarpeta = carpeta;
        EvidenciaInforme = informe;
        if (!string.IsNullOrWhiteSpace(logTail))
            _lines.AddRange(logTail.Replace("\r\n", "\n").Split('\n'));
    }

    public static RunEntry FromPersisted(
        string id,
        string escenarioId,
        string titulo,
        int? exitCode,
        string? error,
        DateTimeOffset iniciado,
        DateTimeOffset? finalizado,
        string? carpeta,
        string? informe,
        string? logTail) =>
        new(id, escenarioId, titulo, exitCode, error, iniciado, finalizado, carpeta, informe, logTail);

    public string Id { get; }
    public string EscenarioId { get; }
    public string Titulo { get; }
    public string ScriptPath { get; }
    public string[] ScriptArgs { get; }
    public string Estado { get; private set; }
    public int? ExitCode { get; private set; }
    public string? Error { get; private set; }
    public DateTimeOffset IniciadoUtc { get; }
    public DateTimeOffset? FinalizadoUtc { get; private set; }
    public string? EvidenciaCarpeta { get; private set; }
    public string? EvidenciaInforme { get; private set; }
    public bool CancelRequested { get; private set; }
    /// <summary>Exit 0 pero 0 tests / solo Omitidos.</summary>
    public bool CorridaVacia { get; private set; }
    private Process? _process;

    public void AttachProcess(Process process)
    {
        lock (_sync) _process = process;
    }

    public void DetachProcess()
    {
        lock (_sync) _process = null;
    }

    /// <summary>Marca cancelación y mata el árbol de procesos si ya arrancó.</summary>
    public bool RequestCancel()
    {
        Process? process;
        lock (_sync)
        {
            CancelRequested = true;
            process = _process;
        }

        try
        {
            if (process is { HasExited: false })
            {
                process.Kill(entireProcessTree: true);
                lock (_sync) _lines.Add("[Runner] Kill enviado al proceso PowerShell.");
                return true;
            }
        }
        catch (Exception ex)
        {
            lock (_sync) _lines.Add("[Runner] No se pudo matar el proceso: " + ex.Message);
        }

        return false;
    }

    public void MarkRunning()
    {
        lock (_sync) Estado = "ejecutando";
    }

    public void AppendLog(string line, StringBuilder mirror)
    {
        lock (_sync)
        {
            _lines.Add(line);
            mirror.AppendLine(line);
            TryParseEvidenciaLine(line);
        }
    }

    /// <summary>Carpeta de evidencia → corrida que ya la reclamó (evita atribuirla a otra corrida).</summary>
    private static readonly ConcurrentDictionary<string, string> CarpetasAsignadas = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>La carpeta ya pertenece a otra corrida, así que no puede resolverse para esta.</summary>
    public static bool CarpetaAsignadaAOtraCorrida(string carpeta, string runId) =>
        CarpetasAsignadas.TryGetValue(carpeta, out var dueño) &&
        !string.Equals(dueño, runId, StringComparison.OrdinalIgnoreCase);

    public void SetEvidencia(string carpeta, string? informe)
    {
        lock (_sync)
        {
            if (string.IsNullOrWhiteSpace(carpeta))
            {
                if (!string.IsNullOrEmpty(EvidenciaCarpeta))
                    CarpetasAsignadas.TryRemove(EvidenciaCarpeta, out _);
                EvidenciaCarpeta = null;
                EvidenciaInforme = null;
                return;
            }

            EvidenciaCarpeta = carpeta;
            CarpetasAsignadas[carpeta] = Id;
            if (!string.IsNullOrEmpty(informe))
                EvidenciaInforme = informe;
            else
            {
                var cand = Path.Combine(carpeta, "Informes", "ReporteEjecucion.html");
                if (File.Exists(cand)) EvidenciaInforme = cand;
            }
        }
    }

    public (string? Carpeta, string? Informe) ExtraerEvidenciaDelLog()
    {
        lock (_sync)
        {
            string? carpeta = EvidenciaCarpeta;
            string? informe = EvidenciaInforme;
            foreach (var line in _lines)
            {
                var m1 = RxCarpeta.Match(line);
                if (m1.Success) carpeta = m1.Groups[1].Value.Trim();
                var m2 = RxInforme.Match(line);
                if (m2.Success) informe = m2.Groups[1].Value.Trim();
                var m3 = RxInformeLegacy.Match(line);
                if (m3.Success) informe = m3.Groups[1].Value.Trim();
                var m4 = RxCorrida.Match(line);
                if (m4.Success) carpeta ??= m4.Groups[1].Value.Trim();
            }
            return (carpeta, informe);
        }
    }

    private void TryParseEvidenciaLine(string line)
    {
        var m1 = RxCarpeta.Match(line);
        if (m1.Success) EvidenciaCarpeta = m1.Groups[1].Value.Trim();
        var m2 = RxInforme.Match(line);
        if (m2.Success) EvidenciaInforme = m2.Groups[1].Value.Trim();
        var m3 = RxInformeLegacy.Match(line);
        if (m3.Success) EvidenciaInforme = m3.Groups[1].Value.Trim();
        var m4 = RxCorrida.Match(line);
        if (m4.Success && string.IsNullOrEmpty(EvidenciaCarpeta))
            EvidenciaCarpeta = m4.Groups[1].Value.Trim();
    }

    public void MarkFinished(int exitCode)
    {
        lock (_sync)
        {
            ExitCode = exitCode;
            FinalizadoUtc = DateTimeOffset.UtcNow;
            Estado = "finalizado";
        }
    }

    /// <summary>Tras resolver evidencia: mensaje distinto para fallas de escenario (exit 1) vs error de proceso.</summary>
    public void ApplyOutcomeMessage()
    {
        lock (_sync)
        {
            if (ExitCode is null) return;
            var lines = _lines.ToList();
            if (ExitCode == 0 && RunExitInterpretation.IsEmptyRun(lines, out var emptyReason))
            {
                CorridaVacia = true;
                Error = emptyReason ?? "No se ejecutó ningún escenario — revisar filtro/tag.";
                return;
            }

            CorridaVacia = false;
            Error = RunExitInterpretation.InterpretError(
                ExitCode.Value,
                EvidenciaCarpeta,
                EvidenciaInforme,
                lines);
        }
    }

    public void MarkFailed(int exitCode, string error)
    {
        lock (_sync)
        {
            ExitCode = exitCode;
            Error = error;
            FinalizadoUtc = DateTimeOffset.UtcNow;
            Estado = "finalizado";
            _lines.Add(error);
        }
    }

    public string GetLogTail(int maxLines)
    {
        lock (_sync)
        {
            if (_lines.Count <= maxLines) return string.Join(Environment.NewLine, _lines);
            return string.Join(Environment.NewLine, _lines.Skip(_lines.Count - maxLines));
        }
    }

    public IReadOnlyList<string> GetLogLinesForInterpretation()
    {
        lock (_sync) return _lines.ToList();
    }
}
