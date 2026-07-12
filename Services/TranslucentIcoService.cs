using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace TaskbarMiniPlayer
{
    public static class TranslucentIcoService
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string? lpszWindow);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", EntryPoint = "GetWindowLong", SetLastError = true)]
        private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
        private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

        private static IntPtr GetWindowLong(IntPtr hWnd, int nIndex)
        {
            if (IntPtr.Size == 8)
                return GetWindowLongPtr(hWnd, nIndex);
            else
                return new IntPtr(GetWindowLong32(hWnd, nIndex));
        }

        private static IntPtr SetWindowLong(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
        {
            if (IntPtr.Size == 8)
                return SetWindowLongPtr(hWnd, nIndex, dwNewLong);
            else
                return new IntPtr(SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32()));
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern bool RedrawWindow(IntPtr hWnd, IntPtr lprcUpdate, IntPtr hrgnUpdate, uint flags);

        [DllImport("user32.dll")]
        private static extern IntPtr GetParent(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int SystemParametersInfo(uint uAction, uint uParam, string? lpvParam, uint fuWinIni);

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_LAYERED = 0x80000;
        private const uint LWA_ALPHA = 0x2;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_FRAMECHANGED = 0x0020;

        private const uint RDW_INVALIDATE = 0x0001;
        private const uint RDW_ERASE = 0x0004;
        private const uint RDW_ALLCHILDREN = 0x0080;
        private const uint RDW_UPDATENOW = 0x0100;

        private const uint SPI_SETDESKWALLPAPER = 0x0014;
        private const uint SPIF_SENDCHANGE = 0x02;

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
            handles.Progman = FindWindow("Progman", null);
            if (handles.Progman != IntPtr.Zero)
            {
                handles.DefView = FindWindowEx(handles.Progman, IntPtr.Zero, "SHELLDLL_DefView", null);
            }

            if (handles.DefView == IntPtr.Zero)
            {
                IntPtr hwndDefView = IntPtr.Zero;
                IntPtr hwndWorkerW = IntPtr.Zero;

                EnumWindows((hwnd, lParam) =>
                {
                    var sb = new StringBuilder(256);
                    if (GetClassName(hwnd, sb, sb.Capacity) > 0 && sb.ToString() == "WorkerW")
                    {
                        var defView = FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null);
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
                handles.ListView = FindWindowEx(handles.DefView, IntPtr.Zero, "SysListView32", null);
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

            if (hwndTarget == IntPtr.Zero) return false;

            IntPtr style = GetWindowLong(hwndTarget, GWL_EXSTYLE);
            long styleLong = style.ToInt64();

            if (opacity == 255)
            {
                if ((styleLong & WS_EX_LAYERED) != 0)
                {
                    SetWindowLong(hwndTarget, GWL_EXSTYLE, new IntPtr(styleLong & ~WS_EX_LAYERED));
                    SetWindowPos(hwndTarget, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
                }
                SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, null, SPIF_SENDCHANGE);
            }
            else
            {
                if ((styleLong & WS_EX_LAYERED) == 0)
                {
                    SetWindowLong(hwndTarget, GWL_EXSTYLE, new IntPtr(styleLong | WS_EX_LAYERED));
                    SetWindowPos(hwndTarget, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
                }
                SetLayeredWindowAttributes(hwndTarget, 0, (byte)opacity, LWA_ALPHA);
            }

            RedrawWindow(hwndTarget, IntPtr.Zero, IntPtr.Zero, RDW_ERASE | RDW_INVALIDATE | RDW_ALLCHILDREN | RDW_UPDATENOW);
            IntPtr parent = GetParent(hwndTarget);
            while (parent != IntPtr.Zero)
            {
                RedrawWindow(parent, IntPtr.Zero, IntPtr.Zero, RDW_ERASE | RDW_INVALIDATE | RDW_ALLCHILDREN | RDW_UPDATENOW);
                parent = GetParent(parent);
            }

            // Sync with C++ settings.txt
            SaveTxtSettings(opacity, targetLayer);

            return true;
        }

        public static void SaveTxtSettings(int opacity, string layer)
        {
            try
            {
                var dir = @"C:\Users\benne\source\repos\translucentICO";
                if (Directory.Exists(dir))
                {
                    var filePath = Path.Combine(dir, "settings.txt");
                    File.WriteAllText(filePath, $"opacity={opacity}\nlayer={layer}\n");
                }
            }
            catch { }
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
            catch { }
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
                        string path = @"C:\Users\benne\source\repos\translucentICO\desktop_icons.exe";
                        string cmd = $"\"{path}\" --startup";
                        key.SetValue("TranslucentICO", cmd);
                    }
                    else
                    {
                        key.DeleteValue("TranslucentICO", false);
                    }
                    return true;
                }
            }
            catch { }
            return false;
        }
    }
}
