using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.Media.Control;

namespace SongInfoViewer
{
    public class MediaSessionService
    {
        private GlobalSystemMediaTransportControlsSessionManager? _sessionManager;
        private GlobalSystemMediaTransportControlsSession? _currentSession;

        public async Task InitializeAsync()
        {
            try
            {
                _sessionManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                
                if (_sessionManager != null)
                {
                    _sessionManager.CurrentSessionChanged += SessionManager_CurrentSessionChanged;
                    Debug.WriteLine("[GSMTC] SessionManager initialized successfully.");
                    
                    var session = _sessionManager.GetCurrentSession();
                    SubscribeToSessionEvents(session);
                    await UpdateCurrentMediaInfoAsync(session);
                }
                else
                {
                    Debug.WriteLine("[GSMTC] Failed to get GlobalSystemMediaTransportControlsSessionManager.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GSMTC] Exception during initialization: {ex}");
            }
        }

        private async void SessionManager_CurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
        {
            Debug.WriteLine("[GSMTC] Current session changed event fired.");
            var session = sender.GetCurrentSession();
            SubscribeToSessionEvents(session);
            await UpdateCurrentMediaInfoAsync(session);
        }

        private void SubscribeToSessionEvents(GlobalSystemMediaTransportControlsSession? session)
        {
            if (_currentSession == session) return;

            if (_currentSession != null)
            {
                _currentSession.MediaPropertiesChanged -= Session_MediaPropertiesChanged;
                _currentSession.PlaybackInfoChanged -= Session_PlaybackInfoChanged;
            }

            _currentSession = session;

            if (_currentSession != null)
            {
                _currentSession.MediaPropertiesChanged += Session_MediaPropertiesChanged;
                _currentSession.PlaybackInfoChanged += Session_PlaybackInfoChanged;
            }
        }

        private async void Session_MediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
        {
            Debug.WriteLine("[GSMTC] MediaPropertiesChanged event fired.");
            await UpdateCurrentMediaInfoAsync(sender);
        }

        private void Session_PlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
        {
            var playbackInfo = sender.GetPlaybackInfo();
            Debug.WriteLine($"[GSMTC] PlaybackInfoChanged: ControlsStatus={playbackInfo?.Controls}, PlaybackStatus={playbackInfo?.PlaybackStatus}");
        }

        private async Task UpdateCurrentMediaInfoAsync(GlobalSystemMediaTransportControlsSession? session)
        {
            if (session == null)
            {
                Debug.WriteLine("[GSMTC] No active media session detected.");
                return;
            }

            try
            {
                Debug.WriteLine($"[GSMTC] AppUserModelId: {session.SourceAppUserModelId}");
                var mediaProperties = await session.TryGetMediaPropertiesAsync();

                if (mediaProperties != null)
                {
                    Debug.WriteLine("========================================");
                    Debug.WriteLine($"[GSMTC] Title       : {mediaProperties.Title}");
                    Debug.WriteLine($"[GSMTC] Artist      : {mediaProperties.Artist}");
                    Debug.WriteLine($"[GSMTC] Album Title : {mediaProperties.AlbumTitle}");
                    Debug.WriteLine($"[GSMTC] Genres      : {string.Join(", ", mediaProperties.Genres)}");
                    Debug.WriteLine($"[GSMTC] Thumbnail   : {(mediaProperties.Thumbnail != null ? "Present" : "None")}");
                    Debug.WriteLine("========================================");
                }
                else
                {
                    Debug.WriteLine("[GSMTC] Could not retrieve MediaProperties from the session.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GSMTC] Exception in UpdateCurrentMediaInfoAsync: {ex}");
            }
        }
    }
}
