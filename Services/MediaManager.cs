// Tracks system media playing state and sessions.
using System;
using System.Threading.Tasks;
using Windows.Media.Control;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;
using System.Collections.Generic;

namespace TaskbarMiniPlayer
{
    public class MediaTimelineInfo
    {
        public TimeSpan EndTime { get; set; }
        public TimeSpan Position { get; set; }
        public DateTimeOffset LastUpdatedTime { get; set; }
    }

    public class SimulatedSession
    {
        public string SourceApp { get; set; } = "Mock App";
        public string Title { get; set; } = "Simulated Song";
        public string Artist { get; set; } = "Simulated Artist";
        public bool IsPlaying { get; set; } = false;
        public double DurationSeconds { get; set; } = 180;
        public double PositionSeconds { get; set; } = 30;
        public DateTimeOffset LastUpdatedTime { get; set; } = DateTimeOffset.UtcNow;
    }

    public class MediaManager
    {
        private GlobalSystemMediaTransportControlsSessionManager? _sessionManager;
        private GlobalSystemMediaTransportControlsSession? _currentSession;
        private IReadOnlyList<GlobalSystemMediaTransportControlsSession>? _allSessions;
        private int _currentSessionIndex = 0;

        public event Action? MediaStateChanged;

        // Simulation state
        public bool IsSimulationEnabled { get; set; }
        public List<SimulatedSession> SimulatedSessions { get; } = new();
        public int SimulatedCurrentSessionIndex { get; set; }

        public int TotalSessions => IsSimulationEnabled ? SimulatedSessions.Count : (_allSessions?.Count ?? 0);
        public int CurrentSessionIndex => IsSimulationEnabled ? SimulatedCurrentSessionIndex : _currentSessionIndex;

        private bool _isPlaying;
        public bool IsPlaying
        {
            get => IsSimulationEnabled ? (SimulatedSessions.Count > SimulatedCurrentSessionIndex && SimulatedCurrentSessionIndex >= 0 ? SimulatedSessions[SimulatedCurrentSessionIndex].IsPlaying : false) : _isPlaying;
            private set => _isPlaying = value;
        }

        private string _title = "";
        public string Title
        {
            get => IsSimulationEnabled ? (SimulatedSessions.Count > SimulatedCurrentSessionIndex && SimulatedCurrentSessionIndex >= 0 ? SimulatedSessions[SimulatedCurrentSessionIndex].Title : "") : _title;
            private set => _title = value;
        }

        private string _artist = "";
        public string Artist
        {
            get => IsSimulationEnabled ? (SimulatedSessions.Count > SimulatedCurrentSessionIndex && SimulatedCurrentSessionIndex >= 0 ? SimulatedSessions[SimulatedCurrentSessionIndex].Artist : "") : _artist;
            private set => _artist = value;
        }

        private ImageSource? _albumArt;
        public ImageSource? AlbumArt
        {
            get => IsSimulationEnabled ? null : _albumArt;
            private set => _albumArt = value;
        }

        public async Task InitializeAsync()
        {
            try
            {
                _sessionManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                if (_sessionManager != null)
                {
                    _sessionManager.CurrentSessionChanged += OnCurrentSessionChanged;
                    _sessionManager.SessionsChanged += OnSessionsChanged;
                    UpdateCurrentSession();
                }
            }
            catch { }
        }

        private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() => UpdateCurrentSession());
        }

        private void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() => UpdateCurrentSession(keepIndex: true));
        }

        private void UpdateCurrentSession(bool keepIndex = false)
        {
            if (_currentSession != null)
            {
                _currentSession.PlaybackInfoChanged -= OnPlaybackInfoChanged;
                _currentSession.MediaPropertiesChanged -= OnMediaPropertiesChanged;
                _currentSession.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
            }

            _allSessions = _sessionManager?.GetSessions();
            
            if (_allSessions != null && _allSessions.Count > 0)
            {
                if (!keepIndex || _currentSessionIndex >= _allSessions.Count || _currentSessionIndex < 0)
                {
                    // Fall back to the system's current session or first available
                    var sysCurrent = _sessionManager?.GetCurrentSession();
                    _currentSessionIndex = 0;
                    if (sysCurrent != null)
                    {
                        for (int i = 0; i < _allSessions.Count; i++)
                        {
                            if (_allSessions[i].SourceAppUserModelId == sysCurrent.SourceAppUserModelId)
                            {
                                _currentSessionIndex = i;
                                break;
                            }
                        }
                    }
                }
                _currentSession = _allSessions[_currentSessionIndex];
            }
            else
            {
                _currentSession = null;
                _currentSessionIndex = 0;
            }

            if (_currentSession != null)
            {
                _currentSession.PlaybackInfoChanged += OnPlaybackInfoChanged;
                _currentSession.MediaPropertiesChanged += OnMediaPropertiesChanged;
                _currentSession.TimelinePropertiesChanged += OnTimelinePropertiesChanged;
            }

            RefreshState();
            _ = RefreshMediaPropertiesAsync();
        }

        public void SwitchSession(int offset)
        {
            if (IsSimulationEnabled)
            {
                if (SimulatedSessions.Count <= 1) return;
                SimulatedCurrentSessionIndex += offset;
                if (SimulatedCurrentSessionIndex < 0) SimulatedCurrentSessionIndex = SimulatedSessions.Count - 1;
                if (SimulatedCurrentSessionIndex >= SimulatedSessions.Count) SimulatedCurrentSessionIndex = 0;
                MediaStateChanged?.Invoke();
                return;
            }

            if (_allSessions == null || _allSessions.Count <= 1) return;

            _currentSessionIndex += offset;
            if (_currentSessionIndex < 0) _currentSessionIndex = _allSessions.Count - 1;
            if (_currentSessionIndex >= _allSessions.Count) _currentSessionIndex = 0;

            UpdateCurrentSession(keepIndex: true);
        }

        private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
        {
            RefreshState();
        }

        private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
        {
            _ = RefreshMediaPropertiesAsync();
        }

        private void OnTimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args)
        {
            MediaStateChanged?.Invoke();
        }

        private void RefreshState()
        {
            if (_currentSession != null)
            {
                var info = _currentSession.GetPlaybackInfo();
                IsPlaying = info?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            }
            else
            {
                IsPlaying = false;
            }

            MediaStateChanged?.Invoke();
        }

        private async Task RefreshMediaPropertiesAsync()
        {
            if (_currentSession == null) return;

            try
            {
                var props = await _currentSession.TryGetMediaPropertiesAsync();
                if (props != null)
                {
                    Title = props.Title;
                    Artist = props.Artist;

                    if (props.Thumbnail != null)
                    {
                        try
                        {
                            using var stream = await props.Thumbnail.OpenReadAsync();
                            using var memStream = new MemoryStream();
                            await stream.AsStream().CopyToAsync(memStream);
                            memStream.Position = 0;

                            System.Windows.Application.Current.Dispatcher.Invoke(() =>
                            {
                                var bitmap = new BitmapImage();
                                bitmap.BeginInit();
                                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                                bitmap.StreamSource = memStream;
                                bitmap.EndInit();
                                bitmap.Freeze();
                                AlbumArt = bitmap;
                            });
                        }
                        catch
                        {
                            AlbumArt = null;
                        }
                    }
                    else
                    {
                        AlbumArt = null;
                    }
                }
            }
            catch { }
            finally
            {
                MediaStateChanged?.Invoke();
            }
        }

        public async Task PlayPauseAsync()
        {
            if (IsSimulationEnabled)
            {
                if (SimulatedSessions.Count > SimulatedCurrentSessionIndex && SimulatedCurrentSessionIndex >= 0)
                {
                    var session = SimulatedSessions[SimulatedCurrentSessionIndex];
                    session.IsPlaying = !session.IsPlaying;
                    session.LastUpdatedTime = DateTimeOffset.UtcNow;
                    MediaStateChanged?.Invoke();
                }
                await Task.CompletedTask;
                return;
            }

            if (_currentSession != null)
            {
                if (IsPlaying)
                    await _currentSession.TryPauseAsync();
                else
                    await _currentSession.TryPlayAsync();
            }
        }

        public async Task SkipPreviousAsync()
        {
            if (IsSimulationEnabled)
            {
                SwitchSession(-1);
                await Task.CompletedTask;
                return;
            }

            if (_currentSession != null)
            {
                await _currentSession.TrySkipPreviousAsync();
            }
        }

        public async Task SkipNextAsync()
        {
            if (IsSimulationEnabled)
            {
                SwitchSession(1);
                await Task.CompletedTask;
                return;
            }

            if (_currentSession != null)
            {
                await _currentSession.TrySkipNextAsync();
            }
        }

        public MediaTimelineInfo? GetTimelineProperties()
        {
            if (IsSimulationEnabled)
            {
                if (SimulatedSessions.Count > SimulatedCurrentSessionIndex && SimulatedCurrentSessionIndex >= 0)
                {
                    var session = SimulatedSessions[SimulatedCurrentSessionIndex];
                    return new MediaTimelineInfo
                    {
                        EndTime = TimeSpan.FromSeconds(session.DurationSeconds),
                        Position = TimeSpan.FromSeconds(session.PositionSeconds),
                        LastUpdatedTime = session.LastUpdatedTime
                    };
                }
                return null;
            }

            try
            {
                var original = _currentSession?.GetTimelineProperties();
                if (original == null) return null;
                return new MediaTimelineInfo
                {
                    EndTime = original.EndTime,
                    Position = original.Position,
                    LastUpdatedTime = original.LastUpdatedTime
                };
            }
            catch
            {
                return null;
            }
        }

        public double GetTimelineProgress()
        {
            if (IsSimulationEnabled)
            {
                if (SimulatedSessions.Count > SimulatedCurrentSessionIndex && SimulatedCurrentSessionIndex >= 0)
                {
                    var session = SimulatedSessions[SimulatedCurrentSessionIndex];
                    if (session.DurationSeconds <= 0) return 0;
                    double pos = session.PositionSeconds;
                    if (session.IsPlaying)
                    {
                        var timeSinceUpdate = DateTimeOffset.UtcNow - session.LastUpdatedTime;
                        if (timeSinceUpdate.TotalMilliseconds < 0) timeSinceUpdate = TimeSpan.Zero;
                        pos += timeSinceUpdate.TotalSeconds;
                        if (pos > session.DurationSeconds) pos = session.DurationSeconds;
                    }
                    return pos / session.DurationSeconds;
                }
                return 0;
            }

            if (_currentSession == null) return 0;
            try
            {
                var timeline = _currentSession.GetTimelineProperties();
                if (timeline == null || timeline.EndTime.TotalMilliseconds <= 0) return 0;

                var position = timeline.Position;
                if (IsPlaying)
                {
                    var timeSinceUpdate = DateTimeOffset.UtcNow - timeline.LastUpdatedTime;
                    if (timeSinceUpdate.TotalMilliseconds < 0) timeSinceUpdate = TimeSpan.Zero;
                    var estimatedPos = position + timeSinceUpdate;
                    if (estimatedPos > timeline.EndTime) estimatedPos = timeline.EndTime;
                    if (estimatedPos < TimeSpan.Zero) estimatedPos = TimeSpan.Zero;
                    return estimatedPos.TotalMilliseconds / timeline.EndTime.TotalMilliseconds;
                }
                else
                {
                    return position.TotalMilliseconds / timeline.EndTime.TotalMilliseconds;
                }
            }
            catch
            {
                return 0;
            }
        }

        public void TriggerMediaStateChanged()
        {
            MediaStateChanged?.Invoke();
        }

        public void FocusActiveMediaApp()
        {
            if (IsSimulationEnabled) return;
            if (_currentSession == null) return;

            string appId = _currentSession.SourceAppUserModelId.ToLowerInvariant();
            string processTarget = appId;
            try
            {
                if (appId.Contains("!"))
                {
                    var parts = appId.Split(new[] { '_', '!' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 0)
                        processTarget = parts[0];
                }
                else if (appId.EndsWith(".exe"))
                {
                    processTarget = Path.GetFileNameWithoutExtension(appId);
                }
                else if (appId.Contains("\\") || appId.Contains("/"))
                {
                    processTarget = Path.GetFileNameWithoutExtension(appId);
                }
            }
            catch { }

            var processes = System.Diagnostics.Process.GetProcesses();
            foreach (var proc in processes)
            {
                try
                {
                    string procName = proc.ProcessName.ToLowerInvariant();
                    bool match = false;

                    if (procName == processTarget || appId.Contains(procName) || procName.Contains(processTarget))
                    {
                        match = true;
                    }
                    else
                    {
                        string title = proc.MainWindowTitle.ToLowerInvariant();
                        if (!string.IsNullOrEmpty(title) && (title.Contains(Title.ToLowerInvariant()) || title.Contains(Artist.ToLowerInvariant())))
                        {
                            match = true;
                        }
                    }

                    if (match && proc.MainWindowHandle != IntPtr.Zero)
                    {
                        IntPtr hWnd = proc.MainWindowHandle;
                        Win32.ShowWindow(hWnd, Win32.SW_RESTORE);
                        Win32.SetForegroundWindow(hWnd);
                        return;
                    }
                }
                catch { }
            }
        }
    }
}
