using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds and runs PromptPipelineAssets directly inside the editor for quick experimentation.
/// </summary>
public static class PromptPipelineSimulator
{
    public static void Run(
        PromptPipelineAsset asset,
        PipelineState initialState,
        Action<PipelineState> onSuccess,
        Action<string> onError,
        Action<string> onLog = null,
        ILlmService service = null
    )
    {
        if (asset == null)
        {
            onError?.Invoke("No PromptPipelineAsset selected.");
            return;
        }

        bool ownsService = service == null;
        service ??= new LlamaSharpEditorService();
        onLog?.Invoke($"Building pipeline '{asset.displayName}'...");

        try
        {
            var executor = BuildExecutor(asset, service, onLog);
            var startingState = CloneOrCreate(initialState);
            EditorCoroutineRunner.Start(RunExecutor(
                executor,
                startingState,
                onSuccess,
                onError,
                onLog,
                ownsService ? service as IDisposable : null));
        }
        catch (Exception ex)
        {
            if (ownsService && service is IDisposable disposableService)
            {
                disposableService.Dispose();
            }
            onError?.Invoke(ex.Message);
        }
    }

    private static StateSequentialChainExecutor BuildExecutor(
        PromptPipelineAsset asset,
        ILlmService service,
        Action<string> onLog)
    {
        if (service == null)
        {
            throw new InvalidOperationException("ILlmService is missing for PromptPipelineSimulator.");
        }

        var executor = new StateSequentialChainExecutor();
        if (asset.steps == null || asset.steps.Count == 0)
        {
            return executor;
        }

        foreach (var step in asset.steps)
        {
            if (step == null)
            {
                continue;
            }

            var link = CreateLink(step, service, onLog);
            if (link == null)
            {
                throw new InvalidOperationException($"Step '{step.stepName}' failed to create IStateChainLink.");
            }

            executor.AddLink(link);
        }

        return executor;
    }

    private static IStateChainLink CreateLink(
        PromptPipelineStep step,
        ILlmService service,
        Action<string> onLog)
    {
        switch (step.stepKind)
        {
            case PromptPipelineStepKind.JsonLlm:
                EnsureSettings(step);
                return new JSONLLMStateChainLink(
                    service,
                    step.llmProfile,
                    step.userPromptTemplate,
                    step.jsonMaxRetries,
                    step.jsonRetryDelaySeconds,
                    step.useVision,
                    step.imageStateKey,
                    step.requireImage,
                    step.resizeLongestSide,
                    onLog
                );
            case PromptPipelineStepKind.CompletionLlm:
                EnsureSettings(step);
                return new CompletionChainLink(
                    service,
                    step.llmProfile,
                    step.userPromptTemplate,
                    step.useVision,
                    step.imageStateKey,
                    step.requireImage,
                    step.resizeLongestSide,
                    onLog
                );
            case PromptPipelineStepKind.CustomLink:
                return InstantiateCustomLink(step);
            default:
                return null;
        }
    }

    private static IStateChainLink InstantiateCustomLink(PromptPipelineStep step)
    {
        return PromptPipelineAsset.InstantiateCustomLink(step);
    }

    private static IEnumerator RunExecutor(
        StateSequentialChainExecutor executor,
        PipelineState state,
        Action<PipelineState> onSuccess,
        Action<string> onError,
        Action<string> onLog,
        IDisposable ownedService
    )
    {
        PipelineState finalState = null;
        bool failed = false;
        IEnumerator routine = executor.Execute(state, s => finalState = s);

        while (true)
        {
            object currentYield;
            try
            {
                if (!routine.MoveNext())
                {
                    break;
                }

                currentYield = routine.Current;
            }
            catch (Exception ex)
            {
                failed = true;
                onLog?.Invoke($"Pipeline execution threw: {ex}");
                onError?.Invoke(ex.Message);
                break;
            }

            yield return currentYield;
        }

        if (!failed)
        {
            PipelineState successfulState = finalState ?? state;
            if (successfulState != null &&
                successfulState.TryGetString(PromptPipelineConstants.ErrorKey, out string pipelineError) &&
                !string.IsNullOrWhiteSpace(pipelineError))
            {
                onError?.Invoke(pipelineError);
            }
            else
            {
                onSuccess?.Invoke(successfulState);
            }
        }

        ownedService?.Dispose();
    }

    private static PipelineState CloneOrCreate(PipelineState source)
    {
        return source?.Clone() ?? new PipelineState();
    }

    private static void EnsureSettings(PromptPipelineStep step)
    {
        if (step.llmProfile == null)
        {
            throw new InvalidOperationException($"Step '{step.stepName}' requires LlmGenerationProfile.");
        }
    }
}

