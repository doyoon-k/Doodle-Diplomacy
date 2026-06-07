using System;
using System.Collections.Generic;
using UnityEngine;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    public enum FirstContactTranslationStage
    {
        Unknown,
        Hint,
        Partial,
        Solved
    }

    public enum FirstContactCardSource
    {
        DecodeSample,
        Answer
    }

    public enum FirstContactQuestionSource
    {
        Pipeline,
        Fallback,
        RuntimeDefault
    }

    [Serializable]
    public sealed class FirstContactStageTexts
    {
        [Tooltip("Displayed when the hidden concept reaches the first hint level, e.g. [OBJECT?].")]
        public string hint = "[HINT?]";
        [Tooltip("Displayed when the hidden concept is partially translated, e.g. [DEFENSE-RELATED?].")]
        public string partial = "[PARTIAL?]";
        [Tooltip("Displayed when the hidden concept is solved, e.g. PROTECT.")]
        public string solved = "SOLVED";

        public string GetDisplayText(FirstContactTranslationStage stage, string unknownId)
        {
            return stage switch
            {
                FirstContactTranslationStage.Hint => SafeToken(hint, $"[{unknownId}?]"),
                FirstContactTranslationStage.Partial => SafeToken(partial, SafeToken(hint, $"[{unknownId}?]")),
                FirstContactTranslationStage.Solved => SafeToken(solved, SafeToken(partial, SafeToken(hint, $"[{unknownId}?]"))),
                _ => $"[{unknownId}]"
            };
        }

        private static string SafeToken(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }
    }

    [Serializable]
    public sealed class FirstContactUnknownSlotDefinition
    {
        [Tooltip("Stable id without brackets, e.g. UNKNOWN-01.")]
        public string id = "UNKNOWN-01";
        [Tooltip("Hidden concept used for debugging and fallback anchor generation.")]
        public string targetConcept = "IMPORTANT";
        [Tooltip("Internal semantic anchors. These are not shown as answer examples.")]
        public string[] targetAnchors = Array.Empty<string>();
        public FirstContactStageTexts stageTexts = new();

        public string NormalizedId => NormalizeUnknownId(id);

        public static string NormalizeUnknownId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "UNKNOWN";
            }

            string trimmed = value.Trim();
            if (trimmed.StartsWith("[", StringComparison.Ordinal) &&
                trimmed.EndsWith("]", StringComparison.Ordinal) &&
                trimmed.Length > 2)
            {
                trimmed = trimmed.Substring(1, trimmed.Length - 2);
            }

            return trimmed.Trim();
        }
    }

    [Serializable]
    public sealed class FirstContactOfficerDialogueKeys
    {
        public string initial;
        public string readyToAnswer;
        public string answerSent;
    }

    [Serializable]
    public sealed class FirstContactQuestionDefinition
    {
        public string questionId = "fc-q001";
        [Tooltip("Internal-only natural-language intent. Never shown directly to the player.")]
        [TextArea(1, 3)] public string internalIntent;
        [Tooltip("Primitive alien token stream shown in the terminal.")]
        public string[] primitiveTokens = Array.Empty<string>();
        public FirstContactUnknownSlotDefinition[] unknownSlots = Array.Empty<FirstContactUnknownSlotDefinition>();
        public FirstContactOfficerDialogueKeys dialogueKeys = new();
    }

    [CreateAssetMenu(
        fileName = "FirstContactQuestionSet",
        menuName = "DoodleDiplomacy/First Contact/Question Set")]
    public sealed class FirstContactQuestionSet : ScriptableObject
    {
        public FirstContactQuestionDefinition[] questions = Array.Empty<FirstContactQuestionDefinition>();

        public bool TryGetQuestion(int index, out FirstContactQuestionDefinition question)
        {
            question = null;
            if (questions == null || questions.Length == 0)
            {
                return false;
            }

            int safeIndex = Mathf.Clamp(index, 0, questions.Length - 1);
            question = questions[safeIndex];
            return question != null;
        }
    }

    public sealed class AlienQuestion
    {
        public string Id;
        public string InternalIntent;
        public readonly List<string> PrimitiveTokens = new();
        public readonly List<UnknownSlot> UnknownSlots = new();
        public FirstContactOfficerDialogueKeys DialogueKeys;
        public FirstContactQuestionSource Source;

        public bool HasUnknowns => UnknownSlots.Count > 0;

        public static AlienQuestion FromDefinition(
            FirstContactQuestionDefinition definition,
            FirstContactQuestionSource source)
        {
            var question = new AlienQuestion
            {
                Id = string.IsNullOrWhiteSpace(definition?.questionId) ? Guid.NewGuid().ToString("N") : definition.questionId.Trim(),
                InternalIntent = definition?.internalIntent ?? string.Empty,
                DialogueKeys = definition?.dialogueKeys ?? new FirstContactOfficerDialogueKeys(),
                Source = source
            };

            if (definition?.primitiveTokens != null)
            {
                foreach (string token in definition.primitiveTokens)
                {
                    if (!string.IsNullOrWhiteSpace(token))
                    {
                        question.PrimitiveTokens.Add(token.Trim());
                    }
                }
            }

            if (definition?.unknownSlots != null)
            {
                foreach (FirstContactUnknownSlotDefinition slot in definition.unknownSlots)
                {
                    if (slot != null)
                    {
                        question.UnknownSlots.Add(new UnknownSlot(slot));
                    }
                }
            }

            return question;
        }

        public UnknownSlot FindUnknown(string unknownId)
        {
            string normalized = FirstContactUnknownSlotDefinition.NormalizeUnknownId(unknownId);
            for (int i = 0; i < UnknownSlots.Count; i++)
            {
                if (string.Equals(UnknownSlots[i].Id, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return UnknownSlots[i];
                }
            }

            return null;
        }

        public string[] BuildDisplayTokens()
        {
            string[] displayTokens = PrimitiveTokens.ToArray();
            for (int i = 0; i < displayTokens.Length; i++)
            {
                UnknownSlot slot = FindUnknown(displayTokens[i]);
                if (slot != null)
                {
                    displayTokens[i] = slot.GetDisplayToken();
                }
            }

            return displayTokens;
        }

        public string BuildDisplayLine()
        {
            return string.Join(" / ", BuildDisplayTokens());
        }
    }

    public sealed class UnknownSlot
    {
        public readonly FirstContactUnknownSlotDefinition Definition;
        public FirstContactTranslationStage Stage;
        public AnchorEmbeddingSet AnchorSet;
        public float BestScore;
        public string LinkedClusterId;

        public UnknownSlot(FirstContactUnknownSlotDefinition definition)
        {
            Definition = definition;
            Stage = FirstContactTranslationStage.Unknown;
            BestScore = -1f;
        }

        public string Id => Definition != null ? Definition.NormalizedId : "UNKNOWN";
        public string TargetConcept => Definition?.targetConcept ?? string.Empty;
        public string[] Anchors => Definition?.targetAnchors ?? Array.Empty<string>();

        public string GetDisplayToken()
        {
            FirstContactStageTexts stageTexts = Definition?.stageTexts ?? new FirstContactStageTexts();
            return stageTexts.GetDisplayText(Stage, Id);
        }

        public bool TryAdvanceTo(FirstContactTranslationStage nextStage, float score)
        {
            if (nextStage <= Stage)
            {
                BestScore = Mathf.Max(BestScore, score);
                return false;
            }

            Stage = nextStage;
            BestScore = Mathf.Max(BestScore, score);
            return true;
        }
    }

    public sealed class SemanticCardRecord
    {
        public string Id;
        public Texture2D Texture;
        public byte[] PngBytes;
        public string Label;
        public string LocalizedLabel;
        public float[] Embedding;
        public DoodleDiplomacy.Devices.BrainwaveSemanticProfile WaveformProfile;
        public FirstContactCardSource Source;
        public string TargetUnknownId;
        public string QuestionId;
        public string ClusterId;
        public int TurnIndex;
    }

    public sealed class SemanticClusterRecord
    {
        public string Id;
        public readonly List<SemanticCardRecord> Members = new();
        public float[] Centroid;
        public string ProvisionalName;
        public bool IsStable;
        public float Cohesion;

        public string DisplayName => string.IsNullOrWhiteSpace(ProvisionalName) ? $"[{Id}]" : ProvisionalName.Trim();
    }

    public sealed class FirstContactSessionContext
    {
        public int TurnIndex;
        public AlienQuestion CurrentQuestion;
        public SemanticCardRecord PreviousAnswer;
        public readonly List<SemanticCardRecord> RecentCards = new();
        public readonly List<SemanticClusterRecord> StableClusters = new();
    }

    public readonly struct EmbeddingResult
    {
        public readonly string Text;
        public readonly float[] Vector;
        public readonly bool IsValid;
        public readonly string Error;

        public EmbeddingResult(string text, float[] vector, string error = null)
        {
            Text = text ?? string.Empty;
            Vector = vector;
            IsValid = vector != null && vector.Length > 0 && string.IsNullOrWhiteSpace(error);
            Error = error ?? string.Empty;
        }
    }

    public sealed class AnchorEmbeddingSet
    {
        public string TargetConcept;
        public string[] Anchors = Array.Empty<string>();
        public float[][] AnchorVectors = Array.Empty<float[]>();
        public float[] Centroid;
        public bool IsValid => Centroid != null && Centroid.Length > 0;
    }
}
