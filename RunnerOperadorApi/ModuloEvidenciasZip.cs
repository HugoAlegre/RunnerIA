using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

/// <summary>
/// Empaqueta entrega QA SOT tras «Ejecutar todos del módulo»:
/// {TICKET}-Evidencias_{yyyyMMdd_HHmmss}.zip
///   casos-de-prueba/{TICKET}-Casos-y-Evidencias.xlsx
/// Excel con hojas «Casos de prueba» + «Evidencias» (PNG embebidos) + «Queries» (si hay casos.json).
/// </summary>
public static class ModuloEvidenciasZip
{
    private static readonly Regex RxTicket = new(@"\bSC-?\d+\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RxTituloCaCn = new(
        @"\b(SC-?\d+)\s+(CA|CN)-?0?(\d+)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RxTagCc = new(
        @"@CierreCuadre(CA|CN)0?(\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RxTituloCc = new(
        @"\bCC\s+(CA|CN)-?0?(\d+)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RxTagCaCn = new(
        @"@(SC\d+)_(CA|CN)_(\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Excel con hojas Casos + Evidencias (PNG embebidos desde las corridas del módulo).</summary>
    public static ModuloExcelPack GenerarExcel(
        ModuloEvidenciasRequest body,
        string automatizacionRoot,
        string casosPruebaRoot,
        bool guardarCopiaLocal = true)
    {
        if (body?.Casos is null || body.Casos.Count == 0)
            throw new InvalidOperationException("Sin casos para armar el Excel QA.");

        CasosPruebaJsonLoader.Enriquecer(body, casosPruebaRoot);

        var ticket = ResolverTicket(body);
        var excelNombre = CasosPruebaExcel.NombreArchivo(ticket);

        var tempRoot = Path.Combine(Path.GetTempPath(), "RunnerModuloQA_" + Guid.NewGuid().ToString("N"));
        var pngLocal = Path.Combine(tempRoot, "png");
        Directory.CreateDirectory(pngLocal);

        var pasosEvidencia = RecolectarPasosEvidencia(body, automatizacionRoot, pngLocal);
        var queriesPorCaso = RecolectarQueriesPorCaso(body);
        var tituloDoc = CasosPruebaJsonLoader.ResolverTituloDocumento(casosPruebaRoot, body.Ticket, body.NombreModulo)
            ?? (body.Casos.Count == 1 ? body.Casos[0].Titulo : body.NombreModulo);
        var subtitulo = CasosPruebaJsonLoader.ResolverSubtituloDocumento(casosPruebaRoot, body.Ticket, body.NombreModulo)
            ?? ResolverSubtitulo(body);
        var queries = CasosPruebaJsonLoader.ResolverQueries(casosPruebaRoot, body.Ticket, body.NombreModulo);
        var queriesNota = CasosPruebaJsonLoader.ResolverQueriesNota(casosPruebaRoot, body.Ticket, body.NombreModulo);

        var excelBytes = CasosPruebaExcel.Generar(new CasosPruebaExcelOpciones
        {
            TicketOModulo = ticket,
            TituloDocumento = tituloDoc,
            Subtitulo = subtitulo,
            ContextoRelease = body.NombreModulo,
            Casos = body.Casos,
            PasosEvidencia = pasosEvidencia,
            QueriesPorCaso = queriesPorCaso,
            Queries = queries,
            QueriesNota = queriesNota
        });

        if (guardarCopiaLocal)
        {
            try
            {
                var outDir = ResolverCarpetaSalidaTicket(casosPruebaRoot, ticket, body.NombreModulo);
                Directory.CreateDirectory(outDir);
                File.WriteAllBytes(Path.Combine(outDir, excelNombre), excelBytes);
            }
            catch
            {
                /* no bloquea descarga */
            }
        }

        try
        {
            Directory.Delete(tempRoot, recursive: true);
        }
        catch
        {
            /* no bloquea descarga */
        }

        return new ModuloExcelPack
        {
            Bytes = excelBytes,
            ExcelNombre = excelNombre,
            PasosEvidencia = pasosEvidencia.Count
        };
    }

    /// <summary>Excel + ZIP desde casos.json completo (12 casos Xray + PNG en evidencias/).</summary>
    public static ModuloPaqueteCompleto PrepararPaqueteDesdeCasosJson(
        string ticket,
        string automatizacionRoot,
        string casosPruebaRoot)
    {
        var body = CasosPruebaJsonLoader.BuildModuloRequestDesdeJson(casosPruebaRoot, ticket)
            ?? throw new InvalidOperationException($"No se encontró casos.json con casos para {ticket}.");

        var pasosEvidencia = CasosPruebaJsonLoader.RecolectarPasosDesdeJson(casosPruebaRoot, ticket);
        var queriesPorCaso = CasosPruebaJsonLoader.RecolectarQueriesPorCasoDesdeJson(casosPruebaRoot, ticket);
        var tituloDoc = CasosPruebaJsonLoader.ResolverTituloDocumento(casosPruebaRoot, ticket, "Release 9")
            ?? body.Casos![0].Titulo ?? ticket;
        var subtitulo = CasosPruebaJsonLoader.ResolverSubtituloDocumento(casosPruebaRoot, ticket, "Release 9")
            ?? ResolverSubtitulo(body);
        var queries = CasosPruebaJsonLoader.ResolverQueries(casosPruebaRoot, ticket, "Release 9");
        var queriesNota = CasosPruebaJsonLoader.ResolverQueriesNota(casosPruebaRoot, ticket, "Release 9");
        var ticketNorm = ResolverTicket(body);
        var excelNombre = CasosPruebaExcel.NombreArchivo(ticketNorm);

        var excelBytes = CasosPruebaExcel.Generar(new CasosPruebaExcelOpciones
        {
            TicketOModulo = ticketNorm,
            TituloDocumento = tituloDoc,
            Subtitulo = subtitulo,
            ContextoRelease = "Release 9",
            Casos = body.Casos,
            PasosEvidencia = pasosEvidencia,
            QueriesPorCaso = queriesPorCaso,
            Queries = queries,
            QueriesNota = queriesNota
        });

        try
        {
            var outDir = ResolverCarpetaSalidaTicket(casosPruebaRoot, ticketNorm, body.NombreModulo);
            Directory.CreateDirectory(outDir);
            File.WriteAllBytes(Path.Combine(outDir, excelNombre), excelBytes);
        }
        catch
        {
            /* no bloquea descarga */
        }

        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        var zipNombre = $"{ticketNorm}-Evidencias_{stamp}.zip";
        var tempRoot = Path.Combine(Path.GetTempPath(), "RunnerModuloQA_" + Guid.NewGuid().ToString("N"));
        var packRoot = Path.Combine(tempRoot, "pack");
        var casosDir = Path.Combine(packRoot, "casos-de-prueba");
        Directory.CreateDirectory(casosDir);
        File.WriteAllBytes(Path.Combine(casosDir, excelNombre), excelBytes);

        var zipPath = Path.Combine(tempRoot, zipNombre);
        ZipFile.CreateFromDirectory(packRoot, zipPath, CompressionLevel.Fastest, includeBaseDirectory: false);
        var zipBytes = File.ReadAllBytes(zipPath);
        GuardarCopiaLocal(zipBytes, zipNombre, excelNombre, excelBytes, ticketNorm, casosPruebaRoot, body.NombreModulo);

        try
        {
            Directory.Delete(tempRoot, recursive: true);
        }
        catch
        {
            /* no bloquea descarga */
        }

        return new ModuloPaqueteCompleto
        {
            ZipBytes = zipBytes,
            ZipNombre = zipNombre,
            ExcelBytes = excelBytes,
            ExcelNombre = excelNombre,
            PasosEvidencia = pasosEvidencia.Count
        };
    }

    public static ModuloPaqueteCompleto PrepararPaquete(
        ModuloEvidenciasRequest body,
        string automatizacionRoot,
        string casosPruebaRoot)
    {
        if (body?.Casos is null || body.Casos.Count == 0)
            throw new InvalidOperationException("Sin casos para armar el paquete QA.");

        var excelPack = GenerarExcel(body, automatizacionRoot, casosPruebaRoot, guardarCopiaLocal: true);
        var ticket = ResolverTicket(body);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        var zipNombre = $"{ticket}-Evidencias_{stamp}.zip";

        var tempRoot = Path.Combine(Path.GetTempPath(), "RunnerModuloQA_" + Guid.NewGuid().ToString("N"));
        var packRoot = Path.Combine(tempRoot, "pack");
        var casosDir = Path.Combine(packRoot, "casos-de-prueba");
        Directory.CreateDirectory(casosDir);
        File.WriteAllBytes(Path.Combine(casosDir, excelPack.ExcelNombre), excelPack.Bytes);

        var zipPath = Path.Combine(tempRoot, zipNombre);
        ZipFile.CreateFromDirectory(packRoot, zipPath, CompressionLevel.Fastest, includeBaseDirectory: false);
        var zipBytes = File.ReadAllBytes(zipPath);
        GuardarCopiaLocal(zipBytes, zipNombre, excelPack.ExcelNombre, excelPack.Bytes, ticket, casosPruebaRoot, body.NombreModulo);

        try
        {
            Directory.Delete(tempRoot, recursive: true);
        }
        catch
        {
            /* no bloquea descarga */
        }

        return new ModuloPaqueteCompleto
        {
            ZipBytes = zipBytes,
            ZipNombre = zipNombre,
            ExcelBytes = excelPack.Bytes,
            ExcelNombre = excelPack.ExcelNombre,
            PasosEvidencia = excelPack.PasosEvidencia
        };
    }

    public static ModuloEvidenciasPack Empaquetar(
        ModuloEvidenciasRequest body,
        string automatizacionRoot,
        string casosPruebaRoot)
    {
        var pack = PrepararPaquete(body, automatizacionRoot, casosPruebaRoot);
        return new ModuloEvidenciasPack
        {
            Bytes = pack.ZipBytes,
            ZipNombre = pack.ZipNombre,
            ExcelNombre = pack.ExcelNombre,
            PasosEvidencia = pack.PasosEvidencia
        };
    }

    public static string NombreArchivoZip(ModuloEvidenciasRequest body)
    {
        var ticket = ResolverTicket(body);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        return $"{ticket}-Evidencias_{stamp}.zip";
    }

    private static string ResolverSubtitulo(ModuloEvidenciasRequest body)
    {
        var partes = new List<string>();
        var mod = (body.NombreModulo ?? "").Trim();
        if (mod.Contains("Release 9", StringComparison.OrdinalIgnoreCase)
            || mod.Contains("release-9", StringComparison.OrdinalIgnoreCase)
            || (body.Ticket ?? "").StartsWith("SC-", StringComparison.OrdinalIgnoreCase))
            partes.Add("Release 9");
        else if (!string.IsNullOrWhiteSpace(mod))
            partes.Add(mod);

        partes.Add("Ejecutado en Runner");
        partes.Add("Evidencias en hoja «Evidencias»");
        return string.Join(" | ", partes);
    }

    private static List<EvidenciaPasoExcel> RecolectarPasosEvidencia(
        ModuloEvidenciasRequest body,
        string automatizacionRoot,
        string carpetaLocalPng)
    {
        var pasos = new List<EvidenciaPasoExcel>();
        var carpetaBase = ResolverCarpetaBaseEvidencia(automatizacionRoot);

        foreach (var caso in body.Casos ?? [])
        {
            var seenSha = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var carpetaCaso = NormalizarCarpetaCaso(caso.Id, caso.Titulo, caso.Tag);
            var corrida = (body.Corridas ?? [])
                .FirstOrDefault(c =>
                    string.Equals(c.CasoId, caso.Id, StringComparison.OrdinalIgnoreCase)
                    || (!string.IsNullOrWhiteSpace(c.EscenarioId)
                        && string.Equals(c.EscenarioId, caso.Id, StringComparison.OrdinalIgnoreCase))
                    || (!string.IsNullOrWhiteSpace(c.Titulo)
                        && string.Equals(c.Titulo, caso.Titulo, StringComparison.OrdinalIgnoreCase)));

            var capturas = ResolverCarpetaCapturas(corrida, automatizacionRoot, carpetaBase, caso, carpetaCaso);
            if (capturas is null || !Directory.Exists(capturas))
                continue;

            var pngs = Directory.GetFiles(capturas, "*.png")
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var pasoN = 0;
            foreach (var png in pngs)
            {
                var sha = ComputeSha256(png);
                if (!seenSha.Add(sha))
                    continue;

                pasoN++;
                var nombre = Path.GetFileNameWithoutExtension(png);
                var tituloCorto = LimpiarTituloCaptura(nombre);
                pasos.Add(new EvidenciaPasoExcel
                {
                    CasoId = caso.Id,
                    Titulo = string.IsNullOrWhiteSpace(tituloCorto)
                        ? $"captura {pasoN}"
                        : tituloCorto,
                    Descripcion = $"Captura {pasoN}: {Path.GetFileName(png)}",
                    ImagenPath = HidratarPngLocal(png, carpetaLocalPng, pasos.Count + 1) ?? png
                });
            }
        }

        return pasos;
    }

    private static Dictionary<string, IReadOnlyList<CasosPruebaQueryExcel>> RecolectarQueriesPorCaso(
        ModuloEvidenciasRequest body)
    {
        var dict = new Dictionary<string, IReadOnlyList<CasosPruebaQueryExcel>>(StringComparer.OrdinalIgnoreCase);

        foreach (var caso in body.Casos ?? [])
        {
            var casoId = (caso.Id ?? "").Trim();
            if (string.IsNullOrEmpty(casoId))
                continue;

            var corrida = (body.Corridas ?? [])
                .FirstOrDefault(c =>
                    IdsCasoEquivalentesParaQueries(casoId, c.CasoId)
                    || (!string.IsNullOrWhiteSpace(c.Titulo)
                        && string.Equals(c.Titulo, caso.Titulo, StringComparison.OrdinalIgnoreCase)));

            var queries = LeerQueriesDesdeCarpetaCorrida(corrida?.EvidenciaCarpeta);
            if (queries.Count > 0)
                dict[casoId] = queries;
        }

        return dict;
    }

    private static bool IdsCasoEquivalentesParaQueries(string casoId, string? otroId)
    {
        if (string.IsNullOrWhiteSpace(otroId))
            return false;
        if (string.Equals(casoId, otroId, StringComparison.OrdinalIgnoreCase))
            return true;
        var a = casoId.Replace("-", "", StringComparison.Ordinal);
        var b = otroId.Replace("-", "", StringComparison.Ordinal);
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    private static List<CasosPruebaQueryExcel> LeerQueriesDesdeCarpetaCorrida(string? evidenciaCarpeta)
    {
        var lista = new List<CasosPruebaQueryExcel>();
        if (string.IsNullOrWhiteSpace(evidenciaCarpeta) || !Directory.Exists(evidenciaCarpeta))
            return lista;

        var consultasDir = Path.Combine(evidenciaCarpeta, "ConsultasDb");
        if (!Directory.Exists(consultasDir))
        {
            // Buscar en subcarpeta del escenario
            var sub = Directory.GetDirectories(evidenciaCarpeta)
                .Select(d => Path.Combine(d, "ConsultasDb"))
                .FirstOrDefault(Directory.Exists);
            if (sub is not null)
                consultasDir = sub;
            else
                return lista;
        }

        var jsonPath = Path.Combine(consultasDir, "consultas.json");
        if (File.Exists(jsonPath))
        {
            try
            {
                var json = File.ReadAllText(jsonPath, Encoding.UTF8);
                var registros = JsonSerializer.Deserialize<List<ConsultaDbRegistroDto>>(
                    json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (registros is { Count: > 0 })
                {
                    var n = 0;
                    foreach (var r in registros)
                    {
                        n++;
                        var resultado = (r.Muestra ?? "").Trim();
                        if (!string.IsNullOrEmpty(r.Error))
                            resultado = string.IsNullOrEmpty(resultado)
                                ? "Error: " + r.Error
                                : resultado + "\nError: " + r.Error;

                        lista.Add(new CasosPruebaQueryExcel
                        {
                            Orden = n,
                            Titulo = r.Ok ? $"{r.Motor} — consulta" : $"{r.Motor} — error",
                            Proposito = r.Escenario,
                            Motor = r.Motor,
                            Sql = r.Sql,
                            Uso = r.Parametros,
                            Resultado = resultado,
                            Muestra = r.Muestra
                        });
                    }
                }
            }
            catch
            {
                /* no bloquea Excel */
            }
        }

        var cobisTxt = Path.Combine(consultasDir, "cobis_re_cierre.txt");
        if (File.Exists(cobisTxt))
        {
            try
            {
                var texto = File.ReadAllText(cobisTxt, Encoding.UTF8).Trim();
                if (!string.IsNullOrEmpty(texto))
                {
                    lista.Add(new CasosPruebaQueryExcel
                    {
                        Orden = lista.Count + 1,
                        Titulo = "COBIS re_cierre (evidencia corrida)",
                        Motor = "COBIS",
                        Sql = texto,
                        Resultado = "Ver bloque SQL + comentarios de resultado en la corrida."
                    });
                }
            }
            catch
            {
                /* no bloquea Excel */
            }
        }

        return lista;
    }

    private sealed class ConsultaDbRegistroDto
    {
        public string? Escenario { get; set; }
        public string? Motor { get; set; }
        public string? Sql { get; set; }
        public string? Parametros { get; set; }
        public string? Muestra { get; set; }
        public string? Error { get; set; }
        public bool Ok { get; set; }
    }

    /// <summary>
    /// Copia el PNG a disco local antes de embeberlo en el Excel: en el Escritorio sobre OneDrive
    /// los archivos pueden ser placeholders y la celda termina como «No se pudo embeber».
    /// </summary>
    private static string? HidratarPngLocal(string origen, string carpetaLocal, int indice)
    {
        try
        {
            var destino = Path.Combine(carpetaLocal, $"{indice:D4}_{Path.GetFileName(origen)}");
            using var src = new FileStream(origen, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var dst = new FileStream(destino, FileMode.Create, FileAccess.Write, FileShare.None);
            src.CopyTo(dst);
            return destino;
        }
        catch
        {
            return null;
        }
    }

    private static string LimpiarTituloCaptura(string nombre)
    {
        var t = nombre.Trim();
        t = Regex.Replace(t, @"^\d+[_-]?", "");
        t = t.Replace('_', ' ').Replace('-', ' ');
        while (t.Contains("  ", StringComparison.Ordinal))
            t = t.Replace("  ", " ", StringComparison.Ordinal);
        if (t.Length > 80)
            t = t[..80].Trim();
        return t;
    }

    private static string ResolverTicket(ModuloEvidenciasRequest body)
    {
        if (!string.IsNullOrWhiteSpace(body.Ticket))
            return NormalizarTicket(body.Ticket!);

        foreach (var src in new[] { body.NombreModulo }
                     .Concat((body.Casos ?? []).Select(c => c.Id))
                     .Concat((body.Casos ?? []).Select(c => c.Titulo)))
        {
            var t = ExtraerTicketDeTexto(src);
            if (t is not null) return t;
        }

        return SanitizarNombre(body.NombreModulo ?? "Modulo");
    }

    private static string? ExtraerTicketDeTexto(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;
        var mCc = Regex.Match(texto, @"\bCC-?131\b", RegexOptions.IgnoreCase);
        if (mCc.Success) return "CC";
        if (Regex.IsMatch(texto, @"\bCC\b", RegexOptions.IgnoreCase)) return "CC";
        var m = Regex.Match(texto, @"\bSC-?(\d+)\b", RegexOptions.IgnoreCase);
        return m.Success ? $"SC-{m.Groups[1].Value}" : null;
    }

    private static string NormalizarTicket(string raw)
    {
        var t = ExtraerTicketDeTexto(raw);
        return t ?? SanitizarNombre(raw);
    }

    public static string NormalizarCarpetaCaso(string? id, string? titulo, string? tag)
    {
        var fromTagCc = RxTagCc.Match(tag ?? "");
        if (fromTagCc.Success)
            return $"CC-{fromTagCc.Groups[1].Value.ToUpperInvariant()}-{fromTagCc.Groups[2].Value.PadLeft(2, '0')}";

        var fromTituloCc = RxTituloCc.Match(titulo ?? "");
        if (fromTituloCc.Success)
            return $"CC-{fromTituloCc.Groups[1].Value.ToUpperInvariant()}-{fromTituloCc.Groups[2].Value.PadLeft(2, '0')}";

        if (!string.IsNullOrWhiteSpace(id))
        {
            var clean = id.Trim();
            if (Regex.IsMatch(clean, @"^CC-(CA|CN)-\d+$", RegexOptions.IgnoreCase))
                return clean.ToUpperInvariant();

            if (Regex.IsMatch(clean, @"^(30|31)$"))
                return $"CC-{clean}";

            var escCc = Regex.Match(clean, @"^31-(CA|CN)0?(\d+)$", RegexOptions.IgnoreCase);
            if (escCc.Success)
                return $"CC-{escCc.Groups[1].Value.ToUpperInvariant()}-{escCc.Groups[2].Value.PadLeft(2, '0')}";
        }

        var fromTag = RxTagCaCn.Match(tag ?? "");
        if (fromTag.Success)
            return $"{fromTag.Groups[1].Value.ToUpperInvariant()}-{fromTag.Groups[2].Value.ToUpperInvariant()}-{fromTag.Groups[3].Value.PadLeft(2, '0')}";

        var fromTitulo = RxTituloCaCn.Match(titulo ?? "");
        if (fromTitulo.Success)
        {
            var tk = fromTitulo.Groups[1].Value.Replace("-", "", StringComparison.Ordinal).ToUpperInvariant();
            return $"{tk}-{fromTitulo.Groups[2].Value.ToUpperInvariant()}-{fromTitulo.Groups[3].Value.PadLeft(2, '0')}";
        }

        if (!string.IsNullOrWhiteSpace(id))
        {
            var clean = id.Trim();
            if (RxTicket.IsMatch(clean) && clean.Contains("CA", StringComparison.OrdinalIgnoreCase))
            {
                var tk = RxTicket.Match(clean).Value.Replace("-", "", StringComparison.Ordinal).ToUpperInvariant();
                var ca = Regex.Match(clean, @"CA-?0?(\d+)", RegexOptions.IgnoreCase);
                if (ca.Success)
                    return $"{tk}-CA-{ca.Groups[1].Value.PadLeft(2, '0')}";
            }

            if (Regex.IsMatch(clean, @"^SC\d+-CA-\d+$", RegexOptions.IgnoreCase))
                return clean.ToUpperInvariant();

            var escCa = Regex.Match(clean, @"^(\d+)-CA0?(\d+)$", RegexOptions.IgnoreCase);
            if (escCa.Success)
            {
                var tkTitulo = RxTicket.Match(titulo ?? "");
                if (tkTitulo.Success)
                {
                    var tk = tkTitulo.Value.Replace("-", "", StringComparison.Ordinal).ToUpperInvariant();
                    return $"{tk}-CA-{escCa.Groups[2].Value.PadLeft(2, '0')}";
                }
            }
        }

        return SanitizarNombre(id ?? titulo ?? "Caso");
    }

    private static string? ResolverCarpetaCapturas(
        ModuloCorridaItem? corrida,
        string automatizacionRoot,
        string carpetaBase,
        CasoPruebaExcelFila caso,
        string carpetaCaso)
    {
        var evidenciaCarpeta = corrida?.EvidenciaCarpeta;

        if (string.IsNullOrWhiteSpace(evidenciaCarpeta) || !Directory.Exists(evidenciaCarpeta))
            return null;

        var capturasDirectas = Path.Combine(evidenciaCarpeta, "Capturas");
        if (Directory.Exists(capturasDirectas))
            return capturasDirectas;

        var patterns = PatronesBusquedaEscenario(carpetaCaso, caso.Titulo, corrida?.Titulo, corrida?.EscenarioId);
        var subdirs = Directory.GetDirectories(evidenciaCarpeta)
            .Where(d => !string.Equals(Path.GetFileName(d), "Informes", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var pat in patterns)
        {
            var hit = subdirs.FirstOrDefault(d => Like(Path.GetFileName(d), pat));
            if (hit is not null)
            {
                var cap = Path.Combine(hit, "Capturas");
                if (Directory.Exists(cap)) return cap;
            }
        }

        var conCapturas = subdirs
            .Select(d => new { Dir = d, Cap = Path.Combine(d, "Capturas") })
            .Where(x => Directory.Exists(x.Cap))
            .ToList();
        if (conCapturas.Count == 1)
            return conCapturas[0].Cap;

        if (Directory.GetFiles(evidenciaCarpeta, "*.png").Length > 0)
            return evidenciaCarpeta;

        return conCapturas.FirstOrDefault()?.Cap;
    }

    private static IEnumerable<string> PatronesBusquedaEscenario(
        string carpetaCaso,
        string? tituloCaso,
        string? tituloCorrida,
        string? escenarioId)
    {
        if (!string.IsNullOrWhiteSpace(escenarioId))
        {
            var esc = escenarioId.Trim();
            if (Regex.IsMatch(esc, @"^\d+$"))
            {
                yield return $"{esc}_*";
                yield return $"*{esc}_*";
            }

            yield return $"*{esc}*";
        }

        yield return $"*{carpetaCaso}*";
        yield return $"*{carpetaCaso.Replace("-", "_", StringComparison.Ordinal)}*";

        var ca = Regex.Match(carpetaCaso, @"CA-(\d+)$", RegexOptions.IgnoreCase);
        if (ca.Success)
        {
            yield return $"*CA-{ca.Groups[1].Value}*";
            yield return $"*CA_{ca.Groups[1].Value.PadLeft(2, '0')}*";
            yield return $"*CA-0{ca.Groups[1].Value}*";
        }

        foreach (var t in new[] { tituloCaso, tituloCorrida }.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            var sanitized = SanitizarNombre(t!);
            if (sanitized.Length >= 8)
                yield return $"*{sanitized[..Math.Min(40, sanitized.Length)]}*";
        }
    }

    private static string ComputeSha256(string path)
    {
        using var sha = SHA256.Create();
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return Convert.ToHexString(sha.ComputeHash(fs));
    }

    private static void GuardarCopiaLocal(
        byte[] zipBytes,
        string zipNombre,
        string excelNombre,
        byte[] excelBytes,
        string ticket,
        string casosPruebaRoot,
        string? nombreModulo)
    {
        try
        {
            var outDir = ResolverCarpetaSalidaTicket(casosPruebaRoot, ticket, nombreModulo);
            Directory.CreateDirectory(outDir);
            File.WriteAllBytes(Path.Combine(outDir, zipNombre), zipBytes);
            File.WriteAllBytes(Path.Combine(outDir, excelNombre), excelBytes);
        }
        catch
        {
            /* no bloquea descarga */
        }
    }

    /// <summary>
    /// Preferir <c>casos-prueba/Release-9/SC-XXX</c> si el módulo es Release 9 o ya existe esa carpeta.
    /// </summary>
    private static string ResolverCarpetaSalidaTicket(string casosPruebaRoot, string ticket, string? nombreModulo)
    {
        if (string.Equals(ticket, "CC", StringComparison.OrdinalIgnoreCase)
            || ticket.StartsWith("CC-", StringComparison.OrdinalIgnoreCase)
            || ((nombreModulo ?? "").Contains("cuadre", StringComparison.OrdinalIgnoreCase)
                && (nombreModulo ?? "").Contains("cierre", StringComparison.OrdinalIgnoreCase)))
        {
            return Path.Combine(casosPruebaRoot, "CierreCuadre");
        }

        var r9 = Path.Combine(casosPruebaRoot, "Release-9", ticket);
        var plano = Path.Combine(casosPruebaRoot, ticket);
        var esR9 = (nombreModulo ?? "").Contains("Release 9", StringComparison.OrdinalIgnoreCase)
                   || (nombreModulo ?? "").Contains("release-9", StringComparison.OrdinalIgnoreCase)
                   || Directory.Exists(r9);

        return esR9 ? r9 : plano;
    }

    private static string ResolverCarpetaBaseEvidencia(string automatizacionRoot)
    {
        try
        {
            var appsettings = Path.Combine(automatizacionRoot, "appsettings.json");
            if (File.Exists(appsettings))
            {
                using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(appsettings));
                if (doc.RootElement.TryGetProperty("Evidencia", out var ev)
                    && ev.TryGetProperty("CarpetaBase", out var cb))
                {
                    var v = cb.GetString()?.Trim();
                    if (!string.IsNullOrEmpty(v))
                        return Path.GetFullPath(v);
                }
            }
        }
        catch { /* ignore */ }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "Evidencia y reportes");
    }

    private static bool Like(string input, string pattern)
    {
        var rx = "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$";
        return Regex.IsMatch(input, rx, RegexOptions.IgnoreCase);
    }

    private static string SanitizarNombre(string? name)
    {
        var n = string.IsNullOrWhiteSpace(name) ? "Modulo" : name.Trim();
        foreach (var c in Path.GetInvalidFileNameChars().Concat([':', '\\', '/', '?', '*', '[', ']']))
            n = n.Replace(c, '-');
        n = n.Replace(' ', '-');
        while (n.Contains("--", StringComparison.Ordinal))
            n = n.Replace("--", "-", StringComparison.Ordinal);
        if (n.Length > 40) n = n[..40].TrimEnd('-');
        return string.IsNullOrWhiteSpace(n) ? "Modulo" : n;
    }
}

public sealed class ModuloExcelPack
{
    public byte[] Bytes { get; init; } = [];
    public string ExcelNombre { get; init; } = "";
    public int PasosEvidencia { get; init; }
}

public sealed class ModuloPaqueteCompleto
{
    public byte[] ZipBytes { get; init; } = [];
    public string ZipNombre { get; init; } = "";
    public byte[] ExcelBytes { get; init; } = [];
    public string ExcelNombre { get; init; } = "";
    public int PasosEvidencia { get; init; }
}

public sealed class ModuloEvidenciasPack
{
    public byte[]? Bytes { get; init; }
    public string ZipNombre { get; init; } = "";
    public string ExcelNombre { get; init; } = "";
    public int PasosEvidencia { get; init; }
}

public sealed class ModuloEvidenciasRequest
{
    public string? NombreModulo { get; set; }
    public string? Ticket { get; set; }
    public List<CasoPruebaExcelFila>? Casos { get; set; }
    public List<ModuloCorridaItem>? Corridas { get; set; }
}

public sealed class ModuloCorridaItem
{
    public string? CasoId { get; set; }
    public string? Titulo { get; set; }
    public string? Tag { get; set; }
    public string? EscenarioId { get; set; }
    public string? RunId { get; set; }
    public string? EvidenciaCarpeta { get; set; }
}
