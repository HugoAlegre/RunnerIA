using System.Globalization;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using UglyToad.PdfPig;

/// <summary>
/// Asistente de pruebas: asiste a QA/automatización a armar borradores .feature
/// desde Jira, documentación o texto. No escribe fuera de Features/_pruebas.
/// </summary>
public static class AsistenteCasos
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(45) };

    private static readonly string BackgroundLogin = """
  Antecedentes:
    Dado el usuario abre la aplicacion SOT
    Cuando ingresa el usuario configurado en el formulario de login
    Cuando ingresa la contraseña y confirma el acceso al sistema
    Entonces se muestra el popup para elegir sucursal
    Cuando busca y selecciona la sucursal configurada en el listado
    Cuando confirma la seleccion de sucursal
    Entonces el dialogo de sucursal se cierra
    Entonces se muestra el cartel de bienvenida en la pagina de inicio
""";

    public static void MapEndpoints(
        WebApplication app,
        string automatizacionRoot,
        Dictionary<string, EscenarioDef> escenarios)
    {
        var pruebasFeatures = Path.Combine(automatizacionRoot, "Features", "_pruebas");
        Directory.CreateDirectory(pruebasFeatures);
        SyncEscenariosPruebas(pruebasFeatures, escenarios);

        app.MapGet("/pruebas/info", () => Results.Ok(new
        {
            generada = true,
            iaNombre = RunnerIaProyectos.NombreIaActiva(automatizacionRoot) ?? "RunnerIA",
            iaAlcance = "runner-producto",
            alcance =
                "IA limitada al Runner/producto: asiste borradores SpecFlow E2E. " +
                "Incluye reglas fijas (UTF-8/Background, SC-416 Nota, suite vs generadas, Excel QA, sucursal). " +
                "No genera código de producto ni ejecuta comandos libres.",
            salida = "Features/_pruebas/",
            reverso = "Eliminar vista/endpoints y carpeta Features/_pruebas + wwwroot/pruebas."
        }));

        app.MapGet("/pruebas/borradores", () => Results.Ok(ListarBorradores(pruebasFeatures)));

        app.MapGet("/pruebas/borradores/{*nombre}", (string nombre) =>
        {
            try
            {
                nombre = (nombre ?? "").TrimStart('/', '\\');
                nombre = SanitizarNombreFeatureRel(pruebasFeatures, nombre);

                var path = ResolverRutaFeature(pruebasFeatures, nombre);
                if (!File.Exists(path))
                    return Results.Json(new { ok = false, error = "Caso no encontrado." }, statusCode: 404);

                var catalogo = LeerCatalogoGrupos(pruebasFeatures);
                catalogo.Asignaciones.TryGetValue(nombre, out var grupoId);
                grupoId ??= GrupoSinClasificarId;
                var porId = GruposPorId(catalogo.Grupos);
                var item = DescribirBorrador(path, pruebasFeatures);
                return Results.Ok(new
                {
                    ok = true,
                    id = item.Id,
                    nombre = item.Nombre,
                    titulo = item.Titulo,
                    descripcion = item.Descripcion,
                    corto = item.Corto,
                    tag = item.Tag,
                    filterTag = item.FilterTag,
                    grupoId,
                    grupoNombre = ResolverNombreGrupo(porId, grupoId),
                    contenido = File.ReadAllText(path)
                });
            }
            catch (Exception ex)
            {
                return Results.Json(new { ok = false, error = ex.Message }, statusCode: 400);
            }
        });

        app.MapGet("/pruebas/grupos", () => Results.Ok(LeerCatalogoGrupos(pruebasFeatures).Grupos));

        app.MapGet("/pruebas/escenarios", () =>
        {
            SyncEscenariosPruebas(pruebasFeatures, escenarios);
            var catalogo = LeerCatalogoGrupos(pruebasFeatures);
            var casos = ListarBorradores(pruebasFeatures, catalogo);
            var regresionPruebas = ContarRegresionPruebas(pruebasFeatures, catalogo);
            return Results.Ok(new { grupos = catalogo.Grupos, casos, regresionPruebas });
        });

        app.MapDelete("/pruebas/borradores/{*nombre}", (string nombre) =>
        {
            try
            {
                nombre = (nombre ?? "").TrimStart('/', '\\');
                nombre = SanitizarNombreFeatureRel(pruebasFeatures, nombre);
                var path = ResolverRutaFeature(pruebasFeatures, nombre);
                if (!File.Exists(path))
                    return Results.Json(new { ok = false, error = "Borrador no encontrado." }, statusCode: 404);

                var catalogo = LeerCatalogoGrupos(pruebasFeatures);
                catalogo.Asignaciones.TryGetValue(nombre, out var grupoId);
                grupoId ??= GrupoSinClasificarId;
                var contenido = File.ReadAllText(path);
                var grupoNombre = ResolverNombreGrupo(
                    GruposPorId(catalogo.Grupos),
                    grupoId);

                GuardarUndo(pruebasFeatures, new UndoEntry
                {
                    Tipo = "eliminar-caso",
                    Descripcion = $"Eliminar caso «{nombre}» (el runner/grupo se mantiene)",
                    Nombre = nombre,
                    Contenido = contenido,
                    GrupoIdAnterior = grupoId,
                    GrupoNombreAnterior = grupoNombre
                });

                // Solo borra el caso. NO elimina el runner/grupo.
                EliminarArchivoFeature(pruebasFeatures, nombre, escenarios);

                return Results.Ok(new
                {
                    ok = true,
                    id = IdDesdeNombre(nombre),
                    nombre,
                    deshacer = true,
                    mensaje = "Caso eliminado. El runner/grupo se mantuvo. Podés deshacer el cambio."
                });
            }
            catch (Exception ex)
            {
                return Results.Json(new { ok = false, error = ex.Message }, statusCode: 400);
            }
        });

        app.MapGet("/pruebas/deshacer", () =>
        {
            var undo = LeerUndo(pruebasFeatures);
            if (undo is null)
                return Results.Ok(new { ok = true, disponible = false, descripcion = (string?)null });
            return Results.Ok(new { ok = true, disponible = true, descripcion = undo.Descripcion, tipo = undo.Tipo });
        });

        app.MapPost("/pruebas/deshacer", () =>
        {
            try
            {
                var undo = LeerUndo(pruebasFeatures);
                if (undo is null)
                    return Results.Json(new { ok = false, error = "No hay cambios para deshacer." }, statusCode: 400);

                if (string.Equals(undo.Tipo, "eliminar-caso", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(undo.Nombre) || string.IsNullOrWhiteSpace(undo.Contenido))
                        return Results.Json(new { ok = false, error = "No se pudo restaurar el caso (datos incompletos)." }, statusCode: 400);

                    var nombreUndo = SanitizarNombreFeatureRel(pruebasFeatures, undo.Nombre);
                    var dest = ResolverRutaFeature(pruebasFeatures, nombreUndo);
                    if (File.Exists(dest))
                        return Results.Json(new { ok = false, error = $"Ya existe {nombreUndo}. Renombralo o eliminalo antes de deshacer." }, statusCode: 400);

                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    File.WriteAllText(dest, undo.Contenido.TrimEnd() + Environment.NewLine, Encoding.UTF8);
                    AsignarAGrupo(pruebasFeatures, nombreUndo, undo.GrupoIdAnterior, null);
                    SyncEscenariosPruebas(pruebasFeatures, escenarios);
                    BorrarUndo(pruebasFeatures);
                    return Results.Ok(new
                    {
                        ok = true,
                        mensaje = $"Se restauró el caso «{undo.Nombre}» en «{undo.GrupoNombreAnterior ?? undo.GrupoIdAnterior}».",
                        id = IdDesdeNombre(undo.Nombre),
                        nombre = undo.Nombre,
                        grupoId = undo.GrupoIdAnterior
                    });
                }

                if (string.Equals(undo.Tipo, "mover-caso", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(undo.Nombre) || string.IsNullOrWhiteSpace(undo.GrupoIdAnterior))
                        return Results.Json(new { ok = false, error = "No se pudo deshacer el movimiento." }, statusCode: 400);
                    if (!File.Exists(Path.Combine(pruebasFeatures, Path.GetFileName(undo.Nombre))))
                        return Results.Json(new { ok = false, error = "El caso ya no existe; no se puede deshacer el movimiento." }, statusCode: 400);

                    var grupo = AsignarAGrupo(pruebasFeatures, undo.Nombre, undo.GrupoIdAnterior, null);
                    SyncEscenariosPruebas(pruebasFeatures, escenarios);
                    BorrarUndo(pruebasFeatures);
                    return Results.Ok(new
                    {
                        ok = true,
                        mensaje = $"Se revirtió el movimiento: «{undo.Nombre}» volvió a «{grupo.Nombre}».",
                        id = IdDesdeNombre(undo.Nombre),
                        nombre = undo.Nombre,
                        grupoId = grupo.Id
                    });
                }

                return Results.Json(new { ok = false, error = "Tipo de cambio no soportado para deshacer." }, statusCode: 400);
            }
            catch (Exception ex)
            {
                return Results.Json(new { ok = false, error = ex.Message }, statusCode: 400);
            }
        });

        app.MapPost("/pruebas/casos/mover", async (HttpRequest request) =>
        {
            try
            {
                using var doc = await JsonDocument.ParseAsync(request.Body);
                var root = doc.RootElement;
                var nombreRaw = root.TryGetProperty("nombre", out var n) ? n.GetString() ?? "" : "";
                var nombre = SanitizarNombreFeatureRel(pruebasFeatures, nombreRaw);
                var grupoId = root.TryGetProperty("grupoId", out var g) ? g.GetString()?.Trim() : null;

                if (!File.Exists(ResolverRutaFeature(pruebasFeatures, nombre)))
                    return Results.Json(new { ok = false, error = "Caso no encontrado." }, statusCode: 404);
                if (string.IsNullOrWhiteSpace(grupoId))
                    return Results.Json(new { ok = false, error = "Indicá el grupo destino." }, statusCode: 400);

                var catalogoPrev = LeerCatalogoGrupos(pruebasFeatures);
                catalogoPrev.Asignaciones.TryGetValue(nombre, out var grupoAnterior);
                grupoAnterior ??= GrupoSinClasificarId;
                var nombreAnterior = ResolverNombreGrupo(
                    GruposPorId(catalogoPrev.Grupos),
                    grupoAnterior);

                var grupo = AsignarAGrupo(pruebasFeatures, nombre, grupoId, null);
                SyncEscenariosPruebas(pruebasFeatures, escenarios);

                if (!string.Equals(grupoAnterior, grupo.Id, StringComparison.OrdinalIgnoreCase))
                {
                    GuardarUndo(pruebasFeatures, new UndoEntry
                    {
                        Tipo = "mover-caso",
                        Descripcion = $"Mover «{nombre}» de «{nombreAnterior}» a «{grupo.Nombre}»",
                        Nombre = nombre,
                        GrupoIdAnterior = grupoAnterior,
                        GrupoNombreAnterior = nombreAnterior,
                        GrupoIdNuevo = grupo.Id
                    });
                }

                return Results.Ok(new
                {
                    ok = true,
                    nombre,
                    id = IdDesdeNombre(nombre),
                    grupoId = grupo.Id,
                    grupoNombre = grupo.Nombre,
                    deshacer = true,
                    mensaje = $"Caso movido al grupo «{grupo.Nombre}». Podés deshacer el cambio."
                });
            }
            catch (Exception ex)
            {
                return Results.Json(new { ok = false, error = ex.Message }, statusCode: 400);
            }
        });

        app.MapPost("/pruebas/grupos", async (HttpRequest request) =>
        {
            try
            {
                using var doc = await JsonDocument.ParseAsync(request.Body);
                var root = doc.RootElement;
                var nombre = (root.TryGetProperty("nombre", out var n) ? n.GetString() : null)?.Trim();
                if (string.IsNullOrWhiteSpace(nombre))
                    return Results.Json(new { ok = false, error = "Indicá el nombre del módulo." }, statusCode: 400);

                var catalogo = LeerCatalogoGrupos(pruebasFeatures);
                var id = "grp-" + Regex.Replace(nombre.ToLowerInvariant(), @"[^\w]+", "-").Trim('-');
                if (string.IsNullOrWhiteSpace(id) || id == "grp-")
                    id = "grp-" + DateTime.Now.ToString("yyyyMMddHHmmss");

                var existente = catalogo.Grupos.FirstOrDefault(g =>
                    string.Equals(g.Id, id, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(g.Nombre, nombre, StringComparison.OrdinalIgnoreCase));
                if (existente is not null)
                {
                    return Results.Ok(new
                    {
                        ok = true,
                        id = existente.Id,
                        nombre = existente.Nombre,
                        yaExistia = true,
                        mensaje = $"El módulo «{existente.Nombre}» ya existía."
                    });
                }

                catalogo.Grupos.Add(new GrupoTesting(id, nombre, "Módulo de testing"));
                GuardarCatalogoGrupos(pruebasFeatures, catalogo);
                return Results.Ok(new
                {
                    ok = true,
                    id,
                    nombre,
                    yaExistia = false,
                    mensaje = $"Módulo «{nombre}» creado."
                });
            }
            catch (Exception ex)
            {
                return Results.Json(new { ok = false, error = ex.Message }, statusCode: 400);
            }
        });

        app.MapPut("/pruebas/grupos/{id}", async (string id, HttpRequest request) =>
        {
            try
            {
                id = (id ?? "").Trim();
                if (string.IsNullOrWhiteSpace(id))
                    return Results.Json(new { ok = false, error = "Id de módulo inválido." }, statusCode: 400);
                if (RunnersExistentes.ContainsKey(id))
                    return Results.Json(new { ok = false, error = "Los módulos fijos del producto no se pueden renombrar acá." }, statusCode: 400);

                using var doc = await JsonDocument.ParseAsync(request.Body);
                var nombre = (doc.RootElement.TryGetProperty("nombre", out var n) ? n.GetString() : null)?.Trim();
                if (string.IsNullOrWhiteSpace(nombre))
                    return Results.Json(new { ok = false, error = "Indicá el nuevo nombre del módulo." }, statusCode: 400);

                var catalogo = LeerCatalogoGrupos(pruebasFeatures, autoCrearSinClasificar: false);
                var idx = catalogo.Grupos.FindIndex(g => string.Equals(g.Id, id, StringComparison.OrdinalIgnoreCase));
                if (idx < 0)
                    return Results.Json(new { ok = false, error = "Módulo no encontrado." }, statusCode: 404);

                var anterior = catalogo.Grupos[idx];
                catalogo.Grupos[idx] = new GrupoTesting(anterior.Id, nombre, anterior.Sub);
                GuardarCatalogoGrupos(pruebasFeatures, catalogo);
                return Results.Ok(new
                {
                    ok = true,
                    id = anterior.Id,
                    nombre,
                    nombreAnterior = anterior.Nombre,
                    mensaje = $"Módulo renombrado: «{anterior.Nombre}» → «{nombre}»."
                });
            }
            catch (Exception ex)
            {
                return Results.Json(new { ok = false, error = ex.Message }, statusCode: 400);
            }
        });

        app.MapDelete("/pruebas/grupos/{id}", (string id, bool? eliminarCasos) =>
        {
            try
            {
                id = (id ?? "").Trim();
                if (string.IsNullOrWhiteSpace(id))
                    return Results.Json(new { ok = false, error = "Id de grupo inválido." }, statusCode: 400);

                var catalogo = LeerCatalogoGrupos(pruebasFeatures, autoCrearSinClasificar: false);
                var esSinClasificar = string.Equals(id, GrupoSinClasificarId, StringComparison.OrdinalIgnoreCase);
                var grupo = catalogo.Grupos.FirstOrDefault(g => string.Equals(g.Id, id, StringComparison.OrdinalIgnoreCase));

                // Si el usuario ya lo borró o solo existe en UI, igual marcamos omitir y limpiamos.
                if (grupo is null && !esSinClasificar)
                    return Results.Json(new { ok = false, error = "Grupo no encontrado." }, statusCode: 404);

                var casosDelGrupo = catalogo.Asignaciones
                    .Where(kv => string.Equals(kv.Value, id, StringComparison.OrdinalIgnoreCase))
                    .Select(kv => kv.Key)
                    .ToList();

                // Al eliminar el módulo, por defecto se borran sus casos generados (confirmado en UI).
                var borrarCasos = eliminarCasos != false;
                if (casosDelGrupo.Count > 0 && !borrarCasos)
                {
                    return Results.Json(new
                    {
                        ok = false,
                        error = $"El grupo tiene {casosDelGrupo.Count} caso(s). Movelos a otro grupo o confirmá eliminarlos con el grupo.",
                        casos = casosDelGrupo.Count
                    }, statusCode: 400);
                }

                var eliminados = new List<string>();
                if (borrarCasos)
                {
                    foreach (var nombre in casosDelGrupo)
                    {
                        EliminarArchivoFeature(pruebasFeatures, nombre, escenarios);
                        eliminados.Add(nombre);
                    }
                }

                catalogo = LeerCatalogoGrupos(pruebasFeatures, autoCrearSinClasificar: false);
                catalogo.Grupos.RemoveAll(g => string.Equals(g.Id, id, StringComparison.OrdinalIgnoreCase));
                foreach (var key in catalogo.Asignaciones.Where(kv => string.Equals(kv.Value, id, StringComparison.OrdinalIgnoreCase)).Select(kv => kv.Key).ToList())
                    catalogo.Asignaciones.Remove(key);
                if (esSinClasificar)
                    catalogo.OmitirSinClasificar = true;
                GuardarCatalogoGrupos(pruebasFeatures, catalogo);
                SyncEscenariosPruebas(pruebasFeatures, escenarios);

                var nombreGrupo = grupo?.Nombre ?? (esSinClasificar ? "Testing (sin clasificar)" : id);
                return Results.Ok(new
                {
                    ok = true,
                    id,
                    nombre = nombreGrupo,
                    casosEliminados = eliminados,
                    mensaje = eliminados.Count > 0
                        ? $"Grupo «{nombreGrupo}» y {eliminados.Count} caso(s) eliminados."
                        : $"Grupo «{nombreGrupo}» eliminado."
                });
            }
            catch (Exception ex)
            {
                return Results.Json(new { ok = false, error = ex.Message }, statusCode: 400);
            }
        });

        app.MapPost("/pruebas/jira/probar", async (HttpRequest request) =>
        {
            try
            {
                using var reader = new StreamReader(request.Body, Encoding.UTF8);
                var raw = await reader.ReadToEndAsync();
                using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw);
                var root = doc.RootElement;
                var jiraUrl = root.TryGetProperty("jiraUrl", out var ju) ? (ju.GetString() ?? "").Trim() : "";
                var jiraEmail = root.TryGetProperty("jiraEmail", out var je) ? (je.GetString() ?? "").Trim() : "";
                var jiraToken = root.TryGetProperty("jiraToken", out var jt) ? (jt.GetString() ?? "").Trim() : "";

                if (string.IsNullOrWhiteSpace(jiraUrl))
                {
                    return Results.Json(new
                    {
                        ok = false,
                        error = "Pegá el link del ticket Jira (ej. https://tu-dominio.atlassian.net/browse/SC-161)."
                    }, statusCode: 400);
                }

                RunnerSecurity.ValidarUrlJiraOThrow(jiraUrl, automatizacionRoot);
                var jira = await LeerJiraAsync(jiraUrl, jiraEmail, jiraToken);
                if (!jira.Ok)
                {
                    return Results.Json(new
                    {
                        ok = false,
                        key = jira.Key,
                        error = jira.Error
                    }, statusCode: 400);
                }

                return Results.Ok(new
                {
                    ok = true,
                    key = jira.Key,
                    titulo = jira.Titulo,
                    estado = jira.Estado,
                    aviso = jira.Aviso,
                    mensaje = string.IsNullOrWhiteSpace(jira.Titulo)
                        ? $"Conexión OK. Ticket {jira.Key} leído."
                        : $"Conexión OK. {jira.Key}: {jira.Titulo}"
                });
            }
            catch (Exception ex)
            {
                return Results.Json(new { ok = false, error = ex.Message }, statusCode: 400);
            }
        });

        app.MapPost("/pruebas/analizar", async (HttpRequest request) =>
        {
            try
            {
                var form = await request.ReadFormAsync();
                var jiraUrl = (form["jiraUrl"].ToString() ?? "").Trim();
                var jiraEmail = (form["jiraEmail"].ToString() ?? "").Trim();
                var jiraToken = (form["jiraToken"].ToString() ?? "").Trim();
                var textoManual = form["textoManual"].ToString() ?? "";
                var tituloSugerido = (form["tituloSugerido"].ToString() ?? "").Trim();
                var grupoIdHint = (form["grupoId"].ToString() ?? "").Trim();
                var grupoNombreHint = (form["grupoNombre"].ToString() ?? "").Trim();

                var fuentes = new List<FuenteTexto>();
                var avisos = new List<string>();

                // Si el operador eligió módulo en Generar (p. ej. TA- Fallas de Caja), sesgar el mapa/plantilla.
                if (!string.IsNullOrWhiteSpace(grupoIdHint) &&
                    !string.Equals(grupoIdHint, "__auto__", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(grupoIdHint, "__nuevo__", StringComparison.OrdinalIgnoreCase))
                {
                    fuentes.Add(new FuenteTexto(
                        "modulo_seleccionado",
                        $"Módulo elegido en Generar: id={grupoIdHint}; nombre={grupoNombreHint}. " +
                        "Usá el circuito de ese dominio (si es Fallas de Caja → cierre sobrante/faltante + tira auditora; NO Intercaja)."));
                }
                else if (!string.IsNullOrWhiteSpace(grupoNombreHint))
                {
                    fuentes.Add(new FuenteTexto(
                        "modulo_seleccionado",
                        $"Módulo elegido en Generar: {grupoNombreHint}. Preferí el circuito de ese dominio."));
                }

                var usarLlm = form.ContainsKey("usarLlm") &&
                    (string.Equals(form["usarLlm"].ToString(), "true", StringComparison.OrdinalIgnoreCase)
                     || form["usarLlm"].ToString() == "1"
                     || form["usarLlm"].ToString() == "on");
                var llmApiKey = (form["llmApiKey"].ToString() ?? "").Trim();
                var llmBaseUrl = (form["llmBaseUrl"].ToString() ?? "").Trim();
                var llmModel = (form["llmModel"].ToString() ?? "").Trim();
                // Capturas: visión LLM si hay key; si no, OCR local Windows (sin LLM).
                if (!string.IsNullOrWhiteSpace(textoManual))
                    fuentes.Add(new FuenteTexto("texto_manual", textoManual.Trim()));

                foreach (var file in form.Files)
                {
                    var extraido = await ExtraerTextoArchivoAsync(
                        file,
                        llmApiKey,
                        llmBaseUrl,
                        llmModel);
                    if (extraido.Ok)
                        fuentes.Add(new FuenteTexto("archivo:" + file.FileName, extraido.Texto));
                    else
                        avisos.Add(extraido.Error);
                }

                // Si hay PDF + DOCX/MD/etc. con la misma info, quedarse con una sola copia.
                fuentes = DeduplicarFuentesDocumentacion(fuentes, avisos);

                string? jiraModulo = null;
                string? jiraTitulo = null;
                string? jiraTipo = null;
                string? jiraDetalle = null;
                if (!string.IsNullOrWhiteSpace(jiraUrl))
                {
                    RunnerSecurity.ValidarUrlJiraOThrow(jiraUrl, automatizacionRoot);
                    var jira = await LeerJiraAsync(
                        jiraUrl, jiraEmail, jiraToken,
                        llmApiKey,
                        llmBaseUrl,
                        llmModel);
                    if (jira.Ok)
                    {
                        fuentes.Add(new FuenteTexto("jira:" + jira.Key, jira.Texto));
                        if (!string.IsNullOrWhiteSpace(jira.Aviso))
                            avisos.Add(jira.Aviso);
                        jiraModulo = jira.ModuloSugerido;
                        jiraTitulo = jira.Titulo;
                        jiraTipo = jira.Tipo;
                        jiraDetalle = jira.DetalleSugerido;
                    }
                    else
                    {
                        avisos.Add(jira.Error);
                    }
                }

                var ticketKey = ExtraerTicketKey(jiraUrl) ?? ExtraerTicketKey(textoManual + "\n" + tituloSugerido);
                var (ejemplosTexto, _, avisosMemoria) = AprendizajeLlm.ObtenerEjemplosParaAnalizar(
                    pruebasFeatures,
                    string.Join("\n", fuentes.Select(f => f.Texto)) + "\n" + tituloSugerido,
                    ticketKey,
                    tituloSugerido ?? jiraTitulo);
                avisos.AddRange(avisosMemoria);
                if (!string.IsNullOrWhiteSpace(ejemplosTexto))
                    fuentes.Add(new FuenteTexto("aprendizaje", ejemplosTexto));

                var catalogoCelula = CatalogoSotCelula.ObtenerTextoParaAnalizar(automatizacionRoot);
                if (!string.IsNullOrWhiteSpace(catalogoCelula))
                    fuentes.Add(new FuenteTexto("catalogo_celula_sot", catalogoCelula));
                else
                    avisos.Add("Sin catálogo de repos célula SOT. En Configuración podés sincronizar app/api/models.");

                // Mapa de circuitos / XPaths / UI Angular (entrenamiento RunnerIA)
                var hintMapa = string.Join("\n", fuentes.Select(f => f.Texto)) + "\n" + (tituloSugerido ?? "") + "\n" + (jiraTitulo ?? "");
                try
                {
                    MapaAutomatizacion.AsegurarMapa(automatizacionRoot, pruebasFeatures);
                    var sembrados = MapaAutomatizacion.SembrarAprendizaje(automatizacionRoot, pruebasFeatures);
                    if (sembrados > 0)
                        avisos.Add($"RunnerIA: mapa entrenado ({sembrados} circuitos semilla por módulo).");
                    var mapaTexto = MapaAutomatizacion.ObtenerTextoParaAnalizar(
                        automatizacionRoot, pruebasFeatures, hintMapa);
                    if (!string.IsNullOrWhiteSpace(mapaTexto))
                        fuentes.Add(new FuenteTexto("mapa_automatizacion", mapaTexto));
                }
                catch (Exception exMapa)
                {
                    avisos.Add("Mapa automatización no disponible: " + exMapa.Message);
                }

                var corpus = string.Join("\n\n---\n\n", fuentes.Select(f => $"[{f.Origen}]\n{f.Texto}"));
                var stepsCatalogo = IndexarSteps(automatizacionRoot);
                var analisis = await AnalizarAsync(
                    corpus, fuentes, tituloSugerido ?? "", jiraUrl, stepsCatalogo, avisos,
                    jiraModulo, jiraTitulo, jiraTipo, jiraDetalle,
                    ejemplosTexto, usarLlm, llmApiKey, llmBaseUrl, llmModel,
                    automatizacionRoot, pruebasFeatures,
                    grupoNombreHint, grupoIdHint);
                RunnerAudit.Log(automatizacionRoot, "analizar",
                    $"fuentes={fuentes.Count};llm={(usarLlm ? "1" : "0")};jira={(!string.IsNullOrWhiteSpace(jiraUrl) ? "1" : "0")}");
                return Results.Ok(analisis);
            }
            catch (Exception ex)
            {
                return Results.Json(new { ok = false, error = ex.Message }, statusCode: 400);
            }
        });

        app.MapGet("/pruebas/aprendizaje", () => Results.Ok(AprendizajeLlm.InfoMemoria(pruebasFeatures)));

        app.MapGet("/pruebas/mapa", () =>
            Results.Ok(MapaAutomatizacion.Info(automatizacionRoot, pruebasFeatures)));

        app.MapPost("/pruebas/mapa/reentrenar", () =>
        {
            try
            {
                var mapa = MapaAutomatizacion.AsegurarMapa(automatizacionRoot, pruebasFeatures, forzar: true);
                var seeds = MapaAutomatizacion.SembrarAprendizaje(automatizacionRoot, pruebasFeatures);
                RunnerAudit.Log(automatizacionRoot, "mapa-reentrenar",
                    $"modulos={mapa.Modulos.Count};selectores={mapa.Selectores.Count};steps={mapa.Steps.Count};ui={mapa.PistasUi.Count};db={mapa.ConsultasDb.Count};seeds={seeds}");
                return Results.Ok(new
                {
                    ok = true,
                    mensaje = $"Mapa reentrenado: {mapa.Modulos.Count} módulos, {mapa.Selectores.Count} selectores, {mapa.Steps.Count} steps, {mapa.PistasUi.Count} pistas UI, {mapa.ConsultasDb.Count} consultas DB, {seeds} semillas.",
                    info = MapaAutomatizacion.Info(automatizacionRoot, pruebasFeatures)
                });
            }
            catch (Exception ex)
            {
                return Results.Json(new { ok = false, error = ex.Message }, statusCode: 400);
            }
        });

        app.MapPost("/pruebas/guardar", async (HttpRequest request) =>
        {
            try
            {
                using var reader = new StreamReader(request.Body, Encoding.UTF8);
                var raw = await reader.ReadToEndAsync();
                var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw);
                var root = doc.RootElement;
                var nombre = root.TryGetProperty("nombre", out var n) ? n.GetString()?.Trim() : null;
                var contenido = root.TryGetProperty("contenido", out var c) ? c.GetString() : null;
                var forzarIncompleto = root.TryGetProperty("forzarIncompleto", out var f) && f.GetBoolean();
                var grupoId = root.TryGetProperty("grupoId", out var g) ? g.GetString()?.Trim() : null;
                var grupoNuevo = root.TryGetProperty("grupoNuevo", out var gn) ? gn.GetString()?.Trim() : null;
                var tituloEditable = root.TryGetProperty("titulo", out var tit) ? tit.GetString()?.Trim() : null;
                var resumenEditable = root.TryGetProperty("resumen", out var res) ? res.GetString()?.Trim() : null;

                if (string.IsNullOrWhiteSpace(contenido))
                    return Results.Json(new { ok = false, error = "Falta el contenido del feature." }, statusCode: 400);

                if (contenido.Contains("FALTA INFORMACIÓN", StringComparison.OrdinalIgnoreCase) && !forzarIncompleto)
                {
                    return Results.Json(new
                    {
                        ok = false,
                        error = "El borrador indica información faltante. Completá los datos o marcá 'Guardar igual como borrador incompleto'."
                    }, statusCode: 400);
                }

                nombre = SanitizarNombreFeature(nombre);
                var filterTag = FilterTagDesdeNombre(nombre);
                if (!string.IsNullOrWhiteSpace(tituloEditable))
                    contenido = AplicarTituloAlFeature(contenido, tituloEditable);

                // Autocorregir solo si el cliente no pide respetar el Gherkin editado a mano.
                var respetarGherkin = root.TryGetProperty("respetarGherkin", out var rg) &&
                    (rg.ValueKind == JsonValueKind.True || (rg.ValueKind == JsonValueKind.String &&
                        string.Equals(rg.GetString(), "true", StringComparison.OrdinalIgnoreCase)));
                if (!respetarGherkin)
                    contenido = SanitizarEscenarioFeature(contenido);

                if (string.IsNullOrWhiteSpace(resumenEditable))
                    resumenEditable = GenerarResumenDesdeContenido(contenido, tituloEditable);
                contenido = AplicarResumenAlFeature(contenido, resumenEditable!);

                // Validación mínima: Scenario + pasos. Si faltan bindings, se crean abajo.
                var erroresValidacion = ValidarFeaturePrueba(contenido);
                if (erroresValidacion.Count > 0 && !forzarIncompleto)
                {
                    return Results.Json(new
                    {
                        ok = false,
                        error = "El borrador no es ejecutable:\n- " + string.Join("\n- ", erroresValidacion)
                            + "\nMarcá 'Guardar igual si está incompleto' solo si querés guardarlo igual."
                    }, statusCode: 400);
                }

                contenido = AsegurarFeatureUnicoYTags(contenido, filterTag, Path.GetFileNameWithoutExtension(nombre));

                // Crear steps SpecFlow faltantes (stubs) para que compile y sea ejecutable.
                var autoSteps = StepsAutoGenerator.AsegurarBindings(automatizacionRoot, contenido);
                var avisosGuardar = new List<string>();
                if (autoSteps.Creados > 0)
                {
                    avisosGuardar.Add(
                        $"Se crearon {autoSteps.Creados} step(s) nuevos en StepDefinitions/{Path.GetFileName(autoSteps.Archivo)} (stubs Pending). Compilá la suite para Ejecutar.");
                    RunnerAudit.Log(automatizacionRoot, "auto-steps",
                        $"creados={autoSteps.Creados};file={Path.GetFileName(autoSteps.Archivo)}");
                }

                var path = Path.Combine(pruebasFeatures, nombre);
                var sobrescribe = File.Exists(path);
                var forzarSobrescribir = root.TryGetProperty("confirmarSobrescribir", out var cs) &&
                    (cs.ValueKind == JsonValueKind.True || (cs.ValueKind == JsonValueKind.String &&
                        string.Equals(cs.GetString(), "true", StringComparison.OrdinalIgnoreCase)));
                if (sobrescribe && !forzarSobrescribir)
                {
                    var catalogoPrev = LeerCatalogoGrupos(pruebasFeatures);
                    catalogoPrev.Asignaciones.TryGetValue(nombre!, out var grupoIdPrev);
                    grupoIdPrev ??= GrupoSinClasificarId;
                    var porIdPrev = GruposPorId(catalogoPrev.Grupos);
                    var grupoNombrePrev = ResolverNombreGrupo(porIdPrev, grupoIdPrev);
                    return Results.Json(new
                    {
                        ok = false,
                        requiereConfirmacion = true,
                        nombre,
                        grupoId = grupoIdPrev,
                        grupoNombre = grupoNombrePrev,
                        error = $"Ya existe «{nombre}» en el módulo «{grupoNombrePrev}». Podés abrirlo para editarlo o sobrescribirlo."
                    }, statusCode: 409);
                }

                await File.WriteAllTextAsync(path, contenido.TrimEnd() + Environment.NewLine, Encoding.UTF8);

                var grupo = AsignarAGrupo(pruebasFeatures, nombre, grupoId, grupoNuevo);
                var item = DescribirBorrador(path);
                RegistrarEscenario(escenarios, item);

                AprendizajeLlm.RegistrarEjemploAlGuardar(
                    pruebasFeatures,
                    nombre!,
                    contenido,
                    tituloEditable ?? item.Titulo,
                    resumenEditable ?? item.Descripcion,
                    grupo.Nombre);

                return Results.Ok(new
                {
                    ok = true,
                    id = item.Id,
                    nombre = item.Nombre,
                    titulo = item.Titulo,
                    descripcion = item.Descripcion,
                    corto = item.Corto,
                    tag = item.Tag,
                    filterTag = item.FilterTag,
                    grupoId = grupo.Id,
                    grupoNombre = grupo.Nombre,
                    rutaRelativa = item.RutaRelativa,
                    contenido,
                    sobrescrito = sobrescribe,
                    aprendizaje = true,
                    stepsAutoCreados = autoSteps.Creados,
                    stepsAutoNuevos = autoSteps.PasosNuevos,
                    avisos = avisosGuardar,
                    mensaje = (sobrescribe
                        ? $"Se actualizó «{item.Nombre}» en el módulo «{grupo.Nombre}»."
                        : $"Se creó «{item.Nombre}» en el módulo «{grupo.Nombre}».")
                        + (autoSteps.Creados > 0
                            ? $" Además se generaron {autoSteps.Creados} step(s) nuevos (Pending) para que sea ejecutable."
                            : " Quedó como ejemplo para el asistente.")
                });
            }
            catch (Exception ex)
            {
                return Results.Json(new { ok = false, error = ex.Message }, statusCode: 400);
            }
        });

        // Lote: pegar Gherkin (uno o varios Feature) → crea módulo(s) + .feature + stubs.
        // Formato sugerido (generado por el asistente Cursor):
        //   # Modulo: Fallas de caja
        //   Feature: ...
        //   ---
        //   # Modulo: Cheques
        //   Feature: ...
        app.MapPost("/pruebas/importar-lote", async (HttpRequest request) =>
        {
            try
            {
                using var reader = new StreamReader(request.Body, Encoding.UTF8);
                var raw = await reader.ReadToEndAsync();
                var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw);
                var root = doc.RootElement;
                var contenido = root.TryGetProperty("contenido", out var c) ? c.GetString() : null;
                var forzarIncompleto = root.TryGetProperty("forzarIncompleto", out var f) && f.GetBoolean();
                var sobrescribir = root.TryGetProperty("confirmarSobrescribir", out var cs) &&
                    (cs.ValueKind == JsonValueKind.True || (cs.ValueKind == JsonValueKind.String &&
                        string.Equals(cs.GetString(), "true", StringComparison.OrdinalIgnoreCase)));
                var moduloDefault = root.TryGetProperty("moduloDefault", out var md) ? md.GetString()?.Trim() : null;

                if (string.IsNullOrWhiteSpace(contenido))
                    return Results.Json(new { ok = false, error = "Pegá o subí el Gherkin del lote." }, statusCode: 400);

                var partes = PartirPaqueteGherkin(contenido);
                if (partes.Count == 0)
                {
                    return Results.Json(new
                    {
                        ok = false,
                        error = "No se encontró ningún Feature. Esperado: bloques con «Feature:» (opc. «# Modulo: Nombre» y separador ---)."
                    }, statusCode: 400);
                }

                var creados = new List<object>();
                var errores = new List<string>();
                var avisos = new List<string>();
                var totalSteps = 0;

                foreach (var parte in partes)
                {
                    try
                    {
                        var featureTxt = parte.Contenido.TrimEnd() + "\n";
                        if (!respetarSoloValidar(featureTxt, forzarIncompleto, out var errVal))
                        {
                            errores.Add($"{parte.Titulo}: {errVal}");
                            continue;
                        }

                        var nombre = SanitizarNombreFeature(parte.NombreArchivo);
                        var filterTag = FilterTagDesdeNombre(nombre);
                        featureTxt = AsegurarFeatureUnicoYTags(featureTxt, filterTag, Path.GetFileNameWithoutExtension(nombre));

                        var path = Path.Combine(pruebasFeatures, nombre);
                        if (File.Exists(path) && !sobrescribir)
                        {
                            errores.Add($"{nombre}: ya existe. Marcá sobrescribir o renombrá el Feature.");
                            continue;
                        }

                        var autoSteps = StepsAutoGenerator.AsegurarBindings(automatizacionRoot, featureTxt);
                        totalSteps += autoSteps.Creados;

                        await File.WriteAllTextAsync(path, featureTxt, Encoding.UTF8);

                        var moduloNombre = !string.IsNullOrWhiteSpace(parte.Modulo)
                            ? parte.Modulo!
                            : (!string.IsNullOrWhiteSpace(moduloDefault) ? moduloDefault! : "Importados Cursor");
                        var grupo = AsignarAGrupo(pruebasFeatures, nombre, null, moduloNombre);
                        var item = DescribirBorrador(path);
                        RegistrarEscenario(escenarios, item);

                        AprendizajeLlm.RegistrarEjemploAlGuardar(
                            pruebasFeatures, nombre, featureTxt, item.Titulo, item.Descripcion, grupo.Nombre);

                        creados.Add(new
                        {
                            nombre = item.Nombre,
                            titulo = item.Titulo,
                            grupoId = grupo.Id,
                            grupoNombre = grupo.Nombre,
                            stepsAuto = autoSteps.Creados
                        });
                    }
                    catch (Exception exParte)
                    {
                        errores.Add($"{parte.Titulo}: {exParte.Message}");
                    }
                }

                if (creados.Count == 0)
                {
                    return Results.Json(new
                    {
                        ok = false,
                        error = "No se pudo crear ningún caso.\n- " + string.Join("\n- ", errores)
                    }, statusCode: 400);
                }

                if (totalSteps > 0)
                    avisos.Add($"Se generaron {totalSteps} step(s) stub (Pending) en PruebaAutoSteps.cs.");

                return Results.Ok(new
                {
                    ok = true,
                    creados,
                    errores,
                    avisos,
                    cantidad = creados.Count,
                    mensaje = $"Se crearon {creados.Count} caso(s) en módulo(s) nuevos o existentes."
                        + (errores.Count > 0 ? $" ({errores.Count} con error)" : "")
                });
            }
            catch (Exception ex)
            {
                return Results.Json(new { ok = false, error = ex.Message }, statusCode: 400);
            }

            static bool respetarSoloValidar(string featureTxt, bool forzar, out string error)
            {
                error = "";
                if (featureTxt.Contains("FALTA INFORMACIÓN", StringComparison.OrdinalIgnoreCase) && !forzar)
                {
                    error = "contiene FALTA INFORMACIÓN";
                    return false;
                }
                var errs = ValidarFeaturePrueba(featureTxt);
                if (errs.Count > 0 && !forzar)
                {
                    error = string.Join("; ", errs);
                    return false;
                }
                return true;
            }
        });
    }

    private sealed class GherkinLoteParte
    {
        public string? Modulo { get; set; }
        public string Titulo { get; set; } = "";
        public string NombreArchivo { get; set; } = "";
        public string Contenido { get; set; } = "";
    }

    /// <summary>
    /// Parte un paquete con uno o más Feature. Respeta # Modulo: / # Módulo: y separadores --- / ==== .
    /// </summary>
    private static List<GherkinLoteParte> PartirPaqueteGherkin(string texto)
    {
        var raw = (texto ?? "").Replace("\r\n", "\n").Trim();
        if (string.IsNullOrWhiteSpace(raw)) return [];

        // Separadores de lote entre features
        var bloques = Regex.Split(raw, @"(?m)^\s*(?:-{3,}|={3,})\s*$")
            .Select(b => b.Trim())
            .Where(b => b.Length > 0)
            .ToList();

        // Si un solo bloque tiene varios Feature/Característica, partir por cabecera.
        if (bloques.Count == 1 && Regex.Matches(bloques[0], @"(?im)^(?:Feature|Caracter[ií]stica)\s*:").Count > 1)
        {
            var parts = Regex.Split(bloques[0], @"(?im)(?=^(?:Feature|Caracter[ií]stica)\s*:)");
            bloques = parts.Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
        }

        var result = new List<GherkinLoteParte>();
        string? moduloArrastre = null;
        foreach (var bloque in bloques)
        {
            var lines = bloque.Split('\n').ToList();
            string? modulo = null;
            var bodyLines = new List<string>();
            foreach (var line in lines)
            {
                var mMod = Regex.Match(line, @"^\s*#\s*M[oó]dulo\s*:\s*(.+)\s*$", RegexOptions.IgnoreCase);
                if (mMod.Success)
                {
                    modulo = mMod.Groups[1].Value.Trim();
                    continue;
                }
                var mMod2 = Regex.Match(line, @"^\s*@Modulo[_\-]?(.+)\s*$", RegexOptions.IgnoreCase);
                if (mMod2.Success)
                {
                    modulo = mMod2.Groups[1].Value.Replace('_', ' ').Trim();
                    continue;
                }
                bodyLines.Add(line);
            }

            var body = string.Join("\n", bodyLines).Trim();
            if (!Regex.IsMatch(body, @"(?im)^(?:Feature|Caracter[ií]stica)\s*:"))
            {
                // Solo metadatos de módulo para el siguiente
                if (!string.IsNullOrWhiteSpace(modulo))
                    moduloArrastre = modulo;
                continue;
            }

            if (string.IsNullOrWhiteSpace(modulo))
                modulo = moduloArrastre;
            moduloArrastre = modulo;

            var mTit = Regex.Match(body, @"(?im)^(?:Feature|Caracter[ií]stica)\s*:\s*(.+)$");
            var titulo = mTit.Success ? mTit.Groups[1].Value.Trim() : "Caso importado";
            var nombre = SanitizarNombreFeature(titulo + ".feature");

            // Asegurar Background de login si el Feature no lo trae (suite SOT).
            // \s* delante: el Background suele venir indentado dentro del Feature.
            if (!Regex.IsMatch(body, @"(?im)^\s*(?:Background|Antecedentes)\s*:"))
                body = InsertarBackgroundTrasFeature(body);

            result.Add(new GherkinLoteParte
            {
                Modulo = modulo,
                Titulo = titulo,
                NombreArchivo = nombre,
                Contenido = body
            });
        }

        return result;
    }

    private static string InsertarBackgroundTrasFeature(string feature)
    {
        var lines = feature.Replace("\r\n", "\n").Split('\n').ToList();
        var idx = lines.FindIndex(l => Regex.IsMatch(l, @"(?i)^(?:Feature|Caracter[ií]stica)\s*:"));
        if (idx < 0) return feature;
        var insertAt = idx + 1;
        while (insertAt < lines.Count && (lines[insertAt].TrimStart().StartsWith('#') || string.IsNullOrWhiteSpace(lines[insertAt])))
            insertAt++;
        var bg = BackgroundLogin.TrimEnd().Split('\n');
        lines.Insert(insertAt, "");
        for (var i = 0; i < bg.Length; i++)
            lines.Insert(insertAt + 1 + i, bg[i]);
        return string.Join("\n", lines);
    }

    /// <summary>True si el texto ya es un .feature usable (no hace falta regenerar).</summary>
    private static bool EsGherkinListo(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return false;
        var t = texto.Trim();
        return Regex.IsMatch(t, @"(?im)^(?:Feature|Caracter[ií]stica)\s*:") &&
               Regex.IsMatch(t, @"(?im)^(?:Scenario(?: Outline)?|Escenario(?: Outline)?|Esquema del escenario)\s*:") &&
               Regex.IsMatch(t, @"(?im)^(Given|When|Then|And|But|Dado|Cuando|Entonces|Y|Pero)\s+\S+");
    }

    private static object? ConstruirRespuestaGherkinListo(
        string gherkin,
        List<string> avisos,
        string detalleUsuario,
        string? moduloSugerido = null,
        string? tipoIssue = null,
        AnalisisCoreResult? baseResult = null)
    {
        var partes = PartirPaqueteGherkin(gherkin);
        if (partes.Count == 0) return null;

        var featureOut = string.Join("\n\n---\n\n", partes.Select(p =>
            (string.IsNullOrWhiteSpace(p.Modulo) ? "" : $"# Modulo: {p.Modulo}\n") + p.Contenido));
        var hallazgos = baseResult?.Hallazgos ?? new List<string>();
        var riesgos = baseResult?.Riesgos ?? new List<string>();
        var steps = baseResult?.StepsReutilizables ?? new List<string>();
        var titulo = baseResult?.TituloTicket;
        var modulo = baseResult?.ModuloSugerido ?? moduloSugerido;
        var detalleSug = baseResult?.DetalleSugerido;
        var descripcion = baseResult?.DescripcionCaso;
        var tipo = baseResult?.TipoIssue ?? tipoIssue;

        if (partes.Count == 1 && !string.IsNullOrWhiteSpace(partes[0].Titulo))
            titulo = partes[0].Titulo;
        if (partes.Count == 1 && !string.IsNullOrWhiteSpace(partes[0].Modulo))
            modulo = partes[0].Modulo;

        hallazgos.Add(partes.Count > 1
            ? $"Gherkin listo: {partes.Count} Feature(s). Usá «Crear módulo(s) y casos» para importar el lote."
            : "Gherkin listo detectado: se respeta sin regenerar. Podés Guardar o «Crear módulo(s) y casos».");
        detalleSug = Truncar(detalleUsuario.Length >= 25 ? detalleUsuario : (detalleSug ?? ""), 700);

        return Respuesta(
            "crear",
            true,
            new List<string>(),
            riesgos,
            hallazgos,
            avisos,
            steps,
            featureOut,
            "Gherkin listo para crear módulo(s) y casos.",
            descripcion,
            modulo,
            titulo,
            detalleSug,
            tipo);
    }

    private static List<object> ListarBorradores(string pruebasFeatures, CatalogoGrupos? catalogo = null)
    {
        if (!Directory.Exists(pruebasFeatures))
            return [];

        catalogo ??= LeerCatalogoGrupos(pruebasFeatures);
        var porId = GruposPorId(catalogo.Grupos);

        return EnumerarRutasFeatures(pruebasFeatures)
            .Where(rel => !ExcluirDeListadoRunnerBorradores(rel))
            .Select(rel =>
            {
                var full = ResolverRutaFeature(pruebasFeatures, rel);
                var item = DescribirBorrador(full, pruebasFeatures);
                catalogo.Asignaciones.TryGetValue(item.Nombre, out var grupoId);
                grupoId ??= GrupoSinClasificarId;
                grupoId = NormalizarGrupoIdParaUi(grupoId);
                var grupoNombre = ResolverNombreGrupo(porId, grupoId);
                return new
                {
                    id = item.Id,
                    nombre = item.Nombre,
                    titulo = item.Titulo,
                    tag = item.Tag,
                    filterTag = item.FilterTag,
                    corto = item.Corto,
                    descripcion = item.Descripcion,
                    rutaRelativa = item.RutaRelativa,
                    modificadoUtc = item.ModificadoUtc,
                    grupoId,
                    grupoNombre
                };
            })
            .OrderBy(x => x.titulo, StringComparer.Create(new System.Globalization.CultureInfo("es-AR"), ignoreCase: true))
            .ThenBy(x => x.nombre, StringComparer.OrdinalIgnoreCase)
            .Cast<object>()
            .ToList();
    }

    private const string GrupoSinClasificarId = "grp-sin-clasificar";

    private static string NormalizarGrupoIdParaUi(string grupoId) =>
        string.Equals(grupoId, "grp-release-9", StringComparison.OrdinalIgnoreCase) ? "release-9" : grupoId;

    /// <summary>Pruebas @Prueba fuera de Release 9 y de @PruebaAutoStub (regresión global del Runner).</summary>
    private static int ContarRegresionPruebas(string pruebasFeatures, CatalogoGrupos catalogo)
    {
        if (!Directory.Exists(pruebasFeatures))
            return 0;
        return EnumerarRutasFeatures(pruebasFeatures).Count(rel =>
            !EsCasoRelease9(pruebasFeatures, rel, catalogo)
            && !EsCasoPruebaAutoStub(pruebasFeatures, rel));
    }

    /// <summary>
    /// Casos ya cubiertos por catalog-data (suite fija) o borradores SC-437 fuera del módulo Cuadre/Cierre.
    /// </summary>
    private static bool ExcluirDeListadoRunnerBorradores(string rel)
    {
        var n = (rel ?? "").Replace('\\', '/');
        if (n.StartsWith("CierreCuadre/CC_", StringComparison.OrdinalIgnoreCase))
            return true;
        if (n.StartsWith("SC-437_", StringComparison.OrdinalIgnoreCase))
            return true;
        if (n.StartsWith("SC-470_CA-", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    private static bool EsCasoRelease9(string pruebasFeatures, string rel, CatalogoGrupos catalogo)
    {
        var norm = rel.Replace('\\', '/');
        if (norm.StartsWith("Release-9/", StringComparison.OrdinalIgnoreCase))
            return true;
        catalogo.Asignaciones.TryGetValue(rel, out var gid);
        if (string.Equals(gid, "grp-release-9", StringComparison.OrdinalIgnoreCase))
            return true;
        try
        {
            var path = ResolverRutaFeature(pruebasFeatures, rel);
            var contenido = File.ReadAllText(path);
            if (Regex.IsMatch(contenido, @"@Release9\b", RegexOptions.IgnoreCase))
                return true;
        }
        catch
        {
            /* best-effort */
        }
        return false;
    }

    /// <summary>Features con @PruebaAutoStub (steps Ignore): fuera de Correr todo y del listado de regresión.</summary>
    private static bool EsCasoPruebaAutoStub(string pruebasFeatures, string rel)
    {
        var norm = (rel ?? "").Replace('\\', '/');
        if (norm.StartsWith("SC-470_CA-", StringComparison.OrdinalIgnoreCase))
            return true;
        try
        {
            var path = ResolverRutaFeature(pruebasFeatures, rel);
            var contenido = File.ReadAllText(path);
            return Regex.IsMatch(contenido, @"@PruebaAutoStub\b", RegexOptions.IgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Ids de runners ya existentes en el HTML del Runner (no son grupos de testing dinámicos).</summary>
    private static readonly Dictionary<string, string> RunnersExistentes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["regresion"] = "Regresión",
        ["alta-caja"] = "Alta de caja",
        ["intercaja"] = "Intercaja",
        ["caja-boveda"] = "Pases Caja-Bóveda (opcional)",
        ["cheques"] = "Cheques",
        ["parametria-sc161"] = "Parametría SC-161",
        ["cierre-cuadre"] = "Cuadre y Cierre de Caja",
        ["cierre-cuadre-131"] = "Cuadre y Cierre de Caja (alias)",
        ["release-9"] = "Release 9",
    };

    private static string RutaCatalogoGrupos(string pruebasFeatures) =>
        Path.Combine(pruebasFeatures, "grupos.json");

    private static string NormalizarNombreFeature(string pruebasFeatures, string fullPath)
    {
        var rel = Path.GetRelativePath(pruebasFeatures, fullPath);
        return rel.Replace('\\', '/');
    }

    private static string ResolverRutaFeature(string pruebasFeatures, string nombre)
    {
        var key = (nombre ?? "").Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(pruebasFeatures, key);
    }

    private static IEnumerable<string> EnumerarRutasFeatures(string pruebasFeatures)
    {
        if (!Directory.Exists(pruebasFeatures))
            return [];
        return Directory
            .GetFiles(pruebasFeatures, "*.feature", SearchOption.AllDirectories)
            .Select(f => NormalizarNombreFeature(pruebasFeatures, f))
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase);
    }

    private static string SanitizarNombreFeatureRel(string pruebasFeatures, string? nombre)
    {
        var key = (nombre ?? "").Replace('/', Path.DirectorySeparatorChar).TrimStart('\\', '/');
        if (string.IsNullOrWhiteSpace(key) || key.Contains("..", StringComparison.Ordinal))
            throw new InvalidOperationException("Nombre de feature inválido.");
        var root = Path.GetFullPath(pruebasFeatures);
        var full = Path.GetFullPath(Path.Combine(pruebasFeatures, key));
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Ruta de feature fuera de _pruebas.");
        if (!full.EndsWith(".feature", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Nombre de feature inválido.");
        return key.Replace('\\', '/');
    }

    private sealed record GrupoTesting(string Id, string Nombre, string Sub);

    private static Dictionary<string, GrupoTesting> GruposPorId(IEnumerable<GrupoTesting> grupos) =>
        grupos
            .GroupBy(g => g.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

    private sealed class CatalogoGrupos
    {
        public List<GrupoTesting> Grupos { get; set; } = [];
        public Dictionary<string, string> Asignaciones { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>Si true, no se reinserta automáticamente «Testing (sin clasificar)» al leer.</summary>
        public bool OmitirSinClasificar { get; set; }
    }

    private static CatalogoGrupos LeerCatalogoGrupos(string pruebasFeatures, bool autoCrearSinClasificar = true)
    {
        Directory.CreateDirectory(pruebasFeatures);
        var path = RutaCatalogoGrupos(pruebasFeatures);
        CatalogoGrupos catalogo;
        if (!File.Exists(path))
        {
            catalogo = CatalogoInicial();
            GuardarCatalogoGrupos(pruebasFeatures, catalogo);
            return catalogo;
        }

        try
        {
            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            catalogo = new CatalogoGrupos();
            if (doc.RootElement.TryGetProperty("omitirSinClasificar", out var omit) &&
                (omit.ValueKind == JsonValueKind.True || omit.ValueKind == JsonValueKind.False))
                catalogo.OmitirSinClasificar = omit.GetBoolean();
            if (doc.RootElement.TryGetProperty("grupos", out var grupos) && grupos.ValueKind == JsonValueKind.Array)
            {
                foreach (var g in grupos.EnumerateArray())
                {
                    var id = g.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                    var nombre = g.TryGetProperty("nombre", out var nomEl) ? nomEl.GetString() : null;
                    var sub = g.TryGetProperty("sub", out var subEl) ? subEl.GetString() : "Grupo de testing";
                    if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(nombre))
                        catalogo.Grupos.Add(new GrupoTesting(id!, nombre!, sub ?? "Grupo de testing"));
                }
            }
            if (doc.RootElement.TryGetProperty("asignaciones", out var asig) && asig.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in asig.EnumerateObject())
                    catalogo.Asignaciones[p.Name] = p.Value.GetString() ?? GrupoSinClasificarId;
            }
        }
        catch
        {
            catalogo = CatalogoInicial();
        }

        if (catalogo.Grupos.Count > 0)
        {
            var dedup = catalogo.Grupos
                .GroupBy(g => g.Id, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
            if (dedup.Count != catalogo.Grupos.Count)
            {
                catalogo.Grupos = dedup;
                GuardarCatalogoGrupos(pruebasFeatures, catalogo);
            }
        }

        if (autoCrearSinClasificar && !catalogo.OmitirSinClasificar &&
            catalogo.Grupos.All(g => !string.Equals(g.Id, GrupoSinClasificarId, StringComparison.OrdinalIgnoreCase)))
            catalogo.Grupos.Insert(0, new GrupoTesting(GrupoSinClasificarId, "Testing (sin clasificar)", "Casos del asistente aún sin grupo"));

        // Evitar pruebas generadas “fantasma”: no colgarlas de runners de la suite (intercaja, alta-caja…).
        if (ReconciliarCatalogoPruebas(pruebasFeatures, catalogo))
            GuardarCatalogoGrupos(pruebasFeatures, catalogo);

        return catalogo;
    }

    /// <summary>
    /// Remapea asignaciones a runners de la suite → módulos testing (grp-*) y asigna .feature huérfanos.
    /// Así figuran en «Módulos y pruebas» y no solo disparan 409 al guardar.
    /// </summary>
    private static bool ReconciliarCatalogoPruebas(string pruebasFeatures, CatalogoGrupos catalogo)
    {
        var changed = false;

        foreach (var key in catalogo.Asignaciones.Keys.ToList())
        {
            var gid = catalogo.Asignaciones[key];
            if (!RunnersExistentes.ContainsKey(gid))
                continue;
            var destino = AsegurarGrupoTestingDesdeRunner(catalogo, gid);
            if (!string.Equals(catalogo.Asignaciones[key], destino.Id, StringComparison.OrdinalIgnoreCase))
            {
                catalogo.Asignaciones[key] = destino.Id;
                changed = true;
            }
        }

        if (Directory.Exists(pruebasFeatures))
        {
            foreach (var rel in EnumerarRutasFeatures(pruebasFeatures))
            {
                var nombre = rel;
                if (catalogo.Asignaciones.ContainsKey(nombre))
                    continue;

                var f = ResolverRutaFeature(pruebasFeatures, nombre);

                string? titulo = null;
                string contenido = "";
                try
                {
                    contenido = File.ReadAllText(f);
                    titulo = ExtraerTituloFeature(contenido);
                }
                catch { /* best-effort */ }

                var ticket = ExtraerTicketKey(contenido) ?? ExtraerTicketKey(nombre);
                var modulo = InferirModuloDesdeTexto(titulo, contenido, ticket);
                // Si el usuario eliminó «Testing (sin clasificar)», no lo revivimos con huérfanos.
                if (string.IsNullOrWhiteSpace(modulo) && catalogo.OmitirSinClasificar)
                    continue;
                var grupo = AsegurarGrupoTestingPorNombre(catalogo, modulo, revivirSinClasificar: false);
                if (grupo is null)
                    continue;
                catalogo.Asignaciones[nombre] = grupo.Id;
                changed = true;
            }
        }

        foreach (var key in catalogo.Asignaciones.Keys.ToList())
        {
            if (File.Exists(ResolverRutaFeature(pruebasFeatures, key)))
                continue;
            catalogo.Asignaciones.Remove(key);
            changed = true;
        }

        return changed;
    }

    private static GrupoTesting AsegurarGrupoTestingDesdeRunner(CatalogoGrupos catalogo, string runnerId)
    {
        var nombre = RunnersExistentes.TryGetValue(runnerId, out var n) ? n : runnerId;
        var idTesting = "grp-" + runnerId;
        var porId = catalogo.Grupos.FirstOrDefault(g =>
            string.Equals(g.Id, idTesting, StringComparison.OrdinalIgnoreCase));
        if (porId is not null) return porId;

        var porNombre = catalogo.Grupos.FirstOrDefault(g =>
            string.Equals(g.Nombre, nombre, StringComparison.OrdinalIgnoreCase));
        if (porNombre is not null) return porNombre;

        // Nombre corto sin sufijo "(opcional)" para el módulo de pruebas
        var nombreModulo = Regex.Replace(nombre, @"\s*\(opcional\)\s*$", "", RegexOptions.IgnoreCase).Trim();
        porNombre = catalogo.Grupos.FirstOrDefault(g =>
            string.Equals(g.Nombre, nombreModulo, StringComparison.OrdinalIgnoreCase));
        if (porNombre is not null) return porNombre;

        var nuevo = new GrupoTesting(idTesting, nombreModulo, "Módulo de pruebas (separado de la suite)");
        catalogo.Grupos.Add(nuevo);
        return nuevo;
    }

    private static GrupoTesting? AsegurarGrupoTestingPorNombre(
        CatalogoGrupos catalogo,
        string? nombreModulo,
        bool revivirSinClasificar = true)
    {
        var nombre = (nombreModulo ?? "").Trim();
        if (string.IsNullOrWhiteSpace(nombre))
            nombre = "Testing (sin clasificar)";

        var runner = RunnersExistentes.FirstOrDefault(kv =>
            string.Equals(kv.Value, nombre, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                Regex.Replace(kv.Value, @"\s*\(opcional\)\s*$", "", RegexOptions.IgnoreCase).Trim(),
                nombre,
                StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(runner.Key))
            return AsegurarGrupoTestingDesdeRunner(catalogo, runner.Key);

        if (string.Equals(nombre, "Testing (sin clasificar)", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(nombre, "Desde Jira", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(nombre, "Desde Jira", StringComparison.OrdinalIgnoreCase))
        {
            // Respetar borrado del usuario: no recrear el bucket vacío en reconciliación.
            if (catalogo.OmitirSinClasificar && !revivirSinClasificar)
                return null;

            catalogo.OmitirSinClasificar = false;
            var sin = catalogo.Grupos.FirstOrDefault(g =>
                string.Equals(g.Id, GrupoSinClasificarId, StringComparison.OrdinalIgnoreCase));
            if (sin is not null) return sin;
            sin = new GrupoTesting(GrupoSinClasificarId, "Testing (sin clasificar)", "Casos del asistente aún sin grupo");
            catalogo.Grupos.Insert(0, sin);
            return sin;
        }

        var existente = catalogo.Grupos.FirstOrDefault(g =>
            string.Equals(g.Nombre, nombre, StringComparison.OrdinalIgnoreCase));
        if (existente is not null) return existente;

        var id = "grp-" + Regex.Replace(nombre.ToLowerInvariant(), @"[^\w]+", "-").Trim('-');
        if (string.IsNullOrWhiteSpace(id) || id == "grp-")
            id = "grp-" + DateTime.Now.ToString("yyyyMMddHHmmss");
        var nuevo = new GrupoTesting(id, nombre, "Grupo de testing creado desde el asistente");
        catalogo.Grupos.Add(nuevo);
        return nuevo;
    }

    private static CatalogoGrupos CatalogoInicial() => new()
    {
        Grupos =
        [
            new GrupoTesting(GrupoSinClasificarId, "Testing (sin clasificar)", "Casos del asistente aún sin grupo")
        ]
    };

    private static void GuardarCatalogoGrupos(string pruebasFeatures, CatalogoGrupos catalogo)
    {
        OrdenarGruposAlfabeticamente(catalogo);
        var payload = new
        {
            omitirSinClasificar = catalogo.OmitirSinClasificar,
            grupos = catalogo.Grupos.Select(g => new { id = g.Id, nombre = g.Nombre, sub = g.Sub }),
            asignaciones = catalogo.Asignaciones
                .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase)
        };
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(RutaCatalogoGrupos(pruebasFeatures), json + Environment.NewLine);
    }

    /// <summary>
    /// Módulos de testing ordenados A→Z por nombre.
    /// «Testing (sin clasificar)» queda primero si existe; Release 9 al final de los fijos de release.
    /// </summary>
    private static void OrdenarGruposAlfabeticamente(CatalogoGrupos catalogo)
    {
        if (catalogo.Grupos.Count <= 1) return;

        static int Prioridad(GrupoTesting g)
        {
            if (string.Equals(g.Id, GrupoSinClasificarId, StringComparison.OrdinalIgnoreCase)) return 0;
            if (string.Equals(g.Id, "grp-release-9", StringComparison.OrdinalIgnoreCase)
                || string.Equals(g.Id, "release-9", StringComparison.OrdinalIgnoreCase)) return 2;
            return 1;
        }

        catalogo.Grupos = catalogo.Grupos
            .OrderBy(Prioridad)
            .ThenBy(g => g.Nombre, StringComparer.Create(new System.Globalization.CultureInfo("es-AR"), ignoreCase: true))
            .ThenBy(g => g.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static GrupoTesting AsignarAGrupo(string pruebasFeatures, string nombreFeature, string? grupoId, string? grupoNuevo)
    {
        var catalogo = LeerCatalogoGrupos(pruebasFeatures);

        if (!string.IsNullOrWhiteSpace(grupoNuevo))
        {
            var gNuevo = AsegurarGrupoTestingPorNombre(catalogo, grupoNuevo.Trim())!;
            catalogo.Asignaciones[nombreFeature] = gNuevo.Id;
            GuardarCatalogoGrupos(pruebasFeatures, catalogo);
            return gNuevo;
        }

        if (string.IsNullOrWhiteSpace(grupoId))
            grupoId = GrupoSinClasificarId;

        // Si el destino es un runner de la suite, usar módulo de pruebas espejo (visible en Módulos y pruebas).
        if (RunnersExistentes.ContainsKey(grupoId!))
        {
            var gRunner = AsegurarGrupoTestingDesdeRunner(catalogo, grupoId!);
            catalogo.Asignaciones[nombreFeature] = gRunner.Id;
            GuardarCatalogoGrupos(pruebasFeatures, catalogo);
            return gRunner;
        }

        // Si se usa «sin clasificar» y estaba omitido/eliminado, lo recreamos.
        if (string.Equals(grupoId, GrupoSinClasificarId, StringComparison.OrdinalIgnoreCase) &&
            catalogo.Grupos.All(g => !string.Equals(g.Id, GrupoSinClasificarId, StringComparison.OrdinalIgnoreCase)))
        {
            catalogo.OmitirSinClasificar = false;
            catalogo.Grupos.Insert(0, new GrupoTesting(GrupoSinClasificarId, "Testing (sin clasificar)", "Casos del asistente aún sin grupo"));
        }

        catalogo.Asignaciones[nombreFeature] = grupoId!;
        GuardarCatalogoGrupos(pruebasFeatures, catalogo);

        if (catalogo.Grupos.FirstOrDefault(g => string.Equals(g.Id, grupoId, StringComparison.OrdinalIgnoreCase)) is { } g)
            return g;

        return AsegurarGrupoTestingPorNombre(catalogo, grupoId)!;
    }

    private static string ResolverNombreGrupo(Dictionary<string, GrupoTesting> porId, string grupoId)
    {
        if (porId.TryGetValue(grupoId, out var g))
            return g.Nombre;
        if (RunnersExistentes.TryGetValue(grupoId, out var nom))
            return Regex.Replace(nom, @"\s*\(opcional\)\s*$", "", RegexOptions.IgnoreCase).Trim();
        return grupoId;
    }

    private sealed class UndoEntry
    {
        public string Tipo { get; set; } = "";
        public string Descripcion { get; set; } = "";
        public string? Nombre { get; set; }
        public string? Contenido { get; set; }
        public string? GrupoIdAnterior { get; set; }
        public string? GrupoNombreAnterior { get; set; }
        public string? GrupoIdNuevo { get; set; }
    }

    private static string RutaUndo(string pruebasFeatures) =>
        Path.Combine(pruebasFeatures, "_undo.json");

    private static void GuardarUndo(string pruebasFeatures, UndoEntry entry)
    {
        Directory.CreateDirectory(pruebasFeatures);
        var json = JsonSerializer.Serialize(entry, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(RutaUndo(pruebasFeatures), json + Environment.NewLine, Encoding.UTF8);
    }

    private static UndoEntry? LeerUndo(string pruebasFeatures)
    {
        var path = RutaUndo(pruebasFeatures);
        if (!File.Exists(path)) return null;
        try
        {
            var raw = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(raw)) return null;
            return JsonSerializer.Deserialize<UndoEntry>(raw);
        }
        catch
        {
            return null;
        }
    }

    private static void BorrarUndo(string pruebasFeatures)
    {
        var path = RutaUndo(pruebasFeatures);
        if (File.Exists(path))
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }

    private static void QuitarAsignacion(string pruebasFeatures, string nombreFeature)
    {
        var catalogo = LeerCatalogoGrupos(pruebasFeatures);
        if (catalogo.Asignaciones.Remove(nombreFeature))
            GuardarCatalogoGrupos(pruebasFeatures, catalogo);
    }

    private static void EliminarArchivoFeature(
        string pruebasFeatures,
        string nombreFeature,
        Dictionary<string, EscenarioDef> escenarios)
    {
        nombreFeature = SanitizarNombreFeatureRel(pruebasFeatures, nombreFeature);

        var path = ResolverRutaFeature(pruebasFeatures, nombreFeature);
        if (File.Exists(path))
            File.Delete(path);

        // SpecFlow puede dejar el .feature.cs generado.
        var csPath = path + ".cs";
        if (File.Exists(csPath))
        {
            try { File.Delete(csPath); } catch { /* best effort */ }
        }

        escenarios.Remove(IdDesdeNombre(nombreFeature));
        QuitarAsignacion(pruebasFeatures, nombreFeature);
    }

    private static void SyncEscenariosPruebas(string pruebasFeatures, Dictionary<string, EscenarioDef> escenarios)
    {
        var vigentes = new HashSet<string>(StringComparer.Ordinal);
        if (Directory.Exists(pruebasFeatures))
        {
            foreach (var rel in EnumerarRutasFeatures(pruebasFeatures))
            {
                if (EsCasoPruebaAutoStub(pruebasFeatures, rel))
                    continue;

                var f = ResolverRutaFeature(pruebasFeatures, rel);
                // Autocorregir features viejos/rotos + tags únicos.
                try
                {
                    var nombre = rel;
                    var contenido = File.ReadAllText(f);
                    var original = contenido;
                    contenido = SanitizarEscenarioFeature(contenido);
                    var filterTag = ExtraerFilterTag(contenido) ?? FilterTagDesdeNombre(nombre);
                    var slug = Path.GetFileNameWithoutExtension(nombre);
                    var necesitaTag = ExtraerFilterTag(contenido) is null;
                    var necesitaTitulo = !Regex.IsMatch(contenido, @"(?im)^(Feature|Característica):\s*\S");
                    if (necesitaTag || necesitaTitulo)
                        contenido = AsegurarFeatureUnicoYTags(contenido, filterTag, slug);
                    if (!string.Equals(original.TrimEnd(), contenido.TrimEnd(), StringComparison.Ordinal))
                        File.WriteAllText(f, contenido.TrimEnd() + Environment.NewLine, Encoding.UTF8);
                }
                catch
                {
                    // best-effort
                }

                var item = DescribirBorrador(f, pruebasFeatures);
                vigentes.Add(item.Id);
                RegistrarEscenario(escenarios, item);
            }
        }

        foreach (var key in escenarios.Keys.Where(k => k.StartsWith("prueba-", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            if (!vigentes.Contains(key))
                escenarios.Remove(key);
        }
    }

    private static void RegistrarEscenario(Dictionary<string, EscenarioDef> escenarios, BorradorInfo item)
    {
        escenarios[item.Id] = new EscenarioDef(
            item.Titulo,
            "run-PruebaBorrador.ps1",
            ["-Feature", item.Nombre, "-FilterTag", item.FilterTag]);
    }

    private static BorradorInfo DescribirBorrador(string path, string? pruebasFeaturesRoot = null)
    {
        var nombre = pruebasFeaturesRoot is not null
            ? NormalizarNombreFeature(pruebasFeaturesRoot, path)
            : Path.GetFileName(path);
        var contenido = File.ReadAllText(path);
        var titulo = ExtraerTituloFeature(contenido) ?? Path.GetFileNameWithoutExtension(nombre);
        titulo = Regex.Replace(titulo ?? "", @"^Prueba\s*[—\-–:]?\s*", "", RegexOptions.IgnoreCase).Trim();
        if (string.IsNullOrWhiteSpace(titulo))
            titulo = Path.GetFileNameWithoutExtension(nombre);
        // Nombres de archivo legados Prueba_SC-… → mostrar sin el prefijo
        if (titulo.StartsWith("Prueba_", StringComparison.OrdinalIgnoreCase))
            titulo = titulo["Prueba_".Length..];
        var filterTag = ExtraerFilterTag(contenido) ?? FilterTagDesdeNombre(nombre);
        var tag = ExtraerPrimerTag(contenido) ?? ("@Prueba @" + filterTag);
        var resumen = ExtraerResumenFeature(contenido) ?? GenerarResumenDesdeContenido(contenido, titulo);
        var corto = Truncar(resumen, 90);
        return new BorradorInfo(
            IdDesdeNombre(nombre),
            nombre,
            titulo,
            tag,
            filterTag,
            corto,
            resumen,
            "Features/_pruebas/" + nombre.Replace('\\', '/'),
            File.GetLastWriteTimeUtc(path));
    }

    private static string FilterTagDesdeNombre(string nombre)
    {
        var baseName = Path.GetFileNameWithoutExtension(nombre);
        if (baseName.StartsWith("Prueba_", StringComparison.OrdinalIgnoreCase))
            baseName = baseName["Prueba_".Length..];
        var ticket = ExtraerTicketKey(baseName) ?? ExtraerTicketKey(nombre);
        if (!string.IsNullOrWhiteSpace(ticket))
            return "PruebaRun_" + ticket.Replace('-', '_');

        var slug = Regex.Replace(baseName, @"[^\w]+", "_");
        if (string.IsNullOrWhiteSpace(slug)) slug = "Borrador";
        // Evitar tags enormes por nombres de archivo largos
        if (slug.Length > 40) slug = slug[..40].TrimEnd('_');
        return "PruebaRun_" + slug;
    }

    private static string? ExtraerFilterTag(string contenido)
    {
        var m = Regex.Match(contenido, @"@(PruebaRun_[\w]+)");
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>
    /// Tags de ejecución + título Feature único (evita colisión SpecFlow con features productivos).
    /// </summary>
    private static string AsegurarFeatureUnicoYTags(string contenido, string filterTag, string slugArchivo)
    {
        filterTag = filterTag.TrimStart('@');
        var tagLine = "@Prueba @" + filterTag;
        var lines = contenido.Replace("\r\n", "\n").Split('\n').ToList();

        while (lines.Count > 0 && Regex.IsMatch(lines[0].Trim(), @"^(@[\w\-]+\s*)+$"))
            lines.RemoveAt(0);

        for (var i = 0; i < Math.Min(lines.Count, 8); i++)
        {
            if (Regex.IsMatch(lines[i], @"@PruebaRun_[\w]+"))
                lines[i] = Regex.Replace(lines[i], @"@PruebaRun_[\w]+", "").Trim();
        }

        var featureIdx = lines.FindIndex(l => Regex.IsMatch(l, @"(?i)^(Feature|Característica):\s*"));
        if (featureIdx >= 0)
        {
            var m = Regex.Match(lines[featureIdx], @"(?i)^(Feature|Característica):\s*(.+)$");
            var titulo = m.Success ? m.Groups[1].Value.Trim() : "Borrador";
            titulo = LimpiarTituloFeature(titulo);
            if (string.IsNullOrWhiteSpace(titulo)) titulo = "Borrador";
            // Título corto de prueba normal; la unicidad SpecFlow va por tags @PruebaRun_*
            lines[featureIdx] = $"Característica: {AcortarTitulo(titulo, 70)}";
            // Trazabilidad del archivo sin alargar el título visible
            if (!string.IsNullOrWhiteSpace(slugArchivo) &&
                !lines.Any(l => l.Contains(slugArchivo, StringComparison.OrdinalIgnoreCase) && l.TrimStart().StartsWith('#')))
            {
                lines.Insert(featureIdx + 1, $"  # Archivo: {slugArchivo}");
            }
        }
        else
        {
            lines.Insert(0, "Característica: Borrador");
        }

        lines.Insert(0, tagLine);
        return string.Join(Environment.NewLine, lines);
    }

    private static string AplicarTituloAlFeature(string contenido, string titulo)
    {
        titulo = LimpiarTituloFeature(titulo);
        titulo = AcortarTitulo(titulo, 70);
        if (string.IsNullOrWhiteSpace(titulo))
            return contenido;

        var lines = contenido.Replace("\r\n", "\n").Split('\n').ToList();
        var featureIdx = lines.FindIndex(l => Regex.IsMatch(l, @"(?i)^(Feature|Característica):\s*"));
        if (featureIdx >= 0)
            lines[featureIdx] = $"Característica: {titulo}";
        else
            lines.Insert(0, $"Característica: {titulo}");

        return string.Join(Environment.NewLine, lines);
    }

    private static string AsegurarTagsEjecucion(string contenido, string filterTag)
        => AsegurarFeatureUnicoYTags(contenido, filterTag, "Borrador");

    private static string IdDesdeNombre(string nombre)
    {
        var baseName = Path.GetFileNameWithoutExtension(nombre);
        var slug = Regex.Replace(baseName, @"[^\w\-]+", "_");
        return "prueba-" + slug;
    }

    private static string? ExtraerTituloFeature(string contenido)
    {
        var m = Regex.Match(contenido, @"(?im)^(Feature|Característica):\s*(.+)$");
        if (!m.Success) return null;
        return AcortarTitulo(LimpiarTituloFeature(m.Groups[1].Value), 70);
    }

    private static string LimpiarTituloFeature(string? titulo)
    {
        var t = Regex.Replace((titulo ?? "").Trim(), @"\s+", " ");
        t = Regex.Replace(t, @"^Prueba\s*[—\-–:]?\s*", "", RegexOptions.IgnoreCase).Trim();
        t = Regex.Replace(t, @"\s*\[[^\]]+\]\s*$", "").Trim();
        return t;
    }

    private static string? ExtraerResumenFeature(string contenido)
    {
        var m = Regex.Match(contenido, @"(?im)^\s*#\s*Resumen:\s*(.+)$");
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }

    private static string GenerarResumenDesdeContenido(string contenido, string? titulo)
    {
        var lower = (contenido + "\n" + (titulo ?? "")).ToLowerInvariant();
        if (ContieneAlguno(lower, "otros ingresos", "ingreso de dinero"))
        {
            var moneda = ContieneAlguno(lower, "u$s", "usd", "dolar", "dólar")
                ? EtiquetasCajaUi.NormalUsd
                : ContieneAlguno(lower, "euro", "eur", "€")
                    ? EtiquetasCajaUi.NormalEuro
                    : EtiquetasCajaUi.NormalPesos + " (o U$S)";
            var importe = ExtraerImportePedido(contenido + "\n" + (titulo ?? ""));
            var parteImporte = importe is > 0
                ? $"importe {FormatearImportePaso(importe.Value)}"
                : "importe (aleatorio si no se indicó)";
            return $"Otros Ingresos: navega, selecciona caja «{moneda}», crea ingreso con causa y {parteImporte}, y procesa.";
        }

        var pasos = contenido.Replace("\r\n", "\n").Split('\n')
            .Select(l => l.Trim())
            .Where(l => Regex.IsMatch(l, @"^(Given|When|Then|And|Dado|Cuando|Entonces|Y)\s", RegexOptions.IgnoreCase))
            .Select(l => Regex.Replace(l, @"^(Given|When|Then|And|Dado|Cuando|Entonces|Y)\s+", "", RegexOptions.IgnoreCase))
            .Where(l => !l.Contains("abre la aplicacion", StringComparison.OrdinalIgnoreCase)
                        && !l.Contains("usuario configurado", StringComparison.OrdinalIgnoreCase)
                        && !l.Contains("contraseña", StringComparison.OrdinalIgnoreCase)
                        && !l.Contains("sucursal", StringComparison.OrdinalIgnoreCase)
                        && !l.Contains("bienvenida", StringComparison.OrdinalIgnoreCase))
            .Take(5)
            .ToList();

        if (pasos.Count > 0)
            return "Flujo: " + string.Join(" → ", pasos) + ".";

        return "Caso generado con asistente (informe + capturas).";
    }

    private static string AplicarResumenAlFeature(string contenido, string resumen)
    {
        resumen = Regex.Replace((resumen ?? "").Trim(), @"\s+", " ");
        if (string.IsNullOrWhiteSpace(resumen))
            return contenido;

        var lines = contenido.Replace("\r\n", "\n").Split('\n').ToList();
        var resumenIdx = lines.FindIndex(l => Regex.IsMatch(l, @"(?i)^\s*#\s*Resumen:"));
        var linea = "  # Resumen: " + resumen;
        if (resumenIdx >= 0)
        {
            lines[resumenIdx] = linea;
        }
        else
        {
            var featureIdx = lines.FindIndex(l => Regex.IsMatch(l, @"(?i)^(Feature|Característica):\s*"));
            if (featureIdx >= 0)
                lines.Insert(featureIdx + 1, linea);
            else
                lines.Insert(0, linea);
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string? ExtraerPrimerTag(string contenido)
    {
        var m = Regex.Match(contenido, @"(?m)^(@[\w\-]+(?:\s+@[\w\-]+)*)\s*$");
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }

    private sealed record BorradorInfo(
        string Id,
        string Nombre,
        string Titulo,
        string Tag,
        string FilterTag,
        string Corto,
        string Descripcion,
        string RutaRelativa,
        DateTime ModificadoUtc);

    private static async Task<object> AnalizarAsync(
        string corpus,
        List<FuenteTexto> fuentes,
        string tituloSugerido,
        string jiraUrl,
        List<StepInfo> stepsCatalogo,
        List<string> avisos,
        string? moduloSugerido = null,
        string? jiraTitulo = null,
        string? jiraTipo = null,
        string? jiraDetalle = null,
        string? ejemplosTexto = null,
        bool usarLlm = false,
        string? llmApiKey = null,
        string? llmBaseUrl = null,
        string? llmModel = null,
        string? automatizacionRoot = null,
        string? pruebasFeatures = null,
        string? grupoNombreHint = null,
        string? grupoIdHint = null)
    {
        var detalleUsuario = fuentes
            .FirstOrDefault(f => f.Origen.Equals("texto_manual", StringComparison.OrdinalIgnoreCase))
            ?.Texto?.Trim() ?? "";

        var gherkinArchivos = fuentes
            .Where(f => f.Origen.StartsWith("archivo:", StringComparison.OrdinalIgnoreCase))
            .Select(f => new
            {
                Nombre = f.Origen.Length > 8 ? f.Origen[8..] : "",
                Texto = f.Texto?.Trim() ?? ""
            })
            .Where(x => x.Nombre.EndsWith(".feature", StringComparison.OrdinalIgnoreCase) && EsGherkinListo(x.Texto))
            .Select(x => x.Texto)
            .ToList();
        var gherkinDesdeArchivo = gherkinArchivos.Count > 0
            ? string.Join("\n\n---\n\n", gherkinArchivos)
            : null;

        // Gherkin listo en Detalle o en .feature subido: no regenerar con mapa/LLM.
        if (EsGherkinListo(detalleUsuario) || gherkinDesdeArchivo is not null)
        {
            var gherkin = EsGherkinListo(detalleUsuario) ? detalleUsuario : gherkinDesdeArchivo!;
            if (gherkinDesdeArchivo is not null)
                avisos.Add("Gherkin listo en archivo .feature: se respeta sin regenerar.");
            var respuestaGherkin = ConstruirRespuestaGherkinListo(
                gherkin, avisos, detalleUsuario, moduloSugerido, jiraTipo);
            if (respuestaGherkin is not null)
                return respuestaGherkin;
        }

        // Delegamos la lógica síncrona y luego opcionalmente mejoramos con LLM.
        var baseResult = AnalizarCore(
            corpus, fuentes, tituloSugerido, jiraUrl, stepsCatalogo, avisos,
            moduloSugerido, jiraTitulo, jiraTipo, jiraDetalle,
            automatizacionRoot, pruebasFeatures, grupoNombreHint, grupoIdHint);

        // Si el operador pegó Gherkin listo (de Cursor u otro), no regenerar: respetar y ofrecer importar lote.
        if (EsGherkinListo(detalleUsuario) || EsGherkinListo(baseResult.FeatureBorrador))
        {
            var gherkin = EsGherkinListo(detalleUsuario) ? detalleUsuario : baseResult.FeatureBorrador;
            var respuestaPost = ConstruirRespuestaGherkinListo(
                gherkin, baseResult.Avisos, detalleUsuario, baseResult.ModuloSugerido, baseResult.TipoIssue, baseResult);
            if (respuestaPost is not null)
                return respuestaPost;
        }

        if (!string.IsNullOrWhiteSpace(llmApiKey) && baseResult.PuedeGenerar
            && detalleUsuario.Length < 40)
        {
            var corpusSafe = RunnerSecurity.RedactarParaLlm(corpus);
            var det = await AprendizajeLlm.RefinarDetalleCasoAsync(
                baseResult.TituloTicket ?? "",
                corpusSafe,
                baseResult.TipoIssue,
                llmApiKey,
                llmBaseUrl,
                llmModel);
            if (!string.IsNullOrWhiteSpace(det.Aviso))
                baseResult.Avisos.Add(det.Aviso);
            if (det.Ok && !string.IsNullOrWhiteSpace(det.Detalle))
            {
                baseResult.DetalleSugerido = det.Detalle;
                baseResult.DescripcionCaso = det.Detalle;
                baseResult.Hallazgos.Add("Detalle del caso alineado a la intención del ticket (texto + capturas interpretadas).");
            }
        }
        else if (detalleUsuario.Length >= 40)
        {
            baseResult.DetalleSugerido = Truncar(detalleUsuario, 700);
            baseResult.DescripcionCaso = Truncar(detalleUsuario, 700);
            baseResult.Hallazgos.Add("Se respeta el Detalle escrito por el operador; la IA lo mapea a pantallas/steps del mapa.");
        }

        // Con detalle en lenguaje natural: pulir Gherkin con mapa siempre que haya API key (aunque no marquen «usar LLM»).
        // Si el Detalle trae instrucciones explícitas, el LLM SOLO puede alinear frases — no cambiar el orden/circuito.
        var debeMapearConLlm = baseResult.PuedeGenerar && !string.IsNullOrWhiteSpace(llmApiKey)
            && (usarLlm || detalleUsuario.Length >= 25);
        if (debeMapearConLlm)
        {
            var corpusSafe = RunnerSecurity.RedactarParaLlm(corpus);
            var mapaParaLlm = "";
            var idxMapa = corpus.IndexOf("[mapa_automatizacion]", StringComparison.OrdinalIgnoreCase);
            if (idxMapa >= 0)
            {
                var fin = corpus.IndexOf("\n\n---\n\n", idxMapa, StringComparison.Ordinal);
                mapaParaLlm = fin > idxMapa ? corpus[idxMapa..fin] : corpus[idxMapa..];
                mapaParaLlm = RunnerSecurity.RedactarParaLlm(mapaParaLlm);
            }
            var llm = await AprendizajeLlm.MejorarConLlmOpcionalAsync(
                corpusSafe,
                baseResult.FeatureBorrador,
                ejemplosTexto ?? "",
                baseResult.StepsReutilizables,
                llmApiKey,
                llmBaseUrl,
                llmModel,
                usarLlm: true,
                mapaAutomatizacion: mapaParaLlm,
                detalleOperador: detalleUsuario);
            if (!string.IsNullOrWhiteSpace(llm.Aviso))
                baseResult.Avisos.Add(llm.Aviso);
            if (llm.Ok)
            {
                // Con instrucciones explícitas: solo aceptar el LLM si conserva la cantidad/orden razonable de pasos del borrador.
                if (detalleUsuario.Length >= 25
                    && !FeatureRespetaEspirituDelBorrador(baseResult.FeatureBorrador, llm.FeatureMejorado))
                {
                    baseResult.Avisos.Add(
                        "LLM omitido: proponía otro circuito; se mantiene la interpretación del Detalle.");
                }
                else
                {
                    baseResult.FeatureBorrador = SanitizarEscenarioFeature(llm.FeatureMejorado);
                    baseResult.Hallazgos.Add(detalleUsuario.Length >= 25
                        ? "Borrador alineado al Detalle del operador (interpretado como está escrito)."
                        : "Borrador mapeado desde el Detalle (lenguaje natural) al circuito de pantallas del mapa.");
                }
            }
        }

        return Respuesta(
            baseResult.Veredicto,
            baseResult.PuedeGenerar,
            baseResult.Faltantes,
            baseResult.Riesgos,
            baseResult.Hallazgos,
            baseResult.Avisos,
            baseResult.StepsReutilizables,
            baseResult.FeatureBorrador,
            baseResult.Resumen,
            baseResult.DescripcionCaso,
            baseResult.ModuloSugerido,
            baseResult.TituloTicket,
            baseResult.DetalleSugerido,
            baseResult.TipoIssue);
    }

    private sealed class AnalisisCoreResult
    {
        public string Veredicto { get; set; } = "";
        public bool PuedeGenerar { get; set; }
        public List<string> Faltantes { get; set; } = [];
        public List<string> Riesgos { get; set; } = [];
        public List<string> Hallazgos { get; set; } = [];
        public List<string> Avisos { get; set; } = [];
        public List<string> StepsReutilizables { get; set; } = [];
        public string FeatureBorrador { get; set; } = "";
        public string Resumen { get; set; } = "";
        public string DescripcionCaso { get; set; } = "";
        public string? ModuloSugerido { get; set; }
        public string? TituloTicket { get; set; }
        public string? DetalleSugerido { get; set; }
        public string? TipoIssue { get; set; }
    }

    private static AnalisisCoreResult AnalizarCore(
        string corpus,
        List<FuenteTexto> fuentes,
        string tituloSugerido,
        string jiraUrl,
        List<StepInfo> stepsCatalogo,
        List<string> avisos,
        string? moduloSugerido = null,
        string? jiraTitulo = null,
        string? jiraTipo = null,
        string? jiraDetalle = null,
        string? automatizacionRoot = null,
        string? pruebasFeatures = null,
        string? grupoNombreHint = null,
        string? grupoIdHint = null)
    {
        var faltantes = new List<string>();
        var riesgos = new List<string>();
        var hallazgos = new List<string>();

        var detalleUsuario = fuentes
            .FirstOrDefault(f => f.Origen.Equals("texto_manual", StringComparison.OrdinalIgnoreCase))
            ?.Texto?.Trim() ?? "";
        var hayIntencionDetalle = detalleUsuario.Length >= 25
            || (!string.IsNullOrWhiteSpace(grupoNombreHint) && grupoNombreHint.Length > 2)
            || (!string.IsNullOrWhiteSpace(grupoIdHint)
                && !grupoIdHint.StartsWith("__", StringComparison.Ordinal));

        if (fuentes.Count == 0)
        {
            faltantes.Add("Escribí en Detalle qué querés probar (lenguaje natural), o pegá Jira / documentación.");
            return new AnalisisCoreResult
            {
                Veredicto = "falta_info",
                PuedeGenerar = false,
                Faltantes = faltantes,
                Riesgos = riesgos,
                Hallazgos = hallazgos,
                Avisos = avisos,
                FeatureBorrador = FeatureIncompleto("Sin título", faltantes, jiraUrl),
                Resumen = "Sin fuentes de información.",
                ModuloSugerido = moduloSugerido
            };
        }

        var texto = corpus;
        var ticket = ExtraerTicketKey(jiraUrl) ?? ExtraerTicketKey(texto);
        var hayJira = fuentes.Any(f => f.Origen.StartsWith("jira:", StringComparison.OrdinalIgnoreCase));
        var tipoNorm = (jiraTipo ?? "").Trim();

        var tituloRaw = !string.IsNullOrWhiteSpace(tituloSugerido)
            ? tituloSugerido
            : (!string.IsNullOrWhiteSpace(jiraTitulo)
                ? jiraTitulo!
                : InferirTitulo(texto, ticket));
        var titulo = ArmarTituloCorto(tituloRaw, ticket, 90);

        if (string.IsNullOrWhiteSpace(moduloSugerido))
            moduloSugerido = InferirModuloDesdeTexto(titulo, texto, ticket);
        if (!string.IsNullOrWhiteSpace(grupoNombreHint)
            && (string.IsNullOrWhiteSpace(moduloSugerido)
                || moduloSugerido.StartsWith("Ticket ", StringComparison.Ordinal)
                || moduloSugerido.Equals("Desde Jira", StringComparison.OrdinalIgnoreCase)
                || moduloSugerido.Equals("Desde Jira", StringComparison.OrdinalIgnoreCase)))
            moduloSugerido = grupoNombreHint;

        var detalleSugerido = detalleUsuario.Length >= 25
            ? Truncar(detalleUsuario, 700)
            : (!string.IsNullOrWhiteSpace(jiraDetalle)
                ? jiraDetalle!
                : GenerarDetalleDesdeFuentes(titulo, texto, ticket, tipoNorm));

        List<string>? circuitoMapa = null;
        if (!string.IsNullOrWhiteSpace(automatizacionRoot) && !string.IsNullOrWhiteSpace(pruebasFeatures))
        {
            var res = MapaAutomatizacion.ResolverCircuitoPorIntencion(
                automatizacionRoot, pruebasFeatures,
                detalleUsuario + "\n" + titulo + "\n" + (grupoNombreHint ?? ""),
                grupoNombreHint ?? grupoIdHint ?? moduloSugerido);
            if (res.Circuito.Count > 0)
            {
                circuitoMapa = res.Circuito;
                if (!string.IsNullOrWhiteSpace(res.Nombre))
                    moduloSugerido = res.Nombre;
                hallazgos.Add($"Mapa: intención → módulo «{res.Nombre}» ({res.Circuito.Count} pasos de circuito).");
            }
        }

        var tieneObjetivo = ContieneAlguno(texto,
            "objetivo", "debe", "verificar", "validar", "scenario", "caso", "flujo", "cuando", "then", "resultado",
            "bug", "falla", "error", "incorrecto", "no funciona", "reproduc", "mejora", "historia", "como usuario",
            "quiero", "necesito", "probar", "hacer", "generar", "cerrar", "abrir", "pase");
        var tienePasos = ContieneAlguno(texto,
            "paso", "when ", "given ", "then ", "1)", "2)", "1.", "2.", "navega", "ingresa", "selecciona", "confirma",
            "reproduc", "pasos para", "steps to", "criterio");
        var tieneResultado = ContieneAlguno(texto,
            "resultado", "esperado", "then ", "debe mostrar", "queda", "se muestra", "ok", "correctamente", "error",
            "actual", "obtenido", "observed", "aceptación", "aceptacion", "verificar", "validar", "montos");
        var tienePrecond = ContieneAlguno(texto,
            "precond", "prerequis", "dado que", "background", "usuario", "sucursal", "login", "caja abierta", "ambiente");
        var tieneDatos = ContieneAlguno(texto,
            "sucursal", "131", "usuario", "sot1", "moneda", "pesos", "usd", "correlativo", "importe", "monto");

        if (hayJira && !string.IsNullOrWhiteSpace(jiraTitulo ?? titulo))
        {
            tieneObjetivo = true;
            if (!tieneResultado)
                tieneResultado = true;
        }

        if (hayIntencionDetalle || circuitoMapa is { Count: > 0 })
        {
            tieneObjetivo = true;
            tienePasos = true;
            tieneResultado = true;
            hallazgos.Add("Modo Detalle: se escribe en claro qué se quiere hacer; RunnerIA mapea a pantallas/steps del mapa (sin exigir Jira).");
        }

        if (!tieneObjetivo)
            faltantes.Add("No se entiende el objetivo de la prueba (qué funcionalidad o pantalla validar).");
        if (!tienePasos)
        {
            if (hayJira || hayIntencionDetalle)
                riesgos.Add("Sugerencia: el texto no trae pasos explícitos. Se usa el circuito del mapa de pantallas.");
            else
                faltantes.Add("Faltan pasos accionables del flujo (navegación, clicks, carga de datos).");
        }
        if (!tieneResultado)
        {
            if (hayJira || hayIntencionDetalle)
                riesgos.Add("Sugerencia: no hay resultado esperado explícito; se toma del Detalle / título y del circuito del mapa.");
            else
                faltantes.Add("Falta el resultado esperado verificable (qué debe verse o quedar registrado).");
        }
        if (!tienePrecond)
            riesgos.Add("Sugerencia: no hay precondiciones claras (usuario, sucursal, estado de caja). Se usa el Background de login estándar; conviene completarlas.");
        if (!tieneDatos)
            riesgos.Add("Sugerencia: pocos datos de prueba (sucursal, moneda, importes, supervisión). Completalos si el caso lo necesita.");

        if (texto.Length < 80 && !(hayJira && !string.IsNullOrWhiteSpace(jiraTitulo)) && !hayIntencionDetalle)
            faltantes.Add("El texto es demasiado corto para armar un caso E2E confiable.");

        if (hayIntencionDetalle || circuitoMapa is { Count: > 0 })
        {
            faltantes.RemoveAll(f =>
                f.Contains("pasos accionables", StringComparison.OrdinalIgnoreCase)
                || f.Contains("resultado esperado", StringComparison.OrdinalIgnoreCase)
                || f.Contains("demasiado corto", StringComparison.OrdinalIgnoreCase)
                || f.Contains("objetivo de la prueba", StringComparison.OrdinalIgnoreCase));
        }
        var stepsReutilizables = BuscarStepsRelacionados(texto + "\n" + titulo, stepsCatalogo);
        if (stepsReutilizables.Count > 0)
            hallazgos.Add($"Se reutilizarían {stepsReutilizables.Count} step(s) ya existentes en el proyecto.");
        else
            riesgos.Add("Sugerencia: no se detectaron steps existentes claramente reutilizables. El borrador usa frases nuevas; luego se pueden alinear al catálogo del proyecto.");

        if (PlantillaPasosPorDominio(texto + "\n" + titulo) is not null || circuitoMapa is { Count: > 0 })
        {
            hallazgos.Add(
                "Circuito de dominio / mapa aplicado. Etiquetas UI de caja: "
                + $"«{EtiquetasCajaUi.NormalPesos}», «{EtiquetasCajaUi.NormalUsd}», «{EtiquetasCajaUi.NormalEuro}», «{EtiquetasCajaUi.Miniboveda}».");
        }

        if (Regex.IsMatch(texto, @"\b(seg[uú]n\s+corresponda|a\s+criterio|etc\.|\.\.\.)\b", RegexOptions.IgnoreCase)
            || texto.Contains("…", StringComparison.Ordinal))
            riesgos.Add("Sugerencia: hay ambigüedades ('según corresponda', 'etc.'). Conviene explicitarlas antes de automatizar en serio.");

        var escenariosDetectados = ExtraerEscenariosDesdeTexto(
            detalleUsuario.Length >= 25 ? detalleUsuario : texto);
        if (escenariosDetectados.Count > 1)
            hallazgos.Add($"Se detectaron {escenariosDetectados.Count} escenarios/casos en el ticket o la documentación.");

        if (hayJira)
        {
            var tipoMsg = string.IsNullOrWhiteSpace(tipoNorm) ? "ticket Jira" : $"ticket Jira tipo «{tipoNorm}»";
            hallazgos.Add($"{Capitalizar(tipoMsg)}: mismo flujo de generación (título + detalle + escenarios). Bug, historia, mejora o task se tratan igual.");
        }
        hallazgos.Add("Al Ejecutar: cada paso de UI genera captura PNG + informe Extent (Hooks), igual que la suite SOT.");

        // Adjuntos no legibles → sugerencia clara (también vienen en avisos de Jira).
        foreach (var a in avisos.Where(x => x.Contains("Sugerencia:", StringComparison.OrdinalIgnoreCase)))
            hallazgos.Add(a);

        string veredicto;
        bool puedeGenerar;
        if (hayJira && !string.IsNullOrWhiteSpace(jiraTitulo ?? titulo) && faltantes.Count == 0)
        {
            veredicto = riesgos.Count > 2 || !tienePasos ? "parcial" : "crear";
            puedeGenerar = true;
        }
        else if (hayIntencionDetalle || circuitoMapa is { Count: > 0 })
        {
            veredicto = "crear";
            puedeGenerar = true;
            faltantes.Clear();
        }
        else if (faltantes.Count >= 2 || (!tienePasos && !tieneResultado && !hayJira))
        {
            veredicto = "falta_info";
            puedeGenerar = false;
        }
        else if (faltantes.Count > 0 || riesgos.Count > 2 || (hayJira && !tienePasos))
        {
            veredicto = "parcial";
            puedeGenerar = true;
        }
        else
        {
            veredicto = "crear";
            puedeGenerar = true;
        }

        // Último recurso: si hay Jira y título, no devolver "FALTA INFORMACIÓN" (sin importar el tipo).
        if (!puedeGenerar && hayJira && !string.IsNullOrWhiteSpace(jiraTitulo ?? titulo))
        {
            puedeGenerar = true;
            veredicto = "parcial";
            riesgos.Add("Sugerencia: borrador desde ticket Jira listo para revisar; completá pasos si hace falta antes de automatizar en serio.");
            faltantes.Clear();
        }

        var feature = puedeGenerar
            ? GenerarFeature(
                titulo, ticket, jiraUrl, texto, stepsReutilizables, faltantes, riesgos,
                escenariosDetectados, tipoNorm, circuitoMapa, detalleUsuario)
            : FeatureIncompleto(titulo, faltantes, jiraUrl);

        var resumenAnalisis = veredicto switch
        {
            "crear" => hayIntencionDetalle
                ? "Detalle interpretado tal como lo escribiste (sin formato especial)."
                : "Hay información suficiente para un borrador revisable (estilo SpecFlow del proyecto).",
            "parcial" => hayJira
                ? $"Ticket Jira{(string.IsNullOrWhiteSpace(tipoNorm) ? "" : $" ({tipoNorm})")}: borrador E2E listo para revisar (título + detalle). Completá pasos si hace falta y guardá."
                : "Se puede generar un borrador parcial; revisá faltantes y riesgos antes de usarlo.",
            _ => "Falta información: escribí el Detalle en claro, o completá Jira/documentación."
        };
        var descripcionCaso = puedeGenerar
            ? detalleSugerido
            : "Pendiente: completar información para armar el caso.";

        return new AnalisisCoreResult
        {
            Veredicto = veredicto,
            PuedeGenerar = puedeGenerar,
            Faltantes = faltantes,
            Riesgos = riesgos,
            Hallazgos = hallazgos,
            Avisos = avisos,
            StepsReutilizables = stepsReutilizables,
            FeatureBorrador = feature,
            Resumen = resumenAnalisis,
            DescripcionCaso = descripcionCaso,
            ModuloSugerido = moduloSugerido,
            TituloTicket = titulo,
            DetalleSugerido = detalleSugerido,
            TipoIssue = tipoNorm
        };
    }

    private static object Respuesta(
        string veredicto,
        bool puedeGenerar,
        List<string> faltantes,
        List<string> riesgos,
        List<string> hallazgos,
        List<string> avisos,
        List<string> stepsReutilizables,
        string featureBorrador,
        string resumen,
        string? descripcionCaso = null,
        string? moduloSugerido = null,
        string? tituloTicket = null,
        string? detalleSugerido = null,
        string? tipoIssue = null) => new
    {
        ok = true,
        iaAlcance = "runner-producto",
        iaAlcanceTexto =
            "IA limitada al Runner/producto: solo Analizar/Generar borradores E2E en Features/_pruebas. " +
            "No es un asistente general ni ejecuta comandos libres.",
        veredicto,
        puedeGenerar,
        resumen,
        descripcionCaso = descripcionCaso ?? "",
        moduloSugerido = moduloSugerido ?? "",
        tituloTicket = tituloTicket ?? "",
        detalleSugerido = detalleSugerido ?? descripcionCaso ?? "",
        tipoIssue = tipoIssue ?? "",
        faltantes,
        riesgos,
        hallazgos,
        avisos,
        stepsReutilizables,
        featureBorrador,
        manualCorto =
            "RunnerIA facilita casos E2E para QA/automatización del proyecto activo. " +
            "No reemplaza desarrollo de la app ni ejecuta acciones fuera del sandbox Features/_pruebas. " +
            "Si falta información, lo indica. Revisá el borrador antes de guardarlo. " +
            "Cada paso de UI se captura en el informe Extent (como en la suite SOT)."
    };

    private static string GenerarDetalleDesdeFuentes(string titulo, string texto, string? ticket, string? tipoIssue)
    {
        var raw = (texto ?? "").Replace("\r\n", "\n");
        var partes = new List<string>();

        // Bloque Descripción completo (no cortar en el primer párrafo vacío corto).
        var mDesc = Regex.Match(raw,
            @"(?is)Descripci[oó]n:\s*(.+?)(?=\n(?:--- Adjunto:|--- Imagen|Comentarios funcionales:|Adjunto imagen)|$)");
        var cuerpoDesc = mDesc.Success ? LimpiarBloqueDetalle(mDesc.Groups[1].Value) : "";

        var objetivo = ExtraerSeccionIntencion(cuerpoDesc, raw,
            "objetivo", "que se quiere", "qué se quiere", "se debe", "debe ", "verificar", "validar", "como usuario", "necesito");
        var esperado = ExtraerSeccionIntencion(cuerpoDesc, raw,
            "resultado esperado", "expected", "debe mostrar", "debe quedar", "esperado", "entonces ");
        var actual = ExtraerSeccionIntencion(cuerpoDesc, raw,
            "resultado actual", "actual", "obtenido", "observed", "falla", "error", "incorrecto");
        var pasos = ExtraerSeccionIntencion(cuerpoDesc, raw,
            "pasos para reproducir", "pasos", "reproducir", "steps to reproduce");

        var imagenes = new List<string>();
        foreach (Match mi in Regex.Matches(raw, @"(?is)--- Imagen interpretada:\s*([^\n]+)\s*---\s*(.+?)(?=\n--- |\nComentarios funcionales:|$)"))
        {
            var descImg = LimpiarBloqueDetalle(mi.Groups[2].Value);
            if (descImg.Length > 30)
                imagenes.Add(Truncar(descImg, 220));
        }

        if (!string.IsNullOrWhiteSpace(objetivo))
            partes.Add(objetivo);
        else if (!string.IsNullOrWhiteSpace(cuerpoDesc))
            partes.Add(Truncar(cuerpoDesc, 420));

        if (!string.IsNullOrWhiteSpace(esperado))
            partes.Add("Resultado esperado: " + Truncar(esperado, 220));
        if (!string.IsNullOrWhiteSpace(actual))
            partes.Add("Condición actual: " + Truncar(actual, 180));
        if (!string.IsNullOrWhiteSpace(pasos) && partes.Count < 3)
            partes.Add("Flujo: " + Truncar(pasos, 200));
        if (imagenes.Count > 0)
            partes.Add("Evidencia en pantallas: " + Truncar(string.Join(" · ", imagenes.Take(2)), 280));

        // Comentarios funcionales útiles
        var mCom = Regex.Match(raw, @"(?is)Comentarios funcionales:\s*(.+)$");
        if (mCom.Success && partes.Count < 2)
        {
            var com = LimpiarBloqueDetalle(mCom.Groups[1].Value);
            if (com.Length > 40) partes.Add(Truncar(com, 220));
        }

        var detalle = string.Join(" ", partes.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct());
        detalle = Regex.Replace(detalle, @"\s+", " ").Trim();

        // Evitar eco vacío del título
        var tituloLimpio = QuitarTicketDelTitulo(titulo ?? "", ticket);
        if (string.IsNullOrWhiteSpace(detalle) ||
            string.Equals(detalle, tituloLimpio, StringComparison.OrdinalIgnoreCase) ||
            detalle.Length < 40)
        {
            detalle = string.IsNullOrWhiteSpace(tituloLimpio)
                ? "Caso E2E generado desde fuentes del asistente. Completar qué se quiere verificar."
                : $"Verificar / cubrir: {tituloLimpio}. Revisar descripción del ticket, criterios y capturas asociadas.";
        }

        var tipo = (tipoIssue ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(tipo) &&
            !detalle.StartsWith(tipo, StringComparison.OrdinalIgnoreCase))
            detalle = $"{tipo}: {detalle}";

        return Truncar(detalle, 700);
    }

    private static string LimpiarBloqueDetalle(string s)
    {
        var t = Regex.Replace(s ?? "", @"\r\n|\r", "\n");
        t = Regex.Replace(t, @"\[imagen adjunta[^\]]*\]", " ", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"\n{3,}", "\n\n");
        t = Regex.Replace(t, @"[ \t]+", " ");
        return t.Trim();
    }

    private static string ExtraerSeccionIntencion(string cuerpoDesc, string raw, params string[] claves)
    {
        var blob = string.IsNullOrWhiteSpace(cuerpoDesc) ? raw : cuerpoDesc;
        foreach (var clave in claves)
        {
            var m = Regex.Match(blob,
                $@"(?im)(?:^|\n)\s*(?:[#*\-]+\s*)?{Regex.Escape(clave)}\s*[:\-–—]\s*(.+?)(?=\n\s*(?:[#*\-]|\d+[\).]|[A-ZÁÉÍÓÚ][^\n]{{0,40}}:\s)|\n\n|$)");
            if (m.Success)
            {
                var v = LimpiarBloqueDetalle(m.Groups[1].Value);
                if (v.Length >= 20) return v;
            }
        }

        // Frases con verbos de intención en el cuerpo
        foreach (var line in blob.Replace("\r\n", "\n").Split('\n'))
        {
            var l = line.Trim().TrimStart('-', '*', '•', ' ');
            if (l.Length < 25 || l.Length > 280) continue;
            var lower = l.ToLowerInvariant();
            if (claves.Any(c => lower.Contains(c, StringComparison.OrdinalIgnoreCase)))
                return l;
        }
        return "";
    }

    private static string GenerarFeature(
        string titulo,
        string? ticket,
        string jiraUrl,
        string texto,
        List<string> stepsReutilizables,
        List<string> faltantes,
        List<string> riesgos,
        List<(string Nombre, string Bloque)>? escenariosDetectados = null,
        string? tipoIssue = null,
        List<string>? circuitoMapa = null,
        string? detalleUsuario = null)
    {
        var tipoNorm = (tipoIssue ?? "").Trim();
        var tipoTag = SanitizarTagTipoIssue(tipoNorm);
        var detalleOp = (detalleUsuario ?? "").Trim();
        // Cualquier Detalle con sustancia manda: se interpreta como esté escrito (sin formato especial).
        var hayDetalleOperador = detalleOp.Length >= 25;
        var sb = new StringBuilder();
        // El tag @PruebaRun_… definitivo se fija al Guardar (según nombre de archivo).
        sb.AppendLine("@Prueba @PruebaRun_Borrador");
        if (!string.IsNullOrWhiteSpace(tipoTag))
            sb.AppendLine("@" + tipoTag);
        sb.AppendLine("# language: es");
        sb.AppendLine($"Característica: {ArmarTituloCorto(titulo, ticket, 55)}");
        sb.AppendLine($"  # Resumen: {GenerarResumenDesdeContenido(titulo + "\n" + (detalleOp.Length > 0 ? detalleOp : texto), titulo)}");
        sb.AppendLine();
        sb.AppendLine("  # EVIDENCIA SOT: al Ejecutar, Hooks captura PNG de cada paso + informe Extent");
        sb.AppendLine("  # (misma mecánica que la suite). Preferí un paso Cuando/Entonces por acción visible en pantalla.");
        sb.AppendLine("  # Captura: sí si hay UI tras el paso; no si falló antes / sin browser / PNG duplicado (si está activo omitir).");
        sb.AppendLine(hayDetalleOperador
            ? "  # Origen: Detalle del operador interpretado (como esté escrito; plantilla/mapa no lo reemplazan)."
            : "  # Origen: intención inferida (Jira/docs/mapa) — conviene completar el Detalle.");
        if (!string.IsNullOrWhiteSpace(jiraUrl))
            sb.AppendLine($"  # Origen Jira: {jiraUrl.Trim()}");
        if (!string.IsNullOrWhiteSpace(ticket))
            sb.AppendLine($"  # Ticket: {ticket}");
        if (!string.IsNullOrWhiteSpace(tipoNorm))
            sb.AppendLine($"  # Tipo Jira: {tipoNorm} — mismo flujo de generación para todos los tipos");
        if (faltantes.Count > 0)
        {
            sb.AppendLine("  # Pendiente de completar:");
            foreach (var f in faltantes)
                sb.AppendLine("  #  - " + f);
        }
        if (riesgos.Count > 0)
        {
            sb.AppendLine("  # Sugerencias (no bloquean generar):");
            foreach (var r in riesgos.Take(5))
                sb.AppendLine("  #  - " + r);
        }
        sb.AppendLine();
        sb.AppendLine(BackgroundLogin.TrimEnd());
        sb.AppendLine();

        var escenarios = escenariosDetectados is { Count: > 0 }
            ? escenariosDetectados
            : [("Escenario generado", detalleOp.Length >= 25 ? detalleOp : (texto ?? ""))];

        for (var i = 0; i < escenarios.Count; i++)
        {
            var (nombreEsc, bloque) = escenarios[i];
            // Para plantillas/dominio: preferir Detalle del operador (evita que Jira/mapa sesguen a Intercaja u otro).
            var textoPasos = PreferirTextoInstrucciones(detalleOp, bloque, texto);
            var contextoDominio = (titulo ?? "") + "\n" + textoPasos;
            var pasos = ResolverPasosDelEscenario(
                textoPasos,
                contextoDominio,
                titulo,
                stepsReutilizables,
                i == 0 ? circuitoMapa : null,
                hayDetalleOperador,
                riesgos);

            pasos = AsegurarOrdenBasicoOtrosIngresos(pasos, contextoDominio, hayDetalleOperador);
            pasos = AsegurarThenDesdeResultadoEsperado(pasos, contextoDominio);

            var scenarioName = escenarios.Count == 1 && nombreEsc.Contains("generado", StringComparison.OrdinalIgnoreCase)
                ? ArmarTituloCorto(titulo, ticket, 48)
                : ArmarTituloCorto(nombreEsc, ticket, 48);

            if (i > 0) sb.AppendLine();
            sb.AppendLine($"  Escenario: {scenarioName}");
            foreach (var p in pasos)
            {
                var line = NormalizarLineaPaso(p);
                if (string.IsNullOrWhiteSpace(line) || !EsPasoPruebaSeguro(line))
                    continue;
                sb.AppendLine("    " + line);
            }
        }

        var borrador = sb.ToString().TrimEnd() + "\n";
        return SanitizarEscenarioFeature(borrador);
    }

    /// <summary>
    /// Elige la fuente de pasos: Detalle del operador primero (si tiene sustancia).
    /// </summary>
    private static string PreferirTextoInstrucciones(string detalleOp, string? bloque, string? textoCorpus)
    {
        if (!string.IsNullOrWhiteSpace(detalleOp) && detalleOp.Trim().Length >= 25)
            return detalleOp.Trim();
        if (!string.IsNullOrWhiteSpace(bloque) && bloque.Trim().Length >= 25)
            return bloque.Trim();
        // Evitar el corpus completo (mapa/Jira): recortar ruido típico.
        var t = (textoCorpus ?? "").Trim();
        t = Regex.Replace(t, @"(?is)\[mapa_automatizacion\][\s\S]*?(?=\n---|\z)", " ");
        t = Regex.Replace(t, @"(?is)\[aprendizaje\][\s\S]*?(?=\n---|\z)", " ");
        return t.Trim();
    }

    /// <summary>
    /// True si el operador escribió pasos/orden claro (lista, Given/When, primero/luego, varias acciones).
    /// En ese caso NO se debe reemplazar por una plantilla genérica de otro circuito.
    /// </summary>
    private static bool TieneInstruccionesExplicitas(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return false;
        var t = texto.Trim();
        if (t.Length < 20) return false;

        if (Regex.IsMatch(t, @"(?im)^\s*(Given|When|Then|And)\s+\S+"))
            return true;

        var itemsLista = Regex.Matches(t, @"(?im)^\s*(?:\d+[\).\:-]|[-*•])\s+\S+");
        if (itemsLista.Count >= 2)
            return true;

        var lower = t.ToLowerInvariant();
        var tieneSecuencia = ContieneAlguno(lower,
            "primero", "luego", "después", "despues", "a continuación", "a continuacion",
            "finalmente", "seguido", "y después", "y despues", "y luego", "paso 1", "paso 2");
        var tieneAccion = ContieneAlguno(lower,
            "navega", "ingresa", "selecciona", "confirma", "verifica", "valida", "abrir", "abre",
            "cierra", "consulta", "ejecuta", "busca", "carga", "genera", "registra", "acepta",
            "anula", "ir a", "voy a", "probar", "verificar", "hacer click", "hace click");
        if (tieneSecuencia && tieneAccion)
            return true;

        var lineasAccion = t.Replace("\r\n", "\n").Split('\n')
            .Select(l => Regex.Replace(l, @"^\s*(?:\d+[\).\:-]|[-*•])\s+", "").Trim())
            .Where(l => l.Length >= 10)
            .Count(l => Regex.IsMatch(l,
                @"(?i)\b(navega|ingresa|selecciona|confirma|verifica|valida|abre|abrir|cierra|consulta|ejecuta|busca|carga|genera|registra|acepta|anula|ir a|voy a|probar|verificar)\b"));
        if (lineasAccion >= 2)
            return true;

        var acciones = Regex.Matches(t,
            @"(?i)\b(navega|ingresa|selecciona|confirma|verifica|valida|abre|cierra|consulta|ejecuta|busca|carga|genera|registra)\b");
        return acciones.Count >= 3;
    }

    /// <summary>
    /// Interpreta el Detalle como esté escrito. Si hay Detalle del operador, manda sobre mapa/plantilla genérica.
    /// Plantilla solo como ayuda del MISMO dominio del texto (no otro circuito).
    /// </summary>
    private static List<string> ResolverPasosDelEscenario(
        string textoPasos,
        string contextoDominio,
        string? titulo,
        List<string> stepsReutilizables,
        List<string>? circuitoMapa,
        bool hayDetalleOperador,
        List<string> riesgos)
    {
        // 1) Con Detalle: siempre interpretar primero lo que escribió (cualquier redacción).
        if (hayDetalleOperador || textoPasos.Trim().Length >= 25)
        {
            var desdeDetalle = PasosDesdeLenguajeNatural(textoPasos + "\n" + (titulo ?? ""));
            // Si el párrafo es corto/intención, la plantilla del MISMO dominio completa el circuito
            // (ej. "probar falla de sobrante") — nunca un módulo ajeno.
            var plantillaMismoDominio = PlantillaPasosPorDominio(textoPasos + "\n" + (titulo ?? ""));
            if (desdeDetalle.Count >= 2)
            {
                riesgos.Add("Sugerencia: pasos interpretados desde el Detalle; al Guardar se crean bindings si faltan.");
                return AlinearFrasesConStepsExistentes(desdeDetalle, stepsReutilizables);
            }

            if (plantillaMismoDominio is { Count: > 0 })
            {
                // Mezcla: si NL sacó 1 paso útil, anteponerlo; si no, solo plantilla del dominio pedido.
                if (desdeDetalle.Count == 1
                    && !plantillaMismoDominio.Any(p =>
                        p.Contains(desdeDetalle[0].Split(' ', 2).LastOrDefault() ?? "___",
                            StringComparison.OrdinalIgnoreCase)))
                {
                    var mix = new List<string> { desdeDetalle[0] };
                    mix.AddRange(plantillaMismoDominio);
                    return AlinearFrasesConStepsExistentes(mix, stepsReutilizables);
                }

                riesgos.Add("Sugerencia: el Detalle se interpretó con la plantilla del dominio indicado (mismo circuito, no otro módulo).");
                return plantillaMismoDominio;
            }

            if (desdeDetalle.Count > 0)
            {
                riesgos.Add("Sugerencia: pasos interpretados desde el Detalle; al Guardar se crean bindings si faltan.");
                return AlinearFrasesConStepsExistentes(desdeDetalle, stepsReutilizables);
            }
        }

        // 2) Sin Detalle útil: plantilla / mapa / catálogo (flujo histórico).
        var plantilla = PlantillaPasosPorDominio(contextoDominio);
        if (plantilla is { Count: > 0 })
            return plantilla;

        if (circuitoMapa is { Count: > 0 })
            return circuitoMapa.ToList();

        if (stepsReutilizables.Count >= 1)
        {
            var pasos = FiltrarPasosCoherentesConDominio(
                    stepsReutilizables.Take(12).ToList(),
                    contextoDominio)
                .Where(EsPasoPruebaSeguro)
                .Select(NormalizarLineaPaso)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(8)
                .ToList();

            if (pasos.Count >= 2 && pasos.Any(p => p.Contains("navega", StringComparison.OrdinalIgnoreCase)))
                return pasos;
        }

        var extraidos = ExtraerPasosAccionables(textoPasos)
            .Select(NormalizarLineaPaso)
            .Where(p => !string.IsNullOrWhiteSpace(p) && EsPasoPruebaSeguro(p))
            .ToList();
        if (extraidos.Count > 0)
            return extraidos;

        var nl = PasosDesdeLenguajeNatural(textoPasos + "\n" + (titulo ?? ""));
        if (nl.Count > 0)
        {
            riesgos.Add("Sugerencia: se armaron pasos desde el texto; al Guardar se crean bindings si faltan.");
            return nl;
        }

        riesgos.Add("Sugerencia: sin pasos claros; se dejó navegación base. Completá el Detalle y Analizá de nuevo.");
        return
        [
            "Cuando navega al menu Acciones de Caja",
            "Entonces se muestra el cartel de bienvenida en la pagina de inicio"
        ];
    }

    /// <summary>
    /// Si un paso del Detalle coincide casi con un step existente, usa la frase del catálogo (mismo orden).
    /// </summary>
    private static List<string> AlinearFrasesConStepsExistentes(List<string> pasos, List<string> stepsCatalogo)
    {
        if (pasos.Count == 0 || stepsCatalogo.Count == 0)
            return pasos;

        var catalogoNorm = stepsCatalogo
            .Select(NormalizarLineaPaso)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var result = new List<string>();
        foreach (var paso in pasos)
        {
            var n = NormalizarLineaPaso(paso);
            var cuerpo = Regex.Replace(n, @"^(Given|When|Then|And)\s+", "", RegexOptions.IgnoreCase).Trim();
            if (cuerpo.Length < 8)
            {
                result.Add(n);
                continue;
            }

            string? mejor = null;
            var mejorScore = 0;
            foreach (var cat in catalogoNorm)
            {
                var cuerpoCat = Regex.Replace(cat, @"^(Given|When|Then|And)\s+", "", RegexOptions.IgnoreCase).Trim();
                if (cuerpoCat.Length < 8) continue;
                if (cuerpoCat.Equals(cuerpo, StringComparison.OrdinalIgnoreCase))
                {
                    mejor = cat;
                    mejorScore = 100;
                    break;
                }

                // Contiene o es contenido (mismo sentido, frase del proyecto).
                if (cuerpoCat.Contains(cuerpo, StringComparison.OrdinalIgnoreCase)
                    || cuerpo.Contains(cuerpoCat, StringComparison.OrdinalIgnoreCase))
                {
                    var score = Math.Min(cuerpo.Length, cuerpoCat.Length);
                    if (score > mejorScore && score >= 18)
                    {
                        mejorScore = score;
                        mejor = cat;
                    }
                }
            }

            result.Add(mejor ?? n);
        }

        return result;
    }

    /// <summary>
    /// Convierte Detalle en claro (oraciones / ítems) a pasos When/Then.
    /// Si no hay binding SpecFlow, al Guardar se crea el stub automáticamente.
    /// </summary>
    private static List<string> PasosDesdeLenguajeNatural(string texto)
    {
        var raw = (texto ?? "").Replace("\r\n", "\n");
        // Quitar ruido de corpus (mapa, jira metadata)
        raw = Regex.Replace(raw, @"(?is)\[mapa_automatizacion\][\s\S]*?(?=\n---|\z)", " ");
        raw = Regex.Replace(raw, @"(?is)\[aprendizaje\][\s\S]*?(?=\n---|\z)", " ");
        raw = Regex.Replace(raw, @"(?im)^#.*$", " ");

        var chunks = Regex.Split(raw,
            @"\n+|(?<=[.!?])\s+|\s+;\s+|(?:,?\s+y\s+luego\s+)|(?:\s+luego\s+)|(?:\s+después\s+)|(?:\s+despues\s+)|(?:\s+y\s+después\s+)|(?:\s+y\s+despues\s+)|(?:\s+primero\s+)|(?:\s+finalmente\s+)|(?:\s+a\s+continuaci[oó]n\s+)");
        var pasos = new List<string>();
        foreach (var chunk in chunks)
        {
            var t = Regex.Replace(chunk ?? "", @"\s+", " ").Trim().TrimEnd('.', '…');
            if (t.Length < 8 || t.Length > 280) continue;
            if (t.StartsWith('[')) continue;
            var lower = t.ToLowerInvariant();
            if (ContieneAlguno(lower,
                    "mapa automatización", "generadoutc", "keywords:", "pantallas:", "circuito",
                    "background:", "feature:", "origen jira", "ticket:", "evide", "specflow",
                    "preferí", "preferi", "sql (referencia)", "propósito:", "proposito:",
                    "módulo elegido", "modulo elegido"))
                continue;
            if (Regex.IsMatch(t, @"^(Given|When|Then|And)\s", RegexOptions.IgnoreCase))
            {
                var n = NormalizarLineaPaso(t);
                if (EsPasoPruebaSeguro(n)) pasos.Add(n);
                continue;
            }

            // Quitar numeración "1) " "2. " "Paso 1:"
            t = Regex.Replace(t, @"^(?i)(paso\s*)?(\d+[\).\:-]|\-|\*|•)\s*", "").Trim();
            t = Regex.Replace(t, @"^(?i)paso\s*\d+\s*[:.\-]\s*", "").Trim();
            if (t.Length < 8) continue;

            var esThen = ContieneAlguno(lower,
                "debe ", "debe mostrar", "queda ", "se muestra", "verificar", "validar",
                "coincide", "figura", "resultado", "esperado", "entonces", "observ");
            var kw = esThen ? "Entonces" : "Cuando";
            // Prefijos naturales → acción
            t = Regex.Replace(t, @"^(?i)(quiero|necesito|hay que|se debe|vamos a|para |debo |tiene que )\s+", "");
            var line = kw + " " + Capitalizar(t);
            if (EsPasoPruebaSeguro(line))
                pasos.Add(line);
        }

        return pasos
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(25)
            .ToList();
    }

    /// <summary>
    /// Evita que el LLM reemplace el circuito del Detalle por otro (p. ej. Intercaja).
    /// Compara cantidad de pasos When/Then del Scenario (sin Background).
    /// </summary>
    private static bool FeatureRespetaEspirituDelBorrador(string original, string propuesto)
    {
        static List<string> PasosScenario(string f)
        {
            var lines = (f ?? "").Replace("\r\n", "\n").Split('\n');
            var enScenario = false;
            var list = new List<string>();
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (Regex.IsMatch(line, @"^Scenario(?: Outline)?\s*:", RegexOptions.IgnoreCase))
                {
                    enScenario = true;
                    continue;
                }
                if (Regex.IsMatch(line, @"^(Feature|Background)\s*:", RegexOptions.IgnoreCase))
                {
                    enScenario = false;
                    continue;
                }
                if (!enScenario) continue;
                if (Regex.IsMatch(line, @"^(Given|When|Then|And)\s+", RegexOptions.IgnoreCase))
                    list.Add(Regex.Replace(line, @"^(Given|When|Then|And)\s+", "", RegexOptions.IgnoreCase).Trim().ToLowerInvariant());
            }
            return list;
        }

        var a = PasosScenario(original);
        var b = PasosScenario(propuesto);
        if (a.Count == 0) return true;
        if (b.Count == 0) return false;

        // Misma cantidad ±2, o al menos 60% de overlap de tokens clave.
        if (Math.Abs(a.Count - b.Count) > 2 && b.Count < a.Count * 0.6)
            return false;

        static HashSet<string> Tokens(IEnumerable<string> pasos)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in pasos)
            {
                foreach (Match m in Regex.Matches(p, @"\b[a-záéíóúñ]{4,}\b", RegexOptions.IgnoreCase))
                    set.Add(m.Value.ToLowerInvariant());
            }
            return set;
        }

        var ta = Tokens(a);
        var tb = Tokens(b);
        if (ta.Count == 0) return true;
        var inter = ta.Count(x => tb.Contains(x));
        var ratio = (double)inter / ta.Count;
        return ratio >= 0.45;
    }

    private static string SanitizarTagTipoIssue(string? tipo)
    {
        if (string.IsNullOrWhiteSpace(tipo)) return "";
        var t = Regex.Replace(tipo.Trim(), @"[^\w\-]+", "_");
        t = Regex.Replace(t, @"_+", "_").Trim('_');
        if (string.IsNullOrWhiteSpace(t)) return "";
        // Tags SpecFlow sin espacios; capitalizar estilo Pascal simple
        return char.ToUpperInvariant(t[0]) + (t.Length > 1 ? t[1..] : "");
    }

    /// <summary>
    /// Etiquetas reales del mat-select de cajas en la app (legibles para QA).
    /// UI: "Normal | $", "Normal | U$S", "Normal | €", "Minibóveda".
    /// </summary>
    private static class EtiquetasCajaUi
    {
        public const string NormalPesos = "Normal | $";
        public const string NormalUsd = "Normal | U$S";
        public const string NormalEuro = "Normal | €";
        public const string Miniboveda = "Minibóveda";
    }

    /// <summary>
    /// Plantillas cortas y coherentes por dominio. Respeta moneda e importe pedidos en el texto.
    /// </summary>
    private static List<string>? PlantillaPasosPorDominio(string texto)
    {
        var lower = (texto ?? string.Empty).ToLowerInvariant();

        // Fallas de caja ANTES que Intercaja: "falla" + billetaje no debe armar pases.
        if (EsDominioFallasCaja(lower))
        {
            var sobrante = ContieneAlguno(lower, "sobrante") && !ContieneAlguno(lower, "faltante");
            var faltante = ContieneAlguno(lower, "faltante") && !ContieneAlguno(lower, "sobrante");
            var tipoFalla = sobrante ? "sobrante" : faltante ? "faltante" : "sobrante o faltante";
            return
            [
                "Cuando navega a Cuadre y cierre desde Acciones de Caja",
                "Cuando selecciona en Cuadre y cierre una caja abierta en Pesos o USD",
                "Cuando asegura saldo minimo para cierre desde Cuadre si la caja esta en cero",
                "Cuando inicia el cierre de caja",
                $"Cuando en cierre carga billetaje distinto al saldo para provocar {tipoFalla}",
                "Cuando avanza a la confirmacion de cierre",
                $"Entonces la caja figura como Caja descuadrada por {tipoFalla}",
                "Cuando registra la falla de caja con supervision local",
                "Cuando confirma el cierre de caja",
                "Entonces el cierre de caja queda confirmado",
                "Cuando ingresa a Transacciones Monetarias desde Acciones de Caja",
                "Cuando selecciona en Transacciones Monetarias la caja del cierre",
                "Cuando busca en Transacciones Monetarias la falla de caja del dia",
                "Entonces en Transacciones Monetarias los montos de la falla coinciden con el documento e imagenes",
                "Cuando navega a Reportes Tira auditora",
                "Entonces en tira auditora se observa el evento de falla con los montos del documento e imagenes"
            ];
        }

        if (ContieneAlguno(lower, "otros ingresos", "otro ingreso", "ingreso de dinero"))
        {
            var preferUsd = ContieneAlguno(lower, "u$s", "usd", "dolar", "dólar", "dolares", "dólares");
            var preferEuro = !preferUsd && ContieneAlguno(lower, "euro", "eur", "€");
            var pasoCaja = preferUsd
                ? "Cuando selecciona en Otros Ingresos una caja Normal en U$S"
                : preferEuro
                    ? "Cuando selecciona en Otros Ingresos una caja Normal en Euro"
                    : "Cuando selecciona en Otros Ingresos una caja Normal en Pesos o USD";

            var importe = ExtraerImportePedido(texto);
            var quiereAleatorio = ContieneAlguno(lower, "aleatorio", "al azar", "random");
            var pasoImporte = (!quiereAleatorio && importe is > 0)
                ? $"Cuando ingresa un importe de {FormatearImportePaso(importe.Value)} para otros ingresos"
                : "Cuando ingresa un importe aleatorio para otros ingresos";

            return
            [
                "Cuando navega a Otros Ingresos desde Acciones de Caja",
                pasoCaja,
                "Cuando crea un nuevo ingreso de dinero",
                "Cuando selecciona la causa ideal para otros ingresos",
                pasoImporte,
                "Cuando procesa el ingreso de dinero en la caja seleccionada",
                "Entonces el ingreso de dinero queda procesado en la caja seleccionada"
            ];
        }

        if (ContieneAlguno(lower, "intercaja", "pase intercaja", "pases intercaja"))
        {
            var anula = ContieneAlguno(lower, "anula", "anulación", "anulacion");
            if (anula)
            {
                return
                [
                    "Cuando navega a Pases Intercaja desde Acciones de Caja",
                    "Cuando prepara el envio de un pase intercaja con billetaje",
                    "Cuando confirma el envio del pase intercaja",
                    "Cuando anula el pase intercaja desde la caja origen",
                    "Entonces el pase intercaja figura anulado"
                ];
            }
            return
            [
                "Cuando navega a Pases Intercaja desde Acciones de Caja",
                "Cuando prepara el envio de un pase intercaja con billetaje",
                "Cuando confirma el envio del pase intercaja",
                "Cuando acepta el pase intercaja en la caja destino",
                "Entonces el pase intercaja queda aceptado en destino"
            ];
        }

        if (ContieneAlguno(lower, "caja-bóveda", "caja boveda", "pase a bóveda", "pase a boveda", "caja boveda"))
        {
            return
            [
                "Cuando navega a Pases Caja-Boveda desde Acciones de Caja",
                "Cuando prepara un pase hacia boveda con el importe indicado",
                "Cuando confirma el pase caja-boveda",
                "Entonces el pase a boveda queda registrado correctamente"
            ];
        }

        if (ContieneAlguno(lower, "alta de caja", "alta caja", "abrir caja", "apertura de caja", "apertura operativa", "equivalencia coe"))
        {
            if (ContieneAlguno(lower, "sin equivalencia", "falta equivalencia", "no tiene equivalencia"))
            {
                return
                [
                    "Cuando navega al alta de caja",
                    "Cuando completa el alta de una caja nueva sin equivalencia COE",
                    "Cuando asigna la caja al usuario configurado",
                    "Entonces en Apertura se observa el aviso de equivalencia COE faltante"
                ];
            }
            return
            [
                "Cuando navega al alta de caja",
                "Cuando completa el alta y asignacion de caja con equivalencia COE",
                "Cuando abre la caja en pesos desde Apertura",
                "Entonces la caja queda abierta y lista para operar"
            ];
        }

        if (ContieneAlguno(lower, "cierre", "cuadre", "caja cuadrada", "confirmar cierre"))
        {
            return
            [
                "Cuando navega a Cuadre y cierre desde Acciones de Caja",
                "Cuando selecciona en Cuadre y cierre una caja abierta en Pesos o USD",
                "Cuando inicia el cierre de caja",
                "Cuando en cierre carga billetaje igual al saldo actual",
                "Cuando avanza a la confirmacion de cierre",
                "Entonces la caja figura como Caja cuadrada",
                "Cuando confirma el cierre de caja",
                "Entonces el cierre de caja queda confirmado"
            ];
        }

        // Datos COBIS (Sybase) / SQL SOT: conexión + SELECT (antes de cheques para no mezclar con UI).
        if (EsDominioDatosCobisSqlSot(lower))
        {
            var quiereCobis = ContieneAlguno(lower, "cobis", "sybase");
            var quiereSql = ContieneAlguno(lower, "sql sot", "sqlsot", "uw_cashier", "sql server", "sqlsot");
            // Si no aclara motor, ofrece ambos (opción documentada en Generar → Detalle).
            if (!quiereCobis && !quiereSql)
            {
                quiereCobis = true;
                quiereSql = true;
            }

            var quiereConsulta = ContieneAlguno(lower,
                "consulta", "query", "select", "fecha de proceso", "buscar en la base", "hacer un select");
            var pasos = new List<string>();

            if (quiereCobis)
            {
                pasos.Add("Cuando se verifica la conexion a COBIS configurada");
                pasos.Add("Entonces la conexion a COBIS responde correctamente");
                if (quiereConsulta || ContieneAlguno(lower, "fecha de proceso"))
                    pasos.Add("Entonces COBIS devuelve la fecha de proceso");
                if (ContieneAlguno(lower, "select ") || ContieneAlguno(lower, "consulta select"))
                {
                    pasos.Add("Cuando ejecuta en COBIS la consulta SELECT TOP 1 fp_fecha FROM cobis..ba_fecha_proceso");
                    pasos.Add("Entonces la consulta a COBIS devolvio filas");
                }
            }

            if (quiereSql)
            {
                pasos.Add("Cuando se verifica la conexion a SQL SOT configurada");
                pasos.Add("Entonces la conexion a SQL SOT responde correctamente");
                if (quiereConsulta || ContieneAlguno(lower, "select", "consulta"))
                {
                    pasos.Add("Cuando ejecuta en SQL SOT la consulta de prueba");
                    pasos.Add("Entonces la consulta a SQL SOT devolvio filas");
                }
            }

            return pasos;
        }

        if (ContieneAlguno(lower, "cheque", "depósito de cheque", "deposito de cheque", "interdepósito", "interdeposito"))
        {
            return
            [
                "Cuando navega a Deposito e Interdeposito de cheques",
                "Cuando completa la operacion de cheque segun el ticket",
                "Cuando confirma el procesamiento del cheque",
                "Entonces la operacion de cheque queda registrada correctamente"
            ];
        }

        if (ContieneAlguno(lower, "parametría", "parametria", "perfil contable", "relación transacción", "relacion transaccion", "sc-161"))
        {
            return
            [
                "Cuando navega a la parametría de relacion transaccion y perfil contable",
                "Cuando aplica la configuracion indicada en el ticket",
                "Cuando guarda los cambios de parametría",
                "Entonces la parametría queda aplicada segun el resultado esperado"
            ];
        }

        return null;
    }

    /// <summary>
    /// Intención de datos: probar/usar COBIS (Sybase) o SQL SOT (UW_CASHIER), no pantallas de cheques.
    /// </summary>
    private static bool EsDominioDatosCobisSqlSot(string lower)
    {
        if (string.IsNullOrWhiteSpace(lower)) return false;
        // Cheques UI: no tratar como "solo datos" aunque mencione COBIS para la cuenta.
        if (ContieneAlguno(lower, "cheque", "interdepósito", "interdeposito", "deposito de cheque", "depósito de cheque"))
            return false;

        if (ContieneAlguno(lower, "sql sot", "sqlsot", "uw_cashier", "sybase"))
            return true;

        if (ContieneAlguno(lower, "cobis") && ContieneAlguno(lower,
                "conexion", "conexión", "conectar", "consulta", "query", "select",
                "fecha de proceso", "base de datos", "sybase", "verificar"))
            return true;

        return ContieneAlguno(lower,
            "verificar conexion", "verificar conexión", "probar conexion", "probar conexión",
            "conexion a la base", "conexión a la base", "consulta select", "datos cobis", "datos sql");
    }

    private static bool EsDominioFallasCaja(string lower)
    {
        if (string.IsNullOrWhiteSpace(lower)) return false;
        // No confundir con "equivalencia COE faltante" (Alta de caja).
        if (ContieneAlguno(lower, "equivalencia") && ContieneAlguno(lower, "faltante")
            && !ContieneAlguno(lower, "falla de caja", "fallas de caja", "sobrante", "tira auditora", "sc-470"))
            return false;

        if (ContieneAlguno(lower,
                "falla de caja", "fallas de caja", "ta- fallas", "ta fallas", "ta-fallas",
                "sc-470", "caja descuadrada", "descuadrada", "tira auditora",
                "grp-ta-fallas", "fallas-caja", "ta- fallas de caja"))
            return true;

        if (ContieneAlguno(lower, "sobrante", "faltante") &&
            ContieneAlguno(lower, "cierre", "cuadre", "falla", "descuadre", "ajuste", "auditora", "billetaje", "supervision"))
            return true;

        if (ContieneAlguno(lower, "transacciones monetarias", "gestion-transacciones", "montos de la falla"))
            return true;

        return ContieneAlguno(lower, "registrar falla", "autorizacion falla", "supervision falla", "ajuste de falla");
    }

    /// <summary>
    /// Detecta importes pedidos en lenguaje claro: "importe 100", "cargar 100", "ingresar 100", etc.
    /// </summary>
    private static decimal? ExtraerImportePedido(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return null;

        var lower = texto.ToLowerInvariant();
        if (ContieneAlguno(lower, "importe aleatorio", "monto aleatorio", "al azar"))
            return null;

        var patterns = new[]
        {
            @"importe\s*(?:fijo|exacto|de|:)?\s*\$?\s*(\d+(?:[.,]\d+)?)",
            @"monto\s*(?:fijo|exacto|de|:)?\s*\$?\s*(\d+(?:[.,]\d+)?)",
            @"carg(?:a|ar|ue)\s+(?:un\s+)?(?:importe\s+de\s+)?\$?\s*(\d+(?:[.,]\d+)?)",
            @"ingres(?:a|ar|e)\s+(?:un\s+)?(?:importe\s+de\s+)?\$?\s*(\d+(?:[.,]\d+)?)",
            @"por\s+\$?\s*(\d+(?:[.,]\d+)?)\s*(?:pesos|u\$s|usd|euros?)?",
            @"\$\s*(\d+(?:[.,]\d+)?)"
        };

        foreach (var pat in patterns)
        {
            var m = Regex.Match(texto, pat, RegexOptions.IgnoreCase);
            if (!m.Success)
                continue;
            if (TryParseImporteSimple(m.Groups[1].Value, out var val) && val > 0m && val < 100_000_000m)
                return decimal.Round(val, 2, MidpointRounding.AwayFromZero);
        }

        return null;
    }

    private static bool TryParseImporteSimple(string texto, out decimal importe)
    {
        importe = 0m;
        if (string.IsNullOrWhiteSpace(texto))
            return false;
        var t = texto.Trim().Replace(" ", "");
        if (t.Contains(',') && t.Contains('.'))
            t = t.Replace(".", "").Replace(',', '.');
        else if (t.Contains(','))
            t = t.Replace(',', '.');
        return decimal.TryParse(t, System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture, out importe);
    }

    private static string FormatearImportePaso(decimal importe)
    {
        if (importe == decimal.Truncate(importe))
            return ((long)importe).ToString(System.Globalization.CultureInfo.InvariantCulture);
        return importe.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string NormalizarLineaPaso(string paso)
    {
        var line = (paso ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(line)) return "";

        if (!Regex.IsMatch(line, @"^(Given|When|Then|And|Dado|Cuando|Entonces|Y)\s", RegexOptions.IgnoreCase))
            line = "Cuando " + line;

        line = Regex.Replace(line, @"^(given|when|then|and|dado|cuando|entonces|y)\s+", m =>
        {
            var k = m.Groups[1].Value.ToLowerInvariant();
            return k switch
            {
                "given" or "dado" => "Dado ",
                "when" or "cuando" => "Cuando ",
                "then" or "entonces" => "Entonces ",
                _ => "Y "
            };
        }, RegexOptions.IgnoreCase);

        // Legibilidad: no usar "caja operativa detectada" (exige Apertura previa).
        line = Regex.Replace(
            line,
            @"selecciona en Otros Ingresos la caja operativa detectada",
            "selecciona en Otros Ingresos una caja Normal en Pesos o USD",
            RegexOptions.IgnoreCase);

        // Steps rotos del indexado: "una caja en" → default legible.
        line = Regex.Replace(
            line,
            @"selecciona en Otros Ingresos una caja\s+en\s*$",
            "selecciona en Otros Ingresos una caja Normal en Pesos o USD",
            RegexOptions.IgnoreCase);

        return line.Trim();
    }

    /// <summary>
    /// Pasos que rompen escenarios generados si no hay contexto previo (Apertura, saldo, etc.).
    /// </summary>
    private static bool EsPasoPruebaSeguro(string paso)
    {
        var p = Regex.Replace(paso ?? "", @"^(Given|When|Then|And|Dado|Cuando|Entonces|Y)\s+", "", RegexOptions.IgnoreCase).Trim();
        if (p.Length < 5) return false;
        if (p.Contains("FALTA INFORMACIÓN", StringComparison.OrdinalIgnoreCase)) return false;
        if (Regex.IsMatch(p, @"\buna caja en\s*$", RegexOptions.IgnoreCase)) return false;
        if (p.EndsWith(" en", StringComparison.OrdinalIgnoreCase) && p.Length < 20) return false;
        // Cualquier otra frase en claro es válida: si no hay binding, al Guardar se crea el stub.
        return true;
    }

    private static bool EsPasoGherkinUtil(string paso) => EsPasoPruebaSeguro(paso);

    /// <summary>
    /// Si el texto apunta a un dominio, descarta steps de otros módulos y los inseguros.
    /// </summary>
    private static List<string> FiltrarPasosCoherentesConDominio(List<string> pasos, string texto)
    {
        var lower = (texto ?? string.Empty).ToLowerInvariant();
        string[]? prohibidos = null;

        if (ContieneAlguno(lower, "otros ingresos", "otro ingreso", "ingreso de dinero"))
        {
            prohibidos =
            [
                "apertura", "pases intercaja", "pases caja", "cuadre", "cierre",
                "bóveda", "boveda", "intercaja", "interdepósito", "interdeposito",
                "depósito de cheques", "deposito de cheques", "parametría", "parametria",
                "asegura saldo", "caja operativa detectada", "miniboveda", "minibóveda"
            ];
        }

        return pasos
            .Where(EsPasoPruebaSeguro)
            .Where(p =>
            {
                if (prohibidos is null) return true;
                var pl = p.ToLowerInvariant();
                // Permitir solo frases del flujo de ingreso (no "…con Otros Ingresos si hace falta").
                var esFlujoIngreso =
                    (pl.Contains("otros ingresos") || pl.Contains("ingreso de dinero") || pl.Contains("importe aleatorio para otros"))
                    && !pl.Contains("asegura")
                    && !pl.Contains("cierre")
                    && !pl.Contains("cuadre")
                    && !pl.Contains("pase");
                if (esFlujoIngreso) return true;
                return !prohibidos.Any(x => pl.Contains(x, StringComparison.Ordinal));
            })
            .ToList();
    }

    private static List<string> AsegurarThenDesdeResultadoEsperado(List<string> pasos, string contexto)
    {
        if (pasos.Any(p => p.StartsWith("Entonces ", StringComparison.OrdinalIgnoreCase) || p.StartsWith("Then ", StringComparison.OrdinalIgnoreCase)))
            return pasos;

        var esperado = ExtraerSeccionIntencion("", contexto,
            "resultado esperado", "expected", "debe mostrar", "debe quedar", "entonces ");
        if (string.IsNullOrWhiteSpace(esperado) || esperado.Length < 15)
            esperado = ExtraerSeccionIntencion("", contexto, "verificar", "validar");

        var list = pasos.ToList();
        if (!string.IsNullOrWhiteSpace(esperado) && esperado.Length >= 15)
        {
            var cuerpo = Truncar(esperado.Trim().TrimEnd('.'), 150);
            list.Add("Entonces se verifica: " + Capitalizar(cuerpo));
        }
        else if (Regex.IsMatch(contexto ?? "", @"(?i)--- Imagen interpretada:"))
        {
            list.Add("Entonces la pantalla coincide con la evidencia del ticket");
        }
        return list;
    }

    private static List<string> AsegurarOrdenBasicoOtrosIngresos(
        List<string> pasos,
        string contexto,
        bool instruccionesExplicitas = false)
    {
        // Si el operador escribió el orden, no pisar con la plantilla de Otros Ingresos.
        if (instruccionesExplicitas)
            return pasos;

        if (!ContieneAlguno(contexto.ToLowerInvariant(), "otros ingresos", "otro ingreso", "ingreso de dinero"))
            return pasos;

        var plantilla = PlantillaPasosPorDominio(contexto);
        if (plantilla is { Count: > 0 })
            return plantilla;

        return pasos;
    }

    /// <summary>
    /// Reescribe cada Scenario para quitar pasos peligrosos / duplicados / sin navegación.
    /// Conserva múltiples Scenario si el ticket trae varios casos.
    /// </summary>
    private static string SanitizarEscenarioFeature(string contenido)
    {
        var lines = contenido.Replace("\r\n", "\n").Split('\n').ToList();
        var scenarioIndexes = new List<int>();
        for (var i = 0; i < lines.Count; i++)
        {
            if (Regex.IsMatch(lines[i], @"(?i)^\s*(Scenario|Escenario):"))
                scenarioIndexes.Add(i);
        }
        if (scenarioIndexes.Count == 0)
            return contenido;

        var preamble = lines.Take(scenarioIndexes[0]).ToList();
        var sb = new StringBuilder();
        foreach (var h in preamble)
            sb.AppendLine(h);

        for (var s = 0; s < scenarioIndexes.Count; s++)
        {
            var start = scenarioIndexes[s];
            var end = s + 1 < scenarioIndexes.Count ? scenarioIndexes[s + 1] : lines.Count;
            var scenarioHeader = lines[start];
            var body = lines.Skip(start + 1).Take(end - start - 1)
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Where(l => !Regex.IsMatch(l, @"(?i)^\s*(Scenario|Escenario):"))
                .Select(NormalizarLineaPaso)
                .Where(l => !string.IsNullOrWhiteSpace(l) && EsPasoPruebaSeguro(l))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var blob = string.Join("\n", preamble.Concat(new[] { scenarioHeader }).Concat(body));
            var plantilla = PlantillaPasosPorDominio(blob);
            // Solo reemplazar por plantilla si el escenario quedó vacío o sin navegación básica.
            if (plantilla is { Count: > 0 } && (body.Count < 2 ||
                !body.Any(p => ContieneAlguno(p.ToLowerInvariant(), "navega", "ingresa", "selecciona", "cuando", "when"))))
            {
                body = plantilla;
            }
            else if (plantilla is { Count: > 0 } &&
                     ContieneAlguno(blob.ToLowerInvariant(), "otros ingresos", "ingreso de dinero") &&
                     !ContieneAlguno(blob.ToLowerInvariant(), "falla", "cuadre y cierre"))
            {
                var hasCaja = body.Any(p => p.Contains("selecciona en Otros Ingresos una caja", StringComparison.OrdinalIgnoreCase));
                var hasImporte = body.Any(p => p.Contains("importe", StringComparison.OrdinalIgnoreCase));
                var hasProcesar = body.Any(p => p.Contains("procesa el ingreso", StringComparison.OrdinalIgnoreCase));
                var ordenOk = (body.FirstOrDefault() ?? "").Contains("navega a Otros Ingresos", StringComparison.OrdinalIgnoreCase);
                if (!hasCaja || !hasImporte || !hasProcesar || !ordenOk)
                    body = plantilla;
            }

            if (s > 0) sb.AppendLine();
            var headerMatch = Regex.Match(scenarioHeader, @"(?i)^(\s*(?:Scenario|Escenario):\s*)(.*)$");
            if (headerMatch.Success)
                scenarioHeader = headerMatch.Groups[1].Value + AcortarTitulo(LimpiarTituloFeature(headerMatch.Groups[2].Value), 48);
            sb.AppendLine(scenarioHeader);
            foreach (var b in body)
                sb.AppendLine("    " + b.TrimStart());
        }

        var resultado = sb.ToString().TrimEnd() + "\n";
        // Asegurar Background de login SOT si faltó (misma lógica que GenerarFeature).
        if (!Regex.IsMatch(resultado, @"(?im)^\s*(Background|Antecedentes):"))
        {
            var insertAt = IndexOfEscenario(resultado);
            if (insertAt > 0)
            {
                resultado = resultado[..insertAt]
                    + BackgroundLogin.TrimEnd() + "\n\n"
                    + resultado[insertAt..];
            }
        }
        return resultado;
    }

    private static int IndexOfEscenario(string texto)
    {
        var es = texto.IndexOf("Escenario:", StringComparison.OrdinalIgnoreCase);
        if (es >= 0) return es;
        return texto.IndexOf("Scenario:", StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> ValidarFeaturePrueba(string contenido)
    {
        var errores = new List<string>();
        var scenarioPos = IndexOfEscenario(contenido);
        if (scenarioPos < 0)
        {
            errores.Add("Falta la sección Escenario.");
            return errores;
        }

        var escenario = contenido[scenarioPos..];
        var pasos = escenario.Replace("\r\n", "\n").Split('\n')
            .Select(l => l.Trim())
            .Where(l => Regex.IsMatch(l, @"^(Given|When|Then|And|Dado|Cuando|Entonces|Y)\s", RegexOptions.IgnoreCase))
            .ToList();

        if (pasos.Count == 0)
            errores.Add("El Scenario no tiene pasos. Escribí en Detalle qué querés hacer y Analizá de nuevo.");

        // Solo rechazar basura obvia; el resto se acepta y se auto-crean bindings al guardar.
        foreach (var p in pasos)
        {
            var cuerpo = Regex.Replace(p, @"^(Given|When|Then|And)\s+", "", RegexOptions.IgnoreCase).Trim();
            if (cuerpo.Length < 5)
                errores.Add("Paso vacío o demasiado corto: " + p);
            if (cuerpo.Contains("FALTA INFORMACIÓN", StringComparison.OrdinalIgnoreCase))
                errores.Add("El borrador todavía marca FALTA INFORMACIÓN.");
            if (Regex.IsMatch(cuerpo, @"^@\w+", RegexOptions.IgnoreCase))
                errores.Add(
                    "Paso con tag (@…) en lugar de texto Gherkin: " + p +
                    " — el @tag va en la línea anterior al Scenario, no como When/Then.");
        }

        return errores;
    }

    private static string FeatureIncompleto(string titulo, List<string> faltantes, string jiraUrl)
    {
        var sb = new StringBuilder();
        sb.AppendLine("@Prueba @FaltaInformacion");
        sb.AppendLine($"Característica: {AcortarTitulo(titulo, 85)}");
        sb.AppendLine();
        sb.AppendLine("  # FALTA INFORMACIÓN — no inventar pasos");
        if (!string.IsNullOrWhiteSpace(jiraUrl))
            sb.AppendLine($"  # Jira: {jiraUrl.Trim()}");
        foreach (var f in faltantes)
            sb.AppendLine("  #  - " + f);
        sb.AppendLine();
        sb.AppendLine("  # Completá: link Jira autenticado, documentación o texto con");
        sb.AppendLine("  # objetivo + pasos + resultado esperado (+ datos de prueba).");
        sb.AppendLine();
        sb.AppendLine("  Escenario: Pendiente — completar informacion");
        sb.AppendLine("    Given FALTA INFORMACIÓN para generar el caso E2E");
        return sb.ToString();
    }

    private static List<string> ExtraerPasosAccionables(string texto)
    {
        var pasos = new List<string>();
        var lines = texto.Replace("\r\n", "\n").Split('\n');
        var enPasos = false;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length < 6 || line.Length > 260) continue;
            if (line.StartsWith("#") || line.StartsWith("//")) continue;
            if (Regex.IsMatch(line, @"(?i)^(pasos\s+para\s+reproducir|steps\s+to\s+reproduce|pasos)\s*:?\s*$"))
            {
                enPasos = true;
                continue;
            }
            if (enPasos && Regex.IsMatch(line, @"(?i)^(resultado|expected|actual|notas|observaciones)\b"))
                enPasos = false;

            var mGherkin = Regex.Match(line, @"^(Given|When|Then|And)\s+(.+)$", RegexOptions.IgnoreCase);
            if (mGherkin.Success)
            {
                pasos.Add(mGherkin.Groups[1].Value + " " + mGherkin.Groups[2].Value.Trim());
                continue;
            }

            var mNum = Regex.Match(line, @"^(\d+[\).\:-]|\-|\*|•)\s+(.+)$");
            if (mNum.Success)
            {
                var cuerpo = mNum.Groups[2].Value.Trim();
                if (enPasos || EsAccion(cuerpo))
                    pasos.Add("Cuando " + Capitalizar(cuerpo));
                continue;
            }

            if (enPasos && EsAccion(line))
                pasos.Add("Cuando " + Capitalizar(line));
        }

        return pasos.Distinct(StringComparer.OrdinalIgnoreCase).Take(15).ToList();
    }

    private static bool EsAccion(string s)
    {
        var lower = s.ToLowerInvariant();
        return ContieneAlguno(lower,
            "navega", "ingresa", "selecciona", "confirma", "abre", "cierra", "carga", "valida",
            "verifica", "busca", "elige", "guarda", "anula", "acepta", "rechaza", "filtra",
            "inicia", "resuelve", "asegura", "ejecuta", "pulsa", "click", "hacer clic", "presiona",
            "completa", "elige", "marca", "desmarca", "procesa", "consulta", "visualiza", "reproduce");
    }

    private static string Capitalizar(string s) =>
        string.IsNullOrWhiteSpace(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];

    private static List<string> BuscarStepsRelacionados(string texto, List<StepInfo> catalogo)
    {
        var lower = texto.ToLowerInvariant();

        // Dominio conocido: devolver la plantilla (misma que GenerarFeature) para el preview del asistente.
        var plantilla = PlantillaPasosPorDominio(texto);
        if (plantilla is { Count: > 0 })
        {
            return plantilla
                .Select(p => Regex.Replace(p, @"^(Given|When|Then|And)\s+", "", RegexOptions.IgnoreCase).Trim())
                .ToList();
        }

        var tokens = Regex.Matches(lower, @"[a-záéíóúñü]{4,}")
            .Select(m => m.Value)
            .Distinct()
            .Take(80)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var related = catalogo
            .Select(s => new
            {
                s.Texto,
                Score = s.Tokens.Count(t => tokens.Contains(t))
            })
            .Where(x => x.Score >= 2)
            .OrderByDescending(x => x.Score)
            .Take(12)
            .Select(x => x.Texto)
            .ToList();

        return FiltrarPasosCoherentesConDominio(related, texto);
    }

    private static List<StepInfo> IndexarSteps(string automatizacionRoot)
    {
        var dir = Path.Combine(automatizacionRoot, "StepDefinitions");
        var list = new List<StepInfo>();
        if (!Directory.Exists(dir)) return list;

        foreach (var file in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
        {
            var content = File.ReadAllText(file);
            foreach (Match m in Regex.Matches(content, @"\[(Given|When|Then)\(@""([^""]+)""\)\]"))
            {
                var texto = m.Groups[2].Value
                    .Replace("\\'", "'")
                    .Replace(@"\""", "\"");
                // Grupos de captura → frase legible (no dejar "caja en" vacío).
                texto = Regex.Replace(texto, @"\(Normal\|[^)]+\)", "Normal", RegexOptions.IgnoreCase);
                texto = Regex.Replace(texto, @"\(Pesos\|[^)]+\)", "Pesos", RegexOptions.IgnoreCase);
                texto = Regex.Replace(texto, @"\(Miniboveda\|[^)]+\)", "Miniboveda", RegexOptions.IgnoreCase);
                texto = Regex.Replace(texto, @"\(\[\\d\]\+[^\)]*\)", "100", RegexOptions.IgnoreCase);
                texto = Regex.Replace(texto, @"\([^)]*\)", "").Trim();
                texto = Regex.Replace(texto, @"\s+", " ").Trim();
                if (texto.Length < 8) continue;
                if (Regex.IsMatch(texto, @"importe de\s+para", RegexOptions.IgnoreCase))
                    texto = "ingresa un importe de 100 para otros ingresos";
                if (!EsPasoGherkinUtil(texto) && texto.Length < 8) continue;
                var tokens = Regex.Matches(texto.ToLowerInvariant(), @"[a-záéíóúñü]{4,}")
                    .Select(x => x.Value)
                    .Distinct()
                    .ToList();
                list.Add(new StepInfo(texto, tokens));
            }
        }

        return list
            .GroupBy(s => s.Texto, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    private static async Task<(bool Ok, string Texto, string Error)> ExtraerTextoArchivoAsync(
        IFormFile file,
        string? llmApiKey = null,
        string? llmBaseUrl = null,
        string? llmModel = null)
    {
        var name = file.FileName ?? "archivo";
        var ext = Path.GetExtension(name).ToLowerInvariant();
        await using var stream = file.OpenReadStream();

        try
        {
            if (ext is ".txt" or ".md" or ".csv" or ".feature" or ".json" or ".log")
            {
                using var sr = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                var t = await sr.ReadToEndAsync();
                if (string.IsNullOrWhiteSpace(t))
                    return (false, "", $"El archivo '{name}' está vacío.");
                return (true, t, "");
            }

            if (ext == ".docx")
            {
                var t = ExtraerDocx(stream);
                if (string.IsNullOrWhiteSpace(t))
                    return (false, "", $"No se pudo leer texto de '{name}'.");
                return (true, t, "");
            }

            if (ext == ".pdf")
            {
                var t = ExtraerPdf(stream);
                if (string.IsNullOrWhiteSpace(t))
                    return (false, "",
                        $"No se extrajo texto de '{name}' (PDF escaneado/imagen o vacío). Convertí a .txt/.md/.docx o pegá el texto.");
                return (true, t, "");
            }

            if (ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp")
            {
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms);
                var bytes = ms.ToArray();
                if (!string.IsNullOrWhiteSpace(llmApiKey))
                {
                    var vision = await AprendizajeLlm.InterpretarImagenAsync(
                        bytes, name, llmApiKey, llmBaseUrl, llmModel);
                    if (vision.Ok)
                        return (true, $"--- Imagen interpretada: {name} ---\n{vision.Descripcion}", "");
                    // Fallback OCR local si falla la visión
                }
                var ocr = await OcrWindows.ExtraerTextoAsync(bytes, name);
                if (ocr.Ok)
                    return (true, $"--- Imagen interpretada: {name} ---\n{ocr.Texto}", "");
                return (false, "", string.IsNullOrWhiteSpace(ocr.Aviso)
                    ? $"No se pudo leer la imagen '{name}' (OCR/LLM). Pegá el texto visible o configurá API key LLM."
                    : ocr.Aviso);
            }

            return (false, "",
                $"El formato '{ext}' no se lee en el asistente. Convertí a .txt/.md/.docx/.pdf o pegá el texto. Archivo: {name}");
        }
        catch (Exception ex)
        {
            return (false, "", $"Error leyendo '{name}': {ex.Message}");
        }
    }

    private static string ExtraerDocx(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        ms.Position = 0;
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: false);
        var entry = zip.GetEntry("word/document.xml");
        if (entry is null) return "";
        using var es = entry.Open();
        var doc = XDocument.Load(es);
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var paragraphs = doc.Descendants(w + "p")
            .Select(p => string.Concat(p.Descendants(w + "t").Select(t => t.Value)))
            .Where(s => !string.IsNullOrWhiteSpace(s));
        return string.Join("\n", paragraphs);
    }

    private static string ExtraerPdf(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        ms.Position = 0;
        using var doc = PdfDocument.Open(ms);
        var sb = new StringBuilder();
        foreach (var page in doc.GetPages())
        {
            var pageText = page.Text;
            if (string.IsNullOrWhiteSpace(pageText)) continue;
            sb.AppendLine(pageText);
        }
        return sb.ToString().Trim();
    }

    /// <summary>
    /// Si hay varios archivos (PDF, DOCX, MD…) con la misma información, conserva uno
    /// para no duplicar escenarios al analizar.
    /// </summary>
    private static List<FuenteTexto> DeduplicarFuentesDocumentacion(List<FuenteTexto> fuentes, List<string> avisos)
    {
        var result = new List<FuenteTexto>();
        var fingerprintsArchivo = new List<(string Origen, string Finger)>();

        foreach (var f in fuentes)
        {
            if (!f.Origen.StartsWith("archivo:", StringComparison.OrdinalIgnoreCase))
            {
                result.Add(f);
                continue;
            }

            var finger = FingerprintTextoDocumentacion(f.Texto);
            var dup = fingerprintsArchivo.FirstOrDefault(x =>
                SonTextosDocumentacionEquivalentes(x.Finger, finger));
            if (!string.IsNullOrEmpty(dup.Origen))
            {
                var nombreDup = NombreArchivoFuente(dup.Origen);
                var nombre = NombreArchivoFuente(f.Origen);
                avisos.Add(
                    $"«{nombre}» se interpreta como la misma información que «{nombreDup}»; se usa una sola copia para no duplicar escenarios.");
                continue;
            }

            fingerprintsArchivo.Add((f.Origen, finger));
            result.Add(f);
        }

        return result;
    }

    private static string NombreArchivoFuente(string origen) =>
        origen.StartsWith("archivo:", StringComparison.OrdinalIgnoreCase) && origen.Length > 8
            ? origen[8..]
            : origen;

    private static string FingerprintTextoDocumentacion(string? texto)
    {
        var n = (texto ?? "").ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(n.Length);
        foreach (var ch in n)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            else if (char.IsWhiteSpace(ch) || ch is '-' or '_' or '.' or ':' or '/')
                sb.Append(' ');
        }
        var words = sb.ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(w => w.Length > 2)
            .Take(500);
        return string.Join(' ', words);
    }

    private static bool SonTextosDocumentacionEquivalentes(string a, string b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
        if (string.Equals(a, b, StringComparison.Ordinal)) return true;

        var shorter = a.Length <= b.Length ? a : b;
        var longer = a.Length <= b.Length ? b : a;
        if (shorter.Length < 80) return false;

        if (longer.Contains(shorter, StringComparison.Ordinal)
            && shorter.Length >= (int)(longer.Length * 0.68))
            return true;

        var wa = shorter.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var wb = longer.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        if (wa.Count < 12 || wb.Count < 12) return false;
        var inter = wa.Count(w => wb.Contains(w));
        var union = wa.Count + wb.Count - inter;
        return union > 0 && (double)inter / union >= 0.82;
    }

    private static async Task<(bool Ok, string Key, string Texto, string Error, string Aviso, string Titulo, string Estado, string ModuloSugerido, string Tipo, string DetalleSugerido)> LeerJiraAsync(
        string jiraUrl,
        string email,
        string token,
        string? llmApiKey = null,
        string? llmBaseUrl = null,
        string? llmModel = null)
    {
        var key = ExtraerTicketKey(jiraUrl);
        if (string.IsNullOrWhiteSpace(key))
            return (false, "", "", "No se pudo detectar la clave del ticket en el link (ej. SC-161).", "", "", "", "", "", "");

        var baseUrl = ExtraerJiraBase(jiraUrl);
        if (string.IsNullOrWhiteSpace(baseUrl))
            return (false, key, "", "URL de Jira no reconocida. Ejemplo: https://tu-dominio.atlassian.net/browse/SC-161", "", "", "", "", "", "");

        email = string.IsNullOrWhiteSpace(email)
            ? (Environment.GetEnvironmentVariable("JIRA_EMAIL") ?? "").Trim()
            : email;
        token = string.IsNullOrWhiteSpace(token)
            ? (Environment.GetEnvironmentVariable("JIRA_API_TOKEN") ?? "").Trim()
            : token;

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(token))
        {
            return (false, key, "",
                "Para leer Jira hace falta autenticación: completá email + API token en Configuración → Jira " +
                "(o variables de entorno JIRA_EMAIL / JIRA_API_TOKEN). " +
                "Creá el token en https://id.atlassian.com/manage-profile/security/api-tokens",
                "", "", "", "", "", "");
        }

        var api = $"{baseUrl.TrimEnd('/')}/rest/api/3/issue/{key}?fields=summary,description,comment,attachment,status,components,labels,issuetype";
        using var req = new HttpRequestMessage(HttpMethod.Get, api);
        var creds = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{email}:{token}"));
        var auth = new AuthenticationHeaderValue("Basic", creds);
        req.Headers.Authorization = auth;
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var res = await Http.SendAsync(req);
        var body = await res.Content.ReadAsStringAsync();
        if (!res.IsSuccessStatusCode)
        {
            var statusCode = (int)res.StatusCode;
            string? cuenta = null;
            try
            {
                using var meReq = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl.TrimEnd('/')}/rest/api/3/myself");
                meReq.Headers.Authorization = auth;
                meReq.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                using var meRes = await Http.SendAsync(meReq);
                if (meRes.IsSuccessStatusCode)
                {
                    var meBody = await meRes.Content.ReadAsStringAsync();
                    using var meDoc = JsonDocument.Parse(meBody);
                    var display = meDoc.RootElement.TryGetProperty("displayName", out var dn) ? dn.GetString() : null;
                    var mail = meDoc.RootElement.TryGetProperty("emailAddress", out var em) ? em.GetString() : null;
                    cuenta = string.Join(" · ", new[] { mail, display }.Where(x => !string.IsNullOrWhiteSpace(x)));
                }
            }
            catch
            {
                /* ignore */
            }

            var hint = statusCode switch
            {
                401 or 403 =>
                    "Credenciales inválidas o sin permiso. Revisá que el email sea el de Atlassian (el mismo de la cuenta que creó el token) y regenerá el API token si hace falta.",
                404 =>
                    $"El ticket {key} no existe en {baseUrl} o la cuenta del token no tiene permiso para verlo. " +
                    "Abrí el mismo link en el navegador con esa cuenta; si no lo ves, pedí acceso al proyecto o usá otra cuenta/token. " +
                    "Confirmá también que el link sea del sitio Jira correcto (mismo dominio).",
                _ => $"Jira respondió {statusCode}. Revisá permisos del token o la clave {key}."
            };
            if (!string.IsNullOrWhiteSpace(cuenta))
                hint += $" Cuenta autenticada: {cuenta}.";
            var detalleJira = Truncar(body, 180);
            if (!string.IsNullOrWhiteSpace(detalleJira))
                hint += $" Detalle: {detalleJira}";

            return (false, key, "", hint, "", "", "", "", "", "");
        }

        using var doc = JsonDocument.Parse(body);
        var fields = doc.RootElement.GetProperty("fields");
        var summary = fields.TryGetProperty("summary", out var s) ? s.GetString() ?? "" : "";
        var status = fields.TryGetProperty("status", out var st) && st.TryGetProperty("name", out var sn)
            ? sn.GetString() ?? ""
            : "";
        var issueType = fields.TryGetProperty("issuetype", out var it) && it.TryGetProperty("name", out var itn)
            ? (itn.GetString() ?? "")
            : "";
        var description = ExtraerAdfOTexto(fields.TryGetProperty("description", out var d) ? d : default);
        var componentes = new List<string>();
        if (fields.TryGetProperty("components", out var comps) && comps.ValueKind == JsonValueKind.Array)
        {
            foreach (var c in comps.EnumerateArray())
            {
                if (c.TryGetProperty("name", out var cn) && !string.IsNullOrWhiteSpace(cn.GetString()))
                    componentes.Add(cn.GetString()!);
            }
        }
        var labels = new List<string>();
        if (fields.TryGetProperty("labels", out var labs) && labs.ValueKind == JsonValueKind.Array)
        {
            foreach (var l in labs.EnumerateArray())
            {
                if (l.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(l.GetString()))
                    labels.Add(l.GetString()!);
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Ticket: {key}");
        sb.AppendLine($"Título: {summary}");
        sb.AppendLine($"Estado: {status}");
        if (!string.IsNullOrWhiteSpace(issueType))
            sb.AppendLine($"Tipo: {issueType}");
        if (componentes.Count > 0)
            sb.AppendLine("Componentes: " + string.Join(", ", componentes));
        if (labels.Count > 0)
            sb.AppendLine("Labels: " + string.Join(", ", labels));
        sb.AppendLine();
        sb.AppendLine("Descripción:");
        sb.AppendLine(string.IsNullOrWhiteSpace(description) ? "(sin descripción)" : description);

        var avisos = new List<string>();
        var adjuntosTexto = 0;
        var sugerenciasManual = new List<string>();
        if (fields.TryGetProperty("attachment", out var atts) && atts.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in atts.EnumerateArray().Take(25))
            {
                var filename = a.TryGetProperty("filename", out var fn) ? fn.GetString() ?? "adjunto" : "adjunto";
                var contentUrl = a.TryGetProperty("content", out var cu) ? cu.GetString() : null;
                var ext = Path.GetExtension(filename).ToLowerInvariant();
                if (ext is ".txt" or ".md" or ".csv" or ".feature" or ".json" or ".log" or ".docx" or ".pdf")
                {
                    if (string.IsNullOrWhiteSpace(contentUrl))
                    {
                        sugerenciasManual.Add(filename);
                        continue;
                    }
                    var extraido = await DescargarAdjuntoJiraTextoAsync(contentUrl!, auth, filename);
                    if (extraido.Ok)
                    {
                        adjuntosTexto++;
                        sb.AppendLine();
                        sb.AppendLine($"--- Adjunto: {filename} ---");
                        sb.AppendLine(Truncar(extraido.Texto, 12000));
                    }
                    else
                    {
                        sugerenciasManual.Add($"{filename} ({extraido.Error})");
                    }
                }
                else if (ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp")
                {
                    sb.AppendLine();
                    if (string.IsNullOrWhiteSpace(contentUrl))
                    {
                        sb.AppendLine($"Adjunto imagen (sin URL de descarga): {filename}");
                        sugerenciasManual.Add(filename);
                        continue;
                    }

                    var bytes = await DescargarAdjuntoJiraBytesAsync(contentUrl!, auth);
                    if (bytes is not { Length: > 0 })
                    {
                        sb.AppendLine($"Adjunto imagen (sin bytes): {filename}");
                        sugerenciasManual.Add(filename);
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(llmApiKey))
                    {
                        var vision = await AprendizajeLlm.InterpretarImagenAsync(
                            bytes, filename, llmApiKey, llmBaseUrl, llmModel);
                        if (vision.Ok)
                        {
                            adjuntosTexto++;
                            sb.AppendLine($"--- Imagen interpretada: {filename} ---");
                            sb.AppendLine(vision.Descripcion);
                            avisos.Add($"Captura «{filename}» interpretada (visión LLM).");
                            continue;
                        }
                        if (!string.IsNullOrWhiteSpace(vision.Aviso))
                            avisos.Add(vision.Aviso);
                    }

                    var ocr = await OcrWindows.ExtraerTextoAsync(bytes, filename);
                    if (ocr.Ok)
                    {
                        adjuntosTexto++;
                        sb.AppendLine($"--- Imagen interpretada: {filename} ---");
                        sb.AppendLine(ocr.Texto);
                        avisos.Add($"Captura «{filename}» leída con OCR local (sin LLM).");
                    }
                    else
                    {
                        sb.AppendLine($"Adjunto imagen (no interpretada): {filename}");
                        avisos.Add(string.IsNullOrWhiteSpace(ocr.Aviso)
                            ? $"Imagen «{filename}»: OCR sin texto útil. Pegá el texto o usá API key LLM (visión)."
                            : ocr.Aviso);
                    }
                }
                else
                {
                    sugerenciasManual.Add($"{filename} (formato {ext} no legible acá)");
                }
            }
        }

        if (adjuntosTexto > 0)
            avisos.Add($"Se leyó el contenido de {adjuntosTexto} adjunto(s) de texto desde Jira.");
        if (sugerenciasManual.Count > 0)
        {
            avisos.Add(
                "Sugerencia: descargá desde Jira y subí manualmente en Documentación (o convertí a .txt/.md/.docx/.pdf): "
                + string.Join("; ", sugerenciasManual.Take(6)) + ".");
        }

        var comentariosFuncionales = 0;
        var comentariosOmitidos = 0;
        if (fields.TryGetProperty("comment", out var comments)
            && comments.TryGetProperty("comments", out var list)
            && list.ValueKind == JsonValueKind.Array)
        {
            var todos = list.EnumerateArray().ToList();
            var funcionales = new List<string>();
            foreach (var c in todos)
            {
                var bodyC = c.TryGetProperty("body", out var b) ? ExtraerAdfOTexto(b) : "";
                if (string.IsNullOrWhiteSpace(bodyC)) continue;
                if (EsComentarioFuncionalJira(bodyC))
                    funcionales.Add(Truncar(bodyC.Trim(), 1500));
                else
                    comentariosOmitidos++;
            }
            comentariosFuncionales = funcionales.Count;
            if (funcionales.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Comentarios funcionales:");
                foreach (var c in funcionales.Take(12))
                    sb.AppendLine("- " + c);
            }
        }

        if (comentariosFuncionales > 0)
            avisos.Add($"Se usaron {comentariosFuncionales} comentario(s) funcionales (se omitieron {comentariosOmitidos} no funcionales).");
        else if (comentariosOmitidos > 0)
            avisos.Add($"Había {comentariosOmitidos} comentario(s), pero ninguno se consideró funcional (criterios/pasos/resultado).");

        var modulo = InferirModuloDesdeTexto(summary, sb.ToString(), key);
        if (componentes.Count > 0 && !string.IsNullOrWhiteSpace(componentes[0]))
            modulo = Truncar(componentes[0].Trim(), 60);

        var detalle = GenerarDetalleDesdeFuentes(
            string.IsNullOrWhiteSpace(key) ? summary : $"{key} — {summary}",
            sb.ToString(),
            key,
            issueType);

        var aviso = string.Join(" ", avisos);
        return (true, key, sb.ToString(), "", aviso, summary, status, modulo, issueType ?? "", detalle);
    }

    private static async Task<(bool Ok, string Texto, string Error)> DescargarAdjuntoJiraTextoAsync(
        string contentUrl,
        AuthenticationHeaderValue auth,
        string filename)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, contentUrl);
            req.Headers.Authorization = auth;
            using var res = await Http.SendAsync(req);
            if (!res.IsSuccessStatusCode)
                return (false, "", $"HTTP {(int)res.StatusCode}");

            await using var stream = await res.Content.ReadAsStreamAsync();
            var ext = Path.GetExtension(filename).ToLowerInvariant();
            if (ext is ".txt" or ".md" or ".csv" or ".feature" or ".json" or ".log")
            {
                using var sr = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                var t = await sr.ReadToEndAsync();
                return string.IsNullOrWhiteSpace(t) ? (false, "", "vacío") : (true, t, "");
            }
            if (ext == ".docx")
            {
                var t = ExtraerDocx(stream);
                return string.IsNullOrWhiteSpace(t) ? (false, "", "docx sin texto") : (true, t, "");
            }
            if (ext == ".pdf")
            {
                var t = ExtraerPdf(stream);
                return string.IsNullOrWhiteSpace(t)
                    ? (false, "", "pdf sin texto extraíble")
                    : (true, t, "");
            }
            return (false, "", "formato no soportado");
        }
        catch (Exception ex)
        {
            return (false, "", ex.Message);
        }
    }

    private static async Task<byte[]?> DescargarAdjuntoJiraBytesAsync(
        string contentUrl,
        AuthenticationHeaderValue auth)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, contentUrl);
            req.Headers.Authorization = auth;
            using var res = await Http.SendAsync(req);
            if (!res.IsSuccessStatusCode) return null;
            return await res.Content.ReadAsByteArrayAsync();
        }
        catch
        {
            return null;
        }
    }

    private static bool EsComentarioFuncionalJira(string texto)
    {
        var t = (texto ?? "").Trim();
        if (t.Length < 25) return false;
        if (Regex.IsMatch(t, @"^(ok|oki|lgtm|gracias|thanks|bump|visto|👍|➕|revisado)\b", RegexOptions.IgnoreCase))
            return false;
        if (Regex.IsMatch(t, @"^(merged|done|listo|aprobado)\.?$", RegexOptions.IgnoreCase))
            return false;
        // Comentarios medianos/largos suelen aportar contexto aunque no tengan keyword exacta.
        if (t.Length >= 120) return true;
        return ContieneAlguno(t.ToLowerInvariant(),
            "criterio", "aceptación", "aceptacion", "acceptance", "paso", "debe ", "validar", "verificar",
            "escenario", "caso de", "caso:", "flujo", "given", "when", "then", "precond", "resultado",
            "pantalla", "usuario", "navega", "ingresa", "selecciona", "mensaje", "botón", "boton",
            "caja", "sucursal", "importe", "error", "funcional", "hu-", "ac-", "ac:", "dado que",
            "cuando ", "entonces", "esperado", "reproduc", "bug", "falla", "incorrecto", "mostrar",
            "aparece", "queda", "guardar", "procesar");
    }

    private static string InferirModuloDesdeTexto(string? titulo, string? texto, string? ticket)
    {
        var blob = ((titulo ?? "") + "\n" + (texto ?? "")).ToLowerInvariant();
        // Preferir ids de runners existentes (el front los matchea en la lista completa).
        if (EsDominioFallasCaja(blob) || ContieneAlguno(blob, "falla de caja", "fallas de caja"))
            return "TA- Fallas de Caja";
        if (ContieneAlguno(blob, "otros ingresos", "otro ingreso")) return "Otros Ingresos";
        if (ContieneAlguno(blob, "intercaja", "pase intercaja")) return "intercaja";
        if (ContieneAlguno(blob, "caja-bóveda", "caja boveda", "pase a bóveda", "pase a boveda")) return "caja-boveda";
        if (ContieneAlguno(blob, "alta de caja", "alta caja", "abrir caja", "apertura de caja")) return "alta-caja";
        if (ContieneAlguno(blob, "cierre", "cuadre")) return "cierre-cuadre";
        if (ContieneAlguno(blob, "cheque", "depósito de cheque", "deposito de cheque", "interdepósito", "interdeposito")) return "cheques";
        if (ContieneAlguno(blob, "parametría", "parametria", "perfil contable", "sc-161")) return "parametria-sc161";
        if (ContieneAlguno(blob, "regresión", "regresion")) return "regresion";

        var baseTitulo = (titulo ?? "").Trim();
        baseTitulo = Regex.Replace(baseTitulo, @"^\s*\[?[A-Z][A-Z0-9]+-\d+\]?\s*[-—:]?\s*", "");
        if (baseTitulo.Length > 3)
            return Truncar(baseTitulo, 50);

        return string.IsNullOrWhiteSpace(ticket) ? "Desde Jira" : $"Ticket {ticket}";
    }

    private static List<(string Nombre, string Bloque)> ExtraerEscenariosDesdeTexto(string texto)
    {
        var t = (texto ?? "").Replace("\r\n", "\n");
        if (string.IsNullOrWhiteSpace(t))
            return [];

        var matches = Regex.Matches(t,
            @"(?im)^(?:#{1,3}\s*)?(?:escenario|caso(?:\s+de\s+prueba)?|scenario|ca)\s*[-_]?\s*(\d+|[A-Z]?\d+)\s*[:.\-–—)]\s*(.*)$");
        if (matches.Count < 2)
        {
            // Criterios de aceptación numerados densos
            var ac = Regex.Matches(t, @"(?im)^(?:#{1,3}\s*)?(?:criterio(?:s)?\s+de\s+aceptaci[oó]n|acceptance\s+criteria)\b.*$");
            if (ac.Count == 0)
                return [("Escenario generado", t)];
        }

        if (matches.Count == 0)
            return [("Escenario generado", t)];

        var result = new List<(string, string)>();
        for (var i = 0; i < matches.Count; i++)
        {
            var m = matches[i];
            var nombreExtra = (m.Groups[2].Value ?? "").Trim();
            var nombre = string.IsNullOrWhiteSpace(nombreExtra)
                ? $"Escenario {m.Groups[1].Value}"
                : Truncar($"Escenario {m.Groups[1].Value}: {nombreExtra}", 70);
            var start = m.Index;
            var end = i + 1 < matches.Count ? matches[i + 1].Index : t.Length;
            var bloque = t[start..end].Trim();
            if (bloque.Length >= 20)
                result.Add((nombre, bloque));
        }

        if (result.Count < 2)
            return [("Escenario generado", t)];

        // Misma info repetida (p. ej. PDF + DOCX en el corpus) → un escenario por caso.
        var dedup = DeduplicarEscenariosDetectados(result);
        return dedup.Count >= 2 ? dedup.Take(8).ToList() : [("Escenario generado", t)];
    }

    private static List<(string Nombre, string Bloque)> DeduplicarEscenariosDetectados(
        List<(string Nombre, string Bloque)> escenarios)
    {
        var kept = new List<(string Nombre, string Bloque)>();
        foreach (var e in escenarios)
        {
            var finger = FingerprintTextoDocumentacion(e.Bloque);
            if (kept.Any(k =>
                    SonTextosDocumentacionEquivalentes(FingerprintTextoDocumentacion(k.Bloque), finger)))
                continue;
            kept.Add(e);
        }
        return kept;
    }

    private static string ExtraerAdfOTexto(JsonElement el)
    {
        if (el.ValueKind == JsonValueKind.String)
            return el.GetString() ?? "";
        if (el.ValueKind != JsonValueKind.Object && el.ValueKind != JsonValueKind.Array)
            return "";

        var sb = new StringBuilder();
        void Walk(JsonElement node, string listPrefix = "")
        {
            if (node.ValueKind == JsonValueKind.Array)
            {
                foreach (var child in node.EnumerateArray())
                    Walk(child, listPrefix);
                return;
            }
            if (node.ValueKind != JsonValueKind.Object) return;

            var type = node.TryGetProperty("type", out var tp) ? tp.GetString() ?? "" : "";

            if (type is "paragraph" or "heading")
            {
                if (sb.Length > 0 && sb[^1] != '\n') sb.AppendLine();
                if (node.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.Array)
                {
                    foreach (var child in c.EnumerateArray())
                        Walk(child, listPrefix);
                }
                sb.AppendLine();
                return;
            }

            if (type is "bulletList" or "orderedList")
            {
                var i = 1;
                if (node.TryGetProperty("content", out var items) && items.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in items.EnumerateArray())
                    {
                        var prefix = type == "orderedList" ? $"{i}. " : "- ";
                        i++;
                        if (sb.Length > 0 && sb[^1] != '\n') sb.AppendLine();
                        sb.Append(prefix);
                        Walk(item, prefix);
                        if (sb.Length > 0 && sb[^1] != '\n') sb.AppendLine();
                    }
                }
                return;
            }

            if (type == "listItem")
            {
                if (node.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.Array)
                {
                    foreach (var child in c.EnumerateArray())
                        Walk(child, listPrefix);
                }
                return;
            }

            if (type == "hardBreak")
            {
                sb.AppendLine();
                return;
            }

            if (type is "mediaSingle" or "media" or "mediaInline")
            {
                sb.Append(" [imagen adjunta] ");
                return;
            }

            if (node.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
            {
                var txt = t.GetString() ?? "";
                if (!string.IsNullOrEmpty(txt)) sb.Append(txt);
            }

            if (node.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
            {
                foreach (var child in content.EnumerateArray())
                    Walk(child, listPrefix);
            }
        }

        Walk(el);
        var result = Regex.Replace(sb.ToString(), @"[ \t]+\n", "\n");
        result = Regex.Replace(result, @"\n{3,}", "\n\n");
        return result.Trim();
    }

    private static string? ExtraerTicketKey(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        // Case-insensitive: sc-470 / SC-470; también selectedIssue=SC-470 en boards.
        var m = Regex.Match(text, @"\b([A-Za-z][A-Za-z0-9]+-\d+)\b");
        return m.Success ? m.Groups[1].Value.ToUpperInvariant() : null;
    }

    private static string? ExtraerJiraBase(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        if (!uri.Host.Contains("atlassian", StringComparison.OrdinalIgnoreCase)
            && !uri.AbsolutePath.Contains("/browse/", StringComparison.OrdinalIgnoreCase))
        {
            // Permitir hosts Jira Server/DC propios
            if (uri.Scheme is not ("http" or "https")) return null;
        }
        return $"{uri.Scheme}://{uri.Authority}";
    }

    private static string InferirTitulo(string texto, string? ticket)
    {
        var m = Regex.Match(texto, @"(?im)^(?:t[ií]tulo|summary|feature)\s*[:\-]\s*(.+)$");
        if (m.Success) return Truncar(m.Groups[1].Value.Trim(), 120);
        var first = texto.Replace("\r\n", "\n").Split('\n')
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.Length > 15 && !l.StartsWith('['));
        if (!string.IsNullOrWhiteSpace(first))
            return Truncar((ticket is null ? "" : ticket + " — ") + first, 120);
        return ticket is null ? "Caso E2E" : $"{ticket} — caso E2E";
    }

    private static string SanitizarNombreFeature(string? nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre))
            nombre = "Prueba_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".feature";
        // Legado: no generar ni conservar prefijo Prueba_ en el nombre visible del archivo
        var fn = Path.GetFileName(nombre.Trim());
        if (fn.StartsWith("Prueba_", StringComparison.OrdinalIgnoreCase))
            fn = fn["Prueba_".Length..];
        if (string.IsNullOrWhiteSpace(fn) || fn.Equals(".feature", StringComparison.OrdinalIgnoreCase))
            fn = "Prueba_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".feature";
        return RunnerSecurity.NombreFeatureSeguro(fn);
    }

    /// <summary>
    /// Título corto legible: sin duplicar ticket, corte en límite de palabra.
    /// </summary>
    private static string ArmarTituloCorto(string? tituloBase, string? ticket, int maxTotal)
    {
        var baseT = QuitarTicketDelTitulo(tituloBase ?? "", ticket);
        baseT = Regex.Replace(baseT, @"^\[[^\]]+\]\s*", "").Trim(); // [Cajero], [QA], etc.
        if (string.IsNullOrWhiteSpace(ticket))
            return string.IsNullOrWhiteSpace(baseT) ? "Caso E2E" : AcortarTitulo(baseT, maxTotal);

        var reserved = ticket.Length + 3; // "SC-161 — "
        var maxBase = Math.Max(28, maxTotal - reserved);
        baseT = AcortarTitulo(baseT, maxBase);
        if (string.IsNullOrWhiteSpace(baseT))
            return ticket;
        return $"{ticket} — {baseT}";
    }

    private static string QuitarTicketDelTitulo(string titulo, string? ticket)
    {
        if (string.IsNullOrWhiteSpace(titulo)) return "";
        var t = Regex.Replace(titulo.Trim(), @"\s+", " ");
        if (string.IsNullOrWhiteSpace(ticket)) return t;
        t = Regex.Replace(t, @"^" + Regex.Escape(ticket) + @"\s*[—\-–:|]\s*", "", RegexOptions.IgnoreCase).Trim();
        t = Regex.Replace(t, @"\b" + Regex.Escape(ticket) + @"\b\s*[—\-–:]?\s*", "", RegexOptions.IgnoreCase).Trim();
        return t;
    }

    private static string AcortarTitulo(string? s, int max)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var t = Regex.Replace(s.Trim(), @"\s+", " ");
        if (max < 8) max = 8;
        if (t.Length <= max) return t;

        var corte = t[..max];
        var lastBreak = corte.LastIndexOfAny([' ', '-', '—', '–', ',', ';', ':', '/', '|']);
        if (lastBreak >= Math.Max(12, max / 2))
            corte = corte[..lastBreak];
        return corte.TrimEnd(' ', '-', '—', '–', ',', ';', ':', '/', '|', '.') + "…";
    }

    private static bool ContieneAlguno(string texto, params string[] needles)
    {
        var lower = texto.ToLowerInvariant();
        return needles.Any(n => lower.Contains(n.ToLowerInvariant(), StringComparison.Ordinal));
    }

    private static string Truncar(string s, int max) =>
        string.IsNullOrEmpty(s) || s.Length <= max ? s : AcortarTitulo(s, max);

    private sealed record FuenteTexto(string Origen, string Texto);
    private sealed record StepInfo(string Texto, List<string> Tokens);
}
