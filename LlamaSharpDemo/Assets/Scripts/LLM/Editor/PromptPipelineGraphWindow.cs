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
    private const string LocalFileSummaryPrefix = "Local file: ";
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
    private readonly Dictionary<string, string> _textInputValues = new();
    private readonly Dictionary<string, UnityEngine.Object> _imageInputValues = new();
    private readonly Dictionary<string, string> _imageInputSummaries = new();
    private readonly HashSet<Texture2D> _temporaryImageInputs = new();
    private static readonly string[] SupportedLocalImageExtensions = { ".png", ".jpg", ".jpeg" };

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
        ReleaseAllImageInputs();

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
        var inputKeys = model?.keys?.Where(k => k.kind == AnalyzedStateKeyKind.Input).ToList()
                        ?? new List<AnalyzedStateKey>();

        PruneSimulationInputCache(inputKeys);
        _simulationInputs.Clear();
        if (_activeAsset == null)
        {
            _simulationInputs.Add(new Label("Select a PromptPipelineAsset to view its inputs."));
            return;
        }

        if (inputKeys.Count == 0)
        {
            _simulationInputs.Add(new Label("No external inputs required."));
            return;
        }

        foreach (var key in inputKeys)
        {
            if (key.valueKind == AnalyzedStateValueKind.Image)
            {
                CreateImageInputField(key);
                continue;
            }

            CreateTextInputField(key);
        }
    }

    private void CreateTextInputField(AnalyzedStateKey key)
    {
        if (!_textInputValues.ContainsKey(key.keyName))
        {
            _textInputValues[key.keyName] = string.Empty;
        }

        var field = new TextField(key.keyName)
        {
            multiline = true,
            value = _textInputValues[key.keyName],
            style = { minHeight = 40 }
        };
        field.RegisterValueChangedCallback(evt =>
        {
            _textInputValues[key.keyName] = evt.newValue;
        });
        _simulationInputs.Add(field);
    }

    private void CreateImageInputField(AnalyzedStateKey key)
    {
        _imageInputValues.TryGetValue(key.keyName, out UnityEngine.Object currentValue);

        var container = new VisualElement
        {
            style =
            {
                flexDirection = FlexDirection.Column,
                marginBottom = 8
            }
        };

        var field = new ObjectField(key.keyName)
        {
            objectType = typeof(UnityEngine.Object),
            allowSceneObjects = false,
            value = currentValue
        };
        container.Add(field);

        var helpLabel = new Label
        {
            style =
            {
                whiteSpace = WhiteSpace.Normal,
                unityFontStyleAndWeight = FontStyle.Italic,
                marginBottom = 4
            }
        };
        container.Add(helpLabel);

        var preview = new Image
        {
            scaleMode = ScaleMode.ScaleToFit,
            style =
            {
                width = 128,
                height = 128,
                marginBottom = 4,
                backgroundColor = new Color(0.12f, 0.12f, 0.12f, 1f)
            }
        };
        container.Add(preview);

        var buttons = new VisualElement
        {
            style =
            {
                flexDirection = FlexDirection.Row
            }
        };

        var loadButton = new Button(() => LoadLocalImageInput(key.keyName, field, preview, helpLabel))
        {
            text = "Load File..."
        };
        loadButton.style.marginRight = 4;
        buttons.Add(loadButton);

        var validateButton = new Button(() => ValidateImageInput(key.keyName))
        {
            text = "Validate Image"
        };
        validateButton.style.marginRight = 4;
        buttons.Add(validateButton);

        var clearButton = new Button(() =>
        {
            ReleaseImageInput(key.keyName);
            field.SetValueWithoutNotify(null);
            UpdateImagePreview(preview, helpLabel, key.keyName, null);
        })
        {
            text = "Clear"
        };
        buttons.Add(clearButton);

        container.Add(buttons);

        field.RegisterValueChangedCallback(evt =>
        {
            if (!IsSupportedImageObject(evt.newValue))
            {
                EditorUtility.DisplayDialog(
                    "Unsupported Image Input",
                    "Only Texture2D or Sprite assets can be assigned directly. Use 'Load File...' for local PNG/JPG images.",
                    "OK");
                field.SetValueWithoutNotify(evt.previousValue);
                return;
            }

            SetImageInputValue(key.keyName, evt.newValue);
            UpdateImagePreview(preview, helpLabel, key.keyName, evt.newValue);
        });

        UpdateImagePreview(preview, helpLabel, key.keyName, currentValue);
        _simulationInputs.Add(container);
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

    private PipelineState BuildSimulationState()
    {
        var state = new PipelineState();
        var inputKeys = _graphView?.CurrentStateModel?.keys?
            .Where(key => key.kind == AnalyzedStateKeyKind.Input)
            .ToList();

        if (inputKeys == null)
        {
            return state;
        }

        foreach (var key in inputKeys.Where(key => key.valueKind != AnalyzedStateValueKind.Image))
        {
            state.SetString(key.keyName, _textInputValues.TryGetValue(key.keyName, out string value) ? value : string.Empty);
        }

        foreach (var key in inputKeys.Where(key => key.valueKind == AnalyzedStateValueKind.Image))
        {
            if (_imageInputValues.TryGetValue(key.keyName, out UnityEngine.Object imageValue) && imageValue != null)
            {
                state.SetImage(key.keyName, imageValue);
            }
        }

        return state;
    }

    private void OnSimulationCompleted(PipelineState resultState)
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

    private void ValidateImageInput(string keyName)
    {
        if (!_imageInputValues.TryGetValue(keyName, out UnityEngine.Object imageValue) || imageValue == null)
        {
            AppendSimulationLog($"Image input '{keyName}' is empty.");
            _simulationStatus.text = $"Image '{keyName}' is empty";
            return;
        }

        if (!PipelineImageUtility.TryNormalize(imageValue, 1024, out var normalized, out string error))
        {
            AppendSimulationLog($"Image input '{keyName}' failed validation: {error}");
            _simulationStatus.text = $"Image validation failed: {keyName}";
            return;
        }

        using (normalized)
        {
            AppendSimulationLog(
                $"Validated image '{keyName}': {normalized.Texture.width}x{normalized.Texture.height} from {normalized.SourceSummary}.");
            _simulationStatus.text = $"Image '{keyName}' validated";
        }
    }

    private static bool IsSupportedImageObject(UnityEngine.Object value)
    {
        return value == null || value is Texture2D || value is Sprite;
    }

    private void UpdateImagePreview(Image preview, Label helpLabel, string keyName, UnityEngine.Object imageValue)
    {
        if (preview == null || helpLabel == null)
        {
            return;
        }

        preview.image = ResolvePreviewTexture(imageValue);
        helpLabel.text = imageValue == null
            ? "Assign a Texture2D or Sprite asset, or use 'Load File...' for a local PNG/JPG image."
            : ResolveImageInputSummary(keyName, imageValue);
    }

    private static Texture ResolvePreviewTexture(UnityEngine.Object imageValue)
    {
        if (imageValue == null)
        {
            return null;
        }

        Texture2D assetPreview = AssetPreview.GetAssetPreview(imageValue) ?? AssetPreview.GetMiniThumbnail(imageValue);
        if (assetPreview != null)
        {
            return assetPreview;
        }

        return imageValue switch
        {
            Texture2D texture => texture,
            Sprite sprite => sprite.texture,
            _ => null
        };
    }

    private void LoadLocalImageInput(string keyName, ObjectField field, Image preview, Label helpLabel)
    {
        string initialDirectory = GetImageFilePanelDirectory(keyName);
        string selectedPath = EditorUtility.OpenFilePanel("Select reference image", initialDirectory, string.Empty);
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            return;
        }

        if (!TryLoadTextureFromFile(selectedPath, out Texture2D texture, out string error))
        {
            EditorUtility.DisplayDialog("Load Image Failed", error, "OK");
            return;
        }

        SetImageInputValue(keyName, texture, ownsTexture: true, summary: $"{LocalFileSummaryPrefix}{selectedPath}");
        field.SetValueWithoutNotify(texture);
        UpdateImagePreview(preview, helpLabel, keyName, texture);
    }

    private void SetImageInputValue(string keyName, UnityEngine.Object imageValue, bool ownsTexture = false, string summary = null)
    {
        ReleaseImageInput(keyName);

        if (imageValue == null)
        {
            return;
        }

        _imageInputValues[keyName] = imageValue;

        if (ownsTexture && imageValue is Texture2D texture)
        {
            _temporaryImageInputs.Add(texture);
        }

        if (!string.IsNullOrWhiteSpace(summary))
        {
            _imageInputSummaries[keyName] = summary;
        }
    }

    private void ReleaseImageInput(string keyName)
    {
        if (_imageInputValues.TryGetValue(keyName, out UnityEngine.Object imageValue) &&
            imageValue is Texture2D texture &&
            _temporaryImageInputs.Remove(texture))
        {
            DestroyTemporaryTexture(texture);
        }

        _imageInputValues.Remove(keyName);
        _imageInputSummaries.Remove(keyName);
    }

    private void ReleaseAllImageInputs()
    {
        foreach (string keyName in _imageInputValues.Keys.ToList())
        {
            ReleaseImageInput(keyName);
        }

        _imageInputValues.Clear();
        _imageInputSummaries.Clear();
        _temporaryImageInputs.Clear();
    }

    private void PruneSimulationInputCache(IReadOnlyCollection<AnalyzedStateKey> inputKeys)
    {
        var validKeys = new HashSet<string>(inputKeys.Select(key => key.keyName), StringComparer.Ordinal);

        foreach (string staleTextKey in _textInputValues.Keys.Where(key => !validKeys.Contains(key)).ToList())
        {
            _textInputValues.Remove(staleTextKey);
        }

        foreach (string staleImageKey in _imageInputValues.Keys.Where(key => !validKeys.Contains(key)).ToList())
        {
            ReleaseImageInput(staleImageKey);
        }
    }

    private string ResolveImageInputSummary(string keyName, UnityEngine.Object imageValue)
    {
        if (_imageInputSummaries.TryGetValue(keyName, out string summary) && !string.IsNullOrWhiteSpace(summary))
        {
            return summary;
        }

        return PipelineState.DescribeValue(imageValue);
    }

    private string GetImageFilePanelDirectory(string keyName)
    {
        if (_imageInputSummaries.TryGetValue(keyName, out string summary) &&
            summary.StartsWith(LocalFileSummaryPrefix, StringComparison.Ordinal))
        {
            string localPath = summary.Substring(LocalFileSummaryPrefix.Length);
            if (File.Exists(localPath))
            {
                return Path.GetDirectoryName(localPath);
            }
        }

        if (_imageInputValues.TryGetValue(keyName, out UnityEngine.Object imageValue) && imageValue != null)
        {
            string assetPath = AssetDatabase.GetAssetPath(imageValue);
            if (!string.IsNullOrWhiteSpace(assetPath))
            {
                string absolutePath = Path.GetFullPath(assetPath);
                if (File.Exists(absolutePath))
                {
                    return Path.GetDirectoryName(absolutePath);
                }
            }
        }

        return Application.dataPath;
    }

    private static bool TryLoadTextureFromFile(string path, out Texture2D texture, out string error)
    {
        texture = null;
        error = null;

        if (string.IsNullOrWhiteSpace(path))
        {
            error = "Image path is empty.";
            return false;
        }

        if (!File.Exists(path))
        {
            error = $"Image file not found: {path}";
            return false;
        }

        string extension = Path.GetExtension(path);
        if (!SupportedLocalImageExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            error = $"Unsupported image format '{extension}'. Use PNG or JPG/JPEG.";
            return false;
        }

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex)
        {
            error = $"Failed to read image file: {ex.Message}";
            return false;
        }

        texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
        {
            name = Path.GetFileName(path),
            hideFlags = HideFlags.HideAndDontSave
        };

        if (texture.LoadImage(bytes, markNonReadable: false))
        {
            return true;
        }

        DestroyTemporaryTexture(texture);
        texture = null;
        error = "Unity could not decode the selected image. Use PNG or JPG/JPEG.";
        return false;
    }

    private static void DestroyTemporaryTexture(Texture2D texture)
    {
        if (texture == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            UnityEngine.Object.Destroy(texture);
        }
        else
        {
            UnityEngine.Object.DestroyImmediate(texture);
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
