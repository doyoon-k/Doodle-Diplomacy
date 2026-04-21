using System;
using System.Collections.Generic;
using UnityEngine;

namespace DoodleDiplomacy.AI
{
    [Serializable]
    public class PreGeneratedObjectImageEntry
    {
        [TextArea(2, 4)]
        public string prompt;
        public string promptKey;
        public Texture2D texture;
    }

    [CreateAssetMenu(
        fileName = "PreGeneratedObjectImageCatalog",
        menuName = "DoodleDiplomacy/Pre-generated Object Image Catalog")]
    public class PreGeneratedObjectImageCatalog : ScriptableObject
    {
        [SerializeField] private List<PreGeneratedObjectImageEntry> entries = new();

        private readonly Dictionary<string, Texture2D> _lookup = new(StringComparer.Ordinal);
        private bool _lookupDirty = true;

        public IReadOnlyList<PreGeneratedObjectImageEntry> Entries => entries;

        public bool TryGetTextureByPrompt(string prompt, out Texture2D texture)
        {
            string key = PreGeneratedObjectImageKeyUtility.ComputePromptKey(prompt);
            return TryGetTextureByKey(key, out texture);
        }

        public bool TryGetTextureByKey(string promptKey, out Texture2D texture)
        {
            texture = null;
            if (string.IsNullOrWhiteSpace(promptKey))
            {
                return false;
            }

            EnsureLookup();
            if (!_lookup.TryGetValue(promptKey.Trim(), out Texture2D found) || found == null)
            {
                return false;
            }

            texture = found;
            return true;
        }

        public void MarkLookupDirty()
        {
            _lookupDirty = true;
        }

#if UNITY_EDITOR
        public void ReplaceEntries(List<PreGeneratedObjectImageEntry> newEntries)
        {
            entries = newEntries ?? new List<PreGeneratedObjectImageEntry>();
            _lookupDirty = true;
        }
#endif

        private void OnEnable()
        {
            _lookupDirty = true;
        }

        private void OnValidate()
        {
            _lookupDirty = true;
        }

        private void EnsureLookup()
        {
            if (!_lookupDirty)
            {
                return;
            }

            _lookupDirty = false;
            _lookup.Clear();
            if (entries == null)
            {
                return;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                PreGeneratedObjectImageEntry entry = entries[i];
                if (entry == null)
                {
                    continue;
                }

                string key = string.IsNullOrWhiteSpace(entry.promptKey)
                    ? PreGeneratedObjectImageKeyUtility.ComputePromptKey(entry.prompt)
                    : entry.promptKey.Trim();
                entry.promptKey = key;

                if (string.IsNullOrWhiteSpace(key) || entry.texture == null)
                {
                    continue;
                }

                _lookup[key] = entry.texture;
            }
        }
    }
}
