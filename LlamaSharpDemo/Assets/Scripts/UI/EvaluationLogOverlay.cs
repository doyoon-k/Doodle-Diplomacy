using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DoodleDiplomacy.UI
{
    public class EvaluationLogOverlay : MonoBehaviour
    {
        [Header("Layout")]
        [SerializeField] private Vector2 anchoredPosition = new Vector2(24f, -24f);
        [SerializeField] private Vector2 panelSize = new Vector2(520f, 220f);
        [SerializeField] private int maxEntries = 10;

        [Header("Style")]
        [SerializeField] private Color panelColor = new Color(0.04f, 0.06f, 0.08f, 0.86f);
        [SerializeField] private Color borderColor = new Color(0.24f, 0.95f, 0.62f, 0.95f);
        [SerializeField] private Color textColor = new Color(0.78f, 1f, 0.88f, 1f);
        [SerializeField] private int fontSize = 20;

        private readonly Queue<string> _entries = new Queue<string>();
        private readonly StringBuilder _builder = new StringBuilder(1024);

        private Canvas _canvas;
        private TextMeshProUGUI _text;

        private void Awake()
        {
            EnsureUi();
            RebuildText();
        }

        public void Clear()
        {
            _entries.Clear();
            RebuildText();
        }

        public void Log(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            EnsureUi();
            _entries.Enqueue($"[{System.DateTime.Now:HH:mm:ss}] {message.Trim()}");
            while (_entries.Count > Mathf.Max(1, maxEntries))
            {
                _entries.Dequeue();
            }

            RebuildText();
        }

        private void EnsureUi()
        {
            if (_text != null)
            {
                return;
            }

            var canvasObject = new GameObject("EvaluationLogCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);

            _canvas = canvasObject.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 400;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            var panelObject = new GameObject("EvaluationLogPanel", typeof(RectTransform), typeof(Image), typeof(Outline));
            panelObject.transform.SetParent(canvasObject.transform, false);

            var panelRect = panelObject.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0f, 1f);
            panelRect.anchorMax = new Vector2(0f, 1f);
            panelRect.pivot = new Vector2(0f, 1f);
            panelRect.anchoredPosition = anchoredPosition;
            panelRect.sizeDelta = panelSize;

            var panelImage = panelObject.GetComponent<Image>();
            panelImage.color = panelColor;

            var outline = panelObject.GetComponent<Outline>();
            outline.effectColor = borderColor;
            outline.effectDistance = new Vector2(2f, -2f);

            var textObject = new GameObject("EvaluationLogText", typeof(RectTransform), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(panelObject.transform, false);

            var textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(14f, 14f);
            textRect.offsetMax = new Vector2(-14f, -14f);

            _text = textObject.GetComponent<TextMeshProUGUI>();
            _text.font = TMP_Settings.defaultFontAsset;
            _text.fontSize = fontSize;
            _text.color = textColor;
            _text.alignment = TextAlignmentOptions.TopLeft;
            _text.enableWordWrapping = true;
            _text.overflowMode = TextOverflowModes.Overflow;
            _text.text = string.Empty;
        }

        private void RebuildText()
        {
            if (_text == null)
            {
                return;
            }

            _builder.Clear();
            foreach (string entry in _entries)
            {
                if (_builder.Length > 0)
                {
                    _builder.Append('\n');
                }

                _builder.Append(entry);
            }

            _text.text = _builder.ToString();
        }
    }
}
