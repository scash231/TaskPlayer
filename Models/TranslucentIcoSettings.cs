using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace TaskbarMiniPlayer
{
    public class TranslucentIcoSettings
    {
        public int Opacity { get; set; } = 255;
        public string Layer { get; set; } = "listview";
        public bool LaunchOnStartup { get; set; } = false;
        public string? ActiveProfileName { get; set; }

        private static string GetSettingsPath()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dir = Path.Combine(appData, "TaskbarMiniPlayer");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "translucent_settings.json");
        }

        private static string GetProfilesDir()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dir = Path.Combine(appData, "TaskbarMiniPlayer", "translucent_profiles");
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

        public static TranslucentIcoSettings Load()
        {
            try
            {
                var path = GetSettingsPath();
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    return JsonSerializer.Deserialize<TranslucentIcoSettings>(json) ?? new TranslucentIcoSettings();
                }
            }
            catch { }
            return new TranslucentIcoSettings();
        }

        public void Save()
        {
            try
            {
                var json = JsonSerializer.Serialize(this, _jsonOptions);
                File.WriteAllText(GetSettingsPath(), json);
            }
            catch { }
        }

        // ── Profile Management ──

        public void SaveAsProfile(string name)
        {
            try
            {
                var path = Path.Combine(GetProfilesDir(), SanitizeFileName(name) + ".json");
                var json = JsonSerializer.Serialize(this, _jsonOptions);
                File.WriteAllText(path, json);
            }
            catch { }
        }

        public static TranslucentIcoSettings? LoadProfile(string name)
        {
            try
            {
                var path = Path.Combine(GetProfilesDir(), SanitizeFileName(name) + ".json");
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    return JsonSerializer.Deserialize<TranslucentIcoSettings>(json);
                }
            }
            catch { }
            return null;
        }

        public static List<string> GetProfileNames()
        {
            try
            {
                var dir = GetProfilesDir();
                return Directory.GetFiles(dir, "*.json")
                    .Select(f => Path.GetFileNameWithoutExtension(f))
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch { }
            return new List<string>();
        }

        public static bool DeleteProfile(string name)
        {
            try
            {
                var path = Path.Combine(GetProfilesDir(), SanitizeFileName(name) + ".json");
                if (File.Exists(path)) { File.Delete(path); return true; }
            }
            catch { }
            return false;
        }

        public static bool RenameProfile(string oldName, string newName)
        {
            try
            {
                var dir = GetProfilesDir();
                var oldPath = Path.Combine(dir, SanitizeFileName(oldName) + ".json");
                var newPath = Path.Combine(dir, SanitizeFileName(newName) + ".json");
                if (File.Exists(oldPath) && !File.Exists(newPath))
                {
                    File.Move(oldPath, newPath);
                    return true;
                }
            }
            catch { }
            return false;
        }

        private static string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c));
        }
    }
}
