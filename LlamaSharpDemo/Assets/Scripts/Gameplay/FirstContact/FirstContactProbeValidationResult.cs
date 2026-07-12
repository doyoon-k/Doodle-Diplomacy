using System;
using System.Collections.Generic;
using DoodleDiplomacy.Core;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    public enum FirstContactProbeVisualIssue
    {
        Blank,
        TextOrSymbol,
        SceneOrAction,
        MultipleObjects,
        UnresolvedObject
    }

    public sealed class FirstContactProbeValidationResult
    {
        private const string IsBlankKey = "is_blank";
        private const string ObjectCountKey = "object_count";
        private const string HasTextOrSymbolKey = "has_text_or_symbol";
        private const string IsSceneOrActionKey = "is_scene_or_action";
        private const string LabelMatchKey = "label_match";

        public bool IsBlank;
        public int ObjectCount;
        public bool HasTextOrSymbol;
        public bool IsSceneOrAction;
        public string LabelMatch = "unclear";
        public string Error = string.Empty;

        public bool IsSuccess => string.IsNullOrWhiteSpace(Error);
        public bool IsLabelMismatch => string.Equals(LabelMatch, "mismatch", StringComparison.OrdinalIgnoreCase);

        public IReadOnlyList<FirstContactProbeVisualIssue> CollectRejectedVisualIssues(
            FirstContactVlmSettings settings)
        {
            var issues = new List<FirstContactProbeVisualIssue>();
            if (settings == null)
            {
                return issues;
            }

            if (settings.rejectBlank && IsBlank)
            {
                issues.Add(FirstContactProbeVisualIssue.Blank);
                return issues;
            }

            if (settings.rejectWrittenText && HasTextOrSymbol)
            {
                issues.Add(FirstContactProbeVisualIssue.TextOrSymbol);
            }

            if (settings.rejectActionOrScene && IsSceneOrAction)
            {
                issues.Add(FirstContactProbeVisualIssue.SceneOrAction);
            }

            if (settings.rejectMultipleObjects)
            {
                if (ObjectCount > 1)
                {
                    issues.Add(FirstContactProbeVisualIssue.MultipleObjects);
                }
                else if (ObjectCount <= 0 && !IsSceneOrAction && !HasTextOrSymbol)
                {
                    issues.Add(FirstContactProbeVisualIssue.UnresolvedObject);
                }
            }

            return issues;
        }

        public static FirstContactProbeValidationResult PassedUnchecked(string reason = null)
        {
            return new FirstContactProbeValidationResult
            {
                IsBlank = false,
                ObjectCount = 1,
                HasTextOrSymbol = false,
                IsSceneOrAction = false,
                LabelMatch = "unclear",
                Error = string.Empty
            };
        }

        public static FirstContactProbeValidationResult Failed(string message)
        {
            return new FirstContactProbeValidationResult
            {
                Error = string.IsNullOrWhiteSpace(message) ? "Probe validation failed." : message.Trim()
            };
        }

        public static bool TryFromPipelineState(PipelineState state, out FirstContactProbeValidationResult result)
        {
            result = null;
            if (state == null)
            {
                result = Failed("Validator returned no state.");
                return false;
            }

            if (state.TryGetString(PromptPipelineConstants.ErrorKey, out string pipelineError) &&
                !string.IsNullOrWhiteSpace(pipelineError))
            {
                result = Failed(pipelineError);
                return false;
            }

            if (!TryReadBool(state, IsBlankKey, out bool isBlank))
            {
                result = Failed("Validator result is missing is_blank.");
                return false;
            }

            if (!TryReadInt(state, ObjectCountKey, out int objectCount))
            {
                result = Failed("Validator result is missing object_count.");
                return false;
            }

            if (!TryReadBool(state, HasTextOrSymbolKey, out bool hasTextOrSymbol))
            {
                result = Failed("Validator result is missing has_text_or_symbol.");
                return false;
            }

            if (!TryReadBool(state, IsSceneOrActionKey, out bool isSceneOrAction))
            {
                result = Failed("Validator result is missing is_scene_or_action.");
                return false;
            }

            if (!state.TryGetString(LabelMatchKey, out string labelMatch) ||
                !TryNormalizeLabelMatch(labelMatch, out string normalizedLabelMatch))
            {
                result = Failed("Validator result is missing label_match.");
                return false;
            }

            objectCount = Math.Max(0, objectCount);
            if (isBlank || isSceneOrAction)
            {
                objectCount = 0;
            }

            if (objectCount == 0 &&
                string.Equals(normalizedLabelMatch, "match", StringComparison.OrdinalIgnoreCase))
            {
                normalizedLabelMatch = "unclear";
            }

            result = new FirstContactProbeValidationResult
            {
                IsBlank = isBlank,
                ObjectCount = objectCount,
                HasTextOrSymbol = hasTextOrSymbol,
                IsSceneOrAction = isSceneOrAction,
                LabelMatch = normalizedLabelMatch,
                Error = string.Empty
            };
            return true;
        }

        private static bool TryReadBool(PipelineState state, string key, out bool value)
        {
            value = false;
            if (state == null || !state.TryGetString(key, out string text))
            {
                return false;
            }

            text = text?.Trim();
            if (bool.TryParse(text, out value))
            {
                return true;
            }

            if (string.Equals(text, "1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "yes", StringComparison.OrdinalIgnoreCase))
            {
                value = true;
                return true;
            }

            if (string.Equals(text, "0", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "no", StringComparison.OrdinalIgnoreCase))
            {
                value = false;
                return true;
            }

            return false;
        }

        private static bool TryReadInt(PipelineState state, string key, out int value)
        {
            value = 0;
            if (state == null || !state.TryGetString(key, out string text))
            {
                return false;
            }

            if (int.TryParse(text?.Trim(), out value))
            {
                return true;
            }

            if (float.TryParse(text?.Trim(), out float floatValue))
            {
                value = (int)Math.Round(floatValue);
                return true;
            }

            return false;
        }

        private static bool TryNormalizeLabelMatch(string value, out string normalized)
        {
            normalized = string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Trim().ToLowerInvariant();
            if (normalized == "match" || normalized == "unclear" || normalized == "mismatch")
            {
                return true;
            }

            return false;
        }
    }
}
