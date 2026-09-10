using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace RunnerIA.Desktop;

internal sealed class MainForm : Form
{
    private readonly ServerHost _host;
    private readonly WebView2 _webView;
    private readonly Label _status;

    public MainForm(ServerHost host)
    {
        _host = host;
        Text = "RunnerIA";
        Width = 1280;
        Height = 800;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(900, 600);

        _status = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Text = "Cargando RunnerIA…",
            Font = new Font("Segoe UI", 12f)
        };

        _webView = new WebView2
        {
            Dock = DockStyle.Fill,
            Visible = false
        };

        Controls.Add(_webView);
        Controls.Add(_status);

        Shown += async (_, _) => await InitializeWebViewAsync();
        FormClosed += (_, _) => _host.Dispose();
    }

    private async Task InitializeWebViewAsync()
    {
        try
        {
            var userData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RunnerIA",
                "webview2");
            Directory.CreateDirectory(userData);

            var env = await CoreWebView2Environment.CreateAsync(userDataFolder: userData);
            await _webView.EnsureCoreWebView2Async(env);
            _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            _webView.CoreWebView2.Settings.AreDevToolsEnabled = true;
            _webView.CoreWebView2.Navigate(_host.Url);

            _status.Visible = false;
            _webView.Visible = true;
        }
        catch (WebView2RuntimeNotFoundException)
        {
            _status.Text =
                "Falta el runtime de Microsoft Edge WebView2.\n\n"
                + "Instalalo desde:\nhttps://developer.microsoft.com/microsoft-edge/webview2/\n\n"
                + "Luego volvé a abrir RunnerIA.exe";
            TryOpenInBrowserFallback();
        }
        catch (Exception ex)
        {
            _status.Text = "No se pudo cargar la interfaz.\n\n" + ex.Message;
            TryOpenInBrowserFallback();
        }
    }

    private void TryOpenInBrowserFallback()
    {
        var result = MessageBox.Show(
            this,
            "¿Abrir RunnerIA en el navegador mientras tanto?\n\n" + _host.Url,
            "RunnerIA",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);
        if (result == DialogResult.Yes)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = _host.Url,
                    UseShellExecute = true
                });
            }
            catch
            {
                /* ignore */
            }
        }
    }
}
