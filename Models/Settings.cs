// Application configuration settings and savable profiles.
using System;
using System.Collections.Generic;
using System.IO;
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
        // ── Schema version for migration support ──
        public int SettingsVersion { get; set; } = 2;

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
        public string CustomAccentColor { get; set; } = "#0078D7";

        public bool DisableFluidAnimations { get; set; } = false;
        public bool DisableTimelineAnimation { get; set; } = false;
        public bool DisableVisualizer { get; set; } = false;
        public bool DisableTextScrolling { get; set; } = false;
        public bool DisableAlbumArt { get; set; } = false;
        public bool DisableTransparency { get; set; } = false;
        public bool EnableTranslucentIco { get; set; } = true;
        public bool EnableTooltips { get; set; } = true;

        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsTransparent => EnableTransparency && !DisableTransparency;

        public bool OptimizeTimerFrequencies { get; set; } = false;
        public int TopmostIntervalMs { get; set; } = 500;
        public bool DisableTopmostTimer { get; set; } = true;
        public int PeakMeterIntervalMs { get; set; } = 30;

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
        public bool DevMode { get; set; } = false;

        // ── Profile manager (shared logic via ProfileManager<T>) ──
        private static readonly ProfileManager<Settings> _profiles = new(GetProfilesDir());

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
                    RunMigrations(settings);
                    return settings;
                }
            }
            catch (Exception ex) { Log.Error("[Settings] Failed to load settings", ex); }
            return new Settings();
        }

        /// <summary>
        /// Runs version-gated migrations. Each migration bumps the version so it only runs once.
        /// </summary>
        private static void RunMigrations(Settings settings)
        {
            bool changed = false;

            // v1 → v2: Migrate HideBorder boolean to BorderMode enum
            if (settings.SettingsVersion < 2)
            {
                if (settings.HideBorder && settings.BorderMode == BorderMode.Static)
                {
                    settings.BorderMode = BorderMode.None;
                    settings.HideBorder = false;
                }
                settings.SettingsVersion = 2;
                changed = true;
            }

            if (changed)
            {
                settings.Save();
                Log.Info("[Settings] Migrated settings to version " + settings.SettingsVersion);
            }
        }

        public void Save()
        {
            try
            {
                var json = JsonSerializer.Serialize(this, _jsonOptions);
                File.WriteAllText(GetSettingsPath(), json);
                ApplyStartup();
            }
            catch (Exception ex) { Log.Error("[Settings] Failed to save settings", ex); }
        }

        // ── Profile Management (delegated to ProfileManager<T>) ──

        public void SaveAsProfile(string name) => _profiles.SaveAsProfile(this, name);
        public static Settings? LoadProfile(string name) => _profiles.LoadProfile(name);
        public static List<string> GetProfileNames() => _profiles.GetProfileNames();
        public static bool DeleteProfile(string name) => _profiles.DeleteProfile(name);
        public static bool RenameProfile(string oldName, string newName) => _profiles.RenameProfile(oldName, newName);

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
            catch (Exception ex) { Log.Error("[Settings] Failed to apply startup registry", ex); }
        }
    }
}
