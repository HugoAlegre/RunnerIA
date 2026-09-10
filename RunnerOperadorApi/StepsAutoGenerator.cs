using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// Detecta pasos Gherkin del .feature sin binding SpecFlow y genera stubs en PruebaAutoSteps.cs
/// para que el caso sea compilable/ejecutable (Pending/Ignore hasta automatizar la UI).
/// </summary>
public static class StepsAutoGenerator
{
    private const string AutoFileName = "PruebaAutoSteps.cs";
    private const string MarkerBegin = "// <auto-prueba-steps>";
    private const string MarkerEnd = "// </auto-prueba-steps>";

    public static (int Creados, string Archivo, List<string> PasosNuevos) AsegurarBindings(
        string automatizacionRoot,
        string featureGherkin)
    {
        var pasosFeature = ExtraerPasosFeature(featureGherkin);
        if (pasosFeature.Count == 0)
            return (0, "", []);

        var bindings = IndexarBindingsExistentes(automatizacionRoot);
        var faltantes = new List<(string Tipo, string Cuerpo)>();
        foreach (var (tipo, cuerpo) in pasosFeature)
        {
            if (EstaCubierto(cuerpo, bindings))
                continue;
            if (faltantes.Any(f =>
                    string.Equals(f.Cuerpo, cuerpo, StringComparison.OrdinalIgnoreCase)))
                continue;
            faltantes.Add((tipo, cuerpo));
        }

        if (faltantes.Count == 0)
            return (0, "", []);

        var dir = Path.Combine(automatizacionRoot, "StepDefinitions");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, AutoFileName);
        if (!File.Exists(path))
            File.WriteAllText(path, PlantillaArchivoVacio(), new UTF8Encoding(false));

        var texto = File.ReadAllText(path);
        var existentesEnAuto = ExtraerCuerposDeArchivoAuto(texto);
        var aAgregar = faltantes
            .Where(f => !existentesEnAuto.Contains(f.Cuerpo))
            .ToList();
        if (aAgregar.Count == 0)
            return (0, path, []);

        var sbMetodos = new StringBuilder();
        foreach (var (tipo, cuerpo) in aAgregar)
            sbMetodos.Append(GenerarMetodoStub(tipo, cuerpo));

        texto = InsertarMetodos(texto, sbMetodos.ToString());
        File.WriteAllText(path, texto, new UTF8Encoding(false));
        return (aAgregar.Count, path, aAgregar.Select(a => a.Tipo + " " + a.Cuerpo).ToList());
    }

    private static List<(string Tipo, string Cuerpo)> ExtraerPasosFeature(string gherkin)
    {
        var list = new List<(string, string)>();
        foreach (var raw in (gherkin ?? "").Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            var m = Regex.Match(line, @"^(Given|When|Then|And)\s+(.+)$", RegexOptions.IgnoreCase);
            if (!m.Success) continue;
            var tipo = m.Groups[1].Value;
            if (tipo.Equals("And", StringComparison.OrdinalIgnoreCase))
                tipo = list.Count > 0 ? list[^1].Item1 : "When";
            var cuerpo = m.Groups[2].Value.Trim();
            if (cuerpo.Length < 5) continue;
            if (cuerpo.Contains("FALTA INFORMACIÓN", StringComparison.OrdinalIgnoreCase)) continue;
            list.Add((CapitalizarTipo(tipo), cuerpo));
        }
        return list;
    }

    private static List<(string Tipo, string Pattern)> IndexarBindingsExistentes(string root)
    {
        var dir = Path.Combine(root, "StepDefinitions");
        var list = new List<(string, string)>();
        if (!Directory.Exists(dir)) return list;
        foreach (var file in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
        {
            string content;
            try { content = File.ReadAllText(file); }
            catch { continue; }
            foreach (Match m in Regex.Matches(content,
                         @"\[(Given|When|Then)\(@""([^""]+)""\)\]",
                         RegexOptions.IgnoreCase))
            {
                list.Add((m.Groups[1].Value, m.Groups[2].Value));
            }
        }
        return list;
    }

    private static bool EstaCubierto(string cuerpoPaso, List<(string Tipo, string Pattern)> bindings)
    {
        foreach (var (_, pattern) in bindings)
        {
            try
            {
                if (Regex.IsMatch(cuerpoPaso, "^" + pattern + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                    return true;
            }
            catch
            {
                // patrón inválido: comparar literal aproximado
                var lit = Regex.Replace(pattern, @"\([^)]*\)", "").Trim();
                if (!string.IsNullOrWhiteSpace(lit) &&
                    cuerpoPaso.Contains(lit, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        return false;
    }

    private static HashSet<string> ExtraerCuerposDeArchivoAuto(string texto)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(texto, @"\[(Given|When|Then)\(@""([^""]+)""\)\]"))
        {
            // En auto-steps el patrón es Regex.Escape → Unescape para comparar
            var pat = m.Groups[2].Value;
            try { set.Add(Regex.Unescape(pat)); }
            catch { set.Add(pat); }
        }
        // También marca // step: ...
        foreach (Match m in Regex.Matches(texto, @"//\s*step:\s*(.+)"))
            set.Add(m.Groups[1].Value.Trim());
        return set;
    }

    private static string InsertarMetodos(string archivo, string metodos)
    {
        var idxBegin = archivo.IndexOf(MarkerBegin, StringComparison.Ordinal);
        var idxEnd = archivo.IndexOf(MarkerEnd, StringComparison.Ordinal);
        if (idxBegin >= 0 && idxEnd > idxBegin)
        {
            var insertAt = idxEnd;
            return archivo[..insertAt] + metodos + archivo[insertAt..];
        }

        // Fallback: antes del cierre de clase
        var cierre = archivo.LastIndexOf('}');
        if (cierre > 0)
            return archivo[..cierre] + "\n" + MarkerBegin + "\n" + metodos + MarkerEnd + "\n" + archivo[cierre..];
        return archivo + "\n" + metodos;
    }

    private static string GenerarMetodoStub(string tipo, string cuerpo)
    {
        var attrTipo = CapitalizarTipo(tipo);
        var escaped = Regex.Escape(cuerpo);
        var method = "Auto_" + HashCorto(cuerpo) + "_" + SanitizarNombreMetodo(cuerpo);
        var sb = new StringBuilder();
        sb.AppendLine($"    // step: {cuerpo}");
        sb.AppendLine($"    [{attrTipo}(@\"{escaped}\")]");
        sb.AppendLine($"    public async Task {method}()");
        sb.AppendLine("    {");
        sb.AppendLine("        var page = await PlaywrightDriverFactory.GetPageAsync();");
        sb.AppendLine($"        Console.Out.WriteLine(\"[PruebaAuto] {EscapeCs(cuerpo)}\");");
        sb.AppendLine("        // Stub auto-generado: deja evidencia y no frena el resto del run.");
        sb.AppendLine("        try { await page.WaitForTimeoutAsync(300); } catch { /* ignore */ }");
        sb.AppendLine($"        Assert.Ignore(\"Step auto-generado pendiente de automatizar UI: {EscapeCs(cuerpo)}\");");
        sb.AppendLine("        await Task.CompletedTask;");
        sb.AppendLine("    }");
        sb.AppendLine();
        return sb.ToString();
    }

    private static string PlantillaArchivoVacio()
    {
        return
            "namespace AutomatizacionSOT;\r\n\r\n" +
            "using AutomatizacionSOT.Utils;\r\n" +
            "using Microsoft.Playwright;\r\n" +
            "using NUnit.Framework;\r\n" +
            "using TechTalk.SpecFlow;\r\n\r\n" +
            "/// <summary>\r\n" +
            "/// Steps auto-generados por RunnerIA al Guardar un .feature generado\r\n" +
            "/// cuyo paso aún no existía. Reemplazar Assert.Ignore por automatización UI real.\r\n" +
            "/// </summary>\r\n" +
            "[Binding]\r\n" +
            "public class PruebaAutoSteps\r\n" +
            "{\r\n" +
            "    " + MarkerBegin + "\r\n" +
            "    " + MarkerEnd + "\r\n" +
            "}\r\n";
    }

    private static string CapitalizarTipo(string t)
    {
        t = (t ?? "When").Trim();
        if (t.Equals("given", StringComparison.OrdinalIgnoreCase)) return "Given";
        if (t.Equals("then", StringComparison.OrdinalIgnoreCase)) return "Then";
        return "When";
    }

    private static string SanitizarNombreMetodo(string cuerpo)
    {
        var s = Regex.Replace(cuerpo ?? "", @"[^a-zA-Z0-9áéíóúÁÉÍÓÚñÑ]+", "_");
        s = Regex.Replace(s, @"_+", "_").Trim('_');
        if (s.Length > 40) s = s[..40];
        if (string.IsNullOrWhiteSpace(s)) s = "Paso";
        if (char.IsDigit(s[0])) s = "S_" + s;
        return s;
    }

    private static string HashCorto(string s)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(s ?? ""));
        return Convert.ToHexString(hash.AsSpan(0, 3));
    }

    private static string EscapeCs(string s) =>
        (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
}
