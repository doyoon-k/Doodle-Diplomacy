using System;
using System.Collections.Generic;
using DoodleDiplomacy.Localization;
using UnityEngine;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    public enum FirstContactCardSource
    {
        BootstrapProbe
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
        public string Label;
        public string CanonicalLabel;
        public string LocalizedLabel;
        public bool TranslationAvailable;
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
    }

    public sealed class SemanticClusterRecord
    {
        public string Id;
        public readonly List<SemanticCardRecord> Members = new();
        public float[] Centroid;
        public string ProvisionalName;
        public bool IsStable;
        public float Cohesion;

        public string DisplayName => string.IsNullOrWhiteSpace(ProvisionalName) ? $"[{Id}]" : ProvisionalName.Trim();
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
        public static bool TryFindDuplicate(
            SemanticCardRecord candidate,
            IReadOnlyList<SemanticCardRecord> recordedCards,
            FirstContactEmbeddingService embeddingService,
            FirstContactSemanticSettings settings,
            out SemanticCardRecord duplicate)
        {
            duplicate = null;
            if (candidate == null || recordedCards == null)
            {
                return false;
            }

            float semanticThreshold = settings != null
                ? settings.bootstrapDuplicateSemanticThreshold
                : 0.96f;
            string candidateLabel = NormalizeLabel(candidate.Label);

            for (int i = 0; i < recordedCards.Count; i++)
            {
                SemanticCardRecord recorded = recordedCards[i];
                if (recorded == null || ReferenceEquals(recorded, candidate))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(candidateLabel) &&
                    string.Equals(candidateLabel, NormalizeLabel(recorded.Label), StringComparison.Ordinal))
                {
                    duplicate = recorded;
                    return true;
                }

                if (HaveIdenticalPng(candidate.PngBytes, recorded.PngBytes))
                {
                    duplicate = recorded;
                    return true;
                }

                if (embeddingService != null &&
                    candidate.Embedding != null &&
                    recorded.Embedding != null &&
                    embeddingService.Similarity(candidate.Embedding, recorded.Embedding) >=
                    semanticThreshold)
                {
                    duplicate = recorded;
                    return true;
                }
            }

            return false;
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

        private static string NormalizeLabel(string label)
        {
            return string.IsNullOrWhiteSpace(label)
                ? string.Empty
                : FirstContactEmbeddingService.NormalizeText(label);
        }
    }
}
