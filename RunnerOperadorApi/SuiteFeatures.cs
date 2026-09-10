using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

/// <summary>
/// Lectura/escritura de .feature de la suite (Features/**, excluye _pruebas).
/// Permite editar escenarios nativos del Runner desde la UI.
/// </summary>
public static class SuiteFeatures
{
    public static void MapEndpoints(WebApplication app, string automatizacionRoot)
    {
        var featuresRoot = Path.Combine(automatizacionRoot, "Features");

        app.MapGet("/suite/feature-by-tag", (string? tag) =>
        {
            if (string.IsNullOrWhiteSpace(tag))
                return Results.Json(new { ok = false, error = "Falta tag." }, statusCode: 400);

            var tagNorm = tag.Trim().TrimStart('@');
            if (string.IsNullOrWhiteSpace(tagNorm))
                return Results.Json(new { ok = false, error = "Tag inválido." }, statusCode: 400);

            if (!Directory.Exists(featuresRoot))
                return Results.Json(new { ok = false, error = "No existe la carpeta Features." }, statusCode: 404);

            foreach (var file in Directory.EnumerateFiles(featuresRoot, "*.feature", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}_pruebas{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                    || file.Contains("/_pruebas/", StringComparison.OrdinalIgnoreCase)
                    || file.Contains("\\_pruebas\\", StringComparison.OrdinalIgnoreCase))
                    continue;

                string contenido;
                try { contenido = File.ReadAllText(file, Encoding.UTF8); }
                catch { continue; }

                // Busca @Tag como palabra (evita coincidencias parciales)
                if (!Regex.IsMatch(contenido, @"@" + Regex.Escape(tagNorm) + @"\b", RegexOptions.IgnoreCase))
                    continue;

                var rel = Path.GetRelativePath(automatizacionRoot, file).Replace('\\', '/');
                return Results.Ok(new
                {
                    ok = true,
                    tag = "@" + tagNorm,
                    ruta = rel,
                    nombre = Path.GetFileName(file),
                    contenido
                });
            }

            return Results.Json(new { ok = false, error = "No se encontró un .feature con el tag @" + tagNorm + "." }, statusCode: 404);
        });

        app.MapPost("/suite/feature", async (HttpRequest req) =>
        {
            using var doc = await JsonDocument.ParseAsync(req.Body);
            var root = doc.RootElement;
            var ruta = root.TryGetProperty("ruta", out var r) ? r.GetString()?.Trim() : null;
            var contenido = root.TryGetProperty("contenido", out var c) ? c.GetString() : null;

            if (string.IsNullOrWhiteSpace(ruta))
                return Results.Json(new { ok = false, error = "Falta la ruta del feature." }, statusCode: 400);
            if (contenido is null)
                return Results.Json(new { ok = false, error = "Falta el contenido." }, statusCode: 400);

            // Solo permitir rutas bajo Features/, nunca _pruebas ni salir del root
            var rel = ruta.Replace('\\', '/').TrimStart('/');
            if (!rel.StartsWith("Features/", StringComparison.OrdinalIgnoreCase))
                return Results.Json(new { ok = false, error = "Solo se pueden editar archivos bajo Features/." }, statusCode: 400);
            if (rel.Contains("..", StringComparison.Ordinal) || rel.Contains("/_pruebas/", StringComparison.OrdinalIgnoreCase))
                return Results.Json(new { ok = false, error = "Ruta no permitida." }, statusCode: 400);

            var full = Path.GetFullPath(Path.Combine(automatizacionRoot, rel.Replace('/', Path.DirectorySeparatorChar)));
            var featuresFull = Path.GetFullPath(featuresRoot);
            if (!full.StartsWith(featuresFull, StringComparison.OrdinalIgnoreCase))
                return Results.Json(new { ok = false, error = "Ruta fuera de Features/." }, statusCode: 400);
            if (!File.Exists(full))
                return Results.Json(new { ok = false, error = "El archivo no existe: " + rel }, statusCode: 404);

            await File.WriteAllTextAsync(full, contenido, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return Results.Ok(new { ok = true, mensaje = "Feature de la suite actualizado.", ruta = rel });
        });
    }
}
