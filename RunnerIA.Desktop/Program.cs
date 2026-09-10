using System.Diagnostics;
using System.Net.Http;

namespace RunnerIA.Desktop;

internal static class Program
{
    public const string DefaultUrl = "http://localhost:5050/";
    public const int DefaultPort = 5050;

    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
            MessageBox.Show(e.Exception.Message, "RunnerIA", MessageBoxButtons.OK, MessageBoxIcon.Error);

        var portableRoot = ResolvePortableRoot();
        using var host = new ServerHost(portableRoot);
        try
        {
            host.EnsureStarted();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "No se pudo iniciar el servidor interno de RunnerIA.\n\n" + ex.Message,
                "RunnerIA",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        Application.Run(new MainForm(host));
    }

    /// <summary>
    /// Raíz del portable (donde están app/, wwwroot/) o carpeta del exe Desktop.
    /// </summary>
    internal static string ResolvePortableRoot()
    {
        var baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        // Publish: .../RunnerIA-win-x64/RunnerIA.exe  → raíz = esa carpeta
        if (Directory.Exists(Path.Combine(baseDir, "app")) && Directory.Exists(Path.Combine(baseDir, "wwwroot")))
            return baseDir;

        // Dev: .../bin/.../net9.0-windows → subir hasta encontrar app/ o el repo
        var dir = new DirectoryInfo(baseDir);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "app"))
                && Directory.Exists(Path.Combine(dir.FullName, "wwwroot")))
                return dir.FullName;

            var apiProj = Path.Combine(dir.FullName, "RunnerOperadorApi", "RunnerOperadorApi.csproj");
            if (File.Exists(apiProj))
                return dir.FullName;

            dir = dir.Parent;
        }

        return baseDir;
    }
}

/// <summary>Arranca o reutiliza la API local (app/RunnerIA.Server.exe o app/RunnerIA.exe).</summary>
internal sealed class ServerHost : IDisposable
{
    private readonly string _portableRoot;
    private Process? _process;
    private bool _startedByUs;

    public ServerHost(string portableRoot)
    {
        _portableRoot = portableRoot;
        Url = Program.DefaultUrl;
    }

    public string Url { get; }

    public void EnsureStarted()
    {
        if (IsHealthy())
            return;

        var exe = FindServerExe();
        if (exe is null)
        {
            throw new FileNotFoundException(
                "No se encontró el servidor.\nBuscado: app\\RunnerIA.Server.exe o app\\RunnerIA.exe\n"
                + "Carpeta: " + _portableRoot);
        }

        var appDir = Path.GetDirectoryName(exe)!;
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            WorkingDirectory = appDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        psi.Environment["ASPNETCORE_URLS"] = $"http://localhost:{Program.DefaultPort}";
        psi.Environment["ASPNETCORE_CONTENTROOT"] = appDir;

        var suite = ResolveSuiteRoot(_portableRoot);
        if (suite is not null)
            psi.Environment["AutomatizacionSOT_ROOT"] = suite;

        _process = Process.Start(psi)
            ?? throw new InvalidOperationException("No se pudo lanzar el proceso del servidor.");
        _startedByUs = true;

        if (!WaitForHealthy(TimeSpan.FromSeconds(45)))
        {
            try { _process.Kill(entireProcessTree: true); } catch { /* ignore */ }
            throw new TimeoutException(
                "El servidor no respondió en /health a tiempo.\nURL: " + Url);
        }
    }

    public void Dispose()
    {
        if (!_startedByUs || _process is null)
            return;

        try
        {
            if (!_process.HasExited)
                _process.Kill(entireProcessTree: true);
        }
        catch
        {
            /* ignore */
        }
        finally
        {
            _process.Dispose();
            _process = null;
        }
    }

    private static string? ResolveSuiteRoot(string portableRoot)
    {
        var candidates = new[]
        {
            Path.Combine(portableRoot, "..", "AutomatizacionSOT", "AutomatizacionSOT"),
            Path.Combine(portableRoot, "..", "AutomatizacionSOT"),
            Path.Combine(portableRoot, "AutomatizacionSOT", "AutomatizacionSOT"),
            Path.Combine(portableRoot, "AutomatizacionSOT")
        };
        foreach (var c in candidates)
        {
            var full = Path.GetFullPath(c);
            if (File.Exists(Path.Combine(full, "AutomatizacionSOT.csproj")))
                return full;
        }
        return null;
    }

    private string? FindServerExe()
    {
        var app = Path.Combine(_portableRoot, "app");
        var server = Path.Combine(app, "RunnerIA.Server.exe");
        if (File.Exists(server)) return server;
        var legacy = Path.Combine(app, "RunnerIA.exe");
        if (File.Exists(legacy)) return legacy;
        return null;
    }

    private static bool IsHealthy()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var json = http.GetStringAsync(Program.DefaultUrl.TrimEnd('/') + "/health").GetAwaiter().GetResult();
            return json.Contains("\"ok\":true", StringComparison.OrdinalIgnoreCase)
                   || json.Contains("\"ok\": true", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool WaitForHealthy(TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            if (IsHealthy()) return true;
            Thread.Sleep(400);
        }
        return false;
    }
}
