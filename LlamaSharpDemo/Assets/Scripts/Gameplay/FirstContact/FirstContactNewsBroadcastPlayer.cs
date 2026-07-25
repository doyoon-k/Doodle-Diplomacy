using System;
using System.Collections;
using System.Collections.Generic;
using DoodleDiplomacy.Narrative;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.Serialization;
using UnityEngine.UI;
using UnityEngine.Video;
using UnityCamera = UnityEngine.Camera;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    public enum FirstContactNewsMediaType
    {
        Video,
        StillImage
    }

    [Serializable]
    public sealed class FirstContactNewsBroadcastItem
    {
        [SerializeField] private FirstContactNewsMediaType mediaType;
        [SerializeField] private VideoClip videoClip;
        [SerializeField] private Texture2D stillImage;
        [SerializeField, Min(0.1f)] private float stillImageSeconds = 3f;
        [FormerlySerializedAs("bannerFallback")]
        [SerializeField] private string bannerText;
        [FormerlySerializedAs("statusFallback")]
        [SerializeField] private string statusText;
        [FormerlySerializedAs("headlineFallback")]
        [SerializeField] private string headlineText;
        [FormerlySerializedAs("captionFallback")]
        [SerializeField] private string captionText;

        public FirstContactNewsMediaType MediaType => mediaType;
        public VideoClip VideoClip => videoClip;
        public Texture2D StillImage => stillImage;
        public float StillImageSeconds => Mathf.Max(0.1f, stillImageSeconds);
        public string BannerText => bannerText;
        public string StatusText => statusText;
        public string HeadlineText => headlineText;
        public string CaptionText => captionText;

        public bool IsConfigured =>
            mediaType == FirstContactNewsMediaType.Video
                ? videoClip != null
                : stillImage != null;

        public static FirstContactNewsBroadcastItem CreateVideo(
            VideoClip clip,
            string banner,
            string status,
            string headline,
            string caption)
        {
            return new FirstContactNewsBroadcastItem
            {
                mediaType = FirstContactNewsMediaType.Video,
                videoClip = clip,
                bannerText = banner,
                statusText = status,
                headlineText = headline,
                captionText = caption
            };
        }

        public static FirstContactNewsBroadcastItem CreateStill(
            Texture2D image,
            float seconds,
            string banner,
            string status,
            string headline,
            string caption)
        {
            return new FirstContactNewsBroadcastItem
            {
                mediaType = FirstContactNewsMediaType.StillImage,
                stillImage = image,
                stillImageSeconds = seconds,
                bannerText = banner,
                statusText = status,
                headlineText = headline,
                captionText = caption
            };
        }
    }

    [DisallowMultipleComponent]
    public sealed class FirstContactNewsBroadcastPlayer : MonoBehaviour
    {
        private const int OverlayCaptureLayer = 29;
        private const int DefaultCaptureLayer = 30;
        private const int CompositeWidth = 1280;
        private const int CompositeHeight = 720;
        private const float BroadcastContentAspect = 4f / 3f;
        private const float VideoPrepareTimeoutSeconds = 12f;
        private const int VideoPrepareAttempts = 2;
        private const string StationText = "KSEA-TV 7";
#if UNITY_EDITOR
        private const string NarrativeSettingsAssetPath =
            "Assets/Data/FirstContact/FirstContactNarrativeSettings.asset";
#endif

        [Header("TV Output")]
        [SerializeField] private Renderer tvScreenRenderer;
        [SerializeField] private Material crtMaterialTemplate;

        [Header("Playlist")]
        [SerializeField] private FirstContactNewsBroadcastItem[] playlist =
            Array.Empty<FirstContactNewsBroadcastItem>();

        // Dialogue copy and timing are authored in Tools > Narrative Desk.
        [Header("Narrative Desk")]
        [Tooltip("Narrative Desk data containing the intro.news.clip.* dialogue beats.")]
        [SerializeField] private FirstContactNarrativeSettings narrativeSettings;

        private GameObject _runtimeRoot;
        private UnityCamera _captureCamera;
        private UnityCamera _overlayCaptureCamera;
        private Canvas _canvas;
        private RawImage _mediaImage;
        private Image _lowerThirdPanel;
        private Image _bannerPanel;
        private TextMeshProUGUI _stationText;
        private TextMeshProUGUI _bannerText;
        private TextMeshProUGUI _statusText;
        private TextMeshProUGUI _headlineText;
        private TextMeshProUGUI _captionText;
        private VideoPlayer _videoPlayer;
        private RenderTexture _mediaTexture;
        private RenderTexture _compositeTexture;
        private RenderTexture _overlayTexture;
        private Material _runtimeCrtMaterial;
        private Material _originalMaterial;
        private FirstContactNewsBroadcastItem _currentItem;
        private FirstContactNewsSubtitleDisplay _subtitleDisplay;
        private Coroutine _subtitleRoutine;
        private readonly List<NarrativeBeat> _activeSubtitleBeats = new();
        private bool _runtimeReady;
        private bool _stopRequested;
        private bool _isPlaying;

        public bool IsPlaying => _isPlaying;
        public int PlaylistCount => playlist?.Length ?? 0;
        public NarrativeScenarioAsset NarrativeScenario => ResolveNarrativeScenario();

        public bool IsConfigured
        {
            get
            {
                if (tvScreenRenderer == null ||
                    crtMaterialTemplate == null ||
                    playlist == null ||
                    playlist.Length == 0)
                {
                    return false;
                }

                foreach (FirstContactNewsBroadcastItem item in playlist)
                {
                    if (item == null || !item.IsConfigured)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        public void Configure(
            Renderer screenRenderer,
            Material crtTemplate,
            FirstContactNewsBroadcastItem[] items)
        {
            tvScreenRenderer = screenRenderer;
            crtMaterialTemplate = crtTemplate;
            playlist = items ?? Array.Empty<FirstContactNewsBroadcastItem>();
        }

        /// <summary>
        /// The sequence controller supplies its existing HUD as a host. The
        /// display is created only while playing, so no scene layout is rebuilt.
        /// </summary>
        public void SetSubtitleHost(FirstContactIntroHud hud)
        {
            if (hud == null)
            {
                Debug.LogWarning(
                    "[FirstContactNewsSubtitle] Intro HUD host is missing.",
                    this);
                return;
            }

            _subtitleDisplay = hud.GetComponent<FirstContactNewsSubtitleDisplay>();
            if (_subtitleDisplay == null)
            {
                _subtitleDisplay = hud.gameObject.AddComponent<FirstContactNewsSubtitleDisplay>();
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            // External Narrative Desk synchronization can update the scene YAML
            // while this scene remains open. Reconnect the in-memory component
            // without reloading or rebuilding the user's scene.
            if (narrativeSettings == null)
            {
                narrativeSettings =
                    AssetDatabase.LoadAssetAtPath<FirstContactNarrativeSettings>(
                        NarrativeSettingsAssetPath);
            }
        }
#endif

        private void Awake()
        {
            EnsureRuntime();
            TurnOffImmediate();
        }

        private void OnDisable()
        {
            StopBroadcast();
        }

        private void OnDestroy()
        {
            if (tvScreenRenderer != null &&
                _runtimeCrtMaterial != null &&
                tvScreenRenderer.sharedMaterial == _runtimeCrtMaterial)
            {
                tvScreenRenderer.sharedMaterial = _originalMaterial;
            }

            if (_mediaTexture != null)
            {
                _mediaTexture.Release();
                Destroy(_mediaTexture);
            }

            if (_compositeTexture != null)
            {
                _compositeTexture.Release();
                Destroy(_compositeTexture);
            }

            if (_overlayTexture != null)
            {
                _overlayTexture.Release();
                Destroy(_overlayTexture);
            }

            if (_runtimeCrtMaterial != null)
            {
                Destroy(_runtimeCrtMaterial);
            }

            if (_runtimeRoot != null)
            {
                Destroy(_runtimeRoot);
            }
        }

        public IEnumerator PlayBroadcastRoutine()
        {
            if (_isPlaying)
            {
                yield break;
            }

            EnsureRuntime();
            if (!_runtimeReady || !IsConfigured)
            {
                Debug.LogWarning(
                    "[FirstContactNewsBroadcastPlayer] Broadcast is not fully configured.",
                    this);
                TurnOffImmediate();
                yield break;
            }

            _isPlaying = true;
            _stopRequested = false;
            SetTelevisionPowered(true);

            for (int playlistIndex = 0; playlistIndex < playlist.Length; playlistIndex++)
            {
                if (_stopRequested)
                {
                    break;
                }

                FirstContactNewsBroadcastItem item = playlist[playlistIndex];
                _currentItem = item;
                RefreshOverlay();
                SetOverlayVisible(true);

                if (item.MediaType == FirstContactNewsMediaType.Video)
                {
                    yield return PlayVideoRoutine(item, playlistIndex);
                }
                else
                {
                    StartSubtitleTrack(playlistIndex);
                    yield return PlayStillRoutine(item);
                }

                StopSubtitleTrack();
            }

            _currentItem = null;
            _isPlaying = false;
            StopSubtitleTrack(immediate: true);
            TurnOffImmediate();
        }

        public void StopBroadcast()
        {
            _stopRequested = true;
            _isPlaying = false;
            if (_videoPlayer != null)
            {
                _videoPlayer.Stop();
            }

            StopSubtitleTrack(immediate: true);
            TurnOffImmediate();
        }

        private IEnumerator PlayVideoRoutine(FirstContactNewsBroadcastItem item, int playlistIndex)
        {
            ClearRenderTexture(_mediaTexture, Color.black);
            _mediaImage.texture = _mediaTexture;
            _mediaImage.color = Color.white;
            ApplyMediaAspect(
                item.VideoClip != null ? (float)item.VideoClip.width : 16f,
                item.VideoClip != null ? (float)item.VideoClip.height : 9f);

            bool prepared = false;
            for (int attempt = 1;
                 attempt <= VideoPrepareAttempts && !_stopRequested;
                 attempt++)
            {
                _videoPlayer.Stop();
                _videoPlayer.enabled = true;
                _videoPlayer.source = VideoSource.VideoClip;
                _videoPlayer.clip = item.VideoClip;
                _videoPlayer.targetTexture = _mediaTexture;
                _videoPlayer.audioOutputMode = VideoAudioOutputMode.None;
                _videoPlayer.Prepare();

                float prepareElapsed = 0f;
                while (!_stopRequested &&
                       !_videoPlayer.isPrepared &&
                       prepareElapsed < VideoPrepareTimeoutSeconds)
                {
                    // Large editor hitches should not consume the whole timeout in
                    // one frame while the native video decoder is also stalled.
                    prepareElapsed += Mathf.Min(Time.unscaledDeltaTime, 0.1f);
                    yield return null;
                }

                if (_videoPlayer.isPrepared)
                {
                    prepared = true;
                    break;
                }

                if (attempt < VideoPrepareAttempts)
                {
                    Debug.LogWarning(
                        $"[FirstContactNewsBroadcastPlayer] Retrying preparation for " +
                        $"{item.VideoClip.name}.",
                        this);
                }

                yield return null;
            }

            if (_stopRequested)
            {
                yield break;
            }

            if (!prepared)
            {
                Debug.LogWarning(
                    $"[FirstContactNewsBroadcastPlayer] Timed out preparing " +
                    $"{item.VideoClip.name} after {VideoPrepareAttempts} attempts.",
                    this);
                yield break;
            }

            bool reachedEnd = false;
            bool playbackError = false;
            VideoPlayer.EventHandler completedHandler = _ => reachedEnd = true;
            VideoPlayer.ErrorEventHandler errorHandler = (_, message) =>
            {
                playbackError = true;
                Debug.LogWarning(
                    $"[FirstContactNewsBroadcastPlayer] Video playback failed: {message}",
                    this);
            };

            _videoPlayer.loopPointReached += completedHandler;
            _videoPlayer.errorReceived += errorHandler;
            _videoPlayer.Play();
            StartSubtitleTrack(playlistIndex);

            double clipLength = ResolveClipLengthSeconds(item.VideoClip);
            float playbackTimeout = (float)clipLength + 3f;
            float playbackElapsed = 0f;

            while (!_stopRequested &&
                   !reachedEnd &&
                   !playbackError &&
                   playbackElapsed < playbackTimeout)
            {
                playbackElapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            _videoPlayer.loopPointReached -= completedHandler;
            _videoPlayer.errorReceived -= errorHandler;
            _videoPlayer.Stop();
        }

        private static double ResolveClipLengthSeconds(VideoClip clip)
        {
            const double fallbackLengthSeconds = 10d;
            const double maximumPlaybackLengthSeconds = 12d;

            if (clip == null)
            {
                return fallbackLengthSeconds;
            }

            if (clip.frameRate > 0.01d && clip.frameCount > 0)
            {
                double frameBasedLength = clip.frameCount / clip.frameRate;
                return Math.Min(frameBasedLength, maximumPlaybackLengthSeconds);
            }

            if (clip.length > 0.01d)
            {
                return Math.Min(clip.length, maximumPlaybackLengthSeconds);
            }

            return fallbackLengthSeconds;
        }

        private IEnumerator PlayStillRoutine(FirstContactNewsBroadcastItem item)
        {
            _videoPlayer.Stop();
            _mediaImage.texture = item.StillImage;
            _mediaImage.color = Color.white;
            ApplyMediaAspect(item.StillImage.width, item.StillImage.height);

            float elapsed = 0f;
            while (!_stopRequested && elapsed < item.StillImageSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        private void StartSubtitleTrack(int playlistIndex)
        {
            StopSubtitleTrack();
            if (_subtitleDisplay == null)
            {
                Debug.LogWarning(
                    $"[FirstContactNewsSubtitle] No display for clip {playlistIndex}.",
                    this);
                return;
            }

            NarrativeScenarioAsset scenario = ResolveNarrativeScenario();
            if (scenario == null)
            {
                Debug.LogWarning(
                    $"[FirstContactNewsSubtitle] Narrative scenario is missing for " +
                    $"clip {playlistIndex}.",
                    this);
                return;
            }

            string triggerEvent = $"intro.news.clip.{playlistIndex}";
            _activeSubtitleBeats.Clear();
            IReadOnlyList<NarrativeBeat> beats = scenario.Beats;
            for (int i = 0; i < beats.Count; i++)
            {
                NarrativeBeat beat = beats[i];
                if (beat != null &&
                    beat.enabled &&
                    string.Equals(
                        beat.triggerEvent,
                        triggerEvent,
                        StringComparison.OrdinalIgnoreCase))
                {
                    _activeSubtitleBeats.Add(beat);
                }
            }

            if (_activeSubtitleBeats.Count == 0)
            {
                Debug.LogWarning(
                    $"[FirstContactNewsSubtitle] No beats matched {triggerEvent}.",
                    this);
                return;
            }

            _activeSubtitleBeats.Sort((left, right) => left.order.CompareTo(right.order));
            _subtitleRoutine = StartCoroutine(PlaySubtitleTrackRoutine(scenario));
        }

        private NarrativeScenarioAsset ResolveNarrativeScenario()
        {
            NarrativeScenarioAsset scenario = narrativeSettings != null
                ? narrativeSettings.narrativeScenario
                : null;
#if UNITY_EDITOR
            if (scenario == null)
            {
                narrativeSettings =
                    AssetDatabase.LoadAssetAtPath<FirstContactNarrativeSettings>(
                        NarrativeSettingsAssetPath);
                scenario = narrativeSettings != null
                    ? narrativeSettings.narrativeScenario
                    : null;
            }
#endif
            return scenario;
        }

        private void StopSubtitleTrack(bool immediate = false)
        {
            if (_subtitleRoutine != null)
            {
                StopCoroutine(_subtitleRoutine);
                _subtitleRoutine = null;
            }

            if (immediate)
            {
                _subtitleDisplay?.HideImmediate();
            }
            else
            {
                _subtitleDisplay?.Hide();
            }
        }

        private IEnumerator PlaySubtitleTrackRoutine(NarrativeScenarioAsset scenario)
        {
            const float entryDelaySeconds = 0.15f;
            float entryElapsed = 0f;
            while (!_stopRequested && entryElapsed < entryDelaySeconds)
            {
                entryElapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            for (int i = 0; i < _activeSubtitleBeats.Count; i++)
            {
                if (_stopRequested)
                {
                    yield break;
                }

                NarrativeBeat beat = _activeSubtitleBeats[i];
                NarrativeTrace.Emit(scenario.ScenarioId, beat.id, "enter");
                _subtitleDisplay?.Show(beat.ResolveSpeaker(), beat.ResolveText());

                float duration = Mathf.Max(0.1f, beat.minimumSeconds);
                float elapsed = 0f;
                while (!_stopRequested && elapsed < duration)
                {
                    elapsed += Time.unscaledDeltaTime;
                    yield return null;
                }

                if (_stopRequested)
                {
                    yield break;
                }

                _subtitleDisplay?.Hide();
                NarrativeTrace.Emit(scenario.ScenarioId, beat.id, "exit");
            }

            _subtitleRoutine = null;
        }

        private void EnsureRuntime()
        {
            if (_runtimeReady || !IsConfigured)
            {
                return;
            }

            _mediaTexture = CreateRenderTexture("FirstContact_NewsMedia");
            _compositeTexture = CreateRenderTexture("FirstContact_NewsComposite");
            _overlayTexture = CreateRenderTexture("FirstContact_NewsOverlay");
            ClearRenderTexture(_mediaTexture, Color.black);
            ClearRenderTexture(_compositeTexture, Color.black);
            ClearRenderTexture(_overlayTexture, Color.clear);

            _runtimeRoot = new GameObject("NewsBroadcast_Runtime");
            _runtimeRoot.hideFlags = HideFlags.DontSave;
            _runtimeRoot.layer = DefaultCaptureLayer;

            CreateCaptureCamera();
            CreateOverlayCaptureCamera();
            CreateCanvas();
            CreateNewsLayout();
            CreateVideoPlayer();

            _originalMaterial = tvScreenRenderer.sharedMaterial;
            _runtimeCrtMaterial = new Material(crtMaterialTemplate)
            {
                name = "NewsBroadcast_CRT_Runtime",
                hideFlags = HideFlags.DontSave
            };
            _runtimeCrtMaterial.SetTexture("_BaseMap", _compositeTexture);
            _runtimeCrtMaterial.SetTexture("_OverlayMap", _overlayTexture);
            tvScreenRenderer.sharedMaterial = _runtimeCrtMaterial;

            _runtimeReady = true;
        }

        private void CreateCaptureCamera()
        {
            GameObject cameraObject = new("NewsBroadcast_CaptureCamera");
            cameraObject.hideFlags = HideFlags.DontSave;
            cameraObject.layer = DefaultCaptureLayer;
            cameraObject.transform.SetParent(_runtimeRoot.transform, false);
            cameraObject.transform.localPosition = new Vector3(0f, 0f, -10f);
            cameraObject.transform.localRotation = Quaternion.Euler(0f, 0f, 180f);

            _captureCamera = cameraObject.AddComponent<UnityCamera>();
            _captureCamera.clearFlags = CameraClearFlags.SolidColor;
            _captureCamera.backgroundColor = Color.black;
            _captureCamera.cullingMask = 1 << DefaultCaptureLayer;
            _captureCamera.orthographic = true;
            _captureCamera.orthographicSize = 3.6f;
            _captureCamera.nearClipPlane = 0.01f;
            _captureCamera.farClipPlane = 20f;
            _captureCamera.depth = -100f;
            _captureCamera.targetTexture = _compositeTexture;
            _captureCamera.allowHDR = false;
            _captureCamera.allowMSAA = false;

            UniversalAdditionalCameraData cameraData =
                cameraObject.AddComponent<UniversalAdditionalCameraData>();
            cameraData.renderType = CameraRenderType.Base;
            cameraData.renderPostProcessing = false;
            cameraData.antialiasing = AntialiasingMode.None;
        }

        private void CreateOverlayCaptureCamera()
        {
            GameObject cameraObject = new("NewsBroadcast_OverlayCamera");
            cameraObject.hideFlags = HideFlags.DontSave;
            cameraObject.layer = OverlayCaptureLayer;
            cameraObject.transform.SetParent(_runtimeRoot.transform, false);
            cameraObject.transform.localPosition = new Vector3(0f, 0f, -10f);
            cameraObject.transform.localRotation = Quaternion.Euler(0f, 0f, 180f);

            _overlayCaptureCamera = cameraObject.AddComponent<UnityCamera>();
            _overlayCaptureCamera.clearFlags = CameraClearFlags.SolidColor;
            _overlayCaptureCamera.backgroundColor = Color.clear;
            _overlayCaptureCamera.cullingMask = 1 << OverlayCaptureLayer;
            _overlayCaptureCamera.orthographic = true;
            _overlayCaptureCamera.orthographicSize = 3.6f;
            _overlayCaptureCamera.nearClipPlane = 0.01f;
            _overlayCaptureCamera.farClipPlane = 20f;
            _overlayCaptureCamera.depth = -99f;
            _overlayCaptureCamera.targetTexture = _overlayTexture;
            _overlayCaptureCamera.allowHDR = false;
            _overlayCaptureCamera.allowMSAA = false;

            UniversalAdditionalCameraData cameraData =
                cameraObject.AddComponent<UniversalAdditionalCameraData>();
            cameraData.renderType = CameraRenderType.Base;
            cameraData.renderPostProcessing = false;
            cameraData.antialiasing = AntialiasingMode.None;
        }

        private void CreateCanvas()
        {
            GameObject canvasObject = new("NewsBroadcast_Canvas", typeof(RectTransform));
            canvasObject.hideFlags = HideFlags.DontSave;
            canvasObject.layer = DefaultCaptureLayer;
            canvasObject.transform.SetParent(_runtimeRoot.transform, false);

            RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(CompositeWidth, CompositeHeight);
            canvasRect.localPosition = Vector3.zero;
            canvasRect.localRotation = Quaternion.identity;
            canvasRect.localScale = Vector3.one * 0.01f;

            _canvas = canvasObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            _canvas.worldCamera = _captureCamera;
            _canvas.sortingOrder = 0;

            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;
        }

        private void CreateNewsLayout()
        {
            Image blackBackground = CreatePanel(
                "BlackBackground",
                _canvas.transform,
                Vector2.zero,
                Vector2.one,
                Color.black);

            _mediaImage = CreateRawImage(
                "Media",
                blackBackground.transform,
                Vector2.zero,
                Vector2.one);

            _lowerThirdPanel = CreatePanel(
                "LowerThird",
                _canvas.transform,
                new Vector2(0.12f, 0.055f),
                new Vector2(0.88f, 0.285f),
                new Color(0.025f, 0.045f, 0.13f, 0.98f),
                OverlayCaptureLayer);

            CreatePanel(
                "AccentStripe",
                _lowerThirdPanel.transform,
                Vector2.zero,
                new Vector2(1f, 0.055f),
                new Color(0.58f, 0.055f, 0.075f, 1f),
                OverlayCaptureLayer);

            _bannerPanel = CreatePanel(
                "BulletinBanner",
                _lowerThirdPanel.transform,
                new Vector2(0f, 0.72f),
                new Vector2(0.29f, 1f),
                new Color(0.58f, 0.055f, 0.075f, 1f),
                OverlayCaptureLayer);

            _stationText = CreateText(
                "Station",
                _lowerThirdPanel.transform,
                new Vector2(0.32f, 0.74f),
                new Vector2(0.59f, 0.98f),
                22f,
                FontStyles.Bold,
                TextAlignmentOptions.Left,
                new Color(0.96f, 0.91f, 0.68f, 1f),
                OverlayCaptureLayer);

            _statusText = CreateText(
                "Status",
                _lowerThirdPanel.transform,
                new Vector2(0.58f, 0.74f),
                new Vector2(0.965f, 0.98f),
                19f,
                FontStyles.Normal,
                TextAlignmentOptions.Right,
                new Color(0.88f, 0.9f, 0.94f, 1f),
                OverlayCaptureLayer);

            _bannerText = CreateText(
                "Banner",
                _bannerPanel.transform,
                new Vector2(0.05f, 0f),
                new Vector2(0.95f, 1f),
                21f,
                FontStyles.Bold,
                TextAlignmentOptions.Center,
                new Color(1f, 0.96f, 0.82f, 1f),
                OverlayCaptureLayer);

            _headlineText = CreateText(
                "Headline",
                _lowerThirdPanel.transform,
                new Vector2(0.035f, 0.36f),
                new Vector2(0.965f, 0.7f),
                30f,
                FontStyles.Bold,
                TextAlignmentOptions.Left,
                new Color(0.98f, 0.94f, 0.74f, 1f),
                OverlayCaptureLayer);

            _captionText = CreateText(
                "Caption",
                _lowerThirdPanel.transform,
                new Vector2(0.035f, 0.075f),
                new Vector2(0.965f, 0.35f),
                21f,
                FontStyles.Normal,
                TextAlignmentOptions.TopLeft,
                new Color(0.92f, 0.93f, 0.9f, 1f),
                OverlayCaptureLayer);
        }

        private void CreateVideoPlayer()
        {
            _videoPlayer = gameObject.GetComponent<VideoPlayer>();
            if (_videoPlayer == null)
            {
                _videoPlayer = gameObject.AddComponent<VideoPlayer>();
            }

            _videoPlayer.playOnAwake = false;
            _videoPlayer.isLooping = false;
            _videoPlayer.waitForFirstFrame = true;
            _videoPlayer.skipOnDrop = true;
            _videoPlayer.renderMode = VideoRenderMode.RenderTexture;
            _videoPlayer.targetTexture = _mediaTexture;
            _videoPlayer.audioOutputMode = VideoAudioOutputMode.None;
        }

        private void RefreshOverlay()
        {
            if (_currentItem == null)
            {
                return;
            }

            _stationText.text = StationText;
            _bannerText.text = _currentItem.BannerText;
            _statusText.text = _currentItem.StatusText;
            _headlineText.text = _currentItem.HeadlineText;
            _captionText.text = _currentItem.CaptionText;
        }

        private void SetTelevisionPowered(bool powered)
        {
            if (!_runtimeReady)
            {
                return;
            }

            _runtimeRoot.SetActive(powered);
            if (_captureCamera != null)
            {
                _captureCamera.enabled = powered;
            }

            if (_overlayCaptureCamera != null)
            {
                _overlayCaptureCamera.enabled = powered;
            }

            _runtimeCrtMaterial.SetColor("_BaseColor", powered ? Color.white : Color.black);
        }

        private void TurnOffImmediate()
        {
            if (!_runtimeReady)
            {
                return;
            }

            SetOverlayVisible(false);
            _mediaImage.texture = null;
            _mediaImage.color = Color.black;
            SetTelevisionPowered(false);
        }

        private void SetOverlayVisible(bool visible)
        {
            if (_lowerThirdPanel != null)
            {
                _lowerThirdPanel.gameObject.SetActive(visible);
            }

            if (_stationText != null)
            {
                _stationText.gameObject.SetActive(visible);
            }

            if (_statusText != null)
            {
                _statusText.gameObject.SetActive(visible);
            }
        }

        private void ApplyMediaAspect(float width, float height)
        {
            if (_mediaImage == null)
            {
                return;
            }

            float sourceAspect = width > 0f && height > 0f
                ? width / height
                : BroadcastContentAspect;
            float outputAspect = CompositeWidth / (float)CompositeHeight;
            RectTransform rect = _mediaImage.rectTransform;

            if (BroadcastContentAspect > outputAspect)
            {
                float heightFraction = outputAspect / BroadcastContentAspect;
                rect.anchorMin = new Vector2(0f, (1f - heightFraction) * 0.5f);
                rect.anchorMax = new Vector2(1f, (1f + heightFraction) * 0.5f);
            }
            else
            {
                float widthFraction = BroadcastContentAspect / outputAspect;
                rect.anchorMin = new Vector2((1f - widthFraction) * 0.5f, 0f);
                rect.anchorMax = new Vector2((1f + widthFraction) * 0.5f, 1f);
            }

            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            if (sourceAspect > BroadcastContentAspect)
            {
                float visibleWidth = BroadcastContentAspect / sourceAspect;
                _mediaImage.uvRect = new Rect(
                    (1f - visibleWidth) * 0.5f,
                    0f,
                    visibleWidth,
                    1f);
            }
            else if (sourceAspect < BroadcastContentAspect)
            {
                float visibleHeight = sourceAspect / BroadcastContentAspect;
                _mediaImage.uvRect = new Rect(
                    0f,
                    (1f - visibleHeight) * 0.5f,
                    1f,
                    visibleHeight);
            }
            else
            {
                _mediaImage.uvRect = new Rect(0f, 0f, 1f, 1f);
            }
        }

        private static RenderTexture CreateRenderTexture(string textureName)
        {
            RenderTextureDescriptor descriptor = new(
                CompositeWidth,
                CompositeHeight,
                RenderTextureFormat.ARGB32,
                0)
            {
                msaaSamples = 1,
                useMipMap = false,
                autoGenerateMips = false,
                sRGB = true
            };

            RenderTexture texture = new(descriptor)
            {
                name = textureName,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };
            texture.Create();
            return texture;
        }

        private static void ClearRenderTexture(RenderTexture texture, Color color)
        {
            if (texture == null)
            {
                return;
            }

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = texture;
            GL.Clear(true, true, color);
            RenderTexture.active = previous;
        }

        private static Image CreatePanel(
            string objectName,
            Transform parent,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Color color,
            int captureLayer = DefaultCaptureLayer)
        {
            GameObject panelObject = CreateUiObject(objectName, parent, captureLayer);
            Image image = panelObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            Stretch(image.rectTransform, anchorMin, anchorMax);
            return image;
        }

        private static RawImage CreateRawImage(
            string objectName,
            Transform parent,
            Vector2 anchorMin,
            Vector2 anchorMax)
        {
            GameObject imageObject = CreateUiObject(objectName, parent);
            RawImage image = imageObject.AddComponent<RawImage>();
            image.color = Color.white;
            image.raycastTarget = false;
            Stretch(image.rectTransform, anchorMin, anchorMax);
            return image;
        }

        private static TextMeshProUGUI CreateText(
            string objectName,
            Transform parent,
            Vector2 anchorMin,
            Vector2 anchorMax,
            float fontSize,
            FontStyles style,
            TextAlignmentOptions alignment,
            Color color,
            int captureLayer = DefaultCaptureLayer)
        {
            GameObject textObject = CreateUiObject(objectName, parent, captureLayer);
            TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.alignment = alignment;
            text.color = color;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
            Stretch(text.rectTransform, anchorMin, anchorMax);
            return text;
        }

        private static GameObject CreateUiObject(
            string objectName,
            Transform parent,
            int captureLayer = DefaultCaptureLayer)
        {
            GameObject gameObject = new(objectName, typeof(RectTransform));
            gameObject.hideFlags = HideFlags.DontSave;
            gameObject.layer = captureLayer;
            gameObject.transform.SetParent(parent, false);
            return gameObject;
        }

        private static void Stretch(
            RectTransform rect,
            Vector2 anchorMin,
            Vector2 anchorMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
