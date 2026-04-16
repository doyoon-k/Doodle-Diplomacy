using System.Collections;
using DoodleDiplomacy.Core;
using UnityEngine;

namespace DoodleDiplomacy.Devices
{
    public enum MonitorState { Idle, Generating, DisplayObjects, DisplaySubmission }

    public class SharedMonitorDisplay : MonoBehaviour
    {
        private const int DefaultSlotWidth = 512;
        private const int DefaultSlotHeight = 512;
        private const string BaseMapPropertyName = "_BaseMap";
        private const string MainTexPropertyName = "_MainTex";
        private const string BaseColorPropertyName = "_BaseColor";
        private const string ColorPropertyName = "_Color";
        private const string PixelResolutionPropertyName = "_PixelResolution";
        private const string PixelateStrengthPropertyName = "_PixelateStrength";

        [Header("Renderer")]
        [SerializeField] private Renderer targetRenderer;
        [Tooltip("URP: _BaseMap / Legacy: _MainTex")]
        [SerializeField] private string texturePropertyName = "_BaseMap";

        [Header("Default Textures")]
        [SerializeField] private Texture2D idleTexture;

        [Header("Fade")]
        [SerializeField] private float fadeDuration = 0.3f;

        [Header("Display")]
        [SerializeField] private bool flipSlotsVertically = true;
        [SerializeField] private bool overridePixelResolution = true;
        [SerializeField] private Vector2Int pixelResolution = new(320, 180);
        [SerializeField, Range(0f, 1f)] private float pixelateStrength = 1f;

        private Material _materialInstance;
        private string _resolvedTexturePropertyName;
        private string _resolvedColorPropertyName;
        private Texture2D _splitTexture;
        private Color32[] _splitClearPixels;
        private Coroutine _fadeRoutine;

        public MonitorState CurrentState { get; private set; } = MonitorState.Idle;
        public bool HasInspectableImage =>
            CurrentState == MonitorState.DisplayObjects ||
            CurrentState == MonitorState.DisplaySubmission;

        private void Awake()
        {
            if (targetRenderer == null)
                targetRenderer = GetComponent<Renderer>();
            if (targetRenderer == null)
            {
                Debug.LogWarning("[SharedMonitorDisplay] No Renderer.", this);
                return;
            }

            var shared = targetRenderer.sharedMaterials;
            _materialInstance = new Material(shared[0]);
            shared[0] = _materialInstance;
            targetRenderer.sharedMaterials = shared;

            if (idleTexture == null)
                idleTexture = MakeSolidTexture(Color.black, 2, 2);

            _resolvedTexturePropertyName = ResolveTexturePropertyName(_materialInstance, texturePropertyName);
            _resolvedColorPropertyName = ResolveColorPropertyName(_materialInstance);

            if (string.IsNullOrEmpty(_resolvedTexturePropertyName))
            {
                Debug.LogWarning(
                    "[SharedMonitorDisplay] Could not find texture property. " +
                    "Tried configured name, _BaseMap, and _MainTex. Falling back to material.mainTexture.",
                    this);
            }

            ApplyTexture(idleTexture);
            SetTintColor(Color.white);
            ApplyPixelSettings();
        }

        public void SetIdle()
        {
            CurrentState = MonitorState.Idle;
            StartFade(idleTexture);
        }

        public void ShowGenerating(Texture2D progressTexture)
        {
            ShowGenerating(progressTexture, null);
        }

        public void ShowGenerating(Texture2D leftTexture, Texture2D rightTexture)
        {
            CurrentState = MonitorState.Generating;
            StopFade();
            UpdateSplitTexture(leftTexture, rightTexture);
            ApplyTexture(_splitTexture);
        }

        public void ShowObjects(Texture2D objA, Texture2D objB)
        {
            CurrentState = MonitorState.DisplayObjects;
            UpdateSplitTexture(objA, objB);
            StartFade(_splitTexture);
        }

        public void ShowSubmission(Texture2D submission)
        {
            CurrentState = MonitorState.DisplaySubmission;
            StartFade(submission != null ? submission : idleTexture);
        }

        public void OnGameStateChanged(GameState state)
        {
            switch (state)
            {
                case GameState.Presenting:
                    ShowGenerating(null, null);
                    break;
                case GameState.Submitting:
                    ShowSubmission(null);
                    break;
                case GameState.WaitingForRound:
                    SetIdle();
                    break;
            }
        }

        private void StartFade(Texture2D nextTex)
        {
            StopFade();
            if (fadeDuration > 0f && !string.IsNullOrEmpty(_resolvedColorPropertyName))
                _fadeRoutine = StartCoroutine(FadeRoutine(nextTex));
            else
                ApplyTexture(nextTex);
        }

        private void StopFade()
        {
            if (_fadeRoutine != null)
            {
                StopCoroutine(_fadeRoutine);
                _fadeRoutine = null;
            }

            SetTintColor(Color.white);
        }

        private IEnumerator FadeRoutine(Texture2D nextTex)
        {
            float t = 0f;
            while (t < fadeDuration)
            {
                t += Time.deltaTime;
                float v = 1f - Mathf.Clamp01(t / fadeDuration);
                SetTintColor(new Color(v, v, v));
                yield return null;
            }

            ApplyTexture(nextTex);

            t = 0f;
            while (t < fadeDuration)
            {
                t += Time.deltaTime;
                float v = Mathf.Clamp01(t / fadeDuration);
                SetTintColor(new Color(v, v, v));
                yield return null;
            }

            SetTintColor(Color.white);
            _fadeRoutine = null;
        }

        private void ApplyTexture(Texture2D tex)
        {
            if (_materialInstance == null)
                return;

            Texture targetTexture = tex != null ? tex : idleTexture;
            if (string.IsNullOrEmpty(_resolvedTexturePropertyName))
            {
                _materialInstance.mainTexture = targetTexture;
                return;
            }

            _materialInstance.SetTexture(_resolvedTexturePropertyName, targetTexture);
        }

        private void SetTintColor(Color color)
        {
            if (_materialInstance == null || string.IsNullOrEmpty(_resolvedColorPropertyName))
                return;

            _materialInstance.SetColor(_resolvedColorPropertyName, color);
        }

        private static string ResolveTexturePropertyName(Material material, string preferredPropertyName)
        {
            if (material == null)
                return string.Empty;

            if (!string.IsNullOrEmpty(preferredPropertyName) && material.HasProperty(preferredPropertyName))
                return preferredPropertyName;

            if (material.HasProperty(BaseMapPropertyName))
                return BaseMapPropertyName;

            if (material.HasProperty(MainTexPropertyName))
                return MainTexPropertyName;

            return string.Empty;
        }

        private static string ResolveColorPropertyName(Material material)
        {
            if (material == null)
                return string.Empty;

            if (material.HasProperty(BaseColorPropertyName))
                return BaseColorPropertyName;

            if (material.HasProperty(ColorPropertyName))
                return ColorPropertyName;

            return string.Empty;
        }

        private void ApplyPixelSettings()
        {
            if (_materialInstance == null)
                return;

            if (_materialInstance.HasProperty(PixelateStrengthPropertyName))
                _materialInstance.SetFloat(PixelateStrengthPropertyName, Mathf.Clamp01(pixelateStrength));

            if (!overridePixelResolution || !_materialInstance.HasProperty(PixelResolutionPropertyName))
                return;

            int width = Mathf.Max(1, pixelResolution.x);
            int height = Mathf.Max(1, pixelResolution.y);
            _materialInstance.SetVector(PixelResolutionPropertyName, new Vector4(width, height, 0f, 0f));
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

        private void OnDestroy()
        {
            StopFade();
            if (_splitTexture != null)
                Destroy(_splitTexture);
        }

        private void OnValidate()
        {
            pixelResolution.x = Mathf.Max(1, pixelResolution.x);
            pixelResolution.y = Mathf.Max(1, pixelResolution.y);
            pixelateStrength = Mathf.Clamp01(pixelateStrength);

            if (_materialInstance != null)
                ApplyPixelSettings();
        }

        private void UpdateSplitTexture(Texture2D leftTexture, Texture2D rightTexture)
        {
            int slotWidth = Mathf.Max(
                DefaultSlotWidth,
                leftTexture != null ? leftTexture.width : 0,
                rightTexture != null ? rightTexture.width : 0);
            int slotHeight = Mathf.Max(
                DefaultSlotHeight,
                leftTexture != null ? leftTexture.height : 0,
                rightTexture != null ? rightTexture.height : 0);

            EnsureSplitTexture(slotWidth, slotHeight);

            _splitTexture.SetPixels32(_splitClearPixels);
            BlitSlot(leftTexture, 0, slotWidth, slotHeight);
            BlitSlot(rightTexture, slotWidth, slotWidth, slotHeight);
            _splitTexture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
        }

        private void EnsureSplitTexture(int slotWidth, int slotHeight)
        {
            int targetWidth = Mathf.Max(DefaultSlotWidth, slotWidth) * 2;
            int targetHeight = Mathf.Max(DefaultSlotHeight, slotHeight);

            if (_splitTexture != null &&
                _splitTexture.width == targetWidth &&
                _splitTexture.height == targetHeight)
            {
                return;
            }

            if (_splitTexture != null)
                Destroy(_splitTexture);

            _splitTexture = new Texture2D(targetWidth, targetHeight, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                name = "SharedMonitorSplitTexture"
            };

            _splitClearPixels = new Color32[targetWidth * targetHeight];
            for (int i = 0; i < _splitClearPixels.Length; i++)
                _splitClearPixels[i] = Color.black;
        }

        private void BlitSlot(Texture2D source, int xOffset, int slotWidth, int slotHeight)
        {
            if (source == null || _splitTexture == null)
                return;

            int copyWidth = Mathf.Min(slotWidth, source.width);
            int copyHeight = Mathf.Min(slotHeight, source.height);
            int targetX = xOffset + Mathf.Max(0, (slotWidth - copyWidth) / 2);
            int targetY = Mathf.Max(0, (slotHeight - copyHeight) / 2);
            Color[] sourcePixels = source.GetPixels(0, 0, copyWidth, copyHeight);

            if (flipSlotsVertically)
                FlipPixelsVertically(sourcePixels, copyWidth, copyHeight);

            _splitTexture.SetPixels(
                targetX,
                targetY,
                copyWidth,
                copyHeight,
                sourcePixels);
        }

        private static void FlipPixelsVertically(Color[] pixels, int width, int height)
        {
            if (pixels == null || width <= 0 || height <= 1)
                return;

            int halfRows = height / 2;
            for (int y = 0; y < halfRows; y++)
            {
                int oppositeY = height - 1 - y;
                int topOffset = y * width;
                int bottomOffset = oppositeY * width;
                for (int x = 0; x < width; x++)
                {
                    (pixels[topOffset + x], pixels[bottomOffset + x]) =
                        (pixels[bottomOffset + x], pixels[topOffset + x]);
                }
            }
        }

        private static Texture2D MakeSolidTexture(Color color, int width, int height)
        {
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            var pixels = new Color[width * height];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = color;

            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }

        [ContextMenu("Test: SetIdle")]
        private void TestIdle() => SetIdle();

        [ContextMenu("Test: ShowGenerating (null)")]
        private void TestGenerating() => ShowGenerating(null, null);

        [ContextMenu("Test: ShowSubmission (null)")]
        private void TestSubmission() => ShowSubmission(null);
    }
}
