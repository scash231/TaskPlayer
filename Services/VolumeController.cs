// Manages volume popup interactions: drag, overdrag spring-back, peak meter, and UI updates.
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Application = System.Windows.Application;
using Point = System.Windows.Point;
using NAudio.CoreAudioApi;

namespace TaskbarMiniPlayer.Services
{
    /// <summary>
    /// Manages the volume slider popup: open/close animations, drag-to-set-volume with
    /// elastic overdrag, spring-back, peak meter visualization, and volume icon updates.
    /// </summary>
    public class VolumeController
    {
        // UI element references
        private readonly System.Windows.Controls.Primitives.Popup _popup;
        private readonly Grid _popupGrid;
        private readonly TranslateTransform _popupTranslate;
        private readonly Border _volumeBorder;
        private readonly ScaleTransform _volumeBorderScale;
        private readonly Border _volumeFill;
        private readonly Grid _volumeTrack;
        private readonly TextBlock _volumeIcon;
        private readonly ScaleTransform _volumeIconScale;

        private MMDevice? _audioDevice;
        private bool _isDragging;
        private DispatcherTimer? _peakMeterTimer;
        private readonly Func<bool> _isTaskbarAtTop;

        public bool IsDragging => _isDragging;

        public VolumeController(
            System.Windows.Controls.Primitives.Popup popup,
            Grid popupGrid,
            TranslateTransform popupTranslate,
            Border volumeBorder,
            ScaleTransform volumeBorderScale,
            Border volumeFill,
            Grid volumeTrack,
            TextBlock volumeIcon,
            ScaleTransform volumeIconScale,
            Func<bool> isTaskbarAtTop)
        {
            _popup = popup;
            _popupGrid = popupGrid;
            _popupTranslate = popupTranslate;
            _volumeBorder = volumeBorder;
            _volumeBorderScale = volumeBorderScale;
            _volumeFill = volumeFill;
            _volumeTrack = volumeTrack;
            _volumeIcon = volumeIcon;
            _volumeIconScale = volumeIconScale;
            _isTaskbarAtTop = isTaskbarAtTop;
        }

        /// <summary>
        /// Initialize the audio device and peak meter timer. Call once after window loads.
        /// </summary>
        public void Initialize(int peakMeterIntervalMs)
        {
            try
            {
                var enumerator = new MMDeviceEnumerator();
                _audioDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                _audioDevice.AudioEndpointVolume.OnVolumeNotification += OnVolumeNotification;
                UpdateUI(_audioDevice.AudioEndpointVolume.MasterVolumeLevelScalar);
            }
            catch (Exception ex) { Log.Error("[VolumeController] Failed to initialize audio device", ex); }

            _peakMeterTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(peakMeterIntervalMs)
            };
            _peakMeterTimer.Tick += PeakMeterTimer_Tick;
        }

        /// <summary>
        /// Reconfigure the peak meter timer interval.
        /// </summary>
        public void SetPeakMeterInterval(int ms)
        {
            if (_peakMeterTimer != null)
                _peakMeterTimer.Interval = TimeSpan.FromMilliseconds(ms);
        }

        /// <summary>
        /// Handles external volume change notifications from the OS.
        /// </summary>
        public void OnExternalVolumeChange(Action<Action> dispatchAsync)
        {
            // Already handled by OnVolumeNotification subscribing directly
        }

        private void OnVolumeNotification(AudioVolumeNotificationData data)
        {
            if (!_isDragging)
            {
                Application.Current?.Dispatcher?.InvokeAsync(() => UpdateUI(data.MasterVolume));
            }
        }

        public void UpdateUI(float volumeLevel)
        {
            _volumeFill.Width = _volumeTrack.ActualWidth * volumeLevel;
            if (_volumeFill.Width > 16)
                _volumeFill.CornerRadius = new CornerRadius(16, volumeLevel >= 0.98f ? 16 : 0, volumeLevel >= 0.98f ? 16 : 0, 16);
            else
                _volumeFill.CornerRadius = new CornerRadius(16, 0, 0, 16);

            if (volumeLevel == 0)
                _volumeIcon.Text = "\uE74F";
            else if (volumeLevel <= 0.33f)
                _volumeIcon.Text = "\uE992";
            else if (volumeLevel <= 0.66f)
                _volumeIcon.Text = "\uE993";
            else
                _volumeIcon.Text = "\uE994";
        }

        public void Open(bool disableVisualizer)
        {
            if (_popup.IsOpen) return;
            if (_volumeTrack.ActualWidth > 0 && _audioDevice != null)
            {
                UpdateUI(_audioDevice.AudioEndpointVolume.MasterVolumeLevelScalar);
            }

            bool isTop = _isTaskbarAtTop();
            _popup.Placement = isTop
                ? System.Windows.Controls.Primitives.PlacementMode.Bottom
                : System.Windows.Controls.Primitives.PlacementMode.Top;
            _popup.VerticalOffset = isTop ? 8 : -8;

            _popup.IsOpen = true;
            _popupGrid.IsHitTestVisible = true;

            var fadeDur = TimeSpan.FromMilliseconds(200);
            var springDur = TimeSpan.FromMilliseconds(400);

            var fadeIn = new DoubleAnimation(0, 1, fadeDur);
            double startY = isTop ? -20 : 20;
            var slide = new DoubleAnimation(startY, 0, springDur) { EasingFunction = new Animations.AppleSpringEase(0.8, 0.4) };

            _popupGrid.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            _popupTranslate.BeginAnimation(TranslateTransform.YProperty, slide);

            if (!disableVisualizer)
            {
                _peakMeterTimer?.Start();
            }
        }

        public void Close()
        {
            if (!_popup.IsOpen) return;
            _popupGrid.IsHitTestVisible = false;

            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150));
            fadeOut.Completed += (s, ev) => { _popup.IsOpen = false; };
            _popupGrid.BeginAnimation(UIElement.OpacityProperty, fadeOut);

            _isDragging = false;
            _volumeTrack.ReleaseMouseCapture();

            _peakMeterTimer?.Stop();
            if (_volumeIconScale != null)
            {
                _volumeIconScale.ScaleX = 1.0;
                _volumeIconScale.ScaleY = 1.0;
            }
        }

        public void OnTrackMouseDown(MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                _isDragging = true;
                _volumeTrack.CaptureMouse();
                UpdateVolumeFromMouse(e);
            }
        }

        public void OnTrackMouseMove(MouseEventArgs e)
        {
            if (_isDragging && e.LeftButton == MouseButtonState.Pressed)
            {
                UpdateVolumeFromMouse(e);
            }
        }

        public void OnTrackMouseUp(MouseButtonEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                _volumeTrack.ReleaseMouseCapture();
                UpdateVolumeFromMouse(e);
                SpringBackBorder();
            }
        }

        private void UpdateVolumeFromMouse(MouseEventArgs e)
        {
            if (_audioDevice == null) return;

            var pos = e.GetPosition(_volumeTrack);
            double width = _volumeTrack.ActualWidth;
            if (width <= 0) return;

            double ratio = pos.X / width;
            double stretchX = 1.0;
            double squeezeY = 1.0;

            if (ratio > 1.0)
            {
                double overDrag = Math.Min(pos.X - width, 150.0);
                stretchX = 1.0 + (overDrag / width) * 0.4;
                squeezeY = 1.0 - (stretchX - 1.0) * 0.5;
                _volumeBorder.RenderTransformOrigin = new Point(0.0, 0.5);
            }
            else if (ratio < 0.0)
            {
                double overDrag = Math.Min(-pos.X, 150.0);
                stretchX = 1.0 + (overDrag / width) * 0.4;
                squeezeY = 1.0 - (stretchX - 1.0) * 0.5;
                _volumeBorder.RenderTransformOrigin = new Point(1.0, 0.5);
            }
            else
            {
                _volumeBorder.RenderTransformOrigin = new Point(0.5, 0.5);
            }

            _volumeBorderScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            _volumeBorderScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            _volumeBorderScale.ScaleX = stretchX;
            _volumeBorderScale.ScaleY = squeezeY;

            double clampedRatio = Math.Clamp(ratio, 0.0, 1.0);
            float volume = (float)clampedRatio;
            UpdateUI(volume);

            try
            {
                _audioDevice.AudioEndpointVolume.MasterVolumeLevelScalar = volume;
            }
            catch (Exception ex) { Log.Warn($"[VolumeController] Failed to set volume: {ex.Message}"); }
        }

        private void SpringBackBorder()
        {
            var springDur = TimeSpan.FromMilliseconds(500);
            var springEase = new Animations.AppleSpringEase(0.5, 0.3);

            var scaleXAnim = new DoubleAnimation(_volumeBorderScale.ScaleX, 1.0, springDur) { EasingFunction = springEase };
            var scaleYAnim = new DoubleAnimation(_volumeBorderScale.ScaleY, 1.0, springDur) { EasingFunction = springEase };

            _volumeBorderScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleXAnim);
            _volumeBorderScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleYAnim);
        }

        private void PeakMeterTimer_Tick(object? sender, EventArgs e)
        {
            if (_audioDevice == null || !_popup.IsOpen) return;
            try
            {
                float peak = _audioDevice.AudioMeterInformation.MasterPeakValue;
                double targetScale = 1.0 + peak * 0.25;
                double currentScale = _volumeIconScale.ScaleX;
                double newScale = currentScale + (targetScale - currentScale) * 0.4;

                _volumeIconScale.ScaleX = newScale;
                _volumeIconScale.ScaleY = newScale;
            }
            catch (Exception ex) { Log.Warn($"[VolumeController] Peak meter error: {ex.Message}"); }
        }

        /// <summary>
        /// Repositions the popup by nudging HorizontalOffset to force WPF to recalculate.
        /// This is a workaround for WPF's Popup not automatically repositioning when the
        /// PlacementTarget moves — toggling the offset by a sub-pixel amount forces a layout pass.
        /// </summary>
        public void RepositionPopup()
        {
            if (_popup != null && _popup.IsOpen)
            {
                var offset = _popup.HorizontalOffset;
                _popup.HorizontalOffset = offset + 0.01;
                _popup.HorizontalOffset = offset;
            }
        }

        /// <summary>
        /// Clean up audio device resources.
        /// </summary>
        public void Dispose()
        {
            _peakMeterTimer?.Stop();
            if (_audioDevice != null)
            {
                try
                {
                    _audioDevice.AudioEndpointVolume.OnVolumeNotification -= OnVolumeNotification;
                    _audioDevice.Dispose();
                }
                catch (Exception ex) { Log.Warn($"[VolumeController] Dispose error: {ex.Message}"); }
                _audioDevice = null;
            }
        }
    }
}
