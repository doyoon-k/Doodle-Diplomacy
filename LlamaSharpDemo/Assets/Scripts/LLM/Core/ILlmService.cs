using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LLama;
using LLama.Abstractions;
using LLama.Common;
using LLama.Sampling;
using UnityEngine;

/// <summary>
/// Provider-agnostic LLM service contract used by runtime and editor tools.
/// </summary>
public interface ILlmService
{
    /// <summary>
    /// Returns a LLamaSharp executor for the given profile.
    /// Implementations may cache and reuse executors.
    /// </summary>
    ILLamaExecutor GetExecutor(LlmGenerationProfile settings);

    IEnumerator GenerateCompletion(
        LlmGenerationProfile settings,
        string userPrompt,
        Action<string> onResponse);

    IEnumerator GenerateCompletionWithState(
        LlmGenerationProfile settings,
        string userPrompt,
        Dictionary<string, string> state,
        Action<string> onResponse);

    IEnumerator ChatCompletion(
        LlmGenerationProfile settings,
        ChatMessage[] messages,
        Action<string> onResponse);

    IEnumerator Embed(
        LlmGenerationProfile settings,
        string[] inputs,
        Action<float[][]> onEmbeddings);
}

/// <summary>
/// Shared helpers for mapping project settings to LLamaSharp primitives.
/// </summary>
public static class LlamaSharpInterop
{
    private const int AutoThreadCap = 4;
    private const int AutoBatchThreadCap = 1;
    private static readonly SemaphoreSlim InferenceGate = new SemaphoreSlim(1, 1);

    public static string ResolveModelPath(LlmGenerationProfile settings)
    {
        if (settings == null || string.IsNullOrWhiteSpace(settings.model))
        {
            return null;
        }

        string candidate = settings.model.Trim();
        if (Path.IsPathRooted(candidate))
        {
            return candidate;
        }

        if (settings.runtimeParams != null && settings.runtimeParams.modelPathRelativeToStreamingAssets)
        {
            return Path.Combine(Application.streamingAssetsPath, candidate);
        }

        return Path.GetFullPath(candidate);
    }

    public static ModelParams CreateModelParams(LlmGenerationProfile settings, string resolvedModelPath)
    {
        if (settings == null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        if (string.IsNullOrWhiteSpace(resolvedModelPath))
        {
            throw new ArgumentException("Resolved model path is required.", nameof(resolvedModelPath));
        }

        var runtime = settings.runtimeParams ?? new LlmGenerationProfile.RuntimeParams();
        var modelParams = new ModelParams(resolvedModelPath)
        {
            ContextSize = (uint)Mathf.Max(128, runtime.contextSize),
            GpuLayerCount = Mathf.Max(0, runtime.gpuLayerCount)
        };

        int threadCount = ResolveThreadCount(runtime.threads);
        modelParams.Threads = threadCount;
        modelParams.BatchThreads = ResolveBatchThreadCount(threadCount);

        return modelParams;
    }

    public static int ResolveThreadCount(int configuredThreads)
    {
        if (configuredThreads > 0)
        {
            return configuredThreads;
        }

        int processorCount = Math.Max(1, Environment.ProcessorCount);
        int autoThreads = Math.Max(1, processorCount / 2);
        return Math.Min(AutoThreadCap, autoThreads);
    }

    public static int ResolveBatchThreadCount(int threadCount)
    {
        return Math.Max(1, Math.Min(AutoBatchThreadCap, threadCount));
    }

    public static InferenceParams CreateInferenceParams(LlmGenerationProfile settings)
    {
        var source = settings?.modelParams ?? new LlmGenerationProfile.ModelParams();
        var sampling = new DefaultSamplingPipeline
        {
            Temperature = Mathf.Max(0f, source.temperature),
            TopP = Mathf.Clamp01(source.top_p),
            TopK = Mathf.Max(1, Mathf.RoundToInt(source.top_k)),
            RepeatPenalty = Mathf.Max(0f, source.repeat_penalty)
        };

        return new InferenceParams
        {
            MaxTokens = source.num_predict > 0 ? source.num_predict : -1,
            SamplingPipeline = sampling
        };
    }

    public static string RenderSystemPrompt(LlmGenerationProfile settings, Dictionary<string, string> state)
    {
        if (settings == null)
        {
            return null;
        }

        settings.RenderSystemPrompt(state);
        return settings.GetLastRenderedPrompt();
    }

    public static string BuildUserPrompt(
        LlmGenerationProfile settings,
        string userPrompt,
        bool requiresJson,
        string systemPrompt = null)
    {
        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            builder.AppendLine("System:");
            builder.AppendLine(systemPrompt);
            builder.AppendLine();
        }

        builder.AppendLine(userPrompt ?? string.Empty);

        if (requiresJson && settings != null && !string.IsNullOrWhiteSpace(settings.format))
        {
            builder.AppendLine();
            builder.AppendLine("Respond with only valid JSON that matches this schema:");
            builder.Append(settings.format);
        }

        return builder.ToString();
    }

    public static void ConfigureExecutor(ILLamaExecutor executor, string systemPrompt)
    {
        // No-op. Keep this method to avoid touching all call sites during migration.
    }

    public static IEnumerator InferToString(
        ILLamaExecutor executor,
        string prompt,
        IInferenceParams inferenceParams,
        Action<string> onResponse)
    {
        if (executor == null)
        {
            onResponse?.Invoke(string.Empty);
            yield break;
        }

        Task waitTask = InferenceGate.WaitAsync();
        while (!waitTask.IsCompleted)
        {
            yield return null;
        }

        Exception waitFailure = GetTaskException(waitTask);
        if (waitFailure != null)
        {
            Debug.LogError($"[LlamaSharpInterop] Failed to enter inference gate: {waitFailure}");
            onResponse?.Invoke(string.Empty);
            yield break;
        }

        Task<string> inferenceTask = null;
        try
        {
            inferenceTask = Task.Run(async () =>
            {
                var output = new StringBuilder();
                IAsyncEnumerator<string> enumerator = null;
                try
                {
                    enumerator = executor
                        .InferAsync(prompt ?? string.Empty, inferenceParams, CancellationToken.None)
                        .GetAsyncEnumerator(CancellationToken.None);

                    while (await enumerator.MoveNextAsync())
                    {
                        output.Append(enumerator.Current);
                    }
                }
                finally
                {
                    if (enumerator != null)
                    {
                        await enumerator.DisposeAsync();
                    }
                }

                return output.ToString();
            });

            while (!inferenceTask.IsCompleted)
            {
                yield return null;
            }

            Exception failure = GetTaskException(inferenceTask);
            if (failure != null)
            {
                Debug.LogError($"[LlamaSharpInterop] Inference failed: {failure}");
                onResponse?.Invoke(string.Empty);
                yield break;
            }

            onResponse?.Invoke(inferenceTask.Result);
        }
        finally
        {
            InferenceGate.Release();
        }
    }

    private static Exception GetTaskException(Task task)
    {
        if (task == null || !task.IsFaulted)
        {
            return null;
        }

        return task.Exception?.GetBaseException() ?? task.Exception;
    }
}
