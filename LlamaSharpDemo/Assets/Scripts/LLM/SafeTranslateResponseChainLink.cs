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

    private readonly BaseLlmGenerationProfile _settings;
    private readonly string _sourceKey;
    private readonly string _outputKey;
    private readonly string _localeKey;
    private readonly string _languageKey;
    private readonly string _nativeLanguageKey;
    private readonly string _enabledKey;
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

        if (!IsTranslationEnabled(state))
        {
            Debug.Log($"[SafeTranslateResponseChainLink] Translation disabled by state key '{_enabledKey}'. Keeping source text.");
            SetOutput(state, sourceText);
            onDone?.Invoke(state);
            yield break;
        }

        string targetLocale = state.GetString(_localeKey, "en-US");
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
            SetOutput(state, sourceText);
            onDone?.Invoke(state);
            yield break;
        }

        ILlmService service = _service ?? LlmServiceLocator.Current;
        if (service == null)
        {
            Debug.LogWarning("[SafeTranslateResponseChainLink] ILlmService is missing. Keeping source text.");
            SetOutput(state, sourceText);
            onDone?.Invoke(state);
            yield break;
        }

        string targetLanguage = state.GetString(_languageKey, targetLocale);
        string nativeLanguage = state.GetString(_nativeLanguageKey, targetLanguage);
        string prompt = BuildTranslationPrompt(sourceText, targetLanguage, nativeLanguage, targetLocale);
        string rawResponse = null;
        Debug.Log(
            $"[SafeTranslateResponseChainLink] Translating state key '{_sourceKey}' to '{targetLocale}' " +
            $"with profile '{_settings.name}'.");

        yield return service.GenerateCompletionWithState(
            _settings,
            prompt,
            state,
            response => rawResponse = response);

        if (!TryReadTranslatedResponse(rawResponse, out string translated) ||
            string.IsNullOrWhiteSpace(translated))
        {
            Debug.LogWarning(
                "[SafeTranslateResponseChainLink] Translation response was not valid JSON. Keeping source text. " +
                $"Raw response: {BuildLogExcerpt(rawResponse)}");
            SetOutput(state, sourceText);
            onDone?.Invoke(state);
            yield break;
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

    private static bool TryReadTranslatedResponse(string rawResponse, out string translated)
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
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty(PromptPipelineConstants.AnswerKey, out JsonElement responseElement))
            {
                return false;
            }

            if (responseElement.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            translated = responseElement.GetString();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
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
