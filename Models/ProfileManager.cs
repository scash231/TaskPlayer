// Generic profile management for JSON-serializable settings types.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace TaskbarMiniPlayer
{
    public class ProfileManager<T> where T : class
    {
        private readonly string _profilesDir;
        private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

        public ProfileManager(string profilesDir)
        {
            _profilesDir = profilesDir;
            Directory.CreateDirectory(_profilesDir);
        }

        public void SaveAsProfile(T settings, string name)
        {
            try
            {
                var path = Path.Combine(_profilesDir, SanitizeFileName(name) + ".json");
                var json = JsonSerializer.Serialize(settings, _jsonOptions);
                File.WriteAllText(path, json);
            }
            catch (Exception ex) { Log.Error($"[ProfileManager] Failed to save profile '{name}'", ex); }
        }

        public T? LoadProfile(string name)
        {
            try
            {
                var path = Path.Combine(_profilesDir, SanitizeFileName(name) + ".json");
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    return JsonSerializer.Deserialize<T>(json);
                }
            }
            catch (Exception ex) { Log.Error($"[ProfileManager] Failed to load profile '{name}'", ex); }
            return null;
        }

        public List<string> GetProfileNames()
        {
            try
            {
                return Directory.GetFiles(_profilesDir, "*.json")
                    .Select(f => Path.GetFileNameWithoutExtension(f))
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception ex) { Log.Error("[ProfileManager] Failed to list profiles", ex); }
            return new List<string>();
        }

        public bool DeleteProfile(string name)
        {
            try
            {
                var path = Path.Combine(_profilesDir, SanitizeFileName(name) + ".json");
                if (File.Exists(path)) { File.Delete(path); return true; }
            }
            catch (Exception ex) { Log.Error($"[ProfileManager] Failed to delete profile '{name}'", ex); }
            return false;
        }

        public bool RenameProfile(string oldName, string newName)
        {
            try
            {
                var oldPath = Path.Combine(_profilesDir, SanitizeFileName(oldName) + ".json");
                var newPath = Path.Combine(_profilesDir, SanitizeFileName(newName) + ".json");
                if (File.Exists(oldPath) && !File.Exists(newPath))
                {
                    File.Move(oldPath, newPath);
                    return true;
                }
            }
            catch (Exception ex) { Log.Error($"[ProfileManager] Failed to rename profile '{oldName}' -> '{newName}'", ex); }
            return false;
        }

        public static string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c));
        }
    }
}
