using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace SongInfoViewer
{
    public class SongInfo
    {
        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string AlbumTitle { get; set; } = string.Empty;
        public ImageSource? AlbumArt { get; set; }
        public bool PreserveExistingArtOnNull { get; set; } = false;
    }

    public class MediaSessionService
    {
        private GlobalSystemMediaTransportControlsSessionManager? _sessionManager;
        private GlobalSystemMediaTransportControlsSession? _currentSession;
        private CancellationTokenSource? _updateCts;

        public event EventHandler<SongInfo>? SongInfoChanged;

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
                    TriggerUpdate(session);
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

        private void SessionManager_CurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
        {
            Debug.WriteLine("[GSMTC] Current session changed event fired.");
            var session = sender.GetCurrentSession();
            SubscribeToSessionEvents(session);
            TriggerUpdate(session);
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

        private void Session_MediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
        {
            Debug.WriteLine("[GSMTC] MediaPropertiesChanged event fired.");
            TriggerUpdate(sender);
        }

        private void Session_PlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
        {
            var playbackInfo = sender.GetPlaybackInfo();
            Debug.WriteLine($"[GSMTC] PlaybackInfoChanged: ControlsStatus={playbackInfo?.Controls}, PlaybackStatus={playbackInfo?.PlaybackStatus}");
        }

        private void TriggerUpdate(GlobalSystemMediaTransportControlsSession? session)
        {
            _updateCts?.Cancel();
            _updateCts?.Dispose();
            _updateCts = new CancellationTokenSource();

            var token = _updateCts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(150, token);
                    await UpdateCurrentMediaInfoAsync(session, token);
                }
                catch (OperationCanceledException)
                {
                    // 新しいイベントが発生したためキャンセル（正常）
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[GSMTC] Error in TriggerUpdate: {ex}");
                }
            }, token);
        }

        private async Task UpdateCurrentMediaInfoAsync(GlobalSystemMediaTransportControlsSession? session, CancellationToken token)
        {
            if (session == null)
            {
                Debug.WriteLine("[GSMTC] No active media session detected.");
                SongInfoChanged?.Invoke(this, new SongInfo
                {
                    Title = "再生中の曲はありません",
                    Artist = "-",
                    AlbumTitle = "-",
                    AlbumArt = null
                });
                return;
            }

            token.ThrowIfCancellationRequested();

            try
            {
                string appId = session.SourceAppUserModelId ?? "";
                Debug.WriteLine($"[GSMTC] AppUserModelId: {appId}");
                var mediaProperties = await session.TryGetMediaPropertiesAsync().AsTask(token);

                if (mediaProperties != null)
                {
                    token.ThrowIfCancellationRequested();

                    var (thumbnailImage, isFilteredIcon) = await ReadThumbnailAsync(mediaProperties.Thumbnail, appId, token);

                    token.ThrowIfCancellationRequested();

                    var info = new SongInfo
                    {
                        Title = string.IsNullOrWhiteSpace(mediaProperties.Title) ? "不明な曲名" : mediaProperties.Title,
                        Artist = string.IsNullOrWhiteSpace(mediaProperties.Artist) ? "不明なアーティスト" : mediaProperties.Artist,
                        AlbumTitle = string.IsNullOrWhiteSpace(mediaProperties.AlbumTitle) ? "" : mediaProperties.AlbumTitle,
                        AlbumArt = thumbnailImage,
                        PreserveExistingArtOnNull = isFilteredIcon
                    };

                    Debug.WriteLine("========================================");
                    Debug.WriteLine($"[GSMTC] Title       : {info.Title}");
                    Debug.WriteLine($"[GSMTC] Artist      : {info.Artist}");
                    Debug.WriteLine($"[GSMTC] Album Title : {info.AlbumTitle}");
                    Debug.WriteLine($"[GSMTC] Thumbnail   : {(info.AlbumArt != null ? "Present" : isFilteredIcon ? "Filtered (Icon)" : "None")}");
                    Debug.WriteLine("========================================");

                    SongInfoChanged?.Invoke(this, info);
                }
                else
                {
                    Debug.WriteLine("[GSMTC] Could not retrieve MediaProperties from the session.");
                }
            }
            catch (OperationCanceledException)
            {
                // キャンセル処理は無視
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GSMTC] Exception in UpdateCurrentMediaInfoAsync: {ex}");
            }
        }

        private async Task<(ImageSource? Image, bool IsFilteredIcon)> ReadThumbnailAsync(IRandomAccessStreamReference? thumbnailRef, string appId, CancellationToken token)
        {
            if (thumbnailRef == null)
            {
                Debug.WriteLine("[Thumbnail Log] thumbnailRef is NULL");
                return (null, false);
            }

            try
            {
                using IRandomAccessStream stream = await thumbnailRef.OpenReadAsync().AsTask(token);
                if (stream == null || stream.Size == 0)
                {
                    Debug.WriteLine("[Thumbnail Log] Stream is null or empty (Size = 0)");
                    return (null, false);
                }

                using Stream sysStream = stream.AsStreamForRead();
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = sysStream;
                bitmap.EndInit();
                bitmap.Freeze(); // UIスレッドへ安全に渡す

                token.ThrowIfCancellationRequested();

                bool isIcon = IsBrowserApp(appId) && IsLikelyAppIcon(bitmap, stream.Size);

                // 詳細デバッグログ出力
                Debug.WriteLine("----------------------------------------");
                Debug.WriteLine($"[Thumbnail Log] AppId       : {appId}");
                Debug.WriteLine($"[Thumbnail Log] Stream Size : {stream.Size} bytes");
                Debug.WriteLine($"[Thumbnail Log] Dimensions  : {bitmap.PixelWidth} x {bitmap.PixelHeight}");
                Debug.WriteLine($"[Thumbnail Log] IsLikelyIcon: {isIcon}");
                Debug.WriteLine("----------------------------------------");

                if (isIcon)
                {
                    Debug.WriteLine($"[GSMTC] Filtered out browser app icon ({bitmap.PixelWidth}x{bitmap.PixelHeight}, {stream.Size} bytes) for appId: {appId}");
                    return (null, true);
                }

                return (bitmap, false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GSMTC] Error reading thumbnail stream: {ex.Message}");
                return (null, false);
            }
        }

        private bool IsBrowserApp(string appId)
        {
            if (string.IsNullOrEmpty(appId)) return false;
            var lower = appId.ToLowerInvariant();
            return lower.Contains("chrome") || lower.Contains("msedge") || lower.Contains("firefox") || lower.Contains("brave") || lower.Contains("opera");
        }

        private bool IsLikelyAppIcon(BitmapImage bitmap, ulong streamSize)
        {
            // 解像度が 64x64 以下かつデータサイズが 15KB 以下の極小画像を「アプリアイコン」と判定
            if (bitmap.PixelWidth <= 64 && bitmap.PixelHeight <= 64 && streamSize <= 15360)
            {
                return true;
            }
            return false;
        }
    }
}
