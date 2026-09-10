using System.Text;
using System.Text.Json;

public static class UiMapeoEndpoints
{
    public static void Map(WebApplication app, string automatizacionRoot)
    {
        app.MapGet("/mapeo-ui/estado", () => Results.Ok(UiMapeoGrabador.Estado()));

        app.MapGet("/mapeo-ui/url-default", (string? proyectoId) =>
        {
            try
            {
                return Results.Ok(UiMapeoConfig.LeerVistaPrevia(automatizacionRoot, proyectoId));
            }
            catch (Exception ex)
            {
                var url = LeerUrlInicio(automatizacionRoot) ?? "http://localhost:4200";
                return Results.Ok(new
                {
                    ok = false,
                    url,
                    error = ex.Message,
                    nota = "Configure proyecto activo, integraciones (URL app) o appsettings SOT."
                });
            }
        });

        app.MapPost("/mapeo-ui/iniciar", async (MapeoIniciarRequest? body) =>
        {
            var url = (body?.Url ?? "").Trim();
            if (string.IsNullOrWhiteSpace(url))
            {
                try
                {
                    var ctx = UiMapeoConfig.ResolverContexto(automatizacionRoot, body?.ProyectoId);
                    url = ctx.UrlInicio;
                }
                catch
                {
                    url = LeerUrlInicio(automatizacionRoot) ?? "";
                }
            }
            if (string.IsNullOrWhiteSpace(url))
                return Results.BadRequest(new { ok = false, error = "Indique URL o configure la app del proyecto." });

            try
            {
                var r = await UiMapeoGrabador.IniciarAsync(
                    url,
                    automatizacionRoot,
                    headless: body?.Headless == true,
                    proyectoId: body?.ProyectoId,
                    loginAutomatico: body?.LoginAutomatico);
                return Results.Ok(r);
            }
            catch (Exception ex)
            {
                return Results.Json(
                    new { ok = false, error = "No se pudo iniciar el mapeo: " + ex.Message },
                    statusCode: 500);
            }
        });

        app.MapPost("/mapeo-ui/detener", async () =>
        {
            try
            {
                return Results.Ok(await UiMapeoGrabador.DetenerAsync(automatizacionRoot));
            }
            catch (Exception ex)
            {
                return Results.Json(new { ok = false, error = ex.Message }, statusCode: 500);
            }
        });

        app.MapGet("/mapeo-ui/ultima-sesion", () =>
        {
            var data = UiMapeoGrabador.LeerUltimaSesionGuardada(automatizacionRoot);
            return data is null
                ? Results.Ok(new { ok = true, disponible = false })
                : Results.Ok(data);
        });

        app.MapGet("/mapeo-ui/eventos", (int? desde) =>
            Results.Ok(UiMapeoGrabador.ListarEventos(desde)));

        app.MapPost("/mapeo-ui/buscar-codigo", (MapeoBuscarCodigoRequest? body) =>
        {
            var q = (body?.Consulta ?? body?.Texto ?? "").Trim();
            if (string.IsNullOrWhiteSpace(q) && string.IsNullOrWhiteSpace(body?.Xpath))
                return Results.BadRequest(new { ok = false, error = "Indique consulta o xpath." });

            var hits = UiMapeoGrabador.BuscarCodigoManual(
                automatizacionRoot,
                q,
                body?.Xpath?.Trim(),
                UiMapeoGrabador.CatalogoSesionActiva());

            return Results.Ok(new { ok = true, total = hits.Count, coincidencias = hits });
        });

        app.MapGet("/mapeo-ui/exportar.json", () =>
        {
            var bytes = UiMapeoGrabador.ExportarJson();
            return Results.File(bytes, "application/json", "mapeo-ui-sesion.json");
        });

        app.MapPost("/mapeo-ui/liberar", async () =>
        {
            try
            {
                return Results.Ok(await UiMapeoGrabador.DetenerAsync(automatizacionRoot));
            }
            catch (Exception ex)
            {
                return Results.Json(new { ok = false, error = ex.Message }, statusCode: 500);
            }
        });
    }

    private static string? LeerUrlInicio(string automatizacionRoot)
    {
        try
        {
            var path = Path.Combine(automatizacionRoot, "appsettings.json");
            if (!File.Exists(path))
                return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.TryGetProperty("Aplicacion", out var app)
                && app.TryGetProperty("UrlInicio", out var url))
            {
                var v = url.GetString()?.Trim();
                return string.IsNullOrWhiteSpace(v) ? null : v;
            }
        }
        catch { /* ignore */ }

        return null;
    }
}

public sealed class MapeoIniciarRequest
{
    public string? Url { get; set; }
    public bool Headless { get; set; }
    public string? ProyectoId { get; set; }
    public bool? LoginAutomatico { get; set; }
}

public sealed class MapeoBuscarCodigoRequest
{
    public string? Consulta { get; set; }
    public string? Texto { get; set; }
    public string? Xpath { get; set; }
}
