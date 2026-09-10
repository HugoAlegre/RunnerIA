using System.Text.RegularExpressions;
using Microsoft.Playwright;

/// <summary>Login + sucursal SOT para el grabador (evita «sesión activa en otra pestaña» por ir directo a /home/welcome).</summary>
public static class UiMapeoSotLoginHelper
{
    private const int TimeoutMs = 90_000;

    public static async Task<IReadOnlyList<string>> PrepararSesionAsync(IPage page, string automatizacionRoot)
    {
        var avisos = new List<string>();
        var cred = UiMapeoConfig.LeerCredenciales(automatizacionRoot);

        for (var intento = 1; intento <= 4; intento++)
        {
            await LimpiarAlmacenamientoAsync(page);
            await LogoutKeycloakSiCorrespondeAsync(page, cred.UrlLogin);
            await page.GotoAsync(cred.UrlLogin, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = TimeoutMs
            });
            await page.WaitForTimeoutAsync(1200);

            await CerrarDialogosBloqueantesAsync(page, avisos);

            if (await EsKeycloakLoginAsync(page))
            {
                await LoginKeycloakAsync(page, cred);
                await EsperarEstabilizacionPostLoginAsync(page, avisos);
            }
            else if (await EstaEnLoginAsync(page))
            {
                await page.FillAsync("#username", cred.Usuario);
                await page.FillAsync("#password", cred.Contrasena);
                await page.PressAsync("#password", "Enter");
                try
                {
                    await page.Locator("#password").First.WaitForAsync(new LocatorWaitForOptions
                    {
                        State = WaitForSelectorState.Hidden,
                        Timeout = TimeoutMs
                    });
                }
                catch (TimeoutException)
                {
                    await CerrarDialogosBloqueantesAsync(page, avisos);
                }

                await EsperarEstabilizacionPostLoginAsync(page, avisos);
            }
            else if (EsUrlCallbackOAuth(page.Url))
            {
                avisos.Add("Callback OAuth detectado — esperando carga de la aplicación.");
                await EsperarEstabilizacionPostLoginAsync(page, avisos);
            }
            else
            {
                await page.WaitForTimeoutAsync(1500);
                if (await EsKeycloakLoginAsync(page))
                {
                    await LoginKeycloakAsync(page, cred);
                    await EsperarEstabilizacionPostLoginAsync(page, avisos);
                }
            }

            await CerrarDialogosBloqueantesAsync(page, avisos);

            if (await HayConflictoSesionVisibleAsync(page))
            {
                avisos.Add($"Intento {intento}: conflicto de sesión SOT — se cierra diálogo y se reintenta login.");
                await CerrarDialogoConflictoSesionAsync(page, avisos);
                await page.WaitForTimeoutAsync(2000);
                continue;
            }

            await CompletarSucursalSiVisibleAsync(page, cred, avisos);
            await CerrarDialogosBloqueantesAsync(page, avisos);

            if (await HayConflictoSesionVisibleAsync(page))
            {
                avisos.Add($"Intento {intento}: conflicto tras sucursal — reintentando.");
                await CerrarDialogoConflictoSesionAsync(page, avisos);
                continue;
            }

            if (await EstaHomeOperativaAsync(page))
                return avisos;

            if (intento < 4)
                avisos.Add($"Intento {intento}: sesión incompleta — limpiando y reintentando login.");
        }

        if (!await EstaHomeOperativaAsync(page))
        {
            var enLogin = await EstaEnLoginAsync(page);
            var conflicto = await HayConflictoSesionVisibleAsync(page);
            var sucursal = page.Locator("app-branch-selector-dialog").First;
            var sucursalVisible = await sucursal.CountAsync() > 0 && await sucursal.IsVisibleAsync();
            throw new InvalidOperationException(
                "No se pudo iniciar sesión SOT para el mapeo. "
                + $"URL={page.Url}; login={enLogin}; sucursal={sucursalVisible}; conflicto={conflicto}. "
                + (avisos.Count > 0 ? "Avisos: " + string.Join(" | ", avisos) + ". " : "")
                + "Cierre otras ventanas SOT del mismo usuario o use «Liberar sesión» en Mapeo UI.");
        }

        return avisos;
    }

    public static async Task LogoutLocalAsync(IPage page, string? urlLogin = null)
    {
        try
        {
            var logout = page.Locator("button.btn-logout, .btn-logout").First;
            if (await logout.CountAsync() > 0 && await logout.IsVisibleAsync())
            {
                await logout.ClickAsync();
                await page.WaitForTimeoutAsync(2500);
            }
        }
        catch { /* best-effort */ }

        await LimpiarAlmacenamientoAsync(page);

        if (!string.IsNullOrWhiteSpace(urlLogin))
        {
            try
            {
                await page.GotoAsync(urlLogin, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = TimeoutMs
                });
            }
            catch { /* ignore */ }
        }
    }

    private static async Task LogoutKeycloakSiCorrespondeAsync(IPage page, string urlLogin)
    {
        try
        {
            var redirect = Uri.EscapeDataString(urlLogin.TrimEnd('/'));
            var logoutUrl =
                "http://auth.accusys-dev.io/realms/uniweb-cloud/protocol/openid-connect/logout"
                + "?redirect_uri=" + redirect;
            await page.GotoAsync(logoutUrl, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = 20000
            });
            await page.WaitForTimeoutAsync(800);
        }
        catch { /* best-effort */ }
    }

    private static async Task<bool> EsKeycloakLoginAsync(IPage page)
    {
        if (page.Url.Contains("auth.accusys", StringComparison.OrdinalIgnoreCase))
            return true;

        var user = page.GetByRole(AriaRole.Textbox, new() { Name = "Username or email" });
        return await user.CountAsync() > 0 && await user.First.IsVisibleAsync();
    }

    private static async Task LoginKeycloakAsync(IPage page, UiMapeoSotCredenciales cred)
    {
        var user = page.GetByRole(AriaRole.Textbox, new() { Name = "Username or email" });
        var pass = page.GetByRole(AriaRole.Textbox, new() { Name = "Password" });
        await user.First.FillAsync(cred.Usuario);
        await pass.First.FillAsync(cred.Contrasena);
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign In" }).ClickAsync();
        try
        {
            await page.WaitForURLAsync("**/home/**", new PageWaitForURLOptions { Timeout = TimeoutMs });
        }
        catch (TimeoutException)
        {
            /* callback OAuth o sucursal — lo resuelve EsperarEstabilizacionPostLoginAsync */
        }
    }

    private static bool EsUrlCallbackOAuth(string url) =>
        url.Contains("code=", StringComparison.OrdinalIgnoreCase)
        || url.Contains("session_state=", StringComparison.OrdinalIgnoreCase);

    private static async Task EsperarEstabilizacionPostLoginAsync(IPage page, List<string> avisos)
    {
        var limite = DateTime.UtcNow.AddSeconds(75);
        while (DateTime.UtcNow < limite)
        {
            if (await EstaHomeOperativaAsync(page))
                return;

            var dialogo = page.Locator("app-branch-selector-dialog").First;
            if (await dialogo.CountAsync() > 0 && await dialogo.IsVisibleAsync())
                return;

            if (await EstaEnLoginAsync(page) && !EsUrlCallbackOAuth(page.Url))
                return;

            if (EsUrlCallbackOAuth(page.Url))
            {
                await page.WaitForTimeoutAsync(400);
                continue;
            }

            var topbar = page.Locator("app-topbar, button.btn-logout").First;
            if (await topbar.CountAsync() > 0)
            {
                try
                {
                    await topbar.WaitForAsync(new LocatorWaitForOptions
                    {
                        State = WaitForSelectorState.Visible,
                        Timeout = 5000
                    });
                    return;
                }
                catch
                {
                    /* sigue esperando */
                }
            }

            await page.WaitForTimeoutAsync(350);
        }

        avisos.Add("Tiempo de espera post-login agotado (OAuth/home).");
    }

    private static async Task LimpiarAlmacenamientoAsync(IPage page)
    {
        try
        {
            await page.Context.ClearCookiesAsync();
        }
        catch { /* ignore */ }

        try
        {
            await page.EvaluateAsync(
                """
                () => {
                  try {
                    localStorage.clear();
                    sessionStorage.clear();
                    const id = (typeof crypto !== 'undefined' && crypto.randomUUID)
                      ? crypto.randomUUID()
                      : 'runner-mapeo-' + Date.now();
                    sessionStorage.setItem('browserTabId', id);
                  } catch (e) {}
                }
                """);
        }
        catch { /* ignore */ }
    }

    private static async Task CerrarDialogosBloqueantesAsync(IPage page, List<string> avisos)
    {
        await ClickBotonDialogoSiVisibleAsync(page, "Continuar de todos modos", "Socket: Continuar de todos modos.", avisos);
        await ClickBotonDialogoSiVisibleAsync(page, "Aceptar", "Diálogo cerrado (Aceptar).", avisos);
        await ClickBotonDialogoSiVisibleAsync(page, "Entendido", "Diálogo cerrado (Entendido).", avisos);
    }

    private static async Task<bool> HayConflictoSesionVisibleAsync(IPage page)
    {
        var dialogos = page.Locator("mat-dialog-container, [role='dialog'], .cdk-overlay-container .mat-mdc-dialog-container");
        var n = await dialogos.CountAsync();
        for (var i = 0; i < n; i++)
        {
            var d = dialogos.Nth(i);
            if (!await d.IsVisibleAsync())
                continue;

            var text = (await d.InnerTextAsync()).Replace('\r', ' ').Replace('\n', ' ');
            if (text.Contains("sesión activa", StringComparison.OrdinalIgnoreCase)
                || text.Contains("sesion activa", StringComparison.OrdinalIgnoreCase)
                || text.Contains("otra pestaña", StringComparison.OrdinalIgnoreCase)
                || text.Contains("otra pestana", StringComparison.OrdinalIgnoreCase)
                || text.Contains("otra terminal", StringComparison.OrdinalIgnoreCase)
                || text.Contains("ERROR_UPDATING", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static async Task CerrarDialogoConflictoSesionAsync(IPage page, List<string> avisos)
    {
        await ClickBotonDialogoSiVisibleAsync(page, "Aceptar", "Conflicto sesión: Aceptar (libera Redis/Keycloak).", avisos);
        await page.WaitForTimeoutAsync(1500);
        await LimpiarAlmacenamientoAsync(page);
    }

    private static async Task ClickBotonDialogoSiVisibleAsync(
        IPage page,
        string textoBoton,
        string aviso,
        List<string> avisos)
    {
        var escaped = textoBoton.Replace("'", "\\'");
        var xpath =
            $"xpath=//div[contains(@class,'cdk-overlay-container')]//button"
            + $"[contains(normalize-space(.),'{escaped}') or .//span[contains(normalize-space(.),'{escaped}')]]"
            + $" | //mat-dialog-container//button[contains(normalize-space(.),'{escaped}') or .//span[contains(normalize-space(.),'{escaped}')]]"
            + $" | //*[@role='dialog']//button[contains(normalize-space(.),'{escaped}') or .//span[contains(normalize-space(.),'{escaped}')]]";

        var btn = page.Locator(xpath).First;

        if (await btn.CountAsync() == 0 || !await btn.IsVisibleAsync())
            return;

        try
        {
            await btn.ClickAsync(new LocatorClickOptions { Timeout = 5000 });
            await page.WaitForTimeoutAsync(600);
            if (!avisos.Contains(aviso))
                avisos.Add(aviso);
        }
        catch { /* ignore */ }
    }

    private static async Task CompletarSucursalSiVisibleAsync(
        IPage page,
        UiMapeoSotCredenciales cred,
        List<string> avisos)
    {
        var dialogo = page.Locator("app-branch-selector-dialog").First;
        if (await dialogo.CountAsync() == 0 || !await dialogo.IsVisibleAsync())
            return;

        await ClickBotonDialogoSiVisibleAsync(page, "Continuar de todos modos", "Socket omitido en selector sucursal.", avisos);

        var buscar = page.Locator(
            "app-branch-selector-dialog input[placeholder='Buscar sucursal...'], input[placeholder='Buscar sucursal...']").First;
        await buscar.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = TimeoutMs
        });

        var filtros = ObtenerFiltrosSucursalEnOrden(cred);
        var seleccionada = false;
        foreach (var filtro in filtros)
        {
            await buscar.FillAsync("");
            if (!string.IsNullOrWhiteSpace(filtro))
                await buscar.FillAsync(filtro);
            await page.WaitForTimeoutAsync(800);

            if (await IntentarSeleccionarSucursalObjetivoAsync(page, cred.SucursalObjetivo))
            {
                seleccionada = true;
                break;
            }
        }

        if (!seleccionada)
        {
            var visibles = await ListarOpcionesSucursalVisiblesAsync(page);
            throw new InvalidOperationException(
                $"No se encontró la sucursal «{cred.SucursalObjetivo}» (código {cred.FiltroSucursal}). "
                + "Revise DialogoSucursal en appsettings o Runner → Configuración. "
                + $"Opciones visibles: {string.Join(" | ", visibles.Take(8))}");
        }

        var confirmar = page.Locator(
            "xpath=//app-branch-selector-dialog//button[.//span[normalize-space()='Confirmar'] or normalize-space()='Confirmar']").First;
        await confirmar.ClickAsync();
        await page.WaitForTimeoutAsync(2500);
        avisos.Add($"Sucursal seleccionada: {cred.SucursalObjetivo} (código {cred.FiltroSucursal}).");

        await CerrarDialogosBloqueantesAsync(page, avisos);
    }

    private static IReadOnlyList<string> ObtenerFiltrosSucursalEnOrden(UiMapeoSotCredenciales cred)
    {
        var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lista = new List<string>();

        void Agregar(string? t)
        {
            if (string.IsNullOrWhiteSpace(t) || !vistos.Add(t.Trim()))
                return;
            lista.Add(t.Trim());
        }

        Agregar(cred.FiltroSucursal);
        Agregar(cred.SucursalObjetivo);
        var codigoObj = Regex.Match(cred.SucursalObjetivo ?? "", @"^\s*(\d+)").Groups[1].Value;
        if (!string.IsNullOrWhiteSpace(codigoObj))
            Agregar(codigoObj);

        if (lista.Count == 0)
            lista.Add("");

        return lista;
    }

    private static async Task<bool> IntentarSeleccionarSucursalObjetivoAsync(IPage page, string objetivo)
    {
        if (string.IsNullOrWhiteSpace(objetivo))
            return false;

        var escaped = objetivo.Replace("'", "\\'");
        var porTexto = page.Locator(
            "xpath=//div[contains(@class,'cdk-overlay-container')]"
            + $"//*[local-name()='mat-option' or local-name()='mat-mdc-option'][contains(normalize-space(.),'{escaped}')]"
            + " | //app-branch-selector-dialog//mat-option[1]").First;

        if (await porTexto.CountAsync() > 0 && await porTexto.IsVisibleAsync())
        {
            await porTexto.ClickAsync();
            return true;
        }

        var primera = page.Locator(
            "xpath=//div[contains(@class,'cdk-overlay-container')]//mat-option[1]"
            + " | //app-branch-selector-dialog//mat-option[1]").First;
        if (await primera.CountAsync() > 0 && await primera.IsVisibleAsync())
        {
            var txt = (await primera.InnerTextAsync()).Trim();
            if (txt.Contains(objetivo, StringComparison.OrdinalIgnoreCase)
                || objetivo.Contains(txt, StringComparison.OrdinalIgnoreCase))
            {
                await primera.ClickAsync();
                return true;
            }
        }

        return false;
    }

    private static async Task<IReadOnlyList<string>> ListarOpcionesSucursalVisiblesAsync(IPage page)
    {
        var opts = page.Locator(
            "xpath=//div[contains(@class,'cdk-overlay-container')]"
            + "//*[local-name()='mat-option' or local-name()='mat-mdc-option']");
        var n = await opts.CountAsync();
        var textos = new List<string>();
        for (var i = 0; i < Math.Min(n, 12); i++)
        {
            var t = (await opts.Nth(i).InnerTextAsync()).Trim();
            if (!string.IsNullOrWhiteSpace(t))
                textos.Add(t);
        }

        return textos;
    }

    private static async Task<bool> EstaEnLoginAsync(IPage page)
    {
        var login = page.Locator("#username").First;
        return await login.CountAsync() > 0 && await login.IsVisibleAsync();
    }

    private static async Task<bool> EstaHomeOperativaAsync(IPage page)
    {
        if (await EstaEnLoginAsync(page))
            return false;

        if (await HayConflictoSesionVisibleAsync(page))
            return false;

        var dialogo = page.Locator("app-branch-selector-dialog").First;
        if (await dialogo.CountAsync() > 0 && await dialogo.IsVisibleAsync())
            return false;

        var topbar = page.Locator("app-topbar, button.btn-logout").First;
        return await topbar.CountAsync() > 0 && await topbar.IsVisibleAsync();
    }
}
