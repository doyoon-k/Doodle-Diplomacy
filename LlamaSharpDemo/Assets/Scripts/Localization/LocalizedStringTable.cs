using System;
using System.Collections.Generic;
using UnityEngine;

namespace DoodleDiplomacy.Localization
{
    [CreateAssetMenu(fileName = "LocalizedStringTable", menuName = "DoodleDiplomacy/Localization/String Table")]
    public sealed class LocalizedStringTable : ScriptableObject
    {
        [SerializeField] private List<LocalizedStringEntry> entries = new();

        private Dictionary<string, LocalizedStringEntry> _entriesByKey;

        public bool TryGetSource(string key, out string sourceText)
        {
            sourceText = string.Empty;
            if (!TryGetEntry(key, out LocalizedStringEntry entry))
            {
                return false;
            }

            sourceText = entry.sourceText ?? string.Empty;
            return !string.IsNullOrWhiteSpace(sourceText);
        }

        public bool TryGetLocalized(string key, string locale, out string text)
        {
            text = string.Empty;
            if (string.IsNullOrWhiteSpace(locale) ||
                !TryGetEntry(key, out LocalizedStringEntry entry))
            {
                return false;
            }

            return entry.TryGetLocalized(locale, out text);
        }

        private bool TryGetEntry(string key, out LocalizedStringEntry entry)
        {
            entry = null;
            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            EnsureIndex();
            return _entriesByKey.TryGetValue(key.Trim(), out entry);
        }

        private void EnsureIndex()
        {
            if (_entriesByKey != null)
            {
                return;
            }

            _entriesByKey = new Dictionary<string, LocalizedStringEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (LocalizedStringEntry entry in entries)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.key))
                {
                    continue;
                }

                _entriesByKey[entry.key.Trim()] = entry;
            }
        }

        private void OnValidate()
        {
            _entriesByKey = null;
        }
    }

    [Serializable]
    public sealed class LocalizedStringEntry
    {
        public string key = string.Empty;

        [TextArea(1, 5)]
        public string sourceText = string.Empty;

        public List<LocalizedLocaleText> translations = new();

        public bool TryGetLocalized(string locale, out string text)
        {
            text = string.Empty;
            if (string.IsNullOrWhiteSpace(locale) || translations == null)
            {
                return false;
            }

            string normalizedLocale = NormalizeLocale(locale);
            string language = ExtractLanguage(normalizedLocale);

            foreach (LocalizedLocaleText translation in translations)
            {
                if (LocaleMatches(translation.locale, normalizedLocale, language))
                {
                    text = translation.text ?? string.Empty;
                    return !string.IsNullOrWhiteSpace(text);
                }
            }

            return false;
        }

        private static bool LocaleMatches(string candidate, string locale, string language)
        {
            string normalizedCandidate = NormalizeLocale(candidate);
            if (string.IsNullOrWhiteSpace(normalizedCandidate))
            {
                return false;
            }

            if (string.Equals(normalizedCandidate, locale, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return !string.IsNullOrWhiteSpace(language) &&
                   string.Equals(ExtractLanguage(normalizedCandidate), language, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeLocale(string locale)
        {
            return string.IsNullOrWhiteSpace(locale)
                ? string.Empty
                : locale.Trim().Replace('_', '-');
        }

        private static string ExtractLanguage(string locale)
        {
            if (string.IsNullOrWhiteSpace(locale))
            {
                return string.Empty;
            }

            int separatorIndex = locale.IndexOf('-');
            return separatorIndex > 0 ? locale.Substring(0, separatorIndex) : locale;
        }
    }

    [Serializable]
    public struct LocalizedLocaleText
    {
        public string locale;

        [TextArea(1, 5)]
        public string text;
    }
}
