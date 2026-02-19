using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.Json;
using UnityEngine;

/// <summary>
/// Executes an LLM call that is expected to return JSON and merges the parsed keys into the shared state dictionary.
/// </summary>
public class JSONLLMStateChainLink : IStateChainLink
{
    private readonly ILlmService _llmService;
    private readonly LlmGenerationProfile _settings;
    private readonly int _maxRetries;
    private readonly float _delayBetweenRetries;
    private readonly PromptTemplate _userPromptTemplate;
    private readonly Action<string> _log;

    public JSONLLMStateChainLink(
        LlmGenerationProfile settings,
        string userPromptTemplate,
        int maxRetries = 3,
        float delayBetweenRetries = 0.1f,
        Action<string> log = null
    )
        : this(LlmServiceLocator.Require(), settings, userPromptTemplate, maxRetries, delayBetweenRetries, log)
    {
    }

    public JSONLLMStateChainLink(
        ILlmService service,
        LlmGenerationProfile settings,
        string userPromptTemplate,
        int maxRetries = 3,
        float delayBetweenRetries = 0.1f,
        Action<string> log = null
    )
    {
        _llmService = service;
        _log = log;
        _settings = settings;
        _userPromptTemplate = new PromptTemplate(userPromptTemplate ?? string.Empty);
        _maxRetries = Mathf.Max(1, maxRetries);
        _delayBetweenRetries = Mathf.Max(0f, delayBetweenRetries);
    }

    public IEnumerator Execute(
        Dictionary<string, string> state,
        Action<Dictionary<string, string>> onDone
    )
    {
        state ??= new Dictionary<string, string>();
        if (_settings == null)
        {
            Debug.LogError("[JSONLLMStateChainLink] LlmGenerationProfile is missing.");
            onDone?.Invoke(state);
            yield break;
        }

        if (_llmService == null)
        {
            Debug.LogError("[JSONLLMStateChainLink] ILlmService is missing.");
            onDone?.Invoke(state);
            yield break;
        }

        int attempt = 0;
        bool parsedSuccessfully = false;
        string parsedJson = null;

        while (attempt < _maxRetries && !parsedSuccessfully)
        {
            attempt++;

            string userPrompt = RenderUserPrompt(state);
            Log($"[JSONLLMStateChainLink] Attempt {attempt}/{_maxRetries} - User Prompt:\n{userPrompt}");

            string jsonResponse = null;
            yield return _llmService.GenerateCompletionWithState(
                _settings,
                userPrompt,
                state,
                resp => jsonResponse = resp);

            Log($"[JSONLLMStateChainLink] Attempt {attempt}/{_maxRetries} - Raw Response:\n{jsonResponse}");

            try
            {
                parsedJson = ExtractJsonObject(jsonResponse);
                using var document = JsonDocument.Parse(parsedJson);
                parsedSuccessfully = document.RootElement.ValueKind == JsonValueKind.Object;
            }
            catch (Exception e)
            {
                Log($"[JSONLLMStateChainLink] JSON parse failed: {e.Message}");
                parsedJson = null;
            }

            if (!parsedSuccessfully && attempt < _maxRetries && _delayBetweenRetries > 0f)
            {
                yield return new WaitForSeconds(_delayBetweenRetries);
            }
        }

        if (parsedSuccessfully && !string.IsNullOrWhiteSpace(parsedJson))
        {
            using var document = JsonDocument.Parse(parsedJson);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                state[property.Name] = JsonElementToStateString(property.Value);
            }
        }

        onDone?.Invoke(state);
    }

    private string RenderUserPrompt(Dictionary<string, string> state)
    {
        if (_userPromptTemplate == null)
        {
            return string.Empty;
        }

        return _userPromptTemplate.Render(state);
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

    private void Log(string message)
    {
        _log?.Invoke(message);
        Debug.Log(message);
    }
}
