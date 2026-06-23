using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using DoodleDiplomacy.Data;
using DoodleDiplomacy.Localization;
using UnityEditor;
using UnityEngine;

namespace DoodleDiplomacy.Editor
{
    public sealed class LocalizationWorkbenchWindow : EditorWindow
    {
        private const string MenuPath = "Window/Doodle Diplomacy/Localization Workbench";
        private const string LocalizationSettingsPath = "Assets/Resources/Localization/GameLocalizationSettings.asset";
        private const string SearchControlName = "LocalizationWorkbenchSearch";
        private const float EntryListWidth = 470f;
        private const float ListRowMinHeight = 86f;
        private static readonly Regex DirectL10nCallRegex = new(
            "L10n\\.T\\s*\\(\\s*\"(?<key>(?:\\\\.|[^\"\\\\])*)\"\\s*,\\s*\"(?<fallback>(?:\\\\.|[^\"\\\\])*)\"",
            RegexOptions.Compiled | RegexOptions.Singleline);

        private readonly List<LocalizationRow> _rows = new();
        private readonly Dictionary<string, List<LocalizationUsage>> _dialogueUsageByKey = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<CodeUsage>> _codeUsageByKey = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _tableKeys = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _keyCounts = new(StringComparer.OrdinalIgnoreCase);

        private GameLocalizationSettings _settings;
        private LocalizedStringTable _stringTable;
        private SerializedObject _tableObject;
        private SerializedProperty _entriesProperty;

        private string _search = string.Empty;
        private EntryFilter _filter = EntryFilter.All;
        private string _selectedKey = string.Empty;
        private int _selectedEntryIndex = -1;
        private string _createKey = string.Empty;
        private string _createSourceText = string.Empty;
        private Vector2 _listScroll;
        private Vector2 _detailScroll;
        private Vector2 _usageScroll;

        [MenuItem(MenuPath)]
        public static void Open()
        {
            var window = GetWindow<LocalizationWorkbenchWindow>();
            window.titleContent = new GUIContent("Localization Workbench");
            window.minSize = new Vector2(920f, 560f);
            window.Show();
        }

        private void OnEnable()
        {
            Reload();
        }

        private void OnFocus()
        {
            if (_stringTable == null)
            {
                Reload();
            }
        }

        private void OnGUI()
        {
            DrawToolbar();

            if (!HasValidTable())
            {
                EditorGUILayout.HelpBox(
                    $"Could not load localization settings or string table from {LocalizationSettingsPath}.",
                    MessageType.Warning);
                return;
            }

            _tableObject.Update();

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawEntryList(GUILayout.Width(EntryListWidth));
                DrawDetailPanel();
            }

            _tableObject.ApplyModifiedProperties();
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.ObjectField(_settings, typeof(GameLocalizationSettings), false, GUILayout.Width(240f));
                    EditorGUILayout.ObjectField(_stringTable, typeof(LocalizedStringTable), false, GUILayout.Width(240f));
                }

                GUILayout.FlexibleSpace();

                if (GUILayout.Button("Dialogue Timeline", EditorStyles.toolbarButton, GUILayout.Width(116f)))
                {
                    DialogueTimelineWindow.Open();
                }

                using (new EditorGUI.DisabledScope(_stringTable == null))
                {
                    if (GUILayout.Button("Ping Table", EditorStyles.toolbarButton, GUILayout.Width(78f)))
                    {
                        EditorGUIUtility.PingObject(_stringTable);
                    }

                    if (GUILayout.Button("Save", EditorStyles.toolbarButton, GUILayout.Width(54f)))
                    {
                        SaveTable();
                    }
                }

                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(70f)))
                {
                    Reload();
                }
            }
        }

        private void DrawEntryList(params GUILayoutOption[] options)
        {
            using (new EditorGUILayout.VerticalScope(options))
            {
                EditorGUILayout.LabelField("String Table", EditorStyles.boldLabel);

                GUI.SetNextControlName(SearchControlName);
                string nextSearch = EditorGUILayout.TextField(_search, EditorStyles.toolbarSearchField);
                if (!string.Equals(nextSearch, _search, StringComparison.Ordinal))
                {
                    _search = nextSearch ?? string.Empty;
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    _filter = (EntryFilter)EditorGUILayout.EnumPopup(_filter);
                    if (GUILayout.Button("Clear", GUILayout.Width(54f)))
                    {
                        _search = string.Empty;
                        GUI.FocusControl(SearchControlName);
                    }
                }

                EditorGUILayout.LabelField(GetTableSummary(), EditorStyles.miniLabel);
                EditorGUILayout.Space(4f);

                _listScroll = EditorGUILayout.BeginScrollView(_listScroll);
                int shown = 0;
                foreach (LocalizationRow row in _rows)
                {
                    if (!MatchesFilter(row) || !MatchesSearch(row))
                    {
                        continue;
                    }

                    DrawEntryRow(row);
                    shown++;
                }

                if (shown == 0)
                {
                    EditorGUILayout.HelpBox("No localization entries match the current search/filter.", MessageType.Info);
                }

                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawEntryRow(LocalizationRow row)
        {
            bool selected = IsSelected(row);
            GUIStyle boxStyle = selected ? EditorStyles.helpBox : GUI.skin.box;

            using (new EditorGUILayout.VerticalScope(boxStyle, GUILayout.MinHeight(ListRowMinHeight)))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(row.Key, selected ? EditorStyles.boldLabel : EditorStyles.label))
                    {
                        SelectRow(row);
                    }

                    GUILayout.FlexibleSpace();
                    GUILayout.Label(row.Category, EditorStyles.miniLabel, GUILayout.Width(128f));
                }

                string status = row.GetStatusLabel();
                if (!string.IsNullOrWhiteSpace(status))
                {
                    EditorGUILayout.LabelField(status, EditorStyles.miniBoldLabel);
                }

                DrawPreviewLabel("Source", row.SourceText);
                DrawPreviewLabel(row.TargetLocaleLabel, row.TargetText);

                string usage = row.GetUsagePreview();
                if (!string.IsNullOrWhiteSpace(usage))
                {
                    EditorGUILayout.LabelField(usage, EditorStyles.wordWrappedMiniLabel);
                }
            }
        }

        private static void DrawPreviewLabel(string label, string value)
        {
            string preview = MakeSingleLinePreview(value);
            EditorGUILayout.LabelField($"{label}: {preview}", EditorStyles.wordWrappedMiniLabel);
        }

        private void DrawDetailPanel()
        {
            using (new EditorGUILayout.VerticalScope())
            {
                LocalizationRow selected = FindSelectedRow();
                _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll);
                DrawCreateEntryPanel(selected);
                EditorGUILayout.Space(8f);

                if (selected == null)
                {
                    EditorGUILayout.HelpBox("Select an entry to edit source text, target text, and usage context.", MessageType.Info);
                    EditorGUILayout.EndScrollView();
                    return;
                }

                if (selected.IsMissingEntry)
                {
                    DrawMissingEntryDetail(selected);
                }
                else
                {
                    DrawExistingEntryDetail(selected);
                }

                EditorGUILayout.Space(8f);
                DrawUsagePanel(selected);
                EditorGUILayout.Space(8f);
                DrawIssuePanel(selected);
                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawCreateEntryPanel(LocalizationRow selected)
        {
            EditorGUILayout.LabelField("Create Entry", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                _createKey = EditorGUILayout.TextField("Key", _createKey);
                using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_createKey) || _tableKeys.Contains(_createKey.Trim())))
                {
                    if (GUILayout.Button("Add", GUILayout.Width(70f)))
                    {
                        CreateEntry(_createKey, _createSourceText);
                    }
                }
            }

            _createSourceText = EditorGUILayout.TextArea(_createSourceText, GUILayout.MinHeight(42f));

            if (selected != null && selected.IsMissingEntry)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Use Selected Missing Key", GUILayout.Width(180f)))
                    {
                        _createKey = selected.Key;
                        _createSourceText = selected.SourceText;
                    }
                }
            }
        }

        private void DrawMissingEntryDetail(LocalizationRow row)
        {
            EditorGUILayout.LabelField("Missing Table Entry", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("This key is referenced by dialogue or code, but it does not exist in the string table.", MessageType.Warning);
            EditorGUILayout.TextField("Key", row.Key);
            string suggestedSource = EditorGUILayout.TextArea(row.SourceText, GUILayout.MinHeight(88f));

            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(row.Key)))
            {
                if (GUILayout.Button("Create Entry From This Reference"))
                {
                    CreateEntry(row.Key, suggestedSource);
                }
            }
        }

        private void DrawExistingEntryDetail(LocalizationRow row)
        {
            if (!TryGetEntryProperty(row, out SerializedProperty entry))
            {
                EditorGUILayout.HelpBox("The selected entry no longer exists. Refresh the workbench.", MessageType.Warning);
                return;
            }

            SerializedProperty key = entry.FindPropertyRelative("key");
            SerializedProperty sourceText = entry.FindPropertyRelative("sourceText");
            SerializedProperty translations = entry.FindPropertyRelative("translations");

            EditorGUILayout.LabelField("Entry", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.TextField("Key", key.stringValue);
                if (GUILayout.Button("Copy", GUILayout.Width(56f)))
                {
                    EditorGUIUtility.systemCopyBuffer = key.stringValue;
                }
            }

            EditorGUILayout.LabelField("Source Text", EditorStyles.miniBoldLabel);
            EditorGUI.BeginChangeCheck();
            string nextSource = EditorGUILayout.TextArea(sourceText.stringValue, GUILayout.MinHeight(100f));
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_stringTable, "Edit source localization text");
                sourceText.stringValue = nextSource;
                ApplyTableChanges();
            }

            EditorGUILayout.Space(8f);
            DrawTargetTranslationEditor(translations, sourceText.stringValue);
        }

        private void DrawTargetTranslationEditor(SerializedProperty translations, string sourceText)
        {
            string sourceLocale = GetSourceLocale();
            string targetLocale = GetTargetLocale();
            if (GameLocalizationSettings.LocaleEquals(sourceLocale, targetLocale))
            {
                EditorGUILayout.HelpBox("Target locale matches source locale. The game will read Source Text.", MessageType.None);
                return;
            }

            SerializedProperty translation = FindTranslation(translations, targetLocale);
            EditorGUILayout.LabelField($"Target Text ({targetLocale})", EditorStyles.miniBoldLabel);

            if (translation == null)
            {
                EditorGUILayout.HelpBox($"No {targetLocale} translation row exists for this key.", MessageType.Warning);
                if (GUILayout.Button($"Add {targetLocale} Translation"))
                {
                    Undo.RecordObject(_stringTable, "Add localization translation");
                    CreateTranslation(translations, targetLocale, sourceText);
                    ApplyTableChanges(rebuild: true);
                }

                return;
            }

            SerializedProperty translatedText = translation.FindPropertyRelative("text");
            EditorGUI.BeginChangeCheck();
            string nextTranslation = EditorGUILayout.TextArea(translatedText.stringValue, GUILayout.MinHeight(100f));
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_stringTable, "Edit target localization text");
                translatedText.stringValue = nextTranslation;
                ApplyTableChanges();
            }
        }

        private void DrawUsagePanel(LocalizationRow row)
        {
            EditorGUILayout.LabelField("Usage", EditorStyles.boldLabel);
            _usageScroll = EditorGUILayout.BeginScrollView(_usageScroll, GUILayout.MinHeight(120f), GUILayout.MaxHeight(230f));

            bool drewUsage = false;
            if (_dialogueUsageByKey.TryGetValue(row.Key, out List<LocalizationUsage> dialogueUsages))
            {
                foreach (LocalizationUsage usage in dialogueUsages)
                {
                    DrawDialogueUsage(usage);
                    drewUsage = true;
                }
            }

            if (_codeUsageByKey.TryGetValue(row.Key, out List<CodeUsage> codeUsages))
            {
                foreach (CodeUsage usage in codeUsages)
                {
                    DrawCodeUsage(usage);
                    drewUsage = true;
                }
            }

            if (!drewUsage)
            {
                EditorGUILayout.HelpBox("No direct dialogue or L10n.T string-literal usage was found.", MessageType.None);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawDialogueUsage(LocalizationUsage usage)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField($"{usage.Role} - {usage.SequenceLabel} / line {usage.LineNumber}", EditorStyles.miniBoldLabel);
                EditorGUILayout.LabelField($"Speaker: {usage.Speaker}", EditorStyles.miniLabel);
                if (!string.IsNullOrWhiteSpace(usage.ContextNote))
                {
                    EditorGUILayout.LabelField($"Context: {MakeSingleLinePreview(usage.ContextNote)}", EditorStyles.wordWrappedMiniLabel);
                }

                EditorGUILayout.LabelField(MakeSingleLinePreview(usage.FallbackText), EditorStyles.wordWrappedMiniLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Ping Sequence", GUILayout.Width(110f)))
                    {
                        EditorGUIUtility.PingObject(usage.Sequence);
                    }

                    if (GUILayout.Button("Open Timeline", GUILayout.Width(110f)))
                    {
                        Selection.activeObject = usage.Sequence;
                        DialogueTimelineWindow.Open();
                    }

                    GUILayout.Label(usage.AssetPath, EditorStyles.miniLabel);
                }
            }
        }

        private static void DrawCodeUsage(CodeUsage usage)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField($"Code - {usage.AssetPath}:{usage.LineNumber}", EditorStyles.miniBoldLabel);
                EditorGUILayout.LabelField($"Fallback: {MakeSingleLinePreview(usage.FallbackText)}", EditorStyles.wordWrappedMiniLabel);
                if (GUILayout.Button("Open Script", GUILayout.Width(96f)))
                {
                    UnityEngine.Object script = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(usage.AssetPath);
                    if (script != null)
                    {
                        AssetDatabase.OpenAsset(script, usage.LineNumber);
                    }
                }
            }
        }

        private void DrawIssuePanel(LocalizationRow row)
        {
            EditorGUILayout.LabelField("Checks", EditorStyles.boldLabel);
            List<string> issues = row.BuildIssueList();
            if (issues.Count == 0)
            {
                EditorGUILayout.HelpBox("No obvious issues for this entry.", MessageType.None);
                return;
            }

            foreach (string issue in issues)
            {
                EditorGUILayout.HelpBox(issue, MessageType.Warning);
            }
        }

        private void Reload()
        {
            LoadLocalizationAssets();
            RebuildIndex();
            Repaint();
        }

        private void LoadLocalizationAssets()
        {
            _settings = AssetDatabase.LoadAssetAtPath<GameLocalizationSettings>(LocalizationSettingsPath);
            _stringTable = null;
            _tableObject = null;
            _entriesProperty = null;

            if (_settings == null)
            {
                return;
            }

            var settingsObject = new SerializedObject(_settings);
            SerializedProperty tableProperty = settingsObject.FindProperty("stringTable");
            _stringTable = tableProperty?.objectReferenceValue as LocalizedStringTable;
            if (_stringTable == null)
            {
                return;
            }

            _tableObject = new SerializedObject(_stringTable);
            _entriesProperty = _tableObject.FindProperty("entries");
        }

        private void RebuildIndex()
        {
            _rows.Clear();
            _dialogueUsageByKey.Clear();
            _codeUsageByKey.Clear();
            _tableKeys.Clear();
            _keyCounts.Clear();

            BuildDialogueUsageIndex();
            BuildCodeUsageIndex();
            BuildRowsFromStringTable();
            AddRowsForMissingReferencedKeys();
            _rows.Sort(CompareRows);

            if (FindSelectedRow() == null && _rows.Count > 0)
            {
                SelectRow(_rows[0]);
            }
        }

        private void BuildRowsFromStringTable()
        {
            if (!HasValidTable())
            {
                return;
            }

            _tableObject.Update();
            string sourceLocale = GetSourceLocale();
            string targetLocale = GetTargetLocale();

            for (int i = 0; i < _entriesProperty.arraySize; i++)
            {
                SerializedProperty entry = _entriesProperty.GetArrayElementAtIndex(i);
                string key = entry.FindPropertyRelative("key").stringValue?.Trim() ?? string.Empty;
                string sourceText = entry.FindPropertyRelative("sourceText").stringValue ?? string.Empty;
                SerializedProperty translations = entry.FindPropertyRelative("translations");
                SerializedProperty targetTranslation = FindTranslation(translations, targetLocale);
                string targetText = targetTranslation?.FindPropertyRelative("text").stringValue ?? string.Empty;
                bool targetMissing = !GameLocalizationSettings.LocaleEquals(sourceLocale, targetLocale) &&
                                     string.IsNullOrWhiteSpace(targetText);

                if (!string.IsNullOrWhiteSpace(key))
                {
                    _tableKeys.Add(key);
                    _keyCounts.TryGetValue(key, out int count);
                    _keyCounts[key] = count + 1;
                }

                _rows.Add(new LocalizationRow(
                    key,
                    i,
                    sourceText,
                    targetText,
                    targetLocale,
                    targetMissing,
                    isMissingEntry: false,
                    hasDialogueUsage: _dialogueUsageByKey.ContainsKey(key),
                    hasCodeUsage: _codeUsageByKey.ContainsKey(key),
                    duplicateKey: false,
                    category: CategorizeKey(key),
                    usagePreview: BuildUsagePreview(key),
                    usageSearchText: BuildUsageSearchText(key),
                    terminalWarning: BuildTerminalWarning(key, sourceText, targetText)));
            }

            for (int i = 0; i < _rows.Count; i++)
            {
                LocalizationRow row = _rows[i];
                _rows[i] = row.WithDuplicateKey(!string.IsNullOrWhiteSpace(row.Key) &&
                                                _keyCounts.TryGetValue(row.Key, out int count) &&
                                                count > 1);
            }
        }

        private void AddRowsForMissingReferencedKeys()
        {
            foreach (string key in EnumerateReferencedKeys())
            {
                if (string.IsNullOrWhiteSpace(key) || _tableKeys.Contains(key))
                {
                    continue;
                }

                string source = GetSuggestedSourceForMissingKey(key);
                _rows.Add(new LocalizationRow(
                    key,
                    -1,
                    source,
                    string.Empty,
                    GetTargetLocale(),
                    targetMissing: true,
                    isMissingEntry: true,
                    hasDialogueUsage: _dialogueUsageByKey.ContainsKey(key),
                    hasCodeUsage: _codeUsageByKey.ContainsKey(key),
                    duplicateKey: false,
                    category: CategorizeKey(key),
                    usagePreview: BuildUsagePreview(key),
                    usageSearchText: BuildUsageSearchText(key),
                    terminalWarning: BuildTerminalWarning(key, source, string.Empty)));
            }
        }

        private IEnumerable<string> EnumerateReferencedKeys()
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string key in _dialogueUsageByKey.Keys)
            {
                keys.Add(key);
            }

            foreach (string key in _codeUsageByKey.Keys)
            {
                keys.Add(key);
            }

            return keys;
        }

        private void BuildDialogueUsageIndex()
        {
            string[] guids = AssetDatabase.FindAssets("t:DialogueSequence");
            foreach (string guid in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                DialogueSequence sequence = AssetDatabase.LoadAssetAtPath<DialogueSequence>(assetPath);
                if (sequence == null || sequence.lines == null)
                {
                    continue;
                }

                for (int i = 0; i < sequence.lines.Count; i++)
                {
                    DialogueLineData line = sequence.lines[i];
                    if (line == null)
                    {
                        continue;
                    }

                    AddDialogueUsage(line.localizationKey, new LocalizationUsage(
                        sequence,
                        assetPath,
                        i + 1,
                        "Line Text",
                        sequence.sequenceID,
                        sequence.contextNote,
                        line.characterID,
                        line.text));

                    AddDialogueUsage(line.speakerLocalizationKey, new LocalizationUsage(
                        sequence,
                        assetPath,
                        i + 1,
                        "Speaker",
                        sequence.sequenceID,
                        sequence.contextNote,
                        line.characterID,
                        line.characterID));
                }
            }
        }

        private void BuildCodeUsageIndex()
        {
            string dataPath = Application.dataPath;
            if (string.IsNullOrWhiteSpace(dataPath) || !Directory.Exists(dataPath))
            {
                return;
            }

            foreach (string filePath in Directory.GetFiles(dataPath, "*.cs", SearchOption.AllDirectories))
            {
                string normalizedPath = filePath.Replace('\\', '/');
                if (normalizedPath.IndexOf("/TextMesh Pro/Examples & Extras/", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    continue;
                }

                string text;
                try
                {
                    text = File.ReadAllText(filePath);
                }
                catch (IOException)
                {
                    continue;
                }

                foreach (Match match in DirectL10nCallRegex.Matches(text))
                {
                    string key = UnescapeStringLiteral(match.Groups["key"].Value);
                    if (string.IsNullOrWhiteSpace(key))
                    {
                        continue;
                    }

                    AddCodeUsage(key, new CodeUsage(
                        ToAssetPath(filePath),
                        GetLineNumber(text, match.Index),
                        UnescapeStringLiteral(match.Groups["fallback"].Value)));
                }
            }
        }

        private void AddDialogueUsage(string key, LocalizationUsage usage)
        {
            key = key?.Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            if (!_dialogueUsageByKey.TryGetValue(key, out List<LocalizationUsage> usages))
            {
                usages = new List<LocalizationUsage>();
                _dialogueUsageByKey[key] = usages;
            }

            usages.Add(usage);
        }

        private void AddCodeUsage(string key, CodeUsage usage)
        {
            key = key?.Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            if (!_codeUsageByKey.TryGetValue(key, out List<CodeUsage> usages))
            {
                usages = new List<CodeUsage>();
                _codeUsageByKey[key] = usages;
            }

            usages.Add(usage);
        }

        private void CreateEntry(string key, string sourceText)
        {
            if (!HasValidTable() || string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            string trimmedKey = key.Trim();
            if (_tableKeys.Contains(trimmedKey))
            {
                SelectRow(new LocalizationRow(trimmedKey, -1, string.Empty, string.Empty, GetTargetLocale(), false, false, false, false, false, CategorizeKey(trimmedKey), string.Empty, string.Empty, string.Empty));
                return;
            }

            _tableObject.Update();
            Undo.RecordObject(_stringTable, "Create localization entry");
            int index = _entriesProperty.arraySize;
            _entriesProperty.InsertArrayElementAtIndex(index);
            SerializedProperty entry = _entriesProperty.GetArrayElementAtIndex(index);
            entry.FindPropertyRelative("key").stringValue = trimmedKey;
            entry.FindPropertyRelative("sourceText").stringValue = sourceText ?? string.Empty;
            entry.FindPropertyRelative("translations").arraySize = 0;
            ApplyTableChanges(rebuild: true);

            _createKey = string.Empty;
            _createSourceText = string.Empty;
            _selectedKey = trimmedKey;
            _selectedEntryIndex = index;
            RebuildIndex();
        }

        private void ApplyTableChanges(bool rebuild = false)
        {
            _tableObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(_stringTable);
            if (rebuild)
            {
                RebuildIndex();
            }
        }

        private void SaveTable()
        {
            if (_tableObject != null)
            {
                _tableObject.ApplyModifiedProperties();
            }

            if (_stringTable != null)
            {
                EditorUtility.SetDirty(_stringTable);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Reload();
        }

        private bool HasValidTable()
        {
            return _settings != null && _stringTable != null && _tableObject != null && _entriesProperty != null;
        }

        private bool TryGetEntryProperty(LocalizationRow row, out SerializedProperty entry)
        {
            entry = null;
            if (!HasValidTable() || row.EntryIndex < 0 || row.EntryIndex >= _entriesProperty.arraySize)
            {
                return false;
            }

            entry = _entriesProperty.GetArrayElementAtIndex(row.EntryIndex);
            string key = entry.FindPropertyRelative("key").stringValue;
            return string.Equals(key, row.Key, StringComparison.OrdinalIgnoreCase);
        }

        private void SelectRow(LocalizationRow row)
        {
            _selectedKey = row.Key;
            _selectedEntryIndex = row.EntryIndex;
            _detailScroll = Vector2.zero;
        }

        private bool IsSelected(LocalizationRow row)
        {
            return string.Equals(row.Key, _selectedKey, StringComparison.OrdinalIgnoreCase) &&
                   row.EntryIndex == _selectedEntryIndex;
        }

        private LocalizationRow FindSelectedRow()
        {
            foreach (LocalizationRow row in _rows)
            {
                if (IsSelected(row))
                {
                    return row;
                }
            }

            foreach (LocalizationRow row in _rows)
            {
                if (string.Equals(row.Key, _selectedKey, StringComparison.OrdinalIgnoreCase))
                {
                    return row;
                }
            }

            return null;
        }

        private bool MatchesFilter(LocalizationRow row)
        {
            return _filter switch
            {
                EntryFilter.Dialogue => row.HasDialogueUsage,
                EntryFilter.Code => row.HasCodeUsage,
                EntryFilter.Terminal => row.Key.StartsWith("first_contact.terminal.", StringComparison.OrdinalIgnoreCase),
                EntryFilter.MissingTarget => row.TargetMissing,
                EntryFilter.MissingEntry => row.IsMissingEntry,
                EntryFilter.Unused => !row.HasDialogueUsage && !row.HasCodeUsage && !row.IsMissingEntry,
                EntryFilter.Duplicates => row.DuplicateKey,
                EntryFilter.TerminalWarnings => !string.IsNullOrWhiteSpace(row.TerminalWarning),
                _ => true,
            };
        }

        private bool MatchesSearch(LocalizationRow row)
        {
            if (string.IsNullOrWhiteSpace(_search))
            {
                return true;
            }

            string haystack = row.BuildSearchText();
            string[] terms = _search.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < terms.Length; i++)
            {
                if (haystack.IndexOf(terms[i], StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return false;
                }
            }

            return true;
        }

        private string GetTableSummary()
        {
            int missingTargets = 0;
            int missingEntries = 0;
            int duplicateRows = 0;
            int terminalWarnings = 0;
            for (int i = 0; i < _rows.Count; i++)
            {
                LocalizationRow row = _rows[i];
                if (row.TargetMissing)
                {
                    missingTargets++;
                }

                if (row.IsMissingEntry)
                {
                    missingEntries++;
                }

                if (row.DuplicateKey)
                {
                    duplicateRows++;
                }

                if (!string.IsNullOrWhiteSpace(row.TerminalWarning))
                {
                    terminalWarnings++;
                }
            }

            return $"{_rows.Count} rows | {missingTargets} missing target | {missingEntries} missing entries | {duplicateRows} duplicate rows | {terminalWarnings} terminal warnings";
        }

        private string GetSuggestedSourceForMissingKey(string key)
        {
            if (_dialogueUsageByKey.TryGetValue(key, out List<LocalizationUsage> dialogueUsages) && dialogueUsages.Count > 0)
            {
                return dialogueUsages[0].FallbackText;
            }

            if (_codeUsageByKey.TryGetValue(key, out List<CodeUsage> codeUsages) && codeUsages.Count > 0)
            {
                return codeUsages[0].FallbackText;
            }

            return string.Empty;
        }

        private string BuildUsagePreview(string key)
        {
            if (_dialogueUsageByKey.TryGetValue(key, out List<LocalizationUsage> dialogueUsages) && dialogueUsages.Count > 0)
            {
                LocalizationUsage usage = dialogueUsages[0];
                return $"{usage.Role}: {usage.SequenceLabel} line {usage.LineNumber}";
            }

            if (_codeUsageByKey.TryGetValue(key, out List<CodeUsage> codeUsages) && codeUsages.Count > 0)
            {
                CodeUsage usage = codeUsages[0];
                return $"Code: {usage.AssetPath}:{usage.LineNumber}";
            }

            return string.Empty;
        }

        private string BuildUsageSearchText(string key)
        {
            var parts = new List<string>();
            if (_dialogueUsageByKey.TryGetValue(key, out List<LocalizationUsage> dialogueUsages))
            {
                foreach (LocalizationUsage usage in dialogueUsages)
                {
                    parts.Add(usage.Role);
                    parts.Add(usage.SequenceLabel);
                    parts.Add(usage.ContextNote);
                    parts.Add(usage.Speaker);
                    parts.Add(usage.FallbackText);
                    parts.Add(usage.AssetPath);
                }
            }

            if (_codeUsageByKey.TryGetValue(key, out List<CodeUsage> codeUsages))
            {
                foreach (CodeUsage usage in codeUsages)
                {
                    parts.Add(usage.AssetPath);
                    parts.Add(usage.FallbackText);
                }
            }

            return string.Join("\n", parts);
        }

        private string GetSourceLocale()
        {
            return _settings != null ? _settings.SourceLocale : "en-US";
        }

        private string GetTargetLocale()
        {
            return _settings != null ? _settings.TargetLocale : "en-US";
        }

        private static SerializedProperty FindTranslation(SerializedProperty translations, string locale)
        {
            if (translations == null || string.IsNullOrWhiteSpace(locale))
            {
                return null;
            }

            for (int i = 0; i < translations.arraySize; i++)
            {
                SerializedProperty translation = translations.GetArrayElementAtIndex(i);
                SerializedProperty localeProperty = translation.FindPropertyRelative("locale");
                if (GameLocalizationSettings.LocaleEquals(localeProperty.stringValue, locale))
                {
                    return translation;
                }
            }

            return null;
        }

        private static void CreateTranslation(SerializedProperty translations, string locale, string text)
        {
            int index = translations.arraySize;
            translations.InsertArrayElementAtIndex(index);
            SerializedProperty translation = translations.GetArrayElementAtIndex(index);
            translation.FindPropertyRelative("locale").stringValue = locale;
            translation.FindPropertyRelative("text").stringValue = text ?? string.Empty;
        }

        private static int CompareRows(LocalizationRow a, LocalizationRow b)
        {
            int missingCompare = b.IsMissingEntry.CompareTo(a.IsMissingEntry);
            if (missingCompare != 0)
            {
                return missingCompare;
            }

            int categoryCompare = string.Compare(a.Category, b.Category, StringComparison.OrdinalIgnoreCase);
            if (categoryCompare != 0)
            {
                return categoryCompare;
            }

            return string.Compare(a.Key, b.Key, StringComparison.OrdinalIgnoreCase);
        }

        private static string CategorizeKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return "Invalid";
            }

            if (key.StartsWith("first_contact.terminal.", StringComparison.OrdinalIgnoreCase))
            {
                return "Terminal";
            }

            if (key.StartsWith("dialogue.", StringComparison.OrdinalIgnoreCase) ||
                key.StartsWith("day1.", StringComparison.OrdinalIgnoreCase))
            {
                return "Dialogue";
            }

            if (key.StartsWith("speaker.", StringComparison.OrdinalIgnoreCase))
            {
                return "Speaker";
            }

            if (key.StartsWith("ui.", StringComparison.OrdinalIgnoreCase))
            {
                return "UI";
            }

            if (key.StartsWith("label.", StringComparison.OrdinalIgnoreCase))
            {
                return "Label";
            }

            if (key.StartsWith("alien.reaction.", StringComparison.OrdinalIgnoreCase))
            {
                return "Alien Reaction";
            }

            return "Other";
        }

        private static string BuildTerminalWarning(string key, string sourceText, string targetText)
        {
            if (string.IsNullOrWhiteSpace(key) ||
                !key.StartsWith("first_contact.terminal.", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            string combined = $"{sourceText}\n{targetText}";
            if (combined.IndexOf("SELECT-ONE", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "First Contact terminal text should not expose SELECT-ONE.";
            }

            if (Regex.IsMatch(combined, "\\bTOKEN\\b", RegexOptions.IgnoreCase))
            {
                return "First Contact terminal text should use MEANING instead of TOKEN.";
            }

            if (combined.IndexOf("\uD0D0\uCE68", StringComparison.Ordinal) >= 0)
            {
                return "Korean First Contact terminal text should use the player term for PROBE, not the technical term.";
            }

            return string.Empty;
        }

        private static string MakeSingleLinePreview(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return "(empty)";
            }

            string singleLine = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
            return singleLine.Length <= 120 ? singleLine : singleLine.Substring(0, 117) + "...";
        }

        private static string UnescapeStringLiteral(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            try
            {
                return Regex.Unescape(value);
            }
            catch (ArgumentException)
            {
                return value;
            }
        }

        private static int GetLineNumber(string text, int index)
        {
            int line = 1;
            int max = Mathf.Clamp(index, 0, text.Length);
            for (int i = 0; i < max; i++)
            {
                if (text[i] == '\n')
                {
                    line++;
                }
            }

            return line;
        }

        private static string ToAssetPath(string fullPath)
        {
            string projectDataPath = Application.dataPath.Replace('\\', '/');
            string normalized = fullPath.Replace('\\', '/');
            if (normalized.StartsWith(projectDataPath, StringComparison.OrdinalIgnoreCase))
            {
                return "Assets" + normalized.Substring(projectDataPath.Length);
            }

            return normalized;
        }

        private enum EntryFilter
        {
            All,
            Dialogue,
            Code,
            Terminal,
            MissingTarget,
            MissingEntry,
            Unused,
            Duplicates,
            TerminalWarnings
        }

        private sealed class LocalizationRow
        {
            public LocalizationRow(
                string key,
                int entryIndex,
                string sourceText,
                string targetText,
                string targetLocale,
                bool targetMissing,
                bool isMissingEntry,
                bool hasDialogueUsage,
                bool hasCodeUsage,
                bool duplicateKey,
                string category,
                string usagePreview,
                string usageSearchText,
                string terminalWarning)
            {
                Key = key ?? string.Empty;
                EntryIndex = entryIndex;
                SourceText = sourceText ?? string.Empty;
                TargetText = targetText ?? string.Empty;
                TargetLocaleLabel = string.IsNullOrWhiteSpace(targetLocale) ? "Target" : targetLocale;
                TargetMissing = targetMissing;
                IsMissingEntry = isMissingEntry;
                HasDialogueUsage = hasDialogueUsage;
                HasCodeUsage = hasCodeUsage;
                DuplicateKey = duplicateKey;
                Category = category ?? "Other";
                UsagePreview = usagePreview ?? string.Empty;
                UsageSearchText = usageSearchText ?? string.Empty;
                TerminalWarning = terminalWarning ?? string.Empty;
            }

            public string Key { get; }
            public int EntryIndex { get; }
            public string SourceText { get; }
            public string TargetText { get; }
            public string TargetLocaleLabel { get; }
            public bool TargetMissing { get; }
            public bool IsMissingEntry { get; }
            public bool HasDialogueUsage { get; }
            public bool HasCodeUsage { get; }
            public bool DuplicateKey { get; }
            public string Category { get; }
            public string UsagePreview { get; }
            public string UsageSearchText { get; }
            public string TerminalWarning { get; }

            public LocalizationRow WithDuplicateKey(bool duplicateKey)
            {
                return new LocalizationRow(
                    Key,
                    EntryIndex,
                    SourceText,
                    TargetText,
                    TargetLocaleLabel,
                    TargetMissing,
                    IsMissingEntry,
                    HasDialogueUsage,
                    HasCodeUsage,
                    duplicateKey,
                    Category,
                    UsagePreview,
                    UsageSearchText,
                    TerminalWarning);
            }

            public string GetStatusLabel()
            {
                var parts = new List<string>();
                if (IsMissingEntry)
                {
                    parts.Add("missing table entry");
                }

                if (TargetMissing)
                {
                    parts.Add("missing target text");
                }

                if (DuplicateKey)
                {
                    parts.Add("duplicate key");
                }

                if (!string.IsNullOrWhiteSpace(TerminalWarning))
                {
                    parts.Add("terminal copy warning");
                }

                return string.Join(" | ", parts);
            }

            public string GetUsagePreview()
            {
                if (!string.IsNullOrWhiteSpace(UsagePreview))
                {
                    return UsagePreview;
                }

                return HasDialogueUsage || HasCodeUsage ? "Referenced" : "No direct usage found";
            }

            public List<string> BuildIssueList()
            {
                var issues = new List<string>();
                if (string.IsNullOrWhiteSpace(Key))
                {
                    issues.Add("Key is empty.");
                }

                if (IsMissingEntry)
                {
                    issues.Add("Referenced key does not exist in the string table.");
                }

                if (string.IsNullOrWhiteSpace(SourceText))
                {
                    issues.Add("Source text is empty.");
                }

                if (TargetMissing)
                {
                    issues.Add("Target locale text is missing or empty.");
                }

                if (DuplicateKey)
                {
                    issues.Add("More than one string table entry uses this key.");
                }

                if (!string.IsNullOrWhiteSpace(TerminalWarning))
                {
                    issues.Add(TerminalWarning);
                }

                return issues;
            }

            public string BuildSearchText()
            {
                return string.Join("\n", Key, SourceText, TargetText, Category, UsagePreview, UsageSearchText, TerminalWarning);
            }
        }

        private readonly struct LocalizationUsage
        {
            public LocalizationUsage(
                DialogueSequence sequence,
                string assetPath,
                int lineNumber,
                string role,
                string sequenceId,
                string contextNote,
                string speaker,
                string fallbackText)
            {
                Sequence = sequence;
                AssetPath = assetPath ?? string.Empty;
                LineNumber = lineNumber;
                Role = role ?? string.Empty;
                SequenceLabel = string.IsNullOrWhiteSpace(sequenceId)
                    ? sequence != null ? sequence.name : "(unknown sequence)"
                    : sequenceId;
                ContextNote = contextNote ?? string.Empty;
                Speaker = string.IsNullOrWhiteSpace(speaker) ? "(no speaker)" : speaker;
                FallbackText = fallbackText ?? string.Empty;
            }

            public DialogueSequence Sequence { get; }
            public string AssetPath { get; }
            public int LineNumber { get; }
            public string Role { get; }
            public string SequenceLabel { get; }
            public string ContextNote { get; }
            public string Speaker { get; }
            public string FallbackText { get; }
        }

        private readonly struct CodeUsage
        {
            public CodeUsage(string assetPath, int lineNumber, string fallbackText)
            {
                AssetPath = assetPath ?? string.Empty;
                LineNumber = lineNumber;
                FallbackText = fallbackText ?? string.Empty;
            }

            public string AssetPath { get; }
            public int LineNumber { get; }
            public string FallbackText { get; }
        }
    }
}
