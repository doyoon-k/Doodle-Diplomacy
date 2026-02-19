using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

public class PromptPipelineGraphWindow : EditorWindow
{
    private PromptPipelineGraphView _graphView;
    private PromptPipelineAsset _activeAsset;
    private PromptPipelineCommandManager _commandManager;
    private ObjectField _assetField;
    private TextField _displayNameField;
    private TextField _descriptionField;
    private ScrollView _simulationInputs;
    private Label _simulationStatus;
    private Button _runButton;
    private ScrollView _simulationLogScroll;
    private TextField _simulationLogField;
    private bool _isSimulating;
    private readonly Dictionary<string, string> _inputValues = new();

    [MenuItem("Window/LLM/Prompt Pipeline Editor")]
    public static void ShowWindow()
    {
        var window = GetWindow<PromptPipelineGraphWindow>();
        window.titleContent = new GUIContent("Prompt Pipeline");
        window.Show();
    }

    private void OnEnable()
    {
        ConstructUI();
        if (_activeAsset != null)
        {
            _graphView.SetAsset(_activeAsset);
            RebuildSimulationInputs(_graphView.CurrentStateModel);
        }
        else
        {
            RebuildSimulationInputs(null);
        }

        _assetField?.SetValueWithoutNotify(_activeAsset);
        RefreshMetadataFieldsFromAsset();
        UpdateWindowTitle();
    }

    private void OnDisable()
    {
        if (_graphView != null)
        {
            _graphView.StateModelChanged -= OnStateModelChanged;
            _graphView.Dispose();
        }
    }

    private void ConstructUI()
    {
        if (_graphView != null)
        {
            _graphView.StateModelChanged -= OnStateModelChanged;
            _graphView.Dispose();
        }

        rootVisualElement.Clear();
        rootVisualElement.style.flexDirection = FlexDirection.Column;

        BuildToolbar();
        BuildAssetMetadataSection();
        BuildMainArea();
        RefreshMetadataFieldsFromAsset();
        rootVisualElement.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
    }

    private void BuildToolbar()
    {
        var toolbar = new Toolbar();

        _assetField = new ObjectField("Pipeline Asset")
        {
            objectType = typeof(PromptPipelineAsset),
            value = _activeAsset
        };
        _assetField.RegisterValueChangedCallback(evt =>
        {
            if (evt.newValue == evt.previousValue)
            {
                return;
            }

            _activeAsset = evt.newValue as PromptPipelineAsset;
            OnAssetChanged();
        });
        toolbar.Add(_assetField);

        toolbar.Add(new Button(CreateNewAsset)
        {
            text = "New Asset"
        });

        toolbar.Add(new Button(() => _graphView?.CreateStepAtCenter())
        {
            text = "Add Step"
        });
        toolbar.Add(new Button(SaveAsset) { text = "Save" });
        toolbar.Add(new Button(ValidateAsset) { text = "Validate" });
        toolbar.Add(new Button(RunSimulation) { text = "Run" });
        toolbar.Add(new Button(PingAssetInProject) { text = "Ping Asset" });

        rootVisualElement.Add(toolbar);
    }

    private void BuildAssetMetadataSection()
    {
        var container = new VisualElement
        {
            style =
            {
                flexDirection = FlexDirection.Column,
                paddingLeft = 8,
                paddingRight = 8,
                paddingTop = 4,
                paddingBottom = 4
            }
        };

        var header = new Label("Pipeline Info")
        {
            style =
            {
                unityFontStyleAndWeight = FontStyle.Bold,
                marginBottom = 2
            }
        };
        container.Add(header);

        _displayNameField = new TextField("Name")
        {
            isDelayed = true
        };
        _displayNameField.style.flexGrow = 1f;
        _displayNameField.RegisterValueChangedCallback(evt =>
        {
            if (_activeAsset == null || _commandManager == null)
            {
                return;
            }

            string newName = evt.newValue?.Trim() ?? string.Empty;
            if (string.Equals(newName, _activeAsset.displayName, StringComparison.Ordinal))
            {
                return;
            }

            _commandManager.Execute("Rename Pipeline", () =>
            {
                _activeAsset.displayName = newName;
                UpdateWindowTitle();
            });
        });
        container.Add(_displayNameField);

        _descriptionField = new TextField("Description")
        {
            multiline = true
        };
        _descriptionField.style.flexGrow = 1f;
        _descriptionField.style.minHeight = 60f;
        _descriptionField.RegisterValueChangedCallback(evt =>
        {
            if (_activeAsset == null || _commandManager == null)
            {
                return;
            }

            string newDescription = evt.newValue ?? string.Empty;
            if (string.Equals(newDescription, _activeAsset.description, StringComparison.Ordinal))
            {
                return;
            }

            _commandManager.Execute("Edit Pipeline Description", () =>
            {
                _activeAsset.description = newDescription;
            });
        });
        container.Add(_descriptionField);

        rootVisualElement.Add(container);
    }

    private void BuildMainArea()
    {
        var split = new TwoPaneSplitView(0, 600, TwoPaneSplitViewOrientation.Vertical);

        var graphContainer = new VisualElement { style = { flexGrow = 1f } };
        _graphView = new PromptPipelineGraphView(MarkAssetDirty, ExecuteCommand);
        _graphView.StateModelChanged += OnStateModelChanged;
        graphContainer.Add(_graphView);
        split.Add(graphContainer);

        _commandManager = new PromptPipelineCommandManager(() => _activeAsset, RefreshGraphAfterHistoryChange, MarkAssetDirty);

        var simulationContainer = new VisualElement
        {
            style =
            {
                flexDirection = FlexDirection.Column,
                paddingLeft = 8,
                paddingRight = 8,
                paddingBottom = 8
            }
        };

        var simulationHeader = new Label("Simulation")
        {
            style =
            {
                unityFontStyleAndWeight = FontStyle.Bold,
                marginTop = 4,
                marginBottom = 4
            }
        };
        simulationContainer.Add(simulationHeader);

        _simulationInputs = new ScrollView
        {
            style =
            {
                flexGrow = 1f,
                minHeight = 120
            }
        };
        simulationContainer.Add(_simulationInputs);

        _runButton = new Button(RunSimulation) { text = "Run Pipeline" };
        simulationContainer.Add(_runButton);

        _simulationStatus = new Label("Idle");
        simulationContainer.Add(_simulationStatus);

        _simulationLogScroll = new ScrollView(ScrollViewMode.Vertical)
        {
            style =
            {
                flexGrow = 1f,
                minHeight = 160
            }
        };
        _simulationLogScroll.AddToClassList("simulation-log-scroll");

        _simulationLogField = new TextField("Step Log")
        {
            multiline = true,
            isReadOnly = true
        };
        _simulationLogField.style.flexGrow = 1f;
        _simulationLogField.style.whiteSpace = WhiteSpace.Normal;
        _simulationLogScroll.Add(_simulationLogField);

        simulationContainer.Add(_simulationLogScroll);

        split.Add(simulationContainer);
        rootVisualElement.Add(split);
    }

    private void OnAssetChanged()
    {
        UpdateWindowTitle();
        _assetField?.SetValueWithoutNotify(_activeAsset);
        _commandManager?.Reset();
        _graphView.SetAsset(_activeAsset);
        RebuildSimulationInputs(_graphView.CurrentStateModel);
        RefreshMetadataFieldsFromAsset();
    }

    private void CreateNewAsset()
    {
        string path = EditorUtility.SaveFilePanelInProject(
            "Create Prompt Pipeline Asset",
            "PromptPipeline",
            "asset",
            "Select where to save the new PromptPipelineAsset.");

        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        path = AssetDatabase.GenerateUniqueAssetPath(path);

        var asset = ScriptableObject.CreateInstance<PromptPipelineAsset>();
        asset.displayName = Path.GetFileNameWithoutExtension(path);
        asset.description = string.Empty;
        asset.steps = new List<PromptPipelineStep>();
        asset.layoutSettings = new PromptPipelineLayoutSettings();

        AssetDatabase.CreateAsset(asset, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Selection.activeObject = asset;
        _activeAsset = asset;
        _assetField?.SetValueWithoutNotify(asset);

        OnAssetChanged();
    }

    private void RefreshMetadataFieldsFromAsset()
    {
        if (_displayNameField == null || _descriptionField == null)
        {
            return;
        }

        bool hasAsset = _activeAsset != null;
        _displayNameField.SetEnabled(hasAsset);
        _descriptionField.SetEnabled(hasAsset);

        if (!hasAsset)
        {
            _displayNameField.SetValueWithoutNotify(string.Empty);
            _descriptionField.SetValueWithoutNotify(string.Empty);
            return;
        }

        _displayNameField.SetValueWithoutNotify(_activeAsset.displayName);
        _descriptionField.SetValueWithoutNotify(_activeAsset.description);
    }

    private void UpdateWindowTitle()
    {
        titleContent = new GUIContent(_activeAsset != null
            ? $"Prompt Pipeline - {_activeAsset.displayName}"
            : "Prompt Pipeline");
    }

    private void OnStateModelChanged(AnalyzedStateModel model)
    {
        RebuildSimulationInputs(model);
    }

    private void RebuildSimulationInputs(AnalyzedStateModel model)
    {
        _simulationInputs.Clear();
        if (_activeAsset == null)
        {
            _simulationInputs.Add(new Label("Select a PromptPipelineAsset to view its inputs."));
            return;
        }

        var inputKeys = model?.keys?.Where(k => k.kind == AnalyzedStateKeyKind.Input).ToList()
                        ?? new List<AnalyzedStateKey>();

        if (inputKeys.Count == 0)
        {
            _simulationInputs.Add(new Label("No external inputs required."));
            return;
        }

        foreach (var key in inputKeys)
        {
            if (!_inputValues.ContainsKey(key.keyName))
            {
                _inputValues[key.keyName] = string.Empty;
            }

            var field = new TextField(key.keyName)
            {
                multiline = true,
                value = _inputValues[key.keyName],
                style = { minHeight = 40 }
            };
            field.RegisterValueChangedCallback(evt =>
            {
                _inputValues[key.keyName] = evt.newValue;
            });
            _simulationInputs.Add(field);
        }
    }

    private void ClearSimulationLog()
    {
        if (_simulationLogField != null)
        {
            _simulationLogField.value = string.Empty;
        }
    }

    private void AppendSimulationLog(string message)
    {
        if (_simulationLogField == null)
        {
            return;
        }

        if (!string.IsNullOrEmpty(_simulationLogField.value))
        {
            _simulationLogField.value += "\n";
        }

        _simulationLogField.value += $"[{DateTime.Now:HH:mm:ss}] {message}";
    }

    private void RunSimulation()
    {
        if (_activeAsset == null)
        {
            EditorUtility.DisplayDialog("Prompt Pipeline", "Select a pipeline asset first.", "OK");
            return;
        }

        if (_isSimulating)
        {
            return;
        }

        _isSimulating = true;
        _runButton.SetEnabled(false);
        _simulationStatus.text = "Running...";
        ClearSimulationLog();
        AppendSimulationLog($"Started simulation for '{_activeAsset.displayName}'.");

        PromptPipelineSimulator.Run(
            _activeAsset,
            BuildSimulationState(),
            OnSimulationCompleted,
            OnSimulationFailed,
            AppendSimulationLog
        );
    }

    private Dictionary<string, string> BuildSimulationState()
    {
        var state = new Dictionary<string, string>();
        foreach (var kvp in _inputValues)
        {
            state[kvp.Key] = kvp.Value;
        }
        return state;
    }

    private void OnSimulationCompleted(Dictionary<string, string> resultState)
    {
        _isSimulating = false;
        _runButton.SetEnabled(true);
        _simulationStatus.text = $"Completed at {DateTime.Now:T}";
        AppendSimulationLog("Simulation completed successfully.");

        if (resultState != null)
        {
            _graphView.ApplySimulationResult(resultState);
        }
    }

    private void OnSimulationFailed(string error)
    {
        _isSimulating = false;
        _runButton.SetEnabled(true);
        _simulationStatus.text = $"Error: {error}";
        AppendSimulationLog($"Simulation failed: {error}");
        Debug.LogError($"Prompt Pipeline Simulation failed: {error}");
    }

    private void SaveAsset()
    {
        if (_activeAsset == null)
        {
            return;
        }

        EditorUtility.SetDirty(_activeAsset);
        AssetDatabase.SaveAssets();
    }

    private void ValidateAsset()
    {
        if (_activeAsset == null)
        {
            EditorUtility.DisplayDialog("Prompt Pipeline", "Select a pipeline asset first.", "OK");
            return;
        }

        var model = PipelineStateAnalyzer.Analyze(_activeAsset);
        string summary = $"Steps: {model.stepCount}\nState Keys: {model.keys.Count}";
        EditorUtility.DisplayDialog("Pipeline Validation", summary, "OK");
    }

    private void PingAssetInProject()
    {
        if (_activeAsset != null)
        {
            EditorGUIUtility.PingObject(_activeAsset);
        }
    }

    private void MarkAssetDirty()
    {
        if (_activeAsset != null)
        {
            EditorUtility.SetDirty(_activeAsset);
        }
    }

    private void ExecuteCommand(string label, Action mutate) => _commandManager?.Execute(label, mutate);

    private void RefreshGraphAfterHistoryChange()
    {
        if (_activeAsset == null || _graphView == null)
        {
            return;
        }

        _assetField?.SetValueWithoutNotify(_activeAsset);
        RefreshMetadataFieldsFromAsset();
        UpdateWindowTitle();
        _graphView.SetAsset(_activeAsset, skipSnapshotCache: true, skipSnapshotPersistence: true);
        RebuildSimulationInputs(_graphView.CurrentStateModel);
        if (_simulationStatus != null)
        {
            _simulationStatus.text = "Undo/Redo applied";
        }
        Repaint();
    }

    private void OnKeyDown(KeyDownEvent evt)
    {
        bool isCtrl = evt.ctrlKey || evt.commandKey;
        if (!isCtrl)
        {
            return;
        }

        if (evt.keyCode == KeyCode.Z && evt.shiftKey)
        {
            _commandManager?.Redo();
            evt.StopPropagation();
        }
        else if (evt.keyCode == KeyCode.Z)
        {
            _commandManager?.Undo();
            evt.StopPropagation();
        }
        else if (evt.keyCode == KeyCode.Y)
        {
            _commandManager?.Redo();
            evt.StopPropagation();
        }
    }
}
