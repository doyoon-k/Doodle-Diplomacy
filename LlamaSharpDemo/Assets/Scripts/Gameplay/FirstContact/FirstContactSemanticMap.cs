using System;
using System.Collections.Generic;
using UnityEngine;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    public enum FirstContactSemanticMapNodeKind
    {
        UnknownSlot,
        Card,
        StableCluster
    }

    public sealed class FirstContactSemanticMapNode
    {
        public string Id;
        public string Label;
        public string SecondaryLabel;
        public FirstContactSemanticMapNodeKind Kind;
        public Vector2 Position;
        public float[] Embedding;
        public bool IsActive;
        public char Marker;
    }

    public readonly struct FirstContactSemanticMapLink
    {
        public readonly string FromId;
        public readonly string ToId;
        public readonly float Strength;

        public FirstContactSemanticMapLink(string fromId, string toId, float strength)
        {
            FromId = fromId ?? string.Empty;
            ToId = toId ?? string.Empty;
            Strength = strength;
        }
    }

    public sealed class FirstContactSemanticMapSnapshot
    {
        public readonly List<FirstContactSemanticMapNode> Nodes = new();
        public readonly List<FirstContactSemanticMapLink> Links = new();

        public FirstContactSemanticMapNode FindNode(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return null;
            }

            for (int i = 0; i < Nodes.Count; i++)
            {
                if (string.Equals(Nodes[i].Id, id, StringComparison.Ordinal))
                {
                    return Nodes[i];
                }
            }

            return null;
        }
    }

    public sealed class FirstContactSemanticMapLayout
    {
        private readonly Dictionary<string, Vector2> _positions = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Vector2> _velocities = new(StringComparer.Ordinal);
        private int _sessionSeed = 1;

        public void Reset(int sessionSeed)
        {
            _sessionSeed = Mathf.Max(1, sessionSeed);
            _positions.Clear();
            _velocities.Clear();
        }

        public FirstContactSemanticMapSnapshot BuildSnapshot(
            AlienQuestion question,
            IReadOnlyList<SemanticCardRecord> cards,
            IReadOnlyList<SemanticClusterRecord> clusters,
            SemanticCardRecord activeCard,
            string activeUnknownId,
            FirstContactSemanticSettings settings)
        {
            settings ??= ScriptableObject.CreateInstance<FirstContactSemanticSettings>();
            var snapshot = new FirstContactSemanticMapSnapshot();
            string activeCardNodeId = BuildCardNodeId(activeCard);
            string activeUnknownNodeId = BuildUnknownNodeId(question, activeUnknownId);

            AddUnknownNodes(snapshot, question, activeUnknownNodeId);
            AddCardNodes(snapshot, cards, activeCard, settings.semanticMapMaxCards, activeCardNodeId);
            AddStableClusterNodes(snapshot, clusters);

            if (snapshot.Nodes.Count == 0)
            {
                return snapshot;
            }

            EnsurePositions(snapshot.Nodes);
            RunLayout(snapshot.Nodes, settings);
            AssignNodePositions(snapshot.Nodes);
            AddRelatedLinks(snapshot, settings);
            AddActiveLinks(snapshot, activeCardNodeId, activeUnknownNodeId);
            return snapshot;
        }

        public static string BuildUnknownNodeId(AlienQuestion question, string unknownId)
        {
            string questionId = string.IsNullOrWhiteSpace(question?.Id) ? "QUESTION" : question.Id.Trim();
            string normalized = FirstContactUnknownSlotDefinition.NormalizeUnknownId(unknownId);
            return string.IsNullOrWhiteSpace(normalized)
                ? string.Empty
                : $"U:{questionId}:{normalized}";
        }

        public static string BuildCardNodeId(SemanticCardRecord card)
        {
            return card == null || string.IsNullOrWhiteSpace(card.Id) ? string.Empty : $"C:{card.Id}";
        }

        private static void AddUnknownNodes(
            FirstContactSemanticMapSnapshot snapshot,
            AlienQuestion question,
            string activeUnknownNodeId)
        {
            if (question == null)
            {
                return;
            }

            for (int i = 0; i < question.UnknownSlots.Count; i++)
            {
                UnknownSlot slot = question.UnknownSlots[i];
                if (slot?.AnchorSet == null || !slot.AnchorSet.IsValid)
                {
                    continue;
                }

                string id = BuildUnknownNodeId(question, slot.Id);
                snapshot.Nodes.Add(new FirstContactSemanticMapNode
                {
                    Id = id,
                    Label = slot.Id,
                    SecondaryLabel = slot.GetDisplayToken(),
                    Kind = FirstContactSemanticMapNodeKind.UnknownSlot,
                    Embedding = slot.AnchorSet.Centroid,
                    IsActive = string.Equals(id, activeUnknownNodeId, StringComparison.Ordinal),
                    Marker = GetUnknownMarker(i)
                });
            }
        }

        private static void AddCardNodes(
            FirstContactSemanticMapSnapshot snapshot,
            IReadOnlyList<SemanticCardRecord> cards,
            SemanticCardRecord activeCard,
            int maxCards,
            string activeCardNodeId)
        {
            if (cards == null)
            {
                if (activeCard != null)
                {
                    AddCardNode(snapshot, activeCard, activeCardNodeId);
                }

                return;
            }

            int count = Mathf.Max(1, maxCards);
            int start = Mathf.Max(0, cards.Count - count);
            for (int i = start; i < cards.Count; i++)
            {
                AddCardNode(snapshot, cards[i], activeCardNodeId);
            }

            if (activeCard != null && snapshot.FindNode(activeCardNodeId) == null)
            {
                AddCardNode(snapshot, activeCard, activeCardNodeId);
            }
        }

        private static void AddCardNode(
            FirstContactSemanticMapSnapshot snapshot,
            SemanticCardRecord card,
            string activeCardNodeId)
        {
            if (card?.Embedding == null || card.Embedding.Length == 0)
            {
                return;
            }

            string id = BuildCardNodeId(card);
            if (string.IsNullOrWhiteSpace(id) || snapshot.FindNode(id) != null)
            {
                return;
            }

            snapshot.Nodes.Add(new FirstContactSemanticMapNode
            {
                Id = id,
                Label = string.IsNullOrWhiteSpace(card.Label) ? "CARD" : card.Label.Trim().ToUpperInvariant(),
                SecondaryLabel = card.ClusterId,
                Kind = FirstContactSemanticMapNodeKind.Card,
                Embedding = card.Embedding,
                IsActive = string.Equals(id, activeCardNodeId, StringComparison.Ordinal),
                Marker = string.Equals(id, activeCardNodeId, StringComparison.Ordinal) ? '@' : 'o'
            });
        }

        private static void AddStableClusterNodes(
            FirstContactSemanticMapSnapshot snapshot,
            IReadOnlyList<SemanticClusterRecord> clusters)
        {
            if (clusters == null)
            {
                return;
            }

            for (int i = 0; i < clusters.Count; i++)
            {
                SemanticClusterRecord cluster = clusters[i];
                if (cluster == null || !cluster.IsStable || cluster.Centroid == null)
                {
                    continue;
                }

                snapshot.Nodes.Add(new FirstContactSemanticMapNode
                {
                    Id = $"K:{cluster.Id}",
                    Label = cluster.Id,
                    SecondaryLabel = cluster.DisplayName,
                    Kind = FirstContactSemanticMapNodeKind.StableCluster,
                    Embedding = cluster.Centroid,
                    Marker = '#'
                });
            }
        }

        private void EnsurePositions(IReadOnlyList<FirstContactSemanticMapNode> nodes)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                FirstContactSemanticMapNode node = nodes[i];
                if (_positions.ContainsKey(node.Id))
                {
                    continue;
                }

                _positions[node.Id] = ResolveInitialPosition(node, nodes);
                _velocities[node.Id] = Vector2.zero;
            }
        }

        private Vector2 ResolveInitialPosition(
            FirstContactSemanticMapNode node,
            IReadOnlyList<FirstContactSemanticMapNode> nodes)
        {
            FirstContactSemanticMapNode bestNode = null;
            float bestScore = -1f;
            for (int i = 0; i < nodes.Count; i++)
            {
                FirstContactSemanticMapNode candidate = nodes[i];
                if (candidate == node || !_positions.ContainsKey(candidate.Id))
                {
                    continue;
                }

                float score = Cosine(node.Embedding, candidate.Embedding);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestNode = candidate;
                }
            }

            Vector2 direction = DeterministicUnitVector(node.Id);
            if (bestNode != null)
            {
                float normalized = Mathf.Clamp01((bestScore + 1f) * 0.5f);
                float distance = Mathf.Lerp(0.18f, 0.72f, 1f - normalized);
                return ClampToMap(_positions[bestNode.Id] + direction * distance);
            }

            float radius = node.Kind == FirstContactSemanticMapNodeKind.UnknownSlot ? 0.42f : 0.66f;
            return ClampToMap(direction * radius);
        }

        private void RunLayout(
            IReadOnlyList<FirstContactSemanticMapNode> nodes,
            FirstContactSemanticSettings settings)
        {
            int iterations = Mathf.Max(1, settings.semanticMapLayoutIterations);
            for (int iteration = 0; iteration < iterations; iteration++)
            {
                var forces = new Dictionary<string, Vector2>(StringComparer.Ordinal);
                for (int i = 0; i < nodes.Count; i++)
                {
                    forces[nodes[i].Id] = Vector2.zero;
                }

                for (int i = 0; i < nodes.Count; i++)
                {
                    for (int j = i + 1; j < nodes.Count; j++)
                    {
                        ApplyPairForces(nodes[i], nodes[j], settings, forces);
                    }
                }

                for (int i = 0; i < nodes.Count; i++)
                {
                    FirstContactSemanticMapNode node = nodes[i];
                    Vector2 velocity = _velocities.TryGetValue(node.Id, out Vector2 currentVelocity)
                        ? currentVelocity
                        : Vector2.zero;
                    velocity = (velocity + forces[node.Id]) * settings.semanticMapDamping;
                    velocity = ClampMagnitude(velocity, settings.semanticMapMaxStep);
                    _positions[node.Id] = ClampToMap(_positions[node.Id] + velocity);
                    _velocities[node.Id] = velocity;
                }
            }
        }

        private void ApplyPairForces(
            FirstContactSemanticMapNode first,
            FirstContactSemanticMapNode second,
            FirstContactSemanticSettings settings,
            IDictionary<string, Vector2> forces)
        {
            Vector2 firstPosition = _positions[first.Id];
            Vector2 secondPosition = _positions[second.Id];
            Vector2 delta = secondPosition - firstPosition;
            float distance = delta.magnitude;
            if (distance <= 0.0001f)
            {
                delta = DeterministicUnitVector(first.Id + second.Id) * 0.001f;
                distance = delta.magnitude;
            }

            Vector2 direction = delta / distance;
            float repulsion = settings.semanticMapRepulsionStrength / Mathf.Max(0.04f, distance * distance);
            forces[first.Id] -= direction * repulsion;
            forces[second.Id] += direction * repulsion;

            float score = Cosine(first.Embedding, second.Embedding);
            if (score < settings.semanticMapAttractionThreshold)
            {
                return;
            }

            float normalized = Mathf.Clamp01((score - settings.semanticMapAttractionThreshold) /
                                             Mathf.Max(0.0001f, 1f - settings.semanticMapAttractionThreshold));
            float targetDistance = Mathf.Lerp(0.16f, 0.62f, 1f - normalized);
            Vector2 attraction = direction *
                                 ((distance - targetDistance) *
                                  settings.semanticMapAttractionStrength *
                                  Mathf.Max(0.12f, normalized));
            forces[first.Id] += attraction;
            forces[second.Id] -= attraction;
        }

        private void AssignNodePositions(IReadOnlyList<FirstContactSemanticMapNode> nodes)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                FirstContactSemanticMapNode node = nodes[i];
                if (_positions.TryGetValue(node.Id, out Vector2 position))
                {
                    node.Position = position;
                }
            }
        }

        private static void AddActiveLinks(
            FirstContactSemanticMapSnapshot snapshot,
            string activeCardNodeId,
            string activeUnknownNodeId)
        {
            FirstContactSemanticMapNode activeCard = snapshot.FindNode(activeCardNodeId);
            FirstContactSemanticMapNode activeUnknown = snapshot.FindNode(activeUnknownNodeId);
            if (activeCard == null || activeUnknown == null)
            {
                return;
            }

            AddLinkIfMissing(snapshot, new FirstContactSemanticMapLink(
                activeCard.Id,
                activeUnknown.Id,
                Cosine(activeCard.Embedding, activeUnknown.Embedding)));
        }

        private static void AddRelatedLinks(
            FirstContactSemanticMapSnapshot snapshot,
            FirstContactSemanticSettings settings)
        {
            if (snapshot == null || snapshot.Nodes.Count < 2)
            {
                return;
            }

            float threshold = settings != null ? settings.semanticMapAttractionThreshold : 0.36f;
            int maxLinks = Mathf.Max(8, snapshot.Nodes.Count * 2);
            var candidates = new List<FirstContactSemanticMapLink>();
            for (int i = 0; i < snapshot.Nodes.Count; i++)
            {
                FirstContactSemanticMapNode first = snapshot.Nodes[i];
                if (first.Embedding == null)
                {
                    continue;
                }

                for (int j = i + 1; j < snapshot.Nodes.Count; j++)
                {
                    FirstContactSemanticMapNode second = snapshot.Nodes[j];
                    if (second.Embedding == null)
                    {
                        continue;
                    }

                    float score = Cosine(first.Embedding, second.Embedding);
                    if (score < threshold)
                    {
                        continue;
                    }

                    candidates.Add(new FirstContactSemanticMapLink(first.Id, second.Id, score));
                }
            }

            candidates.Sort((a, b) => b.Strength.CompareTo(a.Strength));
            int count = Mathf.Min(maxLinks, candidates.Count);
            for (int i = 0; i < count; i++)
            {
                AddLinkIfMissing(snapshot, candidates[i]);
            }
        }

        private static void AddLinkIfMissing(
            FirstContactSemanticMapSnapshot snapshot,
            FirstContactSemanticMapLink link)
        {
            for (int i = 0; i < snapshot.Links.Count; i++)
            {
                FirstContactSemanticMapLink existing = snapshot.Links[i];
                bool sameDirection = string.Equals(existing.FromId, link.FromId, StringComparison.Ordinal) &&
                                     string.Equals(existing.ToId, link.ToId, StringComparison.Ordinal);
                bool reverseDirection = string.Equals(existing.FromId, link.ToId, StringComparison.Ordinal) &&
                                        string.Equals(existing.ToId, link.FromId, StringComparison.Ordinal);
                if (sameDirection || reverseDirection)
                {
                    if (link.Strength > existing.Strength)
                    {
                        snapshot.Links[i] = link;
                    }

                    return;
                }
            }

            snapshot.Links.Add(link);
        }

        private Vector2 DeterministicUnitVector(string key)
        {
            uint hash = Hash($"{_sessionSeed}:{key}");
            float angle = (hash / (float)uint.MaxValue) * Mathf.PI * 2f;
            return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        }

        private static Vector2 ClampMagnitude(Vector2 value, float maxMagnitude)
        {
            float safeMax = Mathf.Max(0.001f, maxMagnitude);
            return value.magnitude > safeMax ? value.normalized * safeMax : value;
        }

        private static Vector2 ClampToMap(Vector2 value)
        {
            return new Vector2(
                Mathf.Clamp(value.x, -0.96f, 0.96f),
                Mathf.Clamp(value.y, -0.96f, 0.96f));
        }

        private static float Cosine(float[] first, float[] second)
        {
            if (first == null || second == null || first.Length == 0 || first.Length != second.Length)
            {
                return 0f;
            }

            float sum = 0f;
            for (int i = 0; i < first.Length; i++)
            {
                sum += first[i] * second[i];
            }

            return Mathf.Clamp(sum, -1f, 1f);
        }

        private static uint Hash(string value)
        {
            unchecked
            {
                uint hash = 2166136261u;
                for (int i = 0; i < value.Length; i++)
                {
                    hash ^= value[i];
                    hash *= 16777619u;
                }

                return hash == 0u ? 1u : hash;
            }
        }

        private static char GetUnknownMarker(int index)
        {
            return index >= 0 && index <= 8 ? (char)('1' + index) : '?';
        }
    }

}
