using System;
using System.Collections.Generic;
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
        public const string MeaningMapActionLabel = "MEANING MAP";
        public const string AnswerActionLabel = "SEND REPLY";

        private readonly TerminalDisplay _terminalDisplay;
        private readonly TerminalBrainwaveDisplay _brainwaveDisplay;
        private readonly FirstContactSemanticMapDisplay _semanticMapDisplay;
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
            _semanticMapDisplay = ResolveSemanticMapDisplay(terminalDisplay);
            _debugSettings = debugSettings;
        }

        public void Clear()
        {
            ClearVisualOverlays();
            _terminalDisplay?.Clear();
            _hasShownQuestionOnce = false;
        }

        public void ShowQuestion(AlienQuestion question, bool instant = false, string fallbackReason = null)
        {
            ClearVisualOverlays();
            _terminalDisplay?.ShowText(
                BuildQuestionText(question, fallbackReason),
                instant || _hasShownQuestionOnce);
            _hasShownQuestionOnce = true;
        }

        public void ShowQuestionChoices(
            AlienQuestion question,
            int selectedIndex,
            FirstContactSemanticMapSnapshot mapSnapshot,
            FirstContactSemanticSettings semanticSettings,
            bool instant = true,
            string fallbackReason = null,
            string initialProbeUnknownId = null)
        {
            _brainwaveDisplay?.Clear();
            ShowMiniMap(mapSnapshot, semanticSettings);
            string text = BuildQuestionText(question, fallbackReason);
            text += "\n\n[RESPONSE CHANNEL READY]\n\n";

            int choiceIndex = 0;
            string initialProbeId = FirstContactUnknownSlotDefinition.NormalizeUnknownId(initialProbeUnknownId);
            if (!string.IsNullOrWhiteSpace(initialProbeUnknownId))
            {
                text += BuildChoiceLine(choiceIndex, selectedIndex, BuildProbeActionLabel(initialProbeId));
                choiceIndex++;
            }
            else
            {
                if (question != null)
                {
                    for (int i = 0; i < question.UnknownSlots.Count; i++)
                    {
                        string id = question.UnknownSlots[i].Id;
                        text += BuildChoiceLine(choiceIndex, selectedIndex, BuildProbeActionLabel(id));
                        choiceIndex++;
                    }
                }

                text += BuildChoiceLine(choiceIndex, selectedIndex, MeaningMapActionLabel);
                choiceIndex++;
                text += BuildChoiceLine(choiceIndex, selectedIndex, AnswerActionLabel);
                choiceIndex++;
            }

            text += "\nINPUT: 1-" + Mathf.Max(1, choiceIndex) + " / ENTER";

            _terminalDisplay?.ShowText(text, instant);
            _hasShownQuestionOnce = true;
        }

        public void ShowSemanticMapChoices(
            AlienQuestion question,
            int selectedIndex,
            FirstContactSemanticMapSnapshot mapSnapshot,
            FirstContactSemanticSettings semanticSettings,
            bool instant = true)
        {
            _brainwaveDisplay?.Clear();
            ShowFullMap(mapSnapshot, semanticSettings);

            string text =
                "[MEANING MAP]\n\n" +
                "DRAWING REFERENCE\n\n";

            int choiceIndex = 0;
            text += BuildChoiceLine(choiceIndex, selectedIndex, "BACK TO REQUEST");
            choiceIndex++;

            if (question != null)
            {
                for (int i = 0; i < question.UnknownSlots.Count; i++)
                {
                    string id = question.UnknownSlots[i].Id;
                    text += BuildChoiceLine(choiceIndex, selectedIndex, BuildProbeActionLabel(id));
                    choiceIndex++;
                }
            }

            text += BuildChoiceLine(choiceIndex, selectedIndex, AnswerActionLabel);
            text += "\nINPUT: 1-" + Mathf.Max(1, choiceIndex + 1) + " / ENTER";
            _terminalDisplay?.ShowText(text, instant);
            _hasShownQuestionOnce = true;
        }

        public void ShowQuestionChoiceEcho(AlienQuestion question, string choiceLabel, bool instant = true)
        {
            ClearVisualOverlays();
            string text = BuildQuestionText(question);
            text += "\n\n[INPUT ACCEPTED]\n\n";
            text += $"> {choiceLabel ?? string.Empty}";
            _terminalDisplay?.ShowText(text, instant);
            _hasShownQuestionOnce = true;
        }

        public void ShowTabletLinkOpen(
            AlienQuestion question,
            FirstContactCardSource source,
            string unknownId,
            bool instant = true)
        {
            ClearVisualOverlays();
            string text = BuildQuestionText(question);
            text += "\n\n[TABLET LINK OPEN]\n\n";
            text += $"CHANNEL: {BuildChannelLabel(source, unknownId)}\n";
            text += "DRAW ONE OBJECT\n\n";
            text += "SUBMIT: ENTER";
            _terminalDisplay?.ShowText(text, instant);
            _hasShownQuestionOnce = true;
        }

        public void ShowTabletImageReceived(
            FirstContactCardSource source,
            string unknownId,
            bool instant = true)
        {
            ClearVisualOverlays();
            string text =
                "[TABLET IMAGE RECEIVED]\n\n" +
                $"CHANNEL: {BuildChannelLabel(source, unknownId)}\n\n" +
                "ANALYZING VISUAL SIGNAL...";
            _terminalDisplay?.ShowText(text, instant);
        }

        public void ShowInputRejected(string reason, int selectedIndex, bool instant = true)
        {
            ClearVisualOverlays();
            string text =
                "[INPUT REJECTED]\n\n" +
                $"{NormalizeTerminalLine(reason, "DRAW ONE OBJECT")}\n\n" +
                BuildChoiceLine(0, selectedIndex, "REDRAW") +
                "\nINPUT: 1 / ENTER";
            _terminalDisplay?.ShowText(text, instant);
        }

        public void ShowLabelReview(
            FirstContactCardSource source,
            string unknownId,
            string label,
            int selectedIndex,
            bool instant = true)
        {
            ClearVisualOverlays();
            string text =
                "[VISUAL LABEL]\n\n" +
                $"{NormalizeTerminalLine(label, "UNKNOWN").ToUpperInvariant()}\n\n" +
                $"CHANNEL: {BuildChannelLabel(source, unknownId)}\n\n" +
                BuildChoiceLine(0, selectedIndex, "ACCEPT") +
                BuildChoiceLine(1, selectedIndex, "REDRAW") +
                "\nINPUT: 1-2 / ENTER";
            _terminalDisplay?.ShowText(text, instant);
        }

        public void ShowDecodeTarget(AlienQuestion question, string unknownId)
        {
            ClearVisualOverlays();
            string line = question?.BuildDisplayLine() ?? string.Empty;
            string id = FirstContactUnknownSlotDefinition.NormalizeUnknownId(unknownId);
            string text = $"[PROBE SAMPLE]\n\n{line}\n\n<{id}>";
            _terminalDisplay?.ShowText(text);
        }

        public void ShowCard(SemanticCardRecord card, SemanticClusterRecord cluster, bool instant = false)
        {
            if (card == null)
            {
                return;
            }

            _semanticMapDisplay?.Clear();
            string clusterLine = cluster != null ? $"[{cluster.Id}]" : "[NO CLUSTER]";
            string text =
                $"[SIGNAL CAPTURE]\n\n" +
                $"VISUAL READ: {GetDisplayLabel(card)}\n\n" +
                $"{clusterLine}";

            if (_debugSettings != null && _debugSettings.showScoresOnTerminal && !string.IsNullOrWhiteSpace(card.TargetUnknownId))
            {
                text += $"\nTARGET: {card.TargetUnknownId}";
            }

            text += BuildContinuePrompt();
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

            ClearVisualOverlays();
            string members = BuildMemberLine(cluster);
            string text =
                $"[{cluster.Id}]\n" +
                $"{members}\n\n" +
                $"{cluster.DisplayName}";
            _terminalDisplay?.ShowText(text);
        }

        public void ShowSemanticAnalysis(
            SemanticCardRecord card,
            SemanticClusterRecord cluster,
            IReadOnlyList<FirstContactSlotScore> slotScores,
            FirstContactResolutionResult? activeResolution,
            FirstContactSemanticMapSnapshot mapSnapshot,
            FirstContactSemanticSettings semanticSettings,
            bool instant = true)
        {
            _semanticMapDisplay?.Clear();
            string label = card != null ? GetDisplayLabel(card) : string.Empty;
            bool isProbeResult = activeResolution.HasValue && activeResolution.Value.Slot != null;
            string text = isProbeResult
                ? "[PROBE RESULT]\n\n"
                : "[REPLY SIGNAL]\n\n";
            text += $"VISUAL READ: {NormalizeTerminalLine(label, "UNKNOWN").ToUpperInvariant()}\n";

            if (isProbeResult)
            {
                FirstContactResolutionResult result = activeResolution.Value;
                text += $"SIGNAL MATCH: {BuildSignalMatchLabel(result.Score, semanticSettings)}\n";
                text += BuildTokenUpdateBlock(result);
            }
            else
            {
                text += "SIGNAL MATCH: STORED\n";
            }

            if (_debugSettings != null && _debugSettings.showScoresOnTerminal && cluster != null)
            {
                string stability = cluster.IsStable ? "STABLE" : "FORMING";
                text += $"CLUSTER: {cluster.Id} / {stability}\n";
            }

            if (_debugSettings != null && _debugSettings.showScoresOnTerminal)
            {
                text += BuildSlotScoreBlock(slotScores, semanticSettings);
            }

            text += BuildContinuePrompt();

            _terminalDisplay?.ShowText(text, instant);
            if (_brainwaveDisplay != null && card != null && card.WaveformProfile.IsValid)
            {
                _brainwaveDisplay.PlayLocked(
                    ReactionTier.Moderate,
                    card.Label,
                    Mathf.Max(1, card.TurnIndex + 1),
                    1,
                    card.WaveformProfile);
            }
        }

        public void ShowAnswerTransmitted(SemanticCardRecord card)
        {
            ClearVisualOverlays();
            string label = card != null ? GetDisplayLabel(card) : string.Empty;
            _terminalDisplay?.ShowText($"[REPLY SENT]\n\n{label}");
        }

        private string BuildQuestionText(AlienQuestion question, string fallbackReason = null)
        {
            string text = $"[ALIEN REQUEST]\n\n{question?.BuildDisplayLine() ?? string.Empty}";
            if (_debugSettings != null && _debugSettings.showQuestionFallbackReason && !string.IsNullOrWhiteSpace(fallbackReason))
            {
                text += $"\n\n[FALLBACK]\n{fallbackReason}";
            }

            return text;
        }

        private static string BuildChoiceLine(int choiceIndex, int selectedIndex, string label)
        {
            string marker = choiceIndex == selectedIndex ? ">" : " ";
            return $"{marker} {choiceIndex + 1}. {label}\n";
        }

        private static string BuildContinuePrompt()
        {
            return "\n\n" + BuildChoiceLine(0, 0, "CONTINUE") + "INPUT: 1 / ENTER";
        }

        private static string BuildChannelLabel(FirstContactCardSource source, string unknownId)
        {
            if (source == FirstContactCardSource.Answer)
            {
                return AnswerActionLabel;
            }

            return BuildProbeActionLabel(unknownId);
        }

        public static string BuildProbeActionLabel(string unknownId)
        {
            string id = FirstContactUnknownSlotDefinition.NormalizeUnknownId(unknownId);
            return string.IsNullOrWhiteSpace(id) ? "PROBE UNKNOWN" : $"PROBE {id}";
        }

        private static string NormalizeTerminalLine(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
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

        private static string BuildTokenUpdateBlock(FirstContactResolutionResult result)
        {
            if (result.Slot == null)
            {
                return string.Empty;
            }

            string text =
                "\nTRANSLATION UPDATE\n" +
                $"TOKEN: {result.Slot.Id}\n";

            if (!result.Changed)
            {
                return text + "NO CHANGE\n";
            }

            FirstContactStageTexts stageTexts = result.Slot.Definition?.stageTexts ?? new FirstContactStageTexts();
            string before = stageTexts.GetDisplayText(result.PreviousStage, result.Slot.Id);
            string after = stageTexts.GetDisplayText(result.NewStage, result.Slot.Id);
            return text + $"{before} -> {after}\n";
        }

        private string BuildSlotScoreBlock(
            IReadOnlyList<FirstContactSlotScore> slotScores,
            FirstContactSemanticSettings settings)
        {
            if (slotScores == null || slotScores.Count == 0)
            {
                return string.Empty;
            }

            string text = "\n[TOKEN SIGNALS]\n";
            for (int i = 0; i < slotScores.Count; i++)
            {
                FirstContactSlotScore score = slotScores[i];
                if (score.Slot == null)
                {
                    continue;
                }

                string marker = score.IsActive ? ">" : " ";
                text += $"{marker} {score.Slot.Id}: {BuildSignalMatchLabel(score.Score, settings)}";
                if (_debugSettings != null && _debugSettings.showScoresOnTerminal)
                {
                    text += $" ({score.Score:0.000})";
                }

                text += "\n";
            }

            return text;
        }

        private static string BuildSignalMatchLabel(float score, FirstContactSemanticSettings settings)
        {
            settings ??= ScriptableObject.CreateInstance<FirstContactSemanticSettings>();
            if (score >= settings.solvedThreshold)
            {
                return "LOCK";
            }

            if (score >= settings.partialThreshold)
            {
                return "STRONG";
            }

            if (score >= settings.hintThreshold)
            {
                return "FAINT";
            }

            if (score >= Mathf.Min(0.28f, settings.hintThreshold))
            {
                return "WEAK";
            }

            return "NONE";
        }

        private void ShowMiniMap(
            FirstContactSemanticMapSnapshot mapSnapshot,
            FirstContactSemanticSettings semanticSettings)
        {
            if (semanticSettings != null && !semanticSettings.showSemanticMapFeedback)
            {
                _semanticMapDisplay?.Clear();
                return;
            }

            if (mapSnapshot == null || mapSnapshot.Nodes.Count == 0)
            {
                _semanticMapDisplay?.Clear();
                return;
            }

            _semanticMapDisplay?.ShowMiniMap(mapSnapshot);
        }

        private void ShowFullMap(
            FirstContactSemanticMapSnapshot mapSnapshot,
            FirstContactSemanticSettings semanticSettings)
        {
            if (semanticSettings != null && !semanticSettings.showSemanticMapFeedback)
            {
                _semanticMapDisplay?.Clear();
                return;
            }

            if (mapSnapshot == null || mapSnapshot.Nodes.Count == 0)
            {
                _semanticMapDisplay?.Clear();
                return;
            }

            _semanticMapDisplay?.ShowFullMap(mapSnapshot);
        }

        private void ClearVisualOverlays()
        {
            _brainwaveDisplay?.Clear();
            _semanticMapDisplay?.Clear();
        }

        private static FirstContactSemanticMapDisplay ResolveSemanticMapDisplay(TerminalDisplay terminalDisplay)
        {
            if (terminalDisplay == null)
            {
                return null;
            }

            FirstContactSemanticMapDisplay display =
                terminalDisplay.GetComponent<FirstContactSemanticMapDisplay>() ??
                terminalDisplay.GetComponentInChildren<FirstContactSemanticMapDisplay>(true);
            return display != null
                ? display
                : terminalDisplay.gameObject.AddComponent<FirstContactSemanticMapDisplay>();
        }
    }

    public interface IFirstContactActionPresenter
    {
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
