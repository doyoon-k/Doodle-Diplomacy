using System;
using UnityEngine;
using UnityEngine.UI;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    /// <summary>
    /// A bounded CRT response-channel renderer. Semantic membership is expressed as
    /// discrete traces and directory rows instead of spatial containment.
    /// </summary>
    public sealed class FirstContactSemanticMapGraphic : MaskableGraphic
    {
        private readonly FirstContactResponseChannelPresentation _ownedPresentation = new();

        private FirstContactSemanticMapStyle _style;
        private FirstContactSemanticMapSnapshot _snapshot;
        private FirstContactResponseChannelPresentation _presentation;
        private bool _fullMode;

        private FirstContactSemanticMapStyle Style =>
            FirstContactSemanticMapStyle.GetOrDefault(_style);

        private FirstContactSemanticMapModeStyle Mode => Style.GetMode(_fullMode);

        private FirstContactSemanticMapGeometryStyle Geometry => Style.geometry;

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
        }

        public void ApplyStyle(FirstContactSemanticMapStyle style)
        {
            if (_style == style)
            {
                return;
            }

            _style = style;
            SetVerticesDirty();
        }

        public void Show(FirstContactSemanticMapSnapshot snapshot, bool fullMode)
        {
            FirstContactSemanticMapStyle style = Style;
            _ownedPresentation.Build(
                snapshot,
                style.analyzerTraceRows,
                fullMode ? style.analyzerFullDirectoryRows : style.analyzerMiniDirectoryRows);
            Show(snapshot, fullMode, _ownedPresentation);
        }

        public void Show(
            FirstContactSemanticMapSnapshot snapshot,
            bool fullMode,
            FirstContactResponseChannelPresentation presentation)
        {
            _snapshot = snapshot;
            _presentation = presentation;
            _fullMode = fullMode;
            SetVerticesDirty();
        }

        public void Clear()
        {
            _snapshot = null;
            _presentation = null;
            SetVerticesDirty();
        }

        // Retained for compatibility with semantic layout utilities and editor tooling.
        public static Vector2 MapToLocal(
            Vector2 position,
            Rect rect,
            FirstContactSemanticMapStyle configuredStyle)
        {
            FirstContactSemanticMapStyle style =
                FirstContactSemanticMapStyle.GetOrDefault(configuredStyle);
            float paddingX = Mathf.Max(
                style.minimumMapPadding,
                rect.width * style.mapHorizontalPaddingRatio);
            float paddingY = Mathf.Max(
                style.minimumMapPadding,
                rect.height * style.mapVerticalPaddingRatio);
            float normalizedX = Mathf.Clamp01((position.x + 1f) * 0.5f);
            float normalizedY = Mathf.Clamp01((position.y + 1f) * 0.5f);
            return new Vector2(
                Mathf.Lerp(rect.xMin + paddingX, rect.xMax - paddingX, normalizedX),
                Mathf.Lerp(rect.yMin + paddingY, rect.yMax - paddingY, normalizedY));
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect rect = rectTransform.rect;
            if (rect.width <= 1f || rect.height <= 1f)
            {
                return;
            }

            DrawRect(vh, rect.min, rect.max, Style.backgroundColor);
            if (_snapshot == null || _presentation == null)
            {
                return;
            }

            FirstContactResponseChannelLayout layout =
                FirstContactResponseChannelLayout.Resolve(rect, _fullMode, Style);
            DrawPanel(vh, layout.Scope, Style.analyzerPanelColor);
            DrawPanel(vh, layout.Directory, Style.analyzerPanelColor);
            if (layout.HasRecentProbe)
            {
                Color recentColor = _presentation.RecentProbe != null &&
                                    !_presentation.RecentProbeMatchesActiveEntry
                    ? Style.detachedActiveCardColor
                    : Style.analyzerPanelColor;
                DrawPanel(vh, layout.RecentProbe, recentColor);
            }

            DrawScopeGrid(vh, layout);
            DrawDirectorySelection(vh, layout);
            DrawWaveforms(vh, layout);
        }

        private void DrawPanel(VertexHelper vh, Rect rect, Color panelColor)
        {
            DrawOutline(
                vh,
                rect,
                Style.analyzerPanelLineThickness,
                panelColor);
        }

        private void DrawScopeGrid(VertexHelper vh, FirstContactResponseChannelLayout layout)
        {
            Rect plot = layout.ScopePlot;
            Color grid = Style.gridColor;
            int verticalLines = Mathf.Max(2, Mode.gridVerticalLineCount);
            for (int i = 1; i < verticalLines; i++)
            {
                float x = Mathf.Lerp(plot.xMin, plot.xMax, i / (float)verticalLines);
                DrawLine(
                    vh,
                    new Vector2(x, plot.yMin),
                    new Vector2(x, plot.yMax),
                    Mode.gridLineThickness,
                    grid);
            }

            int traceRows = Mathf.Max(1, Style.analyzerTraceRows);
            Color baseline = Style.analyzerPanelColor;
            baseline.a *= Style.analyzerBaselineAlpha;
            for (int row = 0; row < traceRows; row++)
            {
                Rect rowRect = layout.GetTraceRowRect(row, traceRows);
                DrawLine(
                    vh,
                    new Vector2(rowRect.xMin, rowRect.center.y),
                    new Vector2(rowRect.xMax, rowRect.center.y),
                    Mathf.Max(0.5f, Mode.gridLineThickness),
                    baseline);
            }

            float dividerY = layout.Scope.yMax - layout.HeaderHeight;
            DrawLine(
                vh,
                new Vector2(layout.Scope.xMin, dividerY),
                new Vector2(layout.Scope.xMax, dividerY),
                Style.analyzerPanelLineThickness,
                Style.analyzerPanelColor);
            float directoryDividerY = layout.Directory.yMax - layout.HeaderHeight;
            DrawLine(
                vh,
                new Vector2(layout.Directory.xMin, directoryDividerY),
                new Vector2(layout.Directory.xMax, directoryDividerY),
                Style.analyzerPanelLineThickness,
                Style.analyzerPanelColor);
        }

        private void DrawDirectorySelection(
            VertexHelper vh,
            FirstContactResponseChannelLayout layout)
        {
            int rowCount = _fullMode
                ? Style.analyzerFullDirectoryRows
                : Style.analyzerMiniDirectoryRows;
            for (int row = 0; row < _presentation.VisibleDirectoryCount; row++)
            {
                int entryIndex = _presentation.VisibleDirectoryStart + row;
                FirstContactResponseChannelEntry entry =
                    _presentation.DirectoryEntries[entryIndex];
                Rect rowRect = layout.GetDirectoryRowRect(row, rowCount);
                if (ReferenceEquals(entry, _presentation.ActiveEntry))
                {
                    Color selected = entry.Kind == FirstContactResponseChannelKind.Pattern
                        ? Style.analyzerPatternColor
                        : Style.activeCardColor;
                    selected.a = Style.analyzerSelectionFillAlpha;
                    DrawRect(
                        vh,
                        new Vector2(rowRect.xMin, rowRect.yMin + 1f),
                        new Vector2(rowRect.xMax, rowRect.yMax - 1f),
                        selected);
                }

                Color tickColor = entry.Kind == FirstContactResponseChannelKind.Pattern
                    ? Style.analyzerPatternColor
                    : entry.IsStable
                        ? Style.stableBootstrapCategoryColor
                        : Style.bootstrapCategoryColor;
                float tickSize = Mathf.Clamp(rowRect.height * 0.18f, 2f, 6f);
                Vector2 tickCenter = new(rowRect.xMax - tickSize * 1.5f, rowRect.center.y);
                DrawRect(
                    vh,
                    tickCenter - Vector2.one * tickSize * 0.5f,
                    tickCenter + Vector2.one * tickSize * 0.5f,
                    tickColor);
            }
        }

        private void DrawWaveforms(
            VertexHelper vh,
            FirstContactResponseChannelLayout layout)
        {
            int traceRows = Mathf.Max(1, Style.analyzerTraceRows);
            int visibleTraces = Mathf.Min(traceRows, _presentation.TraceNodes.Count);
            for (int row = 0; row < visibleTraces; row++)
            {
                FirstContactSemanticMapNode node = _presentation.TraceNodes[row];
                if (node == null)
                {
                    continue;
                }

                Rect rowRect = layout.GetTraceRowRect(row, traceRows);
                DrawWaveform(vh, rowRect, node, row);
            }
        }

        private void DrawWaveform(
            VertexHelper vh,
            Rect rowRect,
            FirstContactSemanticMapNode node,
            int row)
        {
            int samples = Mathf.Clamp(Style.analyzerWaveformSamples, 12, 72);
            uint hash = StableHash(node.Id);
            float phase = (hash & 1023u) / 1023f * Mathf.PI * 2f;
            float primaryCycles = 1.8f + ((hash >> 10) & 7u) * 0.22f;
            float secondaryCycles = 6f + ((hash >> 14) & 7u) * 0.46f;
            float pulse = Mathf.Clamp01(node.Pulse);
            float amplitude = rowRect.height * Style.analyzerWaveformAmplitudeRatio *
                              (node.IsActive ? 1.08f + pulse * 0.18f : 0.82f);
            Color waveformColor = _presentation.ActiveEntry != null &&
                                  _presentation.ActiveEntry.Kind == FirstContactResponseChannelKind.Pattern
                ? Style.analyzerPatternColor
                : node.IsActive
                    ? Style.activeCardColor
                    : Style.analyzerWaveformColor;
            waveformColor.a = Mathf.Clamp01(waveformColor.a + pulse * 0.08f);

            Vector2 previous = ResolveWavePoint(
                rowRect,
                node,
                0f,
                phase,
                primaryCycles,
                secondaryCycles,
                amplitude,
                row);
            for (int sample = 1; sample <= samples; sample++)
            {
                float t = sample / (float)samples;
                Vector2 next = ResolveWavePoint(
                    rowRect,
                    node,
                    t,
                    phase,
                    primaryCycles,
                    secondaryCycles,
                    amplitude,
                    row);
                DrawLine(
                    vh,
                    previous,
                    next,
                    Style.analyzerWaveformThickness + pulse * 0.55f,
                    waveformColor);
                previous = next;
            }

            if (!node.IsActive && pulse <= 0.001f)
            {
                return;
            }

            float cursorX = Mathf.Lerp(rowRect.xMin, rowRect.xMax, 0.94f);
            Color cursor = Style.activeCardColor;
            cursor.a *= 0.72f + pulse * 0.28f;
            DrawLine(
                vh,
                new Vector2(cursorX, rowRect.yMin + rowRect.height * 0.18f),
                new Vector2(cursorX, rowRect.yMax - rowRect.height * 0.18f),
                Style.analyzerWaveformThickness,
                cursor);
        }

        private static Vector2 ResolveWavePoint(
            Rect rowRect,
            FirstContactSemanticMapNode node,
            float t,
            float phase,
            float primaryCycles,
            float secondaryCycles,
            float amplitude,
            int row)
        {
            float first = Mathf.Sin(t * Mathf.PI * 2f * primaryCycles + phase) * 0.62f;
            float second = Mathf.Sin(t * Mathf.PI * 2f * secondaryCycles + phase * 0.37f) * 0.23f;
            float embedding = ResolveEmbeddingSample(node.Embedding, t, row) * 0.28f;
            return new Vector2(
                Mathf.Lerp(rowRect.xMin, rowRect.xMax, t),
                rowRect.center.y + (first + second + embedding) * amplitude);
        }

        private static float ResolveEmbeddingSample(float[] embedding, float t, int row)
        {
            if (embedding == null || embedding.Length == 0)
            {
                return 0f;
            }

            int index = Mathf.Clamp(
                Mathf.FloorToInt(t * embedding.Length + row * 7) % embedding.Length,
                0,
                embedding.Length - 1);
            return Mathf.Clamp(embedding[index], -1f, 1f);
        }

        private void DrawOutline(VertexHelper vh, Rect rect, float thickness, Color drawColor)
        {
            DrawLine(vh, new Vector2(rect.xMin, rect.yMin), new Vector2(rect.xMax, rect.yMin), thickness, drawColor);
            DrawLine(vh, new Vector2(rect.xMax, rect.yMin), new Vector2(rect.xMax, rect.yMax), thickness, drawColor);
            DrawLine(vh, new Vector2(rect.xMax, rect.yMax), new Vector2(rect.xMin, rect.yMax), thickness, drawColor);
            DrawLine(vh, new Vector2(rect.xMin, rect.yMax), new Vector2(rect.xMin, rect.yMin), thickness, drawColor);
        }

        private static void DrawRect(VertexHelper vh, Vector2 min, Vector2 max, Color drawColor)
        {
            int start = vh.currentVertCount;
            AddVert(vh, new Vector2(min.x, min.y), drawColor);
            AddVert(vh, new Vector2(min.x, max.y), drawColor);
            AddVert(vh, new Vector2(max.x, max.y), drawColor);
            AddVert(vh, new Vector2(max.x, min.y), drawColor);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start, start + 2, start + 3);
        }

        private void DrawLine(
            VertexHelper vh,
            Vector2 start,
            Vector2 end,
            float thickness,
            Color drawColor)
        {
            Vector2 delta = end - start;
            if (delta.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            float safeThickness = Mathf.Max(Geometry.minimumLineThickness, thickness);
            Vector2 normal = new Vector2(-delta.y, delta.x).normalized * (safeThickness * 0.5f);
            int first = vh.currentVertCount;
            AddVert(vh, start - normal, drawColor);
            AddVert(vh, start + normal, drawColor);
            AddVert(vh, end + normal, drawColor);
            AddVert(vh, end - normal, drawColor);
            vh.AddTriangle(first, first + 1, first + 2);
            vh.AddTriangle(first, first + 2, first + 3);
        }

        private static uint StableHash(string value)
        {
            unchecked
            {
                uint hash = 2166136261u;
                string safeValue = value ?? string.Empty;
                for (int i = 0; i < safeValue.Length; i++)
                {
                    hash ^= safeValue[i];
                    hash *= 16777619u;
                }

                return hash == 0u ? 1u : hash;
            }
        }

        private static void AddVert(VertexHelper vh, Vector2 position, Color drawColor)
        {
            UIVertex vertex = UIVertex.simpleVert;
            vertex.position = position;
            vertex.color = drawColor;
            vh.AddVert(vertex);
        }
    }
}
