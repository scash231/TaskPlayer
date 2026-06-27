using System;
using System.IO;
using System.Text.Json;

namespace TaskbarMiniPlayer
{
    public enum LayoutStyle { Compact, Standard, Expanded }
    public enum AppTheme { System, Light, Dark }

    public class Settings
    {
        public bool ShowPlayer { get; set; } = true;
        public int ButtonSize { get; set; } = 28;
        public int ButtonGap { get; set; } = 2;
        public string HoverColor { get; set; } = "#464646";
        public string PressedColor { get; set; } = "#696969";
        public int XOffset { get; set; } = 0;
        public int AutoHideSeconds { get; set; } = 60;
        public bool LaunchOnStartup { get; set; } = false;
        public bool EnableVolumeSlider { get; set; } = false;
        public bool ScrollLongText { get; set; } = true;

        public LayoutStyle Layout { get; set; } = LayoutStyle.Expanded;
        public AppTheme Theme { get; set; } = AppTheme.Dark;

        public uint PlayPauseHotkeyMod { get; set; } = 0;
        public uint PlayPauseHotkeyKey { get; set; } = 0;
        public uint PrevHotkeyMod { get; set; } = 0;
        public uint PrevHotkeyKey { get; set; } = 0;
        public uint NextHotkeyMod { get; set; } = 0;
        public uint NextHotkeyKey { get; set; } = 0;

        private static string GetSettingsPath()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dir = Path.Combine(appData, "TaskbarMiniPlayer");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "settings.json");
        }

        public static Settings Load()
        {
            try
            {
                var path = GetSettingsPath();
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    return JsonSerializer.Deserialize<Settings>(json) ?? new Settings();
                }
            }
            catch { }
            return new Settings();
        }

        public void Save()
        {
            try
            {
                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(GetSettingsPath(), json);
                ApplyStartup();
            }
            catch { }
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
