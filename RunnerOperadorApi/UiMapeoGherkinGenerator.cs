using System.Text;
using System.Text.RegularExpressions;

/// <summary>Convierte eventos del grabador Mapeo UI en borrador Gherkin para Generar.</summary>
public static class UiMapeoGherkinGenerator
{
    public const string FormatoVersion = "mapeo-ui-v1";

    private static readonly Regex RxBindingStep = new(
        @"\[(?:Given|When|Then|And|But)\(@""([^""]+)""\)\]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex RxFeatureStep = new(
        @"^\s*(Dado|Cuando|Entonces|Y|Pero|Given|When|Then|And|But)\s+(.+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string Generar(
        IReadOnlyList<MapeoEventoUi> eventos,
        string? urlInicio = null,
        string? sesionId = null,
        string? plantilla = null)
    {
        var esSot = string.Equals(plantilla, "sot", StringComparison.OrdinalIgnoreCase);
        var sb = new StringBuilder();
        sb.AppendLine("# language: es");
        sb.AppendLine();
        var titulo = InferirTituloFeature(urlInicio);
        sb.AppendLine($"Característica: {titulo}");
        sb.AppendLine("  Borrador desde Mapeo UI (clics grabados).");
        if (!string.IsNullOrWhiteSpace(sesionId))
            sb.AppendLine($"  Sesión: {sesionId.Trim()}.");
        if (!string.IsNullOrWhiteSpace(urlInicio))
            sb.AppendLine($"  URL: {urlInicio.Trim()}.");
        sb.AppendLine("  Revisá bindings antes de Guardar en Generar.");
        sb.AppendLine();
        sb.AppendLine("  Antecedentes:");
        if (esSot)
        {
            sb.AppendLine("    Dado el usuario abre la aplicacion SOT");
            sb.AppendLine("    Cuando inicia sesion con las credenciales configuradas");
            sb.AppendLine("    Y elige la sucursal por defecto y confirma");
        }
        else
        {
            sb.AppendLine("    Dado que el usuario abrio la aplicacion en la URL configurada");
            sb.AppendLine("    # Completar login manual en el grabador si la app lo requiere");
        }
        sb.AppendLine();
        sb.AppendLine($"  Escenario: {titulo} — pasos grabados");

        if (eventos.Count == 0)
        {
            sb.AppendLine("    # Sin clics grabados — volvé a Mapeo UI e iniciá la grabación.");
            return sb.ToString().TrimEnd() + Environment.NewLine;
        }

        var primerPaso = true;
        foreach (var ev in eventos.OrderBy(e => e.Orden))
        {
            var paso = InferirPasoGherkin(ev);
            var prefijo = primerPaso ? "Cuando" : "Y";
            primerPaso = false;

            if (paso.StartsWith("Dado ", StringComparison.OrdinalIgnoreCase)
                || paso.StartsWith("Cuando ", StringComparison.OrdinalIgnoreCase)
                || paso.StartsWith("Entonces ", StringComparison.OrdinalIgnoreCase)
                || paso.StartsWith("Given ", StringComparison.OrdinalIgnoreCase)
                || paso.StartsWith("When ", StringComparison.OrdinalIgnoreCase)
                || paso.StartsWith("Then ", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine($"    {paso}");
            }
            else if (paso.StartsWith("Y ", StringComparison.OrdinalIgnoreCase)
                     || paso.StartsWith("And ", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine($"    {paso}");
            }
            else if (paso.StartsWith("#", StringComparison.Ordinal))
            {
                sb.AppendLine($"    {paso}");
            }
            else
            {
                sb.AppendLine($"    {prefijo} {paso}");
            }
        }

        return sb.ToString().TrimEnd() + Environment.NewLine;
    }

    private static string InferirTituloFeature(string? urlInicio)
    {
        if (string.IsNullOrWhiteSpace(urlInicio))
            return "Flujo grabado Mapeo UI";

        try
        {
            var uri = new Uri(urlInicio.Trim());
            var path = uri.AbsolutePath.Trim('/');
            if (string.IsNullOrWhiteSpace(path))
                return "Flujo grabado Mapeo UI";
            var partes = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var ultima = partes[^1].Replace('-', ' ');
            return $"Mapeo {ultima}";
        }
        catch
        {
            return "Flujo grabado Mapeo UI";
        }
    }

    private static string InferirPasoGherkin(MapeoEventoUi ev)
    {
        foreach (var c in ev.CoincidenciasCodigo ?? [])
        {
            if (TryExtraerPasoDesdeCoincidencia(c, out var paso))
                return paso;
        }

        if (!string.IsNullOrWhiteSpace(ev.Texto))
            return $"hace clic en «{Escapar(ev.Texto.Trim())}»";

        if (!string.IsNullOrWhiteSpace(ev.Href))
            return $"navega a «{Escapar(ev.Href.Trim())}»";

        var xp = ev.XpathPlaywright ?? ev.XpathUtil ?? ev.Xpath;
        if (!string.IsNullOrWhiteSpace(xp))
            return $"# TODO binding — selector {Escapar(xp.Trim())}";

        return "# TODO binding — clic sin texto ni selector util";
    }

    private static bool TryExtraerPasoDesdeCoincidencia(CoincidenciaCodigoMapeo c, out string paso)
    {
        paso = "";
        var frag = (c.Fragmento ?? "").Trim();
        if (string.IsNullOrWhiteSpace(frag))
            return false;

        if (c.Archivo?.EndsWith(".feature", StringComparison.OrdinalIgnoreCase) == true)
        {
            foreach (var linea in frag.Split('\n'))
            {
                var t = linea.Trim();
                if (t.StartsWith('#'))
                    continue;
                var m = RxFeatureStep.Match(t);
                if (!m.Success)
                    continue;
                paso = $"{CapitalizarPaso(m.Groups[1].Value)} {m.Groups[2].Value.Trim()}";
                return true;
            }
        }

        if (c.Archivo?.EndsWith("Steps.cs", StringComparison.OrdinalIgnoreCase) == true
            || frag.Contains("[When(", StringComparison.Ordinal)
            || frag.Contains("[Given(", StringComparison.Ordinal))
        {
            var m = RxBindingStep.Match(frag);
            if (m.Success)
            {
                paso = m.Groups[1].Value.Trim();
                return true;
            }
        }

        return false;
    }

    private static string CapitalizarPaso(string keyword)
    {
        if (keyword.Equals("given", StringComparison.OrdinalIgnoreCase)) return "Dado";
        if (keyword.Equals("when", StringComparison.OrdinalIgnoreCase)) return "Cuando";
        if (keyword.Equals("then", StringComparison.OrdinalIgnoreCase)) return "Entonces";
        if (keyword.Equals("and", StringComparison.OrdinalIgnoreCase)) return "Y";
        if (keyword.Equals("but", StringComparison.OrdinalIgnoreCase)) return "Pero";
        return keyword.Length > 0 ? char.ToUpper(keyword[0]) + keyword[1..] : keyword;
    }

    private static string Escapar(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
