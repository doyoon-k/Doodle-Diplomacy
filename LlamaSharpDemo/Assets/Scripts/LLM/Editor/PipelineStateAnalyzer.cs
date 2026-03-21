using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text.Json;
using UnityEngine;

/// <summary>
/// Editor-only helper that inspects a PromptPipelineAsset and infers which state keys each step uses or produces.
/// </summary>
public static class PipelineStateAnalyzer
{
    private static readonly Regex PromptKeyRegex = new Regex(
        "{{([A-Za-z0-9_]+)}}",
        RegexOptions.Compiled
    );

    public static AnalyzedStateModel Analyze(PromptPipelineAsset asset)
    {
        var model = new AnalyzedStateModel();
        if (asset == null || asset.steps == null)
        {
            return model;
        }

        model.stepCount = asset.steps.Count;
        var keyMap = new Dictionary<string, AnalyzedStateKey>();

        for (int i = 0; i < asset.steps.Count; i++)
        {
            var step = asset.steps[i];
            if (step == null)
            {
                continue;
            }

            switch (step.stepKind)
            {
                case PromptPipelineStepKind.JsonLlm:
                case PromptPipelineStepKind.CompletionLlm:
                    CollectPromptKeys(step, keyMap, i);
                    CollectVisionKeys(step, keyMap, i);
                    break;
                case PromptPipelineStepKind.CustomLink:
                    CollectCustomLinkKeys(step, keyMap, i);
                    break;
            }

            switch (step.stepKind)
            {
                case PromptPipelineStepKind.JsonLlm:
                    CollectJsonProducedKeys(step, keyMap, i);
                    break;
                case PromptPipelineStepKind.CompletionLlm:
                    model.hasCompletionStep = true;
                    RegisterKey(keyMap, PromptPipelineConstants.AnswerKey)
                        .producedByStepIndices
                        .AddUnique(i);
                    break;
            }
        }

        foreach (AnalyzedStateKey key in keyMap.Values)
        {
            bool hasProducer = key.producedByStepIndices.Count > 0;
            bool hasConsumer = key.consumedByStepIndices.Count > 0;

            if (string.Equals(key.keyName, PromptPipelineConstants.AnswerKey, StringComparison.Ordinal) &&
                model.hasCompletionStep)
            {
                key.kind = AnalyzedStateKeyKind.Output;
            }
            else if (!hasProducer && hasConsumer)
            {
                key.kind = AnalyzedStateKeyKind.Input;
            }
            else if (hasProducer && !hasConsumer)
            {
                key.kind = AnalyzedStateKeyKind.Output;
            }
            else if (hasProducer && hasConsumer)
            {
                // Fix: State keys incorrectly hidden if produced by *any* step, even if needed earlier.
                // If the key is consumed BEFORE (or at the same step as) it is first produced, it must be an input.
                int firstProd = key.producedByStepIndices.Min();
                int firstCons = key.consumedByStepIndices.Min();

                if (firstCons <= firstProd)
                {
                    key.kind = AnalyzedStateKeyKind.Input;
                }
                else
                {
                    key.kind = AnalyzedStateKeyKind.Intermediate;
                }
            }
            else
            {
                key.kind = AnalyzedStateKeyKind.Input;
            }
        }

        model.keys = keyMap.Values
            .OrderBy(k => k.kind)
            .ThenBy(k => k.keyName, StringComparer.Ordinal)
            .ToList();

        ComputeStepStates(asset, model);

        return model;
    }

    private static void CollectPromptKeys(
        PromptPipelineStep step,
        Dictionary<string, AnalyzedStateKey> keyMap,
        int stepIndex
    )
    {
        if (!string.IsNullOrEmpty(step.userPromptTemplate))
        {
            foreach (string key in ExtractPromptKeys(step.userPromptTemplate))
            {
                RegisterKey(keyMap, key).consumedByStepIndices.AddUnique(stepIndex);
            }
        }

        var settings = step.llmProfile;
        if (settings != null && !string.IsNullOrEmpty(settings.systemPromptTemplate))
        {
            foreach (string key in ExtractPromptKeys(settings.systemPromptTemplate))
            {
                RegisterKey(keyMap, key).consumedByStepIndices.AddUnique(stepIndex);
            }
        }
    }

    private static void CollectCustomLinkKeys(
        PromptPipelineStep step,
        Dictionary<string, AnalyzedStateKey> keyMap,
        int stepIndex)
    {
        if (string.IsNullOrWhiteSpace(step.customLinkTypeName))
        {
            return;
        }

        if (!CustomLinkStateResolver.TryResolve(step, out var writes))
        {
            return;
        }

        foreach (string key in writes)
        {
            RegisterKey(keyMap, key).producedByStepIndices.AddUnique(stepIndex);
        }
    }

    private static void CollectVisionKeys(
        PromptPipelineStep step,
        Dictionary<string, AnalyzedStateKey> keyMap,
        int stepIndex)
    {
        if (step == null || !step.useVision || string.IsNullOrWhiteSpace(step.imageStateKey))
        {
            return;
        }

        RegisterKey(keyMap, step.imageStateKey)
            .MarkValueKind(AnalyzedStateValueKind.Image)
            .consumedByStepIndices
            .AddUnique(stepIndex);
    }

    private static void CollectJsonProducedKeys(
        PromptPipelineStep step,
        Dictionary<string, AnalyzedStateKey> keyMap,
        int stepIndex
    )
    {
        var settings = step.llmProfile;
        string schema = settings?.format;
        if (settings == null || string.IsNullOrWhiteSpace(schema))
        {
            return;
        }

        string trimmed = schema.TrimStart();
        if (trimmed.Length == 0 || (trimmed[0] != '{' && trimmed[0] != '['))
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(schema);
            foreach (string keyName in ExtractSchemaPropertyNames(document.RootElement))
            {
                if (!string.IsNullOrWhiteSpace(keyName))
                {
                    RegisterKey(keyMap, keyName).producedByStepIndices.AddUnique(stepIndex);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"PipelineStateAnalyzer: Failed to parse JSON format for step '{step.stepName}': {ex.Message}");
        }
    }

    private static IEnumerable<string> ExtractPromptKeys(string template)
    {
        var matches = PromptKeyRegex.Matches(template);
        foreach (Match match in matches)
        {
            if (match.Groups.Count > 1)
            {
                yield return match.Groups[1].Value;
            }
        }
    }

    private static IEnumerable<string> ExtractSchemaPropertyNames(JsonElement token)
    {
        if (token.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        if (token.TryGetProperty("properties", out JsonElement propsToken) &&
            propsToken.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty prop in propsToken.EnumerateObject())
            {
                yield return prop.Name;
            }

            yield break;
        }

        foreach (JsonProperty prop in token.EnumerateObject())
        {
            yield return prop.Name;
        }
    }

    private static AnalyzedStateKey RegisterKey(Dictionary<string, AnalyzedStateKey> keyMap, string keyName)
    {
        if (!keyMap.TryGetValue(keyName, out var key))
        {
            key = new AnalyzedStateKey
            {
                keyName = keyName,
                valueKind = AnalyzedStateValueKind.Text
            };
            keyMap.Add(keyName, key);
        }
        return key;
    }

    private static void ComputeStepStates(PromptPipelineAsset asset, AnalyzedStateModel model)
    {
        model.stepStates = new List<AnalyzedStepState>();
        model.finalStateKeys = new List<string>();
        if (asset == null || asset.steps == null || model.keys == null)
        {
            return;
        }

        int stepCount = asset.steps.Count;
        if (stepCount == 0)
        {
            model.finalStateKeys = model.keys
                .Where(k => k.kind == AnalyzedStateKeyKind.Input)
                .Select(k => k.keyName)
                .OrderBy(k => k, StringComparer.Ordinal)
                .ToList();
            return;
        }
        var producedByStep = new List<HashSet<string>>(stepCount);
        for (int i = 0; i < stepCount; i++)
        {
            producedByStep.Add(new HashSet<string>(StringComparer.Ordinal));
        }

        foreach (AnalyzedStateKey key in model.keys)
        {
            foreach (int idx in key.producedByStepIndices)
            {
                if (idx >= 0 && idx < stepCount)
                {
                    producedByStep[idx].Add(key.keyName);
                }
            }
        }

        var currentKeys = new HashSet<string>(
            model.keys.Where(k => k.kind == AnalyzedStateKeyKind.Input).Select(k => k.keyName),
            StringComparer.Ordinal
        );

        for (int i = 0; i < stepCount; i++)
        {
            var produced = producedByStep[i];
            var newKeys = produced.Where(k => !currentKeys.Contains(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();
            foreach (string k in produced)
            {
                currentKeys.Add(k);
            }

            var snapshot = new AnalyzedStepState
            {
                stepIndex = i,
                stateKeys = currentKeys.OrderBy(k => k, StringComparer.Ordinal).ToList(),
                newKeys = newKeys
            };

            model.stepStates.Add(snapshot);
        }

        if (model.stepStates.Count > 0)
        {
            model.finalStateKeys = new List<string>(model.stepStates.Last().stateKeys);
        }
    }
}

[Serializable]
public class AnalyzedStateModel
{
    public List<AnalyzedStateKey> keys = new();
    public int stepCount;
    public bool hasCompletionStep;
    public List<AnalyzedStepState> stepStates = new();
    public List<string> finalStateKeys = new();
}

[Serializable]
public class AnalyzedStateKey
{
    public string keyName;
    public List<int> producedByStepIndices = new();
    public List<int> consumedByStepIndices = new();
    public AnalyzedStateKeyKind kind;
    public AnalyzedStateValueKind valueKind;
    public string lastValuePreview;

    public AnalyzedStateKey MarkValueKind(AnalyzedStateValueKind kindToApply)
    {
        if (kindToApply == AnalyzedStateValueKind.Image)
        {
            valueKind = AnalyzedStateValueKind.Image;
        }

        return this;
    }
}

[Serializable]
public class AnalyzedStepState
{
    public int stepIndex;
    public List<string> stateKeys = new();
    public List<string> newKeys = new();
}

public enum AnalyzedStateKeyKind
{
    Input = 0,
    Intermediate = 1,
    Output = 2
}

public enum AnalyzedStateValueKind
{
    Text = 0,
    Image = 1
}

internal static class ListExtensions
{
    public static void AddUnique(this List<int> source, int value)
    {
        if (!source.Contains(value))
        {
            source.Add(value);
        }
    }
}
