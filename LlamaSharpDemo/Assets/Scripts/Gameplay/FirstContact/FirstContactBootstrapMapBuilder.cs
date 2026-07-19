using System;
using System.Collections.Generic;
using UnityEngine;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    public sealed class FirstContactBootstrapMapBuilder
    {
        private readonly FirstContactEmbeddingService _embeddingService;
        private readonly FirstContactSemanticMapLayout _layout = new();

        public FirstContactBootstrapMapBuilder(FirstContactEmbeddingService embeddingService)
        {
            _embeddingService = embeddingService;
        }

        public void Reset(int sessionSeed)
        {
            _layout.Reset(sessionSeed);
        }

        public FirstContactSemanticMapSnapshot Build(
            IReadOnlyList<SemanticCardRecord> cards,
            IReadOnlyList<SemanticClusterRecord> clusters,
            SemanticCardRecord activeCard,
            IReadOnlyList<FirstContactBootstrapCategoryState> categories,
            FirstContactBootstrapCategoryState activeCategory,
            bool includeActiveCard,
            FirstContactSemanticSettings settings)
        {
            FirstContactSemanticMapSnapshot snapshot = _layout.BuildSnapshot(
                cards,
                clusters,
                activeCard,
                settings);
            if (activeCategory == null)
            {
                return snapshot;
            }

            string activeCardNodeId = FirstContactSemanticMapLayout.BuildCardNodeId(activeCard);
            HashSet<string> relevantClusterNodeIds = BuildRelevantClusterNodeIds(clusters);
            PruneSnapshot(
                snapshot,
                activeCardNodeId,
                includeActiveCard,
                relevantClusterNodeIds);
            snapshot.Links.Clear();

            int categoryCount = categories?.Count ?? 0;
            for (int categoryIndex = 0; categoryIndex < categoryCount; categoryIndex++)
            {
                FirstContactBootstrapCategoryState category = categories[categoryIndex];
                if (!ShouldShowCategory(category, activeCategory))
                {
                    continue;
                }

                AddCategoryNode(
                    snapshot,
                    category,
                    ReferenceEquals(category, activeCategory),
                    ResolveCategoryPosition(categoryIndex, categoryCount));
                ShapeCategoryNodes(snapshot, category, categoryIndex);
            }

            ShapeDetachedNodes(snapshot, activeCardNodeId);
            return snapshot;
        }

        public static bool ShouldShowFormation(
            FirstContactBootstrapCategoryState category,
            SemanticClusterRecord cluster,
            FirstContactClusterFormationEvent formation)
        {
            return formation.ShouldAnimate &&
                   category != null &&
                   cluster != null &&
                   HasDetachedCategoryMember(cluster, category);
        }

        private static HashSet<string> BuildRelevantClusterNodeIds(
            IReadOnlyList<SemanticClusterRecord> clusters)
        {
            var clusterNodeIds = new HashSet<string>(StringComparer.Ordinal);
            if (clusters == null)
            {
                return clusterNodeIds;
            }

            for (int i = 0; i < clusters.Count; i++)
            {
                SemanticClusterRecord cluster = clusters[i];
                if (cluster != null &&
                    cluster.IsStable &&
                    HasAnyDetachedMember(cluster))
                {
                    clusterNodeIds.Add(FirstContactSemanticMapLayout.BuildClusterNodeId(cluster));
                }
            }

            return clusterNodeIds;
        }

        private static bool HasAnyDetachedMember(SemanticClusterRecord cluster)
        {
            if (cluster == null)
            {
                return false;
            }

            for (int i = 0; i < cluster.Members.Count; i++)
            {
                SemanticCardRecord member = cluster.Members[i];
                if (member != null &&
                    member.BootstrapCategoryEvaluated &&
                    !member.BootstrapCategoryAccepted)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasDetachedCategoryMember(
            SemanticClusterRecord cluster,
            FirstContactBootstrapCategoryState category)
        {
            if (cluster == null || category == null)
            {
                return false;
            }

            for (int i = 0; i < cluster.Members.Count; i++)
            {
                SemanticCardRecord member = cluster.Members[i];
                if (member != null &&
                    member.BootstrapCategoryEvaluated &&
                    !member.BootstrapCategoryAccepted &&
                    string.Equals(
                        member.BootstrapCategoryId,
                        category.Id,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static void PruneSnapshot(
            FirstContactSemanticMapSnapshot snapshot,
            string activeCardNodeId,
            bool includeActiveCard,
            ISet<string> relevantClusterNodeIds)
        {
            if (snapshot == null)
            {
                return;
            }

            for (int i = snapshot.Nodes.Count - 1; i >= 0; i--)
            {
                FirstContactSemanticMapNode node = snapshot.Nodes[i];
                bool keep = node != null;
                if (node?.Kind == FirstContactSemanticMapNodeKind.Card &&
                    !includeActiveCard &&
                    string.Equals(node.Id, activeCardNodeId, StringComparison.Ordinal))
                {
                    keep = false;
                }

                if (node?.Kind == FirstContactSemanticMapNodeKind.StableCluster &&
                    (relevantClusterNodeIds == null || !relevantClusterNodeIds.Contains(node.Id)))
                {
                    keep = false;
                }

                if (!keep)
                {
                    snapshot.Nodes.RemoveAt(i);
                }
            }
        }

        private void AddCategoryNode(
            FirstContactSemanticMapSnapshot snapshot,
            FirstContactBootstrapCategoryState category,
            bool isActive,
            Vector2 position)
        {
            if (snapshot == null || category == null)
            {
                return;
            }

            string categoryNodeId = BuildCategoryNodeId(category);
            if (snapshot.FindNode(categoryNodeId) != null)
            {
                return;
            }

            category.TryBuildCentroid(_embeddingService, out float[] centroid);
            snapshot.Nodes.Add(new FirstContactSemanticMapNode
            {
                Id = categoryNodeId,
                Label = FirstContactTerminalLocalization
                    .LocalizeBootstrapCategory(category.Id, category.DisplayName)
                    .ToUpperInvariant(),
                SecondaryLabel = FirstContactTerminalLocalization
                    .LocalizeMeaning(category.Id, category.Meaning)
                    .ToUpperInvariant(),
                Kind = FirstContactSemanticMapNodeKind.BootstrapCategory,
                Position = position,
                Embedding = centroid,
                IsActive = isActive,
                Marker = '*',
                BootstrapCategoryId = category.Id,
                TraceCount = category.TraceCount,
                RequiredTraceCount = category.RequiredTraceCount,
                IsBootstrapCategoryStable = category.IsStable
            });
        }

        private static void ShapeCategoryNodes(
            FirstContactSemanticMapSnapshot snapshot,
            FirstContactBootstrapCategoryState category,
            int categoryIndex)
        {
            if (snapshot == null || category == null)
            {
                return;
            }

            FirstContactSemanticMapNode categoryNode = snapshot.FindNode(BuildCategoryNodeId(category));
            if (categoryNode == null)
            {
                return;
            }

            var acceptedNodes = new List<FirstContactSemanticMapNode>();
            for (int i = 0; i < snapshot.Nodes.Count; i++)
            {
                FirstContactSemanticMapNode node = snapshot.Nodes[i];
                if (node == null)
                {
                    continue;
                }

                if (node.Kind != FirstContactSemanticMapNodeKind.Card ||
                    node.IsBootstrapDetached ||
                    !string.Equals(
                        node.BootstrapCategoryId,
                        category.Id,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                acceptedNodes.Add(node);
            }

            for (int i = 0; i < acceptedNodes.Count; i++)
            {
                FirstContactSemanticMapNode node = acceptedNodes[i];
                float angle = ResolveAcceptedAngle(i, categoryIndex);
                Vector2 orbit = new(Mathf.Cos(angle), Mathf.Sin(angle));
                float distance = Mathf.Lerp(
                    0.18f,
                    0.24f,
                    Mathf.Clamp01((acceptedNodes.Count - 1) / 4f));
                node.Position = ClampPosition(categoryNode.Position + orbit * distance);
                AddLinkIfMissing(snapshot, node.Id, categoryNode.Id, 0.72f);
            }
        }

        private static void ShapeDetachedNodes(
            FirstContactSemanticMapSnapshot snapshot,
            string activeCardNodeId)
        {
            if (snapshot == null)
            {
                return;
            }

            var clusterNodes = new List<FirstContactSemanticMapNode>();
            for (int i = 0; i < snapshot.Nodes.Count; i++)
            {
                FirstContactSemanticMapNode node = snapshot.Nodes[i];
                if (node?.Kind == FirstContactSemanticMapNodeKind.StableCluster)
                {
                    node.Position = ResolveDetachedClusterPosition(clusterNodes.Count);
                    clusterNodes.Add(node);
                }
            }

            var clusterMemberIndices = new Dictionary<string, int>(StringComparer.Ordinal);
            int looseDetachedIndex = 0;
            for (int i = 0; i < snapshot.Nodes.Count; i++)
            {
                FirstContactSemanticMapNode node = snapshot.Nodes[i];
                if (node?.Kind != FirstContactSemanticMapNodeKind.Card ||
                    !node.IsBootstrapDetached)
                {
                    continue;
                }

                bool active = string.Equals(node.Id, activeCardNodeId, StringComparison.Ordinal);
                string clusterNodeId = FirstContactSemanticMapLayout.BuildClusterNodeId(node.SecondaryLabel);
                FirstContactSemanticMapNode clusterNode = snapshot.FindNode(clusterNodeId);
                if (clusterNode != null)
                {
                    clusterMemberIndices.TryGetValue(clusterNodeId, out int memberIndex);
                    node.Position = ResolveDetachedClusterMemberPosition(
                        clusterNode.Position,
                        memberIndex,
                        active);
                    AddLinkIfMissing(snapshot, node.Id, clusterNode.Id, active ? 0.92f : 0.74f);
                    clusterMemberIndices[clusterNodeId] = memberIndex + 1;
                    continue;
                }

                node.Position = ResolveDetachedPosition(looseDetachedIndex, active);
                looseDetachedIndex++;
            }
        }

        private static bool ShouldShowCategory(
            FirstContactBootstrapCategoryState category,
            FirstContactBootstrapCategoryState activeCategory)
        {
            return category != null &&
                   (ReferenceEquals(category, activeCategory) ||
                    category.TraceCount > 0 ||
                    category.DetachedCards.Count > 0 ||
                    category.IsStable);
        }

        private static Vector2 ResolveCategoryPosition(int index, int categoryCount)
        {
            int safeCount = Mathf.Max(1, categoryCount);
            int safeIndex = Mathf.Clamp(index, 0, safeCount - 1);
            if (safeCount == 1)
            {
                return new Vector2(0.36f, 0.06f);
            }

            float radius = safeCount <= 2 ? 0.46f : 0.6f;
            float angle = (135f - safeIndex * (360f / safeCount)) * Mathf.Deg2Rad;
            return ClampPosition(new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
        }

        private static float ResolveAcceptedAngle(int index, int categoryIndex)
        {
            float degree = index switch
            {
                0 => 140f,
                1 => -145f,
                2 => 74f,
                3 => -58f,
                4 => 188f,
                5 => 14f,
                _ => 140f + index * 137.5f
            };
            degree -= Mathf.Max(0, categoryIndex) * 17f;
            return degree * Mathf.Deg2Rad;
        }

        private static Vector2 ResolveDetachedClusterPosition(int index)
        {
            int safeIndex = Mathf.Max(0, index);
            if (safeIndex == 0)
            {
                return Vector2.zero;
            }

            float angle = (90f + (safeIndex - 1) * 137.5f) * Mathf.Deg2Rad;
            float radius = 0.2f + ((safeIndex - 1) / 5) * 0.18f;
            return ClampPosition(new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
        }

        private static Vector2 ResolveDetachedClusterMemberPosition(
            Vector2 clusterPosition,
            int index,
            bool active)
        {
            float degree = index switch
            {
                0 => 180f,
                1 => 122f,
                2 => -132f,
                3 => -72f,
                4 => 68f,
                5 => 8f,
                _ => 180f + index * 137.5f
            };
            float radius = active ? 0.22f : 0.17f;
            Vector2 orbit = new(
                Mathf.Cos(degree * Mathf.Deg2Rad),
                Mathf.Sin(degree * Mathf.Deg2Rad));
            return ClampPosition(clusterPosition + orbit * radius);
        }

        private static Vector2 ResolveDetachedPosition(int index, bool active)
        {
            int safeIndex = Mathf.Max(0, index);
            float angle = (215f + safeIndex * 137.5f) * Mathf.Deg2Rad;
            float radius = active ? 0.25f : 0.32f + (safeIndex / 7) * 0.12f;
            return ClampPosition(new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
        }

        private static void AddLinkIfMissing(
            FirstContactSemanticMapSnapshot snapshot,
            string fromId,
            string toId,
            float strength)
        {
            if (snapshot == null ||
                string.IsNullOrWhiteSpace(fromId) ||
                string.IsNullOrWhiteSpace(toId))
            {
                return;
            }

            for (int i = 0; i < snapshot.Links.Count; i++)
            {
                FirstContactSemanticMapLink existing = snapshot.Links[i];
                bool sameDirection =
                    string.Equals(existing.FromId, fromId, StringComparison.Ordinal) &&
                    string.Equals(existing.ToId, toId, StringComparison.Ordinal);
                bool reverseDirection =
                    string.Equals(existing.FromId, toId, StringComparison.Ordinal) &&
                    string.Equals(existing.ToId, fromId, StringComparison.Ordinal);
                if (!sameDirection && !reverseDirection)
                {
                    continue;
                }

                if (strength > existing.Strength)
                {
                    snapshot.Links[i] = new FirstContactSemanticMapLink(fromId, toId, strength);
                }

                return;
            }

            snapshot.Links.Add(new FirstContactSemanticMapLink(fromId, toId, strength));
        }

        private static string BuildCategoryNodeId(FirstContactBootstrapCategoryState category)
        {
            return category == null || string.IsNullOrWhiteSpace(category.Id)
                ? string.Empty
                : $"B:{category.Id}";
        }

        private static Vector2 ClampPosition(Vector2 value)
        {
            return new Vector2(
                Mathf.Clamp(value.x, -0.92f, 0.92f),
                Mathf.Clamp(value.y, -0.9f, 0.9f));
        }
    }
}
