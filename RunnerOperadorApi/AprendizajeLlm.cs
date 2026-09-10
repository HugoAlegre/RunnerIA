using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

/// <summary>
/// Memoria de ejemplos al guardar + LLM opcional (OpenAI-compatible) para mejorar borradores.
/// No guarda API keys en disco: solo llegan en el request de analizar.
/// </summary>
public static class AprendizajeLlm
{
    private static readonly HttpClient LlmHttp = new() { Timeout = TimeSpan.FromSeconds(90) };
    private const int MaxEjemplos = 48;
    private const int MaxEjemplosInyectar = 5;

    private sealed class CatalogoAprendizaje
    {
        public List<EjemploAprendizaje> Ejemplos { get; set; } = [];
    }

    private sealed class EjemploAprendizaje
    {
        public string Id { get; set; } = "";
        public string Ticket { get; set; } = "";
        public string Titulo { get; set; } = "";
        public string Detalle { get; set; } = "";
        public string Modulo { get; set; } = "";
        public string NombreArchivo { get; set; } = "";
        public string Gherkin { get; set; } = "";
        public string GuardadoUtc { get; set; } = "";
    }

    private static string RutaCatalogo(string pruebasFeatures) =>
        Path.Combine(pruebasFeatures, "aprendizaje.json");

    public static void RegistrarEjemploAlGuardar(
        string pruebasFeatures,
        string nombreArchivo,
        string gherkin,
        string? titulo,
        string? detalle,
        string? modulo)
    {
        try
        {
            Directory.CreateDirectory(pruebasFeatures);
            if (string.IsNullOrWhiteSpace(gherkin) ||
                gherkin.Contains("FALTA INFORMACIÓN", StringComparison.OrdinalIgnoreCase))
                return;

            var ticket = ExtraerTicket(gherkin) ?? "";
            var cat = LeerCatalogo(pruebasFeatures);
            var id = !string.IsNullOrWhiteSpace(ticket)
                ? "tkt-" + ticket
                : "file-" + Path.GetFileNameWithoutExtension(nombreArchivo);

            cat.Ejemplos.RemoveAll(e =>
                string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(e.NombreArchivo, nombreArchivo, StringComparison.OrdinalIgnoreCase));

            cat.Ejemplos.Insert(0, new EjemploAprendizaje
            {
                Id = id,
                Ticket = ticket,
                Titulo = (titulo ?? "").Trim(),
                Detalle = Truncar((detalle ?? "").Trim(), 400),
                Modulo = (modulo ?? "").Trim(),
                NombreArchivo = nombreArchivo,
                Gherkin = Truncar(gherkin.Trim(), 12000),
                GuardadoUtc = DateTime.UtcNow.ToString("o")
            });

            if (cat.Ejemplos.Count > MaxEjemplos)
                cat.Ejemplos = cat.Ejemplos.Take(MaxEjemplos).ToList();

            GuardarCatalogo(pruebasFeatures, cat);
        }
        catch
        {
            // best-effort: no tumba el guardado
        }
    }

    public static (string TextoEjemplos, int Cantidad, List<string> Avisos) ObtenerEjemplosParaAnalizar(
        string pruebasFeatures,
        string corpus,
        string? ticket,
        string? titulo)
    {
        var avisos = new List<string>();
        var cat = LeerCatalogo(pruebasFeatures);
        if (cat.Ejemplos.Count == 0)
            return ("", 0, avisos);

        var ranked = cat.Ejemplos
            .Select(e => new { Ej = e, Score = ScoreSimilitud(e, corpus, ticket, titulo) })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Ej.GuardadoUtc)
            .Take(MaxEjemplosInyectar)
            .Select(x => x.Ej)
            .ToList();

        if (ranked.Count == 0)
        {
            ranked = cat.Ejemplos.Take(Math.Min(2, cat.Ejemplos.Count)).ToList();
            avisos.Add("Memoria: se usan ejemplos recientes (poca similitud con el ticket actual).");
        }
        else
        {
            avisos.Add($"Memoria: se reutilizan {ranked.Count} ejemplo(s) guardados al confirmar pruebas.");
        }

        var sb = new StringBuilder();
        sb.AppendLine(ReglasGeneracion.BloqueUserPrompt());
        sb.AppendLine("Ejemplos de Gherkin ya validados por el equipo (aprendidos al Guardar):");
        var i = 1;
        foreach (var e in ranked)
        {
            sb.AppendLine($"--- Ejemplo {i++} ({(string.IsNullOrWhiteSpace(e.Ticket) ? e.NombreArchivo : e.Ticket)}) ---");
            if (!string.IsNullOrWhiteSpace(e.Titulo)) sb.AppendLine("Título: " + e.Titulo);
            if (!string.IsNullOrWhiteSpace(e.Detalle)) sb.AppendLine("Detalle: " + e.Detalle);
            sb.AppendLine(e.Gherkin);
            sb.AppendLine();
        }
        return (sb.ToString().Trim(), ranked.Count, avisos);
    }

    public static async Task<(bool Ok, string FeatureMejorado, string Aviso)> MejorarConLlmOpcionalAsync(
        string corpus,
        string featureBorrador,
        string ejemplosTexto,
        IEnumerable<string> stepsReutilizables,
        string? llmApiKey,
        string? llmBaseUrl,
        string? llmModel,
        bool usarLlm,
        string? mapaAutomatizacion = null,
        string? detalleOperador = null)
    {
        if (!usarLlm || string.IsNullOrWhiteSpace(llmApiKey))
            return (false, featureBorrador, "");

        var baseUrl = string.IsNullOrWhiteSpace(llmBaseUrl)
            ? "https://api.openai.com/v1"
            : llmBaseUrl.Trim().TrimEnd('/');
        var model = string.IsNullOrWhiteSpace(llmModel) ? "gpt-4o-mini" : llmModel.Trim();

        var steps = string.Join("\n", stepsReutilizables.Take(30).Select(s => "- " + s));
        var system = """
Sos RunnerIA: asistente de QA para automatización E2E del sistema SOT (caja bancaria) con SpecFlow/Gherkin y Playwright.
Solo generás borradores .feature del producto. NO inventés código de aplicación ni comandos de sistema.
PRIORIDAD 1 (obligatoria): interpretá el DETALLE del operador como esté escrito (párrafo, lista, chat, etc.).
NO exijas formato especial. Convertí esa intención al Gherkin correcto. NO lo reemplaces por otro módulo
(p. ej. no pases a Intercaja si el Detalle pide Fallas, Cierre, COBIS, SQL SOT u otra cosa).
PRIORIDAD 2: si no hay Detalle, usá Jira/docs/mapa.
Conocés circuitos (Intercaja, Alta, Cierre/cuadre, Cierre forzado Nota SC-416, Fallas de caja, Otros Ingresos, Cheques, Parametría, Caja-Bóveda, COBIS/SQL SOT).
Fallas de caja (solo si el Detalle lo pide): caja con dinero → falla → Transacciones Monetarias + Tira auditora.
SC-416 / Nota en Forzar cierre: Compensada SIN Nota; Normal y MiniBóveda CON Nota; cancelar diálogo.
Si el Detalle habla de Sybase/COBIS o SQL SOT/UW_CASHIER: verificar conexion y/o SELECT (solo lectura).
En Gherkin NO inventes pantallas fuera del mapa. Tu salida DEBE ser SOLO un .feature válido en español UTF-8
(Background canónico con «contraseña» y ñ real). No expliques nada fuera del Gherkin.
""" + "\n" + ReglasGeneracion.ReglasSystem;

        var user = new StringBuilder();
        user.AppendLine(ReglasGeneracion.BloqueUserPrompt());
        if (!string.IsNullOrWhiteSpace(detalleOperador))
        {
            user.AppendLine("=== DETALLE DEL OPERADOR (interpretar como esté escrito; prioridad máxima) ===");
            user.AppendLine(Truncar(detalleOperador, 4000));
            user.AppendLine();
        }
        if (!string.IsNullOrWhiteSpace(mapaAutomatizacion))
        {
            user.AppendLine(Truncar(mapaAutomatizacion, 12000));
            user.AppendLine();
        }
        user.AppendLine("Contexto del ticket / fuentes:");
        user.AppendLine(Truncar(corpus, 9000));
        user.AppendLine();
        if (!string.IsNullOrWhiteSpace(ejemplosTexto))
        {
            user.AppendLine(Truncar(ejemplosTexto, 7000));
            user.AppendLine();
        }
        if (!string.IsNullOrWhiteSpace(steps))
        {
            user.AppendLine("Steps existentes del proyecto (preferí reutilizar frases parecidas, sin cambiar el orden del Detalle):");
            user.AppendLine(steps);
            user.AppendLine();
        }
        user.AppendLine("Borrador actual a mejorar (mantené Background y el ORDEN de pasos del Detalle/borrador):");
        user.AppendLine(Truncar(featureBorrador, 6000));

        try
        {
            var url = baseUrl + "/chat/completions";
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", llmApiKey.Trim());
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var payload = new
            {
                model,
                temperature = 0.2,
                messages = new object[]
                {
                    new { role = "system", content = system },
                    new { role = "user", content = user.ToString() }
                }
            };
            req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var res = await LlmHttp.SendAsync(req);
            var body = await res.Content.ReadAsStringAsync();
            if (!res.IsSuccessStatusCode)
                return (false, featureBorrador, $"LLM no disponible ({(int)res.StatusCode}). Se usa el borrador local. Detalle: {Truncar(body, 180)}");

            using var doc = JsonDocument.Parse(body);
            var content = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? "";

            var feature = ExtraerFeatureDeRespuestaLlm(content);
            if (string.IsNullOrWhiteSpace(feature) || !feature.Contains("Feature:", StringComparison.OrdinalIgnoreCase))
                return (false, featureBorrador, "LLM respondió sin un .feature usable; se mantiene el borrador local.");

            if (!feature.Contains("@Prueba", StringComparison.OrdinalIgnoreCase))
                feature = "@Prueba @PruebaRun_Borrador\n" + feature.TrimStart();

            return (true, feature.TrimEnd() + "\n", "LLM: borrador mejorado con mapa UI + ejemplos + steps del proyecto.");
        }
        catch (Exception ex)
        {
            return (false, featureBorrador, "LLM falló (" + ex.Message + "). Se usa el borrador local.");
        }
    }

    /// <summary>
    /// Describe pantallazos (visión multimodal) para enriquecer el análisis del ticket.
    /// </summary>
    public static async Task<(bool Ok, string Descripcion, string Aviso)> InterpretarImagenAsync(
        byte[] bytes,
        string filename,
        string? llmApiKey,
        string? llmBaseUrl,
        string? llmModel)
    {
        if (bytes.Length == 0 || string.IsNullOrWhiteSpace(llmApiKey))
            return (false, "", "");

        // Limitar tamaño (~4 MB) para APIs típicas.
        if (bytes.Length > 4_000_000)
            return (false, "", $"Imagen «{filename}» demasiado grande para interpretar ({bytes.Length / 1024} KB).");

        var baseUrl = string.IsNullOrWhiteSpace(llmBaseUrl)
            ? "https://api.openai.com/v1"
            : llmBaseUrl.Trim().TrimEnd('/');
        // Mini suele soportar visión; si el modelo no, el caller verá el aviso.
        var model = string.IsNullOrWhiteSpace(llmModel) ? "gpt-4o-mini" : llmModel.Trim();
        var mime = MimeImagen(filename);
        var b64 = Convert.ToBase64String(bytes);
        var dataUrl = $"data:{mime};base64,{b64}";

        var system = """
Sos RunnerIA (alcance limitado): analista QA del sistema SOT. Describí la captura en español para armar una prueba E2E.
Incluí: pantalla/módulo, mensajes, campos, botones, montos, estados y qué parece querer validar el tester.
Si es Cuadre/cierre: indicá si hay Caja cuadrada o descuadrada, sobrante/faltante, billetaje vs saldo, diálogos de supervisión.
Si es Transacciones Monetarias: listá tipo de movimiento, estado e importes visibles de la falla.
Si es Tira auditora: listá filas visibles (fecha, usuario, evento, importe, caja/sucursal) y si parece sobrante o faltante.
Contrastá montos/textos legibles entre capturas; no inventes texto ilegible: indicá "(ilegible)".
No sugieras acciones fuera de QA E2E. Máximo 14 líneas. Solo la descripción.
""";

        try
        {
            var url = baseUrl + "/chat/completions";
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", llmApiKey.Trim());
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var payload = new Dictionary<string, object?>
            {
                ["model"] = model,
                ["temperature"] = 0.1,
                ["max_tokens"] = 700,
                ["messages"] = new object[]
                {
                    new { role = "system", content = system },
                    new
                    {
                        role = "user",
                        content = new object[]
                        {
                            new
                            {
                                type = "text",
                                text = $"Interpretá esta captura adjunta de Jira/documentación: {filename}"
                            },
                            new
                            {
                                type = "image_url",
                                image_url = new { url = dataUrl, detail = "high" }
                            }
                        }
                    }
                }
            };
            req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var res = await LlmHttp.SendAsync(req);
            var body = await res.Content.ReadAsStringAsync();
            if (!res.IsSuccessStatusCode)
                return (false, "", $"No se pudo interpretar «{filename}» ({(int)res.StatusCode}). Detalle: {Truncar(body, 160)}");

            using var doc = JsonDocument.Parse(body);
            var content = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? "";
            content = content.Trim();
            if (string.IsNullOrWhiteSpace(content))
                return (false, "", $"Visión sin texto útil para «{filename}».");
            return (true, Truncar(content, 1800), "");
        }
        catch (Exception ex)
        {
            return (false, "", $"Error interpretando «{filename}»: {ex.Message}");
        }
    }

    /// <summary>
    /// Arma un detalle/descripción del caso fiel a lo que el ticket pide verificar.
    /// </summary>
    public static async Task<(bool Ok, string Detalle, string Aviso)> RefinarDetalleCasoAsync(
        string titulo,
        string corpus,
        string? tipoIssue,
        string? llmApiKey,
        string? llmBaseUrl,
        string? llmModel)
    {
        if (string.IsNullOrWhiteSpace(llmApiKey))
            return (false, "", "");

        var baseUrl = string.IsNullOrWhiteSpace(llmBaseUrl)
            ? "https://api.openai.com/v1"
            : llmBaseUrl.Trim().TrimEnd('/');
        var model = string.IsNullOrWhiteSpace(llmModel) ? "gpt-4o-mini" : llmModel.Trim();
        var tipo = string.IsNullOrWhiteSpace(tipoIssue) ? "ticket" : tipoIssue.Trim();

        var system = """
Sos RunnerIA (alcance limitado): analista QA SOT. Redactá en español el DETALLE del caso de prueba (2 a 5 oraciones).
Debe decir: qué se quiere verificar, resultado esperado y, si hay, condición actual / bug o evidencia de pantallas.
Si el ticket es Fallas de caja / sobrante / faltante: mencioná caja con dinero, cierre descuadrado, tipo de falla y verificación de montos en Transacciones Monetarias y en tira auditora según documento/imágenes.
Si el ticket es SC-416 / Nota en cierre forzado: Compensada sin Nota de billetaje; Normal/MiniBóveda con Nota; diálogo Forzar cierre y cancelar (no confirmar cierre).
Si el ticket pide datos: mencioná COBIS (Sybase) y/o SQL SOT, probar conexión y consultas SELECT de solo lectura.
No copies solo el título. No uses markdown. No inventes datos. No sugieras comandos de sistema ni cambios de código de producto.
""" + "\n" + ReglasGeneracion.ReglasSystem;
        var user = $"Tipo: {tipo}\nTítulo: {titulo}\n\nContexto:\n{Truncar(corpus, 12000)}\n\nDetalle del caso:";

        try
        {
            var url = baseUrl + "/chat/completions";
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", llmApiKey.Trim());
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            var payload = new
            {
                model,
                temperature = 0.2,
                max_tokens = 450,
                messages = new object[]
                {
                    new { role = "system", content = system },
                    new { role = "user", content = user }
                }
            };
            req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var res = await LlmHttp.SendAsync(req);
            var body = await res.Content.ReadAsStringAsync();
            if (!res.IsSuccessStatusCode)
                return (false, "", $"No se pudo refinar el detalle ({(int)res.StatusCode}).");

            using var doc = JsonDocument.Parse(body);
            var content = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? "";
            content = Regex.Replace(content.Trim(), @"\s+", " ");
            if (content.Length < 40)
                return (false, "", "Detalle LLM demasiado corto; se mantiene el local.");
            return (true, Truncar(content, 700), "Detalle del caso refinado con LLM a partir del ticket y capturas.");
        }
        catch (Exception ex)
        {
            return (false, "", "Detalle LLM falló: " + ex.Message);
        }
    }

    private static string MimeImagen(string filename)
    {
        var ext = Path.GetExtension(filename ?? "").ToLowerInvariant();
        return ext switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            _ => "image/png"
        };
    }

    public static object InfoMemoria(string pruebasFeatures)
    {
        var cat = LeerCatalogo(pruebasFeatures);
        return new
        {
            ok = true,
            ejemplos = cat.Ejemplos.Count,
            recientes = cat.Ejemplos.Take(8).Select(e => new
            {
                e.Ticket,
                e.Titulo,
                e.Modulo,
                e.NombreArchivo,
                e.GuardadoUtc
            })
        };
    }

    private static CatalogoAprendizaje LeerCatalogo(string pruebasFeatures)
    {
        var path = RutaCatalogo(pruebasFeatures);
        if (!File.Exists(path)) return new CatalogoAprendizaje();
        try
        {
            var json = File.ReadAllText(path, Encoding.UTF8);
            return JsonSerializer.Deserialize<CatalogoAprendizaje>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new CatalogoAprendizaje();
        }
        catch
        {
            return new CatalogoAprendizaje();
        }
    }

    private static void GuardarCatalogo(string pruebasFeatures, CatalogoAprendizaje cat)
    {
        var path = RutaCatalogo(pruebasFeatures);
        var json = JsonSerializer.Serialize(cat, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json, Encoding.UTF8);
    }

    private static int ScoreSimilitud(EjemploAprendizaje e, string corpus, string? ticket, string? titulo)
    {
        var score = 0;
        if (!string.IsNullOrWhiteSpace(ticket) &&
            string.Equals(e.Ticket, ticket, StringComparison.OrdinalIgnoreCase))
            score += 50;

        var blob = (corpus + "\n" + (titulo ?? "")).ToLowerInvariant();
        foreach (var tok in Tokenizar((e.Titulo + " " + e.Detalle + " " + e.Ticket).ToLowerInvariant()))
        {
            if (tok.Length < 4) continue;
            if (blob.Contains(tok, StringComparison.Ordinal)) score += 2;
        }
        return score;
    }

    private static IEnumerable<string> Tokenizar(string s) =>
        Regex.Matches(s ?? "", @"[a-záéíóúñü0-9]{4,}")
            .Select(m => m.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase);

    private static string? ExtraerTicket(string texto)
    {
        var m = Regex.Match(texto ?? "", @"\b([A-Z][A-Z0-9]+-\d+)\b");
        return m.Success ? m.Groups[1].Value : null;
    }

    private static string ExtraerFeatureDeRespuestaLlm(string content)
    {
        var t = (content ?? "").Trim();
        var fence = Regex.Match(t, @"```(?:gherkin|feature)?\s*([\s\S]*?)```", RegexOptions.IgnoreCase);
        if (fence.Success) return fence.Groups[1].Value.Trim();
        var idx = t.IndexOf("Feature:", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0) return t[idx..].Trim();
        var idxTag = Regex.Match(t, @"@(?:Prueba|SC-\d+|CierreForzadoNota)", RegexOptions.IgnoreCase);
        if (idxTag.Success) return t[idxTag.Index..].Trim();
        return t;
    }

    private static string Truncar(string s, int max) =>
        string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max] + "…";
}
