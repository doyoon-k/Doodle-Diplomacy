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

        private static readonly HashSet<string> WrittenTextExactLabels = new(StringComparer.OrdinalIgnoreCase)
        {
            "text",
            "texts",
            "word",
            "words",
            "letter",
            "letters",
            "character",
            "characters",
            "number",
            "numbers",
            "digit",
            "digits",
            "alphabet",
            "glyph",
            "glyphs",
            "handwriting",
            "writing",
            "caption",
            "captions",
            "inscription",
            "inscriptions"
        };

        private static readonly string[] WrittenTextPhrases =
        {
            "written symbol",
            "written symbols",
            "written character",
            "written characters",
            "alphabet character",
            "alphabet characters",
            "text character",
            "text characters",
            "letter character",
            "letter characters",
            "numeric character",
            "numeric characters"
        };

        private static readonly HashSet<string> ActionSceneExactLabels = new(StringComparer.OrdinalIgnoreCase)
        {
            "action",
            "actions",
            "activity",
            "activities",
            "scene",
            "event",
            "events",
            "gesture",
            "gestures",
            "movement",
            "motion",
            "interaction",
            "interactions",
            "relationship",
            "relationships",
            "verb",
            "running",
            "jumping",
            "walking",
            "dancing",
            "fighting",
            "hugging",
            "kissing",
            "throwing",
            "eating",
            "sleeping"
        };

        private static readonly string[] ActionScenePhrases =
        {
            "action or scene",
            "action scene",
            "scene or action",
            "human action",
            "person action",
            "body action",
            "verb concept",
            "visual verb",
            "story scene",
            "narrative scene",
            "relationship scene",
            "interaction scene",
            "person running",
            "running person",
            "person jumping",
            "jumping person",
            "person walking",
            "walking person",
            "person dancing",
            "dancing person",
            "person fighting",
            "fighting person",
            "person throwing",
            "throwing person",
            "person eating",
            "eating person",
            "person sleeping",
            "sleeping person",
            "two people interacting",
            "people interacting"
        };

        private static readonly string[] MixedConceptPhrases =
        {
            "multiple objects",
            "multiple items",
            "several objects",
            "several items",
            "different objects",
            "different items",
            "different things",
            "different kinds",
            "different types",
            "various objects",
            "various items",
            "assorted objects",
            "assorted items",
            "mixed objects",
            "mixed items",
            "mixed fruit",
            "mixed fruits"
        };

        private static readonly string[] MixedConceptSeparators =
        {
            " and ",
            " with ",
            " plus ",
            " next to ",
            " beside ",
            " between ",
            ",",
            "&",
            "+",
            "/"
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

            if (IsWrittenTextLabel(normalized))
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

        public static bool IsWrittenTextLabel(string label)
        {
            string normalized = Day1ReactionTierEvaluator.NormalizeLabel(label);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return false;
            }

            if (WrittenTextExactLabels.Contains(normalized) ||
                VisualStimulusClassificationResult.LabelIndicatesWrittenText(normalized))
            {
                return true;
            }

            foreach (string phrase in WrittenTextPhrases)
            {
                if (ContainsPhrase(normalized, phrase))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IsActionOrSceneLabel(string label)
        {
            string normalized = Day1ReactionTierEvaluator.NormalizeLabel(label);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return false;
            }

            if (ActionSceneExactLabels.Contains(normalized))
            {
                return true;
            }

            foreach (string phrase in ActionScenePhrases)
            {
                if (ContainsPhrase(normalized, phrase))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IsAllowedObjectCount(int objectCount, string label)
        {
            if (objectCount <= 0)
            {
                return false;
            }

            if (objectCount == 1)
            {
                return true;
            }

            return IsSingleConceptStimulus(label);
        }

        public static bool IsSingleConceptStimulus(string label)
        {
            string normalized = Day1ReactionTierEvaluator.NormalizeLabel(label);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return false;
            }

            if (IsBlockedLabel(normalized))
            {
                return false;
            }

            foreach (string phrase in MixedConceptPhrases)
            {
                if (ContainsPhrase(normalized, phrase))
                {
                    return false;
                }
            }

            foreach (string separator in MixedConceptSeparators)
            {
                if (normalized.IndexOf(separator, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return false;
                }
            }

            return true;
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
