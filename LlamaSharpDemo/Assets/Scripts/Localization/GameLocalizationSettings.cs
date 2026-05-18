using System;
using UnityEngine;

namespace DoodleDiplomacy.Localization
{
    [CreateAssetMenu(fileName = "GameLocalizationSettings", menuName = "DoodleDiplomacy/Localization/Game Settings")]
    public sealed class GameLocalizationSettings : ScriptableObject
    {
        [SerializeField] private bool enableLocalization = true;
        [SerializeField] private string sourceLocale = "en-US";
        [SerializeField] private string targetLocale = "en-US";
        [SerializeField] private string targetLanguage = "English";
        [SerializeField] private string targetLanguageNativeName = "English";
        [SerializeField] private LocalizedStringTable stringTable;
        [SerializeField] private bool logMissingTranslations = true;

        public bool EnableLocalization => enableLocalization;
        public string SourceLocale => string.IsNullOrWhiteSpace(sourceLocale) ? "en-US" : sourceLocale.Trim();
        public string TargetLocale => string.IsNullOrWhiteSpace(targetLocale) ? SourceLocale : targetLocale.Trim();
        public string TargetLanguage => string.IsNullOrWhiteSpace(targetLanguage) ? TargetLocale : targetLanguage.Trim();
        public string TargetLanguageNativeName => string.IsNullOrWhiteSpace(targetLanguageNativeName) ? TargetLanguage : targetLanguageNativeName.Trim();
        public bool LogMissingTranslations => logMissingTranslations;

        public bool UsesSourceLocale => !enableLocalization || LocaleEquals(SourceLocale, TargetLocale);

        public string GetLanguageName(string locale)
        {
            if (LocaleEquals(locale, SourceLocale))
            {
                return "English";
            }

            if (LocaleEquals(locale, TargetLocale))
            {
                return TargetLanguage;
            }

            return string.IsNullOrWhiteSpace(locale) ? TargetLanguage : locale.Trim();
        }

        public string GetLanguageNativeName(string locale)
        {
            if (LocaleEquals(locale, SourceLocale))
            {
                return "English";
            }

            if (LocaleEquals(locale, TargetLocale))
            {
                return TargetLanguageNativeName;
            }

            return GetLanguageName(locale);
        }

        public bool TryGetString(string key, out string text)
        {
            return TryGetString(key, TargetLocale, out text);
        }

        public bool TryGetString(string key, string locale, out string text)
        {
            text = string.Empty;
            if (stringTable == null || string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            string resolvedLocale = string.IsNullOrWhiteSpace(locale) ? TargetLocale : locale.Trim();
            if (enableLocalization &&
                !LocaleEquals(SourceLocale, resolvedLocale) &&
                stringTable.TryGetLocalized(key, resolvedLocale, out text))
            {
                return true;
            }

            return stringTable.TryGetSource(key, out text);
        }

        public static bool LocaleEquals(string a, string b)
        {
            string normalizedA = NormalizeLocale(a);
            string normalizedB = NormalizeLocale(b);
            return string.Equals(normalizedA, normalizedB, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeLocale(string locale)
        {
            return string.IsNullOrWhiteSpace(locale)
                ? string.Empty
                : locale.Trim().Replace('_', '-');
        }
    }
}
