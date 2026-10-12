using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace YoKaiWatchLauncher
{
    public class LauncherConfig
    {
        public string? GameInstallPath { get; set; }
    }

    public static class LauncherConfigService
    {
        public static string GetConfigDirectory()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppData, "Yo-kai Launcher");
        }

        public static string GetConfigFilePath()
        {
            return Path.Combine(GetConfigDirectory(), "launcher_config.json");
        }

        public static string GetDefaultInstallRoot()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppData, "Yo-kai Launcher");
        }

        public static string GetDefaultGamePath()
        {
            return Path.Combine(GetDefaultInstallRoot(), "Yo-kai Watch 3 Ultimate");
        }

        public static LauncherConfig LoadConfig()
        {
            try
            {
                string path = GetConfigFilePath();
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    return JsonSerializer.Deserialize<LauncherConfig>(json) ?? new LauncherConfig();
                }
            }
            catch { }
            return new LauncherConfig();
        }

        public static void SaveConfig(LauncherConfig config)
        {
            try
            {
                string dir = GetConfigDirectory();
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(GetConfigFilePath(), json);
            }
            catch { }
        }

        public static string? ResolveInstalledGamePath()
        {
            var cfg = LoadConfig();
            if (!string.IsNullOrWhiteSpace(cfg.GameInstallPath))
            {
                if (File.Exists(Path.Combine(cfg.GameInstallPath, "yokaiwatch3.exe")))
                    return cfg.GameInstallPath;
                // le jeu est maintenant dans le sous-dossier "Yo-kai Watch 3 Release"
                string release = Path.Combine(cfg.GameInstallPath, "Yo-kai Watch 3 Release");
                if (File.Exists(Path.Combine(release, "yokaiwatch3.exe")))
                {
                    SaveConfig(new LauncherConfig { GameInstallPath = release });
                    return release;
                }
            }

            string def = GetDefaultGamePath();
            if (File.Exists(Path.Combine(def, "yokaiwatch3.exe")))
                return def;

            return null;
        }

        public static (long FreeBytes, long TotalBytes) GetDriveSpace(string path)
        {
            try
            {
                string root = Path.GetPathRoot(Path.GetFullPath(path)) ?? "C:\\";
                var drive = new DriveInfo(root);
                return (drive.AvailableFreeSpace, drive.TotalSize);
            }
            catch
            {
                return (0, 0);
            }
        }
    }
}
