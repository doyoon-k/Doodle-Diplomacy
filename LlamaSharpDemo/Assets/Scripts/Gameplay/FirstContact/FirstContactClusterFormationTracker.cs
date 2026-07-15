using System;
using System.Collections.Generic;
using UnityEngine;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    public readonly struct FirstContactClusterTransitionSnapshot
    {
        public FirstContactClusterTransitionSnapshot(string id, bool isStable, int memberCount)
        {
            Id = id ?? string.Empty;
            IsStable = isStable;
            MemberCount = Mathf.Max(0, memberCount);
        }

        public string Id { get; }
        public bool IsStable { get; }
        public int MemberCount { get; }
    }

    public static class FirstContactClusterFormationTracker
    {
        public static IReadOnlyList<FirstContactClusterTransitionSnapshot> Capture(
            IReadOnlyList<SemanticClusterRecord> clusters)
        {
            var snapshots = new List<FirstContactClusterTransitionSnapshot>();
            if (clusters == null)
            {
                return snapshots;
            }

            for (int i = 0; i < clusters.Count; i++)
            {
                SemanticClusterRecord cluster = clusters[i];
                if (cluster == null || string.IsNullOrWhiteSpace(cluster.Id))
                {
                    continue;
                }

                snapshots.Add(new FirstContactClusterTransitionSnapshot(
                    cluster.Id,
                    cluster.IsStable,
                    cluster.Members.Count));
            }

            return snapshots;
        }

        public static FirstContactClusterFormationEvent BuildFormation(
            SemanticCardRecord card,
            SemanticClusterRecord cluster,
            IReadOnlyList<FirstContactClusterTransitionSnapshot> beforeSnapshots,
            IReadOnlyList<FirstContactClusterFormationEdge> formationEdges)
        {
            if (card == null || cluster == null)
            {
                return default;
            }

            bool hadCluster = TryFindSnapshot(
                beforeSnapshots,
                cluster.Id,
                out FirstContactClusterTransitionSnapshot beforeSnapshot);
            bool becameStable = cluster.IsStable && (!hadCluster || !beforeSnapshot.IsStable);
            bool isNewCluster =
                !hadCluster ||
                (beforeSnapshot.MemberCount <= 1 && cluster.Members.Count > 1);
            return new FirstContactClusterFormationEvent(
                FirstContactSemanticMapLayout.BuildCardNodeId(card),
                cluster.IsStable
                    ? FirstContactSemanticMapLayout.BuildClusterNodeId(cluster)
                    : string.Empty,
                cluster.DisplayName,
                hasCluster: true,
                isNewCluster,
                becameStable,
                cluster.IsStable,
                cluster.Members.Count,
                CopyEdges(formationEdges),
                BuildMemberNodeIds(cluster));
        }

        private static FirstContactClusterFormationEdge[] CopyEdges(
            IReadOnlyList<FirstContactClusterFormationEdge> edges)
        {
            if (edges == null || edges.Count == 0)
            {
                return Array.Empty<FirstContactClusterFormationEdge>();
            }

            var copy = new FirstContactClusterFormationEdge[edges.Count];
            for (int i = 0; i < edges.Count; i++)
            {
                copy[i] = edges[i];
            }

            return copy;
        }

        private static string[] BuildMemberNodeIds(SemanticClusterRecord cluster)
        {
            if (cluster == null || cluster.Members.Count == 0)
            {
                return Array.Empty<string>();
            }

            var nodeIds = new string[cluster.Members.Count];
            for (int i = 0; i < cluster.Members.Count; i++)
            {
                nodeIds[i] = FirstContactSemanticMapLayout.BuildCardNodeId(cluster.Members[i]);
            }

            return nodeIds;
        }

        private static bool TryFindSnapshot(
            IReadOnlyList<FirstContactClusterTransitionSnapshot> snapshots,
            string clusterId,
            out FirstContactClusterTransitionSnapshot snapshot)
        {
            if (snapshots != null && !string.IsNullOrWhiteSpace(clusterId))
            {
                for (int i = 0; i < snapshots.Count; i++)
                {
                    if (string.Equals(
                            snapshots[i].Id,
                            clusterId,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        snapshot = snapshots[i];
                        return true;
                    }
                }
            }

            snapshot = default;
            return false;
        }
    }
}
