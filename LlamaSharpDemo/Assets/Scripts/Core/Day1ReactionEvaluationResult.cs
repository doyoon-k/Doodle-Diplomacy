using System;
using System.Collections.Generic;
using System.Text.Json;

namespace DoodleDiplomacy.Core
{
    [Serializable]
    public sealed class Day1ReactionEvaluationResult
    {
        public ReactionTier reactionTier = ReactionTier.Subtle;
        public string reason = string.Empty;
        public string error = string.Empty;

        private const string ReactionTierKey = "reaction_tier";
        private const string ReasonKey = "reason";

        private static readonly HashSet<string> AllowedKeys = new(StringComparer.Ordinal)
        {
            ReactionTierKey,
            ReasonKey,
            PromptPipelineConstants.ErrorKey
        };

        public bool IsSuccess => string.IsNullOrWhiteSpace(error);

        public static Day1ReactionEvaluationResult Failed(string message)
        {
            return new Day1ReactionEvaluationResult
            {
                reactionTier = ReactionTier.Subtle,
                reason = string.Empty,
                error = string.IsNullOrWhiteSpace(message)
                    ? "Reaction evaluation failed."
                    : message.Trim()
            };
        }

        public static bool TryFromPipelineState(PipelineState state, out Day1ReactionEvaluationResult result)
        {
            result = null;
            if (state == null)
            {
                result = Failed("Reaction evaluator returned no state.");
                return false;
            }

            if (state.TryGetString(PromptPipelineConstants.ErrorKey, out string pipelineError) &&
                !string.IsNullOrWhiteSpace(pipelineError))
            {
                result = Failed(pipelineError);
                return false;
            }

            foreach (KeyValuePair<string, string> entry in state.TextEntries)
            {
                if (!AllowedKeys.Contains(entry.Key))
                {
                    result = Failed($"Reaction evaluator returned unexpected key '{entry.Key}'.");
                    return false;
                }
            }

            if (!state.TryGetString(ReactionTierKey, out string tierText) ||
                !TryParseReactionTier(tierText, out ReactionTier tier))
            {
                result = Failed("Reaction evaluator result is missing reaction_tier.");
                return false;
            }

            state.TryGetString(ReasonKey, out string reason);
            result = new Day1ReactionEvaluationResult
            {
                reactionTier = tier,
                reason = reason?.Trim() ?? string.Empty,
                error = string.Empty
            };
            return true;
        }

        public static bool TryFromJson(string json, out Day1ReactionEvaluationResult result)
        {
            result = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                result = Failed("Reaction evaluator returned empty JSON.");
                return false;
            }

            try
            {
                using JsonDocument document = JsonDocument.Parse(json);
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    result = Failed("Reaction evaluator JSON root is not an object.");
                    return false;
                }

                foreach (JsonProperty property in root.EnumerateObject())
                {
                    if (!AllowedKeys.Contains(property.Name))
                    {
                        result = Failed($"Reaction evaluator returned unexpected key '{property.Name}'.");
                        return false;
                    }
                }

                if (!root.TryGetProperty(ReactionTierKey, out JsonElement tierElement) ||
                    tierElement.ValueKind != JsonValueKind.String ||
                    !TryParseReactionTier(tierElement.GetString(), out ReactionTier tier))
                {
                    result = Failed("Reaction evaluator result is missing reaction_tier.");
                    return false;
                }

                string reason = string.Empty;
                if (root.TryGetProperty(ReasonKey, out JsonElement reasonElement))
                {
                    if (reasonElement.ValueKind != JsonValueKind.String)
                    {
                        result = Failed("Reaction evaluator reason must be a string.");
                        return false;
                    }

                    reason = reasonElement.GetString()?.Trim() ?? string.Empty;
                }

                result = new Day1ReactionEvaluationResult
                {
                    reactionTier = tier,
                    reason = reason,
                    error = string.Empty
                };
                return true;
            }
            catch (JsonException ex)
            {
                result = Failed($"Reaction evaluator JSON parse failed: {ex.Message}");
                return false;
            }
        }

        private static bool TryParseReactionTier(string value, out ReactionTier tier)
        {
            tier = ReactionTier.Subtle;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            switch (value.Trim().ToLowerInvariant())
            {
                case "none":
                case "0":
                    tier = ReactionTier.None;
                    return true;
                case "subtle":
                case "1":
                    tier = ReactionTier.Subtle;
                    return true;
                case "moderate":
                case "2":
                    tier = ReactionTier.Moderate;
                    return true;
                case "strong":
                case "3":
                    tier = ReactionTier.Strong;
                    return true;
                default:
                    return false;
            }
        }
    }
}
