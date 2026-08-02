using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DoodleDiplomacy.Localization.Editor
{
    public sealed class UiCopyCatalogImporter : AssetPostprocessor
    {
        public const string CatalogPath = "Assets/Localization/Authoring/ui_copy.catalog.json";
        public const string ManifestPath = "Assets/Generated/Localization/ui_copy.manifest.json";
        private const string LocalizationSettingsPath =
            "Assets/Resources/Localization/GameLocalizationSettings.asset";
        private static bool _isSyncing;

        [InitializeOnLoadMethod]
        private static void ScheduleSyncAfterScriptsReload()
        {
            EditorApplication.delayCall += () => SyncCatalog();
        }

        [MenuItem("Tools/Narrative Desk/Sync UI Copy Catalog", priority = 2)]
        public static void SyncCatalogMenu()
        {
            SyncCatalog(showCompletionDialog: true);
        }

        public static void SyncCatalog(bool showCompletionDialog = false)
        {
            if (_isSyncing || !File.Exists(ToAbsolutePath(CatalogPath)))
            {
                return;
            }

            _isSyncing = true;
            try
            {
                string json = File.ReadAllText(ToAbsolutePath(CatalogPath));
                UiCopyCatalogDocument catalog = JsonUtility.FromJson<UiCopyCatalogDocument>(json);
                if (catalog == null || catalog.entries == null)
                {
                    throw new InvalidDataException("UI copy catalog could not be parsed.");
                }

                GameLocalizationSettings settings =
                    AssetDatabase.LoadAssetAtPath<GameLocalizationSettings>(LocalizationSettingsPath);
                var settingsObject = settings != null ? new SerializedObject(settings) : null;
                LocalizedStringTable table = settingsObject?.FindProperty("stringTable")?.objectReferenceValue
                    as LocalizedStringTable;
                if (table == null)
                {
                    throw new InvalidOperationException("Localization string table was not found.");
                }

                HashSet<string> previousKeys = ReadManifestKeys();
                var currentKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                Undo.RecordObject(table, "Synchronize UI copy catalog");
                var tableObject = new SerializedObject(table);
                SerializedProperty entries = tableObject.FindProperty("entries");
                for (int i = 0; i < catalog.entries.Count; i++)
                {
                    UiCopyEntry entry = catalog.entries[i];
                    if (entry == null || string.IsNullOrWhiteSpace(entry.key))
                    {
                        continue;
                    }

                    string key = entry.key.Trim();
                    if (!currentKeys.Add(key))
                    {
                        Debug.LogWarning($"[Narrative Desk] Duplicate UI copy key '{key}' was ignored.");
                        continue;
                    }

                    UpsertEntry(entries, key, entry.sourceText, entry.localizedTexts);
                }

                RemoveRetiredEntries(entries, previousKeys, currentKeys);
                tableObject.ApplyModifiedPropertiesWithoutUndo();
                table.InvalidateRuntimeCache();
                EditorUtility.SetDirty(table);
                WriteManifest(catalog, currentKeys);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                if (showCompletionDialog)
                {
                    EditorUtility.DisplayDialog(
                        "Narrative Desk",
                        $"Synchronized {currentKeys.Count} UI string(s).",
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
            if (_isSyncing || !ContainsCatalog(importedAssets) && !ContainsCatalog(deletedAssets) &&
                !ContainsCatalog(movedAssets) && !ContainsCatalog(movedFromAssetPaths))
            {
                return;
            }

            EditorApplication.delayCall += () => SyncCatalog();
        }

        private static void UpsertEntry(
            SerializedProperty entries,
            string key,
            string sourceText,
            IReadOnlyList<UiCopyLocalizedText> localizedTexts)
        {
            SerializedProperty entry = FindEntry(entries, key);
            if (entry == null)
            {
                int index = entries.arraySize;
                entries.InsertArrayElementAtIndex(index);
                entry = entries.GetArrayElementAtIndex(index);
                entry.FindPropertyRelative("translations").ClearArray();
            }

            entry.FindPropertyRelative("key").stringValue = key;
            entry.FindPropertyRelative("sourceText").stringValue = sourceText ?? string.Empty;
            SerializedProperty translations = entry.FindPropertyRelative("translations");
            if (localizedTexts == null)
            {
                return;
            }

            for (int i = 0; i < localizedTexts.Count; i++)
            {
                UiCopyLocalizedText localized = localizedTexts[i];
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

        private static void RemoveRetiredEntries(
            SerializedProperty entries,
            IEnumerable<string> previousKeys,
            ISet<string> currentKeys)
        {
            var retired = new HashSet<string>(previousKeys, StringComparer.OrdinalIgnoreCase);
            retired.ExceptWith(currentKeys);
            if (retired.Count == 0)
            {
                return;
            }

            for (int i = entries.arraySize - 1; i >= 0; i--)
            {
                SerializedProperty candidate = entries.GetArrayElementAtIndex(i);
                string key = candidate.FindPropertyRelative("key").stringValue;
                if (retired.Contains(key))
                {
                    entries.DeleteArrayElementAtIndex(i);
                }
            }
        }

        private static SerializedProperty FindEntry(SerializedProperty entries, string key)
        {
            for (int i = 0; i < entries.arraySize; i++)
            {
                SerializedProperty candidate = entries.GetArrayElementAtIndex(i);
                if (string.Equals(
                        candidate.FindPropertyRelative("key").stringValue,
                        key,
                        StringComparison.OrdinalIgnoreCase))
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
                if (GameLocalizationSettings.LocaleEquals(
                        candidate.FindPropertyRelative("locale").stringValue,
                        locale))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static HashSet<string> ReadManifestKeys()
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string path = ToAbsolutePath(ManifestPath);
            if (!File.Exists(path))
            {
                return keys;
            }

            UiCopyGeneratedManifest manifest = JsonUtility.FromJson<UiCopyGeneratedManifest>(File.ReadAllText(path));
            if (manifest?.entries == null)
            {
                return keys;
            }

            for (int i = 0; i < manifest.entries.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(manifest.entries[i]?.key))
                {
                    keys.Add(manifest.entries[i].key.Trim());
                }
            }

            return keys;
        }

        private static void WriteManifest(UiCopyCatalogDocument catalog, IEnumerable<string> keys)
        {
            var byKey = new Dictionary<string, UiCopyEntry>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < catalog.entries.Count; i++)
            {
                UiCopyEntry entry = catalog.entries[i];
                if (entry != null && !string.IsNullOrWhiteSpace(entry.key))
                {
                    byKey[entry.key.Trim()] = entry;
                }
            }

            var manifest = new UiCopyGeneratedManifest();
            foreach (string key in keys)
            {
                byKey.TryGetValue(key, out UiCopyEntry entry);
                manifest.entries.Add(new UiCopyGeneratedManifestEntry
                {
                    key = key,
                    screenId = entry?.screenId ?? string.Empty,
                    surface = entry?.surface ?? string.Empty
                });
            }
            manifest.entries.Sort((a, b) => string.Compare(a.key, b.key, StringComparison.OrdinalIgnoreCase));

            string absolutePath = ToAbsolutePath(ManifestPath);
            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath) ?? string.Empty);
            File.WriteAllText(absolutePath, JsonUtility.ToJson(manifest, true) + System.Environment.NewLine);
        }

        private static bool ContainsCatalog(IReadOnlyList<string> paths)
        {
            if (paths == null)
            {
                return false;
            }

            for (int i = 0; i < paths.Count; i++)
            {
                if (string.Equals(paths[i], CatalogPath, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string ToAbsolutePath(string assetPath)
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath) ?? string.Empty;
            return Path.GetFullPath(Path.Combine(projectRoot, assetPath));
        }
    }

    [Serializable]
    public sealed class UiCopyGeneratedManifest
    {
        public List<UiCopyGeneratedManifestEntry> entries = new();
    }

    [Serializable]
    public sealed class UiCopyGeneratedManifestEntry
    {
        public string key = string.Empty;
        public string screenId = string.Empty;
        public string surface = string.Empty;
    }
}
