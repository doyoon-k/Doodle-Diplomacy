using System;
using System.Collections.Generic;
using DoodleDiplomacy.Devices;
using UnityEngine;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    public sealed class FirstContactSemanticMemory
    {
        private readonly FirstContactEmbeddingService _embeddingService;
        private readonly FirstContactSemanticSettings _settings;
        private readonly FirstContactDebugSettings _debugSettings;
        private readonly List<SemanticCardRecord> _cards = new();
        private readonly List<SemanticClusterRecord> _clusters = new();
        private readonly List<SemanticCardRecord> _pendingCards = new();
        private FirstContactClusterFormationEdge[] _lastFormationEdges =
            Array.Empty<FirstContactClusterFormationEdge>();

        private int _nextCardIndex = 1;
        private int _nextClusterIndex = 1;

        public FirstContactSemanticMemory(
            FirstContactEmbeddingService embeddingService,
            FirstContactSemanticSettings settings,
            FirstContactDebugSettings debugSettings,
            IReadOnlyList<FirstContactBootstrapCategoryDefinition> bootstrapCategories = null)
        {
            _embeddingService = embeddingService;
            _settings = settings;
            _debugSettings = debugSettings;
        }

        public IReadOnlyList<SemanticCardRecord> Cards => _cards;
        public IReadOnlyList<SemanticClusterRecord> Clusters => _clusters;
        public IReadOnlyList<SemanticCardRecord> PendingCards => _pendingCards;
        public IReadOnlyList<FirstContactClusterFormationEdge> LastFormationEdges => _lastFormationEdges;

        public IReadOnlyList<SemanticClusterRecord> StableClusters
        {
            get
            {
                var stable = new List<SemanticClusterRecord>();
                for (int i = 0; i < _clusters.Count; i++)
                {
                    if (_clusters[i].IsStable)
                    {
                        stable.Add(_clusters[i]);
                    }
                }

                return stable;
            }
        }

        public void RegisterAcceptedCard(SemanticCardRecord card)
        {
            if (!RegisterCard(card))
            {
                return;
            }

            card.ClusterId = string.Empty;
            card.SemanticGroupAssignment = FirstContactSemanticGroupAssignmentState.NotApplicable;
            _lastFormationEdges = Array.Empty<FirstContactClusterFormationEdge>();
            LogAssignment(card, null, "CATEGORY");
        }

        public SemanticClusterRecord CreateDetachedGroup(SemanticCardRecord card)
        {
            if (!RegisterCard(card))
            {
                return FindCluster(card?.ClusterId);
            }

            SemanticClusterRecord cluster = CreateCluster();
            AddMember(cluster, card);
            _clusters.Add(cluster);
            _lastFormationEdges = Array.Empty<FirstContactClusterFormationEdge>();
            LogAssignment(card, cluster, "NEW");
            return cluster;
        }

        public SemanticClusterRecord JoinDetachedGroup(
            SemanticCardRecord card,
            SemanticClusterRecord targetCluster,
            float semanticSimilarity,
            string categoryHypothesis = null)
        {
            SemanticClusterRecord cluster = ResolveOwnedCluster(targetCluster);
            if (cluster == null || !RegisterCard(card))
            {
                return cluster;
            }

            SemanticCardRecord edgeMember = FindClosestMember(card, cluster);
            string storedCategory =
                FirstContactSemanticCategory.Normalize(cluster.CategoryHypothesis);
            if (storedCategory.Length > 0)
            {
                cluster.CategoryHypothesis = storedCategory;
            }
            else
            {
                string joinedCategory =
                    FirstContactSemanticCategory.Normalize(categoryHypothesis);
                if (joinedCategory.Length > 0)
                {
                    cluster.CategoryHypothesis = joinedCategory;
                }
            }
            AddMember(cluster, card);
            _lastFormationEdges = edgeMember == null
                ? Array.Empty<FirstContactClusterFormationEdge>()
                : new[]
                {
                    new FirstContactClusterFormationEdge(
                        FirstContactSemanticMapLayout.BuildCardNodeId(card),
                        FirstContactSemanticMapLayout.BuildCardNodeId(edgeMember),
                        semanticSimilarity,
                        confirmed: true)
                };
            LogAssignment(card, cluster, "JOIN");
            return cluster;
        }

        public void RegisterPendingCard(
            SemanticCardRecord card,
            IReadOnlyList<FirstContactSemanticGroupCandidate> candidates,
            IReadOnlyList<SemanticClusterRecord> integrityConflictClusters = null)
        {
            if (!RegisterCard(card))
            {
                return;
            }

            card.ClusterId = string.Empty;
            card.SemanticGroupAssignment = FirstContactSemanticGroupAssignmentState.Pending;
            _pendingCards.Add(card);
            _lastFormationEdges = BuildCandidateEdges(card, candidates);

            if (integrityConflictClusters != null)
            {
                for (int i = 0; i < integrityConflictClusters.Count; i++)
                {
                    if (integrityConflictClusters[i] != null)
                    {
                        integrityConflictClusters[i].HasIntegrityConflict = true;
                        integrityConflictClusters[i].IsStable = false;
                    }
                }
            }

            LogAssignment(
                card,
                null,
                integrityConflictClusters?.Count > 1 ? "PENDING-CONFLICT" : "PENDING");
        }

        public IReadOnlyList<FirstContactSemanticGroupCandidate> FindGroupCandidates(
            SemanticCardRecord card,
            int maximumCandidates)
        {
            var candidates = new List<FirstContactSemanticGroupCandidate>();
            if (card?.Embedding == null || card.Embedding.Length == 0 || _embeddingService == null)
            {
                return candidates;
            }

            for (int i = 0; i < _clusters.Count; i++)
            {
                SemanticClusterRecord cluster = _clusters[i];
                if (cluster == null || cluster.Members.Count == 0)
                {
                    continue;
                }

                float similarity = cluster.Centroid != null
                    ? _embeddingService.Similarity(card.Embedding, cluster.Centroid)
                    : FindBestMemberSimilarity(card, cluster);
                candidates.Add(new FirstContactSemanticGroupCandidate(cluster, similarity));
            }

            candidates.Sort((left, right) =>
                right.SemanticSimilarity.CompareTo(left.SemanticSimilarity));
            int limit = Mathf.Max(1, maximumCandidates);
            if (candidates.Count > limit)
            {
                candidates.RemoveRange(limit, candidates.Count - limit);
            }

            return candidates;
        }

        public IReadOnlyList<SemanticCardRecord> BuildRepresentativeMembers(
            SemanticClusterRecord cluster,
            int maximumMembers)
        {
            var representatives = new List<SemanticCardRecord>();
            if (cluster == null || cluster.Members.Count == 0)
            {
                return representatives;
            }

            int limit = Mathf.Clamp(maximumMembers, 1, cluster.Members.Count);
            if (cluster.Members.Count <= limit || cluster.Centroid == null || _embeddingService == null)
            {
                for (int i = 0; i < limit; i++)
                {
                    representatives.Add(cluster.Members[i]);
                }

                return representatives;
            }

            var ranked = new List<MemberSimilarity>(cluster.Members.Count);
            for (int i = 0; i < cluster.Members.Count; i++)
            {
                SemanticCardRecord member = cluster.Members[i];
                float similarity = member?.Embedding == null
                    ? -1f
                    : _embeddingService.Similarity(member.Embedding, cluster.Centroid);
                ranked.Add(new MemberSimilarity(member, similarity));
            }

            ranked.Sort((left, right) => left.Similarity.CompareTo(right.Similarity));
            int low = 0;
            int high = ranked.Count - 1;
            while (representatives.Count < limit && low <= high)
            {
                representatives.Add(ranked[high--].Card);
                if (representatives.Count < limit && low <= high)
                {
                    representatives.Add(ranked[low++].Card);
                }
            }

            return representatives;
        }

        public SemanticClusterRecord FindCluster(string clusterId)
        {
            if (string.IsNullOrWhiteSpace(clusterId))
            {
                return null;
            }

            for (int i = 0; i < _clusters.Count; i++)
            {
                if (string.Equals(_clusters[i].Id, clusterId, StringComparison.OrdinalIgnoreCase))
                {
                    return _clusters[i];
                }
            }

            return null;
        }

        public bool TryAssignMeaning(string clusterId, string meaning)
        {
            SemanticClusterRecord cluster = FindCluster(clusterId);
            string normalizedMeaning = FirstContactProbeProcessor.NormalizePlayerLabelText(meaning);
            if (cluster == null || !cluster.IsStable || string.IsNullOrWhiteSpace(normalizedMeaning))
            {
                return false;
            }

            cluster.ProvisionalName = normalizedMeaning;
            cluster.MeaningAssignedByPlayer = true;
            return true;
        }

        public bool TryCreateWaveformProfile(
            SemanticCardRecord card,
            int sessionSeed,
            out BrainwaveSemanticProfile profile)
        {
            profile = BrainwaveSemanticProfile.Invalid;
            if (card?.Embedding == null)
            {
                return false;
            }

            return TryCreateWaveformProfile(
                card.Embedding,
                card.OriginalLabel,
                Mathf.Max(1, card.ProbeIndex + 1),
                sessionSeed,
                out profile);
        }

        public bool TryCreateWaveformProfile(
            float[] embedding,
            string label,
            int sampleIndex,
            int sessionSeed,
            out BrainwaveSemanticProfile profile)
        {
            profile = BrainwaveSemanticProfile.Invalid;
            if (embedding == null)
            {
                return false;
            }

            FirstContactSemanticSettings settings = GetSettings();
            return BrainwaveEmbeddingProfileMapper.TryCreate(
                embedding,
                label,
                Mathf.Max(1, sampleIndex),
                sessionSeed,
                settings.waveformProjectionSeed,
                settings.waveformFeatureCount,
                settings.waveformSemanticInfluence,
                settings.waveformSessionJitter,
                out profile);
        }

        public bool TryCreateTokenWaveformProfile(
            string token,
            int sampleIndex,
            int sessionSeed,
            out BrainwaveSemanticProfile profile)
        {
            profile = BrainwaveSemanticProfile.Invalid;
            if (string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            float[] tokenVector = BuildSyntheticTokenVector(token.Trim(), 24);
            return TryCreateWaveformProfile(
                tokenVector,
                token,
                sampleIndex,
                sessionSeed,
                out profile);
        }

        private bool RegisterCard(SemanticCardRecord card)
        {
            if (card == null)
            {
                return false;
            }

            for (int i = 0; i < _cards.Count; i++)
            {
                if (ReferenceEquals(_cards[i], card))
                {
                    return false;
                }
            }

            if (string.IsNullOrWhiteSpace(card.Id))
            {
                card.Id = $"CARD-{_nextCardIndex++:000}";
            }

            _cards.Add(card);
            return true;
        }

        private SemanticClusterRecord CreateCluster()
        {
            return new SemanticClusterRecord
            {
                Id = $"CLUSTER-{_nextClusterIndex++:00}",
                CategoryHypothesis = string.Empty,
                ProvisionalName = string.Empty,
                MeaningAssignedByPlayer = false,
                IsStable = false,
                Cohesion = 0f,
                Version = 0,
                HasIntegrityConflict = false
            };
        }

        private void AddMember(SemanticClusterRecord cluster, SemanticCardRecord card)
        {
            cluster.Members.Add(card);
            cluster.Version++;
            card.ClusterId = cluster.Id;
            card.SemanticGroupAssignment = FirstContactSemanticGroupAssignmentState.Assigned;
            RecalculateCluster(cluster);
            if (!cluster.HasIntegrityConflict &&
                cluster.Members.Count >= GetSettings().minClusterMembers)
            {
                cluster.IsStable = true;
            }
        }

        private void RecalculateCluster(SemanticClusterRecord cluster)
        {
            var vectors = new List<float[]>();
            for (int i = 0; i < cluster.Members.Count; i++)
            {
                if (cluster.Members[i]?.Embedding != null)
                {
                    vectors.Add(cluster.Members[i].Embedding);
                }
            }

            cluster.Centroid = null;
            if (_embeddingService != null)
            {
                _embeddingService.TryBuildCentroid(vectors, out cluster.Centroid);
            }
            cluster.Cohesion = CalculateCohesion(cluster);
        }

        private float CalculateCohesion(SemanticClusterRecord cluster)
        {
            if (_embeddingService == null || cluster?.Centroid == null || cluster.Members.Count == 0)
            {
                return 0f;
            }

            float sum = 0f;
            int count = 0;
            for (int i = 0; i < cluster.Members.Count; i++)
            {
                float[] embedding = cluster.Members[i]?.Embedding;
                if (embedding == null)
                {
                    continue;
                }

                sum += _embeddingService.Similarity(embedding, cluster.Centroid);
                count++;
            }

            return count > 0 ? sum / count : 0f;
        }

        private SemanticClusterRecord ResolveOwnedCluster(SemanticClusterRecord cluster)
        {
            if (cluster == null)
            {
                return null;
            }

            for (int i = 0; i < _clusters.Count; i++)
            {
                if (ReferenceEquals(_clusters[i], cluster) ||
                    string.Equals(_clusters[i].Id, cluster.Id, StringComparison.OrdinalIgnoreCase))
                {
                    return _clusters[i];
                }
            }

            return null;
        }

        private SemanticCardRecord FindClosestMember(
            SemanticCardRecord card,
            SemanticClusterRecord cluster)
        {
            SemanticCardRecord best = null;
            float bestScore = float.NegativeInfinity;
            for (int i = 0; i < cluster.Members.Count; i++)
            {
                SemanticCardRecord member = cluster.Members[i];
                if (member?.Embedding == null || card?.Embedding == null)
                {
                    continue;
                }

                float score = _embeddingService.Similarity(card.Embedding, member.Embedding);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = member;
                }
            }

            return best ?? (cluster.Members.Count > 0 ? cluster.Members[0] : null);
        }

        private float FindBestMemberSimilarity(
            SemanticCardRecord card,
            SemanticClusterRecord cluster)
        {
            float best = -1f;
            for (int i = 0; i < cluster.Members.Count; i++)
            {
                if (cluster.Members[i]?.Embedding != null)
                {
                    best = Mathf.Max(
                        best,
                        _embeddingService.Similarity(card.Embedding, cluster.Members[i].Embedding));
                }
            }

            return best;
        }

        private static FirstContactClusterFormationEdge[] BuildCandidateEdges(
            SemanticCardRecord card,
            IReadOnlyList<FirstContactSemanticGroupCandidate> candidates)
        {
            if (card == null || candidates == null || candidates.Count == 0)
            {
                return Array.Empty<FirstContactClusterFormationEdge>();
            }

            var edges = new List<FirstContactClusterFormationEdge>(candidates.Count);
            string activeNodeId = FirstContactSemanticMapLayout.BuildCardNodeId(card);
            for (int i = 0; i < candidates.Count; i++)
            {
                SemanticClusterRecord cluster = candidates[i].Cluster;
                if (cluster == null || cluster.Members.Count == 0)
                {
                    continue;
                }

                edges.Add(new FirstContactClusterFormationEdge(
                    activeNodeId,
                    FirstContactSemanticMapLayout.BuildCardNodeId(cluster.Members[0]),
                    candidates[i].SemanticSimilarity,
                    confirmed: false));
            }

            return edges.ToArray();
        }

        private void LogAssignment(
            SemanticCardRecord card,
            SemanticClusterRecord cluster,
            string decision)
        {
            if (_debugSettings?.logClusterUpdates != true)
            {
                return;
            }

            Debug.Log(
                $"[FirstContactSemanticMemory] Card '{card?.OriginalLabel}' decision={decision} " +
                $"group={cluster?.Id ?? "NONE"} members={cluster?.Members.Count ?? 0} " +
                $"stable={cluster?.IsStable ?? false}");
        }

        private FirstContactSemanticSettings GetSettings()
        {
            return _settings != null ? _settings : ScriptableObject.CreateInstance<FirstContactSemanticSettings>();
        }

        private static float[] BuildSyntheticTokenVector(string token, int dimensions)
        {
            int count = Mathf.Max(8, dimensions);
            var vector = new float[count];
            double sum = 0d;
            for (int i = 0; i < count; i++)
            {
                float value = StableSignedUnit(token, i);
                vector[i] = value;
                sum += value * value;
            }

            if (sum <= double.Epsilon)
            {
                vector[0] = 1f;
                return vector;
            }

            float inv = (float)(1d / Math.Sqrt(sum));
            for (int i = 0; i < vector.Length; i++)
            {
                vector[i] *= inv;
            }

            return vector;
        }

        private static float StableSignedUnit(string token, int salt)
        {
            unchecked
            {
                uint hash = 2166136261u ^ (uint)salt;
                for (int i = 0; i < token.Length; i++)
                {
                    hash ^= char.ToLowerInvariant(token[i]);
                    hash *= 16777619u;
                }

                hash ^= hash >> 16;
                hash *= 0x7FEB352Du;
                hash ^= hash >> 15;
                hash *= 0x846CA68Bu;
                hash ^= hash >> 16;
                return ((hash & 0x00FFFFFFu) / 8388607.5f) - 1f;
            }
        }

        private readonly struct MemberSimilarity
        {
            public MemberSimilarity(SemanticCardRecord card, float similarity)
            {
                Card = card;
                Similarity = similarity;
            }

            public SemanticCardRecord Card { get; }
            public float Similarity { get; }
        }
    }
}
