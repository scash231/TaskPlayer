// Settings window supporting simple/expert modes and profiles.
using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using TaskbarMiniPlayer.Animations;
using TaskbarMiniPlayer.Views;

using ComboBox = System.Windows.Controls.ComboBox;
using Button = System.Windows.Controls.Button;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Point = System.Windows.Point;

namespace TaskbarMiniPlayer
{
    public partial class SettingsWindow : Window
    {
        private Settings _settings;
        private readonly Rect _originRect;
        private bool _isAnimatingClose;
        private StackPanel? _activePanel;
        private bool _suppressProfileChange;
        private readonly bool _spawnBelow;
        private int _helpSlideIndex = 0;

        // Profile inline-edit state
        private enum ProfileEditMode { None, Save, Rename }
        private ProfileEditMode _profileEditMode = ProfileEditMode.None;

        public SettingsWindow(Rect originRect)
        {
            InitializeComponent();
            _originRect = originRect;
            _settings = Settings.Load();

            // Determine if we should spawn the window below the player (e.g. if taskbar is at the top of the screen)
            double dpiScale = 1.0;
            if (System.Windows.Application.Current.MainWindow != null)
            {
                try
                {
                    dpiScale = VisualTreeHelper.GetDpi(System.Windows.Application.Current.MainWindow).PixelsPerDip;
                }
                catch { }
            }
            double physY = (originRect.Top + originRect.Height / 2) * dpiScale;
            var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)((originRect.Left + originRect.Width / 2) * dpiScale), (int)physY));
            _spawnBelow = physY < (screen.Bounds.Top + screen.Bounds.Height / 2);

            // Set initial window width and height based on mode
            var isExpert = _settings.ExpertMode;
            var initialHeight = isExpert ? 796 : 454;
            this.Width = 704;
            this.Height = initialHeight;

            // Spawn window to the left and either above or below the player window
            this.Left = originRect.Right - 704;
            if (_spawnBelow)
            {
                this.Top = originRect.Bottom + 15;
            }
            else
            {
                this.Top = originRect.Top - initialHeight - 15;
            }

            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            FluidMotion.MorphOpen(RootGrid, WindowScale, WindowTranslate, _originRect, this);

            _activePanel = PanelPositioning;
            AnimateTabContent(PanelPositioning);

            PopulateFromSettings(_settings);

            // Expert mode toggle
            ChkExpertMode.IsChecked = _settings.ExpertMode;
            ApplyExpertMode(_settings.ExpertMode);

            // Profiles
            RefreshProfileList();

            ApplyCustomAccent(_settings.CustomAccentColor);
        }

        private void PopulateFromSettings(Settings s)
        {
            // General
            ChkStartup.IsChecked = s.LaunchOnStartup;
            ChkAutoPlayOnLaunch.IsChecked = s.AutoPlayOnLaunch;
            ChkShowPlayer.IsChecked = s.ShowPlayer;
            ChkVolumeSlider.IsChecked = s.EnableVolumeSlider;

            SldAutoHide.Value = s.AutoHideSeconds;
            UpdateAutoHideValLabel(s.AutoHideSeconds);
 
            // Layout & Dimensions
            SelectComboByTag(CmbLayout, s.Layout.ToString());
            ChkHideArtist.IsChecked = s.HideArtist;
            SldButtonSize.Value = s.ButtonSize;
            TxtButtonSizeVal.Text = $"{s.ButtonSize}px";
            SldButtonGap.Value = s.ButtonGap;
            TxtButtonGapVal.Text = $"{s.ButtonGap}px";
            SldXOffset.Value = s.XOffset;
            TxtXOffsetVal.Text = $"{s.XOffset}px";
            SldYOffset.Value = s.YOffset;
            TxtYOffsetVal.Text = $"{s.YOffset}px";

            // Dynamic Sizing
            ChkDynamicSizing.IsChecked = s.EnableDynamicSizing;
            RowMinWidth.IsEnabled = s.EnableDynamicSizing;
            RowMaxWidth.IsEnabled = s.EnableDynamicSizing;
            RowSizingTarget.IsEnabled = s.EnableDynamicSizing;
            SldMinWidth.Value = s.MinWidth;
            TxtMinWidthVal.Text = $"{s.MinWidth}px";
            SldMaxWidth.Value = s.MaxWidth;
            TxtMaxWidthVal.Text = $"{s.MaxWidth}px";
            SelectComboByTag(CmbSizingTarget, s.DynamicSizingTarget.ToString());
            ChkSmartResize.IsChecked = s.EnableSmartResize;
 
            // Appearance & Color
            // Visuals
            ChkEnableTransparency.IsChecked = s.EnableTransparency;
            RowBackgroundOpacity.IsEnabled = s.EnableTransparency;
            SldBackgroundOpacity.Value = s.BackgroundOpacity * 100.0;
            TxtBackgroundOpacityVal.Text = $"{(int)(s.BackgroundOpacity * 100.0)}%";
            
            SldAdaptiveTintStrength.Value = s.AdaptiveTintStrength * 100.0;
            TxtAdaptiveTintStrengthVal.Text = $"{(int)(s.AdaptiveTintStrength * 100.0)}%";
            
            ChkScrollTextVisuals.IsChecked = s.ScrollLongText;
            SelectComboByTag(CmbScrollBehavior, s.ScrollBehavior);
            SldScrollSpeed.Value = s.ScrollSpeed;
            TxtScrollSpeedVal.Text = $"{(int)s.ScrollSpeed} px/s";
            
            SldScrollDelay.Value = s.ScrollDelay;
            TxtScrollDelayVal.Text = $"{s.ScrollDelay:0.0}s";
 
            ChkHideScrollbars.IsChecked = s.HideScrollbars;
            ApplyScrollbarVisibility();
             SelectComboByTag(CmbBorderMode, s.BorderMode.ToString());
             SelectComboByTag(CmbTimelineStyle, s.TimelineStyle.ToString());
             ChkDisableFluidAnimations.IsChecked = s.DisableFluidAnimations;
             ChkDisableTimelineAnimation.IsChecked = s.DisableTimelineAnimation;
             ChkDisableVisualizer.IsChecked = s.DisableVisualizer;
             ChkDisableTextScrolling.IsChecked = s.DisableTextScrolling;
             ChkDisableAlbumArt.IsChecked = s.DisableAlbumArt;
             ChkDisableTransparency.IsChecked = s.DisableTransparency;
             ChkOptimizeTimerFrequencies.IsChecked = s.OptimizeTimerFrequencies;
             ChkEnableTranslucentIco.IsChecked = s.EnableTranslucentIco;
             ChkTooltips.IsChecked = s.EnableTooltips;
 
            // Hotkeys
            SetHotkeyButton(BtnPlayPauseHotkey, s.PlayPauseHotkeyKey);
            SetHotkeyButton(BtnPrevHotkey, s.PrevHotkeyKey);
            SetHotkeyButton(BtnNextHotkey, s.NextHotkeyKey);
        }

        // ══════ Expert Mode ══════

        private void ChkExpertMode_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;
            ApplyExpertMode(ChkExpertMode.IsChecked == true);
        }

        private void ApplyExpertMode(bool expert)
        {
            if (!IsLoaded) return;

            var targetHeight = expert ? 796.0 : 454.0;
            var currentHeight = this.Height;

            // Anchor the bottom or top of the window depending on taskbar position
            double targetTop;
            if (_spawnBelow)
            {
                targetTop = _originRect.Bottom + 15;
            }
            else
            {
                var bottomY = _originRect.Top - 15;
                targetTop = bottomY - targetHeight;
            }

            if (expert)
            {
                ExpertBorder.Visibility = Visibility.Visible;
            }

            var ease = AppleSpringEase.Interactive;
            var springDur = TimeSpan.FromMilliseconds(450);

            // Animate Height
            var heightAnim = new DoubleAnimation(currentHeight, targetHeight, springDur) { EasingFunction = ease };
            // Animate Top
            var topAnim = new DoubleAnimation(this.Top, targetTop, springDur) { EasingFunction = ease };

            if (!expert)
            {
                heightAnim.Completed += (s, e) =>
                {
                    ExpertBorder.Visibility = Visibility.Collapsed;
                };
            }

            this.BeginAnimation(Window.HeightProperty, heightAnim);
            this.BeginAnimation(Window.TopProperty, topAnim);
        }

        // ══════ Profiles ══════

        private void RefreshProfileList()
        {
            _suppressProfileChange = true;
            CmbProfile.Items.Clear();
            CmbProfile.Items.Add(new ComboBoxItem { Content = "Default", Tag = "" });

            foreach (var name in Settings.GetProfileNames())
            {
                CmbProfile.Items.Add(new ComboBoxItem { Content = name, Tag = name });
            }

            // Select active profile
            var active = _settings.ActiveProfileName;
            bool found = false;
            if (!string.IsNullOrEmpty(active))
            {
                foreach (ComboBoxItem item in CmbProfile.Items)
                {
                    if (item.Tag?.ToString() == active)
                    {
                        CmbProfile.SelectedItem = item;
                        found = true;
                        break;
                    }
                }
            }
            if (!found) CmbProfile.SelectedIndex = 0;
            _suppressProfileChange = false;

            UpdateProfileButtons();
        }

        private void UpdateProfileButtons()
        {
            var isCustomProfile = CmbProfile.SelectedItem is ComboBoxItem cbi && !string.IsNullOrEmpty(cbi.Tag?.ToString());
            BtnDeleteProfile.IsEnabled = isCustomProfile;
            BtnRenameProfile.IsEnabled = isCustomProfile;
        }

        private void CmbProfile_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressProfileChange || !IsLoaded) return;

            UpdateProfileButtons();

            if (CmbProfile.SelectedItem is ComboBoxItem cbi)
            {
                var profileName = cbi.Tag?.ToString();
                if (string.IsNullOrEmpty(profileName))
                {
                    // Default profile — reload from settings.json
                    _settings = Settings.Load();
                    _settings.ActiveProfileName = null;
                }
                else
                {
                    var loaded = Settings.LoadProfile(profileName);
                    if (loaded != null)
                    {
                        _settings = loaded;
                        _settings.ActiveProfileName = profileName;
                    }
                }
                PopulateFromSettings(_settings);
                ApplyCustomAccent(_settings.CustomAccentColor);
            }
        }

        private void BtnSaveProfile_Click(object sender, RoutedEventArgs e)
        {
            // Show inline text input for profile name
            _profileEditMode = ProfileEditMode.Save;
            TxtProfileName.Text = "";
            TxtProfileName.Visibility = Visibility.Visible;
            CmbProfile.Visibility = Visibility.Collapsed;
            TxtProfileName.Focus();
            TxtProfileName.SelectAll();
        }

        private void BtnRenameProfile_Click(object sender, RoutedEventArgs e)
        {
            if (CmbProfile.SelectedItem is ComboBoxItem cbi && !string.IsNullOrEmpty(cbi.Tag?.ToString()))
            {
                _profileEditMode = ProfileEditMode.Rename;
                TxtProfileName.Text = cbi.Tag.ToString();
                TxtProfileName.Visibility = Visibility.Visible;
                CmbProfile.Visibility = Visibility.Collapsed;
                TxtProfileName.Focus();
                TxtProfileName.SelectAll();
            }
        }

        private void BtnDeleteProfile_Click(object sender, RoutedEventArgs e)
        {
            if (CmbProfile.SelectedItem is ComboBoxItem cbi && !string.IsNullOrEmpty(cbi.Tag?.ToString()))
            {
                var name = cbi.Tag.ToString()!;
                var result = System.Windows.MessageBox.Show($"Delete profile \"{name}\"?", "Delete Profile",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result == MessageBoxResult.Yes)
                {
                    Settings.DeleteProfile(name);
                    _settings.ActiveProfileName = null;
                    RefreshProfileList();
                }
            }
        }

        private void TxtProfileName_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                CommitProfileEdit();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                CancelProfileEdit();
                e.Handled = true;
            }
        }

        private void TxtProfileName_LostFocus(object sender, RoutedEventArgs e)
        {
            // Only commit if still in an edit mode (not already cancelled)
            if (_profileEditMode != ProfileEditMode.None)
                CommitProfileEdit();
        }

        private void CommitProfileEdit()
        {
            var newName = TxtProfileName.Text.Trim();
            if (string.IsNullOrEmpty(newName))
            {
                CancelProfileEdit();
                return;
            }

            if (_profileEditMode == ProfileEditMode.Save)
            {
                // Gather current UI state into _settings then save as profile
                GatherCurrentSettings();
                _settings.SaveAsProfile(newName);
                _settings.ActiveProfileName = newName;
                _settings.Save();
            }
            else if (_profileEditMode == ProfileEditMode.Rename)
            {
                if (CmbProfile.SelectedItem is ComboBoxItem cbi && !string.IsNullOrEmpty(cbi.Tag?.ToString()))
                {
                    var oldName = cbi.Tag.ToString()!;
                    if (Settings.RenameProfile(oldName, newName))
                    {
                        _settings.ActiveProfileName = newName;
                        _settings.Save();
                    }
                }
            }

            _profileEditMode = ProfileEditMode.None;
            TxtProfileName.Visibility = Visibility.Collapsed;
            CmbProfile.Visibility = Visibility.Visible;
            RefreshProfileList();
        }

        private void CancelProfileEdit()
        {
            _profileEditMode = ProfileEditMode.None;
            TxtProfileName.Visibility = Visibility.Collapsed;
            CmbProfile.Visibility = Visibility.Visible;
        }

        // ══════ Tab Switching ══════

        private void TitleBar_Drag(object s, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        }

        private void Tab_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;
 
            var target = sender == TabPositioning ? PanelPositioning
                       : sender == TabSizing ? PanelSizing
                       : sender == TabScrolling ? PanelScrolling
                       : sender == TabHotkeys ? PanelHotkeys
                       : sender == TabPerformance ? PanelPerformance
                       : sender == TabAdvancedStyle ? PanelAdvancedStyle
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

        // ══════ Save / Cancel ══════

        private void GatherCurrentSettings()
        {
            _settings.LaunchOnStartup = ChkStartup.IsChecked == true;
            _settings.AutoPlayOnLaunch = ChkAutoPlayOnLaunch.IsChecked == true;
            _settings.ShowPlayer = ChkShowPlayer.IsChecked == true;
            _settings.EnableVolumeSlider = ChkVolumeSlider.IsChecked == true;
            _settings.AutoHideSeconds = (int)SldAutoHide.Value;
 
            if (CmbLayout.SelectedItem is ComboBoxItem cbiLayout && Enum.TryParse(cbiLayout.Tag.ToString(), out LayoutStyle ls))
                _settings.Layout = ls;

            _settings.ButtonSize = (int)SldButtonSize.Value;
            _settings.ButtonGap = (int)SldButtonGap.Value;
            _settings.XOffset = (int)SldXOffset.Value;
            _settings.YOffset = (int)SldYOffset.Value;
            _settings.HideArtist = ChkHideArtist.IsChecked == true;

            // Dynamic Sizing
            _settings.EnableDynamicSizing = ChkDynamicSizing.IsChecked == true;
            _settings.MinWidth = (int)SldMinWidth.Value;
            _settings.MaxWidth = (int)SldMaxWidth.Value;
            if (CmbSizingTarget.SelectedItem is ComboBoxItem cbiTarget && Enum.TryParse(cbiTarget.Tag.ToString(), out SizingTarget target))
                _settings.DynamicSizingTarget = target;
            _settings.EnableSmartResize = ChkSmartResize.IsChecked == true;
 
            // Visuals
            _settings.EnableTransparency = ChkEnableTransparency.IsChecked == true;
            _settings.BackgroundOpacity = SldBackgroundOpacity.Value / 100.0;
            _settings.AdaptiveTintStrength = SldAdaptiveTintStrength.Value / 100.0;
            
            _settings.ScrollLongText = ChkScrollTextVisuals.IsChecked == true;
            if (CmbScrollBehavior.SelectedItem is ComboBoxItem cbiBehavior)
                _settings.ScrollBehavior = cbiBehavior.Tag?.ToString() ?? "Marquee";
            
            _settings.ScrollSpeed = SldScrollSpeed.Value;
            _settings.ScrollDelay = SldScrollDelay.Value;
            _settings.HideScrollbars = ChkHideScrollbars.IsChecked == true;
             if (CmbBorderMode.SelectedItem is ComboBoxItem cbiBorder && Enum.TryParse(cbiBorder.Tag.ToString(), out BorderMode bm))
                 _settings.BorderMode = bm;

             if (CmbTimelineStyle.SelectedItem is ComboBoxItem cbiTimelineStyle && Enum.TryParse(cbiTimelineStyle.Tag.ToString(), out TimelineStyle ts))
                 _settings.TimelineStyle = ts;

            if (BtnPlayPauseHotkey.Tag is uint pp) _settings.PlayPauseHotkeyKey = pp;
            if (BtnPrevHotkey.Tag is uint prev) _settings.PrevHotkeyKey = prev;
             if (BtnNextHotkey.Tag is uint next) _settings.NextHotkeyKey = next;

             _settings.DisableFluidAnimations = ChkDisableFluidAnimations.IsChecked == true;
             _settings.DisableTimelineAnimation = ChkDisableTimelineAnimation.IsChecked == true;
             _settings.DisableVisualizer = ChkDisableVisualizer.IsChecked == true;
             _settings.DisableTextScrolling = ChkDisableTextScrolling.IsChecked == true;
             _settings.DisableAlbumArt = ChkDisableAlbumArt.IsChecked == true;
             _settings.DisableTransparency = ChkDisableTransparency.IsChecked == true;
             _settings.OptimizeTimerFrequencies = ChkOptimizeTimerFrequencies.IsChecked == true;
             _settings.EnableTranslucentIco = ChkEnableTranslucentIco.IsChecked == true;
             _settings.EnableTooltips = ChkTooltips.IsChecked == true;
 
             _settings.ExpertMode = ChkExpertMode.IsChecked == true;
        }

        private void SaveCurrentSettings()
        {
            GatherCurrentSettings();
            _settings.Save();

            if (System.Windows.Application.Current.MainWindow is MainWindow mainWindow)
            {
                mainWindow.ReloadSettings();
            }
        }

        private void OpenDevWindow()
        {
            foreach (Window window in System.Windows.Application.Current.Windows)
            {
                if (window is DevWindow)
                {
                    window.Activate();
                    return;
                }
            }

            var devWin = new DevWindow();
            devWin.Show();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            SaveCurrentSettings();
        }

        private void SaveAndExit_Click(object sender, RoutedEventArgs e)
        {
            SaveCurrentSettings();
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
            
            FluidMotion.MorphClose(RootGrid, WindowScale, WindowTranslate, _originRect, this,
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

        // ══════ Helpers ══════

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

        private Button? _bindingButton;

        private void SetHotkeyButton(Button btn, uint keyCode)
        {
            btn.Tag = keyCode;
            if (keyCode == 0)
            {
                btn.Content = "Click to bind";
            }
            else
            {
                try
                {
                    var key = KeyInterop.KeyFromVirtualKey((int)keyCode);
                    btn.Content = key == Key.None ? $"KeyCode: {keyCode}" : key.ToString();
                }
                catch
                {
                    btn.Content = $"KeyCode: {keyCode}";
                }
            }
        }

        private void HotkeyBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
            {
                _bindingButton = btn;
                btn.Content = "Press a key...";
                btn.Focus();
            }
        }

        private void HotkeyBtn_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_bindingButton != null && sender == _bindingButton)
            {
                e.Handled = true;
                
                if (e.Key == Key.Escape)
                {
                    SetHotkeyButton(_bindingButton, 0);
                }
                else
                {
                    Key key = e.Key == Key.System ? e.SystemKey : e.Key;
                    uint virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);
                    SetHotkeyButton(_bindingButton, virtualKey);
                }
                
                _bindingButton = null;
                // Move focus away to complete binding visually
                this.Focus(); 
            }
        }

        private void HotkeyBtn_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_bindingButton != null && sender == _bindingButton)
            {
                // Restore if clicked away
                if (_bindingButton.Tag is uint keyCode)
                {
                    SetHotkeyButton(_bindingButton, keyCode);
                }
                else
                {
                    SetHotkeyButton(_bindingButton, 0);
                }
                _bindingButton = null;
            }
        }

        // ══════ Slider / Toggle Handlers ══════

        private void SldBackgroundOpacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtBackgroundOpacityVal != null)
                TxtBackgroundOpacityVal.Text = $"{(int)e.NewValue}%";
        }

        private void SldAdaptiveTintStrength_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtAdaptiveTintStrengthVal != null)
                TxtAdaptiveTintStrengthVal.Text = $"{(int)e.NewValue}%";
        }

        private void SldScrollSpeed_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtScrollSpeedVal != null)
                TxtScrollSpeedVal.Text = $"{(int)e.NewValue} px/s";
        }

        private void SldScrollDelay_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtScrollDelayVal != null)
                TxtScrollDelayVal.Text = $"{e.NewValue:0.0}s";
        }

        private void ChkEnableTransparency_Changed(object sender, RoutedEventArgs e)
        {
            if (RowBackgroundOpacity != null)
                RowBackgroundOpacity.IsEnabled = ChkEnableTransparency.IsChecked == true;
        }

        private void ChkHideScrollbars_Changed(object sender, RoutedEventArgs e)
        {
            ApplyScrollbarVisibility();
        }

        private void ApplyScrollbarVisibility()
        {
            if (ChkHideScrollbars == null) return;
            var hide = ChkHideScrollbars.IsChecked == true;
            Resources["GlobalScrollBarWidth"] = hide ? 0.0 : 6.0;
            Resources["GlobalScrollBarHeight"] = hide ? 0.0 : 6.0;
        }

        private void UpdateAutoHideValLabel(double val)
        {
            if (val == 0) TxtAutoHideVal.Text = "Disabled";
            else TxtAutoHideVal.Text = $"{val}s";
        }

        private void SldAutoHide_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtAutoHideVal != null)
                UpdateAutoHideValLabel(e.NewValue);
        }

        private void SldButtonSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtButtonSizeVal != null) TxtButtonSizeVal.Text = $"{(int)e.NewValue}px";
        }

        private void SldButtonGap_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtButtonGapVal != null) TxtButtonGapVal.Text = $"{(int)e.NewValue}px";
        }

        private void SldXOffset_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtXOffsetVal != null) TxtXOffsetVal.Text = $"{(int)e.NewValue}px";
        }

        private void SldYOffset_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtYOffsetVal != null) TxtYOffsetVal.Text = $"{(int)e.NewValue}px";
        }

        private void ChkDynamicSizing_Changed(object sender, RoutedEventArgs e)
        {
            if (RowMinWidth != null) RowMinWidth.IsEnabled = ChkDynamicSizing.IsChecked == true;
            if (RowMaxWidth != null) RowMaxWidth.IsEnabled = ChkDynamicSizing.IsChecked == true;
            if (RowSizingTarget != null) RowSizingTarget.IsEnabled = ChkDynamicSizing.IsChecked == true;
        }

        private void SldMinWidth_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtMinWidthVal != null) TxtMinWidthVal.Text = $"{(int)e.NewValue}px";
            // Prevent min width from exceeding max width slider value
            if (SldMaxWidth != null && SldMaxWidth.Value < e.NewValue)
            {
                SldMaxWidth.Value = e.NewValue;
            }
        }

        private void SldMaxWidth_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtMaxWidthVal != null) TxtMaxWidthVal.Text = $"{(int)e.NewValue}px";
            // Prevent max width from going below min width slider value
            if (SldMinWidth != null && SldMinWidth.Value > e.NewValue)
            {
                SldMinWidth.Value = e.NewValue;
            }
        }



        private void ApplyCustomAccent(string colorHex)
        {
            try
            {
                var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colorHex);
                Resources["Accent"] = new SolidColorBrush(color);
            }
            catch { }
        }

        private void TxtSettingsTitle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                OpenDevWindow();
            }
        }

        private void OpenTranslucentIco_Click(object sender, RoutedEventArgs e)
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

            var button = (System.Windows.Controls.Button)sender;
            var buttonPos = button.PointToScreen(new Point(0, 0));

            double dpiScale = 1.0;
            try
            {
                dpiScale = System.Windows.Media.VisualTreeHelper.GetDpi(this).PixelsPerDip;
            }
            catch { }

            double logicalLeft = buttonPos.X / dpiScale;
            double logicalTop = buttonPos.Y / dpiScale;
            double logicalWidth = button.ActualWidth;
            double logicalHeight = button.ActualHeight;

            var rect = new Rect(logicalLeft, logicalTop, logicalWidth, logicalHeight);
            var translucentIcoWindow = new TranslucentIcoWindow(rect, this);
            translucentIcoWindow.Show();
        }

        private void Help_Click(object sender, RoutedEventArgs e)
        {
            _helpSlideIndex = 0;
            HelpOverlay.Visibility = Visibility.Visible;
            UpdateHelpUI();
        }

        private void CloseHelp_Click(object sender, RoutedEventArgs e)
        {
            HelpOverlay.Visibility = Visibility.Collapsed;
        }

        private void PrevHelp_Click(object sender, RoutedEventArgs e)
        {
            if (_helpSlideIndex > 0)
            {
                _helpSlideIndex--;
                UpdateHelpUI();
            }
        }

        private void NextHelp_Click(object sender, RoutedEventArgs e)
        {
            if (_helpSlideIndex < 2)
            {
                _helpSlideIndex++;
                UpdateHelpUI();
            }
        }

        private void UpdateHelpUI()
        {
            HelpSlide1.Visibility = _helpSlideIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
            HelpSlide2.Visibility = _helpSlideIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
            HelpSlide3.Visibility = _helpSlideIndex == 2 ? Visibility.Visible : Visibility.Collapsed;

            BtnPrevHelp.IsEnabled = _helpSlideIndex > 0;
            BtnNextHelp.IsEnabled = _helpSlideIndex < 2;

            var accentBrush = TryFindResource("Accent") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.DodgerBlue;
            var textSecBrush = TryFindResource("TextSec") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.Gray;

            Dot1.Opacity = _helpSlideIndex == 0 ? 1.0 : 0.4;
            Dot2.Opacity = _helpSlideIndex == 1 ? 1.0 : 0.4;
            Dot3.Opacity = _helpSlideIndex == 2 ? 1.0 : 0.4;

            Dot1.Fill = _helpSlideIndex == 0 ? accentBrush : textSecBrush;
            Dot2.Fill = _helpSlideIndex == 1 ? accentBrush : textSecBrush;
            Dot3.Fill = _helpSlideIndex == 2 ? accentBrush : textSecBrush;
        }
    }
}

