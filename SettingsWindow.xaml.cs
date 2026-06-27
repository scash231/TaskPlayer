using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using TaskbarMiniPlayer.Animations;

using ComboBox = System.Windows.Controls.ComboBox;
using Point = System.Windows.Point;

namespace TaskbarMiniPlayer
{
    public partial class SettingsWindow : Window
    {
        private Settings _settings;
        private readonly Rect _originRect;
        private bool _isAnimatingClose;
        private StackPanel? _activePanel;

        public SettingsWindow(Rect originRect)
        {
            InitializeComponent();
            _originRect = originRect;
            _settings = Settings.Load();

            // Spawn window to the left and slightly above the button
            // Typical button is ~120 wide, window is 680x500
            this.Left = originRect.Right - 680;
            this.Top = originRect.Top - 500 - 15;

            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            FluidMotion.MorphOpen(RootBorder, WindowScale, WindowTranslate, _originRect, this);

            _activePanel = PanelGeneral;
            AnimateTabContent(PanelGeneral);

            // General
            ChkStartup.IsChecked = _settings.LaunchOnStartup;
            ChkShowPlayer.IsChecked = _settings.ShowPlayer;
            ChkVolumeSlider.IsChecked = _settings.EnableVolumeSlider;
            ChkScrollText.IsChecked = _settings.ScrollLongText;
            TxtAutoHide.Text = _settings.AutoHideSeconds.ToString();

            // Appearance
            SelectComboByTag(CmbLayout, _settings.Layout.ToString());
            SelectComboByTag(CmbTheme, _settings.Theme.ToString());
            TxtBtnSize.Text = _settings.ButtonSize.ToString();
            TxtXOffset.Text = _settings.XOffset.ToString();

            // Hotkeys
            TxtPlayPauseHotkey.Text = _settings.PlayPauseHotkeyKey.ToString();
            TxtPrevHotkey.Text = _settings.PrevHotkeyKey.ToString();
            TxtNextHotkey.Text = _settings.NextHotkeyKey.ToString();
        }

        private void TitleBar_Drag(object s, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        }

        private void Tab_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;

            var target = sender == TabGeneral ? PanelGeneral
                       : sender == TabLayout ? PanelLayout
                       : sender == TabAppearance ? PanelAppearance
                       : sender == TabHotkeys ? PanelHotkeys
                       : null;

            if (target == null || target == _activePanel) return;
            SwitchTab(target);
        }

        private void SwitchTab(StackPanel target)
        {
            var old = _activePanel;
            _activePanel = target;

            if (old != null)
            {
                var ease = AppleSpringEase.Snappy;
                var dur = TimeSpan.FromMilliseconds(180);
                var fadeOut = new DoubleAnimation(1, 0, dur) { EasingFunction = ease };
                fadeOut.Completed += (_, _) =>
                {
                    old.Visibility = Visibility.Collapsed;
                };
                old.BeginAnimation(UIElement.OpacityProperty, fadeOut);

                target.Opacity = 0;
                target.Visibility = Visibility.Visible;
                AnimateTabContent(target);
            }
            else
            {
                target.Visibility = Visibility.Visible;
                AnimateTabContent(target);
            }
        }

        private static void AnimateTabContent(StackPanel panel)
        {
            panel.Opacity = 0;
            panel.RenderTransformOrigin = new Point(0.5, 0.0);
            var group = new TransformGroup();
            var st = new ScaleTransform(0.97, 0.97);
            var tt = new TranslateTransform(0, 8);
            group.Children.Add(st);
            group.Children.Add(tt);
            panel.RenderTransform = group;

            var spring = AppleSpringEase.Interactive;
            var smooth = AppleSpringEase.Gentle;
            var springDur = TimeSpan.FromMilliseconds(420);

            panel.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200))
                { EasingFunction = smooth });
            tt.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(8, 0, springDur) { EasingFunction = spring });
            st.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(0.97, 1, springDur) { EasingFunction = spring });
            st.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(0.97, 1, springDur) { EasingFunction = spring });

            int idx = 0;
            foreach (UIElement child in panel.Children)
            {
                if (child is not FrameworkElement fe) continue;
                fe.Opacity = 0;
                fe.RenderTransformOrigin = new Point(0.5, 0.0);
                var cGroup = new TransformGroup();
                var cTt = new TranslateTransform(0, 12 + idx * 2);
                cGroup.Children.Add(new ScaleTransform(1, 1));
                cGroup.Children.Add(cTt);
                fe.RenderTransform = cGroup;

                var delay = TimeSpan.FromMilliseconds(40 + idx * 35);
                fe.BeginAnimation(UIElement.OpacityProperty,
                    new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
                    { BeginTime = delay, EasingFunction = smooth });
                cTt.BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(12 + idx * 2, 0, TimeSpan.FromMilliseconds(380))
                    { BeginTime = delay, EasingFunction = spring });
                idx++;
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            _settings.LaunchOnStartup = ChkStartup.IsChecked == true;
            _settings.ShowPlayer = ChkShowPlayer.IsChecked == true;
            _settings.EnableVolumeSlider = ChkVolumeSlider.IsChecked == true;
            _settings.ScrollLongText = ChkScrollText.IsChecked == true;
            
            if (int.TryParse(TxtAutoHide.Text, out int hide)) _settings.AutoHideSeconds = hide;

            if (CmbLayout.SelectedItem is ComboBoxItem cbiLayout && Enum.TryParse(cbiLayout.Tag.ToString(), out LayoutStyle ls))
                _settings.Layout = ls;

            if (CmbTheme.SelectedItem is ComboBoxItem cbiTheme && Enum.TryParse(cbiTheme.Tag.ToString(), out AppTheme theme))
                _settings.Theme = theme;

            if (int.TryParse(TxtBtnSize.Text, out int size)) _settings.ButtonSize = size;
            if (int.TryParse(TxtXOffset.Text, out int xoff)) _settings.XOffset = xoff;

            if (uint.TryParse(TxtPlayPauseHotkey.Text, out uint pp)) _settings.PlayPauseHotkeyKey = pp;
            if (uint.TryParse(TxtPrevHotkey.Text, out uint prev)) _settings.PrevHotkeyKey = prev;
            if (uint.TryParse(TxtNextHotkey.Text, out uint next)) _settings.NextHotkeyKey = next;

            _settings.Save();
            CloseWithAnimation();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            CloseWithAnimation();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!_isAnimatingClose) 
            { 
                e.Cancel = true; 
                CloseWithAnimation(); 
            }
            base.OnClosing(e);
        }

        private void CloseWithAnimation()
        {
            if (_isAnimatingClose) return;
            _isAnimatingClose = true;
            
            FluidMotion.MorphClose(RootBorder, WindowScale, WindowTranslate, _originRect, this,
                () => {
                    try
                    {
                        Close();
                    }
                    catch (InvalidOperationException)
                    {
                        // Ignore
                    }
                });
        }

        private static void SelectComboByTag(ComboBox combo, string tag)
        {
            foreach (ComboBoxItem item in combo.Items)
            {
                if (item.Tag?.ToString() == tag) 
                { 
                    combo.SelectedItem = item; 
                    return; 
                }
            }
        }
    }
}
