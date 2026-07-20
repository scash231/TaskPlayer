using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TaskbarMiniPlayer.Animations;
using ComboBox = System.Windows.Controls.ComboBox;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace TaskbarMiniPlayer
{
    public partial class TranslucentIcoWindow : Window
    {
        private TranslucentIcoSettings _settings;
        private readonly TranslucentIcoSettings _savedBackup;
        private readonly Rect _originRect;
        private bool _isAnimatingClose;
        private bool _suppressProfileChange;
        private bool _isLoaded;

        private enum ProfileEditMode { None, Save, Rename }
        private ProfileEditMode _profileEditMode = ProfileEditMode.None;

        public TranslucentIcoWindow(Rect originRect, Window? owner = null)
        {
            InitializeComponent();
            _originRect = originRect;
            _settings = TranslucentIcoSettings.Load();
            this.Owner = owner;
            
            // Clone the loaded settings for reversion backup
            _savedBackup = new TranslucentIcoSettings
            {
                Opacity = _settings.Opacity,
                Layer = _settings.Layer,
                LaunchOnStartup = _settings.LaunchOnStartup,
                ActiveProfileName = _settings.ActiveProfileName
            };

            // Size of this window: Width = 704, Height = 454
            this.Width = 704;
            this.Height = 454;

            // Sync with main app theme and accent if available
            try
            {
                var mainSettings = Settings.Load();
                ApplyCustomAccent(mainSettings.CustomAccentColor);
            }
            catch (Exception ex) { Log.Warn($"[TranslucentIcoWindow] Failed to apply custom accent: {ex.Message}"); }

            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            var fadeDur = TimeSpan.FromMilliseconds(200);
            RootGrid.BeginAnimation(UIElement.OpacityProperty,
                new System.Windows.Media.Animation.DoubleAnimation(0, 1, fadeDur) { EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut } });

            PopulateFromSettings(_settings);
            RefreshProfileList();

            _isLoaded = true;
        }

        private void PopulateFromSettings(TranslucentIcoSettings s)
        {
            SldOpacity.Value = s.Opacity;
            TxtOpacityVal.Text = s.Opacity.ToString();

            SelectComboByTag(CmbLayer, s.Layer);

            // Read startup from registry or setting
            bool startup = TranslucentIcoService.IsStartupEnabled();
            ChkStartup.IsChecked = startup;
            s.LaunchOnStartup = startup;
        }

        private void ApplyCurrentSettings()
        {
            if (!_isLoaded) return;

            int opacity = (int)SldOpacity.Value;
            string layer = "listview";

            if (CmbLayer.SelectedItem is ComboBoxItem cbi)
            {
                layer = cbi.Tag?.ToString() ?? "listview";
            }

            TranslucentIcoService.SetDesktopIconOpacity(opacity, layer);
        }

        private void SldOpacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtOpacityVal != null)
                TxtOpacityVal.Text = $"{(int)e.NewValue}";

            ApplyCurrentSettings();
        }

        private void CmbLayer_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyCurrentSettings();
        }

        private void ChkStartup_Click(object sender, RoutedEventArgs e)
        {
            if (_isLoaded)
            {
                bool enable = ChkStartup.IsChecked == true;
                TranslucentIcoService.SetStartupEnabled(enable);
                _settings.LaunchOnStartup = enable;
            }
        }

        // ── Profiles ──

        private void RefreshProfileList()
        {
            _suppressProfileChange = true;
            CmbProfile.Items.Clear();
            CmbProfile.Items.Add(new ComboBoxItem { Content = "Default", Tag = "" });

            foreach (var name in TranslucentIcoSettings.GetProfileNames())
            {
                CmbProfile.Items.Add(new ComboBoxItem { Content = name, Tag = name });
            }

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
            if (_suppressProfileChange || !_isLoaded) return;

            UpdateProfileButtons();

            if (CmbProfile.SelectedItem is ComboBoxItem cbi)
            {
                var profileName = cbi.Tag?.ToString();
                if (string.IsNullOrEmpty(profileName))
                {
                    _settings = TranslucentIcoSettings.Load();
                    _settings.ActiveProfileName = null;
                }
                else
                {
                    var loaded = TranslucentIcoSettings.LoadProfile(profileName);
                    if (loaded != null)
                    {
                        _settings = loaded;
                        _settings.ActiveProfileName = profileName;
                    }
                }
                PopulateFromSettings(_settings);
                ApplyCurrentSettings();
            }
        }

        private void BtnSaveProfile_Click(object sender, RoutedEventArgs e)
        {
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
                    TranslucentIcoSettings.DeleteProfile(name);
                    _settings.ActiveProfileName = null;
                    RefreshProfileList();
                }
            }
        }

        private void TxtProfileName_KeyDown(object sender, KeyEventArgs e)
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
                    if (TranslucentIcoSettings.RenameProfile(oldName, newName))
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

        private void TitleBar_Drag(object s, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        }

        private void GatherCurrentSettings()
        {
            _settings.Opacity = (int)SldOpacity.Value;
            if (CmbLayer.SelectedItem is ComboBoxItem cbi)
            {
                _settings.Layer = cbi.Tag?.ToString() ?? "listview";
            }
            _settings.LaunchOnStartup = ChkStartup.IsChecked == true;
        }

        private void SaveCurrentSettings()
        {
            GatherCurrentSettings();
            _settings.Save();
            
            // Set startup in registry
            TranslucentIcoService.SetStartupEnabled(_settings.LaunchOnStartup);
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
            // Revert settings to backup on cancel
            TranslucentIcoService.SetDesktopIconOpacity(_savedBackup.Opacity, _savedBackup.Layer);
            TranslucentIcoService.SetStartupEnabled(_savedBackup.LaunchOnStartup);
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

            var fadeDur = TimeSpan.FromMilliseconds(150);
            var fadeAnim = new System.Windows.Media.Animation.DoubleAnimation(0, fadeDur)
            {
                EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn }
            };
            fadeAnim.Completed += (s, e) =>
            {
                try
                {
                    Close();
                }
                catch (InvalidOperationException) { }
            };
            RootGrid.BeginAnimation(UIElement.OpacityProperty, fadeAnim);
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

        private void ApplyCustomAccent(string colorHex)
        {
            try
            {
                var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colorHex);
                Resources["Accent"] = new SolidColorBrush(color);
            }
            catch (Exception ex) { Log.Warn($"[TranslucentIcoWindow] Failed to convert accent color '{colorHex}': {ex.Message}"); }
        }
    }
}
