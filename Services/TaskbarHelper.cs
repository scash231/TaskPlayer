// Handles Windows taskbar detection and player window placement.
using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Automation;

namespace TaskbarMiniPlayer
{
    public static class TaskbarHelper
    {
        private static double _cachedIconsRightLimit;
        private static DateTime _lastIconsRightCheck = DateTime.MinValue;

        public static void ResetCache()
        {
            _cachedIconsRightLimit = 0;
            _lastIconsRightCheck = DateTime.MinValue;
        }

        private static double GetTaskbarIconsRightLimit()
        {
            try
            {
                var taskbarElement = AutomationElement.RootElement.FindFirst(
                    TreeScope.Children,
                    new PropertyCondition(AutomationElement.ClassNameProperty, "Shell_TrayWnd")
                );
                if (taskbarElement == null) return 0;

                var taskListElement = taskbarElement.FindFirst(
                    TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.ClassNameProperty, "MSTaskListWClass")
                );

                if (taskListElement != null)
                {
                    var buttons = taskListElement.FindAll(
                        TreeScope.Descendants,
                        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button)
                    );

                    double maxRight = 0;
                    foreach (AutomationElement btn in buttons)
                    {
                        var rect = btn.Current.BoundingRectangle;
                        if (rect.Width > 0 && rect.Height > 0)
                        {
                            if (rect.Right > maxRight)
                            {
                                maxRight = rect.Right;
                            }
                        }
                    }
                    if (maxRight > 0) return maxRight;
                }

                // Fallback: Scan all buttons in Shell_TrayWnd to the left of the TrayNotifyWnd
                var taskbar = Win32.FindWindow("Shell_TrayWnd", null);
                var trayWnd = Win32.FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
                double trayLeft = double.MaxValue;
                if (trayWnd != IntPtr.Zero)
                {
                    if (Win32.GetWindowRect(trayWnd, out var trayRect))
                    {
                        trayLeft = trayRect.Left;
                    }
                }

                var allButtons = taskbarElement.FindAll(
                    TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button)
                );

                double fallbackMaxRight = 0;
                foreach (AutomationElement btn in allButtons)
                {
                    var rect = btn.Current.BoundingRectangle;
                    if (rect.Width > 0 && rect.Height > 0 && rect.Right < trayLeft)
                    {
                        if (rect.Right > fallbackMaxRight)
                        {
                            fallbackMaxRight = rect.Right;
                        }
                    }
                }
                return fallbackMaxRight;
            }
            catch
            {
                return 0;
            }
        }

        private static double GetTaskbarIconsRightLimitCached()
        {
            var now = DateTime.UtcNow;
            if ((now - _lastIconsRightCheck).TotalSeconds < 5)
                return _cachedIconsRightLimit;

            _lastIconsRightCheck = now;
            _cachedIconsRightLimit = GetTaskbarIconsRightLimit();
            return _cachedIconsRightLimit;
        }

        public static double GetSmartResizeMaxWidth(Settings settings, MediaManager mediaManager)
        {
            var taskbar = Win32.FindWindow("Shell_TrayWnd", null);
            if (taskbar == IntPtr.Zero) return 0;

            Win32.GetWindowRect(taskbar, out var tbRect);
            var trayWnd = Win32.FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);

            double trayLeft = tbRect.Right - 350;
            if (trayWnd != IntPtr.Zero)
            {
                if (Win32.GetWindowRect(trayWnd, out var trayRect))
                {
                    trayLeft = trayRect.Left;
                }
            }

            double iconsRight = GetTaskbarIconsRightLimitCached();
            if (iconsRight <= 0) return 0;

            double buffer = 20; // 20px distance
            double maxW = trayLeft - iconsRight - settings.ButtonGap + settings.XOffset - buffer;

            double minCompactW = (settings.ButtonSize * 3) + 30;
            if (mediaManager != null && mediaManager.TotalSessions > 1)
            {
                minCompactW += 18;
            }

            if (maxW < minCompactW)
            {
                maxW = minCompactW;
            }

            return maxW;
        }

        public static void RepositionWindow(
            Window window,
            Settings settings,
            MediaManager mediaManager,
            double targetWidth,
            bool forceZOrder,
            bool wasHiddenForFullscreen,
            bool isAutoHidden)
        {
            if (wasHiddenForFullscreen) return;

            var taskbar = Win32.FindWindow("Shell_TrayWnd", null);
            if (taskbar == IntPtr.Zero) return;

            var helper = new WindowInteropHelper(window);
            var hwnd = helper.Handle;
            if (hwnd == IntPtr.Zero) return;

            try
            {
                if (helper.Owner != taskbar)
                {
                    helper.Owner = taskbar;
                }
            }
            catch 
            {
                try
                {
                    Win32.SetWindowLongPtr(hwnd, Win32.GWL_HWNDPARENT, taskbar);
                }
                catch { }
            }

            Win32.GetWindowRect(taskbar, out var tbRect);
            var trayWnd = Win32.FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);

            int myW = (int)targetWidth;
            int myH = (int)window.Height;
            int xPos = tbRect.Right - myW - 350; // Fallback position

            if (trayWnd != IntPtr.Zero)
            {
                Win32.GetWindowRect(trayWnd, out var trayRect);
                xPos = trayRect.Left - myW - settings.ButtonGap;
            }

            xPos += settings.XOffset;

            int yPos = tbRect.Top + (tbRect.Height - myH) / 2;
            yPos += settings.YOffset;

            if (!wasHiddenForFullscreen && settings.ShowPlayer && !isAutoHidden && window.Visibility != Visibility.Visible)
            {
                window.Show();
            }

            Win32.SetWindowPos(hwnd, Win32.HWND_TOPMOST, xPos, yPos, myW, myH, Win32.SWP_NOACTIVATE | Win32.SWP_SHOWWINDOW);
        }
    }
}
