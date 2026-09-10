using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AutomatizacionSOT.Config;

/// <summary>
/// Auth PIN, allowlists, rate limit y auditoría mínima del Runner.
/// </summary>
public static class RunnerSecurity
{
    private static readonly ConcurrentDictionary<string, byte> Sesiones = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, RateBucket> Rates = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, CodigoRecuperacion> CodigosRecuperacion =
        new(StringComparer.OrdinalIgnoreCase);

    private static string? _sessionsFilePath;

    /// <summary>Vencimiento de PIN desactivado por ahora (no forzar cambio cada 90 días).</summary>
    private const bool PinVencimientoHabilitado = false;
    private const int DiasCaducidadPin = 90; // solo si se rehabilita PinVencimientoHabilitado
    private const int MinutosValidezCodigo = 30;

    /// <summary>PIN por defecto intranet/dev cuando aún no hay RunnerAuth:Pin en secrets.</summary>
    public const string PinPredeterminado = "1234";


    private sealed class CodigoRecuperacion
    {
        public string CodigoHash { get; set; } = "";
        public DateTimeOffset ExpiraUtc { get; set; }
        public int Intentos { get; set; }
    }

    private static readonly string[] DefaultJiraHosts =
    [
        "atlassian.net", "jira.com", "jira.atlassian.com"
    ];

    private static readonly string[] DefaultGitHosts =
    [
        "tfs2018", "dev.azure.com", "visualstudio.com"
    ];

    public static void MapAuthEndpoints(WebApplication app, string automatizacionRoot)
    {
        _sessionsFilePath = Path.GetFullPath(
            Path.Combine(automatizacionRoot, "..", "RunnerOperador", ".runner-auth-sessions.json"));
        CargarSesionesDesdeArchivo();
        AsegurarPinPredeterminado(automatizacionRoot);

        app.MapGet("/auth/status", (HttpContext ctx) =>
        {
            AsegurarPinPredeterminado(automatizacionRoot);
            var pinSet = !string.IsNullOrWhiteSpace(LeerPin(automatizacionRoot));
            var vencido = pinSet && PinVencido(automatizacionRoot);
            var ok = !pinSet || SesionValida(ctx);
            var emailRec = LeerRecoveryEmail(automatizacionRoot);
            return Results.Ok(new
            {
                ok = true,
                autenticado = ok && (pinSet ? SesionValida(ctx) : true),
                pinConfigurado = pinSet,
                pinVencido = vencido,
                pinVencimientoHabilitado = PinVencimientoHabilitado,
                pinPredeterminado = PinPredeterminado,
                diasCaducidadPin = PinVencimientoHabilitado ? DiasCaducidadPin : (int?)null,
                diasRestantesPin = pinSet && PinVencimientoHabilitado ? DiasRestantesPin(automatizacionRoot) : (int?)null,
                recoveryEmailConfigurado = !string.IsNullOrWhiteSpace(emailRec),
                recoveryEmailMascara = EnmascararEmail(emailRec),
                authObligatorio = pinSet,
                intranetAviso =
                    "Uso pensado para intranet. PIN predeterminado: " + PinPredeterminado
                    + " (vencimiento desactivado). Completá Configuración para armar secrets."
            });
        });

        app.MapPost("/auth/setup", async (HttpRequest request) =>
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(LeerPin(automatizacionRoot)))
                    return Results.Json(new { ok = false, error = "El PIN ya está configurado. Usá login." },
                        statusCode: 400);

                using var doc = await LeerJsonBody(request);
                var pin = JsonStr(doc, "pin");
                var pin2 = JsonStr(doc, "pinConfirmacion", pin);
                if (pin.Length < 4)
                    return Results.Json(new { ok = false, error = "PIN mínimo 4 caracteres." }, statusCode: 400);
                if (!FixedTimeEquals(pin, pin2))
                    return Results.Json(new { ok = false, error = "PIN y confirmación no coinciden." }, statusCode: 400);

                GuardarPin(automatizacionRoot, pin);
                var token = CrearSesion();
                RunnerAudit.Log(automatizacionRoot, "auth-setup", "PIN configurado");
                return Results.Ok(new { ok = true, token, debeCambiarPin = false, mensaje = "PIN del Runner guardado." });
            }
            catch (Exception ex)
            {
                return Results.Json(new { ok = false, error = ex.Message }, statusCode: 400);
            }
        });

        app.MapPost("/auth/login", async (HttpRequest request) =>
        {
            try
            {
                var esperado = LeerPin(automatizacionRoot);
                if (string.IsNullOrWhiteSpace(esperado))
                    return Results.Json(new { ok = false, error = "No hay PIN. Usá /auth/setup." }, statusCode: 400);

                using var doc = await LeerJsonBody(request);
                var pin = JsonStr(doc, "pin");
                if (!FixedTimeEquals(pin, esperado))
                {
                    RunnerAudit.Log(automatizacionRoot, "auth-fail", "PIN incorrecto");
                    return Results.Json(new { ok = false, error = "PIN incorrecto." }, statusCode: 401);
                }

                var vencido = PinVencido(automatizacionRoot);
                if (!vencido)
                    AsegurarFechaPinSiFalta(automatizacionRoot);
                var token = CrearSesion();
                RunnerAudit.Log(automatizacionRoot, "auth-ok", vencido ? "login-pin-vencido" : "login");
                return Results.Ok(new
                {
                    ok = true,
                    token,
                    debeCambiarPin = vencido,
                    mensaje = vencido
                        ? "Sesión iniciada. El PIN tiene más de 3 meses: debés cambiarlo antes de continuar."
                        : "Sesión iniciada."
                });
            }
            catch (Exception ex)
            {
                return Results.Json(new { ok = false, error = ex.Message }, statusCode: 400);
            }
        });

        app.MapPost("/auth/logout", (HttpContext ctx) =>
        {
            var t = ExtraerToken(ctx);
            if (!string.IsNullOrWhiteSpace(t))
                Sesiones.TryRemove(t, out _);
            PersistirSesiones();
            return Results.Ok(new { ok = true });
        });

        app.MapPost("/auth/cambiar-pin", async (HttpRequest request) =>
        {
            try
            {
                using var doc = await LeerJsonBody(request);
                var actual = JsonStr(doc, "pinActual");
                var nuevo = JsonStr(doc, "pinNuevo");
                var conf = JsonStr(doc, "pinConfirmacion");
                var esperado = LeerPin(automatizacionRoot);
                if (string.IsNullOrWhiteSpace(esperado) || !FixedTimeEquals(actual, esperado))
                    return Results.Json(new { ok = false, error = "PIN actual incorrecto." }, statusCode: 401);
                if (nuevo.Length < 4)
                    return Results.Json(new { ok = false, error = "PIN nuevo mínimo 4 caracteres." }, statusCode: 400);
                if (!FixedTimeEquals(nuevo, conf))
                    return Results.Json(new { ok = false, error = "PIN nuevo y confirmación no coinciden." }, statusCode: 400);
                GuardarPin(automatizacionRoot, nuevo);
                Sesiones.Clear();
                RunnerAudit.Log(automatizacionRoot, "auth-pin-change", "ok");
                return Results.Ok(new { ok = true, mensaje = "PIN actualizado. Volvé a iniciar sesión." });
            }
            catch (Exception ex)
            {
                return Results.Json(new { ok = false, error = ex.Message }, statusCode: 400);
            }
        });

        app.MapPost("/auth/recuperar/enviar", async (HttpRequest request) =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(LeerPin(automatizacionRoot)))
                    return Results.Json(new { ok = false, error = "No hay PIN configurado." }, statusCode: 400);

                using var doc = await LeerJsonBody(request);
                var emailReq = JsonStr(doc, "email");

                var emailCfg = LeerRecoveryEmail(automatizacionRoot);
                if (string.IsNullOrWhiteSpace(emailCfg))
                {
                    return Results.Json(new
                    {
                        ok = false,
                        error = "Configurá RunnerAuth:RecoveryEmail (y SMTP) en appsettings.secrets.json para recuperar el PIN."
                    }, statusCode: 400);
                }

                if (!string.IsNullOrWhiteSpace(emailReq)
                    && !string.Equals(emailReq, emailCfg, StringComparison.OrdinalIgnoreCase))
                {
                    await Task.Delay(400);
                    return Results.Ok(new
                    {
                        ok = true,
                        mensaje = "Si el correo coincide con el configurado, enviamos una clave temporal desde no-reply."
                    });
                }

                var codigo = RandomNumberGenerator.GetInt32(100000, 999999).ToString(CultureInfo.InvariantCulture);
                CodigosRecuperacion[emailCfg] = new CodigoRecuperacion
                {
                    CodigoHash = HashCodigo(codigo),
                    ExpiraUtc = DateTimeOffset.UtcNow.AddMinutes(MinutosValidezCodigo),
                    Intentos = 0
                };

                var envio = await EnviarEmailRecuperacionAsync(automatizacionRoot, emailCfg, codigo);
                RunnerAudit.Log(automatizacionRoot, "auth-recover-send", envio.Ok ? "email-ok" : "email-fallback");
                return Results.Ok(new
                {
                    ok = true,
                    mensaje = envio.Ok
                        ? $"Enviamos una clave temporal a {EnmascararEmail(emailCfg)} desde no-reply (válida {MinutosValidezCodigo} min)."
                        : $"Clave generada. {envio.Aviso} Completá clave + PIN nuevo + confirmación abajo."
                });
            }
            catch (Exception ex)
            {
                return Results.Json(new { ok = false, error = ex.Message }, statusCode: 400);
            }
        });

        app.MapPost("/auth/recuperar/restablecer", async (HttpRequest request) =>
        {
            try
            {
                using var doc = await LeerJsonBody(request);
                var codigo = JsonStr(doc, "codigo");
                var nuevo = JsonStr(doc, "pinNuevo");
                var conf = JsonStr(doc, "pinConfirmacion");
                var emailCfg = LeerRecoveryEmail(automatizacionRoot) ?? "";

                if (string.IsNullOrWhiteSpace(emailCfg) ||
                    !CodigosRecuperacion.TryGetValue(emailCfg, out var reg))
                    return Results.Json(new { ok = false, error = "Pedí primero una clave con «Me olvidé la contraseña»." },
                        statusCode: 400);

                if (reg.ExpiraUtc < DateTimeOffset.UtcNow)
                {
                    CodigosRecuperacion.TryRemove(emailCfg, out _);
                    return Results.Json(new { ok = false, error = "La clave expiró. Pedí una nueva." }, statusCode: 400);
                }

                reg.Intentos++;
                if (reg.Intentos > 8)
                {
                    CodigosRecuperacion.TryRemove(emailCfg, out _);
                    return Results.Json(new { ok = false, error = "Demasiados intentos. Pedí una clave nueva." },
                        statusCode: 429);
                }

                if (!FixedTimeEquals(HashCodigo(codigo), reg.CodigoHash))
                    return Results.Json(new { ok = false, error = "Clave incorrecta." }, statusCode: 401);

                if (nuevo.Length < 4)
                    return Results.Json(new { ok = false, error = "PIN nuevo mínimo 4 caracteres." }, statusCode: 400);
                if (!FixedTimeEquals(nuevo, conf))
                    return Results.Json(new { ok = false, error = "PIN nuevo y confirmación no coinciden." },
                        statusCode: 400);

                GuardarPin(automatizacionRoot, nuevo);
                CodigosRecuperacion.TryRemove(emailCfg, out _);
                Sesiones.Clear();
                RunnerAudit.Log(automatizacionRoot, "auth-recover-ok", "pin restablecido");
                return Results.Ok(new
                {
                    ok = true,
                    mensaje = "PIN restablecido. Iniciá sesión con el nuevo PIN."
                });
            }
            catch (Exception ex)
            {
                return Results.Json(new { ok = false, error = ex.Message }, statusCode: 400);
            }
        });
    }

    public static void UseAuthGate(WebApplication app, string automatizacionRoot)
    {
        app.Use(async (ctx, next) =>
        {
            var path = ctx.Request.Path.Value ?? "";

            if (EsRutaPublica(path))
            {
                await next();
                return;
            }

            // Rate limit en endpoints sensibles
            if (EsRutaRateLimited(path) && !PermitirRate(ctx, path))
            {
                ctx.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                await ctx.Response.WriteAsJsonAsync(new
                {
                    ok = false,
                    error = "Demasiadas solicitudes. Esperá un momento e intentá de nuevo."
                });
                return;
            }

            var pinSet = !string.IsNullOrWhiteSpace(LeerPin(automatizacionRoot));
            if (!pinSet)
            {
                // Sin PIN: modo bootstrap (intranet). Se recomienda configurarlo en RunnerIA.
                await next();
                return;
            }

            if (!SesionValida(ctx))
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await ctx.Response.WriteAsJsonAsync(new
                {
                    ok = false,
                    error = "Sesión requerida. Iniciá sesión con el PIN del Runner.",
                    authRequired = true
                });
                return;
            }

            await next();
        });
    }

    public static void ValidarUrlJiraOThrow(string? url, string automatizacionRoot)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new InvalidOperationException("Falta URL de Jira.");
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException("URL Jira inválida (solo http/https).");

        var hosts = LeerAllowlist(automatizacionRoot, "JiraHosts", DefaultJiraHosts);
        if (!HostPermitido(uri.Host, hosts))
            throw new InvalidOperationException(
                $"Host Jira «{uri.Host}» no está en la allowlist. Agregalo en secrets Allowlist:JiraHosts.");
    }

    public static void ValidarUrlGitOThrow(string? url, string automatizacionRoot)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException("URL git inválida (solo http/https).");

        if (uri.Scheme.Equals("file", StringComparison.OrdinalIgnoreCase)
            || uri.Scheme.Equals("ssh", StringComparison.OrdinalIgnoreCase)
            || uri.Scheme.Equals("git", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Esquema git no permitido.");

        var path = uri.AbsolutePath ?? "";
        if (!path.Contains("/_git/", StringComparison.OrdinalIgnoreCase)
            && !path.Contains("/_git", StringComparison.OrdinalIgnoreCase)
            && !path.Contains(".git", StringComparison.OrdinalIgnoreCase))
        {
            // TFS/_git/ o Azure; permitir también paths que terminen en nombre repo típico bajo allowlist host
        }

        var hosts = LeerAllowlist(automatizacionRoot, "GitHosts", DefaultGitHosts);
        if (!HostPermitido(uri.Host, hosts))
            throw new InvalidOperationException(
                $"Host git «{uri.Host}» no está en la allowlist. Agregalo en secrets Allowlist:GitHosts.");
    }

    public static string NombreFeatureSeguro(string? nombre)
    {
        nombre = Path.GetFileName((nombre ?? "").Trim());
        if (string.IsNullOrWhiteSpace(nombre))
            throw new InvalidOperationException("Nombre de feature vacío.");
        if (nombre.Contains("..", StringComparison.Ordinal) || nombre.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidOperationException("Nombre de feature inválido.");
        if (!nombre.EndsWith(".feature", StringComparison.OrdinalIgnoreCase))
            nombre += ".feature";
        var baseName = Path.GetFileNameWithoutExtension(nombre);
        // Quitar tildes y caracteres no ASCII para que el guardado no falle con títulos en español.
        baseName = QuitarDiacriticos(baseName);
        baseName = Regex.Replace(baseName, @"[^A-Za-z0-9_\-]+", "_");
        baseName = Regex.Replace(baseName, @"_+", "_").Trim('_');
        if (string.IsNullOrWhiteSpace(baseName) || !char.IsLetterOrDigit(baseName[0]))
            baseName = "Prueba_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
        if (baseName.StartsWith("Prueba_", StringComparison.OrdinalIgnoreCase))
            baseName = baseName["Prueba_".Length..];
        if (string.IsNullOrWhiteSpace(baseName) || !char.IsLetterOrDigit(baseName[0]))
            baseName = "Prueba_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
        if (baseName.Length > 80)
            baseName = baseName[..80].TrimEnd('_', '-');
        if (!Regex.IsMatch(baseName, @"^[A-Za-z0-9][A-Za-z0-9_\-]{0,80}$"))
            throw new InvalidOperationException(
                "Nombre de feature: letras/números/guiones, máx. 80 (ej. SC-161_Caso.feature).");
        return baseName + ".feature";
    }

    private static string QuitarDiacriticos(string s)
    {
        var norm = (s ?? "").Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(norm.Length);
        foreach (var c in norm)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    public static string RedactarParaLlm(string? texto)
    {
        if (string.IsNullOrEmpty(texto)) return "";
        var t = SecretRedactor.Redactar(texto);
        t = Regex.Replace(t, @"C:\\Repositorio\\[^\s""']+", "***…/repo");
        t = Regex.Replace(t, @"(?i)Catalogo[/\\]cache[/\\][^\s""']+", "***…/cache");
        if (t.Length > 12000) t = t[..12000] + "\n…[truncado]";
        return t;
    }

    // Rutas SPA: públicas para que F5/Ctrl+F5 sirva index.html (APIs siguen protegidas).
    private static readonly string[] RutasSpa = ["/runner", "/generar", "/config", "/ayuda"];

    private static bool EsRutaPublica(string path)
    {
        if (string.IsNullOrEmpty(path) || path == "/") return true;
        if (RutasSpa.Contains(path, StringComparer.OrdinalIgnoreCase)) return true;
        if (path.StartsWith("/health", StringComparison.OrdinalIgnoreCase)) return true;
        if (path.StartsWith("/preflight", StringComparison.OrdinalIgnoreCase)) return true;
        if (path.StartsWith("/auth/", StringComparison.OrdinalIgnoreCase)) return true;
        if (path.StartsWith("/app", StringComparison.OrdinalIgnoreCase)) return true;
        if (path.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".js", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".css", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".ico", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".woff2", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".map", StringComparison.OrdinalIgnoreCase))
            return true;
        // Fallback SPA: cualquier path sin extensión que no sea API.
        if (!path.StartsWith("/api", StringComparison.OrdinalIgnoreCase)
            && !path.StartsWith("/run", StringComparison.OrdinalIgnoreCase)
            && !path.StartsWith("/config", StringComparison.OrdinalIgnoreCase)
            && !path.StartsWith("/pruebas", StringComparison.OrdinalIgnoreCase)
            && !path.StartsWith("/catalogo-sot", StringComparison.OrdinalIgnoreCase)
            && !path.StartsWith("/suite", StringComparison.OrdinalIgnoreCase)
            && !path.StartsWith("/runner-ia", StringComparison.OrdinalIgnoreCase)
            && !path.StartsWith("/manual", StringComparison.OrdinalIgnoreCase)
            && !path.StartsWith("/escenarios", StringComparison.OrdinalIgnoreCase)
            && !path.StartsWith("/casos-prueba", StringComparison.OrdinalIgnoreCase)
            && !Path.HasExtension(path))
            return true;
        return false;
    }

    private static bool EsRutaRateLimited(string path) =>
        path.StartsWith("/pruebas/analizar", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/catalogo-sot/sync", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/auth/login", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/auth/setup", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/auth/recuperar", StringComparison.OrdinalIgnoreCase);

    private static bool PermitirRate(HttpContext ctx, string path)
    {
        var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "local";
        var key = ip + "|" + path.ToLowerInvariant();
        var now = DateTimeOffset.UtcNow;
        var bucket = Rates.GetOrAdd(key, _ => new RateBucket());
        lock (bucket)
        {
            if (now - bucket.WindowStart > TimeSpan.FromMinutes(1))
            {
                bucket.WindowStart = now;
                bucket.Count = 0;
            }
            bucket.Count++;
            var max = path.Contains("analizar", StringComparison.OrdinalIgnoreCase) ? 20
                : path.Contains("/auth/", StringComparison.OrdinalIgnoreCase) ? 12
                : 10;
            return bucket.Count <= max;
        }
    }

    private static string CrearSesion()
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        Sesiones[token] = 0;
        PersistirSesiones();
        return token;
    }

    private static bool SesionValida(HttpContext ctx)
    {
        var t = ExtraerToken(ctx);
        return !string.IsNullOrWhiteSpace(t) && Sesiones.ContainsKey(t);
    }

    private static string? ExtraerToken(HttpContext ctx)
    {
        if (ctx.Request.Headers.TryGetValue("X-Runner-Auth", out var h) && !string.IsNullOrWhiteSpace(h))
            return h.ToString().Trim();
        var auth = ctx.Request.Headers.Authorization.ToString();
        if (auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return auth["Bearer ".Length..].Trim();
        // iframe / pestaña nueva / <a download> no mandan headers: token en query
        if (ctx.Request.Query.TryGetValue("auth", out var q) && !string.IsNullOrWhiteSpace(q))
            return q.ToString().Trim();
        return null;
    }

    private static string? LeerPin(string automatizacionRoot)
    {
        try
        {
            var path = Path.Combine(automatizacionRoot, "appsettings.secrets.json");
            if (!File.Exists(path)) return null;
            var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
            var v = (root?["RunnerAuth"] as JsonObject)?["Pin"]?.ToString();
            if (string.IsNullOrWhiteSpace(v) || EsMascara(v)) return null;
            return SecretProtector.UnprotectFromStorage(v.Trim());
        }
        catch { return null; }
    }

    private static string? LeerRecoveryEmail(string automatizacionRoot)
    {
        try
        {
            var path = Path.Combine(automatizacionRoot, "appsettings.secrets.json");
            if (!File.Exists(path)) return null;
            var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
            var v = (root?["RunnerAuth"] as JsonObject)?["RecoveryEmail"]?.ToString();
            return string.IsNullOrWhiteSpace(v) || EsMascara(v) ? null : v.Trim();
        }
        catch { return null; }
    }

    private static DateTimeOffset? LeerPinChangedUtc(string automatizacionRoot)
    {
        try
        {
            var path = Path.Combine(automatizacionRoot, "appsettings.secrets.json");
            if (!File.Exists(path)) return null;
            var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
            var v = (root?["RunnerAuth"] as JsonObject)?["PinChangedUtc"]?.ToString();
            if (string.IsNullOrWhiteSpace(v)) return null;
            return DateTimeOffset.TryParse(v, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var dt) ? dt : null;
        }
        catch { return null; }
    }

    private static bool PinVencido(string automatizacionRoot)
    {
        if (!PinVencimientoHabilitado)
            return false;
        var changed = LeerPinChangedUtc(automatizacionRoot);
        if (changed is null) return false; // PINs previos: el reloj arranca en el próximo login/cambio
        return DateTimeOffset.UtcNow - changed.Value > TimeSpan.FromDays(DiasCaducidadPin);
    }

    /// <summary>Si no hay PIN en secrets, crea RunnerAuth:Pin = PinPredeterminado.</summary>
    private static void AsegurarPinPredeterminado(string automatizacionRoot)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(LeerPin(automatizacionRoot)))
                return;
            GuardarPin(automatizacionRoot, PinPredeterminado);
            RunnerAudit.Log(automatizacionRoot, "auth-seed", "PIN predeterminado " + PinPredeterminado);
        }
        catch
        {
            /* best-effort: sin suite / sin permiso de escritura */
        }
    }

    private static void AsegurarFechaPinSiFalta(string automatizacionRoot)
    {
        if (!PinVencimientoHabilitado) return;
        if (LeerPinChangedUtc(automatizacionRoot) is not null) return;
        try
        {
            var path = Path.Combine(automatizacionRoot, "appsettings.secrets.json");
            if (!File.Exists(path)) return;
            var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject();
            if (root["RunnerAuth"] is not JsonObject sec) return;
            sec["PinChangedUtc"] = DateTimeOffset.UtcNow.ToString("o");
            File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
                new UTF8Encoding(false));
        }
        catch { /* best-effort */ }
    }

    private static int DiasRestantesPin(string automatizacionRoot)
    {
        var changed = LeerPinChangedUtc(automatizacionRoot) ?? DateTimeOffset.UtcNow.AddDays(-DiasCaducidadPin);
        var resto = DiasCaducidadPin - (int)(DateTimeOffset.UtcNow - changed).TotalDays;
        return Math.Max(0, resto);
    }

    private static string EnmascararEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@')) return "";
        var parts = email.Split('@');
        var user = parts[0];
        var dom = parts[1];
        var u = user.Length <= 2 ? user[0] + "*" : user[..2] + new string('*', Math.Min(6, user.Length - 2));
        return u + "@" + dom;
    }

    private static string HashCodigo(string codigo)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes("runner-pin-rec|" + (codigo ?? "")));
        return Convert.ToHexString(bytes);
    }

    private static async Task<(bool Ok, string Aviso)> EnviarEmailRecuperacionAsync(
        string automatizacionRoot, string toEmail, string codigo)
    {
        var from = "noreply@runneria.local";
        string? host = null;
        var port = 587;
        string? user = null;
        string? pass = null;
        var ssl = true;
        try
        {
            var path = Path.Combine(automatizacionRoot, "appsettings.secrets.json");
            if (File.Exists(path))
            {
                var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
                var smtp = (root?["RunnerAuth"] as JsonObject)?["Smtp"] as JsonObject;
                if (smtp is not null)
                {
                    host = smtp["Host"]?.ToString();
                    if (int.TryParse(smtp["Port"]?.ToString(), out var p)) port = p;
                    user = smtp["User"]?.ToString();
                    pass = smtp["Password"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(pass) && !EsMascara(pass))
                        pass = SecretProtector.UnprotectFromStorage(pass.Trim());
                    from = smtp["From"]?.ToString() ?? from;
                    if (bool.TryParse(smtp["EnableSsl"]?.ToString(), out var s)) ssl = s;
                }
            }
        }
        catch { /* fallback local */ }

        var cuerpo =
            "RunnerIA — recuperación de PIN\n\n" +
            $"Tu clave temporal es: {codigo}\n" +
            $"Válida por {MinutosValidezCodigo} minutos.\n\n" +
            "Ingresala en la pantalla del Runner junto con el PIN nuevo y su confirmación.\n" +
            "Este mensaje es automático de no-reply; no respondas este correo.\n";

        if (!string.IsNullOrWhiteSpace(host) && !EsMascara(host))
        {
            try
            {
                using var msg = new System.Net.Mail.MailMessage(from, toEmail)
                {
                    Subject = "RunnerIA — clave temporal para restablecer PIN",
                    Body = cuerpo,
                    IsBodyHtml = false
                };
                using var client = new System.Net.Mail.SmtpClient(host, port)
                {
                    EnableSsl = ssl,
                    DeliveryMethod = System.Net.Mail.SmtpDeliveryMethod.Network
                };
                if (!string.IsNullOrWhiteSpace(user) && !string.IsNullOrWhiteSpace(pass) && !EsMascara(pass))
                    client.Credentials = new System.Net.NetworkCredential(user, pass);
                await client.SendMailAsync(msg);
                return (true, "");
            }
            catch (Exception ex)
            {
                // Continúa a fallback local
                GuardarClaveRecuperacionLocal(automatizacionRoot, toEmail, codigo, ex.Message);
                return (false, $"No se pudo enviar SMTP ({ex.Message}). Clave guardada en Catalogo/ultima-clave-recuperacion-pin.txt para el operador.");
            }
        }

        GuardarClaveRecuperacionLocal(automatizacionRoot, toEmail, codigo, "SMTP no configurado");
        return (false,
            "SMTP no configurado en RunnerAuth:Smtp. Clave en Catalogo/ultima-clave-recuperacion-pin.txt (intranet).");
    }

    private static void GuardarClaveRecuperacionLocal(
        string automatizacionRoot, string email, string codigo, string motivo)
    {
        try
        {
            var dir = Path.Combine(automatizacionRoot, "Catalogo");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "ultima-clave-recuperacion-pin.txt");
            File.WriteAllText(path,
                $"Utc={DateTimeOffset.UtcNow:o}\nPara={email}\nClave={codigo}\nMotivo={motivo}\n",
                new UTF8Encoding(false));
        }
        catch { /* best-effort */ }
    }

    private static void GuardarPin(string automatizacionRoot, string pin)
    {
        var path = Path.Combine(automatizacionRoot, "appsettings.secrets.json");
        JsonObject root;
        if (File.Exists(path))
        {
            try { root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject(); }
            catch { root = new JsonObject(); }
        }
        else root = new JsonObject();

        if (root["RunnerAuth"] is not JsonObject sec)
        {
            sec = new JsonObject();
            root["RunnerAuth"] = sec;
        }
        sec["Pin"] = pin;
        sec["PinChangedUtc"] = DateTimeOffset.UtcNow.ToString("o");
        SecretProtector.EncryptSensitiveFieldsInPlace(root);
        if (File.Exists(path)) File.Copy(path, path + ".bak", overwrite: true);
        File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(false));
    }

    private static List<string> LeerAllowlist(string automatizacionRoot, string clave, string[] defaults)
    {
        try
        {
            var path = Path.Combine(automatizacionRoot, "appsettings.secrets.json");
            if (File.Exists(path))
            {
                var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
                var arr = (root?["Allowlist"] as JsonObject)?[clave] as JsonArray;
                if (arr is { Count: > 0 })
                {
                    return arr.Select(x => x?.ToString()?.Trim() ?? "")
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .ToList();
                }
                // también string separado por comas
                var s = (root?["Allowlist"] as JsonObject)?[clave]?.ToString();
                if (!string.IsNullOrWhiteSpace(s) && s.Contains(','))
                    return s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .ToList();
            }
        }
        catch { /* defaults */ }
        return defaults.ToList();
    }

    private static bool HostPermitido(string host, List<string> allow)
    {
        host = host.Trim().ToLowerInvariant();
        foreach (var a in allow)
        {
            var rule = a.Trim().ToLowerInvariant().TrimStart('*', '.');
            if (string.IsNullOrWhiteSpace(rule)) continue;
            if (host == rule || host.EndsWith("." + rule, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static bool EsMascara(string? v) => RunnerSecrets.EsMascara(v);

    /** Lee el body JSON de un request auth. */
    private static async Task<JsonDocument> LeerJsonBody(HttpRequest request)
    {
        using var reader = new StreamReader(request.Body, Encoding.UTF8);
        var raw = await reader.ReadToEndAsync();
        return JsonDocument.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw);
    }

    private static string JsonStr(JsonDocument doc, string prop, string fallback = "")
    {
        return doc.RootElement.TryGetProperty(prop, out var p) ? (p.GetString() ?? "").Trim() : fallback;
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        var ba = Encoding.UTF8.GetBytes(a ?? "");
        var bb = Encoding.UTF8.GetBytes(b ?? "");
        if (ba.Length != bb.Length) return false;
        return CryptographicOperations.FixedTimeEquals(ba, bb);
    }

    private static void CargarSesionesDesdeArchivo()
    {
        if (string.IsNullOrWhiteSpace(_sessionsFilePath) || !File.Exists(_sessionsFilePath))
            return;
        try
        {
            var tokens = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(_sessionsFilePath));
            if (tokens is null) return;
            foreach (var t in tokens)
            {
                if (!string.IsNullOrWhiteSpace(t))
                    Sesiones.TryAdd(t.Trim(), 0);
            }
        }
        catch
        {
            /* best-effort */
        }
    }

    private static void PersistirSesiones()
    {
        if (string.IsNullOrWhiteSpace(_sessionsFilePath)) return;
        try
        {
            var dir = Path.GetDirectoryName(_sessionsFilePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            var tokens = Sesiones.Keys.ToList();
            File.WriteAllText(_sessionsFilePath, JsonSerializer.Serialize(tokens), Encoding.UTF8);
        }
        catch
        {
            /* best-effort */
        }
    }

    private sealed class RateBucket
    {
        public DateTimeOffset WindowStart = DateTimeOffset.UtcNow;
        public int Count;
    }
}

public static class RunnerAudit
{
    public static void Log(string automatizacionRoot, string evento, string detalle)
    {
        try
        {
            var dir = Path.Combine(automatizacionRoot, "Catalogo");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "audit-runner.log");
            var line = $"{DateTimeOffset.Now:o}\t{evento}\t{Sanitizar(detalle)}{Environment.NewLine}";
            File.AppendAllText(path, line, Encoding.UTF8);
        }
        catch { /* no tumbar flujo */ }
    }

    private static string Sanitizar(string s)
    {
        s = Regex.Replace(s ?? "", @"(?i)(pin|password|token|pat|bearer)\s*[:=]\s*\S+", "$1=[REDACTED]");
        if (s.Length > 300) s = s[..300] + "…";
        return s.Replace('\t', ' ').Replace('\n', ' ');
    }
}
