using System.Text.RegularExpressions;

namespace AutomatizacionSOT.Config;

/// <summary>
/// Quita contraseñas y el JSON de secretos de logs, informes y ZIP.
/// El archivo real (<c>appsettings.secrets.json</c>) no se versiona: solo la plantilla vacía.
/// </summary>
public static class SecretRedactor
{
    private static readonly Regex RxClaveValor = new(
        @"(?i)(?<![A-Za-z])(password|contraseña|contrasena|api[_-]?token|api[_-]?key|pat|bearer|connectionstring|pwd|pin)\s*[:=]\s*[^\s;]+",
        RegexOptions.Compiled);

    private static readonly Regex RxCadenaConexion = new(
        @"(?i)(Password|User ID|UID|PWD)=([^;]+)",
        RegexOptions.Compiled);

    private static readonly Regex RxEnc = new(
        @"(?i)enc:[A-Za-z0-9+/=]{8,}",
        RegexOptions.Compiled);

    private static readonly Regex RxDpapi = new(
        @"(?i)dpapi:[A-Za-z0-9+/=]{8,}",
        RegexOptions.Compiled);

    private static readonly Regex RxJsonSecreto = new(
        @"(?i)(""(?:Contrasena|Password|Pin|Pat|ClientSecret|Secret)""\s*:\s*)""[^""]*""",
        RegexOptions.Compiled);

    public static string Redactar(string? texto)
    {
        if (string.IsNullOrEmpty(texto))
            return "";

        var t = texto;
        t = RxClaveValor.Replace(t, "$1=[REDACTED]");
        t = RxCadenaConexion.Replace(t, "$1=[REDACTED]");
        t = RxEnc.Replace(t, "enc:[REDACTED]");
        t = RxDpapi.Replace(t, "dpapi:[REDACTED]");
        t = RxJsonSecreto.Replace(t, "$1\"\"");
        t = Regex.Replace(t, @"(?i)appsettings\.secrets\.json", "appsettings.[secrets]");
        return t;
    }

    public static bool EsNombreArchivoSensible(string? nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre))
            return false;
        var n = nombre.Trim();
        return n.Contains("secrets", StringComparison.OrdinalIgnoreCase)
               || n.EndsWith(".secrets.json", StringComparison.OrdinalIgnoreCase)
               || n.Contains("password", StringComparison.OrdinalIgnoreCase)
               || n.Contains(".runner-auth", StringComparison.OrdinalIgnoreCase)
               || n.Contains("ultima-clave-recuperacion", StringComparison.OrdinalIgnoreCase);
    }
}
