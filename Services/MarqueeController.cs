// Handles marquee (scrolling text) animation for title and artist labels.
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using FlowDirection = System.Windows.FlowDirection;
using Brushes = System.Windows.Media.Brushes;

namespace TaskbarMiniPlayer.Services
{
    /// <summary>
    /// Manages marquee text scrolling for the title and artist TextBlocks.
    /// </summary>
    public class MarqueeController
    {
        private readonly TextBlock _txtTitle;
        private readonly TextBlock _txtArtist;
        private readonly Grid _titleGrid;
        private readonly Canvas _titleCanvas;
        private readonly Canvas _artistCanvas;
        private readonly Grid _artistGrid;
        private readonly Func<bool> _isHoveringFunc;
        private readonly Func<double> _dpiScaleFunc;

        public MarqueeController(
            TextBlock txtTitle,
            TextBlock txtArtist,
            Grid titleGrid,
            Canvas titleCanvas,
            Canvas artistCanvas,
            Grid artistGrid,
            Func<bool> isHoveringFunc,
            Func<double> dpiScaleFunc)
        {
            _txtTitle = txtTitle;
            _txtArtist = txtArtist;
            _titleGrid = titleGrid;
            _titleCanvas = titleCanvas;
            _artistCanvas = artistCanvas;
            _artistGrid = artistGrid;
            _isHoveringFunc = isHoveringFunc;
            _dpiScaleFunc = dpiScaleFunc;
        }

        /// <summary>
        /// Updates the marquee layout for both title and artist text. Pass an override container width
        /// during song transitions when the final width is known but not yet measured.
        /// </summary>
        public void Update(Settings settings, MediaManager mediaManager, bool isAnimatingWidth, double? overrideContainerWidth = null)
        {
            if (isAnimatingWidth && overrideContainerWidth == null) return;

            _txtTitle.BeginAnimation(Canvas.LeftProperty, null);
            _txtArtist.BeginAnimation(Canvas.LeftProperty, null);
            _txtTitle.BeginAnimation(UIElement.OpacityProperty, null);
            _txtArtist.BeginAnimation(UIElement.OpacityProperty, null);

            Canvas.SetLeft(_txtTitle, 0);
            Canvas.SetLeft(_txtArtist, 0);
            _txtTitle.Opacity = 1;
            _txtArtist.Opacity = 1;

            bool shouldScroll = settings.ScrollLongText && !settings.DisableTextScrolling;
            if (shouldScroll && settings.ScrollBehavior == "Hover" && !_isHoveringFunc())
            {
                shouldScroll = false;
            }

            string titleText = string.IsNullOrEmpty(mediaManager.Title) ? "Unknown" : mediaManager.Title;
            string artistText = string.IsNullOrEmpty(mediaManager.Artist) ? "Unknown" : mediaManager.Artist;

            double titleW = GetTextWidth(_txtTitle, titleText);
            double artistW = GetTextWidth(_txtArtist, artistText);

            double containerWidth = overrideContainerWidth ?? (_titleGrid.ActualWidth > 0 ? _titleGrid.ActualWidth : 120.0);

            if (shouldScroll && titleW > containerWidth && containerWidth > 0)
            {
                _titleCanvas.HorizontalAlignment = HorizontalAlignment.Left;
                _titleCanvas.ClearValue(FrameworkElement.WidthProperty);

                string spacer = "        ";
                _txtTitle.Text = titleText + spacer + titleText;
                double singleWidth = GetTextWidth(_txtTitle, titleText + spacer);
                StartAnimation(_txtTitle, singleWidth, containerWidth, settings);
            }
            else
            {
                _txtTitle.Text = titleText;
                if (settings.HideArtist)
                {
                    _txtTitle.Width = double.NaN;
                    _titleCanvas.HorizontalAlignment = HorizontalAlignment.Center;
                    _titleCanvas.Width = titleW;
                    Canvas.SetLeft(_txtTitle, 0);
                }
                else
                {
                    _txtTitle.Width = containerWidth > 0 ? containerWidth : double.NaN;
                    _titleCanvas.HorizontalAlignment = HorizontalAlignment.Left;
                    _titleCanvas.ClearValue(FrameworkElement.WidthProperty);
                }
            }

            if (shouldScroll && artistW > containerWidth && containerWidth > 0)
            {
                string spacer = "        ";
                _txtArtist.Text = artistText + spacer + artistText;
                double singleWidth = GetTextWidth(_txtArtist, artistText + spacer);
                StartAnimation(_txtArtist, singleWidth, containerWidth, settings);
            }
            else
            {
                _txtArtist.Text = artistText;
                _txtArtist.Width = containerWidth > 0 ? containerWidth : double.NaN;
            }
        }

        public double GetTextWidth(TextBlock tb, string text)
        {
            var typeface = new Typeface(tb.FontFamily, tb.FontStyle, tb.FontWeight, tb.FontStretch);
            var formattedText = new FormattedText(
                text,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                tb.FontSize,
                Brushes.Black,
                _dpiScaleFunc());
            return formattedText.Width;
        }

        private void StartAnimation(TextBlock txt, double scrollLimit, double containerWidth, Settings settings)
        {
            double speed = settings.ScrollSpeed > 0 ? settings.ScrollSpeed : 30.0;
            double delay = settings.ScrollDelay >= 0 ? settings.ScrollDelay : 1.5;
            string behavior = settings.ScrollBehavior ?? "Marquee";

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
                moveAnim.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(totalDuration))));

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
                moveAnim.KeyFrames.Add(new LinearDoubleKeyFrame(-scrollLimit, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(totalDuration))));
                moveAnim.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(totalDuration))));

                sb.Children.Add(moveAnim);
                sb.Duration = new Duration(TimeSpan.FromSeconds(totalDuration));
            }

            sb.Begin();
        }
    }
}
