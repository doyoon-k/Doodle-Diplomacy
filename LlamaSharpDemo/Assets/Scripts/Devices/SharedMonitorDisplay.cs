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

        private Material _materialInstance;
        private Texture2D _splitTexture;
        private Color32[] _splitClearPixels;
        private Coroutine _fadeRoutine;

        public MonitorState CurrentState { get; private set; } = MonitorState.Idle;

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

            _materialInstance.SetTexture(texturePropertyName, idleTexture);
            _materialInstance.SetColor("_BaseColor", Color.white);
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
            if (fadeDuration > 0f)
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

            if (_materialInstance != null)
                _materialInstance.SetColor("_BaseColor", Color.white);
        }

        private IEnumerator FadeRoutine(Texture2D nextTex)
        {
            float t = 0f;
            while (t < fadeDuration)
            {
                t += Time.deltaTime;
                float v = 1f - Mathf.Clamp01(t / fadeDuration);
                _materialInstance.SetColor("_BaseColor", new Color(v, v, v));
                yield return null;
            }

            ApplyTexture(nextTex);

            t = 0f;
            while (t < fadeDuration)
            {
                t += Time.deltaTime;
                float v = Mathf.Clamp01(t / fadeDuration);
                _materialInstance.SetColor("_BaseColor", new Color(v, v, v));
                yield return null;
            }

            _materialInstance.SetColor("_BaseColor", Color.white);
            _fadeRoutine = null;
        }

        private void ApplyTexture(Texture2D tex)
        {
            if (_materialInstance == null)
                return;

            _materialInstance.SetTexture(texturePropertyName, tex != null ? tex : idleTexture);
        }

        private void OnDestroy()
        {
            StopFade();
            if (_splitTexture != null)
                Destroy(_splitTexture);
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
