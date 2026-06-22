using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using UnityEngine;

/// <summary>
/// Final-pass localization link for player-facing pipeline text.
/// Translation failure is non-fatal: the original source text remains available.
/// </summary>
public class SafeTranslateResponseChainLink : IStateChainLink, ICustomLinkStateProvider
{
    private const string DefaultSourceKey = PromptPipelineConstants.AnswerKey;
    private const string DefaultOutputKey = PromptPipelineConstants.AnswerKey;
    private const string DefaultLocaleKey = PromptPipelineConstants.TargetLocaleKey;
    private const string DefaultLanguageKey = PromptPipelineConstants.TargetLanguageKey;
    private const string DefaultNativeLanguageKey = PromptPipelineConstants.TargetLanguageNativeNameKey;
    private const string DefaultEnabledKey = PromptPipelineConstants.LlmTranslationEnabledKey;
    private const string LabelPromptStyle = "label";
    private const int DefaultLabelTranslationMaxAttempts = 3;

    private static readonly Dictionary<string, string> LabelTranslationCache = new(StringComparer.OrdinalIgnoreCase);

    private readonly BaseLlmGenerationProfile _settings;
    private readonly string _sourceKey;
    private readonly string _outputKey;
    private readonly string _localeKey;
    private readonly string _languageKey;
    private readonly string _nativeLanguageKey;
    private readonly string _enabledKey;
    private readonly string _promptStyle;
    private readonly bool _isLabelTranslation;
    private readonly int _maxAttempts;
    private readonly ILlmService _service;

    public SafeTranslateResponseChainLink()
        : this(null, null)
    {
    }

    public SafeTranslateResponseChainLink(ScriptableObject profileAsset)
        : this(null, profileAsset)
    {
    }

    public SafeTranslateResponseChainLink(Dictionary<string, string> parameters)
        : this(parameters, null)
    {
    }

    public SafeTranslateResponseChainLink(Dictionary<string, string> parameters, ScriptableObject profileAsset)
        : this(parameters, profileAsset, null)
    {
    }

    public SafeTranslateResponseChainLink(
        Dictionary<string, string> parameters,
        ScriptableObject profileAsset,
        ILlmService service)
    {
        _settings = profileAsset as BaseLlmGenerationProfile;
        _service = service;
        _sourceKey = GetParameter(parameters, "sourceKey", DefaultSourceKey);
        _outputKey = GetParameter(parameters, "outputKey", DefaultOutputKey);
        _localeKey = GetParameter(parameters, "localeKey", DefaultLocaleKey);
        _languageKey = GetParameter(parameters, "languageKey", DefaultLanguageKey);
        _nativeLanguageKey = GetParameter(parameters, "nativeLanguageKey", DefaultNativeLanguageKey);
        _enabledKey = GetParameter(parameters, "enabledKey", DefaultEnabledKey);
        _promptStyle = GetParameter(parameters, "promptStyle", string.Empty);
        _isLabelTranslation = string.Equals(_promptStyle, LabelPromptStyle, StringComparison.OrdinalIgnoreCase);
        _maxAttempts = Math.Max(
            1,
            GetIntParameter(
                parameters,
                "maxAttempts",
                _isLabelTranslation ? DefaultLabelTranslationMaxAttempts : 1));
    }

    public IEnumerator Execute(PipelineState state, Action<PipelineState> onDone)
    {
        state ??= new PipelineState();

        string sourceText = state.GetString(_sourceKey, string.Empty);
        if (string.IsNullOrWhiteSpace(sourceText))
        {
            Debug.Log($"[SafeTranslateResponseChainLink] Source key '{_sourceKey}' is empty. Keeping source text.");
            SetOutput(state, sourceText);
            onDone?.Invoke(state);
            yield break;
        }

        string targetLocale = state.GetString(_localeKey, "en-US");
        if (!IsTranslationEnabled(state))
        {
            Debug.Log($"[SafeTranslateResponseChainLink] Translation disabled by state key '{_enabledKey}'. Keeping source text.");
            SetFallbackOutput(state, sourceText, targetLocale);
            onDone?.Invoke(state);
            yield break;
        }

        if (LlmLocalizationSettings.IsEnglishLocale(targetLocale))
        {
            Debug.Log($"[SafeTranslateResponseChainLink] Target locale '{targetLocale}' is English. Keeping source text.");
            SetOutput(state, sourceText);
            onDone?.Invoke(state);
            yield break;
        }

        if (_settings == null)
        {
            Debug.LogWarning("[SafeTranslateResponseChainLink] Translation profile is missing. Keeping source text.");
            SetFallbackOutput(state, sourceText, targetLocale);
            onDone?.Invoke(state);
            yield break;
        }

        ILlmService service = _service ?? LlmServiceLocator.Current;
        if (service == null)
        {
            Debug.LogWarning("[SafeTranslateResponseChainLink] ILlmService is missing. Keeping source text.");
            SetFallbackOutput(state, sourceText, targetLocale);
            onDone?.Invoke(state);
            yield break;
        }

        string targetLanguage = state.GetString(_languageKey, targetLocale);
        string nativeLanguage = state.GetString(_nativeLanguageKey, targetLanguage);
        if (_isLabelTranslation && TryGetCachedLabelTranslation(sourceText, targetLocale, out string cachedTranslation))
        {
            Debug.Log(
                $"[SafeTranslateResponseChainLink] Using cached label translation for '{sourceText}' " +
                $"in '{targetLocale}'.");
            SetOutput(state, cachedTranslation);
            onDone?.Invoke(state);
            yield break;
        }

        string prompt = _isLabelTranslation
            ? BuildLabelTranslationPrompt(sourceText, targetLanguage, nativeLanguage, targetLocale)
            : BuildTranslationPrompt(sourceText, targetLanguage, nativeLanguage, targetLocale);
        string translated = null;
        string lastInvalidReason = string.Empty;
        Debug.Log(
            $"[SafeTranslateResponseChainLink] Translating state key '{_sourceKey}' to '{targetLocale}' " +
            $"with profile '{_settings.name}'." +
            (_isLabelTranslation ? " PromptStyle=label." : string.Empty));

        for (int attempt = 1; attempt <= _maxAttempts; attempt++)
        {
            string rawResponse = null;
            yield return service.GenerateCompletionWithState(
                _settings,
                prompt,
                state,
                response => rawResponse = response);

            if (TryReadTranslatedResponse(rawResponse, _isLabelTranslation, out string candidate) &&
                !string.IsNullOrWhiteSpace(candidate))
            {
                candidate = NormalizeTranslatedLabel(candidate);
                if (!_isLabelTranslation ||
                    IsValidLabelTranslation(candidate, sourceText, targetLocale, out lastInvalidReason))
                {
                    translated = candidate;
                    break;
                }
            }
            else
            {
                lastInvalidReason = _isLabelTranslation
                    ? "response was not valid JSON or a plain label"
                    : "response was not valid JSON";
            }

            Debug.LogWarning(
                $"[SafeTranslateResponseChainLink] Translation attempt {attempt}/{_maxAttempts} rejected: " +
                $"{lastInvalidReason}. Raw response: {BuildLogExcerpt(rawResponse)}");
        }

        if (string.IsNullOrWhiteSpace(translated))
        {
            Debug.LogWarning(
                "[SafeTranslateResponseChainLink] Translation failed validation. Keeping source text. " +
                $"Reason: {lastInvalidReason}");
            SetFallbackOutput(state, sourceText, targetLocale);
            onDone?.Invoke(state);
            yield break;
        }

        if (_isLabelTranslation)
        {
            CacheLabelTranslation(sourceText, targetLocale, translated);
        }

        Debug.Log(
            $"[SafeTranslateResponseChainLink] Translation complete for '{_sourceKey}' -> '{_outputKey}'. " +
            $"SourceLength={sourceText.Length}, TranslatedLength={translated.Length}");
        SetOutput(state, translated);
        onDone?.Invoke(state);
    }

    public IEnumerable<string> GetWrites()
    {
        yield return _outputKey;
    }

    private bool IsTranslationEnabled(PipelineState state)
    {
        if (!state.TryGetString(_enabledKey, out string enabledText) ||
            string.IsNullOrWhiteSpace(enabledText))
        {
            return true;
        }

        string normalized = enabledText.Trim();
        return !string.Equals(normalized, "false", StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(normalized, "0", StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(normalized, "off", StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(normalized, "no", StringComparison.OrdinalIgnoreCase);
    }

    private void SetOutput(PipelineState state, string value)
    {
        state.SetString(_outputKey, value ?? string.Empty);
    }

    private void SetFallbackOutput(PipelineState state, string sourceText, string targetLocale)
    {
        if (_isLabelTranslation &&
            !string.Equals(_sourceKey, _outputKey, StringComparison.Ordinal) &&
            !LlmLocalizationSettings.IsEnglishLocale(targetLocale))
        {
            SetOutput(state, string.Empty);
            return;
        }

        SetOutput(state, sourceText);
    }

    private static string BuildTranslationPrompt(
        string sourceText,
        string targetLanguage,
        string nativeLanguage,
        string targetLocale)
    {
        string languageLabel = BuildLanguageLabel(targetLanguage, nativeLanguage);
        var builder = new StringBuilder();
        builder.Append("Target language: ");
        builder.Append(languageLabel);
        builder.Append(" (");
        builder.Append(string.IsNullOrWhiteSpace(targetLocale) ? "target locale" : targetLocale.Trim());
        builder.AppendLine(").");
        builder.AppendLine("Source text:");
        builder.Append(sourceText);
        return builder.ToString();
    }

    private static string BuildLabelTranslationPrompt(
        string sourceText,
        string targetLanguage,
        string nativeLanguage,
        string targetLocale)
    {
        string languageLabel = BuildLanguageLabel(targetLanguage, nativeLanguage);
        var builder = new StringBuilder();
        builder.Append("Target language: ");
        builder.Append(languageLabel);
        builder.Append(" (");
        builder.Append(string.IsNullOrWhiteSpace(targetLocale) ? "target locale" : targetLocale.Trim());
        builder.AppendLine(").");
        builder.AppendLine("Source drawing label:");
        builder.AppendLine(sourceText);
        builder.AppendLine();
        builder.AppendLine("Translate only this short visual object label.");
        builder.AppendLine("Return a concise natural label in the target language.");
        builder.AppendLine("Do not copy the English label unless it is already a proper name.");
        builder.AppendLine("Do not explain, transliterate, add quotes, or add punctuation.");
        return builder.ToString();
    }

    private static string BuildLanguageLabel(string targetLanguage, string nativeLanguage)
    {
        string language = string.IsNullOrWhiteSpace(targetLanguage)
            ? "the target language"
            : targetLanguage.Trim();
        string native = string.IsNullOrWhiteSpace(nativeLanguage) ? string.Empty : nativeLanguage.Trim();

        if (string.IsNullOrWhiteSpace(native) ||
            string.Equals(language, native, StringComparison.OrdinalIgnoreCase))
        {
            return language;
        }

        return $"{language} / {native}";
    }

    private static bool TryReadTranslatedResponse(
        string rawResponse,
        bool allowPlainLabelResponse,
        out string translated)
    {
        translated = null;
        if (string.IsNullOrWhiteSpace(rawResponse))
        {
            return false;
        }

        string json = ExtractJsonObject(rawResponse);
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty(PromptPipelineConstants.AnswerKey, out JsonElement responseElement) &&
                responseElement.ValueKind == JsonValueKind.String)
            {
                translated = responseElement.GetString();
                return true;
            }
        }
        catch (JsonException)
        {
        }

        return allowPlainLabelResponse && TryReadPlainLabelResponse(rawResponse, out translated);
    }

    private static bool TryReadPlainLabelResponse(string rawResponse, out string translated)
    {
        translated = null;
        string candidate = StripCodeFence(rawResponse).Trim();
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        string[] lines = candidate.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length != 1)
        {
            return false;
        }

        candidate = TrimWrappingQuotes(lines[0].Trim());
        candidate = NormalizeTranslatedLabel(candidate);
        if (string.IsNullOrWhiteSpace(candidate) ||
            candidate.Length > 80 ||
            candidate.Contains("{", StringComparison.Ordinal) ||
            candidate.Contains("}", StringComparison.Ordinal) ||
            candidate.Contains(":", StringComparison.Ordinal))
        {
            return false;
        }

        translated = candidate;
        return true;
    }

    private static bool IsValidLabelTranslation(
        string translated,
        string sourceText,
        string targetLocale,
        out string reason)
    {
        reason = string.Empty;
        if (string.IsNullOrWhiteSpace(translated))
        {
            reason = "translated label is empty";
            return false;
        }

        if (LabelsMatch(translated, sourceText))
        {
            reason = "translated label echoed the source label";
            return false;
        }

        if (RequiresHangul(targetLocale) && !ContainsHangul(translated))
        {
            reason = "Korean label translation did not contain Hangul";
            return false;
        }

        if (RequiresJapaneseScript(targetLocale) && !ContainsJapaneseScript(translated))
        {
            reason = "Japanese label translation did not contain Japanese script";
            return false;
        }

        if (RequiresCjkScript(targetLocale) && !ContainsCjkUnifiedIdeograph(translated))
        {
            reason = "Chinese label translation did not contain CJK script";
            return false;
        }

        return true;
    }

    private static string ExtractJsonObject(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        int start = text.IndexOf('{');
        int end = text.LastIndexOf('}');
        if (start >= 0 && end > start)
        {
            return text.Substring(start, end - start + 1);
        }

        return text.Trim();
    }

    private static string NormalizeTranslatedLabel(string text)
    {
        string normalized = TrimWrappingQuotes(text?.Trim() ?? string.Empty);
        return normalized.Trim().TrimEnd('.', '。', '!', '?', ';', ':').Trim();
    }

    private static string TrimWrappingQuotes(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        string trimmed = text.Trim();
        if (trimmed.Length >= 2 &&
            ((trimmed[0] == '"' && trimmed[trimmed.Length - 1] == '"') ||
             (trimmed[0] == '\'' && trimmed[trimmed.Length - 1] == '\'')))
        {
            return trimmed.Substring(1, trimmed.Length - 2).Trim();
        }

        return trimmed;
    }

    private static string StripCodeFence(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        string trimmed = text.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        int firstLineEnd = trimmed.IndexOf('\n');
        int lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        if (firstLineEnd >= 0 && lastFence > firstLineEnd)
        {
            return trimmed.Substring(firstLineEnd + 1, lastFence - firstLineEnd - 1).Trim();
        }

        return trimmed.Trim('`').Trim();
    }

    private static bool LabelsMatch(string translated, string sourceText)
    {
        string normalizedTranslated = NormalizeForLabelComparison(translated);
        string normalizedSource = NormalizeForLabelComparison(sourceText);
        return !string.IsNullOrWhiteSpace(normalizedTranslated) &&
               string.Equals(normalizedTranslated, normalizedSource, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeForLabelComparison(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (char c in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    private static bool RequiresHangul(string locale)
    {
        return LocaleStartsWith(locale, "ko");
    }

    private static bool RequiresJapaneseScript(string locale)
    {
        return LocaleStartsWith(locale, "ja");
    }

    private static bool RequiresCjkScript(string locale)
    {
        return LocaleStartsWith(locale, "zh");
    }

    private static bool LocaleStartsWith(string locale, string language)
    {
        if (string.IsNullOrWhiteSpace(locale))
        {
            return false;
        }

        string normalized = locale.Trim().Replace('_', '-');
        return string.Equals(normalized, language, StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith($"{language}-", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsHangul(string text)
    {
        foreach (char c in text ?? string.Empty)
        {
            if ((c >= '\uac00' && c <= '\ud7af') ||
                (c >= '\u1100' && c <= '\u11ff') ||
                (c >= '\u3130' && c <= '\u318f'))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsJapaneseScript(string text)
    {
        foreach (char c in text ?? string.Empty)
        {
            if ((c >= '\u3040' && c <= '\u30ff') ||
                (c >= '\u4e00' && c <= '\u9fff'))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsCjkUnifiedIdeograph(string text)
    {
        foreach (char c in text ?? string.Empty)
        {
            if (c >= '\u4e00' && c <= '\u9fff')
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetCachedLabelTranslation(
        string sourceText,
        string targetLocale,
        out string translated)
    {
        return LabelTranslationCache.TryGetValue(BuildLabelCacheKey(sourceText, targetLocale), out translated) &&
               !string.IsNullOrWhiteSpace(translated);
    }

    private static void CacheLabelTranslation(string sourceText, string targetLocale, string translated)
    {
        if (string.IsNullOrWhiteSpace(sourceText) || string.IsNullOrWhiteSpace(translated))
        {
            return;
        }

        LabelTranslationCache[BuildLabelCacheKey(sourceText, targetLocale)] = translated.Trim();
    }

    private static string BuildLabelCacheKey(string sourceText, string targetLocale)
    {
        string locale = string.IsNullOrWhiteSpace(targetLocale)
            ? "target"
            : targetLocale.Trim().Replace('_', '-').ToLowerInvariant();
        return $"{locale}\n{NormalizeForLabelComparison(sourceText)}";
    }

    private static string GetParameter(
        IReadOnlyDictionary<string, string> parameters,
        string key,
        string fallback)
    {
        if (parameters == null ||
            string.IsNullOrWhiteSpace(key) ||
            !parameters.TryGetValue(key, out string value) ||
            string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return value.Trim();
    }

    private static int GetIntParameter(
        IReadOnlyDictionary<string, string> parameters,
        string key,
        int fallback)
    {
        if (parameters == null ||
            string.IsNullOrWhiteSpace(key) ||
            !parameters.TryGetValue(key, out string value) ||
            string.IsNullOrWhiteSpace(value) ||
            !int.TryParse(value.Trim(), out int parsed))
        {
            return fallback;
        }

        return parsed;
    }

#if UNITY_INCLUDE_TESTS || UNITY_EDITOR
    public static void ClearLabelTranslationCacheForTests()
    {
        LabelTranslationCache.Clear();
    }
#endif

    private static string BuildLogExcerpt(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "<empty>";
        }

        string trimmed = text.Trim().Replace("\r", "\\r").Replace("\n", "\\n");
        return trimmed.Length <= 500 ? trimmed : $"{trimmed.Substring(0, 500)}...";
    }
}
