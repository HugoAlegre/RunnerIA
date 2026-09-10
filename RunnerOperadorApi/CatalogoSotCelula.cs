using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AutomatizacionSOT.Config;

/// <summary>
/// Catálogo de la célula SOT: sync (git checkout/pull develop) + índice.
/// Rutas locales y/o URL remota TFS; PAT en appsettings.secrets.json (CatalogoTfs).
/// </summary>
public static class CatalogoSotCelula
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly string[] DefaultExcludeDirNames =
    [
        "node_modules", "bin", "obj", ".git", "dist", "wwwroot", "coverage",
        "TestResults", ".vs", ".idea", "packages", "out", "build", "cache"
    ];

    private static readonly HashSet<string> SecretFileHints = new(StringComparer.OrdinalIgnoreCase)
    {
        "appsettings.secrets.json", ".env", ".env.local", "secrets.json", "credentials.json"
    };

    private const string TfsBase =
        "http://tfs2018:8080/tfs/Productos%20y%20Arquitectura/Accusys_Sistema_de_Cajas/_git/";

    public static void MapEndpoints(WebApplication app, string automatizacionRoot)
    {
        var dir = DirCatalogo(automatizacionRoot);
        Directory.CreateDirectory(dir);
        AsegurarConfigPorDefecto(automatizacionRoot);

        app.MapGet("/catalogo-sot", (HttpContext _) =>
        {
            var cfg = LeerConfig(automatizacionRoot);
            NormalizarConfig(cfg, automatizacionRoot);
            var cat = LeerCatalogo(automatizacionRoot);
            var (tfsUser, tfsPat) = LeerCredencialesTfs(automatizacionRoot);
            return Results.Ok(new
            {
                ok = true,
                config = new
                {
                    cfg.PermitirGitPull,
                    cfg.RamaObjetivo,
                    // No se exponen raicesPermitidas ni rutas reales al cliente.
                    tfsUsuario = tfsUser ?? "",
                    tienePatTfs = !string.IsNullOrWhiteSpace(tfsPat),
                    repos = cfg.Repos.Select(r =>
                    {
                        var ix = cat?.Repos?.FirstOrDefault(x =>
                            string.Equals(x.Id, r.Id, StringComparison.OrdinalIgnoreCase));
                        return new
                        {
                            r.Id,
                            r.Nombre,
                            r.Rol,
                            r.UrlRemota,
                            rutaEnmascarada = EnmascararRuta(r.Ruta, r.Id, automatizacionRoot),
                            usaCache = EsRutaCache(r.Ruta, automatizacionRoot),
                            existe = ix?.Existe ?? Directory.Exists(r.Ruta),
                            rama = ix?.Rama,
                            ramaObjetivo = ix?.RamaObjetivo,
                            commitCorto = ix?.CommitCorto,
                            atrasadoVsObjetivo = ix?.AtrasadoVsObjetivo,
                            okPull = ix?.OkPull,
                            aviso = ix?.Aviso,
                            estado = EstadoRepoUi(ix, r.Ruta),
                            conteos = ix == null
                                ? null
                                : new
                                {
                                    rutas = ix.RutasUi?.Count ?? 0,
                                    endpoints = ix.Endpoints?.Count ?? 0,
                                    modelos = ix.Modelos?.Count ?? 0,
                                    pantallas = ix.Pantallas?.Count ?? 0
                                }
                        };
                    })
                },
                catalogo = cat == null
                    ? null
                    : new
                    {
                        cat.GeneradoUtc,
                        cat.AmbienteFuente,
                        cat.ResumenCorto,
                        repos = cat.Repos.Select(r => new
                        {
                            r.Id,
                            r.Nombre,
                            r.Existe,
                            r.Rama,
                            r.RamaObjetivo,
                            r.CommitCorto,
                            r.AtrasadoVsObjetivo,
                            r.OkPull,
                            r.Aviso,
                            r.UrlRemota,
                            rutaEnmascarada = EnmascararRuta(r.Ruta, r.Id, automatizacionRoot),
                            usaCache = EsRutaCache(r.Ruta, automatizacionRoot),
                            estado = EstadoRepoUi(r, r.Ruta),
                            conteos = new
                            {
                                rutas = r.RutasUi?.Count ?? 0,
                                endpoints = r.Endpoints?.Count ?? 0,
                                modelos = r.Modelos?.Count ?? 0,
                                pantallas = r.Pantallas?.Count ?? 0
                            }
                        })
                    },
                rutaCatalogo = RutaCatalogo(automatizacionRoot),
                rutaConfig = RutaConfig(automatizacionRoot)
            });
        });

        app.MapGet("/catalogo-sot/resumen", (HttpContext _) =>
        {
            var texto = ObtenerTextoParaAnalizar(automatizacionRoot);
            if (string.IsNullOrWhiteSpace(texto))
                return Results.Json(new { ok = false, error = "No hay catálogo. Pulsá «Sincronizar repos célula SOT»." }, statusCode: 404);
            return Results.Ok(new { ok = true, texto, caracteres = texto.Length });
        });

        app.MapPost("/catalogo-sot/config", async (HttpRequest request) =>
        {
            try
            {
                using var reader = new StreamReader(request.Body, Encoding.UTF8);
                var raw = await reader.ReadToEndAsync();
                using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw);
                var root = doc.RootElement;

                var cfg = JsonSerializer.Deserialize<CatalogoReposConfig>(raw, JsonOpts)
                          ?? new CatalogoReposConfig();
                // La UI no envía rutas reales: conservar las del servidor o usar cache.
                FusionarRutasConConfigExistente(cfg, automatizacionRoot);
                NormalizarConfig(cfg, automatizacionRoot);
                ValidarRutasOThrow(cfg, automatizacionRoot);
                GuardarConfig(automatizacionRoot, cfg);

                // PAT / usuario TFS → secrets (vacío = mantener)
                if (root.TryGetProperty("tfsUsuario", out var ju) || root.TryGetProperty("tfsPat", out _))
                {
                    var user = root.TryGetProperty("tfsUsuario", out var u) ? (u.GetString() ?? "").Trim() : null;
                    var pat = root.TryGetProperty("tfsPat", out var p) ? (p.GetString() ?? "").Trim() : null;
                    GuardarCredencialesTfs(automatizacionRoot, user, pat);
                }

                var (_, savedPat) = LeerCredencialesTfs(automatizacionRoot);
                return Results.Ok(new
                {
                    ok = true,
                    mensaje = "Repos del catálogo guardados.",
                    tienePatTfs = !string.IsNullOrWhiteSpace(savedPat),
                    config = new
                    {
                        cfg.PermitirGitPull,
                        cfg.RamaObjetivo,
                        tfsUsuario = LeerCredencialesTfs(automatizacionRoot).Usuario ?? "",
                        tienePatTfs = !string.IsNullOrWhiteSpace(savedPat),
                        repos = cfg.Repos.Select(r => new
                        {
                            r.Id,
                            r.Nombre,
                            r.Rol,
                            r.UrlRemota,
                            rutaEnmascarada = EnmascararRuta(r.Ruta, r.Id, automatizacionRoot),
                            usaCache = EsRutaCache(r.Ruta, automatizacionRoot)
                        })
                    }
                });
            }
            catch (Exception ex)
            {
                return Results.Json(new { ok = false, error = ex.Message }, statusCode: 400);
            }
        });

        app.MapPost("/catalogo-sot/sync", async (HttpContext ctx) =>
        {
            try
            {
                var pull = true;
                if (ctx.Request.Query.TryGetValue("pull", out var p))
                    pull = !string.Equals(p.ToString(), "false", StringComparison.OrdinalIgnoreCase)
                           && p.ToString() != "0";

                var cat = await SincronizarEIndexarAsync(automatizacionRoot, pull, ctx.RequestAborted);
                RunnerAudit.Log(automatizacionRoot, "catalogo-sync", pull ? "pull+index" : "index-only");
                return Results.Ok(new
                {
                    ok = true,
                    mensaje = pull
                        ? "Repos sincronizados (git pull) e índice actualizado."
                        : "Índice actualizado sin git pull.",
                    generadoUtc = cat.GeneradoUtc,
                    resumenCorto = cat.ResumenCorto,
                    repos = cat.Repos.Select(r => new
                    {
                        r.Id,
                        r.Nombre,
                        r.Existe,
                        r.Rama,
                        r.RamaObjetivo,
                        r.CommitCorto,
                        r.AtrasadoVsObjetivo,
                        r.OkPull,
                        r.Aviso,
                        r.UrlRemota,
                        rutaEnmascarada = EnmascararRuta(r.Ruta, r.Id, automatizacionRoot),
                        usaCache = EsRutaCache(r.Ruta, automatizacionRoot),
                        estado = EstadoRepoUi(r, r.Ruta),
                        rutas = r.RutasUi?.Count ?? 0,
                        endpoints = r.Endpoints?.Count ?? 0,
                        modelos = r.Modelos?.Count ?? 0,
                        pantallas = r.Pantallas?.Count ?? 0
                    })
                });
            }
            catch (Exception ex)
            {
                return Results.Json(new { ok = false, error = ex.Message }, statusCode: 400);
            }
        });
    }

    public static string? ObtenerTextoParaAnalizar(string automatizacionRoot, int maxChars = 14000)
    {
        var cat = LeerCatalogo(automatizacionRoot);
        if (cat == null) return null;
        var sb = new StringBuilder();
        sb.AppendLine("CATÁLOGO CÉLULA SOT (código local DEV — no es QA)");
        sb.AppendLine($"GeneradoUtc: {cat.GeneradoUtc:o}");
        sb.AppendLine($"Fuente: {cat.AmbienteFuente}");
        sb.AppendLine();
        sb.AppendLine("Notas QA (sin acceso al código QA; comportamientos esperados):");
        foreach (var n in cat.NotasQa ?? [])
            sb.AppendLine("- " + n);
        sb.AppendLine();

        foreach (var r in cat.Repos ?? [])
        {
            sb.AppendLine($"## {r.Nombre} ({r.Id})");
            sb.AppendLine($"Ruta: {r.Ruta}");
            if (!string.IsNullOrWhiteSpace(r.UrlRemota))
                sb.AppendLine($"URL remota: {r.UrlRemota}");
            sb.AppendLine($"Git: rama={r.Rama ?? "—"} commit={r.CommitCorto ?? "—"} pullOk={r.OkPull}");
            if (!string.IsNullOrWhiteSpace(r.Aviso))
                sb.AppendLine("Aviso: " + r.Aviso);
            if (r.Pantallas is { Count: > 0 })
            {
                sb.AppendLine("Pantallas/componentes:");
                foreach (var x in r.Pantallas.Take(80))
                    sb.AppendLine("  - " + x);
            }
            if (r.RutasUi is { Count: > 0 })
            {
                sb.AppendLine("Rutas UI:");
                foreach (var x in r.RutasUi.Take(120))
                    sb.AppendLine("  - " + x);
            }
            if (r.Endpoints is { Count: > 0 })
            {
                sb.AppendLine("Endpoints API:");
                foreach (var x in r.Endpoints.Take(150))
                    sb.AppendLine("  - " + x);
            }
            if (r.Modelos is { Count: > 0 })
            {
                sb.AppendLine("Modelos:");
                foreach (var x in r.Modelos.Take(100))
                    sb.AppendLine("  - " + x);
            }
            sb.AppendLine();
        }

        var texto = sb.ToString();
        if (texto.Length <= maxChars) return texto;
        return texto[..maxChars] + "\n…[catálogo truncado]";
    }

    public static async Task<CatalogoSotDocumento> SincronizarEIndexarAsync(
        string automatizacionRoot,
        bool hacerPull,
        CancellationToken ct)
    {
        var cfg = LeerConfig(automatizacionRoot);
        NormalizarConfig(cfg, automatizacionRoot);
        ValidarRutasOThrow(cfg, automatizacionRoot);
        var (tfsUser, tfsPat) = LeerCredencialesTfs(automatizacionRoot);

        var doc = new CatalogoSotDocumento
        {
            GeneradoUtc = DateTimeOffset.UtcNow,
            AmbienteFuente = "DEV (clones locales / TFS)",
            NotasQa =
            [
                "En QA no hay acceso al código; se asume misma UI/flujos que DEV para caja, apertura, cierre, pases y cheques.",
                "QA requiere Auth OpenID (Configuración → Ambiente QA + permiso Auth QA).",
                "URLs y hosts SQL/Auth cambian por ambiente; la lógica de pantallas suele ser la misma."
            ],
            Repos = []
        };

        foreach (var repo in cfg.Repos)
        {
            ct.ThrowIfCancellationRequested();
            var item = new CatalogoRepoIndexado
            {
                Id = repo.Id,
                Nombre = repo.Nombre,
                Ruta = repo.Ruta,
                Rol = repo.Rol,
                UrlRemota = repo.UrlRemota,
                Existe = Directory.Exists(repo.Ruta)
            };

            if (!item.Existe)
            {
                if (!string.IsNullOrWhiteSpace(repo.UrlRemota) && hacerPull && cfg.PermitirGitPull)
                {
                    var clone = await GitCloneAsync(repo.UrlRemota!, repo.Ruta, tfsUser, tfsPat, ct);
                    item.Existe = Directory.Exists(repo.Ruta);
                    item.OkPull = clone.Ok;
                    if (!string.IsNullOrWhiteSpace(clone.Aviso))
                        item.Aviso = clone.Aviso;
                    if (!item.Existe)
                    {
                        doc.Repos.Add(item);
                        continue;
                    }
                }
                else
                {
                    item.Aviso = string.IsNullOrWhiteSpace(repo.UrlRemota)
                        ? "La carpeta no existe y no hay URL remota TFS."
                        : "La carpeta no existe. Activá git pull y sincronizá para clonar desde la URL.";
                    item.OkPull = false;
                    doc.Repos.Add(item);
                    continue;
                }
            }

            if (!Directory.Exists(Path.Combine(repo.Ruta, ".git")))
            {
                item.Aviso = string.IsNullOrWhiteSpace(item.Aviso)
                    ? "No es un repositorio git (.git ausente). Se indexa igual."
                    : item.Aviso;
            }
            else if (hacerPull && cfg.PermitirGitPull)
            {
                if (!string.IsNullOrWhiteSpace(repo.UrlRemota))
                    await AsegurarRemoteOriginAsync(repo.Ruta, repo.UrlRemota!, ct);

                var ramaObj = string.IsNullOrWhiteSpace(cfg.RamaObjetivo) ? "develop" : cfg.RamaObjetivo.Trim();
                var (ok, aviso) = await GitSyncRamaObjetivoAsync(repo.Ruta, ramaObj, tfsUser, tfsPat, ct);
                item.OkPull = ok;
                item.RamaObjetivo = ramaObj;
                if (!string.IsNullOrWhiteSpace(aviso))
                    item.Aviso = string.IsNullOrWhiteSpace(item.Aviso) ? aviso : item.Aviso + " · " + aviso;
            }
            else
            {
                item.OkPull = null;
                item.RamaObjetivo = string.IsNullOrWhiteSpace(cfg.RamaObjetivo) ? "develop" : cfg.RamaObjetivo.Trim();
                if (!cfg.PermitirGitPull)
                    item.Aviso = "Git pull desactivado en config.";
            }

            item.Rama = await GitOutAsync(repo.Ruta, "rev-parse --abbrev-ref HEAD", ct);
            var commit = await GitOutAsync(repo.Ruta, "rev-parse --short HEAD", ct);
            item.CommitCorto = string.IsNullOrWhiteSpace(commit) ? null : commit.Trim();
            var ramaObjFinal = item.RamaObjetivo ?? cfg.RamaObjetivo ?? "develop";
            item.AtrasadoVsObjetivo = await GitOutAsync(
                repo.Ruta, $"rev-list --count HEAD..origin/{ramaObjFinal}", ct);

            IndexarRepo(item);
            doc.Repos.Add(item);
        }

        doc.ResumenCorto = string.Join(" · ",
            doc.Repos.Select(r =>
                $"{r.Id}: {(r.Existe ? "ok" : "falta")} " +
                $"(UI {r.RutasUi?.Count ?? 0}/API {r.Endpoints?.Count ?? 0}/M {r.Modelos?.Count ?? 0})"));

        GuardarCatalogo(automatizacionRoot, doc);
        return doc;
    }

    private static void IndexarRepo(CatalogoRepoIndexado item)
    {
        item.RutasUi = [];
        item.Pantallas = [];
        item.Endpoints = [];
        item.Modelos = [];

        var id = (item.Id ?? "").ToLowerInvariant();
        var rol = (item.Rol ?? "").ToLowerInvariant();
        if (id.Contains("app-cashier") || id == "app" || rol.Contains("ui") || rol.Contains("angular"))
        {
            item.RutasUi = ExtraerRutasAngular(item.Ruta).Take(200).ToList();
            item.Pantallas = ExtraerPantallasAngular(item.Ruta).Take(150).ToList();
        }
        else if (id.Contains("api-cashier") || id.Contains("api-connector") || id.Contains("api")
                 || rol.Contains("api") || rol.Contains("endpoint"))
        {
            item.Endpoints = ExtraerEndpointsAspNet(item.Ruta).Take(250).ToList();
        }
        else if (id.Contains("models") || id.Contains("models-lib") || rol.Contains("modelo"))
        {
            item.Modelos = ExtraerModelosCs(item.Ruta).Take(200).ToList();
        }
        else
        {
            item.RutasUi = ExtraerRutasAngular(item.Ruta).Take(80).ToList();
            item.Endpoints = ExtraerEndpointsAspNet(item.Ruta).Take(80).ToList();
            item.Modelos = ExtraerModelosCs(item.Ruta).Take(80).ToList();
        }
    }

    private static List<string> ExtraerRutasAngular(string root)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in EnumerarArchivos(root, "*.ts"))
        {
            if (!file.Contains("route", StringComparison.OrdinalIgnoreCase)
                && !file.EndsWith("routes.ts", StringComparison.OrdinalIgnoreCase)
                && !file.Contains($"{Path.DirectorySeparatorChar}app.routes", StringComparison.OrdinalIgnoreCase))
                continue;

            var text = LeerTextoSeguro(file);
            foreach (Match m in Regex.Matches(text, @"path\s*:\s*['""]([^'""]+)['""]"))
            {
                var p = m.Groups[1].Value.Trim();
                if (string.IsNullOrWhiteSpace(p) || p == "**") continue;
                set.Add(p);
            }
        }
        return set.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static List<string> ExtraerPantallasAngular(string root)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in EnumerarArchivos(root, "*.ts"))
        {
            if (!file.Contains($"{Path.DirectorySeparatorChar}pages{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                && !file.Contains($"{Path.DirectorySeparatorChar}pages/", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!file.EndsWith(".component.ts", StringComparison.OrdinalIgnoreCase))
                continue;

            var name = Path.GetFileName(file);
            if (name.EndsWith(".component.ts", StringComparison.OrdinalIgnoreCase))
                name = name[..^".component.ts".Length];
            set.Add(name.Replace('-', ' '));
        }
        return set.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static List<string> ExtraerEndpointsAspNet(string root)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in EnumerarArchivos(root, "*Controller*.cs"))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}Test", StringComparison.OrdinalIgnoreCase)
                || file.Contains(".Test", StringComparison.OrdinalIgnoreCase)
                || file.Contains("Tests", StringComparison.OrdinalIgnoreCase))
                continue;

            var text = LeerTextoSeguro(file);
            var ctrl = Path.GetFileNameWithoutExtension(file);
            var routeCtrl = Regex.Match(text, @"\[Route\s*\(\s*""([^""]+)""\s*\)\]");
            var baseRoute = routeCtrl.Success ? routeCtrl.Groups[1].Value : ctrl.Replace("Controller", "", StringComparison.OrdinalIgnoreCase);

            foreach (Match m in Regex.Matches(
                         text,
                         @"\[Http(Get|Post|Put|Delete|Patch)\s*(?:\(\s*""([^""]*)""\s*\))?\s*\]",
                         RegexOptions.IgnoreCase))
            {
                var verb = m.Groups[1].Value.ToUpperInvariant();
                var sub = m.Groups[2].Success ? m.Groups[2].Value : "";
                var full = string.IsNullOrWhiteSpace(sub) ? $"{verb} {baseRoute}" : $"{verb} {baseRoute}/{sub}".Replace("//", "/");
                set.Add(full);
            }

            if (set.Count == 0)
                set.Add("CONTROLLER " + ctrl);
        }
        return set.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static List<string> ExtraerModelosCs(string root)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in EnumerarArchivos(root, "*.cs"))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!file.Contains("Model", StringComparison.OrdinalIgnoreCase)
                && !file.Contains($"{Path.DirectorySeparatorChar}Models{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                continue;

            var text = LeerTextoSeguro(file);
            foreach (Match m in Regex.Matches(text, @"\b(?:public\s+)?(?:partial\s+)?(?:record|class|enum)\s+(\w+)"))
            {
                var n = m.Groups[1].Value;
                if (n is "Program" or "Startup") continue;
                set.Add(n);
            }
        }
        return set.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static IEnumerable<string> EnumerarArchivos(string root, string pattern)
    {
        if (!Directory.Exists(root)) yield break;
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            string[] subs;
            try { subs = Directory.GetDirectories(dir); }
            catch { continue; }
            foreach (var sub in subs)
            {
                var name = Path.GetFileName(sub);
                if (DefaultExcludeDirNames.Any(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase)))
                    continue;
                stack.Push(sub);
            }

            string[] files;
            try { files = Directory.GetFiles(dir, pattern); }
            catch { continue; }
            foreach (var f in files)
            {
                var fn = Path.GetFileName(f);
                if (SecretFileHints.Contains(fn)) continue;
                yield return f;
            }
        }
    }

    private static string LeerTextoSeguro(string path)
    {
        try
        {
            var fi = new FileInfo(path);
            if (!fi.Exists || fi.Length > 2_000_000) return "";
            return File.ReadAllText(path);
        }
        catch { return ""; }
    }

    private static async Task<(bool Ok, string Aviso)> GitCloneAsync(
        string urlRemota, string destPath, string? user, string? pat, CancellationToken ct)
    {
        try
        {
            var parent = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrWhiteSpace(parent))
                Directory.CreateDirectory(parent);

            if (Directory.Exists(destPath) && Directory.EnumerateFileSystemEntries(destPath).Any())
                return (false, "Destino de clone no vacío.");

            var args = $"clone --branch develop --single-branch \"{urlRemota.Trim()}\" \"{destPath}\"";
            var run = await GitRunAsync(null, args, ct, user, pat);
            if (run.ExitCode != 0)
            {
                // Fallback sin --branch por si develop no es default remoto todavía
                if (Directory.Exists(destPath))
                {
                    try { Directory.Delete(destPath, true); } catch { /* ignore */ }
                }
                args = $"clone \"{urlRemota.Trim()}\" \"{destPath}\"";
                run = await GitRunAsync(null, args, ct, user, pat);
            }

            if (run.ExitCode != 0)
            {
                var hint = string.IsNullOrWhiteSpace(pat)
                    ? " Si Credential Manager no alcanza, cargá usuario + PAT TFS en Configuración."
                    : "";
                return (false, "git clone falló: " + Truncar(run.StdErrOrOut, 220) + hint);
            }
            return (true, "Clonado desde URL remota TFS.");
        }
        catch (Exception ex)
        {
            return (false, "Clone: " + ex.Message);
        }
    }

    private static async Task AsegurarRemoteOriginAsync(string repoPath, string urlRemota, CancellationToken ct)
    {
        var current = await GitOutAsync(repoPath, "remote get-url origin", ct);
        if (string.IsNullOrWhiteSpace(current))
            await GitRunAsync(repoPath, $"remote add origin \"{urlRemota.Trim()}\"", ct);
        else if (!string.Equals(current.Trim(), urlRemota.Trim(), StringComparison.OrdinalIgnoreCase))
            await GitRunAsync(repoPath, $"remote set-url origin \"{urlRemota.Trim()}\"", ct);
    }

    /// <summary>
    /// Fetch + checkout rama objetivo (default develop) + pull --ff-only origin/rama.
    /// </summary>
    private static async Task<(bool Ok, string Aviso)> GitSyncRamaObjetivoAsync(
        string repoPath, string rama, string? user, string? pat, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rama)) rama = "develop";
        rama = rama.Trim();

        var fetch = await GitRunAsync(repoPath, "fetch origin --prune", ct, user, pat);
        if (fetch.ExitCode != 0)
        {
            var hint = string.IsNullOrWhiteSpace(pat)
                ? " Revisá Credential Manager o cargá PAT TFS en Configuración."
                : "";
            return (false, "git fetch origin falló: " + Truncar(fetch.StdErrOrOut, 240) + hint);
        }

        var remoteOk = await GitOutAsync(repoPath, $"rev-parse --verify refs/remotes/origin/{rama}", ct);
        if (string.IsNullOrWhiteSpace(remoteOk))
            return (false, $"No existe origin/{rama}. Revisá el remoto TFS/Azure o cambiá ramaObjetivo.");

        var current = await GitOutAsync(repoPath, "rev-parse --abbrev-ref HEAD", ct) ?? "";
        if (!string.Equals(current, rama, StringComparison.OrdinalIgnoreCase))
        {
            var dirty = await GitOutAsync(repoPath, "status --porcelain", ct);
            if (!string.IsNullOrWhiteSpace(dirty))
            {
                return (false,
                    $"Estás en «{current}» con cambios locales; no se puede pasar a «{rama}». " +
                    "Hacé commit/stash o usá Solo reindexar.");
            }

            var co = await GitRunAsync(repoPath, $"checkout {rama}", ct);
            if (co.ExitCode != 0)
            {
                var track = await GitRunAsync(repoPath, $"checkout -B {rama} --track origin/{rama}", ct);
                if (track.ExitCode != 0)
                    return (false, $"No se pudo checkout {rama}: " + Truncar(track.StdErrOrOut, 220));
            }
        }

        var pull = await GitRunAsync(repoPath, $"pull --ff-only origin {rama}", ct, user, pat);
        if (pull.ExitCode != 0)
            return (false, $"git pull --ff-only origin/{rama} falló: " + Truncar(pull.StdErrOrOut, 240));

        var behind = await GitOutAsync(repoPath, $"rev-list --count HEAD..origin/{rama}", ct) ?? "?";
        var ahead = await GitOutAsync(repoPath, $"rev-list --count origin/{rama}..HEAD", ct) ?? "?";
        if (behind == "0" && ahead == "0")
            return (true, $"Sync {rama} OK · al día con origin/{rama}.");
        return (true, $"Sync {rama} OK · ahead={ahead} behind={behind} vs origin/{rama}.");
    }

    private static async Task<string?> GitOutAsync(string repoPath, string args, CancellationToken ct)
    {
        var r = await GitRunAsync(repoPath, args, ct);
        if (r.ExitCode != 0) return null;
        return string.IsNullOrWhiteSpace(r.StdOut) ? null : r.StdOut.Trim();
    }

    private static async Task<(int ExitCode, string StdOut, string StdErrOrOut)> GitRunAsync(
        string? repoPath,
        string args,
        CancellationToken ct,
        string? user = null,
        string? pat = null)
    {
        var fullArgs = string.IsNullOrWhiteSpace(repoPath)
            ? args
            : "-C \"" + repoPath.Replace("\"", "") + "\" " + args;

        var psi = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = fullArgs,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // PAT sin escribirlo en .git/config (mismo patrón que secrets enmascarados).
        if (!string.IsNullOrWhiteSpace(pat))
        {
            var u = string.IsNullOrWhiteSpace(user) ? "pat" : user.Trim();
            var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{u}:{pat.Trim()}"));
            psi.Environment["GIT_CONFIG_COUNT"] = "1";
            psi.Environment["GIT_CONFIG_KEY_0"] = "http.extraHeader";
            psi.Environment["GIT_CONFIG_VALUE_0"] = $"Authorization: Basic {token}";
        }

        using var p = new Process { StartInfo = psi };
        p.Start();
        var stdoutTask = p.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        var combined = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
        return (p.ExitCode, stdout, combined);
    }

    private static string DirCatalogo(string automatizacionRoot) =>
        Path.Combine(automatizacionRoot, "Catalogo");

    private static string RutaConfig(string automatizacionRoot) =>
        RunnerIaProyectos.RutaConfigCatalogoActivo(automatizacionRoot);

    private static string RutaCatalogo(string automatizacionRoot) =>
        Path.Combine(DirCatalogo(automatizacionRoot), "catalogo-sot.json");

    private static string RutaSecrets(string automatizacionRoot) =>
        Path.Combine(automatizacionRoot, "appsettings.secrets.json");

    private static string RutaCache(string automatizacionRoot) =>
        Path.Combine(DirCatalogo(automatizacionRoot), "cache");

    private static (string? Usuario, string? Pat) LeerCredencialesTfs(string automatizacionRoot)
    {
        try
        {
            var path = RutaSecrets(automatizacionRoot);
            if (!File.Exists(path)) return (null, null);
            var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
            var sec = root?["CatalogoTfs"] as JsonObject;
            var user = sec?["Usuario"]?.ToString();
            var pat = sec?["Pat"]?.ToString();
            if (string.IsNullOrWhiteSpace(user)) user = null;
            if (string.IsNullOrWhiteSpace(pat) || EsMascara(pat)) pat = null;
            else pat = SecretProtector.UnprotectFromStorage(pat.Trim());
            return (user, pat);
        }
        catch { return (null, null); }
    }

    private static void GuardarCredencialesTfs(string automatizacionRoot, string? usuario, string? pat)
    {
        var path = RutaSecrets(automatizacionRoot);
        JsonObject root;
        if (File.Exists(path))
        {
            try { root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject(); }
            catch { root = new JsonObject(); }
        }
        else root = new JsonObject();

        if (root["CatalogoTfs"] is not JsonObject sec)
        {
            sec = new JsonObject();
            root["CatalogoTfs"] = sec;
        }

        if (usuario != null)
            sec["Usuario"] = usuario.Trim();

        // Solo escribir PAT si vino no vacío (vacío = mantener el guardado).
        if (!string.IsNullOrWhiteSpace(pat) && !EsMascara(pat))
            sec["Pat"] = pat.Trim();

        SecretProtector.EncryptSensitiveFieldsInPlace(root);
        if (File.Exists(path))
            File.Copy(path, path + ".bak", overwrite: true);
        File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
    }

    private static bool EsMascara(string? valor) => RunnerSecrets.EsMascara(valor);

    private static void AsegurarConfigPorDefecto(string automatizacionRoot)
    {
        var path = RutaConfig(automatizacionRoot);
        if (!File.Exists(path))
        {
            GuardarConfig(automatizacionRoot, ConfigPorDefecto(automatizacionRoot));
            return;
        }

        // Migrar: agregar urlRemota si falta en repos conocidos.
        try
        {
            var cfg = LeerConfig(automatizacionRoot);
            var changed = false;
            foreach (var r in cfg.Repos ?? [])
            {
                if (!string.IsNullOrWhiteSpace(r.UrlRemota)) continue;
                var url = UrlPorDefectoParaId(r.Id);
                if (url is null) continue;
                r.UrlRemota = url;
                changed = true;
            }
            if (changed)
            {
                NormalizarConfig(cfg, automatizacionRoot);
                GuardarConfig(automatizacionRoot, cfg);
            }
        }
        catch { /* ignore */ }
    }

    private static string? UrlPorDefectoParaId(string? id) => (id ?? "").Trim().ToLowerInvariant() switch
    {
        "app-cashier" => TfsBase + "Accusys_SOT-app-cashier",
        "api-cashier" => TfsBase + "Accusys_SOT-api-cashier",
        "api-connector" => TfsBase + "Accusys_SOT-api-connector",
        "models-lib" => TfsBase + "cashier-models-lib",
        _ => null
    };

    private static CatalogoReposConfig ConfigPorDefecto(string? automatizacionRoot = null) => new()
    {
        PermitirGitPull = true,
        RamaObjetivo = "develop",
        RaicesPermitidas = ["C:\\Repositorio"],
        Repos =
        [
            new CatalogoRepoConfig
            {
                Id = "app-cashier",
                Nombre = "Accusys SOT App Cashier",
                Rol = "UI Angular — pantallas y rutas",
                Ruta = @"C:\Repositorio\Accusys_SOT-app-cashier",
                UrlRemota = TfsBase + "Accusys_SOT-app-cashier"
            },
            new CatalogoRepoConfig
            {
                Id = "api-cashier",
                Nombre = "Accusys SOT API Cashier",
                Rol = "API cashier — endpoints",
                Ruta = @"C:\Repositorio\Accusys_SOT-api-cashier",
                UrlRemota = TfsBase + "Accusys_SOT-api-cashier"
            },
            new CatalogoRepoConfig
            {
                Id = "api-connector",
                Nombre = "Accusys SOT API Connector",
                Rol = "API connector — integraciones",
                Ruta = @"C:\Repositorio\Accusys_SOT-api-connector",
                UrlRemota = TfsBase + "Accusys_SOT-api-connector"
            },
            new CatalogoRepoConfig
            {
                Id = "models-lib",
                Nombre = "Cashier Models Lib",
                Rol = "Modelos compartidos",
                Ruta = @"C:\Repositorio\cashier-models-lib",
                UrlRemota = TfsBase + "cashier-models-lib"
            }
        ]
    };

    private static CatalogoReposConfig LeerConfig(string automatizacionRoot)
    {
        var path = RutaConfig(automatizacionRoot);
        if (!File.Exists(path))
            return ConfigPorDefecto(automatizacionRoot);
        try
        {
            var cfg = JsonSerializer.Deserialize<CatalogoReposConfig>(File.ReadAllText(path), JsonOpts);
            return cfg ?? ConfigPorDefecto(automatizacionRoot);
        }
        catch
        {
            return ConfigPorDefecto(automatizacionRoot);
        }
    }

    private static void GuardarConfig(string automatizacionRoot, CatalogoReposConfig cfg)
    {
        Directory.CreateDirectory(DirCatalogo(automatizacionRoot));
        // No persistir raíces derivadas (cache) si no estaban explícitas.
        File.WriteAllText(RutaConfig(automatizacionRoot), JsonSerializer.Serialize(cfg, JsonOpts), new UTF8Encoding(false));
    }

    private static CatalogoSotDocumento? LeerCatalogo(string automatizacionRoot)
    {
        var path = RutaCatalogo(automatizacionRoot);
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<CatalogoSotDocumento>(File.ReadAllText(path), JsonOpts);
        }
        catch { return null; }
    }

    private static void GuardarCatalogo(string automatizacionRoot, CatalogoSotDocumento doc)
    {
        Directory.CreateDirectory(DirCatalogo(automatizacionRoot));
        File.WriteAllText(RutaCatalogo(automatizacionRoot), JsonSerializer.Serialize(doc, JsonOpts), new UTF8Encoding(false));
    }

    private static void NormalizarConfig(CatalogoReposConfig cfg, string automatizacionRoot)
    {
        cfg.RaicesPermitidas ??= ["C:\\Repositorio"];
        cfg.Repos ??= [];
        if (string.IsNullOrWhiteSpace(cfg.RamaObjetivo))
            cfg.RamaObjetivo = "develop";
        else
            cfg.RamaObjetivo = cfg.RamaObjetivo.Trim();

        var cacheRoot = RutaCache(automatizacionRoot);

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in cfg.Repos)
        {
            r.Id = SanitizeId(r.Id);
            if (string.IsNullOrWhiteSpace(r.Id))
                r.Id = "repo-" + Guid.NewGuid().ToString("N")[..8];
            if (!ids.Add(r.Id))
                r.Id = r.Id + "-" + Guid.NewGuid().ToString("N")[..4];

            r.Nombre = string.IsNullOrWhiteSpace(r.Nombre) ? r.Id : r.Nombre.Trim();
            r.Rol = (r.Rol ?? "").Trim();
            r.UrlRemota = string.IsNullOrWhiteSpace(r.UrlRemota) ? null : r.UrlRemota.Trim();

            if (string.IsNullOrWhiteSpace(r.Ruta))
                r.Ruta = Path.Combine(cacheRoot, r.Id);
            else
                r.Ruta = Path.GetFullPath(r.Ruta.Trim());
        }
    }

    /// <summary>
    /// La UI no edita rutas de disco. Si llega vacía / enmascarada, se conserva la del servidor
    /// o se asigna cache del Runner.
    /// </summary>
    private static void FusionarRutasConConfigExistente(CatalogoReposConfig cfg, string automatizacionRoot)
    {
        var prev = LeerConfig(automatizacionRoot);
        var prevById = (prev.Repos ?? []).ToDictionary(r => r.Id, StringComparer.OrdinalIgnoreCase);
        cfg.Repos ??= [];
        foreach (var r in cfg.Repos)
        {
            var id = SanitizeId(r.Id);
            var rutaIn = (r.Ruta ?? "").Trim();
            var esMascara = string.IsNullOrWhiteSpace(rutaIn)
                            || rutaIn.StartsWith("***", StringComparison.Ordinal)
                            || EsMascara(rutaIn);

            if (!esMascara)
            {
                // Solo aceptar ruta explícita si está bajo raíces permitidas (compat scripts).
                continue;
            }

            if (!string.IsNullOrWhiteSpace(id) && prevById.TryGetValue(id, out var old)
                && !string.IsNullOrWhiteSpace(old.Ruta))
            {
                r.Ruta = old.Ruta;
            }
            else
            {
                r.Ruta = ""; // NormalizarConfig → cache/{id}
            }
        }

        // Conservar raíces del servidor (no vienen de la UI).
        if (prev.RaicesPermitidas is { Count: > 0 })
            cfg.RaicesPermitidas = prev.RaicesPermitidas;
    }

    private static string EnmascararRuta(string? ruta, string? id, string automatizacionRoot)
    {
        if (string.IsNullOrWhiteSpace(ruta))
            return "***…/cache/" + (string.IsNullOrWhiteSpace(id) ? "?" : id);

        try
        {
            var full = Path.GetFullPath(ruta);
            if (EsRutaCache(full, automatizacionRoot))
                return "***…/cache/" + (string.IsNullOrWhiteSpace(id) ? Path.GetFileName(full) : id);

            var leaf = Path.GetFileName(full.TrimEnd('\\', '/'));
            return string.IsNullOrWhiteSpace(leaf) ? "***…" : "***…/" + leaf;
        }
        catch
        {
            return "***…";
        }
    }

    private static bool EsRutaCache(string? ruta, string automatizacionRoot)
    {
        if (string.IsNullOrWhiteSpace(ruta)) return true;
        try
        {
            var full = Path.GetFullPath(ruta);
            var cache = Path.GetFullPath(RutaCache(automatizacionRoot));
            return full.Equals(cache, StringComparison.OrdinalIgnoreCase)
                   || full.StartsWith(cache + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                   || full.StartsWith(cache + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static string EstadoRepoUi(CatalogoRepoIndexado? ix, string ruta)
    {
        var existe = ix?.Existe ?? (!string.IsNullOrWhiteSpace(ruta) && Directory.Exists(ruta));
        if (!existe) return "pendiente";
        if (ix?.OkPull == false) return "error";
        if (ix?.OkPull == true) return "ok";
        if (!string.IsNullOrWhiteSpace(ix?.Rama) || !string.IsNullOrWhiteSpace(ix?.CommitCorto))
            return "indexado";
        return "clonado";
    }

    private static string SanitizeId(string? id)
    {
        var s = (id ?? "").Trim().ToLowerInvariant();
        s = Regex.Replace(s, @"[^a-z0-9\-_]+", "-");
        s = Regex.Replace(s, @"-+", "-").Trim('-');
        return s;
    }

    private static void ValidarRutasOThrow(CatalogoReposConfig cfg, string automatizacionRoot)
    {
        if (cfg.Repos.Count == 0)
            throw new InvalidOperationException("No hay repos configurados. Agregá al menos uno.");

        var raices = (cfg.RaicesPermitidas ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => Path.GetFullPath(x.Trim().TrimEnd('\\', '/')))
            .ToList();

        var cacheRoot = Path.GetFullPath(RutaCache(automatizacionRoot));
        if (!raices.Any(r => r.Equals(cacheRoot, StringComparison.OrdinalIgnoreCase)))
            raices.Add(cacheRoot);

        if (raices.Count == 0)
            throw new InvalidOperationException("Faltan raíces permitidas (ej. C:\\Repositorio).");

        foreach (var r in cfg.Repos)
        {
            if (string.IsNullOrWhiteSpace(r.Id))
                throw new InvalidOperationException("Cada repo necesita Id.");
            if (string.IsNullOrWhiteSpace(r.Ruta) && string.IsNullOrWhiteSpace(r.UrlRemota))
                throw new InvalidOperationException($"Repo {r.Id}: indicá ruta local y/o URL remota TFS.");

            var full = Path.GetFullPath(r.Ruta);
            var okRoot = raices.Any(root =>
                full.Equals(root, StringComparison.OrdinalIgnoreCase)
                || full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || full.StartsWith(root + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
            if (!okRoot)
                throw new InvalidOperationException(
                    $"Repo {r.Id}: la ruta '{full}' debe estar bajo una raíz permitida ({string.Join(", ", raices)}).");

            if (!string.IsNullOrWhiteSpace(r.UrlRemota))
            {
                if (!Uri.TryCreate(r.UrlRemota, UriKind.Absolute, out var uri)
                    || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                    throw new InvalidOperationException($"Repo {r.Id}: URL remota inválida (http/https).");
                RunnerSecurity.ValidarUrlGitOThrow(r.UrlRemota, automatizacionRoot);
            }
        }
    }

    private static string Truncar(string? s, int max)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        s = s.Trim();
        return s.Length <= max ? s : s[..max] + "…";
    }
}

public sealed class CatalogoReposConfig
{
    public bool PermitirGitPull { get; set; } = true;
    public string RamaObjetivo { get; set; } = "develop";
    public List<string> RaicesPermitidas { get; set; } = ["C:\\Repositorio"];
    public List<CatalogoRepoConfig> Repos { get; set; } = [];
}

public sealed class CatalogoRepoConfig
{
    public string Id { get; set; } = "";
    public string Nombre { get; set; } = "";
    public string Rol { get; set; } = "";
    public string Ruta { get; set; } = "";
    /// <summary>URL git TFS/Azure (sin credenciales). El PAT va en secrets.</summary>
    public string? UrlRemota { get; set; }
}

public sealed class CatalogoSotDocumento
{
    public DateTimeOffset GeneradoUtc { get; set; }
    public string AmbienteFuente { get; set; } = "DEV";
    public string? ResumenCorto { get; set; }
    public List<string>? NotasQa { get; set; }
    public List<CatalogoRepoIndexado> Repos { get; set; } = [];
}

public sealed class CatalogoRepoIndexado
{
    public string Id { get; set; } = "";
    public string Nombre { get; set; } = "";
    public string Rol { get; set; } = "";
    public string Ruta { get; set; } = "";
    public string? UrlRemota { get; set; }
    public bool Existe { get; set; }
    public string? Rama { get; set; }
    public string? RamaObjetivo { get; set; }
    public string? CommitCorto { get; set; }
    public string? AtrasadoVsObjetivo { get; set; }
    public bool? OkPull { get; set; }
    public string? Aviso { get; set; }
    public List<string>? RutasUi { get; set; }
    public List<string>? Pantallas { get; set; }
    public List<string>? Endpoints { get; set; }
    public List<string>? Modelos { get; set; }
}
