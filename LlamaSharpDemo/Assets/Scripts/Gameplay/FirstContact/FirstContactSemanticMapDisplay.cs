using System;
using System.Collections;
using System.Collections.Generic;
using DoodleDiplomacy.Devices;
using DoodleDiplomacy.Localization;
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
        private FirstContactSemanticMapSnapshot _currentSnapshot;
        private bool _fullMode;
        private Coroutine _transitionRoutine;
        private string _persistentPulseNodeId;
        private float _persistentPulseStartTime;

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

        private void Update()
        {
            if (string.IsNullOrWhiteSpace(_persistentPulseNodeId) ||
                _currentSnapshot == null ||
                mapGraphic == null ||
                !mapGraphic.gameObject.activeInHierarchy)
            {
                return;
            }

            FirstContactSemanticMapNode node = _currentSnapshot.FindNode(_persistentPulseNodeId);
            if (node == null)
            {
                ClearPersistentPulse();
                return;
            }

            node.Pulse = BuildPersistentNodePulse(Time.time - _persistentPulseStartTime);
            mapGraphic.Show(_currentSnapshot, _fullMode);
        }

        public void ShowMiniMap(FirstContactSemanticMapSnapshot snapshot)
        {
            StopTransition();
            ClearPersistentPulse();
            Show(snapshot, fullMode: false);
        }

        public void ShowFullMap(FirstContactSemanticMapSnapshot snapshot)
        {
            StopTransition();
            ClearPersistentPulse();
            Show(snapshot, fullMode: true);
        }

        public void ShowBootstrapResultTransition(
            FirstContactSemanticMapSnapshot beforeSnapshot,
            FirstContactSemanticMapSnapshot afterSnapshot,
            string activeCardNodeId,
            string categoryNodeId,
            bool accepted,
            bool becameStable)
        {
            StopTransition();
            ClearPersistentPulse();
            if (afterSnapshot == null || afterSnapshot.Nodes.Count == 0)
            {
                Clear();
                return;
            }

            _transitionRoutine = StartCoroutine(BootstrapResultTransitionRoutine(
                beforeSnapshot,
                afterSnapshot,
                activeCardNodeId,
                categoryNodeId,
                accepted,
                becameStable));
        }

        public void ShowClusterFormationTransition(
            FirstContactSemanticMapSnapshot beforeSnapshot,
            FirstContactSemanticMapSnapshot afterSnapshot,
            FirstContactClusterFormationEvent formation)
        {
            StopTransition();
            ClearPersistentPulse();
            if (afterSnapshot == null || afterSnapshot.Nodes.Count == 0)
            {
                Clear();
                return;
            }

            _transitionRoutine = StartCoroutine(ClusterFormationTransitionRoutine(
                beforeSnapshot,
                afterSnapshot,
                formation));
        }

        public void Clear(bool resetTerminalInset = true)
        {
            StopTransition();
            ClearPersistentPulse();
            _currentSnapshot = null;
            mapGraphic?.Clear();
            ClearLabels();
            SetVisible(false);
            if (resetTerminalInset)
            {
                terminalDisplay?.SetContentTopInsetNormalized(0f);
            }
        }

        private IEnumerator BootstrapResultTransitionRoutine(
            FirstContactSemanticMapSnapshot beforeSnapshot,
            FirstContactSemanticMapSnapshot afterSnapshot,
            string activeCardNodeId,
            string categoryNodeId,
            bool accepted,
            bool becameStable)
        {
            ClearLabels();
            const float duration = 1.35f;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                float progress = duration <= 0.0001f ? 1f : Mathf.Clamp01(elapsed / duration);
                FirstContactSemanticMapSnapshot frame = BuildTransitionSnapshot(
                    beforeSnapshot,
                    afterSnapshot,
                    activeCardNodeId,
                    categoryNodeId,
                    accepted,
                    becameStable,
                    progress);
                Show(frame, fullMode: true, rebuildLabels: false);
                elapsed += Time.deltaTime;
                yield return null;
            }

            Show(afterSnapshot, fullMode: true, rebuildLabels: true);
            StartPersistentPulse(activeCardNodeId);
            _transitionRoutine = null;
        }

        private IEnumerator ClusterFormationTransitionRoutine(
            FirstContactSemanticMapSnapshot beforeSnapshot,
            FirstContactSemanticMapSnapshot afterSnapshot,
            FirstContactClusterFormationEvent formation)
        {
            ClearLabels();
            float duration = formation.BecameStable ? 2.12f : formation.IsIsolated ? 1.28f : 1.62f;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                float progress = duration <= 0.0001f ? 1f : Mathf.Clamp01(elapsed / duration);
                FirstContactSemanticMapSnapshot frame = BuildClusterFormationSnapshot(
                    beforeSnapshot,
                    afterSnapshot,
                    formation,
                    progress);
                Show(frame, fullMode: true, rebuildLabels: true);
                elapsed += Time.deltaTime;
                yield return null;
            }

            Show(afterSnapshot, fullMode: true, rebuildLabels: true);
            StartPersistentPulse(formation.IsStable && !string.IsNullOrWhiteSpace(formation.ClusterNodeId)
                ? formation.ClusterNodeId
                : formation.ActiveCardNodeId);
            _transitionRoutine = null;
        }

        private void StopTransition()
        {
            if (_transitionRoutine == null)
            {
                return;
            }

            StopCoroutine(_transitionRoutine);
            _transitionRoutine = null;
        }

        private void Show(FirstContactSemanticMapSnapshot snapshot, bool fullMode)
        {
            Show(snapshot, fullMode, rebuildLabels: true);
        }

        private void Show(
            FirstContactSemanticMapSnapshot snapshot,
            bool fullMode,
            bool rebuildLabels)
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
            _currentSnapshot = snapshot;
            ConfigureCurrentLayout();
            graphic.Show(snapshot, fullMode);
            SetVisible(true);
            terminalDisplay?.SetContentTopInsetNormalized(fullMode ? fullTextTopInsetRatio : miniTextTopInsetRatio);
            if (rebuildLabels)
            {
                RebuildLabels(snapshot, fullMode);
            }
        }

        private static FirstContactSemanticMapSnapshot BuildTransitionSnapshot(
            FirstContactSemanticMapSnapshot beforeSnapshot,
            FirstContactSemanticMapSnapshot afterSnapshot,
            string activeCardNodeId,
            string categoryNodeId,
            bool accepted,
            bool becameStable,
            float progress)
        {
            var frame = new FirstContactSemanticMapSnapshot();
            if (afterSnapshot == null)
            {
                return frame;
            }

            float eased = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress));
            FirstContactSemanticMapNode afterCategory = afterSnapshot.FindNode(categoryNodeId);
            for (int i = 0; i < afterSnapshot.Nodes.Count; i++)
            {
                FirstContactSemanticMapNode after = afterSnapshot.Nodes[i];
                if (after == null)
                {
                    continue;
                }

                FirstContactSemanticMapNode before = beforeSnapshot?.FindNode(after.Id);
                FirstContactSemanticMapNode node = CloneNode(after);
                Vector2 start = ResolveTransitionStartPosition(before, after, afterCategory, activeCardNodeId, accepted);
                node.Position = ResolveTransitionPosition(
                    after,
                    afterCategory,
                    start,
                    activeCardNodeId,
                    accepted,
                    eased);
                if (string.Equals(after.Id, categoryNodeId, StringComparison.Ordinal) && accepted)
                {
                    node.Pulse = Mathf.Sin(Mathf.Clamp01(progress) * Mathf.PI) * (becameStable ? 1.35f : 1f);
                }
                else if (string.Equals(after.Id, activeCardNodeId, StringComparison.Ordinal))
                {
                    node.Pulse = BuildNewNodePulse(progress);
                }

                frame.Nodes.Add(node);
            }

            for (int i = 0; i < afterSnapshot.Links.Count; i++)
            {
                FirstContactSemanticMapLink afterLink = afterSnapshot.Links[i];
                float startStrength = FindLinkStrength(beforeSnapshot, afterLink.FromId, afterLink.ToId);
                float strength = Mathf.Lerp(startStrength, afterLink.Strength, eased);
                frame.Links.Add(new FirstContactSemanticMapLink(afterLink.FromId, afterLink.ToId, strength));
            }

            if (accepted &&
                !string.IsNullOrWhiteSpace(activeCardNodeId) &&
                !string.IsNullOrWhiteSpace(categoryNodeId) &&
                frame.FindNode(activeCardNodeId) != null &&
                frame.FindNode(categoryNodeId) != null)
            {
                float signalStrength = Mathf.Sin(Mathf.Clamp01(progress) * Mathf.PI);
                if (signalStrength > 0.01f)
                {
                    frame.Links.Add(new FirstContactSemanticMapLink(
                        activeCardNodeId,
                        categoryNodeId,
                        signalStrength));
                }
            }

            return frame;
        }

        private static FirstContactSemanticMapSnapshot BuildClusterFormationSnapshot(
            FirstContactSemanticMapSnapshot beforeSnapshot,
            FirstContactSemanticMapSnapshot afterSnapshot,
            FirstContactClusterFormationEvent formation,
            float progress)
        {
            var frame = new FirstContactSemanticMapSnapshot();
            if (afterSnapshot == null)
            {
                return frame;
            }

            float normalized = Mathf.Clamp01(progress);
            float eased = Mathf.SmoothStep(0f, 1f, normalized);
            float scanPulse = Mathf.Sin(normalized * Mathf.PI);
            bool lockPhase = formation.BecameStable && normalized >= 0.52f;
            bool labelGlitch = formation.BecameStable && normalized >= 0.52f && normalized <= 0.68f;
            FirstContactSemanticMapNode afterActive = afterSnapshot.FindNode(formation.ActiveCardNodeId);
            FirstContactSemanticMapNode afterCluster = afterSnapshot.FindNode(formation.ClusterNodeId);

            for (int i = 0; i < afterSnapshot.Nodes.Count; i++)
            {
                FirstContactSemanticMapNode after = afterSnapshot.Nodes[i];
                if (after == null)
                {
                    continue;
                }

                FirstContactSemanticMapNode before = beforeSnapshot?.FindNode(after.Id);
                FirstContactSemanticMapNode node = CloneNode(after);
                Vector2 start = ResolveClusterFormationStartPosition(
                    before,
                    after,
                    afterActive,
                    afterCluster,
                    formation.ActiveCardNodeId,
                    formation.ClusterNodeId);
                node.Position = Vector2.Lerp(start, after.Position, eased);

                if (string.Equals(after.Id, formation.ActiveCardNodeId, StringComparison.Ordinal))
                {
                    node.Pulse = Mathf.Max(node.Pulse, BuildNewNodePulse(normalized));
                }
                else if (string.Equals(after.Id, formation.ClusterNodeId, StringComparison.Ordinal))
                {
                    node.Pulse = Mathf.Max(node.Pulse, scanPulse * (formation.BecameStable ? 1.9f : 1.15f));
                    if (labelGlitch)
                    {
                        node.SecondaryLabel = LocalizedGroupUnknownLabel();
                    }
                }
                else if (IsFormationMember(formation, after.Id))
                {
                    float memberPulse = lockPhase
                        ? BuildSynchronizedMemberPulse(normalized)
                        : scanPulse * 0.48f;
                    node.Pulse = Mathf.Max(node.Pulse, memberPulse);
                }

                frame.Nodes.Add(node);
            }

            if (formation.MemberCount >= 3 && string.IsNullOrWhiteSpace(formation.ClusterNodeId))
            {
                AddTransientFormationRing(frame, afterSnapshot, formation, scanPulse);
            }

            for (int i = 0; i < afterSnapshot.Links.Count; i++)
            {
                FirstContactSemanticMapLink afterLink = afterSnapshot.Links[i];
                float startStrength = FindLinkStrength(beforeSnapshot, afterLink.FromId, afterLink.ToId);
                float strength = Mathf.Lerp(startStrength, afterLink.Strength, eased);
                if (IsFormationFocusLink(afterLink, formation.ActiveCardNodeId, formation.ClusterNodeId))
                {
                    strength = Mathf.Max(strength, scanPulse * (formation.BecameStable ? 1f : 0.72f));
                }

                AddOrBoostLink(frame, new FirstContactSemanticMapLink(afterLink.FromId, afterLink.ToId, strength));
            }

            AddFormationCandidateLinks(frame, formation, normalized);

            return frame;
        }

        private static void AddFormationCandidateLinks(
            FirstContactSemanticMapSnapshot frame,
            FirstContactClusterFormationEvent formation,
            float progress)
        {
            if (frame == null || formation.CandidateEdges == null)
            {
                return;
            }

            for (int i = 0; i < formation.CandidateEdges.Length; i++)
            {
                FirstContactClusterFormationEdge edge = formation.CandidateEdges[i];
                if (frame.FindNode(edge.FromNodeId) == null || frame.FindNode(edge.ToNodeId) == null)
                {
                    continue;
                }

                float start = 0.16f + i * 0.14f;
                float scanEnd = start + 0.24f;
                float settleEnd = scanEnd + 0.32f;
                if (progress < start)
                {
                    continue;
                }

                float localScan = Mathf.Clamp01((progress - start) / Mathf.Max(0.0001f, scanEnd - start));
                if (progress <= scanEnd)
                {
                    float pulse = Mathf.Sin(localScan * Mathf.PI);
                    AddOrBoostLink(frame, new FirstContactSemanticMapLink(
                        edge.FromNodeId,
                        edge.ToNodeId,
                        Mathf.Lerp(0.18f, Mathf.Max(0.36f, edge.Strength), pulse),
                        FirstContactSemanticMapLinkKind.Candidate));
                    continue;
                }

                float localSettle = Mathf.Clamp01((progress - scanEnd) / Mathf.Max(0.0001f, settleEnd - scanEnd));
                if (edge.Confirmed)
                {
                    float strength = Mathf.Lerp(Mathf.Max(0.42f, edge.Strength), 1f, Mathf.SmoothStep(0f, 1f, localSettle));
                    AddOrBoostLink(frame, new FirstContactSemanticMapLink(
                        edge.FromNodeId,
                        edge.ToNodeId,
                        strength,
                        FirstContactSemanticMapLinkKind.Confirmed));
                    continue;
                }

                float fade = 1f - Mathf.SmoothStep(0f, 1f, localSettle);
                if (fade > 0.03f)
                {
                    AddOrBoostLink(frame, new FirstContactSemanticMapLink(
                        edge.FromNodeId,
                        edge.ToNodeId,
                        Mathf.Max(0.08f, edge.Strength) * fade,
                        FirstContactSemanticMapLinkKind.Rejected));
                }
            }
        }

        private static void AddTransientFormationRing(
            FirstContactSemanticMapSnapshot frame,
            FirstContactSemanticMapSnapshot afterSnapshot,
            FirstContactClusterFormationEvent formation,
            float pulse)
        {
            if (frame == null || afterSnapshot == null)
            {
                return;
            }

            if (!TryResolveMemberCenter(afterSnapshot, formation.MemberNodeIds, out Vector2 center))
            {
                FirstContactSemanticMapNode active = afterSnapshot.FindNode(formation.ActiveCardNodeId);
                center = active != null ? active.Position : Vector2.zero;
            }

            frame.Nodes.Add(new FirstContactSemanticMapNode
            {
                Id = $"F:{formation.ActiveCardNodeId}",
                Label = LocalizedGroupLabel(),
                SecondaryLabel = LocalizedGroupUnknownLabel(),
                Kind = FirstContactSemanticMapNodeKind.StableCluster,
                Position = center,
                IsActive = false,
                Marker = '#',
                Pulse = Mathf.Max(0.18f, pulse * 1.18f)
            });
        }

        private static bool TryResolveMemberCenter(
            FirstContactSemanticMapSnapshot snapshot,
            string[] memberNodeIds,
            out Vector2 center)
        {
            center = Vector2.zero;
            if (snapshot == null || memberNodeIds == null || memberNodeIds.Length == 0)
            {
                return false;
            }

            int count = 0;
            for (int i = 0; i < memberNodeIds.Length; i++)
            {
                FirstContactSemanticMapNode node = snapshot.FindNode(memberNodeIds[i]);
                if (node == null)
                {
                    continue;
                }

                center += node.Position;
                count++;
            }

            if (count <= 0)
            {
                return false;
            }

            center /= count;
            return true;
        }

        private static bool IsFormationMember(
            FirstContactClusterFormationEvent formation,
            string nodeId)
        {
            if (formation.MemberNodeIds == null || string.IsNullOrWhiteSpace(nodeId))
            {
                return false;
            }

            for (int i = 0; i < formation.MemberNodeIds.Length; i++)
            {
                if (string.Equals(formation.MemberNodeIds[i], nodeId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static float BuildSynchronizedMemberPulse(float progress)
        {
            float lockProgress = Mathf.Clamp01((progress - 0.52f) / 0.34f);
            float envelope = Mathf.Sin(lockProgress * Mathf.PI);
            float flash = Mathf.Sin(lockProgress * Mathf.PI * 6f);
            return Mathf.Clamp01(0.32f + flash * flash * envelope * 1.18f);
        }

        private static float BuildNewNodePulse(float progress)
        {
            float normalized = Mathf.Clamp01(progress);
            float envelope = Mathf.SmoothStep(1f, 0f, normalized);
            float flashes = Mathf.Sin(normalized * Mathf.PI * 5.5f);
            return Mathf.Clamp01(flashes * flashes * envelope);
        }

        private static float BuildPersistentNodePulse(float elapsed)
        {
            float wave = (Mathf.Sin(Mathf.Max(0f, elapsed) * Mathf.PI * 2f / 1.35f) + 1f) * 0.5f;
            return Mathf.Lerp(0.38f, 0.92f, wave);
        }

        private void StartPersistentPulse(string nodeId)
        {
            if (string.IsNullOrWhiteSpace(nodeId) || _currentSnapshot?.FindNode(nodeId) == null)
            {
                ClearPersistentPulse();
                return;
            }

            _persistentPulseNodeId = nodeId;
            _persistentPulseStartTime = Time.time;
        }

        private void ClearPersistentPulse()
        {
            if (!string.IsNullOrWhiteSpace(_persistentPulseNodeId) && _currentSnapshot != null)
            {
                FirstContactSemanticMapNode node = _currentSnapshot.FindNode(_persistentPulseNodeId);
                if (node != null)
                {
                    node.Pulse = 0f;
                }
            }

            _persistentPulseNodeId = string.Empty;
            _persistentPulseStartTime = 0f;
        }

        private static Vector2 ResolveTransitionStartPosition(
            FirstContactSemanticMapNode before,
            FirstContactSemanticMapNode after,
            FirstContactSemanticMapNode afterCategory,
            string activeCardNodeId,
            bool accepted)
        {
            if (before != null)
            {
                return before.Position;
            }

            if (after != null && string.Equals(after.Id, activeCardNodeId, StringComparison.Ordinal))
            {
                float x = afterCategory != null
                    ? Mathf.Lerp(afterCategory.Position.x, after.Position.x, accepted ? 0.25f : 0.5f)
                    : after.Position.x;
                return new Vector2(Mathf.Clamp(x, -0.78f, 0.78f), 0.92f);
            }

            return after?.Position ?? Vector2.zero;
        }

        private static Vector2 ResolveTransitionPosition(
            FirstContactSemanticMapNode after,
            FirstContactSemanticMapNode afterCategory,
            Vector2 start,
            string activeCardNodeId,
            bool accepted,
            float eased)
        {
            if (after == null)
            {
                return start;
            }

            if (!string.Equals(after.Id, activeCardNodeId, StringComparison.Ordinal) ||
                accepted ||
                afterCategory == null)
            {
                return Vector2.Lerp(start, after.Position, eased);
            }

            Vector2 nearCategory = Vector2.Lerp(start, afterCategory.Position, 0.58f);
            if (eased < 0.58f)
            {
                return Vector2.Lerp(start, nearCategory, Mathf.SmoothStep(0f, 1f, eased / 0.58f));
            }

            return Vector2.Lerp(
                nearCategory,
                after.Position,
                Mathf.SmoothStep(0f, 1f, (eased - 0.58f) / 0.42f));
        }

        private static Vector2 ResolveClusterFormationStartPosition(
            FirstContactSemanticMapNode before,
            FirstContactSemanticMapNode after,
            FirstContactSemanticMapNode afterActive,
            FirstContactSemanticMapNode afterCluster,
            string activeCardNodeId,
            string clusterNodeId)
        {
            if (before != null)
            {
                return before.Position;
            }

            if (after == null)
            {
                return Vector2.zero;
            }

            if (string.Equals(after.Id, activeCardNodeId, StringComparison.Ordinal))
            {
                float x = afterCluster != null
                    ? Mathf.Lerp(afterCluster.Position.x, after.Position.x, 0.42f)
                    : after.Position.x;
                return new Vector2(Mathf.Clamp(x, -0.78f, 0.78f), 0.92f);
            }

            if (string.Equals(after.Id, clusterNodeId, StringComparison.Ordinal) && afterActive != null)
            {
                return afterActive.Position;
            }

            return after.Position;
        }

        private static float FindLinkStrength(
            FirstContactSemanticMapSnapshot snapshot,
            string fromId,
            string toId)
        {
            if (snapshot == null)
            {
                return 0f;
            }

            for (int i = 0; i < snapshot.Links.Count; i++)
            {
                FirstContactSemanticMapLink link = snapshot.Links[i];
                bool same = string.Equals(link.FromId, fromId, StringComparison.Ordinal) &&
                            string.Equals(link.ToId, toId, StringComparison.Ordinal);
                bool reverse = string.Equals(link.FromId, toId, StringComparison.Ordinal) &&
                               string.Equals(link.ToId, fromId, StringComparison.Ordinal);
                if (same || reverse)
                {
                    return link.Strength;
                }
            }

            return 0f;
        }

        private static bool IsLinkedTo(
            FirstContactSemanticMapSnapshot snapshot,
            string nodeId,
            string activeCardNodeId,
            string clusterNodeId)
        {
            if (snapshot == null || string.IsNullOrWhiteSpace(nodeId))
            {
                return false;
            }

            for (int i = 0; i < snapshot.Links.Count; i++)
            {
                FirstContactSemanticMapLink link = snapshot.Links[i];
                if (!LinkContains(link, nodeId))
                {
                    continue;
                }

                if (LinkContains(link, activeCardNodeId) || LinkContains(link, clusterNodeId))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsFormationFocusLink(
            FirstContactSemanticMapLink link,
            string activeCardNodeId,
            string clusterNodeId)
        {
            return LinkContains(link, activeCardNodeId) || LinkContains(link, clusterNodeId);
        }

        private static bool LinkContains(FirstContactSemanticMapLink link, string nodeId)
        {
            return !string.IsNullOrWhiteSpace(nodeId) &&
                   (string.Equals(link.FromId, nodeId, StringComparison.Ordinal) ||
                    string.Equals(link.ToId, nodeId, StringComparison.Ordinal));
        }

        private static void AddOrBoostLink(
            FirstContactSemanticMapSnapshot snapshot,
            FirstContactSemanticMapLink link)
        {
            if (snapshot == null ||
                string.IsNullOrWhiteSpace(link.FromId) ||
                string.IsNullOrWhiteSpace(link.ToId))
            {
                return;
            }

            for (int i = 0; i < snapshot.Links.Count; i++)
            {
                FirstContactSemanticMapLink existing = snapshot.Links[i];
                bool sameDirection = string.Equals(existing.FromId, link.FromId, StringComparison.Ordinal) &&
                                     string.Equals(existing.ToId, link.ToId, StringComparison.Ordinal);
                bool reverseDirection = string.Equals(existing.FromId, link.ToId, StringComparison.Ordinal) &&
                                        string.Equals(existing.ToId, link.FromId, StringComparison.Ordinal);
                if (sameDirection || reverseDirection)
                {
                    bool preferFormationLink =
                        existing.Kind == FirstContactSemanticMapLinkKind.Normal &&
                        link.Kind != FirstContactSemanticMapLinkKind.Normal;
                    bool preferStrongerSameKind =
                        existing.Kind == link.Kind &&
                        link.Strength > existing.Strength;
                    bool preferConfirmed =
                        link.Kind == FirstContactSemanticMapLinkKind.Confirmed &&
                        existing.Kind != FirstContactSemanticMapLinkKind.Confirmed;
                    if (preferFormationLink || preferStrongerSameKind || preferConfirmed)
                    {
                        snapshot.Links[i] = link;
                    }

                    return;
                }
            }

            snapshot.Links.Add(link);
        }

        private static FirstContactSemanticMapNode CloneNode(FirstContactSemanticMapNode node)
        {
            return new FirstContactSemanticMapNode
            {
                Id = node.Id,
                Label = node.Label,
                SecondaryLabel = node.SecondaryLabel,
                Kind = node.Kind,
                Position = node.Position,
                Embedding = node.Embedding,
                IsActive = node.IsActive,
                Marker = node.Marker,
                BootstrapCategoryId = node.BootstrapCategoryId,
                IsBootstrapDetached = node.IsBootstrapDetached,
                TraceCount = node.TraceCount,
                RequiredTraceCount = node.RequiredTraceCount,
                IsBootstrapCategoryStable = node.IsBootstrapCategoryStable,
                Pulse = node.Pulse
            };
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
                   node.Kind == FirstContactSemanticMapNodeKind.UnknownSlot ||
                   node.Kind == FirstContactSemanticMapNodeKind.StableCluster ||
                   node.Kind == FirstContactSemanticMapNodeKind.BootstrapCategory;
        }

        private TextMeshProUGUI CreateLabel(FirstContactSemanticMapNode node, bool fullMode)
        {
            var labelObject = new GameObject($"Label_{node.Id}", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(_labelRoot, false);

            TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
            label.text = BuildLabelText(node, fullMode);
            label.color = ResolveLabelColor(node);
            TMP_FontAsset localizedFont = L10n.CurrentFont;
            if (localizedFont != null)
            {
                label.font = localizedFont;
            }

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
                FirstContactSemanticMapNodeKind.UnknownSlot => BuildUnknownSlotLabel(node),
                FirstContactSemanticMapNodeKind.StableCluster => BuildStableClusterLabel(node),
                FirstContactSemanticMapNodeKind.BootstrapCategory => BuildBootstrapCategoryLabel(node),
                FirstContactSemanticMapNodeKind.Card => fullMode || node.IsActive ? node.Label : string.Empty,
                _ => node.Label
            };
        }

        private static string BuildUnknownSlotLabel(FirstContactSemanticMapNode node)
        {
            if (!string.IsNullOrWhiteSpace(node.SecondaryLabel))
            {
                return FirstContactTerminalLocalization.LocalizeToken(node.SecondaryLabel).ToUpperInvariant();
            }

            string fallback = string.IsNullOrWhiteSpace(node.Label)
                ? L10n.T("first_contact.terminal.token.object", "[OBJECT?]")
                : $"[{node.Label.Trim()}]";
            return FirstContactTerminalLocalization.LocalizeToken(fallback).ToUpperInvariant();
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
                FirstContactSemanticMapNodeKind.BootstrapCategory => node.IsBootstrapCategoryStable
                    ? new Color(0.98f, 0.9f, 0.48f, 0.96f)
                    : new Color(0.72f, 0.95f, 0.52f, 0.86f),
                FirstContactSemanticMapNodeKind.Card => new Color(0.45f, 0.92f, 1f, 0.82f),
                _ => Color.white
            };
        }

        private Vector2 ResolveLabelOffset(FirstContactSemanticMapNode node, bool fullMode)
        {
            float distance = labelOffset * (fullMode ? 1.2f : 0.85f);
            if (node.Kind == FirstContactSemanticMapNodeKind.Card &&
                !string.IsNullOrWhiteSpace(node.BootstrapCategoryId))
            {
                if (node.IsBootstrapDetached)
                {
                    return new Vector2(-distance * 4.2f, -distance * 0.25f);
                }

                return node.IsActive
                    ? new Vector2(-distance * 2.8f, distance * 1.05f)
                    : new Vector2(-distance * 2.4f, distance * 0.75f);
            }

            return node.Kind switch
            {
                FirstContactSemanticMapNodeKind.UnknownSlot => new Vector2(distance, distance * 0.35f),
                FirstContactSemanticMapNodeKind.StableCluster => new Vector2(distance, -distance * 0.45f),
                FirstContactSemanticMapNodeKind.BootstrapCategory => new Vector2(distance * 1.35f, -distance * 0.85f),
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
        [SerializeField] private Color bootstrapCategoryColor = new(0.78f, 1f, 0.42f, 0.82f);
        [SerializeField] private Color stableBootstrapCategoryColor = new(1f, 0.86f, 0.28f, 0.94f);
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

            DrawBootstrapCategoryFields(vh, rect);
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
                        Mathf.Lerp(0.08f, 0.58f, normalizedSignal));
                    float signalThickness = Mathf.Lerp(_fullMode ? 1.2f : 0.8f, _fullMode ? 4.2f : 2.8f, normalizedSignal);
                    DrawLine(vh, MapToLocal(from.Position, rect), MapToLocal(to.Position, rect), signalThickness, signalColor);
                    continue;
                }

                float normalized = Mathf.Clamp01((link.Strength + 1f) * 0.5f);
                Color color = Color.Lerp(weakLinkColor, strongLinkColor, normalized);
                if (from.IsActive || to.IsActive)
                {
                    color.a = Mathf.Max(color.a, 0.95f);
                }

                float thickness = Mathf.Lerp(_fullMode ? 1.4f : 1f, _fullMode ? 4.5f : 2.6f, normalized);
                if (from.IsActive || to.IsActive)
                {
                    thickness += _fullMode ? 1.5f : 0.8f;
                }

                DrawLine(vh, MapToLocal(from.Position, rect), MapToLocal(to.Position, rect), thickness, color);
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
                    color = Color.Lerp(new Color(0.72f, 1f, 0.72f, 0.82f), strongLinkColor, normalized);
                    color.a = Mathf.Max(color.a, 0.88f);
                    thickness = Mathf.Lerp(_fullMode ? 2.8f : 1.8f, _fullMode ? 6.2f : 3.8f, normalized);
                    break;
                case FirstContactSemanticMapLinkKind.Rejected:
                    color = new Color(0.44f, 0.72f, 0.95f, Mathf.Lerp(0.08f, 0.34f, normalized));
                    thickness = Mathf.Lerp(_fullMode ? 0.7f : 0.5f, _fullMode ? 2.1f : 1.4f, normalized);
                    break;
                default:
                    color = new Color(0.46f, 0.96f, 1f, Mathf.Lerp(0.18f, 0.76f, normalized));
                    thickness = Mathf.Lerp(_fullMode ? 1.1f : 0.8f, _fullMode ? 3.6f : 2.2f, normalized);
                    break;
            }

            DrawLine(vh, MapToLocal(from.Position, rect), MapToLocal(to.Position, rect), thickness, color);
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
                float fillAlpha = Mathf.Lerp(0.055f, categoryNode.IsBootstrapCategoryStable ? 0.18f : 0.13f, traceRatio);
                DrawFilledEllipse(vh, center, radii, new Color(fieldColor.r, fieldColor.g, fieldColor.b, fillAlpha), 48);

                float ringAlpha = Mathf.Lerp(0.32f, categoryNode.IsBootstrapCategoryStable ? 0.82f : 0.62f, traceRatio);
                float ringThickness = _fullMode ? 2.8f : 1.8f;
                DrawEllipseRing(vh, center, radii, ringThickness, new Color(fieldColor.r, fieldColor.g, fieldColor.b, ringAlpha), 54);

                if (acceptedCount > 1)
                {
                    Vector2 innerRadii = radii * 0.86f;
                    DrawEllipseRing(
                        vh,
                        center,
                        innerRadii,
                        _fullMode ? 1.2f : 0.8f,
                        new Color(fieldColor.r, fieldColor.g, fieldColor.b, Mathf.Min(0.36f, ringAlpha * 0.55f)),
                        54);
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
            Vector2 categoryCenter = MapToLocal(categoryNode.Position, rect);
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

                Vector2 point = MapToLocal(node.Position, rect);
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
                acceptedCount++;
            }

            center = (min + max) * 0.5f;
            float baseSize = Mathf.Min(rect.width, rect.height);
            float padding = baseSize * (_fullMode ? 0.09f : 0.065f);
            float minRadius = baseSize * (_fullMode ? 0.18f : 0.12f);
            radii = new Vector2(
                Mathf.Max(minRadius, (max.x - min.x) * 0.5f + padding),
                Mathf.Max(minRadius * 0.72f, (max.y - min.y) * 0.5f + padding));

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

                Vector2 center = MapToLocal(node.Position, rect);
                float radius = Mathf.Min(rect.width, rect.height) * (_fullMode ? 0.075f : 0.055f);
                Color cluster = ResolveNodeColor(node);
                float traceRatio = node.RequiredTraceCount > 0
                    ? Mathf.Clamp01(node.TraceCount / (float)node.RequiredTraceCount)
                    : 1f;
                float fillAlpha = node.Kind == FirstContactSemanticMapNodeKind.BootstrapCategory
                    ? Mathf.Lerp(0.08f, 0.2f, traceRatio)
                    : 0.16f;
                DrawFilledCircle(vh, center, radius, new Color(cluster.r, cluster.g, cluster.b, fillAlpha), 32);
                DrawRing(vh, center, radius, _fullMode ? 2.4f : 1.6f, cluster, 32);
                if (node.Pulse > 0.001f)
                {
                    Color pulseColor = new(cluster.r, cluster.g, cluster.b, Mathf.Clamp01(0.55f * node.Pulse));
                    DrawRing(vh, center, radius * (1.08f + 0.22f * node.Pulse), _fullMode ? 3.4f : 2.2f, pulseColor, 36);
                    if (node.Pulse > 1.05f)
                    {
                        Color lockColor = new(cluster.r, cluster.g, cluster.b, Mathf.Clamp01(0.32f * node.Pulse));
                        DrawRing(vh, center, radius * (1.62f + 0.18f * node.Pulse), _fullMode ? 2.2f : 1.5f, lockColor, 42);
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
            Vector2 center = MapToLocal(node.Position, rect);
            float baseRadius = Mathf.Min(rect.width, rect.height);
            float radius = node.Kind switch
            {
                FirstContactSemanticMapNodeKind.UnknownSlot => baseRadius * (_fullMode ? 0.025f : 0.023f),
                FirstContactSemanticMapNodeKind.StableCluster => baseRadius * (_fullMode ? 0.022f : 0.018f),
                FirstContactSemanticMapNodeKind.BootstrapCategory => baseRadius * (_fullMode ? 0.024f : 0.02f),
                _ => baseRadius * (_fullMode ? 0.021f : 0.018f)
            };

            if (node.IsActive)
            {
                radius *= 1.6f + node.Pulse * 0.35f;
            }

            Color nodeColor = ResolveNodeColor(node);
            if (node.Kind == FirstContactSemanticMapNodeKind.UnknownSlot)
            {
                DrawRing(vh, center, radius * 1.18f, Mathf.Max(1.6f, radius * 0.22f), nodeColor, 24);
                DrawFilledCircle(vh, center, radius * 0.48f, new Color(nodeColor.r, nodeColor.g, nodeColor.b, 0.35f), 20);
                return;
            }

            if (node.Kind == FirstContactSemanticMapNodeKind.StableCluster ||
                node.Kind == FirstContactSemanticMapNodeKind.BootstrapCategory)
            {
                DrawFilledCircle(vh, center, radius, nodeColor, 20);
                if (node.Kind == FirstContactSemanticMapNodeKind.BootstrapCategory && node.TraceCount > 0)
                {
                    DrawRing(vh, center, radius * 1.75f, Mathf.Max(1.4f, radius * 0.14f), nodeColor, 28);
                }
                return;
            }

            DrawFilledCircle(vh, center, radius, nodeColor, 20);
            if (node.Pulse > 0.001f)
            {
                Color glowColor = new(nodeColor.r, nodeColor.g, nodeColor.b, Mathf.Clamp01(0.28f * node.Pulse));
                Color pulseColor = new(nodeColor.r, nodeColor.g, nodeColor.b, Mathf.Clamp01(0.98f * node.Pulse));
                DrawFilledCircle(vh, center, radius * (1.28f + 0.16f * node.Pulse), glowColor, 24);
                DrawRing(vh, center, radius * (1.55f + 0.42f * node.Pulse), Mathf.Max(2.8f, radius * 0.28f), pulseColor, 32);
                DrawRing(vh, center, radius * (2.25f + 0.78f * node.Pulse), Mathf.Max(1.9f, radius * 0.17f), new Color(pulseColor.r, pulseColor.g, pulseColor.b, pulseColor.a * 0.58f), 36);
            }

            if (node.IsActive)
            {
                DrawRing(vh, center, radius * 1.65f, Mathf.Max(2.4f, radius * 0.22f), activeCardColor, 28);
            }
        }

        private Color ResolveNodeColor(FirstContactSemanticMapNode node)
        {
            if (node.IsActive)
            {
                if (node.IsBootstrapDetached)
                {
                    return new Color(0.52f, 0.84f, 1f, 0.96f);
                }

                return activeCardColor;
            }

            if (node.IsBootstrapDetached)
            {
                return new Color(0.3f, 0.66f, 0.92f, 0.66f);
            }

            return node.Kind switch
            {
                FirstContactSemanticMapNodeKind.UnknownSlot => unknownColor,
                FirstContactSemanticMapNodeKind.StableCluster => clusterColor,
                FirstContactSemanticMapNodeKind.BootstrapCategory => node.IsBootstrapCategoryStable
                    ? stableBootstrapCategoryColor
                    : bootstrapCategoryColor,
                FirstContactSemanticMapNodeKind.Card => cardColor,
                _ => color
            };
        }

        private Color ResolveBootstrapFieldColor(FirstContactSemanticMapNode node)
        {
            if (node == null)
            {
                return bootstrapCategoryColor;
            }

            return node.IsBootstrapCategoryStable
                ? stableBootstrapCategoryColor
                : bootstrapCategoryColor;
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

        private static void DrawFilledEllipse(
            VertexHelper vh,
            Vector2 center,
            Vector2 radii,
            Color drawColor,
            int segments)
        {
            int safeSegments = Mathf.Max(12, segments);
            Vector2 safeRadii = new(Mathf.Max(1f, radii.x), Mathf.Max(1f, radii.y));
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

        private static void DrawEllipseRing(
            VertexHelper vh,
            Vector2 center,
            Vector2 radii,
            float thickness,
            Color drawColor,
            int segments)
        {
            int safeSegments = Mathf.Max(12, segments);
            Vector2 safeRadii = new(Mathf.Max(1f, radii.x), Mathf.Max(1f, radii.y));
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
