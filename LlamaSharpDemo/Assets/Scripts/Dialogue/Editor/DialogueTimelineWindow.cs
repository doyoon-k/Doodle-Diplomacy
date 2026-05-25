using System;
using System.Collections.Generic;
using System.IO;
using DoodleDiplomacy.Data;
using DoodleDiplomacy.Localization;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace DoodleDiplomacy.Editor
{
    public sealed class DialogueTimelineWindow : EditorWindow
    {
        private const string MenuPath = "Window/Doodle Diplomacy/Dialogue Timeline";
        private const float DefaultLeftPanelWidth = 260f;
        private const float DefaultDetailPanelWidth = 340f;
        private const float DefaultValidationPanelWidth = 340f;
        private const float MinLeftPanelWidth = 180f;
        private const float MinCenterPanelWidth = 300f;
        private const float MinDetailPanelWidth = 240f;
        private const float MinPreviewPanelWidth = 260f;
        private const float MinValidationPanelWidth = 220f;
        private const float SplitterWidth = 5f;
        private const float PreviewPanelHeight = 150f;
        private const float LineHeight = 44f;
        private const float PreviewTypingSpeed = 30f;
        private const float PreviewClickHoldSeconds = 1.25f;
        private const string LeftPanelWidthPrefsKey = "DoodleDiplomacy.DialogueTimeline.LeftPanelWidth";
        private const string DetailPanelWidthPrefsKey = "DoodleDiplomacy.DialogueTimeline.DetailPanelWidth";
        private const string ValidationPanelWidthPrefsKey = "DoodleDiplomacy.DialogueTimeline.ValidationPanelWidth";
        private const string SearchControlName = "DialogueTimelineSearch";
        private const string GameSceneAssetPath = "Assets/Scenes/GameScene.unity";
        private const string IntroSequenceFieldName = "introSequence:";
        private const string LocalizationSettingsPath = "Assets/Resources/Localization/GameLocalizationSettings.asset";
        private const string Day1OpeningSequencePath = "Assets/Resources/Dialogue/Day1/Day1Opening.asset";
        private const string Day1DialogueFolderPath = "Assets/Resources/Dialogue/Day1/";

        private readonly List<DialogueAssetEntry> _sequences = new();
        private readonly List<ValidationMessage> _validationMessages = new();

        private DialogueSequence _activeSequence;
        private SerializedObject _serializedSequence;
        private SerializedProperty _sequenceIdProperty;
        private SerializedProperty _contextNoteProperty;
        private SerializedProperty _linesProperty;
        private ReorderableList _lineList;

        private Vector2 _sequenceScroll;
        private Vector2 _detailScroll;
        private Vector2 _previewScroll;
        private string _search = string.Empty;
        private int _selectedLineIndex = -1;

        private bool _isPreviewPlaying;
        private double _previewStartedAt;
        private int _previewLineIndex;
        private float _leftPanelWidth = DefaultLeftPanelWidth;
        private float _detailPanelWidth = DefaultDetailPanelWidth;
        private float _validationPanelWidth = DefaultValidationPanelWidth;
        private SplitterTarget _activeSplitter = SplitterTarget.None;

        [MenuItem(MenuPath)]
        public static void Open()
        {
            var window = GetWindow<DialogueTimelineWindow>();
            window.titleContent = new GUIContent("Dialogue Timeline");
            window.minSize = new Vector2(980f, 560f);
            window.Show();
        }

        private void OnEnable()
        {
            LoadPanelWidths();
            RefreshSequenceList();
            TrySelectCurrentProjectSelection();
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            SavePanelWidths();
            EditorApplication.update -= OnEditorUpdate;
        }

        private void OnSelectionChange()
        {
            if (Selection.activeObject is DialogueSequence selectedSequence)
            {
                SetActiveSequence(selectedSequence);
                Repaint();
            }
        }

        private void OnGUI()
        {
            DrawToolbar();

            if (_serializedSequence != null)
            {
                _serializedSequence.Update();
            }

            Rect bodyRect = new Rect(0f, EditorGUIUtility.singleLineHeight + 6f, position.width, position.height - EditorGUIUtility.singleLineHeight - 6f);
            Rect mainRect = new Rect(bodyRect.x, bodyRect.y, bodyRect.width, Mathf.Max(0f, bodyRect.height - PreviewPanelHeight));
            Rect previewRect = new Rect(bodyRect.x, mainRect.yMax, bodyRect.width, PreviewPanelHeight);

            DrawMainArea(mainRect);
            DrawPreviewAndValidation(previewRect);

            if (_serializedSequence != null)
            {
                _serializedSequence.ApplyModifiedProperties();
            }
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                DialogueSequence selected = (DialogueSequence)EditorGUILayout.ObjectField(_activeSequence, typeof(DialogueSequence), false, GUILayout.Width(300f));
                if (selected != _activeSequence)
                {
                    SetActiveSequence(selected);
                }

                if (GUILayout.Button("New", EditorStyles.toolbarButton, GUILayout.Width(52f)))
                {
                    CreateNewSequence();
                }

                using (new EditorGUI.DisabledScope(_activeSequence == null))
                {
                    if (GUILayout.Button("Save", EditorStyles.toolbarButton, GUILayout.Width(52f)))
                    {
                        SaveActiveSequence();
                    }

                    if (GUILayout.Button("Ping", EditorStyles.toolbarButton, GUILayout.Width(52f)))
                    {
                        EditorGUIUtility.PingObject(_activeSequence);
                    }
                }

                if (GUILayout.Button("Open Day1 Opening", EditorStyles.toolbarButton, GUILayout.Width(126f)))
                {
                    OpenDay1OpeningSequence();
                }

                if (GUILayout.Button("Open Round Intro", EditorStyles.toolbarButton, GUILayout.Width(118f)))
                {
                    OpenGameSceneIntroSequence();
                }

                GUILayout.FlexibleSpace();

                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(70f)))
                {
                    RefreshSequenceList();
                }
            }
        }

        private void DrawMainArea(Rect rect)
        {
            ClampMainPanelWidths(rect.width);

            Rect leftRect = new Rect(rect.x, rect.y, _leftPanelWidth, rect.height);
            Rect leftSplitterRect = new Rect(leftRect.xMax, rect.y, SplitterWidth, rect.height);
            Rect detailRect = new Rect(rect.xMax - _detailPanelWidth, rect.y, _detailPanelWidth, rect.height);
            Rect detailSplitterRect = new Rect(detailRect.x - SplitterWidth, rect.y, SplitterWidth, rect.height);
            Rect centerRect = new Rect(leftSplitterRect.xMax, rect.y, Mathf.Max(0f, detailSplitterRect.x - leftSplitterRect.xMax), rect.height);

            DrawPanelBackground(leftRect);
            DrawPanelBackground(centerRect);
            DrawPanelBackground(detailRect);

            DrawSequenceBrowser(leftRect);
            DrawLineEditor(centerRect);
            DrawLineDetail(detailRect);
            DrawSplitter(leftSplitterRect, SplitterTarget.MainLeft);
            DrawSplitter(detailSplitterRect, SplitterTarget.MainDetail);
        }

        private void DrawSequenceBrowser(Rect rect)
        {
            GUILayout.BeginArea(rect);
            GUILayout.Space(8f);
            EditorGUILayout.LabelField("Sequences", EditorStyles.boldLabel);

            GUI.SetNextControlName(SearchControlName);
            string nextSearch = EditorGUILayout.TextField(_search, EditorStyles.toolbarSearchField);
            if (nextSearch != _search)
            {
                _search = nextSearch;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Clear", GUILayout.Width(56f)))
                {
                    _search = string.Empty;
                    GUI.FocusControl(SearchControlName);
                }

                GUILayout.Label($"{GetFilteredSequenceCount()} found", EditorStyles.miniLabel);
            }

            EditorGUILayout.Space(4f);

            _sequenceScroll = EditorGUILayout.BeginScrollView(_sequenceScroll);
            foreach (DialogueAssetEntry entry in _sequences)
            {
                if (!MatchesSearch(entry))
                {
                    continue;
                }

                bool selected = entry.Asset == _activeSequence;
                GUIStyle style = selected ? EditorStyles.helpBox : EditorStyles.label;
                Rect rowRect = EditorGUILayout.BeginVertical(style);

                if (GUI.Button(rowRect, GUIContent.none, GUIStyle.none))
                {
                    SetActiveSequence(entry.Asset);
                }

                EditorGUILayout.LabelField(entry.DisplayName, selected ? EditorStyles.boldLabel : EditorStyles.label);
                string usageLabel = GetUsageLabel(entry);
                if (!string.IsNullOrEmpty(usageLabel))
                {
                    EditorGUILayout.LabelField(usageLabel, EditorStyles.miniBoldLabel);
                }

                string note = MakeOptionalSingleLinePreview(entry.Asset.contextNote);
                if (!string.IsNullOrWhiteSpace(note))
                {
                    EditorGUILayout.LabelField(note, EditorStyles.wordWrappedMiniLabel);
                }

                EditorGUILayout.LabelField(entry.AssetPath, EditorStyles.miniLabel);
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawLineEditor(Rect rect)
        {
            GUILayout.BeginArea(rect);
            GUILayout.Space(8f);

            if (_activeSequence == null || _serializedSequence == null)
            {
                EditorGUILayout.HelpBox("Select or create a DialogueSequence asset.", MessageType.Info);
                GUILayout.EndArea();
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Timeline", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField(GetSequenceSummary(), EditorStyles.miniLabel, GUILayout.Width(190f));
            }

            EditorGUILayout.PropertyField(_sequenceIdProperty, new GUIContent("Sequence ID"));
            DrawContextNoteField();
            if (IsGameSceneIntroSequence(_activeSequence))
            {
                EditorGUILayout.HelpBox(
                    "This is the sequence wired to GameScene's RoundManager.introSequence. Day1CalibrationMode uses separate Day1 dialogue assets.",
                    MessageType.Info);

                if (GUILayout.Button("Bind Day1 Intro Localization Keys"))
                {
                    BindDay1IntroLocalizationKeys();
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(_selectedLineIndex < 0))
                {
                    if (GUILayout.Button("Insert Before"))
                    {
                        InsertLine(Mathf.Max(0, _selectedLineIndex));
                    }

                    if (GUILayout.Button("Insert After"))
                    {
                        InsertLine(Mathf.Min(_linesProperty.arraySize, _selectedLineIndex + 1));
                    }

                    if (GUILayout.Button("Duplicate"))
                    {
                        DuplicateSelectedLine();
                    }
                }
            }

            EditorGUILayout.Space(4f);
            _lineList?.DoLayoutList();
            GUILayout.EndArea();
        }

        private void DrawLineDetail(Rect rect)
        {
            GUILayout.BeginArea(rect);
            GUILayout.Space(8f);
            EditorGUILayout.LabelField("Line Detail", EditorStyles.boldLabel);

            if (_activeSequence == null || _linesProperty == null)
            {
                EditorGUILayout.HelpBox("No sequence selected.", MessageType.Info);
                GUILayout.EndArea();
                return;
            }

            if (!HasSelectedLine())
            {
                EditorGUILayout.HelpBox("Select a dialogue line to edit its fields.", MessageType.Info);
                GUILayout.EndArea();
                return;
            }

            SerializedProperty line = _linesProperty.GetArrayElementAtIndex(_selectedLineIndex);
            SerializedProperty characterId = line.FindPropertyRelative("characterID");
            SerializedProperty speakerLocalizationKey = line.FindPropertyRelative("speakerLocalizationKey");
            SerializedProperty text = line.FindPropertyRelative("text");
            SerializedProperty localizationKey = line.FindPropertyRelative("localizationKey");
            SerializedProperty displayMode = line.FindPropertyRelative("displayMode");
            SerializedProperty portraitId = line.FindPropertyRelative("portraitID");
            SerializedProperty advanceType = line.FindPropertyRelative("advanceType");
            SerializedProperty autoDelay = line.FindPropertyRelative("autoDelay");

            _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll);
            EditorGUILayout.LabelField($"Line #{_selectedLineIndex + 1}", EditorStyles.miniBoldLabel);
            EditorGUILayout.PropertyField(characterId, new GUIContent("Speaker"));
            EditorGUILayout.PropertyField(speakerLocalizationKey, new GUIContent("Speaker Key"));
            EditorGUILayout.PropertyField(displayMode, new GUIContent("Display"));
            EditorGUILayout.PropertyField(portraitId, new GUIContent("Portrait / Emotion"));
            EditorGUILayout.PropertyField(advanceType, new GUIContent("Advance"));

            using (new EditorGUI.DisabledScope((AdvanceType)advanceType.enumValueIndex != AdvanceType.Wait))
            {
                EditorGUILayout.PropertyField(autoDelay, new GUIContent("Wait Seconds"));
            }

            if ((AdvanceType)advanceType.enumValueIndex == AdvanceType.Click)
            {
                EditorGUILayout.HelpBox("Click lines wait for player input after typing. Preview uses a short simulated click hold.", MessageType.None);
            }
            else if ((AdvanceType)advanceType.enumValueIndex == AdvanceType.Auto)
            {
                EditorGUILayout.HelpBox("Auto lines continue immediately after typing.", MessageType.None);
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.PropertyField(localizationKey, new GUIContent("Text Key"));
            DrawRuntimeTextEditor(text, localizationKey);

            EditorGUILayout.Space(8f);
            DrawLocalizationEditor(characterId, speakerLocalizationKey, text, localizationKey);
            EditorGUILayout.Space(8f);
            DrawSelectedLineTiming(line);
            DrawSelectedLineTools();
            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawRuntimeTextEditor(SerializedProperty fallbackText, SerializedProperty localizationKey)
        {
            EditorGUILayout.LabelField("Runtime Text", EditorStyles.boldLabel);

            string key = localizationKey.stringValue;
            LocalizationContext context = LoadLocalizationContext();
            if (string.IsNullOrWhiteSpace(key) || !context.IsValid)
            {
                EditorGUILayout.HelpBox("No Text Key is set, so Play Mode uses this fallback text directly.", MessageType.None);
                fallbackText.stringValue = EditorGUILayout.TextArea(fallbackText.stringValue, GUILayout.MinHeight(130f));
                return;
            }

            var tableObject = new SerializedObject(context.StringTable);
            SerializedProperty entries = tableObject.FindProperty("entries");
            SerializedProperty entry = FindLocalizationEntry(entries, key);
            if (entry == null)
            {
                EditorGUILayout.HelpBox(
                    $"No localization entry exists for '{key}'. Play Mode will use the fallback text until an entry is created.",
                    MessageType.Warning);
                fallbackText.stringValue = EditorGUILayout.TextArea(fallbackText.stringValue, GUILayout.MinHeight(130f));
                return;
            }

            bool editSource = GameLocalizationSettings.LocaleEquals(context.SourceLocale, context.TargetLocale);
            SerializedProperty sourceText = entry.FindPropertyRelative("sourceText");
            SerializedProperty targetText = editSource
                ? sourceText
                : FindTranslation(entry.FindPropertyRelative("translations"), context.TargetLocale)?.FindPropertyRelative("text");

            string activeLocale = editSource ? context.SourceLocale : context.TargetLocale;
            string currentText = targetText != null ? targetText.stringValue : sourceText.stringValue;
            EditorGUILayout.HelpBox(
                $"Play Mode uses this {activeLocale} text because Text Key is set. Edit here if you want the game output to change.",
                MessageType.None);

            EditorGUI.BeginChangeCheck();
            string nextText = EditorGUILayout.TextArea(currentText, GUILayout.MinHeight(130f));
            if (!EditorGUI.EndChangeCheck())
            {
                return;
            }

            if (editSource)
            {
                sourceText.stringValue = nextText;
                fallbackText.stringValue = nextText;
                MarkActiveSequenceDirty();
            }
            else if (targetText != null)
            {
                targetText.stringValue = nextText;
            }
            else
            {
                CreateTranslation(tableObject, entry.FindPropertyRelative("translations"), context.TargetLocale, nextText);
            }

            tableObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(context.StringTable);
        }

        private void DrawSelectedLineTiming(SerializedProperty line)
        {
            string lineText = line.FindPropertyRelative("text").stringValue ?? string.Empty;
            AdvanceType advanceType = (AdvanceType)line.FindPropertyRelative("advanceType").enumValueIndex;
            float waitSeconds = line.FindPropertyRelative("autoDelay").floatValue;
            float typingSeconds = EstimateTypingSeconds(lineText);
            float durationSeconds = EstimateLineDuration(line);

            EditorGUILayout.LabelField("Estimated Timing", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Typing", FormatSeconds(typingSeconds));
            EditorGUILayout.LabelField("Line Duration", FormatDurationForAdvance(advanceType, durationSeconds, waitSeconds));
        }

        private void DrawSelectedLineTools()
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Line Tools", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(_selectedLineIndex <= 0))
                {
                    if (GUILayout.Button("Move Up"))
                    {
                        MoveSelectedLine(-1);
                    }
                }

                using (new EditorGUI.DisabledScope(_selectedLineIndex < 0 || _selectedLineIndex >= _linesProperty.arraySize - 1))
                {
                    if (GUILayout.Button("Move Down"))
                    {
                        MoveSelectedLine(1);
                    }
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Duplicate"))
                {
                    DuplicateSelectedLine();
                }

                if (GUILayout.Button("Delete"))
                {
                    DeleteSelectedLine();
                }
            }
        }

        private void DrawLocalizationEditor(
            SerializedProperty characterId,
            SerializedProperty speakerLocalizationKey,
            SerializedProperty text,
            SerializedProperty localizationKey)
        {
            EditorGUILayout.LabelField("Localization", EditorStyles.boldLabel);

            LocalizationContext context = LoadLocalizationContext();
            if (!context.IsValid)
            {
                EditorGUILayout.HelpBox(
                    $"Localization settings or string table could not be loaded from {LocalizationSettingsPath}.",
                    MessageType.Warning);
                return;
            }

            EditorGUILayout.LabelField("Source Locale", context.SourceLocale);
            EditorGUILayout.LabelField("Target Locale", context.TargetLocale);

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!string.IsNullOrWhiteSpace(speakerLocalizationKey.stringValue)))
                {
                    if (GUILayout.Button("Generate Speaker Key"))
                    {
                        speakerLocalizationKey.stringValue = $"speaker.{BuildKeySuffix(characterId.stringValue)}";
                    }
                }

                using (new EditorGUI.DisabledScope(!string.IsNullOrWhiteSpace(localizationKey.stringValue)))
                {
                    if (GUILayout.Button("Generate Text Key"))
                    {
                        localizationKey.stringValue = GenerateLineLocalizationKey();
                    }
                }
            }

            DrawLocalizationEntryEditor("Speaker", speakerLocalizationKey, characterId, true, context);
            DrawLocalizationEntryEditor("Line Text", localizationKey, text, false, context);
        }

        private void DrawLocalizationEntryEditor(
            string label,
            SerializedProperty keyProperty,
            SerializedProperty fallbackProperty,
            bool singleLineFallback,
            LocalizationContext context)
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel);

            string key = keyProperty.stringValue;
            if (string.IsNullOrWhiteSpace(key))
            {
                EditorGUILayout.HelpBox($"{label} has no localization key. Runtime will use the fallback field.", MessageType.None);
                return;
            }

            var tableObject = new SerializedObject(context.StringTable);
            SerializedProperty entries = tableObject.FindProperty("entries");
            SerializedProperty entry = FindLocalizationEntry(entries, key);
            if (entry == null)
            {
                EditorGUILayout.HelpBox($"No string table entry exists for '{key}'.", MessageType.Warning);
                if (GUILayout.Button($"Create {label} Entry From Fallback"))
                {
                    CreateLocalizationEntry(tableObject, entries, key, fallbackProperty.stringValue);
                    EditorUtility.SetDirty(context.StringTable);
                    AssetDatabase.SaveAssets();
                }

                return;
            }

            SerializedProperty sourceText = entry.FindPropertyRelative("sourceText");
            EditorGUILayout.LabelField("Key", key);
            EditorGUI.BeginChangeCheck();
            string nextSource = singleLineFallback
                ? EditorGUILayout.TextField("Source", sourceText.stringValue)
                : EditorGUILayout.TextArea(sourceText.stringValue, GUILayout.MinHeight(72f));
            if (EditorGUI.EndChangeCheck())
            {
                sourceText.stringValue = nextSource;
                fallbackProperty.stringValue = nextSource;
                tableObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(context.StringTable);
            }

            if (GameLocalizationSettings.LocaleEquals(context.SourceLocale, context.TargetLocale))
            {
                EditorGUILayout.HelpBox("Target locale matches source locale, so no translation row is needed.", MessageType.None);
                return;
            }

            SerializedProperty translations = entry.FindPropertyRelative("translations");
            SerializedProperty translation = FindTranslation(translations, context.TargetLocale);
            if (translation == null)
            {
                if (GUILayout.Button($"Add {context.TargetLocale} Translation"))
                {
                    CreateTranslation(tableObject, translations, context.TargetLocale, sourceText.stringValue);
                    EditorUtility.SetDirty(context.StringTable);
                    AssetDatabase.SaveAssets();
                }

                return;
            }

            SerializedProperty translatedText = translation.FindPropertyRelative("text");
            EditorGUI.BeginChangeCheck();
            string nextTranslation = singleLineFallback
                ? EditorGUILayout.TextField(context.TargetLocale, translatedText.stringValue)
                : EditorGUILayout.TextArea(translatedText.stringValue, GUILayout.MinHeight(72f));
            if (EditorGUI.EndChangeCheck())
            {
                translatedText.stringValue = nextTranslation;
                tableObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(context.StringTable);
            }
        }

        private void DrawPreviewAndValidation(Rect rect)
        {
            DrawPanelBackground(rect);
            GUILayout.BeginArea(rect);
            GUILayout.Space(8f);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel, GUILayout.Width(70f));

                using (new EditorGUI.DisabledScope(_activeSequence == null || _linesProperty == null || _linesProperty.arraySize == 0))
                {
                    if (GUILayout.Button(_isPreviewPlaying ? "Pause" : "Play", GUILayout.Width(64f)))
                    {
                        TogglePreviewPlayback();
                    }

                    if (GUILayout.Button("Step", GUILayout.Width(64f)))
                    {
                        StepPreview();
                    }

                    if (GUILayout.Button("Restart", GUILayout.Width(72f)))
                    {
                        RestartPreview();
                    }
                }

                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField(GetPreviewStatus(), EditorStyles.miniLabel, GUILayout.Width(260f));
            }

            Rect contentRect = GUILayoutUtility.GetRect(0f, 92f, GUILayout.ExpandWidth(true), GUILayout.Height(92f));
            ClampPreviewPanelWidths(contentRect.width);
            Rect validationRect = new Rect(contentRect.xMax - _validationPanelWidth, contentRect.y, _validationPanelWidth, contentRect.height);
            Rect splitterRect = new Rect(validationRect.x - SplitterWidth, contentRect.y, SplitterWidth, contentRect.height);
            Rect previewLineRect = new Rect(contentRect.x, contentRect.y, Mathf.Max(0f, splitterRect.x - contentRect.x), contentRect.height);

            DrawPreviewLine(previewLineRect);
            DrawValidationMessages(validationRect);
            DrawSplitter(splitterRect, SplitterTarget.PreviewValidation);

            GUILayout.EndArea();
        }

        private void DrawPreviewLine(Rect boxRect)
        {
            GUI.Box(boxRect, GUIContent.none, EditorStyles.helpBox);

            if (_activeSequence == null || _linesProperty == null || _linesProperty.arraySize == 0)
            {
                GUI.Label(new Rect(boxRect.x + 8f, boxRect.y + 8f, boxRect.width - 16f, 20f), "No dialogue lines to preview.", EditorStyles.miniLabel);
                return;
            }

            int index = Mathf.Clamp(_previewLineIndex, 0, _linesProperty.arraySize - 1);
            SerializedProperty line = _linesProperty.GetArrayElementAtIndex(index);
            string speaker = ResolveLocalizedTextForEditor(
                line.FindPropertyRelative("speakerLocalizationKey").stringValue,
                line.FindPropertyRelative("characterID").stringValue);
            string text = ResolveLocalizedTextForEditor(
                line.FindPropertyRelative("localizationKey").stringValue,
                line.FindPropertyRelative("text").stringValue);
            DisplayMode displayMode = (DisplayMode)line.FindPropertyRelative("displayMode").enumValueIndex;

            float elapsed = GetPreviewLineElapsed();
            int visibleCharacters = Mathf.Clamp(Mathf.FloorToInt(elapsed * PreviewTypingSpeed), 0, text.Length);
            if (!_isPreviewPlaying)
            {
                visibleCharacters = text.Length;
            }

            string visibleText = text.Length > 0 ? text.Substring(0, visibleCharacters) : string.Empty;
            GUI.Label(new Rect(boxRect.x + 8f, boxRect.y + 8f, boxRect.width - 16f, 18f), $"{index + 1}/{_linesProperty.arraySize}  {speaker}  [{displayMode}]", EditorStyles.miniBoldLabel);
            GUI.Label(new Rect(boxRect.x + 8f, boxRect.y + 30f, boxRect.width - 16f, boxRect.height - 38f), visibleText, EditorStyles.wordWrappedLabel);
        }

        private void DrawValidationMessages(Rect boxRect)
        {
            GUI.Box(boxRect, GUIContent.none, EditorStyles.helpBox);
            GUI.Label(new Rect(boxRect.x + 8f, boxRect.y + 8f, boxRect.width - 16f, 18f), "Validation", EditorStyles.miniBoldLabel);

            RebuildValidationMessages();
            Rect scrollRect = new Rect(boxRect.x + 4f, boxRect.y + 28f, boxRect.width - 8f, boxRect.height - 32f);
            Rect contentRect = new Rect(0f, 0f, scrollRect.width - 16f, Mathf.Max(scrollRect.height, _validationMessages.Count * 34f));

            _previewScroll = GUI.BeginScrollView(scrollRect, _previewScroll, contentRect);
            if (_validationMessages.Count == 0)
            {
                GUI.Label(new Rect(4f, 0f, contentRect.width - 8f, 20f), "No blocking issues found.", EditorStyles.miniLabel);
            }
            else
            {
                for (int i = 0; i < _validationMessages.Count; i++)
                {
                    ValidationMessage message = _validationMessages[i];
                    GUI.Label(new Rect(4f, i * 34f, contentRect.width - 8f, 32f), message.Text, EditorStyles.wordWrappedMiniLabel);
                }
            }

            GUI.EndScrollView();
        }

        private void DrawLineElement(Rect rect, int index, bool isActive, bool isFocused)
        {
            SerializedProperty line = _linesProperty.GetArrayElementAtIndex(index);
            SerializedProperty characterId = line.FindPropertyRelative("characterID");
            SerializedProperty text = line.FindPropertyRelative("text");
            SerializedProperty displayMode = line.FindPropertyRelative("displayMode");
            SerializedProperty advanceType = line.FindPropertyRelative("advanceType");
            SerializedProperty autoDelay = line.FindPropertyRelative("autoDelay");

            AdvanceType advance = (AdvanceType)advanceType.enumValueIndex;
            string lineText = text.stringValue ?? string.Empty;
            string preview = MakeSingleLinePreview(lineText);

            if (isActive)
            {
                EditorGUI.DrawRect(new Rect(rect.x - 4f, rect.y, rect.width + 8f, rect.height), new Color(0.22f, 0.34f, 0.52f, 0.25f));
            }

            Rect numberRect = new Rect(rect.x, rect.y + 3f, 38f, 18f);
            Rect metaRect = new Rect(numberRect.xMax + 4f, rect.y + 3f, rect.width - 42f, 18f);
            Rect textRect = new Rect(numberRect.xMax + 4f, rect.y + 22f, rect.width - 42f, 18f);

            EditorGUI.LabelField(numberRect, $"#{index + 1}", EditorStyles.miniBoldLabel);
            EditorGUI.LabelField(metaRect, $"{characterId.stringValue} | {(DisplayMode)displayMode.enumValueIndex} | {FormatAdvance(advance, autoDelay.floatValue)}", EditorStyles.miniLabel);
            EditorGUI.LabelField(textRect, preview, EditorStyles.label);
        }

        private void BuildLineList()
        {
            if (_linesProperty == null)
            {
                _lineList = null;
                return;
            }

            _lineList = new ReorderableList(_serializedSequence, _linesProperty, true, true, true, true)
            {
                elementHeight = LineHeight,
                drawHeaderCallback = rect =>
                {
                    EditorGUI.LabelField(rect, "Dialogue Lines");
                },
                drawElementCallback = DrawLineElement,
                onSelectCallback = list =>
                {
                    _selectedLineIndex = list.index;
                    _previewLineIndex = Mathf.Clamp(_selectedLineIndex, 0, Mathf.Max(0, _linesProperty.arraySize - 1));
                    ResetPreviewClock();
                },
                onAddCallback = list =>
                {
                    int insertIndex = Mathf.Clamp(list.index + 1, 0, _linesProperty.arraySize);
                    InsertLine(insertIndex);
                },
                onRemoveCallback = list =>
                {
                    DeleteSelectedLine();
                },
                onReorderCallback = list =>
                {
                    _selectedLineIndex = list.index;
                    _previewLineIndex = Mathf.Clamp(_selectedLineIndex, 0, Mathf.Max(0, _linesProperty.arraySize - 1));
                    MarkActiveSequenceDirty();
                }
            };
        }

        private void InsertLine(int index)
        {
            if (_linesProperty == null)
            {
                return;
            }

            _serializedSequence.Update();
            _linesProperty.InsertArrayElementAtIndex(index);
            SerializedProperty inserted = _linesProperty.GetArrayElementAtIndex(index);
            ResetLine(inserted);
            _selectedLineIndex = index;
            _lineList.index = index;
            _serializedSequence.ApplyModifiedProperties();
            MarkActiveSequenceDirty();
            ResetPreviewClock();
        }

        private void DuplicateSelectedLine()
        {
            if (!HasSelectedLine())
            {
                return;
            }

            _serializedSequence.Update();
            int targetIndex = _selectedLineIndex + 1;
            _linesProperty.InsertArrayElementAtIndex(targetIndex);
            _selectedLineIndex = targetIndex;
            _lineList.index = targetIndex;
            _serializedSequence.ApplyModifiedProperties();
            MarkActiveSequenceDirty();
            ResetPreviewClock();
        }

        private void DeleteSelectedLine()
        {
            if (!HasSelectedLine())
            {
                return;
            }

            _serializedSequence.Update();
            _linesProperty.DeleteArrayElementAtIndex(_selectedLineIndex);
            _selectedLineIndex = Mathf.Clamp(_selectedLineIndex, 0, _linesProperty.arraySize - 1);
            _lineList.index = _selectedLineIndex;
            _serializedSequence.ApplyModifiedProperties();
            MarkActiveSequenceDirty();
            _previewLineIndex = Mathf.Clamp(_previewLineIndex, 0, Mathf.Max(0, _linesProperty.arraySize - 1));
            ResetPreviewClock();
        }

        private void MoveSelectedLine(int direction)
        {
            if (!HasSelectedLine())
            {
                return;
            }

            int targetIndex = Mathf.Clamp(_selectedLineIndex + direction, 0, _linesProperty.arraySize - 1);
            if (targetIndex == _selectedLineIndex)
            {
                return;
            }

            _serializedSequence.Update();
            _linesProperty.MoveArrayElement(_selectedLineIndex, targetIndex);
            _selectedLineIndex = targetIndex;
            _lineList.index = targetIndex;
            _serializedSequence.ApplyModifiedProperties();
            MarkActiveSequenceDirty();
            ResetPreviewClock();
        }

        private void ResetLine(SerializedProperty line)
        {
            line.FindPropertyRelative("characterID").stringValue = "Adjutant";
            line.FindPropertyRelative("speakerLocalizationKey").stringValue = "speaker.adjutant";
            line.FindPropertyRelative("text").stringValue = string.Empty;
            line.FindPropertyRelative("localizationKey").stringValue = string.Empty;
            line.FindPropertyRelative("displayMode").enumValueIndex = (int)DisplayMode.Subtitle;
            line.FindPropertyRelative("portraitID").stringValue = string.Empty;
            line.FindPropertyRelative("advanceType").enumValueIndex = (int)AdvanceType.Click;
            line.FindPropertyRelative("autoDelay").floatValue = 2f;
        }

        private void RefreshSequenceList()
        {
            _sequences.Clear();
            string[] guids = AssetDatabase.FindAssets("t:DialogueSequence");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                DialogueSequence asset = AssetDatabase.LoadAssetAtPath<DialogueSequence>(path);
                if (asset == null)
                {
                    continue;
                }

                _sequences.Add(new DialogueAssetEntry(asset, path));
            }

            _sequences.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));
            Repaint();
        }

        private void TrySelectCurrentProjectSelection()
        {
            if (Selection.activeObject is DialogueSequence selectedSequence)
            {
                SetActiveSequence(selectedSequence);
                return;
            }

            if (_activeSequence == null && _sequences.Count > 0)
            {
                SetActiveSequence(_sequences[0].Asset);
            }
        }

        private void SetActiveSequence(DialogueSequence sequence)
        {
            if (_activeSequence == sequence)
            {
                return;
            }

            _activeSequence = sequence;
            _serializedSequence = sequence != null ? new SerializedObject(sequence) : null;
            _sequenceIdProperty = _serializedSequence?.FindProperty("sequenceID");
            _contextNoteProperty = _serializedSequence?.FindProperty("contextNote");
            _linesProperty = _serializedSequence?.FindProperty("lines");
            _selectedLineIndex = _linesProperty != null && _linesProperty.arraySize > 0 ? 0 : -1;
            _previewLineIndex = Mathf.Max(0, _selectedLineIndex);
            BuildLineList();
            if (_lineList != null)
            {
                _lineList.index = _selectedLineIndex;
            }

            ResetPreviewClock();
            Repaint();
        }

        private void CreateNewSequence()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Create Dialogue Sequence",
                "DialogueSequence",
                "asset",
                "Choose where to create the dialogue sequence.",
                "Assets/Data");

            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            var sequence = CreateInstance<DialogueSequence>();
            sequence.sequenceID = System.IO.Path.GetFileNameWithoutExtension(path);
            sequence.contextNote = string.Empty;
            sequence.lines = new List<DialogueLineData>();

            AssetDatabase.CreateAsset(sequence, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            RefreshSequenceList();
            SetActiveSequence(sequence);
            Selection.activeObject = sequence;
        }

        private void OpenGameSceneIntroSequence()
        {
            DialogueSequence sequence = FindGameSceneIntroSequence();
            if (sequence == null)
            {
                EditorUtility.DisplayDialog(
                    "Day1 Intro Not Found",
                    $"Could not find a DialogueSequence reference in {GameSceneAssetPath}.",
                    "OK");
                return;
            }

            SetActiveSequence(sequence);
            Selection.activeObject = sequence;
            EditorGUIUtility.PingObject(sequence);
        }

        private void OpenDay1OpeningSequence()
        {
            DialogueSequence sequence = AssetDatabase.LoadAssetAtPath<DialogueSequence>(Day1OpeningSequencePath);
            if (sequence == null)
            {
                EditorUtility.DisplayDialog(
                    "Day1 Opening Not Found",
                    $"Could not load {Day1OpeningSequencePath}.",
                    "OK");
                return;
            }

            SetActiveSequence(sequence);
            Selection.activeObject = sequence;
            EditorGUIUtility.PingObject(sequence);
        }

        private void SaveActiveSequence()
        {
            if (_serializedSequence != null)
            {
                _serializedSequence.ApplyModifiedProperties();
            }

            MarkActiveSequenceDirty();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            RefreshSequenceList();
        }

        private void MarkActiveSequenceDirty()
        {
            if (_activeSequence == null)
            {
                return;
            }

            EditorUtility.SetDirty(_activeSequence);
        }

        private bool HasSelectedLine()
        {
            return _linesProperty != null && _selectedLineIndex >= 0 && _selectedLineIndex < _linesProperty.arraySize;
        }

        private int GetFilteredSequenceCount()
        {
            int count = 0;
            foreach (DialogueAssetEntry entry in _sequences)
            {
                if (MatchesSearch(entry))
                {
                    count++;
                }
            }

            return count;
        }

        private bool MatchesSearch(DialogueAssetEntry entry)
        {
            if (string.IsNullOrWhiteSpace(_search))
            {
                return true;
            }

            return entry.DisplayName.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0
                || entry.AssetPath.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0
                || (entry.Asset != null && entry.Asset.sequenceID != null && entry.Asset.sequenceID.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0)
                || (entry.Asset != null && entry.Asset.contextNote != null && entry.Asset.contextNote.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void DrawContextNoteField()
        {
            if (_contextNoteProperty == null)
            {
                return;
            }

            EditorGUILayout.LabelField("Situation Note", EditorStyles.miniBoldLabel);
            EditorGUI.BeginChangeCheck();
            string nextNote = EditorGUILayout.TextArea(_contextNoteProperty.stringValue, GUILayout.MinHeight(42f));
            if (EditorGUI.EndChangeCheck())
            {
                _contextNoteProperty.stringValue = nextNote;
                MarkActiveSequenceDirty();
            }
        }

        private string GetSequenceSummary()
        {
            if (_linesProperty == null)
            {
                return string.Empty;
            }

            return $"{_linesProperty.arraySize} lines | {FormatSeconds(EstimateSequenceDuration())}";
        }

        private float EstimateSequenceDuration()
        {
            if (_linesProperty == null)
            {
                return 0f;
            }

            float seconds = 0f;
            for (int i = 0; i < _linesProperty.arraySize; i++)
            {
                seconds += EstimateLineDuration(_linesProperty.GetArrayElementAtIndex(i));
            }

            return seconds;
        }

        private static float EstimateLineDuration(SerializedProperty line)
        {
            string text = line.FindPropertyRelative("text").stringValue ?? string.Empty;
            AdvanceType advanceType = (AdvanceType)line.FindPropertyRelative("advanceType").enumValueIndex;
            float waitSeconds = line.FindPropertyRelative("autoDelay").floatValue;
            float seconds = EstimateTypingSeconds(text);

            if (advanceType == AdvanceType.Wait)
            {
                seconds += Mathf.Max(0f, waitSeconds);
            }
            else if (advanceType == AdvanceType.Click)
            {
                seconds += PreviewClickHoldSeconds;
            }

            return seconds;
        }

        private static float EstimateTypingSeconds(string text)
        {
            return string.IsNullOrEmpty(text) ? 0f : text.Length / PreviewTypingSpeed;
        }

        private static string FormatDurationForAdvance(AdvanceType advanceType, float durationSeconds, float waitSeconds)
        {
            return advanceType switch
            {
                AdvanceType.Click => $"{FormatSeconds(durationSeconds)} preview, waits for player in game",
                AdvanceType.Wait => $"{FormatSeconds(durationSeconds)} including {FormatSeconds(Mathf.Max(0f, waitSeconds))} wait",
                _ => FormatSeconds(durationSeconds),
            };
        }

        private static string FormatSeconds(float seconds)
        {
            return $"{Mathf.Max(0f, seconds):0.00}s";
        }

        private static string FormatAdvance(AdvanceType advanceType, float autoDelay)
        {
            return advanceType == AdvanceType.Wait ? $"Wait {Mathf.Max(0f, autoDelay):0.##}s" : advanceType.ToString();
        }

        private static string MakeSingleLinePreview(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return "(empty text)";
            }

            string singleLine = text.Replace('\n', ' ').Replace('\r', ' ');
            return singleLine.Length <= 92 ? singleLine : singleLine.Substring(0, 89) + "...";
        }

        private static string MakeOptionalSingleLinePreview(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            return MakeSingleLinePreview(text);
        }

        private void TogglePreviewPlayback()
        {
            _isPreviewPlaying = !_isPreviewPlaying;
            ResetPreviewClock();
        }

        private void RestartPreview()
        {
            _previewLineIndex = 0;
            _selectedLineIndex = _linesProperty != null && _linesProperty.arraySize > 0 ? 0 : -1;
            if (_lineList != null)
            {
                _lineList.index = _selectedLineIndex;
            }

            _isPreviewPlaying = true;
            ResetPreviewClock();
        }

        private void StepPreview()
        {
            if (_linesProperty == null || _linesProperty.arraySize == 0)
            {
                return;
            }

            _previewLineIndex = (_previewLineIndex + 1) % _linesProperty.arraySize;
            _selectedLineIndex = _previewLineIndex;
            if (_lineList != null)
            {
                _lineList.index = _selectedLineIndex;
            }

            ResetPreviewClock();
            Repaint();
        }

        private void ResetPreviewClock()
        {
            _previewStartedAt = EditorApplication.timeSinceStartup;
        }

        private float GetPreviewLineElapsed()
        {
            return Mathf.Max(0f, (float)(EditorApplication.timeSinceStartup - _previewStartedAt));
        }

        private string GetPreviewStatus()
        {
            if (_activeSequence == null || _linesProperty == null)
            {
                return "No active sequence";
            }

            if (_linesProperty.arraySize == 0)
            {
                return "Sequence has no lines";
            }

            SerializedProperty line = _linesProperty.GetArrayElementAtIndex(Mathf.Clamp(_previewLineIndex, 0, _linesProperty.arraySize - 1));
            AdvanceType advanceType = (AdvanceType)line.FindPropertyRelative("advanceType").enumValueIndex;
            return $"{(_isPreviewPlaying ? "Playing" : "Paused")} | line {_previewLineIndex + 1}/{_linesProperty.arraySize} | {FormatAdvance(advanceType, line.FindPropertyRelative("autoDelay").floatValue)}";
        }

        private void OnEditorUpdate()
        {
            if (!_isPreviewPlaying || _linesProperty == null || _linesProperty.arraySize == 0)
            {
                return;
            }

            _serializedSequence?.Update();
            _previewLineIndex = Mathf.Clamp(_previewLineIndex, 0, _linesProperty.arraySize - 1);
            SerializedProperty line = _linesProperty.GetArrayElementAtIndex(_previewLineIndex);
            float duration = EstimateLineDuration(line);

            if (GetPreviewLineElapsed() >= duration)
            {
                _previewLineIndex++;
                if (_previewLineIndex >= _linesProperty.arraySize)
                {
                    _previewLineIndex = _linesProperty.arraySize - 1;
                    _isPreviewPlaying = false;
                }

                _selectedLineIndex = _previewLineIndex;
                if (_lineList != null)
                {
                    _lineList.index = _selectedLineIndex;
                }

                ResetPreviewClock();
            }

            Repaint();
        }

        private void RebuildValidationMessages()
        {
            _validationMessages.Clear();

            if (_activeSequence == null || _linesProperty == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(_sequenceIdProperty.stringValue))
            {
                _validationMessages.Add(new ValidationMessage("Sequence ID is empty."));
            }

            if (IsGameSceneIntroSequence(_activeSequence))
            {
                _validationMessages.Add(new ValidationMessage("GameScene RoundManager.introSequence uses this sequence. Day1CalibrationMode uses separate Day1 dialogue assets."));
            }

            for (int i = 0; i < _linesProperty.arraySize; i++)
            {
                SerializedProperty line = _linesProperty.GetArrayElementAtIndex(i);
                string speaker = line.FindPropertyRelative("characterID").stringValue;
                string speakerKey = line.FindPropertyRelative("speakerLocalizationKey").stringValue;
                string text = line.FindPropertyRelative("text").stringValue;
                string textKey = line.FindPropertyRelative("localizationKey").stringValue;
                AdvanceType advanceType = (AdvanceType)line.FindPropertyRelative("advanceType").enumValueIndex;
                float autoDelay = line.FindPropertyRelative("autoDelay").floatValue;

                if (string.IsNullOrWhiteSpace(speaker))
                {
                    _validationMessages.Add(new ValidationMessage($"Line {i + 1}: speaker is empty."));
                }

                if (string.IsNullOrWhiteSpace(text))
                {
                    _validationMessages.Add(new ValidationMessage($"Line {i + 1}: text is empty."));
                }

                if (advanceType == AdvanceType.Wait && autoDelay < 0f)
                {
                    _validationMessages.Add(new ValidationMessage($"Line {i + 1}: wait seconds is negative."));
                }

                if (!string.IsNullOrWhiteSpace(speakerKey) && !LocalizationKeyExists(speakerKey))
                {
                    _validationMessages.Add(new ValidationMessage($"Line {i + 1}: speaker key '{speakerKey}' is missing from the string table."));
                }

                if (!string.IsNullOrWhiteSpace(textKey) && !LocalizationKeyExists(textKey))
                {
                    _validationMessages.Add(new ValidationMessage($"Line {i + 1}: text key '{textKey}' is missing from the string table."));
                }
            }
        }

        private static string GetUsageLabel(DialogueAssetEntry entry)
        {
            if (entry.AssetPath.StartsWith(Day1DialogueFolderPath, StringComparison.OrdinalIgnoreCase))
            {
                return "Day1 Dialogue";
            }

            return GetUsageLabel(entry.Asset);
        }

        private static string GetUsageLabel(DialogueSequence sequence)
        {
            return IsGameSceneIntroSequence(sequence) ? "GameScene Round Intro" : string.Empty;
        }

        private static bool IsGameSceneIntroSequence(DialogueSequence sequence)
        {
            if (sequence == null)
            {
                return false;
            }

            DialogueSequence gameSceneIntro = FindGameSceneIntroSequence();
            return gameSceneIntro == sequence;
        }

        private static DialogueSequence FindGameSceneIntroSequence()
        {
            string scenePath = Path.Combine(Directory.GetCurrentDirectory(), GameSceneAssetPath);
            if (!File.Exists(scenePath))
            {
                return null;
            }

            foreach (string line in File.ReadLines(scenePath))
            {
                int fieldIndex = line.IndexOf(IntroSequenceFieldName, StringComparison.Ordinal);
                if (fieldIndex < 0)
                {
                    continue;
                }

                int guidIndex = line.IndexOf("guid:", fieldIndex, StringComparison.Ordinal);
                if (guidIndex < 0)
                {
                    continue;
                }

                int guidStart = guidIndex + "guid:".Length;
                int guidEnd = line.IndexOf(',', guidStart);
                if (guidEnd < 0)
                {
                    guidEnd = line.Length;
                }

                string guid = line.Substring(guidStart, guidEnd - guidStart).Trim();
                if (string.IsNullOrWhiteSpace(guid))
                {
                    continue;
                }

                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                return string.IsNullOrEmpty(assetPath) ? null : AssetDatabase.LoadAssetAtPath<DialogueSequence>(assetPath);
            }

            return null;
        }

        private void BindDay1IntroLocalizationKeys()
        {
            if (_linesProperty == null)
            {
                return;
            }

            string[] lineKeys =
            {
                "intro.adjutant.line1",
                "intro.adjutant.line2",
                "intro.adjutant.line3"
            };

            _serializedSequence.Update();
            for (int i = 0; i < Mathf.Min(lineKeys.Length, _linesProperty.arraySize); i++)
            {
                SerializedProperty line = _linesProperty.GetArrayElementAtIndex(i);
                line.FindPropertyRelative("speakerLocalizationKey").stringValue = "speaker.adjutant";
                line.FindPropertyRelative("localizationKey").stringValue = lineKeys[i];
            }

            _serializedSequence.ApplyModifiedProperties();
            MarkActiveSequenceDirty();
        }

        private string GenerateLineLocalizationKey()
        {
            string sequenceId = _sequenceIdProperty != null && !string.IsNullOrWhiteSpace(_sequenceIdProperty.stringValue)
                ? _sequenceIdProperty.stringValue
                : _activeSequence != null ? _activeSequence.name : "sequence";

            return $"dialogue.{BuildKeySuffix(sequenceId)}.line_{Mathf.Max(1, _selectedLineIndex + 1):00}";
        }

        private static string ResolveLocalizedTextForEditor(string key, string fallback)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return fallback ?? string.Empty;
            }

            LocalizationContext context = LoadLocalizationContext();
            if (!context.IsValid)
            {
                return fallback ?? string.Empty;
            }

            return context.Settings.TryGetString(key, context.TargetLocale, out string text)
                ? text
                : fallback ?? string.Empty;
        }

        private static bool LocalizationKeyExists(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            LocalizationContext context = LoadLocalizationContext();
            if (!context.IsValid)
            {
                return false;
            }

            var tableObject = new SerializedObject(context.StringTable);
            SerializedProperty entries = tableObject.FindProperty("entries");
            return FindLocalizationEntry(entries, key) != null;
        }

        private static LocalizationContext LoadLocalizationContext()
        {
            GameLocalizationSettings settings = AssetDatabase.LoadAssetAtPath<GameLocalizationSettings>(LocalizationSettingsPath);
            if (settings == null)
            {
                return default;
            }

            var settingsObject = new SerializedObject(settings);
            SerializedProperty stringTableProperty = settingsObject.FindProperty("stringTable");
            LocalizedStringTable stringTable = stringTableProperty?.objectReferenceValue as LocalizedStringTable;
            if (stringTable == null)
            {
                return default;
            }

            return new LocalizationContext(settings, stringTable, settings.SourceLocale, settings.TargetLocale);
        }

        private static SerializedProperty FindLocalizationEntry(SerializedProperty entries, string key)
        {
            if (entries == null || string.IsNullOrWhiteSpace(key))
            {
                return null;
            }

            for (int i = 0; i < entries.arraySize; i++)
            {
                SerializedProperty entry = entries.GetArrayElementAtIndex(i);
                SerializedProperty keyProperty = entry.FindPropertyRelative("key");
                if (string.Equals(keyProperty.stringValue, key, StringComparison.OrdinalIgnoreCase))
                {
                    return entry;
                }
            }

            return null;
        }

        private static void CreateLocalizationEntry(SerializedObject tableObject, SerializedProperty entries, string key, string sourceText)
        {
            int index = entries.arraySize;
            entries.InsertArrayElementAtIndex(index);
            SerializedProperty entry = entries.GetArrayElementAtIndex(index);
            entry.FindPropertyRelative("key").stringValue = key.Trim();
            entry.FindPropertyRelative("sourceText").stringValue = sourceText ?? string.Empty;
            entry.FindPropertyRelative("translations").arraySize = 0;
            tableObject.ApplyModifiedProperties();
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

        private static void CreateTranslation(SerializedObject tableObject, SerializedProperty translations, string locale, string text)
        {
            int index = translations.arraySize;
            translations.InsertArrayElementAtIndex(index);
            SerializedProperty translation = translations.GetArrayElementAtIndex(index);
            translation.FindPropertyRelative("locale").stringValue = locale;
            translation.FindPropertyRelative("text").stringValue = text ?? string.Empty;
            tableObject.ApplyModifiedProperties();
        }

        private static string BuildKeySuffix(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "unnamed";
            }

            char[] chars = value.Trim().ToLowerInvariant().ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                chars[i] = char.IsLetterOrDigit(c) ? c : '_';
            }

            string suffix = new string(chars).Trim('_');
            return string.IsNullOrWhiteSpace(suffix) ? "unnamed" : suffix;
        }

        private static void DrawPanelBackground(Rect rect)
        {
            EditorGUI.DrawRect(rect, new Color(0.16f, 0.16f, 0.16f, 1f));
        }

        private void DrawSplitter(Rect rect, SplitterTarget target)
        {
            EditorGUI.DrawRect(rect, new Color(0.09f, 0.09f, 0.09f, 1f));
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.ResizeHorizontal);

            Event evt = Event.current;
            switch (evt.type)
            {
                case EventType.MouseDown:
                    if (rect.Contains(evt.mousePosition) && evt.button == 0)
                    {
                        _activeSplitter = target;
                        evt.Use();
                    }
                    break;

                case EventType.MouseDrag:
                    if (_activeSplitter == target)
                    {
                        ApplySplitterDelta(target, evt.delta.x);
                        evt.Use();
                        Repaint();
                    }
                    break;

                case EventType.MouseUp:
                    if (_activeSplitter == target)
                    {
                        _activeSplitter = SplitterTarget.None;
                        SavePanelWidths();
                        evt.Use();
                    }
                    break;
            }
        }

        private void ApplySplitterDelta(SplitterTarget target, float deltaX)
        {
            switch (target)
            {
                case SplitterTarget.MainLeft:
                    _leftPanelWidth += deltaX;
                    ClampMainPanelWidths(position.width);
                    break;

                case SplitterTarget.MainDetail:
                    _detailPanelWidth -= deltaX;
                    ClampMainPanelWidths(position.width);
                    break;

                case SplitterTarget.PreviewValidation:
                    _validationPanelWidth -= deltaX;
                    ClampPreviewPanelWidths(position.width);
                    break;
            }
        }

        private void ClampMainPanelWidths(float totalWidth)
        {
            float maxLeft = Mathf.Max(MinLeftPanelWidth, totalWidth - _detailPanelWidth - MinCenterPanelWidth - SplitterWidth * 2f);
            _leftPanelWidth = Mathf.Clamp(_leftPanelWidth, MinLeftPanelWidth, maxLeft);

            float maxDetail = Mathf.Max(MinDetailPanelWidth, totalWidth - _leftPanelWidth - MinCenterPanelWidth - SplitterWidth * 2f);
            _detailPanelWidth = Mathf.Clamp(_detailPanelWidth, MinDetailPanelWidth, maxDetail);

            maxLeft = Mathf.Max(MinLeftPanelWidth, totalWidth - _detailPanelWidth - MinCenterPanelWidth - SplitterWidth * 2f);
            _leftPanelWidth = Mathf.Clamp(_leftPanelWidth, MinLeftPanelWidth, maxLeft);
        }

        private void ClampPreviewPanelWidths(float totalWidth)
        {
            float maxValidation = Mathf.Max(MinValidationPanelWidth, totalWidth - MinPreviewPanelWidth - SplitterWidth);
            _validationPanelWidth = Mathf.Clamp(_validationPanelWidth, MinValidationPanelWidth, maxValidation);
        }

        private void LoadPanelWidths()
        {
            _leftPanelWidth = EditorPrefs.GetFloat(LeftPanelWidthPrefsKey, DefaultLeftPanelWidth);
            _detailPanelWidth = EditorPrefs.GetFloat(DetailPanelWidthPrefsKey, DefaultDetailPanelWidth);
            _validationPanelWidth = EditorPrefs.GetFloat(ValidationPanelWidthPrefsKey, DefaultValidationPanelWidth);
        }

        private void SavePanelWidths()
        {
            EditorPrefs.SetFloat(LeftPanelWidthPrefsKey, _leftPanelWidth);
            EditorPrefs.SetFloat(DetailPanelWidthPrefsKey, _detailPanelWidth);
            EditorPrefs.SetFloat(ValidationPanelWidthPrefsKey, _validationPanelWidth);
        }

        private readonly struct LocalizationContext
        {
            public readonly GameLocalizationSettings Settings;
            public readonly LocalizedStringTable StringTable;
            public readonly string SourceLocale;
            public readonly string TargetLocale;

            public LocalizationContext(
                GameLocalizationSettings settings,
                LocalizedStringTable stringTable,
                string sourceLocale,
                string targetLocale)
            {
                Settings = settings;
                StringTable = stringTable;
                SourceLocale = sourceLocale;
                TargetLocale = targetLocale;
            }

            public bool IsValid => Settings != null && StringTable != null;
        }

        private enum SplitterTarget
        {
            None,
            MainLeft,
            MainDetail,
            PreviewValidation
        }

        private readonly struct DialogueAssetEntry
        {
            public readonly DialogueSequence Asset;
            public readonly string AssetPath;
            public readonly string DisplayName;

            public DialogueAssetEntry(DialogueSequence asset, string assetPath)
            {
                Asset = asset;
                AssetPath = assetPath;
                DisplayName = string.IsNullOrWhiteSpace(asset.sequenceID) ? asset.name : $"{asset.name} ({asset.sequenceID})";
            }
        }

        private readonly struct ValidationMessage
        {
            public readonly string Text;

            public ValidationMessage(string text)
            {
                Text = text;
            }
        }
    }
}
