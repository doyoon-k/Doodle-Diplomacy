using System;
using System.Collections;
using DoodleDiplomacy.Devices;
using UnityEngine;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TerminalDisplay))]
    public sealed class FirstContactSemanticMapDisplay : MonoBehaviour
    {
        private const string DefaultMapName = "FirstContactSemanticMap";
        private const string LabelRootName = "SemanticMapLabels";

        [Header("References")]
        [Tooltip("의미 공간 맵을 표시할 터미널입니다. 비워 두면 같은 GameObject의 TerminalDisplay를 사용합니다.")]
        [SerializeField] private TerminalDisplay terminalDisplay;
        [Tooltip("터미널 화면 안에 표시할 의미 공간 그래픽입니다. 비워 두면 런타임에 자동 생성합니다.")]
        [SerializeField] private FirstContactSemanticMapGraphic mapGraphic;
        [Tooltip("프리팹에서 배치한 의미 맵 RectTransform을 유지합니다. 비활성화하면 스타일 에셋의 비율로 런타임 배치합니다.")]
        [SerializeField] private bool preserveAuthoredMapLayout = true;

        private readonly FirstContactSemanticMapLabelLayer _labelLayer = new();
        private RectTransform _mapRect;
        private RectTransform _labelRoot;
        private FirstContactSemanticMapSnapshot _currentSnapshot;
        private FirstContactSemanticMapStyle _style;
        private bool _fullMode;
        private bool _displayLayoutApplied;
        private bool _displayLayoutFullMode;
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

            node.Pulse = FirstContactSemanticMapTransitionBuilder.BuildPersistentNodePulse(Time.time - _persistentPulseStartTime);
            mapGraphic.Show(_currentSnapshot, _fullMode);
        }

        public void SetStyle(FirstContactSemanticMapStyle style)
        {
            if (_style == style)
            {
                return;
            }

            _style = style;
            _displayLayoutApplied = false;
            mapGraphic?.ApplyStyle(_style);
            if (_currentSnapshot != null)
            {
                ApplyDisplayLayoutIfNeeded(_fullMode);
                RebuildLabels(_currentSnapshot, _fullMode);
            }
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
            _displayLayoutApplied = false;
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
                FirstContactSemanticMapSnapshot frame = FirstContactSemanticMapTransitionBuilder.BuildBootstrapResultFrame(
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
                FirstContactSemanticMapSnapshot frame = FirstContactSemanticMapTransitionBuilder.BuildClusterFormationFrame(
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
            ApplyDisplayLayoutIfNeeded(fullMode);
            graphic.Show(snapshot, fullMode);
            SetVisible(true);
            if (rebuildLabels)
            {
                RebuildLabels(snapshot, fullMode);
            }
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

        private FirstContactSemanticMapGraphic EnsureMapGraphic()
        {
            ResolveReferences();
            if (mapGraphic != null)
            {
                EnsureLabelRoot();
                mapGraphic.ApplyStyle(_style);
                return mapGraphic;
            }

            if (terminalDisplay == null)
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
            ConfigureCurrentLayout(force: true);
            mapGraphic.ApplyStyle(_style);
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
                _labelRoot.anchorMin = Vector2.zero;
                _labelRoot.anchorMax = Vector2.one;
                _labelRoot.offsetMin = Vector2.zero;
                _labelRoot.offsetMax = Vector2.zero;
                _labelRoot.pivot = new Vector2(0.5f, 0.5f);
            }

            _labelLayer.SetRoot(_labelRoot);
        }

        private void ApplyDisplayLayoutIfNeeded(bool fullMode)
        {
            if (_displayLayoutApplied && _displayLayoutFullMode == fullMode)
            {
                return;
            }

            _displayLayoutApplied = true;
            _displayLayoutFullMode = fullMode;
            ConfigureCurrentLayout();
            FirstContactSemanticMapModeStyle mode =
                FirstContactSemanticMapStyle.GetOrDefault(_style).GetMode(fullMode);
            terminalDisplay?.SetContentTopInsetNormalized(mode.terminalTextTopInset);
        }

        private void ConfigureCurrentLayout(bool force = false)
        {
            if (_mapRect == null || (preserveAuthoredMapLayout && !force))
            {
                return;
            }

            FirstContactSemanticMapStyle style = FirstContactSemanticMapStyle.GetOrDefault(_style);
            FirstContactSemanticMapModeStyle mode = style.GetMode(_fullMode);
            _mapRect.anchorMin = new Vector2(
                style.mapHorizontalPaddingRatio,
                1f - mode.mapHeightRatio);
            _mapRect.anchorMax = new Vector2(
                1f - style.mapHorizontalPaddingRatio,
                1f - style.mapVerticalPaddingRatio);
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
            _labelLayer.Render(snapshot, fullMode, _style);
        }

        private void ClearLabels()
        {
            _labelLayer.Hide();
        }

        private void SetVisible(bool visible)
        {
            if (mapGraphic != null)
            {
                mapGraphic.gameObject.SetActive(visible);
            }
        }
    }
}
