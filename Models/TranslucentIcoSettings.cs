// Translucent icon overlay settings and savable profiles.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace TaskbarMiniPlayer
{
    public class TranslucentIcoSettings
    {
        public int Opacity { get; set; } = 255;
        public string Layer { get; set; } = "listview";
        public bool LaunchOnStartup { get; set; } = false;
        public string? ActiveProfileName { get; set; }

        // ── Profile manager (shared logic via ProfileManager<T>) ──
        private static readonly ProfileManager<TranslucentIcoSettings> _profiles = new(GetProfilesDir());

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
            catch (Exception ex) { Log.Error("[TranslucentIcoSettings] Failed to load settings", ex); }
            return new TranslucentIcoSettings();
        }

        public void Save()
        {
            try
            {
                var json = JsonSerializer.Serialize(this, _jsonOptions);
                File.WriteAllText(GetSettingsPath(), json);
            }
            catch (Exception ex) { Log.Error("[TranslucentIcoSettings] Failed to save settings", ex); }
        }

        // ── Profile Management (delegated to ProfileManager<T>) ──

        public void SaveAsProfile(string name) => _profiles.SaveAsProfile(this, name);
        public static TranslucentIcoSettings? LoadProfile(string name) => _profiles.LoadProfile(name);
        public static List<string> GetProfileNames() => _profiles.GetProfileNames();
        public static bool DeleteProfile(string name) => _profiles.DeleteProfile(name);
        public static bool RenameProfile(string oldName, string newName) => _profiles.RenameProfile(oldName, newName);
    }
}
