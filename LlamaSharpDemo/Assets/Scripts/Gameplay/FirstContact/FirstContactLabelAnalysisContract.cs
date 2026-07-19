using System;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    public sealed class FirstContactLabelAnalysisData
    {
        public string Decision = FirstContactLabelAnalysisContract.UnclearDecision;
        public string ClassificationClaimText = string.Empty;
        public string NeutralSubjectLabel = string.Empty;
    }

    public static class FirstContactLabelAnalysisContract
    {
        public const string DecisionKey = "label_decision";
        public const string ClassificationClaimTextKey = "classification_claim_text";
        public const string NeutralSubjectLabelKey = "neutral_subject_label";
        public const string InconclusiveKey = "label_analysis_inconclusive";
        public const string ContractErrorKey = "label_analysis_contract_error";

        public const string AcceptDecision = "accept";
        public const string ActionOrAbstractDecision = "action_or_abstract";
        public const string BroadCategoryDecision = "broad_category";
        public const string MultipleSubjectsDecision = "multiple_subjects";
        public const string ClassificationClaimDecision = "classification_claim";
        public const string UnclearDecision = "unclear";

        public static bool TryValidate(
            PipelineState state,
            out FirstContactLabelAnalysisData data,
            out string error)
        {
            data = null;
            error = string.Empty;
            if (state == null)
            {
                error = "Label analysis returned no state.";
                return false;
            }

            string originalLabel = state.GetString("probe_display_label", string.Empty).Trim();
            string decision = state.GetString(DecisionKey, string.Empty).Trim().ToLowerInvariant();
            string claimText = state.GetString(ClassificationClaimTextKey, string.Empty).Trim();
            string neutralSubject = state.GetString(NeutralSubjectLabelKey, string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(originalLabel))
            {
                error = "probe_display_label is empty.";
                return false;
            }

            if (!IsKnownDecision(decision))
            {
                error = $"label_decision '{decision}' is not supported.";
                return false;
            }

            switch (decision)
            {
                case AcceptDecision:
                    if (!string.IsNullOrWhiteSpace(claimText))
                    {
                        error = "accept requires an empty classification_claim_text.";
                        return false;
                    }

                    // The application already owns the exact player input. Do not depend on a
                    // generative model to reproduce deterministic source data without changes.
                    neutralSubject = originalLabel;
                    break;

                case ClassificationClaimDecision:
                    if (string.IsNullOrWhiteSpace(claimText))
                    {
                        error = "classification_claim requires a non-empty claim phrase.";
                        return false;
                    }

                    if (string.IsNullOrWhiteSpace(neutralSubject))
                    {
                        error = "classification_claim requires a non-empty neutral subject.";
                        return false;
                    }

                    if (!IsProperRemovablePhrase(originalLabel, claimText))
                    {
                        error = "The claim phrase must be a removable proper part of the original player label.";
                        return false;
                    }

                    if (!IsStrictReduction(originalLabel, neutralSubject))
                    {
                        error = "neutral_subject_label must be a strict reduction of the original player label.";
                        return false;
                    }
                    break;

                case ActionOrAbstractDecision:
                case BroadCategoryDecision:
                case MultipleSubjectsDecision:
                case UnclearDecision:
                    if (!string.IsNullOrWhiteSpace(claimText))
                    {
                        error = $"{decision} requires an empty classification_claim_text.";
                        return false;
                    }

                    if (!string.IsNullOrWhiteSpace(neutralSubject))
                    {
                        error = $"{decision} requires an empty neutral_subject_label.";
                        return false;
                    }
                    break;
            }

            data = new FirstContactLabelAnalysisData
            {
                Decision = decision,
                ClassificationClaimText = claimText,
                NeutralSubjectLabel = neutralSubject
            };
            return true;
        }

        public static void ApplyInconclusive(PipelineState state, string error)
        {
            if (state == null)
            {
                return;
            }

            state.SetString(DecisionKey, UnclearDecision);
            state.SetString(ClassificationClaimTextKey, string.Empty);
            state.SetString(NeutralSubjectLabelKey, string.Empty);
            state.SetString(InconclusiveKey, "true");
            state.SetString(ContractErrorKey, error ?? string.Empty);
        }

        private static bool IsKnownDecision(string value)
        {
            return value == AcceptDecision ||
                   value == ActionOrAbstractDecision ||
                   value == BroadCategoryDecision ||
                   value == MultipleSubjectsDecision ||
                   value == ClassificationClaimDecision ||
                   value == UnclearDecision;
        }

        private static bool IsProperRemovablePhrase(string source, string value)
        {
            string normalizedSource = Normalize(source);
            string normalizedValue = Normalize(value);
            if (string.IsNullOrWhiteSpace(normalizedSource) ||
                string.IsNullOrWhiteSpace(normalizedValue) ||
                normalizedSource == normalizedValue)
            {
                return false;
            }

            int index = normalizedSource.IndexOf(normalizedValue, StringComparison.Ordinal);
            if (index < 0)
            {
                return false;
            }

            return normalizedSource.Remove(index, normalizedValue.Length).Length > 0;
        }

        private static bool IsStrictReduction(string source, string candidate)
        {
            string normalizedSource = Normalize(source);
            string normalizedCandidate = Normalize(candidate);
            return !string.IsNullOrWhiteSpace(normalizedSource) &&
                   !string.IsNullOrWhiteSpace(normalizedCandidate) &&
                   normalizedSource != normalizedCandidate &&
                   normalizedSource.Contains(normalizedCandidate, StringComparison.Ordinal);
        }

        private static string Normalize(string text)
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
    }
}
