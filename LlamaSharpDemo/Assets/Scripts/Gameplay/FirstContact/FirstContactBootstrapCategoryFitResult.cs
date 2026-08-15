using System;
using System.Collections.Generic;
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
        public const string EquivalentName = "equivalent_name";
        public const string DistinctSubject = "distinct_subject";
        public const string Uncertain = "uncertain";

        private const string RelationKey = "relation";

        public string Relation = Uncertain;
        public string Error = string.Empty;

        public bool IsSuccess => string.IsNullOrWhiteSpace(Error);
        public bool ClaimsEquivalentName =>
            IsSuccess && string.Equals(Relation, EquivalentName, StringComparison.Ordinal);

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
                result = Failed("Semantic duplicate pipeline returned no relation.");
                return false;
            }

            string rawRelation = relation?.Trim() ?? string.Empty;
            relation = NormalizeRelation(rawRelation);
            if (string.Equals(relation, Uncertain, StringComparison.Ordinal) &&
                !string.Equals(rawRelation, Uncertain, StringComparison.OrdinalIgnoreCase))
            {
                result = Failed("Semantic duplicate pipeline returned an invalid relation.");
                return false;
            }

            result = new FirstContactSemanticDuplicateReviewResult
            {
                Relation = relation
            };
            return true;
        }

        private static string NormalizeRelation(string relation)
        {
            relation = relation?.Trim().ToLowerInvariant() ?? string.Empty;
            return relation switch
            {
                EquivalentName => EquivalentName,
                DistinctSubject => DistinctSubject,
                Uncertain => Uncertain,
                _ => Uncertain
            };
        }
    }

    public sealed class FirstContactSemanticDuplicateChallengeResult
    {
        public const string Yes = "yes";
        public const string No = "no";
        public const string Uncertain = "uncertain";

        private const string DistinctExampleExistsKey = "distinct_example_exists";

        public string DistinctExampleExists = Uncertain;
        public string Error = string.Empty;

        public bool IsSuccess => string.IsNullOrWhiteSpace(Error);
        public bool RulesOutDistinctSubject =>
            IsSuccess && string.Equals(DistinctExampleExists, No, StringComparison.Ordinal);

        public static FirstContactSemanticDuplicateChallengeResult Failed(string message)
        {
            return new FirstContactSemanticDuplicateChallengeResult
            {
                Error = string.IsNullOrWhiteSpace(message)
                    ? "Semantic duplicate challenge failed."
                    : message.Trim()
            };
        }

        public static bool TryFromPipelineState(
            PipelineState state,
            out FirstContactSemanticDuplicateChallengeResult result)
        {
            result = null;
            if (state == null)
            {
                result = Failed("Semantic duplicate challenge returned no state.");
                return false;
            }

            if (state.TryGetString(PromptPipelineConstants.ErrorKey, out string pipelineError) &&
                !string.IsNullOrWhiteSpace(pipelineError))
            {
                result = Failed(pipelineError);
                return false;
            }

            if (!state.TryGetString(DistinctExampleExistsKey, out string rawDecision))
            {
                result = Failed("Semantic duplicate challenge returned no distinct_example_exists.");
                return false;
            }

            string decision = NormalizeDecision(rawDecision);
            if (string.IsNullOrWhiteSpace(decision))
            {
                result = Failed("Semantic duplicate challenge returned an invalid distinct_example_exists.");
                return false;
            }

            result = new FirstContactSemanticDuplicateChallengeResult
            {
                DistinctExampleExists = decision
            };
            return true;
        }

        private static string NormalizeDecision(string decision)
        {
            decision = decision?.Trim().ToLowerInvariant() ?? string.Empty;
            return decision switch
            {
                Yes => Yes,
                No => No,
                Uncertain => Uncertain,
                _ => string.Empty
            };
        }
    }

    public static class FirstContactSemanticDuplicateDecision
    {
        public static bool ConfirmsDuplicate(
            FirstContactSemanticDuplicateReviewResult review,
            FirstContactSemanticDuplicateChallengeResult challenge)
        {
            return review?.IsSuccess == true &&
                   challenge?.IsSuccess == true &&
                   review.ClaimsEquivalentName &&
                   challenge?.RulesOutDistinctSubject == true;
        }
    }

    public static class FirstContactSemanticCategory
    {
        private static readonly char[] BoundaryDecoration =
        {
            '.', ',', ':', ';', '!', '?',
            '"', '\'', '`',
            '(', ')', '[', ']', '{', '}'
        };

        public static string Normalize(string category)
        {
            if (string.IsNullOrWhiteSpace(category))
            {
                return string.Empty;
            }

            return category.Trim().Trim(BoundaryDecoration).Trim();
        }
    }

    public sealed class FirstContactSemanticGroupFitResult
    {
        public const string JoinDecision = "join";
        public const string RejectDecision = "reject";
        public const string UncertainDecision = "uncertain";

        private const string DecisionKey = "decision";
        private const string CategoryKey = "category";

        public string Decision = UncertainDecision;
        public string Category = string.Empty;
        public string Error = string.Empty;

        public bool IsSuccess => string.IsNullOrWhiteSpace(Error);
        public bool JoinsGroup =>
            IsSuccess &&
            string.Equals(Decision, JoinDecision, StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(Category);
        public bool IsUncertain =>
            !IsSuccess ||
            string.Equals(Decision, UncertainDecision, StringComparison.Ordinal) ||
            (string.Equals(Decision, JoinDecision, StringComparison.Ordinal) &&
             string.IsNullOrWhiteSpace(Category));

        public static FirstContactSemanticGroupFitResult Failed(string message)
        {
            return new FirstContactSemanticGroupFitResult
            {
                Decision = UncertainDecision,
                Error = string.IsNullOrWhiteSpace(message)
                    ? "Semantic group fit processing failed."
                    : message.Trim()
            };
        }

        public static bool TryFromSeedPipelineState(
            PipelineState state,
            out FirstContactSemanticGroupFitResult result)
        {
            if (!TryValidatePipelineState(state, out result))
            {
                return false;
            }

            if (!state.TryGetString(CategoryKey, out string rawCategory))
            {
                result = Failed("Semantic group seed pipeline returned no category field.");
                return false;
            }

            string category = FirstContactSemanticCategory.Normalize(rawCategory);
            result = new FirstContactSemanticGroupFitResult
            {
                Decision = category.Length > 0 ? JoinDecision : RejectDecision,
                Category = category
            };
            return true;
        }

        public static bool TryFromMembershipPipelineState(
            PipelineState state,
            string existingCategory,
            out FirstContactSemanticGroupFitResult result)
        {
            if (!TryValidatePipelineState(state, out result))
            {
                return false;
            }

            string category = FirstContactSemanticCategory.Normalize(existingCategory);
            if (category.Length == 0)
            {
                result = Failed("Semantic group membership requires an existing category.");
                return false;
            }

            if (!state.TryGetString(DecisionKey, out string rawDecision) ||
                string.IsNullOrWhiteSpace(rawDecision))
            {
                result = Failed("Semantic group membership pipeline returned no decision.");
                return false;
            }

            string decision = NormalizeDecision(rawDecision);
            if (string.IsNullOrWhiteSpace(decision))
            {
                result = Failed(
                    $"Semantic group fit pipeline returned an invalid decision: '{rawDecision?.Trim()}'.");
                return false;
            }

            result = new FirstContactSemanticGroupFitResult
            {
                Decision = decision,
                Category = string.Equals(decision, JoinDecision, StringComparison.Ordinal)
                    ? category
                    : string.Empty
            };
            return true;
        }

        private static bool TryValidatePipelineState(
            PipelineState state,
            out FirstContactSemanticGroupFitResult result)
        {
            result = null;
            if (state == null)
            {
                result = Failed("Semantic group pipeline returned no state.");
                return false;
            }

            if (state.TryGetString(PromptPipelineConstants.ErrorKey, out string pipelineError) &&
                !string.IsNullOrWhiteSpace(pipelineError))
            {
                result = Failed(pipelineError);
                return false;
            }

            return true;
        }

        private static string NormalizeDecision(string decision)
        {
            decision = decision?.Trim().ToLowerInvariant() ?? string.Empty;
            return decision switch
            {
                JoinDecision => JoinDecision,
                RejectDecision => RejectDecision,
                UncertainDecision => UncertainDecision,
                _ => string.Empty
            };
        }
    }

    public enum FirstContactSemanticGroupResolutionKind
    {
        CreateNewGroup,
        JoinExistingGroup,
        Pending
    }

    public readonly struct FirstContactSemanticGroupResolution
    {
        public FirstContactSemanticGroupResolution(
            FirstContactSemanticGroupResolutionKind kind,
            SemanticClusterRecord targetCluster,
            IReadOnlyList<SemanticClusterRecord> integrityConflictClusters = null,
            string categoryHypothesis = null)
        {
            Kind = kind;
            TargetCluster = targetCluster;
            CategoryHypothesis = FirstContactSemanticCategory.Normalize(categoryHypothesis);
            if (integrityConflictClusters == null || integrityConflictClusters.Count == 0)
            {
                IntegrityConflictClusters = Array.Empty<SemanticClusterRecord>();
            }
            else
            {
                var clusters = new SemanticClusterRecord[integrityConflictClusters.Count];
                for (int i = 0; i < integrityConflictClusters.Count; i++)
                {
                    clusters[i] = integrityConflictClusters[i];
                }

                IntegrityConflictClusters = clusters;
            }
        }

        public FirstContactSemanticGroupResolutionKind Kind { get; }
        public SemanticClusterRecord TargetCluster { get; }
        public string CategoryHypothesis { get; }
        public IReadOnlyList<SemanticClusterRecord> IntegrityConflictClusters { get; }
        public bool HasIntegrityConflict => IntegrityConflictClusters?.Count > 1;
    }

    public static class FirstContactSemanticGroupDecision
    {
        public static FirstContactSemanticGroupResolution Resolve(
            IReadOnlyList<FirstContactSemanticGroupCandidate> candidates,
            IReadOnlyList<FirstContactSemanticGroupFitResult> results)
        {
            int count = Math.Min(candidates?.Count ?? 0, results?.Count ?? 0);
            int joinCount = 0;
            bool hasUncertain = false;
            SemanticClusterRecord joinedCluster = null;
            string joinedCategory = string.Empty;
            var joinedClusters = new List<SemanticClusterRecord>();

            for (int i = 0; i < count; i++)
            {
                FirstContactSemanticGroupFitResult result = results[i];
                if (result?.JoinsGroup == true)
                {
                    joinCount++;
                    joinedCluster = candidates[i].Cluster;
                    joinedCategory = result.Category;
                    if (joinedCluster != null)
                    {
                        joinedClusters.Add(joinedCluster);
                    }
                }
                else if (result == null || result.IsUncertain)
                {
                    hasUncertain = true;
                }
            }

            if (joinCount == 1 && !hasUncertain && joinedCluster != null)
            {
                return new FirstContactSemanticGroupResolution(
                    FirstContactSemanticGroupResolutionKind.JoinExistingGroup,
                    joinedCluster,
                    categoryHypothesis: joinedCategory);
            }

            if (joinCount == 0 && !hasUncertain)
            {
                return new FirstContactSemanticGroupResolution(
                    FirstContactSemanticGroupResolutionKind.CreateNewGroup,
                    null);
            }

            return new FirstContactSemanticGroupResolution(
                FirstContactSemanticGroupResolutionKind.Pending,
                null,
                joinCount > 1 ? joinedClusters : null);
        }
    }
}
