using System;
using UnityEngine;

namespace DoodleDiplomacy.Localization
{
    [CreateAssetMenu(fileName = "GameLocalizationSettings", menuName = "DoodleDiplomacy/Localization/Game Settings")]
    public sealed class GameLocalizationSettings : ScriptableObject
    {
        [Tooltip("Enable lookup from the localized string table instead of always using source text.")]
        [SerializeField] private bool enableLocalization = true;
        [Tooltip("Locale code used by source strings, typically en-US.")]
        [SerializeField] private string sourceLocale = "en-US";
        [Tooltip("Locale code currently targeted by generated or translated strings.")]
        [SerializeField] private string targetLocale = "en-US";
        [Tooltip("English display name for the target language.")]
        [SerializeField] private string targetLanguage = "English";
        [Tooltip("Native display name for the target language.")]
        [SerializeField] private string targetLanguageNativeName = "English";
        [Tooltip("String table asset containing source and localized UI/dialogue text.")]
        [SerializeField] private LocalizedStringTable stringTable;
        [Tooltip("Log a warning when a localization key cannot be resolved.")]
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
