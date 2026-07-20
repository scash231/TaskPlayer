// Main player taskbar overlay window.
using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Windows.Media.Animation;
using System.Collections.Generic;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TaskbarMiniPlayer.Services;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace TaskbarMiniPlayer
{
    public partial class MainWindow : Window
    {
        // ── Layout Constants ──
        private const double ButtonPanelRightMargin = 14;
        private const double SessionIndicatorLeftMargin = 14;
        private const double SessionIndicatorWidth = 4;
        private const double SessionIndicatorTotalWidth = 18; // margin + width
        private const double AlbumArtWidth = 32;
        private const double AlbumArtLeftMarginWithIndicator = 8;
        private const double AlbumArtLeftMarginDefault = 14;
        private const double AlbumArtRightMargin = 4;
        private const double MetadataPanelLeftMargin = 4;
        private const double MetadataPanelRightMargin = 8;
        private const double MetadataPanelLeftMarginNoArt = 14;
        private const double SafetyPadding = 16;
        private const double CompactSidePadding = 30;
        private const double CornerRadius = 16;

        // ── Core services ──
        private readonly MediaManager _mediaManager;
        public MediaManager MediaManagerInstance => _mediaManager;
        private Settings _settings;
        private IntPtr _winEventHook;
        private Win32.WinEventDelegate _winEventDelegate;
        private HwndSource? _source;
        private HotkeyManager? _hotkeyManager;
        private readonly ColorExtractor _colorExtractor = new();

        // Extracted services
        private MarqueeController? _marqueeController;
        private VolumeController? _volumeController;
        private TimelineBorderAnimator? _timelineBorderAnimator;

        // ── Timers ──
        private DispatcherTimer _autoHideTimer;
        private DispatcherTimer _zOrderTimer;
        private DispatcherTimer _timelineTimer;

        // ── State ──
        private bool _isAutoHidden = false;
        private bool _wasHiddenForFullscreen = false;
        private bool _isAnimatingWidth = false;

        /// <summary>
        /// Helper that returns the app's accent brush, with a safe fallback.
        /// </summary>
        private SolidColorBrush AccentBrush =>
            Resources["Accent"] as SolidColorBrush ?? new SolidColorBrush(Colors.DeepSkyBlue);

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
            if (!_settings.DisableTopmostTimer)
                _zOrderTimer.Start();

            _timelineTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _timelineTimer.Tick += (s, e) => _timelineBorderAnimator?.Tick(_settings, _mediaManager);

            _timelineBorderAnimator = new TimelineBorderAnimator(TimelineBorder, TimelineBorder2, MainBorder);
            MainBorder.SizeChanged += (s, e) => _timelineBorderAnimator.Update(_settings, _mediaManager, animate: false);

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

            // Initialize extracted services
            _marqueeController = new MarqueeController(
                TxtTitle, TxtArtist, TitleGrid, TitleCanvas, ArtistCanvas, ArtistGrid,
                isHoveringFunc: () => MainBorder.IsMouseOver || (VolumePopup.IsOpen && VolumePopupGrid.IsMouseOver),
                dpiScaleFunc: () => VisualTreeHelper.GetDpi(this).PixelsPerDip);

            _volumeController = new VolumeController(
                VolumePopup, VolumePopupGrid, VolumePopupTranslate,
                VolumeBorder, VolumeBorderScale, VolumeFill, VolumeTrack,
                VolumeIcon, VolumeIconScale,
                isTaskbarAtTop: IsTaskbarAtTop);
            _volumeController.Initialize(_settings.PeakMeterIntervalMs);

            await _mediaManager.InitializeAsync();

            if (_settings.AutoPlayOnLaunch && !_mediaManager.IsPlaying)
            {
                try { await _mediaManager.PlayPauseAsync(); }
                catch (Exception ex) { Log.Warn($"[MainWindow] Auto-play failed: {ex.Message}"); }
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
                catch (Exception ex) { Log.Error("[MainWindow] Failed to apply TranslucentIco on startup", ex); }
            }
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            _timelineTimer.Stop();
            _hotkeyManager?.UnregisterAll();
            _volumeController?.Dispose();

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
                catch (Exception ex) { Log.Error("[MainWindow] Failed to apply TranslucentIco on reload", ex); }
            }
            else
            {
                try
                {
                    var translucentSettings = TranslucentIcoSettings.Load();
                    TranslucentIcoService.SetDesktopIconOpacity(255, translucentSettings.Layer);
                }
                catch (Exception ex) { Log.Error("[MainWindow] Failed to reset TranslucentIco on reload", ex); }
            }

            if (_zOrderTimer != null)
            {
                _zOrderTimer.Interval = TimeSpan.FromMilliseconds(_settings.TopmostIntervalMs);
                if (_settings.DisableTopmostTimer)
                    _zOrderTimer.Stop();
                else
                    _zOrderTimer.Start();
            }
            _volumeController?.SetPeakMeterInterval(_settings.PeakMeterIntervalMs);
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

        // ══════ Media State Handling ══════

        private void OnMediaStateChanged()
        {
            Dispatcher.InvokeAsync(() =>
            {
                BtnPlay.Content = _mediaManager?.IsPlaying == true ? "\uE769" : "\uE768";

                string? newTitle = _mediaManager?.Title;
                if (string.IsNullOrEmpty(newTitle)) newTitle = "Unknown";
                string? newArtist = _mediaManager?.Artist;
                if (string.IsNullOrEmpty(newArtist)) newArtist = "Unknown";
                bool songChanged = TxtTitle.Text != newTitle || TxtArtist.Text != newArtist;

                byte alpha = _settings.IsTransparent ? (byte)(_settings.BackgroundOpacity * 255) : (byte)255;
                double tintStrength = _settings.AdaptiveTintStrength;

                var bmp = _mediaManager?.AlbumArt as BitmapSource;
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

                    TimelineBorder.Stroke = AccentBrush;
                    TimelineBorder2.Stroke = AccentBrush;
                }

                if (_mediaManager?.IsPlaying == true)
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
                        _marqueeController?.Update(_settings, _mediaManager, _isAnimatingWidth, targetMetadataWidth);

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
                    if (_mediaManager?.IsPlaying == true)
                    {
                        _timelineTimer.Start();
                    }
                    else
                    {
                        _timelineTimer.Stop();
                    }
                    _timelineBorderAnimator?.Update(_settings, _mediaManager, animate: true);
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
                    brushes.Add(new SolidColorBrush(i == _mediaManager.CurrentSessionIndex ? Colors.White : Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)));
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

        // ══════ Auto-Hide ══════

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

        // ══════ Settings Application ══════

        public void ApplySettings(bool animate = false)
        {
            if (_zOrderTimer != null)
            {
                _zOrderTimer.Interval = TimeSpan.FromMilliseconds(_settings.TopmostIntervalMs);
                if (_settings.DisableTopmostTimer)
                    _zOrderTimer.Stop();
                else
                    _zOrderTimer.Start();
            }
            if (_timelineTimer != null)
            {
                _timelineTimer.Interval = _settings.OptimizeTimerFrequencies ? TimeSpan.FromMilliseconds(2000) : TimeSpan.FromMilliseconds(500);
            }

            try
            {
                var accentColor = (Color)ColorConverter.ConvertFromString(_settings.CustomAccentColor);
                Resources["Accent"] = new SolidColorBrush(accentColor);
            }
            catch (Exception ex) { Log.Warn($"[MainWindow] Failed to parse accent color '{_settings.CustomAccentColor}': {ex.Message}"); }

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
                        var accentColor = (Color)ColorConverter.ConvertFromString(_settings.CustomAccentColor);
                        strokeBrush = new SolidColorBrush(accentColor);
                    }
                    catch
                    {
                        strokeBrush = AccentBrush;
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
                _timelineBorderAnimator?.Update(_settings, _mediaManager!, animate: false);
            }

            if (_settings.Layout == LayoutStyle.Compact)
            {
                ColAlbumArt.Width = new GridLength(0);
                ColMetadata.Width = new GridLength(0);
                ImgAlbumArt.Visibility = Visibility.Collapsed;
                PanelMetadata.Visibility = Visibility.Collapsed;
                PanelButtons.Margin = new Thickness(ButtonPanelRightMargin, 0, ButtonPanelRightMargin, 0);
            }
            else if (_settings.Layout == LayoutStyle.Standard)
            {
                ColAlbumArt.Width = new GridLength(0);
                ColMetadata.Width = new GridLength(1, GridUnitType.Star);
                ImgAlbumArt.Visibility = Visibility.Collapsed;
                PanelMetadata.Visibility = Visibility.Visible;
                PanelButtons.Margin = new Thickness(0, 0, ButtonPanelRightMargin, 0);
            }
            else // Expanded
            {
                ColAlbumArt.Width = new GridLength(1, GridUnitType.Auto);
                ColMetadata.Width = new GridLength(1, GridUnitType.Star);
                ImgAlbumArt.Visibility = Visibility.Visible;
                PanelMetadata.Visibility = Visibility.Visible;
                PanelButtons.Margin = new Thickness(0, 0, ButtonPanelRightMargin, 0);
            }

            bool showSessionIndicator = _mediaManager != null && _mediaManager.TotalSessions > 1;

            if (showSessionIndicator)
            {
                SessionIndicator.Margin = new Thickness(SessionIndicatorLeftMargin, 0, 0, 0);
                ImgAlbumArt.Margin = new Thickness(AlbumArtLeftMarginWithIndicator, 4, AlbumArtRightMargin, 4);
                PanelMetadata.Margin = new Thickness(MetadataPanelLeftMargin, 0, MetadataPanelRightMargin, 0);
            }
            else if (_settings.Layout == LayoutStyle.Expanded)
            {
                ImgAlbumArt.Margin = new Thickness(AlbumArtLeftMarginDefault, 4, AlbumArtRightMargin, 4);
                PanelMetadata.Margin = new Thickness(MetadataPanelLeftMargin, 0, MetadataPanelRightMargin, 0);
            }
            else // Standard layout (no album art, no session indicator)
            {
                PanelMetadata.Margin = new Thickness(MetadataPanelLeftMarginNoArt, 0, MetadataPanelRightMargin, 0);
            }

            // Set Window width. If dynamic sizing is disabled, use calculated target width.
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
                _marqueeController?.Update(_settings, _mediaManager, _isAnimatingWidth);
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

        // ══════ Width Calculation ══════

        private double CalculateNonTextWidth()
        {
            if (_settings.Layout == LayoutStyle.Compact)
            {
                double width = (_settings.ButtonSize * 3) + CompactSidePadding;
                if (_mediaManager != null && _mediaManager.TotalSessions > 1)
                {
                    width += SessionIndicatorTotalWidth;
                }
                return width;
            }

            double nonTextWidth = (_settings.ButtonSize * 3) + ButtonPanelRightMargin;
            if (_mediaManager != null && _mediaManager.TotalSessions > 1)
            {
                nonTextWidth += SessionIndicatorTotalWidth;
            }

            if (_settings.Layout == LayoutStyle.Expanded)
            {
                if (_mediaManager != null && _mediaManager.TotalSessions > 1)
                {
                    nonTextWidth += AlbumArtLeftMarginWithIndicator + AlbumArtWidth + AlbumArtRightMargin;
                }
                else
                {
                    nonTextWidth += AlbumArtLeftMarginDefault + AlbumArtWidth + AlbumArtRightMargin;
                }
            }

            // Metadata panel margins + safety padding
            if (_mediaManager != null && _mediaManager.TotalSessions <= 1 && _settings.Layout == LayoutStyle.Standard)
            {
                nonTextWidth += (MetadataPanelLeftMarginNoArt + MetadataPanelRightMargin) + SafetyPadding;
            }
            else
            {
                nonTextWidth += (MetadataPanelLeftMargin + MetadataPanelRightMargin) + SafetyPadding;
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
                width = (_settings.ButtonSize * 3) + CompactSidePadding;
                if (_mediaManager != null && _mediaManager.TotalSessions > 1)
                {
                    width += SessionIndicatorTotalWidth;
                }
            }
            else if (_settings.EnableDynamicSizing)
            {
                string titleText = string.IsNullOrEmpty(_mediaManager?.Title) ? "Unknown" : _mediaManager.Title;
                string artistText = string.IsNullOrEmpty(_mediaManager?.Artist) ? "Unknown" : _mediaManager.Artist;

                double maxTextW = 0;
                if (_settings.HideArtist || _settings.DynamicSizingTarget == SizingTarget.TitleOnly)
                {
                    maxTextW = _marqueeController?.GetTextWidth(TxtTitle, titleText) ?? 120;
                }
                else if (_settings.DynamicSizingTarget == SizingTarget.ArtistOnly)
                {
                    maxTextW = _marqueeController?.GetTextWidth(TxtArtist, artistText) ?? 120;
                }
                else // Both
                {
                    double titleW = _marqueeController?.GetTextWidth(TxtTitle, titleText) ?? 120;
                    double artistW = _marqueeController?.GetTextWidth(TxtArtist, artistText) ?? 120;
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
                    baseWidth += SessionIndicatorTotalWidth;
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
                _marqueeController?.Update(_settings, _mediaManager, _isAnimatingWidth);
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
                _marqueeController?.Update(_settings, _mediaManager, _isAnimatingWidth);
                onComplete?.Invoke();
            };

            MainBorder.BeginAnimation(WidthProperty, widthAnim);
        }

        // ══════ Positioning ══════

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            if (IsLoaded)
            {
                Reposition();
                _volumeController?.RepositionPopup();
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
                catch (Exception ex) { Log.Warn($"[MainWindow] Failed to reparent to taskbar: {ex.Message}"); }
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

        // ══════ Button Click Handlers ══════

        private async void BtnPrev_Click(object sender, RoutedEventArgs e) => await _mediaManager.SkipPreviousAsync();
        private async void BtnPlay_Click(object sender, RoutedEventArgs e) => await _mediaManager.PlayPauseAsync();
        private async void BtnNext_Click(object sender, RoutedEventArgs e) => await _mediaManager.SkipNextAsync();

        // ══════ Mouse Interaction ══════

        private void MainBorder_MouseEnter(object sender, MouseEventArgs e)
        {
            if (_settings.EnableVolumeSlider)
            {
                _volumeController?.Open(_settings.DisableVisualizer);
            }
            if (_settings.ScrollBehavior == "Hover")
            {
                _marqueeController?.Update(_settings, _mediaManager, _isAnimatingWidth);
            }
        }

        private async void MainBorder_MouseLeave(object sender, MouseEventArgs e)
        {
            await System.Threading.Tasks.Task.Delay(50);
            if (!MainBorder.IsMouseOver && !VolumePopupGrid.IsMouseOver)
            {
                _volumeController?.Close();
            }
            if (_settings.ScrollBehavior == "Hover")
            {
                _marqueeController?.Update(_settings, _mediaManager, _isAnimatingWidth);
            }
        }

        private void VolumePopup_MouseEnter(object sender, MouseEventArgs e)
        {
            if (_settings.ScrollBehavior == "Hover")
            {
                _marqueeController?.Update(_settings, _mediaManager, _isAnimatingWidth);
            }
        }

        private async void VolumePopup_MouseLeave(object sender, MouseEventArgs e)
        {
            await System.Threading.Tasks.Task.Delay(50);
            if (!MainBorder.IsMouseOver && !VolumePopupGrid.IsMouseOver)
            {
                _volumeController?.Close();
            }
            if (_settings.ScrollBehavior == "Hover")
            {
                _marqueeController?.Update(_settings, _mediaManager, _isAnimatingWidth);
            }
        }

        private void MainBorder_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left && e.ClickCount == 2)
            {
                _mediaManager.FocusActiveMediaApp();
            }
            else if (e.ChangedButton == MouseButton.Right)
            {
                ToggleLayout();
            }
            else if (e.ChangedButton == MouseButton.Middle)
            {
                ShowSettings();
            }
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
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
                _marqueeController?.Update(_settings, _mediaManager, _isAnimatingWidth));
        }

        // ══════ Volume Track Events (delegated to VolumeController) ══════

        private void VolumeTrack_MouseDown(object sender, MouseButtonEventArgs e)
            => _volumeController?.OnTrackMouseDown(e);

        private void VolumeTrack_MouseMove(object sender, MouseEventArgs e)
            => _volumeController?.OnTrackMouseMove(e);

        private void VolumeTrack_MouseUp(object sender, MouseButtonEventArgs e)
            => _volumeController?.OnTrackMouseUp(e);

        // ══════ Settings & Layout ══════

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
            var tw = new TranslucentIcoWindow(rect, this);
            tw.Show();
        }
    }
}