using System;
using System.Collections.Generic;
using System.IO;
using DoodleDiplomacy.Gameplay.FirstContact;
using DoodleDiplomacy.Localization;
using UnityEditor;
using UnityEngine;

namespace DoodleDiplomacy.Narrative.Editor
{
    public sealed class NarrativeScenarioImporter : AssetPostprocessor
    {
        public const string NarrativeFolder = "Assets/Narrative";
        public const string GeneratedFolder = "Assets/Generated/Narrative";
        public const string ManifestPath = NarrativeFolder + "/generated-keys.manifest.json";
        private const string LocalizationSettingsPath =
            "Assets/Resources/Localization/GameLocalizationSettings.asset";
        private const string FirstContactSettingsPath =
            "Assets/Data/FirstContact/FirstContactNarrativeSettings.asset";

        private static bool _isSyncing;

        [InitializeOnLoadMethod]
        private static void ScheduleSyncAfterScriptsReload()
        {
            EditorApplication.delayCall += () => SyncAll();
        }

        [MenuItem("Tools/Narrative Desk/Sync All Narrative Data", priority = 1)]
        public static void SyncAllMenu()
        {
            SyncAll(showCompletionDialog: true);
        }

        public static void SyncAll(bool showCompletionDialog = false)
        {
            if (_isSyncing)
            {
                return;
            }

            _isSyncing = true;
            try
            {
                EnsureAssetFolder(GeneratedFolder);
                string absoluteNarrativeFolder = ToAbsolutePath(NarrativeFolder);
                if (!Directory.Exists(absoluteNarrativeFolder))
                {
                    return;
                }

                var scenarios = new List<NarrativeScenarioAsset>();
                string[] files = Directory.GetFiles(
                    absoluteNarrativeFolder,
                    "*.narrative.json",
                    SearchOption.TopDirectoryOnly);
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < files.Length; i++)
                {
                    NarrativeScenarioAsset scenario = ImportScenario(ToAssetPath(files[i]));
                    if (scenario != null)
                    {
                        scenarios.Add(scenario);
                    }
                }

                MergeLocalization(scenarios);
                WriteGeneratedKeyManifest(scenarios);
                LinkFirstContactScenario(scenarios);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                if (showCompletionDialog)
                {
                    EditorUtility.DisplayDialog(
                        "Narrative Desk",
                        $"Synchronized {scenarios.Count} scenario(s).",
                        "OK");
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (showCompletionDialog)
                {
                    EditorUtility.DisplayDialog("Narrative Desk", exception.Message, "OK");
                }
            }
            finally
            {
                _isSyncing = false;
            }
        }

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (_isSyncing || !ContainsNarrativeDocument(importedAssets) &&
                !ContainsNarrativeDocument(deletedAssets) &&
                !ContainsNarrativeDocument(movedAssets) &&
                !ContainsNarrativeDocument(movedFromAssetPaths))
            {
                return;
            }

            EditorApplication.delayCall += () => SyncAll();
        }

        private static NarrativeScenarioAsset ImportScenario(string sourceAssetPath)
        {
            string json = File.ReadAllText(ToAbsolutePath(sourceAssetPath));
            NarrativeScenarioDocument document = NarrativeScenarioJson.Parse(json);
            if (document == null || string.IsNullOrWhiteSpace(document.scenarioId))
            {
                Debug.LogError($"[Narrative Desk] Missing scenarioId in {sourceAssetPath}.");
                return null;
            }

            string safeId = MakeSafeFileName(document.scenarioId);
            string assetPath = $"{GeneratedFolder}/{safeId}.asset";
            NarrativeScenarioAsset scenario =
                AssetDatabase.LoadAssetAtPath<NarrativeScenarioAsset>(assetPath);
            if (scenario == null)
            {
                scenario = ScriptableObject.CreateInstance<NarrativeScenarioAsset>();
                AssetDatabase.CreateAsset(scenario, assetPath);
            }

            Undo.RecordObject(scenario, "Synchronize narrative scenario");
            scenario.ApplyDocument(document);
            scenario.name = document.scenarioId;
            EditorUtility.SetDirty(scenario);
            return scenario;
        }

        private static void MergeLocalization(IReadOnlyList<NarrativeScenarioAsset> scenarios)
        {
            GameLocalizationSettings settings =
                AssetDatabase.LoadAssetAtPath<GameLocalizationSettings>(LocalizationSettingsPath);
            if (settings == null)
            {
                Debug.LogWarning("[Narrative Desk] Localization settings asset was not found.");
                return;
            }

            var settingsObject = new SerializedObject(settings);
            LocalizedStringTable table = settingsObject.FindProperty("stringTable")?.objectReferenceValue
                as LocalizedStringTable;
            if (table == null)
            {
                Debug.LogWarning("[Narrative Desk] Localization string table was not found.");
                return;
            }

            Undo.RecordObject(table, "Synchronize narrative localization");
            var tableObject = new SerializedObject(table);
            SerializedProperty entries = tableObject.FindProperty("entries");
            RemoveRetiredNarrativeEntries(entries, scenarios);
            for (int i = 0; i < scenarios.Count; i++)
            {
                NarrativeScenarioAsset scenario = scenarios[i];
                IReadOnlyList<NarrativeBeat> beats = scenario.Beats;
                for (int j = 0; j < beats.Count; j++)
                {
                    NarrativeBeat beat = beats[j];
                    if (beat == null || string.IsNullOrWhiteSpace(beat.localizationKey))
                    {
                        continue;
                    }

                    UpsertEntry(
                        entries,
                        beat.localizationKey,
                        beat.sourceText,
                        beat.localizedTexts);
                }

                IReadOnlyList<NarrativeLocalizationEntry> extraEntries =
                    scenario.LocalizationEntries;
                for (int j = 0; j < extraEntries.Count; j++)
                {
                    NarrativeLocalizationEntry entry = extraEntries[j];
                    if (entry == null || string.IsNullOrWhiteSpace(entry.key))
                    {
                        continue;
                    }

                    UpsertEntry(entries, entry.key, entry.sourceText, entry.localizedTexts);
                }
            }

            tableObject.ApplyModifiedPropertiesWithoutUndo();
            table.InvalidateRuntimeCache();
            EditorUtility.SetDirty(table);
        }

        private static void RemoveRetiredNarrativeEntries(
            SerializedProperty entries,
            IReadOnlyList<NarrativeScenarioAsset> scenarios)
        {
            string manifestPath = ToAbsolutePath(ManifestPath);
            if (!File.Exists(manifestPath))
            {
                return;
            }

            NarrativeGeneratedKeyManifest previousManifest =
                JsonUtility.FromJson<NarrativeGeneratedKeyManifest>(File.ReadAllText(manifestPath));
            if (previousManifest?.entries == null)
            {
                return;
            }

            var currentKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < scenarios.Count; i++)
            {
                NarrativeScenarioAsset scenario = scenarios[i];
                for (int j = 0; j < scenario.Beats.Count; j++)
                {
                    AddKey(currentKeys, scenario.Beats[j]?.localizationKey);
                }

                for (int j = 0; j < scenario.LocalizationEntries.Count; j++)
                {
                    AddKey(currentKeys, scenario.LocalizationEntries[j]?.key);
                }
            }

            var retiredKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < previousManifest.entries.Count; i++)
            {
                string key = previousManifest.entries[i]?.key;
                if (!string.IsNullOrWhiteSpace(key) && !currentKeys.Contains(key.Trim()))
                {
                    retiredKeys.Add(key.Trim());
                }
            }

            for (int i = entries.arraySize - 1; i >= 0; i--)
            {
                SerializedProperty entry = entries.GetArrayElementAtIndex(i);
                string key = entry.FindPropertyRelative("key").stringValue;
                if (retiredKeys.Contains(key))
                {
                    entries.DeleteArrayElementAtIndex(i);
                }
            }
        }

        private static void AddKey(ISet<string> keys, string key)
        {
            if (!string.IsNullOrWhiteSpace(key))
            {
                keys.Add(key.Trim());
            }
        }

        private static void UpsertEntry(
            SerializedProperty entries,
            string key,
            string sourceText,
            IReadOnlyList<NarrativeLocalizedText> localizedTexts)
        {
            SerializedProperty entry = FindEntry(entries, key);
            if (entry == null)
            {
                int index = entries.arraySize;
                entries.InsertArrayElementAtIndex(index);
                entry = entries.GetArrayElementAtIndex(index);
                entry.FindPropertyRelative("translations").ClearArray();
            }

            entry.FindPropertyRelative("key").stringValue = key.Trim();
            entry.FindPropertyRelative("sourceText").stringValue = sourceText ?? string.Empty;
            SerializedProperty translations = entry.FindPropertyRelative("translations");
            if (localizedTexts == null)
            {
                return;
            }

            for (int i = 0; i < localizedTexts.Count; i++)
            {
                NarrativeLocalizedText localized = localizedTexts[i];
                if (localized == null || string.IsNullOrWhiteSpace(localized.locale))
                {
                    continue;
                }

                SerializedProperty translation = FindTranslation(translations, localized.locale);
                if (translation == null)
                {
                    int index = translations.arraySize;
                    translations.InsertArrayElementAtIndex(index);
                    translation = translations.GetArrayElementAtIndex(index);
                }

                translation.FindPropertyRelative("locale").stringValue = localized.locale.Trim();
                translation.FindPropertyRelative("text").stringValue = localized.text ?? string.Empty;
            }
        }

        private static SerializedProperty FindEntry(SerializedProperty entries, string key)
        {
            for (int i = 0; i < entries.arraySize; i++)
            {
                SerializedProperty candidate = entries.GetArrayElementAtIndex(i);
                string candidateKey = candidate.FindPropertyRelative("key").stringValue;
                if (string.Equals(candidateKey, key, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static SerializedProperty FindTranslation(SerializedProperty translations, string locale)
        {
            for (int i = 0; i < translations.arraySize; i++)
            {
                SerializedProperty candidate = translations.GetArrayElementAtIndex(i);
                string candidateLocale = candidate.FindPropertyRelative("locale").stringValue;
                if (GameLocalizationSettings.LocaleEquals(candidateLocale, locale))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static void LinkFirstContactScenario(IReadOnlyList<NarrativeScenarioAsset> scenarios)
        {
            NarrativeScenarioAsset firstContact = null;
            for (int i = 0; i < scenarios.Count; i++)
            {
                if (string.Equals(
                        scenarios[i].ScenarioId,
                        "first_contact_day1",
                        StringComparison.OrdinalIgnoreCase))
                {
                    firstContact = scenarios[i];
                    break;
                }
            }

            FirstContactNarrativeSettings settings =
                AssetDatabase.LoadAssetAtPath<FirstContactNarrativeSettings>(FirstContactSettingsPath);
            if (settings == null || settings.narrativeScenario == firstContact)
            {
                return;
            }

            Undo.RecordObject(settings, "Link narrative scenario");
            settings.narrativeScenario = firstContact;
            EditorUtility.SetDirty(settings);
        }

        private static void WriteGeneratedKeyManifest(IReadOnlyList<NarrativeScenarioAsset> scenarios)
        {
            var manifest = new NarrativeGeneratedKeyManifest();
            var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < scenarios.Count; i++)
            {
                NarrativeScenarioAsset scenario = scenarios[i];
                for (int j = 0; j < scenario.Beats.Count; j++)
                {
                    AddManifestKey(manifest, unique, scenario.Beats[j]?.localizationKey, scenario.ScenarioId,
                        scenario.Beats[j]?.id);
                }

                for (int j = 0; j < scenario.LocalizationEntries.Count; j++)
                {
                    NarrativeLocalizationEntry entry = scenario.LocalizationEntries[j];
                    AddManifestKey(manifest, unique, entry?.key, scenario.ScenarioId, entry?.beatId);
                }
            }

            string json = JsonUtility.ToJson(manifest, true) + System.Environment.NewLine;
            string absolutePath = ToAbsolutePath(ManifestPath);
            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath) ?? string.Empty);
            if (!File.Exists(absolutePath) || !string.Equals(File.ReadAllText(absolutePath), json, StringComparison.Ordinal))
            {
                File.WriteAllText(absolutePath, json);
            }
        }

        private static void AddManifestKey(
            NarrativeGeneratedKeyManifest manifest,
            ISet<string> unique,
            string key,
            string scenarioId,
            string beatId)
        {
            if (string.IsNullOrWhiteSpace(key) || !unique.Add(key.Trim()))
            {
                return;
            }

            manifest.entries.Add(new NarrativeGeneratedKeyManifestEntry
            {
                key = key.Trim(),
                scenarioId = scenarioId ?? string.Empty,
                beatId = beatId ?? string.Empty
            });
        }

        private static bool ContainsNarrativeDocument(IReadOnlyList<string> paths)
        {
            if (paths == null)
            {
                return false;
            }

            for (int i = 0; i < paths.Count; i++)
            {
                if (paths[i].EndsWith(".narrative.json", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static void EnsureAssetFolder(string assetPath)
        {
            string[] parts = assetPath.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }

        private static string MakeSafeFileName(string value)
        {
            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                value = value.Replace(invalid, '_');
            }

            return value.Trim();
        }

        private static string ToAbsolutePath(string assetPath)
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath) ?? string.Empty;
            return Path.GetFullPath(Path.Combine(projectRoot, assetPath));
        }

        private static string ToAssetPath(string fullPath)
        {
            string normalizedData = Application.dataPath.Replace('\\', '/');
            string normalizedPath = Path.GetFullPath(fullPath).Replace('\\', '/');
            return normalizedPath.StartsWith(normalizedData, StringComparison.OrdinalIgnoreCase)
                ? "Assets" + normalizedPath.Substring(normalizedData.Length)
                : normalizedPath;
        }
    }

    [Serializable]
    public sealed class NarrativeGeneratedKeyManifest
    {
        public List<NarrativeGeneratedKeyManifestEntry> entries = new();
    }

    [Serializable]
    public sealed class NarrativeGeneratedKeyManifestEntry
    {
        public string key = string.Empty;
        public string scenarioId = string.Empty;
        public string beatId = string.Empty;
    }
}
