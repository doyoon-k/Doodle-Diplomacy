#if UNITY_EDITOR
using System;
using System.Collections;
using LLama.Abstractions;
using UnityEngine;

/// <summary>
/// Editor ILlmService router that dispatches to local LLamaSharp or cloud direct adapters.
/// </summary>
public sealed class RoutingEditorLlmService : ILlmService, IDisposable
{
    private readonly LlamaSharpEditorService _localService;
    private readonly CloudDirectLlmService _cloudService;

    public RoutingEditorLlmService(bool logTraffic = false)
    {
        _localService = new LlamaSharpEditorService(logTraffic: logTraffic);
        _cloudService = new CloudDirectLlmService(logTraffic: logTraffic);
    }

    public ILLamaExecutor GetExecutor(BaseLlmGenerationProfile settings)
    {
        return ResolveTarget(settings).GetExecutor(settings);
    }

    public IEnumerator GenerateCompletion(
        BaseLlmGenerationProfile settings,
        string userPrompt,
        Action<string> onResponse)
    {
        yield return ResolveTarget(settings).GenerateCompletion(settings, userPrompt, onResponse);
    }

    public IEnumerator GenerateCompletionWithState(
        BaseLlmGenerationProfile settings,
        string userPrompt,
        PipelineState state,
        Action<string> onResponse)
    {
        yield return ResolveTarget(settings).GenerateCompletionWithState(settings, userPrompt, state, onResponse);
    }

    public IEnumerator GenerateCompletionWithImage(
        BaseLlmGenerationProfile settings,
        string userPrompt,
        PipelineState state,
        Texture2D image,
        Action<string> onResponse)
    {
        yield return ResolveTarget(settings).GenerateCompletionWithImage(settings, userPrompt, state, image, onResponse);
    }

    public IEnumerator ChatCompletion(
        BaseLlmGenerationProfile settings,
        ChatMessage[] messages,
        Action<string> onResponse)
    {
        yield return ResolveTarget(settings).ChatCompletion(settings, messages, onResponse);
    }

    public IEnumerator Embed(
        BaseLlmGenerationProfile settings,
        string[] inputs,
        Action<float[][]> onEmbeddings)
    {
        yield return ResolveTarget(settings).Embed(settings, inputs, onEmbeddings);
    }

    public void Dispose()
    {
        _localService?.Dispose();
        _cloudService?.Dispose();
    }

    private ILlmService ResolveTarget(BaseLlmGenerationProfile settings)
    {
        if (settings is CloudGenerationProfile)
        {
            return _cloudService;
        }

        return _localService;
    }
}
#endif
