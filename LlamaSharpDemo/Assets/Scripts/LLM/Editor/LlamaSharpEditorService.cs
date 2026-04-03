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
    private const float DefaultInferenceTimeoutSeconds = 120f;
    private const float DefaultShutdownDrainTimeoutSeconds = 0.75f;

    private readonly bool _logTraffic;
    private readonly float _inferenceTimeoutSeconds;
    private readonly float _shutdownDrainTimeoutSeconds;
    private readonly object _operationLock = new object();
    private readonly object _visionLock = new object();
    private CancellationTokenSource _serviceLifetimeCts;
    private CancellationTokenSource _activeInferenceCts;
    private Task<string> _activeInferenceTask;
    private bool _disposed;
    private LLamaWeights _weights;
    private ILLamaExecutor _executor;
    private MtmdWeights _mtmdWeights;
    private string _loadedModelPath;
    private string _loadedVisionProjectorPath;
    private int _loadedContextSize;
    private int _loadedGpuLayerCount;
    private int _loadedThreads;

    public LlamaSharpEditorService(
        bool logTraffic = false,
        float inferenceTimeoutSeconds = DefaultInferenceTimeoutSeconds,
        float shutdownDrainTimeoutSeconds = DefaultShutdownDrainTimeoutSeconds)
    {
        _logTraffic = logTraffic;
        _inferenceTimeoutSeconds = Mathf.Max(0f, inferenceTimeoutSeconds);
        _shutdownDrainTimeoutSeconds = Mathf.Max(0f, shutdownDrainTimeoutSeconds);
        _serviceLifetimeCts = new CancellationTokenSource();
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
        if (_disposed)
        {
            onResponse?.Invoke(string.Empty);
            yield break;
        }

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

        CancellationTokenSource timeoutCts = CreateOperationCancellationSource(_inferenceTimeoutSeconds);
        Task<string> inferenceTask = Task.Run(() =>
            LlamaSharpInterop.InferToStringAsync(
                executor,
                prompt,
                LlamaSharpInterop.CreateInferenceParams(settings),
                timeoutCts.Token),
            timeoutCts.Token);
        RegisterActiveInference(inferenceTask, timeoutCts);

        yield return WaitForInferenceTask(
            inferenceTask,
            timeoutCts,
            _inferenceTimeoutSeconds,
            "editor text inference",
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

        if (_disposed)
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
            systemPrompt: systemPrompt,
            includeVisionMarker: true);

        if (_logTraffic)
        {
            Debug.Log($"[LlamaSharpEditorService] System Prompt:\n{systemPrompt}\nUser Prompt:\n{prompt}\nImage: {image.name} ({image.width}x{image.height})");
        }

        string resolvedModelPath = LlamaSharpInterop.ResolveModelPath(settings);
        CancellationTokenSource timeoutCts = CreateOperationCancellationSource(_inferenceTimeoutSeconds);
        Task<string> inferenceTask = Task.Run(
            () => InferWithVision(settings, resolvedModelPath, prompt, pngBytes, timeoutCts.Token),
            timeoutCts.Token);
        RegisterActiveInference(inferenceTask, timeoutCts);

        yield return WaitForInferenceTask(
            inferenceTask,
            timeoutCts,
            _inferenceTimeoutSeconds,
            "editor vision inference",
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
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CancelActiveOperations();

        if (!TryDrainActiveOperations(_shutdownDrainTimeoutSeconds))
        {
            Debug.LogWarning("[LlamaSharpEditorService] Dispose skipped immediate LLama resource disposal because editor tasks were still active.");
            DisposeLifetimeCancellationSource();
            return;
        }

        ClearCompletedOperationReferences();
        DisposeExecutor();
        DisposeLifetimeCancellationSource();
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
            _mtmdWeights = MtmdWeights.LoadFromFile(
                resolvedVisionPath,
                _weights,
                LlamaSharpInterop.CreateMtmdContextParams(settings));
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
        byte[] imageBytes,
        CancellationToken cancellationToken)
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
                .InferToStringAsync(executor, prompt, LlamaSharpInterop.CreateInferenceParams(settings), cancellationToken)
                .GetAwaiter()
                .GetResult();
        }
    }

    public void CancelActiveOperations()
    {
        CancellationTokenSource lifetimeCts;
        CancellationTokenSource inferenceCts;

        lock (_operationLock)
        {
            lifetimeCts = _serviceLifetimeCts;
            inferenceCts = _activeInferenceCts;
        }

        TryCancel(inferenceCts);
        TryCancel(lifetimeCts);
    }

    private void RegisterActiveInference(Task<string> inferenceTask, CancellationTokenSource cancellationSource)
    {
        lock (_operationLock)
        {
            _activeInferenceTask = inferenceTask;
            _activeInferenceCts = cancellationSource;
        }

        inferenceTask.ContinueWith(
            _ => ReleaseActiveInference(inferenceTask, cancellationSource),
            CancellationToken.None,
            TaskContinuationOptions.None,
            TaskScheduler.Default);
    }

    private void ReleaseActiveInference(Task<string> inferenceTask, CancellationTokenSource cancellationSource)
    {
        bool shouldDisposeCts = false;

        lock (_operationLock)
        {
            if (ReferenceEquals(_activeInferenceTask, inferenceTask))
            {
                _activeInferenceTask = null;
            }

            if (ReferenceEquals(_activeInferenceCts, cancellationSource))
            {
                _activeInferenceCts = null;
                shouldDisposeCts = true;
            }
        }

        if (shouldDisposeCts)
        {
            cancellationSource.Dispose();
        }
    }

    private IEnumerator WaitForInferenceTask(
        Task<string> inferenceTask,
        CancellationTokenSource timeoutCts,
        float timeoutSeconds,
        string operationName,
        Action<string> onResponse)
    {
        float startTime = Time.realtimeSinceStartup;
        while (!inferenceTask.IsCompleted)
        {
            if (_disposed)
            {
                timeoutCts?.Cancel();
                onResponse?.Invoke(string.Empty);
                yield break;
            }

            if (timeoutSeconds > 0f &&
                Time.realtimeSinceStartup - startTime >= timeoutSeconds)
            {
                timeoutCts?.Cancel();
                Debug.LogError($"[LlamaSharpEditorService] {operationName} timed out after {timeoutSeconds:0.#} seconds.");
                onResponse?.Invoke(string.Empty);
                yield break;
            }

            yield return null;
        }

        Exception failure = GetTaskException(inferenceTask);
        if (failure != null)
        {
            bool wasCancelled = failure is OperationCanceledException || timeoutCts?.IsCancellationRequested == true;
            string failureMessage = wasCancelled
                ? $"{operationName} was cancelled."
                : $"{operationName} failed: {failure}";
            Debug.LogError($"[LlamaSharpEditorService] {failureMessage}");
            onResponse?.Invoke(string.Empty);
            yield break;
        }

        onResponse?.Invoke(inferenceTask.Result ?? string.Empty);
    }

    private CancellationTokenSource CreateOperationCancellationSource(float timeoutSeconds)
    {
        CancellationTokenSource cancellationSource = _serviceLifetimeCts != null
            ? CancellationTokenSource.CreateLinkedTokenSource(_serviceLifetimeCts.Token)
            : new CancellationTokenSource();

        if (timeoutSeconds > 0f)
        {
            cancellationSource.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        }

        return cancellationSource;
    }

    private bool TryDrainActiveOperations(float timeoutSeconds)
    {
        if (timeoutSeconds <= 0f)
        {
            return !HasRunningOperations();
        }

        DateTime deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            if (!HasRunningOperations())
            {
                return true;
            }

            Thread.Sleep(25);
        }

        return !HasRunningOperations();
    }

    private bool HasRunningOperations()
    {
        lock (_operationLock)
        {
            return _activeInferenceTask != null && !_activeInferenceTask.IsCompleted;
        }
    }

    private void ClearCompletedOperationReferences()
    {
        CancellationTokenSource inferenceCts = null;

        lock (_operationLock)
        {
            if (_activeInferenceTask == null || _activeInferenceTask.IsCompleted)
            {
                _activeInferenceTask = null;
                inferenceCts = _activeInferenceCts;
                _activeInferenceCts = null;
            }
        }

        inferenceCts?.Dispose();
    }

    private void DisposeLifetimeCancellationSource()
    {
        CancellationTokenSource lifetimeCts;

        lock (_operationLock)
        {
            lifetimeCts = _serviceLifetimeCts;
            _serviceLifetimeCts = null;
        }

        lifetimeCts?.Dispose();
    }

    private static void TryCancel(CancellationTokenSource cancellationSource)
    {
        try
        {
            if (cancellationSource != null && !cancellationSource.IsCancellationRequested)
            {
                cancellationSource.Cancel();
            }
        }
        catch (ObjectDisposedException)
        {
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

