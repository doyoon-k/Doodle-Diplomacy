using System;
using DoodleDiplomacy.Core;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    public sealed class FirstContactProbeLabelResult
    {
        private const string CanonicalLabelKey = "canonical_label";
        private const string IsSuitableKey = "is_suitable";
        private const string ReasonKey = "reason";

        public string CanonicalLabel = string.Empty;
        public bool IsSuitable = true;
        public string Reason = string.Empty;
        public string Error = string.Empty;

        public bool IsSuccess => string.IsNullOrWhiteSpace(Error);

        public static FirstContactProbeLabelResult Fallback(string label, string reason = null)
        {
            return new FirstContactProbeLabelResult
            {
                CanonicalLabel = label ?? string.Empty,
                IsSuitable = true,
                Reason = reason ?? string.Empty
            };
        }

        public static FirstContactProbeLabelResult Failed(string message)
        {
            return new FirstContactProbeLabelResult
            {
                Error = string.IsNullOrWhiteSpace(message)
                    ? "Probe label processing failed."
                    : message.Trim()
            };
        }

        public static bool TryFromPipelineState(PipelineState state, out FirstContactProbeLabelResult result)
        {
            result = null;
            if (state == null)
            {
                result = Failed("Label pipeline returned no state.");
                return false;
            }

            if (state.TryGetString(PromptPipelineConstants.ErrorKey, out string pipelineError) &&
                !string.IsNullOrWhiteSpace(pipelineError))
            {
                result = Failed(pipelineError);
                return false;
            }

            if (!state.TryGetString(CanonicalLabelKey, out string canonicalLabel) ||
                string.IsNullOrWhiteSpace(canonicalLabel))
            {
                result = Failed("Label pipeline returned no canonical_label.");
                return false;
            }

            if (!TryReadBool(state, IsSuitableKey, out bool isSuitable))
            {
                result = Failed("Label pipeline returned no is_suitable.");
                return false;
            }

            state.TryGetString(ReasonKey, out string reason);
            result = new FirstContactProbeLabelResult
            {
                CanonicalLabel = canonicalLabel.Trim(),
                IsSuitable = isSuitable,
                Reason = reason?.Trim() ?? string.Empty
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
    }

}
