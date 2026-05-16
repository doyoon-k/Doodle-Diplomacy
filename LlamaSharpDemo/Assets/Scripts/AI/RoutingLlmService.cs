using System;
using System.Collections;
using LLama.Abstractions;
using UnityEngine;

/// <summary>
/// Routes ILlmService calls to local LLamaSharp runtime or cloud direct API service based on profile type.
/// </summary>
public class RoutingLlmService : MonoBehaviour, ILlmService
{
    [Header("Routing")]
    [SerializeField] private RuntimeLlamaSharpService localRuntimeService;
    [SerializeField] private bool registerOnAwake = true;
    [SerializeField] private bool persistAcrossScenes = true;
    [SerializeField] private bool logTraffic;

    private CloudDirectLlmService _cloudService;

    public bool IsPreloadInProgress => localRuntimeService != null && localRuntimeService.IsPreloadInProgress;
    public bool IsPreloadComplete => localRuntimeService != null && localRuntimeService.IsPreloadComplete;
    public bool IsModelReady => localRuntimeService != null && localRuntimeService.IsModelReady;
    public bool LogTrafficEnabled => logTraffic;

    private void Awake()
    {
        if (persistAcrossScenes)
        {
            DontDestroyOnLoad(gameObject);
        }

        EnsureLocalRuntimeService();
        _cloudService ??= new CloudDirectLlmService(logTraffic);

        if (registerOnAwake)
        {
            LlmServiceLocator.Register(this);
        }
    }

    private void OnDestroy()
    {
        _cloudService?.Dispose();
        _cloudService = null;

        if (registerOnAwake)
        {
            LlmServiceLocator.Unregister(this);
        }
    }

    public ILLamaExecutor GetExecutor(BaseLlmGenerationProfile settings)
    {
        ILlmService target = ResolveTargetService(settings);
        return target?.GetExecutor(settings);
    }

    public IEnumerator GenerateCompletion(
        BaseLlmGenerationProfile settings,
        string userPrompt,
        Action<string> onResponse)
    {
        ILlmService target = ResolveTargetService(settings);
        if (target == null)
        {
            onResponse?.Invoke(string.Empty);
            yield break;
        }

        yield return target.GenerateCompletion(settings, userPrompt, onResponse);
    }

    public IEnumerator GenerateCompletionWithState(
        BaseLlmGenerationProfile settings,
        string userPrompt,
        PipelineState state,
        Action<string> onResponse)
    {
        ILlmService target = ResolveTargetService(settings);
        if (target == null)
        {
            onResponse?.Invoke(string.Empty);
            yield break;
        }

        yield return target.GenerateCompletionWithState(settings, userPrompt, state, onResponse);
    }

    public IEnumerator GenerateCompletionWithImage(
        BaseLlmGenerationProfile settings,
        string userPrompt,
        PipelineState state,
        Texture2D image,
        Action<string> onResponse)
    {
        ILlmService target = ResolveTargetService(settings);
        if (target == null)
        {
            onResponse?.Invoke(string.Empty);
            yield break;
        }

        yield return target.GenerateCompletionWithImage(settings, userPrompt, state, image, onResponse);
    }

    public IEnumerator ChatCompletion(
        BaseLlmGenerationProfile settings,
        ChatMessage[] messages,
        Action<string> onResponse)
    {
        ILlmService target = ResolveTargetService(settings);
        if (target == null)
        {
            onResponse?.Invoke(string.Empty);
            yield break;
        }

        yield return target.ChatCompletion(settings, messages, onResponse);
    }

    public IEnumerator Embed(
        BaseLlmGenerationProfile settings,
        string[] inputs,
        Action<float[][]> onEmbeddings)
    {
        ILlmService target = ResolveTargetService(settings);
        if (target == null)
        {
            onEmbeddings?.Invoke(Array.Empty<float[]>());
            yield break;
        }

        yield return target.Embed(settings, inputs, onEmbeddings);
    }

    public void StartPreloadIfRequired(params PromptPipelineAsset[] assets)
    {
        if (RequiresLocalPreload(assets))
        {
            localRuntimeService?.StartPreload();
        }
    }

    public bool RequiresLocalPreload(params PromptPipelineAsset[] assets)
    {
        if (assets == null || assets.Length == 0)
        {
            return true;
        }

        bool foundAnyStep = false;
        for (int i = 0; i < assets.Length; i++)
        {
            PromptPipelineAsset asset = assets[i];
            if (asset == null || asset.steps == null)
            {
                continue;
            }

            for (int stepIndex = 0; stepIndex < asset.steps.Count; stepIndex++)
            {
                PromptPipelineStep step = asset.steps[stepIndex];
                if (step == null || step.llmProfile == null)
                {
                    continue;
                }

                foundAnyStep = true;
                if (step.llmProfile is not CloudGenerationProfile)
                {
                    return true;
                }
            }
        }

        // If no explicit step/profile exists, keep legacy behavior and prepare local runtime.
        return !foundAnyStep;
    }

    public void CancelActiveOperations()
    {
        localRuntimeService?.CancelActiveOperations();
        _cloudService?.CancelActiveOperations();
    }

    private ILlmService ResolveTargetService(BaseLlmGenerationProfile settings)
    {
        if (settings is CloudGenerationProfile)
        {
            _cloudService ??= new CloudDirectLlmService(logTraffic);
            return _cloudService;
        }

        EnsureLocalRuntimeService();
        if (localRuntimeService == null)
        {
            Debug.LogError("[RoutingLlmService] Local runtime service is missing.");
            return null;
        }

        return localRuntimeService;
    }

    private void EnsureLocalRuntimeService()
    {
        if (localRuntimeService != null)
        {
            return;
        }

        localRuntimeService = GetComponent<RuntimeLlamaSharpService>();
        if (localRuntimeService != null)
        {
            return;
        }

        localRuntimeService = FindFirstObjectByType<RuntimeLlamaSharpService>();
        if (localRuntimeService != null)
        {
            return;
        }

        GameObject serviceObject = new GameObject("RuntimeLlamaSharpService");
        localRuntimeService = serviceObject.AddComponent<RuntimeLlamaSharpService>();
        Debug.LogWarning("[RoutingLlmService] RuntimeLlamaSharpService was missing. Created fallback local runtime service.");
    }
}
