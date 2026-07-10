// Manages global keyboard shortcuts registration and message hooks.
using System;
using System.Windows.Interop;

namespace TaskbarMiniPlayer
{
    public class HotkeyManager
    {
        private readonly IntPtr _hwnd;
        private readonly Action<int> _onHotkeyTriggered;

        public HotkeyManager(IntPtr hwnd, Action<int> onHotkeyTriggered)
        {
            _hwnd = hwnd;
            _onHotkeyTriggered = onHotkeyTriggered;
        }

        public void ApplyHotkeys(Settings settings)
        {
            if (_hwnd == IntPtr.Zero) return;

            // Unregister first to refresh settings
            Win32.UnregisterHotKey(_hwnd, 1);
            Win32.UnregisterHotKey(_hwnd, 2);
            Win32.UnregisterHotKey(_hwnd, 3);

            if (settings.PlayPauseHotkeyKey != 0)
            {
                Win32.RegisterHotKey(_hwnd, 1, settings.PlayPauseHotkeyMod, settings.PlayPauseHotkeyKey);
            }
            if (settings.PrevHotkeyKey != 0)
            {
                Win32.RegisterHotKey(_hwnd, 2, settings.PrevHotkeyMod, settings.PrevHotkeyKey);
            }
            if (settings.NextHotkeyKey != 0)
            {
                Win32.RegisterHotKey(_hwnd, 3, settings.NextHotkeyMod, settings.NextHotkeyKey);
            }
        }

        public void UnregisterAll()
        {
            if (_hwnd == IntPtr.Zero) return;
            Win32.UnregisterHotKey(_hwnd, 1);
            Win32.UnregisterHotKey(_hwnd, 2);
            Win32.UnregisterHotKey(_hwnd, 3);
        }

        public bool ProcessMessage(int msg, IntPtr wParam, ref bool handled)
        {
            if (msg == (int)Win32.WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                _onHotkeyTriggered?.Invoke(id);
                handled = true;
                return true;
            }
            return false;
        }
    }
}
