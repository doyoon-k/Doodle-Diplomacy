using System;
using System.Collections.Generic;
using DoodleDiplomacy.Devices;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TerminalDisplay))]
    public sealed class FirstContactSemanticMapDisplay : MonoBehaviour
    {
        private const string DefaultMapName = "FirstContactSemanticMap";
        private const string LabelRootName = "SemanticMapLabels";

        [Header("References")]
        [Tooltip("의미공간 맵을 표시할 터미널입니다. 비워두면 같은 GameObject의 TerminalDisplay를 사용합니다.")]
        [SerializeField] private TerminalDisplay terminalDisplay;
        [Tooltip("터미널 화면 안에 표시할 의미공간 그래픽입니다. 비워두면 런타임에 자동 생성합니다.")]
        [SerializeField] private FirstContactSemanticMapGraphic mapGraphic;

        [Header("Layout")]
        [Tooltip("맵 그래픽을 자동 생성합니다.")]
        [SerializeField] private bool autoCreateMap = true;
        [Tooltip("질문 화면에서 미니맵이 차지하는 터미널 화면 높이 비율입니다.")]
        [SerializeField, Range(0.12f, 0.55f)] private float miniMapHeightRatio = 0.28f;
        [Tooltip("전체 맵 화면에서 맵이 차지하는 터미널 화면 높이 비율입니다.")]
        [SerializeField, Range(0.35f, 0.82f)] private float fullMapHeightRatio = 0.62f;
        [Tooltip("미니맵이 켜졌을 때 터미널 텍스트가 위에서부터 비워둘 비율입니다.")]
        [SerializeField, Range(0f, 0.85f)] private float miniTextTopInsetRatio = 0.31f;
        [Tooltip("전체 맵이 켜졌을 때 터미널 텍스트가 위에서부터 비워둘 비율입니다.")]
        [SerializeField, Range(0f, 0.9f)] private float fullTextTopInsetRatio = 0.66f;
        [Tooltip("맵 좌우 여백 비율입니다.")]
        [SerializeField, Range(0f, 0.12f)] private float horizontalInsetRatio = 0.035f;
        [Tooltip("맵 상단 여백 비율입니다.")]
        [SerializeField, Range(0f, 0.12f)] private float topInsetRatio = 0.035f;

        [Header("Labels")]
        [Tooltip("미니맵에서도 주요 라벨을 표시합니다.")]
        [SerializeField] private bool showMiniMapLabels = true;
        [Tooltip("미니맵 라벨 글자 크기입니다.")]
        [SerializeField, Min(6f)] private float miniLabelFontSize = 10f;
        [Tooltip("전체 맵 라벨 글자 크기입니다.")]
        [SerializeField, Min(8f)] private float fullLabelFontSize = 14f;
        [Tooltip("라벨이 노드에서 떨어지는 거리입니다.")]
        [SerializeField, Min(0f)] private float labelOffset = 12f;

        private readonly List<TextMeshProUGUI> _labels = new();
        private RectTransform _mapRect;
        private RectTransform _labelRoot;
        private bool _fullMode;

        private void Reset()
        {
            terminalDisplay = GetComponent<TerminalDisplay>();
        }

        private void Awake()
        {
            ResolveReferences();
            ConfigureCurrentLayout();
            SetVisible(false);
        }

        private void OnValidate()
        {
            miniMapHeightRatio = Mathf.Clamp(miniMapHeightRatio, 0.12f, 0.55f);
            fullMapHeightRatio = Mathf.Clamp(fullMapHeightRatio, 0.35f, 0.82f);
            miniTextTopInsetRatio = Mathf.Clamp01(miniTextTopInsetRatio);
            fullTextTopInsetRatio = Mathf.Clamp01(fullTextTopInsetRatio);
            horizontalInsetRatio = Mathf.Clamp(horizontalInsetRatio, 0f, 0.12f);
            topInsetRatio = Mathf.Clamp(topInsetRatio, 0f, 0.12f);
            miniLabelFontSize = Mathf.Max(6f, miniLabelFontSize);
            fullLabelFontSize = Mathf.Max(8f, fullLabelFontSize);
            labelOffset = Mathf.Max(0f, labelOffset);

            ResolveReferences();
            ConfigureCurrentLayout();
        }

        public void ShowMiniMap(FirstContactSemanticMapSnapshot snapshot)
        {
            Show(snapshot, fullMode: false);
        }

        public void ShowFullMap(FirstContactSemanticMapSnapshot snapshot)
        {
            Show(snapshot, fullMode: true);
        }

        public void Clear()
        {
            mapGraphic?.Clear();
            ClearLabels();
            SetVisible(false);
            terminalDisplay?.SetContentTopInsetNormalized(0f);
        }

        private void Show(FirstContactSemanticMapSnapshot snapshot, bool fullMode)
        {
            if (snapshot == null || snapshot.Nodes.Count == 0)
            {
                Clear();
                return;
            }

            FirstContactSemanticMapGraphic graphic = EnsureMapGraphic();
            if (graphic == null)
            {
                return;
            }

            _fullMode = fullMode;
            ConfigureCurrentLayout();
            graphic.Show(snapshot, fullMode);
            SetVisible(true);
            terminalDisplay?.SetContentTopInsetNormalized(fullMode ? fullTextTopInsetRatio : miniTextTopInsetRatio);
            RebuildLabels(snapshot, fullMode);
        }

        private FirstContactSemanticMapGraphic EnsureMapGraphic()
        {
            ResolveReferences();
            if (mapGraphic != null)
            {
                EnsureLabelRoot();
                return mapGraphic;
            }

            if (!autoCreateMap || terminalDisplay == null)
            {
                return null;
            }

            RectTransform screenRect = terminalDisplay.ScreenRectTransform;
            if (screenRect == null)
            {
                Debug.LogWarning("[FirstContactSemanticMapDisplay] Terminal screen panel is missing.", this);
                return null;
            }

            var mapObject = new GameObject(
                DefaultMapName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(FirstContactSemanticMapGraphic));
            mapObject.transform.SetParent(screenRect, false);
            mapObject.transform.SetAsFirstSibling();

            mapGraphic = mapObject.GetComponent<FirstContactSemanticMapGraphic>();
            _mapRect = mapGraphic.rectTransform;
            EnsureLabelRoot();
            ConfigureCurrentLayout();
            mapGraphic.Clear();
            return mapGraphic;
        }

        private void ResolveReferences()
        {
            if (terminalDisplay == null)
            {
                terminalDisplay = GetComponent<TerminalDisplay>();
            }

            if (mapGraphic == null && terminalDisplay != null)
            {
                mapGraphic = terminalDisplay.GetComponentInChildren<FirstContactSemanticMapGraphic>(true);
            }

            _mapRect = mapGraphic != null ? mapGraphic.rectTransform : null;
            EnsureLabelRoot();
        }

        private void EnsureLabelRoot()
        {
            if (mapGraphic == null)
            {
                return;
            }

            if (_labelRoot == null)
            {
                Transform existing = mapGraphic.transform.Find(LabelRootName);
                _labelRoot = existing as RectTransform;
            }

            if (_labelRoot == null)
            {
                var root = new GameObject(LabelRootName, typeof(RectTransform));
                root.transform.SetParent(mapGraphic.transform, false);
                _labelRoot = root.GetComponent<RectTransform>();
            }

            _labelRoot.anchorMin = Vector2.zero;
            _labelRoot.anchorMax = Vector2.one;
            _labelRoot.offsetMin = Vector2.zero;
            _labelRoot.offsetMax = Vector2.zero;
            _labelRoot.pivot = new Vector2(0.5f, 0.5f);
        }

        private void ConfigureCurrentLayout()
        {
            if (_mapRect == null)
            {
                return;
            }

            float heightRatio = _fullMode ? fullMapHeightRatio : miniMapHeightRatio;
            float horizontalInset = Mathf.Clamp(horizontalInsetRatio, 0f, 0.12f);
            float topInset = Mathf.Clamp(topInsetRatio, 0f, 0.12f);
            _mapRect.anchorMin = new Vector2(horizontalInset, 1f - heightRatio);
            _mapRect.anchorMax = new Vector2(1f - horizontalInset, 1f - topInset);
            _mapRect.offsetMin = Vector2.zero;
            _mapRect.offsetMax = Vector2.zero;
            _mapRect.pivot = new Vector2(0.5f, 0.5f);

            if (_labelRoot != null)
            {
                _labelRoot.anchorMin = Vector2.zero;
                _labelRoot.anchorMax = Vector2.one;
                _labelRoot.offsetMin = Vector2.zero;
                _labelRoot.offsetMax = Vector2.zero;
            }
        }

        private void RebuildLabels(FirstContactSemanticMapSnapshot snapshot, bool fullMode)
        {
            ClearLabels();
            if (_labelRoot == null || snapshot == null || (!fullMode && !showMiniMapLabels))
            {
                return;
            }

            Rect rect = _labelRoot.rect;
            if (rect.width <= 1f || rect.height <= 1f)
            {
                Canvas.ForceUpdateCanvases();
                rect = _labelRoot.rect;
            }

            for (int i = 0; i < snapshot.Nodes.Count; i++)
            {
                FirstContactSemanticMapNode node = snapshot.Nodes[i];
                if (!ShouldShowLabel(node, fullMode))
                {
                    continue;
                }

                TextMeshProUGUI label = CreateLabel(node, fullMode);
                Vector2 point = FirstContactSemanticMapGraphic.MapToLocal(node.Position, rect);
                Vector2 offset = ResolveLabelOffset(node, fullMode);
                label.rectTransform.anchoredPosition = ClampLabelPosition(
                    point + offset,
                    rect,
                    label.rectTransform.sizeDelta);
            }
        }

        private static bool ShouldShowLabel(FirstContactSemanticMapNode node, bool fullMode)
        {
            if (node == null)
            {
                return false;
            }

            if (fullMode)
            {
                return true;
            }

            return node.IsActive ||
                   node.Kind == FirstContactSemanticMapNodeKind.UnknownSlot ||
                   node.Kind == FirstContactSemanticMapNodeKind.StableCluster;
        }

        private TextMeshProUGUI CreateLabel(FirstContactSemanticMapNode node, bool fullMode)
        {
            var labelObject = new GameObject($"Label_{node.Id}", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(_labelRoot, false);

            TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
            label.text = BuildLabelText(node, fullMode);
            label.color = ResolveLabelColor(node);
            label.fontStyle = FontStyles.Bold;
            label.alignment = TextAlignmentOptions.Left;
            label.raycastTarget = false;
            label.richText = false;
            label.enableAutoSizing = true;
            label.fontSizeMax = fullMode ? fullLabelFontSize : miniLabelFontSize;
            label.fontSizeMin = Mathf.Max(5f, label.fontSizeMax * 0.65f);
            label.characterSpacing = 0f;
            label.overflowMode = TextOverflowModes.Ellipsis;

            RectTransform rect = label.rectTransform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.sizeDelta = fullMode ? new Vector2(190f, 28f) : new Vector2(128f, 20f);
            _labels.Add(label);
            return label;
        }

        private static string BuildLabelText(FirstContactSemanticMapNode node, bool fullMode)
        {
            if (node == null)
            {
                return string.Empty;
            }

            return node.Kind switch
            {
                FirstContactSemanticMapNodeKind.UnknownSlot => string.IsNullOrWhiteSpace(node.SecondaryLabel)
                    ? $"[{node.Label}]"
                    : node.SecondaryLabel,
                FirstContactSemanticMapNodeKind.StableCluster => string.IsNullOrWhiteSpace(node.SecondaryLabel)
                    ? $"[{node.Label}]"
                    : node.SecondaryLabel,
                FirstContactSemanticMapNodeKind.Card => fullMode || node.IsActive ? node.Label : string.Empty,
                _ => node.Label
            };
        }

        private static Color ResolveLabelColor(FirstContactSemanticMapNode node)
        {
            if (node == null)
            {
                return Color.white;
            }

            if (node.IsActive)
            {
                return new Color(0.95f, 1f, 0.68f, 0.98f);
            }

            return node.Kind switch
            {
                FirstContactSemanticMapNodeKind.UnknownSlot => new Color(0.42f, 1f, 0.54f, 0.95f),
                FirstContactSemanticMapNodeKind.StableCluster => new Color(0.85f, 0.7f, 1f, 0.9f),
                FirstContactSemanticMapNodeKind.Card => new Color(0.45f, 0.92f, 1f, 0.82f),
                _ => Color.white
            };
        }

        private Vector2 ResolveLabelOffset(FirstContactSemanticMapNode node, bool fullMode)
        {
            float distance = labelOffset * (fullMode ? 1.2f : 0.85f);
            return node.Kind switch
            {
                FirstContactSemanticMapNodeKind.UnknownSlot => new Vector2(distance, distance * 0.35f),
                FirstContactSemanticMapNodeKind.StableCluster => new Vector2(distance, -distance * 0.45f),
                _ => new Vector2(distance, 0f)
            };
        }

        private static Vector2 ClampLabelPosition(Vector2 position, Rect rect, Vector2 size)
        {
            float halfHeight = size.y * 0.5f;
            return new Vector2(
                Mathf.Clamp(position.x, rect.xMin + 2f, rect.xMax - size.x - 2f),
                Mathf.Clamp(position.y, rect.yMin + halfHeight + 2f, rect.yMax - halfHeight - 2f));
        }

        private void ClearLabels()
        {
            for (int i = 0; i < _labels.Count; i++)
            {
                if (_labels[i] != null)
                {
                    Destroy(_labels[i].gameObject);
                }
            }

            _labels.Clear();
        }

        private void SetVisible(bool visible)
        {
            if (mapGraphic != null)
            {
                mapGraphic.gameObject.SetActive(visible);
            }
        }
    }

    public sealed class FirstContactSemanticMapGraphic : MaskableGraphic
    {
        [Header("Style")]
        [SerializeField] private Color backgroundColor = new(0f, 0.035f, 0.012f, 0.72f);
        [SerializeField] private Color gridColor = new(0.08f, 0.38f, 0.15f, 0.3f);
        [SerializeField] private Color cardColor = new(0.36f, 0.92f, 1f, 0.96f);
        [SerializeField] private Color activeCardColor = new(1f, 0.95f, 0.45f, 1f);
        [SerializeField] private Color unknownColor = new(0.28f, 1f, 0.43f, 1f);
        [SerializeField] private Color clusterColor = new(0.72f, 0.5f, 1f, 0.78f);
        [SerializeField] private Color weakLinkColor = new(0.22f, 0.8f, 0.42f, 0.36f);
        [SerializeField] private Color strongLinkColor = new(0.8f, 1f, 0.55f, 0.88f);

        private FirstContactSemanticMapSnapshot _snapshot;
        private bool _fullMode;

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
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

        public static Vector2 MapToLocal(Vector2 position, Rect rect)
        {
            float paddingX = Mathf.Max(8f, rect.width * 0.045f);
            float paddingY = Mathf.Max(8f, rect.height * 0.08f);
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

            DrawRect(vh, rect.min, rect.max, backgroundColor);
            DrawGrid(vh, rect);

            if (_snapshot == null || _snapshot.Nodes.Count == 0)
            {
                return;
            }

            DrawLinks(vh, rect);
            DrawClusters(vh, rect);
            DrawNodes(vh, rect);
        }

        private void DrawGrid(VertexHelper vh, Rect rect)
        {
            int verticalLines = _fullMode ? 8 : 5;
            int horizontalLines = _fullMode ? 5 : 3;
            float thickness = _fullMode ? 1.1f : 0.8f;

            for (int i = 1; i < verticalLines; i++)
            {
                float x = Mathf.Lerp(rect.xMin, rect.xMax, i / (float)verticalLines);
                DrawLine(vh, new Vector2(x, rect.yMin), new Vector2(x, rect.yMax), thickness, gridColor);
            }

            for (int i = 1; i < horizontalLines; i++)
            {
                float y = Mathf.Lerp(rect.yMin, rect.yMax, i / (float)horizontalLines);
                DrawLine(vh, new Vector2(rect.xMin, y), new Vector2(rect.xMax, y), thickness, gridColor);
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

                float normalized = Mathf.Clamp01((link.Strength + 1f) * 0.5f);
                Color color = Color.Lerp(weakLinkColor, strongLinkColor, normalized);
                if (from.IsActive || to.IsActive)
                {
                    color.a = Mathf.Max(color.a, 0.82f);
                }

                float thickness = Mathf.Lerp(_fullMode ? 1.4f : 1f, _fullMode ? 4.5f : 2.6f, normalized);
                DrawLine(vh, MapToLocal(from.Position, rect), MapToLocal(to.Position, rect), thickness, color);
            }
        }

        private void DrawClusters(VertexHelper vh, Rect rect)
        {
            for (int i = 0; i < _snapshot.Nodes.Count; i++)
            {
                FirstContactSemanticMapNode node = _snapshot.Nodes[i];
                if (node.Kind != FirstContactSemanticMapNodeKind.StableCluster)
                {
                    continue;
                }

                Vector2 center = MapToLocal(node.Position, rect);
                float radius = Mathf.Min(rect.width, rect.height) * (_fullMode ? 0.075f : 0.055f);
                DrawFilledCircle(vh, center, radius, new Color(clusterColor.r, clusterColor.g, clusterColor.b, 0.16f), 32);
                DrawRing(vh, center, radius, _fullMode ? 2.4f : 1.6f, clusterColor, 32);
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
            if (node.Kind == FirstContactSemanticMapNodeKind.StableCluster)
            {
                return 0;
            }

            return node.IsActive ? 2 : 1;
        }

        private void DrawNode(VertexHelper vh, Rect rect, FirstContactSemanticMapNode node)
        {
            Vector2 center = MapToLocal(node.Position, rect);
            float baseRadius = Mathf.Min(rect.width, rect.height);
            float radius = node.Kind switch
            {
                FirstContactSemanticMapNodeKind.UnknownSlot => baseRadius * (_fullMode ? 0.025f : 0.023f),
                FirstContactSemanticMapNodeKind.StableCluster => baseRadius * (_fullMode ? 0.022f : 0.018f),
                _ => baseRadius * (_fullMode ? 0.021f : 0.018f)
            };

            if (node.IsActive)
            {
                radius *= 1.45f;
            }

            Color nodeColor = ResolveNodeColor(node);
            if (node.Kind == FirstContactSemanticMapNodeKind.UnknownSlot)
            {
                DrawRing(vh, center, radius * 1.18f, Mathf.Max(1.6f, radius * 0.22f), nodeColor, 24);
                DrawFilledCircle(vh, center, radius * 0.48f, new Color(nodeColor.r, nodeColor.g, nodeColor.b, 0.35f), 20);
                return;
            }

            if (node.Kind == FirstContactSemanticMapNodeKind.StableCluster)
            {
                DrawFilledCircle(vh, center, radius, nodeColor, 20);
                return;
            }

            DrawFilledCircle(vh, center, radius, nodeColor, 20);
            if (node.IsActive)
            {
                DrawRing(vh, center, radius * 1.65f, Mathf.Max(1.6f, radius * 0.18f), activeCardColor, 28);
            }
        }

        private Color ResolveNodeColor(FirstContactSemanticMapNode node)
        {
            if (node.IsActive)
            {
                return activeCardColor;
            }

            return node.Kind switch
            {
                FirstContactSemanticMapNodeKind.UnknownSlot => unknownColor,
                FirstContactSemanticMapNodeKind.StableCluster => clusterColor,
                FirstContactSemanticMapNodeKind.Card => cardColor,
                _ => color
            };
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

        private static void DrawLine(VertexHelper vh, Vector2 start, Vector2 end, float thickness, Color drawColor)
        {
            Vector2 delta = end - start;
            if (delta.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            Vector2 normal = new Vector2(-delta.y, delta.x).normalized * (Mathf.Max(0.25f, thickness) * 0.5f);
            int first = vh.currentVertCount;
            AddVert(vh, start - normal, drawColor);
            AddVert(vh, start + normal, drawColor);
            AddVert(vh, end + normal, drawColor);
            AddVert(vh, end - normal, drawColor);
            vh.AddTriangle(first, first + 1, first + 2);
            vh.AddTriangle(first, first + 2, first + 3);
        }

        private static void DrawFilledCircle(
            VertexHelper vh,
            Vector2 center,
            float radius,
            Color drawColor,
            int segments)
        {
            int safeSegments = Mathf.Max(12, segments);
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

        private static void DrawRing(
            VertexHelper vh,
            Vector2 center,
            float radius,
            float thickness,
            Color drawColor,
            int segments)
        {
            int safeSegments = Mathf.Max(12, segments);
            Vector2 previous = center + new Vector2(radius, 0f);
            for (int i = 1; i <= safeSegments; i++)
            {
                float angle = (i / (float)safeSegments) * Mathf.PI * 2f;
                Vector2 next = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
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
