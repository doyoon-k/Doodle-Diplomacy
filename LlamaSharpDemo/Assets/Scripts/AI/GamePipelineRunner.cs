using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GamePipelineRunner : MonoBehaviour
{
    public static GamePipelineRunner Instance;
    public RuntimeLlamaSharpService RuntimeService
    {
        get
        {
            EnsureRuntimeService();
            return _runtimeService;
        }
    }

    [Header("Dependencies")]
    [SerializeField] private RuntimeLlamaSharpService _runtimeService;

    private Coroutine _currentRoutine;

    private void Awake()
    {
        Instance = this;
        EnsureRuntimeService();
    }

    public void StopGeneration()
    {
        _runtimeService?.CancelActiveOperations();

        if (_currentRoutine != null)
        {
            StopCoroutine(_currentRoutine);
            _currentRoutine = null;
            Debug.Log("[GamePipelineRunner] Generation stopped by user.");
        }
    }

    private void OnDisable()
    {
        StopGeneration();
    }

    public void RunPipeline(PromptPipelineAsset asset, PipelineState initialState, Action<PipelineState> onComplete)
    {
        EnsureRuntimeService();
        StopGeneration();
        _currentRoutine = StartCoroutine(RunRoutine(asset, initialState, onComplete));
    }

    private void EnsureRuntimeService()
    {
        if (_runtimeService == null)
            _runtimeService = GetComponent<RuntimeLlamaSharpService>();

        if (_runtimeService == null)
            _runtimeService = FindFirstObjectByType<RuntimeLlamaSharpService>();

        if (_runtimeService == null)
        {
            var serviceObject = new GameObject("RuntimeLlamaSharpService");
            _runtimeService = serviceObject.AddComponent<RuntimeLlamaSharpService>();
            Debug.LogWarning("[GamePipelineRunner] RuntimeLlamaSharpService was missing. Created a fallback runtime service.");
        }

        if (_runtimeService != null && LlmServiceLocator.Current == null)
            LlmServiceLocator.Register(_runtimeService);
    }

    private IEnumerator RunRoutine(PromptPipelineAsset asset, PipelineState initialState, Action<PipelineState> onComplete)
    {
        if (asset == null || asset.steps == null)
        {
            Debug.LogError("[GamePipelineRunner] Asset is null or empty!");
            onComplete?.Invoke(CreateErrorState(initialState, "[GamePipelineRunner] Asset is null or empty."));
            yield break;
        }

        // 1. Setup Executor
        StateSequentialChainExecutor executor = new StateSequentialChainExecutor();

        foreach (var step in asset.steps)
        {
            if (step == null) continue;

            IStateChainLink link = CreateLink(step);
            if (link != null)
            {
                executor.AddLink(link);
            }
            else
            {
                Debug.LogError($"[GamePipelineRunner] Failed to create link for step: {step.stepName}");
            }
        }

        // 2. Execute Pipeline
        PipelineState finalState = null;
        IEnumerator execution = null;
        try
        {
            execution = executor.Execute(initialState, result => finalState = result);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[GamePipelineRunner] Failed to create pipeline execution: {ex}");
            finalState = CreateErrorState(initialState, $"[GamePipelineRunner] Failed to create pipeline execution: {ex.Message}");
        }

        if (execution != null)
        {
            while (true)
            {
                bool hasNext;
                object currentYield = null;

                try
                {
                    hasNext = execution.MoveNext();
                    if (hasNext)
                    {
                        currentYield = execution.Current;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[GamePipelineRunner] Pipeline execution threw an exception: {ex}");
                    finalState = CreateErrorState(initialState, $"[GamePipelineRunner] Pipeline execution failed: {ex.Message}");
                    break;
                }

                if (!hasNext)
                {
                    break;
                }

                yield return currentYield;
            }
        }

        // 3. Callback
        if (finalState == null)
        {
            Debug.LogError("[GamePipelineRunner] Pipeline execution failed.");
            finalState = CreateErrorState(initialState, "[GamePipelineRunner] Pipeline execution failed.");
        }

        onComplete?.Invoke(finalState);

        _currentRoutine = null;
    }

    private IStateChainLink CreateLink(PromptPipelineStep step)
    {
        switch (step.stepKind)
        {
            case PromptPipelineStepKind.JsonLlm:
                return new JSONLLMStateChainLink(
                    _runtimeService,
                    step.llmProfile,
                    step.userPromptTemplate,
                    step.jsonMaxRetries,
                    step.jsonRetryDelaySeconds,
                    step.useVision,
                    step.imageStateKey,
                    step.requireImage,
                    step.resizeLongestSide,
                    null, // Log callback is null to avoid double logging (internal Debug.Log is sufficient)
                    step.stepName
                );
            case PromptPipelineStepKind.CompletionLlm:
                return new CompletionChainLink(
                    _runtimeService,
                    step.llmProfile,
                    step.userPromptTemplate,
                    step.useVision,
                    step.imageStateKey,
                    step.requireImage,
                    step.resizeLongestSide,
                    null, // Log callback is null to avoid double logging
                    step.stepName
                );
            case PromptPipelineStepKind.CustomLink:
                return InstantiateCustomLink(step);
            default:
                return null;
        }
    }

    private IStateChainLink InstantiateCustomLink(PromptPipelineStep step)
    {
        return PromptPipelineAsset.InstantiateCustomLink(step);
    }

    private static PipelineState CreateErrorState(PipelineState sourceState, string error)
    {
        PipelineState state = sourceState?.Clone() ?? new PipelineState();
        state.SetString(PromptPipelineConstants.ErrorKey, error ?? "Pipeline execution failed.");
        return state;
    }
}
