// Tracks system media playing state and sessions.
using System;
using System.Threading.Tasks;
using Windows.Media.Control;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;

namespace TaskbarMiniPlayer
{
    public class MediaManager
    {
        private GlobalSystemMediaTransportControlsSessionManager? _sessionManager;
        private GlobalSystemMediaTransportControlsSession? _currentSession;
        private IReadOnlyList<GlobalSystemMediaTransportControlsSession>? _allSessions;
        private int _currentSessionIndex = 0;

        public event Action? MediaStateChanged;

        public int TotalSessions => _allSessions?.Count ?? 0;
        public int CurrentSessionIndex => _currentSessionIndex;

        public bool IsPlaying { get; private set; }
        public string Title { get; private set; } = "";
        public string Artist { get; private set; } = "";
        public ImageSource? AlbumArt { get; private set; }

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
            if (_currentSession != null)
            {
                await _currentSession.TrySkipPreviousAsync();
            }
        }

        public async Task SkipNextAsync()
        {
            if (_currentSession != null)
            {
                await _currentSession.TrySkipNextAsync();
            }
        }

        public Windows.Media.Control.GlobalSystemMediaTransportControlsSessionTimelineProperties? GetTimelineProperties()
        {
            try
            {
                return _currentSession?.GetTimelineProperties();
            }
            catch
            {
                return null;
            }
        }

        public double GetTimelineProgress()
        {
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
    }
}
