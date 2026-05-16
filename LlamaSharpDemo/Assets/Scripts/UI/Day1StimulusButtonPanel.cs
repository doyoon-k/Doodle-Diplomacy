using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DoodleDiplomacy.UI
{
    [DisallowMultipleComponent]
    public sealed class Day1StimulusButtonPanel : MonoBehaviour
    {
        private readonly List<Button> _buttons = new();

        private Canvas _canvas;
        private GameObject _panel;
        private TextMeshProUGUI _promptText;
        private RectTransform _buttonRoot;

        public void ShowSubmit(Action onSubmit)
        {
            EnsureBuilt();
            ClearButtons();
            _promptText.text = string.Empty;
            AddButton("Submit", () => onSubmit?.Invoke(), preferredWidth: 170f);
            SetVisible(true);
        }

        public void ShowCandidates(
            IReadOnlyList<string> candidates,
            Action<string> onCandidateSelected,
            Action onRedraw)
        {
            EnsureBuilt();
            ClearButtons();

            _promptText.text = string.Empty;
            if (candidates != null)
            {
                for (int i = 0; i < candidates.Count && i < 3; i++)
                {
                    string label = candidates[i]?.Trim() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(label))
                    {
                        continue;
                    }

                    AddButton(label, () => onCandidateSelected?.Invoke(label), preferredWidth: 210f);
                }
            }

            AddButton("Redraw", () => onRedraw?.Invoke(), preferredWidth: 170f);
            SetVisible(true);
        }

        public void Hide()
        {
            SetVisible(false);
        }

        private void EnsureBuilt()
        {
            if (_panel != null)
            {
                return;
            }

            GameObject canvasObject = new("Day1StimulusCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            DontDestroyOnLoad(canvasObject);
            _canvas = canvasObject.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 140;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            _panel = new GameObject("Day1StimulusPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(VerticalLayoutGroup));
            _panel.transform.SetParent(canvasObject.transform, false);

            var panelRect = (RectTransform)_panel.transform;
            panelRect.anchorMin = new Vector2(0.5f, 0f);
            panelRect.anchorMax = new Vector2(0.5f, 0f);
            panelRect.pivot = new Vector2(0.5f, 0f);
            panelRect.anchoredPosition = new Vector2(0f, 42f);
            panelRect.sizeDelta = new Vector2(900f, 132f);

            Image panelImage = _panel.GetComponent<Image>();
            panelImage.color = new Color(0.03f, 0.04f, 0.05f, 0.82f);
            panelImage.raycastTarget = true;

            var panelLayout = _panel.GetComponent<VerticalLayoutGroup>();
            panelLayout.padding = new RectOffset(22, 22, 18, 18);
            panelLayout.spacing = 12f;
            panelLayout.childAlignment = TextAnchor.MiddleCenter;
            panelLayout.childControlWidth = true;
            panelLayout.childControlHeight = true;
            panelLayout.childForceExpandWidth = true;
            panelLayout.childForceExpandHeight = false;

            _promptText = CreateText("Prompt", _panel.transform, 20f, FontStyles.Normal, TextAlignmentOptions.Center);
            var promptLayout = _promptText.gameObject.AddComponent<LayoutElement>();
            promptLayout.preferredHeight = 6f;
            promptLayout.flexibleHeight = 0f;

            GameObject row = new("Buttons", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            row.transform.SetParent(_panel.transform, false);
            _buttonRoot = (RectTransform)row.transform;
            var rowLayout = row.GetComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 12f;
            rowLayout.childAlignment = TextAnchor.MiddleCenter;
            rowLayout.childControlWidth = false;
            rowLayout.childControlHeight = true;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = false;
            var rowElement = row.AddComponent<LayoutElement>();
            rowElement.preferredHeight = 62f;
            rowElement.flexibleWidth = 1f;
        }

        private Button AddButton(string label, Action onClick, float preferredWidth)
        {
            GameObject buttonObject = new($"Day1Button_{label}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(LayoutElement));
            buttonObject.transform.SetParent(_buttonRoot, false);

            var rect = (RectTransform)buttonObject.transform;
            rect.sizeDelta = new Vector2(preferredWidth, 56f);

            Image image = buttonObject.GetComponent<Image>();
            image.color = new Color(0.12f, 0.15f, 0.18f, 0.96f);

            Button button = buttonObject.GetComponent<Button>();
            ColorBlock colors = button.colors;
            colors.normalColor = new Color(0.12f, 0.15f, 0.18f, 0.96f);
            colors.highlightedColor = new Color(0.22f, 0.29f, 0.34f, 1f);
            colors.pressedColor = new Color(0.08f, 0.12f, 0.15f, 1f);
            colors.selectedColor = colors.highlightedColor;
            button.colors = colors;
            button.onClick.AddListener(() => onClick?.Invoke());

            var element = buttonObject.GetComponent<LayoutElement>();
            element.preferredWidth = preferredWidth;
            element.preferredHeight = 56f;

            TextMeshProUGUI text = CreateText("Label", buttonObject.transform, 22f, FontStyles.Bold, TextAlignmentOptions.Center);
            text.text = label;
            text.enableAutoSizing = true;
            text.fontSizeMin = 12f;
            text.fontSizeMax = 22f;

            RectTransform textRect = text.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(12f, 4f);
            textRect.offsetMax = new Vector2(-12f, -4f);

            _buttons.Add(button);
            return button;
        }

        private TextMeshProUGUI CreateText(
            string name,
            Transform parent,
            float fontSize,
            FontStyles style,
            TextAlignmentOptions alignment)
        {
            GameObject textObject = new(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(parent, false);
            TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
            text.text = string.Empty;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.alignment = alignment;
            text.color = Color.white;
            text.raycastTarget = false;
            text.characterSpacing = 0f;
            return text;
        }

        private void ClearButtons()
        {
            foreach (Button button in _buttons)
            {
                if (button != null)
                {
                    button.onClick.RemoveAllListeners();
                    Destroy(button.gameObject);
                }
            }

            _buttons.Clear();
        }

        private void SetVisible(bool visible)
        {
            if (_panel != null)
            {
                _panel.SetActive(visible);
            }
        }

        private void OnDestroy()
        {
            ClearButtons();
            if (_canvas != null)
            {
                Destroy(_canvas.gameObject);
            }
        }
    }
}
