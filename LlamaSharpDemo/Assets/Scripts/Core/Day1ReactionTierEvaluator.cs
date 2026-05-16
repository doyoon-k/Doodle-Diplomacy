using System;
using System.Collections.Generic;

namespace DoodleDiplomacy.Core
{
    public static class Day1ReactionTierEvaluator
    {
        private static readonly string[] StrongKeywords =
        {
            "gun",
            "handgun",
            "rifle",
            "weapon",
            "missile",
            "bomb",
            "knife",
            "blood",
            "corpse",
            "skull",
            "reproductive organ"
        };

        private static readonly string[] ModerateKeywords =
        {
            "house",
            "home",
            "shelter",
            "face",
            "body part",
            "flag",
            "religious symbol",
            "ritual icon",
            "abstract symbol",
            "alien",
            "animal",
            "baby",
            "child"
        };

        private static readonly string[] SubtleKeywords =
        {
            "apple",
            "fruit",
            "food",
            "tree",
            "plant",
            "chair",
            "table",
            "cup",
            "ball",
            "tool",
            "vehicle"
        };

        private static readonly string[] NoneKeywords =
        {
            "line",
            "dot",
            "simple shape",
            "blank"
        };

        public static ReactionTier Evaluate(string label)
        {
            string normalized = NormalizeLabel(label);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return ReactionTier.Subtle;
            }

            if (ContainsAny(normalized, StrongKeywords))
            {
                return ReactionTier.Strong;
            }

            if (ContainsAny(normalized, ModerateKeywords))
            {
                return ReactionTier.Moderate;
            }

            if (ContainsAny(normalized, SubtleKeywords))
            {
                return ReactionTier.Subtle;
            }

            return ContainsAny(normalized, NoneKeywords)
                ? ReactionTier.None
                : ReactionTier.Subtle;
        }

        public static string NormalizeLabel(string label)
        {
            return string.IsNullOrWhiteSpace(label)
                ? string.Empty
                : label.Trim().ToLowerInvariant();
        }

        private static bool ContainsAny(string label, IEnumerable<string> keywords)
        {
            foreach (string keyword in keywords)
            {
                if (label.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
