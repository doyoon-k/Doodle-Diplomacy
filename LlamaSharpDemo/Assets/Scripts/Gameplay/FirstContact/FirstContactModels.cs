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
        LocalReference,
        BootstrapProbe,
        DecodeSample,
        Answer
    }

    public enum FirstContactQuestionSource
    {
        Pipeline,
        Fallback,
        RuntimeDefault
    }

    public readonly struct FirstContactSlotScore
    {
        public readonly UnknownSlot Slot;
        public readonly float Score;
        public readonly FirstContactTranslationStage ImpliedStage;
        public readonly bool IsActive;

        public FirstContactSlotScore(
            UnknownSlot slot,
            float score,
            FirstContactTranslationStage impliedStage,
            bool isActive)
        {
            Slot = slot;
            Score = score;
            ImpliedStage = impliedStage;
            IsActive = isActive;
        }
    }

    [Serializable]
    public sealed class FirstContactStageTexts
    {
        [Tooltip("숨겨진 단어가 첫 힌트 단계에 도달했을 때 터미널에 표시할 토큰입니다. 예: [OBJECT?]")]
        public string hint = "[HINT?]";
        [Tooltip("숨겨진 단어가 부분 해석 단계에 도달했을 때 터미널에 표시할 토큰입니다. 예: [DEFENSE-RELATED?]")]
        public string partial = "[PARTIAL?]";
        [Tooltip("숨겨진 단어가 완전히 해석되었을 때 터미널에 표시할 최종 토큰입니다. 예: PROTECT")]
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
        [Tooltip("대괄호 없이 쓰는 안정적인 UNKNOWN 슬롯 ID입니다. 예: UNKNOWN-01")]
        public string id = "UNKNOWN-01";
        [Tooltip("플레이어에게 직접 보여주지 않는 내부 정답 개념입니다. 그림 라벨과의 의미 유사도 비교에 사용합니다.")]
        public string targetConcept = "IMPORTANT";
        [Tooltip("UNKNOWN이 HINT, PARTIAL, SOLVED 단계로 열릴 때 터미널에 표시할 토큰 설정입니다.")]
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
        [Tooltip("질문이 처음 표시될 때 출력할 과학장교 대사의 localization key입니다.")]
        public string initial;
        [Tooltip("질문이 충분히 해석되어 답변 가능 상태일 때 출력할 과학장교 대사의 localization key입니다.")]
        public string readyToAnswer;
        [Tooltip("플레이어 답변을 송신한 뒤 출력할 과학장교 대사의 localization key입니다.")]
        public string answerSent;
    }

    [Serializable]
    public sealed class FirstContactQuestionDefinition
    {
        [Tooltip("질문을 구분하는 안정적인 ID입니다.")]
        public string questionId = "fc-q001";
        [Tooltip("내부 확인용 자연어 의도입니다. 플레이어에게 직접 보여주면 안 됩니다.")]
        [TextArea(1, 3)] public string internalIntent;
        [Tooltip("터미널에 표시할 외계인의 원시 토큰 배열입니다.")]
        public string[] primitiveTokens = Array.Empty<string>();
        [Tooltip("이 질문에 포함된 UNKNOWN 슬롯 정의 목록입니다.")]
        public FirstContactUnknownSlotDefinition[] unknownSlots = Array.Empty<FirstContactUnknownSlotDefinition>();
        [Tooltip("이 질문의 각 타이밍에 출력할 과학장교 대사 localization key 목록입니다.")]
        public FirstContactOfficerDialogueKeys dialogueKeys = new();
    }

    [CreateAssetMenu(
        fileName = "FirstContactQuestionSet",
        menuName = "DoodleDiplomacy/First Contact/Question Set")]
    public sealed class FirstContactQuestionSet : ScriptableObject
    {
        [Tooltip("파이프라인 질문 생성이 실패했을 때 순서대로 사용할 authored 질문 목록입니다.")]
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
        public TargetConceptEmbedding TargetEmbedding;
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
        public string BootstrapCategoryId;
        public string BootstrapCategoryDisplayName;
        public bool BootstrapCategoryEvaluated;
        public bool BootstrapCategoryAccepted;
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

    public sealed class TargetConceptEmbedding
    {
        public string TargetConcept;
        public float[] Vector;
        public bool IsValid => Vector != null && Vector.Length > 0;
    }
}
