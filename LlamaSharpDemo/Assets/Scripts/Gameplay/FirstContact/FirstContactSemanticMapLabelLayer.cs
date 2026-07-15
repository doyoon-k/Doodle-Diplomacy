using System;
using System.Collections.Generic;
using DoodleDiplomacy.Localization;
using TMPro;
using UnityEngine;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    internal sealed class FirstContactSemanticMapLabelLayer
    {
        private readonly List<TextMeshProUGUI> _labels = new();
        private RectTransform _labelRoot;

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

        public void Render(
            FirstContactSemanticMapSnapshot snapshot,
            bool fullMode,
            FirstContactSemanticMapStyle configuredStyle)
        {
            FirstContactSemanticMapStyle style =
                FirstContactSemanticMapStyle.GetOrDefault(configuredStyle);
            FirstContactSemanticMapModeStyle mode = style.GetMode(fullMode);
            if (_labelRoot == null || snapshot == null || (!fullMode && !style.showMiniMapLabels))
            {
                HideLabelsFrom(0);
                return;
            }

            Rect rect = _labelRoot.rect;
            if (rect.width <= 1f || rect.height <= 1f)
            {
                Canvas.ForceUpdateCanvases();
                rect = _labelRoot.rect;
            }

            int labelIndex = 0;
            for (int i = 0; i < snapshot.Nodes.Count; i++)
            {
                FirstContactSemanticMapNode node = snapshot.Nodes[i];
                if (!ShouldShowLabel(node, fullMode))
                {
                    continue;
                }

                TextMeshProUGUI label = GetOrCreateLabel(labelIndex++);
                ConfigureLabelStyle(label, style, mode);
                UpdateLabel(label, node, fullMode, style);
                Vector2 point = FirstContactSemanticMapGraphic.MapToLocal(node.Position, rect, style);
                Vector2 offset = ResolveLabelOffset(node, style, mode);
                label.rectTransform.anchoredPosition = ClampLabelPosition(
                    point + offset,
                    rect,
                    label.rectTransform.sizeDelta,
                    style.labelEdgePadding);
            }

            HideLabelsFrom(labelIndex);
        }

        private static bool ShouldShowLabel(FirstContactSemanticMapNode node, bool fullMode)
        {
            if (node == null)
            {
                return false;
            }

            if (node.Kind == FirstContactSemanticMapNodeKind.Card &&
                !string.IsNullOrWhiteSpace(node.BootstrapCategoryId))
            {
                return node.IsActive;
            }

            if (fullMode)
            {
                return true;
            }

            return node.IsActive ||
                   node.Kind == FirstContactSemanticMapNodeKind.StableCluster ||
                   node.Kind == FirstContactSemanticMapNodeKind.BootstrapCategory;
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
                    $"Label_{labelIndex}",
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

        private static void ConfigureLabelStyle(
            TextMeshProUGUI label,
            FirstContactSemanticMapStyle style,
            FirstContactSemanticMapModeStyle mode)
        {
            label.fontStyle = style.labelFontStyle;
            label.alignment = style.labelAlignment;
            label.raycastTarget = false;
            label.richText = false;
            label.enableAutoSizing = style.labelAutoSizing;
            label.fontSizeMax = mode.labelFontSize;
            label.fontSizeMin = Mathf.Max(
                style.labelMinimumFontSize,
                label.fontSizeMax * style.labelMinimumSizeRatio);
            label.characterSpacing = 0f;
            label.overflowMode = style.labelOverflowMode;

            RectTransform rect = label.rectTransform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.sizeDelta = new Vector2(mode.labelWidth, mode.labelHeight);
        }

        private static void UpdateLabel(
            TextMeshProUGUI label,
            FirstContactSemanticMapNode node,
            bool fullMode,
            FirstContactSemanticMapStyle style)
        {
            string text = BuildLabelText(node, fullMode);
            if (!string.Equals(label.text, text, StringComparison.Ordinal))
            {
                label.text = text;
            }

            Color color = ResolveLabelColor(node, style);
            if (label.color != color)
            {
                label.color = color;
            }

            TMP_FontAsset localizedFont = L10n.CurrentFont;
            if (localizedFont != null && label.font != localizedFont)
            {
                label.font = localizedFont;
            }
        }

        private static string BuildLabelText(FirstContactSemanticMapNode node, bool fullMode)
        {
            if (node == null)
            {
                return string.Empty;
            }

            return node.Kind switch
            {
                FirstContactSemanticMapNodeKind.StableCluster => BuildStableClusterLabel(node),
                FirstContactSemanticMapNodeKind.BootstrapCategory => BuildBootstrapCategoryLabel(node),
                FirstContactSemanticMapNodeKind.Card => fullMode || node.IsActive ? node.Label : string.Empty,
                _ => node.Label
            };
        }

        private static string BuildStableClusterLabel(FirstContactSemanticMapNode node)
        {
            if (!string.IsNullOrWhiteSpace(node.SecondaryLabel))
            {
                return FirstContactTerminalLocalization.LocalizeMeaning(node.SecondaryLabel).ToUpperInvariant();
            }

            string fallback = string.IsNullOrWhiteSpace(node.Label)
                ? LocalizedGroupUnknownLabel()
                : $"[{node.Label.Trim()}]";
            return FirstContactTerminalLocalization.LocalizeMeaning(fallback).ToUpperInvariant();
        }

        private static string LocalizedGroupLabel()
        {
            return L10n.T("first_contact.terminal.semantic_map.group", "GROUP").ToUpperInvariant();
        }

        private static string LocalizedGroupUnknownLabel()
        {
            return L10n.T("first_contact.terminal.semantic_map.group_unknown", "[GROUP-??]").ToUpperInvariant();
        }

        private static string BuildBootstrapCategoryLabel(FirstContactSemanticMapNode node)
        {
            string label = string.IsNullOrWhiteSpace(node.Label)
                ? L10n.T("first_contact.terminal.line.category", "CATEGORY: {category}", L10n.Arg("category", string.Empty)).TrimEnd(':', ' ')
                : node.Label;
            if (node.RequiredTraceCount <= 0)
            {
                return label;
            }

            return $"{label} {Mathf.Max(0, node.TraceCount):00}/{Mathf.Max(1, node.RequiredTraceCount):00}";
        }

        private static Color ResolveLabelColor(
            FirstContactSemanticMapNode node,
            FirstContactSemanticMapStyle style)
        {
            if (node == null)
            {
                return style.fallbackLabelColor;
            }

            if (node.IsActive)
            {
                return style.activeLabelColor;
            }

            return node.Kind switch
            {
                FirstContactSemanticMapNodeKind.StableCluster => style.clusterLabelColor,
                FirstContactSemanticMapNodeKind.BootstrapCategory => node.IsBootstrapCategoryStable
                    ? style.stableBootstrapCategoryLabelColor
                    : style.bootstrapCategoryLabelColor,
                FirstContactSemanticMapNodeKind.Card => style.cardLabelColor,
                _ => style.fallbackLabelColor
            };
        }

        private static Vector2 ResolveLabelOffset(
            FirstContactSemanticMapNode node,
            FirstContactSemanticMapStyle style,
            FirstContactSemanticMapModeStyle mode)
        {
            float distance = style.labelOffset * mode.labelOffsetMultiplier;
            if (node.Kind == FirstContactSemanticMapNodeKind.Card &&
                !string.IsNullOrWhiteSpace(node.BootstrapCategoryId))
            {
                if (node.IsBootstrapDetached)
                {
                    return Vector2.Scale(style.bootstrapDetachedLabelOffset, Vector2.one * distance);
                }

                return node.IsActive
                    ? Vector2.Scale(style.bootstrapActiveLabelOffset, Vector2.one * distance)
                    : Vector2.Scale(style.bootstrapCardLabelOffset, Vector2.one * distance);
            }

            return node.Kind switch
            {
                FirstContactSemanticMapNodeKind.StableCluster => Vector2.Scale(
                    style.clusterLabelOffset,
                    Vector2.one * distance),
                FirstContactSemanticMapNodeKind.BootstrapCategory => Vector2.Scale(
                    style.categoryLabelOffset,
                    Vector2.one * distance),
                _ => Vector2.Scale(style.defaultLabelOffset, Vector2.one * distance)
            };
        }

        private static Vector2 ClampLabelPosition(
            Vector2 position,
            Rect rect,
            Vector2 size,
            float edgePadding)
        {
            float halfHeight = size.y * 0.5f;
            return new Vector2(
                Mathf.Clamp(position.x, rect.xMin + edgePadding, rect.xMax - size.x - edgePadding),
                Mathf.Clamp(position.y, rect.yMin + halfHeight + edgePadding, rect.yMax - halfHeight - edgePadding));
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
