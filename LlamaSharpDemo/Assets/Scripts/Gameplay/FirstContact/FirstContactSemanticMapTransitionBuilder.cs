using System;
using DoodleDiplomacy.Localization;
using UnityEngine;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    public static class FirstContactSemanticMapTransitionBuilder
    {
        public static FirstContactSemanticMapSnapshot BuildBootstrapResultFrame(
            FirstContactSemanticMapSnapshot beforeSnapshot,
            FirstContactSemanticMapSnapshot afterSnapshot,
            string activeCardNodeId,
            string categoryNodeId,
            bool accepted,
            bool becameStable,
            float progress)
        {
            var frame = new FirstContactSemanticMapSnapshot();
            if (afterSnapshot == null)
            {
                return frame;
            }

            float eased = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress));
            FirstContactSemanticMapNode afterCategory = afterSnapshot.FindNode(categoryNodeId);
            for (int i = 0; i < afterSnapshot.Nodes.Count; i++)
            {
                FirstContactSemanticMapNode after = afterSnapshot.Nodes[i];
                if (after == null)
                {
                    continue;
                }

                FirstContactSemanticMapNode before = beforeSnapshot?.FindNode(after.Id);
                FirstContactSemanticMapNode node = CloneNode(after);
                Vector2 start = ResolveTransitionStartPosition(before, after, afterCategory, activeCardNodeId, accepted);
                node.Position = ResolveTransitionPosition(
                    after,
                    afterCategory,
                    start,
                    activeCardNodeId,
                    accepted,
                    eased);
                if (string.Equals(after.Id, categoryNodeId, StringComparison.Ordinal) && accepted)
                {
                    node.Pulse = Mathf.Sin(Mathf.Clamp01(progress) * Mathf.PI) * (becameStable ? 1.35f : 1f);
                }
                else if (string.Equals(after.Id, activeCardNodeId, StringComparison.Ordinal))
                {
                    node.Pulse = BuildNewNodePulse(progress);
                }

                frame.Nodes.Add(node);
            }

            for (int i = 0; i < afterSnapshot.Links.Count; i++)
            {
                FirstContactSemanticMapLink afterLink = afterSnapshot.Links[i];
                float startStrength = FindLinkStrength(beforeSnapshot, afterLink.FromId, afterLink.ToId);
                float strength = Mathf.Lerp(startStrength, afterLink.Strength, eased);
                frame.Links.Add(new FirstContactSemanticMapLink(afterLink.FromId, afterLink.ToId, strength));
            }

            if (accepted &&
                !string.IsNullOrWhiteSpace(activeCardNodeId) &&
                !string.IsNullOrWhiteSpace(categoryNodeId) &&
                frame.FindNode(activeCardNodeId) != null &&
                frame.FindNode(categoryNodeId) != null)
            {
                float signalStrength = Mathf.Sin(Mathf.Clamp01(progress) * Mathf.PI);
                if (signalStrength > 0.01f)
                {
                    frame.Links.Add(new FirstContactSemanticMapLink(
                        activeCardNodeId,
                        categoryNodeId,
                        signalStrength));
                }
            }

            return frame;
        }

        public static FirstContactSemanticMapSnapshot BuildClusterFormationFrame(
            FirstContactSemanticMapSnapshot beforeSnapshot,
            FirstContactSemanticMapSnapshot afterSnapshot,
            FirstContactClusterFormationEvent formation,
            float progress)
        {
            var frame = new FirstContactSemanticMapSnapshot();
            if (afterSnapshot == null)
            {
                return frame;
            }

            float normalized = Mathf.Clamp01(progress);
            float eased = Mathf.SmoothStep(0f, 1f, normalized);
            float scanPulse = Mathf.Sin(normalized * Mathf.PI);
            bool lockPhase = formation.BecameStable && normalized >= 0.52f;
            bool labelGlitch = formation.BecameStable && normalized >= 0.52f && normalized <= 0.68f;
            FirstContactSemanticMapNode afterActive = afterSnapshot.FindNode(formation.ActiveCardNodeId);
            FirstContactSemanticMapNode afterCluster = afterSnapshot.FindNode(formation.ClusterNodeId);

            for (int i = 0; i < afterSnapshot.Nodes.Count; i++)
            {
                FirstContactSemanticMapNode after = afterSnapshot.Nodes[i];
                if (after == null)
                {
                    continue;
                }

                FirstContactSemanticMapNode before = beforeSnapshot?.FindNode(after.Id);
                FirstContactSemanticMapNode node = CloneNode(after);
                Vector2 start = ResolveClusterFormationStartPosition(
                    before,
                    after,
                    afterActive,
                    afterCluster,
                    formation.ActiveCardNodeId,
                    formation.ClusterNodeId);
                node.Position = Vector2.Lerp(start, after.Position, eased);

                if (string.Equals(after.Id, formation.ActiveCardNodeId, StringComparison.Ordinal))
                {
                    node.Pulse = Mathf.Max(node.Pulse, BuildNewNodePulse(normalized));
                }
                else if (string.Equals(after.Id, formation.ClusterNodeId, StringComparison.Ordinal))
                {
                    node.Pulse = Mathf.Max(node.Pulse, scanPulse * (formation.BecameStable ? 1.9f : 1.15f));
                    if (labelGlitch)
                    {
                        node.SecondaryLabel = LocalizedGroupUnknownLabel();
                    }
                }
                else if (IsFormationMember(formation, after.Id))
                {
                    float memberPulse = lockPhase
                        ? BuildSynchronizedMemberPulse(normalized)
                        : scanPulse * 0.48f;
                    node.Pulse = Mathf.Max(node.Pulse, memberPulse);
                }

                frame.Nodes.Add(node);
            }

            if (formation.MemberCount >= 3 && string.IsNullOrWhiteSpace(formation.ClusterNodeId))
            {
                AddTransientFormationRing(frame, afterSnapshot, formation, scanPulse);
            }

            for (int i = 0; i < afterSnapshot.Links.Count; i++)
            {
                FirstContactSemanticMapLink afterLink = afterSnapshot.Links[i];
                float startStrength = FindLinkStrength(beforeSnapshot, afterLink.FromId, afterLink.ToId);
                float strength = Mathf.Lerp(startStrength, afterLink.Strength, eased);
                if (IsFormationFocusLink(afterLink, formation.ActiveCardNodeId, formation.ClusterNodeId))
                {
                    strength = Mathf.Max(strength, scanPulse * (formation.BecameStable ? 1f : 0.72f));
                }

                AddOrBoostLink(frame, new FirstContactSemanticMapLink(afterLink.FromId, afterLink.ToId, strength));
            }

            AddFormationCandidateLinks(frame, formation, normalized);

            return frame;
        }

        private static void AddFormationCandidateLinks(
            FirstContactSemanticMapSnapshot frame,
            FirstContactClusterFormationEvent formation,
            float progress)
        {
            if (frame == null || formation.CandidateEdges == null)
            {
                return;
            }

            for (int i = 0; i < formation.CandidateEdges.Length; i++)
            {
                FirstContactClusterFormationEdge edge = formation.CandidateEdges[i];
                if (frame.FindNode(edge.FromNodeId) == null || frame.FindNode(edge.ToNodeId) == null)
                {
                    continue;
                }

                float start = 0.16f + i * 0.14f;
                float scanEnd = start + 0.24f;
                float settleEnd = scanEnd + 0.32f;
                if (progress < start)
                {
                    continue;
                }

                float localScan = Mathf.Clamp01((progress - start) / Mathf.Max(0.0001f, scanEnd - start));
                if (progress <= scanEnd)
                {
                    float pulse = Mathf.Sin(localScan * Mathf.PI);
                    AddOrBoostLink(frame, new FirstContactSemanticMapLink(
                        edge.FromNodeId,
                        edge.ToNodeId,
                        Mathf.Lerp(0.18f, Mathf.Max(0.36f, edge.Strength), pulse),
                        FirstContactSemanticMapLinkKind.Candidate));
                    continue;
                }

                float localSettle = Mathf.Clamp01((progress - scanEnd) / Mathf.Max(0.0001f, settleEnd - scanEnd));
                if (edge.Confirmed)
                {
                    float strength = Mathf.Lerp(Mathf.Max(0.42f, edge.Strength), 1f, Mathf.SmoothStep(0f, 1f, localSettle));
                    AddOrBoostLink(frame, new FirstContactSemanticMapLink(
                        edge.FromNodeId,
                        edge.ToNodeId,
                        strength,
                        FirstContactSemanticMapLinkKind.Confirmed));
                    continue;
                }

                float fade = 1f - Mathf.SmoothStep(0f, 1f, localSettle);
                if (fade > 0.03f)
                {
                    AddOrBoostLink(frame, new FirstContactSemanticMapLink(
                        edge.FromNodeId,
                        edge.ToNodeId,
                        Mathf.Max(0.08f, edge.Strength) * fade,
                        FirstContactSemanticMapLinkKind.Rejected));
                }
            }
        }

        private static void AddTransientFormationRing(
            FirstContactSemanticMapSnapshot frame,
            FirstContactSemanticMapSnapshot afterSnapshot,
            FirstContactClusterFormationEvent formation,
            float pulse)
        {
            if (frame == null || afterSnapshot == null)
            {
                return;
            }

            if (!TryResolveMemberCenter(afterSnapshot, formation.MemberNodeIds, out Vector2 center))
            {
                FirstContactSemanticMapNode active = afterSnapshot.FindNode(formation.ActiveCardNodeId);
                center = active != null ? active.Position : Vector2.zero;
            }

            frame.Nodes.Add(new FirstContactSemanticMapNode
            {
                Id = $"F:{formation.ActiveCardNodeId}",
                Label = LocalizedGroupLabel(),
                SecondaryLabel = LocalizedGroupUnknownLabel(),
                Kind = FirstContactSemanticMapNodeKind.StableCluster,
                Position = center,
                IsActive = false,
                Marker = '#',
                Pulse = Mathf.Max(0.18f, pulse * 1.18f)
            });
        }

        private static bool TryResolveMemberCenter(
            FirstContactSemanticMapSnapshot snapshot,
            string[] memberNodeIds,
            out Vector2 center)
        {
            center = Vector2.zero;
            if (snapshot == null || memberNodeIds == null || memberNodeIds.Length == 0)
            {
                return false;
            }

            int count = 0;
            for (int i = 0; i < memberNodeIds.Length; i++)
            {
                FirstContactSemanticMapNode node = snapshot.FindNode(memberNodeIds[i]);
                if (node == null)
                {
                    continue;
                }

                center += node.Position;
                count++;
            }

            if (count <= 0)
            {
                return false;
            }

            center /= count;
            return true;
        }

        private static bool IsFormationMember(
            FirstContactClusterFormationEvent formation,
            string nodeId)
        {
            if (formation.MemberNodeIds == null || string.IsNullOrWhiteSpace(nodeId))
            {
                return false;
            }

            for (int i = 0; i < formation.MemberNodeIds.Length; i++)
            {
                if (string.Equals(formation.MemberNodeIds[i], nodeId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static float BuildSynchronizedMemberPulse(float progress)
        {
            float lockProgress = Mathf.Clamp01((progress - 0.52f) / 0.34f);
            float envelope = Mathf.Sin(lockProgress * Mathf.PI);
            float flash = Mathf.Sin(lockProgress * Mathf.PI * 6f);
            return Mathf.Clamp01(0.32f + flash * flash * envelope * 1.18f);
        }

        private static float BuildNewNodePulse(float progress)
        {
            float normalized = Mathf.Clamp01(progress);
            float envelope = Mathf.SmoothStep(1f, 0f, normalized);
            float flashes = Mathf.Sin(normalized * Mathf.PI * 5.5f);
            return Mathf.Clamp01(flashes * flashes * envelope);
        }

        public static float BuildPersistentNodePulse(float elapsed)
        {
            float wave = (Mathf.Sin(Mathf.Max(0f, elapsed) * Mathf.PI * 2f / 1.35f) + 1f) * 0.5f;
            return Mathf.Lerp(0.38f, 0.92f, wave);
        }

        private static Vector2 ResolveTransitionStartPosition(
            FirstContactSemanticMapNode before,
            FirstContactSemanticMapNode after,
            FirstContactSemanticMapNode afterCategory,
            string activeCardNodeId,
            bool accepted)
        {
            if (before != null)
            {
                return before.Position;
            }

            if (after != null && string.Equals(after.Id, activeCardNodeId, StringComparison.Ordinal))
            {
                float x = afterCategory != null
                    ? Mathf.Lerp(afterCategory.Position.x, after.Position.x, accepted ? 0.25f : 0.5f)
                    : after.Position.x;
                return new Vector2(Mathf.Clamp(x, -0.78f, 0.78f), 0.92f);
            }

            return after?.Position ?? Vector2.zero;
        }

        private static Vector2 ResolveTransitionPosition(
            FirstContactSemanticMapNode after,
            FirstContactSemanticMapNode afterCategory,
            Vector2 start,
            string activeCardNodeId,
            bool accepted,
            float eased)
        {
            if (after == null)
            {
                return start;
            }

            if (!string.Equals(after.Id, activeCardNodeId, StringComparison.Ordinal) ||
                accepted ||
                afterCategory == null)
            {
                return Vector2.Lerp(start, after.Position, eased);
            }

            Vector2 nearCategory = Vector2.Lerp(start, afterCategory.Position, 0.58f);
            if (eased < 0.58f)
            {
                return Vector2.Lerp(start, nearCategory, Mathf.SmoothStep(0f, 1f, eased / 0.58f));
            }

            return Vector2.Lerp(
                nearCategory,
                after.Position,
                Mathf.SmoothStep(0f, 1f, (eased - 0.58f) / 0.42f));
        }

        private static Vector2 ResolveClusterFormationStartPosition(
            FirstContactSemanticMapNode before,
            FirstContactSemanticMapNode after,
            FirstContactSemanticMapNode afterActive,
            FirstContactSemanticMapNode afterCluster,
            string activeCardNodeId,
            string clusterNodeId)
        {
            if (before != null)
            {
                return before.Position;
            }

            if (after == null)
            {
                return Vector2.zero;
            }

            if (string.Equals(after.Id, activeCardNodeId, StringComparison.Ordinal))
            {
                float x = afterCluster != null
                    ? Mathf.Lerp(afterCluster.Position.x, after.Position.x, 0.42f)
                    : after.Position.x;
                return new Vector2(Mathf.Clamp(x, -0.78f, 0.78f), 0.92f);
            }

            if (string.Equals(after.Id, clusterNodeId, StringComparison.Ordinal) && afterActive != null)
            {
                return afterActive.Position;
            }

            return after.Position;
        }

        private static float FindLinkStrength(
            FirstContactSemanticMapSnapshot snapshot,
            string fromId,
            string toId)
        {
            if (snapshot == null)
            {
                return 0f;
            }

            for (int i = 0; i < snapshot.Links.Count; i++)
            {
                FirstContactSemanticMapLink link = snapshot.Links[i];
                bool same = string.Equals(link.FromId, fromId, StringComparison.Ordinal) &&
                            string.Equals(link.ToId, toId, StringComparison.Ordinal);
                bool reverse = string.Equals(link.FromId, toId, StringComparison.Ordinal) &&
                               string.Equals(link.ToId, fromId, StringComparison.Ordinal);
                if (same || reverse)
                {
                    return link.Strength;
                }
            }

            return 0f;
        }

        private static bool IsLinkedTo(
            FirstContactSemanticMapSnapshot snapshot,
            string nodeId,
            string activeCardNodeId,
            string clusterNodeId)
        {
            if (snapshot == null || string.IsNullOrWhiteSpace(nodeId))
            {
                return false;
            }

            for (int i = 0; i < snapshot.Links.Count; i++)
            {
                FirstContactSemanticMapLink link = snapshot.Links[i];
                if (!LinkContains(link, nodeId))
                {
                    continue;
                }

                if (LinkContains(link, activeCardNodeId) || LinkContains(link, clusterNodeId))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsFormationFocusLink(
            FirstContactSemanticMapLink link,
            string activeCardNodeId,
            string clusterNodeId)
        {
            return LinkContains(link, activeCardNodeId) || LinkContains(link, clusterNodeId);
        }

        private static bool LinkContains(FirstContactSemanticMapLink link, string nodeId)
        {
            return !string.IsNullOrWhiteSpace(nodeId) &&
                   (string.Equals(link.FromId, nodeId, StringComparison.Ordinal) ||
                    string.Equals(link.ToId, nodeId, StringComparison.Ordinal));
        }

        private static void AddOrBoostLink(
            FirstContactSemanticMapSnapshot snapshot,
            FirstContactSemanticMapLink link)
        {
            if (snapshot == null ||
                string.IsNullOrWhiteSpace(link.FromId) ||
                string.IsNullOrWhiteSpace(link.ToId))
            {
                return;
            }

            for (int i = 0; i < snapshot.Links.Count; i++)
            {
                FirstContactSemanticMapLink existing = snapshot.Links[i];
                bool sameDirection = string.Equals(existing.FromId, link.FromId, StringComparison.Ordinal) &&
                                     string.Equals(existing.ToId, link.ToId, StringComparison.Ordinal);
                bool reverseDirection = string.Equals(existing.FromId, link.ToId, StringComparison.Ordinal) &&
                                        string.Equals(existing.ToId, link.FromId, StringComparison.Ordinal);
                if (sameDirection || reverseDirection)
                {
                    bool preferFormationLink =
                        existing.Kind == FirstContactSemanticMapLinkKind.Normal &&
                        link.Kind != FirstContactSemanticMapLinkKind.Normal;
                    bool preferStrongerSameKind =
                        existing.Kind == link.Kind &&
                        link.Strength > existing.Strength;
                    bool preferConfirmed =
                        link.Kind == FirstContactSemanticMapLinkKind.Confirmed &&
                        existing.Kind != FirstContactSemanticMapLinkKind.Confirmed;
                    if (preferFormationLink || preferStrongerSameKind || preferConfirmed)
                    {
                        snapshot.Links[i] = link;
                    }

                    return;
                }
            }

            snapshot.Links.Add(link);
        }

        private static FirstContactSemanticMapNode CloneNode(FirstContactSemanticMapNode node)
        {
            return new FirstContactSemanticMapNode
            {
                Id = node.Id,
                Label = node.Label,
                SecondaryLabel = node.SecondaryLabel,
                Kind = node.Kind,
                Position = node.Position,
                Embedding = node.Embedding,
                IsActive = node.IsActive,
                Marker = node.Marker,
                BootstrapCategoryId = node.BootstrapCategoryId,
                IsBootstrapDetached = node.IsBootstrapDetached,
                TraceCount = node.TraceCount,
                RequiredTraceCount = node.RequiredTraceCount,
                IsBootstrapCategoryStable = node.IsBootstrapCategoryStable,
                Pulse = node.Pulse
            };
        }

        private static string LocalizedGroupLabel()
        {
            return L10n.T("first_contact.terminal.semantic_map.group", "PATTERN").ToUpperInvariant();
        }

        private static string LocalizedGroupUnknownLabel()
        {
            return L10n.T("first_contact.terminal.semantic_map.group_unknown", "[PATTERN-??]").ToUpperInvariant();
        }
    }
}
