using System.Globalization;
using System.Text.RegularExpressions;
using ClosedXML.Excel;

/// <summary>
/// Excel QA SOT — formato <c>{ticket}-Casos-y-Evidencias.xlsx</c> (referencia SC-414):
/// <list type="bullet">
/// <item>Hoja «Casos de prueba»: título + subtítulo + columnas ID…Observaciones</item>
/// <item>Hoja «Evidencias»: capturas embebidas con título y descripción por paso</item>
/// <item>Hoja «Queries»: SQL de consulta (prep / verify / restore) cuando hay casos.json con queries</item>
/// </list>
/// </summary>
public static class CasosPruebaExcel
{
    private const string HojaCasos = "Casos de prueba";
    private const string HojaEvidencias = "Evidencias";
    private const string HojaQueries = "Queries";

    private static readonly string[] QueryHeaders =
    [
        "#",
        "Propósito",
        "SQL",
        "Uso / notas"
    ];

    private static readonly string[] Headers =
    [
        "ID",
        "Título",
        "Precondiciones",
        "Pasos",
        "Resultado esperado",
        "Resultado obtenido",
        "Estado",
        "Observaciones"
    ];

    public static byte[] Generar(string ticketOModulo, IReadOnlyList<CasoPruebaExcelFila> casos)
        => Generar(new CasosPruebaExcelOpciones
        {
            TicketOModulo = ticketOModulo,
            Casos = casos
        });

    public static byte[] Generar(CasosPruebaExcelOpciones opts)
    {
        ArgumentNullException.ThrowIfNull(opts);
        var casos = opts.Casos ?? [];
        var ticket = NormalizarTicketParaArchivo(opts.TicketOModulo);
        var tituloPrincipal = ResolverTituloPrincipal(ticket, opts.TituloDocumento, casos);
        var subtitulo = string.IsNullOrWhiteSpace(opts.Subtitulo)
            ? $"QA SOT | Evidencias en hoja «{HojaEvidencias}»"
            : opts.Subtitulo!.Trim();

        using var wb = new XLWorkbook();
        EscribirHojaCasos(wb, tituloPrincipal, subtitulo, casos);
        EscribirHojaEvidencias(wb, ticket, casos, opts.PasosEvidencia, opts.QueriesPorCaso);
        if (opts.Queries is { Count: > 0 })
            EscribirHojaQueries(wb, ticket, opts.Queries, opts.QueriesNota);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static void EscribirHojaCasos(
        XLWorkbook wb,
        string tituloPrincipal,
        string subtitulo,
        IReadOnlyList<CasoPruebaExcelFila> casos)
    {
        var ws = wb.Worksheets.Add(HojaCasos);

        var titleFill = XLColor.FromHtml("#2F5496");
        var headerFill = XLColor.FromHtml("#1F4E79");
        var subFill = XLColor.FromHtml("#D6E4F0");
        var passFill = XLColor.FromHtml("#C6EFCE");
        var failFill = XLColor.FromHtml("#FFC7CE");

        ws.Range(1, 1, 1, Headers.Length).Merge();
        var t = ws.Cell(1, 1);
        t.Value = tituloPrincipal;
        t.Style.Font.Bold = true;
        t.Style.Font.FontSize = 13;
        t.Style.Font.FontColor = XLColor.White;
        t.Style.Fill.BackgroundColor = titleFill;
        t.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        t.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        t.Style.Alignment.WrapText = true;
        t.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        ws.Row(1).Height = 28;

        ws.Range(2, 1, 2, Headers.Length).Merge();
        var s = ws.Cell(2, 1);
        s.Value = subtitulo;
        s.Style.Font.FontSize = 11;
        s.Style.Fill.BackgroundColor = subFill;
        s.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        s.Style.Alignment.WrapText = true;
        s.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        ws.Row(2).Height = 36;

        const int headerRow = 3;
        for (var c = 0; c < Headers.Length; c++)
        {
            var cell = ws.Cell(headerRow, c + 1);
            cell.Value = Headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = headerFill;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            cell.Style.Alignment.WrapText = true;
            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        }

        ws.Row(headerRow).Height = 26;

        for (var i = 0; i < casos.Count; i++)
        {
            var caso = casos[i];
            var row = headerRow + 1 + i;
            var estado = NormalizarEstado(caso.Estado);
            var esperado = (caso.ResultadoEsperado ?? "").Trim();
            var obtenido = (caso.ResultadoObtenido ?? "").Trim();
            var obs = ObservacionSegunRegla(estado, caso.Observaciones, esperado, obtenido);

            ws.Cell(row, 1).Value = caso.Id ?? "";
            ws.Cell(row, 2).Value = caso.Titulo ?? "";
            ws.Cell(row, 3).Value = caso.Precondiciones ?? "";
            ws.Cell(row, 4).Value = caso.Pasos ?? "";
            ws.Cell(row, 5).Value = esperado;
            ws.Cell(row, 6).Value = obtenido;
            ws.Cell(row, 7).Value = estado;
            ws.Cell(row, 8).Value = obs;

            for (var c = 1; c <= Headers.Length; c++)
            {
                var cell = ws.Cell(row, c);
                cell.Style.Alignment.WrapText = true;
                cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
                cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            }

            var estadoCell = ws.Cell(row, 7);
            if (estado.Equals("PASS", StringComparison.OrdinalIgnoreCase))
            {
                estadoCell.Style.Fill.BackgroundColor = passFill;
                estadoCell.Style.Font.FontColor = XLColor.FromHtml("#006100");
                estadoCell.Style.Font.Bold = true;
            }
            else if (estado.Equals("FAIL", StringComparison.OrdinalIgnoreCase)
                     || estado.Equals("FALLA", StringComparison.OrdinalIgnoreCase)
                     || estado.Equals("BUG", StringComparison.OrdinalIgnoreCase))
            {
                estadoCell.Style.Fill.BackgroundColor = failFill;
                estadoCell.Style.Font.FontColor = XLColor.FromHtml("#9C0006");
                estadoCell.Style.Font.Bold = true;
            }
            else if (estado.Equals("PENDIENTE", StringComparison.OrdinalIgnoreCase))
            {
                estadoCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#FFEB9C");
                estadoCell.Style.Font.Bold = true;
            }
            estadoCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            ws.Row(row).Height = 150;
        }

        double[] widths = [14, 34, 32, 40, 34, 46, 10, 34];
        for (var i = 0; i < widths.Length; i++)
            ws.Column(i + 1).Width = widths[i];

        ws.SheetView.FreezeRows(headerRow);
        if (casos.Count > 0)
            ws.Range(headerRow, 1, headerRow + casos.Count, Headers.Length).SetAutoFilter();
    }

    private static void EscribirHojaEvidencias(
        XLWorkbook wb,
        string ticket,
        IReadOnlyList<CasoPruebaExcelFila> casos,
        IReadOnlyList<EvidenciaPasoExcel>? pasos,
        IReadOnlyDictionary<string, IReadOnlyList<CasosPruebaQueryExcel>>? queriesPorCaso = null)
    {
        var ws = wb.Worksheets.Add(HojaEvidencias);
        ws.Column(1).Width = 4;
        ws.Column(2).Width = 120;

        var titleFill = XLColor.FromHtml("#2F5496");
        var headerFill = XLColor.FromHtml("#1F4E79");
        var stepFill = XLColor.FromHtml("#1F4E79");
        var sepFill = XLColor.FromHtml("#E8EEF7");
        var cnFill = XLColor.FromHtml("#FCE8E6");
        var pendingFill = XLColor.FromHtml("#FFF8E1");
        var qHdrFill = XLColor.FromHtml("#375623");
        var qBoxFill = XLColor.FromHtml("#F4F9F1");

        ws.Range(1, 1, 1, 2).Merge();
        var titulo = ws.Cell(1, 1);
        titulo.Value = $"{ticket} — Evidencias paso a paso";
        titulo.Style.Font.Bold = true;
        titulo.Style.Font.FontSize = 13;
        titulo.Style.Font.FontColor = XLColor.White;
        titulo.Style.Fill.BackgroundColor = titleFill;
        titulo.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        titulo.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        titulo.Style.Alignment.WrapText = true;
        titulo.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        ws.Row(1).Height = 26;

        var lista = pasos ?? [];
        if (casos.Count == 0)
        {
            ws.Cell(3, 2).Value =
                "Sin capturas embebidas. Completar esta hoja con PNG por paso tras la ejecución.";
            ws.Cell(3, 2).Style.Alignment.WrapText = true;
            ws.Row(3).Height = 46;
            return;
        }

        var row = 3;
        var hubo = false;
        string? prevTipo = null;

        foreach (var caso in casos)
        {
            var casoId = (caso.Id ?? "").Trim();
            var tipo = TipoCasoExcel(casoId);
            if (prevTipo is null)
            {
                EscribirSeparadorEvidencias(ws, ref row, sepFill, $"════ Casos positivos (CA) ════");
            }
            else if (!string.Equals(prevTipo, tipo, StringComparison.OrdinalIgnoreCase))
            {
                var etiqueta = tipo.Equals("CN", StringComparison.OrdinalIgnoreCase)
                    ? "negativos (CN)"
                    : "positivos (CA)";
                EscribirSeparadorEvidencias(ws, ref row, sepFill, $"════ Casos {etiqueta} ════");
            }

            prevTipo = tipo;

            var pasosCaso = lista
                .Where(p => IdsCasoEquivalentes(casoId, p.CasoId))
                .ToList();
            var estado = NormalizarEstado(caso.Estado);
            var encabezado =
                $"[{tipo}] {casoId} — {(caso.Titulo ?? "").Trim()} ({estado})".Trim(' ', '—');
            var head = ws.Cell(row, 2);
            head.Value = encabezado;
            head.Style.Font.Bold = true;
            head.Style.Font.FontColor = XLColor.White;
            head.Style.Fill.BackgroundColor = tipo.Equals("CN", StringComparison.OrdinalIgnoreCase)
                ? cnFill
                : headerFill;
            head.Style.Alignment.WrapText = true;
            head.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            ws.Row(row).Height = 26;
            row++;

            if (pasosCaso.Count == 0)
            {
                var note = ws.Cell(row, 2);
                note.Value =
                    $"Pendiente de capturas — ejecutar el caso en Runner o carpeta evidencias/{casoId}/";
                note.Style.Alignment.WrapText = true;
                note.Style.Fill.BackgroundColor = pendingFill;
                note.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                note.Style.Font.Italic = true;
                note.Style.Font.FontColor = XLColor.FromHtml("#7F6000");
                ws.Row(row).Height = 36;
                row += 2;
            }
            else
            {
                hubo = true;
                for (var i = 0; i < pasosCaso.Count; i++)
                {
                    var paso = pasosCaso[i];
                    var n = i + 1;
                    var tituloPaso = (paso.Titulo ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(tituloPaso))
                        tituloPaso = $"Paso {n}";
                    else if (!tituloPaso.StartsWith("Paso ", StringComparison.OrdinalIgnoreCase))
                        tituloPaso = $"Paso {n} — {tituloPaso}";

                    var h = ws.Cell(row, 2);
                    h.Value = tituloPaso;
                    h.Style.Font.Bold = true;
                    h.Style.Font.FontColor = XLColor.White;
                    h.Style.Fill.BackgroundColor = stepFill;
                    h.Style.Alignment.WrapText = true;
                    ws.Row(row).Height = 20;
                    row++;

                    var desc = (paso.Descripcion ?? "").Trim();
                    if (string.IsNullOrEmpty(desc))
                        desc = $"Evidencia del caso {casoId}.";
                    var d = ws.Cell(row, 2);
                    d.Value = desc;
                    d.Style.Alignment.WrapText = true;
                    d.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
                    ws.Row(row).Height = 34;
                    row++;

                    var imgRow = row;
                    if (!string.IsNullOrWhiteSpace(paso.ImagenPath) && File.Exists(paso.ImagenPath))
                    {
                        try
                        {
                            var (iw, ih) = LeerDimensionesPng(paso.ImagenPath);
                            var scale = Math.Min(1.0, 920.0 / Math.Max(1, iw));
                            var pic = ws.AddPicture(paso.ImagenPath).MoveTo(ws.Cell(imgRow, 2));
                            pic.Width = (int)(iw * scale);
                            pic.Height = (int)(ih * scale);
                            ws.Row(imgRow).Height = Math.Max(80, pic.Height * 0.75);
                            row += 2;
                        }
                        catch
                        {
                            ws.Cell(imgRow, 2).Value =
                                $"(No se pudo embeber: {Path.GetFileName(paso.ImagenPath)})";
                            ws.Row(imgRow).Height = 46;
                            row += 2;
                        }
                    }
                    else
                    {
                        var ph = ws.Cell(imgRow, 2);
                        ph.Value = "Pegar captura aquí";
                        ph.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        ph.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                        ph.Style.Alignment.WrapText = true;
                        ph.Style.Fill.BackgroundColor = XLColor.FromHtml("#F2F2F2");
                        ph.Style.Font.Italic = true;
                        ph.Style.Font.FontColor = XLColor.FromHtml("#666666");
                        ws.Row(imgRow).Height = 220;
                        row += 2;
                    }
                }
            }

            if (queriesPorCaso is not null
                && queriesPorCaso.TryGetValue(casoId, out var qCaso)
                && qCaso.Count > 0)
            {
                row = EscribirBloqueQueriesEnEvidencias(
                    ws, row, qCaso, stepFill, qHdrFill, qBoxFill);
            }

            row++;
        }

        if (!hubo && lista.Count == 0)
        {
            ws.Cell(3, 2).Value =
                "Sin capturas embebidas. Completar esta hoja con PNG por paso tras la ejecución.";
            ws.Cell(3, 2).Style.Alignment.WrapText = true;
            ws.Row(3).Height = 46;
        }
    }

    private static void EscribirSeparadorEvidencias(
        IXLWorksheet ws,
        ref int row,
        XLColor fill,
        string texto)
    {
        var sep = ws.Cell(row, 2);
        sep.Value = texto;
        sep.Style.Font.Bold = true;
        sep.Style.Font.FontColor = XLColor.FromHtml("#1F4E79");
        sep.Style.Fill.BackgroundColor = fill;
        sep.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        sep.Style.Alignment.WrapText = true;
        sep.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        ws.Row(row).Height = 24;
        row++;
    }

    private static int EscribirBloqueQueriesEnEvidencias(
        IXLWorksheet ws,
        int row,
        IReadOnlyList<CasosPruebaQueryExcel> queries,
        XLColor stepFill,
        XLColor qHdrFill,
        XLColor qBoxFill)
    {
        var head = ws.Cell(row, 2);
        head.Value = "Consultas BD — solo lectura (SOT / COBIS)";
        head.Style.Font.Bold = true;
        head.Style.Font.FontColor = XLColor.White;
        head.Style.Fill.BackgroundColor = qHdrFill;
        head.Style.Alignment.WrapText = true;
        ws.Row(row).Height = 22;
        row++;

        foreach (var q in queries)
        {
            var motor = (q.Motor ?? "").Trim();
            var proposito = (q.Titulo ?? q.Proposito ?? "Consulta").Trim();
            var label = string.IsNullOrEmpty(motor) ? proposito : $"{proposito} [{motor}]";

            var h = ws.Cell(row, 2);
            h.Value = label;
            h.Style.Font.Bold = true;
            h.Style.Font.FontColor = XLColor.White;
            h.Style.Fill.BackgroundColor = stepFill;
            h.Style.Alignment.WrapText = true;
            ws.Row(row).Height = 20;
            row++;

            var sql = (q.Sql ?? "").Trim();
            if (!string.IsNullOrEmpty(sql))
            {
                var s = ws.Cell(row, 2);
                s.Value = sql;
                s.Style.Alignment.WrapText = true;
                s.Style.Font.FontName = "Consolas";
                s.Style.Font.FontSize = 9;
                s.Style.Fill.BackgroundColor = qBoxFill;
                s.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                var lineas = Math.Max(1, sql.Count(c => c == '\n') + 1);
                ws.Row(row).Height = Math.Clamp(14 * lineas + 12, 48, 180);
                row++;
            }

            var resultado = (q.Resultado ?? q.Muestra ?? q.Uso ?? "").Trim();
            if (!string.IsNullOrEmpty(resultado))
            {
                var r = ws.Cell(row, 2);
                r.Value = resultado.StartsWith("Resultado:", StringComparison.OrdinalIgnoreCase)
                    ? resultado
                    : "Resultado:\n" + resultado;
                r.Style.Alignment.WrapText = true;
                r.Style.Font.FontName = "Consolas";
                r.Style.Font.FontSize = 9;
                r.Style.Font.Bold = true;
                r.Style.Fill.BackgroundColor = qBoxFill;
                r.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                var lineas = Math.Max(1, resultado.Count(c => c == '\n') + 1);
                ws.Row(row).Height = Math.Clamp(14 * lineas + 12, 36, 160);
                row++;
            }

            row++;
        }

        return row;
    }

    private static string TipoCasoExcel(string? casoId) =>
        casoId?.Contains("-CN-", StringComparison.OrdinalIgnoreCase) == true ? "CN" : "CA";

    private static void EscribirHojaQueries(
        XLWorkbook wb,
        string ticket,
        IReadOnlyList<CasosPruebaQueryExcel> queries,
        string? nota)
    {
        var ws = wb.Worksheets.Add(HojaQueries);
        var titleFill = XLColor.FromHtml("#2F5496");
        var headerFill = XLColor.FromHtml("#1F4E79");
        var subFill = XLColor.FromHtml("#D6E4F0");
        var lastCol = QueryHeaders.Length;

        ws.Range(1, 1, 1, lastCol).Merge();
        var titulo = ws.Cell(1, 1);
        titulo.Value = $"{ticket} — Queries de consulta BD";
        titulo.Style.Font.Bold = true;
        titulo.Style.Font.FontSize = 13;
        titulo.Style.Font.FontColor = XLColor.White;
        titulo.Style.Fill.BackgroundColor = titleFill;
        titulo.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        titulo.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        titulo.Style.Alignment.WrapText = true;
        titulo.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        ws.Row(1).Height = 28;

        var headerRow = 2;
        if (!string.IsNullOrWhiteSpace(nota))
        {
            ws.Range(2, 1, 2, lastCol).Merge();
            var n = ws.Cell(2, 1);
            n.Value = nota.Trim();
            n.Style.Font.FontSize = 11;
            n.Style.Fill.BackgroundColor = subFill;
            n.Style.Alignment.WrapText = true;
            n.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            n.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            ws.Row(2).Height = 40;
            headerRow = 3;
        }

        for (var c = 0; c < QueryHeaders.Length; c++)
        {
            var cell = ws.Cell(headerRow, c + 1);
            cell.Value = QueryHeaders[c];
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = headerFill;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            cell.Style.Alignment.WrapText = true;
            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        }

        ws.Row(headerRow).Height = 24;

        var row = headerRow + 1;
        for (var i = 0; i < queries.Count; i++)
        {
            var q = queries[i];
            var orden = q.Orden ?? (i + 1);
            var proposito = (q.Titulo ?? q.Proposito ?? "").Trim();
            var sql = (q.Sql ?? "").Trim();
            var uso = (q.Uso ?? q.Notas ?? "").Trim();

            ws.Cell(row, 1).Value = orden;
            ws.Cell(row, 2).Value = proposito;
            ws.Cell(row, 3).Value = sql;
            ws.Cell(row, 4).Value = uso;

            for (var c = 1; c <= lastCol; c++)
            {
                var cell = ws.Cell(row, c);
                cell.Style.Alignment.WrapText = true;
                cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
                cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                if (c == 3)
                    cell.Style.Font.FontName = "Consolas";
            }

            var lineas = Math.Max(1, sql.Count(c => c == '\n') + 1);
            ws.Row(row).Height = Math.Clamp(14 * lineas + 40, 60, 220);
            row++;
        }

        ws.Column(1).Width = 6;
        ws.Column(2).Width = 34;
        ws.Column(3).Width = 72;
        ws.Column(4).Width = 34;
        ws.SheetView.FreezeRows(headerRow);
    }

    private static bool IdsCasoEquivalentes(string casoId, string? pasoCasoId)
    {
        if (string.IsNullOrWhiteSpace(casoId) || string.IsNullOrWhiteSpace(pasoCasoId))
            return false;
        if (string.Equals(casoId, pasoCasoId, StringComparison.OrdinalIgnoreCase))
            return true;
        var a = casoId.Replace("-", "", StringComparison.Ordinal);
        var b = pasoCasoId.Replace("-", "", StringComparison.Ordinal);
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    private static (int Width, int Height) LeerDimensionesPng(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            Span<byte> hdr = stackalloc byte[24];
            if (fs.Read(hdr) < 24 || hdr[0] != 137)
                return (900, 395);
            var w = (hdr[16] << 24) | (hdr[17] << 16) | (hdr[18] << 8) | hdr[19];
            var h = (hdr[20] << 24) | (hdr[21] << 16) | (hdr[22] << 8) | hdr[23];
            if (w <= 0 || h <= 0) return (900, 395);
            return (w, h);
        }
        catch
        {
            return (900, 395);
        }
    }

    private static string ResolverTituloPrincipal(
        string ticket,
        string? tituloDocumento,
        IReadOnlyList<CasoPruebaExcelFila> casos)
    {
        if (!string.IsNullOrWhiteSpace(tituloDocumento))
            return $"{ticket} — {tituloDocumento.Trim()}";

        if (casos.Count == 1 && !string.IsNullOrWhiteSpace(casos[0].Titulo))
            return $"{ticket} — {casos[0].Titulo!.Trim()}";

        if (casos.Count > 1)
            return $"{ticket} — Casos de prueba ({casos.Count})";

        return $"{ticket} — Casos de prueba";
    }

    /// <summary>
    /// Conserva observaciones informadas. Si FAIL/BUG sin obs y esperado ≠ obtenido, sugiere bug.
    /// </summary>
    public static string ObservacionSegunRegla(
        string estado,
        string? observaciones,
        string? resultadoEsperado,
        string? resultadoObtenido)
    {
        var obs = (observaciones ?? "").Trim();
        if (!string.IsNullOrEmpty(obs))
            return obs;

        var e = NormalizarEstado(estado);
        if (e.Equals("PASS", StringComparison.OrdinalIgnoreCase))
            return "";

        var esp = (resultadoEsperado ?? "").Trim();
        var obt = (resultadoObtenido ?? "").Trim();
        if (esp.Length > 0 && obt.Length > 0 && !ResultadosEquivalentes(esp, obt))
            return "Bug: el resultado obtenido no coincide con el esperado.";

        return "";
    }

    private static bool ResultadosEquivalentes(string esperado, string obtenido)
    {
        if (string.Equals(esperado, obtenido, StringComparison.OrdinalIgnoreCase))
            return true;

        if (obtenido.StartsWith("OK.", StringComparison.OrdinalIgnoreCase)
            && obtenido.Contains(esperado, StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private static string NormalizarEstado(string? estado)
    {
        var e = (estado ?? "").Trim().ToUpperInvariant();
        return e switch
        {
            "OK" or "PASSED" or "PASS" or "EXITO" or "ÉXITO" or "EXITOSA" or "WARNING" or "WARN" => "PASS",
            "FAIL" or "FAILED" or "FALLO" or "FALLÓ" or "FALLA" or "ERROR" or "BUG" => e switch
            {
                "BUG" => "BUG",
                "FALLA" => "FALLA",
                _ => "FAIL"
            },
            _ => string.IsNullOrEmpty(e) ? "FAIL" : e
        };
    }

    public static string NombreArchivo(string ticketOModulo, string? stamp = null)
    {
        var ticket = NormalizarTicketParaArchivo(ticketOModulo);
        // Formato canónico (referencia SC-414). Stamp opcional solo para copias locales concurrentes.
        if (string.IsNullOrWhiteSpace(stamp))
            return $"{ticket}-Casos-y-Evidencias.xlsx";
        return $"{ticket}-Casos-y-Evidencias_{stamp}.xlsx";
    }

    public static string NormalizarTicketParaArchivo(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "Modulo";

        var m = Regex.Match(raw.Trim(), @"\bSC-?(\d+)\b", RegexOptions.IgnoreCase);
        if (m.Success)
            return $"SC-{m.Groups[1].Value}";

        var safe = raw.Trim();
        foreach (var ch in Path.GetInvalidFileNameChars().Concat([':', '\\', '/', '?', '*', '[', ']']))
            safe = safe.Replace(ch, '-');
        safe = safe.Replace(' ', '-');
        while (safe.Contains("--", StringComparison.Ordinal))
            safe = safe.Replace("--", "-", StringComparison.Ordinal);
        if (safe.Length > 40)
            safe = safe[..40].TrimEnd('-');
        return string.IsNullOrWhiteSpace(safe) ? "Modulo" : safe;
    }
}

public sealed class CasosPruebaExcelOpciones
{
    public string? TicketOModulo { get; set; }
    public string? TituloDocumento { get; set; }
    public string? Subtitulo { get; set; }
    public string? ContextoRelease { get; set; }
    public IReadOnlyList<CasoPruebaExcelFila>? Casos { get; set; }
    public IReadOnlyList<EvidenciaPasoExcel>? PasosEvidencia { get; set; }
    public IReadOnlyDictionary<string, IReadOnlyList<CasosPruebaQueryExcel>>? QueriesPorCaso { get; set; }
    public IReadOnlyList<CasosPruebaQueryExcel>? Queries { get; set; }
    public string? QueriesNota { get; set; }
}

public sealed class EvidenciaPasoExcel
{
    public string? CasoId { get; set; }
    public string? Titulo { get; set; }
    public string? Descripcion { get; set; }
    public string? ImagenPath { get; set; }
}

public sealed class CasoPruebaExcelFila
{
    public string? Id { get; set; }
    public string? Titulo { get; set; }
    public string? Tag { get; set; }
    public string? Precondiciones { get; set; }
    public string? Pasos { get; set; }
    public string? ResultadoEsperado { get; set; }
    public string? ResultadoObtenido { get; set; }
    public string? Estado { get; set; }
    public string? Observaciones { get; set; }
}

public sealed class CasosPruebaQueryExcel
{
    public int? Orden { get; set; }
    public string? Titulo { get; set; }
    public string? Proposito { get; set; }
    public string? Sql { get; set; }
    public string? Uso { get; set; }
    public string? Notas { get; set; }
    public string? Motor { get; set; }
    public string? Resultado { get; set; }
    public string? Muestra { get; set; }
}

public sealed class ExcelModuloRequest
{
    public string? NombreModulo { get; set; }
    public string? Ticket { get; set; }
    public List<CasoPruebaExcelFila>? Casos { get; set; }
}
