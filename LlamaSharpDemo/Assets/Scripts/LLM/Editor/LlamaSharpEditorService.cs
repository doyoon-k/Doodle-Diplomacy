#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using LLama;
using LLama.Abstractions;
using UnityEngine;

/// <summary>
/// Editor-safe ILlmService implementation backed by LLamaSharp.
/// </summary>
public sealed class LlamaSharpEditorService : ILlmService, IDisposable
{
    private readonly bool _logTraffic;
    private LLamaWeights _weights;
    private ILLamaExecutor _executor;
    private string _loadedModelPath;
    private int _loadedContextSize;
    private int _loadedGpuLayerCount;
    private int _loadedThreads;

    public LlamaSharpEditorService(bool logTraffic = false)
    {
        _logTraffic = logTraffic;
    }

    public ILLamaExecutor GetExecutor(LlmGenerationProfile settings)
    {
        if (settings == null)
        {
            Debug.LogError("[LlamaSharpEditorService] LlmGenerationProfile is missing.");
            return null;
        }

        string resolvedModelPath = LlamaSharpInterop.ResolveModelPath(settings);
        if (string.IsNullOrWhiteSpace(resolvedModelPath))
        {
            Debug.LogError("[LlamaSharpEditorService] Model path is empty.");
            return null;
        }

        if (!File.Exists(resolvedModelPath))
        {
            Debug.LogError($"[LlamaSharpEditorService] Model file not found: {resolvedModelPath}");
            return null;
        }

        var runtime = settings.runtimeParams ?? new LlmGenerationProfile.RuntimeParams();
        bool mustReload = _executor == null ||
                          !string.Equals(_loadedModelPath, resolvedModelPath, StringComparison.OrdinalIgnoreCase) ||
                          _loadedContextSize != runtime.contextSize ||
                          _loadedGpuLayerCount != runtime.gpuLayerCount ||
                          _loadedThreads != runtime.threads;

        if (mustReload && !TryLoadExecutor(settings, resolvedModelPath))
        {
            return null;
        }

        return _executor;
    }

    public IEnumerator GenerateCompletion(
        LlmGenerationProfile settings,
        string userPrompt,
        Action<string> onResponse)
    {
        yield return GenerateCompletionWithState(settings, userPrompt, null, onResponse);
    }

    public IEnumerator GenerateCompletionWithState(
        LlmGenerationProfile settings,
        string userPrompt,
        Dictionary<string, string> state,
        Action<string> onResponse)
    {
        var executor = GetExecutor(settings);
        if (executor == null)
        {
            onResponse?.Invoke(string.Empty);
            yield break;
        }

        string systemPrompt = LlamaSharpInterop.RenderSystemPrompt(settings, state);
        string prompt = LlamaSharpInterop.BuildUserPrompt(
            settings,
            userPrompt,
            requiresJson: !string.IsNullOrWhiteSpace(settings?.format),
            systemPrompt: systemPrompt);

        if (_logTraffic)
        {
            Debug.Log($"[LlamaSharpEditorService] System Prompt:\n{systemPrompt}\nUser Prompt:\n{prompt}");
        }

        yield return LlamaSharpInterop.InferToString(
            executor,
            prompt,
            LlamaSharpInterop.CreateInferenceParams(settings),
            onResponse);
    }

    public IEnumerator ChatCompletion(
        LlmGenerationProfile settings,
        ChatMessage[] messages,
        Action<string> onResponse)
    {
        string prompt = FlattenMessages(messages);
        yield return GenerateCompletion(settings, prompt, onResponse);
    }

    public IEnumerator Embed(
        LlmGenerationProfile settings,
        string[] inputs,
        Action<float[][]> onEmbeddings)
    {
        Debug.LogWarning("[LlamaSharpEditorService] Embedding is not implemented for LLamaSharp in this adapter.");
        onEmbeddings?.Invoke(Array.Empty<float[]>());
        yield break;
    }

    public void Dispose()
    {
        DisposeExecutor();
    }

    private bool TryLoadExecutor(LlmGenerationProfile settings, string resolvedModelPath)
    {
        DisposeExecutor();

        try
        {
            var modelParams = LlamaSharpInterop.CreateModelParams(settings, resolvedModelPath);
            _weights = LLamaWeights.LoadFromFile(modelParams);
            _executor = new StatelessExecutor(_weights, modelParams, null);

            var runtime = settings.runtimeParams ?? new LlmGenerationProfile.RuntimeParams();
            _loadedModelPath = resolvedModelPath;
            _loadedContextSize = runtime.contextSize;
            _loadedGpuLayerCount = runtime.gpuLayerCount;
            _loadedThreads = runtime.threads;

            if (_logTraffic)
            {
                Debug.Log($"[LlamaSharpEditorService] Loaded model: {_loadedModelPath}");
            }

            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[LlamaSharpEditorService] Failed to load model '{resolvedModelPath}': {ex}");
            DisposeExecutor();
            return false;
        }
    }

    private void DisposeExecutor()
    {
        if (_executor is IDisposable disposableExecutor)
        {
            disposableExecutor.Dispose();
        }

        _executor = null;

        if (_weights != null)
        {
            _weights.Dispose();
            _weights = null;
        }

        _loadedModelPath = null;
        _loadedContextSize = 0;
        _loadedGpuLayerCount = 0;
        _loadedThreads = 0;
    }

    private static string FlattenMessages(ChatMessage[] messages)
    {
        if (messages == null || messages.Length == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var message in messages)
        {
            if (string.IsNullOrWhiteSpace(message.content))
            {
                continue;
            }

            string role = string.IsNullOrWhiteSpace(message.role) ? "user" : message.role;
            builder.Append(role);
            builder.Append(": ");
            builder.AppendLine(message.content);
        }

        return builder.ToString();
    }
}
#endif

