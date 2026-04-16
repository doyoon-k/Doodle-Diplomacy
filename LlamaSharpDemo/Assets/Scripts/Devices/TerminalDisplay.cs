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
        [Header("Display")]
        [SerializeField] private TextMeshProUGUI textMesh;
        [SerializeField] private GameObject screenPanel;

        [Header("Typing")]
        [Tooltip("Per-character typing delay in seconds.")]
        [SerializeField] private float typingSpeed = 0.05f;
        [Tooltip("When enabled, briefly flashes random noise characters while typing.")]
        [SerializeField] private bool useNoise = true;
        [SerializeField] private float noiseDisplayTime = 0.02f;

        [Header("Cursor")]
        [SerializeField] private bool showCursor = true;
        [SerializeField] private float cursorBlinkRate = 0.5f;

        [Header("Scroll")]
        [Tooltip("Allows dragging and mouse-wheel scrolling when text exceeds the panel height.")]
        [SerializeField] private bool enableScroll = true;
        [SerializeField, Min(1f)] private float scrollSensitivity = 24f;
        [SerializeField] private bool autoFollowLatestLine = true;
        [SerializeField, Range(0f, 0.1f)] private float bottomSnapThreshold = 0.01f;

        [Header("Events")]
        public UnityEvent OnTypingComplete = new();

        private Coroutine _typingRoutine;
        private Coroutine _cursorRoutine;
        private string _currentText = string.Empty;
        private bool _isTyping;
        private ScrollRect _scrollRect;
        private RectTransform _panelRect;
        private RectTransform _textRect;
        private LayoutElement _textLayoutElement;
        private bool _scrollInitialized;

        private static readonly char[] NoiseChars =
            "!@#$%^&*<>?/\\|~`0123456789ABCDEFXYZabcxyz".ToCharArray();

        public bool IsTyping() => _isTyping;

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

            if (_scrollRect != null)
                _scrollRect.scrollSensitivity = scrollSensitivity;
        }

        private void OnRectTransformDimensionsChange()
        {
            if (!_scrollInitialized || _textLayoutElement == null || _panelRect == null)
                return;

            _textLayoutElement.minHeight = Mathf.Max(1f, _panelRect.rect.height);
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

            Canvas sourceCanvas = _panelRect.GetComponentInParent<Canvas>();
            if (sourceCanvas != null)
            {
                if (sourceCanvas.GetComponent<GraphicRaycaster>() == null)
                    sourceCanvas.gameObject.AddComponent<GraphicRaycaster>();

                if (sourceCanvas.renderMode == RenderMode.WorldSpace && sourceCanvas.worldCamera == null)
                    sourceCanvas.worldCamera = UnityEngine.Camera.main;
            }

            if (EventSystem.current == null)
                Debug.LogWarning("[TerminalDisplay] EventSystem is missing. Drag scroll will not receive pointer input.", this);

            if (screenPanel.GetComponent<RectMask2D>() == null)
                screenPanel.AddComponent<RectMask2D>();

            if (_textRect.parent != _panelRect)
                _textRect.SetParent(_panelRect, false);

            _textRect.anchorMin = new Vector2(0f, 1f);
            _textRect.anchorMax = new Vector2(1f, 1f);
            _textRect.pivot = new Vector2(0.5f, 1f);
            _textRect.anchoredPosition = Vector2.zero;
            _textRect.offsetMin = new Vector2(0f, _textRect.offsetMin.y);
            _textRect.offsetMax = Vector2.zero;

            ContentSizeFitter sizeFitter = textMesh.GetComponent<ContentSizeFitter>();
            if (sizeFitter == null)
                sizeFitter = textMesh.gameObject.AddComponent<ContentSizeFitter>();

            sizeFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            sizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _textLayoutElement = textMesh.GetComponent<LayoutElement>();
            if (_textLayoutElement == null)
                _textLayoutElement = textMesh.gameObject.AddComponent<LayoutElement>();

            _textLayoutElement.minHeight = Mathf.Max(1f, _panelRect.rect.height);
            _textLayoutElement.flexibleHeight = 0f;

            _scrollRect = screenPanel.GetComponent<ScrollRect>();
            if (_scrollRect == null)
                _scrollRect = screenPanel.AddComponent<ScrollRect>();

            _scrollRect.viewport = _panelRect;
            _scrollRect.content = _textRect;
            _scrollRect.horizontal = false;
            _scrollRect.vertical = true;
            _scrollRect.movementType = ScrollRect.MovementType.Clamped;
            _scrollRect.inertia = false;
            _scrollRect.scrollSensitivity = scrollSensitivity;

            _scrollInitialized = true;
            RefreshScrollLayout(true);
        }

        private bool ShouldFollowBottom()
        {
            if (!autoFollowLatestLine || _scrollRect == null)
                return false;

            return _scrollRect.verticalNormalizedPosition <= bottomSnapThreshold;
        }

        private void RefreshScrollLayout(bool forceToBottom)
        {
            if (!_scrollInitialized || _scrollRect == null)
                return;

            if (_textLayoutElement != null && _panelRect != null)
                _textLayoutElement.minHeight = Mathf.Max(1f, _panelRect.rect.height);

            Canvas.ForceUpdateCanvases();
            if (forceToBottom)
                _scrollRect.verticalNormalizedPosition = 0f;
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
