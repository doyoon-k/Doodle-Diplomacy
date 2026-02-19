using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using LLama;
using LLama.Abstractions;
using LLama.Common;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Runtime ILlmService implementation backed by LLamaSharp.
/// </summary>
public class RuntimeLlamaSharpService : MonoBehaviour, ILlmService
{
    [Serializable]
    public sealed class PreloadBoolUnityEvent : UnityEvent<bool> { }

    [Header("Runtime LLM Settings")]
    [SerializeField] private LlmGenerationProfile defaultProfile;
    [SerializeField] private bool registerOnAwake = true;
    [SerializeField] private bool persistAcrossScenes = true;

    [Header("Preload Events")]
    [SerializeField] private UnityEvent onPreloadSucceeded;
    [SerializeField] private UnityEvent onPreloadFailed;
    [SerializeField] private PreloadBoolUnityEvent onPreloadCompleted;

    [Tooltip("Log prompts and responses.")]
    public bool logTraffic;

    [Tooltip("Use explicit native bootstrap logic. Disable to rely on Unity plugin loading only.")]
    [SerializeField] private bool useNativeBootstrap;

    public bool IsPreloadInProgress { get; private set; }
    public bool IsPreloadComplete { get; private set; }
    public bool IsModelReady => _executor != null;

    /// <summary>
    /// Invoked when default profile preload ends. bool = success.
    /// </summary>
    public event Action<bool> PreloadCompleted;

    private LLamaWeights _weights;
    private ILLamaExecutor _executor;
    private string _loadedModelPath;
    private int _loadedContextSize;
    private int _loadedGpuLayerCount;
    private int _loadedThreads;
    private bool _preloadStarted;

    private sealed class ExecutorLoadResult
    {
        public LLamaWeights weights;
        public ILLamaExecutor executor;
        public Exception error;
    }

    private void Awake()
    {
        if (persistAcrossScenes)
        {
            DontDestroyOnLoad(gameObject);
        }

        if (registerOnAwake)
        {
            LlmServiceLocator.Register(this);
        }

        StartPreload();
    }

    private void OnDestroy()
    {
        if (registerOnAwake)
        {
            LlmServiceLocator.Unregister(this);
        }

        DisposeExecutor();
    }

    public ILLamaExecutor GetExecutor(LlmGenerationProfile settings)
    {
        settings ??= defaultProfile;
        if (settings == null)
        {
            Debug.LogError("[RuntimeLlamaSharpService] LlmGenerationProfile is missing.");
            return null;
        }

        string resolvedModelPath = LlamaSharpInterop.ResolveModelPath(settings);
        if (string.IsNullOrWhiteSpace(resolvedModelPath))
        {
            Debug.LogError("[RuntimeLlamaSharpService] Model path is empty.");
            return null;
        }

        if (!File.Exists(resolvedModelPath))
        {
            Debug.LogError($"[RuntimeLlamaSharpService] Model file not found: {resolvedModelPath}");
            return null;
        }

        var runtime = settings.runtimeParams ?? new LlmGenerationProfile.RuntimeParams();
        int desiredThreads = LlamaSharpInterop.ResolveThreadCount(runtime.threads);
        bool mustReload = _executor == null ||
                          !string.Equals(_loadedModelPath, resolvedModelPath, StringComparison.OrdinalIgnoreCase) ||
                          _loadedContextSize != runtime.contextSize ||
                          _loadedGpuLayerCount != runtime.gpuLayerCount ||
                          _loadedThreads != desiredThreads;

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
        ILLamaExecutor executor = null;
        yield return EnsureExecutorLoaded(settings, exec => executor = exec);
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

        if (logTraffic)
        {
            Debug.Log($"[RuntimeLlamaSharpService] System Prompt:\n{systemPrompt}\nUser Prompt:\n{prompt}");
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
        Debug.LogWarning("[RuntimeLlamaSharpService] Embedding is not implemented for LLamaSharp in this adapter.");
        onEmbeddings?.Invoke(Array.Empty<float[]>());
        yield break;
    }

    public void StartPreload()
    {
        if (_preloadStarted)
        {
            return;
        }

        _preloadStarted = true;

        if (defaultProfile == null)
        {
            IsPreloadInProgress = false;
            IsPreloadComplete = true;
            NotifyPreloadCompleted(false);
            return;
        }

        StartCoroutine(PreloadDefaultProfileRoutine());
    }

    public IEnumerator WaitForPreload()
    {
        while (!IsPreloadComplete)
        {
            yield return null;
        }
    }

    private IEnumerator PreloadDefaultProfileRoutine()
    {
        IsPreloadInProgress = true;
        bool success = false;
        yield return EnsureExecutorLoaded(defaultProfile, exec => success = exec != null);
        IsPreloadInProgress = false;
        IsPreloadComplete = true;
        NotifyPreloadCompleted(success);
    }

    private void NotifyPreloadCompleted(bool success)
    {
        PreloadCompleted?.Invoke(success);
        onPreloadCompleted?.Invoke(success);

        if (success)
        {
            onPreloadSucceeded?.Invoke();
        }
        else
        {
            onPreloadFailed?.Invoke();
        }
    }

    private IEnumerator EnsureExecutorLoaded(LlmGenerationProfile settings, Action<ILLamaExecutor> onReady)
    {
        settings ??= defaultProfile;
        if (settings == null)
        {
            Debug.LogError("[RuntimeLlamaSharpService] LlmGenerationProfile is missing.");
            onReady?.Invoke(null);
            yield break;
        }

        string resolvedModelPath = LlamaSharpInterop.ResolveModelPath(settings);
        if (string.IsNullOrWhiteSpace(resolvedModelPath))
        {
            Debug.LogError("[RuntimeLlamaSharpService] Model path is empty.");
            onReady?.Invoke(null);
            yield break;
        }

        if (!File.Exists(resolvedModelPath))
        {
            Debug.LogError($"[RuntimeLlamaSharpService] Model file not found: {resolvedModelPath}");
            onReady?.Invoke(null);
            yield break;
        }

        var runtime = settings.runtimeParams ?? new LlmGenerationProfile.RuntimeParams();
        int desiredThreads = LlamaSharpInterop.ResolveThreadCount(runtime.threads);
        bool mustReload = _executor == null ||
                          !string.Equals(_loadedModelPath, resolvedModelPath, StringComparison.OrdinalIgnoreCase) ||
                          _loadedContextSize != runtime.contextSize ||
                          _loadedGpuLayerCount != runtime.gpuLayerCount ||
                          _loadedThreads != desiredThreads;

        if (!mustReload)
        {
            onReady?.Invoke(_executor);
            yield break;
        }

        int contextSize = Math.Max(128, runtime.contextSize);
        int gpuLayerCount = Math.Max(0, runtime.gpuLayerCount);
        int threads = desiredThreads;

        Task<ExecutorLoadResult> loadTask = Task.Run(() =>
            RunWithOptionalNativeBootstrap(() =>
                LoadExecutorOnWorker(resolvedModelPath, contextSize, gpuLayerCount, threads)));

        while (!loadTask.IsCompleted)
        {
            yield return null;
        }

        Exception taskException = GetTaskException(loadTask);
        if (taskException != null)
        {
            Debug.LogError($"[RuntimeLlamaSharpService] Model load task failed: {taskException}");
            onReady?.Invoke(null);
            yield break;
        }

        ExecutorLoadResult result = loadTask.Result;
        if (result == null || result.error != null || result.executor == null || result.weights == null)
        {
            if (result?.executor is IDisposable disposableExecutor)
            {
                disposableExecutor.Dispose();
            }

            result?.weights?.Dispose();
            Debug.LogError($"[RuntimeLlamaSharpService] Failed to load model '{resolvedModelPath}': {result?.error}");
            onReady?.Invoke(null);
            yield break;
        }

        DisposeExecutor();
        _weights = result.weights;
        _executor = result.executor;
        _loadedModelPath = resolvedModelPath;
        _loadedContextSize = runtime.contextSize;
        _loadedGpuLayerCount = runtime.gpuLayerCount;
        _loadedThreads = threads;

        if (logTraffic)
        {
            Debug.Log($"[RuntimeLlamaSharpService] Loaded model: {_loadedModelPath}");
        }

        onReady?.Invoke(_executor);
    }

    private static ExecutorLoadResult LoadExecutorOnWorker(
        string modelPath,
        int contextSize,
        int gpuLayerCount,
        int threads)
    {
        var result = new ExecutorLoadResult();
        try
        {
            var modelParams = new ModelParams(modelPath)
            {
                ContextSize = (uint)contextSize,
                GpuLayerCount = gpuLayerCount
            };

            modelParams.Threads = threads;
            modelParams.BatchThreads = LlamaSharpInterop.ResolveBatchThreadCount(threads);

            result.weights = LLamaWeights.LoadFromFile(modelParams);
            result.executor = new StatelessExecutor(result.weights, modelParams, null);
            return result;
        }
        catch (Exception ex)
        {
            result.error = ex;
            return result;
        }
    }

    private bool TryLoadExecutor(LlmGenerationProfile settings, string resolvedModelPath)
    {
        DisposeExecutor();

        try
        {
            RunWithOptionalNativeBootstrap(() =>
            {
                var modelParams = LlamaSharpInterop.CreateModelParams(settings, resolvedModelPath);
                _weights = LLamaWeights.LoadFromFile(modelParams);
                _executor = new StatelessExecutor(_weights, modelParams, null);
                return true;
            });

            var runtime = settings.runtimeParams ?? new LlmGenerationProfile.RuntimeParams();
            int desiredThreads = LlamaSharpInterop.ResolveThreadCount(runtime.threads);
            _loadedModelPath = resolvedModelPath;
            _loadedContextSize = runtime.contextSize;
            _loadedGpuLayerCount = runtime.gpuLayerCount;
            _loadedThreads = desiredThreads;

            if (logTraffic)
            {
                Debug.Log($"[RuntimeLlamaSharpService] Loaded model: {_loadedModelPath}");
            }

            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[RuntimeLlamaSharpService] Failed to load model '{resolvedModelPath}': {ex}");
            DisposeExecutor();
            return false;
        }
    }

    private T RunWithOptionalNativeBootstrap<T>(Func<T> action)
    {
        if (action == null)
        {
            throw new ArgumentNullException(nameof(action));
        }

        if (!useNativeBootstrap)
        {
            return action();
        }

        LlamaNativeBootstrap.EnsureInitialized(logTraffic);
        return LlamaNativeBootstrap.RunWithNativeWorkingDirectory(action);
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
