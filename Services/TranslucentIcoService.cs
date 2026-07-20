// Controls desktop icon transparency via layered window attributes.
using System;
using System.IO;
using System.Text;

namespace TaskbarMiniPlayer
{
    public static class TranslucentIcoService
    {
        private struct DesktopHandles
        {
            public IntPtr Progman;
            public IntPtr WorkerW;
            public IntPtr DefView;
            public IntPtr ListView;
        }

        private static DesktopHandles GetDesktopHandles()
        {
            var handles = new DesktopHandles();
            handles.Progman = Win32.FindWindow("Progman", null);
            if (handles.Progman != IntPtr.Zero)
            {
                handles.DefView = Win32.FindWindowEx(handles.Progman, IntPtr.Zero, "SHELLDLL_DefView", null);
            }

            if (handles.DefView == IntPtr.Zero)
            {
                IntPtr hwndDefView = IntPtr.Zero;
                IntPtr hwndWorkerW = IntPtr.Zero;

                Win32.EnumWindows((hwnd, lParam) =>
                {
                    var sb = new StringBuilder(256);
                    if (Win32.GetClassName(hwnd, sb, sb.Capacity) > 0 && sb.ToString() == "WorkerW")
                    {
                        var defView = Win32.FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null);
                        if (defView != IntPtr.Zero)
                        {
                            hwndDefView = defView;
                            hwndWorkerW = hwnd;
                            return false; // Stop enumeration
                        }
                    }
                    return true; // Continue enumeration
                }, IntPtr.Zero);

                handles.DefView = hwndDefView;
                handles.WorkerW = hwndWorkerW;
            }

            if (handles.DefView != IntPtr.Zero)
            {
                handles.ListView = Win32.FindWindowEx(handles.DefView, IntPtr.Zero, "SysListView32", null);
            }

            return handles;
        }

        public static bool SetDesktopIconOpacity(int opacity, string targetLayer)
        {
            if (opacity < 0 || opacity > 255) return false;

            var handles = GetDesktopHandles();
            IntPtr hwndTarget = IntPtr.Zero;

            if (targetLayer == "defview")
            {
                hwndTarget = handles.DefView;
            }
            else if (targetLayer == "workerw")
            {
                hwndTarget = (handles.WorkerW != IntPtr.Zero) ? handles.WorkerW : handles.Progman;
            }
            else
            {
                hwndTarget = handles.ListView;
            }

            if (hwndTarget == IntPtr.Zero)
            {
                Log.Warn("[TranslucentIco] Target window handle not found");
                return false;
            }

            IntPtr style = Win32.GetWindowLongPtr(hwndTarget, Win32.GWL_EXSTYLE);
            long styleLong = style.ToInt64();

            if (opacity == 255)
            {
                if ((styleLong & Win32.WS_EX_LAYERED) != 0)
                {
                    Win32.SetWindowLongPtr(hwndTarget, Win32.GWL_EXSTYLE, new IntPtr(styleLong & ~Win32.WS_EX_LAYERED));
                    Win32.SetWindowPos(hwndTarget, IntPtr.Zero, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE | Win32.SWP_FRAMECHANGED);
                }
                Win32.SystemParametersInfo(Win32.SPI_SETDESKWALLPAPER, 0, null, Win32.SPIF_SENDCHANGE);
            }
            else
            {
                if ((styleLong & Win32.WS_EX_LAYERED) == 0)
                {
                    Win32.SetWindowLongPtr(hwndTarget, Win32.GWL_EXSTYLE, new IntPtr(styleLong | Win32.WS_EX_LAYERED));
                    Win32.SetWindowPos(hwndTarget, IntPtr.Zero, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE | Win32.SWP_FRAMECHANGED);
                }
                Win32.SetLayeredWindowAttributes(hwndTarget, 0, (byte)opacity, Win32.LWA_ALPHA);
            }

            Win32.RedrawWindow(hwndTarget, IntPtr.Zero, IntPtr.Zero, Win32.RDW_ERASE | Win32.RDW_INVALIDATE | Win32.RDW_ALLCHILDREN | Win32.RDW_UPDATENOW);
            IntPtr parent = Win32.GetParent(hwndTarget);
            while (parent != IntPtr.Zero)
            {
                Win32.RedrawWindow(parent, IntPtr.Zero, IntPtr.Zero, Win32.RDW_ERASE | Win32.RDW_INVALIDATE | Win32.RDW_ALLCHILDREN | Win32.RDW_UPDATENOW);
                parent = Win32.GetParent(parent);
            }

            // Sync settings to companion app data directory
            SaveTxtSettings(opacity, targetLayer);

            return true;
        }

        /// <summary>
        /// Saves opacity/layer to a settings.txt for the companion C++ TranslucentICO app.
        /// Uses the shared AppData directory instead of a hardcoded path.
        /// </summary>
        public static void SaveTxtSettings(int opacity, string layer)
        {
            try
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var dir = Path.Combine(appData, "TaskbarMiniPlayer", "translucentico");
                Directory.CreateDirectory(dir);
                var filePath = Path.Combine(dir, "settings.txt");
                File.WriteAllText(filePath, $"opacity={opacity}\nlayer={layer}\n");
            }
            catch (Exception ex) { Log.Error("[TranslucentIco] Failed to save txt settings", ex); }
        }

        public static bool IsStartupEnabled()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false);
                if (key != null)
                {
                    var val = key.GetValue("TranslucentICO") as string;
                    return !string.IsNullOrEmpty(val);
                }
            }
            catch (Exception ex) { Log.Error("[TranslucentIco] Failed to check startup status", ex); }
            return false;
        }

        public static bool SetStartupEnabled(bool enable)
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                if (key != null)
                {
                    if (enable)
                    {
                        // Use the current app's directory to locate the companion EXE
                        var appDir = AppContext.BaseDirectory;
                        var exePath = Path.Combine(appDir, "desktop_icons.exe");

                        // Fallback: check AppData if not bundled alongside
                        if (!File.Exists(exePath))
                        {
                            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                            exePath = Path.Combine(appData, "TaskbarMiniPlayer", "translucentico", "desktop_icons.exe");
                        }

                        string cmd = $"\"{exePath}\" --startup";
                        key.SetValue("TranslucentICO", cmd);
                    }
                    else
                    {
                        key.DeleteValue("TranslucentICO", false);
                    }
                    return true;
                }
            }
            catch (Exception ex) { Log.Error("[TranslucentIco] Failed to set startup", ex); }
            return false;
        }
    }
}
