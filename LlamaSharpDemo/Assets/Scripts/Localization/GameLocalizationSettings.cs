using System;
using System.Collections.Generic;
using TMPro;
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
        [Tooltip("Default TMP font used when no locale-specific font override exists.")]
        [SerializeField] private TMP_FontAsset defaultFont;
        [Tooltip("Locales exposed by the game. Each entry owns its language names, font, and text direction.")]
        [SerializeField] private List<SupportedLocaleDefinition> supportedLocales = new();
        [Tooltip("TMP font overrides keyed by locale, for example ko-KR.")]
        [SerializeField] private List<LocalizedFontEntry> fontOverrides = new();
        [Tooltip("Log a warning when a localization key cannot be resolved.")]
        [SerializeField] private bool logMissingTranslations = true;

        public bool EnableLocalization => enableLocalization;
        public string SourceLocale => string.IsNullOrWhiteSpace(sourceLocale) ? "en-US" : sourceLocale.Trim();
        public string TargetLocale => string.IsNullOrWhiteSpace(targetLocale) ? SourceLocale : targetLocale.Trim();
        public string TargetLanguage => string.IsNullOrWhiteSpace(targetLanguage) ? TargetLocale : targetLanguage.Trim();
        public string TargetLanguageNativeName => string.IsNullOrWhiteSpace(targetLanguageNativeName) ? TargetLanguage : targetLanguageNativeName.Trim();
        public bool LogMissingTranslations => logMissingTranslations;
        public IReadOnlyList<SupportedLocaleDefinition> SupportedLocales => supportedLocales;

        public bool UsesSourceLocale => !enableLocalization || LocaleEquals(SourceLocale, TargetLocale);

        public TMP_FontAsset ResolveFontForLocale(string locale)
        {
            string resolvedLocale = string.IsNullOrWhiteSpace(locale) ? TargetLocale : locale.Trim();
            string normalizedLocale = NormalizeLocale(resolvedLocale);
            string language = ExtractLanguage(normalizedLocale);

            if (TryGetSupportedLocale(resolvedLocale, out SupportedLocaleDefinition supported) &&
                supported.Font != null)
            {
                return supported.Font;
            }

            if (fontOverrides != null)
            {
                for (int i = 0; i < fontOverrides.Count; i++)
                {
                    LocalizedFontEntry entry = fontOverrides[i];
                    if (entry.Font == null || string.IsNullOrWhiteSpace(entry.Locale))
                    {
                        continue;
                    }

                    string candidate = NormalizeLocale(entry.Locale);
                    if (string.Equals(candidate, normalizedLocale, StringComparison.OrdinalIgnoreCase) ||
                        (!string.IsNullOrWhiteSpace(language) &&
                         string.Equals(ExtractLanguage(candidate), language, StringComparison.OrdinalIgnoreCase)))
                    {
                        return entry.Font;
                    }
                }
            }

            return defaultFont;
        }

        public string GetLanguageName(string locale)
        {
            if (TryGetSupportedLocale(locale, out SupportedLocaleDefinition supported) &&
                !string.IsNullOrWhiteSpace(supported.EnglishName))
            {
                return supported.EnglishName;
            }

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
            if (TryGetSupportedLocale(locale, out SupportedLocaleDefinition supported) &&
                !string.IsNullOrWhiteSpace(supported.NativeName))
            {
                return supported.NativeName;
            }

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

        public bool TryGetSupportedLocale(string locale, out SupportedLocaleDefinition definition)
        {
            definition = default;
            if (string.IsNullOrWhiteSpace(locale) || supportedLocales == null)
            {
                return false;
            }

            string normalizedLocale = NormalizeLocale(locale);
            string language = ExtractLanguage(normalizedLocale);
            SupportedLocaleDefinition languageMatch = default;
            bool hasLanguageMatch = false;
            for (int i = 0; i < supportedLocales.Count; i++)
            {
                SupportedLocaleDefinition candidate = supportedLocales[i];
                string candidateLocale = NormalizeLocale(candidate.Locale);
                if (string.IsNullOrWhiteSpace(candidateLocale))
                {
                    continue;
                }

                if (string.Equals(candidateLocale, normalizedLocale, StringComparison.OrdinalIgnoreCase))
                {
                    definition = candidate;
                    return true;
                }

                if (!hasLanguageMatch &&
                    !string.IsNullOrWhiteSpace(language) &&
                    string.Equals(ExtractLanguage(candidateLocale), language, StringComparison.OrdinalIgnoreCase))
                {
                    languageMatch = candidate;
                    hasLanguageMatch = true;
                }
            }

            definition = languageMatch;
            return hasLanguageMatch;
        }

        public bool IsRightToLeft(string locale)
        {
            return TryGetSupportedLocale(locale, out SupportedLocaleDefinition definition) &&
                   definition.TextDirection == LocalizedTextDirection.RightToLeft;
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

    public enum LocalizedTextDirection
    {
        LeftToRight,
        RightToLeft
    }

    [Serializable]
    public struct SupportedLocaleDefinition
    {
        [Tooltip("BCP-47 locale code, such as en-US, ko-KR, or ja-JP.")]
        [SerializeField] private string locale;
        [Tooltip("English language name used in model prompts and diagnostics.")]
        [SerializeField] private string englishName;
        [Tooltip("Language name written in the language itself, shown in the settings menu.")]
        [SerializeField] private string nativeName;
        [Tooltip("TMP font or fallback font asset for this locale.")]
        [SerializeField] private TMP_FontAsset font;
        [Tooltip("Writing direction used by this locale.")]
        [SerializeField] private LocalizedTextDirection textDirection;

        public SupportedLocaleDefinition(
            string locale,
            string englishName,
            string nativeName,
            TMP_FontAsset font = null,
            LocalizedTextDirection textDirection = LocalizedTextDirection.LeftToRight)
        {
            this.locale = locale ?? string.Empty;
            this.englishName = englishName ?? string.Empty;
            this.nativeName = nativeName ?? string.Empty;
            this.font = font;
            this.textDirection = textDirection;
        }

        public string Locale => locale?.Trim() ?? string.Empty;
        public string EnglishName => englishName?.Trim() ?? string.Empty;
        public string NativeName => nativeName?.Trim() ?? string.Empty;
        public TMP_FontAsset Font => font;
        public LocalizedTextDirection TextDirection => textDirection;
    }

    [Serializable]
    public struct LocalizedFontEntry
    {
        [Tooltip("Locale code for this font override, such as ko-KR or ko.")]
        [SerializeField] private string locale;
        [Tooltip("TMP font used for this locale.")]
        [SerializeField] private TMP_FontAsset font;

        public string Locale => locale;
        public TMP_FontAsset Font => font;
    }
}
