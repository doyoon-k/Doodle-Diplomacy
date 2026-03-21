using System;
using System.Collections;
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

    public JSONLLMStateChainLink(
        LlmGenerationProfile settings,
        string userPromptTemplate,
        int maxRetries = 3,
        float delayBetweenRetries = 0.1f,
        bool useVision = false,
        string imageStateKey = null,
        bool requireImage = false,
        int resizeLongestSide = 1024,
        Action<string> log = null
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
            log)
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
        Action<string> log = null
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

            try
            {
                parsedJson = ExtractJsonObject(jsonResponse);
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

        return _userPromptTemplate.Render(state);
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
