using System;
using DoodleDiplomacy.Core;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    public sealed class FirstContactBootstrapCategoryFitResult
    {
        private const string DecisionKey = "decision";
        public const string OrdinaryMatchDecision = "ordinary_match";
        public const string CategoryMismatchDecision = "category_mismatch";
        public const string ContextualOnlyDecision = "contextual_only";
        public const string UncertainDecision = "uncertain";
        private const string ReasonKey = "reason";

        public string Decision = UncertainDecision;
        public string Reason = string.Empty;
        public string Error = string.Empty;

        public bool IsSuccess => string.IsNullOrWhiteSpace(Error);
        public bool FitsCategory =>
            string.Equals(Decision, OrdinaryMatchDecision, StringComparison.Ordinal);

        public static FirstContactBootstrapCategoryFitResult Accepted(string reason = null)
        {
            return new FirstContactBootstrapCategoryFitResult
            {
                Decision = OrdinaryMatchDecision,
                Reason = reason ?? string.Empty
            };
        }

        public static FirstContactBootstrapCategoryFitResult Failed(string message)
        {
            return new FirstContactBootstrapCategoryFitResult
            {
                Decision = UncertainDecision,
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

            if (!state.TryGetString(DecisionKey, out string rawDecision) ||
                string.IsNullOrWhiteSpace(rawDecision))
            {
                result = Failed("Category fit pipeline returned no decision.");
                return false;
            }

            string decision = NormalizeDecision(rawDecision);
            if (string.IsNullOrWhiteSpace(decision))
            {
                result = Failed(
                    $"Category fit pipeline returned an invalid decision: '{rawDecision?.Trim()}'.");
                return false;
            }

            state.TryGetString(ReasonKey, out string reason);
            result = new FirstContactBootstrapCategoryFitResult
            {
                Decision = decision,
                Reason = reason?.Trim() ?? string.Empty
            };
            return true;
        }

        private static string NormalizeDecision(string decision)
        {
            decision = decision?.Trim().ToLowerInvariant() ?? string.Empty;
            return decision switch
            {
                OrdinaryMatchDecision => OrdinaryMatchDecision,
                CategoryMismatchDecision => CategoryMismatchDecision,
                ContextualOnlyDecision => ContextualOnlyDecision,
                UncertainDecision => UncertainDecision,
                _ => string.Empty
            };
        }
    }

    public sealed class FirstContactSemanticDuplicateReviewResult
    {
        public const string SameConcept = "same_concept";
        public const string DifferentConcept = "different_concept";
        public const string Uncertain = "uncertain";

        private const string RelationKey = "semantic_relation";
        private const string ReasonKey = "reason";

        public string Relation = Uncertain;
        public string Reason = string.Empty;
        public string Error = string.Empty;

        public bool IsSuccess => string.IsNullOrWhiteSpace(Error);
        public bool ConfirmsDuplicate =>
            string.Equals(Relation, SameConcept, StringComparison.Ordinal);

        public static FirstContactSemanticDuplicateReviewResult Failed(string message)
        {
            return new FirstContactSemanticDuplicateReviewResult
            {
                Error = string.IsNullOrWhiteSpace(message)
                    ? "Semantic duplicate review failed."
                    : message.Trim()
            };
        }

        public static bool TryFromPipelineState(
            PipelineState state,
            out FirstContactSemanticDuplicateReviewResult result)
        {
            result = null;
            if (state == null)
            {
                result = Failed("Semantic duplicate pipeline returned no state.");
                return false;
            }

            if (state.TryGetString(PromptPipelineConstants.ErrorKey, out string pipelineError) &&
                !string.IsNullOrWhiteSpace(pipelineError))
            {
                result = Failed(pipelineError);
                return false;
            }

            if (!state.TryGetString(RelationKey, out string relation))
            {
                result = Failed("Semantic duplicate pipeline returned no semantic_relation.");
                return false;
            }

            string rawRelation = relation?.Trim() ?? string.Empty;
            relation = NormalizeRelation(rawRelation);
            if (string.Equals(relation, Uncertain, StringComparison.Ordinal) &&
                !string.Equals(rawRelation, Uncertain, StringComparison.OrdinalIgnoreCase))
            {
                result = Failed("Semantic duplicate pipeline returned an invalid semantic_relation.");
                return false;
            }

            state.TryGetString(ReasonKey, out string reason);
            result = new FirstContactSemanticDuplicateReviewResult
            {
                Relation = relation,
                Reason = reason?.Trim() ?? string.Empty
            };
            return true;
        }

        private static string NormalizeRelation(string relation)
        {
            relation = relation?.Trim().ToLowerInvariant() ?? string.Empty;
            return relation switch
            {
                SameConcept => SameConcept,
                DifferentConcept => DifferentConcept,
                Uncertain => Uncertain,
                _ => Uncertain
            };
        }
    }
}
