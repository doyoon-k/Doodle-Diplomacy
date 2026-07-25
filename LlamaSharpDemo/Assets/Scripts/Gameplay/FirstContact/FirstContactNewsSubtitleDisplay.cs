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
        private CanvasGroup _canvasGroup;
        private TextMeshProUGUI _speakerText;
        private TextMeshProUGUI _dialogueText;
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
                Destroy(_root);
            }
        }

        public void Show(string speaker, string dialogue)
        {
            EnsureLayout();
            if (_root == null)
            {
                return;
            }

            _speakerText.text = speaker ?? string.Empty;
            _dialogueText.text = dialogue ?? string.Empty;
            RefreshFont();
            _root.SetActive(true);
            FadeTo(1f, FadeSeconds);
        }

        public void Hide()
        {
            if (_root == null || !_root.activeSelf)
            {
                return;
            }

            FadeTo(0f, FadeSeconds, deactivateOnComplete: true);
        }

        public void HideImmediate()
        {
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
            RectTransform rootRect = _root.GetComponent<RectTransform>();
            Stretch(rootRect, new Vector2(0.12f, 0.045f), new Vector2(0.88f, 0.175f));

            Image background = _root.AddComponent<Image>();
            background.color = new Color(0.012f, 0.02f, 0.045f, 0.91f);
            background.raycastTarget = false;

            Image topAccent = CreatePanel(
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

            _canvasGroup = _root.AddComponent<CanvasGroup>();
            _canvasGroup.alpha = 0f;
            _root.SetActive(false);
            RefreshFont();
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
