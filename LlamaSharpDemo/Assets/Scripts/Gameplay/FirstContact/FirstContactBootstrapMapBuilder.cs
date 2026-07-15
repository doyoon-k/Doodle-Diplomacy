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
            FirstContactBootstrapCategoryState category,
            bool includeActiveCard,
            FirstContactSemanticSettings settings)
        {
            FirstContactSemanticMapSnapshot snapshot = _layout.BuildSnapshot(
                cards,
                clusters,
                activeCard,
                settings);
            if (category == null)
            {
                return snapshot;
            }

            string activeCardNodeId = FirstContactSemanticMapLayout.BuildCardNodeId(activeCard);
            HashSet<string> relevantClusterNodeIds = BuildRelevantClusterNodeIds(clusters, category);
            PruneSnapshot(
                snapshot,
                category,
                activeCardNodeId,
                includeActiveCard,
                relevantClusterNodeIds);
            snapshot.Links.Clear();
            AddCategoryNode(snapshot, category);
            ShapeCategoryNodes(snapshot, category, activeCardNodeId);
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
            IReadOnlyList<SemanticClusterRecord> clusters,
            FirstContactBootstrapCategoryState category)
        {
            var clusterNodeIds = new HashSet<string>(StringComparer.Ordinal);
            if (category == null || clusters == null)
            {
                return clusterNodeIds;
            }

            for (int i = 0; i < clusters.Count; i++)
            {
                SemanticClusterRecord cluster = clusters[i];
                if (cluster != null &&
                    cluster.IsStable &&
                    HasDetachedCategoryMember(cluster, category))
                {
                    clusterNodeIds.Add(FirstContactSemanticMapLayout.BuildClusterNodeId(cluster));
                }
            }

            return clusterNodeIds;
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
            FirstContactBootstrapCategoryState category,
            string activeCardNodeId,
            bool includeActiveCard,
            ISet<string> relevantClusterNodeIds)
        {
            if (snapshot == null || category == null)
            {
                return;
            }

            for (int i = snapshot.Nodes.Count - 1; i >= 0; i--)
            {
                FirstContactSemanticMapNode node = snapshot.Nodes[i];
                bool keepCurrentCategoryCard =
                    node != null &&
                    node.Kind == FirstContactSemanticMapNodeKind.Card &&
                    string.Equals(
                        node.BootstrapCategoryId,
                        category.Id,
                        StringComparison.Ordinal);
                bool keepRelevantCluster =
                    node != null &&
                    node.Kind == FirstContactSemanticMapNodeKind.StableCluster &&
                    relevantClusterNodeIds != null &&
                    relevantClusterNodeIds.Contains(node.Id);
                bool keep = keepCurrentCategoryCard || keepRelevantCluster;
                if (keepCurrentCategoryCard &&
                    !includeActiveCard &&
                    string.Equals(node.Id, activeCardNodeId, StringComparison.Ordinal))
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
            FirstContactBootstrapCategoryState category)
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
                Position = new Vector2(0.36f, 0.06f),
                Embedding = centroid,
                IsActive = true,
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
            string activeCardNodeId)
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
            var detachedNodes = new List<FirstContactSemanticMapNode>();
            var clusterNodes = new List<FirstContactSemanticMapNode>();
            for (int i = 0; i < snapshot.Nodes.Count; i++)
            {
                FirstContactSemanticMapNode node = snapshot.Nodes[i];
                if (node == null)
                {
                    continue;
                }

                if (node.Kind == FirstContactSemanticMapNodeKind.StableCluster)
                {
                    clusterNodes.Add(node);
                    continue;
                }

                if (node.Kind != FirstContactSemanticMapNodeKind.Card ||
                    !string.Equals(
                        node.BootstrapCategoryId,
                        category.Id,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                if (node.IsBootstrapDetached)
                {
                    detachedNodes.Add(node);
                }
                else
                {
                    acceptedNodes.Add(node);
                }
            }

            for (int i = 0; i < clusterNodes.Count; i++)
            {
                clusterNodes[i].Position = ResolveDetachedClusterPosition(i);
            }

            for (int i = 0; i < acceptedNodes.Count; i++)
            {
                FirstContactSemanticMapNode node = acceptedNodes[i];
                float angle = ResolveAcceptedAngle(i);
                Vector2 orbit = new(Mathf.Cos(angle), Mathf.Sin(angle));
                float distance = Mathf.Lerp(
                    0.42f,
                    0.5f,
                    Mathf.Clamp01((acceptedNodes.Count - 1) / 4f));
                node.Position = ClampPosition(categoryNode.Position + orbit * distance);
                AddLinkIfMissing(snapshot, node.Id, categoryNode.Id, 0.72f);
            }

            var clusterMemberIndices = new Dictionary<string, int>(StringComparer.Ordinal);
            int looseDetachedIndex = 0;
            for (int i = 0; i < detachedNodes.Count; i++)
            {
                FirstContactSemanticMapNode node = detachedNodes[i];
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

        private static float ResolveAcceptedAngle(int index)
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
            return degree * Mathf.Deg2Rad;
        }

        private static Vector2 ResolveDetachedClusterPosition(int index)
        {
            int safeIndex = Mathf.Max(0, index);
            int column = safeIndex % 2;
            int row = safeIndex / 2;
            Vector2 basePosition = new(-0.54f, 0.08f);
            Vector2 offset = new(column * 0.46f, -row * 0.46f);
            return ClampPosition(basePosition + offset);
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
            float radius = active ? 0.32f : 0.26f;
            Vector2 orbit = new(
                Mathf.Cos(degree * Mathf.Deg2Rad),
                Mathf.Sin(degree * Mathf.Deg2Rad));
            return ClampPosition(clusterPosition + orbit * radius);
        }

        private static Vector2 ResolveDetachedPosition(int index, bool active)
        {
            int safeIndex = Mathf.Max(0, index);
            int column = safeIndex % 3;
            int row = safeIndex / 3;
            Vector2 basePosition = active
                ? new Vector2(-0.58f, -0.22f)
                : new Vector2(-0.72f, -0.42f);
            Vector2 offset = new(column * 0.24f, -row * 0.22f);
            return ClampPosition(basePosition + offset);
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
