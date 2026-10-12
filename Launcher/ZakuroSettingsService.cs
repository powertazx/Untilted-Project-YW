using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace YoKaiWatchLauncher
{
    public class ZakuroSettingsService
    {
        public static string GetSettingsPath(string? gameDir = null)
        {
            if (!string.IsNullOrEmpty(gameDir))
            {
                string savesPath = Path.Combine(gameDir, "saves", "settings.toml");
                if (File.Exists(savesPath)) return savesPath;
                string rootPath = Path.Combine(gameDir, "settings.toml");
                if (File.Exists(rootPath)) return rootPath;
                return savesPath;
            }
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "zakuro", "settings.toml");
        }

        public static string ReadSettingsJson(string? gameDir = null)
        {
            string path = GetSettingsPath(gameDir);
            if (!File.Exists(path))
            {
                string global = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "zakuro", "settings.toml");
                if (File.Exists(global)) path = global;
            }
            var dict = new Dictionary<string, object>();
            var keysDict = new Dictionary<string, string>();
            var padDict = new Dictionary<string, string>();

            if (File.Exists(path))
            {
                string[] lines = File.ReadAllLines(path);
                string currentSection = "root";

                foreach (string rawLine in lines)
                {
                    string line = rawLine.Trim();
                    if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;

                    if (line.StartsWith("[") && line.EndsWith("]"))
                    {
                        currentSection = line[1..^1].Trim();
                        continue;
                    }

                    int eqIdx = line.IndexOf('=');
                    if (eqIdx > 0)
                    {
                        string k = line[..eqIdx].Trim();
                        string v = line[(eqIdx + 1)..].Trim().Trim('"');

                        if (currentSection == "keys")
                        {
                            keysDict[k] = v;
                        }
                        else if (currentSection == "pad")
                        {
                            padDict[k] = v;
                        }
                        else if (currentSection == "root")
                        {
                            if (bool.TryParse(v, out bool bVal)) dict[k] = bVal;
                            else if (int.TryParse(v, out int iVal)) dict[k] = iVal;
                            else if (float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float fVal)) dict[k] = fVal;
                            else dict[k] = v;
                        }
                    }
                }
            }
            else
            {
                // Default settings if file does not exist
                dict["renderer"] = "vulkan";
                dict["hardware_rasterizer"] = true;
                dict["resolution"] = 4;
                dict["scale"] = 4;
                dict["layout"] = "stacked";
                dict["fullscreen"] = true;
                dict["stretch"] = true;
                dict["volume"] = 1.0f;
                dict["show_fps"] = true;

                keysDict["a"] = "KeyF";
                keysDict["b"] = "ShiftLeft";
                keysDict["x"] = "KeyX";
                keysDict["y"] = "KeyY";
                keysDict["l"] = "KeyQ";
                keysDict["r"] = "KeyE";
                keysDict["start"] = "Enter";
                keysDict["select"] = "Backspace";
                keysDict["up"] = "KeyW";
                keysDict["down"] = "KeyS";
                keysDict["left"] = "KeyA";
                keysDict["right"] = "KeyD";
                keysDict["circle_up"] = "KeyW";
                keysDict["circle_down"] = "KeyS";
                keysDict["circle_left"] = "KeyA";
                keysDict["circle_right"] = "KeyD";

                padDict["a"] = "East";
                padDict["b"] = "South";
                padDict["x"] = "North";
                padDict["y"] = "West";
                padDict["l"] = "LeftTrigger";
                padDict["r"] = "RightTrigger";
                padDict["start"] = "Start";
                padDict["select"] = "Select";
                padDict["up"] = "DPadUp";
                padDict["down"] = "DPadDown";
                padDict["left"] = "DPadLeft";
                padDict["right"] = "DPadRight";
            }

            if (!dict.ContainsKey("target_fps"))
            {
                dict["target_fps"] = 60;
            }

            dict["keys"] = keysDict;
            dict["pad"] = padDict;

            return JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true });
        }

        public static bool SaveSettingsJson(string json, string? gameDir = null)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                string path = GetSettingsPath(gameDir);

                string? dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var sb = new StringBuilder();

                // Write root settings
                sb.AppendLine("# Yo-kai Watch 3 Ultimate PC Settings (Configuré via Launcher)");
                if (root.TryGetProperty("renderer", out var renderer))
                    sb.AppendLine($"renderer = \"{renderer.GetString()}\"");
                if (root.TryGetProperty("hardware_rasterizer", out var hr))
                    sb.AppendLine($"hardware_rasterizer = {hr.GetBoolean().ToString().ToLowerInvariant()}");
                if (root.TryGetProperty("resolution", out var res))
                    sb.AppendLine($"resolution = {res.GetInt32()}");
                if (root.TryGetProperty("recompiled", out var recomp))
                    sb.AppendLine($"recompiled = {recomp.GetBoolean().ToString().ToLowerInvariant()}");
                else
                    sb.AppendLine("recompiled = true");
                if (root.TryGetProperty("scale", out var scale))
                    sb.AppendLine($"scale = {scale.GetInt32()}");
                if (root.TryGetProperty("layout", out var layout))
                    sb.AppendLine($"layout = \"{layout.GetString()}\"");
                else
                    sb.AppendLine("layout = \"stacked\"");
                if (root.TryGetProperty("fullscreen", out var full))
                    sb.AppendLine($"fullscreen = {full.GetBoolean().ToString().ToLowerInvariant()}");
                if (root.TryGetProperty("stretch", out var stretch))
                    sb.AppendLine($"stretch = {stretch.GetBoolean().ToString().ToLowerInvariant()}");
                if (root.TryGetProperty("volume", out var vol))
                    sb.AppendLine($"volume = {vol.GetSingle().ToString(System.Globalization.CultureInfo.InvariantCulture)}");
                if (root.TryGetProperty("mute", out var mute))
                    sb.AppendLine($"mute = {mute.GetBoolean().ToString().ToLowerInvariant()}");
                if (root.TryGetProperty("show_fps", out var fps))
                    sb.AppendLine($"show_fps = {fps.GetBoolean().ToString().ToLowerInvariant()}");
                if (root.TryGetProperty("target_fps", out var tfps))
                    sb.AppendLine($"target_fps = {tfps.GetInt32()}");
                else if (root.TryGetProperty("fps", out var fpsProp))
                    sb.AppendLine($"target_fps = {fpsProp.GetInt32()}");
                else
                    sb.AppendLine("target_fps = 60");
                if (root.TryGetProperty("background_opacity", out var bgo))
                    sb.AppendLine($"background_opacity = {bgo.GetSingle().ToString(System.Globalization.CultureInfo.InvariantCulture)}");

                sb.AppendLine();
                sb.AppendLine("[keys]");
                if (root.TryGetProperty("keys", out var keysElem))
                {
                    foreach (var prop in keysElem.EnumerateObject())
                    {
                        sb.AppendLine($"{prop.Name} = \"{prop.Value.GetString()}\"");
                    }
                }

                sb.AppendLine();
                sb.AppendLine("[pad]");
                if (root.TryGetProperty("pad", out var padElem))
                {
                    foreach (var prop in padElem.EnumerateObject())
                    {
                        sb.AppendLine($"{prop.Name} = \"{prop.Value.GetString()}\"");
                    }
                }

                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
                if (!string.IsNullOrEmpty(gameDir))
                {
                    try
                    {
                        string rootPath = Path.Combine(gameDir, "settings.toml");
                        if (!string.Equals(rootPath, path, StringComparison.OrdinalIgnoreCase))
                        {
                            File.WriteAllText(rootPath, sb.ToString(), Encoding.UTF8);
                        }
                    }
                    catch { }
                }
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
