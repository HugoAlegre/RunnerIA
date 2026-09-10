using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Carga casos-prueba/Release-9/{ticket}/casos.json (formato SC-514 / QA manual)
/// y enriquece las filas del Excel del Runner con pre/pasos/esperado del diseño.
/// </summary>
public static class CasosPruebaJsonLoader
{
    public static CasosPruebaJsonDocumento? TryLoad(string casosPruebaRoot, string? ticket, string? nombreModulo)
    {
        var path = ResolverRuta(casosPruebaRoot, ticket, nombreModulo);
        if (path is null || !File.Exists(path))
            return null;

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<CasosPruebaJsonDocumento>(json, JsonOpts);
        }
        catch
        {
            return null;
        }
    }

    public static void Enriquecer(ModuloEvidenciasRequest body, string casosPruebaRoot)
    {
        var doc = TryLoad(casosPruebaRoot, body.Ticket, body.NombreModulo);
        if (doc?.Casos is null || doc.Casos.Count == 0)
            return;

        foreach (var fila in body.Casos ?? [])
        {
            var src = doc.Casos.FirstOrDefault(c => IdsEquivalentes(c.Id, fila.Id, fila.Titulo, fila.Tag));
            if (src is null)
                continue;

            if (!string.IsNullOrWhiteSpace(src.Titulo))
                fila.Titulo = src.Titulo.Trim();

            var pre = (src.Pre ?? src.Precondiciones ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(pre) && string.IsNullOrWhiteSpace(fila.Precondiciones))
                fila.Precondiciones = pre;

            var pasos = (src.Pasos ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(pasos) && string.IsNullOrWhiteSpace(fila.Pasos))
                fila.Pasos = pasos;

            var esp = (src.Esperado ?? src.ResultadoEsperado ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(esp) && string.IsNullOrWhiteSpace(fila.ResultadoEsperado))
                fila.ResultadoEsperado = esp;
        }
    }

    public static string? ResolverSubtituloDocumento(string casosPruebaRoot, string? ticket, string? nombreModulo)
    {
        var doc = TryLoad(casosPruebaRoot, ticket, nombreModulo);
        return string.IsNullOrWhiteSpace(doc?.Subtitulo) ? null : doc!.Subtitulo!.Trim();
    }

    public static string? ResolverTituloDocumento(string casosPruebaRoot, string? ticket, string? nombreModulo)
    {
        var doc = TryLoad(casosPruebaRoot, ticket, nombreModulo);
        return string.IsNullOrWhiteSpace(doc?.Titulo) ? null : doc!.Titulo!.Trim();
    }

    public static IReadOnlyList<CasosPruebaQueryExcel> ResolverQueries(
        string casosPruebaRoot,
        string? ticket,
        string? nombreModulo)
    {
        var doc = TryLoad(casosPruebaRoot, ticket, nombreModulo);
        return doc?.Queries ?? [];
    }

    public static string? ResolverQueriesNota(string casosPruebaRoot, string? ticket, string? nombreModulo)
    {
        var doc = TryLoad(casosPruebaRoot, ticket, nombreModulo);
        return string.IsNullOrWhiteSpace(doc?.QueriesNota) ? null : doc!.QueriesNota!.Trim();
    }

    private static string? ResolverRuta(string casosPruebaRoot, string? ticket, string? nombreModulo)
    {
        var tk = CasosPruebaExcel.NormalizarTicketParaArchivo(ticket ?? nombreModulo ?? "");
        if (string.IsNullOrWhiteSpace(ticket)
            && !string.IsNullOrWhiteSpace(nombreModulo)
            && nombreModulo.Contains("cuadre", StringComparison.OrdinalIgnoreCase)
            && nombreModulo.Contains("cierre", StringComparison.OrdinalIgnoreCase))
        {
            tk = "CC";
        }

        var candidatos = new[]
        {
            Path.Combine(casosPruebaRoot, "CC", "casos.json"),
            Path.Combine(casosPruebaRoot, "CierreCuadre", "casos.json"),
            Path.Combine(casosPruebaRoot, "CC-131", "casos.json"),
            Path.Combine(casosPruebaRoot, "Release-9", tk, "casos.json"),
            Path.Combine(casosPruebaRoot, tk, "casos.json"),
            Path.Combine(casosPruebaRoot, "Release-9", tk.Replace("-", "", StringComparison.Ordinal), "casos.json")
        };
        return candidatos.FirstOrDefault(File.Exists);
    }

    private static bool IdsEquivalentes(string? jsonId, string? filaId, string? filaTitulo, string? filaTag)
    {
        if (string.IsNullOrWhiteSpace(jsonId) || string.IsNullOrWhiteSpace(filaId))
            return false;

        if (string.Equals(NormalizarId(jsonId), NormalizarId(filaId), StringComparison.OrdinalIgnoreCase))
            return true;

        if (!string.IsNullOrWhiteSpace(filaTitulo)
            && filaTitulo.Contains(jsonId, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!string.IsNullOrWhiteSpace(filaTag)
            && filaTag.Contains(jsonId.Replace("-", "", StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private static string NormalizarId(string raw)
        => raw.Trim().Replace(" ", "", StringComparison.Ordinal).Replace("_", "-", StringComparison.Ordinal);

    public static string? ResolverDirectorioTicket(string casosPruebaRoot, string? ticket)
    {
        var tk = CasosPruebaExcel.NormalizarTicketParaArchivo(ticket ?? "");
        if (string.IsNullOrWhiteSpace(tk) || tk == "Modulo")
            return null;

        var r9 = Path.Combine(casosPruebaRoot, "Release-9", tk);
        if (Directory.Exists(r9))
            return r9;

        var plano = Path.Combine(casosPruebaRoot, tk);
        return Directory.Exists(plano) ? plano : null;
    }

    public static ModuloEvidenciasRequest? BuildModuloRequestDesdeJson(string casosPruebaRoot, string ticket)
    {
        var doc = TryLoad(casosPruebaRoot, ticket, "Release 9");
        if (doc?.Casos is null || doc.Casos.Count == 0)
            return null;

        var casos = doc.Casos.Select(c => new CasoPruebaExcelFila
        {
            Id = c.Id,
            Titulo = c.Titulo,
            Precondiciones = (c.Pre ?? c.Precondiciones ?? "").Trim(),
            Pasos = (c.Pasos ?? "").Trim(),
            ResultadoEsperado = (c.Esperado ?? c.ResultadoEsperado ?? "").Trim(),
            ResultadoObtenido = (c.Obtenido ?? c.ResultadoObtenido ?? "").Trim(),
            Estado = c.Estado,
            Observaciones = (c.Obs ?? c.Observaciones ?? "").Trim()
        }).ToList();

        return new ModuloEvidenciasRequest
        {
            Ticket = doc.Ticket ?? ticket,
            NombreModulo = "Release 9",
            Casos = casos,
            Corridas = []
        };
    }

    public static List<EvidenciaPasoExcel> RecolectarPasosDesdeJson(string casosPruebaRoot, string ticket)
    {
        var doc = TryLoad(casosPruebaRoot, ticket, "Release 9");
        var dir = ResolverDirectorioTicket(casosPruebaRoot, ticket);
        if (doc?.Casos is null || dir is null)
            return [];

        var pasos = new List<EvidenciaPasoExcel>();
        foreach (var caso in doc.Casos)
        {
            var casoId = (caso.Id ?? "").Trim();
            if (string.IsNullOrEmpty(casoId))
                continue;

            var evidDirRel = (caso.EvidenciasDir ?? "").Trim();
            var evidDir = string.IsNullOrWhiteSpace(evidDirRel)
                ? Path.Combine(dir, "evidencias", casoId)
                : Path.IsPathRooted(evidDirRel)
                    ? evidDirRel
                    : Path.Combine(dir, evidDirRel.Replace('/', Path.DirectorySeparatorChar));

            if (caso.Evid is { Count: > 0 })
            {
                foreach (var ev in caso.Evid)
                {
                    var archivo = (ev.Archivo ?? "").Trim();
                    if (string.IsNullOrEmpty(archivo))
                        continue;

                    var imgPath = Path.Combine(evidDir, archivo);
                    if (!File.Exists(imgPath))
                        continue;

                    pasos.Add(new EvidenciaPasoExcel
                    {
                        CasoId = casoId,
                        Titulo = ev.Titulo,
                        Descripcion = ev.Descripcion,
                        ImagenPath = imgPath
                    });
                }
            }
            else if (Directory.Exists(evidDir))
            {
                foreach (var png in Directory.GetFiles(evidDir, "*.png").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                {
                    pasos.Add(new EvidenciaPasoExcel
                    {
                        CasoId = casoId,
                        Titulo = Path.GetFileNameWithoutExtension(png),
                        Descripcion = Path.GetFileName(png),
                        ImagenPath = png
                    });
                }
            }
        }

        return pasos;
    }

    public static Dictionary<string, IReadOnlyList<CasosPruebaQueryExcel>> RecolectarQueriesPorCasoDesdeJson(
        string casosPruebaRoot,
        string ticket)
    {
        var doc = TryLoad(casosPruebaRoot, ticket, "Release 9");
        var dict = new Dictionary<string, IReadOnlyList<CasosPruebaQueryExcel>>(StringComparer.OrdinalIgnoreCase);
        if (doc?.Casos is null)
            return dict;

        foreach (var caso in doc.Casos)
        {
            var casoId = (caso.Id ?? "").Trim();
            if (string.IsNullOrEmpty(casoId) || caso.Queries is not { Count: > 0 })
                continue;

            var lista = new List<CasosPruebaQueryExcel>();
            var n = 0;
            foreach (var q in caso.Queries)
            {
                n++;
                lista.Add(new CasosPruebaQueryExcel
                {
                    Orden = n,
                    Titulo = q.Titulo,
                    Motor = q.Motor,
                    Sql = q.Sql,
                    Resultado = q.Resultado,
                    Muestra = q.Resultado
                });
            }

            dict[casoId] = lista;
        }

        return dict;
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };
}

public sealed class CasosPruebaJsonDocumento
{
    public string? Ticket { get; set; }
    public string? Titulo { get; set; }
    public string? Subtitulo { get; set; }
    [JsonPropertyName("queries_nota")]
    public string? QueriesNota { get; set; }
    public List<CasosPruebaQueryExcel>? Queries { get; set; }
    public List<CasosPruebaJsonCaso>? Casos { get; set; }
}

public sealed class CasosPruebaJsonCaso
{
    public string? Id { get; set; }
    public string? Titulo { get; set; }
    [JsonPropertyName("pre")]
    public string? Pre { get; set; }
    public string? Precondiciones { get; set; }
    public string? Pasos { get; set; }
    [JsonPropertyName("esperado")]
    public string? Esperado { get; set; }
    public string? ResultadoEsperado { get; set; }
    [JsonPropertyName("obtenido")]
    public string? Obtenido { get; set; }
    public string? ResultadoObtenido { get; set; }
    public string? Estado { get; set; }
    [JsonPropertyName("obs")]
    public string? Obs { get; set; }
    public string? Observaciones { get; set; }
    [JsonPropertyName("evidencias_dir")]
    public string? EvidenciasDir { get; set; }
    [JsonPropertyName("evid")]
    public List<CasosPruebaJsonEvidencia>? Evid { get; set; }
    public List<CasosPruebaJsonQuery>? Queries { get; set; }
}

public sealed class CasosPruebaJsonEvidencia
{
    public string? Titulo { get; set; }
    public string? Descripcion { get; set; }
    public string? Archivo { get; set; }
}

public sealed class CasosPruebaJsonQuery
{
    public string? Motor { get; set; }
    public string? Titulo { get; set; }
    public string? Sql { get; set; }
    public string? Resultado { get; set; }
}
