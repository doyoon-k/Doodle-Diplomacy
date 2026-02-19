using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GamePipelineRunner : MonoBehaviour
{
    public static GamePipelineRunner Instance;

    [Header("Dependencies")]
    [SerializeField] private RuntimeLlamaSharpService _runtimeService;

    private Coroutine _currentRoutine;

    private void Awake()
    {
        Instance = this;
        if (_runtimeService == null) _runtimeService = GetComponent<RuntimeLlamaSharpService>();
    }

    public void StopGeneration()
    {
        if (_currentRoutine != null)
        {
            StopCoroutine(_currentRoutine);
            _currentRoutine = null;
            Debug.Log("[GamePipelineRunner] Generation stopped by user.");
        }
    }

    public void RunPipeline(PromptPipelineAsset asset, Dictionary<string, string> initialState, Action<Dictionary<string, string>> onComplete)
    {
        StopGeneration();
        _currentRoutine = StartCoroutine(RunRoutine(asset, initialState, onComplete));
    }

    private IEnumerator RunRoutine(PromptPipelineAsset asset, Dictionary<string, string> initialState, Action<Dictionary<string, string>> onComplete)
    {
        if (asset == null || asset.steps == null)
        {
            Debug.LogError("[GamePipelineRunner] Asset is null or empty!");
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
        Dictionary<string, string> finalState = null;
        yield return executor.Execute(initialState, result => finalState = result);

        // 3. Callback
        if (finalState != null)
        {
            onComplete?.Invoke(finalState);
        }
        else
        {
            Debug.LogError("[GamePipelineRunner] Pipeline execution failed.");
        }

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
                    null // Log callback is null to avoid double logging (internal Debug.Log is sufficient)
                );
            case PromptPipelineStepKind.CompletionLlm:
                return new CompletionChainLink(
                    _runtimeService,
                    step.llmProfile,
                    step.userPromptTemplate,
                    null // Log callback is null to avoid double logging
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
}
