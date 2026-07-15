
using System;
using UnityEngine;
using UnityEngine.UI;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    public sealed class FirstContactSemanticMapGraphic : MaskableGraphic
    {
        private FirstContactSemanticMapStyle _style;

        private FirstContactSemanticMapSnapshot _snapshot;
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
            _snapshot = snapshot;
            _fullMode = fullMode;
            SetVerticesDirty();
        }

        public void Clear()
        {
            _snapshot = null;
            SetVerticesDirty();
        }

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

        private Vector2 MapPoint(Vector2 position, Rect rect)
        {
            return MapToLocal(position, rect, Style);
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
            DrawGrid(vh, rect);

            if (_snapshot == null || _snapshot.Nodes.Count == 0)
            {
                return;
            }

            DrawBootstrapCategoryFields(vh, rect);
            DrawLinks(vh, rect);
            DrawClusters(vh, rect);
            DrawNodes(vh, rect);
        }

        private void DrawGrid(VertexHelper vh, Rect rect)
        {
            int verticalLines = Mode.gridVerticalLineCount;
            int horizontalLines = Mode.gridHorizontalLineCount;
            float thickness = Mode.gridLineThickness;

            for (int i = 1; i < verticalLines; i++)
            {
                float x = Mathf.Lerp(rect.xMin, rect.xMax, i / (float)verticalLines);
                DrawLine(vh, new Vector2(x, rect.yMin), new Vector2(x, rect.yMax), thickness, Style.gridColor);
            }

            for (int i = 1; i < horizontalLines; i++)
            {
                float y = Mathf.Lerp(rect.yMin, rect.yMax, i / (float)horizontalLines);
                DrawLine(vh, new Vector2(rect.xMin, y), new Vector2(rect.xMax, y), thickness, Style.gridColor);
            }
        }

        private void DrawLinks(VertexHelper vh, Rect rect)
        {
            for (int i = 0; i < _snapshot.Links.Count; i++)
            {
                FirstContactSemanticMapLink link = _snapshot.Links[i];
                FirstContactSemanticMapNode from = _snapshot.FindNode(link.FromId);
                FirstContactSemanticMapNode to = _snapshot.FindNode(link.ToId);
                if (from == null || to == null)
                {
                    continue;
                }

                if (link.Kind != FirstContactSemanticMapLinkKind.Normal)
                {
                    DrawFormationLink(vh, rect, from, to, link);
                    continue;
                }

                if (TryResolveBootstrapCategoryLink(from, to, out FirstContactSemanticMapNode categoryNode))
                {
                    float normalizedSignal = Mathf.Clamp01(link.Strength);
                    Color categoryColor = ResolveBootstrapFieldColor(categoryNode);
                    Color signalColor = new(
                        categoryColor.r,
                        categoryColor.g,
                        categoryColor.b,
                        Mathf.Lerp(
                            Style.bootstrapSignalMinimumAlpha,
                            Style.bootstrapSignalMaximumAlpha,
                            normalizedSignal));
                    float signalThickness = Mathf.Lerp(
                        Mode.bootstrapSignalMinimumThickness,
                        Mode.bootstrapSignalMaximumThickness,
                        normalizedSignal);
                    DrawLine(vh, MapPoint(from.Position, rect), MapPoint(to.Position, rect), signalThickness, signalColor);
                    continue;
                }

                float normalized = Mathf.Clamp01((link.Strength + 1f) * 0.5f);
                Color color = Color.Lerp(Style.weakLinkColor, Style.strongLinkColor, normalized);
                if (from.IsActive || to.IsActive)
                {
                    color.a = Mathf.Max(color.a, Style.activeLinkMinimumAlpha);
                }

                float thickness = Mathf.Lerp(
                    Mode.normalLinkMinimumThickness,
                    Mode.normalLinkMaximumThickness,
                    normalized);
                if (from.IsActive || to.IsActive)
                {
                    thickness += Mode.activeLinkThicknessBonus;
                }

                DrawLine(vh, MapPoint(from.Position, rect), MapPoint(to.Position, rect), thickness, color);
            }
        }

        private void DrawFormationLink(
            VertexHelper vh,
            Rect rect,
            FirstContactSemanticMapNode from,
            FirstContactSemanticMapNode to,
            FirstContactSemanticMapLink link)
        {
            float normalized = Mathf.Clamp01(link.Strength);
            Color color;
            float thickness;
            switch (link.Kind)
            {
                case FirstContactSemanticMapLinkKind.Confirmed:
                    color = Color.Lerp(
                        Style.confirmedFormationLinkColor,
                        Style.strongLinkColor,
                        normalized);
                    color.a = Mathf.Max(color.a, Style.confirmedFormationMinimumAlpha);
                    thickness = Mathf.Lerp(
                        Mode.confirmedLinkMinimumThickness,
                        Mode.confirmedLinkMaximumThickness,
                        normalized);
                    break;
                case FirstContactSemanticMapLinkKind.Rejected:
                    color = new Color(
                        Style.rejectedFormationLinkColor.r,
                        Style.rejectedFormationLinkColor.g,
                        Style.rejectedFormationLinkColor.b,
                        Mathf.Lerp(
                            Style.rejectedFormationMinimumAlpha,
                            Style.rejectedFormationMaximumAlpha,
                            normalized));
                    thickness = Mathf.Lerp(
                        Mode.rejectedLinkMinimumThickness,
                        Mode.rejectedLinkMaximumThickness,
                        normalized);
                    break;
                default:
                    color = new Color(
                        Style.candidateFormationLinkColor.r,
                        Style.candidateFormationLinkColor.g,
                        Style.candidateFormationLinkColor.b,
                        Mathf.Lerp(
                            Style.candidateFormationMinimumAlpha,
                            Style.candidateFormationMaximumAlpha,
                            normalized));
                    thickness = Mathf.Lerp(
                        Mode.candidateLinkMinimumThickness,
                        Mode.candidateLinkMaximumThickness,
                        normalized);
                    break;
            }

            DrawLine(vh, MapPoint(from.Position, rect), MapPoint(to.Position, rect), thickness, color);
        }

        private void DrawBootstrapCategoryFields(VertexHelper vh, Rect rect)
        {
            for (int i = 0; i < _snapshot.Nodes.Count; i++)
            {
                FirstContactSemanticMapNode categoryNode = _snapshot.Nodes[i];
                if (categoryNode == null ||
                    categoryNode.Kind != FirstContactSemanticMapNodeKind.BootstrapCategory ||
                    string.IsNullOrWhiteSpace(categoryNode.BootstrapCategoryId))
                {
                    continue;
                }

                if (!TryResolveBootstrapFieldBounds(
                        categoryNode,
                        rect,
                        out Vector2 center,
                        out Vector2 radii,
                        out int acceptedCount))
                {
                    continue;
                }

                Color fieldColor = ResolveBootstrapFieldColor(categoryNode);
                float traceRatio = categoryNode.RequiredTraceCount > 0
                    ? Mathf.Clamp01(categoryNode.TraceCount / (float)categoryNode.RequiredTraceCount)
                    : 1f;
                float fillAlpha = Mathf.Lerp(
                    Style.categoryFieldMinimumFillAlpha,
                    categoryNode.IsBootstrapCategoryStable
                        ? Style.stableCategoryFieldFillAlpha
                        : Style.categoryFieldFillAlpha,
                    traceRatio);
                DrawFilledEllipse(
                    vh,
                    center,
                    radii,
                    new Color(fieldColor.r, fieldColor.g, fieldColor.b, fillAlpha),
                    Geometry.categoryFieldFillSegments);

                float ringAlpha = Mathf.Lerp(
                    Style.categoryFieldMinimumRingAlpha,
                    categoryNode.IsBootstrapCategoryStable
                        ? Style.stableCategoryFieldRingAlpha
                        : Style.categoryFieldRingAlpha,
                    traceRatio);
                DrawEllipseRing(
                    vh,
                    center,
                    radii,
                    Mode.categoryFieldRingThickness,
                    new Color(fieldColor.r, fieldColor.g, fieldColor.b, ringAlpha),
                    Geometry.categoryFieldRingSegments);

                if (acceptedCount > 1)
                {
                    Vector2 innerRadii = radii * Style.categoryFieldInnerRingRadiusMultiplier;
                    DrawEllipseRing(
                        vh,
                        center,
                        innerRadii,
                        Mode.categoryFieldInnerRingThickness,
                        new Color(
                            fieldColor.r,
                            fieldColor.g,
                            fieldColor.b,
                            Mathf.Min(
                                Style.categoryFieldInnerRingMaximumAlpha,
                                ringAlpha * Style.categoryFieldInnerRingAlphaMultiplier)),
                        Geometry.categoryFieldRingSegments);
                }
            }
        }

        private bool TryResolveBootstrapFieldBounds(
            FirstContactSemanticMapNode categoryNode,
            Rect rect,
            out Vector2 center,
            out Vector2 radii,
            out int acceptedCount)
        {
            Vector2 categoryCenter = MapPoint(categoryNode.Position, rect);
            Vector2 min = categoryCenter;
            Vector2 max = categoryCenter;
            acceptedCount = 0;

            for (int i = 0; i < _snapshot.Nodes.Count; i++)
            {
                FirstContactSemanticMapNode node = _snapshot.Nodes[i];
                if (node == null ||
                    node.Kind != FirstContactSemanticMapNodeKind.Card ||
                    node.IsBootstrapDetached ||
                    !string.Equals(node.BootstrapCategoryId, categoryNode.BootstrapCategoryId, StringComparison.Ordinal))
                {
                    continue;
                }

                Vector2 point = MapPoint(node.Position, rect);
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
                acceptedCount++;
            }

            center = (min + max) * 0.5f;
            float baseSize = Mathf.Min(rect.width, rect.height);
            float padding = baseSize * Mode.categoryFieldPaddingRatio;
            float minRadius = baseSize * Mode.categoryFieldMinimumRadiusRatio;
            radii = new Vector2(
                Mathf.Max(minRadius, (max.x - min.x) * 0.5f + padding),
                Mathf.Max(
                    minRadius * Mode.categoryFieldMinimumVerticalRadiusMultiplier,
                    (max.y - min.y) * 0.5f + padding));

            return true;
        }

        private static bool TryResolveBootstrapCategoryLink(
            FirstContactSemanticMapNode first,
            FirstContactSemanticMapNode second,
            out FirstContactSemanticMapNode categoryNode)
        {
            categoryNode = null;
            if (first == null || second == null)
            {
                return false;
            }

            if (first.Kind == FirstContactSemanticMapNodeKind.BootstrapCategory &&
                second.Kind == FirstContactSemanticMapNodeKind.Card)
            {
                categoryNode = first;
                return true;
            }

            if (second.Kind == FirstContactSemanticMapNodeKind.BootstrapCategory &&
                first.Kind == FirstContactSemanticMapNodeKind.Card)
            {
                categoryNode = second;
                return true;
            }

            return false;
        }

        private void DrawClusters(VertexHelper vh, Rect rect)
        {
            for (int i = 0; i < _snapshot.Nodes.Count; i++)
            {
                FirstContactSemanticMapNode node = _snapshot.Nodes[i];
                if (node.Kind != FirstContactSemanticMapNodeKind.StableCluster &&
                    node.Kind != FirstContactSemanticMapNodeKind.BootstrapCategory)
                {
                    continue;
                }

                Vector2 center = MapPoint(node.Position, rect);
                float radius = Mathf.Min(rect.width, rect.height) * Mode.clusterRadiusRatio;
                Color cluster = ResolveNodeColor(node);
                float traceRatio = node.RequiredTraceCount > 0
                    ? Mathf.Clamp01(node.TraceCount / (float)node.RequiredTraceCount)
                    : 1f;
                float fillAlpha = node.Kind == FirstContactSemanticMapNodeKind.BootstrapCategory
                    ? Mathf.Lerp(
                        Style.categoryFieldMinimumFillAlpha,
                        Style.stableCategoryFieldFillAlpha,
                        traceRatio)
                    : Style.stableClusterFillAlpha;
                DrawFilledCircle(
                    vh,
                    center,
                    radius,
                    new Color(cluster.r, cluster.g, cluster.b, fillAlpha),
                    Geometry.clusterSegments);
                DrawRing(
                    vh,
                    center,
                    radius,
                    Mode.clusterRingThickness,
                    cluster,
                    Geometry.clusterSegments);
                if (node.Pulse > Style.pulseVisibilityThreshold)
                {
                    Color pulseColor = new(
                        cluster.r,
                        cluster.g,
                        cluster.b,
                        Mathf.Clamp01(Style.clusterPulseAlpha * node.Pulse));
                    DrawRing(
                        vh,
                        center,
                        radius * (
                            Style.clusterPulseRingBaseScale +
                            Style.clusterPulseRingPulseScale * node.Pulse),
                        Mode.clusterPulseRingThickness,
                        pulseColor,
                        Geometry.clusterPulseSegments);
                    if (node.Pulse > Style.clusterLockPulseThreshold)
                    {
                        Color lockColor = new(
                            cluster.r,
                            cluster.g,
                            cluster.b,
                            Mathf.Clamp01(Style.clusterLockPulseAlpha * node.Pulse));
                        DrawRing(
                            vh,
                            center,
                            radius * (
                                Style.clusterLockRingBaseScale +
                                Style.clusterLockRingPulseScale * node.Pulse),
                            Mode.clusterLockRingThickness,
                            lockColor,
                            Geometry.clusterLockSegments);
                    }
                }
            }
        }

        private void DrawNodes(VertexHelper vh, Rect rect)
        {
            for (int pass = 0; pass < 3; pass++)
            {
                for (int i = 0; i < _snapshot.Nodes.Count; i++)
                {
                    FirstContactSemanticMapNode node = _snapshot.Nodes[i];
                    if (GetNodePass(node) != pass)
                    {
                        continue;
                    }

                    DrawNode(vh, rect, node);
                }
            }
        }

        private static int GetNodePass(FirstContactSemanticMapNode node)
        {
            if (node.Kind == FirstContactSemanticMapNodeKind.StableCluster ||
                node.Kind == FirstContactSemanticMapNodeKind.BootstrapCategory)
            {
                return 0;
            }

            return node.IsActive ? 2 : 1;
        }

        private void DrawNode(VertexHelper vh, Rect rect, FirstContactSemanticMapNode node)
        {
            Vector2 center = MapPoint(node.Position, rect);
            float baseRadius = Mathf.Min(rect.width, rect.height);
            float radius = node.Kind switch
            {
                FirstContactSemanticMapNodeKind.StableCluster =>
                    baseRadius * Mode.stableClusterNodeRadiusRatio,
                FirstContactSemanticMapNodeKind.BootstrapCategory =>
                    baseRadius * Mode.bootstrapCategoryNodeRadiusRatio,
                _ => baseRadius * Mode.cardNodeRadiusRatio
            };

            if (node.IsActive)
            {
                radius *= Style.activeNodeBaseScale + node.Pulse * Style.activeNodePulseScale;
            }

            Color nodeColor = ResolveNodeColor(node);
            if (node.Kind == FirstContactSemanticMapNodeKind.StableCluster ||
                node.Kind == FirstContactSemanticMapNodeKind.BootstrapCategory)
            {
                DrawFilledCircle(vh, center, radius, nodeColor, Geometry.nodeSegments);
                if (node.Kind == FirstContactSemanticMapNodeKind.BootstrapCategory && node.TraceCount > 0)
                {
                    DrawRing(
                        vh,
                        center,
                        radius * Style.categoryTraceRingRadiusMultiplier,
                        Mathf.Max(
                            Style.categoryTraceRingMinimumThickness,
                            radius * Style.categoryTraceRingThicknessMultiplier),
                        nodeColor,
                        Geometry.categoryTraceRingSegments);
                }
                return;
            }

            DrawFilledCircle(vh, center, radius, nodeColor, Geometry.nodeSegments);
            if (node.Pulse > Style.pulseVisibilityThreshold)
            {
                Color glowColor = new(
                    nodeColor.r,
                    nodeColor.g,
                    nodeColor.b,
                    Mathf.Clamp01(Style.nodeGlowAlpha * node.Pulse));
                Color pulseColor = new(
                    nodeColor.r,
                    nodeColor.g,
                    nodeColor.b,
                    Mathf.Clamp01(Style.nodePulseAlpha * node.Pulse));
                DrawFilledCircle(
                    vh,
                    center,
                    radius * (
                        Style.nodeGlowBaseScale +
                        Style.nodeGlowPulseScale * node.Pulse),
                    glowColor,
                    Geometry.nodeGlowSegments);
                DrawRing(
                    vh,
                    center,
                    radius * (
                        Style.nodePulseRingBaseScale +
                        Style.nodePulseRingPulseScale * node.Pulse),
                    Mathf.Max(
                        Style.nodePulseRingMinimumThickness,
                        radius * Style.nodePulseRingThicknessMultiplier),
                    pulseColor,
                    Geometry.nodePulseSegments);
                DrawRing(
                    vh,
                    center,
                    radius * (
                        Style.nodeOuterPulseRingBaseScale +
                        Style.nodeOuterPulseRingPulseScale * node.Pulse),
                    Mathf.Max(
                        Style.nodeOuterPulseRingMinimumThickness,
                        radius * Style.nodeOuterPulseRingThicknessMultiplier),
                    new Color(
                        pulseColor.r,
                        pulseColor.g,
                        pulseColor.b,
                        pulseColor.a * Style.nodeOuterPulseAlphaMultiplier),
                    Geometry.nodeOuterPulseSegments);
            }

            if (node.IsActive)
            {
                DrawRing(
                    vh,
                    center,
                    radius * Style.activeNodeRingScale,
                    Mathf.Max(
                        Style.activeNodeRingMinimumThickness,
                        radius * Style.activeNodeRingThicknessMultiplier),
                    Style.activeCardColor,
                    Geometry.activeNodeRingSegments);
            }
        }

        private Color ResolveNodeColor(FirstContactSemanticMapNode node)
        {
            if (node.IsActive)
            {
                if (node.IsBootstrapDetached)
                {
                    return Style.detachedActiveCardColor;
                }

                return Style.activeCardColor;
            }

            if (node.IsBootstrapDetached)
            {
                return Style.detachedCardColor;
            }

            return node.Kind switch
            {
                FirstContactSemanticMapNodeKind.StableCluster => Style.clusterColor,
                FirstContactSemanticMapNodeKind.BootstrapCategory => node.IsBootstrapCategoryStable
                    ? Style.stableBootstrapCategoryColor
                    : Style.bootstrapCategoryColor,
                FirstContactSemanticMapNodeKind.Card => Style.cardColor,
                _ => Style.fallbackNodeColor
            };
        }

        private Color ResolveBootstrapFieldColor(FirstContactSemanticMapNode node)
        {
            if (node == null)
            {
                return Style.bootstrapCategoryColor;
            }

            return node.IsBootstrapCategoryStable
                ? Style.stableBootstrapCategoryColor
                : Style.bootstrapCategoryColor;
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

        private void DrawLine(VertexHelper vh, Vector2 start, Vector2 end, float thickness, Color drawColor)
        {
            Vector2 delta = end - start;
            if (delta.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            Vector2 normal = new Vector2(-delta.y, delta.x).normalized * (Mathf.Max(Geometry.minimumLineThickness, thickness) * 0.5f);
            int first = vh.currentVertCount;
            AddVert(vh, start - normal, drawColor);
            AddVert(vh, start + normal, drawColor);
            AddVert(vh, end + normal, drawColor);
            AddVert(vh, end - normal, drawColor);
            vh.AddTriangle(first, first + 1, first + 2);
            vh.AddTriangle(first, first + 2, first + 3);
        }

        private void DrawFilledCircle(
            VertexHelper vh,
            Vector2 center,
            float radius,
            Color drawColor,
            int segments)
        {
            int safeSegments = Mathf.Max(Geometry.minimumSegments, segments);
            int centerIndex = vh.currentVertCount;
            AddVert(vh, center, drawColor);
            for (int i = 0; i <= safeSegments; i++)
            {
                float angle = (i / (float)safeSegments) * Mathf.PI * 2f;
                AddVert(vh, center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius, drawColor);
            }

            for (int i = 1; i <= safeSegments; i++)
            {
                vh.AddTriangle(centerIndex, centerIndex + i, centerIndex + i + 1);
            }
        }

        private void DrawFilledEllipse(
            VertexHelper vh,
            Vector2 center,
            Vector2 radii,
            Color drawColor,
            int segments)
        {
            int safeSegments = Mathf.Max(Geometry.minimumSegments, segments);
            Vector2 safeRadii = new(
                Mathf.Max(Geometry.minimumEllipseRadius, radii.x),
                Mathf.Max(Geometry.minimumEllipseRadius, radii.y));
            int centerIndex = vh.currentVertCount;
            AddVert(vh, center, drawColor);
            for (int i = 0; i <= safeSegments; i++)
            {
                float angle = (i / (float)safeSegments) * Mathf.PI * 2f;
                Vector2 point = center + new Vector2(Mathf.Cos(angle) * safeRadii.x, Mathf.Sin(angle) * safeRadii.y);
                AddVert(vh, point, drawColor);
            }

            for (int i = 1; i <= safeSegments; i++)
            {
                vh.AddTriangle(centerIndex, centerIndex + i, centerIndex + i + 1);
            }
        }

        private void DrawRing(
            VertexHelper vh,
            Vector2 center,
            float radius,
            float thickness,
            Color drawColor,
            int segments)
        {
            int safeSegments = Mathf.Max(Geometry.minimumSegments, segments);
            Vector2 previous = center + new Vector2(radius, 0f);
            for (int i = 1; i <= safeSegments; i++)
            {
                float angle = (i / (float)safeSegments) * Mathf.PI * 2f;
                Vector2 next = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                DrawLine(vh, previous, next, thickness, drawColor);
                previous = next;
            }
        }

        private void DrawEllipseRing(
            VertexHelper vh,
            Vector2 center,
            Vector2 radii,
            float thickness,
            Color drawColor,
            int segments)
        {
            int safeSegments = Mathf.Max(Geometry.minimumSegments, segments);
            Vector2 safeRadii = new(
                Mathf.Max(Geometry.minimumEllipseRadius, radii.x),
                Mathf.Max(Geometry.minimumEllipseRadius, radii.y));
            Vector2 previous = center + new Vector2(safeRadii.x, 0f);
            for (int i = 1; i <= safeSegments; i++)
            {
                float angle = (i / (float)safeSegments) * Mathf.PI * 2f;
                Vector2 next = center + new Vector2(Mathf.Cos(angle) * safeRadii.x, Mathf.Sin(angle) * safeRadii.y);
                DrawLine(vh, previous, next, thickness, drawColor);
                previous = next;
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

