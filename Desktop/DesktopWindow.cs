using System.Diagnostics;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace MigrateSQLServerDatabases.Desktop;

/// <summary>
/// Shows the app in its own window (WebView2, built into Windows 10/11) instead of a browser tab.
/// The web server runs in the same process on a local port and stops when the window closes.
/// </summary>
public static class DesktopWindow
{
    public const string Title = "SQL Server Manager";

    /// <summary>Blocks until the window is closed.</summary>
    public static void Run(string url)
    {
        var thread = new Thread(() =>
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using var form = new MainForm(url);
            Application.Run(form);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
    }

    public static void ShowError(string message)
    {
        var thread = new Thread(() => MessageBox.Show(message, Title, MessageBoxButtons.OK, MessageBoxIcon.Error));
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
    }

    private sealed class MainForm : Form
    {
        private readonly string url;
        private readonly WebView2 web = new() { Dock = DockStyle.Fill };

        public MainForm(string url)
        {
            this.url = url;
            Text = Title;
            Width = 1440;
            Height = 900;
            MinimumSize = new System.Drawing.Size(800, 520);
            StartPosition = FormStartPosition.CenterScreen;
            WindowState = FormWindowState.Maximized;
            try
            {
                Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
            }
            catch (Exception)
            {
            }

            Controls.Add(web);
            Load += async (_, _) => await StartAsync();
        }

        private async Task StartAsync()
        {
            try
            {
                // Keeps localStorage (language, theme, recent servers) in the user's profile, not next to the exe
                var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SQLServerManager", "WebView2");
                var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: data);
                await web.EnsureCoreWebView2Async(environment);

                var core = web.CoreWebView2;
                core.Settings.IsStatusBarEnabled = false;
                core.Settings.AreDevToolsEnabled = false;
                core.DocumentTitleChanged += (_, _) => Text = string.IsNullOrWhiteSpace(core.DocumentTitle) ? Title : core.DocumentTitle;
                // Links that try to open a new window go to the default browser
                core.NewWindowRequested += (_, e) =>
                {
                    e.Handled = true;
                    OpenInBrowser(e.Uri);
                };
                web.Source = new Uri(url);
            }
            catch (WebView2RuntimeNotFoundException)
            {
                // Very old Windows without the WebView2 runtime: use the browser and keep running until this window closes
                OpenInBrowser(url);
                Controls.Remove(web);
                Controls.Add(new Label
                {
                    Dock = DockStyle.Fill,
                    TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
                    Text = $"The app is open in your browser:\n{url}\n\nClose this window to stop it."
                });
                WindowState = FormWindowState.Normal;
                Width = 520;
                Height = 220;
            }
        }

        private static void OpenInBrowser(string target)
        {
            try
            {
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            }
            catch (Exception)
            {
            }
        }
    }
}
