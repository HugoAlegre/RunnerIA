using System.Text.Json;

/// <summary>Persistencia local de sesiones Mapeo UI (mapeos-ui/, no versionado en Git).</summary>
public static class UiMapeoPersistencia
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static string CarpetaAbsoluta(string automatizacionRoot) =>
        Path.Combine(automatizacionRoot, "mapeos-ui");

    public static (string rutaAbsoluta, string rutaRelativa, string nombreArchivo) Guardar(
        string automatizacionRoot,
        byte[] jsonBytes,
        string sesionId)
    {
        var dir = CarpetaAbsoluta(automatizacionRoot);
        Directory.CreateDirectory(dir);

        var id = string.IsNullOrWhiteSpace(sesionId) ? "sesion" : sesionId.Trim();
        var stamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        var nombre = $"{stamp}_{id}.json";
        var full = Path.Combine(dir, nombre);
        File.WriteAllBytes(full, jsonBytes);

        var ultima = Path.Combine(dir, "ultima-sesion.json");
        File.WriteAllBytes(ultima, jsonBytes);

        var rel = Path.Combine("mapeos-ui", nombre).Replace('\\', '/');
        return (full, rel, nombre);
    }

    public static byte[]? LeerUltimaSesionBytes(string automatizacionRoot)
    {
        var path = Path.Combine(CarpetaAbsoluta(automatizacionRoot), "ultima-sesion.json");
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }

    public static object? LeerUltimaSesion(string automatizacionRoot)
    {
        var bytes = LeerUltimaSesionBytes(automatizacionRoot);
        if (bytes is null || bytes.Length == 0)
            return null;

        try
        {
            using var doc = JsonDocument.Parse(bytes);
            var root = doc.RootElement;
            var eventos = root.TryGetProperty("eventos", out var ev) && ev.ValueKind == JsonValueKind.Array
                ? ev.GetArrayLength()
                : 0;

            return new
            {
                ok = true,
                guardadoEn = "mapeos-ui/ultima-sesion.json",
                sesionId = root.TryGetProperty("sesionId", out var sid) ? sid.GetString() : null,
                proyectoId = root.TryGetProperty("proyectoId", out var pid) ? pid.GetString() : null,
                proyectoNombre = root.TryGetProperty("proyectoNombre", out var pn) ? pn.GetString() : null,
                urlInicio = root.TryGetProperty("urlInicio", out var u) ? u.GetString() : null,
                totalEventos = eventos,
                generadoUtc = root.TryGetProperty("generadoUtc", out var g) ? g.GetString() : null,
                gherkin = root.TryGetProperty("gherkin", out var gh) ? gh.GetString() : null,
                eventos = root.TryGetProperty("eventos", out var ev2) ? JsonSerializer.Deserialize<object>(ev2.GetRawText()) : null,
                json = JsonSerializer.Deserialize<object>(root.GetRawText())
            };
        }
        catch
        {
            return null;
        }
    }
}
