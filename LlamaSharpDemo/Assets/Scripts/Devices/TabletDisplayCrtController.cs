using UnityEngine;
using UnityEngine.SceneManagement;

namespace DoodleDiplomacy.Devices
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-320)]
    public sealed class TabletDisplayCrtController : MonoBehaviour
    {
        private const string CtrShaderName = "DoodleDiplomacy/SharedMonitorCRT";
        private const string TabletRootPath = "Interactables/tablet";
        private const string PaintAreaPath = "Interactables/tablet/PaintAreaProxy";
        private const string SpectrumBarPath = "Interactables/tablet/PhysicalControls/SpectrumBar";
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
        [SerializeField] private DrawingBoardController drawingBoard;
        [SerializeField] private Renderer paintAreaRenderer;
        [SerializeField] private Renderer spectrumBarRenderer;

        [Header("Source")]
        [SerializeField] private Material crtMaterialTemplate;
        [SerializeField] private bool applyToPaintArea = true;
        [SerializeField] private bool applyToSpectrumBar = true;

        [Header("Paint CRT")]
        [SerializeField] private Vector2Int paintPixelResolution = new(360, 360);
        [SerializeField] [Range(0f, 1f)] private float paintPixelateStrength = 0.35f;
        [SerializeField] [Range(100f, 1200f)] private float paintScanlineDensity = 600f;
        [SerializeField] [Range(0f, 1f)] private float paintScanlineStrength = 0.10f;
        [SerializeField] [Range(0f, 0.3f)] private float paintNoiseStrength = 0.012f;
        [SerializeField] [Range(0f, 1f)] private float paintVignetteStrength = 0.06f;
        [SerializeField] [Range(0f, 0.2f)] private float paintCurvature = 0.018f;
        [SerializeField] [Range(0f, 0.01f)] private float paintChromaticAberration = 0.0012f;
        [SerializeField] private Color paintTint = Color.white;

        [Header("Spectrum CRT")]
        [SerializeField] private Vector2Int spectrumPixelResolution = new(320, 24);
        [SerializeField] [Range(0f, 1f)] private float spectrumPixelateStrength = 0.75f;
        [SerializeField] [Range(100f, 1200f)] private float spectrumScanlineDensity = 800f;
        [SerializeField] [Range(0f, 1f)] private float spectrumScanlineStrength = 0.08f;
        [SerializeField] [Range(0f, 0.3f)] private float spectrumNoiseStrength = 0.01f;
        [SerializeField] [Range(0f, 1f)] private float spectrumVignetteStrength = 0.04f;
        [SerializeField] [Range(0f, 0.2f)] private float spectrumCurvature = 0.012f;
        [SerializeField] [Range(0f, 0.01f)] private float spectrumChromaticAberration = 0.0007f;
        [SerializeField] private Color spectrumTint = Color.white;

        private Material _paintMaterialInstance;
        private Material _spectrumMaterialInstance;
        private bool _paintTemplateLinked;
        private bool _ready;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            EnsureController();
        }

        private static void OnSceneLoaded(Scene _, LoadSceneMode __)
        {
            EnsureController();
        }

        private static void EnsureController()
        {
            GameObject tabletRoot = GameObject.Find(TabletRootPath);
            if (tabletRoot == null)
            {
                return;
            }

            if (tabletRoot.GetComponent<TabletDisplayCrtController>() == null)
            {
                tabletRoot.AddComponent<TabletDisplayCrtController>();
            }
        }

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
            AutoAssignReferences();
#if UNITY_EDITOR
            if (crtMaterialTemplate == null)
            {
                crtMaterialTemplate = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/SharedMonitorCRT.mat");
            }
#endif

            if (!Application.isPlaying || !isActiveAndEnabled)
            {
                return;
            }

            EnsureRuntimeMaterials();
            ApplyRuntimeSettings();
        }

        public void EnsureNow()
        {
            AutoAssignReferences();
            EnsureRuntimeMaterials();
            ApplyRuntimeSettings();
        }

        private void OnDestroy()
        {
            DestroyRuntimeMaterial(ref _paintMaterialInstance);
            DestroyRuntimeMaterial(ref _spectrumMaterialInstance);
        }

        private void AutoAssignReferences()
        {
            drawingBoard ??= GetComponent<DrawingBoardController>();

            if (paintAreaRenderer == null)
            {
                GameObject paintAreaObject = GameObject.Find(PaintAreaPath);
                if (paintAreaObject != null)
                {
                    paintAreaRenderer = paintAreaObject.GetComponent<Renderer>();
                }
            }

            if (spectrumBarRenderer == null)
            {
                GameObject spectrumBarObject = GameObject.Find(SpectrumBarPath);
                if (spectrumBarObject != null)
                {
                    spectrumBarRenderer = spectrumBarObject.GetComponent<Renderer>();
                }
            }
        }

        private void EnsureRuntimeMaterials()
        {
            Shader crtShader = Shader.Find(CtrShaderName);
            if (crtShader == null)
            {
                Debug.LogWarning($"[TabletDisplayCrtController] Missing shader: {CtrShaderName}", this);
                return;
            }

            if (applyToPaintArea)
            {
                EnsureRuntimeMaterial(ref _paintMaterialInstance, crtShader, "TabletPaintCRT");
            }

            if (applyToSpectrumBar)
            {
                EnsureRuntimeMaterial(ref _spectrumMaterialInstance, crtShader, "TabletSpectrumCRT");
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

        private void EnsureRuntimeMaterial(ref Material targetMaterial, Shader crtShader, string materialName)
        {
            if (targetMaterial != null && targetMaterial.shader == crtShader)
            {
                return;
            }

            DestroyRuntimeMaterial(ref targetMaterial);
            targetMaterial = crtMaterialTemplate != null
                ? new Material(crtMaterialTemplate)
                : new Material(crtShader);
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
