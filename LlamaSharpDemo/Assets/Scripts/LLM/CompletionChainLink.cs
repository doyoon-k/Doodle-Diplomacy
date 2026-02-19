using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Executes a single completion style LLM call and stores the answer text into the shared state dictionary.
/// </summary>
public class CompletionChainLink : IStateChainLink
{
    private readonly LlmGenerationProfile _settings;
    private readonly PromptTemplate _userPromptTemplate;
    private readonly ILlmService _llmService;
    private readonly Action<string> _log;

    public CompletionChainLink(LlmGenerationProfile settings, string userPromptTemplate, Action<string> log = null)
        : this(LlmServiceLocator.Require(), settings, userPromptTemplate, log)
    {
    }

    public CompletionChainLink(
        ILlmService service,
        LlmGenerationProfile settings,
        string userPromptTemplate,
        Action<string> log = null)
    {
        _llmService = service;
        _log = log;
        _settings = settings;
        _userPromptTemplate = new PromptTemplate(userPromptTemplate ?? string.Empty);
    }

    public IEnumerator Execute(
        Dictionary<string, string> state,
        Action<Dictionary<string, string>> onDone
    )
    {
        state ??= new Dictionary<string, string>();
        if (_settings == null)
        {
            Debug.LogError("[CompletionChainLink] LlmGenerationProfile is missing.");
            onDone?.Invoke(state);
            yield break;
        }

        if (_llmService == null)
        {
            Debug.LogError("[CompletionChainLink] ILlmService is missing.");
            onDone?.Invoke(state);
            yield break;
        }

        string userPrompt = _userPromptTemplate.Render(state);
        string response = null;

        Log($"[CompletionChainLink] User Prompt:\n{userPrompt}");

        yield return _llmService.GenerateCompletionWithState(
            _settings,
            userPrompt,
            state,
            text => response = text);

        Log($"[CompletionChainLink] Raw Response:\n{response}");

        state[PromptPipelineConstants.AnswerKey] = response;
        onDone?.Invoke(state);
    }

    private void Log(string message)
    {
        _log?.Invoke(message);
        Debug.Log(message);
    }
}
