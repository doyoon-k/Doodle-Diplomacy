using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

[CustomEditor(typeof(LlmGenerationProfile))]
public class LlmGenerationProfileEditor : Editor
{
    public static bool JsonFieldsEnabled = true;
    public static string JsonFieldsDisabledMessage;

    // Unity serialization depth limit is 10; we keep a stricter ceiling to avoid hitting it.
    private const int MaxNestingDepth = 6;
    private const string DefaultStreamingModelsRelativeFolder = "Models";

    private readonly Dictionary<string, ReorderableList> _listCache = new();
    private readonly Dictionary<string, int> _selectionByPath = new();
    private SerializedProperty _modelProp;
    private SerializedProperty _streamProp;
    private SerializedProperty _keepAliveProp;
    private SerializedProperty _systemPromptProp;
    private SerializedProperty _modelParamsProp;
    private SerializedProperty _runtimeParamsProp;
    private SerializedProperty _jsonFieldsProp;

    private void OnEnable()
    {
        _modelProp = serializedObject.FindProperty("model");
        _streamProp = serializedObject.FindProperty("stream");
        _keepAliveProp = serializedObject.FindProperty("keepAlive");
        _systemPromptProp = serializedObject.FindProperty("systemPromptTemplate");
        _modelParamsProp = serializedObject.FindProperty("modelParams");
        _runtimeParamsProp = serializedObject.FindProperty("runtimeParams");
        _jsonFieldsProp = serializedObject.FindProperty("jsonFields");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUI.BeginChangeCheck();
        EditorGUILayout.LabelField("Basic Settings", EditorStyles.boldLabel);
        DrawModelSetupSection();
        EditorGUILayout.PropertyField(_streamProp);
        EditorGUILayout.PropertyField(_keepAliveProp);
        EditorGUILayout.PropertyField(_systemPromptProp);
        EditorGUILayout.PropertyField(_modelParamsProp, true);
        EditorGUILayout.PropertyField(_runtimeParamsProp, true);
        bool basicSettingsChanged = EditorGUI.EndChangeCheck();

        EditorGUILayout.Space(12f);

        bool schemaChanged = DrawJsonSchemaBuilder();

        serializedObject.ApplyModifiedProperties();

        if (basicSettingsChanged || schemaChanged)
        {
            foreach (var targetObject in targets)
            {
                if (targetObject is LlmGenerationProfile settings)
                {
                    if (schemaChanged)
                    {
                        settings.RebuildFormatFromFields();
                    }

                    EditorUtility.SetDirty(settings);
                    LlmSettingsChangeNotifier.RaiseChanged(settings);
                }
            }

            Repaint();
        }

        DrawFormatPreview();
    }

    private void DrawModelSetupSection()
    {
        EditorGUILayout.PropertyField(_modelProp, new GUIContent("Model Path"));

        SerializedProperty relativeToStreamingAssetsProp = _runtimeParamsProp?.FindPropertyRelative("modelPathRelativeToStreamingAssets");
        List<string> streamingModelChoices = GetStreamingAssetModelChoices();

        if (streamingModelChoices.Count > 0)
        {
            string current = NormalizePath(_modelProp.stringValue);
            int currentIndex = streamingModelChoices.FindIndex(path => string.Equals(path, current, StringComparison.OrdinalIgnoreCase));

            var popupEntries = new List<string> { "(manual path)" };
            popupEntries.AddRange(streamingModelChoices);

            int popupIndex = currentIndex >= 0 ? currentIndex + 1 : 0;
            int selectedPopupIndex = EditorGUILayout.Popup(new GUIContent("StreamingAssets Model"), popupIndex, popupEntries.ToArray());
            if (selectedPopupIndex > 0 && selectedPopupIndex != popupIndex)
            {
                _modelProp.stringValue = streamingModelChoices[selectedPopupIndex - 1];
                if (relativeToStreamingAssetsProp != null)
                {
                    relativeToStreamingAssetsProp.boolValue = true;
                }
            }
        }
        else
        {
            EditorGUILayout.HelpBox(
                "No .gguf files found under Assets/StreamingAssets. Use 'Import GGUF To StreamingAssets' to add one.",
                MessageType.Info);
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Select GGUF..."))
            {
                SelectModelFile(relativeToStreamingAssetsProp);
            }

            if (GUILayout.Button("Import GGUF To StreamingAssets"))
            {
                ImportModelToStreamingAssets(relativeToStreamingAssetsProp);
            }
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Apply Model To All Profiles"))
            {
                serializedObject.ApplyModifiedProperties();
                bool relativeToStreaming = relativeToStreamingAssetsProp != null && relativeToStreamingAssetsProp.boolValue;
                ApplyModelToAllProfiles(_modelProp.stringValue, relativeToStreaming);
                serializedObject.Update();
            }

            string resolvedPath = ResolveModelPathForEditor(
                _modelProp.stringValue,
                relativeToStreamingAssetsProp != null && relativeToStreamingAssetsProp.boolValue);
            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(resolvedPath) || !File.Exists(resolvedPath)))
            {
                if (GUILayout.Button("Reveal Model File"))
                {
                    EditorUtility.RevealInFinder(resolvedPath);
                }
            }
        }

        DrawModelStatus(relativeToStreamingAssetsProp != null && relativeToStreamingAssetsProp.boolValue);
    }

    private void DrawModelStatus(bool relativeToStreamingAssets)
    {
        string modelPath = _modelProp.stringValue?.Trim();
        if (string.IsNullOrWhiteSpace(modelPath))
        {
            EditorGUILayout.HelpBox("Model path is empty.", MessageType.Warning);
            return;
        }

        if (!modelPath.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase))
        {
            EditorGUILayout.HelpBox("Model path should point to a .gguf file.", MessageType.Warning);
        }

        string resolved = ResolveModelPathForEditor(modelPath, relativeToStreamingAssets);
        if (string.IsNullOrWhiteSpace(resolved))
        {
            EditorGUILayout.HelpBox("Could not resolve the model path.", MessageType.Warning);
            return;
        }

        if (File.Exists(resolved))
        {
            EditorGUILayout.HelpBox($"Resolved model file:\n{resolved}", MessageType.Info);
        }
        else
        {
            EditorGUILayout.HelpBox($"Model file not found:\n{resolved}", MessageType.Error);
        }
    }

    private void SelectModelFile(SerializedProperty relativeToStreamingAssetsProp)
    {
        string preferredFolder = Path.Combine(Application.streamingAssetsPath, DefaultStreamingModelsRelativeFolder);
        string initialFolder = Directory.Exists(preferredFolder) ? preferredFolder : Application.dataPath;
        string selectedPath = EditorUtility.OpenFilePanel("Select GGUF model", initialFolder, "gguf");
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            return;
        }

        if (TryGetStreamingAssetsRelativePath(selectedPath, out string streamingRelativePath))
        {
            _modelProp.stringValue = streamingRelativePath;
            if (relativeToStreamingAssetsProp != null)
            {
                relativeToStreamingAssetsProp.boolValue = true;
            }
        }
        else
        {
            _modelProp.stringValue = selectedPath;
            if (relativeToStreamingAssetsProp != null)
            {
                relativeToStreamingAssetsProp.boolValue = false;
            }
        }
    }

    private void ImportModelToStreamingAssets(SerializedProperty relativeToStreamingAssetsProp)
    {
        string sourcePath = EditorUtility.OpenFilePanel("Import GGUF model", string.Empty, "gguf");
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return;
        }

        try
        {
            string destinationDirectory = Path.Combine(Application.streamingAssetsPath, DefaultStreamingModelsRelativeFolder);
            Directory.CreateDirectory(destinationDirectory);

            string fileName = Path.GetFileName(sourcePath);
            string destinationPath = Path.Combine(destinationDirectory, fileName);
            string sourceFullPath = Path.GetFullPath(sourcePath);
            string destinationFullPath = Path.GetFullPath(destinationPath);

            bool sameFile = string.Equals(sourceFullPath, destinationFullPath, StringComparison.OrdinalIgnoreCase);
            if (!sameFile && File.Exists(destinationFullPath))
            {
                bool replace = EditorUtility.DisplayDialog(
                    "Replace Existing Model",
                    $"A model with the same name already exists:\n{destinationFullPath}\n\nReplace it?",
                    "Replace",
                    "Cancel");

                if (!replace)
                {
                    return;
                }
            }

            if (!sameFile)
            {
                File.Copy(sourceFullPath, destinationFullPath, true);
            }

            AssetDatabase.Refresh();

            if (TryGetStreamingAssetsRelativePath(destinationFullPath, out string relativePath))
            {
                _modelProp.stringValue = relativePath;
                if (relativeToStreamingAssetsProp != null)
                {
                    relativeToStreamingAssetsProp.boolValue = true;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[LlmGenerationProfileEditor] Failed to import GGUF file: {ex}");
            EditorUtility.DisplayDialog("Import Failed", ex.Message, "OK");
        }
    }

    private static void ApplyModelToAllProfiles(string modelPath, bool relativeToStreamingAssets)
    {
        string[] profileGuids = AssetDatabase.FindAssets("t:LlmGenerationProfile");
        int updatedCount = 0;

        foreach (string guid in profileGuids)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            var profile = AssetDatabase.LoadAssetAtPath<LlmGenerationProfile>(assetPath);
            if (profile == null)
            {
                continue;
            }

            var profileSerializedObject = new SerializedObject(profile);
            profileSerializedObject.Update();

            var modelProp = profileSerializedObject.FindProperty("model");
            var runtimeProp = profileSerializedObject.FindProperty("runtimeParams");
            var relativeProp = runtimeProp?.FindPropertyRelative("modelPathRelativeToStreamingAssets");

            bool dirty = false;
            if (modelProp != null && modelProp.stringValue != modelPath)
            {
                modelProp.stringValue = modelPath;
                dirty = true;
            }

            if (relativeProp != null && relativeProp.boolValue != relativeToStreamingAssets)
            {
                relativeProp.boolValue = relativeToStreamingAssets;
                dirty = true;
            }

            if (!dirty)
            {
                continue;
            }

            profileSerializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(profile);
            LlmSettingsChangeNotifier.RaiseChanged(profile);
            updatedCount++;
        }

        if (updatedCount > 0)
        {
            AssetDatabase.SaveAssets();
        }

        EditorUtility.DisplayDialog("Apply Model", $"Updated {updatedCount} profile(s).", "OK");
    }

    private static List<string> GetStreamingAssetModelChoices()
    {
        if (string.IsNullOrWhiteSpace(Application.streamingAssetsPath) || !Directory.Exists(Application.streamingAssetsPath))
        {
            return new List<string>();
        }

        var files = Directory
            .GetFiles(Application.streamingAssetsPath, "*.gguf", SearchOption.AllDirectories)
            .Select(path =>
            {
                TryGetStreamingAssetsRelativePath(path, out string relativePath);
                return relativePath;
            })
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return files;
    }

    private static bool TryGetStreamingAssetsRelativePath(string absolutePath, out string relativePath)
    {
        relativePath = null;
        if (string.IsNullOrWhiteSpace(absolutePath) || string.IsNullOrWhiteSpace(Application.streamingAssetsPath))
        {
            return false;
        }

        string streamingRoot = Path.GetFullPath(Application.streamingAssetsPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string candidatePath = Path.GetFullPath(absolutePath);

        if (!candidatePath.StartsWith(streamingRoot, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string trimmed = candidatePath.Substring(streamingRoot.Length)
            .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        relativePath = NormalizePath(trimmed);
        return !string.IsNullOrWhiteSpace(relativePath);
    }

    private static string ResolveModelPathForEditor(string modelPath, bool relativeToStreamingAssets)
    {
        if (string.IsNullOrWhiteSpace(modelPath))
        {
            return string.Empty;
        }

        string trimmed = modelPath.Trim();
        if (Path.IsPathRooted(trimmed))
        {
            return Path.GetFullPath(trimmed);
        }

        if (relativeToStreamingAssets)
        {
            return Path.GetFullPath(Path.Combine(Application.streamingAssetsPath, trimmed));
        }

        return Path.GetFullPath(trimmed);
    }

    private static string NormalizePath(string path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : path.Replace('\\', '/');
    }

    private bool DrawJsonSchemaBuilder()
    {
        bool changed = false;

        EditorGUILayout.LabelField("JSON Output Fields", EditorStyles.boldLabel);
        if (!JsonFieldsEnabled && !string.IsNullOrEmpty(JsonFieldsDisabledMessage))
        {
            EditorGUILayout.HelpBox(JsonFieldsDisabledMessage, MessageType.Info);
        }

        EditorGUILayout.HelpBox(
            "Add the keys you expect from the LLM. The generated JSON schema and analyzer output keys are built from this list.",
            MessageType.Info
        );

        // Clamp excessive nesting to avoid Unity serialization depth limit errors.
        ClampDepth(_jsonFieldsProp, 0);

        EditorGUI.BeginChangeCheck();
        using (new EditorGUI.DisabledScope(!JsonFieldsEnabled))
        {
            var list = GetOrCreateList(_jsonFieldsProp, "JSON Output Fields");
            list.DoLayoutList();
            changed |= DrawSelectedFieldDetails(_jsonFieldsProp, 0);
        }

        changed |= EditorGUI.EndChangeCheck();

        return changed;
    }

    private void InitializeFieldDefaults(SerializedProperty element)
    {
        element.FindPropertyRelative(nameof(JsonFieldDefinition.fieldName)).stringValue = "field";
        element.FindPropertyRelative(nameof(JsonFieldDefinition.fieldType)).enumValueIndex = (int)JsonFieldType.String;
        element.FindPropertyRelative(nameof(JsonFieldDefinition.arrayElementType)).enumValueIndex = (int)JsonArrayElementType.String;
        element.FindPropertyRelative(nameof(JsonFieldDefinition.minValue)).stringValue = string.Empty;
        element.FindPropertyRelative(nameof(JsonFieldDefinition.maxValue)).stringValue = string.Empty;
        var enums = element.FindPropertyRelative(nameof(JsonFieldDefinition.enumOptions));
        if (enums != null)
        {
            while (enums.arraySize > 0)
            {
                enums.DeleteArrayElementAtIndex(enums.arraySize - 1);
            }
        }
        var children = element.FindPropertyRelative(nameof(JsonFieldDefinition.children));
        if (children != null)
        {
            while (children.arraySize > 0)
            {
                children.DeleteArrayElementAtIndex(children.arraySize - 1);
            }
        }
    }

    private void DrawFormatPreview()
    {
        var settings = (LlmGenerationProfile)target;
        string preview = string.IsNullOrWhiteSpace(settings.format) ? "(no fields defined)" : settings.format;

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("Generated Format (read-only)", EditorStyles.boldLabel);
        var style = new GUIStyle(EditorStyles.textArea) { wordWrap = true };

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Open Preview Window", GUILayout.Width(160f)))
            {
                LlmFormatPreviewWindow.Show(preview);
            }
        }

        _previewScroll = EditorGUILayout.BeginScrollView(_previewScroll, GUILayout.MinHeight(60f));
        EditorGUILayout.SelectableLabel(preview, style, GUILayout.ExpandHeight(true));
        EditorGUILayout.EndScrollView();
    }

    private void DrawEnumList(SerializedProperty enumProp)
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("Enum Options (optional)", EditorStyles.boldLabel);
            for (int i = 0; i < enumProp.arraySize; i++)
            {
                EditorGUILayout.BeginHorizontal();
                var element = enumProp.GetArrayElementAtIndex(i);
                element.stringValue = EditorGUILayout.TextField(element.stringValue);
                if (GUILayout.Button("X", GUILayout.Width(24f)))
                {
                    enumProp.DeleteArrayElementAtIndex(i);
                    EditorGUILayout.EndHorizontal();
                    break;
                }
                EditorGUILayout.EndHorizontal();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("+ Add Option", GUILayout.Width(120f)))
                {
                    int newIdx = enumProp.arraySize;
                    enumProp.InsertArrayElementAtIndex(newIdx);
                    enumProp.GetArrayElementAtIndex(newIdx).stringValue = string.Empty;
                }
            }
        }
    }

    private Vector2 _previewScroll;

    private ReorderableList GetOrCreateList(SerializedProperty listProp, string header)
    {
        string key = listProp.propertyPath;
        if (_listCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var list = new ReorderableList(listProp.serializedObject, listProp, true, true, true, true);
        list.drawHeaderCallback = rect => EditorGUI.LabelField(rect, header);
        list.drawElementCallback = (rect, index, active, focused) =>
        {
            var element = listProp.GetArrayElementAtIndex(index);
            string name = element.FindPropertyRelative(nameof(JsonFieldDefinition.fieldName)).stringValue;
            var typeProp = element.FindPropertyRelative(nameof(JsonFieldDefinition.fieldType));
            string typeName = ((JsonFieldType)typeProp.enumValueIndex).ToString();
            EditorGUI.LabelField(rect, $"{name} ({typeName})");
        };
        list.elementHeight = EditorGUIUtility.singleLineHeight + 4f;
        list.onSelectCallback = l => _selectionByPath[key] = l.index;
        list.onAddCallback = l =>
        {
            int newIndex = listProp.arraySize;
            listProp.InsertArrayElementAtIndex(newIndex);
            InitializeFieldDefaults(listProp.GetArrayElementAtIndex(newIndex));
            _selectionByPath[key] = newIndex;
        };
        list.onRemoveCallback = l =>
        {
            int idx = l.index;
            listProp.DeleteArrayElementAtIndex(idx);
            _selectionByPath[key] = Mathf.Clamp(idx - 1, 0, listProp.arraySize - 1);
        };
        list.onReorderCallback = l =>
        {
            _selectionByPath[key] = Mathf.Clamp(GetSelectionIndex(key), 0, l.count - 1);
        };

        _listCache[key] = list;
        return list;
    }

    private int GetSelectionIndex(string key)
    {
        return _selectionByPath.TryGetValue(key, out var idx) ? idx : -1;
    }

    private bool DrawSelectedFieldDetails(SerializedProperty listProp, int depth)
    {
        bool changed = false;
        if (listProp == null || listProp.arraySize == 0)
        {
            return changed;
        }

        int sel = Mathf.Clamp(GetSelectionIndex(listProp.propertyPath), 0, listProp.arraySize - 1);
        _selectionByPath[listProp.propertyPath] = sel;
        var element = listProp.GetArrayElementAtIndex(sel);

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField(depth == 0 ? "Field Details" : "Child Field Details", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(element.FindPropertyRelative(nameof(JsonFieldDefinition.fieldName)), new GUIContent("Field Name"));

            var fieldTypeProp = element.FindPropertyRelative(nameof(JsonFieldDefinition.fieldType));
            EditorGUILayout.PropertyField(fieldTypeProp, new GUIContent("Type"));

            bool isArray = (JsonFieldType)fieldTypeProp.enumValueIndex == JsonFieldType.Array;
            bool isObject = (JsonFieldType)fieldTypeProp.enumValueIndex == JsonFieldType.Object;
            bool isNumeric = !isArray && !isObject &&
                             ((JsonFieldType)fieldTypeProp.enumValueIndex == JsonFieldType.Number ||
                              (JsonFieldType)fieldTypeProp.enumValueIndex == JsonFieldType.Integer);

            if (isArray)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    EditorGUILayout.PropertyField(
                        element.FindPropertyRelative(nameof(JsonFieldDefinition.arrayElementType)),
                        new GUIContent("Element Type")
                    );
                }
            }

            DrawEnumList(element.FindPropertyRelative(nameof(JsonFieldDefinition.enumOptions)));

            if (isNumeric)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    EditorGUILayout.PropertyField(element.FindPropertyRelative(nameof(JsonFieldDefinition.minValue)), new GUIContent("Min"));
                    EditorGUILayout.PropertyField(element.FindPropertyRelative(nameof(JsonFieldDefinition.maxValue)), new GUIContent("Max"));
                }
            }

            bool needsChildren = isObject ||
                                 (isArray && (JsonArrayElementType)element.FindPropertyRelative(nameof(JsonFieldDefinition.arrayElementType)).enumValueIndex == JsonArrayElementType.Object);
            if (needsChildren)
            {
                var childrenProp = element.FindPropertyRelative(nameof(JsonFieldDefinition.children));
                if (depth >= MaxNestingDepth)
                {
                    EditorGUILayout.HelpBox($"Reached max nesting depth ({MaxNestingDepth}). Further child fields are not serialized.", MessageType.Warning);
                    changed |= ClampDepth(childrenProp, depth + 1);
                }
                else
                {
                    var childList = GetOrCreateList(childrenProp, "Child Fields");
                    childList.DoLayoutList();
                    changed |= DrawSelectedFieldDetails(childrenProp, depth + 1);
                }
            }
        }

        return changed;
    }

    private bool ClampDepth(SerializedProperty listProp, int depth)
    {
        bool changed = false;
        if (listProp == null)
        {
            return changed;
        }

        if (depth > MaxNestingDepth)
        {
            while (listProp.arraySize > 0)
            {
                listProp.DeleteArrayElementAtIndex(listProp.arraySize - 1);
                changed = true;
            }
            return changed;
        }

        for (int i = 0; i < listProp.arraySize; i++)
        {
            var element = listProp.GetArrayElementAtIndex(i);
            var children = element.FindPropertyRelative(nameof(JsonFieldDefinition.children));
            changed |= ClampDepth(children, depth + 1);
        }

        return changed;
    }

    private sealed class LlmFormatPreviewWindow : EditorWindow
    {
        private string _content;
        private Vector2 _scroll;

        public static void Show(string content)
        {
            var window = CreateInstance<LlmFormatPreviewWindow>();
            window._content = content ?? string.Empty;
            window.titleContent = new GUIContent("LLM Format");
            window.minSize = new Vector2(360f, 240f);
            window.ShowUtility();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Generated Format (read-only)", EditorStyles.boldLabel);
            var style = new GUIStyle(EditorStyles.textArea) { wordWrap = true };
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.SelectableLabel(_content, style, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }
    }
}
