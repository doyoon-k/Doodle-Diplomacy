using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LLama;
using LLama.Abstractions;
using LLama.Common;
using LLama.Native;
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
        PipelineState state,
        Action<string> onResponse);

    IEnumerator GenerateCompletionWithImage(
        LlmGenerationProfile settings,
        string userPrompt,
        PipelineState state,
        Texture2D image,
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
    private const string FallbackVisionMarker = "<image>";
    private const string QwenVisionMarker = "<|vision_start|><|image_pad|><|vision_end|>";
    private static readonly SemaphoreSlim InferenceGate = new SemaphoreSlim(1, 1);
    private static readonly object GrammarCacheLock = new object();
    private static readonly Dictionary<string, GrammarCacheEntry> GrammarCache =
        new Dictionary<string, GrammarCacheEntry>(StringComparer.Ordinal);
    private static readonly IReadOnlyList<string> QwenAntiPrompts = new[]
    {
        "<|im_end|>",
        "<|im_start|>user",
        "<|im_start|>system"
    };

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

    public static MtmdContextParams CreateMtmdContextParams(LlmGenerationProfile settings)
    {
        var runtime = settings?.runtimeParams ?? new LlmGenerationProfile.RuntimeParams();
        return new MtmdContextParams
        {
            NThreads = ResolveThreadCount(runtime.threads),
            UseGpu = Mathf.Max(0, runtime.gpuLayerCount) > 0,
            PrintTimings = false,
            Warmup = false,
            MediaMarker = ResolveVisionMarker(settings)
        };
    }

    public static string ResolveVisionMarker(LlmGenerationProfile settings = null)
    {
        if (UsesQwenChatTemplate(settings))
        {
            return QwenVisionMarker;
        }

        try
        {
            string nativeMarker = NativeApi.MtmdDefaultMarker();
            if (!string.IsNullOrWhiteSpace(nativeMarker))
            {
                return nativeMarker;
            }
        }
        catch
        {
            // Fall back to the common llama.cpp multimodal marker when native lookup is unavailable.
        }

        return FallbackVisionMarker;
    }

    public static InferenceParams CreateInferenceParams(LlmGenerationProfile settings)
    {
        var source = settings?.modelParams ?? new LlmGenerationProfile.ModelParams();
        Grammar grammar = null;
        bool shouldAttachGrammar = RequiresJsonSchema(settings) &&
                                   ShouldUseGrammar(settings.jsonSchemaDeliveryMode);
        if (shouldAttachGrammar)
        {
            TryGetJsonGrammar(settings, out grammar);
        }

        if (shouldAttachGrammar && grammar == null)
        {
            Debug.LogWarning(
                $"[LlamaSharpInterop] JSON schema mode is '{settings?.jsonSchemaDeliveryMode}', but grammar could not be built. Output will not be strictly constrained.");
        }

        var sampling = new DefaultSamplingPipeline
        {
            Temperature = Mathf.Max(0f, source.temperature),
            TopP = Mathf.Clamp01(source.top_p),
            TopK = Mathf.Max(1, Mathf.RoundToInt(source.top_k)),
            RepeatPenalty = Mathf.Max(0f, source.repeat_penalty),
            Grammar = grammar,
            GrammarOptimization = grammar != null
                ? DefaultSamplingPipeline.GrammarOptimizationMode.Basic
                : DefaultSamplingPipeline.GrammarOptimizationMode.None
        };

        if (source.seed > 0)
        {
            sampling.Seed = (uint)source.seed;
        }

        bool useQwenChatTemplate = UsesQwenChatTemplate(settings);
        return new InferenceParams
        {
            MaxTokens = source.num_predict > 0 ? source.num_predict : -1,
            SamplingPipeline = sampling,
            AntiPrompts = useQwenChatTemplate ? QwenAntiPrompts : null,
            DecodeSpecialTokens = useQwenChatTemplate
        };
    }

    public static string RenderSystemPrompt(LlmGenerationProfile settings, PipelineState state)
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
        string systemPrompt = null,
        bool includeVisionMarker = false)
    {
        string userContent = BuildUserContent(settings, userPrompt, requiresJson);
        if (UsesQwenChatTemplate(settings))
        {
            return BuildQwenPrompt(settings, userContent, systemPrompt, includeVisionMarker);
        }

        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            builder.AppendLine("System:");
            builder.AppendLine(systemPrompt);
            builder.AppendLine();
        }

        if (includeVisionMarker)
        {
            string visionMarker = ResolveVisionMarker(settings);
            if (!string.IsNullOrWhiteSpace(visionMarker))
            {
                builder.AppendLine(visionMarker);
                builder.AppendLine();
            }
        }

        builder.Append(userContent);

        return builder.ToString();
    }

    public static string SanitizeCompletion(string completion, LlmGenerationProfile settings)
    {
        if (string.IsNullOrWhiteSpace(completion))
        {
            return string.Empty;
        }

        string sanitized = completion.Trim();
        if (!UsesQwenChatTemplate(settings))
        {
            return sanitized;
        }

        const string AssistantPrefix = "<|im_start|>assistant";
        while (sanitized.StartsWith(AssistantPrefix, StringComparison.Ordinal))
        {
            sanitized = sanitized.Substring(AssistantPrefix.Length).TrimStart('\r', '\n', ' ');
        }

        int endMarkerIndex = sanitized.IndexOf("<|im_end|>", StringComparison.Ordinal);
        if (endMarkerIndex >= 0)
        {
            sanitized = sanitized.Substring(0, endMarkerIndex);
        }

        int turnMarkerIndex = sanitized.IndexOf("<|im_start|>", StringComparison.Ordinal);
        if (turnMarkerIndex >= 0)
        {
            sanitized = sanitized.Substring(0, turnMarkerIndex);
        }

        return sanitized.Trim();
    }

    public static bool UsesQwenChatTemplate(LlmGenerationProfile settings)
    {
        if (settings == null)
        {
            return false;
        }

        return ContainsIgnoreCase(settings.model, "qwen") ||
               ContainsIgnoreCase(settings.visionProjectorModel, "qwen");
    }

    private static bool RequiresJsonSchema(LlmGenerationProfile settings)
    {
        return settings != null && !string.IsNullOrWhiteSpace(settings.format);
    }

    private static bool ShouldUseGrammar(JsonSchemaDeliveryMode mode)
    {
        return mode == JsonSchemaDeliveryMode.Auto || mode == JsonSchemaDeliveryMode.GrammarOnly;
    }

    private static bool ShouldAppendSchemaToPrompt(JsonSchemaDeliveryMode mode)
    {
        return mode == JsonSchemaDeliveryMode.PromptAppendOnly;
    }

    private static string BuildUserContent(
        LlmGenerationProfile settings,
        string userPrompt,
        bool requiresJson)
    {
        var builder = new StringBuilder();
        builder.Append(userPrompt ?? string.Empty);

        if (requiresJson && settings != null)
        {
            bool shouldAppendSchema = ShouldAppendSchemaToPrompt(settings.jsonSchemaDeliveryMode);
            if (settings.jsonSchemaDeliveryMode == JsonSchemaDeliveryMode.Auto)
            {
                shouldAppendSchema = !TryGetJsonGrammar(settings, out _);
            }

            if (shouldAppendSchema)
            {
                builder.AppendLine();
                builder.AppendLine();
                builder.AppendLine("Respond with only valid JSON.");
                if (!string.IsNullOrWhiteSpace(settings.format))
                {
                    builder.AppendLine("Match this JSON schema:");
                    builder.Append(settings.format);
                }
            }
        }

        return builder.ToString();
    }

    private static string BuildQwenPrompt(
        LlmGenerationProfile settings,
        string userContent,
        string systemPrompt,
        bool includeVisionMarker)
    {
        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            builder.AppendLine("<|im_start|>system");
            builder.AppendLine(systemPrompt.Trim());
            builder.AppendLine("<|im_end|>");
        }

        builder.AppendLine("<|im_start|>user");
        if (includeVisionMarker)
        {
            builder.AppendLine(ResolveVisionMarker(settings));
            if (!string.IsNullOrWhiteSpace(userContent))
            {
                builder.AppendLine();
            }
        }

        if (!string.IsNullOrWhiteSpace(userContent))
        {
            builder.Append(userContent.Trim());
            builder.AppendLine();
        }

        builder.AppendLine("<|im_end|>");
        builder.Append("<|im_start|>assistant\n");
        return builder.ToString();
    }

    private static bool ContainsIgnoreCase(string source, string value)
    {
        return !string.IsNullOrWhiteSpace(source) &&
               source.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public static void ConfigureExecutor(ILLamaExecutor executor, string systemPrompt)
    {
        // No-op. Keep this method to avoid touching all call sites during migration.
    }

    public static async Task<string> InferToStringAsync(
        ILLamaExecutor executor,
        string prompt,
        IInferenceParams inferenceParams,
        CancellationToken cancellationToken)
    {
        if (executor == null)
        {
            return string.Empty;
        }

        await InferenceGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var output = new StringBuilder();
            IAsyncEnumerator<string> enumerator = null;
            try
            {
                enumerator = executor
                    .InferAsync(prompt ?? string.Empty, inferenceParams, cancellationToken)
                    .GetAsyncEnumerator(cancellationToken);

                while (await enumerator.MoveNextAsync().ConfigureAwait(false))
                {
                    output.Append(enumerator.Current);
                }
            }
            finally
            {
                if (enumerator != null)
                {
                    await enumerator.DisposeAsync().ConfigureAwait(false);
                }
            }

            return output.ToString();
        }
        finally
        {
            InferenceGate.Release();
        }
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

        Task<string> inferenceTask = null;
        inferenceTask = Task.Run(() => InferToStringAsync(
            executor,
            prompt,
            inferenceParams,
            CancellationToken.None));

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

    private static bool TryGetJsonGrammar(LlmGenerationProfile settings, out Grammar grammar)
    {
        grammar = null;
        string schema = settings?.format;
        if (string.IsNullOrWhiteSpace(schema))
        {
            return false;
        }

        GrammarCacheEntry cached;
        lock (GrammarCacheLock)
        {
            if (GrammarCache.TryGetValue(schema, out cached))
            {
                grammar = cached.Grammar;
                return grammar != null;
            }
        }

        Grammar created = null;
        string error = null;
        if (JsonSchemaGrammarBuilder.TryBuild(schema, out string gbnf, out string root, out string buildError))
        {
            created = new Grammar(gbnf, root);
        }
        else
        {
            error = buildError;
        }

        lock (GrammarCacheLock)
        {
            GrammarCache[schema] = new GrammarCacheEntry(created, error);
        }

        if (created == null && !string.IsNullOrWhiteSpace(error))
        {
            Debug.LogWarning($"[LlamaSharpInterop] JSON schema grammar fallback to prompt instructions: {error}");
        }

        grammar = created;
        return grammar != null;
    }

    private static Exception GetTaskException(Task task)
    {
        if (task == null || !task.IsFaulted)
        {
            return null;
        }

        return task.Exception?.GetBaseException() ?? task.Exception;
    }

    private sealed class GrammarCacheEntry
    {
        public GrammarCacheEntry(Grammar grammar, string error)
        {
            Grammar = grammar;
            Error = error;
        }

        public Grammar Grammar { get; }
        public string Error { get; }
    }

    private sealed class JsonSchemaGrammarBuilder
    {
        private const string RootRuleName = "root";
        private readonly StringBuilder _builder = new StringBuilder(1024);
        private int _ruleIndex;

        private readonly List<string> _deferredRules = new List<string>();

        private sealed class ObjectPropertyRule
        {
            public string Name;
            public string ValueRule;
        }

        public static bool TryBuild(string schema, out string gbnf, out string rootRule, out string error)
        {
            gbnf = null;
            rootRule = RootRuleName;
            error = null;

            if (string.IsNullOrWhiteSpace(schema))
            {
                error = "Schema is empty.";
                return false;
            }

            try
            {
                using var document = JsonDocument.Parse(schema);
                var builder = new JsonSchemaGrammarBuilder();
                string valueRule = builder.BuildRuleForSchema(document.RootElement, "root_value");
                builder.AppendRule(RootRuleName, valueRule);
                builder.AppendDeferredRules();
                builder.AppendPrimitiveRules();
                gbnf = builder._builder.ToString();
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private string BuildRuleForSchema(JsonElement schema, string hint)
        {
            string schemaType = ReadSchemaType(schema);

            if (schema.TryGetProperty("enum", out JsonElement enumElement) &&
                enumElement.ValueKind == JsonValueKind.Array &&
                enumElement.GetArrayLength() > 0)
            {
                if (!string.IsNullOrEmpty(schemaType) &&
                    !schemaType.Equals("string", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Unsupported enum type '{schemaType}' for schema node '{hint}'.");
                }

                return BuildStringEnumRule(enumElement, hint);
            }

            if (schemaType == null)
            {
                if (schema.TryGetProperty("properties", out _))
                {
                    schemaType = "object";
                }
                else if (schema.TryGetProperty("items", out _))
                {
                    schemaType = "array";
                }
            }

            return schemaType switch
            {
                "object" => BuildObjectRule(schema, hint),
                "array" => BuildArrayRule(schema, hint),
                "string" => BuildStringRule(schema, hint),
                "number" => "jsonnumber",
                "integer" => "jsoninteger",
                "boolean" => "jsonboolean",
                _ => throw new InvalidOperationException($"Unsupported or missing schema type for node '{hint}'.")
            };
        }

        private string BuildObjectRule(JsonElement schema, string hint)
        {
            if (!schema.TryGetProperty("properties", out JsonElement propertiesElement) ||
                propertiesElement.ValueKind != JsonValueKind.Object)
            {
                string emptyRule = NextRuleName($"{hint}_object");
                AppendRule(emptyRule, "\"{\" ws \"}\" ws");
                return emptyRule;
            }

            if (schema.TryGetProperty("additionalProperties", out JsonElement additionalPropertiesElement) &&
                additionalPropertiesElement.ValueKind == JsonValueKind.True)
            {
                throw new InvalidOperationException(
                    $"Schema node '{hint}' allows additionalProperties=true, which is not supported for strict grammar.");
            }

            var properties = new List<ObjectPropertyRule>();
            foreach (JsonProperty property in propertiesElement.EnumerateObject())
            {
                string valueRule = BuildRuleForSchema(property.Value, $"{hint}_{property.Name}");
                properties.Add(new ObjectPropertyRule
                {
                    Name = property.Name,
                    ValueRule = valueRule
                });
            }

            string objectRuleName = NextRuleName($"{hint}_object");
            if (properties.Count == 0)
            {
                AppendRule(objectRuleName, "\"{\" ws \"}\" ws");
                return objectRuleName;
            }

            bool hasRequiredArray = schema.TryGetProperty("required", out JsonElement requiredElement) &&
                                    requiredElement.ValueKind == JsonValueKind.Array;

            HashSet<string> required = ReadRequired(schema);
            if (!hasRequiredArray)
            {
                required = new HashSet<string>(StringComparer.Ordinal);
                foreach (ObjectPropertyRule property in properties)
                {
                    required.Add(property.Name);
                }
            }

            var requiredProperties = new List<ObjectPropertyRule>();
            var optionalProperties = new List<ObjectPropertyRule>();
            foreach (ObjectPropertyRule property in properties)
            {
                if (required.Contains(property.Name))
                {
                    requiredProperties.Add(property);
                }
                else
                {
                    optionalProperties.Add(property);
                }
            }

            foreach (string requiredKey in required)
            {
                bool found = false;
                foreach (ObjectPropertyRule property in properties)
                {
                    if (property.Name.Equals(requiredKey, StringComparison.Ordinal))
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    throw new InvalidOperationException(
                        $"Schema node '{hint}' declares required key '{requiredKey}' that is missing from properties.");
                }
            }

            if (requiredProperties.Count == 0)
            {
                AppendRule(objectRuleName, BuildAllOptionalObjectExpression(optionalProperties));
                return objectRuleName;
            }

            var expression = new StringBuilder();
            expression.Append("\"{\" ws ");
            for (int i = 0; i < requiredProperties.Count; i++)
            {
                if (i > 0)
                {
                    expression.Append("\",\" ws ");
                }

                expression.Append(BuildPropertyExpression(requiredProperties[i]));
            }

            foreach (ObjectPropertyRule optional in optionalProperties)
            {
                expression.Append(" (\",\" ws ");
                expression.Append(BuildPropertyExpression(optional));
                expression.Append(")?");
            }

            expression.Append(" \"}\" ws");
            AppendRule(objectRuleName, expression.ToString());
            return objectRuleName;
        }

        private string BuildAllOptionalObjectExpression(List<ObjectPropertyRule> optionalProperties)
        {
            if (optionalProperties == null || optionalProperties.Count == 0)
            {
                return "\"{\" ws \"}\" ws";
            }

            var alternatives = new List<string> { "\"{\" ws \"}\" ws" };
            for (int start = 0; start < optionalProperties.Count; start++)
            {
                var expression = new StringBuilder();
                expression.Append("\"{\" ws ");
                expression.Append(BuildPropertyExpression(optionalProperties[start]));

                for (int i = start + 1; i < optionalProperties.Count; i++)
                {
                    expression.Append(" (\",\" ws ");
                    expression.Append(BuildPropertyExpression(optionalProperties[i]));
                    expression.Append(")?");
                }

                expression.Append(" \"}\" ws");
                alternatives.Add(expression.ToString());
            }

            return string.Join(" | ", alternatives);
        }

        private string BuildArrayRule(JsonElement schema, string hint)
        {
            if (!schema.TryGetProperty("items", out JsonElement itemsElement))
            {
                throw new InvalidOperationException($"Schema array node '{hint}' is missing 'items'.");
            }

            string itemRule = BuildRuleForSchema(itemsElement, $"{hint}_item");
            int minItems = TryReadNonNegativeInt(schema, "minItems", out int parsedMinItems) ? parsedMinItems : 0;
            int? maxItems = TryReadNonNegativeInt(schema, "maxItems", out int parsedMaxItems) ? parsedMaxItems : (int?)null;
            if (maxItems.HasValue && maxItems.Value < minItems)
            {
                throw new InvalidOperationException(
                    $"Schema array node '{hint}' has maxItems ({maxItems}) < minItems ({minItems}).");
            }

            string ruleName = NextRuleName($"{hint}_array");
            AppendRule(ruleName, BuildArrayExpression(itemRule, minItems, maxItems));
            return ruleName;
        }

        private string BuildStringRule(JsonElement schema, string hint)
        {
            int minLength = TryReadNonNegativeInt(schema, "minLength", out int parsedMinLength) ? parsedMinLength : 0;
            int? maxLength = TryReadNonNegativeInt(schema, "maxLength", out int parsedMaxLength) ? parsedMaxLength : (int?)null;
            if (maxLength.HasValue && maxLength.Value < minLength)
            {
                throw new InvalidOperationException(
                    $"Schema string node '{hint}' has maxLength ({maxLength}) < minLength ({minLength}).");
            }

            if (minLength == 0 && !maxLength.HasValue)
            {
                return "jsonstring";
            }

            string ruleName = NextRuleName($"{hint}_string");
            var expression = new StringBuilder();
            expression.Append("\"\\\"\" ");

            for (int i = 0; i < minLength; i++)
            {
                expression.Append("jsonchar ");
            }

            if (maxLength.HasValue)
            {
                int optionalCount = maxLength.Value - minLength;
                for (int i = 0; i < optionalCount; i++)
                {
                    expression.Append("jsonchar? ");
                }
            }
            else
            {
                expression.Append("jsonchar* ");
            }

            expression.Append("\"\\\"\" ws");
            AppendRule(ruleName, expression.ToString());
            return ruleName;
        }

        private string BuildStringEnumRule(JsonElement enumElement, string hint)
        {
            var literals = new List<string>();
            var dedupe = new HashSet<string>(StringComparer.Ordinal);

            foreach (JsonElement option in enumElement.EnumerateArray())
            {
                if (option.ValueKind != JsonValueKind.String)
                {
                    throw new InvalidOperationException($"Enum on schema node '{hint}' must contain string values only.");
                }

                string value = option.GetString() ?? string.Empty;
                string serialized = JsonSerializer.Serialize(value);
                if (dedupe.Add(serialized))
                {
                    literals.Add($"{QuoteGrammarLiteral(serialized)} ws");
                }
            }

            if (literals.Count == 0)
            {
                throw new InvalidOperationException($"Enum on schema node '{hint}' is empty.");
            }

            string ruleName = NextRuleName($"{hint}_enum");
            AppendRule(ruleName, string.Join(" | ", literals));
            return ruleName;
        }

        private static string BuildPropertyExpression(ObjectPropertyRule property)
        {
            string jsonName = JsonSerializer.Serialize(property.Name ?? string.Empty);
            return $"{QuoteGrammarLiteral(jsonName)} ws \":\" ws {property.ValueRule}";
        }

        private static HashSet<string> ReadRequired(JsonElement schema)
        {
            var required = new HashSet<string>(StringComparer.Ordinal);
            if (!schema.TryGetProperty("required", out JsonElement requiredElement) ||
                requiredElement.ValueKind != JsonValueKind.Array)
            {
                return required;
            }

            foreach (JsonElement item in requiredElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    string key = item.GetString();
                    if (!string.IsNullOrWhiteSpace(key))
                    {
                        required.Add(key);
                    }
                }
            }

            return required;
        }

        private static string ReadSchemaType(JsonElement schema)
        {
            if (!schema.TryGetProperty("type", out JsonElement typeElement) ||
                typeElement.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            string value = typeElement.GetString();
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return value.Trim().ToLowerInvariant();
        }

        private static bool TryReadNonNegativeInt(JsonElement schema, string propertyName, out int value)
        {
            value = 0;
            if (!schema.TryGetProperty(propertyName, out JsonElement token))
            {
                return false;
            }

            if (token.ValueKind == JsonValueKind.Number && token.TryGetInt32(out int numericValue) && numericValue >= 0)
            {
                value = numericValue;
                return true;
            }

            if (token.ValueKind == JsonValueKind.String &&
                int.TryParse(token.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int stringValue) &&
                stringValue >= 0)
            {
                value = stringValue;
                return true;
            }

            return false;
        }

        private static string BuildArrayExpression(string itemRule, int minItems, int? maxItems)
        {
            if (minItems < 0)
            {
                throw new InvalidOperationException("minItems cannot be negative.");
            }

            if (maxItems.HasValue && maxItems.Value < minItems)
            {
                throw new InvalidOperationException("maxItems cannot be smaller than minItems.");
            }

            if (maxItems.HasValue)
            {
                var alternatives = new List<string>();
                for (int count = minItems; count <= maxItems.Value; count++)
                {
                    alternatives.Add(BuildExactArrayExpression(itemRule, count));
                }

                if (alternatives.Count == 0)
                {
                    throw new InvalidOperationException("Array constraints produced no valid length.");
                }

                return string.Join(" | ", alternatives);
            }

            if (minItems == 0)
            {
                return $"\"[\" ws \"]\" ws | \"[\" ws {itemRule} (\",\" ws {itemRule})* \"]\" ws";
            }

            var expression = new StringBuilder();
            expression.Append("\"[\" ws ");
            for (int i = 0; i < minItems; i++)
            {
                if (i > 0)
                {
                    expression.Append("\",\" ws ");
                }

                expression.Append(itemRule);
            }

            expression.Append(" (\",\" ws ");
            expression.Append(itemRule);
            expression.Append(")* \"]\" ws");
            return expression.ToString();
        }

        private static string BuildExactArrayExpression(string itemRule, int count)
        {
            var expression = new StringBuilder();
            expression.Append("\"[\" ws ");
            if (count > 0)
            {
                for (int i = 0; i < count; i++)
                {
                    if (i > 0)
                    {
                        expression.Append("\",\" ws ");
                    }

                    expression.Append(itemRule);
                }
            }

            expression.Append("\"]\" ws");
            return expression.ToString();
        }

        private string NextRuleName(string hint)
        {
            return $"rule{_ruleIndex++}";
        }

        private static string QuoteGrammarLiteral(string raw)
        {
            var builder = new StringBuilder(raw.Length + 8);
            builder.Append('"');
            foreach (char c in raw)
            {
                switch (c)
                {
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        builder.Append(c);
                        break;
                }
            }

            builder.Append('"');
            return builder.ToString();
        }

        private void AppendRule(string name, string expression)
        {
            _deferredRules.Add($"{name} ::= {expression}");
        }

        private void AppendDeferredRules()
        {
            foreach (string rule in _deferredRules)
            {
                _builder.Append(rule);
                _builder.Append('\n');
            }
        }

        private void AppendPrimitiveRules()
        {
            // Compact JSON mode: enforce minified output to reduce grammar branching and token waste.
            _builder.Append("ws ::= \"\"\n");
            _builder.Append("jsonboolean ::= \"true\" ws | \"false\" ws\n");
            _builder.Append("jsoninteger ::= \"-\"? (\"0\" | [1-9] [0-9]*) ws\n");
            _builder.Append("jsonnumber ::= \"-\"? (\"0\" | [1-9] [0-9]*) (\".\" [0-9]+)? ([eE] [+-]? [0-9]+)? ws\n");
            _builder.Append("jsonstring ::= \"\\\"\" jsonchar* \"\\\"\" ws\n");
            _builder.Append("jsonchar ::= [^\"\\\\] | \"\\\\\" ([\"\\\\/bfnrt] | \"u\" [0-9a-fA-F] [0-9a-fA-F] [0-9a-fA-F] [0-9a-fA-F])\n");
        }
    }
}
