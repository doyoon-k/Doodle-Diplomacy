using System;
using System.Collections;
using System.Collections.Generic;
using DoodleDiplomacy.Devices;
using UnityEngine;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    public readonly struct FirstContactResolutionResult
    {
        public readonly UnknownSlot Slot;
        public readonly FirstContactTranslationStage PreviousStage;
        public readonly FirstContactTranslationStage NewStage;
        public readonly float Score;
        public readonly bool Changed;

        public FirstContactResolutionResult(
            UnknownSlot slot,
            FirstContactTranslationStage previousStage,
            FirstContactTranslationStage newStage,
            float score,
            bool changed)
        {
            Slot = slot;
            PreviousStage = previousStage;
            NewStage = newStage;
            Score = score;
            Changed = changed;
        }
    }

    public sealed class FirstContactUnknownResolver
    {
        private readonly FirstContactEmbeddingService _embeddingService;
        private readonly FirstContactSemanticSettings _settings;

        public FirstContactUnknownResolver(
            FirstContactEmbeddingService embeddingService,
            FirstContactSemanticSettings settings)
        {
            _embeddingService = embeddingService;
            _settings = settings;
        }

        public IEnumerator PrepareQuestion(AlienQuestion question)
        {
            if (question == null || _embeddingService == null)
            {
                yield break;
            }

            for (int i = 0; i < question.UnknownSlots.Count; i++)
            {
                UnknownSlot slot = question.UnknownSlots[i];
                TargetConceptEmbedding targetEmbedding = null;
                yield return _embeddingService.EmbedTargetConcept(
                    slot.TargetConcept,
                    result => targetEmbedding = result);
                slot.TargetEmbedding = targetEmbedding;
            }
        }

        public FirstContactResolutionResult EvaluateCard(SemanticCardRecord card, UnknownSlot slot)
        {
            if (card == null || slot == null || card.Embedding == null || slot.TargetEmbedding == null || !slot.TargetEmbedding.IsValid)
            {
                return new FirstContactResolutionResult(
                    slot,
                    slot != null ? slot.Stage : FirstContactTranslationStage.Unknown,
                    slot != null ? slot.Stage : FirstContactTranslationStage.Unknown,
                    0f,
                    false);
            }

            float score = ScoreCardAgainstSlot(card, slot);
            FirstContactTranslationStage nextStage = DetermineStage(score);
            FirstContactTranslationStage previous = slot.Stage;
            bool changed = slot.TryAdvanceTo(nextStage, score);
            return new FirstContactResolutionResult(slot, previous, slot.Stage, score, changed);
        }

        public float ScoreCardAgainstSlot(SemanticCardRecord card, UnknownSlot slot)
        {
            if (card == null || slot == null || card.Embedding == null || slot.TargetEmbedding == null || !slot.TargetEmbedding.IsValid)
            {
                return 0f;
            }

            return _embeddingService != null
                ? _embeddingService.Similarity(card.Embedding, slot.TargetEmbedding.Vector)
                : 0f;
        }

        public FirstContactTranslationStage DetermineStageForScore(float score)
        {
            return DetermineStage(score);
        }

        public bool ApplyAutomaticClusterHints(AlienQuestion question, FirstContactSemanticMemory memory)
        {
            if (question == null || memory == null)
            {
                return false;
            }

            bool changedAny = false;
            IReadOnlyList<SemanticClusterRecord> stableClusters = memory.StableClusters;
            for (int i = 0; i < question.UnknownSlots.Count; i++)
            {
                UnknownSlot slot = question.UnknownSlots[i];
                if (slot == null || slot.Stage >= FirstContactTranslationStage.Partial ||
                    slot.TargetEmbedding == null || !slot.TargetEmbedding.IsValid)
                {
                    continue;
                }

                SemanticClusterRecord bestCluster = null;
                float bestScore = -1f;
                for (int c = 0; c < stableClusters.Count; c++)
                {
                    SemanticClusterRecord cluster = stableClusters[c];
                    if (cluster?.Centroid == null)
                    {
                        continue;
                    }

                    float score = _embeddingService != null
                        ? _embeddingService.Similarity(cluster.Centroid, slot.TargetEmbedding.Vector)
                        : 0f;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestCluster = cluster;
                    }
                }

                if (bestCluster != null && bestScore >= GetSettings().clusterAutoPartialThreshold)
                {
                    if (slot.TryAdvanceTo(FirstContactTranslationStage.Partial, bestScore))
                    {
                        slot.LinkedClusterId = bestCluster.Id;
                        changedAny = true;
                    }
                }
            }

            return changedAny;
        }

        private FirstContactTranslationStage DetermineStage(float score)
        {
            FirstContactSemanticSettings settings = GetSettings();
            if (score >= settings.solvedThreshold)
            {
                return FirstContactTranslationStage.Solved;
            }

            if (score >= settings.partialThreshold)
            {
                return FirstContactTranslationStage.Partial;
            }

            if (score >= settings.hintThreshold)
            {
                return FirstContactTranslationStage.Hint;
            }

            return FirstContactTranslationStage.Unknown;
        }

        private FirstContactSemanticSettings GetSettings()
        {
            return _settings != null ? _settings : ScriptableObject.CreateInstance<FirstContactSemanticSettings>();
        }
    }

    public sealed class FirstContactSemanticMemory
    {
        private readonly FirstContactEmbeddingService _embeddingService;
        private readonly FirstContactSemanticSettings _settings;
        private readonly FirstContactDebugSettings _debugSettings;
        private readonly List<SemanticCardRecord> _cards = new();
        private readonly List<SemanticClusterRecord> _clusters = new();
        private FirstContactClusterFormationEdge[] _lastFormationEdges = Array.Empty<FirstContactClusterFormationEdge>();

        private int _nextCardIndex = 1;
        private int _nextClusterIndex = 1;

        public FirstContactSemanticMemory(
            FirstContactEmbeddingService embeddingService,
            FirstContactSemanticSettings settings,
            FirstContactDebugSettings debugSettings)
        {
            _embeddingService = embeddingService;
            _settings = settings;
            _debugSettings = debugSettings;
        }

        public IReadOnlyList<SemanticCardRecord> Cards => _cards;
        public IReadOnlyList<SemanticClusterRecord> Clusters => _clusters;
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

        public void AddCard(SemanticCardRecord card)
        {
            if (card == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(card.Id))
            {
                card.Id = $"CARD-{_nextCardIndex:000}";
            }

            _nextCardIndex++;
            _cards.Add(card);

            if (card.Embedding == null || card.Embedding.Length == 0 || _embeddingService == null)
            {
                return;
            }

            RebuildClustersFromGraph(card);
            SemanticClusterRecord cluster = FindCluster(card.ClusterId);

            if (_debugSettings != null && _debugSettings.logClusterUpdates)
            {
                Debug.Log(
                    $"[FirstContactSemanticMemory] Card '{card.Label}' mapped to {cluster?.Id ?? "NO-CLUSTER"} " +
                    $"members={cluster?.Members.Count ?? 0} stable={cluster?.IsStable ?? false}");
            }
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

        private void RebuildClustersFromGraph(SemanticCardRecord activeCard)
        {
            if (_embeddingService == null)
            {
                return;
            }

            var candidates = new List<SemanticCardRecord>();
            for (int i = 0; i < _cards.Count; i++)
            {
                SemanticCardRecord card = _cards[i];
                if (card?.Embedding != null && card.Embedding.Length > 0)
                {
                    candidates.Add(card);
                }
            }

            if (candidates.Count == 0)
            {
                _lastFormationEdges = Array.Empty<FirstContactClusterFormationEdge>();
                return;
            }

            List<SemanticClusterRecord> previousClusters = new(_clusters);
            bool[,] linked = BuildClusterGraph(candidates, out float[,] scores);
            CaptureFormationEdges(activeCard, candidates, scores, linked);
            List<List<int>> components = BuildConnectedComponents(candidates.Count, linked);
            var reusedClusterIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            _clusters.Clear();
            for (int i = 0; i < candidates.Count; i++)
            {
                candidates[i].ClusterId = string.Empty;
            }

            for (int i = 0; i < components.Count; i++)
            {
                List<int> component = components[i];
                SemanticClusterRecord cluster = FindReusableCluster(component, candidates, previousClusters, reusedClusterIds);
                if (cluster == null)
                {
                    cluster = CreateCluster();
                }

                ResetCluster(cluster);
                for (int c = 0; c < component.Count; c++)
                {
                    SemanticCardRecord member = candidates[component[c]];
                    member.ClusterId = cluster.Id;
                    cluster.Members.Add(member);
                }

                RecalculateCluster(cluster);
                TryStabilizeCluster(cluster);
                _clusters.Add(cluster);
            }
        }

        private bool[,] BuildClusterGraph(
            IReadOnlyList<SemanticCardRecord> candidates,
            out float[,] scores)
        {
            int count = candidates.Count;
            var linked = new bool[count, count];
            scores = new float[count, count];
            if (count <= 1)
            {
                return linked;
            }

            FirstContactSemanticSettings settings = GetSettings();
            float threshold = settings.clusterJoinThreshold;
            int neighborCount = Mathf.Max(1, settings.clusterNeighborCount);
            var nearest = new bool[count, count];

            for (int i = 0; i < count; i++)
            {
                for (int j = i + 1; j < count; j++)
                {
                    float score = _embeddingService.Similarity(candidates[i].Embedding, candidates[j].Embedding);
                    scores[i, j] = score;
                    scores[j, i] = score;
                }
            }

            for (int i = 0; i < count; i++)
            {
                SelectNearestNeighbors(i, scores, nearest, threshold, neighborCount);
            }

            for (int i = 0; i < count; i++)
            {
                for (int j = i + 1; j < count; j++)
                {
                    if (scores[i, j] >= threshold && nearest[i, j] && nearest[j, i])
                    {
                        linked[i, j] = true;
                        linked[j, i] = true;
                    }
                }
            }

            return linked;
        }

        private void CaptureFormationEdges(
            SemanticCardRecord activeCard,
            IReadOnlyList<SemanticCardRecord> candidates,
            float[,] scores,
            bool[,] linked)
        {
            _lastFormationEdges = Array.Empty<FirstContactClusterFormationEdge>();
            if (activeCard == null || candidates == null || scores == null || linked == null)
            {
                return;
            }

            int activeIndex = -1;
            for (int i = 0; i < candidates.Count; i++)
            {
                if (ReferenceEquals(activeCard, candidates[i]) ||
                    (!string.IsNullOrWhiteSpace(activeCard.Id) &&
                     string.Equals(activeCard.Id, candidates[i]?.Id, StringComparison.OrdinalIgnoreCase)))
                {
                    activeIndex = i;
                    break;
                }
            }

            if (activeIndex < 0)
            {
                return;
            }

            FirstContactSemanticSettings settings = GetSettings();
            float scanThreshold = Mathf.Min(settings.clusterJoinThreshold, settings.semanticMapAttractionThreshold);
            int maxCandidates = 3;
            var indices = new List<int>();
            for (int i = 0; i < candidates.Count; i++)
            {
                if (i == activeIndex)
                {
                    continue;
                }

                if (scores[activeIndex, i] >= scanThreshold)
                {
                    indices.Add(i);
                }
            }

            indices.Sort((a, b) => scores[activeIndex, b].CompareTo(scores[activeIndex, a]));
            int count = Mathf.Min(maxCandidates, indices.Count);
            if (count <= 0)
            {
                return;
            }

            var edges = new FirstContactClusterFormationEdge[count];
            string activeNodeId = FirstContactSemanticMapLayout.BuildCardNodeId(activeCard);
            for (int i = 0; i < count; i++)
            {
                int candidateIndex = indices[i];
                edges[i] = new FirstContactClusterFormationEdge(
                    activeNodeId,
                    FirstContactSemanticMapLayout.BuildCardNodeId(candidates[candidateIndex]),
                    scores[activeIndex, candidateIndex],
                    linked[activeIndex, candidateIndex]);
            }

            _lastFormationEdges = edges;
        }

        private static void SelectNearestNeighbors(
            int source,
            float[,] scores,
            bool[,] nearest,
            float threshold,
            int neighborCount)
        {
            int count = scores.GetLength(0);
            var selected = new bool[count];
            for (int n = 0; n < neighborCount; n++)
            {
                int bestIndex = -1;
                float bestScore = threshold;
                for (int i = 0; i < count; i++)
                {
                    if (i == source || selected[i])
                    {
                        continue;
                    }

                    float score = scores[source, i];
                    if (score >= bestScore)
                    {
                        bestScore = score;
                        bestIndex = i;
                    }
                }

                if (bestIndex < 0)
                {
                    break;
                }

                selected[bestIndex] = true;
                nearest[source, bestIndex] = true;
            }
        }

        private static List<List<int>> BuildConnectedComponents(int count, bool[,] linked)
        {
            var components = new List<List<int>>();
            var visited = new bool[count];
            for (int i = 0; i < count; i++)
            {
                if (visited[i])
                {
                    continue;
                }

                var component = new List<int>();
                var stack = new Stack<int>();
                stack.Push(i);
                visited[i] = true;
                while (stack.Count > 0)
                {
                    int current = stack.Pop();
                    component.Add(current);
                    for (int next = 0; next < count; next++)
                    {
                        if (visited[next] || !linked[current, next])
                        {
                            continue;
                        }

                        visited[next] = true;
                        stack.Push(next);
                    }
                }

                component.Sort();
                components.Add(component);
            }

            return components;
        }

        private static SemanticClusterRecord FindReusableCluster(
            IReadOnlyList<int> component,
            IReadOnlyList<SemanticCardRecord> candidates,
            IReadOnlyList<SemanticClusterRecord> previousClusters,
            ISet<string> reusedClusterIds)
        {
            SemanticClusterRecord best = null;
            int bestOverlap = 0;
            for (int i = 0; i < previousClusters.Count; i++)
            {
                SemanticClusterRecord cluster = previousClusters[i];
                if (cluster == null ||
                    string.IsNullOrWhiteSpace(cluster.Id) ||
                    reusedClusterIds.Contains(cluster.Id))
                {
                    continue;
                }

                int overlap = CountMemberOverlap(component, candidates, cluster);
                if (overlap > bestOverlap)
                {
                    bestOverlap = overlap;
                    best = cluster;
                }
            }

            if (best != null)
            {
                reusedClusterIds.Add(best.Id);
            }

            return best;
        }

        private static int CountMemberOverlap(
            IReadOnlyList<int> component,
            IReadOnlyList<SemanticCardRecord> candidates,
            SemanticClusterRecord cluster)
        {
            int overlap = 0;
            for (int i = 0; i < component.Count; i++)
            {
                SemanticCardRecord candidate = candidates[component[i]];
                for (int m = 0; m < cluster.Members.Count; m++)
                {
                    if (ReferenceEquals(candidate, cluster.Members[m]) ||
                        (!string.IsNullOrWhiteSpace(candidate.Id) &&
                         string.Equals(candidate.Id, cluster.Members[m]?.Id, StringComparison.OrdinalIgnoreCase)))
                    {
                        overlap++;
                        break;
                    }
                }
            }

            return overlap;
        }

        private static void ResetCluster(SemanticClusterRecord cluster)
        {
            cluster.Members.Clear();
            cluster.Centroid = null;
            cluster.ProvisionalName = string.Empty;
            cluster.IsStable = false;
            cluster.Cohesion = 0f;
        }

        private SemanticClusterRecord CreateCluster()
        {
            return new SemanticClusterRecord
            {
                Id = $"CLUSTER-{_nextClusterIndex++:00}",
                ProvisionalName = string.Empty,
                IsStable = false,
                Cohesion = 0f
            };
        }

        private void RecalculateCluster(SemanticClusterRecord cluster)
        {
            if (cluster == null || _embeddingService == null)
            {
                return;
            }

            var vectors = new List<float[]>();
            for (int i = 0; i < cluster.Members.Count; i++)
            {
                if (cluster.Members[i].Embedding != null)
                {
                    vectors.Add(cluster.Members[i].Embedding);
                }
            }

            _embeddingService.TryBuildCentroid(vectors, out cluster.Centroid);
            cluster.Cohesion = CalculateCohesion(cluster);
        }

        private float CalculateCohesion(SemanticClusterRecord cluster)
        {
            if (cluster == null || cluster.Centroid == null || cluster.Members.Count == 0)
            {
                return 0f;
            }

            float sum = 0f;
            int count = 0;
            for (int i = 0; i < cluster.Members.Count; i++)
            {
                float[] embedding = cluster.Members[i].Embedding;
                if (embedding == null)
                {
                    continue;
                }

                sum += _embeddingService.Similarity(embedding, cluster.Centroid);
                count++;
            }

            return count > 0 ? sum / count : 0f;
        }

        private float CalculatePairwiseSimilarity(SemanticClusterRecord cluster)
        {
            if (cluster == null || cluster.Members.Count < 2)
            {
                return 0f;
            }

            float sum = 0f;
            int count = 0;
            for (int i = 0; i < cluster.Members.Count; i++)
            {
                float[] first = cluster.Members[i].Embedding;
                if (first == null)
                {
                    continue;
                }

                for (int j = i + 1; j < cluster.Members.Count; j++)
                {
                    float[] second = cluster.Members[j].Embedding;
                    if (second == null)
                    {
                        continue;
                    }

                    sum += _embeddingService.Similarity(first, second);
                    count++;
                }
            }

            return count > 0 ? sum / count : 0f;
        }

        private void TryStabilizeCluster(SemanticClusterRecord cluster)
        {
            if (cluster == null || cluster.IsStable)
            {
                return;
            }

            FirstContactSemanticSettings settings = GetSettings();
            if (cluster.Members.Count < settings.minClusterMembers ||
                cluster.Cohesion < settings.minClusterCohesion ||
                CalculatePairwiseSimilarity(cluster) < settings.minClusterPairwiseSimilarity)
            {
                return;
            }

            cluster.IsStable = true;
            cluster.ProvisionalName = InferClusterName(cluster);
        }

        private static string InferClusterName(SemanticClusterRecord cluster)
        {
            bool HasAny(params string[] tokens)
            {
                for (int i = 0; i < cluster.Members.Count; i++)
                {
                    string label = cluster.Members[i].Label ?? string.Empty;
                    for (int t = 0; t < tokens.Length; t++)
                    {
                        if (label.IndexOf(tokens[t], StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            return true;
                        }
                    }
                }

                return false;
            }

            if (HasAny("shield", "wall", "helmet", "bunker", "armor", "lock", "door", "fence"))
            {
                return "[PROTECTION?]";
            }

            if (HasAny("apple", "bread", "rice", "meat", "fish", "fruit", "banana", "egg", "cake"))
            {
                return "[FOOD?]";
            }

            if (HasAny("hammer", "wrench", "saw", "shovel", "needle", "key", "scissors", "tool"))
            {
                return "[TOOL?]";
            }

            if (HasAny("gun", "weapon", "knife", "sword", "missile", "fire", "monster", "bomb"))
            {
                return "[DANGER?]";
            }

            return $"[{cluster.Id}]";
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
                card.Label,
                Mathf.Max(1, card.TurnIndex + 1),
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
    }
}
