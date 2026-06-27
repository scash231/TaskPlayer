using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Collections.Generic;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using NAudio.CoreAudioApi;

namespace TaskbarMiniPlayer
{
    public partial class MainWindow : Window
    {
        private readonly MediaManager _mediaManager;
        private Settings _settings;
        private IntPtr _winEventHook;
        private Win32.WinEventDelegate _winEventDelegate;
        private DispatcherTimer _autoHideTimer;
        private DispatcherTimer _zOrderTimer;
        private bool _isAutoHidden = false;
        private bool _wasHiddenForFullscreen = false;
        private HwndSource? _source;
        private MMDevice? _audioDevice;
        private bool _isDraggingVolume = false;

        public MainWindow()
        {
            InitializeComponent();
            _settings = Settings.Load();
            _mediaManager = new MediaManager();
            _mediaManager.MediaStateChanged += OnMediaStateChanged;

            _winEventDelegate = new Win32.WinEventDelegate(WinEventProc);

            _autoHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _autoHideTimer.Tick += AutoHideTimer_Tick;

            _zOrderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _zOrderTimer.Tick += (s, e) => 
            {
                EnforceZOrder();
                Reposition();
            };
            _zOrderTimer.Start();

            ApplySettings();
        }

        private void EnforceZOrder()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            if (IsForegroundFullscreen())
            {
                if (Visibility == Visibility.Visible)
                {
                    Hide();
                    _wasHiddenForFullscreen = true;
                }
            }
            else
            {
                if (_wasHiddenForFullscreen && _settings.ShowPlayer)
                {
                    Show();
                    _wasHiddenForFullscreen = false;
                }
                if (Visibility == Visibility.Visible)
                {
                    Win32.SetWindowPos(hwnd, Win32.HWND_TOPMOST, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
                }
            }
        }

        private bool IsForegroundFullscreen()
        {
            var foregroundHwnd = Win32.GetForegroundWindow();
            if (foregroundHwnd == IntPtr.Zero) return false;
            if (foregroundHwnd == Win32.GetDesktopWindow() || foregroundHwnd == Win32.GetShellWindow()) return false;

            var sb = new System.Text.StringBuilder(256);
            Win32.GetClassName(foregroundHwnd, sb, sb.Capacity);
            string className = sb.ToString();
            if (className == "WorkerW" || className == "Progman") return false;

            Win32.GetWindowRect(foregroundHwnd, out var rect);
            var screen = System.Windows.Forms.Screen.FromHandle(foregroundHwnd);
            return rect.Left <= screen.Bounds.Left &&
                   rect.Top <= screen.Bounds.Top &&
                   rect.Right >= screen.Bounds.Right &&
                   rect.Bottom >= screen.Bounds.Bottom;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            // Clip the window to a rounded rectangle to prevent the blocky blur effect
            IntPtr hRgn = Win32.CreateRoundRectRgn(0, 0, (int)Width, (int)Height, 32, 32);
            Win32.SetWindowRgn(hwnd, hRgn, true);

            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);

            _source = HwndSource.FromHwnd(hwnd);
            _source?.AddHook(HwndHook);

            _winEventHook = Win32.SetWinEventHook(Win32.EVENT_OBJECT_LOCATIONCHANGE, Win32.EVENT_OBJECT_LOCATIONCHANGE, IntPtr.Zero, _winEventDelegate, 0, 0, Win32.WINEVENT_OUTOFCONTEXT);

            await _mediaManager.InitializeAsync();

            ApplyHotkeys();
            Reposition();
            ResetAutoHideTimer();

            try
            {
                var enumerator = new MMDeviceEnumerator();
                _audioDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                _audioDevice.AudioEndpointVolume.OnVolumeNotification += AudioEndpointVolume_OnVolumeNotification;
                UpdateVolumeUI(_audioDevice.AudioEndpointVolume.MasterVolumeLevelScalar);
            }
            catch { }
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            if (_audioDevice != null)
            {
                _audioDevice.AudioEndpointVolume.OnVolumeNotification -= AudioEndpointVolume_OnVolumeNotification;
                _audioDevice.Dispose();
            }

            if (_winEventHook != IntPtr.Zero)
            {
                Win32.UnhookWinEvent(_winEventHook);
            }
        }

        public void ReloadSettings()
        {
            _settings = Settings.Load();
            ApplySettings();
            ApplyHotkeys();
        }

        public void ApplyHotkeys()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            Win32.UnregisterHotKey(hwnd, 1);
            Win32.UnregisterHotKey(hwnd, 2);
            Win32.UnregisterHotKey(hwnd, 3);

            if (_settings.PlayPauseHotkeyKey != 0) Win32.RegisterHotKey(hwnd, 1, _settings.PlayPauseHotkeyMod, _settings.PlayPauseHotkeyKey);
            if (_settings.PrevHotkeyKey != 0) Win32.RegisterHotKey(hwnd, 2, _settings.PrevHotkeyMod, _settings.PrevHotkeyKey);
            if (_settings.NextHotkeyKey != 0) Win32.RegisterHotKey(hwnd, 3, _settings.NextHotkeyMod, _settings.NextHotkeyKey);
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == Win32.WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                if (id == 1) _ = _mediaManager.PlayPauseAsync();
                else if (id == 2) _ = _mediaManager.SkipPreviousAsync();
                else if (id == 3) _ = _mediaManager.SkipNextAsync();
                handled = true;
            }
            return IntPtr.Zero;
        }

        private void OnMediaStateChanged()
        {
            Dispatcher.Invoke(() =>
            {
                BtnPlay.Content = _mediaManager.IsPlaying ? "\uE769" : "\uE768";
                
                TxtTitle.Text = string.IsNullOrEmpty(_mediaManager.Title) ? "Unknown" : _mediaManager.Title;
                TxtArtist.Text = string.IsNullOrEmpty(_mediaManager.Artist) ? "Unknown" : _mediaManager.Artist;
                
                if (_mediaManager.AlbumArt != null)
                    ImgAlbumArt.Source = _mediaManager.AlbumArt;
                else
                    ImgAlbumArt.Source = null;

                if (_mediaManager.IsPlaying)
                {
                    _isAutoHidden = false;
                    if (_settings.ShowPlayer) Show();
                    _autoHideTimer.Stop();
                }
                else
                {
                    ResetAutoHideTimer();
                }

                UpdateSessionIndicators();
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(UpdateMarquee));
            });
        }

        private void UpdateSessionIndicators()
        {
            bool wasVisible = SessionIndicator.Visibility == Visibility.Visible;
            bool shouldBeVisible = _mediaManager.TotalSessions > 1;

            if (shouldBeVisible)
            {
                var brushes = new List<SolidColorBrush>();
                for (int i = 0; i < _mediaManager.TotalSessions; i++)
                {
                    brushes.Add(new SolidColorBrush(i == _mediaManager.CurrentSessionIndex ? Colors.White : System.Windows.Media.Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)));
                }
                SessionIndicator.ItemsSource = brushes;
                SessionIndicator.Visibility = Visibility.Visible;
            }
            else
            {
                SessionIndicator.Visibility = Visibility.Collapsed;
            }

            if (wasVisible != shouldBeVisible)
            {
                ApplySettings();
            }
        }

        private void UpdateMarquee()
        {
            TxtTitle.BeginAnimation(Canvas.LeftProperty, null);
            TxtArtist.BeginAnimation(Canvas.LeftProperty, null);
            TxtTitle.BeginAnimation(UIElement.OpacityProperty, null);
            TxtArtist.BeginAnimation(UIElement.OpacityProperty, null);
            
            Canvas.SetLeft(TxtTitle, 0);
            Canvas.SetLeft(TxtArtist, 0);
            TxtTitle.Opacity = 1;
            TxtArtist.Opacity = 1;

            if (!_settings.ScrollLongText)
            {
                TxtTitle.Width = TitleGrid.ActualWidth > 0 ? TitleGrid.ActualWidth : double.NaN;
                TxtArtist.Width = ArtistGrid.ActualWidth > 0 ? ArtistGrid.ActualWidth : double.NaN;
                return;
            }

            TxtTitle.Width = double.NaN;
            TxtArtist.Width = double.NaN;
            TxtTitle.UpdateLayout();
            TxtArtist.UpdateLayout();

            if (TxtTitle.ActualWidth > TitleGrid.ActualWidth && TitleGrid.ActualWidth > 0)
            {
                double overflow = TxtTitle.ActualWidth - TitleGrid.ActualWidth + 10;
                StartMarqueeAnimation(TxtTitle, overflow);
            }

            if (TxtArtist.ActualWidth > ArtistGrid.ActualWidth && ArtistGrid.ActualWidth > 0)
            {
                double overflow = TxtArtist.ActualWidth - ArtistGrid.ActualWidth + 10;
                StartMarqueeAnimation(TxtArtist, overflow);
            }
        }

        private void StartMarqueeAnimation(TextBlock txt, double overflow)
        {
            double scrollDuration = Math.Max(1.0, overflow / 30.0);
            var sb = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };

            var moveAnim = new DoubleAnimationUsingKeyFrames();
            Storyboard.SetTarget(moveAnim, txt);
            Storyboard.SetTargetProperty(moveAnim, new PropertyPath("(Canvas.Left)"));
            moveAnim.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            moveAnim.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.5)))); // Wait 1.5s at start
            moveAnim.KeyFrames.Add(new LinearDoubleKeyFrame(-overflow, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.5 + scrollDuration)))); // Scroll left
            moveAnim.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.5 + scrollDuration + 0.4)))); // Reset pos while hidden

            var fadeAnim = new DoubleAnimationUsingKeyFrames();
            Storyboard.SetTarget(fadeAnim, txt);
            Storyboard.SetTargetProperty(fadeAnim, new PropertyPath("Opacity"));
            fadeAnim.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            fadeAnim.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.5 + scrollDuration)))); // Stay visible while scrolling
            fadeAnim.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.5 + scrollDuration + 0.3)))); // Fade out
            fadeAnim.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.5 + scrollDuration + 0.4)))); // Keep hidden during reset
            fadeAnim.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.5 + scrollDuration + 0.7)))); // Fade back in

            sb.Children.Add(moveAnim);
            sb.Children.Add(fadeAnim);
            sb.Duration = new Duration(TimeSpan.FromSeconds(1.5 + scrollDuration + 0.7));

            sb.Begin();
        }

        private void ResetAutoHideTimer()
        {
            if (_settings.AutoHideSeconds > 0 && !_mediaManager.IsPlaying)
            {
                _autoHideTimer.Interval = TimeSpan.FromSeconds(_settings.AutoHideSeconds);
                _autoHideTimer.Start();
            }
            else
            {
                _autoHideTimer.Stop();
            }
        }

        private void AutoHideTimer_Tick(object? sender, EventArgs e)
        {
            if (!_mediaManager.IsPlaying && _settings.AutoHideSeconds > 0)
            {
                _isAutoHidden = true;
                Hide();
            }
            _autoHideTimer.Stop();
        }

        private void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            if (idObject != 0) return;
            var taskbar = Win32.FindWindow("Shell_TrayWnd", null);
            if (hwnd == taskbar)
            {
                Dispatcher.InvokeAsync(Reposition);
            }
        }

        public void ApplySettings()
        {
            double baseWidth = 0;

            if (_settings.Layout == LayoutStyle.Compact)
            {
                ColAlbumArt.Width = new GridLength(0);
                ColMetadata.Width = new GridLength(0);
                ImgAlbumArt.Visibility = Visibility.Collapsed;
                PanelMetadata.Visibility = Visibility.Collapsed;
                PanelButtons.Margin = new Thickness(8, 0, 8, 0);
                baseWidth = (_settings.ButtonSize * 3) + 18;
            }
            else if (_settings.Layout == LayoutStyle.Standard)
            {
                ColAlbumArt.Width = new GridLength(0);
                ColMetadata.Width = new GridLength(1, GridUnitType.Star);
                ImgAlbumArt.Visibility = Visibility.Collapsed;
                PanelMetadata.Visibility = Visibility.Visible;
                PanelButtons.Margin = new Thickness(0, 0, 8, 0);
                baseWidth = 150 + (_settings.ButtonSize * 3);
            }
            else // Expanded
            {
                ColAlbumArt.Width = new GridLength(1, GridUnitType.Auto);
                ColMetadata.Width = new GridLength(1, GridUnitType.Star);
                ImgAlbumArt.Visibility = Visibility.Visible;
                PanelMetadata.Visibility = Visibility.Visible;
                PanelButtons.Margin = new Thickness(0, 0, 8, 0);
                baseWidth = 200 + (_settings.ButtonSize * 3);
            }

            if (_mediaManager != null && _mediaManager.TotalSessions > 1)
            {
                baseWidth += 14;
            }

            Width = baseWidth;

            Height = _settings.ButtonSize + 8;
            
            BtnPrev.Width = _settings.ButtonSize;
            BtnPlay.Width = _settings.ButtonSize;
            BtnNext.Width = _settings.ButtonSize;

            if (_settings.ShowPlayer && !_isAutoHidden)
            {
                Show();
            }
            else
            {
                if (IsLoaded) Hide();
            }

            if (IsLoaded) Reposition();
        }

        public void Reposition()
        {
            var taskbar = Win32.FindWindow("Shell_TrayWnd", null);
            if (taskbar == IntPtr.Zero) return;

            var helper = new WindowInteropHelper(this);
            var hwnd = helper.Handle;
            if (hwnd == IntPtr.Zero) return;

            // Set the taskbar as the Owner.
            try
            {
                if (helper.Owner != taskbar)
                {
                    helper.Owner = taskbar;
                }
            }
            catch { }

            Win32.GetWindowRect(taskbar, out var tbRect);
            
            // Find System Tray to anchor to the left of it
            var trayWnd = Win32.FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);

            int myW = (int)Width;
            int myH = (int)Height;
            int xPos = tbRect.Right - myW - 350; // Fallback position

            if (trayWnd != IntPtr.Zero)
            {
                Win32.GetWindowRect(trayWnd, out var trayRect);
                // Anchor exactly to the left of the system tray
                xPos = trayRect.Left - myW - _settings.ButtonGap;
            }

            int yPos = tbRect.Top + (tbRect.Height - myH) / 2;

            // Check for Fullscreen overlap handles hiding
            // We removed taskbar overlap hide because Windows natively stretches the app container 
            // to the system tray regardless of how many apps are open, causing false positives.

            // Ensure we show if there's enough space and we aren't hidden by fullscreen
            if (!_wasHiddenForFullscreen && _settings.ShowPlayer && Visibility != Visibility.Visible)
            {
                Show();
            }

            // Apply Region Clip for dynamic width
            IntPtr hRgn = Win32.CreateRoundRectRgn(0, 0, myW, myH, 32, 32);
            Win32.SetWindowRgn(hwnd, hRgn, true);

            Win32.SetWindowPos(hwnd, Win32.HWND_TOPMOST, xPos, yPos, myW, myH, Win32.SWP_NOACTIVATE | Win32.SWP_SHOWWINDOW);
        }

        private async void BtnPrev_Click(object sender, RoutedEventArgs e) => await _mediaManager.SkipPreviousAsync();
        private async void BtnPlay_Click(object sender, RoutedEventArgs e) => await _mediaManager.PlayPauseAsync();
        private async void BtnNext_Click(object sender, RoutedEventArgs e) => await _mediaManager.SkipNextAsync();

        private void AudioEndpointVolume_OnVolumeNotification(AudioVolumeNotificationData data)
        {
            if (!_isDraggingVolume)
            {
                Dispatcher.InvokeAsync(() => UpdateVolumeUI(data.MasterVolume));
            }
        }
        
        private void UpdateVolumeUI(float volumeLevel)
        {
            VolumeFill.Width = VolumeTrack.ActualWidth * volumeLevel;
            if (VolumeFill.Width > 16)
                VolumeFill.CornerRadius = new CornerRadius(16, volumeLevel >= 0.98f ? 16 : 0, volumeLevel >= 0.98f ? 16 : 0, 16);
            else
                VolumeFill.CornerRadius = new CornerRadius(16, 0, 0, 16);
        }

        private void MainBorder_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_settings.EnableVolumeSlider)
            {
                OpenVolumePopup();
            }
        }

        private async void MainBorder_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            await System.Threading.Tasks.Task.Delay(50);
            if (!MainBorder.IsMouseOver && !VolumePopupGrid.IsMouseOver)
            {
                CloseVolumePopup();
            }
        }

        private void VolumePopup_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
        }

        private async void VolumePopup_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            await System.Threading.Tasks.Task.Delay(50);
            if (!MainBorder.IsMouseOver && !VolumePopupGrid.IsMouseOver)
            {
                CloseVolumePopup();
            }
        }

        private void OpenVolumePopup()
        {
            if (VolumePopup.IsOpen) return;
            if (VolumeTrack.ActualWidth > 0 && _audioDevice != null)
            {
                UpdateVolumeUI(_audioDevice.AudioEndpointVolume.MasterVolumeLevelScalar);
            }

            VolumePopup.IsOpen = true;
            VolumePopupGrid.IsHitTestVisible = true;

            var fadeDur = TimeSpan.FromMilliseconds(200);
            var springDur = TimeSpan.FromMilliseconds(400);

            var fadeIn = new DoubleAnimation(0, 1, fadeDur);
            var slideUp = new DoubleAnimation(20, 0, springDur) { EasingFunction = new Animations.AppleSpringEase(0.8, 0.4) };

            VolumePopupGrid.BeginAnimation(OpacityProperty, fadeIn);
            VolumePopupTranslate.BeginAnimation(TranslateTransform.YProperty, slideUp);
        }

        private void CloseVolumePopup()
        {
            if (!VolumePopup.IsOpen) return;
            VolumePopupGrid.IsHitTestVisible = false;
            
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150));
            fadeOut.Completed += (s, ev) => { VolumePopup.IsOpen = false; };
            VolumePopupGrid.BeginAnimation(OpacityProperty, fadeOut);

            _isDraggingVolume = false;
            VolumeTrack.ReleaseMouseCapture();
        }

        private void MainBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
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

                var rect = new Rect(this.Left, this.Top, this.Width, this.Height);
                var sw = new SettingsWindow(rect);
                sw.ShowDialog();
                ReloadSettings();
            }
        }

        private void MainBorder_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_settings.Layout == LayoutStyle.Compact)
                _settings.Layout = LayoutStyle.Standard;
            else if (_settings.Layout == LayoutStyle.Standard)
                _settings.Layout = LayoutStyle.Expanded;
            else
                _settings.Layout = LayoutStyle.Compact;
                
            _settings.Save();
            ApplySettings();
        }

        private void MainBorder_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Delta > 0)
                _mediaManager.SwitchSession(-1);
            else if (e.Delta < 0)
                _mediaManager.SwitchSession(1);
        }

        private void PanelMetadata_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(UpdateMarquee));
        }

        private void VolumeTrack_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                _isDraggingVolume = true;
                VolumeTrack.CaptureMouse();
                UpdateVolumeFromMouse(e);
            }
        }

        private void VolumeTrack_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_isDraggingVolume && e.LeftButton == MouseButtonState.Pressed)
            {
                UpdateVolumeFromMouse(e);
            }
        }

        private void VolumeTrack_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDraggingVolume)
            {
                _isDraggingVolume = false;
                VolumeTrack.ReleaseMouseCapture();
                UpdateVolumeFromMouse(e);
            }
        }

        private void UpdateVolumeFromMouse(System.Windows.Input.MouseEventArgs e)
        {
            if (_audioDevice == null) return;

            var pos = e.GetPosition(VolumeTrack);
            double width = VolumeTrack.ActualWidth;
            if (width <= 0) return;

            double ratio = pos.X / width;
            if (ratio < 0) ratio = 0;
            if (ratio > 1) ratio = 1;

            float volume = (float)ratio;
            UpdateVolumeUI(volume);

            try
            {
                _audioDevice.AudioEndpointVolume.MasterVolumeLevelScalar = volume;
            }
            catch { }
        }

        private const int GWL_EXSTYLE = -20;
        private const int GWL_HWNDPARENT = -8;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    }
}