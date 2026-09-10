using System.Text;
using System.Text.Json;
using Microsoft.Playwright;

/// <summary>
/// Sesión Playwright con grabación de clics: XPath utilizable + búsqueda en código.
/// </summary>
public static class UiMapeoGrabador
{
    private static readonly SemaphoreSlim Lock = new(1, 1);
    private static UiMapeoSesionActiva? _sesion;
    private static UiMapeoSesionSnapshot? _ultimaSesion;

    private const string InitScript = """
        () => {
          if (window.__runnerMapeoInstalled) return;
          window.__runnerMapeoInstalled = true;

          function xpathCompleto(el) {
            if (!el || el.nodeType !== 1) return '';
            if (el.id) return '//*[@id="' + String(el.id).replace(/"/g, '\\"') + '"]';
            const parts = [];
            let node = el;
            while (node && node.nodeType === 1) {
              let ix = 0;
              let sib = node.previousSibling;
              while (sib) {
                if (sib.nodeType === 1 && sib.nodeName === node.nodeName) ix++;
                sib = sib.previousSibling;
              }
              const tag = node.nodeName.toLowerCase();
              parts.unshift(tag + (ix ? '[' + (ix + 1) + ']' : ''));
              node = node.parentElement;
            }
            return '/' + parts.join('/');
          }

          function xpathUtil(el) {
            if (!el || el.nodeType !== 1) return '';
            if (el.id) return '//*[@id="' + String(el.id).replace(/"/g, '\\"') + '"]';
            const fc = el.getAttribute && el.getAttribute('formcontrolname');
            if (fc) return '//*[@formcontrolname="' + fc.replace(/"/g, '\\"') + '"]';
            const testId = el.getAttribute && (el.getAttribute('data-testid') || el.getAttribute('data-cy'));
            if (testId) return '//*[@data-testid="' + testId.replace(/"/g, '\\"') + '"]';
            const href = el.getAttribute && el.getAttribute('href');
            if (href && href.startsWith('/')) {
              return '//a[contains(@href,"' + href.replace(/"/g, '\\"') + '")]';
            }
            const txt = (el.innerText || el.textContent || '').trim().replace(/\s+/g, ' ');
            if (txt && txt.length > 0 && txt.length <= 80) {
              const tag = el.tagName.toLowerCase();
              return '//' + tag + '[normalize-space()="' + txt.replace(/"/g, '\\"') + '"]';
            }
            return xpathCompleto(el);
          }

          document.addEventListener('click', (e) => {
            if (window.__runnerMapeoRecording !== true) return;
            const t = e.target;
            if (!t || t.nodeType !== 1) return;
            const payload = {
              xpath: xpathCompleto(t),
              xpathUtil: xpathUtil(t),
              tag: t.tagName || '',
              texto: (t.innerText || t.textContent || '').trim().replace(/\s+/g, ' ').slice(0, 200),
              id: t.id || '',
              clases: (typeof t.className === 'string' ? t.className : '').slice(0, 200),
              href: (t.getAttribute && t.getAttribute('href')) || '',
              url: location.href,
              ts: Date.now()
            };
            if (window.__runnerMapeoOnClick) window.__runnerMapeoOnClick(payload);
          }, true);
        }
        """;

    public static async Task<object> IniciarAsync(
        string url,
        string automatizacionRoot,
        bool headless = false,
        string? proyectoId = null,
        bool? loginAutomatico = null,
        CancellationToken ct = default)
    {
        await Lock.WaitAsync(ct);
        try
        {
            await CerrarSesionInternaAsync();

            var ctx = UiMapeoConfig.ResolverContexto(automatizacionRoot, proyectoId);
            var urlFinal = string.IsNullOrWhiteSpace(url) ? ctx.UrlInicio : url.Trim();
            if (string.IsNullOrWhiteSpace(urlFinal))
                throw new InvalidOperationException("Indique URL o configure la integración «URL aplicación» del proyecto.");

            var usarLoginSot = loginAutomatico ?? ctx.LoginAutomaticoSot;
            if (usarLoginSot && !string.Equals(ctx.Plantilla, "sot", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "Login automático SOT solo aplica a proyectos plantilla SOT. Desactivá «Login automático SOT».");

            var playwright = await Playwright.CreateAsync();
            var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = headless,
                SlowMo = headless ? 0 : 50
            });

            var context = await browser.NewContextAsync(new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = 1440, Height = 900 },
                IgnoreHTTPSErrors = true
            });

            var page = await context.NewPageAsync();
            await page.AddInitScriptAsync(
                """
                () => {
                  try {
                    localStorage.removeItem('activeTabs');
                    if (!sessionStorage.getItem('browserTabId')) {
                      const id = (typeof crypto !== 'undefined' && crypto.randomUUID)
                        ? crypto.randomUUID()
                        : 'runner-mapeo-' + Date.now();
                      sessionStorage.setItem('browserTabId', id);
                    }
                  } catch (e) {}
                }
                """);
            await page.AddInitScriptAsync(InitScript);

            var sesion = new UiMapeoSesionActiva
            {
                Id = Guid.NewGuid().ToString("N")[..12],
                UrlInicio = urlFinal,
                InicioUtc = DateTime.UtcNow,
                Playwright = playwright,
                Browser = browser,
                Context = context,
                Page = page,
                AutomatizacionRoot = automatizacionRoot,
                ProyectoId = ctx.ProyectoId,
                ProyectoNombre = ctx.ProyectoNombre,
                Plantilla = ctx.Plantilla,
                CatalogoReposPath = ctx.CatalogoReposPath,
                LoginAutomaticoSot = usarLoginSot
            };

            await page.ExposeFunctionAsync("__runnerMapeoOnClick", (JsonElement payload) =>
            {
                RegistrarClick(sesion, payload);
                return Task.CompletedTask;
            });

            List<string> avisosLogin;
            if (usarLoginSot)
            {
                try
                {
                    avisosLogin = (await UiMapeoSotLoginHelper.PrepararSesionAsync(page, automatizacionRoot)).ToList();
                }
                catch (Exception exLogin)
                {
                    await CerrarSesionInternaAsync();
                    throw new InvalidOperationException(
                        "Login SOT falló para el mapeo: " + exLogin.Message, exLogin);
                }

                var credPreview = UiMapeoConfig.LeerCredenciales(automatizacionRoot);
                sesion.SucursalObjetivo = credPreview.SucursalObjetivo;
                sesion.CodigoSucursal = credPreview.FiltroSucursal;
            }
            else
            {
                avisosLogin =
                [
                    "Sin login automático: la app se abrió en la URL indicada.",
                    "Completá login manualmente en la ventana Chromium si hace falta, luego grabá clics."
                ];
                await page.GotoAsync(urlFinal, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = 120_000
                });
            }

            await page.EvaluateAsync("window.__runnerMapeoRecording = true");
            _sesion = sesion;
            sesion.AvisosLogin = avisosLogin;

            return RespuestaEstado(sesion);
        }
        catch
        {
            await CerrarSesionInternaAsync();
            throw;
        }
        finally
        {
            Lock.Release();
        }
    }

    public static async Task<object> DetenerAsync(string automatizacionRoot)
    {
        await Lock.WaitAsync();
        try
        {
            var id = _sesion?.Id;
            await CerrarSesionInternaAsync();

            string? guardadoEn = null;
            string? guardadoAbsoluto = null;
            var total = _ultimaSesion?.Eventos.Count ?? 0;
            string? gherkinPreview = null;

            if (_ultimaSesion is not null && total > 0)
            {
                var bytes = SerializarExport(_ultimaSesion);
                gherkinPreview = ExtraerGherkinPreview(bytes);
                var g = UiMapeoPersistencia.Guardar(automatizacionRoot, bytes, _ultimaSesion.Id);
                guardadoEn = g.rutaRelativa;
                guardadoAbsoluto = g.rutaAbsoluta;
            }

            return new
            {
                ok = true,
                sesionId = id,
                mensaje = total > 0
                    ? $"Grabación detenida y guardada ({total} clic(s))."
                    : "Grabación detenida (sin clics).",
                guardadoEn,
                guardadoAbsoluto,
                totalEventos = total,
                gherkinPreview
            };
        }
        finally
        {
            Lock.Release();
        }
    }

    public static object? LeerUltimaSesionGuardada(string automatizacionRoot) =>
        UiMapeoPersistencia.LeerUltimaSesion(automatizacionRoot);

    public static object Estado()
    {
        var s = _sesion;
        if (s is null)
            return new { ok = true, activa = false, eventos = 0 };

        return RespuestaEstado(s);
    }

    public static object ListarEventos(int? desde = null)
    {
        var s = _sesion;
        if (s is null)
            return new { ok = true, activa = false, eventos = Array.Empty<MapeoEventoUi>() };

        var lista = s.Eventos.AsEnumerable();
        if (desde is > 0)
            lista = lista.Where(e => e.Orden > desde.Value);

        return new
        {
            ok = true,
            activa = true,
            sesionId = s.Id,
            total = s.Eventos.Count,
            eventos = lista.ToList()
        };
    }

    public static byte[] ExportarJson()
    {
        var fuente = ResolverFuenteExport();
        if (fuente is null)
            return JsonSerializer.SerializeToUtf8Bytes(new { formato = UiMapeoGherkinGenerator.FormatoVersion, eventos = Array.Empty<object>() });
        var bytes = SerializarExport(fuente);
        _ultimaSesion = fuente;
        return bytes;
    }

    private static byte[] SerializarExport(UiMapeoSesionSnapshot fuente)
    {
        var eventos = fuente.Eventos;
        var gherkin = UiMapeoGherkinGenerator.Generar(
            eventos,
            fuente.UrlInicio,
            fuente.Id,
            fuente.Plantilla);
        var export = new
        {
            formato = UiMapeoGherkinGenerator.FormatoVersion,
            generadoUtc = DateTime.UtcNow,
            sesionId = fuente.Id,
            proyectoId = fuente.ProyectoId,
            proyectoNombre = fuente.ProyectoNombre,
            plantilla = fuente.Plantilla,
            loginAutomaticoSot = fuente.LoginAutomaticoSot,
            urlInicio = fuente.UrlInicio,
            inicioUtc = fuente.InicioUtc,
            gherkin,
            eventos
        };

        return JsonSerializer.SerializeToUtf8Bytes(export, new JsonSerializerOptions { WriteIndented = true });
    }

    private static string? ExtraerGherkinPreview(byte[] jsonBytes)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonBytes);
            if (!doc.RootElement.TryGetProperty("gherkin", out var gh))
                return null;
            var text = gh.GetString() ?? "";
            var lines = text.Split('\n').Take(12);
            return string.Join('\n', lines).TrimEnd();
        }
        catch
        {
            return null;
        }
    }

    private static UiMapeoSesionSnapshot? ResolverFuenteExport()
    {
        if (_sesion is not null)
            return UiMapeoSesionSnapshot.DesdeActiva(_sesion);
        return _ultimaSesion;
    }

    public static IReadOnlyList<CoincidenciaCodigoMapeo> BuscarCodigoManual(
        string automatizacionRoot,
        string consulta,
        string? xpath = null,
        string? catalogoReposPath = null)
    {
        var ev = new MapeoEventoUi
        {
            Texto = consulta,
            XpathUtil = xpath,
            Xpath = xpath
        };
        return UiMapeoCodigoBusqueda.Buscar(
            automatizacionRoot,
            ev,
            catalogoReposPath: catalogoReposPath ?? CatalogoSesionActiva());
    }

    public static string? CatalogoSesionActiva() => _sesion?.CatalogoReposPath;

    private static object RespuestaEstado(UiMapeoSesionActiva s) => new
    {
        ok = true,
        activa = true,
        sesionId = s.Id,
        proyectoId = s.ProyectoId,
        proyectoNombre = s.ProyectoNombre,
        plantilla = s.Plantilla,
        loginAutomaticoSot = s.LoginAutomaticoSot,
        urlInicio = s.UrlInicio,
        urlActual = s.Page.Url,
        inicioUtc = s.InicioUtc,
        eventos = s.Eventos.Count,
        avisosLogin = s.AvisosLogin ?? [],
        sucursalObjetivo = s.SucursalObjetivo,
        codigoSucursal = s.CodigoSucursal
    };

    private static void RegistrarClick(UiMapeoSesionActiva sesion, JsonElement payload)
    {
        lock (sesion.Sync)
        {
            var ev = new MapeoEventoUi
            {
                Orden = sesion.Eventos.Count + 1,
                Xpath = payload.TryGetProperty("xpath", out var x) ? x.GetString() : null,
                XpathUtil = payload.TryGetProperty("xpathUtil", out var xu) ? xu.GetString() : null,
                XpathPlaywright = FormatearXpathPlaywright(
                    payload.TryGetProperty("xpathUtil", out var xu2) ? xu2.GetString() : null),
                Tag = payload.TryGetProperty("tag", out var tg) ? tg.GetString() : null,
                Texto = payload.TryGetProperty("texto", out var tx) ? tx.GetString() : null,
                Id = payload.TryGetProperty("id", out var id) ? id.GetString() : null,
                Clases = payload.TryGetProperty("clases", out var cl) ? cl.GetString() : null,
                Href = payload.TryGetProperty("href", out var hr) ? hr.GetString() : null,
                Url = payload.TryGetProperty("url", out var u) ? u.GetString() : null,
                TimestampUtc = DateTime.UtcNow
            };

            try
            {
                ev.CoincidenciasCodigo = UiMapeoCodigoBusqueda
                    .Buscar(sesion.AutomatizacionRoot, ev, catalogoReposPath: sesion.CatalogoReposPath)
                    .ToList();
            }
            catch
            {
                ev.CoincidenciasCodigo = [];
            }

            sesion.Eventos.Add(ev);
        }
    }

    private static string? FormatearXpathPlaywright(string? xpathUtil)
    {
        if (string.IsNullOrWhiteSpace(xpathUtil))
            return null;
        return xpathUtil.StartsWith("xpath=", StringComparison.OrdinalIgnoreCase)
            ? xpathUtil
            : "xpath=" + xpathUtil;
    }

    private static async Task CerrarSesionInternaAsync()
    {
        var s = _sesion;
        _sesion = null;
        if (s is null)
            return;

        if (s.Eventos.Count > 0)
            _ultimaSesion = UiMapeoSesionSnapshot.DesdeActiva(s);

        try
        {
            if (s.Page is not null)
            {
                await s.Page.EvaluateAsync("window.__runnerMapeoRecording = false");
                if (s.LoginAutomaticoSot)
                {
                    var urlLogin = UiMapeoConfig.ResolverUrlLogin(s.UrlInicio);
                    await UiMapeoSotLoginHelper.LogoutLocalAsync(s.Page, urlLogin);
                }
            }
        }
        catch { /* ignore */ }

        try { await s.Context.CloseAsync(); } catch { /* ignore */ }
        try { await s.Browser.CloseAsync(); } catch { /* ignore */ }
        s.Playwright.Dispose();
    }

    private sealed class UiMapeoSesionActiva
    {
        public string Id { get; init; } = "";
        public string UrlInicio { get; init; } = "";
        public DateTime InicioUtc { get; init; }
        public string AutomatizacionRoot { get; init; } = "";
        public string ProyectoId { get; init; } = "";
        public string ProyectoNombre { get; init; } = "";
        public string Plantilla { get; init; } = "generico";
        public string CatalogoReposPath { get; init; } = "";
        public bool LoginAutomaticoSot { get; init; }
        public IPlaywright Playwright { get; init; } = null!;
        public IBrowser Browser { get; init; } = null!;
        public IBrowserContext Context { get; init; } = null!;
        public IPage Page { get; init; } = null!;
        public List<MapeoEventoUi> Eventos { get; } = [];
        public List<string>? AvisosLogin { get; set; }
        public string? SucursalObjetivo { get; set; }
        public string? CodigoSucursal { get; set; }
        public object Sync { get; } = new();
    }

    private sealed class UiMapeoSesionSnapshot
    {
        public string Id { get; init; } = "";
        public string UrlInicio { get; init; } = "";
        public DateTime InicioUtc { get; init; }
        public string ProyectoId { get; init; } = "";
        public string ProyectoNombre { get; init; } = "";
        public string Plantilla { get; init; } = "generico";
        public bool LoginAutomaticoSot { get; init; }
        public List<MapeoEventoUi> Eventos { get; init; } = [];

        public static UiMapeoSesionSnapshot DesdeActiva(UiMapeoSesionActiva s) => new()
        {
            Id = s.Id,
            UrlInicio = s.UrlInicio,
            InicioUtc = s.InicioUtc,
            ProyectoId = s.ProyectoId,
            ProyectoNombre = s.ProyectoNombre,
            Plantilla = s.Plantilla,
            LoginAutomaticoSot = s.LoginAutomaticoSot,
            Eventos = s.Eventos.ToList()
        };
    }
}

public sealed class MapeoEventoUi
{
    public int Orden { get; set; }
    public string? Xpath { get; set; }
    public string? XpathUtil { get; set; }
    public string? XpathPlaywright { get; set; }
    public string? Tag { get; set; }
    public string? Texto { get; set; }
    public string? Id { get; set; }
    public string? Clases { get; set; }
    public string? Href { get; set; }
    public string? Url { get; set; }
    public DateTime TimestampUtc { get; set; }
    public List<CoincidenciaCodigoMapeo>? CoincidenciasCodigo { get; set; }
}
