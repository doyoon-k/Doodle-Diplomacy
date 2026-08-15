using System;
using System.Collections.Generic;
using DoodleDiplomacy.Localization;
using UnityEngine;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    public enum FirstContactSemanticMapNodeKind
    {
        Card,
        StableCluster,
        BootstrapCategory
    }

    public enum FirstContactSemanticMapLinkKind
    {
        Normal,
        Candidate,
        Rejected,
        Confirmed
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
        public string BootstrapCategoryId;
        public bool IsBootstrapDetached;
        public bool IsSemanticGroupPending;
        public int TraceCount;
        public int RequiredTraceCount;
        public bool IsBootstrapCategoryStable;
        public float Pulse;
    }

    public readonly struct FirstContactSemanticMapLink
    {
        public readonly string FromId;
        public readonly string ToId;
        public readonly float Strength;
        public readonly FirstContactSemanticMapLinkKind Kind;

        public FirstContactSemanticMapLink(
            string fromId,
            string toId,
            float strength,
            FirstContactSemanticMapLinkKind kind = FirstContactSemanticMapLinkKind.Normal)
        {
            FromId = fromId ?? string.Empty;
            ToId = toId ?? string.Empty;
            Strength = strength;
            Kind = kind;
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
            IReadOnlyList<SemanticCardRecord> cards,
            IReadOnlyList<SemanticClusterRecord> clusters,
            SemanticCardRecord activeCard,
            FirstContactSemanticSettings settings)
        {
            settings ??= ScriptableObject.CreateInstance<FirstContactSemanticSettings>();
            var snapshot = new FirstContactSemanticMapSnapshot();
            string activeCardNodeId = BuildCardNodeId(activeCard);
            AddCardNodes(snapshot, cards, activeCard, activeCardNodeId);
            AddStableClusterNodes(snapshot, clusters);

            if (snapshot.Nodes.Count == 0)
            {
                return snapshot;
            }

            EnsurePositions(snapshot.Nodes);
            RunLayout(snapshot.Nodes, settings);
            AssignNodePositions(snapshot.Nodes);
            AddRelatedLinks(snapshot, settings);
            return snapshot;
        }

        public static string BuildCardNodeId(SemanticCardRecord card)
        {
            return card == null || string.IsNullOrWhiteSpace(card.Id) ? string.Empty : $"C:{card.Id}";
        }

        public static string BuildClusterNodeId(SemanticClusterRecord cluster)
        {
            return cluster == null ? string.Empty : BuildClusterNodeId(cluster.Id);
        }

        public static string BuildClusterNodeId(string clusterId)
        {
            return string.IsNullOrWhiteSpace(clusterId) ? string.Empty : $"K:{clusterId.Trim()}";
        }


        private static void AddCardNodes(
            FirstContactSemanticMapSnapshot snapshot,
            IReadOnlyList<SemanticCardRecord> cards,
            SemanticCardRecord activeCard,
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

            for (int i = 0; i < cards.Count; i++)
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
                Label = ResolveCardDisplayLabel(card),
                SecondaryLabel = card.ClusterId,
                Kind = FirstContactSemanticMapNodeKind.Card,
                Embedding = card.Embedding,
                IsActive = string.Equals(id, activeCardNodeId, StringComparison.Ordinal),
                Marker = string.Equals(id, activeCardNodeId, StringComparison.Ordinal) ? '@' : 'o',
                BootstrapCategoryId = card.BootstrapCategoryId,
                IsBootstrapDetached = card.BootstrapCategoryEvaluated && !card.BootstrapCategoryAccepted,
                IsSemanticGroupPending =
                    card.SemanticGroupAssignment == FirstContactSemanticGroupAssignmentState.Pending
            });
        }

        private static string ResolveCardDisplayLabel(SemanticCardRecord card)
        {
            if (!string.IsNullOrWhiteSpace(card?.OriginalLabel))
            {
                return card.OriginalLabel.Trim().ToUpperInvariant();
            }

            if (LlmLocalizationSettings.IsEnglishLocale(L10n.CurrentLocale))
            {
                return string.IsNullOrWhiteSpace(card?.NormalizedLabel)
                    ? "CARD"
                    : card.NormalizedLabel.Trim().ToUpperInvariant();
            }

            return L10n.T("first_contact.terminal.fallback.unknown", "UNKNOWN").ToUpperInvariant();
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
                    Id = BuildClusterNodeId(cluster),
                    Label = cluster.Id,
                    SecondaryLabel = FirstContactTerminalLocalization.LocalizeMeaning(cluster.DisplayName),
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
            if (node.IsBootstrapDetached && TryGetAcceptedBootstrapCenter(node, nodes, out Vector2 acceptedCenter))
            {
                return ClampToMap(acceptedCenter + direction * 0.82f);
            }

            if (bestNode != null)
            {
                float normalized = Mathf.Clamp01((bestScore + 1f) * 0.5f);
                float distance = Mathf.Lerp(0.18f, 0.72f, 1f - normalized);
                return ClampToMap(_positions[bestNode.Id] + direction * distance);
            }

            return ClampToMap(direction * 0.66f);
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
            if (ShouldSuppressBootstrapPair(first, second))
            {
                return;
            }

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

        private static void AddRelatedLinks(
            FirstContactSemanticMapSnapshot snapshot,
            FirstContactSemanticSettings settings)
        {
            if (snapshot == null || snapshot.Nodes.Count < 2)
            {
                return;
            }

            float threshold = settings != null ? settings.semanticMapAttractionThreshold : 0.7f;
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
                    if (ShouldSuppressBootstrapPair(first, second))
                    {
                        continue;
                    }

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

        private bool TryGetAcceptedBootstrapCenter(
            FirstContactSemanticMapNode detachedNode,
            IReadOnlyList<FirstContactSemanticMapNode> nodes,
            out Vector2 center)
        {
            center = Vector2.zero;
            if (detachedNode == null ||
                !detachedNode.IsBootstrapDetached ||
                string.IsNullOrWhiteSpace(detachedNode.BootstrapCategoryId) ||
                nodes == null)
            {
                return false;
            }

            int count = 0;
            for (int i = 0; i < nodes.Count; i++)
            {
                FirstContactSemanticMapNode candidate = nodes[i];
                if (candidate == null ||
                    candidate.IsBootstrapDetached ||
                    !string.Equals(candidate.BootstrapCategoryId, detachedNode.BootstrapCategoryId, StringComparison.Ordinal) ||
                    !_positions.TryGetValue(candidate.Id, out Vector2 position))
                {
                    continue;
                }

                center += position;
                count++;
            }

            if (count <= 0)
            {
                return false;
            }

            center /= count;
            return true;
        }

        private static bool ShouldSuppressBootstrapPair(
            FirstContactSemanticMapNode first,
            FirstContactSemanticMapNode second)
        {
            if (first == null || second == null ||
                string.IsNullOrWhiteSpace(first.BootstrapCategoryId) ||
                !string.Equals(first.BootstrapCategoryId, second.BootstrapCategoryId, StringComparison.Ordinal))
            {
                return false;
            }

            return first.IsBootstrapDetached != second.IsBootstrapDetached;
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

    }

    public enum FirstContactResponseChannelKind
    {
        Category,
        Pattern
    }

    public sealed class FirstContactResponseChannelEntry
    {
        public string Id;
        public string SourceId;
        public string Label;
        public string SecondaryLabel;
        public FirstContactResponseChannelKind Kind;
        public bool IsActive;
        public bool IsStable;
        public int TraceCount;
        public int RequiredTraceCount;
        public int DisplayNumber;
    }

    /// <summary>
    /// Flattens the semantic graph into a bounded, terminal-friendly channel view.
    /// Membership remains explicit: only accepted members of the active channel
    /// can enter TraceNodes. Rejected probes are routed to a PATTERN entry instead.
    /// </summary>
    public sealed class FirstContactResponseChannelPresentation
    {
        private const string UnknownPatternId = "__UNASSIGNED_PATTERN__";

        private readonly Dictionary<string, FirstContactResponseChannelEntry> _entriesById =
            new(StringComparer.Ordinal);
        private readonly List<FirstContactResponseChannelEntry> _entryPool = new();
        private int _entryPoolIndex;

        public readonly List<FirstContactResponseChannelEntry> DirectoryEntries = new();
        public readonly List<FirstContactSemanticMapNode> TraceNodes = new();

        public FirstContactResponseChannelEntry ActiveEntry { get; private set; }
        public FirstContactResponseChannelEntry RecentRouteEntry { get; private set; }
        public FirstContactSemanticMapNode RecentProbe { get; private set; }
        public bool RecentProbeMatchesActiveEntry { get; private set; }
        public int DirectoryPage { get; private set; }
        public int DirectoryPageCount { get; private set; }
        public int VisibleDirectoryStart { get; private set; }
        public int VisibleDirectoryCount { get; private set; }

        public void Build(
            FirstContactSemanticMapSnapshot snapshot,
            int maximumTraceRows,
            int maximumDirectoryRows)
        {
            DirectoryEntries.Clear();
            TraceNodes.Clear();
            _entriesById.Clear();
            _entryPoolIndex = 0;
            ActiveEntry = null;
            RecentRouteEntry = null;
            RecentProbe = null;
            RecentProbeMatchesActiveEntry = false;
            DirectoryPage = 0;
            DirectoryPageCount = 1;
            VisibleDirectoryStart = 0;
            VisibleDirectoryCount = 0;

            if (snapshot == null)
            {
                return;
            }

            AddDeclaredChannels(snapshot);
            AddInferredPatternChannels(snapshot);
            AssignDisplayNumbers();
            ResolveRecentProbe(snapshot);
            ActiveEntry = ResolveActiveEntry();
            for (int i = 0; i < DirectoryEntries.Count; i++)
            {
                DirectoryEntries[i].IsActive = ReferenceEquals(DirectoryEntries[i], ActiveEntry);
            }

            PopulateTraceRows(snapshot, Mathf.Max(1, maximumTraceRows));
            ResolveRecentProbeRoute();
            ResolveDirectoryPage(Mathf.Max(1, maximumDirectoryRows));
        }

        private void AddDeclaredChannels(FirstContactSemanticMapSnapshot snapshot)
        {
            for (int i = 0; i < snapshot.Nodes.Count; i++)
            {
                FirstContactSemanticMapNode node = snapshot.Nodes[i];
                if (node == null)
                {
                    continue;
                }

                if (node.Kind == FirstContactSemanticMapNodeKind.BootstrapCategory)
                {
                    AddEntry(
                        BuildCategoryEntryId(node.BootstrapCategoryId, node.Id),
                        node.BootstrapCategoryId,
                        node.Label,
                        node.SecondaryLabel,
                        FirstContactResponseChannelKind.Category,
                        node.IsActive,
                        node.IsBootstrapCategoryStable,
                        node.TraceCount,
                        node.RequiredTraceCount);
                }
                else if (node.Kind == FirstContactSemanticMapNodeKind.StableCluster)
                {
                    string sourceId = TrimClusterNodePrefix(node.Id);
                    AddEntry(
                        BuildPatternEntryId(sourceId),
                        sourceId,
                        node.SecondaryLabel,
                        node.Label,
                        FirstContactResponseChannelKind.Pattern,
                        node.IsActive,
                        true,
                        0,
                        0);
                }
            }
        }

        private void AddInferredPatternChannels(FirstContactSemanticMapSnapshot snapshot)
        {
            for (int i = 0; i < snapshot.Nodes.Count; i++)
            {
                FirstContactSemanticMapNode node = snapshot.Nodes[i];
                if (node?.Kind != FirstContactSemanticMapNodeKind.Card)
                {
                    continue;
                }

                if (node.IsSemanticGroupPending)
                {
                    continue;
                }

                string categoryEntryId = BuildCategoryEntryId(
                    node.BootstrapCategoryId,
                    string.Empty);
                bool belongsToDeclaredCategory =
                    !node.IsBootstrapDetached &&
                    !string.IsNullOrWhiteSpace(node.BootstrapCategoryId) &&
                    _entriesById.ContainsKey(categoryEntryId);
                if (belongsToDeclaredCategory)
                {
                    continue;
                }

                string sourceId = string.IsNullOrWhiteSpace(node.SecondaryLabel)
                    ? UnknownPatternId
                    : node.SecondaryLabel.Trim();
                string entryId = BuildPatternEntryId(sourceId);
                if (_entriesById.TryGetValue(entryId, out FirstContactResponseChannelEntry entry))
                {
                    entry.TraceCount++;
                    continue;
                }

                AddEntry(
                    entryId,
                    sourceId,
                    string.Empty,
                    string.Empty,
                    FirstContactResponseChannelKind.Pattern,
                    false,
                    false,
                    1,
                    0);
            }
        }

        private void AddEntry(
            string id,
            string sourceId,
            string label,
            string secondaryLabel,
            FirstContactResponseChannelKind kind,
            bool isActive,
            bool isStable,
            int traceCount,
            int requiredTraceCount)
        {
            if (_entriesById.TryGetValue(id, out FirstContactResponseChannelEntry existing))
            {
                existing.IsActive |= isActive;
                existing.IsStable |= isStable;
                existing.TraceCount = Mathf.Max(existing.TraceCount, traceCount);
                existing.RequiredTraceCount = Mathf.Max(existing.RequiredTraceCount, requiredTraceCount);
                if (string.IsNullOrWhiteSpace(existing.Label) && !string.IsNullOrWhiteSpace(label))
                {
                    existing.Label = label;
                }

                if (string.IsNullOrWhiteSpace(existing.SecondaryLabel) && !string.IsNullOrWhiteSpace(secondaryLabel))
                {
                    existing.SecondaryLabel = secondaryLabel;
                }

                return;
            }

            FirstContactResponseChannelEntry entry = RentEntry();
            entry.Id = id;
            entry.SourceId = sourceId ?? string.Empty;
            entry.Label = label ?? string.Empty;
            entry.SecondaryLabel = secondaryLabel ?? string.Empty;
            entry.Kind = kind;
            entry.IsActive = isActive;
            entry.IsStable = isStable;
            entry.TraceCount = Mathf.Max(0, traceCount);
            entry.RequiredTraceCount = Mathf.Max(0, requiredTraceCount);
            entry.DisplayNumber = 0;
            DirectoryEntries.Add(entry);
            _entriesById[id] = entry;
        }

        private FirstContactResponseChannelEntry RentEntry()
        {
            if (_entryPoolIndex >= _entryPool.Count)
            {
                _entryPool.Add(new FirstContactResponseChannelEntry());
            }

            return _entryPool[_entryPoolIndex++];
        }

        private void AssignDisplayNumbers()
        {
            int categoryNumber = 0;
            int patternNumber = 0;
            for (int i = 0; i < DirectoryEntries.Count; i++)
            {
                FirstContactResponseChannelEntry entry = DirectoryEntries[i];
                entry.DisplayNumber = entry.Kind == FirstContactResponseChannelKind.Category
                    ? ++categoryNumber
                    : ++patternNumber;
            }
        }

        private void ResolveRecentProbe(FirstContactSemanticMapSnapshot snapshot)
        {
            for (int i = 0; i < snapshot.Nodes.Count; i++)
            {
                FirstContactSemanticMapNode node = snapshot.Nodes[i];
                if (node?.Kind == FirstContactSemanticMapNodeKind.Card && node.IsActive)
                {
                    RecentProbe = node;
                    return;
                }
            }
        }

        private FirstContactResponseChannelEntry ResolveActiveEntry()
        {
            for (int i = 0; i < DirectoryEntries.Count; i++)
            {
                if (DirectoryEntries[i].Kind == FirstContactResponseChannelKind.Category &&
                    IsDeclaredActive(DirectoryEntries[i]))
                {
                    return DirectoryEntries[i];
                }
            }

            if (RecentProbe != null && !RecentProbe.IsBootstrapDetached)
            {
                string categoryId = BuildCategoryEntryId(
                    RecentProbe.BootstrapCategoryId,
                    string.Empty);
                if (_entriesById.TryGetValue(categoryId, out FirstContactResponseChannelEntry category))
                {
                    return category;
                }

                string patternId = BuildPatternEntryId(RecentProbe.SecondaryLabel);
                if (_entriesById.TryGetValue(patternId, out FirstContactResponseChannelEntry pattern))
                {
                    return pattern;
                }
            }

            for (int i = 0; i < DirectoryEntries.Count; i++)
            {
                if (IsDeclaredActive(DirectoryEntries[i]))
                {
                    return DirectoryEntries[i];
                }
            }

            return DirectoryEntries.Count > 0 ? DirectoryEntries[0] : null;
        }

        private bool IsDeclaredActive(FirstContactResponseChannelEntry entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.Id))
            {
                return false;
            }

            if (RecentProbe != null &&
                entry.Kind == FirstContactResponseChannelKind.Category &&
                string.Equals(entry.SourceId, RecentProbe.BootstrapCategoryId, StringComparison.Ordinal))
            {
                return true;
            }

            return entry.IsActive;
        }

        private void PopulateTraceRows(
            FirstContactSemanticMapSnapshot snapshot,
            int maximumTraceRows)
        {
            if (ActiveEntry == null)
            {
                return;
            }

            if (RecentProbe != null && IsMemberOfActiveEntry(RecentProbe))
            {
                TraceNodes.Add(RecentProbe);
            }

            for (int i = 0; i < snapshot.Nodes.Count && TraceNodes.Count < maximumTraceRows; i++)
            {
                FirstContactSemanticMapNode node = snapshot.Nodes[i];
                if (node == null || ReferenceEquals(node, RecentProbe) || !IsMemberOfActiveEntry(node))
                {
                    continue;
                }

                TraceNodes.Add(node);
            }
        }

        private bool IsMemberOfActiveEntry(FirstContactSemanticMapNode node)
        {
            if (node?.Kind != FirstContactSemanticMapNodeKind.Card || ActiveEntry == null)
            {
                return false;
            }

            if (node.IsSemanticGroupPending)
            {
                return false;
            }

            if (ActiveEntry.Kind == FirstContactResponseChannelKind.Category)
            {
                return !node.IsBootstrapDetached &&
                       string.Equals(
                           node.BootstrapCategoryId,
                           ActiveEntry.SourceId,
                           StringComparison.Ordinal);
            }

            string patternSource = string.IsNullOrWhiteSpace(node.SecondaryLabel)
                ? UnknownPatternId
                : node.SecondaryLabel.Trim();
            return string.Equals(patternSource, ActiveEntry.SourceId, StringComparison.Ordinal);
        }

        private void ResolveRecentProbeRoute()
        {
            if (RecentProbe == null || ActiveEntry == null)
            {
                return;
            }

            if (RecentProbe.IsSemanticGroupPending)
            {
                return;
            }

            RecentProbeMatchesActiveEntry = IsMemberOfActiveEntry(RecentProbe);
            if (RecentProbeMatchesActiveEntry)
            {
                RecentRouteEntry = ActiveEntry;
                return;
            }

            string patternSource = string.IsNullOrWhiteSpace(RecentProbe.SecondaryLabel)
                ? UnknownPatternId
                : RecentProbe.SecondaryLabel.Trim();
            _entriesById.TryGetValue(BuildPatternEntryId(patternSource), out FirstContactResponseChannelEntry route);
            RecentRouteEntry = route;
        }

        private void ResolveDirectoryPage(int maximumDirectoryRows)
        {
            DirectoryPageCount = Mathf.Max(
                1,
                Mathf.CeilToInt(DirectoryEntries.Count / (float)maximumDirectoryRows));
            int activeIndex = ActiveEntry != null ? DirectoryEntries.IndexOf(ActiveEntry) : 0;
            DirectoryPage = Mathf.Clamp(activeIndex / maximumDirectoryRows, 0, DirectoryPageCount - 1);
            VisibleDirectoryStart = DirectoryPage * maximumDirectoryRows;
            VisibleDirectoryCount = Mathf.Min(
                maximumDirectoryRows,
                Mathf.Max(0, DirectoryEntries.Count - VisibleDirectoryStart));
        }

        private static string BuildCategoryEntryId(string sourceId, string fallbackNodeId)
        {
            string value = string.IsNullOrWhiteSpace(sourceId) ? fallbackNodeId : sourceId;
            return $"CATEGORY:{value?.Trim() ?? string.Empty}";
        }

        private static string BuildPatternEntryId(string sourceId)
        {
            string value = string.IsNullOrWhiteSpace(sourceId) ? UnknownPatternId : sourceId.Trim();
            return $"PATTERN:{value}";
        }

        private static string TrimClusterNodePrefix(string nodeId)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                return string.Empty;
            }

            return nodeId.StartsWith("K:", StringComparison.Ordinal)
                ? nodeId.Substring(2)
                : nodeId;
        }
    }

    public readonly struct FirstContactResponseChannelLayout
    {
        public FirstContactResponseChannelLayout(
            Rect scope,
            Rect scopePlot,
            Rect directory,
            Rect recentProbe,
            float headerHeight,
            float gap,
            bool hasRecentProbe)
        {
            Scope = scope;
            ScopePlot = scopePlot;
            Directory = directory;
            RecentProbe = recentProbe;
            HeaderHeight = headerHeight;
            Gap = gap;
            HasRecentProbe = hasRecentProbe;
        }

        public Rect Scope { get; }
        public Rect ScopePlot { get; }
        public Rect Directory { get; }
        public Rect RecentProbe { get; }
        public float HeaderHeight { get; }
        public float Gap { get; }
        public bool HasRecentProbe { get; }

        public Rect GetDirectoryHeaderRect()
        {
            return new Rect(
                Directory.xMin + Gap,
                Directory.yMax - HeaderHeight,
                Mathf.Max(1f, Directory.width - Gap * 2f),
                HeaderHeight);
        }

        public Rect GetDirectoryRowRect(int row, int rowCount)
        {
            int safeRows = Mathf.Max(1, rowCount);
            float contentTop = Directory.yMax - HeaderHeight;
            float contentHeight = Mathf.Max(1f, Directory.height - HeaderHeight - Gap);
            float rowHeight = contentHeight / safeRows;
            return new Rect(
                Directory.xMin + Gap,
                contentTop - (row + 1) * rowHeight,
                Mathf.Max(1f, Directory.width - Gap * 2f),
                rowHeight);
        }

        public Rect GetTraceRowRect(int row, int rowCount)
        {
            int safeRows = Mathf.Max(1, rowCount);
            float rowHeight = ScopePlot.height / safeRows;
            return new Rect(
                ScopePlot.xMin,
                ScopePlot.yMax - (row + 1) * rowHeight,
                ScopePlot.width,
                rowHeight);
        }

        public static FirstContactResponseChannelLayout Resolve(
            Rect rect,
            bool fullMode,
            FirstContactSemanticMapStyle configuredStyle)
        {
            FirstContactSemanticMapStyle style =
                FirstContactSemanticMapStyle.GetOrDefault(configuredStyle);
            float padding = Mathf.Min(
                Mathf.Max(0f, style.analyzerPanelPadding),
                Mathf.Min(rect.width, rect.height) * 0.12f);
            float gap = Mathf.Max(2f, style.analyzerPanelGap);
            Rect content = new(
                rect.xMin + padding,
                rect.yMin + padding,
                Mathf.Max(1f, rect.width - padding * 2f),
                Mathf.Max(1f, rect.height - padding * 2f));
            bool showRecent = fullMode && content.height >= 120f;
            float recentHeight = showRecent
                ? Mathf.Max(42f, content.height * style.analyzerRecentProbeHeightRatio)
                : 0f;
            Rect recent = showRecent
                ? new Rect(content.xMin, content.yMin, content.width, recentHeight)
                : new Rect();
            float mainYMin = showRecent ? recent.yMax + gap : content.yMin;
            float mainHeight = Mathf.Max(1f, content.yMax - mainYMin);
            float directoryWidth = Mathf.Clamp(
                content.width * style.analyzerDirectoryWidthRatio,
                96f,
                Mathf.Max(96f, content.width - 120f));
            Rect directory = new(
                content.xMax - directoryWidth,
                mainYMin,
                directoryWidth,
                mainHeight);
            Rect scope = new(
                content.xMin,
                mainYMin,
                Mathf.Max(1f, directory.xMin - gap - content.xMin),
                mainHeight);
            float headerHeight = Mathf.Min(
                Mathf.Max(12f, style.analyzerHeaderHeight),
                Mathf.Max(12f, scope.height * 0.38f));
            Rect plot = new(
                scope.xMin + gap,
                scope.yMin + gap,
                Mathf.Max(1f, scope.width - gap * 2f),
                Mathf.Max(1f, scope.height - headerHeight - gap * 2f));
            return new FirstContactResponseChannelLayout(
                scope,
                plot,
                directory,
                recent,
                headerHeight,
                gap,
                showRecent);
        }
    }

    public sealed class FirstContactSemanticMapScreenLayout
    {
        private readonly List<Vector2> _workingPositions = new();
        private readonly List<Vector2> _sourcePositions = new();
        private readonly List<Footprint> _footprints = new();
        private readonly List<Vector2> _cachedMapPositions = new();
        private int _cachedSignature;
        private bool _hasCachedLayout;

        public void Invalidate()
        {
            _hasCachedLayout = false;
            _cachedSignature = 0;
            _cachedMapPositions.Clear();
        }

        public FirstContactSemanticMapSnapshot Resolve(
            FirstContactSemanticMapSnapshot snapshot,
            Rect rect,
            bool fullMode,
            FirstContactSemanticMapStyle configuredStyle,
            IReadOnlyList<Vector2> labelSizes)
        {
            FirstContactSemanticMapStyle style =
                FirstContactSemanticMapStyle.GetOrDefault(configuredStyle);
            if (snapshot == null ||
                snapshot.Nodes.Count == 0 ||
                !style.enableFootprintPacking ||
                rect.width <= 1f ||
                rect.height <= 1f)
            {
                return CloneSnapshot(snapshot, null);
            }

            FirstContactSemanticMapModeStyle mode = style.GetMode(fullMode);
            int signature = BuildSignature(snapshot, rect, fullMode, style, mode, labelSizes);
            if (_hasCachedLayout &&
                signature == _cachedSignature &&
                _cachedMapPositions.Count == snapshot.Nodes.Count)
            {
                return CloneSnapshot(snapshot, _cachedMapPositions);
            }

            PrepareWorkingState(snapshot, rect, style, mode, labelSizes);
            ResolveOverlaps(snapshot, rect, style);

            _cachedMapPositions.Clear();
            EnsureCapacity(_cachedMapPositions, snapshot.Nodes.Count);
            for (int i = 0; i < snapshot.Nodes.Count; i++)
            {
                _cachedMapPositions.Add(LocalToMap(_workingPositions[i], rect, style));
            }

            _cachedSignature = signature;
            _hasCachedLayout = true;
            return CloneSnapshot(snapshot, _cachedMapPositions);
        }

        private void PrepareWorkingState(
            FirstContactSemanticMapSnapshot snapshot,
            Rect rect,
            FirstContactSemanticMapStyle style,
            FirstContactSemanticMapModeStyle mode,
            IReadOnlyList<Vector2> labelSizes)
        {
            int count = snapshot.Nodes.Count;
            _workingPositions.Clear();
            _sourcePositions.Clear();
            _footprints.Clear();
            EnsureCapacity(_workingPositions, count);
            EnsureCapacity(_sourcePositions, count);
            EnsureCapacity(_footprints, count);

            float baseSize = Mathf.Min(rect.width, rect.height);
            float labelGap = style.labelOffset * mode.labelOffsetMultiplier;
            for (int i = 0; i < count; i++)
            {
                FirstContactSemanticMapNode node = snapshot.Nodes[i];
                Vector2 position = FirstContactSemanticMapGraphic.MapToLocal(
                    node?.Position ?? Vector2.zero,
                    rect,
                    style);
                Vector2 labelSize = labelSizes != null && i < labelSizes.Count
                    ? labelSizes[i]
                    : Vector2.zero;
                float radius = ResolveVisualRadius(node, baseSize, style, mode);

                _workingPositions.Add(position);
                _sourcePositions.Add(position);
                _footprints.Add(BuildFootprint(radius, labelSize, labelGap));
            }
        }

        private void ResolveOverlaps(
            FirstContactSemanticMapSnapshot snapshot,
            Rect rect,
            FirstContactSemanticMapStyle style)
        {
            int count = snapshot.Nodes.Count;
            int iterations = Mathf.Max(1, style.footprintPackingIterations);
            float epsilon = Mathf.Max(0.01f, style.footprintConvergenceEpsilon);
            for (int iteration = 0; iteration < iterations; iteration++)
            {
                ApplyAnchorForce(style.footprintAnchorStrength);

                int overlapCount = 0;
                float largestCorrection = 0f;
                for (int i = 0; i < count; i++)
                {
                    for (int j = i + 1; j < count; j++)
                    {
                        if (!TryResolvePair(
                                snapshot.Nodes[i],
                                snapshot.Nodes[j],
                                i,
                                j,
                                style.footprintSpacing,
                                out float correction))
                        {
                            continue;
                        }

                        overlapCount++;
                        largestCorrection = Mathf.Max(largestCorrection, correction);
                    }
                }

                ClampAllToBounds(rect, style);
                if (overlapCount == 0 || largestCorrection <= epsilon)
                {
                    break;
                }
            }
        }

        private void ApplyAnchorForce(float strength)
        {
            float safeStrength = Mathf.Clamp01(strength);
            if (safeStrength <= 0f)
            {
                return;
            }

            for (int i = 0; i < _workingPositions.Count; i++)
            {
                _workingPositions[i] = Vector2.Lerp(
                    _workingPositions[i],
                    _sourcePositions[i],
                    safeStrength);
            }
        }

        private bool TryResolvePair(
            FirstContactSemanticMapNode firstNode,
            FirstContactSemanticMapNode secondNode,
            int firstIndex,
            int secondIndex,
            float spacing,
            out float correctionMagnitude)
        {
            Footprint first = _footprints[firstIndex];
            Footprint second = _footprints[secondIndex];
            Vector2 firstCenter = _workingPositions[firstIndex] + first.CenterOffset;
            Vector2 secondCenter = _workingPositions[secondIndex] + second.CenterOffset;
            Vector2 delta = secondCenter - firstCenter;
            float overlapX = first.HalfSize.x + second.HalfSize.x + spacing - Mathf.Abs(delta.x);
            float overlapY = first.HalfSize.y + second.HalfSize.y + spacing - Mathf.Abs(delta.y);
            if (overlapX <= 0f || overlapY <= 0f)
            {
                correctionMagnitude = 0f;
                return false;
            }

            bool separateHorizontally = overlapX < overlapY;
            float signedDirection;
            if (separateHorizontally)
            {
                signedDirection = Mathf.Abs(delta.x) > 0.001f
                    ? Mathf.Sign(delta.x)
                    : ResolveDeterministicDirection(firstNode, secondNode, firstIndex, secondIndex);
                correctionMagnitude = overlapX;
            }
            else
            {
                signedDirection = Mathf.Abs(delta.y) > 0.001f
                    ? Mathf.Sign(delta.y)
                    : ResolveDeterministicDirection(firstNode, secondNode, firstIndex, secondIndex);
                correctionMagnitude = overlapY;
            }

            Vector2 axis = separateHorizontally ? Vector2.right : Vector2.up;
            Vector2 correction = axis * signedDirection * correctionMagnitude;
            float firstMobility = ResolveMobility(firstNode);
            float secondMobility = ResolveMobility(secondNode);
            float totalMobility = Mathf.Max(0.0001f, firstMobility + secondMobility);
            _workingPositions[firstIndex] -= correction * (firstMobility / totalMobility);
            _workingPositions[secondIndex] += correction * (secondMobility / totalMobility);
            return true;
        }

        private void ClampAllToBounds(Rect rect, FirstContactSemanticMapStyle style)
        {
            float boundaryPadding = Mathf.Max(0f, style.footprintBoundaryPadding);
            float mapPaddingX = Mathf.Max(
                style.minimumMapPadding,
                rect.width * style.mapHorizontalPaddingRatio);
            float mapPaddingY = Mathf.Max(
                style.minimumMapPadding,
                rect.height * style.mapVerticalPaddingRatio);
            for (int i = 0; i < _workingPositions.Count; i++)
            {
                Footprint footprint = _footprints[i];
                float minX = Mathf.Max(
                    rect.xMin + mapPaddingX,
                    rect.xMin + boundaryPadding - footprint.MinOffset.x);
                float maxX = Mathf.Min(
                    rect.xMax - mapPaddingX,
                    rect.xMax - boundaryPadding - footprint.MaxOffset.x);
                float minY = Mathf.Max(
                    rect.yMin + mapPaddingY,
                    rect.yMin + boundaryPadding - footprint.MinOffset.y);
                float maxY = Mathf.Min(
                    rect.yMax - mapPaddingY,
                    rect.yMax - boundaryPadding - footprint.MaxOffset.y);
                Vector2 position = _workingPositions[i];
                position.x = ClampToRange(position.x, minX, maxX);
                position.y = ClampToRange(position.y, minY, maxY);
                _workingPositions[i] = position;
            }
        }

        private static Footprint BuildFootprint(
            float visualRadius,
            Vector2 labelSize,
            float labelGap)
        {
            float radius = Mathf.Max(0f, visualRadius);
            float halfWidth = Mathf.Max(radius, Mathf.Max(0f, labelSize.x) * 0.5f);
            float minY = labelSize.y > 0f
                ? -radius - Mathf.Max(0f, labelGap) - labelSize.y
                : -radius;
            Vector2 minOffset = new(-halfWidth, minY);
            Vector2 maxOffset = new(halfWidth, radius);
            return new Footprint(
                (minOffset + maxOffset) * 0.5f,
                (maxOffset - minOffset) * 0.5f,
                minOffset,
                maxOffset);
        }

        internal static float ResolveVisualRadius(
            FirstContactSemanticMapNode node,
            float baseSize,
            FirstContactSemanticMapStyle style,
            FirstContactSemanticMapModeStyle mode)
        {
            if (node == null)
            {
                return 0f;
            }

            if (node.Kind == FirstContactSemanticMapNodeKind.StableCluster ||
                node.Kind == FirstContactSemanticMapNodeKind.BootstrapCategory)
            {
                float clusterRadius = baseSize * mode.clusterRadiusRatio;
                float pulseScale = Mathf.Max(
                    1f,
                    style.clusterLockRingBaseScale + style.clusterLockRingPulseScale);
                return clusterRadius * pulseScale;
            }

            float radius = baseSize * mode.cardNodeRadiusRatio;
            if (node.IsActive)
            {
                radius *= style.activeNodeBaseScale + style.activeNodePulseScale;
            }

            float outerScale = Mathf.Max(
                1f,
                style.nodeOuterPulseRingBaseScale + style.nodeOuterPulseRingPulseScale);
            return radius * outerScale;
        }

        private static float ResolveMobility(FirstContactSemanticMapNode node)
        {
            if (node == null)
            {
                return 1f;
            }

            return node.Kind switch
            {
                FirstContactSemanticMapNodeKind.BootstrapCategory => node.IsActive ? 0.18f : 0.28f,
                FirstContactSemanticMapNodeKind.StableCluster => 0.42f,
                FirstContactSemanticMapNodeKind.Card => node.IsActive ? 0.68f : 1f,
                _ => 1f
            };
        }

        private static float ResolveDeterministicDirection(
            FirstContactSemanticMapNode first,
            FirstContactSemanticMapNode second,
            int firstIndex,
            int secondIndex)
        {
            unchecked
            {
                uint hash = 2166136261u;
                hash = AppendStableHash(hash, first?.Id, firstIndex);
                hash = AppendStableHash(hash, second?.Id, secondIndex);
                return (hash & 1u) == 0u ? 1f : -1f;
            }
        }

        private static uint AppendStableHash(uint hash, string value, int fallback)
        {
            unchecked
            {
                if (string.IsNullOrEmpty(value))
                {
                    hash ^= (uint)fallback;
                    return hash * 16777619u;
                }

                for (int i = 0; i < value.Length; i++)
                {
                    hash ^= value[i];
                    hash *= 16777619u;
                }

                return hash;
            }
        }

        private static float ClampToRange(float value, float minimum, float maximum)
        {
            if (minimum > maximum)
            {
                return (minimum + maximum) * 0.5f;
            }

            return Mathf.Clamp(value, minimum, maximum);
        }

        private static Vector2 LocalToMap(
            Vector2 localPosition,
            Rect rect,
            FirstContactSemanticMapStyle style)
        {
            float paddingX = Mathf.Max(
                style.minimumMapPadding,
                rect.width * style.mapHorizontalPaddingRatio);
            float paddingY = Mathf.Max(
                style.minimumMapPadding,
                rect.height * style.mapVerticalPaddingRatio);
            float usableWidth = Mathf.Max(1f, rect.width - paddingX * 2f);
            float usableHeight = Mathf.Max(1f, rect.height - paddingY * 2f);
            float normalizedX = Mathf.Clamp01((localPosition.x - rect.xMin - paddingX) / usableWidth);
            float normalizedY = Mathf.Clamp01((localPosition.y - rect.yMin - paddingY) / usableHeight);
            return new Vector2(normalizedX * 2f - 1f, normalizedY * 2f - 1f);
        }

        private static int BuildSignature(
            FirstContactSemanticMapSnapshot snapshot,
            Rect rect,
            bool fullMode,
            FirstContactSemanticMapStyle style,
            FirstContactSemanticMapModeStyle mode,
            IReadOnlyList<Vector2> labelSizes)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + rect.width.GetHashCode();
                hash = hash * 31 + rect.height.GetHashCode();
                hash = hash * 31 + fullMode.GetHashCode();
                hash = hash * 31 + style.GetInstanceID();
                hash = hash * 31 + style.footprintPackingIterations;
                hash = hash * 31 + style.footprintSpacing.GetHashCode();
                hash = hash * 31 + style.footprintBoundaryPadding.GetHashCode();
                hash = hash * 31 + style.footprintAnchorStrength.GetHashCode();
                hash = hash * 31 + style.footprintConvergenceEpsilon.GetHashCode();
                hash = hash * 31 + style.minimumMapPadding.GetHashCode();
                hash = hash * 31 + style.mapHorizontalPaddingRatio.GetHashCode();
                hash = hash * 31 + style.mapVerticalPaddingRatio.GetHashCode();
                hash = hash * 31 + style.labelOffset.GetHashCode();
                hash = hash * 31 + style.activeNodeBaseScale.GetHashCode();
                hash = hash * 31 + style.activeNodePulseScale.GetHashCode();
                hash = hash * 31 + style.clusterLockRingBaseScale.GetHashCode();
                hash = hash * 31 + style.clusterLockRingPulseScale.GetHashCode();
                hash = hash * 31 + style.nodeOuterPulseRingBaseScale.GetHashCode();
                hash = hash * 31 + style.nodeOuterPulseRingPulseScale.GetHashCode();
                hash = hash * 31 + mode.clusterRadiusRatio.GetHashCode();
                hash = hash * 31 + mode.cardNodeRadiusRatio.GetHashCode();
                hash = hash * 31 + mode.labelOffsetMultiplier.GetHashCode();
                for (int i = 0; i < snapshot.Nodes.Count; i++)
                {
                    FirstContactSemanticMapNode node = snapshot.Nodes[i];
                    Vector2 labelSize = labelSizes != null && i < labelSizes.Count
                        ? labelSizes[i]
                        : Vector2.zero;
                    hash = hash * 31 + (node?.Id?.GetHashCode() ?? 0);
                    hash = hash * 31 + (node?.Position.GetHashCode() ?? 0);
                    hash = hash * 31 + (node?.Kind.GetHashCode() ?? 0);
                    hash = hash * 31 + (node?.IsActive.GetHashCode() ?? 0);
                    hash = hash * 31 + labelSize.GetHashCode();
                }

                return hash;
            }
        }

        private static FirstContactSemanticMapSnapshot CloneSnapshot(
            FirstContactSemanticMapSnapshot snapshot,
            IReadOnlyList<Vector2> positions)
        {
            var clone = new FirstContactSemanticMapSnapshot();
            if (snapshot == null)
            {
                return clone;
            }

            for (int i = 0; i < snapshot.Nodes.Count; i++)
            {
                FirstContactSemanticMapNode node = snapshot.Nodes[i];
                if (node == null)
                {
                    continue;
                }

                clone.Nodes.Add(new FirstContactSemanticMapNode
                {
                    Id = node.Id,
                    Label = node.Label,
                    SecondaryLabel = node.SecondaryLabel,
                    Kind = node.Kind,
                    Position = positions != null && i < positions.Count ? positions[i] : node.Position,
                    Embedding = node.Embedding,
                    IsActive = node.IsActive,
                    Marker = node.Marker,
                    BootstrapCategoryId = node.BootstrapCategoryId,
                    IsBootstrapDetached = node.IsBootstrapDetached,
                    TraceCount = node.TraceCount,
                    RequiredTraceCount = node.RequiredTraceCount,
                    IsBootstrapCategoryStable = node.IsBootstrapCategoryStable,
                    Pulse = node.Pulse
                });
            }

            for (int i = 0; i < snapshot.Links.Count; i++)
            {
                clone.Links.Add(snapshot.Links[i]);
            }

            return clone;
        }

        private static void EnsureCapacity<T>(List<T> list, int capacity)
        {
            if (list.Capacity < capacity)
            {
                list.Capacity = capacity;
            }
        }

        private readonly struct Footprint
        {
            public Footprint(
                Vector2 centerOffset,
                Vector2 halfSize,
                Vector2 minOffset,
                Vector2 maxOffset)
            {
                CenterOffset = centerOffset;
                HalfSize = halfSize;
                MinOffset = minOffset;
                MaxOffset = maxOffset;
            }

            public Vector2 CenterOffset { get; }
            public Vector2 HalfSize { get; }
            public Vector2 MinOffset { get; }
            public Vector2 MaxOffset { get; }
        }
    }

}
