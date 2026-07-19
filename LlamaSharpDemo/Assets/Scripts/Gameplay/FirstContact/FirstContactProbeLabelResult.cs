using System;
using DoodleDiplomacy.Core;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    public enum FirstContactProbeLabelIssue
    {
        None,
        ActionOrAbstract,
        BroadCategory,
        MultipleSubjects,
        ClassificationClaim,
        LabelMismatch
    }

    public sealed class FirstContactProbeLabelResult
    {
        private const string NormalizedLabelKey = "normalized_label";
        private const string CanonicalLabelKey = "canonical_label";
        private const string HasClassificationClaimKey = "has_classification_claim";
        private const string ClassificationClaimTextKey = "classification_claim_text";
        private const string NeutralSubjectLabelKey = "neutral_subject_label";
        private const string LabelReasonKey = "label_reason";
        private const string IsSuitableKey = "is_suitable";
        private const string ReasonKey = "reason";

        public string NormalizedLabel = string.Empty;
        public bool HasClassificationClaim;
        public string ClassificationClaimText = string.Empty;
        public string NeutralSubjectLabel = string.Empty;
        public FirstContactProbeLabelIssue LabelIssue;
        public bool AnalysisInconclusive;
        public bool IsSuitable = true;
        public string Reason = string.Empty;
        public string Error = string.Empty;

        public bool IsSuccess => string.IsNullOrWhiteSpace(Error);

        public string CanonicalLabel
        {
            get => NormalizedLabel;
            set => NormalizedLabel = value;
        }

        public bool TranslationAvailable
        {
            get => false;
            set { }
        }

        public static FirstContactProbeLabelResult Fallback(string label, string reason = null)
        {
            return new FirstContactProbeLabelResult
            {
                NormalizedLabel = label ?? string.Empty,
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

            if ((!state.TryGetString(NormalizedLabelKey, out string normalizedLabel) ||
                 string.IsNullOrWhiteSpace(normalizedLabel)) &&
                (!state.TryGetString(CanonicalLabelKey, out normalizedLabel) ||
                 string.IsNullOrWhiteSpace(normalizedLabel)))
            {
                result = Failed("Label pipeline returned no normalized label.");
                return false;
            }

            if (state.ContainsString(FirstContactLabelAnalysisContract.DecisionKey))
            {
                return TryFromUnifiedAnalysis(
                    state,
                    normalizedLabel,
                    out result);
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
                    NormalizedLabel = normalizedLabel.Trim(),
                    HasClassificationClaim = true,
                    LabelIssue = FirstContactProbeLabelIssue.ClassificationClaim,
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
                ? normalizedLabel.Trim()
                : neutralSubjectLabel.Trim();
            bool unsupportedClassificationClaim = false;
            if (hasClassificationClaim &&
                !HasSupportedClassificationClaim(
                    state,
                    normalizedLabel,
                    classificationClaimText,
                    neutralSubjectLabel))
            {
                unsupportedClassificationClaim = true;
            }

            state.TryGetString(LabelReasonKey, out string labelReason);
            state.TryGetString(ReasonKey, out string reason);
            string finalReason = reason?.Trim() ?? string.Empty;
            if (hasClassificationClaim)
            {
                isSuitable = false;
                finalReason = unsupportedClassificationClaim
                    ? "Label classifier could not isolate a concrete subject name."
                    : string.IsNullOrWhiteSpace(labelReason)
                    ? "Label includes a classification claim instead of only the subject name."
                    : labelReason.Trim();
            }

            result = new FirstContactProbeLabelResult
            {
                NormalizedLabel = normalizedLabel.Trim(),
                HasClassificationClaim = hasClassificationClaim,
                ClassificationClaimText = classificationClaimText,
                NeutralSubjectLabel = neutralSubjectLabel,
                LabelIssue = hasClassificationClaim
                    ? FirstContactProbeLabelIssue.ClassificationClaim
                    : isSuitable
                        ? FirstContactProbeLabelIssue.None
                        : FirstContactProbeLabelIssue.ActionOrAbstract,
                IsSuitable = isSuitable,
                Reason = finalReason
            };
            return true;
        }

        private static bool TryFromUnifiedAnalysis(
            PipelineState state,
            string normalizedLabel,
            out FirstContactProbeLabelResult result)
        {
            result = null;
            if (!FirstContactLabelAnalysisContract.TryValidate(
                    state,
                    out FirstContactLabelAnalysisData analysis,
                    out string contractError))
            {
                result = Failed($"Label analysis contract failed: {contractError}");
                return false;
            }

            bool inconclusive = analysis.Decision == FirstContactLabelAnalysisContract.UnclearDecision;
            if (state.TryGetString(FirstContactLabelAnalysisContract.InconclusiveKey, out string inconclusiveText) &&
                bool.TryParse(inconclusiveText?.Trim(), out bool parsedInconclusive))
            {
                inconclusive |= parsedInconclusive;
            }

            bool hasClassificationClaim =
                analysis.Decision == FirstContactLabelAnalysisContract.ClassificationClaimDecision;
            bool isSuitable =
                analysis.Decision == FirstContactLabelAnalysisContract.AcceptDecision || inconclusive;
            FirstContactProbeLabelIssue labelIssue = analysis.Decision switch
            {
                FirstContactLabelAnalysisContract.ActionOrAbstractDecision =>
                    FirstContactProbeLabelIssue.ActionOrAbstract,
                FirstContactLabelAnalysisContract.BroadCategoryDecision =>
                    FirstContactProbeLabelIssue.BroadCategory,
                FirstContactLabelAnalysisContract.MultipleSubjectsDecision =>
                    FirstContactProbeLabelIssue.MultipleSubjects,
                FirstContactLabelAnalysisContract.ClassificationClaimDecision =>
                    FirstContactProbeLabelIssue.ClassificationClaim,
                _ => FirstContactProbeLabelIssue.None
            };
            string reason = analysis.Decision switch
            {
                FirstContactLabelAnalysisContract.ActionOrAbstractDecision =>
                    "Label names an action, scene, relationship, message, or abstract concept.",
                FirstContactLabelAnalysisContract.BroadCategoryDecision =>
                    "Label names a broad category instead of one concrete subject.",
                FirstContactLabelAnalysisContract.MultipleSubjectsDecision =>
                    "Label names multiple subjects.",
                FirstContactLabelAnalysisContract.ClassificationClaimDecision =>
                    "Label includes a classification claim instead of only the subject name.",
                _ => string.Empty
            };

            result = new FirstContactProbeLabelResult
            {
                NormalizedLabel = normalizedLabel.Trim(),
                HasClassificationClaim = hasClassificationClaim,
                ClassificationClaimText = analysis.ClassificationClaimText,
                NeutralSubjectLabel = string.IsNullOrWhiteSpace(analysis.NeutralSubjectLabel)
                    ? normalizedLabel.Trim()
                    : analysis.NeutralSubjectLabel,
                LabelIssue = labelIssue,
                AnalysisInconclusive = inconclusive,
                IsSuitable = isSuitable,
                Reason = reason
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
