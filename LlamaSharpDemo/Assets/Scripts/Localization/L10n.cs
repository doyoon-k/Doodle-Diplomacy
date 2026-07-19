using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace DoodleDiplomacy.Localization
{
    public readonly struct L10nArg
    {
        public L10nArg(string key, object value)
        {
            Key = key ?? string.Empty;
            Value = value?.ToString() ?? string.Empty;
        }

        public string Key { get; }
        public string Value { get; }
    }

    public static class L10n
    {
        private const string DefaultSettingsResourcePath = "Localization/GameLocalizationSettings";
        private const string LocalePreferenceKey = "DD_LocalizationLocale";

        private static readonly HashSet<string> LoggedMissingKeys = new(StringComparer.OrdinalIgnoreCase);
        private static GameLocalizationSettings _settings;
        private static bool _attemptedLoad;
        private static string _runtimeLocale;
        private static bool _attemptedLoadPreference;

        public static event Action<string> LocaleChanged;

        public static GameLocalizationSettings Settings
        {
            get
            {
                if (_settings == null && !_attemptedLoad)
                {
                    _attemptedLoad = true;
                    _settings = Resources.Load<GameLocalizationSettings>(DefaultSettingsResourcePath);
                }

                return _settings;
            }
        }

        public static string CurrentLocale
        {
            get
            {
                GameLocalizationSettings settings = Settings;
                string locale = RuntimeLocale;
                if (!string.IsNullOrWhiteSpace(locale))
                {
                    return locale;
                }

                return settings != null ? settings.TargetLocale : "en-US";
            }
        }

        public static string CurrentLanguage
        {
            get
            {
                GameLocalizationSettings settings = Settings;
                return settings != null ? settings.GetLanguageName(CurrentLocale) : "English";
            }
        }

        public static string CurrentLanguageNativeName
        {
            get
            {
                GameLocalizationSettings settings = Settings;
                return settings != null ? settings.GetLanguageNativeName(CurrentLocale) : CurrentLanguage;
            }
        }

        public static TMP_FontAsset CurrentFont
        {
            get
            {
                GameLocalizationSettings settings = Settings;
                return settings != null ? settings.ResolveFontForLocale(CurrentLocale) : null;
            }
        }

        private static string RuntimeLocale
        {
            get
            {
                if (!_attemptedLoadPreference)
                {
                    _attemptedLoadPreference = true;
                    _runtimeLocale = PlayerPrefs.GetString(LocalePreferenceKey, string.Empty);
                }

                return _runtimeLocale;
            }
        }

        public static L10nArg Arg(string key, object value)
        {
            return new L10nArg(key, value);
        }

        public static void SetSettingsForRuntime(GameLocalizationSettings settings)
        {
            _settings = settings;
            _attemptedLoad = true;
            LoggedMissingKeys.Clear();
        }

        public static void SetLocale(string locale, bool persist = true)
        {
            string normalized = NormalizeLocale(locale);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                GameLocalizationSettings settings = Settings;
                normalized = settings != null ? settings.TargetLocale : "en-US";
            }

            if (GameLocalizationSettings.LocaleEquals(CurrentLocale, normalized))
            {
                return;
            }

            _runtimeLocale = normalized;
            _attemptedLoadPreference = true;
            LoggedMissingKeys.Clear();

            if (persist)
            {
                PlayerPrefs.SetString(LocalePreferenceKey, normalized);
                PlayerPrefs.Save();
            }

            LocaleChanged?.Invoke(normalized);
        }

        public static string T(string key, string fallback, params L10nArg[] args)
        {
            UiCopyTrace.Record(key);
            string text = Resolve(key, fallback);
            return Format(text, args);
        }

        private static string Resolve(string key, string fallback)
        {
            GameLocalizationSettings settings = Settings;
            if (settings == null)
            {
                return fallback ?? string.Empty;
            }

            string currentLocale = CurrentLocale;
            if (settings.TryGetString(key, currentLocale, out string localized))
            {
                return localized;
            }

            if (settings.LogMissingTranslations && !string.IsNullOrWhiteSpace(key) && LoggedMissingKeys.Add(key))
            {
                Debug.LogWarning($"[L10n] Missing localized string for key '{key}' in locale '{currentLocale}'.");
            }

            return fallback ?? string.Empty;
        }

        private static string Format(string template, IReadOnlyList<L10nArg> args)
        {
            if (string.IsNullOrEmpty(template) || args == null || args.Count == 0)
            {
                return template ?? string.Empty;
            }

            string result = template;
            for (int i = 0; i < args.Count; i++)
            {
                L10nArg arg = args[i];
                if (string.IsNullOrWhiteSpace(arg.Key))
                {
                    continue;
                }

                result = result.Replace("{" + arg.Key + "}", arg.Value);
            }

            return result;
        }

        private static string NormalizeLocale(string locale)
        {
            return string.IsNullOrWhiteSpace(locale)
                ? string.Empty
                : locale.Trim().Replace('_', '-');
        }
    }
}
