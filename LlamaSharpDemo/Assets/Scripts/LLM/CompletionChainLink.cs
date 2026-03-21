using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Executes a single completion style LLM call and stores the answer text into the shared state.
/// </summary>
public class CompletionChainLink : IStateChainLink
{
    private readonly LlmGenerationProfile _settings;
    private readonly PromptTemplate _userPromptTemplate;
    private readonly ILlmService _llmService;
    private readonly Action<string> _log;
    private readonly bool _useVision;
    private readonly string _imageStateKey;
    private readonly bool _requireImage;
    private readonly int _resizeLongestSide;

    public CompletionChainLink(
        LlmGenerationProfile settings,
        string userPromptTemplate,
        bool useVision = false,
        string imageStateKey = null,
        bool requireImage = false,
        int resizeLongestSide = 1024,
        Action<string> log = null)
        : this(
            LlmServiceLocator.Require(),
            settings,
            userPromptTemplate,
            useVision,
            imageStateKey,
            requireImage,
            resizeLongestSide,
            log)
    {
    }

    public CompletionChainLink(
        ILlmService service,
        LlmGenerationProfile settings,
        string userPromptTemplate,
        bool useVision = false,
        string imageStateKey = null,
        bool requireImage = false,
        int resizeLongestSide = 1024,
        Action<string> log = null)
    {
        _llmService = service;
        _log = log;
        _settings = settings;
        _userPromptTemplate = new PromptTemplate(userPromptTemplate ?? string.Empty);
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
            yield return Fail(state, onDone, "[CompletionChainLink] LlmGenerationProfile is missing.");
            yield break;
        }

        if (_llmService == null)
        {
            yield return Fail(state, onDone, "[CompletionChainLink] ILlmService is missing.");
            yield break;
        }

        string userPrompt = _userPromptTemplate.Render(state);
        string response = null;

        Log($"[CompletionChainLink] User Prompt:\n{userPrompt}");

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
                text => response = text);
        }
        else
        {
            yield return _llmService.GenerateCompletionWithState(
                _settings,
                userPrompt,
                state,
                text => response = text);
        }

        Log($"[CompletionChainLink] Raw Response:\n{response}");

        if (string.IsNullOrWhiteSpace(response))
        {
            yield return Fail(state, onDone, "[CompletionChainLink] Model returned an empty response.");
            yield break;
        }

        state.SetString(PromptPipelineConstants.AnswerKey, response);
        onDone?.Invoke(state);
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
            error = "[CompletionChainLink] Vision is enabled but imageStateKey is empty.";
            return null;
        }

        if (!state.HasAnyValue(_imageStateKey))
        {
            if (_requireImage)
            {
                error = $"[CompletionChainLink] Required image key '{_imageStateKey}' is missing.";
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
            error = $"[CompletionChainLink] {normalizationError}";
            return null;
        }

        return result;
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
