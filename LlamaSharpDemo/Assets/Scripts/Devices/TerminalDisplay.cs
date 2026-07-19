using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using DoodleDiplomacy.Core;
using DoodleDiplomacy.Localization;

namespace DoodleDiplomacy.Devices
{
    [DisallowMultipleComponent]
    public class TerminalDisplay : MonoBehaviour
    {
        private const string TextViewportName = "TerminalTextViewport";
        public const string CursorMarker = "\uE000";

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

        [Header("Refresh")]
        [Tooltip("When enabled, non-instant terminal screen changes briefly refresh the buffer before the next text is printed.")]
        [SerializeField] private bool useRefreshTransition = true;
        [Tooltip("Seconds to hold the old buffer for one beat after a new screen is requested.")]
        [SerializeField, Min(0f)] private float refreshHoldSeconds = 0.025f;
        [Tooltip("Seconds spent dimming the current buffer before the next screen is printed.")]
        [SerializeField, Min(0f)] private float refreshDimSeconds = 0.08f;
        [Tooltip("Seconds to leave the buffer dark between dimming and the next text.")]
        [SerializeField, Min(0f)] private float refreshBlankSeconds = 0.025f;
        [Tooltip("Full-screen overlay color used while the terminal buffer refreshes.")]
        [SerializeField] private Color refreshOverlayColor = new(0f, 0.035f, 0.014f, 0.46f);

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

        [Header("Text Input")]
        [Tooltip("TMP input field used for terminal text entry. If empty, one is created under Screen Panel at runtime.")]
        [SerializeField] private TMP_InputField textInputField;
        [Tooltip("Prefix label shown before the active terminal input value.")]
        [SerializeField] private TextMeshProUGUI textInputPrefixText;
        [Tooltip("Text component used by the active terminal input value.")]
        [SerializeField] private TextMeshProUGUI textInputValueText;
        [Tooltip("Placeholder component used by the active terminal input value.")]
        [SerializeField] private TextMeshProUGUI textInputPlaceholderText;
        [Tooltip("Create a TMP input field under the terminal screen panel when no field is assigned.")]
        [SerializeField] private bool createTextInputIfMissing = true;
        [Tooltip("Normalized vertical anchor min for the terminal input row inside the screen panel. Horizontal alignment follows terminal text.")]
        [SerializeField] private Vector2 textInputAnchorMin = new(0f, 0.255f);
        [Tooltip("Normalized vertical anchor max for the terminal input row inside the screen panel. Horizontal alignment follows terminal text.")]
        [SerializeField] private Vector2 textInputAnchorMax = new(1f, 0.335f);
        [Tooltip("Horizontal spacing between the input prefix and editable text.")]
        [SerializeField, Min(0f)] private float textInputPrefixSpacing = 8f;
        [Tooltip("Caret width for terminal text input.")]
        [SerializeField, Min(1)] private int textInputCaretWidth = 2;
        [Tooltip("Selection color for terminal text input.")]
        [SerializeField] private Color textInputSelectionColor = new(0.35f, 1f, 0.5f, 0.35f);

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
        private bool _hasAuthoredTextViewportLayout;
        private Vector2 _authoredTextViewportAnchorMin;
        private Vector2 _authoredTextViewportAnchorMax;
        private Vector2 _authoredTextViewportOffsetMin;
        private Vector2 _authoredTextViewportOffsetMax;
        private Vector2 _authoredTextViewportPivot;
        private bool _scrollInitialized;
        private float _contentTopInsetNormalized;
        private Vector4 _baseTextMargin;
        private bool _hasBaseTextMargin;
        private bool _inputCursorVisible = true;
        private RectTransform _textInputRootRect;
        private RectTransform _textInputViewportRect;
        private CanvasGroup _textInputCanvasGroup;
        private TMP_FontAsset _baseTextMeshFont;
        private TMP_FontAsset _baseTextInputPrefixFont;
        private TMP_FontAsset _baseTextInputValueFont;
        private TMP_FontAsset _baseTextInputPlaceholderFont;
        private bool _hasBaseFonts;
        private bool _textInputConfigured;
        private bool _textInputActive;
        private bool _textInputVisible = true;
        private bool _suppressTextInputCallbacks;
        private Action<string> _activeTextInputSubmitted;
        private Action<string> _activeTextInputChanged;
        private Coroutine _textInputFocusRoutine;
        private Coroutine _textInputActivationRoutine;
        private Image _refreshOverlay;
        private RectTransform _refreshOverlayRect;

        private static readonly char[] NoiseChars =
            "!@#$%^&*<>?/\\|~`0123456789ABCDEFXYZabcxyz".ToCharArray();

        public RectTransform ScreenRectTransform =>
            screenPanel != null ? screenPanel.GetComponent<RectTransform>() : null;

        public bool IsTyping() => _isTyping;
        public bool IsTextInputActive => _textInputActive;
        public float ContentTopInsetNormalized => _contentTopInsetNormalized;
        public string TextInputValue => textInputField != null ? textInputField.text ?? string.Empty : string.Empty;
        public string TextInputDisplayValue => textInputValueText != null
            ? textInputValueText.text ?? string.Empty
            : TextInputValue;

        public void SetContentTopInsetNormalized(float topInsetNormalized)
        {
            _contentTopInsetNormalized = Mathf.Clamp01(topInsetNormalized);

            if (enableScroll)
                EnsureScrollViewConfigured();

            ApplyTextTopInsetMargin();

            if (!_scrollInitialized)
                return;

            ApplyTextContentLayout();
            RefreshScrollLayout(true);
        }

        private void Awake()
        {
            CaptureBaseTextMargin();
            CaptureBaseFonts();
            EnsureScrollViewConfigured();
            ApplyLocalizedFonts();
            Clear();
        }

        private void OnEnable()
        {
            L10n.LocaleChanged += OnLocaleChanged;
            EnsureScrollViewConfigured();
            ApplyLocalizedFonts();
        }

        private void OnDisable()
        {
            L10n.LocaleChanged -= OnLocaleChanged;
            HideTextInput();
        }

        private void OnValidate()
        {
            scrollSensitivity = Mathf.Max(1f, scrollSensitivity);
            bottomSnapThreshold = Mathf.Clamp(bottomSnapThreshold, 0f, 0.1f);
            refreshHoldSeconds = Mathf.Max(0f, refreshHoldSeconds);
            refreshDimSeconds = Mathf.Max(0f, refreshDimSeconds);
            refreshBlankSeconds = Mathf.Max(0f, refreshBlankSeconds);

            if (scrollRect != null)
                scrollRect.scrollSensitivity = scrollSensitivity;

            textInputPrefixSpacing = Mathf.Max(0f, textInputPrefixSpacing);
            textInputCaretWidth = Mathf.Max(1, textInputCaretWidth);
        }

        private void OnRectTransformDimensionsChange()
        {
            if (!_scrollInitialized || _panelRect == null)
                return;

            ApplyTextViewportLayout();
            ApplyTextContentLayout();
            ApplyTextInputLayout();
            AlignRefreshOverlay();
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
                SetRefreshOverlayVisible(false, 0f);
                _isTyping = false;
                _typingRoutine = null;
                _currentText = resolvedText;
                if (textMesh != null)
                    ApplyRenderedText(BuildRenderedText(_currentText, true), true);

                StartCursorBlinkIfNeeded();
                OnTypingComplete?.Invoke();
                return;
            }

            SuspendTextInputView();
            CancelQueuedTextInputActivation();
            _typingRoutine = StartCoroutine(RefreshThenTypingRoutine(resolvedText));
        }

        public void ShowTextWithTypedSuffix(string text, int visibleCharacterCount, bool instant = false)
        {
            if (_typingRoutine != null)
                StopCoroutine(_typingRoutine);

            if (_cursorRoutine != null)
            {
                StopCoroutine(_cursorRoutine);
                _cursorRoutine = null;
            }

            if (!instant)
            {
                SuspendTextInputView();
                CancelQueuedTextInputActivation();
            }

            SetRefreshOverlayVisible(false, 0f);
            string resolvedText = text ?? string.Empty;
            int clampedVisibleCount = Mathf.Clamp(visibleCharacterCount, 0, resolvedText.Length);
            if (instant || clampedVisibleCount >= resolvedText.Length)
            {
                _isTyping = false;
                _typingRoutine = null;
                _currentText = resolvedText;
                if (textMesh != null)
                    ApplyRenderedText(BuildRenderedText(_currentText, true), true);

                StartCursorBlinkIfNeeded();
                OnTypingComplete?.Invoke();
                return;
            }

            _typingRoutine = StartCoroutine(TypingRoutine(resolvedText, clampedVisibleCount));
        }

        public void Clear()
        {
            HideTextInput();
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
            SetRefreshOverlayVisible(false, 0f);
            if (textMesh != null)
                ApplyRenderedText(BuildRenderedText(string.Empty, true), true);
        }

        public void BeginTextInput(
            string prefix,
            string value,
            int characterLimit,
            Action<string> onSubmitted,
            Action<string> onChanged = null,
            bool visible = true)
        {
            EnsureTextInputConfigured();
            if (textInputField == null)
            {
                Debug.LogWarning("[TerminalDisplay] Cannot begin terminal text input because no TMP_InputField is available.", this);
                return;
            }

            _textInputActive = true;
            _textInputVisible = visible;
            _activeTextInputSubmitted = onSubmitted;
            _activeTextInputChanged = onChanged;

            textInputField.gameObject.SetActive(false);
            textInputField.characterLimit = Mathf.Max(0, characterLimit);
            textInputField.contentType = TMP_InputField.ContentType.Standard;
            textInputField.lineType = TMP_InputField.LineType.SingleLine;
            textInputField.characterValidation = TMP_InputField.CharacterValidation.None;
            textInputField.caretWidth = textInputCaretWidth;
            textInputField.selectionColor = textInputSelectionColor;

            if (textInputPrefixText != null)
            {
                textInputPrefixText.text = prefix ?? string.Empty;
            }

            _suppressTextInputCallbacks = true;
            textInputField.SetTextWithoutNotify(value ?? string.Empty);
            _suppressTextInputCallbacks = false;

            ApplyLocalizedFonts();
            ApplyTextInputLayout();
            QueueTextInputActivation();
        }

        public void HideTextInput()
        {
            _textInputActive = false;
            _activeTextInputSubmitted = null;
            _activeTextInputChanged = null;

            CancelQueuedTextInputActivation();
            if (_textInputFocusRoutine != null)
            {
                StopCoroutine(_textInputFocusRoutine);
                _textInputFocusRoutine = null;
            }

            if (textInputField == null)
            {
                return;
            }

            SuspendTextInputView();
        }

        public void FocusTextInput()
        {
            if (!_textInputActive || textInputField == null || !textInputField.isActiveAndEnabled)
            {
                return;
            }

            if (EventSystem.current == null)
            {
                Debug.LogWarning("[TerminalDisplay] EventSystem is missing. Terminal text input cannot receive UI focus.", this);
                return;
            }

            EventSystem.current.SetSelectedGameObject(textInputField.gameObject);
            textInputField.Select();
            textInputField.ActivateInputField();
            textInputField.MoveTextEnd(false);
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

        private IEnumerator TypingRoutine(string fullText, int visibleCharacterCount = 0)
        {
            _isTyping = true;
            int startIndex = Mathf.Clamp(visibleCharacterCount, 0, fullText.Length);
            _currentText = startIndex > 0 ? fullText.Substring(0, startIndex) : string.Empty;

            if (textMesh != null)
                ApplyRenderedText(BuildTypingText(_currentText), forceFollowBottom: true);

            for (int i = startIndex; i < fullText.Length; i++)
            {
                if (useNoise && fullText[i] != '\n' && fullText[i] != ' ' && UnityEngine.Random.value < 0.25f)
                {
                    char noise = NoiseChars[UnityEngine.Random.Range(0, NoiseChars.Length)];
                    if (textMesh != null)
                        ApplyRenderedText(BuildTypingText(_currentText + noise), forceFollowBottom: true);

                    yield return new WaitForSeconds(noiseDisplayTime);
                }

                _currentText += fullText[i];
                if (textMesh != null)
                    ApplyRenderedText(BuildTypingText(_currentText), forceFollowBottom: true);

                float delay = fullText[i] is '\n' or ' ' ? typingSpeed * 0.3f : typingSpeed;
                yield return new WaitForSeconds(delay);
            }

            _isTyping = false;
            _typingRoutine = null;
            if (textMesh != null)
                ApplyRenderedText(BuildRenderedText(_currentText, true), forceFollowBottom: true);

            StartCursorBlinkIfNeeded();
            OnTypingComplete?.Invoke();
        }

        private IEnumerator RefreshThenTypingRoutine(string fullText)
        {
            _isTyping = true;

            if (useRefreshTransition && textMesh != null && !string.IsNullOrEmpty(_currentText))
            {
                EnsureRefreshOverlay();
                SetRefreshOverlayVisible(true, 0f);
                if (refreshHoldSeconds > 0f)
                {
                    yield return new WaitForSeconds(refreshHoldSeconds);
                }

                if (refreshDimSeconds > 0f)
                {
                    SetRefreshOverlayVisible(true, 0.58f);
                    yield return new WaitForSeconds(refreshDimSeconds);
                }

                ApplyRenderedText(string.Empty, forceFollowBottom: true);
                if (refreshBlankSeconds > 0f)
                {
                    SetRefreshOverlayVisible(true, 0.72f);
                    yield return new WaitForSeconds(refreshBlankSeconds);
                }

                SetRefreshOverlayVisible(false, 0f);
            }

            yield return TypingRoutine(fullText);
        }

        private IEnumerator CursorBlink()
        {
            _inputCursorVisible = true;
            while (true)
            {
                yield return new WaitForSeconds(cursorBlinkRate);
                if (!_isTyping && textMesh != null)
                {
                    _inputCursorVisible = !_inputCursorVisible;
                    ApplyRenderedText(BuildRenderedText(_currentText, _inputCursorVisible));
                }
            }
        }

        private void StartCursorBlinkIfNeeded()
        {
            if (_cursorRoutine != null)
            {
                StopCoroutine(_cursorRoutine);
                _cursorRoutine = null;
            }

            _inputCursorVisible = true;
            if (!showCursor || string.IsNullOrEmpty(_currentText) || !_currentText.Contains(CursorMarker) || !isActiveAndEnabled)
            {
                return;
            }

            _cursorRoutine = StartCoroutine(CursorBlink());
        }

        private string BuildTypingText(string rawText)
        {
            bool hasInputCursor = !string.IsNullOrEmpty(rawText) && rawText.Contains(CursorMarker);
            string typingText = showCursor && !hasInputCursor
                ? (rawText ?? string.Empty) + "_"
                : rawText ?? string.Empty;
            return BuildRenderedText(typingText, true);
        }

        private string BuildRenderedText(string rawText, bool cursorVisible)
        {
            string text = rawText ?? string.Empty;
            if (!text.Contains(CursorMarker))
            {
                return text;
            }

            string cursorText = cursorVisible ? "_" : " ";
            return text.Replace(CursorMarker, cursorText);
        }

        private void EnsureScrollViewConfigured()
        {
            if (_scrollInitialized || !enableScroll || textMesh == null || screenPanel == null)
                return;

            CaptureBaseTextMargin();
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

            textSizeFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            textSizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
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
                if (_textViewportRect != null)
                {
                    CaptureAuthoredTextViewportLayout();
                }
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

        private void CaptureAuthoredTextViewportLayout()
        {
            if (_textViewportRect == null)
                return;

            _hasAuthoredTextViewportLayout = true;
            _authoredTextViewportAnchorMin = _textViewportRect.anchorMin;
            _authoredTextViewportAnchorMax = _textViewportRect.anchorMax;
            _authoredTextViewportOffsetMin = _textViewportRect.offsetMin;
            _authoredTextViewportOffsetMax = _textViewportRect.offsetMax;
            _authoredTextViewportPivot = _textViewportRect.pivot;
        }

        private void EnsureTextInputConfigured()
        {
            if (_textInputConfigured)
            {
                return;
            }

            if (textInputField == null && createTextInputIfMissing)
            {
                CreateRuntimeTextInput();
            }

            if (textInputField == null)
            {
                return;
            }

            _textInputRootRect = textInputField.GetComponent<RectTransform>();
            _textInputCanvasGroup = textInputField.GetComponent<CanvasGroup>();
            if (_textInputCanvasGroup == null)
            {
                _textInputCanvasGroup = textInputField.gameObject.AddComponent<CanvasGroup>();
            }

            _textInputViewportRect = textInputField.textViewport;
            if (textInputValueText == null)
            {
                textInputValueText = textInputField.textComponent as TextMeshProUGUI;
            }

            if (textInputPlaceholderText == null)
            {
                textInputPlaceholderText = textInputField.placeholder as TextMeshProUGUI;
            }

            textInputField.onSubmit.RemoveListener(HandleTextInputSubmitted);
            textInputField.onSubmit.AddListener(HandleTextInputSubmitted);
            textInputField.onValueChanged.RemoveListener(HandleTextInputChanged);
            textInputField.onValueChanged.AddListener(HandleTextInputChanged);

            textInputField.richText = false;
            textInputField.resetOnDeActivation = false;
            textInputField.restoreOriginalTextOnEscape = false;
            textInputField.navigation = new Navigation { mode = Navigation.Mode.None };

            ApplyLocalizedFonts();
            ApplyTextInputLayout();
            textInputField.gameObject.SetActive(false);
            SetTextInputViewVisible(false);
            _textInputConfigured = true;
        }

        private void EnsureRefreshOverlay()
        {
            if (_refreshOverlay != null)
            {
                AlignRefreshOverlay();
                _refreshOverlay.transform.SetAsLastSibling();
                return;
            }

            RectTransform screenRect = ScreenRectTransform;
            if (screenRect == null)
            {
                return;
            }

            GameObject overlayObject = new(
                "TerminalRefreshOverlay",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            overlayObject.transform.SetParent(screenRect, false);
            _refreshOverlayRect = overlayObject.GetComponent<RectTransform>();
            _refreshOverlay = overlayObject.GetComponent<Image>();
            _refreshOverlay.raycastTarget = false;
            AlignRefreshOverlay();
            SetRefreshOverlayVisible(false, 0f);
            _refreshOverlay.transform.SetAsLastSibling();
        }

        private void AlignRefreshOverlay()
        {
            if (_refreshOverlayRect == null)
            {
                return;
            }

            _refreshOverlayRect.anchorMin = Vector2.zero;
            _refreshOverlayRect.anchorMax = Vector2.one;
            _refreshOverlayRect.pivot = new Vector2(0.5f, 0.5f);
            _refreshOverlayRect.offsetMin = Vector2.zero;
            _refreshOverlayRect.offsetMax = Vector2.zero;
        }

        private void SetRefreshOverlayVisible(bool visible, float alphaMultiplier)
        {
            if (_refreshOverlay == null)
            {
                return;
            }

            _refreshOverlay.gameObject.SetActive(visible);
            if (!visible)
            {
                return;
            }

            _refreshOverlay.transform.SetAsLastSibling();
            Color color = refreshOverlayColor;
            color.a *= Mathf.Clamp01(alphaMultiplier);
            _refreshOverlay.color = color;
        }

        private void CreateRuntimeTextInput()
        {
            RectTransform screenRect = ScreenRectTransform;
            if (screenRect == null)
            {
                return;
            }

            GameObject inputObject = new(
                "TerminalTextInput",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(TMP_InputField));
            inputObject.transform.SetParent(screenRect, false);

            _textInputRootRect = inputObject.GetComponent<RectTransform>();
            textInputField = inputObject.GetComponent<TMP_InputField>();

            Image background = inputObject.GetComponent<Image>();
            background.color = new Color(0f, 0f, 0f, 0f);
            background.raycastTarget = true;
            textInputField.targetGraphic = background;

            GameObject prefixObject = new(
                "Prefix",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));
            prefixObject.transform.SetParent(inputObject.transform, false);
            textInputPrefixText = prefixObject.GetComponent<TextMeshProUGUI>();
            textInputPrefixText.raycastTarget = false;
            textInputPrefixText.alignment = TextAlignmentOptions.Left;
            textInputPrefixText.textWrappingMode = TextWrappingModes.PreserveWhitespaceNoWrap;

            GameObject viewportObject = new(
                "Text Area",
                typeof(RectTransform),
                typeof(RectMask2D));
            viewportObject.transform.SetParent(inputObject.transform, false);
            _textInputViewportRect = viewportObject.GetComponent<RectTransform>();
            textInputField.textViewport = _textInputViewportRect;

            GameObject valueObject = new(
                "Text",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));
            valueObject.transform.SetParent(viewportObject.transform, false);
            textInputValueText = valueObject.GetComponent<TextMeshProUGUI>();
            textInputValueText.raycastTarget = false;
            textInputValueText.alignment = TextAlignmentOptions.Left;
            textInputValueText.textWrappingMode = TextWrappingModes.PreserveWhitespaceNoWrap;
            textInputValueText.overflowMode = TextOverflowModes.Overflow;
            textInputField.textComponent = textInputValueText;

            GameObject placeholderObject = new(
                "Placeholder",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));
            placeholderObject.transform.SetParent(viewportObject.transform, false);
            textInputPlaceholderText = placeholderObject.GetComponent<TextMeshProUGUI>();
            textInputPlaceholderText.raycastTarget = false;
            textInputPlaceholderText.alignment = TextAlignmentOptions.Left;
            textInputPlaceholderText.textWrappingMode = TextWrappingModes.PreserveWhitespaceNoWrap;
            textInputPlaceholderText.color = new Color(1f, 1f, 1f, 0.35f);
            textInputPlaceholderText.text = string.Empty;
            textInputField.placeholder = textInputPlaceholderText;
        }

        private float GetContentTopInsetPixels()
        {
            if (_panelRect == null)
                return 0f;

            return Mathf.Clamp01(_contentTopInsetNormalized) * Mathf.Max(0f, _panelRect.rect.height);
        }

        private float GetTextVisibleHeight()
        {
            if (_textViewportRect != null)
                return Mathf.Max(1f, _textViewportRect.rect.height);

            if (_panelRect == null)
                return 1f;

            return Mathf.Max(1f, _panelRect.rect.height - GetContentTopInsetPixels());
        }

        private void ApplyTextViewportLayout()
        {
            if (_textViewportRect == null)
                return;

            if (_hasAuthoredTextViewportLayout)
            {
                _textViewportRect.anchorMin = _authoredTextViewportAnchorMin;
                _textViewportRect.anchorMax = _authoredTextViewportAnchorMax;
                _textViewportRect.pivot = _authoredTextViewportPivot;
                _textViewportRect.offsetMin = _authoredTextViewportOffsetMin;

                Vector2 offsetMax = _authoredTextViewportOffsetMax;
                offsetMax.y -= GetContentTopInsetPixels();
                _textViewportRect.offsetMax = offsetMax;
                return;
            }

            _textViewportRect.anchorMin = Vector2.zero;
            _textViewportRect.anchorMax = Vector2.one;
            _textViewportRect.pivot = new Vector2(0.5f, 0.5f);
            _textViewportRect.offsetMin = Vector2.zero;
            _textViewportRect.offsetMax = new Vector2(0f, -GetContentTopInsetPixels());
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

        private void ApplyTextInputLayout()
        {
            if (textInputField == null)
            {
                return;
            }

            _textInputRootRect ??= textInputField.GetComponent<RectTransform>();
            _textInputViewportRect ??= textInputField.textViewport;

            if (_textInputRootRect != null)
            {
                _textInputRootRect.anchorMin = new Vector2(0f, textInputAnchorMin.y);
                _textInputRootRect.anchorMax = new Vector2(1f, textInputAnchorMax.y);
                _textInputRootRect.pivot = new Vector2(0.5f, 0.5f);
                _textInputRootRect.offsetMin = Vector2.zero;
                _textInputRootRect.offsetMax = Vector2.zero;
            }

            float prefixWidth = 0f;
            if (textInputPrefixText != null)
            {
                RectTransform prefixRect = textInputPrefixText.rectTransform;
                prefixRect.anchorMin = new Vector2(0f, 0f);
                prefixRect.anchorMax = new Vector2(0f, 1f);
                prefixRect.pivot = new Vector2(0f, 0.5f);
                prefixRect.anchoredPosition = Vector2.zero;

                string prefix = textInputPrefixText.text ?? string.Empty;
                prefixWidth = Mathf.Ceil(textInputPrefixText.GetPreferredValues(prefix).x + textInputPrefixSpacing);
                prefixRect.sizeDelta = new Vector2(prefixWidth, 0f);
                prefixRect.offsetMin = new Vector2(0f, 0f);
                prefixRect.offsetMax = new Vector2(prefixWidth, 0f);
            }

            if (_textInputViewportRect != null)
            {
                _textInputViewportRect.anchorMin = Vector2.zero;
                _textInputViewportRect.anchorMax = Vector2.one;
                _textInputViewportRect.pivot = new Vector2(0.5f, 0.5f);
                _textInputViewportRect.offsetMin = new Vector2(prefixWidth, 0f);
                _textInputViewportRect.offsetMax = Vector2.zero;
            }

            ApplyTextInputTextRect(textInputValueText);
            ApplyTextInputTextRect(textInputPlaceholderText);
        }

        private static void ApplyTextInputTextRect(TextMeshProUGUI text)
        {
            if (text == null)
            {
                return;
            }

            RectTransform rect = text.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
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

            ApplyTextTopInsetMargin();
            Canvas.ForceUpdateCanvases();
            if (forceToBottom)
                scrollRect.verticalNormalizedPosition = 0f;
        }

        private void CaptureBaseTextMargin()
        {
            if (_hasBaseTextMargin || textMesh == null)
                return;

            _baseTextMargin = textMesh.margin;
            _hasBaseTextMargin = true;
        }

        private void CaptureBaseFonts()
        {
            if (!_hasBaseFonts && textMesh != null)
            {
                _baseTextMeshFont = textMesh.font;
            }

            if (_baseTextInputPrefixFont == null && textInputPrefixText != null)
            {
                _baseTextInputPrefixFont = textInputPrefixText.font;
            }

            if (_baseTextInputValueFont == null && textInputValueText != null)
            {
                _baseTextInputValueFont = textInputValueText.font;
            }

            if (_baseTextInputPlaceholderFont == null && textInputPlaceholderText != null)
            {
                _baseTextInputPlaceholderFont = textInputPlaceholderText.font;
            }

            _hasBaseFonts = true;
        }

        private void ApplyLocalizedFonts()
        {
            CaptureBaseFonts();
            TMP_FontAsset localizedFont = L10n.CurrentFont;

            if (textMesh != null)
            {
                textMesh.font = localizedFont != null ? localizedFont : _baseTextMeshFont;
            }

            ApplyLocalizedFont(textInputPrefixText, localizedFont, _baseTextInputPrefixFont);
            ApplyLocalizedFont(textInputValueText, localizedFont, _baseTextInputValueFont);
            ApplyLocalizedFont(textInputPlaceholderText, localizedFont, _baseTextInputPlaceholderFont);

            if (textMesh != null)
            {
                ApplyTextInputTextStyle(textInputPrefixText);
                ApplyTextInputTextStyle(textInputValueText);
                ApplyTextInputTextStyle(textInputPlaceholderText);
            }
        }

        private static void ApplyLocalizedFont(
            TextMeshProUGUI text,
            TMP_FontAsset localizedFont,
            TMP_FontAsset fallbackFont)
        {
            if (text == null)
            {
                return;
            }

            text.font = localizedFont != null ? localizedFont : fallbackFont;
        }

        private void ApplyTextInputTextStyle(TextMeshProUGUI text)
        {
            if (text == null || textMesh == null)
            {
                return;
            }

            text.fontSize = textMesh.fontSize;
            text.fontStyle = textMesh.fontStyle;
            text.color = textMesh.color;
            text.characterSpacing = textMesh.characterSpacing;
            text.wordSpacing = textMesh.wordSpacing;
            text.lineSpacing = textMesh.lineSpacing;
            text.alignment = TextAlignmentOptions.Left;
            text.textWrappingMode = TextWrappingModes.PreserveWhitespaceNoWrap;
        }

        private void OnLocaleChanged(string locale)
        {
            ApplyLocalizedFonts();
            ApplyTextInputLayout();
            if (textMesh != null)
            {
                ApplyRenderedText(BuildRenderedText(_currentText, _inputCursorVisible), true);
            }
        }

        private void ApplyTextTopInsetMargin()
        {
            if (textMesh == null)
                return;

            CaptureBaseTextMargin();
            float topInset = UsesInsetViewport() ? 0f : GetContentTopInsetPixels();
            Vector4 margin = _baseTextMargin;
            margin.y += topInset;
            textMesh.margin = margin;
        }

        private bool UsesInsetViewport()
        {
            return _scrollInitialized &&
                   _textViewportRect != null &&
                   _textRect != null &&
                   _textRect.parent == _textViewportRect;
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
                Debug.LogWarning("[TerminalDisplay] Event camera is missing for world-space terminal UI. Drag scroll may not receive pointer input.", this);
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

            ApplyTextTopInsetMargin();
            bool followBottom = forceFollowBottom || ShouldFollowBottom();
            textMesh.text = renderedText;

            if (_scrollInitialized)
                RefreshScrollLayout(followBottom);
        }

        private void HandleTextInputSubmitted(string value)
        {
            if (!_textInputActive)
            {
                return;
            }

            _activeTextInputSubmitted?.Invoke(value ?? string.Empty);

            if (_textInputActive)
            {
                FocusTextInput();
                QueueTextInputFocus();
            }
        }

        private void HandleTextInputChanged(string value)
        {
            if (_suppressTextInputCallbacks || !_textInputActive)
            {
                return;
            }

            _activeTextInputChanged?.Invoke(value ?? string.Empty);
        }

        private void QueueTextInputActivation()
        {
            CancelQueuedTextInputActivation();

            if (!_textInputActive || textInputField == null)
            {
                return;
            }

            if (_isTyping || _typingRoutine != null)
            {
                _textInputActivationRoutine = StartCoroutine(ActivateTextInputAfterTyping());
                return;
            }

            ActivateTextInputView();
        }

        private IEnumerator ActivateTextInputAfterTyping()
        {
            while (_textInputActive && (_isTyping || _typingRoutine != null))
            {
                yield return null;
            }

            _textInputActivationRoutine = null;
            ActivateTextInputView();
        }

        private void ActivateTextInputView()
        {
            if (!_textInputActive || textInputField == null)
            {
                return;
            }

            textInputField.gameObject.SetActive(true);
            SetTextInputViewVisible(_textInputVisible);
            ApplyTextInputLayout();
            FocusTextInput();
            QueueTextInputFocus();
        }

        private void SuspendTextInputView()
        {
            if (textInputField == null)
            {
                return;
            }

            if (_textInputFocusRoutine != null)
            {
                StopCoroutine(_textInputFocusRoutine);
                _textInputFocusRoutine = null;
            }

            textInputField.DeactivateInputField();
            SetTextInputViewVisible(false);
            textInputField.gameObject.SetActive(false);
        }

        private void SetTextInputViewVisible(bool visible)
        {
            if (_textInputCanvasGroup == null && textInputField != null)
            {
                _textInputCanvasGroup = textInputField.GetComponent<CanvasGroup>();
            }

            if (_textInputCanvasGroup == null)
            {
                return;
            }

            _textInputCanvasGroup.alpha = visible ? 1f : 0f;
            _textInputCanvasGroup.interactable = true;
            _textInputCanvasGroup.blocksRaycasts = visible;
        }

        private void CancelQueuedTextInputActivation()
        {
            if (_textInputActivationRoutine == null)
            {
                return;
            }

            StopCoroutine(_textInputActivationRoutine);
            _textInputActivationRoutine = null;
        }

        private void QueueTextInputFocus()
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            if (_textInputFocusRoutine != null)
            {
                StopCoroutine(_textInputFocusRoutine);
            }

            _textInputFocusRoutine = StartCoroutine(FocusTextInputNextFrame());
        }

        private IEnumerator FocusTextInputNextFrame()
        {
            yield return null;
            _textInputFocusRoutine = null;
            FocusTextInput();
        }

        [ContextMenu("Test: ShowDummyText")]
        private void TestShow() =>
            ShowText(
                L10n.T("first_contact.terminal.header.translation_buffer", "[TRANSLATION BUFFER]") + "\n" +
                L10n.T("first_contact.terminal.line.translator_ready", "TRANSLATOR READY") + "\n> _");

        [ContextMenu("Test: Clear")]
        private void TestClear() => Clear();
    }
}
