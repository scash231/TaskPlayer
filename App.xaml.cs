using System.Windows;
using System.Windows.Forms;
using System.Drawing;

namespace TaskbarMiniPlayer
{
    public partial class App : System.Windows.Application
    {
        private static System.Threading.Mutex? _singleInstanceMutex;
        private NotifyIcon? _notifyIcon;
        private MainWindow? _mainWindow;

        protected override void OnStartup(StartupEventArgs e)
        {
            _singleInstanceMutex = new System.Threading.Mutex(true, "TaskbarMiniPlayer_SingleInstance", out bool createdNew);
            if (!createdNew)
            {
                _singleInstanceMutex.Dispose();
                _singleInstanceMutex = null;
                Shutdown();
                return;
            }

            // Check for side taskbar (vertical)
            var taskbar = Win32.FindWindow("Shell_TrayWnd", null);
            if (taskbar != IntPtr.Zero && Win32.GetWindowRect(taskbar, out var tbRect))
            {
                bool isVertical = (tbRect.Bottom - tbRect.Top) > (tbRect.Right - tbRect.Left);
                if (isVertical)
                {
                    var errorWin = new TaskbarMiniPlayer.Views.TaskbarErrorWindow();
                    errorWin.ShowDialog();
                    _singleInstanceMutex.ReleaseMutex();
                    _singleInstanceMutex.Dispose();
                    Shutdown();
                    return;
                }
            }

            base.OnStartup(e);

            try
            {
                _mainWindow = new MainWindow();

                _notifyIcon = new NotifyIcon();
                _notifyIcon.Icon = SystemIcons.Application;
                
                try
                {
                    string exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "";
                    if (!string.IsNullOrEmpty(exePath) && exePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        var icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
                        if (icon != null) _notifyIcon.Icon = icon;
                    }
                }
                catch { }

                _notifyIcon.Visible = true;
                _notifyIcon.Text = "TaskPlayer";

                var contextMenu = new ContextMenuStrip();
                contextMenu.Items.Add("Settings...", null, (s, args) => ShowSettings());

                var translucentIcoItem = new ToolStripMenuItem("TranslucentICO Settings...", null, (s, args) => ShowTranslucentIcoSettings());
                contextMenu.Items.Add(translucentIcoItem);

                contextMenu.Items.Add(new ToolStripSeparator());
                contextMenu.Items.Add("Exit", null, (s, args) => Shutdown());

                contextMenu.Opening += (s, args) =>
                {
                    var settings = Settings.Load();
                    translucentIcoItem.Visible = settings.EnableTranslucentIco;
                };

                _notifyIcon.ContextMenuStrip = contextMenu;
                _notifyIcon.MouseClick += (s, args) =>
                {
                    if (args.Button == MouseButtons.Middle)
                    {
                        ShowSettings();
                    }
                };
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(ex.ToString(), "Startup Error");
                Shutdown();
            }
        }

        private void ShowSettings()
        {
            if (_mainWindow != null)
            {
                foreach (Window window in System.Windows.Application.Current.Windows)
                {
                    if (window is SettingsWindow existingWindow)
                    {
                        if (existingWindow.WindowState == WindowState.Minimized)
                            existingWindow.WindowState = WindowState.Normal;
                        existingWindow.Activate();
                        return;
                    }
                }

                var rect = new System.Windows.Rect(_mainWindow.Left, _mainWindow.Top, _mainWindow.Width, _mainWindow.Height);
                var settingsWindow = new SettingsWindow(rect);
                settingsWindow.Closed += (s, ev) => _mainWindow.ReloadSettings();
                settingsWindow.Show();
            }
        }

        private void ShowTranslucentIcoSettings()
        {
            if (_mainWindow != null)
            {
                foreach (Window window in System.Windows.Application.Current.Windows)
                {
                    if (window is TranslucentIcoWindow existingWindow)
                    {
                        if (existingWindow.WindowState == WindowState.Minimized)
                            existingWindow.WindowState = WindowState.Normal;
                        existingWindow.Activate();
                        return;
                    }
                }

                var rect = new System.Windows.Rect(_mainWindow.Left, _mainWindow.Top, _mainWindow.Width, _mainWindow.Height);
                var translucentIcoWindow = new TranslucentIcoWindow(rect, _mainWindow);
                translucentIcoWindow.Show();
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }
            _singleInstanceMutex?.ReleaseMutex();
            _singleInstanceMutex?.Dispose();
            base.OnExit(e);
        }
    }
}
