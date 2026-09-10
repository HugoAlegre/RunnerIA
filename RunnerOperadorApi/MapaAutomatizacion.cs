using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

/// <summary>
/// Mapa de conocimiento para RunnerIA: circuitos por módulo, steps SpecFlow,
/// XPaths de appsettings/Settings y pistas de UI Angular (botones, testid, rutas).
/// Se regenera desde el código local y se inyecta en Analizar / LLM.
/// </summary>
public static class MapaAutomatizacion
{
    private static readonly object Gate = new();
    private static MapaDoc? Cache;
    private static DateTimeOffset CacheUtc;

    public sealed class MapaDoc
    {
        public DateTimeOffset GeneradoUtc { get; set; }
        public List<ModuloMapa> Modulos { get; set; } = [];
        public List<SelectorMapa> Selectores { get; set; } = [];
        public List<StepMapa> Steps { get; set; } = [];
        public List<PistaUi> PistasUi { get; set; } = [];
        public List<ConsultaDbMapa> ConsultasDb { get; set; } = [];
        public List<string> Avisos { get; set; } = [];
    }

    public sealed class ConsultaDbMapa
    {
        public string Motor { get; set; } = ""; // COBIS | SQL_SOT
        public string Nombre { get; set; } = "";
        public string Proposito { get; set; } = "";
        public string Sql { get; set; } = "";
        public string Tablas { get; set; } = "";
        public string Origen { get; set; } = "";
        public string Modulo { get; set; } = "";
    }

    public sealed class ModuloMapa
    {
        public string Id { get; set; } = "";
        public string Nombre { get; set; } = "";
        public List<string> Keywords { get; set; } = [];
        public List<string> Circuito { get; set; } = [];
        public List<string> Pantallas { get; set; } = [];
        public List<string> StepsClave { get; set; } = [];
    }

    public sealed class SelectorMapa
    {
        public string Modulo { get; set; } = "";
        public string Clave { get; set; } = "";
        public string Valor { get; set; } = "";
        public string Origen { get; set; } = "";
    }

    public sealed class StepMapa
    {
        public string Modulo { get; set; } = "";
        public string Tipo { get; set; } = "";
        public string Texto { get; set; } = "";
        public string Archivo { get; set; } = "";
    }

    public sealed class PistaUi
    {
        public string Modulo { get; set; } = "";
        public string Tipo { get; set; } = "";
        public string Valor { get; set; } = "";
        public string Archivo { get; set; } = "";
    }

    private static string RutaMapa(string pruebasFeatures) =>
        Path.Combine(pruebasFeatures, "mapa-automatizacion.json");

    public static MapaDoc AsegurarMapa(string automatizacionRoot, string pruebasFeatures, bool forzar = false)
    {
        lock (Gate)
        {
            var path = RutaMapa(pruebasFeatures);
            if (!forzar && Cache is not null && (DateTimeOffset.UtcNow - CacheUtc).TotalMinutes < 30)
                return Cache;

            if (!forzar && File.Exists(path))
            {
                try
                {
                    var disk = JsonSerializer.Deserialize<MapaDoc>(File.ReadAllText(path), JsonOpts());
                    if (disk is not null && disk.Modulos.Count > 0 &&
                        (DateTimeOffset.UtcNow - disk.GeneradoUtc).TotalHours < 12)
                    {
                        Cache = disk;
                        CacheUtc = DateTimeOffset.UtcNow;
                        return disk;
                    }
                }
                catch { /* rebuild */ }
            }

            var mapa = Construir(automatizacionRoot);
            Directory.CreateDirectory(pruebasFeatures);
            File.WriteAllText(path, JsonSerializer.Serialize(mapa, JsonOptsIndented()) + Environment.NewLine);
            Cache = mapa;
            CacheUtc = DateTimeOffset.UtcNow;
            return mapa;
        }
    }

    public static object Info(string automatizacionRoot, string pruebasFeatures)
    {
        var m = AsegurarMapa(automatizacionRoot, pruebasFeatures);
            return new
            {
                ok = true,
                generadoUtc = m.GeneradoUtc,
                modulos = m.Modulos.Count,
                selectores = m.Selectores.Count,
                steps = m.Steps.Count,
                pistasUi = m.PistasUi.Count,
                consultasDb = m.ConsultasDb.Count,
                avisos = m.Avisos,
                modulosDetalle = m.Modulos.Select(x => new
                {
                    x.Id,
                    x.Nombre,
                    circuito = x.Circuito.Count,
                    pantallas = x.Pantallas.Count,
                    steps = x.StepsClave.Count
                })
            };
    }

    /// <summary>Texto del mapa para Analizar. Siempre incluye TODOS los circuitos (aunque no haya contexto).</summary>
    public static string ObtenerTextoParaAnalizar(
        string automatizacionRoot,
        string pruebasFeatures,
        string? corpusHint,
        int maxChars = 22000)
    {
        var mapa = AsegurarMapa(automatizacionRoot, pruebasFeatures);
        var hint = (corpusHint ?? "").ToLowerInvariant();
        var mods = mapa.Modulos
            .Select(m => new
            {
                M = m,
                Score = m.Keywords.Count(k => hint.Contains(k, StringComparison.OrdinalIgnoreCase))
                    + (hint.Contains(m.Id, StringComparison.OrdinalIgnoreCase) ? 3 : 0)
                    + (hint.Contains(m.Nombre, StringComparison.OrdinalIgnoreCase) ? 2 : 0)
            })
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.M.Nombre)
            .ToList();

        // Foco: los que matchean el detalle; si no hay hint, igual se listan TODOS los circuitos abajo.
        var foco = mods.Where(x => x.Score > 0).Select(x => x.M).Take(4).ToList();
        if (foco.Count == 0)
            foco = mapa.Modulos.Take(Math.Min(8, mapa.Modulos.Count)).ToList();

        var ids = new HashSet<string>(foco.Select(m => m.Id), StringComparer.OrdinalIgnoreCase);
        var sb = new StringBuilder();
        sb.AppendLine("MAPA AUTOMATIZACIÓN SOT (siempre disponible — no hace falta Jira/docs para mapear)");
        sb.AppendLine($"GeneradoUtc: {mapa.GeneradoUtc:o}");
        sb.AppendLine("MODO DETALLE: el operador escribe en lenguaje natural qué quiere probar.");
        sb.AppendLine("Tu trabajo: interpretar esa intención y mapearla al circuito/pantallas/steps de este mapa.");
        sb.AppendLine("Preferí frases exactas del circuito o StepsClave. No inventes pantallas fuera del mapa.");
        sb.AppendLine();
        sb.AppendLine(ReglasGeneracion.BloqueUserPrompt());

        // Índice completo SIEMPRE (todos los módulos con circuito resumido)
        sb.AppendLine("## TODOS LOS MÓDULOS Y CIRCUITOS (conocimiento base)");
        foreach (var m in mapa.Modulos)
        {
            sb.AppendLine($"### {m.Nombre} (id={m.Id})");
            if (m.Keywords.Count > 0)
                sb.AppendLine("Keywords: " + string.Join(", ", m.Keywords.Take(14)));
            if (m.Pantallas.Count > 0)
                sb.AppendLine("Pantallas: " + string.Join(", ", m.Pantallas.Distinct().Take(10)));
            foreach (var c in m.Circuito.Take(16))
                sb.AppendLine("  - " + c);
            sb.AppendLine();
        }

        sb.AppendLine("## DETALLE DEL FOCO (selectores / UI del módulo más probable)");
        foreach (var m in foco)
        {
            sb.AppendLine($"## Módulo foco: {m.Nombre} (id={m.Id})");
            if (m.StepsClave.Count > 0)
            {
                sb.AppendLine("Steps clave:");
                foreach (var s in m.StepsClave.Take(16))
                    sb.AppendLine("  - " + s);
            }

            var sels = mapa.Selectores.Where(s => ids.Contains(s.Modulo) || string.Equals(s.Modulo, m.Id, StringComparison.OrdinalIgnoreCase)).Take(18);
            if (sels.Any())
            {
                sb.AppendLine("Selectores / XPath conocidos:");
                foreach (var s in sels)
                    sb.AppendLine($"  - [{s.Clave}] {Truncar(s.Valor, 140)}");
            }

            var pistas = mapa.PistasUi.Where(p => string.Equals(p.Modulo, m.Id, StringComparison.OrdinalIgnoreCase)).Take(20);
            if (pistas.Any())
            {
                sb.AppendLine("UI (botones / testid / labels del front):");
                foreach (var p in pistas)
                    sb.AppendLine($"  - ({p.Tipo}) {p.Valor}");
            }
            sb.AppendLine();
        }

        // Reglas de negocio Fallas de caja (siempre si el ticket/módulo lo pide)
        var quiereFallas = ContieneAlguno(hint,
            "falla de caja", "fallas de caja", "sobrante", "faltante", "tira auditora",
            "caja descuadrada", "sc-470", "ta- fallas", "ta fallas", "descuadre");
        if (quiereFallas || foco.Any(m => m.Id is "fallas-caja" or "cierre-cuadre"))
        {
            sb.AppendLine("## REGLAS FALLAS DE CAJA (cierre con sobrante/faltante)");
            sb.AppendLine("Dominio: Cuadre y cierre + verificaciones. NO es Intercaja.");
            sb.AppendLine("Circuito OBLIGATORIO completo:");
            sb.AppendLine("  1) Ir a Cuadre y cierre y elegir caja abierta.");
            sb.AppendLine("  2) Asegurar que la caja tenga dinero (saldo minimo / Otros Ingresos si está en cero).");
            sb.AppendLine("  3) Iniciar cierre con billetaje distinto al saldo → Caja descuadrada (sobrante o faltante).");
            sb.AppendLine("  4) Registrar la falla con supervisión Local y confirmar el cierre.");
            sb.AppendLine("  5) Verificar en Transacciones Monetarias que los montos de la falla coinciden con documento e imágenes.");
            sb.AppendLine("  6) Verificar en Reportes > Tira auditora el mismo evento/montos (tipo, importe, caja, usuario).");
            sb.AppendLine("Parametrías: faltante_falla_ID / sobrante_falla_ID; una sola falla por día por caja/sucursal/moneda.");
            sb.AppendLine("Si el módulo elegido es «TA- Fallas de Caja», NO acortes el circuito: debe incluir TM + tira auditora.");
            sb.AppendLine("Al interpretar capturas/docs: extraer montos exactos y contrastarlos en ambos reportes.");
            sb.AppendLine();
        }

        var quiereCierreForzadoNota = ContieneAlguno(hint,
            "sc-416", "cierre forzado", "forzar cierre", "nota de billetaje", "nota billetaje",
            "compensada", "miniboveda", "mini bóveda", "cierre de sucursal");
        if (quiereCierreForzadoNota || foco.Any(m => m.Id == "cierre-forzado-nota"))
        {
            sb.AppendLine("## REGLAS SC-416 — NOTA EN CIERRE FORZADO");
            sb.AppendLine("Dominio: Procesos de Sucursal → Cierre de Sucursal → Forzar cierre. NO es Cuadre y cierre ni Fallas.");
            sb.AppendLine("Criterio: Compensada (tipo 10) SIN sección Nota; Normal y MiniBóveda CON Nota.");
            sb.AppendLine("Usá steps «… caja Compensada/Normal/MiniBoveda configurada …» (correlativos en Config SC-416).");
            sb.AppendLine("Abrí el diálogo Forzar cierre, assert Nota sí/no, y CANCELÁ (no confirmes el cierre forzado).");
            sb.AppendLine("Suite estable: Features/SC-416_CA-0N_*.feature con tags @SC-416 @CierreForzadoNota (sin @Prueba).");
            sb.AppendLine();
        }

        // Bloque DB: siempre si el hint lo pide, o si algún módulo foco es cheques/datos
        var quiereDb = ContieneAlguno(hint,
            "cobis", "sybase", "sql", "query", "consulta", "cuenta", "ahorro", "corriente",
            "uw_cashier", "fecha de proceso", "base de datos", "select ", "interdeposito", "cheque");
        var focoDb = foco.Any(m => m.Id is "cheques" or "datos-cobis-sot") || quiereDb;
        if (focoDb || mapa.ConsultasDb.Count > 0)
        {
            sb.AppendLine("## BASES DE DATOS (solo lectura en la suite)");
            sb.AppendLine("COBIS = Sybase ASE (CobisClient). SQL SOT = SQL Server UW_CASHIER (SqlSotClient).");
            sb.AppendLine("OPCION en Detalle: el operador puede pedir verificar conexion y/o consultas SELECT a COBIS y/o SQL SOT.");
            sb.AppendLine("Steps tipicos: When se verifica la conexion a COBIS/SQL SOT; When ejecuta en ... la consulta SELECT ...; Then ... devolvio filas.");
            sb.AppendLine("Reglas: SOLO SELECT; resultados van al informe/ZIP vía ConsultaDbAuditoria; preferí steps Given/When de datos existentes.");
            sb.AppendLine("Tablas COBIS habituales: cobis..ba_fecha_proceso; cob_ahorros..ah_cuenta (ah_cta_banco, ah_oficina, ah_estado, ah_moneda, ah_deposito_chq); cob_cuentas..cc_ctacte.");
            sb.AppendLine("SQL SOT: conexión SqlSot (servidor/base desde Config). Cliente genérico EjecutarConsultaAsync solo acepta SELECT.");
            sb.AppendLine();
            var consultas = mapa.ConsultasDb
                .Where(c => !focoDb || quiereDb || ids.Contains(c.Modulo) || c.Modulo is "cheques" or "datos-cobis-sot" or "general")
                .Take(24)
                .ToList();
            if (consultas.Count == 0)
                consultas = mapa.ConsultasDb.Take(16).ToList();
            foreach (var c in consultas)
            {
                sb.AppendLine($"### [{c.Motor}] {c.Nombre}");
                if (!string.IsNullOrWhiteSpace(c.Proposito))
                    sb.AppendLine("Propósito: " + c.Proposito);
                if (!string.IsNullOrWhiteSpace(c.Tablas))
                    sb.AppendLine("Tablas: " + c.Tablas);
                sb.AppendLine("SQL (referencia):");
                sb.AppendLine(Truncar(c.Sql.Replace("\r\n", "\n"), 500));
                sb.AppendLine();
            }

            var stepsDb = mapa.Steps
                .Where(s => s.Texto.Contains("Cobis", StringComparison.OrdinalIgnoreCase)
                            || s.Texto.Contains("COBIS", StringComparison.OrdinalIgnoreCase)
                            || s.Texto.Contains("consulta", StringComparison.OrdinalIgnoreCase))
                .Select(s => $"{s.Tipo} {s.Texto}")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(20);
            if (stepsDb.Any())
            {
                sb.AppendLine("Steps Given/Then de datos (preferí estos en el .feature):");
                foreach (var s in stepsDb)
                    sb.AppendLine("  - " + s);
                sb.AppendLine();
            }
        }

        // Resumen global compacto si hay espacio
        sb.AppendLine("## Índice módulos disponibles");
        foreach (var m in mapa.Modulos)
            sb.AppendLine($"- {m.Id}: {m.Nombre} ({m.Circuito.Count} pasos circuito, {m.StepsClave.Count} steps)");

        var texto = sb.ToString();
        if (texto.Length <= maxChars) return texto;
        return texto[..maxChars] + "\n…[mapa truncado]";
    }

    /// <summary>
    /// A partir de un detalle en lenguaje natural (o módulo elegido), devuelve el circuito del mapa.
    /// </summary>
    public static (string? ModuloId, string? Nombre, List<string> Circuito) ResolverCircuitoPorIntencion(
        string automatizacionRoot,
        string pruebasFeatures,
        string? intencion,
        string? moduloHint = null)
    {
        var mapa = AsegurarMapa(automatizacionRoot, pruebasFeatures);
        var hint = ((intencion ?? "") + "\n" + (moduloHint ?? "")).ToLowerInvariant();

        if (!string.IsNullOrWhiteSpace(moduloHint))
        {
            var porNombre = mapa.Modulos.FirstOrDefault(m =>
                string.Equals(m.Id, moduloHint, StringComparison.OrdinalIgnoreCase)
                || string.Equals(m.Nombre, moduloHint, StringComparison.OrdinalIgnoreCase)
                || m.Nombre.Contains(moduloHint, StringComparison.OrdinalIgnoreCase)
                || moduloHint.Contains(m.Nombre, StringComparison.OrdinalIgnoreCase)
                || moduloHint.Contains("falla", StringComparison.OrdinalIgnoreCase) && m.Id == "fallas-caja"
                || (moduloHint.Contains("416", StringComparison.OrdinalIgnoreCase)
                    || moduloHint.Contains("cierre forzado", StringComparison.OrdinalIgnoreCase)
                    || moduloHint.Contains("nota", StringComparison.OrdinalIgnoreCase))
                   && m.Id == "cierre-forzado-nota");
            if (porNombre is { Circuito.Count: > 0 })
                return (porNombre.Id, porNombre.Nombre, porNombre.Circuito.ToList());
        }

        var ranked = mapa.Modulos
            .Select(m => new
            {
                M = m,
                Score = m.Keywords.Count(k => hint.Contains(k, StringComparison.OrdinalIgnoreCase))
                    + (hint.Contains(m.Id, StringComparison.OrdinalIgnoreCase) ? 4 : 0)
                    + (hint.Contains(m.Nombre, StringComparison.OrdinalIgnoreCase) ? 3 : 0)
            })
            .OrderByDescending(x => x.Score)
            .FirstOrDefault();

        if (ranked is null || ranked.Score <= 0 || ranked.M.Circuito.Count == 0)
            return (null, null, []);

        return (ranked.M.Id, ranked.M.Nombre, ranked.M.Circuito.ToList());
    }

    private static bool ContieneAlguno(string texto, params string[] claves) =>
        claves.Any(k => texto.Contains(k, StringComparison.OrdinalIgnoreCase));

    /// <summary>Siembra ejemplos Gherkin por módulo en aprendizaje.json (actualiza seeds _seed_*).</summary>
    public static int SembrarAprendizaje(string automatizacionRoot, string pruebasFeatures)
    {
        var mapa = AsegurarMapa(automatizacionRoot, pruebasFeatures, forzar: false);
        var n = 0;
        foreach (var m in mapa.Modulos)
        {
            if (m.Circuito.Count < 2) continue;
            var gherkin = ArmarFeatureSemilla(m);
            AprendizajeLlm.RegistrarEjemploAlGuardar(
                pruebasFeatures,
                $"_seed_{m.Id}.feature",
                gherkin,
                $"Seed {m.Nombre}",
                $"Circuito entrenado automáticamente para el módulo {m.Nombre}. Pantallas: {string.Join(", ", m.Pantallas.Take(6))}.",
                m.Nombre);
            n++;
        }

        // Corrige ejemplos guardados erróneos (p. ej. SC-470 armado como Intercaja).
        var fallas = mapa.Modulos.FirstOrDefault(m => m.Id == "fallas-caja");
        if (fallas is not null)
        {
            var gherkinSc470 = ArmarFeatureSemilla(fallas)
                .Replace("Característica: Seed Fallas de caja (sobrante / faltante)",
                    "Característica: SC-470 — TA- Fallas de Caja", StringComparison.Ordinal)
                .Replace("@SeedMapa", "@SeedMapa @SC-470", StringComparison.Ordinal);
            if (!gherkinSc470.Contains("Ticket: SC-470", StringComparison.OrdinalIgnoreCase))
            {
                gherkinSc470 = gherkinSc470.Replace(
                    "  # Resumen:",
                    "  # Ticket: SC-470\r\n  # Resumen:",
                    StringComparison.Ordinal);
            }
            AprendizajeLlm.RegistrarEjemploAlGuardar(
                pruebasFeatures,
                "SC-470_TA-_Fallas_de_Caja.feature",
                gherkinSc470,
                "SC-470 — TA- Fallas de Caja",
                "Error: Fallas de caja en cierre con sobrante/faltante; validar tira auditora contra documento e imágenes.",
                "TA- Fallas de Caja");
            n++;
        }

        // SC-416: seeds con Gherkin de suite (Compensada sin Nota / Normal y MiniBóveda con Nota).
        var cierreNota = mapa.Modulos.FirstOrDefault(m => m.Id == "cierre-forzado-nota");
        if (cierreNota is not null)
        {
            var seeds416 = new (string Archivo, string Titulo, string Detalle, string Gherkin)[]
            {
                (
                    "SC-416_CA-01_Compensada_sin_Nota_en_cierre_forzado.feature",
                    "SC-416 CA-01 Compensada sin Nota en cierre forzado",
                    "Criterio: Compensada (tipo 10) en Forzar cierre NO muestra Nota de billetaje. Cancelar diálogo. Correlativo CierreForzadoNota.",
                    ArmarFeatureSc416(
                        "@SC-416 @CierreForzadoNota @SC416_CA_01",
                        "SC-416 CA-01 Compensada sin Nota en cierre forzado",
                        "SC-416 CA-01 — Compensada no muestra Nota en confirmacion de cierre forzado",
                        "Compensada",
                        sinNota: true)
                ),
                (
                    "SC-416_CA-02_Normal_con_Nota_en_cierre_forzado.feature",
                    "SC-416 CA-02 Normal con Nota en cierre forzado",
                    "Criterio: caja Normal con efectivo SÍ muestra Nota de billetaje en Forzar cierre. Cancelar diálogo.",
                    ArmarFeatureSc416(
                        "@SC-416 @CierreForzadoNota @SC416_CA_02",
                        "SC-416 CA-02 Normal con Nota en cierre forzado",
                        "SC-416 CA-02 — Normal muestra Nota en confirmacion de cierre forzado",
                        "Normal",
                        sinNota: false)
                ),
                (
                    "SC-416_CA-03_MiniBoveda_con_Nota_en_cierre_forzado.feature",
                    "SC-416 CA-03 MiniBoveda con Nota en cierre forzado",
                    "Extensión: MiniBóveda maneja efectivo → Forzar cierre SÍ muestra Nota. Cancelar diálogo.",
                    ArmarFeatureSc416(
                        "@SC-416 @CierreForzadoNota @SC416_CA_03",
                        "SC-416 CA-03 MiniBoveda con Nota en cierre forzado",
                        "SC-416 CA-03 — MiniBoveda muestra Nota en confirmacion de cierre forzado",
                        "MiniBoveda",
                        sinNota: false)
                )
            };
            foreach (var s in seeds416)
            {
                AprendizajeLlm.RegistrarEjemploAlGuardar(
                    pruebasFeatures,
                    s.Archivo,
                    s.Gherkin,
                    s.Titulo,
                    s.Detalle,
                    "SC-416");
                n++;
            }
        }

        return n;
    }

    private static string ArmarFeatureSc416(
        string tags,
        string featureName,
        string scenarioName,
        string tipoCaja,
        bool sinNota)
    {
        var assert = sinNota
            ? "Entonces el dialogo de cierre forzado no muestra la seccion Nota de billetaje"
            : "Entonces el dialogo de cierre forzado muestra la seccion Nota de billetaje";
        var sb = new StringBuilder();
        sb.AppendLine(tags);
        sb.AppendLine("# language: es");
        sb.AppendLine($"Característica: {featureName}");
        sb.AppendLine("  # Ticket: SC-416");
        sb.AppendLine("  # Suite Features/ (sin @Prueba). Correlativos en Config SC-416.");
        sb.AppendLine();
        sb.AppendLine("  Antecedentes:");
        sb.AppendLine("    Dado el usuario abre la aplicacion SOT");
        sb.AppendLine("    Cuando ingresa el usuario configurado en el formulario de login");
        sb.AppendLine("    Cuando ingresa la contraseña y confirma el acceso al sistema");
        sb.AppendLine("    Entonces se muestra el popup para elegir sucursal");
        sb.AppendLine("    Cuando busca y selecciona la sucursal configurada en el listado");
        sb.AppendLine("    Cuando confirma la seleccion de sucursal");
        sb.AppendLine("    Entonces el dialogo de sucursal se cierra");
        sb.AppendLine("    Entonces se muestra el cartel de bienvenida en la pagina de inicio");
        sb.AppendLine();
        sb.AppendLine($"  Escenario: {scenarioName}");
        sb.AppendLine("    Cuando navega a Cierre de Sucursal desde Procesos de Sucursal");
        sb.AppendLine($"    Cuando en Cierre de Sucursal localiza la caja {tipoCaja} configurada abierta en Pesos");
        sb.AppendLine("    Cuando en Cierre de Sucursal abre el dialogo de Forzar cierre de esa caja");
        sb.AppendLine("    " + assert);
        sb.AppendLine("    Cuando cancela el dialogo de cierre forzado");
        return sb.ToString();
    }

    private static string ArmarFeatureSemilla(ModuloMapa m)
    {
        var sb = new StringBuilder();
        sb.AppendLine("@Prueba @PruebaRun_Borrador @SeedMapa");
        sb.AppendLine("# language: es");
        sb.AppendLine($"Característica: Seed {m.Nombre}");
        sb.AppendLine($"  # Resumen: circuito de referencia del módulo {m.Nombre}");
        sb.AppendLine();
        sb.AppendLine("  Antecedentes:");
        sb.AppendLine("    Dado el usuario abre la aplicacion SOT");
        sb.AppendLine("    Cuando ingresa el usuario configurado en el formulario de login");
        sb.AppendLine("    Cuando ingresa la contraseña y confirma el acceso al sistema");
        sb.AppendLine("    Entonces se muestra el popup para elegir sucursal");
        sb.AppendLine("    Cuando busca y selecciona la sucursal configurada en el listado");
        sb.AppendLine("    Cuando confirma la seleccion de sucursal");
        sb.AppendLine("    Entonces el dialogo de sucursal se cierra");
        sb.AppendLine("    Entonces se muestra el cartel de bienvenida en la pagina de inicio");
        sb.AppendLine();
        sb.AppendLine($"  Escenario: Circuito {m.Nombre}");
        foreach (var paso in m.Circuito)
            sb.AppendLine("    " + paso);
        return sb.ToString();
    }

    private static MapaDoc Construir(string automatizacionRoot)
    {
        var doc = new MapaDoc { GeneradoUtc = DateTimeOffset.UtcNow };
        doc.Modulos = ModulosBase();

        IndexarAppsettings(automatizacionRoot, doc);
        IndexarSettingsCs(automatizacionRoot, doc);
        IndexarSteps(automatizacionRoot, doc);
        IndexarAngularUi(automatizacionRoot, doc);
        IndexarDatosDb(automatizacionRoot, doc);
        EnriquecerModulosConSteps(doc);

        if (doc.Selectores.Count == 0)
            doc.Avisos.Add("No se indexaron XPaths de appsettings (revisá AutomatizacionSOT/appsettings.json).");
        if (doc.PistasUi.Count == 0)
            doc.Avisos.Add("Sin pistas UI de app-cashier (¿ruta en repos-celula.json?).");
        if (doc.ConsultasDb.Count == 0)
            doc.Avisos.Add("Sin consultas DB indexadas (Data/CobisClient.cs / SqlSotClient.cs).");
        else
            doc.Avisos.Add($"Mapa OK: {doc.Modulos.Count} módulos, {doc.Selectores.Count} selectores, {doc.Steps.Count} steps, {doc.PistasUi.Count} pistas UI, {doc.ConsultasDb.Count} consultas DB.");

        return doc;
    }

    private static List<ModuloMapa> ModulosBase() =>
    [
        new()
        {
            Id = "login-inicio",
            Nombre = "Login e inicio",
            Keywords = ["login", "bienvenida", "sucursal", "sesion", "topbar"],
            Circuito =
            [
                "Dado el usuario abre la aplicacion SOT",
                "Cuando ingresa el usuario configurado en el formulario de login",
                "Cuando ingresa la contraseña y confirma el acceso al sistema",
                "Entonces se muestra el popup para elegir sucursal",
                "Cuando busca y selecciona la sucursal configurada en el listado",
                "Cuando confirma la seleccion de sucursal",
                "Entonces se muestra el cartel de bienvenida en la pagina de inicio"
            ],
            Pantallas = ["welcome", "branch-selector-dialog", "home-page", "topbar"]
        },
        new()
        {
            Id = "alta-caja",
            Nombre = "Alta de caja",
            Keywords = ["alta de caja", "alta caja", "equivalencia", "coe", "parametria cajas", "asignacion"],
            Circuito =
            [
                "Cuando navega al alta de caja",
                "Cuando completa el alta y asignacion de caja con equivalencia COE",
                "Cuando abre la caja en pesos desde Apertura",
                "Entonces la caja queda abierta y lista para operar"
            ],
            Pantallas = ["cashdrawer-form", "cashdrawers-allocation", "apertura"]
        },
        new()
        {
            Id = "intercaja",
            Nombre = "Intercaja",
            Keywords = ["intercaja", "pase intercaja", "interpase", "pases intercaja"],
            Circuito =
            [
                "Cuando navega a Pases Intercaja desde Acciones de Caja",
                "Cuando prepara el envio de un pase intercaja con billetaje",
                "Cuando confirma el envio del pase intercaja",
                "Cuando acepta el pase intercaja en la caja destino",
                "Entonces el pase intercaja queda aceptado en destino"
            ],
            Pantallas = ["pases-intercaja", "acciones-de-caja"]
        },
        new()
        {
            Id = "caja-boveda",
            Nombre = "Pases Caja-Bóveda",
            Keywords = ["caja-boveda", "caja bóveda", "boveda", "pase a boveda"],
            Circuito =
            [
                "Cuando navega a Pases Caja-Boveda desde Acciones de Caja",
                "Cuando prepara un pase hacia boveda con el importe indicado",
                "Cuando confirma el pase caja-boveda",
                "Entonces el pase a boveda queda registrado correctamente"
            ],
            Pantallas = ["pases-caja-boveda"]
        },
        new()
        {
            Id = "otros-ingresos",
            Nombre = "Otros Ingresos",
            Keywords = ["otros ingresos", "ingreso de dinero", "importe"],
            Circuito =
            [
                "Cuando navega a Otros Ingresos desde Acciones de Caja",
                "Cuando selecciona en Otros Ingresos una caja Normal en Pesos o USD",
                "Cuando crea un nuevo ingreso de dinero",
                "Cuando selecciona la causa ideal para otros ingresos",
                "Cuando ingresa un importe aleatorio para otros ingresos",
                "Cuando procesa el ingreso de dinero en la caja seleccionada",
                "Entonces el ingreso de dinero queda procesado en la caja seleccionada"
            ],
            Pantallas = ["otros-ingresos"]
        },
        new()
        {
            Id = "cierre-cuadre",
            Nombre = "Cierre y cuadre",
            Keywords =
            [
                "cierre", "cuadre", "cuadre y cierre", "caja cuadrada", "caja descuadrada",
                "billetaje cierre", "confirmar cierre"
            ],
            Circuito =
            [
                "Cuando navega a Cuadre y cierre desde Acciones de Caja",
                "Cuando selecciona en Cuadre y cierre una caja abierta en Pesos o USD",
                "Cuando inicia el cierre de caja",
                "Cuando en cierre carga billetaje igual al saldo actual",
                "Cuando avanza a la confirmacion de cierre",
                "Entonces la caja figura como Caja cuadrada",
                "Cuando confirma el cierre de caja",
                "Entonces el cierre de caja queda confirmado"
            ],
            Pantallas = ["cuadre-cierre", "cierre", "cuadre", "billetaje"]
        },
        new()
        {
            Id = "cierre-forzado-nota",
            Nombre = "Cierre forzado Nota (SC-416)",
            Keywords =
            [
                "sc-416", "cierre forzado", "forzar cierre", "nota de billetaje", "nota billetaje",
                "compensada", "miniboveda", "mini bóveda", "cierre de sucursal",
                "procesos de sucursal", "cierreforzadonota", "printbanknotes"
            ],
            Circuito =
            [
                "Cuando navega a Cierre de Sucursal desde Procesos de Sucursal",
                "Cuando en Cierre de Sucursal localiza la caja Compensada configurada abierta en Pesos",
                "Cuando en Cierre de Sucursal abre el dialogo de Forzar cierre de esa caja",
                "Entonces el dialogo de cierre forzado no muestra la seccion Nota de billetaje",
                "Cuando cancela el dialogo de cierre forzado",
                "Cuando en Cierre de Sucursal localiza la caja Normal configurada abierta en Pesos",
                "Cuando en Cierre de Sucursal abre el dialogo de Forzar cierre de esa caja",
                "Entonces el dialogo de cierre forzado muestra la seccion Nota de billetaje",
                "Cuando cancela el dialogo de cierre forzado",
                "Cuando en Cierre de Sucursal localiza la caja MiniBoveda configurada abierta en Pesos",
                "Cuando en Cierre de Sucursal abre el dialogo de Forzar cierre de esa caja",
                "Entonces el dialogo de cierre forzado muestra la seccion Nota de billetaje",
                "Cuando cancela el dialogo de cierre forzado"
            ],
            Pantallas =
            [
                "cierre-sucursal", "procesos-sucursal", "forzar-cierre", "nota-billetaje"
            ]
        },
        new()
        {
            Id = "fallas-caja",
            Nombre = "Fallas de caja (sobrante / faltante)",
            Keywords =
            [
                "falla de caja", "fallas de caja", "ta- fallas", "ta fallas", "sc-470",
                "sobrante", "faltante", "caja descuadrada", "descuadre",
                "tira auditora", "auditoria de caja", "ajuste de cierre",
                "autorizacion falla", "supervision falla", "newincome", "preclose",
                "transacciones monetarias", "montos", "importe falla"
            ],
            Circuito =
            [
                "Cuando navega a Cuadre y cierre desde Acciones de Caja",
                "Cuando selecciona en Cuadre y cierre una caja abierta en Pesos o USD",
                "Cuando asegura saldo minimo para cierre desde Cuadre si la caja esta en cero",
                "Cuando inicia el cierre de caja",
                "Cuando en cierre carga billetaje distinto al saldo para provocar sobrante o faltante",
                "Cuando avanza a la confirmacion de cierre",
                "Entonces la caja figura como Caja descuadrada por sobrante o faltante",
                "Cuando registra la falla de caja con supervision local",
                "Cuando confirma el cierre de caja",
                "Entonces el cierre de caja queda confirmado",
                "Cuando ingresa a Transacciones Monetarias desde Acciones de Caja",
                "Cuando selecciona en Transacciones Monetarias la caja del cierre",
                "Cuando busca en Transacciones Monetarias la falla de caja del dia",
                "Entonces en Transacciones Monetarias los montos de la falla coinciden con el documento e imagenes",
                "Cuando navega a Reportes Tira auditora",
                "Entonces en tira auditora se observa el evento de falla con los montos del documento e imagenes"
            ],
            Pantallas =
            [
                "cuadre-cierre", "cierre", "caja-descuadrada", "ajuste-falla",
                "supervision-dialog", "gestion-transacciones", "transacciones-monetarias",
                "tira-auditora", "new-adjustment-form"
            ]
        },
        new()
        {
            Id = "cheques",
            Nombre = "Cheques",
            Keywords = ["cheque", "deposito", "interdeposito", "cobis", "cuenta ahorro", "cuenta corriente"],
            Circuito =
            [
                "Dado consulta en COBIS una cuenta de ahorro de otra sucursal para interdeposito",
                "Cuando navega al deposito o interdeposito de cheques",
                "Cuando completa los datos del cheque segun el caso",
                "Cuando ingresa la cuenta COBIS del escenario en el campo Cta",
                "Cuando confirma la operacion de cheques",
                "Entonces el resultado del cheque coincide con lo esperado"
            ],
            Pantallas = ["deposito-cheques", "interdeposito"]
        },
        new()
        {
            Id = "parametria-sc161",
            Nombre = "Parametría SC-161",
            Keywords = ["parametria", "sc-161", "perfil contable", "relacion transaccion"],
            Circuito =
            [
                "Cuando navega a Parametria Relacion Transaccion y Perfil Contable",
                "Cuando opera la bandeja segun el caso SC-161",
                "Entonces el resultado en pantalla coincide con el caso"
            ],
            Pantallas = ["relacion-transaccion", "perfil-contable", "parametria"]
        },
        new()
        {
            Id = "datos-cobis-sot",
            Nombre = "Datos COBIS / SQL SOT",
            Keywords =
            [
                "cobis", "sybase", "sql sot", "sqlsot", "uw_cashier", "consulta", "query", "select",
                "fecha de proceso", "cuenta", "ahorro", "corriente", "base de datos",
                "conexion", "conexión", "verificar conexion", "probar conexion", "datos cobis"
            ],
            Circuito =
            [
                "Cuando se verifica la conexion a COBIS configurada",
                "Entonces la conexion a COBIS responde correctamente",
                "Entonces COBIS devuelve la fecha de proceso",
                "Cuando se verifica la conexion a SQL SOT configurada",
                "Entonces la conexion a SQL SOT responde correctamente",
                "Cuando ejecuta en SQL SOT la consulta de prueba",
                "Entonces la consulta a SQL SOT devolvio filas"
            ],
            Pantallas = ["cobis-sybase", "sql-sot-uw-cashier"]
        }
    ];

    private static void IndexarDatosDb(string root, MapaDoc doc)
    {
        // Catálogo semántico (siempre) — ayuda al LLM aunque falle el parseo de C#.
        doc.ConsultasDb.AddRange(
        [
            new ConsultaDbMapa
            {
                Motor = "COBIS",
                Nombre = "Fecha de proceso",
                Proposito = "Obtener la fecha operativa del banco (ba_fecha_proceso).",
                Sql = "SELECT fp_fecha FROM cobis..ba_fecha_proceso",
                Tablas = "cobis..ba_fecha_proceso",
                Origen = "CobisClient.ObtenerFechaProcesoAsync",
                Modulo = "datos-cobis-sot"
            },
            new ConsultaDbMapa
            {
                Motor = "COBIS",
                Nombre = "Cuenta ahorro por número",
                Proposito = "Resolver cuenta de ahorro (15 dígitos) por ah_cta_banco.",
                Sql = "SELECT TOP 1 ah_cta_banco, ah_nombre, ah_oficina, ah_estado FROM cob_ahorros..ah_cuenta WHERE ah_cta_banco = @cuenta",
                Tablas = "cob_ahorros..ah_cuenta",
                Origen = "CobisClient.ObtenerCuentaAhorroPorNumeroAsync",
                Modulo = "cheques"
            },
            new ConsultaDbMapa
            {
                Motor = "COBIS",
                Nombre = "Cuentas ahorro candidatas (filtro)",
                Proposito = "Listar cuentas AH por oficina/estado/moneda/depósito cheques (misma u otra sucursal, judicial, USD, bloqueada, etc.).",
                Sql = "SELECT TOP N ah_cta_banco, ah_nombre, ah_oficina, ah_estado FROM cob_ahorros..ah_cuenta WHERE {filtro} ORDER BY ah_oficina, ah_cta_banco",
                Tablas = "cob_ahorros..ah_cuenta",
                Origen = "CobisClient.ListarCuentasAhorroPorFiltroAsync",
                Modulo = "cheques"
            },
            new ConsultaDbMapa
            {
                Motor = "COBIS",
                Nombre = "Cuentas corriente candidatas (filtro)",
                Proposito = "Listar cuentas CC para depósito/interdepósito.",
                Sql = "SELECT TOP N cc_cta_banco, cc_nombre, cc_oficina, cc_estado FROM cob_cuentas..cc_ctacte WHERE {filtro} ORDER BY cc_oficina, cc_cta_banco",
                Tablas = "cob_cuentas..cc_ctacte",
                Origen = "CobisClient.ListarCuentasCorrientePorFiltroAsync",
                Modulo = "cheques"
            },
            new ConsultaDbMapa
            {
                Motor = "SQL_SOT",
                Nombre = "Probar conexión SQL SOT",
                Proposito = "Validar servidor/base UW_CASHIER (o la configurada en SqlSot).",
                Sql = "SELECT DB_NAME() AS BaseDatos, @@SERVERNAME AS Servidor",
                Tablas = "(sistema)",
                Origen = "SqlSotClient.ProbarConexionAsync",
                Modulo = "datos-cobis-sot"
            },
            new ConsultaDbMapa
            {
                Motor = "SQL_SOT",
                Nombre = "Consulta SELECT genérica",
                Proposito = "SqlSotClient.EjecutarConsultaAsync solo admite SELECT; resultados auditados en el informe.",
                Sql = "SELECT ... FROM dbo.<tabla> WHERE ...  -- solo lectura",
                Tablas = "UW_CASHIER (configurable SqlSot:BaseDatos)",
                Origen = "SqlSotClient.EjecutarConsultaAsync",
                Modulo = "datos-cobis-sot"
            }
        ]);

        IndexarSqlDesdeArchivoCs(Path.Combine(root, "Data", "CobisClient.cs"), "COBIS", "cheques", doc);
        IndexarSqlDesdeArchivoCs(Path.Combine(root, "Data", "SqlSotClient.cs"), "SQL_SOT", "datos-cobis-sot", doc);

        foreach (var sqlFile in Directory.Exists(root)
                     ? Directory.GetFiles(root, "*.sql", SearchOption.AllDirectories).Take(40)
                     : [])
        {
            try
            {
                var sql = File.ReadAllText(sqlFile);
                if (!sql.Contains("SELECT", StringComparison.OrdinalIgnoreCase)) continue;
                doc.ConsultasDb.Add(new ConsultaDbMapa
                {
                    Motor = sql.Contains("cob_", StringComparison.OrdinalIgnoreCase) || sql.Contains("cobis", StringComparison.OrdinalIgnoreCase)
                        ? "COBIS" : "SQL_SOT",
                    Nombre = Path.GetFileNameWithoutExtension(sqlFile),
                    Proposito = "Script SQL del repo",
                    Sql = Truncar(sql.Trim(), 800),
                    Tablas = ExtraerTablasSql(sql),
                    Origen = Path.GetRelativePath(root, sqlFile),
                    Modulo = InferirModuloDeArchivo(sqlFile)
                });
            }
            catch { /* best-effort */ }
        }

        doc.ConsultasDb = doc.ConsultasDb
            .GroupBy(c => c.Motor + "|" + c.Nombre + "|" + Truncar(c.Sql, 80), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Take(80)
            .ToList();
    }

    private static void IndexarSqlDesdeArchivoCs(string path, string motor, string moduloDefault, MapaDoc doc)
    {
        if (!File.Exists(path)) return;
        string texto;
        try { texto = File.ReadAllText(path); }
        catch { return; }

        // Métodos con const string sql = "..." / """...""" (con o sin summary)
        foreach (Match m in Regex.Matches(texto,
                     @"(?:public\s+static\s+async\s+Task[^\n]*\s+(?<met>\w+)\s*\([^)]*\)[^{]*\{[\s\S]{0,400}?const\s+string\s+sql\s*=\s*(?:""{3}(?<sql>[\s\S]*?)""{3}|""(?<sql2>[^""]+)""))",
                     RegexOptions.IgnoreCase))
        {
            var met = m.Groups["met"].Value;
            var sql = (m.Groups["sql"].Success ? m.Groups["sql"].Value : m.Groups["sql2"].Value).Trim();
            if (string.IsNullOrWhiteSpace(met) || string.IsNullOrWhiteSpace(sql)) continue;
            if (doc.ConsultasDb.Any(c =>
                    string.Equals(c.Origen, Path.GetFileName(path) + "." + met, StringComparison.OrdinalIgnoreCase)))
                continue;
            doc.ConsultasDb.Add(new ConsultaDbMapa
            {
                Motor = motor,
                Nombre = met,
                Proposito = "Consulta indexada desde " + Path.GetFileName(path),
                Sql = Truncar(Regex.Replace(sql, @"\s+", " ").Trim(), 700),
                Tablas = ExtraerTablasSql(sql),
                Origen = Path.GetFileName(path) + "." + met,
                Modulo = moduloDefault
            });
        }

        // Métodos públicos con summary
        foreach (Match m in Regex.Matches(texto,
                     @"///\s*<summary>\s*(?<sum>[^<]+?)\s*</summary>\s*(?:public\s+static\s+async\s+Task[^\n]*\s+(?<met>\w+)\s*\()",
                     RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            var met = m.Groups["met"].Value;
            var sum = Regex.Replace(m.Groups["sum"].Value, @"\s+", " ").Trim();
            if (string.IsNullOrWhiteSpace(met)) continue;
            // Buscar SQL cercano al método
            var idx = texto.IndexOf(met + "(", m.Index, StringComparison.Ordinal);
            if (idx < 0) continue;
            var slice = texto.Substring(idx, Math.Min(1800, texto.Length - idx));
            var sqlMatch = Regex.Match(slice,
                @"(?:const\s+string\s+sql\s*=\s*)?""{3}(?<sql>[\s\S]*?)""{3}|const\s+string\s+sql\s*=\s*""(?<sql2>[^""]+)""",
                RegexOptions.IgnoreCase);
            var sql = sqlMatch.Success
                ? (sqlMatch.Groups["sql"].Success ? sqlMatch.Groups["sql"].Value : sqlMatch.Groups["sql2"].Value).Trim()
                : "";
            if (string.IsNullOrWhiteSpace(sql) && !sum.Contains("SELECT", StringComparison.OrdinalIgnoreCase))
            {
                // método sin SQL literal (usa filtros) — igual documentar
                doc.ConsultasDb.Add(new ConsultaDbMapa
                {
                    Motor = motor,
                    Nombre = met,
                    Proposito = sum,
                    Sql = "(arma SELECT dinámico; ver CobisClient)",
                    Tablas = ExtraerTablasSql(slice),
                    Origen = Path.GetFileName(path) + "." + met,
                    Modulo = moduloDefault
                });
                continue;
            }
            if (string.IsNullOrWhiteSpace(sql)) continue;
            doc.ConsultasDb.Add(new ConsultaDbMapa
            {
                Motor = motor,
                Nombre = met,
                Proposito = sum,
                Sql = Truncar(Regex.Replace(sql, @"\s+", " ").Trim(), 700),
                Tablas = ExtraerTablasSql(sql),
                Origen = Path.GetFileName(path) + "." + met,
                Modulo = moduloDefault
            });
        }
    }

    private static string ExtraerTablasSql(string sql)
    {
        var tablas = new List<string>();
        foreach (Match m in Regex.Matches(sql ?? "",
                     @"\bFROM\s+([a-zA-Z0-9_\.\[\]]+)",
                     RegexOptions.IgnoreCase))
        {
            tablas.Add(m.Groups[1].Value.Trim());
        }
        foreach (Match m in Regex.Matches(sql ?? "",
                     @"\bJOIN\s+([a-zA-Z0-9_\.\[\]]+)",
                     RegexOptions.IgnoreCase))
        {
            tablas.Add(m.Groups[1].Value.Trim());
        }
        return string.Join(", ", tablas.Distinct(StringComparer.OrdinalIgnoreCase).Take(8));
    }

    private static void IndexarAppsettings(string root, MapaDoc doc)
    {
        var path = Path.Combine(root, "appsettings.json");
        if (!File.Exists(path)) return;
        try
        {
            using var jdoc = JsonDocument.Parse(File.ReadAllText(path));
            RecorrerJsonSelectores(jdoc.RootElement, "", "appsettings.json", doc);
        }
        catch (Exception ex)
        {
            doc.Avisos.Add("appsettings.json: " + ex.Message);
        }

        foreach (var overlay in Directory.GetFiles(root, "appsettings.*.json"))
        {
            var name = Path.GetFileName(overlay);
            if (name.Contains("secret", StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                using var jdoc = JsonDocument.Parse(File.ReadAllText(overlay));
                RecorrerJsonSelectores(jdoc.RootElement, "", name, doc);
            }
            catch { /* best-effort */ }
        }
    }

    private static void RecorrerJsonSelectores(JsonElement el, string path, string origen, MapaDoc doc)
    {
        if (el.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in el.EnumerateObject())
            {
                if (p.Name.StartsWith('_')) continue;
                RecorrerJsonSelectores(p.Value, string.IsNullOrEmpty(path) ? p.Name : path + "." + p.Name, origen, doc);
            }
            return;
        }
        if (el.ValueKind != JsonValueKind.String) return;
        var val = el.GetString() ?? "";
        if (!PareceSelector(val)) return;
        doc.Selectores.Add(new SelectorMapa
        {
            Modulo = InferirModuloDeRuta(path),
            Clave = path,
            Valor = val.Trim(),
            Origen = origen
        });
    }

    private static bool PareceSelector(string v)
    {
        if (string.IsNullOrWhiteSpace(v) || v.Length < 4 || v.Length > 500) return false;
        if (v.Contains("***") || v.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return false;
        return v.Contains("//") || v.Contains("xpath=", StringComparison.OrdinalIgnoreCase)
            || v.Contains("mat-") || v.Contains("data-testid") || v.Contains("#")
            || v.Contains("app-") || Regex.IsMatch(v, @"^//");
    }

    private static void IndexarSettingsCs(string root, MapaDoc doc)
    {
        var dir = Path.Combine(root, "Config");
        if (!Directory.Exists(dir)) return;
        foreach (var f in Directory.GetFiles(dir, "*Settings.cs"))
        {
            var texto = File.ReadAllText(f);
            var mod = InferirModuloDeArchivo(Path.GetFileName(f));
            foreach (Match m in Regex.Matches(texto,
                         @"(?m)(?:public\s+static\s+string\s+)?(?<k>XPath\w+|Id\w+|Css\w+)\s*(?:\{[^}]*get[^}]*\}|=)\s*[\$]?@?""(?<v>[^""]{4,})""",
                         RegexOptions.IgnoreCase))
            {
                var val = m.Groups["v"].Value;
                if (!PareceSelector(val) && !m.Groups["k"].Value.StartsWith("XPath", StringComparison.OrdinalIgnoreCase))
                    continue;
                doc.Selectores.Add(new SelectorMapa
                {
                    Modulo = mod,
                    Clave = m.Groups["k"].Value,
                    Valor = val,
                    Origen = Path.GetFileName(f)
                });
            }
            // appsettings binding strings in comments / defaults
            foreach (Match m in Regex.Matches(texto, @"@?""(?<v>//[^""]{6,})"""))
            {
                var val = m.Groups["v"].Value;
                if (!PareceSelector(val)) continue;
                doc.Selectores.Add(new SelectorMapa
                {
                    Modulo = mod,
                    Clave = "literal",
                    Valor = val,
                    Origen = Path.GetFileName(f)
                });
            }
        }
    }

    private static void IndexarSteps(string root, MapaDoc doc)
    {
        var dir = Path.Combine(root, "StepDefinitions");
        if (!Directory.Exists(dir)) return;
        foreach (var f in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
        {
            var texto = File.ReadAllText(f);
            var mod = InferirModuloDeArchivo(Path.GetFileName(f));
            foreach (Match m in Regex.Matches(texto,
                         @"\[(Given|When|Then)\(@""([^""]+)""\)\]",
                         RegexOptions.IgnoreCase))
            {
                doc.Steps.Add(new StepMapa
                {
                    Modulo = mod,
                    Tipo = m.Groups[1].Value,
                    Texto = m.Groups[2].Value,
                    Archivo = Path.GetFileName(f)
                });
            }
        }
    }

    private static void IndexarAngularUi(string root, MapaDoc doc)
    {
        var appCashier = ResolverRutaAppCashier(root);
        if (string.IsNullOrWhiteSpace(appCashier) || !Directory.Exists(appCashier))
        {
            doc.Avisos.Add("app-cashier no encontrado para indexar HTML.");
            return;
        }

        var htmlFiles = Directory.GetFiles(appCashier, "*.html", SearchOption.AllDirectories)
            .Where(f => f.Contains($"{Path.DirectorySeparatorChar}src{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                        || f.Contains("/src/", StringComparison.OrdinalIgnoreCase))
            .Where(f => !f.Contains("node_modules", StringComparison.OrdinalIgnoreCase))
            .Take(400)
            .ToList();

        foreach (var f in htmlFiles)
        {
            string html;
            try { html = File.ReadAllText(f); }
            catch { continue; }
            if (html.Length > 400_000) continue;

            var rel = f;
            try { rel = Path.GetRelativePath(appCashier, f); } catch { /* keep */ }
            var mod = InferirModuloDeArchivo(rel);
            var fileHint = Path.GetFileNameWithoutExtension(f).Replace(".component", "", StringComparison.OrdinalIgnoreCase);

            // data-testid
            foreach (Match m in Regex.Matches(html, @"data-testid\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase))
            {
                doc.PistasUi.Add(new PistaUi
                {
                    Modulo = mod,
                    Tipo = "data-testid",
                    Valor = m.Groups[1].Value,
                    Archivo = fileHint
                });
            }

            // ids útiles
            foreach (Match m in Regex.Matches(html, @"\bid\s*=\s*[""']([a-zA-Z][\w\-]{2,})[""']"))
            {
                var id = m.Groups[1].Value;
                if (id.StartsWith("mat-", StringComparison.OrdinalIgnoreCase) && id.Length < 8) continue;
                doc.PistasUi.Add(new PistaUi { Modulo = mod, Tipo = "id", Valor = id, Archivo = fileHint });
            }

            // textos de botones
            foreach (Match m in Regex.Matches(html,
                         @"<(?:button|a|span)[^>]*>(?:\s*<[^>]+>)*\s*([A-Za-zÁÉÍÓÚáéíóúñÑ][^<>{%]{1,40}?)\s*(?:</|\{)",
                         RegexOptions.IgnoreCase))
            {
                var t = Regex.Replace(m.Groups[1].Value, @"\s+", " ").Trim();
                if (t.Length < 2 || t.Length > 40) continue;
                if (t.Contains("{{") || t.StartsWith("*")) continue;
                doc.PistasUi.Add(new PistaUi { Modulo = mod, Tipo = "boton-texto", Valor = t, Archivo = fileHint });
            }

            // mat-icon names
            foreach (Match m in Regex.Matches(html, @"<mat-icon[^>]*>\s*([a-z0-9_]+)\s*</mat-icon>", RegexOptions.IgnoreCase))
            {
                doc.PistasUi.Add(new PistaUi
                {
                    Modulo = mod,
                    Tipo = "mat-icon",
                    Valor = m.Groups[1].Value,
                    Archivo = fileHint
                });
            }

            // routerLink
            foreach (Match m in Regex.Matches(html, @"routerLink\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase))
            {
                doc.PistasUi.Add(new PistaUi
                {
                    Modulo = mod,
                    Tipo = "ruta",
                    Valor = m.Groups[1].Value,
                    Archivo = fileHint
                });
                var pant = doc.Modulos.FirstOrDefault(x => x.Id == mod);
                pant?.Pantallas.Add(m.Groups[1].Value.Trim('/'));
            }
        }

        // Deduplicar pistas
        doc.PistasUi = doc.PistasUi
            .GroupBy(p => p.Tipo + "|" + p.Valor + "|" + p.Modulo, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Take(2500)
            .ToList();
    }

    private static void EnriquecerModulosConSteps(MapaDoc doc)
    {
        foreach (var m in doc.Modulos)
        {
            var steps = doc.Steps
                .Where(s => string.Equals(s.Modulo, m.Id, StringComparison.OrdinalIgnoreCase)
                            || m.Keywords.Any(k => s.Texto.Contains(k, StringComparison.OrdinalIgnoreCase)))
                .Select(s => $"{s.Tipo} {s.Texto}")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(24)
                .ToList();
            m.StepsClave = steps;
            m.Pantallas = m.Pantallas.Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToList();
        }
        doc.Selectores = doc.Selectores
            .GroupBy(s => s.Clave + "|" + s.Valor, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Take(2000)
            .ToList();
    }

    private static string ResolverRutaAppCashier(string automatizacionRoot)
    {
        var cfg = Path.Combine(automatizacionRoot, "Catalogo", "repos-celula.json");
        if (File.Exists(cfg))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(cfg));
                if (doc.RootElement.TryGetProperty("repos", out var repos))
                {
                    foreach (var r in repos.EnumerateArray())
                    {
                        var id = r.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                        if (!string.Equals(id, "app-cashier", StringComparison.OrdinalIgnoreCase)) continue;
                        var ruta = r.TryGetProperty("ruta", out var ru) ? ru.GetString() : null;
                        if (!string.IsNullOrWhiteSpace(ruta) && Directory.Exists(ruta))
                            return ruta!;
                    }
                }
            }
            catch { /* fallthrough */ }
        }

        var candidatos = new[]
        {
            @"C:\Repositorio\Accusys_SOT-app-cashier",
            Path.GetFullPath(Path.Combine(automatizacionRoot, "..", "..", "Accusys_SOT-app-cashier")),
            Path.GetFullPath(Path.Combine(automatizacionRoot, "..", "Accusys_SOT-app-cashier"))
        };
        return candidatos.FirstOrDefault(Directory.Exists) ?? "";
    }

    private static string InferirModuloDeRuta(string path)
    {
        var p = (path ?? "").ToLowerInvariant();
        if (p.Contains("intercaja") || p.Contains("paseintercaja")) return "intercaja";
        if (p.Contains("boveda") || p.Contains("bóveda")) return "caja-boveda";
        if (p.Contains("otroingreso") || p.Contains("otrosingresos") || p.Contains("otros_ingresos")) return "otros-ingresos";
        if (p.Contains("tira-auditora") || p.Contains("tira_auditora") || p.Contains("audit-trail")
            || p.Contains("newincome") || p.Contains("new-adjustment") || p.Contains("adjustment")
            || p.Contains("gestion-transacciones") || p.Contains("transacciones-monetarias"))
            return "fallas-caja";
        if (p.Contains("forzar") || p.Contains("cierre-forzado") || p.Contains("cierresucursal")
            || p.Contains("cierre-sucursal") || p.Contains("branch-close") || p.Contains("sc-416") || p.Contains("sc416"))
            return "cierre-forzado-nota";
        if (p.Contains("cierre") || p.Contains("cuadre")) return "cierre-cuadre";
        if (p.Contains("cheque") || p.Contains("deposito") || p.Contains("interdeposito")) return "cheques";
        if (p.Contains("sc161") || p.Contains("relacion") || p.Contains("parametria") || p.Contains("perfil")) return "parametria-sc161";
        if (p.Contains("caja") || p.Contains("alta") || p.Contains("apertura") || p.Contains("cashdrawer")) return "alta-caja";
        if (p.Contains("sucursal") || p.Contains("login") || p.Contains("inicio") || p.Contains("dialogo")) return "login-inicio";
        return "general";
    }

    private static string InferirModuloDeArchivo(string name)
    {
        var n = (name ?? "").ToLowerInvariant();
        if (n.Contains("cobis") || n.Contains("sqlsot") || n.Contains("sql-sot") || n.Contains("consulta")) return "datos-cobis-sot";
        if (n.Contains("falla") || n.Contains("tira-auditora") || n.Contains("tira_auditora") || n.Contains("sobrante") || n.Contains("faltante"))
            return "fallas-caja";
        if (n.Contains("sc-416") || n.Contains("sc416") || n.Contains("cierre-forzado") || n.Contains("forzado-nota")
            || n.Contains("cierreforsado") || (n.Contains("nota") && n.Contains("cierre")))
            return "cierre-forzado-nota";
        if (n.Contains("intercaja") || n.Contains("pase-inter")) return "intercaja";
        if (n.Contains("boveda") || n.Contains("caja-boveda")) return "caja-boveda";
        if (n.Contains("otrosingresos") || n.Contains("otros-ingresos") || n.Contains("ingreso")) return "otros-ingresos";
        if (n.Contains("cierre") || n.Contains("cuadre")) return "cierre-cuadre";
        if (n.Contains("cheque") || n.Contains("deposito") || n.Contains("interdeposito")) return "cheques";
        if (n.Contains("sc161") || n.Contains("relacion") || n.Contains("parametria") || n.Contains("perfil")) return "parametria-sc161";
        if (n.Contains("cajaalta") || n.Contains("caja-alta") || n.Contains("apertura") || n.Contains("cashdrawer") || n.Contains("alta")) return "alta-caja";
        if (n.Contains("login") || n.Contains("sucursal") || n.Contains("inicio") || n.Contains("home") || n.Contains("topbar") || n.Contains("welcome")) return "login-inicio";
        return InferirModuloDeRuta(n);
    }

    private static string Truncar(string s, int max) =>
        string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max] + "…";

    private static JsonSerializerOptions JsonOpts() => new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static JsonSerializerOptions JsonOptsIndented() => new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
