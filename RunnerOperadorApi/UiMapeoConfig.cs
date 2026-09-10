using System.Text.Json;
using System.Text.RegularExpressions;
using AutomatizacionSOT.Config;

/// <summary>Config Mapeo UI por proyecto Runner (SOT o genérico).</summary>
public static class UiMapeoConfig
{
    private static readonly JsonDocumentOptions JsonOpts = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    public static UiMapeoSotCredenciales LeerCredenciales(string automatizacionRoot)
    {
        var appPath = Path.Combine(automatizacionRoot, "appsettings.json");
        if (!File.Exists(appPath))
            throw new InvalidOperationException("No se encontró appsettings.json en AutomatizacionSOT.");

        using var app = JsonDocument.Parse(File.ReadAllText(appPath), JsonOpts);
        var aplicacion = app.RootElement.GetProperty("Aplicacion");
        var urlInicio = aplicacion.GetProperty("UrlInicio").GetString()?.Trim()
            ?? throw new InvalidOperationException("Falta Aplicacion:UrlInicio.");

        var usuario = aplicacion.TryGetProperty("Usuario", out var u) ? u.GetString()?.Trim() : null;
        if (string.IsNullOrWhiteSpace(usuario))
            throw new InvalidOperationException("Falta Aplicacion:Usuario en appsettings.json.");

        string? sucursalObjetivo = null;
        string? textoFiltro = null;
        if (app.RootElement.TryGetProperty("DialogoSucursal", out var dlg))
        {
            if (dlg.TryGetProperty("SucursalObjetivo", out var so))
                sucursalObjetivo = so.GetString()?.Trim();
            if (dlg.TryGetProperty("TextoFiltroSucursal", out var tf))
                textoFiltro = tf.GetString()?.Trim();
        }

        var codigoSucursal = ResolverCodigoSucursal(textoFiltro, sucursalObjetivo);
        if (string.IsNullOrWhiteSpace(sucursalObjetivo))
            sucursalObjetivo = codigoSucursal;

        var password = LeerPassword(automatizacionRoot, usuario);

        return new UiMapeoSotCredenciales
        {
            UrlLogin = ResolverUrlLogin(urlInicio),
            UrlInicio = urlInicio,
            Usuario = usuario,
            Contrasena = password,
            FiltroSucursal = codigoSucursal,
            SucursalObjetivo = sucursalObjetivo
        };
    }

    /// <summary>Contexto Mapeo UI según proyecto activo (o id explícito).</summary>
    public static UiMapeoContext ResolverContexto(string automatizacionRoot, string? proyectoId = null)
    {
        var proyecto = RunnerIaProyectos.ResolverProyecto(automatizacionRoot, proyectoId)
            ?? throw new InvalidOperationException("No hay proyecto Runner configurado.");

        var esSot = EsPlantillaSot(proyecto);
        var catalogo = Path.GetFullPath(
            Path.Combine(
                automatizacionRoot,
                (proyecto.CatalogoConfigRelativo ?? "Catalogo/repos-celula.json")
                    .Replace('/', Path.DirectorySeparatorChar)
                    .TrimStart('\\', '/')));

        if (esSot)
        {
            var cred = LeerCredenciales(automatizacionRoot);
            return new UiMapeoContext
            {
                ProyectoId = proyecto.Id,
                ProyectoNombre = proyecto.Nombre,
                Plantilla = "sot",
                UrlInicio = cred.UrlInicio,
                LoginAutomaticoSot = true,
                CatalogoReposPath = catalogo,
                Usuario = cred.Usuario,
                SucursalObjetivo = cred.SucursalObjetivo,
                CodigoSucursal = cred.FiltroSucursal,
                CredencialesSot = cred,
                Nota =
                    "Login SOT opcional (Configuración). Desactivá si preferís login manual."
            };
        }

        var urlApp = ResolverUrlIntegracionApp(proyecto)
                     ?? LeerUrlInicioOpcional(automatizacionRoot);

        return new UiMapeoContext
        {
            ProyectoId = proyecto.Id,
            ProyectoNombre = proyecto.Nombre,
            Plantilla = "generico",
            UrlInicio = urlApp ?? "",
            LoginAutomaticoSot = false,
            CatalogoReposPath = catalogo,
            Nota = string.IsNullOrWhiteSpace(urlApp)
                ? "Indicá la URL de la app abajo."
                : null
        };
    }

    /// <summary>Vista previa para UI/MCP.</summary>
    public static object LeerVistaPrevia(string automatizacionRoot, string? proyectoId = null)
    {
        try
        {
            var ctx = ResolverContexto(automatizacionRoot, proyectoId);
            return new
            {
                ok = true,
                proyectoId = ctx.ProyectoId,
                proyectoNombre = ctx.ProyectoNombre,
                plantilla = ctx.Plantilla,
                url = ctx.UrlInicio,
                loginAutomaticoSot = ctx.LoginAutomaticoSot,
                loginAutomaticoDefault = ctx.LoginAutomaticoSot,
                usuario = ctx.Usuario,
                sucursalObjetivo = ctx.SucursalObjetivo,
                codigoSucursal = ctx.CodigoSucursal,
                nota = ctx.Nota
            };
        }
        catch (Exception ex)
        {
            return new
            {
                ok = false,
                url = LeerUrlInicioOpcional(automatizacionRoot) ?? "http://localhost:4200",
                error = ex.Message,
                nota = "Revise proyecto activo, integraciones (URL app) o appsettings SOT."
            };
        }
    }

    private static bool EsPlantillaSot(RunnerProyecto proyecto) =>
        string.Equals(proyecto.Plantilla, "sot", StringComparison.OrdinalIgnoreCase)
        || (string.IsNullOrWhiteSpace(proyecto.Plantilla)
            && string.Equals(proyecto.Id, "sot-caja", StringComparison.OrdinalIgnoreCase));

    private static string? ResolverUrlIntegracionApp(RunnerProyecto proyecto)
    {
        foreach (var integ in proyecto.Integraciones ?? [])
        {
            if (!string.Equals(integ.Tipo, "app", StringComparison.OrdinalIgnoreCase))
                continue;
            var url = integ.Url?.Trim();
            if (!string.IsNullOrWhiteSpace(url))
                return url;
        }

        return null;
    }

    private static string? LeerUrlInicioOpcional(string automatizacionRoot)
    {
        try
        {
            var path = Path.Combine(automatizacionRoot, "appsettings.json");
            if (!File.Exists(path))
                return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(path), JsonOpts);
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

    internal static string ResolverCodigoSucursal(string? textoFiltro, string? sucursalObjetivo)
    {
        if (!string.IsNullOrWhiteSpace(textoFiltro))
        {
            var digitos = new string(textoFiltro.Where(char.IsDigit).ToArray());
            if (!string.IsNullOrEmpty(digitos))
                return digitos;
            return textoFiltro.Trim();
        }

        if (!string.IsNullOrWhiteSpace(sucursalObjetivo))
        {
            var m = Regex.Match(sucursalObjetivo, @"^\s*(\d+)");
            if (m.Success)
                return m.Groups[1].Value;
        }

        throw new InvalidOperationException(
            "Configure DialogoSucursal:TextoFiltroSucursal o DialogoSucursal:SucursalObjetivo "
            + "en appsettings.json (Runner → Configuración). Cada operador puede usar su sucursal.");
    }

    private static string LeerPassword(string automatizacionRoot, string usuario)
    {
        var secretsPath = Path.Combine(automatizacionRoot, "appsettings.secrets.json");
        string? raw = null;

        if (File.Exists(secretsPath))
        {
            using var sec = JsonDocument.Parse(File.ReadAllText(secretsPath), JsonOpts);
            if (sec.RootElement.TryGetProperty("Aplicacion", out var appSec)
                && appSec.TryGetProperty("Contrasena", out var pw))
                raw = pw.GetString();

            if (string.IsNullOrWhiteSpace(raw) || raw == "***")
            {
                if (sec.RootElement.TryGetProperty("UsuariosSot", out var arr))
                {
                    foreach (var item in arr.EnumerateArray())
                    {
                        var u = item.TryGetProperty("Usuario", out var ju) ? ju.GetString() : null;
                        if (!string.Equals(u, usuario, StringComparison.OrdinalIgnoreCase))
                            continue;
                        if (item.TryGetProperty("Contrasena", out var jc))
                            raw = jc.GetString();
                        break;
                    }
                }
            }
        }

        if (string.IsNullOrWhiteSpace(raw))
            throw new InvalidOperationException(
                "Configure la contraseña en appsettings.secrets.json (Aplicacion:Contrasena o UsuariosSot).");

        return SecretProtector.UnprotectFromStorage(raw.Trim());
    }

    public static string ResolverUrlLogin(string urlInicio)
    {
        var url = urlInicio.Trim();
        var idx = url.IndexOf("/home", StringComparison.OrdinalIgnoreCase);
        return idx > 0 ? url[..idx].TrimEnd('/') : url.TrimEnd('/');
    }
}

public sealed class UiMapeoContext
{
    public string ProyectoId { get; init; } = "";
    public string ProyectoNombre { get; init; } = "";
    public string Plantilla { get; init; } = "generico";
    public string UrlInicio { get; init; } = "";
    public bool LoginAutomaticoSot { get; init; }
    public string CatalogoReposPath { get; init; } = "";
    public string? Usuario { get; init; }
    public string? SucursalObjetivo { get; init; }
    public string? CodigoSucursal { get; init; }
    public UiMapeoSotCredenciales? CredencialesSot { get; init; }
    public string? Nota { get; init; }
}

public sealed class UiMapeoSotCredenciales
{
    public string UrlLogin { get; init; } = "";
    public string UrlInicio { get; init; } = "";
    public string Usuario { get; init; } = "";
    public string Contrasena { get; init; } = "";
    /// <summary>Código para filtrar en «Buscar sucursal…» (DialogoSucursal:TextoFiltroSucursal).</summary>
    public string FiltroSucursal { get; init; } = "";
    /// <summary>Texto objetivo (DialogoSucursal:SucursalObjetivo, ej. «131 - San Martin»).</summary>
    public string SucursalObjetivo { get; init; } = "";
}
