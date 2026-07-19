using System;
using System.Collections.Generic;
using System.IO;
using DoodleDiplomacy.Localization;
using DoodleDiplomacy.Localization.Editor;
using UnityEditor;
using UnityEngine;

namespace DoodleDiplomacy.Narrative.Editor
{
    public static class NarrativeGeneratedKeyRegistry
    {
        private static readonly Dictionary<string, NarrativeGeneratedKeyManifestEntry> Entries =
            new(StringComparer.OrdinalIgnoreCase);
        private static DateTime _loadedWriteTimeUtc;
        private static DateTime _loadedUiWriteTimeUtc;
        private static bool _loadedFromSourceFallback;

        public static bool TryGet(string key, out NarrativeGeneratedKeyManifestEntry entry)
        {
            RefreshIfNeeded();
            return Entries.TryGetValue(key ?? string.Empty, out entry);
        }

        public static void OpenSource(string key)
        {
            if (TryGet(key, out NarrativeGeneratedKeyManifestEntry entry))
            {
                NarrativeDeskLauncher.Open(entry.key, entry.beatId);
            }
            else
            {
                NarrativeDeskLauncher.Open(key);
            }
        }

        private static void RefreshIfNeeded()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath) ?? string.Empty;
            string absolutePath = Path.GetFullPath(Path.Combine(projectRoot, NarrativeScenarioImporter.ManifestPath));
            string absoluteUiPath = Path.GetFullPath(Path.Combine(projectRoot, UiCopyCatalogImporter.ManifestPath));
            bool hasNarrativeManifest = File.Exists(absolutePath);
            bool hasUiManifest = File.Exists(absoluteUiPath);
            if (!hasNarrativeManifest && !hasUiManifest)
            {
                if (!_loadedFromSourceFallback)
                {
                    LoadFromSourceDocuments();
                    LoadUiCatalogSource();
                    _loadedFromSourceFallback = true;
                }
                return;
            }

            DateTime writeTime = hasNarrativeManifest ? File.GetLastWriteTimeUtc(absolutePath) : DateTime.MinValue;
            DateTime uiWriteTime = hasUiManifest ? File.GetLastWriteTimeUtc(absoluteUiPath) : DateTime.MinValue;
            if (writeTime == _loadedWriteTimeUtc && uiWriteTime == _loadedUiWriteTimeUtc && Entries.Count > 0)
            {
                return;
            }

            Entries.Clear();
            _loadedFromSourceFallback = false;
            if (hasNarrativeManifest)
            {
                NarrativeGeneratedKeyManifest manifest =
                    JsonUtility.FromJson<NarrativeGeneratedKeyManifest>(File.ReadAllText(absolutePath));
                if (manifest?.entries != null)
                {
                    for (int i = 0; i < manifest.entries.Count; i++)
                    {
                        NarrativeGeneratedKeyManifestEntry entry = manifest.entries[i];
                        if (entry != null && !string.IsNullOrWhiteSpace(entry.key))
                        {
                            Entries[entry.key.Trim()] = entry;
                        }
                    }
                }
            }

            if (hasUiManifest)
            {
                UiCopyGeneratedManifest manifest =
                    JsonUtility.FromJson<UiCopyGeneratedManifest>(File.ReadAllText(absoluteUiPath));
                if (manifest?.entries != null)
                {
                    for (int i = 0; i < manifest.entries.Count; i++)
                    {
                        AddSourceEntry(manifest.entries[i]?.key, string.Empty, string.Empty);
                    }
                }
            }
            else
            {
                LoadUiCatalogSource();
            }

            _loadedWriteTimeUtc = writeTime;
            _loadedUiWriteTimeUtc = uiWriteTime;
        }

        private static void LoadUiCatalogSource()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath) ?? string.Empty;
            string catalogPath = Path.GetFullPath(Path.Combine(projectRoot, UiCopyCatalogImporter.CatalogPath));
            if (!File.Exists(catalogPath))
            {
                return;
            }

            UiCopyCatalogDocument catalog =
                JsonUtility.FromJson<UiCopyCatalogDocument>(File.ReadAllText(catalogPath));
            if (catalog?.entries == null)
            {
                return;
            }

            for (int i = 0; i < catalog.entries.Count; i++)
            {
                AddSourceEntry(catalog.entries[i]?.key, string.Empty, string.Empty);
            }
        }

        private static void LoadFromSourceDocuments()
        {
            Entries.Clear();
            string projectRoot = Path.GetDirectoryName(Application.dataPath) ?? string.Empty;
            string sourceFolder = Path.GetFullPath(Path.Combine(
                projectRoot,
                NarrativeScenarioImporter.NarrativeFolder));
            if (!Directory.Exists(sourceFolder))
            {
                return;
            }

            string[] paths = Directory.GetFiles(sourceFolder, "*.narrative.json");
            for (int i = 0; i < paths.Length; i++)
            {
                NarrativeScenarioAsset scenario = ScriptableObject.CreateInstance<NarrativeScenarioAsset>();
                try
                {
                    scenario.ApplyDocument(NarrativeScenarioJson.Parse(File.ReadAllText(paths[i])));
                    for (int j = 0; j < scenario.Beats.Count; j++)
                    {
                        NarrativeBeat beat = scenario.Beats[j];
                        AddSourceEntry(beat?.localizationKey, scenario.ScenarioId, beat?.id);
                    }

                    for (int j = 0; j < scenario.LocalizationEntries.Count; j++)
                    {
                        NarrativeLocalizationEntry entry = scenario.LocalizationEntries[j];
                        AddSourceEntry(entry?.key, scenario.ScenarioId, entry?.beatId);
                    }
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(scenario);
                }
            }
        }

        private static void AddSourceEntry(string key, string scenarioId, string beatId)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            Entries[key.Trim()] = new NarrativeGeneratedKeyManifestEntry
            {
                key = key.Trim(),
                scenarioId = scenarioId ?? string.Empty,
                beatId = beatId ?? string.Empty
            };
        }
    }
}
