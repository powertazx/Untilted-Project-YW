using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Wpf;

namespace YoKaiWatchLauncher
{
    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.AutoDual)]
    public class LauncherBridge
    {
        private readonly MainWindow _mainWindow;
        private readonly WebView2 _webView;
        private string _gameDir = "";
        private string _selectedCharacterId = "nathan";
        private string _selectedCharacterName = "Nathan Adams";

        public LauncherBridge(MainWindow mainWindow, WebView2 webView)
        {
            _mainWindow = mainWindow;
            _webView = webView;

            // Resolve installed game path from configuration
            string? installed = LauncherConfigService.ResolveInstalledGamePath();
            _gameDir = installed ?? "";

            // Register LobbyManager events
            LobbyManager.Instance.RoomUpdated += OnRoomUpdated;
            LobbyManager.Instance.LogMessageReceived += OnLogMessageReceived;
            LobbyManager.Instance.GameStartTriggered += OnGameStartTriggered;
        }

        #region Game Status and Installation API

        private const string GameVersionFile = "game_version.txt";

        /// la version du jeu installé (game_version.txt dans le dossier du jeu), "1.0.0" sans fichier.
        public string GetGameVersion()
        {
            try
            {
                string path = Path.Combine(_gameDir, GameVersionFile);
                if (!string.IsNullOrWhiteSpace(_gameDir) && File.Exists(path))
                {
                    string v = File.ReadAllText(path).Trim();
                    if (v.Length > 0) return v;
                }
            }
            catch { }
            return "1.0.0";
        }

        /// télécharge la mise à jour du jeu et l'installe dans le dossier du jeu :
        /// un .zip est extrait par-dessus (mêmes chemins relatifs), un .exe remplace
        /// yokaiwatch3.exe, un .ywpatch va dans 00040000001D6900. Puis la version est notée.
        public async Task<string> UpdateGame(string url, string version)
        {
            if (string.IsNullOrWhiteSpace(_gameDir) || !Directory.Exists(_gameDir))
                return JsonSerializer.Serialize(new { success = false, message = "Jeu non installé." });
            string tmp = Path.Combine(Path.GetTempPath(), "yw3_update_" + Guid.NewGuid().ToString("N"));
            try
            {
                using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromHours(2) };
                using var res = await http.GetAsync(url, System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
                res.EnsureSuccessStatusCode();
                string name = res.Content.Headers.ContentDisposition?.FileNameStar?.Trim('"')
                    ?? res.Content.Headers.ContentDisposition?.FileName?.Trim('"')
                    ?? Path.GetFileName(new Uri(url).LocalPath);
                long total = res.Content.Headers.ContentLength ?? -1;
                await using (var input = await res.Content.ReadAsStreamAsync())
                await using (var output = File.Create(tmp))
                {
                    var buffer = new byte[1 << 16];
                    long done = 0; int read, last = -1;
                    while ((read = await input.ReadAsync(buffer)) > 0)
                    {
                        await output.WriteAsync(buffer.AsMemory(0, read));
                        done += read;
                        int percent = total > 0 ? (int)(done * 100 / total) : -1;
                        if (percent != last)
                        {
                            last = percent;
                            long mb = done / (1024 * 1024);
                            _mainWindow.Dispatcher.Invoke(() => _webView.CoreWebView2?.ExecuteScriptAsync($"if(window.onGameUpdateProgress) window.onGameUpdateProgress({percent}, {mb});"));
                        }
                    }
                }

                string lower = name.ToLowerInvariant();
                bool isZip;
                using (var f = File.OpenRead(tmp)) isZip = f.ReadByte() == 'P' && f.ReadByte() == 'K';
                if (isZip)
                    await Task.Run(() => System.IO.Compression.ZipFile.ExtractToDirectory(tmp, _gameDir, true));
                else if (lower.EndsWith(".exe"))
                    File.Copy(tmp, Path.Combine(_gameDir, "yokaiwatch3.exe"), true);
                else if (lower.EndsWith(".ywpatch"))
                    File.Copy(tmp, Path.Combine(_gameDir, "00040000001D6900", name), true);
                else
                    return JsonSerializer.Serialize(new { success = false, message = $"Format de mise à jour inconnu ({name}) : utilisez un .zip, un .exe ou un .ywpatch." });

                File.WriteAllText(Path.Combine(_gameDir, GameVersionFile), version.Trim());
                return JsonSerializer.Serialize(new { success = true, version = version.Trim() });
            }
            catch (Exception ex)
            {
                return JsonSerializer.Serialize(new { success = false, message = ex.Message });
            }
            finally
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            }
        }
        public string GetGameStatus()
        {
            string? installedPath = LauncherConfigService.ResolveInstalledGamePath();
            bool isInstalled = !string.IsNullOrEmpty(installedPath) && File.Exists(Path.Combine(installedPath, "yokaiwatch3.exe"));
            if (isInstalled) _gameDir = installedPath!;

            string defaultRoot = LauncherConfigService.GetDefaultInstallRoot();
            string defaultGameDir = LauncherConfigService.GetDefaultGamePath();
            string username = Environment.UserName;

            var (freeBytes, totalBytes) = LauncherConfigService.GetDriveSpace(defaultRoot);
            double freeGb = Math.Round(freeBytes / (1024.0 * 1024.0 * 1024.0), 2);
            double totalGb = Math.Round(totalBytes / (1024.0 * 1024.0 * 1024.0), 2);

            return JsonSerializer.Serialize(new
            {
                installed = isInstalled,
                gameDir = isInstalled ? installedPath : "",
                defaultInstallRoot = defaultRoot,
                defaultGameDir = defaultGameDir,
                targetSubfolder = "Yo-kai Watch 3 Ultimate",
                username = username,
                freeGb = freeGb,
                totalGb = totalGb,
                requiredGb = 3.61
            });
        }

        public string CheckFolderDriveSpace(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
                folderPath = LauncherConfigService.GetDefaultInstallRoot();

            var (freeBytes, totalBytes) = LauncherConfigService.GetDriveSpace(folderPath);
            double freeGb = Math.Round(freeBytes / (1024.0 * 1024.0 * 1024.0), 2);
            double totalGb = Math.Round(totalBytes / (1024.0 * 1024.0 * 1024.0), 2);
            bool hasEnough = freeGb >= 3.61;

            return JsonSerializer.Serialize(new
            {
                freeGb,
                totalGb,
                requiredGb = 3.61,
                hasEnough
            });
        }

        public string BrowseFolder(string initialFolder)
        {
            string selected = "";
            _mainWindow.Dispatcher.Invoke(() =>
            {
                try
                {
                    var dialog = new Microsoft.Win32.OpenFolderDialog
                    {
                        Title = "Sélectionnez le dossier où installer Yo-kai Launcher",
                        InitialDirectory = Directory.Exists(initialFolder) ? initialFolder : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
                    };
                    if (dialog.ShowDialog(_mainWindow) == true)
                    {
                        selected = dialog.FolderName;
                    }
                }
                catch { }
            });
            return selected;
        }

        public string BrowseGameExe()
        {
            string selected = "";
            _mainWindow.Dispatcher.Invoke(() =>
            {
                try
                {
                    var dialog = new Microsoft.Win32.OpenFileDialog
                    {
                        Title = "Sélectionnez yokaiwatch3.exe",
                        Filter = "Exécutable Yo-kai Watch 3 (yokaiwatch3.exe)|yokaiwatch3.exe|Tous les fichiers (*.*)|*.*",
                        InitialDirectory = @"D:\Yo-kai Watch 3 For PC\Yo-kai Watch 3 Ultimate PC\Yo-kai Watch 3 Release"
                    };
                    if (dialog.ShowDialog(_mainWindow) == true)
                    {
                        string dir = Path.GetDirectoryName(dialog.FileName) ?? "";
                        if (File.Exists(Path.Combine(dir, "yokaiwatch3.exe")))
                        {
                            selected = dir;
                        }
                    }
                }
                catch { }
            });
            return selected;
        }

        public string LocateExistingGame(string gameFolder)
        {
            if (string.IsNullOrWhiteSpace(gameFolder) || !File.Exists(Path.Combine(gameFolder, "yokaiwatch3.exe")))
            {
                return JsonSerializer.Serialize(new { success = false, message = "Fichier yokaiwatch3.exe introuvable dans ce dossier !" });
            }

            LauncherConfigService.SaveConfig(new LauncherConfig { GameInstallPath = gameFolder });
            _gameDir = gameFolder;
            return JsonSerializer.Serialize(new { success = true, gameDir = gameFolder });
        }

        public async Task<string> InstallGame(string targetRootFolder)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(targetRootFolder))
                    targetRootFolder = LauncherConfigService.GetDefaultInstallRoot();

                string targetGameDir = Path.Combine(targetRootFolder, "Yo-kai Watch 3 Ultimate");
                string? sourceDir = FindGameSourceDirectory();

                if (string.IsNullOrEmpty(sourceDir))
                {
                    return JsonSerializer.Serialize(new { success = false, message = "Dossier source du jeu introuvable sur cette machine." });
                }

                if (Path.GetFullPath(sourceDir).Equals(Path.GetFullPath(targetGameDir), StringComparison.OrdinalIgnoreCase))
                {
                    LauncherConfigService.SaveConfig(new LauncherConfig { GameInstallPath = targetGameDir });
                    _gameDir = targetGameDir;
                    return JsonSerializer.Serialize(new { success = true, gameDir = targetGameDir });
                }

                // Check free space on chosen drive
                var (freeBytes, _) = LauncherConfigService.GetDriveSpace(targetRootFolder);
                long requiredBytes = (long)(3.61 * 1024 * 1024 * 1024);
                if (freeBytes < requiredBytes)
                {
                    double freeGb = Math.Round(freeBytes / (1024.0 * 1024.0 * 1024.0), 2);
                    return JsonSerializer.Serialize(new
                    {
                        success = false,
                        message = $"Espace disque insuffisant sur ce lecteur ({freeGb} Go libres / 3.6 Go requis). Veuillez choisir un autre disque ou libérer de l'espace."
                    });
                }

                Directory.CreateDirectory(targetGameDir);
                var filesToCopy = new List<(string Source, string Dest)>();

                string[] imperativeFiles = { "yokaiwatch3.exe", "code.bin", "discord_rpc.json", "icon.ico", "icon.png", "Lancer Yo-kai Watch 3.bat", "multiplayer_session.json" };
                foreach (var f in imperativeFiles)
                {
                    string s = Path.Combine(sourceDir, f);
                    if (File.Exists(s)) filesToCopy.Add((s, Path.Combine(targetGameDir, f)));
                }

                string[] imperativeDirs = { "00040000001D6900", "runtime", "saves", "mods", "discord_assets" };
                foreach (var d in imperativeDirs)
                {
                    string sDir = Path.Combine(sourceDir, d);
                    if (Directory.Exists(sDir))
                    {
                        foreach (string file in Directory.EnumerateFiles(sDir, "*", SearchOption.AllDirectories))
                        {
                            string rel = Path.GetRelativePath(sourceDir, file);
                            filesToCopy.Add((file, Path.Combine(targetGameDir, rel)));
                        }
                    }
                }

                long totalBytes = 0;
                foreach (var item in filesToCopy)
                {
                    try { totalBytes += new FileInfo(item.Source).Length; } catch { }
                }

                long copiedBytes = 0;
                byte[] buffer = new byte[1024 * 1024]; // 1MB buffer
                long lastReportedTime = 0;

                for (int i = 0; i < filesToCopy.Count; i++)
                {
                    var (src, dst) = filesToCopy[i];
                    string? parentDir = Path.GetDirectoryName(dst);
                    if (parentDir != null && !Directory.Exists(parentDir)) Directory.CreateDirectory(parentDir);

                    using var inStream = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.Read);
                    using var outStream = new FileStream(dst, FileMode.Create, FileAccess.Write, FileShare.None);

                    int bytesRead;
                    while ((bytesRead = await inStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await outStream.WriteAsync(buffer, 0, bytesRead);
                        copiedBytes += bytesRead;

                        long now = Environment.TickCount64;
                        if (now - lastReportedTime > 120 || copiedBytes == totalBytes)
                        {
                            lastReportedTime = now;
                            double percent = totalBytes > 0 ? Math.Round((double)copiedBytes / totalBytes * 100.0, 1) : 0;
                            string fileName = Path.GetFileName(src);
                            double copiedMb = Math.Round(copiedBytes / (1024.0 * 1024.0), 1);
                            double totalMb = Math.Round(totalBytes / (1024.0 * 1024.0), 1);

                            _mainWindow.Dispatcher.Invoke(() =>
                            {
                                _webView.CoreWebView2?.ExecuteScriptAsync($"if(window.onInstallProgress) window.onInstallProgress({percent}, '{EscapeJs(fileName)}', {copiedMb}, {totalMb});");
                            });
                        }
                    }
                }

                LauncherConfigService.SaveConfig(new LauncherConfig { GameInstallPath = targetGameDir });
                _gameDir = targetGameDir;

                _mainWindow.Dispatcher.Invoke(() =>
                {
                    _webView.CoreWebView2?.ExecuteScriptAsync($"if(window.onInstallComplete) window.onInstallComplete('{EscapeJs(targetGameDir)}');");
                });

                return JsonSerializer.Serialize(new { success = true, gameDir = targetGameDir });
            }
            catch (Exception ex)
            {
                return JsonSerializer.Serialize(new { success = false, message = ex.Message });
            }
        }

        private string? FindGameSourceDirectory()
        {
            string known = @"D:\Yo-kai Watch 3 For PC\Yo-kai Watch 3 Ultimate PC\Yo-kai Watch 3 Release";
            if (File.Exists(Path.Combine(known, "yokaiwatch3.exe")))
                return known;

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var dir = new DirectoryInfo(baseDir);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "yokaiwatch3.exe")))
                    return dir.FullName;
                if (File.Exists(Path.Combine(dir.FullName, "Yo-kai Watch 3 Release", "yokaiwatch3.exe")))
                    return Path.Combine(dir.FullName, "Yo-kai Watch 3 Release");
                if (Directory.Exists(Path.Combine(dir.FullName, "Yo-kai Watch 3 Ultimate PC")) &&
                    File.Exists(Path.Combine(dir.FullName, "Yo-kai Watch 3 Ultimate PC", "yokaiwatch3.exe")))
                    return Path.Combine(dir.FullName, "Yo-kai Watch 3 Ultimate PC");
                dir = dir.Parent;
            }
            return null;
        }

        private static string EscapeJs(string s) => s.Replace("\\", "\\\\").Replace("'", "\\'");
        #endregion

        public string GetSettings()
        {
            return ZakuroSettingsService.ReadSettingsJson(_gameDir);
        }

        public bool SaveSettings(string json)
        {
            return ZakuroSettingsService.SaveSettingsJson(json, _gameDir);
        }

        public bool LaunchGame(string gameId, bool isMultiplayer)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_gameDir) || !File.Exists(Path.Combine(_gameDir, "yokaiwatch3.exe")))
                {
                    MessageBox.Show("Yo-kai Watch 3 n'est pas encore installé. Veuillez cliquer sur 'INSTALLER' pour copier les fichiers du jeu.", "Jeu non installé", MessageBoxButton.OK, MessageBoxImage.Information);
                    return false;
                }

                string exePath = Path.Combine(_gameDir, "yokaiwatch3.exe");
                string romPath = Path.Combine(_gameDir, "Yo-Kai Watch 3 (France).3ds");
                if (!File.Exists(romPath))
                {
                    string fallbackRom = @"D:\Yo-kai Watch 3 For PC\Yo-kai Watch 3 Ultimate PC\Yo-kai Watch 3 Release\Yo-Kai Watch 3 (France).3ds";
                    if (File.Exists(fallbackRom)) romPath = fallbackRom;
                }
                string savesDir = Path.Combine(_gameDir, "saves");

                if (!File.Exists(exePath))
                {
                    // Fallback to bat
                    string batPath = Path.Combine(_gameDir, "Lancer Yo-kai Watch 3 (Port Natif Recompile).bat");
                    if (File.Exists(batPath))
                    {
                        var psiBat = new ProcessStartInfo
                        {
                            FileName = batPath,
                            WorkingDirectory = _gameDir,
                            UseShellExecute = true
                        };
                        Process.Start(psiBat);
                        return true;
                    }
                    MessageBox.Show($"Fichier yokaiwatch3.exe introuvable dans {_gameDir}", "Erreur Lancement", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }

                // Write session and character choice config (for in-game model swap & multiplayer)
                WriteMultiplayerConfig();

                string args = $"\"{romPath}\" --dual-screen --fullscreen --data \"{savesDir}\"";

                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = args,
                    WorkingDirectory = _gameDir,
                    UseShellExecute = false
                };

                psi.EnvironmentVariables["YW3_DUAL_SCREEN"] = "1";
                psi.EnvironmentVariables["NO_DIRECTOR"] = "1";

                try
                {
                    string settingsJson = ZakuroSettingsService.ReadSettingsJson(_gameDir);
                    if (settingsJson.Contains("\"target_fps\": 120"))
                    {
                        psi.EnvironmentVariables["ZAKURO_PRESENT_MODE"] = "mailbox";
                    }
                }
                catch { }

                Process.Start(psi);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors du lancement : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private void WriteMultiplayerConfig()
        {
            try
            {
                var room = LobbyManager.Instance.CurrentRoom;
                var local = LobbyManager.Instance.LocalPlayer;

                string charId = !string.IsNullOrWhiteSpace(local?.CharacterId) ? local.CharacterId : (_selectedCharacterId ?? "nathan");
                string charName = !string.IsNullOrWhiteSpace(local?.CharacterName) ? local.CharacterName : (_selectedCharacterName ?? "Nathan Adams");
                string playerName = local?.Name ?? "Joueur";

                string savesDir = Path.Combine(_gameDir, "saves");
                if (!Directory.Exists(savesDir)) Directory.CreateDirectory(savesDir);

                string serverAddress = LobbyManager.Instance.IsHost ? "127.0.0.1" : (LobbyManager.Instance.ConnectedHostAddress ?? "127.0.0.1");

                var allPlayers = new List<PlayerSlot>();
                if (room?.Players != null && room.Players.Count > 0)
                {
                    allPlayers.AddRange(room.Players);
                }
                else if (local != null)
                {
                    allPlayers.Add(local);
                }

                var cfg = new MultiplayerSessionConfig
                {
                    Enabled = true,
                    RoomCode = room?.Code ?? "SOLO",
                    IsHost = LobbyManager.Instance.IsHost,
                    PlayerId = !string.IsNullOrEmpty(local?.Id) ? local.Id : ("player_" + Guid.NewGuid().ToString("N")[..8]),
                    PlayerName = playerName,
                    CharacterId = charId,
                    CharacterName = charName,
                    ServerAddress = serverAddress,
                    UdpPort = 7778,
                    Players = allPlayers
                };

                string json = JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(Path.Combine(savesDir, "multiplayer_session.json"), json);
                File.WriteAllText(Path.Combine(_gameDir, "multiplayer_session.json"), json);
            }
            catch { }
        }

        public void OpenFolder(string type)
        {
            try
            {
                string target = type switch
                {
                    "saves" => Path.Combine(_gameDir, "saves"),
                    "mods" => Path.Combine(_gameDir, "mods"),
                    _ => _gameDir
                };

                if (Directory.Exists(target))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"\"{target}\"",
                        UseShellExecute = true
                    });
                }
            }
            catch { }
        }

        public void OpenExternalUrl(string url)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(url) && (url.StartsWith("http://") || url.StartsWith("https://")))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = url,
                        UseShellExecute = true
                    });
                }
            }
            catch { }
        }

        public void LaunchStudio()
        {
            try
            {
                string studioBat = Path.Combine(_gameDir, "Lancer Editeur Yo-kai Watch 3 Ultimate.bat");
                if (File.Exists(studioBat))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = studioBat,
                        WorkingDirectory = _gameDir,
                        UseShellExecute = true
                    });
                }
                else
                {
                    string studioExe = Path.Combine(_gameDir, "Studio", "Yo-kai Watch 3 Ultimate Studio.exe");
                    if (File.Exists(studioExe))
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = studioExe,
                            WorkingDirectory = Path.Combine(_gameDir, "Studio"),
                            UseShellExecute = true
                        });
                    }
                }
            }
            catch { }
        }

        #region Multiplayer Bridge API
        public string GetHostIp()
        {
            return LobbyManager.GetPrimaryLanIp();
        }

        public async Task<string> CreateRoom(string playerName, string charId, string charName, int port)
        {
            try
            {
                _selectedCharacterId = charId;
                _selectedCharacterName = charName;
                int p = port <= 0 ? 7777 : port;
                string code = await LobbyManager.Instance.CreateRoomAsync(playerName, charId, charName, p);
                WriteMultiplayerConfig();
                string hostIp = LobbyManager.GetPrimaryLanIp();
                return JsonSerializer.Serialize(new { success = true, code, hostIp, port = p });
            }
            catch (Exception ex)
            {
                return JsonSerializer.Serialize(new { success = false, error = ex.Message });
            }
        }

        public async Task<string> JoinRoom(string code, string playerName, string charId, string charName, string hostIp, int port)
        {
            try
            {
                _selectedCharacterId = charId;
                _selectedCharacterName = charName;
                int p = port <= 0 ? 7777 : port;
                var (ok, errorMsg, room) = await LobbyManager.Instance.JoinRoomAsync(code, playerName, charId, charName, hostIp, p);
                if (ok && room != null)
                {
                    WriteMultiplayerConfig();
                    return JsonSerializer.Serialize(new { success = true, code, hostIp = room.HostIp, port = room.Port, room });
                }
                else
                {
                    return JsonSerializer.Serialize(new { success = false, error = string.IsNullOrEmpty(errorMsg) ? "Impossible de rejoindre le salon." : errorMsg });
                }
            }
            catch (Exception ex)
            {
                return JsonSerializer.Serialize(new { success = false, error = ex.Message });
            }
        }

        public async Task<bool> SelectCharacter(string charId, string charName)
        {
            _selectedCharacterId = charId;
            _selectedCharacterName = charName;
            await LobbyManager.Instance.SelectCharacterAsync(charId, charName);
            WriteMultiplayerConfig();
            return true;
        }

        public void StartMultiplayerSession()
        {
            LobbyManager.Instance.StartGameSession();
        }

        public void LeaveRoom()
        {
            LobbyManager.Instance.LeaveRoom();
        }

        public string GetRoomState()
        {
            var room = LobbyManager.Instance.CurrentRoom;
            var local = LobbyManager.Instance.LocalPlayer;
            return JsonSerializer.Serialize(new
            {
                inRoom = room != null,
                isHost = LobbyManager.Instance.IsHost,
                room,
                localPlayer = local
            });
        }
        #endregion

        #region Event Callbacks to UI
        private void OnRoomUpdated(LobbyRoom? room)
        {
            _mainWindow.Dispatcher.Invoke(() =>
            {
                try
                {
                    if (room == null)
                    {
                        _webView.CoreWebView2?.ExecuteScriptAsync("if(window.onRoomLeft) window.onRoomLeft();");
                        return;
                    }

                    WriteMultiplayerConfig();

                    string json = JsonSerializer.Serialize(new
                    {
                        room,
                        localPlayer = LobbyManager.Instance.LocalPlayer,
                        isHost = LobbyManager.Instance.IsHost
                    });
                    _webView.CoreWebView2?.ExecuteScriptAsync($"if(window.onRoomUpdated) window.onRoomUpdated({json});");
                }
                catch { }
            });
        }

        private void OnLogMessageReceived(string msg)
        {
            _mainWindow.Dispatcher.Invoke(() =>
            {
                try
                {
                    string safeMsg = JsonSerializer.Serialize(msg);
                    _webView.CoreWebView2?.ExecuteScriptAsync($"if(window.onLobbyLog) window.onLobbyLog({safeMsg});");
                }
                catch { }
            });
        }

        private void OnGameStartTriggered(string code)
        {
            _mainWindow.Dispatcher.Invoke(() =>
            {
                try
                {
                    _webView.CoreWebView2?.ExecuteScriptAsync($"if(window.onGameStarted) window.onGameStarted('{code}');");
                    // Automatically launch the game
                    LaunchGame("yw3", true);
                }
                catch { }
            });
        }
        #endregion
    }
}
