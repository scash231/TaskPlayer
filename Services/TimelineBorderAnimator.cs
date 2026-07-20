// Animates the timeline progress border around the player.
using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Controls;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace TaskbarMiniPlayer.Services
{
    /// <summary>
    /// Manages the animated timeline border that shows playback progress
    /// by manipulating StrokeDashOffset around the rounded rectangle border.
    /// </summary>
    public class TimelineBorderAnimator
    {
        private readonly Rectangle _border1;
        private readonly Rectangle _border2;
        private readonly Border _mainBorder;

        public TimelineBorderAnimator(Rectangle timelineBorder, Rectangle timelineBorder2, Border mainBorder)
        {
            _border1 = timelineBorder;
            _border2 = timelineBorder2;
            _mainBorder = mainBorder;
        }

        /// <summary>
        /// Called on each timer tick while playing.
        /// </summary>
        public void Tick(Settings settings, MediaManager mediaManager)
        {
            Update(settings, mediaManager, animate: true);
        }

        /// <summary>
        /// Updates the timeline border dash offset based on current playback position.
        /// </summary>
        public void Update(Settings settings, MediaManager mediaManager, bool animate = true)
        {
            if (settings.BorderMode != BorderMode.Timeline || !_mainBorder.IsLoaded)
            {
                _border1.Visibility = Visibility.Collapsed;
                _border2.Visibility = Visibility.Collapsed;
                return;
            }

            if (settings.TimelineStyle == TimelineStyle.BothSides)
            {
                _border1.Visibility = Visibility.Visible;
                _border2.Visibility = Visibility.Visible;
            }
            else
            {
                _border1.Visibility = Visibility.Visible;
                _border2.Visibility = Visibility.Collapsed;
            }

            double w = _mainBorder.Width;
            if (double.IsNaN(w) || w <= 0) w = _mainBorder.ActualWidth;
            double h = _mainBorder.Height;
            if (double.IsNaN(h) || h <= 0) h = _mainBorder.ActualHeight;

            if (w <= 0 || h <= 0) return;

            const double r = 16; // RadiusX/RadiusY matching the XAML
            double perimeter = 2 * (w - 2 * r) + 2 * (h - 2 * r) + 2 * Math.PI * r;

            // Update StrokeDashArray if the border size changed
            if (_border1.StrokeDashArray == null || _border1.StrokeDashArray.Count == 0 || Math.Abs(_border1.StrokeDashArray[0] - perimeter) > 1.0)
            {
                _border1.StrokeDashArray = new DoubleCollection(new double[] { perimeter });
            }
            if (settings.TimelineStyle == TimelineStyle.BothSides)
            {
                if (_border2.StrokeDashArray == null || _border2.StrokeDashArray.Count == 0 || Math.Abs(_border2.StrokeDashArray[0] - perimeter) > 1.0)
                {
                    _border2.StrokeDashArray = new DoubleCollection(new double[] { perimeter });
                }
            }

            var timeline = mediaManager.GetTimelineProperties();
            if (timeline == null || timeline.EndTime.TotalMilliseconds <= 0)
            {
                _border1.BeginAnimation(Shape.StrokeDashOffsetProperty, null);
                _border1.StrokeDashOffset = settings.TimelineStyle == TimelineStyle.Flipped ? -perimeter : perimeter;
                if (settings.TimelineStyle == TimelineStyle.BothSides)
                {
                    _border2.BeginAnimation(Shape.StrokeDashOffsetProperty, null);
                    _border2.StrokeDashOffset = -perimeter;
                }
                return;
            }

            double durationMs = timeline.EndTime.TotalMilliseconds;
            double positionMs = timeline.Position.TotalMilliseconds;

            if (mediaManager.IsPlaying)
            {
                var timeSinceUpdate = DateTimeOffset.UtcNow - timeline.LastUpdatedTime;
                if (timeSinceUpdate.TotalMilliseconds < 0) timeSinceUpdate = TimeSpan.Zero;
                positionMs += timeSinceUpdate.TotalMilliseconds;
            }

            positionMs = Math.Clamp(positionMs, 0, durationMs);
            double progress = positionMs / durationMs;

            double targetOffset1;
            double targetOffset2 = 0;
            double endOffset1;
            double endOffset2 = 0;

            if (settings.TimelineStyle == TimelineStyle.Flipped)
            {
                targetOffset1 = -perimeter * (1.0 - progress);
                endOffset1 = 0;
            }
            else if (settings.TimelineStyle == TimelineStyle.BothSides)
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

            if (!animate || settings.DisableTimelineAnimation)
            {
                _border1.BeginAnimation(Shape.StrokeDashOffsetProperty, null);
                _border1.StrokeDashOffset = targetOffset1;
                if (settings.TimelineStyle == TimelineStyle.BothSides)
                {
                    _border2.BeginAnimation(Shape.StrokeDashOffsetProperty, null);
                    _border2.StrokeDashOffset = targetOffset2;
                }
            }
            else
            {
                if (!mediaManager.IsPlaying)
                {
                    _border1.BeginAnimation(Shape.StrokeDashOffsetProperty, null);
                    _border1.StrokeDashOffset = targetOffset1;
                    if (settings.TimelineStyle == TimelineStyle.BothSides)
                    {
                        _border2.BeginAnimation(Shape.StrokeDashOffsetProperty, null);
                        _border2.StrokeDashOffset = targetOffset2;
                    }
                }
                else
                {
                    // Animate border1
                    double currentOffset1 = _border1.StrokeDashOffset;
                    if (double.IsNaN(currentOffset1) || currentOffset1 == 0 || Math.Abs(currentOffset1 - targetOffset1) > perimeter * 0.1)
                    {
                        _border1.BeginAnimation(Shape.StrokeDashOffsetProperty, null);
                        _border1.StrokeDashOffset = targetOffset1;
                        currentOffset1 = targetOffset1;
                    }

                    double remainingMs = durationMs - positionMs;
                    if (remainingMs > 50)
                    {
                        var anim1 = new DoubleAnimation(currentOffset1, endOffset1, TimeSpan.FromMilliseconds(remainingMs));
                        _border1.BeginAnimation(Shape.StrokeDashOffsetProperty, anim1);
                    }
                    else
                    {
                        _border1.BeginAnimation(Shape.StrokeDashOffsetProperty, null);
                        _border1.StrokeDashOffset = endOffset1;
                    }

                    // Animate border2 if BothSides
                    if (settings.TimelineStyle == TimelineStyle.BothSides)
                    {
                        double currentOffset2 = _border2.StrokeDashOffset;
                        if (double.IsNaN(currentOffset2) || currentOffset2 == 0 || Math.Abs(currentOffset2 - targetOffset2) > perimeter * 0.1)
                        {
                            _border2.BeginAnimation(Shape.StrokeDashOffsetProperty, null);
                            _border2.StrokeDashOffset = targetOffset2;
                            currentOffset2 = targetOffset2;
                        }

                        if (remainingMs > 50)
                        {
                            var anim2 = new DoubleAnimation(currentOffset2, endOffset2, TimeSpan.FromMilliseconds(remainingMs));
                            _border2.BeginAnimation(Shape.StrokeDashOffsetProperty, anim2);
                        }
                        else
                        {
                            _border2.BeginAnimation(Shape.StrokeDashOffsetProperty, null);
                            _border2.StrokeDashOffset = endOffset2;
                        }
                    }
                }
            }
        }
    }
}
