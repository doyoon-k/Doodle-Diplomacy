using System;
using System.Collections.Generic;

namespace DoodleDiplomacy.Core
{
    public static class Day1StimulusSubmissionPolicy
    {
        private static readonly HashSet<string> BlockedExactLabels = new(StringComparer.OrdinalIgnoreCase)
        {
            "blank",
            "empty",
            "line",
            "lines",
            "dot",
            "dots",
            "point",
            "points",
            "stroke",
            "strokes",
            "mark",
            "marks",
            "scribble",
            "scribbles",
            "shape",
            "shapes",
            "circle",
            "square",
            "triangle",
            "rectangle",
            "oval",
            "ellipse",
            "polygon",
            "island"
        };

        private static readonly string[] BlockedPhrases =
        {
            "simple line",
            "simple lines",
            "straight line",
            "straight lines",
            "curved line",
            "curved lines",
            "wavy line",
            "wavy lines",
            "simple mark",
            "simple marks",
            "random mark",
            "random marks",
            "simple shape",
            "simple shapes",
            "basic shape",
            "basic shapes",
            "geometric shape",
            "geometric shapes",
            "abstract shape",
            "abstract shapes",
            "basic geometric shape",
            "basic geometric shapes",
            "random scribble"
        };

        public static bool IsBlockedLabel(string label)
        {
            string normalized = Day1ReactionTierEvaluator.NormalizeLabel(label);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return true;
            }

            if (BlockedExactLabels.Contains(normalized))
            {
                return true;
            }

            foreach (string phrase in BlockedPhrases)
            {
                if (ContainsPhrase(normalized, phrase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsPhrase(string label, string phrase)
        {
            if (string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(phrase))
            {
                return false;
            }

            int index = label.IndexOf(phrase, StringComparison.OrdinalIgnoreCase);
            while (index >= 0)
            {
                int before = index - 1;
                int after = index + phrase.Length;
                bool startsAtBoundary = before < 0 || !char.IsLetterOrDigit(label[before]);
                bool endsAtBoundary = after >= label.Length || !char.IsLetterOrDigit(label[after]);
                if (startsAtBoundary && endsAtBoundary)
                {
                    return true;
                }

                index = label.IndexOf(phrase, index + 1, StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }
    }
}
