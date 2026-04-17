using DoodleDiplomacy.Core;
using TMPro;
using UnityEngine;

namespace DoodleDiplomacy.UI
{
    [DisallowMultipleComponent]
    public sealed class DrawingEscHintOverlay : MonoBehaviour
    {
        [SerializeField] private Canvas targetCanvas;
        [SerializeField] private TMP_Text hintLabel;
        [SerializeField] private string hintText = "Press ESC to finish drawing.";
        [SerializeField] private Vector2 anchoredPosition = new(0f, -24f);
        [SerializeField] private int fontSize = 24;
        [SerializeField] private Color textColor = new(0.94f, 0.96f, 0.98f, 0.95f);

        private RoundManager _roundManager;

        private void Awake()
        {
            targetCanvas ??= GetComponent<Canvas>();
            if (targetCanvas == null)
            {
                targetCanvas = FindFirstObjectByType<Canvas>();
            }

            EnsureLabel();
            UpdateVisibility();
        }

        private void Update()
        {
            UpdateVisibility();
        }

        private void EnsureLabel()
        {
            if (targetCanvas == null)
            {
                return;
            }

            if (hintLabel == null)
            {
                Transform existing = targetCanvas.transform.Find("DrawingEscHintText");
                if (existing != null)
                {
                    hintLabel = existing.GetComponent<TMP_Text>();
                }
            }

            if (hintLabel == null)
            {
                var labelObject = new GameObject("DrawingEscHintText", typeof(RectTransform), typeof(TextMeshProUGUI));
                RectTransform rectTransform = labelObject.GetComponent<RectTransform>();
                rectTransform.SetParent(targetCanvas.transform, false);
                rectTransform.anchorMin = new Vector2(0.5f, 1f);
                rectTransform.anchorMax = new Vector2(0.5f, 1f);
                rectTransform.pivot = new Vector2(0.5f, 1f);
                rectTransform.anchoredPosition = anchoredPosition;
                rectTransform.sizeDelta = new Vector2(640f, 48f);
                hintLabel = labelObject.GetComponent<TextMeshProUGUI>();
            }

            if (hintLabel == null)
            {
                return;
            }

            hintLabel.text = hintText;
            hintLabel.fontSize = fontSize;
            hintLabel.color = textColor;
            hintLabel.alignment = TextAlignmentOptions.Center;
            hintLabel.raycastTarget = false;
        }

        private void UpdateVisibility()
        {
            if (hintLabel == null)
            {
                return;
            }

            _roundManager ??= RoundManager.Instance ?? FindFirstObjectByType<RoundManager>();
            bool visible = _roundManager != null && _roundManager.CurrentState == GameState.Drawing;
            if (hintLabel.gameObject.activeSelf != visible)
            {
                hintLabel.gameObject.SetActive(visible);
            }
        }
    }
}
