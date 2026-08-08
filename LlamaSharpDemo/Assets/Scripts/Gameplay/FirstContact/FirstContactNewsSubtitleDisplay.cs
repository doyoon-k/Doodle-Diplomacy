using System.Collections;
using DoodleDiplomacy.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    /// <summary>
    /// Runtime-only presentation for the readable dialogue that accompanies the
    /// in-world television broadcast. Authored words and timings come from the
    /// Narrative Desk scenario, so designers do not have to edit this UI.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FirstContactNewsSubtitleDisplay : MonoBehaviour
    {
        private const float FadeSeconds = 0.12f;

        private GameObject _root;
        private RectTransform _rootRect;
        private CanvasGroup _canvasGroup;
        private Image _background;
        private Image _topAccent;
        private TextMeshProUGUI _speakerText;
        private TextMeshProUGUI _dialogueText;
        private TextMeshProUGUI _advanceText;
        private Coroutine _fadeRoutine;

        private void OnEnable()
        {
            L10n.LocaleChanged += HandleLocaleChanged;
        }

        private void OnDisable()
        {
            L10n.LocaleChanged -= HandleLocaleChanged;
            HideImmediate();
        }

        private void OnDestroy()
        {
            if (_root != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(_root);
                }
                else
                {
                    DestroyImmediate(_root);
                }
            }
        }

        public void Show(string speaker, string dialogue)
        {
            ShowNews(speaker, dialogue);
        }

        public void ShowNews(string speaker, string dialogue)
        {
            Show(speaker, dialogue, newsStyle: true);
        }

        public void ShowDialogue(string speaker, string dialogue)
        {
            Show(speaker, dialogue, newsStyle: false);
        }

        public void ShowDialogueImmediate(string speaker, string dialogue)
        {
            Show(speaker, dialogue, newsStyle: false, showImmediately: true);
        }

        public bool TryCaptureVisibleDialogue(out string speaker, out string dialogue)
        {
            speaker = string.Empty;
            dialogue = string.Empty;
            if (_root == null ||
                !_root.activeSelf ||
                _canvasGroup == null ||
                _canvasGroup.alpha <= 0f ||
                _speakerText == null ||
                _dialogueText == null)
            {
                return false;
            }

            speaker = _speakerText.text ?? string.Empty;
            dialogue = _dialogueText.text ?? string.Empty;
            return !string.IsNullOrWhiteSpace(dialogue);
        }

        private void Show(
            string speaker,
            string dialogue,
            bool newsStyle,
            bool showImmediately = false)
        {
            EnsureLayout();
            if (_root == null)
            {
                return;
            }

            ApplyPresentationStyle(newsStyle);
            _speakerText.text = speaker ?? string.Empty;
            _dialogueText.text = dialogue ?? string.Empty;
            SetAdvancePromptVisible(false);
            RefreshFont();
            _root.SetActive(true);
            if (showImmediately)
            {
                if (_fadeRoutine != null)
                {
                    StopCoroutine(_fadeRoutine);
                    _fadeRoutine = null;
                }

                _canvasGroup.alpha = 1f;
            }
            else
            {
                FadeTo(1f, FadeSeconds);
            }
        }

        public void SetAdvancePromptVisible(bool visible)
        {
            EnsureLayout();
            if (_advanceText != null)
            {
                _advanceText.gameObject.SetActive(visible);
            }
        }

        public void Hide()
        {
            SetAdvancePromptVisible(false);
            if (_root == null || !_root.activeSelf)
            {
                return;
            }

            FadeTo(0f, FadeSeconds, deactivateOnComplete: true);
        }

        public void HideImmediate()
        {
            if (_advanceText != null)
            {
                _advanceText.gameObject.SetActive(false);
            }

            if (_fadeRoutine != null)
            {
                StopCoroutine(_fadeRoutine);
                _fadeRoutine = null;
            }

            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 0f;
            }

            if (_root != null)
            {
                _root.SetActive(false);
            }
        }

        private void EnsureLayout()
        {
            if (_root != null)
            {
                return;
            }

            Canvas hostCanvas = GetComponent<Canvas>();
            if (hostCanvas == null)
            {
                return;
            }

            _root = CreateUiObject("NewsTranscript_Runtime", hostCanvas.transform);
            _rootRect = _root.GetComponent<RectTransform>();
            Stretch(_rootRect, new Vector2(0.12f, 0.045f), new Vector2(0.88f, 0.175f));

            _background = _root.AddComponent<Image>();
            _background.color = new Color(0.012f, 0.02f, 0.045f, 0.91f);
            _background.raycastTarget = false;

            _topAccent = CreatePanel(
                "Accent",
                _root.transform,
                new Vector2(0f, 0.94f),
                Vector2.one,
                new Color(0.68f, 0.08f, 0.09f, 0.95f));

            _speakerText = CreateText(
                "Speaker",
                _root.transform,
                new Vector2(0.035f, 0.66f),
                new Vector2(0.965f, 0.91f),
                22f,
                FontStyles.Bold,
                TextAlignmentOptions.Left,
                new Color(1f, 0.82f, 0.55f, 1f));

            _dialogueText = CreateText(
                "Dialogue",
                _root.transform,
                new Vector2(0.035f, 0.10f),
                new Vector2(0.965f, 0.67f),
                31f,
                FontStyles.Normal,
                TextAlignmentOptions.TopLeft,
                new Color(0.97f, 0.97f, 0.94f, 1f));

            _advanceText = CreateText(
                "Advance",
                _root.transform,
                new Vector2(0.78f, 0.015f),
                new Vector2(0.965f, 0.20f),
                17f,
                FontStyles.Bold,
                TextAlignmentOptions.BottomRight,
                new Color(1f, 0.82f, 0.55f, 0.95f));
            _advanceText.text = "SPACE  >";
            _advanceText.gameObject.SetActive(false);

            _canvasGroup = _root.AddComponent<CanvasGroup>();
            _canvasGroup.alpha = 0f;
            _root.SetActive(false);
            RefreshFont();
        }

        private void ApplyPresentationStyle(bool newsStyle)
        {
            if (_rootRect == null || _speakerText == null || _dialogueText == null)
            {
                return;
            }

            if (newsStyle)
            {
                Stretch(_rootRect, new Vector2(0.12f, 0.045f), new Vector2(0.88f, 0.175f));
                _background.enabled = true;
                _topAccent.enabled = true;
                Stretch(
                    _dialogueText.rectTransform,
                    new Vector2(0.035f, 0.10f),
                    new Vector2(0.965f, 0.67f));

                _speakerText.fontSize = 22f;
                _speakerText.fontStyle = FontStyles.Bold;
                _speakerText.color = new Color(1f, 0.82f, 0.55f, 1f);
                SetOutline(_speakerText, 0f);

                _dialogueText.fontSize = 31f;
                _dialogueText.enableAutoSizing = false;
                _dialogueText.fontStyle = FontStyles.Normal;
                _dialogueText.color = new Color(0.97f, 0.97f, 0.94f, 1f);
                SetOutline(_dialogueText, 0f);
                return;
            }

            Stretch(_rootRect, new Vector2(0.10f, 0.04f), new Vector2(0.90f, 0.36f));
            _background.enabled = false;
            _topAccent.enabled = false;
            Stretch(
                _dialogueText.rectTransform,
                new Vector2(0.035f, 0.18f),
                new Vector2(0.965f, 0.67f));

            _speakerText.fontSize = 23f;
            _speakerText.fontStyle = FontStyles.Bold;
            _speakerText.color = new Color(1f, 0.82f, 0.55f, 1f);
            SetOutline(_speakerText, 0.20f);

            _dialogueText.fontSize = 32f;
            _dialogueText.enableAutoSizing = true;
            _dialogueText.fontSizeMin = 18f;
            _dialogueText.fontSizeMax = 32f;
            _dialogueText.fontStyle = FontStyles.Normal;
            _dialogueText.color = Color.white;
            SetOutline(_dialogueText, 0.18f);
        }

        private static void SetOutline(TextMeshProUGUI text, float width)
        {
            text.outlineColor = new Color32(0, 0, 0, 255);
            text.outlineWidth = width;
        }

        private void HandleLocaleChanged(string locale)
        {
            RefreshFont();
        }

        private void RefreshFont()
        {
            TMP_FontAsset font = L10n.CurrentFont ?? TMP_Settings.defaultFontAsset;
            if (font == null)
            {
                return;
            }

            if (_speakerText != null)
            {
                _speakerText.font = font;
            }

            if (_dialogueText != null)
            {
                _dialogueText.font = font;
            }

            if (_advanceText != null)
            {
                _advanceText.font = font;
            }
        }

        private void FadeTo(float targetAlpha, float seconds, bool deactivateOnComplete = false)
        {
            if (_canvasGroup == null)
            {
                return;
            }

            if (_fadeRoutine != null)
            {
                StopCoroutine(_fadeRoutine);
            }

            _fadeRoutine = StartCoroutine(FadeRoutine(targetAlpha, seconds, deactivateOnComplete));
        }

        private IEnumerator FadeRoutine(float targetAlpha, float seconds, bool deactivateOnComplete)
        {
            float startAlpha = _canvasGroup.alpha;
            float elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += Time.unscaledDeltaTime;
                float progress = seconds <= 0f ? 1f : Mathf.Clamp01(elapsed / seconds);
                _canvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, progress);
                yield return null;
            }

            _canvasGroup.alpha = targetAlpha;
            _fadeRoutine = null;
            if (deactivateOnComplete && Mathf.Approximately(targetAlpha, 0f) && _root != null)
            {
                _root.SetActive(false);
            }
        }

        private static Image CreatePanel(
            string objectName,
            Transform parent,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Color color)
        {
            GameObject panelObject = CreateUiObject(objectName, parent);
            Image image = panelObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            Stretch(image.rectTransform, anchorMin, anchorMax);
            return image;
        }

        private static TextMeshProUGUI CreateText(
            string objectName,
            Transform parent,
            Vector2 anchorMin,
            Vector2 anchorMax,
            float fontSize,
            FontStyles style,
            TextAlignmentOptions alignment,
            Color color)
        {
            GameObject textObject = CreateUiObject(objectName, parent);
            TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.alignment = alignment;
            text.color = color;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;
            Stretch(text.rectTransform, anchorMin, anchorMax);
            return text;
        }

        private static GameObject CreateUiObject(string objectName, Transform parent)
        {
            GameObject gameObject = new(objectName, typeof(RectTransform));
            gameObject.hideFlags = HideFlags.DontSave;
            gameObject.transform.SetParent(parent, false);
            return gameObject;
        }

        private static void Stretch(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
