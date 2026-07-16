using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MessageBox = System.Windows.MessageBox;
using Application = System.Windows.Application;
using TextBox = System.Windows.Controls.TextBox;
using Slider = System.Windows.Controls.Slider;
using System.Collections.Generic;

namespace TaskbarMiniPlayer.Views
{
    public partial class DevWindow : Window
    {
        private readonly System.Windows.Threading.DispatcherTimer _refreshTimer;
        private readonly Settings _settings;
        private readonly MediaManager? _mediaManager;
        private bool _isInitialized;
        private bool _isUpdating;
        private bool _isSimUpdating;

        public DevWindow()
        {
            InitializeComponent();

            _settings = Settings.Load();

            if (Application.Current.MainWindow is MainWindow mainWin)
            {
                _mediaManager = mainWin.MediaManagerInstance;
            }

            LoadSettingsIntoUI();
            InitializeMediaSimulationUI();

            _isInitialized = true;

            _refreshTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _refreshTimer.Tick += (s, e) => RefreshDiagnostics();

            Loaded += (s, e) =>
            {
                RefreshDiagnostics();
                _refreshTimer.Start();
            };

            Unloaded += (s, e) => _refreshTimer.Stop();
        }

        private void LoadSettingsIntoUI()
        {
            _isUpdating = true;
            try
            {
                // Topmost Interval
                SldTopmostInterval.Value = ClampValue(_settings.TopmostIntervalMs, SldTopmostInterval.Minimum, SldTopmostInterval.Maximum);
                TxtTopmostInterval.Text = _settings.TopmostIntervalMs.ToString();
                ChkDisableTopmostTimer.IsChecked = _settings.DisableTopmostTimer;

                // Peak Meter Interval
                SldPeakMeterInterval.Value = ClampValue(_settings.PeakMeterIntervalMs, SldPeakMeterInterval.Minimum, SldPeakMeterInterval.Maximum);
                TxtPeakMeterInterval.Text = _settings.PeakMeterIntervalMs.ToString();

                // Scroll Speed
                SldScrollSpeed.Value = ClampValue(_settings.ScrollSpeed, SldScrollSpeed.Minimum, SldScrollSpeed.Maximum);
                TxtScrollSpeed.Text = _settings.ScrollSpeed.ToString();

                // Scroll Delay
                SldScrollDelay.Value = ClampValue(_settings.ScrollDelay, SldScrollDelay.Minimum, SldScrollDelay.Maximum);
                TxtScrollDelay.Text = _settings.ScrollDelay.ToString();

                // Auto-Hide Delay
                SldAutoHide.Value = ClampValue(_settings.AutoHideSeconds, SldAutoHide.Minimum, SldAutoHide.Maximum);
                TxtAutoHide.Text = _settings.AutoHideSeconds.ToString();

                // Background Opacity
                SldBackgroundOpacity.Value = ClampValue(_settings.BackgroundOpacity, SldBackgroundOpacity.Minimum, SldBackgroundOpacity.Maximum);
                TxtBackgroundOpacity.Text = _settings.BackgroundOpacity.ToString();

                // Tint Strength
                SldAdaptiveTintStrength.Value = ClampValue(_settings.AdaptiveTintStrength, SldAdaptiveTintStrength.Minimum, SldAdaptiveTintStrength.Maximum);
                TxtAdaptiveTintStrength.Text = _settings.AdaptiveTintStrength.ToString();

                // Button Size
                SldButtonSize.Value = ClampValue(_settings.ButtonSize, SldButtonSize.Minimum, SldButtonSize.Maximum);
                TxtButtonSize.Text = _settings.ButtonSize.ToString();

                // Button Gap
                SldButtonGap.Value = ClampValue(_settings.ButtonGap, SldButtonGap.Minimum, SldButtonGap.Maximum);
                TxtButtonGap.Text = _settings.ButtonGap.ToString();

                // X Offset
                SldXOffset.Value = ClampValue(_settings.XOffset, SldXOffset.Minimum, SldXOffset.Maximum);
                TxtXOffset.Text = _settings.XOffset.ToString();

                // Y Offset
                SldYOffset.Value = ClampValue(_settings.YOffset, SldYOffset.Minimum, SldYOffset.Maximum);
                TxtYOffset.Text = _settings.YOffset.ToString();
            }
            finally
            {
                _isUpdating = false;
            }
        }

        private double ClampValue(double val, double min, double max)
        {
            if (val < min) return min;
            if (val > max) return max;
            return val;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void RefreshDiagnostics()
        {
            try
            {
                // Taskbar position
                var taskbar = Win32.FindWindow("Shell_TrayWnd", null);
                if (taskbar != IntPtr.Zero && Win32.GetWindowRect(taskbar, out var tbRect))
                {
                    bool isVertical = (tbRect.Bottom - tbRect.Top) > (tbRect.Right - tbRect.Left);
                    bool isTop = tbRect.Top <= 0 && tbRect.Bottom > 0 && !isVertical;
                    string posStr = isVertical ? "Left/Right (Vertical)" : (isTop ? "Top" : "Bottom");
                    TxtTaskbarPos.Text = $"Taskbar Position: {posStr} (L:{tbRect.Left}, T:{tbRect.Top}, R:{tbRect.Right}, B:{tbRect.Bottom})";
                }
                else
                {
                    TxtTaskbarPos.Text = "Taskbar Position: Undetected";
                }

                // Window Position
                if (Application.Current.MainWindow != null)
                {
                    var mainWin = Application.Current.MainWindow;
                    TxtWindowPos.Text = $"Window Position: X={mainWin.Left:F1}, Y={mainWin.Top:F1}, W={mainWin.Width:F1}, H={mainWin.Height:F1}";
                }

                // Memory Usage
                using (var proc = System.Diagnostics.Process.GetCurrentProcess())
                {
                    double mb = proc.WorkingSet64 / 1024.0 / 1024.0;
                    TxtMemoryUsage.Text = $"Memory Usage: {mb:F2} MB";
                }

                // Active Sessions (via reflection)
                if (Application.Current.MainWindow is MainWindow mWin)
                {
                    var field = typeof(MainWindow).GetField("_mediaManager", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (field != null)
                    {
                        var mm = field.GetValue(mWin);
                        if (mm != null)
                        {
                            var prop = mm.GetType().GetProperty("TotalSessions");
                            if (prop != null)
                            {
                                int sessions = (int)prop.GetValue(mm)!;
                                TxtActiveSessions.Text = $"Active Media Sessions: {sessions}";
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex.ToString());
            }
        }

        // --- Synchronization Logic ---

        private void OnSliderChanged(Slider slider, TextBox textBox, Action<double> setSettingsValue)
        {
            if (!_isInitialized || _isUpdating) return;
            _isUpdating = true;
            try
            {
                double val = slider.Value;
                setSettingsValue(val);
                _settings.Save();
                textBox.Text = val.ToString("G");

                if (Application.Current.MainWindow is MainWindow mainWin)
                {
                    mainWin.ReloadSettings();
                }
            }
            finally
            {
                _isUpdating = false;
            }
        }

        private void OnTextChanged(TextBox textBox, Slider slider, Action<double> setSettingsValue)
        {
            if (!_isInitialized || _isUpdating) return;
            if (double.TryParse(textBox.Text, out double val))
            {
                _isUpdating = true;
                try
                {
                    setSettingsValue(val);
                    _settings.Save();

                    // Sync slider if within bounds
                    if (val >= slider.Minimum && val <= slider.Maximum)
                    {
                        slider.Value = val;
                    }

                    if (Application.Current.MainWindow is MainWindow mainWin)
                    {
                        mainWin.ReloadSettings();
                    }
                }
                finally
                {
                    _isUpdating = false;
                }
            }
        }

        // --- Value Changed Handlers ---

        private void SldTopmostInterval_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
            OnSliderChanged(SldTopmostInterval, TxtTopmostInterval, (v) => _settings.TopmostIntervalMs = (int)v);

        private void TxtTopmostInterval_TextChanged(object sender, TextChangedEventArgs e) =>
            OnTextChanged(TxtTopmostInterval, SldTopmostInterval, (v) => _settings.TopmostIntervalMs = (int)v);

        private void ChkDisableTopmostTimer_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized || _isUpdating) return;
            _settings.DisableTopmostTimer = ChkDisableTopmostTimer.IsChecked == true;
            _settings.Save();
            if (Application.Current.MainWindow is MainWindow mainWin)
            {
                mainWin.ReloadSettings();
            }
        }

        private void SldPeakMeterInterval_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
            OnSliderChanged(SldPeakMeterInterval, TxtPeakMeterInterval, (v) => _settings.PeakMeterIntervalMs = (int)v);

        private void TxtPeakMeterInterval_TextChanged(object sender, TextChangedEventArgs e) =>
            OnTextChanged(TxtPeakMeterInterval, SldPeakMeterInterval, (v) => _settings.PeakMeterIntervalMs = (int)v);

        private void SldScrollSpeed_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
            OnSliderChanged(SldScrollSpeed, TxtScrollSpeed, (v) => _settings.ScrollSpeed = v);

        private void TxtScrollSpeed_TextChanged(object sender, TextChangedEventArgs e) =>
            OnTextChanged(TxtScrollSpeed, SldScrollSpeed, (v) => _settings.ScrollSpeed = v);

        private void SldScrollDelay_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
            OnSliderChanged(SldScrollDelay, TxtScrollDelay, (v) => _settings.ScrollDelay = v);

        private void TxtScrollDelay_TextChanged(object sender, TextChangedEventArgs e) =>
            OnTextChanged(TxtScrollDelay, SldScrollDelay, (v) => _settings.ScrollDelay = v);

        private void SldAutoHide_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
            OnSliderChanged(SldAutoHide, TxtAutoHide, (v) => _settings.AutoHideSeconds = (int)v);

        private void TxtAutoHide_TextChanged(object sender, TextChangedEventArgs e) =>
            OnTextChanged(TxtAutoHide, SldAutoHide, (v) => _settings.AutoHideSeconds = (int)v);

        private void SldBackgroundOpacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
            OnSliderChanged(SldBackgroundOpacity, TxtBackgroundOpacity, (v) => _settings.BackgroundOpacity = v);

        private void TxtBackgroundOpacity_TextChanged(object sender, TextChangedEventArgs e) =>
            OnTextChanged(TxtBackgroundOpacity, SldBackgroundOpacity, (v) => _settings.BackgroundOpacity = v);

        private void SldAdaptiveTintStrength_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
            OnSliderChanged(SldAdaptiveTintStrength, TxtAdaptiveTintStrength, (v) => _settings.AdaptiveTintStrength = v);

        private void TxtAdaptiveTintStrength_TextChanged(object sender, TextChangedEventArgs e) =>
            OnTextChanged(TxtAdaptiveTintStrength, SldAdaptiveTintStrength, (v) => _settings.AdaptiveTintStrength = v);

        private void SldButtonSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
            OnSliderChanged(SldButtonSize, TxtButtonSize, (v) => _settings.ButtonSize = (int)v);

        private void TxtButtonSize_TextChanged(object sender, TextChangedEventArgs e) =>
            OnTextChanged(TxtButtonSize, SldButtonSize, (v) => _settings.ButtonSize = (int)v);

        private void SldButtonGap_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
            OnSliderChanged(SldButtonGap, TxtButtonGap, (v) => _settings.ButtonGap = (int)v);

        private void TxtButtonGap_TextChanged(object sender, TextChangedEventArgs e) =>
            OnTextChanged(TxtButtonGap, SldButtonGap, (v) => _settings.ButtonGap = (int)v);

        private void SldXOffset_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
            OnSliderChanged(SldXOffset, TxtXOffset, (v) => _settings.XOffset = (int)v);

        private void TxtXOffset_TextChanged(object sender, TextChangedEventArgs e) =>
            OnTextChanged(TxtXOffset, SldXOffset, (v) => _settings.XOffset = (int)v);

        private void SldYOffset_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
            OnSliderChanged(SldYOffset, TxtYOffset, (v) => _settings.YOffset = (int)v);

        private void TxtYOffset_TextChanged(object sender, TextChangedEventArgs e) =>
            OnTextChanged(TxtYOffset, SldYOffset, (v) => _settings.YOffset = (int)v);

        // --- Reset Individual Settings ---

        private void ResetTopmostInterval_Click(object sender, RoutedEventArgs e) { _settings.TopmostIntervalMs = 500; LoadSettingsIntoUI(); }
        private void ResetPeakMeterInterval_Click(object sender, RoutedEventArgs e) { _settings.PeakMeterIntervalMs = 30; LoadSettingsIntoUI(); }
        private void ResetScrollSpeed_Click(object sender, RoutedEventArgs e) { _settings.ScrollSpeed = 30.0; LoadSettingsIntoUI(); }
        private void ResetScrollDelay_Click(object sender, RoutedEventArgs e) { _settings.ScrollDelay = 1.5; LoadSettingsIntoUI(); }
        private void ResetAutoHide_Click(object sender, RoutedEventArgs e) { _settings.AutoHideSeconds = 60; LoadSettingsIntoUI(); }
        private void ResetBackgroundOpacity_Click(object sender, RoutedEventArgs e) { _settings.BackgroundOpacity = 0.40; LoadSettingsIntoUI(); }
        private void ResetAdaptiveTintStrength_Click(object sender, RoutedEventArgs e) { _settings.AdaptiveTintStrength = 0.70; LoadSettingsIntoUI(); }
        private void ResetButtonSize_Click(object sender, RoutedEventArgs e) { _settings.ButtonSize = 28; LoadSettingsIntoUI(); }
        private void ResetButtonGap_Click(object sender, RoutedEventArgs e) { _settings.ButtonGap = 2; LoadSettingsIntoUI(); }
        private void ResetXOffset_Click(object sender, RoutedEventArgs e) { _settings.XOffset = 0; LoadSettingsIntoUI(); }
        private void ResetYOffset_Click(object sender, RoutedEventArgs e) { _settings.YOffset = 0; LoadSettingsIntoUI(); }

        // --- Other Utilities ---

        private void ForceGC_Click(object sender, RoutedEventArgs e)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            RefreshDiagnostics();
            MessageBox.Show("Garbage Collection triggered successfully!", "Developer Panel");
        }

        private void ToggleTopmost_Click(object sender, RoutedEventArgs e)
        {
            if (Application.Current.MainWindow != null)
            {
                Application.Current.MainWindow.Topmost = !Application.Current.MainWindow.Topmost;
                MessageBox.Show($"Main Player Topmost state set to: {Application.Current.MainWindow.Topmost}", "Developer Panel");
            }
        }

        private void Reposition_Click(object sender, RoutedEventArgs e)
        {
            if (Application.Current.MainWindow is MainWindow mainWin)
            {
                mainWin.Reposition(true);
                RefreshDiagnostics();
            }
        }

        private void ResetAllSettings_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Are you sure you want to reset all settings to defaults?", "Confirm Reset", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                var defaultSettings = new Settings();
                defaultSettings.Save();
                if (Application.Current.MainWindow is MainWindow mainWin)
                {
                    mainWin.ReloadSettings();
                }
                
                CopySettings(defaultSettings, _settings);
                LoadSettingsIntoUI();
                MessageBox.Show("All settings reset to defaults.", "Developer Panel");
            }
        }

        private void CopySettings(Settings source, Settings target)
        {
            var props = typeof(Settings).GetProperties();
            foreach (var prop in props)
            {
                if (prop.CanWrite && prop.CanRead)
                {
                    prop.SetValue(target, prop.GetValue(source));
                }
            }
        }

        private void OpenSettingsFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var dir = System.IO.Path.Combine(appData, "TaskbarMiniPlayer");
                System.Diagnostics.Process.Start("explorer.exe", dir);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error");
            }
        }

        // --- Media Simulation Handlers ---

        private void InitializeMediaSimulationUI()
        {
            if (_mediaManager == null) return;

            _isSimUpdating = true;
            try
            {
                ChkSimulateMedia.IsChecked = _mediaManager.IsSimulationEnabled;
                GridSimControls.IsEnabled = _mediaManager.IsSimulationEnabled;

                RefreshSimulatedSessionsList();
            }
            finally
            {
                _isSimUpdating = false;
            }
        }

        private void RefreshSimulatedSessionsList()
        {
            if (_mediaManager == null) return;

            var selectedIndex = LstSimSessions.SelectedIndex;
            LstSimSessions.Items.Clear();

            for (int i = 0; i < _mediaManager.SimulatedSessions.Count; i++)
            {
                var s = _mediaManager.SimulatedSessions[i];
                LstSimSessions.Items.Add($"[{i + 1}] {s.SourceApp} - {s.Title} ({s.Artist})");
            }

            if (selectedIndex >= 0 && selectedIndex < LstSimSessions.Items.Count)
            {
                LstSimSessions.SelectedIndex = selectedIndex;
            }
            else if (LstSimSessions.Items.Count > 0)
            {
                LstSimSessions.SelectedIndex = 0;
            }
            else
            {
                StackSelectedSessionEditor.IsEnabled = false;
            }
        }

        private void ChkSimulateMedia_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized || _isSimUpdating || _mediaManager == null) return;

            _mediaManager.IsSimulationEnabled = ChkSimulateMedia.IsChecked == true;
            GridSimControls.IsEnabled = _mediaManager.IsSimulationEnabled;

            if (_mediaManager.IsSimulationEnabled && _mediaManager.SimulatedSessions.Count == 0)
            {
                // Start with a default session if empty
                _mediaManager.SimulatedSessions.Add(new SimulatedSession
                {
                    SourceApp = "Spotify",
                    Title = "Simulated Song",
                    Artist = "Simulated Artist",
                    IsPlaying = true,
                    DurationSeconds = 180,
                    PositionSeconds = 0
                });
                _mediaManager.SimulatedCurrentSessionIndex = 0;
            }

            RefreshSimulatedSessionsList();
            _mediaManager.TriggerMediaStateChanged();
        }

        private void LstSimSessions_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized || _mediaManager == null) return;

            int idx = LstSimSessions.SelectedIndex;
            if (idx >= 0 && idx < _mediaManager.SimulatedSessions.Count)
            {
                _mediaManager.SimulatedCurrentSessionIndex = idx;
                LoadSimulatedSessionToEditor(_mediaManager.SimulatedSessions[idx]);
                StackSelectedSessionEditor.IsEnabled = true;
            }
            else
            {
                StackSelectedSessionEditor.IsEnabled = false;
            }
            _mediaManager.TriggerMediaStateChanged();
        }

        private void LoadSimulatedSessionToEditor(SimulatedSession session)
        {
            _isSimUpdating = true;
            try
            {
                TxtSimTitle.Text = session.Title;
                TxtSimArtist.Text = session.Artist;
                TxtSimAppSource.Text = session.SourceApp;
                ChkSimIsPlaying.IsChecked = session.IsPlaying;

                SldSimDuration.Value = session.DurationSeconds;
                TxtSimDuration.Text = session.DurationSeconds.ToString("G");

                SldSimPosition.Value = session.PositionSeconds;
                TxtSimPosition.Text = session.PositionSeconds.ToString("G");
            }
            finally
            {
                _isSimUpdating = false;
            }
        }

        private void BtnStartSimSession_Click(object sender, RoutedEventArgs e)
        {
            if (_mediaManager == null) return;

            var newSession = new SimulatedSession
            {
                SourceApp = "App" + (_mediaManager.SimulatedSessions.Count + 1),
                Title = "Simulated Track " + (_mediaManager.SimulatedSessions.Count + 1),
                Artist = "Simulated Artist " + (_mediaManager.SimulatedSessions.Count + 1),
                IsPlaying = false,
                DurationSeconds = 200,
                PositionSeconds = 0
            };

            _mediaManager.SimulatedSessions.Add(newSession);
            _mediaManager.SimulatedCurrentSessionIndex = _mediaManager.SimulatedSessions.Count - 1;

            RefreshSimulatedSessionsList();
            LstSimSessions.SelectedIndex = _mediaManager.SimulatedCurrentSessionIndex;
            _mediaManager.TriggerMediaStateChanged();
        }

        private void BtnExitSimSession_Click(object sender, RoutedEventArgs e)
        {
            if (_mediaManager == null) return;

            int idx = LstSimSessions.SelectedIndex;
            if (idx >= 0 && idx < _mediaManager.SimulatedSessions.Count)
            {
                _mediaManager.SimulatedSessions.RemoveAt(idx);
                if (_mediaManager.SimulatedCurrentSessionIndex >= _mediaManager.SimulatedSessions.Count)
                {
                    _mediaManager.SimulatedCurrentSessionIndex = Math.Max(0, _mediaManager.SimulatedSessions.Count - 1);
                }

                RefreshSimulatedSessionsList();
                _mediaManager.TriggerMediaStateChanged();
            }
        }

        private void UpdateActiveSimSession(Action<SimulatedSession> updateAction)
        {
            if (!_isInitialized || _isSimUpdating || _mediaManager == null) return;

            int idx = _mediaManager.SimulatedCurrentSessionIndex;
            if (idx >= 0 && idx < _mediaManager.SimulatedSessions.Count)
            {
                var session = _mediaManager.SimulatedSessions[idx];
                updateAction(session);
                session.LastUpdatedTime = DateTimeOffset.UtcNow;
            }
        }

        private void TxtSimTitle_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateActiveSimSession(s => s.Title = TxtSimTitle.Text);
        }

        private void TxtSimArtist_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateActiveSimSession(s => s.Artist = TxtSimArtist.Text);
        }

        private void TxtSimAppSource_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateActiveSimSession(s => s.SourceApp = TxtSimAppSource.Text);
        }

        private void ChkSimIsPlaying_Changed(object sender, RoutedEventArgs e)
        {
            UpdateActiveSimSession(s => s.IsPlaying = ChkSimIsPlaying.IsChecked == true);
        }

        private void SldSimDuration_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isInitialized || _isSimUpdating) return;
            _isSimUpdating = true;
            try
            {
                double val = SldSimDuration.Value;
                TxtSimDuration.Text = val.ToString("G");
                UpdateActiveSimSession(s => s.DurationSeconds = val);
            }
            finally
            {
                _isSimUpdating = false;
            }
        }

        private void TxtSimDuration_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isInitialized || _isSimUpdating) return;
            if (double.TryParse(TxtSimDuration.Text, out double val))
            {
                _isSimUpdating = true;
                try
                {
                    SldSimDuration.Value = val;
                    UpdateActiveSimSession(s => s.DurationSeconds = val);
                }
                finally
                {
                    _isSimUpdating = false;
                }
            }
        }

        private void SldSimPosition_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isInitialized || _isSimUpdating) return;
            _isSimUpdating = true;
            try
            {
                double val = SldSimPosition.Value;
                TxtSimPosition.Text = val.ToString("G");
                UpdateActiveSimSession(s => s.PositionSeconds = val);
            }
            finally
            {
                _isSimUpdating = false;
            }
        }

        private void TxtSimPosition_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isInitialized || _isSimUpdating) return;
            if (double.TryParse(TxtSimPosition.Text, out double val))
            {
                _isSimUpdating = true;
                try
                {
                    SldSimPosition.Value = val;
                    UpdateActiveSimSession(s => s.PositionSeconds = val);
                }
                finally
                {
                    _isSimUpdating = false;
                }
            }
        }

        private void BtnApplySimState_Click(object sender, RoutedEventArgs e)
        {
            if (_mediaManager != null)
            {
                // Re-sync List Box labels
                RefreshSimulatedSessionsList();
                _mediaManager.TriggerMediaStateChanged();
            }
        }
    }
}
