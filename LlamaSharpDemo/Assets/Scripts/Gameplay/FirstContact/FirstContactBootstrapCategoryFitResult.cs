using System;
using DoodleDiplomacy.Core;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    public sealed class FirstContactBootstrapCategoryFitResult
    {
        private const string FitsCategoryKey = "fits_category";
        private const string EvidenceTypeKey = "evidence_type";
        public const string OrdinaryIdentityEvidence = "ordinary_identity";
        public const string SymbolicOrContextualEvidence = "symbolic_or_contextual";
        public const string NeutralOrGenericEvidence = "neutral_or_generic";
        public const string UncertainEvidence = "uncertain";
        private const string ReasonKey = "reason";

        public bool FitsCategory = true;
        public string EvidenceType = string.Empty;
        public string Reason = string.Empty;
        public string Error = string.Empty;

        public bool IsSuccess => string.IsNullOrWhiteSpace(Error);

        public static FirstContactBootstrapCategoryFitResult Accepted(string reason = null)
        {
            return new FirstContactBootstrapCategoryFitResult
            {
                FitsCategory = true,
                EvidenceType = OrdinaryIdentityEvidence,
                Reason = reason ?? string.Empty
            };
        }

        public static FirstContactBootstrapCategoryFitResult Failed(string message)
        {
            return new FirstContactBootstrapCategoryFitResult
            {
                FitsCategory = false,
                EvidenceType = UncertainEvidence,
                Error = string.IsNullOrWhiteSpace(message)
                    ? "Bootstrap category fit processing failed."
                    : message.Trim()
            };
        }

        public static bool TryFromPipelineState(
            PipelineState state,
            out FirstContactBootstrapCategoryFitResult result)
        {
            result = null;
            if (state == null)
            {
                result = Failed("Category fit pipeline returned no state.");
                return false;
            }

            if (state.TryGetString(PromptPipelineConstants.ErrorKey, out string pipelineError) &&
                !string.IsNullOrWhiteSpace(pipelineError))
            {
                result = Failed(pipelineError);
                return false;
            }

            if (!TryReadBool(state, FitsCategoryKey, out bool fitsCategory))
            {
                result = Failed("Category fit pipeline returned no fits_category.");
                return false;
            }

            if (!state.TryGetString(EvidenceTypeKey, out string evidenceType) ||
                string.IsNullOrWhiteSpace(evidenceType))
            {
                evidenceType = UncertainEvidence;
            }

            evidenceType = NormalizeEvidenceType(evidenceType);
            if (!string.Equals(evidenceType, OrdinaryIdentityEvidence, StringComparison.Ordinal))
            {
                fitsCategory = false;
            }

            state.TryGetString(ReasonKey, out string reason);
            result = new FirstContactBootstrapCategoryFitResult
            {
                FitsCategory = fitsCategory,
                EvidenceType = evidenceType,
                Reason = reason?.Trim() ?? string.Empty
            };
            return true;
        }

        private static string NormalizeEvidenceType(string evidenceType)
        {
            evidenceType = evidenceType?.Trim().ToLowerInvariant() ?? string.Empty;
            return evidenceType switch
            {
                OrdinaryIdentityEvidence => OrdinaryIdentityEvidence,
                SymbolicOrContextualEvidence => SymbolicOrContextualEvidence,
                NeutralOrGenericEvidence => NeutralOrGenericEvidence,
                _ => UncertainEvidence
            };
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
