using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace DoodleDiplomacy.Core
{
    [Serializable]
    public sealed class VisualStimulusClassificationResult
    {
        public int objectCount;
        public List<string> candidates = new();
        public string error = string.Empty;

        private const string ObjectCountKey = "object_count";
        private const string CandidatesKey = "candidates";

        private static readonly HashSet<string> AllowedKeys = new(StringComparer.Ordinal)
        {
            ObjectCountKey,
            CandidatesKey,
            PromptPipelineConstants.ErrorKey
        };

        private static readonly HashSet<string> ForbiddenKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            "scan_status",
            "quality",
            "visual_tags",
            "confidence",
            "stimulus_id",
            "psychological meaning",
            "psychological_meaning",
            "description",
            "explanation"
        };

        public bool IsSuccess => string.IsNullOrWhiteSpace(error);

        public static VisualStimulusClassificationResult Failed(string message)
        {
            return new VisualStimulusClassificationResult
            {
                objectCount = 0,
                candidates = new List<string>(),
                error = string.IsNullOrWhiteSpace(message) ? "Classification unstable." : message.Trim()
            };
        }

        public static bool TryFromPipelineState(PipelineState state, out VisualStimulusClassificationResult result)
        {
            result = null;
            if (state == null)
            {
                result = Failed("Classifier returned no state.");
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
                if (IsForbiddenKey(entry.Key))
                {
                    result = Failed($"Classifier returned forbidden key '{entry.Key}'.");
                    return false;
                }

                if (!AllowedKeys.Contains(entry.Key))
                {
                    result = Failed($"Classifier returned unexpected key '{entry.Key}'.");
                    return false;
                }
            }

            if (!state.TryGetString(ObjectCountKey, out string objectCountText) ||
                !TryParseObjectCount(objectCountText, out int objectCount))
            {
                result = Failed("Classifier result is missing object_count.");
                return false;
            }

            if (!state.TryGetString(CandidatesKey, out string candidatesText) ||
                !TryParseCandidates(candidatesText, out List<string> candidates))
            {
                result = Failed("Classifier result is missing candidates.");
                return false;
            }

            result = new VisualStimulusClassificationResult
            {
                objectCount = objectCount,
                candidates = candidates,
                error = string.Empty
            };
            return true;
        }

        public static bool TryFromJson(string json, out VisualStimulusClassificationResult result)
        {
            result = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                result = Failed("Classifier returned empty JSON.");
                return false;
            }

            try
            {
                using JsonDocument document = JsonDocument.Parse(json);
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    result = Failed("Classifier JSON root is not an object.");
                    return false;
                }

                foreach (JsonProperty property in root.EnumerateObject())
                {
                    if (IsForbiddenKey(property.Name))
                    {
                        result = Failed($"Classifier returned forbidden key '{property.Name}'.");
                        return false;
                    }

                    if (!AllowedKeys.Contains(property.Name))
                    {
                        result = Failed($"Classifier returned unexpected key '{property.Name}'.");
                        return false;
                    }
                }

                if (!root.TryGetProperty(ObjectCountKey, out JsonElement objectCountElement) ||
                    !TryReadObjectCount(objectCountElement, out int objectCount))
                {
                    result = Failed("Classifier result is missing object_count.");
                    return false;
                }

                if (!root.TryGetProperty(CandidatesKey, out JsonElement candidatesElement) ||
                    !TryReadCandidates(candidatesElement, out List<string> candidates))
                {
                    result = Failed("Classifier result is missing candidates.");
                    return false;
                }

                result = new VisualStimulusClassificationResult
                {
                    objectCount = objectCount,
                    candidates = candidates,
                    error = string.Empty
                };
                return true;
            }
            catch (JsonException ex)
            {
                result = Failed($"Classifier JSON parse failed: {ex.Message}");
                return false;
            }
        }

        private static bool IsForbiddenKey(string key)
        {
            return !string.IsNullOrWhiteSpace(key) && ForbiddenKeys.Contains(key.Trim());
        }

        private static bool TryParseObjectCount(string value, out int objectCount)
        {
            objectCount = 0;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string trimmed = value.Trim();
            if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out objectCount))
            {
                return objectCount >= 0;
            }

            if (!double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out double numeric))
            {
                return false;
            }

            int rounded = (int)Math.Round(numeric);
            if (rounded < 0 || Math.Abs(numeric - rounded) > 0.0001d)
            {
                return false;
            }

            objectCount = rounded;
            return true;
        }

        private static bool TryReadObjectCount(JsonElement element, out int objectCount)
        {
            objectCount = 0;
            switch (element.ValueKind)
            {
                case JsonValueKind.Number:
                    if (element.TryGetInt32(out objectCount))
                    {
                        return objectCount >= 0;
                    }

                    if (!element.TryGetDouble(out double numeric))
                    {
                        return false;
                    }

                    int rounded = (int)Math.Round(numeric);
                    if (rounded < 0 || Math.Abs(numeric - rounded) > 0.0001d)
                    {
                        return false;
                    }

                    objectCount = rounded;
                    return true;
                case JsonValueKind.String:
                    return TryParseObjectCount(element.GetString(), out objectCount);
                default:
                    return false;
            }
        }

        private static bool TryParseCandidates(string value, out List<string> candidates)
        {
            candidates = null;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            try
            {
                using JsonDocument document = JsonDocument.Parse(value);
                return TryReadCandidates(document.RootElement, out candidates);
            }
            catch (JsonException)
            {
                candidates = null;
                return false;
            }
        }

        private static bool TryReadCandidates(JsonElement element, out List<string> candidates)
        {
            candidates = new List<string>();
            if (element.ValueKind != JsonValueKind.Array)
            {
                candidates = null;
                return false;
            }

            foreach (JsonElement item in element.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                {
                    candidates = null;
                    return false;
                }

                string candidate = item.GetString()?.Trim() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(candidate))
                {
                    candidates.Add(candidate);
                }
            }

            if (candidates.Count == 0 || candidates.Count > 3)
            {
                candidates = null;
                return false;
            }

            return true;
        }
    }
}
