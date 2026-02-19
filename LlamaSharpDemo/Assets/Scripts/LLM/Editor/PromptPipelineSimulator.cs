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
        Dictionary<string, string> initialState,
        Action<Dictionary<string, string>> onSuccess,
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
                    onLog
                );
            case PromptPipelineStepKind.CompletionLlm:
                EnsureSettings(step);
                return new CompletionChainLink(
                    service,
                    step.llmProfile,
                    step.userPromptTemplate,
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
        Dictionary<string, string> state,
        Action<Dictionary<string, string>> onSuccess,
        Action<string> onError,
        Action<string> onLog,
        IDisposable ownedService
    )
    {
        Dictionary<string, string> finalState = null;
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
            onSuccess?.Invoke(finalState ?? state);
        }

        ownedService?.Dispose();
    }

    private static Dictionary<string, string> CloneOrCreate(Dictionary<string, string> source)
    {
        return source != null
            ? new Dictionary<string, string>(source)
            : new Dictionary<string, string>();
    }

    private static void EnsureSettings(PromptPipelineStep step)
    {
        if (step.llmProfile == null)
        {
            throw new InvalidOperationException($"Step '{step.stepName}' requires LlmGenerationProfile.");
        }
    }
}

