using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

/// <summary>
/// Busca XPath, textos de UI y selectores en repos del catálogo + AutomatizacionSOT.
/// </summary>
public static class UiMapeoCodigoBusqueda
{
    private static readonly string[] Extensiones =
        [".cs", ".ts", ".html", ".scss", ".feature", ".json", ".md"];

    private static readonly HashSet<string> ExcluirDir = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", "bin", "obj", ".git", "dist", "wwwroot", "coverage",
        "TestResults", ".vs", ".idea", "packages", "out", "build", ".playwright-mcp"
    };

    public static IReadOnlyList<CoincidenciaCodigoMapeo> Buscar(
        string automatizacionRoot,
        MapeoEventoUi evento,
        int maxResultados = 12,
        string? catalogoReposPath = null)
    {
        var terminos = ExtraerTerminosBusqueda(evento);
        if (terminos.Count == 0)
            return [];

        var raices = ResolverRaicesBusqueda(automatizacionRoot, catalogoReposPath);
        var resultados = new List<CoincidenciaCodigoMapeo>();
        var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raiz in raices)
        {
            if (!Directory.Exists(raiz.Path))
                continue;

            BuscarEnArbol(raiz.Path, raiz.RepoId, terminos, resultados, vistos, maxResultados);
            if (resultados.Count >= maxResultados)
                break;
        }

        return resultados
            .OrderByDescending(r => r.Puntaje)
            .Take(maxResultados)
            .ToList();
    }

    private static List<(string RepoId, string Path)> ResolverRaicesBusqueda(
        string automatizacionRoot,
        string? catalogoReposPath = null)
    {
        var lista = new List<(string RepoId, string Path)>
        {
            ("automatizacion", automatizacionRoot)
        };

        try
        {
            var cfgPath = string.IsNullOrWhiteSpace(catalogoReposPath)
                ? RunnerIaProyectos.RutaConfigCatalogoActivo(automatizacionRoot)
                : catalogoReposPath.Trim();
            if (!File.Exists(cfgPath))
                return lista;

            using var doc = JsonDocument.Parse(File.ReadAllText(cfgPath));
            if (!doc.RootElement.TryGetProperty("repos", out var repos))
                return lista;

            foreach (var repo in repos.EnumerateArray())
            {
                var id = repo.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                var ruta = repo.TryGetProperty("ruta", out var rutaEl) ? rutaEl.GetString() : null;
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(ruta))
                    continue;
                if (Directory.Exists(ruta))
                    lista.Add((id.Trim(), Path.GetFullPath(ruta.Trim())));
            }
        }
        catch
        {
            /* best-effort */
        }

        return lista;
    }

    private static List<string> ExtraerTerminosBusqueda(MapeoEventoUi ev)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? t)
        {
            t = (t ?? "").Trim();
            if (t.Length < 2 || t.Length > 120)
                return;
            set.Add(t);
        }

        Add(ev.Texto);
        Add(ev.Id);
        Add(ev.XpathUtil);
        Add(ev.Xpath);

        if (!string.IsNullOrWhiteSpace(ev.XpathUtil))
        {
            foreach (Match m in Regex.Matches(ev.XpathUtil, @"@id=['""]([^'""]+)['""]", RegexOptions.IgnoreCase))
                Add(m.Groups[1].Value);
            foreach (Match m in Regex.Matches(ev.XpathUtil, @"@formcontrolname=['""]([^'""]+)['""]", RegexOptions.IgnoreCase))
                Add(m.Groups[1].Value);
            foreach (Match m in Regex.Matches(ev.XpathUtil, @"normalize-space\(\)=['""]([^'""]+)['""]", RegexOptions.IgnoreCase))
                Add(m.Groups[1].Value);
            foreach (Match m in Regex.Matches(ev.XpathUtil, @"contains\(@href,'([^']+)'\)", RegexOptions.IgnoreCase))
                Add(m.Groups[1].Value);
        }

        if (!string.IsNullOrWhiteSpace(ev.Texto) && ev.Texto.Length <= 40)
            Add($"has-text('{ev.Texto}')");

        return set.Take(8).ToList();
    }

    private static void BuscarEnArbol(
        string root,
        string repoId,
        IReadOnlyList<string> terminos,
        List<CoincidenciaCodigoMapeo> resultados,
        HashSet<string> vistos,
        int maxResultados)
    {
        var stack = new Stack<string>();
        stack.Push(root);
        var archivos = 0;

        while (stack.Count > 0 && resultados.Count < maxResultados && archivos < 8000)
        {
            var dir = stack.Pop();
            IEnumerable<string> subdirs;
            IEnumerable<string> files;
            try
            {
                subdirs = Directory.EnumerateDirectories(dir);
                files = Directory.EnumerateFiles(dir);
            }
            catch
            {
                continue;
            }

            foreach (var sd in subdirs)
            {
                var name = Path.GetFileName(sd);
                if (ExcluirDir.Contains(name))
                    continue;
                stack.Push(sd);
            }

            foreach (var file in files)
            {
                archivos++;
                if (archivos > 8000 || resultados.Count >= maxResultados)
                    break;

                var ext = Path.GetExtension(file);
                if (!Extensiones.Contains(ext))
                    continue;

                var rel = Path.GetRelativePath(root, file).Replace('\\', '/');
                if (rel.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
                    || rel.EndsWith(".feature.cs", StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    var fi = new FileInfo(file);
                    if (fi.Length > 900_000)
                        continue;

                    var lineas = File.ReadAllLines(file, Encoding.UTF8);
                    for (var i = 0; i < lineas.Length; i++)
                    {
                        var linea = lineas[i];
                        if (string.IsNullOrWhiteSpace(linea))
                            continue;

                        var puntaje = 0;
                        string? terminoHit = null;
                        foreach (var term in terminos)
                        {
                            if (!linea.Contains(term, StringComparison.OrdinalIgnoreCase))
                                continue;
                            puntaje += term.Length >= 12 ? 4 : 2;
                            terminoHit = term;
                        }

                        if (puntaje == 0 || terminoHit is null)
                            continue;

                        var key = $"{repoId}|{rel}|{i + 1}";
                        if (!vistos.Add(key))
                            continue;

                        resultados.Add(new CoincidenciaCodigoMapeo
                        {
                            RepoId = repoId,
                            Archivo = rel,
                            Linea = i + 1,
                            Fragmento = Recortar(linea.Trim(), 220),
                            Termino = terminoHit,
                            Puntaje = puntaje
                        });

                        if (resultados.Count >= maxResultados)
                            break;
                    }
                }
                catch
                {
                    /* skip unreadable */
                }
            }
        }
    }

    private static string Recortar(string s, int max)
        => s.Length <= max ? s : s[..max] + "…";
}

public sealed class CoincidenciaCodigoMapeo
{
    public string? RepoId { get; set; }
    public string? Archivo { get; set; }
    public int Linea { get; set; }
    public string? Fragmento { get; set; }
    public string? Termino { get; set; }
    public int Puntaje { get; set; }
}
