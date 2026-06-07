using System;
using DoodleDiplomacy.Core;
using DoodleDiplomacy.Devices;
using DoodleDiplomacy.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    public sealed class FirstContactTerminalPresenter
    {
        private readonly TerminalDisplay _terminalDisplay;
        private readonly TerminalBrainwaveDisplay _brainwaveDisplay;
        private readonly FirstContactDebugSettings _debugSettings;
        private bool _hasShownQuestionOnce;

        public FirstContactTerminalPresenter(
            TerminalDisplay terminalDisplay,
            FirstContactDebugSettings debugSettings)
        {
            _terminalDisplay = terminalDisplay;
            _brainwaveDisplay = terminalDisplay != null
                ? terminalDisplay.GetComponent<TerminalBrainwaveDisplay>() ??
                  terminalDisplay.GetComponentInChildren<TerminalBrainwaveDisplay>(true)
                : null;
            _debugSettings = debugSettings;
        }

        public void Clear()
        {
            _brainwaveDisplay?.Clear();
            _terminalDisplay?.Clear();
            _hasShownQuestionOnce = false;
        }

        public void ShowQuestion(AlienQuestion question, bool instant = false, string fallbackReason = null)
        {
            _brainwaveDisplay?.Clear();
            string text = $"[ALIEN REQUEST]\n\n{question?.BuildDisplayLine() ?? string.Empty}";
            if (_debugSettings != null && _debugSettings.showQuestionFallbackReason && !string.IsNullOrWhiteSpace(fallbackReason))
            {
                text += $"\n\n[FALLBACK]\n{fallbackReason}";
            }

            _terminalDisplay?.ShowText(text, instant || _hasShownQuestionOnce);
            _hasShownQuestionOnce = true;
        }

        public void ShowDecodeTarget(AlienQuestion question, string unknownId)
        {
            _brainwaveDisplay?.Clear();
            string line = question?.BuildDisplayLine() ?? string.Empty;
            string id = FirstContactUnknownSlotDefinition.NormalizeUnknownId(unknownId);
            string text = $"[DECODE SAMPLE]\n\n{line}\n\n<{id}>";
            _terminalDisplay?.ShowText(text);
        }

        public void ShowCard(SemanticCardRecord card, SemanticClusterRecord cluster, bool instant = false)
        {
            if (card == null)
            {
                return;
            }

            string clusterLine = cluster != null ? $"[{cluster.Id}]" : "[NO CLUSTER]";
            string text =
                $"[SEMANTIC CARD]\n\n" +
                $"{GetDisplayLabel(card)}\n\n" +
                $"{clusterLine}";

            if (_debugSettings != null && _debugSettings.showScoresOnTerminal && !string.IsNullOrWhiteSpace(card.TargetUnknownId))
            {
                text += $"\nTARGET: {card.TargetUnknownId}";
            }

            _terminalDisplay?.ShowText(text, instant);
            if (_brainwaveDisplay != null && card.WaveformProfile.IsValid)
            {
                _brainwaveDisplay.PlayLocked(
                    ReactionTier.Moderate,
                    card.Label,
                    Mathf.Max(1, card.TurnIndex + 1),
                    1,
                    card.WaveformProfile);
            }
        }

        public void ShowCluster(SemanticClusterRecord cluster)
        {
            if (cluster == null)
            {
                return;
            }

            _brainwaveDisplay?.Clear();
            string members = BuildMemberLine(cluster);
            string text =
                $"[{cluster.Id}]\n" +
                $"{members}\n\n" +
                $"{cluster.DisplayName}";
            _terminalDisplay?.ShowText(text);
        }

        public void ShowAnswerTransmitted(SemanticCardRecord card)
        {
            _brainwaveDisplay?.Clear();
            string label = card != null ? GetDisplayLabel(card) : string.Empty;
            _terminalDisplay?.ShowText($"[ANSWER TRANSMITTED]\n\n{label}");
        }

        private static string GetDisplayLabel(SemanticCardRecord card)
        {
            if (card == null)
            {
                return string.Empty;
            }

            return string.IsNullOrWhiteSpace(card.LocalizedLabel)
                ? (card.Label ?? string.Empty).ToUpperInvariant()
                : card.LocalizedLabel.Trim().ToUpperInvariant();
        }

        private static string BuildMemberLine(SemanticClusterRecord cluster)
        {
            if (cluster == null || cluster.Members.Count == 0)
            {
                return string.Empty;
            }

            int start = Mathf.Max(0, cluster.Members.Count - 4);
            string line = string.Empty;
            for (int i = start; i < cluster.Members.Count; i++)
            {
                string label = cluster.Members[i].Label ?? string.Empty;
                if (string.IsNullOrWhiteSpace(label))
                {
                    continue;
                }

                if (line.Length > 0)
                {
                    line += " / ";
                }

                line += label.ToUpperInvariant();
            }

            return line;
        }
    }

    public interface IFirstContactActionPresenter
    {
        void ShowQuestionActions(AlienQuestion question, Action<string> onDecode, Action onAnswer);
        void ShowSubmit(string prompt, Action onSubmit);
        void ShowConfirmation(string prompt, string label, Action onConfirm, Action onRedraw);
        void Hide();
    }

    [DisallowMultipleComponent]
    public sealed class FirstContactActionButtonPanel : MonoBehaviour, IFirstContactActionPresenter
    {
        private Canvas _canvas;
        private GameObject _panel;
        private TextMeshProUGUI _promptText;
        private RectTransform _buttonRoot;

        public void ShowQuestionActions(AlienQuestion question, Action<string> onDecode, Action onAnswer)
        {
            EnsureBuilt();
            ClearButtons();
            _promptText.text = string.Empty;

            if (question != null)
            {
                for (int i = 0; i < question.UnknownSlots.Count; i++)
                {
                    string id = question.UnknownSlots[i].Id;
                    AddButton($"DECODE {id}", () => onDecode?.Invoke(id), 220f);
                }
            }

            AddButton(L10n.T("ui.first_contact.transmit_answer", "TRANSMIT ANSWER"), () => onAnswer?.Invoke(), 260f);
            SetVisible(true);
        }

        public void ShowSubmit(string prompt, Action onSubmit)
        {
            EnsureBuilt();
            ClearButtons();
            _promptText.text = prompt ?? string.Empty;
            AddButton(L10n.T("ui.day1.submit", "Submit"), () => onSubmit?.Invoke(), 180f);
            SetVisible(true);
        }

        public void ShowConfirmation(string prompt, string label, Action onConfirm, Action onRedraw)
        {
            EnsureBuilt();
            ClearButtons();
            _promptText.text = string.IsNullOrWhiteSpace(label)
                ? prompt ?? string.Empty
                : $"{prompt}\n{label.ToUpperInvariant()}";
            AddButton(L10n.T("ui.day1.confirm", "Confirm"), () => onConfirm?.Invoke(), 180f);
            AddButton(L10n.T("ui.day1.redraw", "Redraw"), () => onRedraw?.Invoke(), 180f);
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

            GameObject canvasObject = new("FirstContactActionCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            DontDestroyOnLoad(canvasObject);
            _canvas = canvasObject.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 145;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            _panel = new GameObject("FirstContactActionPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(VerticalLayoutGroup));
            _panel.transform.SetParent(canvasObject.transform, false);

            RectTransform panelRect = (RectTransform)_panel.transform;
            panelRect.anchorMin = new Vector2(0.5f, 0f);
            panelRect.anchorMax = new Vector2(0.5f, 0f);
            panelRect.pivot = new Vector2(0.5f, 0f);
            panelRect.anchoredPosition = new Vector2(0f, 42f);
            panelRect.sizeDelta = new Vector2(1120f, 150f);

            Image panelImage = _panel.GetComponent<Image>();
            panelImage.color = new Color(0.03f, 0.04f, 0.05f, 0.86f);
            panelImage.raycastTarget = true;

            VerticalLayoutGroup panelLayout = _panel.GetComponent<VerticalLayoutGroup>();
            panelLayout.padding = new RectOffset(22, 22, 16, 18);
            panelLayout.spacing = 10f;
            panelLayout.childAlignment = TextAnchor.MiddleCenter;
            panelLayout.childControlWidth = true;
            panelLayout.childControlHeight = true;
            panelLayout.childForceExpandWidth = true;
            panelLayout.childForceExpandHeight = false;

            _promptText = CreateText("Prompt", _panel.transform, 20f, FontStyles.Normal);
            LayoutElement promptLayout = _promptText.gameObject.AddComponent<LayoutElement>();
            promptLayout.preferredHeight = 38f;
            promptLayout.flexibleHeight = 0f;

            GameObject row = new("Buttons", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            row.transform.SetParent(_panel.transform, false);
            _buttonRoot = (RectTransform)row.transform;
            HorizontalLayoutGroup rowLayout = row.GetComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 12f;
            rowLayout.childAlignment = TextAnchor.MiddleCenter;
            rowLayout.childControlWidth = false;
            rowLayout.childControlHeight = true;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = false;
            LayoutElement rowElement = row.AddComponent<LayoutElement>();
            rowElement.preferredHeight = 62f;
            rowElement.flexibleWidth = 1f;
        }

        private Button AddButton(string label, Action onClick, float preferredWidth)
        {
            GameObject buttonObject = new($"FirstContactButton_{label}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(LayoutElement));
            buttonObject.transform.SetParent(_buttonRoot, false);

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

            LayoutElement element = buttonObject.GetComponent<LayoutElement>();
            element.preferredWidth = preferredWidth;
            element.preferredHeight = 56f;

            TextMeshProUGUI text = CreateText("Label", buttonObject.transform, 20f, FontStyles.Bold);
            text.text = label;
            text.enableAutoSizing = true;
            text.fontSizeMin = 11f;
            text.fontSizeMax = 20f;
            RectTransform textRect = text.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(12f, 4f);
            textRect.offsetMax = new Vector2(-12f, -4f);
            return button;
        }

        private static TextMeshProUGUI CreateText(string name, Transform parent, float fontSize, FontStyles style)
        {
            GameObject textObject = new(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(parent, false);
            TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
            text.text = string.Empty;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
            text.raycastTarget = false;
            text.characterSpacing = 0f;
            return text;
        }

        private void ClearButtons()
        {
            if (_buttonRoot == null)
            {
                return;
            }

            for (int i = _buttonRoot.childCount - 1; i >= 0; i--)
            {
                Destroy(_buttonRoot.GetChild(i).gameObject);
            }
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
            if (_canvas != null)
            {
                Destroy(_canvas.gameObject);
            }
        }
    }
}
