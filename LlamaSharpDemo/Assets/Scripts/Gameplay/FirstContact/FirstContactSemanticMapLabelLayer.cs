using System;
using System.Collections.Generic;
using System.Text;
using DoodleDiplomacy.Localization;
using TMPro;
using UnityEngine;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    internal sealed class FirstContactSemanticMapLabelLayer
    {
        private readonly List<TextMeshProUGUI> _labels = new();
        private readonly StringBuilder _textBuilder = new(192);
        private readonly FirstContactResponseChannelPresentation _presentation = new();

        private RectTransform _labelRoot;

        public FirstContactResponseChannelPresentation Presentation => _presentation;

        public void SetRoot(RectTransform labelRoot)
        {
            if (_labelRoot == labelRoot)
            {
                return;
            }

            Hide();
            _labelRoot = labelRoot;
            _labels.Clear();
        }

        public FirstContactSemanticMapSnapshot Render(
            FirstContactSemanticMapSnapshot snapshot,
            bool fullMode,
            FirstContactSemanticMapStyle configuredStyle,
            bool resolveLayout)
        {
            FirstContactSemanticMapStyle style =
                FirstContactSemanticMapStyle.GetOrDefault(configuredStyle);
            int directoryRows = fullMode
                ? style.analyzerFullDirectoryRows
                : style.analyzerMiniDirectoryRows;
            _presentation.Build(snapshot, style.analyzerTraceRows, directoryRows);

            if (_labelRoot == null || snapshot == null || (!fullMode && !style.showMiniMapLabels))
            {
                HideLabelsFrom(0);
                return snapshot;
            }

            Rect rect = ResolveRootRect();
            if (rect.width <= 1f || rect.height <= 1f)
            {
                HideLabelsFrom(0);
                return snapshot;
            }

            FirstContactResponseChannelLayout layout =
                FirstContactResponseChannelLayout.Resolve(rect, fullMode, style);
            RenderLabels(layout, fullMode, style, directoryRows);
            return snapshot;
        }

        public FirstContactSemanticMapSnapshot ResolveLayout(
            FirstContactSemanticMapSnapshot snapshot,
            bool fullMode,
            FirstContactSemanticMapStyle configuredStyle)
        {
            FirstContactSemanticMapStyle style =
                FirstContactSemanticMapStyle.GetOrDefault(configuredStyle);
            _presentation.Build(
                snapshot,
                style.analyzerTraceRows,
                fullMode ? style.analyzerFullDirectoryRows : style.analyzerMiniDirectoryRows);
            return snapshot;
        }

        public void InvalidateLayout()
        {
            // The analyzer uses fixed panels. Re-rendering is enough for rect or locale changes.
        }

        private Rect ResolveRootRect()
        {
            Rect rect = _labelRoot.rect;
            if (rect.width > 1f && rect.height > 1f)
            {
                return rect;
            }

            Canvas.ForceUpdateCanvases();
            return _labelRoot.rect;
        }

        private void RenderLabels(
            FirstContactResponseChannelLayout layout,
            bool fullMode,
            FirstContactSemanticMapStyle style,
            int directoryRows)
        {
            FirstContactSemanticMapModeStyle mode = style.GetMode(fullMode);
            int labelIndex = 0;

            Rect scopeHeader = new(
                layout.Scope.xMin + layout.Gap,
                layout.Scope.yMax - layout.HeaderHeight,
                Mathf.Max(1f, layout.Scope.width * 0.66f - layout.Gap),
                layout.HeaderHeight);
            TextMeshProUGUI scopeLabel = GetOrCreateLabel(labelIndex++);
            ConfigureLabel(
                scopeLabel,
                BuildScopeHeader(),
                scopeHeader,
                style.activeLabelColor,
                mode.labelFontSize,
                TextAlignmentOptions.MidlineLeft,
                style);

            Rect scopeStatus = new(
                scopeHeader.xMax,
                scopeHeader.y,
                Mathf.Max(1f, layout.Scope.xMax - layout.Gap - scopeHeader.xMax),
                layout.HeaderHeight);
            TextMeshProUGUI statusLabel = GetOrCreateLabel(labelIndex++);
            ConfigureLabel(
                statusLabel,
                BuildScopeStatus(),
                scopeStatus,
                ResolveEntryColor(_presentation.ActiveEntry, style),
                mode.labelFontSize * 0.86f,
                TextAlignmentOptions.MidlineRight,
                style);

            int traceRows = Mathf.Max(1, style.analyzerTraceRows);
            for (int row = 0; row < _presentation.TraceNodes.Count && row < traceRows; row++)
            {
                FirstContactSemanticMapNode node = _presentation.TraceNodes[row];
                Rect traceRect = layout.GetTraceRowRect(row, traceRows);
                Rect traceLabelRect = new(
                    traceRect.xMin + layout.Gap,
                    traceRect.yMin,
                    Mathf.Max(1f, traceRect.width * 0.38f),
                    traceRect.height);
                TextMeshProUGUI traceLabel = GetOrCreateLabel(labelIndex++);
                ConfigureLabel(
                    traceLabel,
                    $"{row + 1:00}  {ResolveNodeLabel(node)}",
                    traceLabelRect,
                    node != null && node.IsActive ? style.activeLabelColor : style.cardLabelColor,
                    mode.labelFontSize * 0.82f,
                    TextAlignmentOptions.MidlineLeft,
                    style);
            }

            TextMeshProUGUI directoryHeader = GetOrCreateLabel(labelIndex++);
            ConfigureLabel(
                directoryHeader,
                BuildDirectoryHeader(),
                layout.GetDirectoryHeaderRect(),
                style.bootstrapCategoryLabelColor,
                mode.labelFontSize * 0.82f,
                TextAlignmentOptions.MidlineLeft,
                style);

            for (int row = 0; row < _presentation.VisibleDirectoryCount; row++)
            {
                int entryIndex = _presentation.VisibleDirectoryStart + row;
                FirstContactResponseChannelEntry entry =
                    _presentation.DirectoryEntries[entryIndex];
                TextMeshProUGUI directoryLabel = GetOrCreateLabel(labelIndex++);
                ConfigureLabel(
                    directoryLabel,
                    BuildDirectoryRow(entry),
                    layout.GetDirectoryRowRect(row, directoryRows),
                    ReferenceEquals(entry, _presentation.ActiveEntry)
                        ? style.activeLabelColor
                        : ResolveEntryColor(entry, style),
                    mode.labelFontSize * 0.78f,
                    TextAlignmentOptions.MidlineLeft,
                    style);
            }

            if (layout.HasRecentProbe)
            {
                Rect recentRect = new(
                    layout.RecentProbe.xMin + layout.Gap,
                    layout.RecentProbe.yMin + layout.Gap * 0.45f,
                    Mathf.Max(1f, layout.RecentProbe.width - layout.Gap * 2f),
                    Mathf.Max(1f, layout.RecentProbe.height - layout.Gap));
                TextMeshProUGUI recentLabel = GetOrCreateLabel(labelIndex++);
                ConfigureLabel(
                    recentLabel,
                    BuildRecentProbeText(),
                    recentRect,
                    _presentation.RecentProbe != null &&
                    !_presentation.RecentProbeMatchesActiveEntry
                        ? style.detachedActiveCardColor
                        : style.activeLabelColor,
                    mode.labelFontSize * 0.78f,
                    TextAlignmentOptions.TopLeft,
                    style,
                    multiline: true);
            }

            HideLabelsFrom(labelIndex);
        }

        private string BuildScopeHeader()
        {
            string channel = BuildEntryCode(_presentation.ActiveEntry);
            return L10n.T(
                "first_contact.terminal.response_analyzer.header",
                "[RESPONSE ANALYZER / {channel}]",
                L10n.Arg("channel", channel));
        }

        private string BuildScopeStatus()
        {
            FirstContactResponseChannelEntry entry = _presentation.ActiveEntry;
            if (entry == null)
            {
                return L10n.T("first_contact.terminal.fallback.unknown", "UNKNOWN");
            }

            string state = entry.IsStable
                ? L10n.T("first_contact.terminal.cluster.stable", "STABLE")
                : L10n.T("first_contact.terminal.cluster.forming", "FORMING");
            if (entry.Kind == FirstContactResponseChannelKind.Category && entry.RequiredTraceCount > 0)
            {
                return $"{entry.TraceCount:00}/{entry.RequiredTraceCount:00}  {state}";
            }

            return state;
        }

        private string BuildDirectoryHeader()
        {
            return L10n.T(
                "first_contact.terminal.response_analyzer.directory",
                "[CHANNEL DIRECTORY {page}/{pages}]",
                L10n.Arg("page", (_presentation.DirectoryPage + 1).ToString("00")),
                L10n.Arg("pages", _presentation.DirectoryPageCount.ToString("00")));
        }

        private static string BuildDirectoryRow(FirstContactResponseChannelEntry entry)
        {
            if (entry == null)
            {
                return string.Empty;
            }

            string prefix = entry.IsActive ? ">" : " ";
            string count = entry.Kind == FirstContactResponseChannelKind.Category &&
                           entry.RequiredTraceCount > 0
                ? $" {entry.TraceCount:00}/{entry.RequiredTraceCount:00}"
                : entry.IsStable
                    ? " *"
                    : string.Empty;
            return $"{prefix} {BuildEntryCode(entry)}  {ResolveEntryLabel(entry)}{count}";
        }

        private string BuildRecentProbeText()
        {
            FirstContactSemanticMapNode probe = _presentation.RecentProbe;
            _textBuilder.Clear();
            _textBuilder.Append(L10n.T(
                "first_contact.terminal.response_analyzer.recent_probe",
                "[RECENT PROBE]"));
            if (probe == null)
            {
                _textBuilder.Append('\n');
                _textBuilder.Append(L10n.T(
                    "first_contact.terminal.line.response_channel_waiting",
                    "RESPONSE CHANNEL: WAITING"));
                return _textBuilder.ToString();
            }

            _textBuilder.Append('\n');
            _textBuilder.Append(L10n.T(
                "first_contact.terminal.response_analyzer.probe",
                "PROBE: {probe}",
                L10n.Arg("probe", ResolveNodeLabel(probe))));
            _textBuilder.Append("  |  ");
            _textBuilder.Append(L10n.T(
                "first_contact.terminal.line.category",
                "CATEGORY: {category}",
                L10n.Arg("category", ResolveEntryLabel(_presentation.ActiveEntry))));
            _textBuilder.Append("  |  ");
            string status = _presentation.RecentProbeMatchesActiveEntry
                ? L10n.T("first_contact.terminal.response_analyzer.match", "MATCH")
                : L10n.T("first_contact.terminal.response_analyzer.no_match", "NO MATCH");
            _textBuilder.Append(L10n.T(
                "first_contact.terminal.response_analyzer.trace",
                "TRACE: {status}",
                L10n.Arg("status", status)));
            if (!_presentation.RecentProbeMatchesActiveEntry)
            {
                _textBuilder.Append("  ->  ");
                _textBuilder.Append(L10n.T(
                    "first_contact.terminal.response_analyzer.pattern",
                    "PATTERN: {pattern}",
                    L10n.Arg("pattern", ResolveEntryLabel(_presentation.RecentRouteEntry))));
            }

            return _textBuilder.ToString();
        }

        private static string BuildEntryCode(FirstContactResponseChannelEntry entry)
        {
            if (entry == null)
            {
                return "CH-??";
            }

            string prefix = entry.Kind == FirstContactResponseChannelKind.Category ? "CH" : "PT";
            return $"{prefix}-{Mathf.Max(1, entry.DisplayNumber):00}";
        }

        private static string ResolveEntryLabel(FirstContactResponseChannelEntry entry)
        {
            if (entry == null)
            {
                return L10n.T(
                    "first_contact.terminal.response_analyzer.pattern_unknown",
                    "[PATTERN-??]");
            }

            if (!string.IsNullOrWhiteSpace(entry.Label))
            {
                return entry.Label.Trim().ToUpperInvariant();
            }

            if (!string.IsNullOrWhiteSpace(entry.SecondaryLabel))
            {
                return FirstContactTerminalLocalization
                    .LocalizeMeaning(entry.SecondaryLabel)
                    .ToUpperInvariant();
            }

            return entry.Kind == FirstContactResponseChannelKind.Pattern
                ? L10n.T(
                    "first_contact.terminal.response_analyzer.pattern_unknown",
                    "[PATTERN-??]")
                : L10n.T("first_contact.terminal.fallback.unknown", "UNKNOWN");
        }

        private static string ResolveNodeLabel(FirstContactSemanticMapNode node)
        {
            return string.IsNullOrWhiteSpace(node?.Label)
                ? L10n.T("first_contact.terminal.fallback.unknown", "UNKNOWN")
                : node.Label.Trim().ToUpperInvariant();
        }

        private static Color ResolveEntryColor(
            FirstContactResponseChannelEntry entry,
            FirstContactSemanticMapStyle style)
        {
            if (entry == null)
            {
                return style.fallbackLabelColor;
            }

            if (entry.Kind == FirstContactResponseChannelKind.Pattern)
            {
                return style.analyzerPatternColor;
            }

            return entry.IsStable
                ? style.stableBootstrapCategoryLabelColor
                : style.bootstrapCategoryLabelColor;
        }

        private TextMeshProUGUI GetOrCreateLabel(int labelIndex)
        {
            while (_labels.Count <= labelIndex)
            {
                _labels.Add(null);
            }

            if (_labels[labelIndex] == null)
            {
                var labelObject = new GameObject(
                    $"AnalyzerLabel_{labelIndex:00}",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(TextMeshProUGUI));
                labelObject.transform.SetParent(_labelRoot, false);
                _labels[labelIndex] = labelObject.GetComponent<TextMeshProUGUI>();
            }

            TextMeshProUGUI label = _labels[labelIndex];
            if (!label.gameObject.activeSelf)
            {
                label.gameObject.SetActive(true);
            }

            return label;
        }

        private static void ConfigureLabel(
            TextMeshProUGUI label,
            string text,
            Rect rect,
            Color color,
            float fontSize,
            TextAlignmentOptions alignment,
            FirstContactSemanticMapStyle style,
            bool multiline = false)
        {
            if (!string.Equals(label.text, text, StringComparison.Ordinal))
            {
                label.text = text;
            }

            label.color = color;
            label.fontStyle = style.labelFontStyle;
            label.alignment = alignment;
            label.raycastTarget = false;
            label.richText = false;
            label.enableAutoSizing = style.labelAutoSizing;
            label.fontSize = fontSize;
            label.fontSizeMax = fontSize;
            label.fontSizeMin = Mathf.Max(
                style.labelMinimumFontSize,
                fontSize * style.labelMinimumSizeRatio);
            label.characterSpacing = 0f;
            label.overflowMode = style.labelOverflowMode;
            label.textWrappingMode = multiline
                ? TextWrappingModes.Normal
                : TextWrappingModes.NoWrap;
            TMP_FontAsset localizedFont = L10n.CurrentFont;
            if (localizedFont != null && label.font != localizedFont)
            {
                label.font = localizedFont;
            }

            RectTransform transform = label.rectTransform;
            transform.anchorMin = new Vector2(0.5f, 0.5f);
            transform.anchorMax = new Vector2(0.5f, 0.5f);
            transform.pivot = new Vector2(0.5f, 0.5f);
            transform.anchoredPosition = rect.center;
            transform.sizeDelta = rect.size;
        }

        public void Hide()
        {
            HideLabelsFrom(0);
        }

        private void HideLabelsFrom(int startIndex)
        {
            for (int i = Mathf.Max(0, startIndex); i < _labels.Count; i++)
            {
                TextMeshProUGUI label = _labels[i];
                if (label != null && label.gameObject.activeSelf)
                {
                    label.gameObject.SetActive(false);
                }
            }
        }
    }
}
