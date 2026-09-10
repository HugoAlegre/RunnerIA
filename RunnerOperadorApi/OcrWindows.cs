using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

/// <summary>
/// OCR local (Windows.Media.Ocr) para capturas cuando no hay API key LLM.
/// Requiere TFM net9.0-windows10.0.19041.0+.
/// </summary>
public static class OcrWindows
{
    public static async Task<(bool Ok, string Texto, string Aviso)> ExtraerTextoAsync(byte[] bytes, string filename)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
            return (false, "", "OCR Windows requiere Windows 10 1809+.");

        if (bytes.Length == 0)
            return (false, "", $"Imagen «{filename}» vacía.");

        try
        {
            var engine = OcrEngine.TryCreateFromUserProfileLanguages()
                         ?? OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("es"))
                         ?? OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("es-ES"))
                         ?? OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("en"));
            if (engine is null)
                return (false, "", "No hay motor OCR de Windows disponible (idioma es/en).");

            using var softwareBitmap = await ToSoftwareBitmapAsync(bytes);
            if (softwareBitmap is null)
                return (false, "", $"No se pudo decodificar «{filename}» para OCR.");

            var result = await engine.RecognizeAsync(softwareBitmap);
            var text = (result?.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(text) || text.Length < 3)
                return (false, "", $"OCR sin texto útil en «{filename}» (¿captura borrosa o sin texto?).");

            text = System.Text.RegularExpressions.Regex.Replace(text, @"[ \t]+", " ");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\n{3,}", "\n\n");
            if (text.Length > 4000) text = text[..4000] + "…";
            return (true, text, "");
        }
        catch (Exception ex)
        {
            return (false, "", $"OCR falló en «{filename}»: {ex.Message}");
        }
    }

    private static async Task<SoftwareBitmap?> ToSoftwareBitmapAsync(byte[] bytes)
    {
        try
        {
            using var mem = new InMemoryRandomAccessStream();
            var writer = new DataWriter(mem);
            writer.WriteBytes(bytes);
            await writer.StoreAsync();
            await writer.FlushAsync();
            writer.DetachStream();
            mem.Seek(0);

            var decoder = await BitmapDecoder.CreateAsync(mem);
            return await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        }
        catch
        {
            return null;
        }
    }
}
