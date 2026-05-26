using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using DoodleDiplomacy.Core;

namespace DoodleDiplomacy.Devices
{
    [DisallowMultipleComponent]
    public class TerminalDisplay : MonoBehaviour
    {
        private const string TextViewportName = "TerminalTextViewport";

        [Header("Display")]
        [Tooltip("TextMeshPro label that renders terminal output before the CRT composite pass.")]
        [SerializeField] private TextMeshProUGUI textMesh;
        [Tooltip("RectTransform GameObject representing the terminal screen area and scroll viewport.")]
        [SerializeField] private GameObject screenPanel;
        [Tooltip("World-space source canvas containing the terminal text and waveform source UI.")]
        [SerializeField] private Canvas sourceCanvas;
        [Tooltip("Graphic raycaster used by the source canvas for terminal scroll input.")]
        [SerializeField] private GraphicRaycaster sourceGraphicRaycaster;
        [Tooltip("Camera used by world-space UI events. Leave empty only when the canvas does not need an event camera.")]
        [SerializeField] private UnityEngine.Camera eventCamera;

        [Header("Typing")]
        [Tooltip("Per-character typing delay in seconds.")]
        [SerializeField] private float typingSpeed = 0.05f;
        [Tooltip("When enabled, briefly flashes random noise characters while typing.")]
        [SerializeField] private bool useNoise = true;
        [Tooltip("Seconds that a temporary noise character remains visible during typewriter playback.")]
        [SerializeField] private float noiseDisplayTime = 0.02f;

        [Header("Cursor")]
        [Tooltip("Show a blinking underscore cursor at the end of terminal text.")]
        [SerializeField] private bool showCursor = true;
        [Tooltip("Seconds between cursor visibility toggles after typing completes.")]
        [SerializeField] private float cursorBlinkRate = 0.5f;

        [Header("Scroll")]
        [Tooltip("Allows dragging and mouse-wheel scrolling when text exceeds the panel height.")]
        [SerializeField] private bool enableScroll = true;
        [Tooltip("Mouse wheel and drag scroll sensitivity for long terminal text.")]
        [SerializeField, Min(1f)] private float scrollSensitivity = 24f;
        [Tooltip("Keep the scroll view pinned to the newest line while the player is already near the bottom.")]
        [SerializeField] private bool autoFollowLatestLine = true;
        [Tooltip("Normalized scroll distance from the bottom that still counts as pinned to the latest line.")]
        [SerializeField, Range(0f, 0.1f)] private float bottomSnapThreshold = 0.01f;
        [Tooltip("Mask component that clips terminal source content to the screen panel.")]
        [SerializeField] private RectMask2D screenMask;
        [Tooltip("ContentSizeFitter on the terminal text object used to expand the scroll content vertically.")]
        [SerializeField] private ContentSizeFitter textSizeFitter;
        [Tooltip("LayoutElement on the terminal text object used to reserve visible scroll height.")]
        [SerializeField] private LayoutElement textLayoutElement;
        [Tooltip("ScrollRect used to drag or wheel-scroll terminal text.")]
        [SerializeField] private ScrollRect scrollRect;

        [Header("Events")]
        [Tooltip("UnityEvent invoked when terminal typing finishes.")]
        public UnityEvent OnTypingComplete = new();

        private Coroutine _typingRoutine;
        private Coroutine _cursorRoutine;
        private string _currentText = string.Empty;
        private bool _isTyping;
        private RectTransform _panelRect;
        private RectTransform _textRect;
        private RectTransform _textViewportRect;
        private bool _scrollInitialized;
        private float _contentTopInsetNormalized;

        private static readonly char[] NoiseChars =
            "!@#$%^&*<>?/\\|~`0123456789ABCDEFXYZabcxyz".ToCharArray();

        public RectTransform ScreenRectTransform =>
            screenPanel != null ? screenPanel.GetComponent<RectTransform>() : null;

        public bool IsTyping() => _isTyping;

        public void SetContentTopInsetNormalized(float topInsetNormalized)
        {
            _contentTopInsetNormalized = Mathf.Clamp01(topInsetNormalized);

            if (enableScroll)
                EnsureScrollViewConfigured();

            if (!_scrollInitialized)
                return;

            ApplyTextContentLayout();
            RefreshScrollLayout(true);
        }

        private void Awake()
        {
            EnsureScrollViewConfigured();
            Clear();
        }

        private void OnEnable()
        {
            EnsureScrollViewConfigured();
        }

        private void OnValidate()
        {
            scrollSensitivity = Mathf.Max(1f, scrollSensitivity);
            bottomSnapThreshold = Mathf.Clamp(bottomSnapThreshold, 0f, 0.1f);

            if (scrollRect != null)
                scrollRect.scrollSensitivity = scrollSensitivity;
        }

        private void OnRectTransformDimensionsChange()
        {
            if (!_scrollInitialized || _panelRect == null)
                return;

            ApplyTextViewportLayout();
            ApplyTextContentLayout();
            if (textLayoutElement != null)
                textLayoutElement.minHeight = GetTextVisibleHeight();
        }

        public void ShowText(string text)
        {
            ShowText(text, false);
        }

        public void ShowText(string text, bool instant)
        {
            if (_typingRoutine != null)
                StopCoroutine(_typingRoutine);

            if (_cursorRoutine != null)
            {
                StopCoroutine(_cursorRoutine);
                _cursorRoutine = null;
            }

            string resolvedText = text ?? string.Empty;
            if (instant)
            {
                _isTyping = false;
                _typingRoutine = null;
                _currentText = resolvedText;
                if (textMesh != null)
                    ApplyRenderedText(_currentText + (showCursor ? "_" : string.Empty), true);

                if (showCursor)
                    _cursorRoutine = StartCoroutine(CursorBlink());

                OnTypingComplete?.Invoke();
                return;
            }

            _typingRoutine = StartCoroutine(TypingRoutine(resolvedText));
        }

        public void Clear()
        {
            if (_typingRoutine != null)
            {
                StopCoroutine(_typingRoutine);
                _typingRoutine = null;
            }

            if (_cursorRoutine != null)
            {
                StopCoroutine(_cursorRoutine);
                _cursorRoutine = null;
            }

            _isTyping = false;
            _currentText = string.Empty;
            if (textMesh != null)
                ApplyRenderedText(string.Empty, true);
        }

        public void OnGameStateChanged(GameState state)
        {
            switch (state)
            {
                case GameState.InterpreterReady:
                case GameState.WaitingForRound:
                    Clear();
                    break;
            }
        }

        private IEnumerator TypingRoutine(string fullText)
        {
            _isTyping = true;
            _currentText = string.Empty;

            if (showCursor)
                _cursorRoutine = StartCoroutine(CursorBlink());

            for (int i = 0; i < fullText.Length; i++)
            {
                if (useNoise && fullText[i] != '\n' && fullText[i] != ' ' && Random.value < 0.25f)
                {
                    char noise = NoiseChars[Random.Range(0, NoiseChars.Length)];
                    if (textMesh != null)
                        ApplyRenderedText(_currentText + noise + (showCursor ? "_" : string.Empty));

                    yield return new WaitForSeconds(noiseDisplayTime);
                }

                _currentText += fullText[i];
                if (textMesh != null)
                    ApplyRenderedText(_currentText + (showCursor ? "_" : string.Empty));

                float delay = fullText[i] is '\n' or ' ' ? typingSpeed * 0.3f : typingSpeed;
                yield return new WaitForSeconds(delay);
            }

            _isTyping = false;
            _typingRoutine = null;
            OnTypingComplete?.Invoke();
        }

        private IEnumerator CursorBlink()
        {
            bool visible = true;
            while (true)
            {
                yield return new WaitForSeconds(cursorBlinkRate);
                if (!_isTyping && textMesh != null)
                {
                    visible = !visible;
                    ApplyRenderedText(_currentText + (visible ? "_" : string.Empty));
                }
            }
        }

        private void EnsureScrollViewConfigured()
        {
            if (_scrollInitialized || !enableScroll || textMesh == null || screenPanel == null)
                return;

            _panelRect = screenPanel.GetComponent<RectTransform>();
            _textRect = textMesh.rectTransform;
            if (_panelRect == null || _textRect == null)
                return;

            if (!ValidateScrollReferences())
                return;

            EnsureTextViewport();
            if (_textViewportRect == null)
                return;

            if (sourceCanvas.renderMode == RenderMode.WorldSpace && sourceCanvas.worldCamera == null)
                sourceCanvas.worldCamera = eventCamera;

            if (EventSystem.current == null)
                Debug.LogWarning("[TerminalDisplay] EventSystem is missing. Drag scroll will not receive pointer input.", this);

            if (_textRect.parent != _textViewportRect)
                _textRect.SetParent(_textViewportRect, false);

            ApplyTextViewportLayout();
            ApplyTextContentLayout();

            textSizeFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            textSizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            textLayoutElement.minHeight = GetTextVisibleHeight();
            textLayoutElement.flexibleHeight = 0f;

            scrollRect.viewport = _textViewportRect;
            scrollRect.content = _textRect;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.inertia = false;
            scrollRect.scrollSensitivity = scrollSensitivity;

            _scrollInitialized = true;
            RefreshScrollLayout(true);
        }

        private void EnsureTextViewport()
        {
            if (_panelRect == null)
                return;

            if (_textViewportRect == null)
            {
                Transform existingViewport = _panelRect.Find(TextViewportName);
                _textViewportRect = existingViewport as RectTransform;
            }

            if (_textViewportRect == null)
            {
                var viewportObject = new GameObject(
                    TextViewportName,
                    typeof(RectTransform),
                    typeof(RectMask2D));
                _textViewportRect = viewportObject.GetComponent<RectTransform>();
                _textViewportRect.SetParent(_panelRect, false);
            }

            if (_textViewportRect.GetComponent<RectMask2D>() == null)
                _textViewportRect.gameObject.AddComponent<RectMask2D>();

            ApplyTextViewportLayout();
        }

        private float GetContentTopInsetPixels()
        {
            if (_panelRect == null)
                return 0f;

            return Mathf.Clamp01(_contentTopInsetNormalized) * Mathf.Max(0f, _panelRect.rect.height);
        }

        private float GetTextVisibleHeight()
        {
            if (_panelRect == null)
                return 1f;

            return Mathf.Max(1f, _panelRect.rect.height - GetContentTopInsetPixels());
        }

        private void ApplyTextViewportLayout()
        {
            if (_textViewportRect == null)
                return;

            float topInset = GetContentTopInsetPixels();
            _textViewportRect.anchorMin = Vector2.zero;
            _textViewportRect.anchorMax = Vector2.one;
            _textViewportRect.pivot = new Vector2(0.5f, 0.5f);
            _textViewportRect.offsetMin = Vector2.zero;
            _textViewportRect.offsetMax = new Vector2(0f, -topInset);
        }

        private void ApplyTextContentLayout()
        {
            if (_textRect == null)
                return;

            _textRect.anchorMin = new Vector2(0f, 1f);
            _textRect.anchorMax = new Vector2(1f, 1f);
            _textRect.pivot = new Vector2(0.5f, 1f);
            _textRect.anchoredPosition = Vector2.zero;
            _textRect.offsetMin = new Vector2(0f, _textRect.offsetMin.y);
            _textRect.offsetMax = Vector2.zero;
        }

        private bool ShouldFollowBottom()
        {
            if (!autoFollowLatestLine || scrollRect == null)
                return false;

            return scrollRect.verticalNormalizedPosition <= bottomSnapThreshold;
        }

        private void RefreshScrollLayout(bool forceToBottom)
        {
            if (!_scrollInitialized || scrollRect == null)
                return;

            ApplyTextViewportLayout();
            ApplyTextContentLayout();
            if (textLayoutElement != null && _panelRect != null)
                textLayoutElement.minHeight = GetTextVisibleHeight();

            Canvas.ForceUpdateCanvases();
            if (forceToBottom)
                scrollRect.verticalNormalizedPosition = 0f;
        }

        private bool ValidateScrollReferences()
        {
            bool valid = true;
            if (sourceCanvas == null)
            {
                Debug.LogError("[TerminalDisplay] Source canvas must be assigned in the Inspector.", this);
                valid = false;
            }

            if (sourceGraphicRaycaster == null)
            {
                Debug.LogError("[TerminalDisplay] Source graphic raycaster must be assigned in the Inspector.", this);
                valid = false;
            }

            if (sourceCanvas != null && sourceCanvas.renderMode == RenderMode.WorldSpace && eventCamera == null)
            {
                Debug.LogError("[TerminalDisplay] Event camera must be assigned for world-space terminal UI.", this);
                valid = false;
            }

            if (screenMask == null)
            {
                Debug.LogError("[TerminalDisplay] Screen RectMask2D must be assigned in the Inspector.", this);
                valid = false;
            }

            if (textSizeFitter == null)
            {
                Debug.LogError("[TerminalDisplay] Text ContentSizeFitter must be assigned in the Inspector.", this);
                valid = false;
            }

            if (textLayoutElement == null)
            {
                Debug.LogError("[TerminalDisplay] Text LayoutElement must be assigned in the Inspector.", this);
                valid = false;
            }

            if (scrollRect == null)
            {
                Debug.LogError("[TerminalDisplay] ScrollRect must be assigned in the Inspector.", this);
                valid = false;
            }

            return valid;
        }

        private void ApplyRenderedText(string renderedText, bool forceFollowBottom = false)
        {
            if (textMesh == null)
                return;

            if (enableScroll)
                EnsureScrollViewConfigured();

            bool followBottom = forceFollowBottom || ShouldFollowBottom();
            textMesh.text = renderedText;

            if (_scrollInitialized)
                RefreshScrollLayout(followBottom);
        }

        [ContextMenu("Test: ShowDummyText")]
        private void TestShow() =>
            ShowText("[TRANSLATOR v1.0]\n> Decoding...\n> Hello, Ambassador!\n> _");

        [ContextMenu("Test: Clear")]
        private void TestClear() => Clear();
    }
}
