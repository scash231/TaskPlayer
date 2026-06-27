using System.Windows;
using System.Windows.Forms;
using System.Drawing;

namespace TaskbarMiniPlayer
{
    public partial class App : System.Windows.Application
    {
        private NotifyIcon? _notifyIcon;
        private MainWindow? _mainWindow;

        protected override void OnStartup(StartupEventArgs e)
        {
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
                _notifyIcon.Text = "TaskbarMiniPlayer";

                var contextMenu = new ContextMenuStrip();
                contextMenu.Items.Add("Settings...", null, (s, args) => ShowSettings());
                contextMenu.Items.Add(new ToolStripSeparator());
                contextMenu.Items.Add("Exit", null, (s, args) => Shutdown());

                _notifyIcon.ContextMenuStrip = contextMenu;
                _notifyIcon.DoubleClick += (s, args) => ShowSettings();
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
                settingsWindow.ShowDialog();
                _mainWindow.ReloadSettings();
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }
            base.OnExit(e);
        }
    }
}
