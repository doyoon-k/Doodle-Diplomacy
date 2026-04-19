using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using UnityEngine;

/// <summary>
/// Executes an LLM call that is expected to return JSON and merges parsed keys into the shared state.
/// </summary>
public class JSONLLMStateChainLink : IStateChainLink
{
    private readonly ILlmService _llmService;
    private readonly LlmGenerationProfile _settings;
    private readonly int _maxRetries;
    private readonly float _delayBetweenRetries;
    private readonly PromptTemplate _userPromptTemplate;
    private readonly Action<string> _log;
    private readonly bool _useVision;
    private readonly string _imageStateKey;
    private readonly bool _requireImage;
    private readonly int _resizeLongestSide;
    private readonly string _thinkingStateKey;

    public JSONLLMStateChainLink(
        LlmGenerationProfile settings,
        string userPromptTemplate,
        int maxRetries = 3,
        float delayBetweenRetries = 0.1f,
        bool useVision = false,
        string imageStateKey = null,
        bool requireImage = false,
        int resizeLongestSide = 1024,
        Action<string> log = null,
        string stepName = null
    )
        : this(
            LlmServiceLocator.Require(),
            settings,
            userPromptTemplate,
            maxRetries,
            delayBetweenRetries,
            useVision,
            imageStateKey,
            requireImage,
            resizeLongestSide,
            log,
            stepName)
    {
    }

    public JSONLLMStateChainLink(
        ILlmService service,
        LlmGenerationProfile settings,
        string userPromptTemplate,
        int maxRetries = 3,
        float delayBetweenRetries = 0.1f,
        bool useVision = false,
        string imageStateKey = null,
        bool requireImage = false,
        int resizeLongestSide = 1024,
        Action<string> log = null,
        string stepName = null
    )
    {
        _llmService = service;
        _log = log;
        _settings = settings;
        _userPromptTemplate = new PromptTemplate(userPromptTemplate ?? string.Empty);
        _maxRetries = Mathf.Max(1, maxRetries);
        _delayBetweenRetries = Mathf.Max(0f, delayBetweenRetries);
        _useVision = useVision;
        _imageStateKey = imageStateKey;
        _requireImage = requireImage;
        _resizeLongestSide = Mathf.Max(64, resizeLongestSide);
        _thinkingStateKey = BuildThinkingStateKey(stepName);
    }

    public IEnumerator Execute(
        PipelineState state,
        Action<PipelineState> onDone
    )
    {
        state ??= new PipelineState();
        if (_settings == null)
        {
            yield return Fail(state, onDone, "[JSONLLMStateChainLink] LlmGenerationProfile is missing.");
            yield break;
        }

        if (_llmService == null)
        {
            yield return Fail(state, onDone, "[JSONLLMStateChainLink] ILlmService is missing.");
            yield break;
        }

        int attempt = 0;
        bool parsedSuccessfully = false;
        string parsedJson = null;
        string lastError = null;

        while (attempt < _maxRetries && !parsedSuccessfully)
        {
            attempt++;

            string userPrompt = RenderUserPrompt(state);
            Log($"[JSONLLMStateChainLink] Attempt {attempt}/{_maxRetries} - User Prompt:\n{userPrompt}");

            string jsonResponse = null;
            using var imageResult = ResolveImage(state, out string imageError);
            if (imageError != null)
            {
                yield return Fail(state, onDone, imageError);
                yield break;
            }

            if (imageResult != null)
            {
                yield return _llmService.GenerateCompletionWithImage(
                    _settings,
                    userPrompt,
                    state,
                    imageResult.Texture,
                    resp => jsonResponse = resp);
            }
            else
            {
                yield return _llmService.GenerateCompletionWithState(
                    _settings,
                    userPrompt,
                    state,
                    resp => jsonResponse = resp);
            }

            Log($"[JSONLLMStateChainLink] Attempt {attempt}/{_maxRetries} - Raw Response:\n{jsonResponse}");

            if (string.IsNullOrWhiteSpace(jsonResponse))
            {
                lastError = "LLM returned an empty response. Check earlier model or vision loading errors in the console.";
                Log($"[JSONLLMStateChainLink] {lastError}");

                if (attempt < _maxRetries && _delayBetweenRetries > 0f)
                {
                    yield return new WaitForSeconds(_delayBetweenRetries);
                }

                continue;
            }

            try
            {
                string responseForParsing = jsonResponse;
                if (_settings.IsThinkingEnabled)
                {
                    ExtractThinkBlock(jsonResponse, out string thinkContent, out string remainder);
                    if (_settings.thinkingMode == ThinkingMode.PreserveThink && thinkContent != null)
                    {
                        state.SetString(_thinkingStateKey, thinkContent);
                    }

                    responseForParsing = remainder;
                }

                parsedJson = ExtractJsonObject(responseForParsing);
                using var document = JsonDocument.Parse(parsedJson);
                parsedSuccessfully = document.RootElement.ValueKind == JsonValueKind.Object;
                if (!parsedSuccessfully)
                {
                    lastError = "Root JSON value is not an object.";
                }
            }
            catch (Exception e)
            {
                lastError = e.Message;
                Log($"[JSONLLMStateChainLink] JSON parse failed: {e.Message}");
                parsedJson = null;

                string responseForRecovery = jsonResponse;
                if (_settings.IsThinkingEnabled)
                {
                    ExtractThinkBlock(jsonResponse, out _, out responseForRecovery);
                }

                if (TryRecoverSingleStringFieldJson(responseForRecovery, out string recoveredJson, out string recoveryMessage))
                {
                    parsedJson = recoveredJson;
                    parsedSuccessfully = true;
                    lastError = null;
                    Log($"[JSONLLMStateChainLink] Recovered malformed JSON response. {recoveryMessage}");
                }
            }

            if (!parsedSuccessfully && attempt < _maxRetries && _delayBetweenRetries > 0f)
            {
                yield return new WaitForSeconds(_delayBetweenRetries);
            }
        }

        if (!parsedSuccessfully || string.IsNullOrWhiteSpace(parsedJson))
        {
            yield return Fail(
                state,
                onDone,
                $"[JSONLLMStateChainLink] Failed to parse valid JSON after {_maxRetries} attempts. Last error: {lastError}");
            yield break;
        }

        using (var document = JsonDocument.Parse(parsedJson))
        {
            foreach (var property in document.RootElement.EnumerateObject())
            {
                state.SetString(property.Name, JsonElementToStateString(property.Value));
            }
        }

        onDone?.Invoke(state);
    }

    private string RenderUserPrompt(PipelineState state)
    {
        if (_userPromptTemplate == null)
        {
            return string.Empty;
        }

        string prompt = _userPromptTemplate.Render(state);
        if (_settings != null && _settings.IsThinkingEnabled)
        {
            return $"/think\n{prompt}";
        }

        return prompt;
    }

    private PipelineImageUtility.ImageNormalizationResult ResolveImage(PipelineState state, out string error)
    {
        error = null;
        if (!_useVision)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(_imageStateKey))
        {
            error = "[JSONLLMStateChainLink] Vision is enabled but imageStateKey is empty.";
            return null;
        }

        if (!state.HasAnyValue(_imageStateKey))
        {
            if (_requireImage)
            {
                error = $"[JSONLLMStateChainLink] Required image key '{_imageStateKey}' is missing.";
            }

            return null;
        }

        if (!PipelineImageUtility.TryResolveFromState(
                state,
                _imageStateKey,
                _resizeLongestSide,
                out PipelineImageUtility.ImageNormalizationResult result,
                out string normalizationError))
        {
            error = $"[JSONLLMStateChainLink] {normalizationError}";
            return null;
        }

        return result;
    }

    private static string ExtractJsonObject(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        int start = text.IndexOf('{');
        int end = text.LastIndexOf('}');
        if (start >= 0 && end > start)
        {
            return text.Substring(start, end - start + 1);
        }

        return text;
    }

    private static void ExtractThinkBlock(string response, out string thinkContent, out string remainder)
    {
        thinkContent = null;
        remainder = response;

        if (string.IsNullOrEmpty(response))
        {
            return;
        }

        const string startTag = "<think>";
        const string endTag = "</think>";

        int startIndex = response.IndexOf(startTag, StringComparison.OrdinalIgnoreCase);
        if (startIndex < 0)
        {
            return;
        }

        int endTagIndex = response.LastIndexOf(endTag, StringComparison.OrdinalIgnoreCase);
        if (endTagIndex < 0 || endTagIndex < startIndex)
        {
            return;
        }

        int endIndexExclusive = endTagIndex + endTag.Length;
        thinkContent = response.Substring(startIndex, endIndexExclusive - startIndex);
        remainder = endIndexExclusive < response.Length
            ? response.Substring(endIndexExclusive).Trim()
            : string.Empty;
    }

    private static string JsonElementToStateString(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.Null => string.Empty,
            JsonValueKind.Object => element.GetRawText(),
            JsonValueKind.Array => element.GetRawText(),
            _ => element.ToString()
        };
    }

    private bool TryRecoverSingleStringFieldJson(string rawResponse, out string recoveredJson, out string message)
    {
        recoveredJson = null;
        message = null;

        if (!TryGetSingleTopLevelStringFieldName(out string fieldName))
        {
            return false;
        }

        string summary = ExtractLikelySummaryText(rawResponse, fieldName);
        if (string.IsNullOrWhiteSpace(summary))
        {
            return false;
        }

        recoveredJson = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            [fieldName] = summary
        });

        message = $"Filled '{fieldName}' with extracted summary text.";
        return true;
    }

    private bool TryGetSingleTopLevelStringFieldName(out string fieldName)
    {
        fieldName = null;
        IReadOnlyList<JsonFieldDefinition> fields = _settings?.JsonFields;
        if (fields == null)
        {
            return false;
        }

        int validFieldCount = 0;
        for (int i = 0; i < fields.Count; i++)
        {
            JsonFieldDefinition field = fields[i];
            if (field == null || string.IsNullOrWhiteSpace(field.fieldName))
            {
                continue;
            }

            validFieldCount++;
            if (validFieldCount > 1 || field.fieldType != JsonFieldType.String)
            {
                fieldName = null;
                return false;
            }

            fieldName = field.fieldName.Trim();
        }

        return validFieldCount == 1 && !string.IsNullOrWhiteSpace(fieldName);
    }

    private static string ExtractLikelySummaryText(string rawResponse, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(rawResponse))
        {
            return null;
        }

        string normalized = rawResponse.Replace("\r\n", "\n").Replace('\r', '\n');
        int imageMarkerIndex = normalized.LastIndexOf("[IMAGE]", StringComparison.OrdinalIgnoreCase);
        if (imageMarkerIndex >= 0)
        {
            normalized = normalized.Substring(imageMarkerIndex + "[IMAGE]".Length);
        }

        normalized = normalized
            .Replace("```json", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("```", string.Empty, StringComparison.Ordinal);

        var builder = new StringBuilder(normalized.Length);
        string[] lines = normalized.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string cleanedLine = CleanRecoveryLine(lines[i], fieldName);
            if (string.IsNullOrWhiteSpace(cleanedLine))
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(cleanedLine);
        }

        return NormalizeWhitespace(builder.ToString());
    }

    private static string CleanRecoveryLine(string line, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        string trimmed = line.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return null;
        }

        if (string.Equals(trimmed, "[IMAGE]", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "image.png", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "json", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "<__media__>", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (trimmed.StartsWith($"\"{fieldName}\"", StringComparison.OrdinalIgnoreCase))
        {
            int colonIndex = trimmed.IndexOf(':');
            if (colonIndex >= 0 && colonIndex + 1 < trimmed.Length)
            {
                trimmed = trimmed.Substring(colonIndex + 1).Trim();
            }
        }

        trimmed = trimmed.Trim(' ', '"', '{', '}', ',');
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return null;
        }

        if (string.Equals(trimmed, fieldName, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return trimmed;
    }

    private static string NormalizeWhitespace(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var builder = new StringBuilder(text.Length);
        bool previousWasWhitespace = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (char.IsWhiteSpace(c))
            {
                if (!previousWasWhitespace)
                {
                    builder.Append(' ');
                    previousWasWhitespace = true;
                }

                continue;
            }

            builder.Append(c);
            previousWasWhitespace = false;
        }

        return builder.ToString().Trim();
    }

    private static string BuildThinkingStateKey(string stepName)
    {
        return string.IsNullOrWhiteSpace(stepName)
            ? "thinking"
            : $"{stepName.Trim()}_thinking";
    }

    private IEnumerator Fail(PipelineState state, Action<PipelineState> onDone, string error)
    {
        Log(error);
        state.SetString(PromptPipelineConstants.ErrorKey, error);
        onDone?.Invoke(state);
        yield break;
    }

    private void Log(string message)
    {
        _log?.Invoke(message);
        Debug.Log(message);
    }
}
