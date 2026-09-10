using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

/// <summary>
/// Perfiles de proyecto + nombre de IA (RunnerIA). Catálogo por proyecto.
/// </summary>
public static class RunnerIaProyectos
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static void MapEndpoints(WebApplication app, string automatizacionRoot)
    {
        AsegurarArchivo(automatizacionRoot);

        app.MapGet("/runner-ia/proyecto", () =>
        {
            var doc = Leer(automatizacionRoot);
            var activo = doc.Proyectos.FirstOrDefault(p =>
                string.Equals(p.Id, doc.ProyectoActivoId, StringComparison.OrdinalIgnoreCase))
                ?? doc.Proyectos.FirstOrDefault();
            return Results.Ok(new
            {
                ok = true,
                iaAlcance = "runner-producto",
                iaAlcanceTexto =
                    "IA limitada al Runner: Analizar/Generar borradores E2E, OCR, catálogo del proyecto. " +
                    "No es un asistente general ni ejecuta comandos libres.",
                intranetAviso =
                    "Uso pensado para intranet. No exponer a Internet sin PIN, allowlist y HTTPS.",
                proyectoActivoId = doc.ProyectoActivoId,
                proyectoActivo = activo,
                proyectos = doc.Proyectos,
                checklistDemo = ChecklistDemo()
            });
        });

        app.MapPost("/runner-ia/proyecto", async (HttpRequest request) =>
        {
            try
            {
                using var reader = new StreamReader(request.Body, Encoding.UTF8);
                var raw = await reader.ReadToEndAsync();
                using var json = JsonDocument.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw);
                var root = json.RootElement;
                var doc = Leer(automatizacionRoot);

                if (root.TryGetProperty("proyectoActivoId", out var pid))
                {
                    var id = (pid.GetString() ?? "").Trim();
                    if (!doc.Proyectos.Any(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)))
                        return Results.Json(new { ok = false, error = "Proyecto no encontrado." }, statusCode: 400);
                    doc.ProyectoActivoId = id;
                }

                if (root.TryGetProperty("iaNombre", out var ian))
                {
                    var nombre = (ian.GetString() ?? "").Trim();
                    if (nombre.Length is < 2 or > 40)
                        return Results.Json(new { ok = false, error = "Nombre de IA: 2–40 caracteres." }, statusCode: 400);
                    var activo = doc.Proyectos.FirstOrDefault(p =>
                        string.Equals(p.Id, doc.ProyectoActivoId, StringComparison.OrdinalIgnoreCase));
                    if (activo != null) activo.IaNombre = nombre;
                }

                if (root.TryGetProperty("agregar", out var ag) && ag.ValueKind == JsonValueKind.Object)
                {
                    var nuevo = new RunnerProyecto
                    {
                        Id = SanitizeId(ag.TryGetProperty("id", out var i) ? i.GetString() : null),
                        Nombre = (ag.TryGetProperty("nombre", out var n) ? n.GetString() : null)?.Trim() ?? "",
                        IaNombre = (ag.TryGetProperty("iaNombre", out var ia) ? ia.GetString() : null)?.Trim()
                                   ?? "RunnerIA",
                        Descripcion = (ag.TryGetProperty("descripcion", out var d) ? d.GetString() : null)?.Trim()
                                      ?? "",
                        CatalogoConfigRelativo = (ag.TryGetProperty("catalogoConfigRelativo", out var c)
                            ? c.GetString()
                            : null)?.Trim()
                            ?? "Catalogo/repos-celula.json",
                        Plantilla = NormalizarPlantilla(ag.TryGetProperty("plantilla", out var pl) ? pl.GetString() : null),
                        Integraciones = LeerIntegraciones(ag)
                    };
                    if (string.Equals(nuevo.Plantilla, "sot", StringComparison.OrdinalIgnoreCase))
                        nuevo.CatalogoConfigRelativo = "Catalogo/repos-celula.json";
                    if (string.IsNullOrWhiteSpace(nuevo.Id))
                        nuevo.Id = "proj-" + Guid.NewGuid().ToString("N")[..8];
                    if (string.IsNullOrWhiteSpace(nuevo.Nombre))
                        nuevo.Nombre = nuevo.Id;
                    if (doc.Proyectos.Any(p => string.Equals(p.Id, nuevo.Id, StringComparison.OrdinalIgnoreCase)))
                        return Results.Json(new { ok = false, error = "Ya existe un proyecto con ese id." }, statusCode: 400);
                    // Validar que el relativo no salga del root
                    var full = Path.GetFullPath(Path.Combine(automatizacionRoot, nuevo.CatalogoConfigRelativo.Replace('/', Path.DirectorySeparatorChar)));
                    if (!full.StartsWith(Path.GetFullPath(automatizacionRoot), StringComparison.OrdinalIgnoreCase))
                        return Results.Json(new { ok = false, error = "Ruta de catálogo fuera del proyecto." }, statusCode: 400);
                    doc.Proyectos.Add(nuevo);
                    doc.ProyectoActivoId = nuevo.Id;
                }

                if (root.TryGetProperty("modificar", out var mod) && mod.ValueKind == JsonValueKind.Object)
                {
                    var id = (mod.TryGetProperty("id", out var mid) ? mid.GetString() : null)?.Trim() ?? "";
                    if (string.IsNullOrWhiteSpace(id))
                        return Results.Json(new { ok = false, error = "Falta id del proyecto a modificar." }, statusCode: 400);
                    var existente = doc.Proyectos.FirstOrDefault(p =>
                        string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
                    if (existente == null)
                        return Results.Json(new { ok = false, error = "Proyecto no encontrado." }, statusCode: 400);

                    if (mod.TryGetProperty("nombre", out var mn))
                    {
                        var nombre = (mn.GetString() ?? "").Trim();
                        if (nombre.Length is < 1 or > 80)
                            return Results.Json(new { ok = false, error = "Nombre: 1–80 caracteres." }, statusCode: 400);
                        existente.Nombre = nombre;
                    }
                    if (mod.TryGetProperty("descripcion", out var md))
                        existente.Descripcion = (md.GetString() ?? "").Trim();
                    if (mod.TryGetProperty("iaNombre", out var mia))
                    {
                        var iaNombre = (mia.GetString() ?? "").Trim();
                        if (iaNombre.Length is < 2 or > 40)
                            return Results.Json(new { ok = false, error = "Nombre de IA: 2–40 caracteres." }, statusCode: 400);
                        existente.IaNombre = iaNombre;
                    }
                    if (mod.TryGetProperty("catalogoConfigRelativo", out var mcat))
                    {
                        var rel = (mcat.GetString() ?? "").Trim();
                        if (string.IsNullOrWhiteSpace(rel))
                            return Results.Json(new { ok = false, error = "Ruta de catálogo vacía." }, statusCode: 400);
                        var fullMod = Path.GetFullPath(Path.Combine(automatizacionRoot, rel.Replace('/', Path.DirectorySeparatorChar)));
                        if (!fullMod.StartsWith(Path.GetFullPath(automatizacionRoot), StringComparison.OrdinalIgnoreCase))
                            return Results.Json(new { ok = false, error = "Ruta de catálogo fuera del proyecto." }, statusCode: 400);
                        existente.CatalogoConfigRelativo = rel;
                    }
                    if (mod.TryGetProperty("plantilla", out var mpl))
                        existente.Plantilla = NormalizarPlantilla(mpl.GetString());
                    if (mod.TryGetProperty("integraciones", out var mint) && mint.ValueKind == JsonValueKind.Array)
                        existente.Integraciones = LeerIntegraciones(mod);
                }

                if (root.TryGetProperty("eliminar", out var del))
                {
                    var idDel = del.ValueKind == JsonValueKind.String
                        ? (del.GetString() ?? "").Trim()
                        : del.TryGetProperty("id", out var did) ? (did.GetString() ?? "").Trim() : "";
                    if (string.IsNullOrWhiteSpace(idDel))
                        return Results.Json(new { ok = false, error = "Falta id del proyecto a eliminar." }, statusCode: 400);
                    if (doc.Proyectos.Count <= 1)
                        return Results.Json(new { ok = false, error = "No se puede eliminar el único proyecto." }, statusCode: 400);
                    var idx = doc.Proyectos.FindIndex(p =>
                        string.Equals(p.Id, idDel, StringComparison.OrdinalIgnoreCase));
                    if (idx < 0)
                        return Results.Json(new { ok = false, error = "Proyecto no encontrado." }, statusCode: 400);
                    doc.Proyectos.RemoveAt(idx);
                    if (string.Equals(doc.ProyectoActivoId, idDel, StringComparison.OrdinalIgnoreCase))
                        doc.ProyectoActivoId = doc.Proyectos[0].Id;
                }

                Guardar(automatizacionRoot, doc);
                RunnerAudit.Log(automatizacionRoot, "proyecto", $"activo={doc.ProyectoActivoId}");
                var act = doc.Proyectos.FirstOrDefault(p =>
                    string.Equals(p.Id, doc.ProyectoActivoId, StringComparison.OrdinalIgnoreCase));
                return Results.Ok(new
                {
                    ok = true,
                    mensaje = "Proyecto RunnerIA actualizado.",
                    proyectoActivoId = doc.ProyectoActivoId,
                    proyectoActivo = act,
                    proyectos = doc.Proyectos
                });
            }
            catch (Exception ex)
            {
                return Results.Json(new { ok = false, error = ex.Message }, statusCode: 400);
            }
        });
    }

    /// <summary>Proyecto activo o el id indicado.</summary>
    public static RunnerProyecto? ResolverProyecto(string automatizacionRoot, string? proyectoId = null)
    {
        var doc = Leer(automatizacionRoot);
        if (!string.IsNullOrWhiteSpace(proyectoId))
        {
            return doc.Proyectos.FirstOrDefault(p =>
                string.Equals(p.Id, proyectoId.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        return doc.Proyectos.FirstOrDefault(p =>
                   string.Equals(p.Id, doc.ProyectoActivoId, StringComparison.OrdinalIgnoreCase))
               ?? doc.Proyectos.FirstOrDefault();
    }

    public static string RutaConfigCatalogoActivo(string automatizacionRoot)
    {
        var p = ResolverProyecto(automatizacionRoot);
        var rel = p?.CatalogoConfigRelativo ?? "Catalogo/repos-celula.json";
        rel = rel.Replace('/', Path.DirectorySeparatorChar).TrimStart('\\', '/');
        var full = Path.GetFullPath(Path.Combine(automatizacionRoot, rel));
        var root = Path.GetFullPath(automatizacionRoot);
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            return Path.Combine(automatizacionRoot, "Catalogo", "repos-celula.json");
        return full;
    }

    public static string? NombreIaActiva(string automatizacionRoot)
    {
        var doc = Leer(automatizacionRoot);
        return doc.Proyectos.FirstOrDefault(p =>
            string.Equals(p.Id, doc.ProyectoActivoId, StringComparison.OrdinalIgnoreCase))?.IaNombre
               ?? "RunnerIA";
    }

    public static object ChecklistDemo() => new[]
    {
        new { id = "jira", texto = "Jira: email + API token y un ticket SC de prueba (texto + captura)." },
        new { id = "sync", texto = "Catálogo: Sincronizar repos (develop) OK." },
        new { id = "detalle", texto = "Detalle: escribí en claro; la IA mapea a Gherkin y si falta el step lo crea al Guardar." },
        new { id = "mapa", texto = "RunnerIA: Reentrenar mapa UI + DB (módulos, XPath, botones, COBIS/SQL SOT)." },
        new { id = "fallas", texto = "Fallas de caja: saldo → falla → verificar montos en TM + tira auditora." },
        new { id = "pin", texto = "PIN: caduca a los 3 meses; «Me olvidé» envía clave no-reply y pide confirmar PIN nuevo." },
        new { id = "db", texto = "Datos: en Detalle podés pedir verificar conexión y SELECT a COBIS (Sybase) y/o SQL SOT (solo lectura; secrets en Config)." },
        new { id = "dev", texto = "Ambiente DEV + usuario SOT con contraseña en secrets." },
        new { id = "ocr", texto = "Analizar sin LLM (OCR local) sobre una captura." },
        new { id = "llm", texto = "Opcional: Analizar con LLM si hay API key (usa el mapa entrenado)." },
        new { id = "guardar", texto = "Guardar borrador en módulo y ejecutarlo desde Runner." },
        new { id = "pin", texto = "PIN del Runner configurado (auth local)." }
    };

    private static void AsegurarArchivo(string automatizacionRoot)
    {
        var path = RutaArchivo(automatizacionRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path)) return;
        Guardar(automatizacionRoot, new RunnerProyectosDoc
        {
            ProyectoActivoId = "sot-caja",
            Proyectos =
            [
                new RunnerProyecto
                {
                    Id = "sot-caja",
                    Nombre = "SOT",
                    IaNombre = "RunnerIA",
                    Descripcion =
                        "Automatización E2E de caja bancaria SOT (apertura, cierre, pases, cheques, parametría).",
                    CatalogoConfigRelativo = "Catalogo/repos-celula.json",
                    Plantilla = "sot"
                }
            ]
        });
    }

    private static string RutaArchivo(string automatizacionRoot) =>
        Path.Combine(automatizacionRoot, "Catalogo", "proyectos-runner.json");

    private static RunnerProyectosDoc Leer(string automatizacionRoot)
    {
        AsegurarArchivo(automatizacionRoot);
        try
        {
            var doc = JsonSerializer.Deserialize<RunnerProyectosDoc>(
                File.ReadAllText(RutaArchivo(automatizacionRoot)), JsonOpts);
            if (doc == null || doc.Proyectos.Count == 0)
                throw new InvalidOperationException("vacío");
            if (string.IsNullOrWhiteSpace(doc.ProyectoActivoId))
                doc.ProyectoActivoId = doc.Proyectos[0].Id;
            return doc;
        }
        catch
        {
            AsegurarArchivo(automatizacionRoot);
            return JsonSerializer.Deserialize<RunnerProyectosDoc>(
                File.ReadAllText(RutaArchivo(automatizacionRoot)), JsonOpts)!;
        }
    }

    private static void Guardar(string automatizacionRoot, RunnerProyectosDoc doc)
    {
        File.WriteAllText(RutaArchivo(automatizacionRoot), JsonSerializer.Serialize(doc, JsonOpts),
            new UTF8Encoding(false));
    }

    private static string SanitizeId(string? id)
    {
        var s = (id ?? "").Trim().ToLowerInvariant();
        s = Regex.Replace(s, @"[^a-z0-9\-_]+", "-");
        return Regex.Replace(s, @"-+", "-").Trim('-');
    }

    private static string NormalizarPlantilla(string? plantilla) =>
        string.Equals(plantilla, "sot", StringComparison.OrdinalIgnoreCase) ? "sot" : "generico";

    private static List<ProyectoIntegracion>? LeerIntegraciones(JsonElement parent)
    {
        if (!parent.TryGetProperty("integraciones", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return null;
        var list = new List<ProyectoIntegracion>();
        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var id = (item.TryGetProperty("id", out var i) ? i.GetString() : null)?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(id))
                id = "int-" + Guid.NewGuid().ToString("N")[..6];
            list.Add(new ProyectoIntegracion
            {
                Id = id,
                Tipo = (item.TryGetProperty("tipo", out var t) ? t.GetString() : null)?.Trim() ?? "otro",
                Etiqueta = (item.TryGetProperty("etiqueta", out var e) ? e.GetString() : null)?.Trim(),
                Url = (item.TryGetProperty("url", out var u) ? u.GetString() : null)?.Trim(),
                Notas = (item.TryGetProperty("notas", out var n) ? n.GetString() : null)?.Trim()
            });
        }
        return list.Count > 0 ? list : null;
    }
}

public sealed class RunnerProyectosDoc
{
    public string ProyectoActivoId { get; set; } = "sot-caja";
    public List<RunnerProyecto> Proyectos { get; set; } = [];
}

public sealed class RunnerProyecto
{
    public string Id { get; set; } = "";
    public string Nombre { get; set; } = "";
    public string IaNombre { get; set; } = "RunnerIA";
    public string Descripcion { get; set; } = "";
    public string CatalogoConfigRelativo { get; set; } = "Catalogo/repos-celula.json";
    /// <summary>sot | generico — presets SOT vs integraciones propias.</summary>
    public string Plantilla { get; set; } = "generico";
    public List<ProyectoIntegracion>? Integraciones { get; set; }
}

public sealed class ProyectoIntegracion
{
    public string Id { get; set; } = "";
    public string Tipo { get; set; } = "otro";
    public string? Etiqueta { get; set; }
    public string? Url { get; set; }
    public string? Notas { get; set; }
}
