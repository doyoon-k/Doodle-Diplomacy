using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DoodleDiplomacy.Devices
{
    [DisallowMultipleComponent]
    public class TerminalCrtCompositeDisplay : MonoBehaviour
    {
        [Header("Source")]
        [SerializeField] private RectTransform sourcePanel;
        [SerializeField] private TMP_Text sourceText;

        [Header("Output")]
        [SerializeField] private Material crtMaterial;
        [SerializeField] private Vector2Int outputResolution = new(1024, 512);
        [SerializeField] private bool overridePixelResolution = true;
        [SerializeField] private Vector2Int pixelResolution = new(320, 180);
        [SerializeField, Range(0f, 1f)] private float pixelateStrength = 1f;

        [Header("Cameras")]
        [SerializeField] private UnityEngine.Camera mainCamera;
        [SerializeField] private float captureDistance = 0.5f;
        [SerializeField] private float fitPadding = 1.01f;

        [Header("Layer Routing")]
        [SerializeField, Range(0, 31)] private int captureLayer = 30;
        [SerializeField] private bool updateCapturePoseEachFrame = true;

        private const string TerminalCanvasPath = "TerminalCanvas";
        private const string ScreenPanelPath = "TerminalCanvas/ScreenPanel";
        private const string TerminalTextPath = "TerminalCanvas/TerminalText";
        private const string DisplayCanvasName = "TerminalDisplayCanvas";
        private const string CaptureCameraName = "TerminalCaptureCamera";
        private const string DisplayObjectName = "CrtCompositeDisplay";
        private const string PixelResolutionPropertyName = "_PixelResolution";
        private const string PixelateStrengthPropertyName = "_PixelateStrength";

        private RawImage _displayImage;
        private Canvas _sourceCanvas;
        private Canvas _displayCanvas;
        private Material _displayMaterialInstance;
        private UnityEngine.Camera _captureCamera;
        private RenderTexture _renderTexture;
        private int _originalMainMask;
        private int _originalCanvasLayer;
        private int _originalDisplayCanvasLayer;
        private int _originalPanelLayer;
        private int _originalTextLayer;
        private bool _mainMaskOverridden;
        private bool _initialized;
        private readonly Vector3[] _panelCorners = new Vector3[4];

        private void Awake()
        {
            SetupComposite();
        }

        private void OnEnable()
        {
            if (_initialized)
                ApplyLayerRouting();
        }

        private void LateUpdate()
        {
            if (!_initialized || !updateCapturePoseEachFrame || _captureCamera == null || sourcePanel == null)
                return;

            AlignCaptureCameraToPanel();
        }

        private void OnDisable()
        {
            RestoreLayerRouting();
        }

        private void OnDestroy()
        {
            RestoreLayerRouting();

            if (_renderTexture != null)
            {
                if (_captureCamera != null && _captureCamera.targetTexture == _renderTexture)
                    _captureCamera.targetTexture = null;

                _renderTexture.Release();
                Destroy(_renderTexture);
                _renderTexture = null;
            }

            if (_displayMaterialInstance != null)
            {
                Destroy(_displayMaterialInstance);
                _displayMaterialInstance = null;
            }
        }

        [ContextMenu("Setup CRT Composite")]
        public void SetupComposite()
        {
            AutoAssignReferences();
            if (sourcePanel == null)
            {
                Debug.LogWarning("[TerminalCrtCompositeDisplay] ScreenPanel was not found.", this);
                return;
            }

            EnsureRenderTexture();
            EnsureCaptureCamera();
            EnsureDisplayImage();
            ApplyDisplayMaterial();
            AlignDisplayImageToPanel();
            AlignCaptureCameraToPanel();
            CacheOriginalLayers();
            ApplyLayerRouting();

            _initialized = true;
        }

        private void AutoAssignReferences()
        {
            if (sourcePanel == null)
            {
                Transform source = transform.Find(ScreenPanelPath);
                sourcePanel = source as RectTransform;
            }

            if (sourceText == null)
            {
                Transform textTransform = transform.Find(TerminalTextPath);
                if (textTransform != null)
                    sourceText = textTransform.GetComponent<TMP_Text>();
            }

            if (_sourceCanvas == null && sourcePanel != null)
                _sourceCanvas = sourcePanel.GetComponentInParent<Canvas>();

            if (mainCamera == null)
                mainCamera = UnityEngine.Camera.main;
        }

        private void EnsureRenderTexture()
        {
            int width = Mathf.Max(128, outputResolution.x);
            int height = Mathf.Max(128, outputResolution.y);

            if (_renderTexture != null && _renderTexture.width == width && _renderTexture.height == height)
                return;

            if (_renderTexture != null)
            {
                _renderTexture.Release();
                Destroy(_renderTexture);
            }

            _renderTexture = new RenderTexture(width, height, 16, RenderTextureFormat.ARGB32)
            {
                name = $"{name}_TerminalCompositeRT",
                useMipMap = false,
                autoGenerateMips = false,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            _renderTexture.Create();
        }

        private void EnsureCaptureCamera()
        {
            Transform cameraTransform = transform.Find(CaptureCameraName);
            if (cameraTransform == null)
            {
                GameObject cameraObject = new(CaptureCameraName);
                cameraTransform = cameraObject.transform;
                cameraTransform.SetParent(transform, false);
            }

            _captureCamera = cameraTransform.GetComponent<UnityEngine.Camera>();
            if (_captureCamera == null)
                _captureCamera = cameraTransform.gameObject.AddComponent<UnityEngine.Camera>();

            _captureCamera.enabled = true;
            _captureCamera.orthographic = true;
            _captureCamera.clearFlags = CameraClearFlags.SolidColor;
            _captureCamera.backgroundColor = Color.clear;
            _captureCamera.allowHDR = false;
            _captureCamera.allowMSAA = false;
            _captureCamera.depth = -100f;
            _captureCamera.targetTexture = _renderTexture;
        }

        private void EnsureDisplayImage()
        {
            if (_sourceCanvas == null && sourcePanel != null)
                _sourceCanvas = sourcePanel.GetComponentInParent<Canvas>();

            if (_sourceCanvas == null)
            {
                Debug.LogWarning("[TerminalCrtCompositeDisplay] Source canvas was not found.", this);
                return;
            }

            Transform displayCanvasTransform = transform.Find(DisplayCanvasName);
            if (displayCanvasTransform == null)
            {
                GameObject canvasObject = new(DisplayCanvasName, typeof(RectTransform), typeof(Canvas));
                displayCanvasTransform = canvasObject.transform;
                displayCanvasTransform.SetParent(transform, false);
            }

            _displayCanvas = displayCanvasTransform.GetComponent<Canvas>();
            if (_displayCanvas == null)
                _displayCanvas = displayCanvasTransform.gameObject.AddComponent<Canvas>();

            _displayCanvas.renderMode = RenderMode.WorldSpace;
            _displayCanvas.worldCamera = null;
            _displayCanvas.pixelPerfect = false;
            _displayCanvas.overrideSorting = false;
            _displayCanvas.additionalShaderChannels = _sourceCanvas.additionalShaderChannels;

            RectTransform sourceCanvasRect = _sourceCanvas.transform as RectTransform;
            RectTransform displayCanvasRect = (RectTransform)displayCanvasTransform;
            if (sourceCanvasRect != null)
            {
                displayCanvasRect.anchorMin = sourceCanvasRect.anchorMin;
                displayCanvasRect.anchorMax = sourceCanvasRect.anchorMax;
                displayCanvasRect.pivot = sourceCanvasRect.pivot;
                displayCanvasRect.anchoredPosition = sourceCanvasRect.anchoredPosition;
                displayCanvasRect.sizeDelta = sourceCanvasRect.sizeDelta;
                displayCanvasRect.localPosition = sourceCanvasRect.localPosition;
                displayCanvasRect.localRotation = sourceCanvasRect.localRotation;
                displayCanvasRect.localScale = sourceCanvasRect.localScale;
            }

            Transform displayTransform = displayCanvasTransform.Find(DisplayObjectName);
            if (displayTransform == null)
            {
                GameObject displayObject = new(DisplayObjectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
                displayTransform = displayObject.transform;
                displayTransform.SetParent(displayCanvasTransform, false);
            }

            _displayImage = displayTransform.GetComponent<RawImage>();
            if (_displayImage == null)
                _displayImage = displayTransform.gameObject.AddComponent<RawImage>();

            RectTransform displayRect = (RectTransform)displayTransform;
            displayRect.anchorMin = sourcePanel.anchorMin;
            displayRect.anchorMax = sourcePanel.anchorMax;
            displayRect.pivot = sourcePanel.pivot;
            displayRect.anchoredPosition = sourcePanel.anchoredPosition;
            displayRect.sizeDelta = sourcePanel.sizeDelta;
            displayRect.localScale = Vector3.one;
            displayRect.localRotation = Quaternion.identity;
            displayTransform.SetAsLastSibling();
            displayCanvasTransform.SetAsLastSibling();

            _displayImage.texture = _renderTexture;
            _displayImage.color = Color.white;
            _displayImage.raycastTarget = false;
        }

        private void ApplyDisplayMaterial()
        {
            if (_displayImage == null)
                return;

            if (crtMaterial == null)
            {
                Debug.LogWarning("[TerminalCrtCompositeDisplay] CRT material is not assigned.", this);
                return;
            }

            if (_displayMaterialInstance == null || _displayMaterialInstance.shader != crtMaterial.shader)
            {
                if (_displayMaterialInstance != null)
                    Destroy(_displayMaterialInstance);

                _displayMaterialInstance = new Material(crtMaterial)
                {
                    name = $"{crtMaterial.name}_TerminalInstance"
                };
            }

            _displayImage.material = _displayMaterialInstance;
            ApplyPixelSettings();
        }

        private void ApplyPixelSettings()
        {
            Material target = _displayMaterialInstance != null ? _displayMaterialInstance : crtMaterial;
            if (target == null)
                return;

            if (target.HasProperty(PixelateStrengthPropertyName))
                target.SetFloat(PixelateStrengthPropertyName, Mathf.Clamp01(pixelateStrength));

            if (!overridePixelResolution || !target.HasProperty(PixelResolutionPropertyName))
                return;

            int width = Mathf.Max(1, pixelResolution.x);
            int height = Mathf.Max(1, pixelResolution.y);
            target.SetVector(PixelResolutionPropertyName, new Vector4(width, height, 0f, 0f));
        }

        public void SetPixelResolution(Vector2Int resolution)
        {
            pixelResolution = new Vector2Int(Mathf.Max(1, resolution.x), Mathf.Max(1, resolution.y));
            ApplyPixelSettings();
        }

        public void SetPixelateStrength(float strength)
        {
            pixelateStrength = Mathf.Clamp01(strength);
            ApplyPixelSettings();
        }

        private void AlignDisplayImageToPanel()
        {
            if (_displayImage == null || sourcePanel == null)
                return;

            RectTransform displayRect = _displayImage.rectTransform;
            displayRect.anchorMin = sourcePanel.anchorMin;
            displayRect.anchorMax = sourcePanel.anchorMax;
            displayRect.pivot = sourcePanel.pivot;
            displayRect.anchoredPosition = sourcePanel.anchoredPosition;
            displayRect.sizeDelta = sourcePanel.sizeDelta;
        }

        private void AlignCaptureCameraToPanel()
        {
            if (_captureCamera == null || sourcePanel == null)
                return;

            sourcePanel.GetWorldCorners(_panelCorners);

            Vector3 center = (_panelCorners[0] + _panelCorners[1] + _panelCorners[2] + _panelCorners[3]) * 0.25f;
            Vector3 forward = sourcePanel.forward.normalized;
            Vector3 up = sourcePanel.up.normalized;
            float distance = Mathf.Max(0.05f, captureDistance);

            _captureCamera.transform.position = center - forward * distance;
            _captureCamera.transform.rotation = Quaternion.LookRotation(forward, up);

            float width = Vector3.Distance(_panelCorners[0], _panelCorners[3]);
            float height = Vector3.Distance(_panelCorners[0], _panelCorners[1]);
            if (width <= 0.0001f || height <= 0.0001f)
                return;

            float paddedHeight = height * Mathf.Max(1f, fitPadding);
            float paddedWidth = width * Mathf.Max(1f, fitPadding);
            _captureCamera.orthographicSize = paddedHeight * 0.5f;
            _captureCamera.aspect = paddedWidth / paddedHeight;
            _captureCamera.nearClipPlane = 0.01f;
            _captureCamera.farClipPlane = distance + 2f;
        }

        private void CacheOriginalLayers()
        {
            if (_sourceCanvas != null)
                _originalCanvasLayer = _sourceCanvas.gameObject.layer;

            if (_displayCanvas != null)
                _originalDisplayCanvasLayer = _displayCanvas.gameObject.layer;

            if (sourcePanel != null)
                _originalPanelLayer = sourcePanel.gameObject.layer;
            if (sourceText != null)
                _originalTextLayer = sourceText.gameObject.layer;
        }

        private void ApplyLayerRouting()
        {
            if (_sourceCanvas != null)
                _sourceCanvas.gameObject.layer = captureLayer;

            if (sourcePanel != null)
                sourcePanel.gameObject.layer = captureLayer;

            if (sourceText != null)
                sourceText.gameObject.layer = captureLayer;

            if (_displayCanvas != null)
                _displayCanvas.gameObject.layer = gameObject.layer;

            if (_displayImage != null)
                _displayImage.gameObject.layer = gameObject.layer;

            if (_captureCamera != null)
                _captureCamera.cullingMask = 1 << captureLayer;

            if (mainCamera == null)
                mainCamera = UnityEngine.Camera.main;

            if (mainCamera != null)
            {
                if (!_mainMaskOverridden)
                {
                    _originalMainMask = mainCamera.cullingMask;
                    _mainMaskOverridden = true;
                }

                mainCamera.cullingMask = _originalMainMask & ~(1 << captureLayer);
            }
        }

        private void RestoreLayerRouting()
        {
            if (_sourceCanvas != null)
                _sourceCanvas.gameObject.layer = _originalCanvasLayer;

            if (sourcePanel != null)
                sourcePanel.gameObject.layer = _originalPanelLayer;

            if (sourceText != null)
                sourceText.gameObject.layer = _originalTextLayer;

            if (_displayCanvas != null)
                _displayCanvas.gameObject.layer = _originalDisplayCanvasLayer;

            if (_mainMaskOverridden && mainCamera != null)
            {
                mainCamera.cullingMask = _originalMainMask;
                _mainMaskOverridden = false;
            }
        }

        private void OnValidate()
        {
            pixelResolution.x = Mathf.Max(1, pixelResolution.x);
            pixelResolution.y = Mathf.Max(1, pixelResolution.y);
            pixelateStrength = Mathf.Clamp01(pixelateStrength);

            if (_displayMaterialInstance != null)
                ApplyPixelSettings();
        }
    }
}
