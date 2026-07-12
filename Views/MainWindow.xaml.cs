// Main player taskbar overlay window.
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
using System.Windows.Media.Imaging;
using System.Linq;
using Color = System.Windows.Media.Color;
using NAudio.CoreAudioApi;

namespace TaskbarMiniPlayer
{
    public partial class MainWindow : Window
    {
        private readonly MediaManager _mediaManager;
        public MediaManager MediaManagerInstance => _mediaManager;
        private Settings _settings;
        private IntPtr _winEventHook;
        private Win32.WinEventDelegate _winEventDelegate;
        private DispatcherTimer _autoHideTimer;
        private DispatcherTimer _zOrderTimer;
        private DispatcherTimer _timelineTimer;
        private bool _isAutoHidden = false;
        private bool _wasHiddenForFullscreen = false;
        private HwndSource? _source;
        private MMDevice? _audioDevice;
        private bool _isDraggingVolume = false;
        private DispatcherTimer? _peakMeterTimer;

        // Services
        private readonly ColorExtractor _colorExtractor = new();
        private HotkeyManager? _hotkeyManager;

        public MainWindow()
        {
            InitializeComponent();
            _settings = Settings.Load();
            _mediaManager = new MediaManager();
            _mediaManager.MediaStateChanged += OnMediaStateChanged;

            _winEventDelegate = new Win32.WinEventDelegate(WinEventProc);

            _autoHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _autoHideTimer.Tick += AutoHideTimer_Tick;

            _zOrderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(_settings.TopmostIntervalMs) };
            _zOrderTimer.Tick += (s, e) => 
            {
                EnforceZOrder();
                Reposition();
            };
            _zOrderTimer.Start();

            _timelineTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _timelineTimer.Tick += TimelineTimer_Tick;

            _peakMeterTimer = new DispatcherTimer(DispatcherPriority.Render);
            _peakMeterTimer.Interval = TimeSpan.FromMilliseconds(_settings.PeakMeterIntervalMs);
            _peakMeterTimer.Tick += PeakMeterTimer_Tick;
 
            MainBorder.SizeChanged += (s, e) => UpdateTimelineBorder(false);

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
                // Don't touch SetWindowPos while a fullscreen app is running —
                // it disrupts exclusive fullscreen and causes micro-stutters.
                return;
            }

            if (_wasHiddenForFullscreen && _settings.ShowPlayer && !_isAutoHidden)
            {
                Show();
                _wasHiddenForFullscreen = false;
            }
            if (Visibility == Visibility.Visible)
            {
                Win32.SetWindowPos(hwnd, Win32.HWND_TOPMOST, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
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
            
            int exStyle = Win32.GetWindowLong(hwnd, Win32.GWL_EXSTYLE);
            Win32.SetWindowLong(hwnd, Win32.GWL_EXSTYLE, exStyle | Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TOOLWINDOW);

            _hotkeyManager = new HotkeyManager(hwnd, (id) =>
            {
                if (id == 1) _ = _mediaManager.PlayPauseAsync();
                else if (id == 2) _ = _mediaManager.SkipPreviousAsync();
                else if (id == 3) _ = _mediaManager.SkipNextAsync();
            });

            _source = HwndSource.FromHwnd(hwnd);
            _source?.AddHook(HwndHook);
 
            _winEventHook = Win32.SetWinEventHook(Win32.EVENT_OBJECT_LOCATIONCHANGE, Win32.EVENT_OBJECT_LOCATIONCHANGE, IntPtr.Zero, _winEventDelegate, 0, 0, Win32.WINEVENT_OUTOFCONTEXT);

            await _mediaManager.InitializeAsync();
 
            if (_settings.AutoPlayOnLaunch && !_mediaManager.IsPlaying)
            {
                try { await _mediaManager.PlayPauseAsync(); } catch { }
            }

            ApplyHotkeys();
            Reposition(true);
            ResetAutoHideTimer();

            if (_settings.EnableTranslucentIco)
            {
                try
                {
                    var translucentSettings = TranslucentIcoSettings.Load();
                    TranslucentIcoService.SetDesktopIconOpacity(translucentSettings.Opacity, translucentSettings.Layer);
                }
                catch { }
            }

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
            _timelineTimer.Stop();
            _hotkeyManager?.UnregisterAll();
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
            _isAutoHidden = false;
            _settings = Settings.Load();
            ApplySettings(true);
            ApplyHotkeys();
            OnMediaStateChanged();

            if (_settings.EnableTranslucentIco)
            {
                try
                {
                    var translucentSettings = TranslucentIcoSettings.Load();
                    TranslucentIcoService.SetDesktopIconOpacity(translucentSettings.Opacity, translucentSettings.Layer);
                }
                catch { }
            }
            else
            {
                try
                {
                    var translucentSettings = TranslucentIcoSettings.Load();
                    TranslucentIcoService.SetDesktopIconOpacity(255, translucentSettings.Layer);
                }
                catch { }
            }

            if (_zOrderTimer != null)
            {
                _zOrderTimer.Interval = TimeSpan.FromMilliseconds(_settings.TopmostIntervalMs);
            }
            if (_peakMeterTimer != null)
            {
                _peakMeterTimer.Interval = TimeSpan.FromMilliseconds(_settings.PeakMeterIntervalMs);
            }
        }

        public void ApplyHotkeys()
        {
            _hotkeyManager?.ApplyHotkeys(_settings);
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            _hotkeyManager?.ProcessMessage(msg, wParam, ref handled);
            return IntPtr.Zero;
        }

        private void OnMediaStateChanged()
        {
            Dispatcher.Invoke(() =>
            {
                BtnPlay.Content = _mediaManager.IsPlaying ? "\uE769" : "\uE768";
                
                string newTitle = string.IsNullOrEmpty(_mediaManager.Title) ? "Unknown" : _mediaManager.Title;
                string newArtist = string.IsNullOrEmpty(_mediaManager.Artist) ? "Unknown" : _mediaManager.Artist;
                bool songChanged = TxtTitle.Text != newTitle || TxtArtist.Text != newArtist;
                
                byte alpha = _settings.IsTransparent ? (byte)(_settings.BackgroundOpacity * 255) : (byte)255;
                double tintStrength = _settings.AdaptiveTintStrength;

                var bmp = _mediaManager.AlbumArt as BitmapSource;
                if (bmp != null && !_settings.DisableAlbumArt)
                {
                    ImgAlbumArt.Source = bmp;
                    var (color1, color2) = _colorExtractor.GetCachedGradientColors(bmp);
                    
                    var blended1 = ColorExtractor.Blend(Color.FromRgb(31, 31, 36), color1, tintStrength);
                    var blended2 = ColorExtractor.Blend(Color.FromRgb(18, 18, 20), color2, tintStrength);

                    var stop1Color = Color.FromArgb(alpha, blended1.R, blended1.G, blended1.B);
                    var stop2Color = Color.FromArgb(alpha, blended2.R, blended2.G, blended2.B);

                    if (_settings.DisableFluidAnimations)
                    {
                        MainBgStop1.BeginAnimation(GradientStop.ColorProperty, null);
                        MainBgStop2.BeginAnimation(GradientStop.ColorProperty, null);
                        VolumeBgStop1.BeginAnimation(GradientStop.ColorProperty, null);
                        VolumeBgStop2.BeginAnimation(GradientStop.ColorProperty, null);

                        MainBgStop1.Color = stop1Color;
                        MainBgStop2.Color = stop2Color;
                        VolumeBgStop1.Color = stop1Color;
                        VolumeBgStop2.Color = stop2Color;
                    }
                    else
                    {
                        Animations.FluidMotion.AnimateGradientStop(MainBgStop1, stop1Color);
                        Animations.FluidMotion.AnimateGradientStop(MainBgStop2, stop2Color);
                        Animations.FluidMotion.AnimateGradientStop(VolumeBgStop1, stop1Color);
                        Animations.FluidMotion.AnimateGradientStop(VolumeBgStop2, stop2Color);
                    }

                    var brush = new SolidColorBrush(ColorExtractor.BrightenColorIfDark(color1));
                    TimelineBorder.Stroke = brush;
                    TimelineBorder2.Stroke = brush;
                }
                else
                {
                    ImgAlbumArt.Source = null;
                    
                    var fallback1 = Color.FromArgb(alpha, 31, 31, 36);
                    var fallback2 = Color.FromArgb(alpha, 18, 18, 20);
                    
                    if (_settings.DisableFluidAnimations)
                    {
                        MainBgStop1.BeginAnimation(GradientStop.ColorProperty, null);
                        MainBgStop2.BeginAnimation(GradientStop.ColorProperty, null);
                        VolumeBgStop1.BeginAnimation(GradientStop.ColorProperty, null);
                        VolumeBgStop2.BeginAnimation(GradientStop.ColorProperty, null);

                        MainBgStop1.Color = fallback1;
                        MainBgStop2.Color = fallback2;
                        VolumeBgStop1.Color = fallback1;
                        VolumeBgStop2.Color = fallback2;
                    }
                    else
                    {
                        Animations.FluidMotion.AnimateGradientStop(MainBgStop1, fallback1);
                        Animations.FluidMotion.AnimateGradientStop(MainBgStop2, fallback2);
                        Animations.FluidMotion.AnimateGradientStop(VolumeBgStop1, fallback1);
                        Animations.FluidMotion.AnimateGradientStop(VolumeBgStop2, fallback2);
                    }

                    var defaultBrush = Resources["Accent"] as System.Windows.Media.SolidColorBrush ?? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.DeepSkyBlue);
                    TimelineBorder.Stroke = defaultBrush;
                    TimelineBorder2.Stroke = defaultBrush;
                }

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

                if (songChanged && IsLoaded)
                {
                    var fadeOut = new DoubleAnimation(PanelMetadata.Opacity, 0.0, TimeSpan.FromMilliseconds(100))
                    {
                        EasingFunction = new CircleEase { EasingMode = EasingMode.EaseIn }
                    };
                    var slideDown = new DoubleAnimation(MetadataTranslate.Y, 8.0, TimeSpan.FromMilliseconds(100))
                    {
                        EasingFunction = new CircleEase { EasingMode = EasingMode.EaseIn }
                    };

                    fadeOut.Completed += (s, e) =>
                    {
                        TxtTitle.Text = newTitle;
                        TxtArtist.Text = newArtist;

                        double targetWidth = CalculateTargetWidth();
                        double nonTextW = CalculateNonTextWidth();
                        double targetMetadataWidth = targetWidth - nonTextW;

                        // Start width animation
                        AnimateToWidth(targetWidth);

                        // Perform marquee layout updates immediately using final target width
                        UpdateMarquee(targetMetadataWidth);

                        // Reset translation for slide-up
                        MetadataTranslate.Y = -8.0;

                        // Run slide-up and fade-in concurrently with the size change
                        var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(250))
                        {
                            EasingFunction = new CircleEase { EasingMode = EasingMode.EaseOut }
                        };
                        var slideUp = new DoubleAnimation(-8.0, 0.0, TimeSpan.FromMilliseconds(250))
                        {
                            EasingFunction = new CircleEase { EasingMode = EasingMode.EaseOut }
                        };

                        PanelMetadata.BeginAnimation(OpacityProperty, fadeIn);
                        MetadataTranslate.BeginAnimation(TranslateTransform.YProperty, slideUp);
                    };

                    PanelMetadata.BeginAnimation(OpacityProperty, fadeOut);
                    MetadataTranslate.BeginAnimation(TranslateTransform.YProperty, slideDown);
                }
                else
                {
                    TxtTitle.Text = newTitle;
                    TxtArtist.Text = newArtist;
                    double targetWidth = CalculateTargetWidth();
                    AnimateToWidth(targetWidth);
                }

                if (_settings.BorderMode == BorderMode.Timeline)
                {
                    if (_mediaManager.IsPlaying)
                    {
                        _timelineTimer.Start();
                    }
                    else
                    {
                        _timelineTimer.Stop();
                    }
                    UpdateTimelineBorder(true);
                }
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
                ApplySettings(true);
            }
        }

        private void UpdateMarquee(double? overrideContainerWidth = null)
        {
            if (_isAnimatingWidth && overrideContainerWidth == null) return;

            TxtTitle.BeginAnimation(Canvas.LeftProperty, null);
            TxtArtist.BeginAnimation(Canvas.LeftProperty, null);
            TxtTitle.BeginAnimation(UIElement.OpacityProperty, null);
            TxtArtist.BeginAnimation(UIElement.OpacityProperty, null);
            
            Canvas.SetLeft(TxtTitle, 0);
            Canvas.SetLeft(TxtArtist, 0);
            TxtTitle.Opacity = 1;
            TxtArtist.Opacity = 1;

            bool isHovering = MainBorder.IsMouseOver || (VolumePopup.IsOpen && VolumePopupGrid.IsMouseOver);
            bool shouldScroll = _settings.ScrollLongText && !_settings.DisableTextScrolling;
            
            if (shouldScroll && _settings.ScrollBehavior == "Hover" && !isHovering)
            {
                shouldScroll = false;
            }

            string titleText = string.IsNullOrEmpty(_mediaManager.Title) ? "Unknown" : _mediaManager.Title;
            string artistText = string.IsNullOrEmpty(_mediaManager.Artist) ? "Unknown" : _mediaManager.Artist;

            double titleW = GetTextWidth(TxtTitle, titleText);
            double artistW = GetTextWidth(TxtArtist, artistText);

            double containerWidth = overrideContainerWidth ?? (TitleGrid.ActualWidth > 0 ? TitleGrid.ActualWidth : 120.0);

            if (shouldScroll && titleW > containerWidth && containerWidth > 0)
            {
                TitleCanvas.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
                TitleCanvas.ClearValue(FrameworkElement.WidthProperty);

                string spacer = "        ";
                TxtTitle.Text = titleText + spacer + titleText;
                double singleWidth = GetTextWidth(TxtTitle, titleText + spacer);
                StartMarqueeAnimation(TxtTitle, singleWidth, containerWidth);
            }
            else
            {
                TxtTitle.Text = titleText;
                if (_settings.HideArtist)
                {
                    TxtTitle.Width = double.NaN;
                    TitleCanvas.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
                    TitleCanvas.Width = titleW;
                    Canvas.SetLeft(TxtTitle, 0);
                }
                else
                {
                    TxtTitle.Width = containerWidth > 0 ? containerWidth : double.NaN;
                    TitleCanvas.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
                    TitleCanvas.ClearValue(FrameworkElement.WidthProperty);
                }
            }

            if (shouldScroll && artistW > containerWidth && containerWidth > 0)
            {
                string spacer = "        ";
                TxtArtist.Text = artistText + spacer + artistText;
                double singleWidth = GetTextWidth(TxtArtist, artistText + spacer);
                StartMarqueeAnimation(TxtArtist, singleWidth, containerWidth);
            }
            else
            {
                TxtArtist.Text = artistText;
                TxtArtist.Width = containerWidth > 0 ? containerWidth : double.NaN;
            }
        }

        private double GetTextWidth(TextBlock tb, string text)
        {
            var typeface = new Typeface(tb.FontFamily, tb.FontStyle, tb.FontWeight, tb.FontStretch);
            var formattedText = new FormattedText(
                text,
                System.Globalization.CultureInfo.CurrentCulture,
                System.Windows.FlowDirection.LeftToRight,
                typeface,
                tb.FontSize,
                System.Windows.Media.Brushes.Black,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            return formattedText.Width;
        }

        private void StartMarqueeAnimation(TextBlock txt, double scrollLimit, double containerWidth)
        {
            double speed = _settings.ScrollSpeed > 0 ? _settings.ScrollSpeed : 30.0;
            double delay = _settings.ScrollDelay >= 0 ? _settings.ScrollDelay : 1.5;
            string behavior = _settings.ScrollBehavior ?? "Marquee";

            var sb = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };

            if (behavior == "PingPong")
            {
                double overflow = scrollLimit - containerWidth;
                if (overflow <= 0) overflow = 20.0;

                double scrollDuration = Math.Max(1.0, overflow / speed);
                double totalDuration = delay + scrollDuration + delay + scrollDuration;

                var moveAnim = new DoubleAnimationUsingKeyFrames();
                Storyboard.SetTarget(moveAnim, txt);
                Storyboard.SetTargetProperty(moveAnim, new PropertyPath("(Canvas.Left)"));

                moveAnim.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                moveAnim.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(delay))));
                moveAnim.KeyFrames.Add(new LinearDoubleKeyFrame(-overflow, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(delay + scrollDuration))));
                moveAnim.KeyFrames.Add(new LinearDoubleKeyFrame(-overflow, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(delay + scrollDuration + delay))));
                moveAnim.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(delay + scrollDuration + delay + scrollDuration))));

                sb.Children.Add(moveAnim);
                sb.Duration = new Duration(TimeSpan.FromSeconds(totalDuration));
            }
            else // "Marquee" or "Hover" (loop infinitely)
            {
                double scrollDuration = Math.Max(1.0, scrollLimit / speed);
                double totalDuration = delay + scrollDuration;

                var moveAnim = new DoubleAnimationUsingKeyFrames();
                Storyboard.SetTarget(moveAnim, txt);
                Storyboard.SetTargetProperty(moveAnim, new PropertyPath("(Canvas.Left)"));

                moveAnim.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                moveAnim.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(delay))));
                moveAnim.KeyFrames.Add(new LinearDoubleKeyFrame(-scrollLimit, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(delay + scrollDuration))));
                moveAnim.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(delay + scrollDuration))));

                sb.Children.Add(moveAnim);
                sb.Duration = new Duration(TimeSpan.FromSeconds(totalDuration));
            }

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
                if (_isAutoHidden)
                {
                    _isAutoHidden = false;
                    if (_settings.ShowPlayer) Show();
                }
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
                Dispatcher.InvokeAsync(() => Reposition());
            }
        }

        public void ApplySettings(bool animate = false)
        {
            if (_zOrderTimer != null)
            {
                _zOrderTimer.Interval = TimeSpan.FromMilliseconds(_settings.TopmostIntervalMs);
            }
            if (_timelineTimer != null)
            {
                _timelineTimer.Interval = _settings.OptimizeTimerFrequencies ? TimeSpan.FromMilliseconds(2000) : TimeSpan.FromMilliseconds(500);
            }

            try
            {
                var accentColor = (Color)System.Windows.Media.ColorConverter.ConvertFromString(_settings.CustomAccentColor);
                Resources["Accent"] = new SolidColorBrush(accentColor);
            }
            catch { }

            if (_settings.BorderMode == BorderMode.None)
            {
                MainBorder.BorderThickness = new Thickness(0);
                TimelineBorder.Visibility = Visibility.Collapsed;
                TimelineBorder2.Visibility = Visibility.Collapsed;
                _timelineTimer?.Stop();
            }
            else if (_settings.BorderMode == BorderMode.Static)
            {
                MainBorder.BorderThickness = new Thickness(1);
                MainBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 255, 255, 255));
                TimelineBorder.Visibility = Visibility.Collapsed;
                TimelineBorder2.Visibility = Visibility.Collapsed;
                _timelineTimer?.Stop();
            }
            else // Timeline
            {
                MainBorder.BorderThickness = new Thickness(1);
                MainBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(0x14, 255, 255, 255));
                if (_settings.TimelineStyle == TimelineStyle.BothSides)
                {
                    TimelineBorder.Visibility = Visibility.Visible;
                    TimelineBorder2.Visibility = Visibility.Visible;
                }
                else
                {
                    TimelineBorder.Visibility = Visibility.Visible;
                    TimelineBorder2.Visibility = Visibility.Collapsed;
                }

                SolidColorBrush strokeBrush;
                var bmp = _mediaManager?.AlbumArt as BitmapSource;
                if (bmp != null && !_settings.DisableAlbumArt)
                {
                    var (color1, _) = _colorExtractor.GetCachedGradientColors(bmp);
                    strokeBrush = new SolidColorBrush(ColorExtractor.BrightenColorIfDark(color1));
                }
                else
                {
                    try
                    {
                        var accentColor = (Color)System.Windows.Media.ColorConverter.ConvertFromString(_settings.CustomAccentColor);
                        strokeBrush = new SolidColorBrush(accentColor);
                    }
                    catch
                    {
                        strokeBrush = Resources["Accent"] as System.Windows.Media.SolidColorBrush ?? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.DeepSkyBlue);
                    }
                }

                TimelineBorder.Stroke = strokeBrush;
                TimelineBorder2.Stroke = strokeBrush;

                if (_mediaManager != null && _mediaManager.IsPlaying)
                {
                    _timelineTimer?.Start();
                }
                else
                {
                    _timelineTimer?.Stop();
                }
                UpdateTimelineBorder(false);
            }

            if (_settings.Layout == LayoutStyle.Compact)
            {
                ColAlbumArt.Width = new GridLength(0);
                ColMetadata.Width = new GridLength(0);
                ImgAlbumArt.Visibility = Visibility.Collapsed;
                PanelMetadata.Visibility = Visibility.Collapsed;
                PanelButtons.Margin = new Thickness(14, 0, 14, 0);
            }
            else if (_settings.Layout == LayoutStyle.Standard)
            {
                ColAlbumArt.Width = new GridLength(0);
                ColMetadata.Width = new GridLength(1, GridUnitType.Star);
                ImgAlbumArt.Visibility = Visibility.Collapsed;
                PanelMetadata.Visibility = Visibility.Visible;
                PanelButtons.Margin = new Thickness(0, 0, 14, 0);
            }
            else // Expanded
            {
                ColAlbumArt.Width = new GridLength(1, GridUnitType.Auto);
                ColMetadata.Width = new GridLength(1, GridUnitType.Star);
                ImgAlbumArt.Visibility = Visibility.Visible;
                PanelMetadata.Visibility = Visibility.Visible;
                PanelButtons.Margin = new Thickness(0, 0, 14, 0);
            }

            bool showSessionIndicator = _mediaManager != null && _mediaManager.TotalSessions > 1;

            if (showSessionIndicator)
            {
                SessionIndicator.Margin = new Thickness(14, 0, 0, 0);
                ImgAlbumArt.Margin = new Thickness(8, 4, 4, 4);
                PanelMetadata.Margin = new Thickness(4, 0, 8, 0);
            }
            else if (_settings.Layout == LayoutStyle.Expanded)
            {
                ImgAlbumArt.Margin = new Thickness(14, 4, 4, 4);
                PanelMetadata.Margin = new Thickness(4, 0, 8, 0);
            }
            else // Standard layout (no album art, no session indicator)
            {
                PanelMetadata.Margin = new Thickness(14, 0, 8, 0);
            }

            // Set Window width static maximum. If dynamic sizing is disabled, make Window width match target width.
            double windowWidth = _settings.MaxWidth;
            if (!_settings.EnableDynamicSizing)
            {
                windowWidth = CalculateTargetWidth();
            }
            Width = windowWidth;

            double targetWidth = CalculateTargetWidth();

            if (animate && IsLoaded)
            {
                AnimateToWidth(targetWidth);
            }
            else
            {
                MainBorder.BeginAnimation(WidthProperty, null);
                MainBorder.Width = targetWidth;
                UpdateMarquee();
            }

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

            if (ArtistGrid != null)
            {
                ArtistGrid.Visibility = _settings.HideArtist ? Visibility.Collapsed : Visibility.Visible;
            }

            if (IsLoaded) Reposition(true);
        }

        private double CalculateNonTextWidth()
        {
            if (_settings.Layout == LayoutStyle.Compact)
            {
                double width = (_settings.ButtonSize * 3) + 30;
                if (_mediaManager != null && _mediaManager.TotalSessions > 1)
                {
                    width += 18;
                }
                return width;
            }

            double nonTextWidth = (_settings.ButtonSize * 3) + 14; // buttons + right margin (14)
            if (_mediaManager != null && _mediaManager.TotalSessions > 1)
            {
                nonTextWidth += 14 + 4; // session indicator (14 left margin + 4 width = 18)
            }

            if (_settings.Layout == LayoutStyle.Expanded)
            {
                if (_mediaManager != null && _mediaManager.TotalSessions > 1)
                {
                    nonTextWidth += 8 + 32 + 4; // album art (8 left margin + 32 width + 4 right margin = 44)
                }
                else
                {
                    nonTextWidth += 14 + 32 + 4; // album art (14 left margin + 32 width + 4 right margin = 50)
                }
            }

            // Metadata panel margins (4 left + 8 right = 12 or 14 left + 8 right = 22) + safety padding
            if (_mediaManager != null && _mediaManager.TotalSessions <= 1 && _settings.Layout == LayoutStyle.Standard)
            {
                nonTextWidth += 22 + 16;
            }
            else
            {
                nonTextWidth += 12 + 16;
            }

            return nonTextWidth;
        }



        private double GetSmartResizeMaxWidth()
        {
            return TaskbarHelper.GetSmartResizeMaxWidth(_settings, _mediaManager);
        }

        private double CalculateTargetWidth()
        {
            double width;
            if (_settings.Layout == LayoutStyle.Compact)
            {
                width = (_settings.ButtonSize * 3) + 30;
                if (_mediaManager != null && _mediaManager.TotalSessions > 1)
                {
                    width += 18;
                }
            }
            else if (_settings.EnableDynamicSizing)
            {
                string titleText = string.IsNullOrEmpty(_mediaManager?.Title) ? "Unknown" : _mediaManager.Title;
                string artistText = string.IsNullOrEmpty(_mediaManager?.Artist) ? "Unknown" : _mediaManager.Artist;

                double maxTextW = 0;
                if (_settings.HideArtist || _settings.DynamicSizingTarget == SizingTarget.TitleOnly)
                {
                    maxTextW = GetTextWidth(TxtTitle, titleText);
                }
                else if (_settings.DynamicSizingTarget == SizingTarget.ArtistOnly)
                {
                    maxTextW = GetTextWidth(TxtArtist, artistText);
                }
                else // Both
                {
                    double titleW = GetTextWidth(TxtTitle, titleText);
                    double artistW = GetTextWidth(TxtArtist, artistText);
                    maxTextW = Math.Max(titleW, artistW);
                }

                double nonTextWidth = CalculateNonTextWidth();
                double dynamicWidth = nonTextWidth + maxTextW;

                // Safe clamp
                double min = Math.Min(_settings.MinWidth, _settings.MaxWidth);
                double max = Math.Max(_settings.MinWidth, _settings.MaxWidth);
                width = Math.Clamp(dynamicWidth, min, max);
            }
            else
            {
                double baseWidth = 12; // safety offset to balance new margins
                if (_settings.Layout == LayoutStyle.Standard)
                {
                    baseWidth += 150 + (_settings.ButtonSize * 3);
                }
                else // Expanded
                {
                    baseWidth += 200 + (_settings.ButtonSize * 3);
                }

                if (_mediaManager != null && _mediaManager.TotalSessions > 1)
                {
                    baseWidth += 18;
                }
                width = baseWidth;
            }

            if (_settings.EnableSmartResize)
            {
                double maxAllowed = GetSmartResizeMaxWidth();
                if (maxAllowed > 0 && width > maxAllowed)
                {
                    width = maxAllowed;
                }
            }

            return width;
        }

        private bool _isAnimatingWidth = false;

        private void AnimateToWidth(double targetWidth, Action? onComplete = null)
        {
            double currentW = MainBorder.Width;
            if (double.IsNaN(currentW)) currentW = MainBorder.ActualWidth;
            if (currentW <= 0) currentW = CalculateTargetWidth();

            if (Math.Abs(currentW - targetWidth) < 0.5)
            {
                MainBorder.BeginAnimation(WidthProperty, null);
                MainBorder.Width = targetWidth;
                _isAnimatingWidth = false;
                UpdateMarquee();
                onComplete?.Invoke();
                return;
            }

            _isAnimatingWidth = true;

            var springEase = new Animations.AppleSpringEase(0.85, 0.4);
            var widthAnim = new DoubleAnimation(currentW, targetWidth, TimeSpan.FromMilliseconds(450))
            {
                EasingFunction = springEase
            };

            widthAnim.Completed += (s, e) =>
            {
                _isAnimatingWidth = false;
                MainBorder.BeginAnimation(WidthProperty, null);
                MainBorder.Width = targetWidth;
                UpdateMarquee();
                onComplete?.Invoke();
            };

            MainBorder.BeginAnimation(WidthProperty, widthAnim);
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            if (IsLoaded)
            {
                Reposition();
                RepositionVolumePopup();
            }
        }

        public void Reposition(bool forceZOrder = false)
        {
            if (_wasHiddenForFullscreen) return;

            var taskbar = Win32.FindWindow("Shell_TrayWnd", null);
            if (taskbar == IntPtr.Zero) return;

            var helper = new WindowInteropHelper(this);
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

            if (_settings.EnableSmartResize && IsLoaded)
            {
                double targetW = CalculateTargetWidth();
                if (!_settings.EnableDynamicSizing)
                {
                    if (Math.Abs(Width - targetW) > 1.0)
                    {
                        Width = targetW;
                    }
                }

                double currentW = MainBorder.Width;
                if (double.IsNaN(currentW)) currentW = MainBorder.ActualWidth;
                if (Math.Abs(currentW - targetW) > 1.0)
                {
                    AnimateToWidth(targetW);
                }
            }

            Win32.GetWindowRect(taskbar, out var tbRect);
            var trayWnd = Win32.FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);

            int myW = (int)Width;
            int myH = (int)Height;
            int xPos = tbRect.Right - myW - 350; // Fallback position

            if (trayWnd != IntPtr.Zero)
            {
                Win32.GetWindowRect(trayWnd, out var trayRect);
                xPos = trayRect.Left - myW - _settings.ButtonGap;
            }

            xPos += _settings.XOffset;

            int yPos = tbRect.Top + (tbRect.Height - myH) / 2;
            yPos += _settings.YOffset;

            if (_settings.ShowPlayer && !_isAutoHidden && Visibility != Visibility.Visible)
            {
                Show();
            }

            Win32.SetWindowPos(hwnd, Win32.HWND_TOPMOST, xPos, yPos, myW, myH, Win32.SWP_NOACTIVATE | Win32.SWP_SHOWWINDOW);
        }

        private void RepositionVolumePopup()
        {
            if (VolumePopup != null && VolumePopup.IsOpen)
            {
                var offset = VolumePopup.HorizontalOffset;
                VolumePopup.HorizontalOffset = offset + 0.01;
                VolumePopup.HorizontalOffset = offset;
            }
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

            if (volumeLevel == 0)
                VolumeIcon.Text = "\uE74F";
            else if (volumeLevel <= 0.33f)
                VolumeIcon.Text = "\uE992";
            else if (volumeLevel <= 0.66f)
                VolumeIcon.Text = "\uE993";
            else
                VolumeIcon.Text = "\uE994";
        }

        private void MainBorder_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_settings.EnableVolumeSlider)
            {
                OpenVolumePopup();
            }
            if (_settings.ScrollBehavior == "Hover")
            {
                UpdateMarquee();
            }
        }

        private async void MainBorder_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            await System.Threading.Tasks.Task.Delay(50);
            if (!MainBorder.IsMouseOver && !VolumePopupGrid.IsMouseOver)
            {
                CloseVolumePopup();
            }
            if (_settings.ScrollBehavior == "Hover")
            {
                UpdateMarquee();
            }
        }

        private void VolumePopup_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_settings.ScrollBehavior == "Hover")
            {
                UpdateMarquee();
            }
        }

        private async void VolumePopup_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            await System.Threading.Tasks.Task.Delay(50);
            if (!MainBorder.IsMouseOver && !VolumePopupGrid.IsMouseOver)
            {
                CloseVolumePopup();
            }
            if (_settings.ScrollBehavior == "Hover")
            {
                UpdateMarquee();
            }
        }

        private bool IsTaskbarAtTop()
        {
            var taskbar = Win32.FindWindow("Shell_TrayWnd", null);
            if (taskbar != IntPtr.Zero)
            {
                if (Win32.GetWindowRect(taskbar, out var tbRect))
                {
                    return tbRect.Top <= 0 && tbRect.Bottom > 0 && (tbRect.Right - tbRect.Left) > (tbRect.Bottom - tbRect.Top);
                }
            }
            return false;
        }

        private void OpenVolumePopup()
        {
            if (VolumePopup.IsOpen) return;
            if (VolumeTrack.ActualWidth > 0 && _audioDevice != null)
            {
                UpdateVolumeUI(_audioDevice.AudioEndpointVolume.MasterVolumeLevelScalar);
            }

            bool isTop = IsTaskbarAtTop();
            VolumePopup.Placement = isTop ? System.Windows.Controls.Primitives.PlacementMode.Bottom : System.Windows.Controls.Primitives.PlacementMode.Top;
            VolumePopup.VerticalOffset = isTop ? 8 : -8;

            VolumePopup.IsOpen = true;
            VolumePopupGrid.IsHitTestVisible = true;

            var fadeDur = TimeSpan.FromMilliseconds(200);
            var springDur = TimeSpan.FromMilliseconds(400);

            var fadeIn = new DoubleAnimation(0, 1, fadeDur);
            double startY = isTop ? -20 : 20;
            var slide = new DoubleAnimation(startY, 0, springDur) { EasingFunction = new Animations.AppleSpringEase(0.8, 0.4) };

            VolumePopupGrid.BeginAnimation(OpacityProperty, fadeIn);
            VolumePopupTranslate.BeginAnimation(TranslateTransform.YProperty, slide);

            if (!_settings.DisableVisualizer)
            {
                _peakMeterTimer?.Start();
            }
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

            _peakMeterTimer?.Stop();
            if (VolumeIconScale != null)
            {
                VolumeIconScale.ScaleX = 1.0;
                VolumeIconScale.ScaleY = 1.0;
            }
        }

        private void MainBorder_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                if (e.ClickCount == 2)
                {
                    ShowSettings();
                }
            }
            else if (e.ChangedButton == MouseButton.Right)
            {
                ToggleLayout();
            }
            else if (e.ChangedButton == MouseButton.Middle)
            {
                if (_settings.EnableTranslucentIco)
                {
                    ShowTranslucentIcoSettings();
                }
            }
        }

        private void ShowSettings()
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
            sw.Closed += (s, ev) => ReloadSettings();
            sw.Show();
        }

        private void ToggleLayout()
        {
            if (_settings.Layout == LayoutStyle.Compact)
                _settings.Layout = LayoutStyle.Standard;
            else if (_settings.Layout == LayoutStyle.Standard)
                _settings.Layout = LayoutStyle.Expanded;
            else
                _settings.Layout = LayoutStyle.Compact;
                
            _settings.Save();
            ApplySettings(true);
        }

        public void ShowTranslucentIcoSettings()
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

            var rect = new Rect(this.Left, this.Top, this.Width, this.Height);
            var tw = new TranslucentIcoWindow(rect);
            tw.Show();
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
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () => UpdateMarquee());
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
                SpringBackVolumeBorder();
            }
        }

        private void UpdateVolumeFromMouse(System.Windows.Input.MouseEventArgs e)
        {
            if (_audioDevice == null) return;
 
            var pos = e.GetPosition(VolumeTrack);
            double width = VolumeTrack.ActualWidth;
            if (width <= 0) return;
 
            double ratio = pos.X / width;
            double stretchX = 1.0;
            double squeezeY = 1.0;

            if (ratio > 1.0)
            {
                double overDrag = pos.X - width;
                overDrag = Math.Min(overDrag, 150.0);
                stretchX = 1.0 + (overDrag / width) * 0.4;
                squeezeY = 1.0 - (stretchX - 1.0) * 0.5;
                VolumeBorder.RenderTransformOrigin = new System.Windows.Point(0.0, 0.5);
            }
            else if (ratio < 0.0)
            {
                double overDrag = -pos.X;
                overDrag = Math.Min(overDrag, 150.0);
                stretchX = 1.0 + (overDrag / width) * 0.4;
                squeezeY = 1.0 - (stretchX - 1.0) * 0.5;
                VolumeBorder.RenderTransformOrigin = new System.Windows.Point(1.0, 0.5);
            }
            else
            {
                VolumeBorder.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
            }

            VolumeBorderScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            VolumeBorderScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            VolumeBorderScale.ScaleX = stretchX;
            VolumeBorderScale.ScaleY = squeezeY;

            double clampedRatio = Math.Max(0.0, Math.Min(1.0, ratio));
            float volume = (float)clampedRatio;
            UpdateVolumeUI(volume);
 
            try
            {
                _audioDevice.AudioEndpointVolume.MasterVolumeLevelScalar = volume;
            }
            catch { }
        }

        private void SpringBackVolumeBorder()
        {
            var springDur = TimeSpan.FromMilliseconds(500);
            var springEase = new Animations.AppleSpringEase(0.5, 0.3);

            var scaleXAnim = new DoubleAnimation(VolumeBorderScale.ScaleX, 1.0, springDur) { EasingFunction = springEase };
            var scaleYAnim = new DoubleAnimation(VolumeBorderScale.ScaleY, 1.0, springDur) { EasingFunction = springEase };

            VolumeBorderScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleXAnim);
            VolumeBorderScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleYAnim);
        }

        private void PeakMeterTimer_Tick(object? sender, EventArgs e)
        {
            if (_settings.DisableVisualizer || _audioDevice == null || !VolumePopup.IsOpen) return;
            try
            {
                float peak = _audioDevice.AudioMeterInformation.MasterPeakValue;
                double targetScale = 1.0 + peak * 0.25;
                double currentScale = VolumeIconScale.ScaleX;
                double newScale = currentScale + (targetScale - currentScale) * 0.4;
                
                VolumeIconScale.ScaleX = newScale;
                VolumeIconScale.ScaleY = newScale;
            }
            catch { }
        }



        private void TimelineTimer_Tick(object? sender, EventArgs e)
        {
            UpdateTimelineBorder();
        }

        private void UpdateTimelineBorder(bool animate = true)
        {
            if (_settings.BorderMode != BorderMode.Timeline || !IsLoaded)
            {
                TimelineBorder.Visibility = Visibility.Collapsed;
                TimelineBorder2.Visibility = Visibility.Collapsed;
                return;
            }

            if (_settings.TimelineStyle == TimelineStyle.BothSides)
            {
                TimelineBorder.Visibility = Visibility.Visible;
                TimelineBorder2.Visibility = Visibility.Visible;
            }
            else
            {
                TimelineBorder.Visibility = Visibility.Visible;
                TimelineBorder2.Visibility = Visibility.Collapsed;
            }

            double w = MainBorder.Width;
            if (double.IsNaN(w) || w <= 0) w = MainBorder.ActualWidth;
            double h = MainBorder.Height;
            if (double.IsNaN(h) || h <= 0) h = MainBorder.ActualHeight;

            if (w <= 0 || h <= 0) return;

            double r = 16; // RadiusX/RadiusY
            double perimeter = 2 * (w - 2 * r) + 2 * (h - 2 * r) + 2 * Math.PI * r;

            if (TimelineBorder.StrokeDashArray == null || TimelineBorder.StrokeDashArray.Count == 0 || Math.Abs(TimelineBorder.StrokeDashArray[0] - perimeter) > 1.0)
            {
                TimelineBorder.StrokeDashArray = new DoubleCollection(new double[] { perimeter });
            }
            if (_settings.TimelineStyle == TimelineStyle.BothSides)
            {
                if (TimelineBorder2.StrokeDashArray == null || TimelineBorder2.StrokeDashArray.Count == 0 || Math.Abs(TimelineBorder2.StrokeDashArray[0] - perimeter) > 1.0)
                {
                    TimelineBorder2.StrokeDashArray = new DoubleCollection(new double[] { perimeter });
                }
            }

            var timeline = _mediaManager.GetTimelineProperties();
            if (timeline == null || timeline.EndTime.TotalMilliseconds <= 0)
            {
                TimelineBorder.BeginAnimation(System.Windows.Shapes.Shape.StrokeDashOffsetProperty, null);
                TimelineBorder.StrokeDashOffset = _settings.TimelineStyle == TimelineStyle.Flipped ? -perimeter : perimeter;
                if (_settings.TimelineStyle == TimelineStyle.BothSides)
                {
                    TimelineBorder2.BeginAnimation(System.Windows.Shapes.Shape.StrokeDashOffsetProperty, null);
                    TimelineBorder2.StrokeDashOffset = -perimeter;
                }
                return;
            }

            double durationMs = timeline.EndTime.TotalMilliseconds;
            double positionMs = timeline.Position.TotalMilliseconds;

            if (_mediaManager.IsPlaying)
            {
                var timeSinceUpdate = DateTimeOffset.UtcNow - timeline.LastUpdatedTime;
                if (timeSinceUpdate.TotalMilliseconds < 0) timeSinceUpdate = TimeSpan.Zero;
                positionMs += timeSinceUpdate.TotalMilliseconds;
            }

            if (positionMs > durationMs) positionMs = durationMs;
            if (positionMs < 0) positionMs = 0;

            double progress = positionMs / durationMs;

            double targetOffset1;
            double targetOffset2 = 0;
            double endOffset1;
            double endOffset2 = 0;

            if (_settings.TimelineStyle == TimelineStyle.Flipped)
            {
                targetOffset1 = -perimeter * (1.0 - progress);
                endOffset1 = 0;
            }
            else if (_settings.TimelineStyle == TimelineStyle.BothSides)
            {
                targetOffset1 = perimeter * (1.0 - 0.5 * progress);
                targetOffset2 = -perimeter * (1.0 - 0.5 * progress);
                endOffset1 = 0.5 * perimeter;
                endOffset2 = -0.5 * perimeter;
            }
            else // Default
            {
                targetOffset1 = perimeter * (1.0 - progress);
                endOffset1 = 0;
            }

            if (!animate || _settings.DisableTimelineAnimation)
            {
                TimelineBorder.BeginAnimation(System.Windows.Shapes.Shape.StrokeDashOffsetProperty, null);
                TimelineBorder.StrokeDashOffset = targetOffset1;
                if (_settings.TimelineStyle == TimelineStyle.BothSides)
                {
                    TimelineBorder2.BeginAnimation(System.Windows.Shapes.Shape.StrokeDashOffsetProperty, null);
                    TimelineBorder2.StrokeDashOffset = targetOffset2;
                }
            }
            else
            {
                if (!_mediaManager.IsPlaying)
                {
                    TimelineBorder.BeginAnimation(System.Windows.Shapes.Shape.StrokeDashOffsetProperty, null);
                    TimelineBorder.StrokeDashOffset = targetOffset1;
                    if (_settings.TimelineStyle == TimelineStyle.BothSides)
                    {
                        TimelineBorder2.BeginAnimation(System.Windows.Shapes.Shape.StrokeDashOffsetProperty, null);
                        TimelineBorder2.StrokeDashOffset = targetOffset2;
                    }
                }
                else
                {
                    // Animate TimelineBorder
                    double currentOffset1 = TimelineBorder.StrokeDashOffset;
                    if (double.IsNaN(currentOffset1) || currentOffset1 == 0 || Math.Abs(currentOffset1 - targetOffset1) > perimeter * 0.1)
                    {
                        TimelineBorder.BeginAnimation(System.Windows.Shapes.Shape.StrokeDashOffsetProperty, null);
                        TimelineBorder.StrokeDashOffset = targetOffset1;
                        currentOffset1 = targetOffset1;
                    }

                    double remainingMs = durationMs - positionMs;
                    if (remainingMs > 50)
                    {
                        var anim1 = new DoubleAnimation(currentOffset1, endOffset1, TimeSpan.FromMilliseconds(remainingMs));
                        TimelineBorder.BeginAnimation(System.Windows.Shapes.Shape.StrokeDashOffsetProperty, anim1);
                    }
                    else
                    {
                        TimelineBorder.BeginAnimation(System.Windows.Shapes.Shape.StrokeDashOffsetProperty, null);
                        TimelineBorder.StrokeDashOffset = endOffset1;
                    }

                    // Animate TimelineBorder2 if BothSides
                    if (_settings.TimelineStyle == TimelineStyle.BothSides)
                    {
                        double currentOffset2 = TimelineBorder2.StrokeDashOffset;
                        if (double.IsNaN(currentOffset2) || currentOffset2 == 0 || Math.Abs(currentOffset2 - targetOffset2) > perimeter * 0.1)
                        {
                            TimelineBorder2.BeginAnimation(System.Windows.Shapes.Shape.StrokeDashOffsetProperty, null);
                            TimelineBorder2.StrokeDashOffset = targetOffset2;
                            currentOffset2 = targetOffset2;
                        }

                        if (remainingMs > 50)
                        {
                            var anim2 = new DoubleAnimation(currentOffset2, endOffset2, TimeSpan.FromMilliseconds(remainingMs));
                            TimelineBorder2.BeginAnimation(System.Windows.Shapes.Shape.StrokeDashOffsetProperty, anim2);
                        }
                        else
                        {
                            TimelineBorder2.BeginAnimation(System.Windows.Shapes.Shape.StrokeDashOffsetProperty, null);
                            TimelineBorder2.StrokeDashOffset = endOffset2;
                        }
                    }
                }
            }
        }


    }
}