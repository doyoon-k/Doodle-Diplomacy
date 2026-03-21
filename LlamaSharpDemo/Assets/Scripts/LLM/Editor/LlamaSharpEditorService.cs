#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LLama;
using LLama.Abstractions;
using UnityEngine;

/// <summary>
/// Editor-safe ILlmService implementation backed by LLamaSharp.
/// </summary>
public sealed class LlamaSharpEditorService : ILlmService, IDisposable
{
    private readonly bool _logTraffic;
    private readonly object _visionLock = new object();
    private LLamaWeights _weights;
    private ILLamaExecutor _executor;
    private MtmdWeights _mtmdWeights;
    private string _loadedModelPath;
    private string _loadedVisionProjectorPath;
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
        PipelineState state,
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

    public IEnumerator GenerateCompletionWithImage(
        LlmGenerationProfile settings,
        string userPrompt,
        PipelineState state,
        Texture2D image,
        Action<string> onResponse)
    {
        if (image == null)
        {
            onResponse?.Invoke(string.Empty);
            yield break;
        }

        var executor = GetExecutor(settings);
        if (executor == null || _weights == null)
        {
            onResponse?.Invoke(string.Empty);
            yield break;
        }

        if (!TryEnsureVisionWeightsLoaded(settings, out string visionError))
        {
            Debug.LogError($"[LlamaSharpEditorService] {visionError}");
            onResponse?.Invoke(string.Empty);
            yield break;
        }

        if (!PipelineImageUtility.TryEncodeToPng(image, out byte[] pngBytes, out string encodeError))
        {
            Debug.LogError($"[LlamaSharpEditorService] {encodeError}");
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
            Debug.Log($"[LlamaSharpEditorService] System Prompt:\n{systemPrompt}\nUser Prompt:\n{prompt}\nImage: {image.name} ({image.width}x{image.height})");
        }

        string resolvedModelPath = LlamaSharpInterop.ResolveModelPath(settings);
        Task<string> inferenceTask = Task.Run(() => InferWithVision(settings, resolvedModelPath, prompt, pngBytes));
        while (!inferenceTask.IsCompleted)
        {
            yield return null;
        }

        Exception failure = GetTaskException(inferenceTask);
        if (failure != null)
        {
            Debug.LogError($"[LlamaSharpEditorService] Vision inference failed: {failure}");
            onResponse?.Invoke(string.Empty);
            yield break;
        }

        onResponse?.Invoke(inferenceTask.Result ?? string.Empty);
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

    private bool TryEnsureVisionWeightsLoaded(LlmGenerationProfile settings, out string error)
    {
        error = null;
        if (settings == null)
        {
            error = "Vision inference requires an LlmGenerationProfile.";
            return false;
        }

        string resolvedVisionPath = settings.ResolveVisionProjectorPath();
        if (string.IsNullOrWhiteSpace(resolvedVisionPath))
        {
            error = "Vision inference requires 'visionProjectorModel' on the LlmGenerationProfile.";
            return false;
        }

        if (!File.Exists(resolvedVisionPath))
        {
            error = $"Vision projector file not found: {resolvedVisionPath}";
            return false;
        }

        if (_weights == null)
        {
            error = "Text model weights are not loaded.";
            return false;
        }

        if (_mtmdWeights != null &&
            string.Equals(_loadedVisionProjectorPath, resolvedVisionPath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (_mtmdWeights is IDisposable disposableMtmd)
        {
            disposableMtmd.Dispose();
        }

        _mtmdWeights = null;

        try
        {
            _mtmdWeights = MtmdWeights.LoadFromFile(resolvedVisionPath, _weights, default);
            _loadedVisionProjectorPath = resolvedVisionPath;
            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to load vision projector '{resolvedVisionPath}': {ex.Message}";
            _mtmdWeights = null;
            _loadedVisionProjectorPath = null;
            return false;
        }
    }

    private string InferWithVision(
        LlmGenerationProfile settings,
        string resolvedModelPath,
        string prompt,
        byte[] imageBytes)
    {
        if (_weights == null)
        {
            throw new InvalidOperationException("Text model weights are not loaded.");
        }

        if (_mtmdWeights == null)
        {
            throw new InvalidOperationException("Vision projector weights are not loaded.");
        }

        var modelParams = LlamaSharpInterop.CreateModelParams(settings, resolvedModelPath);
        using var context = new LLamaContext(_weights, modelParams, null);
        var executor = new InteractiveExecutor(context, _mtmdWeights, null);

        lock (_visionLock)
        {
            _mtmdWeights.ClearMedia();
            _mtmdWeights.LoadMedia(imageBytes);
            return LlamaSharpInterop
                .InferToStringAsync(executor, prompt, LlamaSharpInterop.CreateInferenceParams(settings), CancellationToken.None)
                .GetAwaiter()
                .GetResult();
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

        if (_mtmdWeights is IDisposable disposableMtmd)
        {
            disposableMtmd.Dispose();
        }

        _mtmdWeights = null;

        _loadedModelPath = null;
        _loadedVisionProjectorPath = null;
        _loadedContextSize = 0;
        _loadedGpuLayerCount = 0;
        _loadedThreads = 0;
    }

    private static Exception GetTaskException(Task task)
    {
        if (task == null || !task.IsFaulted)
        {
            return null;
        }

        return task.Exception?.GetBaseException() ?? task.Exception;
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

