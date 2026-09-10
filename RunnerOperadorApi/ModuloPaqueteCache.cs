using System.Collections.Concurrent;

/// <summary>
/// Cache temporal de paquetes QA (Excel + ZIP) generados una sola vez para descargas GET rápidas.
/// </summary>
public static class ModuloPaqueteCache
{
    private static readonly ConcurrentDictionary<string, Entrada> _entradas = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(2);

    public static string Guardar(byte[] zipBytes, string zipNombre, byte[] excelBytes, string excelNombre)
    {
        PurgeVencidos();
        var token = Guid.NewGuid().ToString("N");
        var dir = Path.Combine(Path.GetTempPath(), "RunnerPaqueteQA", token);
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, zipNombre), zipBytes);
        File.WriteAllBytes(Path.Combine(dir, excelNombre), excelBytes);
        _entradas[token] = new Entrada
        {
            Directorio = dir,
            ZipNombre = zipNombre,
            ExcelNombre = excelNombre,
            CreadoUtc = DateTime.UtcNow
        };
        return token;
    }

    public static Entrada? Obtener(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        PurgeVencidos();
        return _entradas.TryGetValue(token, out var e) ? e : null;
    }

    public static void Eliminar(string token)
    {
        if (!_entradas.TryRemove(token, out var e)) return;
        try
        {
            if (Directory.Exists(e.Directorio))
                Directory.Delete(e.Directorio, recursive: true);
        }
        catch
        {
            /* no bloquea */
        }
    }

    private static void PurgeVencidos()
    {
        var limite = DateTime.UtcNow - Ttl;
        foreach (var kv in _entradas)
        {
            if (kv.Value.CreadoUtc >= limite) continue;
            Eliminar(kv.Key);
        }
    }

    public sealed class Entrada
    {
        public string Directorio { get; init; } = "";
        public string ZipNombre { get; init; } = "";
        public string ExcelNombre { get; init; } = "";
        public DateTime CreadoUtc { get; init; }
    }
}
