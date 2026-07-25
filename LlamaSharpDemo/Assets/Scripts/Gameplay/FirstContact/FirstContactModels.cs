using System;
using System.Collections.Generic;
using DoodleDiplomacy.Localization;
using UnityEngine;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    public enum FirstContactCardSource
    {
        BootstrapProbe,
        PreflightProbe
    }

    internal static class FirstContactTerminalLocalization
    {
        public static string LocalizeBootstrapCategory(string category)
        {
            return LocalizeBootstrapCategory(category, category);
        }

        public static string LocalizeBootstrapCategory(string categoryId, string categoryFallback)
        {
            string fallback = string.IsNullOrWhiteSpace(categoryFallback)
                ? L10n.T("first_contact.terminal.fallback.unknown", "UNKNOWN")
                : categoryFallback.Trim();
            string suffix = BuildKeySuffix(categoryId);
            return string.IsNullOrWhiteSpace(suffix)
                ? fallback
                : L10n.T($"first_contact.terminal.category.{suffix}", fallback);
        }

        public static string LocalizeMeaning(string meaning)
        {
            if (string.IsNullOrWhiteSpace(meaning))
            {
                return L10n.T("first_contact.terminal.meaning.unknown", "[MEANING?]");
            }

            if (string.Equals(BuildKeySuffix(meaning), "meaning", StringComparison.Ordinal))
            {
                return L10n.T("first_contact.terminal.meaning.unknown", meaning.Trim());
            }

            return LocalizeMeaning(meaning, meaning);
        }

        public static string LocalizeMeaning(string meaningId, string meaningFallback)
        {
            string fallback = string.IsNullOrWhiteSpace(meaningFallback) ? "[MEANING?]" : meaningFallback.Trim();
            string suffix = BuildKeySuffix(meaningId);
            if (string.IsNullOrWhiteSpace(suffix))
            {
                return L10n.T("first_contact.terminal.meaning.unknown", fallback);
            }

            return L10n.T($"first_contact.terminal.meaning.{suffix}", fallback);
        }

        public static string LocalizeCategoryDescriptor(string categoryId, string descriptorFallback)
        {
            string fallback = descriptorFallback?.Trim() ?? string.Empty;
            string suffix = BuildKeySuffix(categoryId);
            return string.IsNullOrWhiteSpace(suffix)
                ? fallback
                : L10n.T($"first_contact.terminal.category.{suffix}.descriptor", fallback);
        }

        private static string BuildKeySuffix(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            char[] chars = value.Trim().ToLowerInvariant().ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                chars[i] = char.IsLetterOrDigit(c) ? c : '_';
            }

            return new string(chars).Trim('_');
        }
    }

    public sealed class SemanticCardRecord
    {
        public string Id;
        public Texture2D Texture;
        public byte[] PngBytes;
        public string OriginalLabel;
        public string NormalizedLabel;
        public float[] Embedding;
        public DoodleDiplomacy.Devices.BrainwaveSemanticProfile WaveformProfile;
        public FirstContactCardSource Source;
        public string BootstrapCategoryId;
        public string BootstrapCategoryDisplayName;
        public bool BootstrapCategoryEvaluated;
        public bool BootstrapCategoryAccepted;
        public bool BootstrapCategoryDuplicate;
        public string DuplicateOfCardId;
        public string ClusterId;
        public int ProbeIndex;

        // Compatibility aliases for older callers and save migrations. The player-entered
        // label remains authoritative; no translated canonical label is stored anymore.
        public string Label
        {
            get => OriginalLabel;
            set => OriginalLabel = value;
        }

        public string CanonicalLabel
        {
            get => NormalizedLabel;
            set => NormalizedLabel = value;
        }

        public string LocalizedLabel
        {
            get => OriginalLabel;
            set => OriginalLabel = value;
        }

        public bool TranslationAvailable
        {
            get => false;
            set { }
        }
    }

    public sealed class SemanticClusterRecord
    {
        public string Id;
        public readonly List<SemanticCardRecord> Members = new();
        public float[] Centroid;
        public string ProvisionalName;
        public bool MeaningAssignedByPlayer;
        public bool IsStable;
        public float Cohesion;

        public bool HasMeaning => !string.IsNullOrWhiteSpace(ProvisionalName);
        public bool RequiresMeaningAssignment => IsStable && !HasMeaning;
        public string DisplayName => HasMeaning ? ProvisionalName.Trim() : "[PATTERN-??]";
    }

    public readonly struct FirstContactClusterFormationEdge
    {
        public readonly string FromNodeId;
        public readonly string ToNodeId;
        public readonly float Strength;
        public readonly bool Confirmed;

        public FirstContactClusterFormationEdge(
            string fromNodeId,
            string toNodeId,
            float strength,
            bool confirmed)
        {
            FromNodeId = fromNodeId ?? string.Empty;
            ToNodeId = toNodeId ?? string.Empty;
            Strength = Mathf.Clamp(strength, -1f, 1f);
            Confirmed = confirmed;
        }
    }

    public readonly struct FirstContactClusterFormationEvent
    {
        public readonly string ActiveCardNodeId;
        public readonly string ClusterNodeId;
        public readonly string Meaning;
        public readonly bool HasCluster;
        public readonly bool IsNewCluster;
        public readonly bool BecameStable;
        public readonly bool IsStable;
        public readonly int MemberCount;
        public readonly FirstContactClusterFormationEdge[] CandidateEdges;
        public readonly string[] MemberNodeIds;

        public FirstContactClusterFormationEvent(
            string activeCardNodeId,
            string clusterNodeId,
            string meaning,
            bool hasCluster,
            bool isNewCluster,
            bool becameStable,
            bool isStable,
            int memberCount,
            FirstContactClusterFormationEdge[] candidateEdges,
            string[] memberNodeIds)
        {
            ActiveCardNodeId = activeCardNodeId ?? string.Empty;
            ClusterNodeId = clusterNodeId ?? string.Empty;
            Meaning = meaning ?? string.Empty;
            HasCluster = hasCluster;
            IsNewCluster = isNewCluster;
            BecameStable = becameStable;
            IsStable = isStable;
            MemberCount = Mathf.Max(0, memberCount);
            CandidateEdges = candidateEdges ?? Array.Empty<FirstContactClusterFormationEdge>();
            MemberNodeIds = memberNodeIds ?? Array.Empty<string>();
        }

        public bool HasConfirmedEdge
        {
            get
            {
                for (int i = 0; i < CandidateEdges.Length; i++)
                {
                    if (CandidateEdges[i].Confirmed)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public bool IsIsolated => HasCluster && MemberCount <= 1 && !HasConfirmedEdge;

        public bool ShouldAnimate =>
            HasCluster &&
            !string.IsNullOrWhiteSpace(ActiveCardNodeId) &&
            (CandidateEdges.Length > 0 || MemberCount > 1 || BecameStable || IsIsolated);
    }

    public sealed class FirstContactSessionContext
    {
        public int ProbeIndex;
        public readonly List<SemanticCardRecord> RecentCards = new();
        public readonly List<SemanticClusterRecord> StableClusters = new();
    }

    public readonly struct EmbeddingResult
    {
        public readonly string Text;
        public readonly float[] Vector;
        public readonly bool IsValid;
        public readonly string Error;

        public EmbeddingResult(string text, float[] vector, string error = null)
        {
            Text = text ?? string.Empty;
            Vector = vector;
            IsValid = vector != null && vector.Length > 0 && string.IsNullOrWhiteSpace(error);
            Error = error ?? string.Empty;
        }
    }

    public static class FirstContactProbeDuplicateDetector
    {
        public enum MatchKind
        {
            None,
            IdenticalImage,
            SameLabelReuse,
            StrongSemanticMatch
        }

        public readonly struct MatchEvidence
        {
            public MatchEvidence(MatchKind kind, float semanticSimilarity)
            {
                Kind = kind;
                SemanticSimilarity = semanticSimilarity;
            }

            public MatchKind Kind { get; }
            public float SemanticSimilarity { get; }
            public bool IsCertain => Kind != MatchKind.None;
        }

        public readonly struct ReviewCandidate
        {
            public ReviewCandidate(SemanticCardRecord card, float semanticSimilarity)
            {
                Card = card;
                SemanticSimilarity = semanticSimilarity;
            }

            public SemanticCardRecord Card { get; }
            public float SemanticSimilarity { get; }
        }

        public static bool TryFindDuplicate(
            SemanticCardRecord candidate,
            IReadOnlyList<SemanticCardRecord> recordedCards,
            FirstContactEmbeddingService embeddingService,
            FirstContactSemanticSettings settings,
            out SemanticCardRecord duplicate)
        {
            return TryFindDuplicate(
                candidate,
                recordedCards,
                embeddingService,
                settings,
                out duplicate,
                out _);
        }

        public static bool TryFindDuplicate(
            SemanticCardRecord candidate,
            IReadOnlyList<SemanticCardRecord> recordedCards,
            FirstContactEmbeddingService embeddingService,
            FirstContactSemanticSettings settings,
            out SemanticCardRecord duplicate,
            out MatchEvidence evidence)
        {
            duplicate = null;
            evidence = default;
            if (candidate == null || recordedCards == null)
            {
                return false;
            }

            float semanticThreshold = settings != null
                ? settings.bootstrapDuplicateSemanticThreshold
                : 0.96f;
            string candidateLabel = ResolveNormalizedLabel(candidate);

            for (int i = 0; i < recordedCards.Count; i++)
            {
                SemanticCardRecord recorded = recordedCards[i];
                if (recorded == null || ReferenceEquals(recorded, candidate))
                {
                    continue;
                }

                if (HaveIdenticalPng(candidate.PngBytes, recorded.PngBytes))
                {
                    duplicate = recorded;
                    evidence = new MatchEvidence(MatchKind.IdenticalImage, 1f);
                    return true;
                }

                if (!string.IsNullOrWhiteSpace(candidateLabel) &&
                    string.Equals(candidateLabel, ResolveNormalizedLabel(recorded), StringComparison.Ordinal))
                {
                    duplicate = recorded;
                    evidence = new MatchEvidence(MatchKind.SameLabelReuse, 1f);
                    return true;
                }

                if (embeddingService != null &&
                    candidate.Embedding != null &&
                    recorded.Embedding != null)
                {
                    float similarity = embeddingService.Similarity(candidate.Embedding, recorded.Embedding);
                    if (similarity >= semanticThreshold)
                    {
                        duplicate = recorded;
                        evidence = new MatchEvidence(MatchKind.StrongSemanticMatch, similarity);
                        return true;
                    }
                }
            }

            return false;
        }

        public static IReadOnlyList<ReviewCandidate> FindReviewCandidates(
            SemanticCardRecord candidate,
            IReadOnlyList<SemanticCardRecord> recordedCards,
            FirstContactEmbeddingService embeddingService,
            FirstContactSemanticSettings settings)
        {
            var candidates = new List<ReviewCandidate>();
            if (candidate?.Embedding == null ||
                recordedCards == null ||
                embeddingService == null ||
                settings == null ||
                !settings.enableSemanticDuplicateLlmReview)
            {
                return candidates;
            }

            float reviewThreshold = Mathf.Min(
                settings.bootstrapDuplicateSemanticReviewThreshold,
                settings.bootstrapDuplicateSemanticThreshold);
            float certainThreshold = settings.bootstrapDuplicateSemanticThreshold;
            for (int i = 0; i < recordedCards.Count; i++)
            {
                SemanticCardRecord recorded = recordedCards[i];
                if (recorded?.Embedding == null ||
                    ReferenceEquals(recorded, candidate))
                {
                    continue;
                }

                float similarity = embeddingService.Similarity(candidate.Embedding, recorded.Embedding);
                if (similarity >= reviewThreshold && similarity < certainThreshold)
                {
                    candidates.Add(new ReviewCandidate(recorded, similarity));
                }
            }

            candidates.Sort((left, right) => right.SemanticSimilarity.CompareTo(left.SemanticSimilarity));
            int maxCandidates = Mathf.Max(1, settings.semanticDuplicateReviewMaxCandidates);
            if (candidates.Count > maxCandidates)
            {
                candidates.RemoveRange(maxCandidates, candidates.Count - maxCandidates);
            }

            return candidates;
        }

        private static bool HaveIdenticalPng(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length == 0 || left.Length != right.Length)
            {
                return false;
            }

            for (int i = 0; i < left.Length; i++)
            {
                if (left[i] != right[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static string ResolveNormalizedLabel(SemanticCardRecord card)
        {
            string label = !string.IsNullOrWhiteSpace(card?.NormalizedLabel)
                ? card.NormalizedLabel
                : card?.OriginalLabel;
            return string.IsNullOrWhiteSpace(label)
                ? string.Empty
                : FirstContactEmbeddingService.NormalizeText(label);
        }
    }
}
