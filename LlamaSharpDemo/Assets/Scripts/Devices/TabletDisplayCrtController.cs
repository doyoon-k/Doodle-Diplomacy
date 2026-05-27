using UnityEngine;

namespace DoodleDiplomacy.Devices
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-320)]
    public sealed class TabletDisplayCrtController : MonoBehaviour
    {
        private const string BaseMapPropertyName = "_BaseMap";
        private const string MainTexPropertyName = "_MainTex";
        private const string ScanlineDensityPropertyName = "_ScanlineDensity";
        private const string ScanlineStrengthPropertyName = "_ScanlineStrength";
        private const string NoiseStrengthPropertyName = "_NoiseStrength";
        private const string VignetteStrengthPropertyName = "_VignetteStrength";
        private const string CurvaturePropertyName = "_Curvature";
        private const string ChromaticAberrationPropertyName = "_ChromaticAberration";
        private const string PixelResolutionPropertyName = "_PixelResolution";
        private const string PixelateStrengthPropertyName = "_PixelateStrength";
        private const string BaseColorPropertyName = "_BaseColor";
        private const string ColorPropertyName = "_Color";

        [Header("Targets")]
        [Tooltip("Drawing board whose runtime material is linked to the tablet CRT paint display.")]
        [SerializeField] private DrawingBoardController drawingBoard;
        [Tooltip("Renderer that displays the main paint area CRT material.")]
        [SerializeField] private Renderer paintAreaRenderer;
        [Tooltip("Renderer that displays the color spectrum bar CRT material.")]
        [SerializeField] private Renderer spectrumBarRenderer;

        [Header("Source")]
        [Tooltip("Material template containing the CRT shader properties copied to runtime material instances.")]
        [SerializeField] private Material crtMaterialTemplate;
        [Tooltip("Apply the CRT material instance and settings to the paint area renderer.")]
        [SerializeField] private bool applyToPaintArea = true;
        [Tooltip("Apply the CRT material instance and settings to the spectrum bar renderer.")]
        [SerializeField] private bool applyToSpectrumBar = true;

        [Header("Paint CRT")]
        [Tooltip("Virtual pixel resolution used by the paint area CRT shader.")]
        [SerializeField] private Vector2Int paintPixelResolution = new(360, 360);
        [Tooltip("Strength of pixelation applied to the paint area display.")]
        [SerializeField] [Range(0f, 1f)] private float paintPixelateStrength = 0.35f;
        [Tooltip("Scanline density applied to the paint area display.")]
        [SerializeField] [Range(100f, 1200f)] private float paintScanlineDensity = 600f;
        [Tooltip("Visible intensity of scanlines on the paint area display.")]
        [SerializeField] [Range(0f, 1f)] private float paintScanlineStrength = 0.10f;
        [Tooltip("Procedural noise intensity on the paint area display.")]
        [SerializeField] [Range(0f, 0.3f)] private float paintNoiseStrength = 0.012f;
        [Tooltip("Darkening toward the paint display edges.")]
        [SerializeField] [Range(0f, 1f)] private float paintVignetteStrength = 0.06f;
        [Tooltip("Screen curvature amount on the paint area display.")]
        [SerializeField] [Range(0f, 0.2f)] private float paintCurvature = 0.018f;
        [Tooltip("RGB channel offset applied to the paint area display.")]
        [SerializeField] [Range(0f, 0.01f)] private float paintChromaticAberration = 0.0012f;
        [Tooltip("Color tint multiplied into the paint area CRT material.")]
        [SerializeField] private Color paintTint = Color.white;

        [Header("Spectrum CRT")]
        [Tooltip("Virtual pixel resolution used by the spectrum bar CRT shader.")]
        [SerializeField] private Vector2Int spectrumPixelResolution = new(320, 24);
        [Tooltip("Strength of pixelation applied to the spectrum bar.")]
        [SerializeField] [Range(0f, 1f)] private float spectrumPixelateStrength = 0.75f;
        [Tooltip("Scanline density applied to the spectrum bar.")]
        [SerializeField] [Range(100f, 1200f)] private float spectrumScanlineDensity = 800f;
        [Tooltip("Visible intensity of scanlines on the spectrum bar.")]
        [SerializeField] [Range(0f, 1f)] private float spectrumScanlineStrength = 0.08f;
        [Tooltip("Procedural noise intensity on the spectrum bar.")]
        [SerializeField] [Range(0f, 0.3f)] private float spectrumNoiseStrength = 0.01f;
        [Tooltip("Darkening toward the spectrum bar edges.")]
        [SerializeField] [Range(0f, 1f)] private float spectrumVignetteStrength = 0.04f;
        [Tooltip("Screen curvature amount on the spectrum bar.")]
        [SerializeField] [Range(0f, 0.2f)] private float spectrumCurvature = 0.012f;
        [Tooltip("RGB channel offset applied to the spectrum bar.")]
        [SerializeField] [Range(0f, 0.01f)] private float spectrumChromaticAberration = 0.0007f;
        [Tooltip("Color tint multiplied into the spectrum bar CRT material.")]
        [SerializeField] private Color spectrumTint = Color.white;

        private Material _paintMaterialInstance;
        private Material _spectrumMaterialInstance;
        private bool _paintTemplateLinked;
        private bool _ready;

        private void Awake()
        {
            EnsureNow();
        }

        private void OnEnable()
        {
            EnsureNow();
        }

        private void OnValidate()
        {
            if (!Application.isPlaying || !isActiveAndEnabled)
            {
                return;
            }

            EnsureRuntimeMaterials();
            ApplyRuntimeSettings();
        }

        public void EnsureNow()
        {
            ValidateInspectorReferences();
            EnsureRuntimeMaterials();
            ApplyRuntimeSettings();
        }

        private void OnDestroy()
        {
            DestroyRuntimeMaterial(ref _paintMaterialInstance);
            DestroyRuntimeMaterial(ref _spectrumMaterialInstance);
        }

        private bool ValidateInspectorReferences()
        {
            bool isValid = true;
            if (drawingBoard == null)
            {
                Debug.LogError("[TabletDisplayCrtController] Drawing board must be assigned in the Inspector.", this);
                isValid = false;
            }

            if (applyToPaintArea && paintAreaRenderer == null)
            {
                Debug.LogError("[TabletDisplayCrtController] Paint area renderer must be assigned in the Inspector.", this);
                isValid = false;
            }

            if (applyToSpectrumBar && spectrumBarRenderer == null)
            {
                Debug.LogError("[TabletDisplayCrtController] Spectrum bar renderer must be assigned in the Inspector.", this);
                isValid = false;
            }

            if (crtMaterialTemplate == null)
            {
                Debug.LogError("[TabletDisplayCrtController] CRT material template must be assigned in the Inspector.", this);
                isValid = false;
            }

            return isValid;
        }

        private void EnsureRuntimeMaterials()
        {
            if (!ValidateInspectorReferences())
            {
                _ready = false;
                return;
            }

            if (applyToPaintArea)
            {
                EnsureRuntimeMaterial(ref _paintMaterialInstance, "TabletPaintCRT");
            }

            if (applyToSpectrumBar)
            {
                EnsureRuntimeMaterial(ref _spectrumMaterialInstance, "TabletSpectrumCRT");
            }

            _ready = true;
        }

        private void ApplyRuntimeSettings()
        {
            if (!_ready)
            {
                return;
            }

            if (applyToPaintArea && drawingBoard != null && _paintMaterialInstance != null)
            {
                ApplyCrtParameters(
                    _paintMaterialInstance,
                    paintTint,
                    paintPixelResolution,
                    paintPixelateStrength,
                    paintScanlineDensity,
                    paintScanlineStrength,
                    paintNoiseStrength,
                    paintVignetteStrength,
                    paintCurvature,
                    paintChromaticAberration);

                if (!_paintTemplateLinked)
                {
                    drawingBoard.SetBoardMaterialTemplate(_paintMaterialInstance, reinitializeIfReady: true);
                    _paintTemplateLinked = true;
                }

                Material runtimeBoardMaterial = drawingBoard.RuntimeBoardMaterial;
                if (runtimeBoardMaterial != null)
                {
                    ApplyCrtParameters(
                        runtimeBoardMaterial,
                        paintTint,
                        paintPixelResolution,
                        paintPixelateStrength,
                        paintScanlineDensity,
                        paintScanlineStrength,
                        paintNoiseStrength,
                        paintVignetteStrength,
                        paintCurvature,
                        paintChromaticAberration);
                }
            }

            if (applyToSpectrumBar && spectrumBarRenderer != null && _spectrumMaterialInstance != null)
            {
                CopyPrimaryTextureFromRenderer(spectrumBarRenderer, _spectrumMaterialInstance);
                ApplyCrtParameters(
                    _spectrumMaterialInstance,
                    spectrumTint,
                    spectrumPixelResolution,
                    spectrumPixelateStrength,
                    spectrumScanlineDensity,
                    spectrumScanlineStrength,
                    spectrumNoiseStrength,
                    spectrumVignetteStrength,
                    spectrumCurvature,
                    spectrumChromaticAberration);
                spectrumBarRenderer.sharedMaterial = _spectrumMaterialInstance;
            }
        }

        private void EnsureRuntimeMaterial(ref Material targetMaterial, string materialName)
        {
            if (targetMaterial != null && targetMaterial.shader == crtMaterialTemplate.shader)
            {
                return;
            }

            DestroyRuntimeMaterial(ref targetMaterial);
            targetMaterial = new Material(crtMaterialTemplate);
            targetMaterial.name = $"{materialName}_Runtime";
            targetMaterial.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
        }

        private static void CopyPrimaryTextureFromRenderer(Renderer sourceRenderer, Material destinationMaterial)
        {
            if (sourceRenderer == null || destinationMaterial == null)
            {
                return;
            }

            Material sourceMaterial = sourceRenderer.sharedMaterial;
            if (sourceMaterial == null)
            {
                return;
            }

            Texture sourceTexture = null;
            Vector2 sourceScale = Vector2.one;
            Vector2 sourceOffset = Vector2.zero;
            string sourceTextureProperty = null;

            if (sourceMaterial.HasProperty(BaseMapPropertyName))
            {
                sourceTextureProperty = BaseMapPropertyName;
            }
            else if (sourceMaterial.HasProperty(MainTexPropertyName))
            {
                sourceTextureProperty = MainTexPropertyName;
            }

            if (!string.IsNullOrEmpty(sourceTextureProperty))
            {
                sourceTexture = sourceMaterial.GetTexture(sourceTextureProperty);
                sourceScale = sourceMaterial.GetTextureScale(sourceTextureProperty);
                sourceOffset = sourceMaterial.GetTextureOffset(sourceTextureProperty);
            }
            else
            {
                sourceTexture = sourceMaterial.mainTexture;
                sourceScale = sourceMaterial.mainTextureScale;
                sourceOffset = sourceMaterial.mainTextureOffset;
            }

            if (sourceTexture == null)
            {
                return;
            }

            if (destinationMaterial.HasProperty(BaseMapPropertyName))
            {
                destinationMaterial.SetTexture(BaseMapPropertyName, sourceTexture);
                destinationMaterial.SetTextureScale(BaseMapPropertyName, sourceScale);
                destinationMaterial.SetTextureOffset(BaseMapPropertyName, sourceOffset);
            }

            if (destinationMaterial.HasProperty(MainTexPropertyName))
            {
                destinationMaterial.SetTexture(MainTexPropertyName, sourceTexture);
                destinationMaterial.SetTextureScale(MainTexPropertyName, sourceScale);
                destinationMaterial.SetTextureOffset(MainTexPropertyName, sourceOffset);
            }
        }

        private static void ApplyCrtParameters(
            Material material,
            Color tint,
            Vector2Int pixelResolution,
            float pixelateStrength,
            float scanlineDensity,
            float scanlineStrength,
            float noiseStrength,
            float vignetteStrength,
            float curvature,
            float chromaticAberration)
        {
            if (material == null)
            {
                return;
            }

            Color clampedTint = new(
                Mathf.Clamp01(tint.r),
                Mathf.Clamp01(tint.g),
                Mathf.Clamp01(tint.b),
                1f);

            int resolutionX = Mathf.Max(1, pixelResolution.x);
            int resolutionY = Mathf.Max(1, pixelResolution.y);

            if (material.HasProperty(BaseColorPropertyName))
            {
                material.SetColor(BaseColorPropertyName, clampedTint);
            }

            if (material.HasProperty(ColorPropertyName))
            {
                material.SetColor(ColorPropertyName, clampedTint);
            }

            if (material.HasProperty(PixelResolutionPropertyName))
            {
                material.SetVector(PixelResolutionPropertyName, new Vector4(resolutionX, resolutionY, 0f, 0f));
            }

            if (material.HasProperty(PixelateStrengthPropertyName))
            {
                material.SetFloat(PixelateStrengthPropertyName, Mathf.Clamp01(pixelateStrength));
            }

            if (material.HasProperty(ScanlineDensityPropertyName))
            {
                material.SetFloat(ScanlineDensityPropertyName, Mathf.Clamp(scanlineDensity, 100f, 1200f));
            }

            if (material.HasProperty(ScanlineStrengthPropertyName))
            {
                material.SetFloat(ScanlineStrengthPropertyName, Mathf.Clamp01(scanlineStrength));
            }

            if (material.HasProperty(NoiseStrengthPropertyName))
            {
                material.SetFloat(NoiseStrengthPropertyName, Mathf.Clamp(noiseStrength, 0f, 0.3f));
            }

            if (material.HasProperty(VignetteStrengthPropertyName))
            {
                material.SetFloat(VignetteStrengthPropertyName, Mathf.Clamp01(vignetteStrength));
            }

            if (material.HasProperty(CurvaturePropertyName))
            {
                material.SetFloat(CurvaturePropertyName, Mathf.Clamp(curvature, 0f, 0.2f));
            }

            if (material.HasProperty(ChromaticAberrationPropertyName))
            {
                material.SetFloat(ChromaticAberrationPropertyName, Mathf.Clamp(chromaticAberration, 0f, 0.01f));
            }
        }

        private static void DestroyRuntimeMaterial(ref Material material)
        {
            if (material == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(material);
            }
            else
            {
                DestroyImmediate(material);
            }

            material = null;
        }
    }
}
