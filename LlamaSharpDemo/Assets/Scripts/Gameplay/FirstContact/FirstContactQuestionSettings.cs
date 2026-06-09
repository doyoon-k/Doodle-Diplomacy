using System;
using UnityEngine;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    [CreateAssetMenu(
        fileName = "FirstContactQuestionSettings",
        menuName = "DoodleDiplomacy/First Contact/Question Settings")]
    public sealed class FirstContactQuestionSettings : ScriptableObject
    {
        [Header("Pipeline First")]
        [Tooltip("켜면 질문을 먼저 파이프라인으로 생성하고, 실패하면 fallback 질문을 사용합니다.")]
        public bool enablePipelineGeneration = true;
        [Tooltip("외계 질문 생성을 담당할 Prompt Pipeline 에셋입니다. 비어 있으면 fallback 질문만 사용합니다.")]
        public PromptPipelineAsset questionPipeline;
        [Tooltip("파이프라인 질문 생성이나 파싱이 실패했을 때 다시 시도할 최대 횟수입니다.")]
        [Min(1)] public int maxGenerationRetries = 3;

        [Header("Pipeline State Keys")]
        [Tooltip("파이프라인 결과에서 질문 JSON 문자열을 읽을 state key입니다.")]
        public string outputQuestionJsonKey = "question_json";
        [Tooltip("이전 턴에서 플레이어가 답변한 그림 라벨을 파이프라인에 전달할 state key입니다.")]
        public string previousAnswerLabelKey = "previous_answer_label";
        [Tooltip("최근 그림 카드 라벨 목록을 파이프라인에 전달할 state key입니다.")]
        public string recentCardLabelsKey = "recent_card_labels";
        [Tooltip("안정화된 의미 군집 요약을 파이프라인에 전달할 state key입니다.")]
        public string stableClustersKey = "stable_clusters";
        [Tooltip("현재 대화 턴 번호를 파이프라인에 전달할 state key입니다.")]
        public string turnIndexKey = "turn_index";
        [Tooltip("이전 생성 결과가 거절된 이유를 재시도 파이프라인에 전달할 state key입니다.")]
        public string rejectReasonKey = "reject_reason";

        [Header("Hard Rules")]
        [Tooltip("켜면 생성된 원시 토큰 배열이 반드시 SELECT-ONE으로 끝나야 통과합니다.")]
        public bool requireSelectOne = true;
        [Tooltip("한 질문에 허용할 최소 UNKNOWN 슬롯 수입니다.")]
        [Min(0)] public int minUnknownCount = 1;
        [Tooltip("한 질문에 허용할 최대 UNKNOWN 슬롯 수입니다.")]
        [Min(1)] public int maxUnknownCount = 3;
        [Tooltip("그림 하나로 답하기 어려운 질문을 막기 위해 금지할 원시 토큰 목록입니다.")]
        public string[] bannedTokens =
        {
            "WHY",
            "HOW",
            "REASON",
            "CAUSE",
            "EXPLAIN"
        };

        [Header("Fallback")]
        [Tooltip("파이프라인 생성이 실패했을 때 사용할 authored 질문 세트입니다. 비어 있으면 코드 내 기본 5문항을 사용합니다.")]
        public FirstContactQuestionSet fallbackQuestionSet;
        [Tooltip("켜면 fallback 질문을 모두 사용한 뒤 처음 질문부터 반복합니다.")]
        public bool loopFallbackQuestions;

        private void OnValidate()
        {
            maxGenerationRetries = Math.Max(1, maxGenerationRetries);
            minUnknownCount = Math.Max(0, minUnknownCount);
            maxUnknownCount = Math.Max(1, maxUnknownCount);
            if (maxUnknownCount < minUnknownCount)
            {
                maxUnknownCount = minUnknownCount;
            }
        }
    }
}
