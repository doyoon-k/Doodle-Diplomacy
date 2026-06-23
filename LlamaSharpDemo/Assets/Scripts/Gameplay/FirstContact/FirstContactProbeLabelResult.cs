using System;
using DoodleDiplomacy.Core;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    public sealed class FirstContactProbeLabelResult
    {
        private const string CanonicalLabelKey = "canonical_label";
        private const string HasClassificationClaimKey = "has_classification_claim";
        private const string ClassificationClaimTextKey = "classification_claim_text";
        private const string NeutralSubjectLabelKey = "neutral_subject_label";
        private const string LabelReasonKey = "label_reason";
        private const string IsSuitableKey = "is_suitable";
        private const string ReasonKey = "reason";

        public string CanonicalLabel = string.Empty;
        public bool HasClassificationClaim;
        public string ClassificationClaimText = string.Empty;
        public string NeutralSubjectLabel = string.Empty;
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

            if (!TryReadBool(state, HasClassificationClaimKey, out bool hasClassificationClaim))
            {
                state.TryGetString(LabelReasonKey, out string missingSignalReason);
                result = new FirstContactProbeLabelResult
                {
                    CanonicalLabel = canonicalLabel.Trim(),
                    HasClassificationClaim = true,
                    IsSuitable = false,
                    Reason = string.IsNullOrWhiteSpace(missingSignalReason)
                        ? "Label classifier returned no classification-claim signal."
                        : missingSignalReason.Trim()
                };
                return true;
            }

            state.TryGetString(ClassificationClaimTextKey, out string classificationClaimText);
            classificationClaimText = classificationClaimText?.Trim() ?? string.Empty;
            state.TryGetString(NeutralSubjectLabelKey, out string neutralSubjectLabel);
            neutralSubjectLabel = string.IsNullOrWhiteSpace(neutralSubjectLabel)
                ? canonicalLabel.Trim()
                : neutralSubjectLabel.Trim();
            if (hasClassificationClaim &&
                !HasSupportedClassificationClaim(
                    state,
                    canonicalLabel,
                    classificationClaimText,
                    neutralSubjectLabel))
            {
                hasClassificationClaim = false;
                classificationClaimText = string.Empty;
                neutralSubjectLabel = canonicalLabel.Trim();
            }

            state.TryGetString(LabelReasonKey, out string labelReason);
            state.TryGetString(ReasonKey, out string reason);
            string finalReason = reason?.Trim() ?? string.Empty;
            if (hasClassificationClaim)
            {
                isSuitable = false;
                finalReason = string.IsNullOrWhiteSpace(labelReason)
                    ? "Label includes a classification claim instead of only the subject name."
                    : labelReason.Trim();
            }

            result = new FirstContactProbeLabelResult
            {
                CanonicalLabel = canonicalLabel.Trim(),
                HasClassificationClaim = hasClassificationClaim,
                ClassificationClaimText = classificationClaimText,
                NeutralSubjectLabel = neutralSubjectLabel,
                IsSuitable = isSuitable,
                Reason = finalReason
            };
            return true;
        }

        private static bool HasSupportedClassificationClaim(
            PipelineState state,
            string canonicalLabel,
            string claimText,
            string neutralSubjectLabel)
        {
            if (ClaimTextCanBeRemovedFromLabelState(state, canonicalLabel, claimText))
            {
                return true;
            }

            return NeutralSubjectIsReduction(state, canonicalLabel, neutralSubjectLabel);
        }

        private static bool ClaimTextCanBeRemovedFromLabelState(
            PipelineState state,
            string canonicalLabel,
            string claimText)
        {
            if (string.IsNullOrWhiteSpace(claimText))
            {
                return false;
            }

            if (CanRemoveNormalized(canonicalLabel, claimText))
            {
                return true;
            }

            if (state != null &&
                state.TryGetString("probe_display_label", out string displayLabel) &&
                CanRemoveNormalized(displayLabel, claimText))
            {
                return true;
            }

            return false;
        }

        private static bool NeutralSubjectIsReduction(
            PipelineState state,
            string canonicalLabel,
            string neutralSubjectLabel)
        {
            string normalizedNeutral = NormalizeForClaimCheck(neutralSubjectLabel);
            if (string.IsNullOrWhiteSpace(normalizedNeutral))
            {
                return false;
            }

            if (IsStrictNormalizedSubphrase(canonicalLabel, normalizedNeutral))
            {
                return true;
            }

            return state != null &&
                   state.TryGetString("probe_display_label", out string displayLabel) &&
                   IsStrictNormalizedSubphrase(displayLabel, normalizedNeutral);
        }

        private static bool IsStrictNormalizedSubphrase(string source, string normalizedSubphrase)
        {
            string normalizedSource = NormalizeForClaimCheck(source);
            return !string.IsNullOrWhiteSpace(normalizedSource) &&
                   !string.Equals(normalizedSource, normalizedSubphrase, StringComparison.Ordinal) &&
                   normalizedSource.Contains(normalizedSubphrase);
        }

        private static bool CanRemoveNormalized(string source, string value)
        {
            string normalizedSource = NormalizeForClaimCheck(source);
            string normalizedValue = NormalizeForClaimCheck(value);
            if (string.IsNullOrWhiteSpace(normalizedSource) ||
                string.IsNullOrWhiteSpace(normalizedValue))
            {
                return false;
            }

            int index = normalizedSource.IndexOf(normalizedValue, StringComparison.Ordinal);
            if (index < 0)
            {
                return false;
            }

            string remaining = normalizedSource.Remove(index, normalizedValue.Length);
            return remaining.Length > 0;
        }

        private static string NormalizeForClaimCheck(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            Span<char> buffer = stackalloc char[text.Length];
            int count = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = char.ToLowerInvariant(text[i]);
                if (char.IsWhiteSpace(c) || char.IsPunctuation(c) || char.IsSymbol(c))
                {
                    continue;
                }

                buffer[count++] = c;
            }

            return new string(buffer[..count]);
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
