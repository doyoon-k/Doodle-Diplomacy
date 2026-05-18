using System;
using DoodleDiplomacy.Localization;
using UnityEngine;

[CreateAssetMenu(fileName = "LlmLocalizationSettings", menuName = "LLM/Localization Settings")]
public class LlmLocalizationSettings : ScriptableObject
{
    [Tooltip("When disabled, LLM translation links pass text through unchanged.")]
    public bool enableLlmTranslation = true;

    [Tooltip("Locale used by the internal pipeline output before final translation.")]
    public string sourceLocale = "en-US";

    [Tooltip("Target locale selected by the game, such as en-US, ko-KR, ja-JP, or fr-FR.")]
    public string targetLocale = "en-US";

    [Tooltip("English display name of the target language, such as Korean or Japanese.")]
    public string targetLanguage = "English";

    [Tooltip("Native display name of the target language. This can use the language's own script in the asset.")]
    public string targetLanguageNativeName = "English";

    public void ApplyTo(PipelineState state)
    {
        if (state == null)
        {
            return;
        }

        state.SetString(PromptPipelineConstants.LlmTranslationEnabledKey, enableLlmTranslation ? "true" : "false");
        state.SetString(PromptPipelineConstants.SourceLocaleKey, NormalizeLocale(sourceLocale, "en-US"));
        bool hasGameLocalization = L10n.Settings != null;
        string runtimeTargetLocale = NormalizeLocale(
            hasGameLocalization ? L10n.CurrentLocale : targetLocale,
            NormalizeLocale(targetLocale, "en-US"));
        state.SetString(PromptPipelineConstants.TargetLocaleKey, runtimeTargetLocale);
        state.SetString(
            PromptPipelineConstants.TargetLanguageKey,
            NormalizeLanguage(
                hasGameLocalization ? L10n.CurrentLanguage : targetLanguage,
                NormalizeLanguage(targetLanguage, "English")));
        state.SetString(
            PromptPipelineConstants.TargetLanguageNativeNameKey,
            NormalizeLanguage(
                hasGameLocalization ? L10n.CurrentLanguageNativeName : targetLanguageNativeName,
                NormalizeLanguage(targetLanguageNativeName, NormalizeLanguage(targetLanguage, "English"))));
    }

    public static bool IsEnglishLocale(string locale)
    {
        if (string.IsNullOrWhiteSpace(locale))
        {
            return true;
        }

        string normalized = locale.Trim().Replace('_', '-');
        return string.Equals(normalized, "en", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("en-", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeLocale(string locale, string fallback)
    {
        return string.IsNullOrWhiteSpace(locale) ? fallback : locale.Trim();
    }

    private static string NormalizeLanguage(string language, string fallback)
    {
        return string.IsNullOrWhiteSpace(language) ? fallback : language.Trim();
    }
}
