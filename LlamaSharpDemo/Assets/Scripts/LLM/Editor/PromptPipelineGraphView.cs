using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

public class PromptPipelineGraphView : GraphView
{
    private readonly Action _markAssetDirty;
    private readonly Action<string, Action> _executeCommand;
    private PromptPipelineAsset _asset;
    private readonly List<PromptPipelineStepNode> _stepNodes = new();
    private readonly List<StateSnapshotNode> _snapshotNodes = new();
    private readonly List<Edge> _executionEdges = new();
    private readonly List<Edge> _stateEdges = new();
    private PipelineInputNode _inputNode;
    private PipelineOutputNode _outputNode;
    private AnalyzedStateModel _stateModel;
    private readonly List<Vector2> _snapshotPositionsCache = new();
    private readonly Dictionary<int, List<string>> _readsByStep = new();
    private readonly HashSet<string> _inputKeys = new();
    private bool _pendingStateRefresh;
    private bool _skipSnapshotCacheOnce;
    private bool _ignoreSnapshotCacheOnce;
    private bool _skipSnapshotPersistenceOnce;
    private bool _shouldFocusOnLoad;
    private Vector3 _lastViewPosition;
    private Vector3 _lastViewScale = Vector3.one;
    private Vector2 _lastMouseScreenPosition;
    private Vector2 _lastMouseGraphPosition;
    private bool _hasLastMousePosition;

    public event Action<AnalyzedStateModel> StateModelChanged;
    public AnalyzedStateModel CurrentStateModel => _stateModel;

    public PromptPipelineGraphView(Action markAssetDirty, Action<string, Action> executeCommand)
    {
        _markAssetDirty = markAssetDirty;
        _executeCommand = executeCommand;

        style.flexGrow = 1f;

        SetupZoom(0.05f, 2f);
        var grid = new GridBackground();
        Insert(0, grid);
        grid.StretchToParentSize();

        this.AddManipulator(new ContentDragger());
        this.AddManipulator(new SelectionDragger());
        this.AddManipulator(new RectangleSelector());

        RegisterCallback<MouseDownEvent>(OnMouseDownStorePosition, TrickleDown.TrickleDown);

        graphViewChanged = OnGraphViewChanged;
        nodeCreationRequest = ctx =>
        {
            Vector2 graphPosition = _hasLastMousePosition
                ? _lastMouseGraphPosition
                : contentViewContainer.WorldToLocal(ctx.screenMousePosition);
            CreateStepAt(graphPosition);
        };

        LlmSettingsChangeNotifier.SettingsChanged += OnLlmSettingsChanged;
        _lastViewPosition = GetCurrentViewPosition();
        _lastViewScale = GetCurrentViewScale();
        schedule.Execute(TrackViewTransform).Every(200);
    }

    public void Dispose()
    {
        LlmSettingsChangeNotifier.SettingsChanged -= OnLlmSettingsChanged;
    }

    public void SetAsset(PromptPipelineAsset asset, bool skipSnapshotCache = false, bool skipSnapshotPersistence = false)
    {
        _skipSnapshotCacheOnce = skipSnapshotCache;
        _ignoreSnapshotCacheOnce = skipSnapshotCache;
        _skipSnapshotPersistenceOnce = skipSnapshotPersistence;
        _asset = asset;
        _shouldFocusOnLoad = true;
        Reload();
    }

    public void ApplySimulationResult(Dictionary<string, string> state)
    {
        if (_stateModel == null || state == null)
        {
            return;
        }

        foreach (var key in _stateModel.keys)
        {
            if (state.TryGetValue(key.keyName, out var value))
            {
                key.lastValuePreview = value;
            }
        }
    }

    private void Reload()
    {
        bool restoredView = TryRestoreViewTransform();
        ClearGraph();

        if (_asset == null || _asset.steps == null)
        {
            return;
        }

        BuildStepNodes();
        BuildExecutionEdgesFromAsset();
        RefreshStateAnalysis();

        if (_shouldFocusOnLoad || !restoredView)
        {
            FocusFirstNode();
            _shouldFocusOnLoad = false;
        }
    }

    private void FocusFirstNode()
    {
        // Default to centering on the first step or input node
        Vector2 focusPoint = Vector2.zero;

        if (_inputNode != null)
        {
            focusPoint = _inputNode.GetPosition().center;
        }
        else if (_stepNodes.Count > 0)
        {
            // Try to find the first node (lowest x) or index 0
            var firstNode = _stepNodes.OrderBy(n => n.GetPosition().x).FirstOrDefault();
            if (firstNode != null)
            {
                focusPoint = firstNode.GetPosition().center;
            }
        }

        // Center the view on the focusPoint
        // ViewTransform is (Translate, Scale)
        // Center of viewport (in local graph coords) = -Translate / Scale + ViewportSize/2/Scale
        // We want Center = focusPoint
        // focusPoint = -Translate/Scale + ViewportCenter/Scale
        // focusPoint * Scale = -Translate + ViewportCenter
        // Translate = ViewportCenter - focusPoint * Scale

        // Wait, Unity GraphView uses UpdateViewTransform(Vector3 pos, Vector3 scale)
        // where pos is the translation.

        // We need the viewport size.
        float width = this.contentContainer.layout.width;
        float height = this.contentContainer.layout.height;

        // If layout is not ready, use a default approximation or FrameAll fallback
        if (float.IsNaN(width) || width < 1f) width = 800f;
        if (float.IsNaN(height) || height < 1f) height = 600f;

        Vector3 scale = Vector3.one; // Default zoom 1.0
        Vector3 translate = new Vector3(width * 0.5f, height * 0.5f, 0f) - (Vector3)focusPoint;

        UpdateViewTransform(translate, scale);
        PersistCurrentViewTransform();
    }

    private void ClearGraph()
    {
        if (!_skipSnapshotCacheOnce)
        {
            CacheSnapshotPositions();
        }
        else
        {
            _snapshotPositionsCache.Clear();
        }
        _skipSnapshotCacheOnce = false;
        if (_ignoreSnapshotCacheOnce)
        {
            _snapshotPositionsCache.Clear();
        }

        foreach (var edge in _executionEdges)
        {
            RemoveElement(edge);
        }
        _executionEdges.Clear();

        foreach (var edge in _stateEdges)
        {
            RemoveElement(edge);
        }
        _stateEdges.Clear();

        foreach (var node in _stepNodes)
        {
            RemoveElement(node);
        }
        _stepNodes.Clear();

        foreach (var snap in _snapshotNodes)
        {
            RemoveElement(snap);
        }
        _snapshotNodes.Clear();

        if (_inputNode != null)
        {
            RemoveElement(_inputNode);
            _inputNode = null;
        }

        if (_outputNode != null)
        {
            RemoveElement(_outputNode);
            _outputNode = null;
        }

        _stateModel = null;
        _readsByStep.Clear();
        _inputKeys.Clear();
    }

    private void BuildStepNodes()
    {
        for (int i = 0; i < _asset.steps.Count; i++)
        {
            var step = _asset.steps[i];
            if (step == null)
            {
                continue;
            }

            var node = new PromptPipelineStepNode(
                step,
                i,
                MarkAssetDirty,
                (label, mutate) => ExecuteCommand(label, mutate),
                RequestStateRefresh,
                Reload,
                () => _stateModel?.keys?.Select(k => k.keyName) ?? Enumerable.Empty<string>(),
                DisconnectExecPort
            );

            Vector2 desiredPosition = step.editorPosition;
            if (desiredPosition == Vector2.zero)
            {
                desiredPosition = new Vector2(200 + i * 320, 120);
                step.editorPosition = desiredPosition;
            }

            node.SetPosition(new Rect(desiredPosition, new Vector2(320, 360)));
            AddElement(node);
            _stepNodes.Add(node);
        }
    }

    private void BuildExecutionEdgesFromAsset()
    {
        RemoveExecutionEdges();
        if (_stepNodes.Count == 0)
        {
            return;
        }

        foreach (var node in _stepNodes)
        {
            if (string.IsNullOrEmpty(node.Step.nextStepGuid))
            {
                continue;
            }

            var targetNode = _stepNodes.FirstOrDefault(n => n.Step.guid == node.Step.nextStepGuid);
            if (targetNode != null)
            {
                var edge = node.ExecOutPort.ConnectTo(targetNode.ExecInPort);
                ConfigureExecutionEdge(edge);
            }
        }
    }

    private void RemoveExecutionEdges()
    {
        foreach (var edge in _executionEdges)
        {
            RemoveElement(edge);
        }
        _executionEdges.Clear();
    }

    private void ConfigureExecutionEdge(Edge edge)
    {
        if (edge == null)
        {
            return;
        }

        RegisterExecutionEdge(edge);
        AddElement(edge);
    }

    private void RegisterExecutionEdge(Edge edge)
    {
        if (edge == null)
        {
            return;
        }

        edge.userData = EdgeCategory.Execution;
        if (!_executionEdges.Contains(edge))
        {
            _executionEdges.Add(edge);
        }
    }

    private void DisconnectExecPort(Port port)
    {
        var edges = port?.connections?.ToList();
        if (edges == null || edges.Count == 0)
        {
            return;
        }

        ExecuteCommand("Disconnect Steps", () =>
        {
            foreach (var edge in edges)
            {
                edge.output?.Disconnect(edge);
                edge.input?.Disconnect(edge);
                _executionEdges.Remove(edge);
                RemoveElement(edge);
            }

            ApplyExecutionOrderFromGraph();
        }, refreshState: true, reloadAfter: true);
    }

    public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter adapter)
    {
        var compatible = new List<Port>();
        foreach (var port in ports.ToList())
        {
            if (port == startPort || port.node == startPort.node)
            {
                continue;
            }

            if (startPort.userData is StepPortKind startKind &&
                port.userData is StepPortKind targetKind)
            {
                bool execCompatible =
                    (startKind == StepPortKind.ExecOut && targetKind == StepPortKind.ExecIn) ||
                    (startKind == StepPortKind.ExecIn && targetKind == StepPortKind.ExecOut);

                if (execCompatible)
                {
                    compatible.Add(port);
                }

                continue;
            }

            if (startPort.direction != port.direction &&
                startPort.portType == port.portType)
            {
                compatible.Add(port);
            }
        }

        return compatible;
    }

    private void RefreshStateAnalysis()
    {
        if (_asset == null)
        {
            return;
        }

        _stateModel = PipelineStateAnalyzer.Analyze(_asset);
        BuildStateLookups();
        RebuildStateNodes();
        UpdateNodeStateData();
        StateModelChanged?.Invoke(_stateModel);
    }

    private void BuildStateLookups()
    {
        _readsByStep.Clear();
        _inputKeys.Clear();

        if (_stateModel == null || _stateModel.keys == null)
        {
            return;
        }

        foreach (AnalyzedStateKey key in _stateModel.keys)
        {
            if (key == null)
                continue;

            if (key.kind == AnalyzedStateKeyKind.Input)
            {
                _inputKeys.Add(key.keyName);
            }

            foreach (int idx in key.consumedByStepIndices)
            {
                AddKeyToMap(_readsByStep, idx, key.keyName);
            }

        }

    }

    private static void AddKeyToMap(Dictionary<int, List<string>> map, int index, string keyName)
    {
        if (!map.TryGetValue(index, out var list))
        {
            list = new List<string>();
            map[index] = list;
        }

        if (!list.Contains(keyName))
        {
            list.Add(keyName);
        }
    }

    private void RebuildStateNodes()
    {
        foreach (var edge in _stateEdges)
        {
            RemoveElement(edge);
        }
        _stateEdges.Clear();

        if (_inputNode != null)
        {
            RemoveElement(_inputNode);
            _inputNode = null;
        }

        if (_outputNode != null)
        {
            RemoveElement(_outputNode);
            _outputNode = null;
        }

        foreach (var snap in _snapshotNodes)
        {
            RemoveElement(snap);
        }
        _snapshotNodes.Clear();

        _inputNode = new PipelineInputNode();
        _inputNode.Bind(_stateModel);
        Vector2 inputPosition = GetInputNodePosition();
        _inputNode.SetPosition(new Rect(inputPosition, new Vector2(240, 320)));
        AddElement(_inputNode);

        _outputNode = new PipelineOutputNode();
        _outputNode.Bind(_stateModel?.finalStateKeys);
        Vector2 outputPosition = GetOutputNodePosition();
        _outputNode.SetPosition(new Rect(outputPosition, new Vector2(240, 320)));
        AddElement(_outputNode);

        BuildSnapshotNodes();

        CreateStateEdges();
    }

    private void BuildSnapshotNodes()
    {
        _snapshotNodes.Clear();

        if (_stateModel?.stepStates == null || _stateModel.stepStates.Count == 0)
        {
            _ignoreSnapshotCacheOnce = false;
            return;
        }

        var storedPositions = _asset?.layoutSettings?.snapshotPositions;
        bool useStored = _asset?.layoutSettings?.snapshotPositionsInitialized == true &&
                         storedPositions != null &&
                         storedPositions.Count == _stateModel.stepStates.Count;

        for (int i = 0; i < _stepNodes.Count && i < _stateModel.stepStates.Count; i++)
        {
            var stepNode = _stepNodes[i];
            var state = _stateModel.stepStates[i];

            var snapshot = new StateSnapshotNode(state);
            Vector2 position = Vector2.zero;
            if (useStored && i < storedPositions.Count)
            {
                position = storedPositions[i];
            }
            else if (!_ignoreSnapshotCacheOnce && i < _snapshotPositionsCache.Count)
            {
                position = _snapshotPositionsCache[i];
            }
            snapshot.SetPosition(new Rect(position, new Vector2(220, 280)));
            AddElement(snapshot);
            _snapshotNodes.Add(snapshot);
        }

        _ignoreSnapshotCacheOnce = false;
        _skipSnapshotPersistenceOnce = false;
    }

    private void CacheSnapshotPositions()
    {
        _snapshotPositionsCache.Clear();
        foreach (var snap in _snapshotNodes)
        {
            _snapshotPositionsCache.Add(snap.GetPosition().position);
        }
    }

    private void PersistSnapshotPositions()
    {
        if (_asset?.layoutSettings == null)
        {
            return;
        }

        var currentPositions = new List<Vector2>(_snapshotNodes.Count);
        foreach (var snap in _snapshotNodes)
        {
            currentPositions.Add(snap.GetPosition().position);
        }

        var storedPositions = _asset.layoutSettings.snapshotPositions ?? new List<Vector2>();
        bool changed = storedPositions.Count != currentPositions.Count;
        if (!changed)
        {
            for (int i = 0; i < storedPositions.Count; i++)
            {
                if ((storedPositions[i] - currentPositions[i]).sqrMagnitude > 0.0001f)
                {
                    changed = true;
                    break;
                }
            }
        }

        if (!changed && _asset.layoutSettings.snapshotPositionsInitialized)
        {
            return;
        }

        _asset.layoutSettings.snapshotPositions = currentPositions;
        _asset.layoutSettings.snapshotPositionsInitialized = true;
        MarkAssetDirty();
    }

    private void CreateStateEdges()
    {
        foreach (var edge in _stateEdges)
        {
            RemoveElement(edge);
        }
        _stateEdges.Clear();

        for (int i = 0; i < _stepNodes.Count; i++)
        {
            var node = _stepNodes[i];
            var reads = GetReads(i);
            var availableKeys = GetAvailableKeysBeforeStep(i);

            node.UpdateAvailableStateKeys(availableKeys);

            // Only connect external inputs if this is a start node (no incoming execution)
            if (!HasExecInConnection(node))
            {
                ConnectExternalInputs(node, reads);
            }

            ConnectSnapshots(i);
        }

        ConnectOutputToFinalSnapshot();
    }

    private void ConnectExternalInputs(PromptPipelineStepNode node, List<string> reads)
    {
        if (_inputNode == null || reads == null || reads.Count == 0)
        {
            return;
        }

        var connectedKeys = new HashSet<string>();
        foreach (string key in reads)
        {
            if (!_inputKeys.Contains(key) || !connectedKeys.Add(key))
            {
                continue;
            }

            var inputPort = _inputNode.GetPort(key);
            TryConnectStateEdge(inputPort, node.StateInPort);
        }
    }

    private void ConnectSnapshots(int stepIndex)
    {
        if (stepIndex < 0 || stepIndex >= _snapshotNodes.Count)
        {
            return;
        }

        var stepNode = GetStepNode(stepIndex);
        var snapshot = _snapshotNodes[stepIndex];
        TryConnectStateEdge(stepNode?.StateOutPort, snapshot.StateInPort);

        var nextStep = GetNextExecutionNode(stepNode);
        if (nextStep != null)
        {
            TryConnectStateEdge(snapshot.StateOutPort, nextStep.StateInPort);
        }
    }

    private void ConnectOutputToFinalSnapshot()
    {
        if (_outputNode == null || _stepNodes.Count == 0)
        {
            return;
        }

        // Find the last node in the execution chain
        // We start from the first node (or any node) and traverse nextStepGuid until we hit null
        // But we need to find the START of the chain first.
        // Assuming single chain for now, or we just take the one that has no next step but IS connected?
        // Actually, if we have [A]->[B] and [C], A->B is a chain, C is disconnected.
        // We probably want B's snapshot.

        // Let's find all nodes that are NOT targets.
        var targetGuids = new HashSet<string>(_stepNodes
            .Select(n => n.Step.nextStepGuid)
            .Where(g => !string.IsNullOrEmpty(g)));

        var startNode = _stepNodes.FirstOrDefault(n => !targetGuids.Contains(n.Step.guid));

        PromptPipelineStepNode lastNode = null;
        var current = startNode;
        var visited = new HashSet<PromptPipelineStepNode>();

        while (current != null)
        {
            if (!visited.Add(current)) break;
            lastNode = current;
            current = GetNextExecutionNode(current);
        }

        if (lastNode != null)
        {
            int lastIndex = _stepNodes.IndexOf(lastNode);
            if (lastIndex >= 0 && lastIndex < _snapshotNodes.Count)
            {
                var finalSnapshot = _snapshotNodes[lastIndex];
                var finalKeys = _stateModel?.finalStateKeys ?? new List<string>();
                foreach (string key in finalKeys)
                {
                    var port = _outputNode.GetPort(key);
                    TryConnectStateEdge(finalSnapshot.StateOutPort, port);
                }
            }
        }
    }

    private PromptPipelineStepNode GetStepNode(int index) =>
        index >= 0 && index < _stepNodes.Count ? _stepNodes[index] : null;

    private void TryConnectStateEdge(Port from, Port to)
    {
        if (from == null || to == null)
        {
            return;
        }

        var edge = from.ConnectTo(to);
        ConfigureStateEdge(edge);
    }

    private void ConfigureStateEdge(Edge edge)
    {
        if (edge == null)
        {
            return;
        }

        edge.capabilities &= ~(Capabilities.Deletable | Capabilities.Selectable);
        edge.userData = EdgeCategory.State;
        AddElement(edge);
        _stateEdges.Add(edge);
    }

    private List<string> GetReads(int index)
    {
        return _readsByStep.TryGetValue(index, out var list) ? list : new List<string>();
    }

    private List<string> GetAvailableKeysBeforeStep(int index)
    {
        if (_stateModel == null)
        {
            return new List<string>();
        }

        // Inputs are always available.
        var inputKeys = _stateModel.keys?
            .Where(k => k.kind == AnalyzedStateKeyKind.Input)
            .Select(k => k.keyName)
            .ToList() ?? new List<string>();

        if (_stateModel.stepStates == null || _stateModel.stepStates.Count == 0)
        {
            return inputKeys;
        }

        // For the first step, only inputs are guaranteed.
        if (index <= 0)
        {
            return inputKeys;
        }

        int snapshotIndex = Math.Min(index - 1, _stateModel.stepStates.Count - 1);
        var snapshot = _stateModel.stepStates[snapshotIndex];
        if (snapshot?.stateKeys != null && snapshot.stateKeys.Count > 0)
        {
            return snapshot.stateKeys;
        }

        return inputKeys;
    }

    private GraphViewChange OnGraphViewChanged(GraphViewChange change)
    {
        bool requiresReorder = false;
        bool movedSteps = false;
        bool movedUtilityNodes = false;
        bool movedSnapshots = false;

        if (change.movedElements != null && change.movedElements.Count > 0)
        {
            var movedElements = change.movedElements.ToList();
            foreach (var element in movedElements)
            {
                if (element is PromptPipelineStepNode)
                {
                    movedSteps = true;
                }
                else if (element is PipelineInputNode || element is PipelineOutputNode)
                {
                    movedUtilityNodes = true;
                }
                else if (element is StateSnapshotNode)
                {
                    movedSnapshots = true;
                }
            }

            if (movedSteps || movedUtilityNodes || movedSnapshots)
            {
                ExecuteCommand("Move Nodes", () =>
                {
                    foreach (var element in movedElements)
                    {
                        if (element is PromptPipelineStepNode node)
                        {
                            node.PersistPosition();
                        }
                    }

                    if (movedUtilityNodes)
                    {
                        PersistUtilityNodePositions();
                    }

                    if (movedSnapshots)
                    {
                        PersistSnapshotPositions();
                    }

                    if (movedSteps)
                    {
                        MarkAssetDirty();
                    }
                });
            }
        }

        if (change.elementsToRemove != null)
        {
            foreach (var element in change.elementsToRemove)
            {
                if (element is Edge edge &&
                    edge.userData is EdgeCategory edgeCategory &&
                    edgeCategory == EdgeCategory.Execution)
                {
                    _executionEdges.Remove(edge);
                    if (edge.output.node is PromptPipelineStepNode source)
                    {
                        ExecuteCommand("Disconnect Steps", () =>
                        {
                            source.Step.nextStepGuid = null;
                            MarkAssetDirty();
                        }, refreshState: false);
                    }
                    requiresReorder = true;
                }
            }
        }

        if (change.edgesToCreate != null && change.edgesToCreate.Count > 0)
        {
            foreach (var edge in change.edgesToCreate)
            {
                Debug.Log($"[GraphView] Edge created. Output: {edge.output?.userData}, Input: {edge.input?.userData}");
            }

            change.edgesToCreate = change.edgesToCreate
                .Where(IsExecutionEdge)
                .ToList();

            foreach (var edge in change.edgesToCreate)
            {
                RegisterExecutionEdge(edge);
                if (edge.output.node is PromptPipelineStepNode source &&
                    edge.input.node is PromptPipelineStepNode target)
                {
                    Debug.Log($"[GraphView] Connecting {source.Step.stepName} -> {target.Step.stepName}");
                    ExecuteCommand("Connect Steps", () =>
                    {
                        source.Step.nextStepGuid = target.Step.guid;
                        MarkAssetDirty();
                    }, refreshState: false);
                }
                else
                {
                    Debug.LogWarning($"[GraphView] Failed to cast nodes for edge: {edge.output?.node} -> {edge.input?.node}");
                }
            }

            requiresReorder |= change.edgesToCreate.Count > 0;
        }

        if (requiresReorder)
        {
            // Since we are reloading the graph, we don't want the GraphView to add the edges 
            // to the current (soon to be destroyed) nodes. This prevents "ghost" edges.
            change.edgesToCreate?.Clear();

            // Delay the reload to ensure GraphView finishes its internal updates first
            EditorApplication.delayCall += () =>
            {
                ExecuteCommand("Reorder Steps", () =>
                {
                    ApplyExecutionOrderFromGraph(logWarning: false);
                }, refreshState: true, reloadAfter: true);
            };
        }

        return change;
    }

    private static bool IsExecutionEdge(Edge edge)
    {
        return edge.output?.userData is StepPortKind outputKind &&
               outputKind == StepPortKind.ExecOut &&
               edge.input?.userData is StepPortKind inputKind &&
               inputKind == StepPortKind.ExecIn;
    }

    private void ApplyExecutionOrderFromGraph(bool logWarning = false)
    {
        if (_asset == null || _stepNodes.Count == 0)
        {
            return;
        }

        var orderedNodes = BuildExecutionChain();
        if (orderedNodes != null && orderedNodes.Count == _asset.steps.Count)
        {
            _asset.steps = orderedNodes.Select(n => n.Step).ToList();
            MarkAssetDirty();
        }
    }

    private List<PromptPipelineStepNode> BuildExecutionChain()
    {
        // Find all nodes that are not targets of any other node
        var targetGuids = new HashSet<string>(_stepNodes
            .Select(n => n.Step.nextStepGuid)
            .Where(g => !string.IsNullOrEmpty(g)));

        var startNodes = _stepNodes.Where(n => !targetGuids.Contains(n.Step.guid)).ToList();

        var ordered = new List<PromptPipelineStepNode>();
        var visited = new HashSet<PromptPipelineStepNode>();

        // Process each chain
        foreach (var startNode in startNodes)
        {
            var current = startNode;
            while (current != null)
            {
                if (!visited.Add(current))
                {
                    break; // Cycle detected or already visited (shouldn't happen in tree/list)
                }

                ordered.Add(current);

                // Find next node
                if (string.IsNullOrEmpty(current.Step.nextStepGuid))
                {
                    current = null;
                }
                else
                {
                    current = _stepNodes.FirstOrDefault(n => n.Step.guid == current.Step.nextStepGuid);
                }
            }
        }

        // Add any remaining nodes (cycles or disconnected parts not reachable from start nodes)
        foreach (var node in _stepNodes)
        {
            if (!visited.Contains(node))
            {
                ordered.Add(node);
            }
        }

        return ordered;
    }

    private static bool HasExecInConnection(PromptPipelineStepNode node)
    {
        return node.ExecInPort != null &&
               node.ExecInPort.connections != null &&
               node.ExecInPort.connections.Any();
    }

    private PromptPipelineStepNode GetNextExecutionNode(PromptPipelineStepNode node)
    {
        if (string.IsNullOrEmpty(node.Step.nextStepGuid))
            return null;
        return _stepNodes.FirstOrDefault(n => n.Step.guid == node.Step.nextStepGuid);
    }

    private void OnLlmSettingsChanged(LlmGenerationProfile settings)
    {
        if (settings == null || _asset?.steps == null)
        {
            return;
        }

        foreach (var step in _asset.steps)
        {
            if (step?.llmProfile == settings)
            {
                RefreshStateAnalysis();
                break;
            }
        }
    }

    private void TrackViewTransform()
    {
        if (_asset?.layoutSettings == null)
        {
            return;
        }

        Vector3 position = GetCurrentViewPosition();
        Vector3 scale = GetCurrentViewScale();

        if (HasSignificantDifference(position, _lastViewPosition) ||
            HasSignificantDifference(scale, _lastViewScale))
        {
            _lastViewPosition = position;
            _lastViewScale = scale;
            _asset.layoutSettings.viewPosition = position;
            _asset.layoutSettings.viewScale = scale;
            _asset.layoutSettings.viewInitialized = true;
            MarkAssetDirty();
        }
    }

    private bool TryRestoreViewTransform()
    {
        if (_asset?.layoutSettings == null || !_asset.layoutSettings.viewInitialized)
        {
            return false;
        }

        UpdateViewTransform(_asset.layoutSettings.viewPosition, _asset.layoutSettings.viewScale);
        _lastViewPosition = _asset.layoutSettings.viewPosition;
        _lastViewScale = _asset.layoutSettings.viewScale;
        return true;
    }

    private void PersistCurrentViewTransform()
    {
        _lastViewPosition = GetCurrentViewPosition();
        _lastViewScale = GetCurrentViewScale();

        if (_asset?.layoutSettings == null)
        {
            return;
        }

        _asset.layoutSettings.viewPosition = _lastViewPosition;
        _asset.layoutSettings.viewScale = _lastViewScale;
        _asset.layoutSettings.viewInitialized = true;
        MarkAssetDirty();
    }

    private static bool HasSignificantDifference(Vector3 a, Vector3 b)
    {
        return (a - b).sqrMagnitude > 0.0001f;
    }

    private Vector3 GetCurrentViewPosition()
    {
        var translate = contentViewContainer.resolvedStyle.translate;
        return new Vector3(translate.x, translate.y, translate.z);
    }

    private Vector3 GetCurrentViewScale()
    {
        var scale = contentViewContainer.resolvedStyle.scale;
        return scale.value;
    }

    private void OnMouseDownStorePosition(MouseDownEvent evt)
    {
        _lastMouseScreenPosition = evt.mousePosition;
        _lastMouseGraphPosition = contentViewContainer.WorldToLocal(evt.mousePosition);
        _hasLastMousePosition = true;
    }

    private Vector2 GetInputNodePosition()
    {
        if (_asset?.layoutSettings == null)
        {
            return new Vector2(-600f, 80f);
        }

        if (!_asset.layoutSettings.inputPositionInitialized)
        {
            _asset.layoutSettings.inputNodePosition = new Vector2(-600f, 80f);
            _asset.layoutSettings.inputPositionInitialized = true;
            MarkAssetDirty();
        }

        return _asset.layoutSettings.inputNodePosition;
    }

    private Vector2 GetOutputNodePosition()
    {
        if (_asset?.layoutSettings == null)
        {
            return CalculateDefaultOutputNodePosition();
        }

        if (!_asset.layoutSettings.outputPositionInitialized)
        {
            _asset.layoutSettings.outputNodePosition = CalculateDefaultOutputNodePosition();
            _asset.layoutSettings.outputPositionInitialized = true;
            MarkAssetDirty();
        }

        return _asset.layoutSettings.outputNodePosition;
    }

    private Vector2 CalculateDefaultOutputNodePosition()
    {
        float x = 320f * Mathf.Max(1, _stepNodes.Count + 1);
        return new Vector2(x, 80f);
    }

    private void PersistUtilityNodePositions()
    {
        if (_asset?.layoutSettings == null)
        {
            return;
        }

        if (_inputNode != null)
        {
            _asset.layoutSettings.inputNodePosition = _inputNode.GetPosition().position;
            _asset.layoutSettings.inputPositionInitialized = true;
        }

        if (_outputNode != null)
        {
            _asset.layoutSettings.outputNodePosition = _outputNode.GetPosition().position;
            _asset.layoutSettings.outputPositionInitialized = true;
        }

        MarkAssetDirty();
    }

    private void RequestStateRefresh()
    {
        if (_pendingStateRefresh)
        {
            return;
        }

        _pendingStateRefresh = true;
        EditorApplication.delayCall += () =>
        {
            _pendingStateRefresh = false;
            RefreshStateAnalysis();
        };
    }

    private void UpdateNodeStateData()
    {
        for (int i = 0; i < _stepNodes.Count; i++)
        {
            _stepNodes[i].UpdateDisplayIndex(i);
        }
    }

    private void MarkAssetDirty() => _markAssetDirty?.Invoke();

    private void ExecuteCommand(string label, Action mutate, bool refreshState = false, bool reloadAfter = false)
    {
        _executeCommand?.Invoke(label, mutate);
        if (refreshState)
        {
            RequestStateRefresh();
        }

        if (reloadAfter)
        {
            Reload();
        }
    }

    public void CreateStepAtCenter()
    {
        var rect = contentViewContainer.layout;
        Vector2 graphPosition = rect.width > 0f && rect.height > 0f
            ? rect.center
            : Vector2.zero;
        CreateStepAt(graphPosition);
    }

    private void CreateStepAt(Vector2 graphPosition)
    {
        if (_asset == null)
        {
            return;
        }

        ExecuteCommand("Add Prompt Step", () =>
        {
            var step = new PromptPipelineStep
            {
                stepName = $"Step {_asset.steps.Count + 1}",
                stepKind = PromptPipelineStepKind.JsonLlm,
                editorPosition = graphPosition,
                guid = Guid.NewGuid().ToString(),
                nextStepGuid = null
            };

            _asset.steps.Add(step);
            SetInitialSnapshotPositionForNewStep(graphPosition);
            MarkAssetDirty();
        }, reloadAfter: true);
    }

    public override EventPropagation DeleteSelection()
    {
        bool removedSteps = false;
        if (_asset != null)
        {
            var stepNodesToRemove = selection.OfType<PromptPipelineStepNode>().ToList();
            var stepsToRemove = stepNodesToRemove.Select(n => n.Step).ToList();
            var removedIndices = stepNodesToRemove
                .Select(n => _stepNodes.IndexOf(n))
                .Where(i => i >= 0)
                .ToList();

            if (stepsToRemove.Count > 0)
            {
                removedSteps = true;
                ExecuteCommand("Delete Prompt Step", () =>
                {
                    if (removedIndices.Count > 0)
                    {
                        RemoveSnapshotDataForIndices(removedIndices);
                    }

                    foreach (var step in stepsToRemove)
                    {
                        _asset.steps.Remove(step);
                    }
                    MarkAssetDirty();
                });
            }
        }

        var result = base.DeleteSelection();

        if (removedSteps)
        {
            Reload();
        }

        return result;
    }

    private void RemoveSnapshotDataForIndices(IEnumerable<int> removedIndices)
    {
        if (_asset?.layoutSettings?.snapshotPositions == null || removedIndices == null)
        {
            return;
        }

        var positions = _asset.layoutSettings.snapshotPositions;
        var sorted = removedIndices
            .Distinct()
            .Where(i => i >= 0 && i < positions.Count)
            .OrderByDescending(i => i)
            .ToList();

        foreach (int idx in sorted)
        {
            positions.RemoveAt(idx);
            if (idx >= 0 && idx < _snapshotPositionsCache.Count)
            {
                _snapshotPositionsCache.RemoveAt(idx);
            }
        }

        int targetCount = Math.Max(0, (_asset.steps?.Count ?? 0) - sorted.Count);
        _asset.layoutSettings.snapshotPositionsInitialized =
            positions.Count == targetCount && positions.Count > 0;
    }

    private void SetInitialSnapshotPositionForNewStep(Vector2 stepGraphPosition)
    {
        if (_asset?.layoutSettings == null)
        {
            return;
        }

        var layout = _asset.layoutSettings;
        layout.snapshotPositions ??= new List<Vector2>();

        int newIndex = Mathf.Max(0, (_asset.steps?.Count ?? 0) - 1);
        while (layout.snapshotPositions.Count <= newIndex)
        {
            layout.snapshotPositions.Add(Vector2.zero);
        }

        const float stepWidth = 320f;
        const float offset = 60f;
        layout.snapshotPositions[newIndex] = stepGraphPosition + new Vector2(stepWidth + offset, 0f);
        layout.snapshotPositionsInitialized = true;
    }
}

internal enum StepPortKind
{
    ExecIn,
    ExecOut,
    StateIn,
    StateOut
}

internal enum EdgeCategory
{
    Execution,
    State
}

internal class PromptPipelineStepNode : Node
{
    private readonly Action _markDirty;
    private readonly Action<string, Action> _executeCommand;
    private readonly Action _requestStateRefresh;
    private readonly Action _onStepKindChanged;
    private readonly Func<IEnumerable<string>> _stateKeyProvider;
    private readonly Action<Port> _disconnectExecPort;

    private readonly TextField _nameField;
    private readonly TextField _titleEditField;
    private readonly ObjectField _settingsField;
    private readonly Foldout _settingsFoldout;
    private readonly IMGUIContainer _settingsInspector;
    private UnityEditor.Editor _settingsEditor;
    private bool _lastExpandedState;
    private IVisualElementScheduledItem _expandedMonitor;
    private readonly EnumField _kindField;
    private readonly TextField _userPromptField;
    private readonly IntegerField _maxRetriesField;
    private readonly FloatField _retryDelayField;
    private readonly DropdownField _customTypeDropdown;
    private readonly VisualElement _customParamsContainer;
    private readonly Button _insertStateKeyButton;
    private readonly VisualElement _jsonOptionsContainer;
    private readonly VisualElement _customOptionsContainer;

    private List<string> _availableKeys = new();
    private int _displayIndex;

    public PromptPipelineStep Step { get; }
    public Port ExecInPort { get; }
    public Port ExecOutPort { get; }
    public Port StateInPort { get; }
    public Port StateOutPort { get; }

    public PromptPipelineStepNode(
        PromptPipelineStep step,
        int index,
        Action markDirty,
        Action<string, Action> executeCommand,
        Action requestStateRefresh,
        Action onStepKindChanged,
        Func<IEnumerable<string>> stateKeyProvider,
        Action<Port> disconnectExecPort
    )
    {
        Step = step;
        _markDirty = markDirty;
        _executeCommand = executeCommand;
        _requestStateRefresh = requestStateRefresh;
        _onStepKindChanged = onStepKindChanged;
        _stateKeyProvider = stateKeyProvider;
        _disconnectExecPort = disconnectExecPort;

        title = step.stepName;
        style.minWidth = 320f;
        style.maxWidth = 380f;
        mainContainer.style.minWidth = 320f;
        extensionContainer.style.minWidth = 320f;
        UpdateDisplayIndex(index);
        titleContainer.RegisterCallback<MouseDownEvent>(evt =>
        {
            if (evt.clickCount == 2 && evt.button == 0)
            {
                BeginInlineTitleEdit();
                evt.StopPropagation();
            }
        });

        _titleEditField = new TextField
        {
            style =
            {
                flexGrow = 1f,
                display = DisplayStyle.None
            }
        };
        _titleEditField.RegisterCallback<FocusOutEvent>(_ => CommitInlineTitleEdit());
        _titleEditField.RegisterCallback<KeyDownEvent>(OnTitleEditKeyDown);
        titleContainer.Add(_titleEditField);

        ExecInPort = InstantiatePort(Orientation.Horizontal, Direction.Input, Port.Capacity.Single, typeof(bool));
        ExecInPort.portName = "Exec In";
        ExecInPort.userData = StepPortKind.ExecIn;
        AttachExecPortContextMenu(ExecInPort);
        inputContainer.Add(ExecInPort);

        ExecOutPort = InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Single, typeof(bool));
        ExecOutPort.portName = "Exec Out";
        ExecOutPort.userData = StepPortKind.ExecOut;
        AttachExecPortContextMenu(ExecOutPort);
        outputContainer.Add(ExecOutPort);

        StateInPort = InstantiatePort(Orientation.Horizontal, Direction.Input, Port.Capacity.Multi, typeof(string));
        StateInPort.portName = "State In";
        StateInPort.userData = StepPortKind.StateIn;
        StateInPort.pickingMode = PickingMode.Ignore;
        inputContainer.Add(StateInPort);

        StateOutPort = InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Multi, typeof(string));
        StateOutPort.portName = "State Out";
        StateOutPort.userData = StepPortKind.StateOut;
        StateOutPort.pickingMode = PickingMode.Ignore;
        outputContainer.Add(StateOutPort);

        _nameField = new TextField("Step Name") { value = step.stepName };
        _nameField.RegisterValueChangedCallback(evt =>
        {
            ApplyChange("Rename Step", () =>
            {
                step.stepName = evt.newValue;
                UpdateDisplayIndex(index);
            }, refreshState: false);
        });
        extensionContainer.Add(_nameField);

        _kindField = new EnumField("Step Kind", step.stepKind);
        _kindField.RegisterValueChangedCallback(evt =>
        {
            var newKind = (PromptPipelineStepKind)evt.newValue;
            if (newKind == step.stepKind)
            {
                return;
            }

            ApplyChange("Change Step Kind", () => step.stepKind = newKind, refreshState: false);
            _onStepKindChanged?.Invoke();
            UpdateSettingsInspector();
        });
        extensionContainer.Add(_kindField);

        _settingsField = new ObjectField("LLM Profile")
        {
            objectType = typeof(LlmGenerationProfile),
            value = step.llmProfile
        };
        _settingsField.RegisterValueChangedCallback(evt =>
        {
            ApplyChange("Assign LLM Profile", () => step.llmProfile = evt.newValue as LlmGenerationProfile);
            UpdateSettingsInspector();
            UpdatePromptInputsEnabled();
        });
        extensionContainer.Add(_settingsField);

        _settingsFoldout = new Foldout
        {
            text = "Inline LLM Profile",
            value = false
        };
        _settingsFoldout.RegisterValueChangedCallback(evt =>
        {
            if (evt.newValue)
            {
                BringNodeToFront();
            }
        });
        _settingsInspector = new IMGUIContainer(DrawSettingsInspector)
        {
            style =
            {
                marginLeft = 4,
                marginBottom = 4
            }
        };
        _settingsFoldout.Add(_settingsInspector);
        extensionContainer.Add(_settingsFoldout);

        _userPromptField = new TextField("User Prompt Template")
        {
            multiline = true,
            value = step.userPromptTemplate,
            style = { minHeight = 80 }
        };
        _userPromptField.RegisterValueChangedCallback(evt =>
        {
            ApplyChange("Edit User Prompt", () => step.userPromptTemplate = evt.newValue);
        });
        extensionContainer.Add(_userPromptField);

        _insertStateKeyButton = new Button(OnInsertStateKeyClicked)
        {
            text = "Insert State Key"
        };
        extensionContainer.Add(_insertStateKeyButton);

        _jsonOptionsContainer = new VisualElement { style = { flexDirection = FlexDirection.Column } };
        _jsonOptionsContainer.Add(new Label("JSON Options"));
        _maxRetriesField = new IntegerField("Max Retries") { value = step.jsonMaxRetries };
        _maxRetriesField.RegisterValueChangedCallback(evt =>
        {
            ApplyChange("Edit JSON Retries", () => step.jsonMaxRetries = Mathf.Max(1, evt.newValue));
        });
        _jsonOptionsContainer.Add(_maxRetriesField);

        _retryDelayField = new FloatField("Retry Delay (s)") { value = step.jsonRetryDelaySeconds };
        _retryDelayField.RegisterValueChangedCallback(evt =>
        {
            ApplyChange("Edit JSON Retry Delay", () => step.jsonRetryDelaySeconds = Mathf.Max(0f, evt.newValue));
        });
        _jsonOptionsContainer.Add(_retryDelayField);
        extensionContainer.Add(_jsonOptionsContainer);

        _customOptionsContainer = new VisualElement { style = { flexDirection = FlexDirection.Column } };
        _customOptionsContainer.Add(new Label("Custom Link Options"));

        string initialCustomLabel = CustomLinkTypeProvider.FindLabelForType(step.customLinkTypeName)
            ?? CustomLinkTypeProvider.Labels.FirstOrDefault();

        var customTypeLabels = CustomLinkTypeProvider.Labels.Any()
            ? CustomLinkTypeProvider.Labels.ToList()
            : new List<string> { "(No Custom Link types found)" };
        _customTypeDropdown = new DropdownField(
            "Known Types",
            customTypeLabels,
            initialCustomLabel);
        _customTypeDropdown.SetEnabled(CustomLinkTypeProvider.Labels.Any());
        // If no type was assigned yet, default to the first known type so parameters can sync.
        if (string.IsNullOrWhiteSpace(step.customLinkTypeName) && !string.IsNullOrWhiteSpace(initialCustomLabel) &&
            CustomLinkTypeProvider.TryResolveTypeName(initialCustomLabel, out string resolvedTypeName))
        {
            step.customLinkTypeName = resolvedTypeName;
            _markDirty?.Invoke();
        }
        _customTypeDropdown.RegisterValueChangedCallback(evt =>
        {
            string selected = evt.newValue;
            if (CustomLinkTypeProvider.TryResolveTypeName(selected, out string typeName))
            {
                ApplyChange("Select Custom Link Type", () =>
                {
                    step.customLinkTypeName = typeName;
                    SyncParamsWithConstructor(typeName, force: true);
                }, refreshState: false);
            }
        });
        _customOptionsContainer.Add(_customTypeDropdown);

        _customParamsContainer = new VisualElement { style = { flexDirection = FlexDirection.Column } };
        _customParamsContainer.Add(new Label("Custom Parameters (auto-detected from constructor; values only)"));
        _customOptionsContainer.Add(_customParamsContainer);
        extensionContainer.Add(_customOptionsContainer);

        RefreshSections();
        RebuildCustomParamsUI();
        bool forceParamSync = Step.customLinkParameters == null || Step.customLinkParameters.Count == 0;
        SyncParamsWithConstructor(step.customLinkTypeName, force: forceParamSync);
        UpdateSettingsInspector();
        UpdatePromptInputsEnabled();
        RefreshExpandedState();
        if (expanded)
        {
            BringNodeToFront();
        }
        RegisterCallback<DetachFromPanelEvent>(_ =>
        {
            DisposeSettingsEditor();
            _expandedMonitor?.Pause();
        });
        RegisterCallback<AttachToPanelEvent>(_ =>
        {
            _expandedMonitor?.Resume();
            _lastExpandedState = expanded;
        });
        ApplyBackgroundStyles();
        _lastExpandedState = expanded;
        _expandedMonitor = schedule.Execute(MonitorExpandedState).Every(200);
    }

    public void UpdateAvailableStateKeys(IEnumerable<string> keys)
    {
        _availableKeys = keys?.Distinct().ToList() ?? new List<string>();
        UpdatePromptInputsEnabled();
    }

    public void UpdateDisplayIndex(int index)
    {
        _displayIndex = index;
        title = $"{index + 1}. {Step.stepName} ({Step.stepKind})";
        UpdateHeaderStyle();
    }

    public void PersistPosition()
    {
        Step.editorPosition = GetPosition().position;
    }

    private void ApplyChange(string undoLabel, Action mutate, bool refreshState = true)
    {
        _executeCommand?.Invoke(undoLabel, () =>
        {
            mutate?.Invoke();
            _markDirty?.Invoke();
            if (refreshState)
            {
                _requestStateRefresh?.Invoke();
            }
        });
    }

    private void UpdatePromptInputsEnabled()
    {
        bool hasSettings = Step?.llmProfile != null;
        _userPromptField?.SetEnabled(hasSettings);
        _insertStateKeyButton?.SetEnabled(hasSettings && _availableKeys != null && _availableKeys.Count > 0);
    }

    private void RefreshSections()
    {
        bool isJson = Step.stepKind == PromptPipelineStepKind.JsonLlm;
        bool isCustom = Step.stepKind == PromptPipelineStepKind.CustomLink;

        _jsonOptionsContainer.style.display = isJson ? DisplayStyle.Flex : DisplayStyle.None;
        _customOptionsContainer.style.display = isCustom ? DisplayStyle.Flex : DisplayStyle.None;
        UpdateHeaderStyle();
    }

    private void SyncParamsWithConstructor(string typeName, bool force)
    {
        var type = CustomLinkTypeProvider.ResolveType(typeName);
        // Do not overwrite if already have params unless forced.
        if (!force && Step.customLinkParameters != null && Step.customLinkParameters.Count > 0)
        {
            return;
        }

        var ctor = type != null ? CustomLinkTypeProvider.FindConstructorParameters(type) : Array.Empty<ParameterInfo>();
        if (ctor == null || ctor.Length == 0)
        {
            return;
        }

        ApplyChange("Sync Custom Params", () =>
        {
            Step.customLinkParameters = ctor
                .Where(p => !typeof(ScriptableObject).IsAssignableFrom(p.ParameterType))
                .Select(p => new CustomLinkParameter { key = p.Name, value = string.Empty })
                .ToList();
            RebuildCustomParamsUI();
        }, refreshState: false);
    }

    private void RebuildCustomParamsUI()
    {
        if (Step.customLinkParameters == null)
        {
            Step.customLinkParameters = new List<CustomLinkParameter>();
        }

        // Clear existing rows except the header label (first element)
        while (_customParamsContainer.childCount > 1)
        {
            _customParamsContainer.RemoveAt(_customParamsContainer.childCount - 1);
        }

        var type = CustomLinkTypeProvider.ResolveType(Step.customLinkTypeName);
        string scriptableObjectParamName = null;

        if (type != null)
        {
            var ctorParams = CustomLinkTypeProvider.FindConstructorParameters(type);
            var soParam = ctorParams.FirstOrDefault(p => typeof(ScriptableObject).IsAssignableFrom(p.ParameterType));

            if (soParam != null)
            {
                scriptableObjectParamName = soParam.Name;
                var objectField = new ObjectField("Custom Asset")
                {
                    objectType = soParam.ParameterType, // Use specific type (e.g., StatConfigSO)
                    value = Step.customAsset,
                    allowSceneObjects = false
                };
                objectField.RegisterValueChangedCallback(evt =>
                {
                    ApplyChange("Set Custom Asset", () => Step.customAsset = evt.newValue as ScriptableObject, refreshState: false);
                });
                _customParamsContainer.Add(objectField);
            }
        }

        for (int i = 0; i < Step.customLinkParameters.Count; i++)
        {
            // Skip if this parameter corresponds to the ScriptableObject we already showed an ObjectField for.
            if (!string.IsNullOrEmpty(scriptableObjectParamName) && Step.customLinkParameters[i].key == scriptableObjectParamName)
            {
                continue;
            }
            AddParamRow(i);
        }

        if (Step.customLinkParameters.Count == 0)
        {
            _customParamsContainer.Add(new Label("No constructor parameters detected."));
        }
    }

    private void AddParamRow(int index)
    {
        var param = Step.customLinkParameters[index];
        var row = new VisualElement { style = { flexDirection = FlexDirection.Row, marginBottom = 2, alignItems = Align.Center } };

        var keyLabel = new Label(param.key ?? "(unnamed)")
        {
            style =
            {
                minWidth = 120,
                maxWidth = 200,
                unityFontStyleAndWeight = FontStyle.Bold,
                marginRight = 6
            }
        };
        row.Add(keyLabel);

        var valueField = new TextField()
        {
            value = param.value,
            style = { flexGrow = 1f, marginRight = 4 }
        };
        var warningLabel = new Label
        {
            style =
            {
                color = Color.red,
                unityFontStyleAndWeight = FontStyle.Italic,
                display = DisplayStyle.None,
                marginLeft = 4
            }
        };

        var parameterInfo = CustomLinkTypeProvider.FindParameter(Step.customLinkTypeName, param.key);
        valueField.RegisterValueChangedCallback(evt =>
        {
            ApplyChange("Edit Custom Param Value", () => param.value = evt.newValue, refreshState: false);
            ValidateParamValue(parameterInfo, evt.newValue, warningLabel);
        });
        row.Add(valueField);
        row.Add(warningLabel);

        // initial validation
        ValidateParamValue(parameterInfo, param.value, warningLabel);

        _customParamsContainer.Add(row);
    }

    private void ValidateParamValue(ParameterInfo parameterInfo, string value, Label warningLabel)
    {
        if (warningLabel == null)
        {
            return;
        }

        string error = null;
        if (parameterInfo != null)
        {
            Type targetType = parameterInfo.ParameterType;
            error = targetType switch
            {
                Type t when t == typeof(int) && !int.TryParse(value, out _) => "Requires int",
                Type t when t == typeof(long) && !long.TryParse(value, out _) => "Requires long",
                Type t when t == typeof(float) && !float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _) => "Requires float",
                Type t when t == typeof(double) && !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _) => "Requires double",
                Type t when t == typeof(bool) && !bool.TryParse(value, out _) => "Requires bool (true/false)",
                _ => null
            };
        }

        warningLabel.style.display = string.IsNullOrEmpty(error) ? DisplayStyle.None : DisplayStyle.Flex;
        warningLabel.text = error ?? string.Empty;
    }

    private void OnInsertStateKeyClicked()
    {
        if (_availableKeys == null || _availableKeys.Count == 0)
        {
            return;
        }

        var menu = new GenericMenu();
        foreach (string key in _availableKeys)
        {
            menu.AddItem(new GUIContent(key), false, () =>
            {
                string insertion = $"{{{{{key}}}}}";
                ApplyChange("Insert State Key", () =>
                {
                    Step.userPromptTemplate = (Step.userPromptTemplate ?? string.Empty) + insertion;
                    _userPromptField.value = Step.userPromptTemplate;
                });
            });
        }
        menu.DropDown(_insertStateKeyButton.worldBound);
    }

    private void UpdateHeaderStyle()
    {
        var color = GetColorForKind(Step.stepKind);
        titleContainer.style.backgroundColor = new StyleColor(color);
    }

    private void UpdateSettingsInspector()
    {
        if (_settingsFoldout == null || _settingsInspector == null)
        {
            return;
        }

        var target = Step.llmProfile;
        if (target == null)
        {
            _settingsFoldout.style.display = DisplayStyle.None;
            DisposeSettingsEditor();
            return;
        }

        _settingsFoldout.style.display = DisplayStyle.Flex;
        UnityEditor.Editor.CreateCachedEditor(target, null, ref _settingsEditor);
        _settingsInspector.MarkDirtyRepaint();
    }

    private void DrawSettingsInspector()
    {
        if (_settingsEditor == null)
        {
            EditorGUILayout.HelpBox("Assign LlmGenerationProfile to edit inline.", MessageType.Info);
            return;
        }

        bool isJsonStep = Step.stepKind == PromptPipelineStepKind.JsonLlm;
        LlmGenerationProfileEditor.JsonFieldsEnabled = isJsonStep;
        LlmGenerationProfileEditor.JsonFieldsDisabledMessage = isJsonStep
            ? null
            : "Completion steps always produce 'response'. JSON Output Fields are ignored for this step kind.";

        try
        {
            EditorGUI.BeginChangeCheck();
            _settingsEditor.OnInspectorGUI();
            if (EditorGUI.EndChangeCheck())
            {
                var settings = Step.llmProfile;
                if (settings != null)
                {
                    Undo.RecordObject(settings, "Edit LLM Profile");
                    EditorUtility.SetDirty(settings);
                    LlmSettingsChangeNotifier.RaiseChanged(settings);
                }
            }
        }
        finally
        {
            LlmGenerationProfileEditor.JsonFieldsEnabled = true;
            LlmGenerationProfileEditor.JsonFieldsDisabledMessage = null;
        }
    }

    private void DisposeSettingsEditor()
    {
        if (_settingsEditor != null)
        {
            UnityEngine.Object.DestroyImmediate(_settingsEditor);
            _settingsEditor = null;
        }
    }

    private void ApplyBackgroundStyles()
    {
        var panelColor = new Color(0.11f, 0.11f, 0.11f, 0.98f);
        var bodyColor = new Color(0.16f, 0.16f, 0.16f, 0.98f);
        style.backgroundColor = new StyleColor(panelColor);
        style.opacity = 1f;
        mainContainer.style.backgroundColor = new StyleColor(panelColor);
        mainContainer.style.opacity = 1f;
        extensionContainer.style.backgroundColor = new StyleColor(bodyColor);
        extensionContainer.style.opacity = 1f;
        extensionContainer.style.paddingLeft = 6;
        extensionContainer.style.paddingRight = 6;
        extensionContainer.style.paddingBottom = 6;
    }

    private void BringNodeToFront()
    {
        BringToFront();
        parent?.BringToFront();
    }

    private void MonitorExpandedState()
    {
        if (expanded != _lastExpandedState)
        {
            if (expanded)
            {
                BringNodeToFront();
            }
            _lastExpandedState = expanded;
        }
    }

    private static Color GetColorForKind(PromptPipelineStepKind kind)
    {
        return kind switch
        {
            PromptPipelineStepKind.JsonLlm => new Color(0.18f, 0.5f, 0.82f),
            PromptPipelineStepKind.CompletionLlm => new Color(0.25f, 0.7f, 0.45f),
            PromptPipelineStepKind.CustomLink => new Color(0.8f, 0.55f, 0.2f),
            _ => new Color(0.3f, 0.3f, 0.3f)
        };
    }

    private void AttachExecPortContextMenu(Port port)
    {
        if (port == null)
        {
            return;
        }

        port.AddManipulator(new ContextualMenuManipulator(evt =>
        {
            evt.menu.AppendAction(
                "Disconnect",
                _ => _disconnectExecPort?.Invoke(port),
                _ => port.connections != null && port.connections.Any()
                    ? DropdownMenuAction.Status.Normal
                    : DropdownMenuAction.Status.Disabled
            );
        }));
    }

    private void BeginInlineTitleEdit()
    {
        _titleEditField.value = Step.stepName;
        _titleEditField.style.display = DisplayStyle.Flex;
        _titleEditField.Focus();
        _titleEditField.SelectAll();
    }

    private void CommitInlineTitleEdit()
    {
        if (_titleEditField.style.display == DisplayStyle.None)
        {
            return;
        }

        string newName = _titleEditField.value?.Trim();
        _titleEditField.style.display = DisplayStyle.None;

        if (string.IsNullOrEmpty(newName) || newName == Step.stepName)
        {
            return;
        }

        ApplyChange("Rename Step", () =>
        {
            Step.stepName = newName;
            UpdateDisplayIndex(_displayIndex);
            _nameField.value = newName;
        }, refreshState: false);
    }

    private void CancelInlineTitleEdit()
    {
        _titleEditField.style.display = DisplayStyle.None;
    }

    private void OnTitleEditKeyDown(KeyDownEvent evt)
    {
        if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
        {
            CommitInlineTitleEdit();
            evt.StopPropagation();
        }
        else if (evt.keyCode == KeyCode.Escape)
        {
            CancelInlineTitleEdit();
            evt.StopPropagation();
        }
    }
}

internal class PipelineInputNode : Node
{
    private readonly Dictionary<string, Port> _ports = new();

    public PipelineInputNode()
    {
        title = "Pipeline Input";
        capabilities |= Capabilities.Movable;
    }

    public void Bind(AnalyzedStateModel model)
    {
        _ports.Clear();
        inputContainer.Clear();
        outputContainer.Clear();

        if (model != null && model.keys != null)
        {
            foreach (var key in model.keys.Where(k => k.kind == AnalyzedStateKeyKind.Input))
            {
                var port = InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Multi, typeof(string));
                port.portName = key.keyName;
                port.pickingMode = PickingMode.Ignore;
                outputContainer.Add(port);
                _ports[key.keyName] = port;
            }
        }

        RefreshExpandedState();
        RefreshPorts();
    }

    public Port GetPort(string keyName) =>
        _ports.TryGetValue(keyName, out var port) ? port : null;
}

internal static class CustomLinkTypeProvider
{
    private static readonly List<LinkTypeInfo> _types;
    private static readonly List<string> _labels;
    private static readonly Dictionary<string, string> _labelToType;

    static CustomLinkTypeProvider()
    {
        _types = new List<LinkTypeInfo>();
        _labelToType = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (Type t in TypeCache.GetTypesDerivedFrom<IStateChainLink>())
        {
            // We support multiple constructor signatures now, so we shouldn't strictly require a parameterless one.
            // Supported signatures:
            // 1. (Dictionary<string, string>, ScriptableObject)
            // 2. (ScriptableObject)
            // 3. (Dictionary<string, string>)
            // 4. Bindable parameters
            // 5. Parameterless

            // If it's abstract or generic, we still skip.
            if (t == null || t.IsAbstract || t.IsGenericTypeDefinition)
                continue;

            // We'll assume if it implements IStateChainLink and ICustomLinkStateProvider, it's intended to be used.
            // The instantiation logic in PromptPipelineAsset handles the fallback.

            if (!typeof(ICustomLinkStateProvider).IsAssignableFrom(t))
                continue;

            string label = $"{t.FullName} ({t.Assembly.GetName().Name})";
            string typeName = t.AssemblyQualifiedName;

            if (string.IsNullOrEmpty(label) || string.IsNullOrEmpty(typeName))
                continue;

            _types.Add(new LinkTypeInfo(label, typeName, t.FullName));
            if (!_labelToType.ContainsKey(label))
            {
                _labelToType.Add(label, typeName);
            }
        }

        _types.Sort((a, b) => string.CompareOrdinal(a.Label, b.Label));
        _labels = _types.Select(t => t.Label).ToList();
    }

    public static IReadOnlyList<string> Labels => _labels;

    public static Type ResolveType(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return null;
        }

        if (_labelToType.TryGetValue(typeName, out var mappedType))
        {
            typeName = mappedType;
        }

        var direct = Type.GetType(typeName);
        if (direct != null)
        {
            return direct;
        }

        foreach (Type t in TypeCache.GetTypesDerivedFrom<IStateChainLink>())
        {
            if (t == null || t.IsAbstract || t.IsGenericTypeDefinition)
            {
                continue;
            }

            if (string.Equals(t.AssemblyQualifiedName, typeName, StringComparison.Ordinal) ||
                string.Equals(t.FullName, typeName, StringComparison.Ordinal) ||
                string.Equals(t.Name, typeName, StringComparison.Ordinal))
            {
                return t;
            }
        }

        return null;
    }

    public static bool TryResolveTypeName(string label, out string typeName)
    {
        return _labelToType.TryGetValue(label, out typeName);
    }

    public static string FindLabelForType(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return null;
        }

        // Match assembly-qualified first, then full name.
        var match = _types.FirstOrDefault(t =>
            string.Equals(t.AssemblyQualifiedName, typeName, StringComparison.Ordinal) ||
            string.Equals(t.FullName, typeName, StringComparison.Ordinal) ||
            string.Equals(t.FullName?.Split('.').LastOrDefault(), typeName, StringComparison.Ordinal));
        return match.Label;
    }

    public static ParameterInfo FindParameter(string typeName, string paramName)
    {
        if (string.IsNullOrWhiteSpace(typeName) || string.IsNullOrWhiteSpace(paramName))
        {
            return null;
        }

        var type = ResolveType(typeName);
        if (type == null)
        {
            return null;
        }

        var parameters = FindConstructorParameters(type);
        return parameters.FirstOrDefault(p =>
            string.Equals(p.Name, paramName, StringComparison.Ordinal));
    }

    public static ParameterInfo[] FindConstructorParameters(Type type)
    {
        if (type == null)
        {
            return Array.Empty<ParameterInfo>();
        }

        // Prefer Dictionary<string,string> ctor (already handled downstream); skip it for param syncing.
        var candidates = type
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .Where(c =>
            {
                var parameters = c.GetParameters();
                if (parameters.Length == 1 &&
                    parameters[0].ParameterType == typeof(Dictionary<string, string>))
                {
                    return false;
                }
                return parameters.Length > 0;
            })
            .OrderByDescending(c => c.GetParameters().Length)
            .ToList();

        return candidates.FirstOrDefault()?.GetParameters() ?? Array.Empty<ParameterInfo>();
    }

    private readonly struct LinkTypeInfo
    {
        public string Label { get; }
        public string AssemblyQualifiedName { get; }
        public string FullName { get; }

        public LinkTypeInfo(string label, string assemblyQualifiedName, string fullName)
        {
            Label = label;
            AssemblyQualifiedName = assemblyQualifiedName;
            FullName = fullName;
        }
    }
}

internal class PipelineOutputNode : Node
{
    private readonly Dictionary<string, Port> _ports = new();

    public PipelineOutputNode()
    {
        title = "Pipeline Output";
        capabilities |= Capabilities.Movable;
    }

    public void Bind(IEnumerable<string> outputKeys)
    {
        _ports.Clear();
        inputContainer.Clear();
        outputContainer.Clear();

        if (outputKeys != null)
        {
            foreach (string keyName in outputKeys)
            {
                var port = InstantiatePort(Orientation.Horizontal, Direction.Input, Port.Capacity.Multi, typeof(string));
                port.portName = keyName;
                port.pickingMode = PickingMode.Ignore;
                inputContainer.Add(port);
                _ports[keyName] = port;
            }
        }

        RefreshExpandedState();
        RefreshPorts();
    }

    public Port GetPort(string keyName) =>
        _ports.TryGetValue(keyName, out var port) ? port : null;
}

internal class StateSnapshotNode : Node
{
    public Port StateInPort { get; }
    public Port StateOutPort { get; }

    public StateSnapshotNode(AnalyzedStepState state)
    {
        int displayIndex = state != null ? state.stepIndex + 1 : 0;
        title = $"State after Step {displayIndex}";
        capabilities &= ~Capabilities.Deletable;
        capabilities |= Capabilities.Movable | Capabilities.Selectable | Capabilities.Copiable;

        StateInPort = InstantiatePort(Orientation.Horizontal, Direction.Input, Port.Capacity.Multi, typeof(string));
        StateInPort.portName = "State In";
        StateInPort.pickingMode = PickingMode.Ignore;
        inputContainer.Add(StateInPort);

        StateOutPort = InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Multi, typeof(string));
        StateOutPort.portName = "State Out";
        StateOutPort.pickingMode = PickingMode.Ignore;
        outputContainer.Add(StateOutPort);

        BuildBody(state);
        RefreshExpandedState();
        RefreshPorts();
    }

    private void BuildBody(AnalyzedStepState state)
    {
        extensionContainer.Clear();
        var scroll = new ScrollView();
        scroll.style.maxHeight = 200f;

        if (state?.stateKeys == null || state.stateKeys.Count == 0)
        {
            scroll.Add(new Label("No state keys detected."));
        }
        else
        {
            var newSet = new HashSet<string>(state.newKeys ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            foreach (string key in state.stateKeys)
            {
                var label = new Label(key);
                if (newSet.Contains(key))
                {
                    label.style.unityFontStyleAndWeight = FontStyle.Bold;
                }
                scroll.Add(label);
            }
        }

        extensionContainer.Add(scroll);
    }
}

