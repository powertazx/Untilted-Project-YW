using System;
using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace YoKaiWatchLauncher
{
    public partial class MainWindow : Window
    {
        private LauncherBridge? _bridge;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string dirHash = Convert.ToHexString(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(baseDir.ToLowerInvariant())));
                string userDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YoKaiWatchLauncher_WebView2", $"dir_{dirHash}");
                var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
                await MainWebView.EnsureCoreWebView2Async(env);

                // Disable default context menu in production
                MainWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;

                // Map local wwwroot to virtual HTTPS host: https://launcher.app
                string wwwroot = Path.Combine(baseDir, "wwwroot");
                if (!Directory.Exists(wwwroot))
                {
                    wwwroot = Path.Combine(baseDir, "..", "..", "..", "wwwroot");
                    if (!Directory.Exists(wwwroot))
                    {
                        wwwroot = @"D:\Yo-kai Watch 3 For PC\Launcher\wwwroot";
                    }
                }

                MainWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "launcher.app",
                    wwwroot,
                    CoreWebView2HostResourceAccessKind.Allow);

                // Register bridge object for Javascript
                _bridge = new LauncherBridge(this, MainWebView);
                MainWebView.CoreWebView2.AddHostObjectToScript("launcherBackend", _bridge);

                // Navigate to UI
                MainWebView.CoreWebView2.Navigate("https://launcher.app/index.html");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur d'initialisation du Launcher : {ex.Message}", "Erreur Launcher", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
