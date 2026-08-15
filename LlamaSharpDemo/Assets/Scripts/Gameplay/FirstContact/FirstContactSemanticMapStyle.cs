using System;
using TMPro;
using UnityEngine;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    [CreateAssetMenu(
        fileName = "FirstContactSemanticMapStyle",
        menuName = "DoodleDiplomacy/First Contact/Semantic Map Style")]
    public sealed class FirstContactSemanticMapStyle : ScriptableObject
    {
        [Header("Display Layout")]
        public FirstContactSemanticMapModeStyle miniMap = FirstContactSemanticMapModeStyle.CreateMiniMap();
        public FirstContactSemanticMapModeStyle fullMap = FirstContactSemanticMapModeStyle.CreateFullMap();

        [Header("Map Coordinates")]
        [Min(0f)] public float minimumMapPadding = 8f;
        [Range(0f, 0.25f)] public float mapHorizontalPaddingRatio = 0.045f;
        [Range(0f, 0.25f)] public float mapVerticalPaddingRatio = 0.08f;

        [Header("Palette")]
        public Color backgroundColor = new(0f, 0.035f, 0.012f, 0.72f);
        public Color gridColor = new(0.08f, 0.38f, 0.15f, 0.3f);
        public Color cardColor = new(0.36f, 0.92f, 1f, 0.96f);
        public Color activeCardColor = new(1f, 0.95f, 0.45f, 1f);
        public Color detachedActiveCardColor = new(0.52f, 0.84f, 1f, 0.96f);
        public Color detachedCardColor = new(0.3f, 0.66f, 0.92f, 0.66f);
        public Color clusterColor = new(0.72f, 0.5f, 1f, 0.78f);
        public Color bootstrapCategoryColor = new(0.78f, 1f, 0.42f, 0.82f);
        public Color stableBootstrapCategoryColor = new(1f, 0.86f, 0.28f, 0.94f);
        public Color fallbackNodeColor = Color.white;
        public Color weakLinkColor = new(0.22f, 0.8f, 0.42f, 0.36f);
        public Color strongLinkColor = new(0.8f, 1f, 0.55f, 0.88f);
        public Color confirmedFormationLinkColor = new(0.72f, 1f, 0.72f, 0.82f);
        public Color rejectedFormationLinkColor = new(0.44f, 0.72f, 0.95f, 1f);
        public Color candidateFormationLinkColor = new(0.46f, 0.96f, 1f, 1f);

        [Header("Link Opacity")]
        [Range(0f, 1f)] public float bootstrapSignalMinimumAlpha = 0.08f;
        [Range(0f, 1f)] public float bootstrapSignalMaximumAlpha = 0.58f;
        [Range(0f, 1f)] public float activeLinkMinimumAlpha = 0.95f;
        [Range(0f, 1f)] public float confirmedFormationMinimumAlpha = 0.88f;
        [Range(0f, 1f)] public float rejectedFormationMinimumAlpha = 0.08f;
        [Range(0f, 1f)] public float rejectedFormationMaximumAlpha = 0.34f;
        [Range(0f, 1f)] public float candidateFormationMinimumAlpha = 0.18f;
        [Range(0f, 1f)] public float candidateFormationMaximumAlpha = 0.76f;

        [Header("Category Field")]
        [Range(0f, 1f)] public float categoryFieldMinimumFillAlpha = 0.055f;
        [Range(0f, 1f)] public float categoryFieldFillAlpha = 0.13f;
        [Range(0f, 1f)] public float stableCategoryFieldFillAlpha = 0.18f;
        [Range(0f, 1f)] public float categoryFieldMinimumRingAlpha = 0.32f;
        [Range(0f, 1f)] public float categoryFieldRingAlpha = 0.62f;
        [Range(0f, 1f)] public float stableCategoryFieldRingAlpha = 0.82f;
        [Range(0f, 1f)] public float categoryFieldInnerRingMaximumAlpha = 0.36f;
        [Range(0f, 1f)] public float categoryFieldInnerRingAlphaMultiplier = 0.55f;
        [Range(0.1f, 1f)] public float categoryFieldInnerRingRadiusMultiplier = 0.86f;

        [Header("Node and Pulse")]
        [Range(0f, 1f)] public float stableClusterFillAlpha = 0.16f;
        [Range(0f, 1f)] public float clusterPulseAlpha = 0.55f;
        [Range(0f, 1f)] public float clusterLockPulseAlpha = 0.32f;
        [Min(0f)] public float pulseVisibilityThreshold = 0.001f;
        [Min(0f)] public float clusterLockPulseThreshold = 1.05f;
        [Min(0f)] public float activeNodeBaseScale = 1.6f;
        [Min(0f)] public float activeNodePulseScale = 0.35f;
        [Min(0f)] public float clusterPulseRingBaseScale = 1.08f;
        [Min(0f)] public float clusterPulseRingPulseScale = 0.22f;
        [Min(0f)] public float clusterLockRingBaseScale = 1.62f;
        [Min(0f)] public float clusterLockRingPulseScale = 0.18f;
        [Range(0f, 1f)] public float nodeGlowAlpha = 0.28f;
        [Range(0f, 1f)] public float nodePulseAlpha = 0.98f;
        [Range(0f, 1f)] public float nodeOuterPulseAlphaMultiplier = 0.58f;
        [Min(0f)] public float nodeGlowBaseScale = 1.28f;
        [Min(0f)] public float nodeGlowPulseScale = 0.16f;
        [Min(0f)] public float nodePulseRingBaseScale = 1.55f;
        [Min(0f)] public float nodePulseRingPulseScale = 0.42f;
        [Min(0f)] public float nodeOuterPulseRingBaseScale = 2.25f;
        [Min(0f)] public float nodeOuterPulseRingPulseScale = 0.78f;
        [Min(0f)] public float categoryTraceRingRadiusMultiplier = 1.75f;
        [Min(0f)] public float categoryTraceRingMinimumThickness = 1.4f;
        [Min(0f)] public float categoryTraceRingThicknessMultiplier = 0.14f;
        [Min(0f)] public float activeNodeRingScale = 1.65f;
        [Min(0f)] public float activeNodeRingMinimumThickness = 2.4f;
        [Min(0f)] public float activeNodeRingThicknessMultiplier = 0.22f;
        [Min(0f)] public float nodePulseRingMinimumThickness = 2.8f;
        [Min(0f)] public float nodePulseRingThicknessMultiplier = 0.28f;
        [Min(0f)] public float nodeOuterPulseRingMinimumThickness = 1.9f;
        [Min(0f)] public float nodeOuterPulseRingThicknessMultiplier = 0.17f;

        [Header("Labels")]
        public bool showMiniMapLabels = true;
        [Tooltip("노드의 시각적 외곽과 아래쪽 라벨 사이의 기본 간격입니다.")]
        [Min(0f)] public float labelOffset = 12f;
        public FontStyles labelFontStyle = FontStyles.Bold;
        public TextAlignmentOptions labelAlignment = TextAlignmentOptions.Center;
        public bool labelAutoSizing = true;
        [Range(0.1f, 1f)] public float labelMinimumSizeRatio = 0.65f;
        [Min(1f)] public float labelMinimumFontSize = 5f;
        public TextOverflowModes labelOverflowMode = TextOverflowModes.Ellipsis;
        [Min(0f)] public float labelEdgePadding = 2f;
        [Min(0f)] public float labelPreferredSizePadding = 4f;
        public Color activeLabelColor = new(0.95f, 1f, 0.68f, 0.98f);
        public Color clusterLabelColor = new(0.85f, 0.7f, 1f, 0.9f);
        public Color stableBootstrapCategoryLabelColor = new(0.98f, 0.9f, 0.48f, 0.96f);
        public Color bootstrapCategoryLabelColor = new(0.72f, 0.95f, 0.52f, 0.86f);
        public Color cardLabelColor = new(0.45f, 0.92f, 1f, 0.82f);
        public Color fallbackLabelColor = Color.white;
        [HideInInspector]
        public Vector2 bootstrapDetachedLabelOffset = new(-4.2f, -0.25f);
        [HideInInspector]
        public Vector2 bootstrapActiveLabelOffset = new(-2.8f, 1.05f);
        [HideInInspector]
        public Vector2 bootstrapCardLabelOffset = new(-2.4f, 0.75f);
        [HideInInspector]
        public Vector2 clusterLabelOffset = new(1f, -0.45f);
        [HideInInspector]
        public Vector2 categoryLabelOffset = new(1.35f, -0.85f);
        [HideInInspector]
        public Vector2 defaultLabelOffset = new(1f, 0f);

        [Header("Screen-space Packing")]
        [Tooltip("노드와 아래쪽 라벨을 하나의 화면 좌표 AABB로 취급해 겹침을 해소합니다.")]
        public bool enableFootprintPacking = true;
        [Range(1, 48)] public int footprintPackingIterations = 18;
        [Min(0f)] public float footprintSpacing = 12f;
        [Min(0f)] public float footprintBoundaryPadding = 4f;
        [Range(0f, 0.25f)] public float footprintAnchorStrength = 0.035f;
        [Min(0.01f)] public float footprintConvergenceEpsilon = 0.2f;

        [Header("Response Channel Analyzer")]
        [Tooltip("분석기 외곽과 패널 사이의 화면 좌표 여백입니다.")]
        [Min(0f)] public float analyzerPanelPadding = 10f;
        [Min(0f)] public float analyzerPanelGap = 8f;
        [Range(0.2f, 0.48f)] public float analyzerDirectoryWidthRatio = 0.32f;
        [Range(0.16f, 0.42f)] public float analyzerRecentProbeHeightRatio = 0.25f;
        [Min(8f)] public float analyzerHeaderHeight = 28f;
        [Range(1, 4)] public int analyzerTraceRows = 3;
        [Range(1, 8)] public int analyzerMiniDirectoryRows = 3;
        [Range(1, 12)] public int analyzerFullDirectoryRows = 6;
        [Range(12, 72)] public int analyzerWaveformSamples = 36;
        [Range(0.05f, 0.48f)] public float analyzerWaveformAmplitudeRatio = 0.3f;
        [Min(0f)] public float analyzerPanelLineThickness = 1.25f;
        [Min(0f)] public float analyzerWaveformThickness = 1.5f;
        [Range(0f, 1f)] public float analyzerSelectionFillAlpha = 0.18f;
        [Range(0f, 1f)] public float analyzerBaselineAlpha = 0.25f;
        public Color analyzerPanelColor = new(0.18f, 0.78f, 0.34f, 0.62f);
        public Color analyzerWaveformColor = new(0.42f, 1f, 0.55f, 0.92f);
        public Color analyzerPatternColor = new(0.5f, 0.82f, 1f, 0.9f);

        [Header("Geometry Quality")]
        public FirstContactSemanticMapGeometryStyle geometry = new();

        private static FirstContactSemanticMapStyle _runtimeDefault;

        public static FirstContactSemanticMapStyle GetOrDefault(FirstContactSemanticMapStyle style)
        {
            if (style != null)
            {
                return style;
            }

            if (_runtimeDefault == null)
            {
                _runtimeDefault = CreateInstance<FirstContactSemanticMapStyle>();
                _runtimeDefault.hideFlags = HideFlags.HideAndDontSave;
            }

            return _runtimeDefault;
        }

        public FirstContactSemanticMapModeStyle GetMode(bool fullMode)
        {
            EnsureModeDefaults();
            return fullMode ? fullMap : miniMap;
        }

        private void OnEnable()
        {
            EnsureModeDefaults();
        }

        private void OnValidate()
        {
            EnsureModeDefaults();
            minimumMapPadding = Mathf.Max(0f, minimumMapPadding);
            mapHorizontalPaddingRatio = Mathf.Clamp(mapHorizontalPaddingRatio, 0f, 0.25f);
            mapVerticalPaddingRatio = Mathf.Clamp(mapVerticalPaddingRatio, 0f, 0.25f);
            ClampUnitRange(ref bootstrapSignalMinimumAlpha, ref bootstrapSignalMaximumAlpha);
            ClampUnitRange(ref rejectedFormationMinimumAlpha, ref rejectedFormationMaximumAlpha);
            ClampUnitRange(ref candidateFormationMinimumAlpha, ref candidateFormationMaximumAlpha);
            labelOffset = Mathf.Max(0f, labelOffset);
            labelMinimumSizeRatio = Mathf.Clamp(labelMinimumSizeRatio, 0.1f, 1f);
            labelMinimumFontSize = Mathf.Max(1f, labelMinimumFontSize);
            labelEdgePadding = Mathf.Max(0f, labelEdgePadding);
            labelPreferredSizePadding = Mathf.Max(0f, labelPreferredSizePadding);
            footprintPackingIterations = Mathf.Clamp(footprintPackingIterations, 1, 48);
            footprintSpacing = Mathf.Max(0f, footprintSpacing);
            footprintBoundaryPadding = Mathf.Max(0f, footprintBoundaryPadding);
            footprintAnchorStrength = Mathf.Clamp(footprintAnchorStrength, 0f, 0.25f);
            footprintConvergenceEpsilon = Mathf.Max(0.01f, footprintConvergenceEpsilon);
            analyzerPanelPadding = Mathf.Max(0f, analyzerPanelPadding);
            analyzerPanelGap = Mathf.Max(0f, analyzerPanelGap);
            analyzerDirectoryWidthRatio = Mathf.Clamp(analyzerDirectoryWidthRatio, 0.2f, 0.48f);
            analyzerRecentProbeHeightRatio = Mathf.Clamp(analyzerRecentProbeHeightRatio, 0.16f, 0.42f);
            analyzerHeaderHeight = Mathf.Max(8f, analyzerHeaderHeight);
            analyzerTraceRows = Mathf.Clamp(analyzerTraceRows, 1, 4);
            analyzerMiniDirectoryRows = Mathf.Clamp(analyzerMiniDirectoryRows, 1, 8);
            analyzerFullDirectoryRows = Mathf.Clamp(analyzerFullDirectoryRows, 1, 12);
            analyzerWaveformSamples = Mathf.Clamp(analyzerWaveformSamples, 12, 72);
            analyzerWaveformAmplitudeRatio = Mathf.Clamp(analyzerWaveformAmplitudeRatio, 0.05f, 0.48f);
            analyzerPanelLineThickness = Mathf.Max(0f, analyzerPanelLineThickness);
            analyzerWaveformThickness = Mathf.Max(0f, analyzerWaveformThickness);
            analyzerSelectionFillAlpha = Mathf.Clamp01(analyzerSelectionFillAlpha);
            analyzerBaselineAlpha = Mathf.Clamp01(analyzerBaselineAlpha);
            miniMap.Validate();
            fullMap.Validate();
            geometry.Validate();
        }

        private void EnsureModeDefaults()
        {
            miniMap ??= FirstContactSemanticMapModeStyle.CreateMiniMap();
            fullMap ??= FirstContactSemanticMapModeStyle.CreateFullMap();
            geometry ??= new FirstContactSemanticMapGeometryStyle();
        }

        private static void ClampUnitRange(ref float minimum, ref float maximum)
        {
            minimum = Mathf.Clamp01(minimum);
            maximum = Mathf.Clamp01(Mathf.Max(minimum, maximum));
        }
    }

    [Serializable]
    public sealed class FirstContactSemanticMapModeStyle
    {
        [Header("Screen Layout")]
        [Range(0.05f, 1f)] public float mapHeightRatio;
        [Range(0f, 1f)] public float terminalTextTopInset;

        [Header("Labels")]
        [Min(1f)] public float labelFontSize;
        [Min(1f)] public float labelWidth;
        [Min(1f)] public float labelHeight;
        [Min(0f)] public float labelOffsetMultiplier;

        [Header("Grid")]
        [Min(2)] public int gridVerticalLineCount;
        [Min(2)] public int gridHorizontalLineCount;
        [Min(0f)] public float gridLineThickness;

        [Header("Link Thickness")]
        [Min(0f)] public float bootstrapSignalMinimumThickness;
        [Min(0f)] public float bootstrapSignalMaximumThickness;
        [Min(0f)] public float normalLinkMinimumThickness;
        [Min(0f)] public float normalLinkMaximumThickness;
        [Min(0f)] public float activeLinkThicknessBonus;
        [Min(0f)] public float confirmedLinkMinimumThickness;
        [Min(0f)] public float confirmedLinkMaximumThickness;
        [Min(0f)] public float rejectedLinkMinimumThickness;
        [Min(0f)] public float rejectedLinkMaximumThickness;
        [Min(0f)] public float candidateLinkMinimumThickness;
        [Min(0f)] public float candidateLinkMaximumThickness;

        [Header("Category Field")]
        [Min(0f)] public float categoryFieldPaddingRatio;
        [Min(0f)] public float categoryFieldMinimumRadiusRatio;
        [Min(0f)] public float categoryFieldMinimumVerticalRadiusMultiplier;
        [Min(0f)] public float categoryFieldRingThickness;
        [Min(0f)] public float categoryFieldInnerRingThickness;

        [Header("Clusters")]
        [Min(0f)] public float clusterRadiusRatio;
        [Min(0f)] public float clusterRingThickness;
        [Min(0f)] public float clusterPulseRingThickness;
        [Min(0f)] public float clusterLockRingThickness;

        [Header("Nodes")]
        [Min(0f)] public float cardNodeRadiusRatio;
        [Min(0f)] public float stableClusterNodeRadiusRatio;
        [Min(0f)] public float bootstrapCategoryNodeRadiusRatio;

        public static FirstContactSemanticMapModeStyle CreateMiniMap()
        {
            return new FirstContactSemanticMapModeStyle
            {
                mapHeightRatio = 0.28f,
                terminalTextTopInset = 0.31f,
                labelFontSize = 10f,
                labelWidth = 128f,
                labelHeight = 20f,
                labelOffsetMultiplier = 0.85f,
                gridVerticalLineCount = 5,
                gridHorizontalLineCount = 3,
                gridLineThickness = 0.8f,
                bootstrapSignalMinimumThickness = 0.8f,
                bootstrapSignalMaximumThickness = 2.8f,
                normalLinkMinimumThickness = 1f,
                normalLinkMaximumThickness = 2.6f,
                activeLinkThicknessBonus = 0.8f,
                confirmedLinkMinimumThickness = 1.8f,
                confirmedLinkMaximumThickness = 3.8f,
                rejectedLinkMinimumThickness = 0.5f,
                rejectedLinkMaximumThickness = 1.4f,
                candidateLinkMinimumThickness = 0.8f,
                candidateLinkMaximumThickness = 2.2f,
                categoryFieldPaddingRatio = 0.065f,
                categoryFieldMinimumRadiusRatio = 0.12f,
                categoryFieldMinimumVerticalRadiusMultiplier = 0.72f,
                categoryFieldRingThickness = 1.8f,
                categoryFieldInnerRingThickness = 0.8f,
                clusterRadiusRatio = 0.055f,
                clusterRingThickness = 1.6f,
                clusterPulseRingThickness = 2.2f,
                clusterLockRingThickness = 1.5f,
                cardNodeRadiusRatio = 0.018f,
                stableClusterNodeRadiusRatio = 0.018f,
                bootstrapCategoryNodeRadiusRatio = 0.02f
            };
        }

        public static FirstContactSemanticMapModeStyle CreateFullMap()
        {
            return new FirstContactSemanticMapModeStyle
            {
                mapHeightRatio = 0.62f,
                terminalTextTopInset = 0.66f,
                labelFontSize = 14f,
                labelWidth = 190f,
                labelHeight = 28f,
                labelOffsetMultiplier = 1.2f,
                gridVerticalLineCount = 8,
                gridHorizontalLineCount = 5,
                gridLineThickness = 1.1f,
                bootstrapSignalMinimumThickness = 1.2f,
                bootstrapSignalMaximumThickness = 4.2f,
                normalLinkMinimumThickness = 1.4f,
                normalLinkMaximumThickness = 4.5f,
                activeLinkThicknessBonus = 1.5f,
                confirmedLinkMinimumThickness = 2.8f,
                confirmedLinkMaximumThickness = 6.2f,
                rejectedLinkMinimumThickness = 0.7f,
                rejectedLinkMaximumThickness = 2.1f,
                candidateLinkMinimumThickness = 1.1f,
                candidateLinkMaximumThickness = 3.6f,
                categoryFieldPaddingRatio = 0.09f,
                categoryFieldMinimumRadiusRatio = 0.18f,
                categoryFieldMinimumVerticalRadiusMultiplier = 0.72f,
                categoryFieldRingThickness = 2.8f,
                categoryFieldInnerRingThickness = 1.2f,
                clusterRadiusRatio = 0.075f,
                clusterRingThickness = 2.4f,
                clusterPulseRingThickness = 3.4f,
                clusterLockRingThickness = 2.2f,
                cardNodeRadiusRatio = 0.021f,
                stableClusterNodeRadiusRatio = 0.022f,
                bootstrapCategoryNodeRadiusRatio = 0.024f
            };
        }

        public void Validate()
        {
            mapHeightRatio = Mathf.Clamp(mapHeightRatio, 0.05f, 1f);
            terminalTextTopInset = Mathf.Clamp01(terminalTextTopInset);
            labelFontSize = Mathf.Max(1f, labelFontSize);
            labelWidth = Mathf.Max(1f, labelWidth);
            labelHeight = Mathf.Max(1f, labelHeight);
            labelOffsetMultiplier = Mathf.Max(0f, labelOffsetMultiplier);
            gridVerticalLineCount = Mathf.Max(2, gridVerticalLineCount);
            gridHorizontalLineCount = Mathf.Max(2, gridHorizontalLineCount);
        }
    }

    [Serializable]
    public sealed class FirstContactSemanticMapGeometryStyle
    {
        [Min(0f)] public float minimumLineThickness = 0.25f;
        [Min(0f)] public float minimumEllipseRadius = 1f;
        [Min(3)] public int minimumSegments = 12;
        [Min(3)] public int categoryFieldFillSegments = 48;
        [Min(3)] public int categoryFieldRingSegments = 54;
        [Min(3)] public int clusterSegments = 32;
        [Min(3)] public int clusterPulseSegments = 36;
        [Min(3)] public int clusterLockSegments = 42;
        [Min(3)] public int nodeSegments = 20;
        [Min(3)] public int categoryTraceRingSegments = 28;
        [Min(3)] public int nodeGlowSegments = 24;
        [Min(3)] public int nodePulseSegments = 32;
        [Min(3)] public int nodeOuterPulseSegments = 36;
        [Min(3)] public int activeNodeRingSegments = 28;

        public void Validate()
        {
            minimumLineThickness = Mathf.Max(0f, minimumLineThickness);
            minimumEllipseRadius = Mathf.Max(0f, minimumEllipseRadius);
            minimumSegments = Mathf.Max(3, minimumSegments);
            categoryFieldFillSegments = Mathf.Max(3, categoryFieldFillSegments);
            categoryFieldRingSegments = Mathf.Max(3, categoryFieldRingSegments);
            clusterSegments = Mathf.Max(3, clusterSegments);
            clusterPulseSegments = Mathf.Max(3, clusterPulseSegments);
            clusterLockSegments = Mathf.Max(3, clusterLockSegments);
            nodeSegments = Mathf.Max(3, nodeSegments);
            categoryTraceRingSegments = Mathf.Max(3, categoryTraceRingSegments);
            nodeGlowSegments = Mathf.Max(3, nodeGlowSegments);
            nodePulseSegments = Mathf.Max(3, nodePulseSegments);
            nodeOuterPulseSegments = Mathf.Max(3, nodeOuterPulseSegments);
            activeNodeRingSegments = Mathf.Max(3, activeNodeRingSegments);
        }
    }
}
