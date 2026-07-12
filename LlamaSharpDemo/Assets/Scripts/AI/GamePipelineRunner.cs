using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GamePipelineRunner : MonoBehaviour
{
    public static GamePipelineRunner Instance;
    public RoutingLlmService RuntimeService
    {
        get
        {
            EnsureRuntimeService();
            return _runtimeService;
        }
    }

    [Header("Dependencies")]
    [Tooltip("Runtime LLM service used to execute prompt pipeline steps.")]
    [SerializeField] private RoutingLlmService _runtimeService;

    private Coroutine _currentRoutine;
    private ActiveRun _activeRun;
    private int _nextRunId;

    private sealed class ActiveRun
    {
        public int Id;
        public PipelineState InitialState;
        public Action<PipelineState> OnComplete;
        public Coroutine Routine;
        public bool Completed;
    }

    private void Awake()
    {
        Instance = this;
        EnsureRuntimeService();
    }

    public void StopGeneration()
    {
        CancelActiveRun("[GamePipelineRunner] Pipeline cancelled.");
    }

    private void OnDisable()
    {
        StopGeneration();
    }

    public void RunPipeline(PromptPipelineAsset asset, PipelineState initialState, Action<PipelineState> onComplete)
    {
        EnsureRuntimeService();
        CancelActiveRun("[GamePipelineRunner] Pipeline cancelled by a newer request.");

        var run = new ActiveRun
        {
            Id = ++_nextRunId,
            InitialState = initialState,
            OnComplete = onComplete
        };
        _activeRun = run;
        run.Routine = StartCoroutine(RunRoutine(run, asset, initialState));
        if (ReferenceEquals(_activeRun, run) && !run.Completed)
        {
            _currentRoutine = run.Routine;
        }
    }

    private void EnsureRuntimeService()
    {
        if (_runtimeService == null)
            _runtimeService = GetComponent<RoutingLlmService>();

        if (_runtimeService == null)
            _runtimeService = FindFirstObjectByType<RoutingLlmService>();

        if (_runtimeService == null)
        {
            var serviceObject = new GameObject("RoutingLlmService");
            _runtimeService = serviceObject.AddComponent<RoutingLlmService>();
            Debug.LogWarning("[GamePipelineRunner] RoutingLlmService was missing. Created a fallback runtime service.");
        }

        if (_runtimeService != null && LlmServiceLocator.Current == null)
            LlmServiceLocator.Register(_runtimeService);
    }

    private IEnumerator RunRoutine(ActiveRun run, PromptPipelineAsset asset, PipelineState initialState)
    {
        if (asset == null || asset.steps == null)
        {
            Debug.LogError("[GamePipelineRunner] Asset is null or empty!");
            CompleteRun(run, CreateErrorState(initialState, "[GamePipelineRunner] Asset is null or empty."));
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

        CompleteRun(run, finalState);
    }

    private void CancelActiveRun(string reason)
    {
        ActiveRun run = _activeRun;
        _runtimeService?.CancelActiveOperations();
        if (run == null || run.Completed)
        {
            _currentRoutine = null;
            return;
        }

        if (run.Routine != null)
        {
            StopCoroutine(run.Routine);
        }

        Debug.Log(reason);
        CompleteRun(run, CreateErrorState(run.InitialState, reason));
    }

    private void CompleteRun(ActiveRun run, PipelineState finalState)
    {
        if (run == null || run.Completed)
        {
            return;
        }

        run.Completed = true;
        if (ReferenceEquals(_activeRun, run))
        {
            _activeRun = null;
            _currentRoutine = null;
        }

        Action<PipelineState> callback = run.OnComplete;
        run.OnComplete = null;
        callback?.Invoke(finalState ?? CreateErrorState(run.InitialState, "[GamePipelineRunner] Pipeline returned no state."));
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
            case PromptPipelineStepKind.Embedding:
                return new EmbeddingChainLink(
                    _runtimeService,
                    step.embeddingProfile,
                    step.userPromptTemplate,
                    step.embeddingOutputKey,
                    step.failOnEmptyEmbeddingInput,
                    null,
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
        return PromptPipelineAsset.InstantiateCustomLink(step, _runtimeService);
    }

    private static PipelineState CreateErrorState(PipelineState sourceState, string error)
    {
        PipelineState state = sourceState?.Clone() ?? new PipelineState();
        state.SetString(PromptPipelineConstants.ErrorKey, error ?? "Pipeline execution failed.");
        return state;
    }
}
