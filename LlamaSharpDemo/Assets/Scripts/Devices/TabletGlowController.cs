using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace DoodleDiplomacy.Devices
{
    [DisallowMultipleComponent]
    public sealed class TabletGlowController : MonoBehaviour
    {
        private const string ShaderName = "Custom/TabletSoftGlow";
        private const string TabletRootPath = "Interactables/tablet";
        private const string PaintAreaPath = "Interactables/tablet/PaintAreaProxy";
        private const string SpectrumBarPath = "Interactables/tablet/PhysicalControls/SpectrumBar";
        private const string PaintGlowName = "PaintAreaGlow";
        private const string SpectrumGlowName = "SpectrumBarGlow";

        [Header("Glow Mesh Scale")]
        [SerializeField] private Vector3 paintGlowScale = new(1.06f, 2.10f, 1.06f);
        [SerializeField] private Vector3 spectrumGlowScale = new(1.10f, 2.60f, 1.26f);

        [Header("Paint Glow")]
        [SerializeField] private Color paintGlowColor = new(0.72f, 0.95f, 1.00f, 1f);
        [SerializeField] [Range(0f, 3f)] private float paintBaseIntensity = 0.23f;
        [SerializeField] [Range(0f, 2f)] private float paintPulseAmplitude = 0.06f;
        [SerializeField] [Range(0.5f, 4f)] private float paintFalloff = 1.55f;
        [SerializeField] [Range(0f, 2f)] private float paintEdgeBoost = 0.20f;

        [Header("Spectrum Glow")]
        [SerializeField] private Color spectrumGlowColor = new(0.62f, 0.90f, 1.00f, 1f);
        [SerializeField] [Range(0f, 3f)] private float spectrumBaseIntensity = 0.18f;
        [SerializeField] [Range(0f, 2f)] private float spectrumPulseAmplitude = 0.05f;
        [SerializeField] [Range(0.5f, 4f)] private float spectrumFalloff = 1.85f;
        [SerializeField] [Range(0f, 2f)] private float spectrumEdgeBoost = 0.28f;

        [Header("Pulse")]
        [SerializeField] private bool enablePulse = true;
        [SerializeField] [Range(0f, 8f)] private float pulseSpeed = 1.65f;

        private Renderer _paintGlowRenderer;
        private Renderer _spectrumGlowRenderer;
        private Material _paintGlowMaterial;
        private Material _spectrumGlowMaterial;
        private Shader _glowShader;
        private bool _ready;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            EnsureController();
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
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

            if (tabletRoot.GetComponent<TabletGlowController>() == null)
            {
                tabletRoot.AddComponent<TabletGlowController>();
            }
        }

        private void Awake()
        {
            EnsureGlow();
        }

        public void EnsureNow()
        {
            EnsureGlow();
        }

        private void OnEnable()
        {
            EnsureGlow();
        }

        private void OnValidate()
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            EnsureGlow();
            ApplyGlowSettings();
        }

        private void Update()
        {
            if (!_ready)
            {
                return;
            }

            float pulse = 0f;
            if (enablePulse)
            {
                pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * Mathf.Max(0f, pulseSpeed));
            }

            SetGlowIntensity(_paintGlowMaterial, paintBaseIntensity + pulse * paintPulseAmplitude);
            SetGlowIntensity(_spectrumGlowMaterial, spectrumBaseIntensity + pulse * spectrumPulseAmplitude);
        }

        private void OnDestroy()
        {
            DestroyMaterial(ref _paintGlowMaterial);
            DestroyMaterial(ref _spectrumGlowMaterial);
        }

        private void EnsureGlow()
        {
            _glowShader = Shader.Find(ShaderName);
            if (_glowShader == null)
            {
                Debug.LogWarning($"[TabletGlowController] Missing shader: {ShaderName}", this);
                return;
            }

            _paintGlowRenderer = EnsureGlowChild(PaintAreaPath, PaintGlowName, paintGlowScale);
            _spectrumGlowRenderer = EnsureGlowChild(SpectrumBarPath, SpectrumGlowName, spectrumGlowScale);
            if (_paintGlowRenderer == null || _spectrumGlowRenderer == null)
            {
                return;
            }

            EnsureGlowMaterial(ref _paintGlowMaterial);
            EnsureGlowMaterial(ref _spectrumGlowMaterial);
            ApplyGlowSettings();

            _paintGlowRenderer.sharedMaterial = _paintGlowMaterial;
            _spectrumGlowRenderer.sharedMaterial = _spectrumGlowMaterial;
            _ready = true;
        }

        private void EnsureGlowMaterial(ref Material mat)
        {
            if (mat != null && mat.shader == _glowShader)
            {
                return;
            }

            DestroyMaterial(ref mat);

            mat = new Material(_glowShader)
            {
                hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild
            };
        }

        private void ApplyGlowSettings()
        {
            ApplyMaterialParameters(_paintGlowMaterial, paintGlowColor, paintBaseIntensity, paintFalloff, paintEdgeBoost);
            ApplyMaterialParameters(_spectrumGlowMaterial, spectrumGlowColor, spectrumBaseIntensity, spectrumFalloff, spectrumEdgeBoost);
        }

        private static void ApplyMaterialParameters(Material mat, Color color, float intensity, float falloff, float edgeBoost)
        {
            if (mat == null)
            {
                return;
            }

            mat.SetColor("_GlowColor", color);
            mat.SetFloat("_Intensity", intensity);
            mat.SetFloat("_Falloff", falloff);
            mat.SetFloat("_EdgeBoost", edgeBoost);
        }

        private static void DestroyMaterial(ref Material mat)
        {
            if (mat == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(mat);
            }
            else
            {
                DestroyImmediate(mat);
            }

            mat = null;
        }

        private static void SetGlowIntensity(Material mat, float intensity)
        {
            if (mat == null)
            {
                return;
            }

            mat.SetFloat("_Intensity", intensity);
        }

        private static Renderer EnsureGlowChild(string parentPath, string glowName, Vector3 localScale)
        {
            GameObject parent = GameObject.Find(parentPath);
            if (parent == null)
            {
                return null;
            }

            Transform child = parent.transform.Find(glowName);
            GameObject glowObject;
            if (child == null)
            {
                glowObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
                glowObject.name = glowName;
                glowObject.transform.SetParent(parent.transform, false);
            }
            else
            {
                glowObject = child.gameObject;
            }

            Collider col = glowObject.GetComponent<Collider>();
            if (col != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(col);
                }
            }

            glowObject.layer = parent.layer;
            glowObject.transform.localPosition = Vector3.zero;
            glowObject.transform.localRotation = Quaternion.identity;
            glowObject.transform.localScale = localScale;

            Renderer renderer = glowObject.GetComponent<Renderer>();
            if (renderer == null)
            {
                renderer = glowObject.AddComponent<MeshRenderer>();
            }

            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

            return renderer;
        }
    }
}
