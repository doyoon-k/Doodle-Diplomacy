using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DoodleDiplomacy.Devices
{
    [DisallowMultipleComponent]
    public class TerminalCrtCompositeDisplay : MonoBehaviour
    {
        [Header("Source")]
        [Tooltip("Source terminal screen panel that is captured into the CRT render texture.")]
        [SerializeField] private RectTransform sourcePanel;
        [Tooltip("Source terminal text used to validate that the source canvas is correctly wired.")]
        [SerializeField] private TMP_Text sourceText;

        [Header("Output")]
        [Tooltip("Material applied to the final RawImage to add CRT, scanline, and pixelation effects.")]
        [SerializeField] private Material crtMaterial;
        [Tooltip("Orthographic camera that captures the source panel into a render texture.")]
        [SerializeField] private UnityEngine.Camera captureCamera;
        [Tooltip("World-space output canvas that displays the CRT-processed render texture.")]
        [SerializeField] private Canvas displayCanvas;
        [Tooltip("RawImage that shows the captured terminal render texture.")]
        [SerializeField] private RawImage displayImage;
        [Tooltip("RenderTexture resolution used for the captured terminal image.")]
        [SerializeField] private Vector2Int outputResolution = new(1024, 512);
        [Tooltip("When enabled, sends Pixel Resolution to the CRT material for pixelated output.")]
        [SerializeField] private bool overridePixelResolution = true;
        [Tooltip("Virtual pixel grid size used by the CRT material when Override Pixel Resolution is enabled.")]
        [SerializeField] private Vector2Int pixelResolution = new(320, 180);
        [Tooltip("Strength of the CRT material pixelation effect, where 0 is clean and 1 is fully pixelated.")]
        [SerializeField, Range(0f, 1f)] private float pixelateStrength = 1f;

        [Header("Cameras")]
        [Tooltip("Main gameplay camera whose culling mask is adjusted so it does not directly render the source canvas.")]
        [SerializeField] private UnityEngine.Camera mainCamera;
        [Tooltip("Distance from the source panel used when positioning the capture camera.")]
        [SerializeField] private float captureDistance = 0.5f;
        [Tooltip("Extra framing multiplier around the captured source panel.")]
        [SerializeField] private float fitPadding = 1.01f;

        [Header("Layer Routing")]
        [Tooltip("Temporary Unity layer used for source canvas capture. Keep this separate from visible gameplay layers.")]
        [SerializeField, Range(0, 31)] private int captureLayer = 30;
        [Tooltip("Continuously realign the capture camera to the source panel in LateUpdate.")]
        [SerializeField] private bool updateCapturePoseEachFrame = true;

        private const string PixelResolutionPropertyName = "_PixelResolution";
        private const string PixelateStrengthPropertyName = "_PixelateStrength";

        private Canvas _sourceCanvas;
        private Material _displayMaterialInstance;
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
            if (!_initialized || !updateCapturePoseEachFrame || captureCamera == null || sourcePanel == null)
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
                if (captureCamera != null && captureCamera.targetTexture == _renderTexture)
                    captureCamera.targetTexture = null;

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
            if (!ValidateInspectorReferences())
            {
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

        private bool ValidateInspectorReferences()
        {
            bool valid = true;
            if (sourcePanel == null)
            {
                Debug.LogError("[TerminalCrtCompositeDisplay] Source panel must be assigned in the Inspector.", this);
                valid = false;
            }

            if (sourceText == null)
            {
                Debug.LogError("[TerminalCrtCompositeDisplay] Source text must be assigned in the Inspector.", this);
                valid = false;
            }

            if (crtMaterial == null)
            {
                Debug.LogError("[TerminalCrtCompositeDisplay] CRT material must be assigned in the Inspector.", this);
                valid = false;
            }

            if (mainCamera == null)
            {
                Debug.LogError("[TerminalCrtCompositeDisplay] Main camera must be assigned in the Inspector.", this);
                valid = false;
            }

            if (captureCamera == null)
            {
                Debug.LogError("[TerminalCrtCompositeDisplay] Capture camera must be assigned in the Inspector.", this);
                valid = false;
            }

            if (displayCanvas == null)
            {
                Debug.LogError("[TerminalCrtCompositeDisplay] Display canvas must be assigned in the Inspector.", this);
                valid = false;
            }

            if (displayImage == null)
            {
                Debug.LogError("[TerminalCrtCompositeDisplay] Display image must be assigned in the Inspector.", this);
                valid = false;
            }

            if (_sourceCanvas == null && sourcePanel != null)
                _sourceCanvas = sourcePanel.GetComponentInParent<Canvas>();

            if (_sourceCanvas == null)
            {
                Debug.LogError("[TerminalCrtCompositeDisplay] Source canvas must exist above the source panel.", this);
                valid = false;
            }

            return valid;
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
            captureCamera.enabled = true;
            captureCamera.orthographic = true;
            captureCamera.clearFlags = CameraClearFlags.SolidColor;
            captureCamera.backgroundColor = Color.clear;
            captureCamera.allowHDR = false;
            captureCamera.allowMSAA = false;
            captureCamera.depth = -100f;
            captureCamera.targetTexture = _renderTexture;
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

            displayCanvas.renderMode = RenderMode.WorldSpace;
            displayCanvas.worldCamera = null;
            displayCanvas.pixelPerfect = false;
            displayCanvas.overrideSorting = false;
            displayCanvas.additionalShaderChannels = _sourceCanvas.additionalShaderChannels;

            RectTransform sourceCanvasRect = _sourceCanvas.transform as RectTransform;
            RectTransform displayCanvasRect = displayCanvas.transform as RectTransform;
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

            RectTransform displayRect = displayImage.rectTransform;
            displayRect.anchorMin = sourcePanel.anchorMin;
            displayRect.anchorMax = sourcePanel.anchorMax;
            displayRect.pivot = sourcePanel.pivot;
            displayRect.anchoredPosition = sourcePanel.anchoredPosition;
            displayRect.sizeDelta = sourcePanel.sizeDelta;
            displayRect.localScale = Vector3.one;
            displayRect.localRotation = Quaternion.identity;
            displayImage.transform.SetAsLastSibling();
            displayCanvas.transform.SetAsLastSibling();

            displayImage.texture = _renderTexture;
            displayImage.color = Color.white;
            displayImage.raycastTarget = false;
        }

        private void ApplyDisplayMaterial()
        {
            if (displayImage == null)
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

            displayImage.material = _displayMaterialInstance;
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
            if (displayImage == null || sourcePanel == null)
                return;

            RectTransform displayRect = displayImage.rectTransform;
            displayRect.anchorMin = sourcePanel.anchorMin;
            displayRect.anchorMax = sourcePanel.anchorMax;
            displayRect.pivot = sourcePanel.pivot;
            displayRect.anchoredPosition = sourcePanel.anchoredPosition;
            displayRect.sizeDelta = sourcePanel.sizeDelta;
        }

        private void AlignCaptureCameraToPanel()
        {
            if (captureCamera == null || sourcePanel == null)
                return;

            sourcePanel.GetWorldCorners(_panelCorners);

            Vector3 center = (_panelCorners[0] + _panelCorners[1] + _panelCorners[2] + _panelCorners[3]) * 0.25f;
            Vector3 forward = sourcePanel.forward.normalized;
            Vector3 up = sourcePanel.up.normalized;
            float distance = Mathf.Max(0.05f, captureDistance);

            captureCamera.transform.position = center - forward * distance;
            captureCamera.transform.rotation = Quaternion.LookRotation(forward, up);

            float width = Vector3.Distance(_panelCorners[0], _panelCorners[3]);
            float height = Vector3.Distance(_panelCorners[0], _panelCorners[1]);
            if (width <= 0.0001f || height <= 0.0001f)
                return;

            float paddedHeight = height * Mathf.Max(1f, fitPadding);
            float paddedWidth = width * Mathf.Max(1f, fitPadding);
            captureCamera.orthographicSize = paddedHeight * 0.5f;
            captureCamera.aspect = paddedWidth / paddedHeight;
            captureCamera.nearClipPlane = 0.01f;
            captureCamera.farClipPlane = distance + 2f;
        }

        private void CacheOriginalLayers()
        {
            if (_sourceCanvas != null)
                _originalCanvasLayer = _sourceCanvas.gameObject.layer;

            if (displayCanvas != null)
                _originalDisplayCanvasLayer = displayCanvas.gameObject.layer;

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

            if (displayCanvas != null)
                displayCanvas.gameObject.layer = gameObject.layer;

            if (displayImage != null)
                displayImage.gameObject.layer = gameObject.layer;

            if (captureCamera != null)
                captureCamera.cullingMask = 1 << captureLayer;

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

            if (displayCanvas != null)
                displayCanvas.gameObject.layer = _originalDisplayCanvasLayer;

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
