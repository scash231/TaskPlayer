using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ComboBox = System.Windows.Controls.ComboBox;
using ComboBoxItem = System.Windows.Controls.ComboBoxItem;
using Point = System.Windows.Point;

namespace TaskbarMiniPlayer.Animations
{
    public static class DropdownAnimBehavior
    {
        public static readonly DependencyProperty IsEnabledProperty =
            DependencyProperty.RegisterAttached(
                "IsEnabled", typeof(bool), typeof(DropdownAnimBehavior),
                new PropertyMetadata(false, OnIsEnabledChanged));

        public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);
        public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

        private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not ComboBox combo) return;

            if ((bool)e.NewValue)
            {
                combo.DropDownOpened += OnDropDownOpened;
                combo.DropDownClosed += OnDropDownClosed;
            }
            else
            {
                combo.DropDownOpened -= OnDropDownOpened;
                combo.DropDownClosed -= OnDropDownClosed;
            }
        }

        private static void OnDropDownOpened(object? sender, EventArgs e)
        {
            if (sender is not ComboBox combo) return;

            var popup = combo.Template.FindName("PART_Popup", combo) as Popup;
            if (popup?.Child is not Border border) return;

            // Defer execution until after ComboBoxItems visual containers are generated
            combo.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                popup.PlacementTarget = combo;

                int selIdx = Math.Max(combo.SelectedIndex, 0);
                double itemH = 22.0;
                if (combo.ItemContainerGenerator.ContainerFromIndex(0) is FrameworkElement first && first.ActualHeight > 0)
                    itemH = first.ActualHeight;

                double topPad = 4.0;
                double selectedTop = topPad + selIdx * itemH;
                double selectedCenter = selectedTop + itemH / 2.0;
                double comboCenter = combo.ActualHeight / 2.0;

                popup.Placement = PlacementMode.Relative;
                popup.VerticalOffset = -(selectedCenter - comboCenter);
                popup.HorizontalOffset = 0;

                // Block immediate click-through: the selected item lands under the cursor,
                // so the mouse-up from the opening click would instantly select & close.
                border.IsHitTestVisible = false;
                var guard = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
                guard.Tick += (_, _) => { guard.Stop(); border.IsHitTestVisible = true; };
                guard.Start();

                // --- Scale origin at selected item's relative position ---
                double totalH = topPad * 2 + combo.Items.Count * itemH;
                double originY = Math.Clamp(selectedCenter / totalH, 0.05, 0.95);

                border.RenderTransformOrigin = new Point(0.5, originY);
                var st = new ScaleTransform(0.92, 0.92);
                border.RenderTransform = st;
                border.Opacity = 0;

                var spring = AppleSpringEase.Interactive;
                var smooth = AppleSpringEase.Gentle;
                var springDur = TimeSpan.FromMilliseconds(450);
                var fadeDur = TimeSpan.FromMilliseconds(200);

                border.BeginAnimation(UIElement.OpacityProperty,
                    new DoubleAnimation(0, 1, fadeDur) { EasingFunction = smooth });
                st.BeginAnimation(ScaleTransform.ScaleXProperty,
                    new DoubleAnimation(0.92, 1, springDur) { EasingFunction = spring });
                st.BeginAnimation(ScaleTransform.ScaleYProperty,
                    new DoubleAnimation(0.92, 1, springDur) { EasingFunction = spring });

                // Stagger items outward from the selected item
                StaggerDropdownItems(combo, selIdx);
            }));
        }

        private static void OnDropDownClosed(object? sender, EventArgs e)
        {
            if (sender is not ComboBox combo) return;

            var popup = combo.Template.FindName("PART_Popup", combo) as Popup;
            if (popup == null) return;

            popup.Placement = PlacementMode.Bottom;
            popup.VerticalOffset = 0;
            popup.HorizontalOffset = 0;
        }

        private static void StaggerDropdownItems(ComboBox combo, int selIdx)
        {
            var spring = AppleSpringEase.Bouncy;
            var smooth = AppleSpringEase.Gentle;

            for (int i = 0; i < combo.Items.Count; i++)
            {
                if (combo.ItemContainerGenerator.ContainerFromIndex(i) is not ComboBoxItem item)
                    continue;

                int dist = Math.Abs(i - selIdx);
                double direction = i < selIdx ? -1 : (i > selIdx ? 1 : 0);
                double travel = direction * (6 + dist * 2);

                item.Opacity = 0;
                item.RenderTransformOrigin = new Point(0.5, 0.5);
                var tt = new TranslateTransform(0, travel);
                item.RenderTransform = tt;

                var delay = TimeSpan.FromMilliseconds(20 + dist * 30);

                item.BeginAnimation(UIElement.OpacityProperty,
                    new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200))
                    { BeginTime = delay, EasingFunction = smooth });

                tt.BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(travel, 0, TimeSpan.FromMilliseconds(380))
                    { BeginTime = delay, EasingFunction = spring });
            }
        }
    }
}
