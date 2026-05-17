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
        public string label = string.Empty;
        public string error = string.Empty;

        private const string ObjectCountKey = "object_count";
        private const string LabelKey = "label";

        private static readonly HashSet<string> AllowedKeys = new(StringComparer.Ordinal)
        {
            ObjectCountKey,
            LabelKey,
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

        private static readonly string[] WrittenTextPhrases =
        {
            "written text",
            "handwritten text",
            "typed text",
            "written word",
            "written words",
            "written letter",
            "written letters",
            "written number",
            "written numbers",
            "written language"
        };

        private static readonly HashSet<string> WrittenTextTokens = new(StringComparer.OrdinalIgnoreCase)
        {
            "text",
            "word",
            "words",
            "letter",
            "letters",
            "number",
            "numbers",
            "digit",
            "digits",
            "handwriting",
            "writing",
            "alphabet",
            "glyph",
            "glyphs",
            "caption",
            "captions",
            "inscription",
            "inscriptions"
        };

        public bool IsSuccess => string.IsNullOrWhiteSpace(error);

        public static VisualStimulusClassificationResult Failed(string message)
        {
            return new VisualStimulusClassificationResult
            {
                objectCount = 0,
                label = string.Empty,
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

            if (!state.TryGetString(LabelKey, out string labelText) ||
                !TryParseLabel(labelText, out string label))
            {
                result = Failed("Classifier result is missing label.");
                return false;
            }

            result = new VisualStimulusClassificationResult
            {
                objectCount = objectCount,
                label = label,
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

                if (!root.TryGetProperty(LabelKey, out JsonElement labelElement) ||
                    !TryReadLabel(labelElement, out string label))
                {
                    result = Failed("Classifier result is missing label.");
                    return false;
                }

                result = new VisualStimulusClassificationResult
                {
                    objectCount = objectCount,
                    label = label,
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

        public static bool LabelIndicatesWrittenText(string label)
        {
            if (string.IsNullOrWhiteSpace(label))
            {
                return false;
            }

            string normalized = label.Trim().ToLowerInvariant();
            foreach (string phrase in WrittenTextPhrases)
            {
                if (normalized.IndexOf(phrase, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            foreach (string token in EnumerateLabelTokens(normalized))
            {
                if (WrittenTextTokens.Contains(token))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsForbiddenKey(string key)
        {
            return !string.IsNullOrWhiteSpace(key) && ForbiddenKeys.Contains(key.Trim());
        }

        private static IEnumerable<string> EnumerateLabelTokens(string label)
        {
            int start = -1;
            for (int i = 0; i < label.Length; i++)
            {
                if (char.IsLetterOrDigit(label[i]))
                {
                    start = start < 0 ? i : start;
                    continue;
                }

                if (start >= 0)
                {
                    yield return label.Substring(start, i - start);
                    start = -1;
                }
            }

            if (start >= 0)
            {
                yield return label.Substring(start);
            }
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

        private static bool TryParseLabel(string value, out string label)
        {
            label = value?.Trim() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(label);
        }

        private static bool TryReadLabel(JsonElement element, out string label)
        {
            label = string.Empty;
            if (element.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            return TryParseLabel(element.GetString(), out label);
        }
    }
}
