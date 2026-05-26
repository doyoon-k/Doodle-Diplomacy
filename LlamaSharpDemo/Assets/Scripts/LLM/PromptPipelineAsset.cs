using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using UnityEngine;

/// <summary>
/// ScriptableObject that stores a linear prompt pipeline definition used by the GraphView editor.
/// </summary>
[CreateAssetMenu(fileName = "PromptPipeline", menuName = "LLM/Prompt Pipeline", order = 0)]
public class PromptPipelineAsset : ScriptableObject
{
    [Tooltip("Human readable pipeline name that shows up in GraphView toolbars.")]
    public string displayName = "New Prompt Pipeline";

    [TextArea(2, 5)]
    [Tooltip("Editor-facing description of what this prompt pipeline does.")]
    public string description;

    [Tooltip("Ordered prompt or custom steps executed by this pipeline.")]
    [SerializeField]
    public List<PromptPipelineStep> steps = new();

    [Tooltip("Saved GraphView node layout positions for this pipeline asset.")]
    [SerializeField]
    public PromptPipelineLayoutSettings layoutSettings = new();

    /// <summary>
    /// Ensures we always have a valid step list when the asset is created or loaded.
    /// </summary>
    private void OnValidate()
    {
        if (steps == null)
        {
            steps = new List<PromptPipelineStep>();
        }

        layoutSettings ??= new PromptPipelineLayoutSettings();
        layoutSettings.snapshotPositions ??= new List<Vector2>();

        foreach (var step in steps)
        {
            if (string.IsNullOrEmpty(step.guid))
            {
                step.guid = System.Guid.NewGuid().ToString();
            }
        }
    }

    /// <summary>
    /// Builds a runnable executor for this pipeline using the provided ILlmService.
    /// </summary>
    public StateSequentialChainExecutor BuildExecutor(ILlmService service)
    {
        if (service == null)
        {
            throw new InvalidOperationException("ILlmService is required to build a pipeline executor.");
        }

        var executor = new StateSequentialChainExecutor();

        if (steps == null || steps.Count == 0)
        {
            return executor;
        }

        var nonNullSteps = steps.Where(s => s != null).ToList();
        if (nonNullSteps.Count == 0)
        {
            return executor;
        }

        // Find the start step (node with no incoming connection)
        var referencedGuids = new HashSet<string>(
            nonNullSteps.Select(s => s.nextStepGuid).Where(g => !string.IsNullOrEmpty(g)),
            StringComparer.Ordinal);
        var startStep = nonNullSteps.FirstOrDefault(s =>
            !string.IsNullOrEmpty(s.guid) && !referencedGuids.Contains(s.guid));

        // Fallback: if circular or all referenced, just take the first one
        if (startStep == null)
        {
            startStep = nonNullSteps[0];
        }

        var currentStep = startStep;
        var visited = new HashSet<string>();

        while (currentStep != null)
        {
            if (!visited.Add(currentStep.guid))
            {
                Debug.LogWarning($"Detected cycle in pipeline at step {currentStep.stepName}");
                break;
            }

            executor.AddLink(CreateLink(currentStep, service));

            if (string.IsNullOrEmpty(currentStep.nextStepGuid))
            {
                break;
            }

            currentStep = nonNullSteps.FirstOrDefault(s =>
                string.Equals(s.guid, currentStep.nextStepGuid, StringComparison.Ordinal));
        }

        return executor;
    }

    private static IStateChainLink CreateLink(PromptPipelineStep step, ILlmService service)
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
                    null,
                    step.stepName
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
                    null,
                    step.stepName
                );
            case PromptPipelineStepKind.CustomLink:
                return InstantiateCustomLink(step);
            default:
                throw new InvalidOperationException($"Unsupported PromptPipelineStepKind: {step.stepKind}");
        }
    }

    private static void EnsureSettings(PromptPipelineStep step)
    {
        if (step.llmProfile == null)
        {
            throw new InvalidOperationException($"Step '{step.stepName}' requires an LLM profile.");
        }
    }

    public static IStateChainLink InstantiateCustomLink(PromptPipelineStep step, ILlmService service = null)
    {
        if (string.IsNullOrEmpty(step.customLinkTypeName))
        {
            throw new InvalidOperationException($"Custom link step '{step.stepName}' is missing a type name.");
        }

        var type = Type.GetType(step.customLinkTypeName);
        if (type == null)
        {
            throw new InvalidOperationException($"Could not resolve custom link type '{step.customLinkTypeName}'.");
        }

        if (!typeof(IStateChainLink).IsAssignableFrom(type))
        {
            throw new InvalidOperationException($"Type '{step.customLinkTypeName}' does not implement IStateChainLink.");
        }

        if (!typeof(ICustomLinkStateProvider).IsAssignableFrom(type))
        {
            throw new InvalidOperationException($"Type '{step.customLinkTypeName}' must implement ICustomLinkStateProvider for editor visualization.");
        }

        var args = (step.customLinkParameters ?? new List<CustomLinkParameter>())
            .Where(p => p != null && !string.IsNullOrWhiteSpace(p.key))
            .ToDictionary(p => p.key, p => p.value ?? string.Empty, StringComparer.Ordinal);
        ScriptableObject customLinkAsset = step.customAsset != null ? step.customAsset : step.llmProfile;

        // 1. Try constructor (Dictionary<string, string>, ScriptableObject, ILlmService) for
        // custom links that need to run another LLM call through the active pipeline service.
        var dictAssetServiceCtor = type.GetConstructor(new[] { typeof(Dictionary<string, string>), typeof(ScriptableObject), typeof(ILlmService) });
        if (dictAssetServiceCtor != null)
        {
            if (dictAssetServiceCtor.Invoke(new object[] { args, customLinkAsset, service }) is IStateChainLink instance)
            {
                return instance;
            }
        }

        // 2. Try constructor (Dictionary<string, string>, ScriptableObject) for custom parameter bags + asset.
        var dictAssetCtor = type.GetConstructor(new[] { typeof(Dictionary<string, string>), typeof(ScriptableObject) });
        if (dictAssetCtor != null)
        {
            if (dictAssetCtor.Invoke(new object[] { args, customLinkAsset }) is IStateChainLink instance)
            {
                return instance;
            }
        }

        // 3. Try constructor (ScriptableObject or derived)
        var assetCtor = type.GetConstructors()
            .FirstOrDefault(c =>
            {
                var p = c.GetParameters();
                return p.Length == 1 && typeof(ScriptableObject).IsAssignableFrom(p[0].ParameterType);
            });

        if (assetCtor != null)
        {
            if (assetCtor.Invoke(new object[] { customLinkAsset }) is IStateChainLink instance)
            {
                return instance;
            }
        }

        // 4. Try constructor (Dictionary<string, string>) for custom parameter bags.
        var dictCtor = type.GetConstructor(new[] { typeof(Dictionary<string, string>) });
        if (dictCtor != null)
        {
            if (dictCtor.Invoke(new object[] { args }) is IStateChainLink dictInstance)
            {
                return dictInstance;
            }
        }

        // 5. Try to bind simple constructors (string/int/float/double/bool/long) by parameter name.
        var bindableCtor = FindBindableConstructor(type);
        if (bindableCtor != null)
        {
            var ctorArgs = BuildConstructorArguments(bindableCtor, args);
            if (bindableCtor.Invoke(ctorArgs) is IStateChainLink boundInstance)
            {
                return boundInstance;
            }
        }

        // 6. Fallback to parameterless ctor.
        if (Activator.CreateInstance(type) is not IStateChainLink fallbackInstance)
        {
            throw new InvalidOperationException($"Failed to instantiate custom link '{step.customLinkTypeName}'.");
        }

        return fallbackInstance;
    }

    private static ConstructorInfo FindBindableConstructor(Type type)
    {
        return type
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .Where(c => c.GetParameters().Length > 0)
            .OrderByDescending(c => c.GetParameters().Length)
            .FirstOrDefault(c => c.GetParameters().All(CanBindParameter));
    }

    private static bool CanBindParameter(ParameterInfo p)
    {
        Type t = p.ParameterType;
        return t == typeof(string) ||
               t == typeof(int) || t == typeof(long) ||
               t == typeof(float) || t == typeof(double) ||
               t == typeof(bool);
    }

    private static object[] BuildConstructorArguments(ConstructorInfo ctor, Dictionary<string, string> args)
    {
        var parameters = ctor.GetParameters();
        var result = new object[parameters.Length];
        for (int i = 0; i < parameters.Length; i++)
        {
            var p = parameters[i];
            string input = args != null && args.TryGetValue(p.Name, out var val) ? val : string.Empty;
            result[i] = ConvertValue(input, p.ParameterType);
        }
        return result;
    }

    private static object ConvertValue(string value, Type targetType)
    {
        try
        {
            if (targetType == typeof(string))
                return value ?? string.Empty;
            if (targetType == typeof(int))
                return int.TryParse(value, out var i) ? i : 0;
            if (targetType == typeof(long))
                return long.TryParse(value, out var l) ? l : 0L;
            if (targetType == typeof(float))
                return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : 0f;
            if (targetType == typeof(double))
                return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0d;
            if (targetType == typeof(bool))
                return bool.TryParse(value, out var b) && b;

            return Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
        }
        catch
        {
            return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;
        }
    }
}

/// <summary>
/// Serialized data for a single pipeline step (LLM or custom link).
/// </summary>
[Serializable]
public class PromptPipelineStep
{
    [Tooltip("Editor-facing name for this pipeline step.")]
    public string stepName = "Step";

    [Tooltip("Execution type for this step: JSON LLM, completion LLM, or custom chain link.")]
    public PromptPipelineStepKind stepKind = PromptPipelineStepKind.JsonLlm;

    [Header("Shared LLM Settings")]
    [Tooltip("Generation profile used when this step calls an LLM.")]
    public BaseLlmGenerationProfile llmProfile;

    [TextArea(4, 12)]
    [Tooltip("User prompt template for this step. Supports PipelineState placeholders.")]
    public string userPromptTemplate;

    [Header("JSON LLM Options")]
    [Min(1)]
    [Tooltip("Maximum attempts to retry JSON parsing/validation for this step.")]
    public int jsonMaxRetries = 3;

    [Min(0f)]
    [Tooltip("Delay between JSON retry attempts in seconds.")]
    public float jsonRetryDelaySeconds = 0.1f;

    [Header("Vision Options")]
    [Tooltip("When enabled, this step will try to resolve an image from the shared PipelineState and send it to a vision-capable model.")]
    public bool useVision;

    [Tooltip("PipelineState key that should contain a Texture2D or Sprite for this step.")]
    public string imageStateKey;

    [Tooltip("If true, missing image input fails the pipeline instead of falling back to text-only generation.")]
    public bool requireImage = true;

    [Min(64)]
    [Tooltip("Images are resized so their longest side does not exceed this value before VLM inference.")]
    public int resizeLongestSide = 1024;

    [Header("Custom Link Options")]
    [Tooltip("Full type name implementing IStateChainLink (Type.GetType resolvable).")]
    public string customLinkTypeName;

    [Tooltip("Optional ScriptableObject asset to pass to the custom link constructor.")]
    public ScriptableObject customAsset;

    [Tooltip("Key/value parameters passed into a custom link step.")]
    [SerializeField]
    public List<CustomLinkParameter> customLinkParameters = new();

    [HideInInspector]
    public Vector2 editorPosition;

    [HideInInspector]
    public string guid;

    [HideInInspector]
    public string nextStepGuid;
}

/// <summary>
/// Identifies which kind of step a PromptPipelineStep represents.
/// </summary>
public enum PromptPipelineStepKind
{
    JsonLlm = 0,
    CompletionLlm = 1,
    CustomLink = 2
}

[Serializable]
public class PromptPipelineLayoutSettings
{
    [Tooltip("Saved graph position of the pipeline input node.")]
    public Vector2 inputNodePosition = new(-600f, 80f);
    [Tooltip("Saved graph position of the pipeline output node.")]
    public Vector2 outputNodePosition = Vector2.zero;
    [Tooltip("Whether the input node position has been initialized by the editor.")]
    public bool inputPositionInitialized;
    [Tooltip("Whether the output node position has been initialized by the editor.")]
    public bool outputPositionInitialized;
    [Tooltip("Saved graph view pan position.")]
    public Vector3 viewPosition = Vector3.zero;
    [Tooltip("Saved graph view zoom scale.")]
    public Vector3 viewScale = Vector3.one;
    [Tooltip("Whether the graph view pan/zoom state has been initialized.")]
    public bool viewInitialized;
    [Tooltip("Saved node positions for step snapshots in the graph editor.")]
    public List<Vector2> snapshotPositions = new();
    [Tooltip("Whether saved step node positions have been initialized.")]
    public bool snapshotPositionsInitialized;
}

[Serializable]
public class CustomLinkParameter
{
    [Tooltip("Parameter key passed to a custom pipeline link.")]
    public string key;
    [Tooltip("Parameter value passed to a custom pipeline link.")]
    public string value;
}

