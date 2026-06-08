using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using UnityEngine;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    public sealed class FirstContactQuestionProvider
    {
        private readonly FirstContactQuestionSettings _settings;
        private readonly FirstContactDebugSettings _debugSettings;

        public FirstContactQuestionProvider(
            FirstContactQuestionSettings settings,
            FirstContactDebugSettings debugSettings)
        {
            _settings = settings;
            _debugSettings = debugSettings;
        }

        public IEnumerator GetNextQuestion(
            FirstContactSessionContext context,
            int fallbackIndex,
            Action<AlienQuestion, string> onComplete)
        {
            string fallbackReason = string.Empty;
            FirstContactQuestionSettings settings = GetSettings();
            if (settings.enablePipelineGeneration && settings.questionPipeline != null)
            {
                for (int attempt = 1; attempt <= settings.maxGenerationRetries; attempt++)
                {
                    AlienQuestion generatedQuestion = null;
                    string attemptError = string.Empty;
                    yield return TryGenerateQuestionFromPipeline(
                        context,
                        attempt,
                        fallbackReason,
                        (question, error) =>
                        {
                            generatedQuestion = question;
                            attemptError = error;
                        });

                    if (generatedQuestion != null)
                    {
                        onComplete?.Invoke(generatedQuestion, string.Empty);
                        yield break;
                    }

                    fallbackReason = attemptError;
                }
            }
            else
            {
                fallbackReason = settings.questionPipeline == null
                    ? "question pipeline is not assigned"
                    : "pipeline generation is disabled";
            }

            AlienQuestion fallbackQuestion = GetFallbackQuestion(fallbackIndex);
            if (_debugSettings != null && _debugSettings.logQuestionProvider)
            {
                Debug.Log($"[FirstContactQuestionProvider] Using fallback question. reason={fallbackReason}");
            }

            onComplete?.Invoke(fallbackQuestion, fallbackReason);
        }

        private IEnumerator TryGenerateQuestionFromPipeline(
            FirstContactSessionContext context,
            int attempt,
            string rejectReason,
            Action<AlienQuestion, string> onComplete)
        {
            FirstContactQuestionSettings settings = GetSettings();
            if (GamePipelineRunner.Instance == null)
            {
                onComplete?.Invoke(null, "GamePipelineRunner is missing.");
                yield break;
            }

            PipelineState state = BuildPipelineState(context, rejectReason);
            bool done = false;
            PipelineState finalState = null;
            GamePipelineRunner.Instance.RunPipeline(settings.questionPipeline, state, result =>
            {
                finalState = result;
                done = true;
            });

            yield return new WaitUntil(() => done);

            if (finalState == null)
            {
                onComplete?.Invoke(null, "question pipeline returned no state");
                yield break;
            }

            if (finalState.TryGetString(PromptPipelineConstants.ErrorKey, out string pipelineError) &&
                !string.IsNullOrWhiteSpace(pipelineError))
            {
                onComplete?.Invoke(null, pipelineError);
                yield break;
            }

            if (!TryReadQuestionDefinition(finalState, out FirstContactQuestionDefinition definition, out string parseError))
            {
                onComplete?.Invoke(null, parseError);
                yield break;
            }

            if (!FirstContactQuestionValidator.Validate(definition, settings, out string validationError))
            {
                onComplete?.Invoke(null, validationError);
                yield break;
            }

            if (_debugSettings != null && _debugSettings.logQuestionProvider)
            {
                Debug.Log($"[FirstContactQuestionProvider] Pipeline question accepted on attempt {attempt}: {definition.questionId}");
            }

            onComplete?.Invoke(AlienQuestion.FromDefinition(definition, FirstContactQuestionSource.Pipeline), string.Empty);
        }

        private PipelineState BuildPipelineState(FirstContactSessionContext context, string rejectReason)
        {
            FirstContactQuestionSettings settings = GetSettings();
            var state = new PipelineState();
            state.SetString(settings.turnIndexKey, (context?.TurnIndex ?? 0).ToString());
            state.SetString(settings.previousAnswerLabelKey, context?.PreviousAnswer?.Label ?? string.Empty);
            state.SetString(settings.recentCardLabelsKey, BuildRecentCardLabels(context));
            state.SetString(settings.stableClustersKey, BuildStableClusterSummary(context));
            if (!string.IsNullOrWhiteSpace(rejectReason))
            {
                state.SetString(settings.rejectReasonKey, rejectReason);
            }

            return state;
        }

        private static string BuildRecentCardLabels(FirstContactSessionContext context)
        {
            if (context == null || context.RecentCards.Count == 0)
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            int start = Mathf.Max(0, context.RecentCards.Count - 10);
            for (int i = start; i < context.RecentCards.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(context.RecentCards[i].Label))
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append(", ");
                }

                builder.Append(context.RecentCards[i].Label);
            }

            return builder.ToString();
        }

        private static string BuildStableClusterSummary(FirstContactSessionContext context)
        {
            if (context == null || context.StableClusters.Count == 0)
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            for (int i = 0; i < context.StableClusters.Count; i++)
            {
                SemanticClusterRecord cluster = context.StableClusters[i];
                if (cluster == null)
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append("; ");
                }

                builder.Append(cluster.DisplayName);
            }

            return builder.ToString();
        }

        private bool TryReadQuestionDefinition(
            PipelineState state,
            out FirstContactQuestionDefinition definition,
            out string error)
        {
            definition = null;
            error = string.Empty;
            FirstContactQuestionSettings settings = GetSettings();

            if (state.TryGetString(settings.outputQuestionJsonKey, out string rawJson) &&
                !string.IsNullOrWhiteSpace(rawJson))
            {
                return TryParseQuestionJson(rawJson, out definition, out error);
            }

            return TryBuildQuestionFromStateFields(state, out definition, out error);
        }

        private static bool TryParseQuestionJson(
            string rawJson,
            out FirstContactQuestionDefinition definition,
            out string error)
        {
            definition = null;
            error = string.Empty;
            try
            {
                using JsonDocument document = JsonDocument.Parse(rawJson);
                return TryReadQuestionObject(document.RootElement, out definition, out error);
            }
            catch (Exception ex)
            {
                error = $"question_json parse failed: {ex.Message}";
                return false;
            }
        }

        private static bool TryBuildQuestionFromStateFields(
            PipelineState state,
            out FirstContactQuestionDefinition definition,
            out string error)
        {
            definition = new FirstContactQuestionDefinition();
            error = string.Empty;

            definition.questionId = ReadFirst(state, "questionId", "question_id", "id");
            definition.internalIntent = ReadFirst(state, "internalIntent", "internal_intent", "intent");

            string primitiveTokensText = ReadFirst(state, "primitiveTokens", "primitive_tokens", "tokens");
            definition.primitiveTokens = ParseStringArray(primitiveTokensText);
            string unknownsText = ReadFirst(state, "unknownSlots", "unknowns", "unknown_slots");
            if (!TryParseUnknowns(unknownsText, out FirstContactUnknownSlotDefinition[] unknowns, out error))
            {
                return false;
            }

            definition.unknownSlots = unknowns;
            return definition.primitiveTokens.Length > 0;
        }

        private static bool TryReadQuestionObject(
            JsonElement root,
            out FirstContactQuestionDefinition definition,
            out string error)
        {
            definition = null;
            error = string.Empty;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "question payload root is not an object";
                return false;
            }

            definition = new FirstContactQuestionDefinition
            {
                questionId = ReadString(root, "questionId", "question_id", "id"),
                internalIntent = ReadString(root, "internalIntent", "internal_intent", "intent"),
                primitiveTokens = ReadStringArray(root, "primitiveTokens", "primitive_tokens", "tokens")
            };

            JsonElement unknownsElement;
            if (!TryGetProperty(root, out unknownsElement, "unknownSlots", "unknowns", "unknown_slots") ||
                unknownsElement.ValueKind != JsonValueKind.Array)
            {
                error = "question payload is missing unknowns array";
                return false;
            }

            var unknowns = new List<FirstContactUnknownSlotDefinition>();
            foreach (JsonElement item in unknownsElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var slot = new FirstContactUnknownSlotDefinition
                {
                    id = ReadString(item, "id", "unknownId", "unknown_id"),
                    targetConcept = ReadString(item, "targetConcept", "target_concept"),
                    targetAnchors = ReadStringArray(item, "targetAnchors", "target_anchors", "anchors")
                };

                if (TryGetProperty(item, out JsonElement stages, "stages", "stageTexts", "stage_texts") &&
                    stages.ValueKind == JsonValueKind.Object)
                {
                    slot.stageTexts = new FirstContactStageTexts
                    {
                        hint = ReadString(stages, "hint"),
                        partial = ReadString(stages, "partial"),
                        solved = ReadString(stages, "solved")
                    };
                }

                unknowns.Add(slot);
            }

            definition.unknownSlots = unknowns.ToArray();
            return true;
        }

        private static string ReadFirst(PipelineState state, params string[] keys)
        {
            for (int i = 0; i < keys.Length; i++)
            {
                if (state.TryGetString(keys[i], out string value) && !string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }

            return string.Empty;
        }

        private static bool TryParseUnknowns(
            string value,
            out FirstContactUnknownSlotDefinition[] unknowns,
            out string error)
        {
            unknowns = Array.Empty<FirstContactUnknownSlotDefinition>();
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(value))
            {
                error = "unknowns field is empty";
                return false;
            }

            try
            {
                using JsonDocument document = JsonDocument.Parse(value);
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Array)
                {
                    error = "unknowns field is not an array";
                    return false;
                }

                var wrapped = "{\"primitiveTokens\":[],\"unknowns\":" + root.GetRawText() + "}";
                using JsonDocument wrapper = JsonDocument.Parse(wrapped);
                bool ok = TryReadQuestionObject(wrapper.RootElement, out FirstContactQuestionDefinition definition, out error);
                unknowns = ok ? definition.unknownSlots : Array.Empty<FirstContactUnknownSlotDefinition>();
                return ok;
            }
            catch (Exception ex)
            {
                error = $"unknowns parse failed: {ex.Message}";
                return false;
            }
        }

        private static string ReadString(JsonElement element, params string[] names)
        {
            if (!TryGetProperty(element, out JsonElement value, names))
            {
                return string.Empty;
            }

            return value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() ?? string.Empty : value.ToString();
        }

        private static string[] ReadStringArray(JsonElement element, params string[] names)
        {
            if (!TryGetProperty(element, out JsonElement value, names))
            {
                return Array.Empty<string>();
            }

            if (value.ValueKind == JsonValueKind.Array)
            {
                var result = new List<string>();
                foreach (JsonElement item in value.EnumerateArray())
                {
                    string text = item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        result.Add(text.Trim());
                    }
                }

                return result.ToArray();
            }

            return ParseStringArray(value.ToString());
        }

        private static string[] ParseStringArray(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return Array.Empty<string>();
            }

            string trimmed = value.Trim();
            if (trimmed.StartsWith("[", StringComparison.Ordinal))
            {
                try
                {
                    using JsonDocument document = JsonDocument.Parse(trimmed);
                    if (document.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        var result = new List<string>();
                        foreach (JsonElement item in document.RootElement.EnumerateArray())
                        {
                            string text = item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString();
                            if (!string.IsNullOrWhiteSpace(text))
                            {
                                result.Add(text.Trim());
                            }
                        }

                        return result.ToArray();
                    }
                }
                catch
                {
                    // Fall through to delimiter parsing.
                }
            }

            string[] pieces = trimmed.Split(new[] { '/', ',', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < pieces.Length; i++)
            {
                pieces[i] = pieces[i].Trim();
            }

            return pieces;
        }

        private static bool TryGetProperty(JsonElement element, out JsonElement value, params string[] names)
        {
            for (int i = 0; i < names.Length; i++)
            {
                if (element.TryGetProperty(names[i], out value))
                {
                    return true;
                }
            }

            value = default;
            return false;
        }

        private AlienQuestion GetFallbackQuestion(int fallbackIndex)
        {
            FirstContactQuestionSettings settings = GetSettings();
            FirstContactQuestionDefinition definition = null;
            if (settings.fallbackQuestionSet != null &&
                settings.fallbackQuestionSet.questions != null &&
                settings.fallbackQuestionSet.questions.Length > 0)
            {
                int count = settings.fallbackQuestionSet.questions.Length;
                int index = settings.loopFallbackQuestions
                    ? Mathf.Abs(fallbackIndex) % count
                    : Mathf.Clamp(fallbackIndex, 0, count - 1);
                definition = settings.fallbackQuestionSet.questions[index];
            }

            definition ??= FirstContactRuntimeFallbackQuestions.GetQuestion(fallbackIndex);
            return AlienQuestion.FromDefinition(definition, FirstContactQuestionSource.Fallback);
        }

        private FirstContactQuestionSettings GetSettings()
        {
            return _settings != null ? _settings : ScriptableObject.CreateInstance<FirstContactQuestionSettings>();
        }
    }

    public static class FirstContactQuestionValidator
    {
        public static bool Validate(
            FirstContactQuestionDefinition definition,
            FirstContactQuestionSettings settings,
            out string error)
        {
            error = string.Empty;
            if (definition == null)
            {
                error = "question definition is null";
                return false;
            }

            if (definition.primitiveTokens == null || definition.primitiveTokens.Length == 0)
            {
                error = "primitive token stream is empty";
                return false;
            }

            if (settings != null && settings.requireSelectOne)
            {
                string last = definition.primitiveTokens[definition.primitiveTokens.Length - 1]?.Trim();
                if (!string.Equals(last, "SELECT-ONE", StringComparison.OrdinalIgnoreCase))
                {
                    error = "primitive token stream must end with SELECT-ONE";
                    return false;
                }
            }

            if (settings?.bannedTokens != null)
            {
                for (int i = 0; i < definition.primitiveTokens.Length; i++)
                {
                    string token = FirstContactUnknownSlotDefinition.NormalizeUnknownId(definition.primitiveTokens[i]);
                    for (int b = 0; b < settings.bannedTokens.Length; b++)
                    {
                        if (string.Equals(token, settings.bannedTokens[b], StringComparison.OrdinalIgnoreCase))
                        {
                            error = $"banned token '{settings.bannedTokens[b]}' is present";
                            return false;
                        }
                    }
                }
            }

            int unknownCount = definition.unknownSlots?.Length ?? 0;
            int min = settings != null ? settings.minUnknownCount : 1;
            int max = settings != null ? settings.maxUnknownCount : 3;
            if (unknownCount < min || unknownCount > max)
            {
                error = $"unknown count {unknownCount} is outside {min}-{max}";
                return false;
            }

            for (int i = 0; i < unknownCount; i++)
            {
                FirstContactUnknownSlotDefinition slot = definition.unknownSlots[i];
                if (slot == null || string.IsNullOrWhiteSpace(slot.id))
                {
                    error = "unknown slot is missing id";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(slot.targetConcept) &&
                    (slot.targetAnchors == null || slot.targetAnchors.Length == 0))
                {
                    error = $"unknown slot '{slot.id}' is missing target concept/anchors";
                    return false;
                }
            }

            return true;
        }
    }

    public static class FirstContactRuntimeFallbackQuestions
    {
        private static readonly FirstContactQuestionDefinition[] Questions =
        {
            new()
            {
                questionId = "fc-authored-001",
                internalIntent = "What is one important thing on Earth?",
                primitiveTokens = new[] { "YOU", "EARTH", "[UNKNOWN-01]", "SELECT-ONE" },
                unknownSlots = new[]
                {
                    new FirstContactUnknownSlotDefinition
                    {
                        id = "UNKNOWN-01",
                        targetConcept = "important",
                        targetAnchors = new[] { "important", "valuable", "precious", "value", "treasure", "life" },
                        stageTexts = new FirstContactStageTexts
                        {
                            hint = "[QUALITY?]",
                            partial = "[VALUE-RELATED?]",
                            solved = "HIGH-VALUE"
                        }
                    }
                }
            },
            new()
            {
                questionId = "fc-authored-002",
                internalIntent = "What does humanity protect?",
                primitiveTokens = new[] { "YOU", "[UNKNOWN-01]", "PROTECT", "SELECT-ONE" },
                unknownSlots = new[]
                {
                    new FirstContactUnknownSlotDefinition
                    {
                        id = "UNKNOWN-01",
                        targetConcept = "humanity",
                        targetAnchors = new[] { "human", "humanity", "people", "person", "your kind", "family" },
                        stageTexts = new FirstContactStageTexts
                        {
                            hint = "[WHO?]",
                            partial = "[YOUR-KIND?]",
                            solved = "HUMAN"
                        }
                    }
                }
            },
            new()
            {
                questionId = "fc-authored-003",
                internalIntent = "What object do humans choose in danger?",
                primitiveTokens = new[] { "YOU", "DANGER", "[UNKNOWN-01]", "SELECT-ONE" },
                unknownSlots = new[]
                {
                    new FirstContactUnknownSlotDefinition
                    {
                        id = "UNKNOWN-01",
                        targetConcept = "response object",
                        targetAnchors = new[] { "tool", "weapon", "shield", "defense", "protective object", "phone", "door" },
                        stageTexts = new FirstContactStageTexts
                        {
                            hint = "[OBJECT?]",
                            partial = "[DANGER-RESPONSE?]",
                            solved = "RESPONSE-OBJECT"
                        }
                    }
                }
            },
            new()
            {
                questionId = "fc-authored-004",
                internalIntent = "What does humanity fear?",
                primitiveTokens = new[] { "YOU", "HUMAN", "[UNKNOWN-01]", "SELECT-ONE" },
                unknownSlots = new[]
                {
                    new FirstContactUnknownSlotDefinition
                    {
                        id = "UNKNOWN-01",
                        targetConcept = "fear",
                        targetAnchors = new[] { "fear", "danger", "death", "fire", "weapon", "monster", "threat" },
                        stageTexts = new FirstContactStageTexts
                        {
                            hint = "[FEELING?]",
                            partial = "[THREAT-RELATED?]",
                            solved = "FEAR"
                        }
                    }
                }
            },
            new()
            {
                questionId = "fc-authored-005",
                internalIntent = "What object protects a home?",
                primitiveTokens = new[] { "YOU", "HOME", "[UNKNOWN-01]", "PROTECT", "SELECT-ONE" },
                unknownSlots = new[]
                {
                    new FirstContactUnknownSlotDefinition
                    {
                        id = "UNKNOWN-01",
                        targetConcept = "defense",
                        targetAnchors = new[] { "defense", "protection", "shield", "wall", "helmet", "bunker", "lock", "door", "guard" },
                        stageTexts = new FirstContactStageTexts
                        {
                            hint = "[OBJECT?]",
                            partial = "[DEFENSE-RELATED?]",
                            solved = "DEFENSE"
                        }
                    }
                }
            }
        };

        public static FirstContactQuestionDefinition GetQuestion(int index)
        {
            int safeIndex = Questions.Length == 0 ? 0 : Mathf.Clamp(index, 0, Questions.Length - 1);
            return Questions[safeIndex];
        }

        public static int Count => Questions.Length;
    }
}
