// Application configuration settings and savable profiles.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace TaskbarMiniPlayer
{
    public enum LayoutStyle { Compact, Standard, Expanded }
    public enum AppTheme { System, Light, Dark }
    public enum SizingTarget { Both, TitleOnly, ArtistOnly }
    public enum BorderMode { None, Static, Timeline }
    public enum TimelineStyle { Default, Flipped, BothSides }

    public class Settings
    {
        public bool ShowPlayer { get; set; } = true;
        public int ButtonSize { get; set; } = 28;
        public int ButtonGap { get; set; } = 2;
        public string HoverColor { get; set; } = "#464646";
        public string PressedColor { get; set; } = "#696969";
        public int XOffset { get; set; } = 0;
        public int YOffset { get; set; } = 0;
        public int AutoHideSeconds { get; set; } = 60;
        public bool LaunchOnStartup { get; set; } = false;
        public bool EnableVolumeSlider { get; set; } = false;
        public bool ScrollLongText { get; set; } = true;
        public bool HideArtist { get; set; } = false;
        public bool EnableDynamicSizing { get; set; } = true;
        public bool EnableSmartResize { get; set; } = true;
        public bool HideBorder { get; set; } = false;
        public BorderMode BorderMode { get; set; } = BorderMode.Static;
        public TimelineStyle TimelineStyle { get; set; } = TimelineStyle.Default;
        public int MinWidth { get; set; } = 250;
        public int MaxWidth { get; set; } = 500;
        public SizingTarget DynamicSizingTarget { get; set; } = SizingTarget.Both;

        public bool EnableTransparency { get; set; } = true;
        public double BackgroundOpacity { get; set; } = 0.40;
        public double AdaptiveTintStrength { get; set; } = 0.70;
        public double ScrollSpeed { get; set; } = 30.0;
        public double ScrollDelay { get; set; } = 1.5;
        public string ScrollBehavior { get; set; } = "Marquee";
        public bool HideScrollbars { get; set; } = false;
        public bool AutoPlayOnLaunch { get; set; } = false;
        public bool KeepTopmost { get; set; } = true;
        public string CustomAccentColor { get; set; } = "#0078D7";

        public bool DisableFluidAnimations { get; set; } = false;
        public bool DisableTimelineAnimation { get; set; } = false;
        public bool DisableVisualizer { get; set; } = false;
        public bool DisableTextScrolling { get; set; } = false;
        public bool DisableAlbumArt { get; set; } = false;
        public bool DisableTransparency { get; set; } = false;

        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsTransparent => EnableTransparency && !DisableTransparency;

        public bool OptimizeTimerFrequencies { get; set; } = false;

        public LayoutStyle Layout { get; set; } = LayoutStyle.Expanded;
        public AppTheme Theme { get; set; } = AppTheme.Dark;

        public uint PlayPauseHotkeyMod { get; set; } = 0;
        public uint PlayPauseHotkeyKey { get; set; } = 0;
        public uint PrevHotkeyMod { get; set; } = 0;
        public uint PrevHotkeyKey { get; set; } = 0;
        public uint NextHotkeyMod { get; set; } = 0;
        public uint NextHotkeyKey { get; set; } = 0;

        // ── Mode & Profiles ──
        public bool ExpertMode { get; set; } = false;
        public string? ActiveProfileName { get; set; }

        private static string GetSettingsPath()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dir = Path.Combine(appData, "TaskbarMiniPlayer");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "settings.json");
        }

        private static string GetProfilesDir()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dir = Path.Combine(appData, "TaskbarMiniPlayer", "profiles");
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

        public static Settings Load()
        {
            try
            {
                var path = GetSettingsPath();
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    var settings = JsonSerializer.Deserialize<Settings>(json) ?? new Settings();
                    if (settings.HideBorder && settings.BorderMode == BorderMode.Static)
                    {
                        settings.BorderMode = BorderMode.None;
                        settings.HideBorder = false;
                    }
                    return settings;
                }
            }
            catch { }
            return new Settings();
        }

        public void Save()
        {
            try
            {
                var json = JsonSerializer.Serialize(this, _jsonOptions);
                File.WriteAllText(GetSettingsPath(), json);
                ApplyStartup();
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

        public static Settings? LoadProfile(string name)
        {
            try
            {
                var path = Path.Combine(GetProfilesDir(), SanitizeFileName(name) + ".json");
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    return JsonSerializer.Deserialize<Settings>(json);
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

        public void ApplyStartup()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                if (key != null)
                {
                    if (LaunchOnStartup)
                        key.SetValue("TaskbarMiniPlayer", Environment.ProcessPath ?? "");
                    else
                        key.DeleteValue("TaskbarMiniPlayer", false);
                }
            }
            catch { }
        }
    }
}
