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
        public bool enablePipelineGeneration = true;
        public PromptPipelineAsset questionPipeline;
        [Min(1)] public int maxGenerationRetries = 3;

        [Header("Pipeline State Keys")]
        public string outputQuestionJsonKey = "question_json";
        public string previousAnswerLabelKey = "previous_answer_label";
        public string recentCardLabelsKey = "recent_card_labels";
        public string stableClustersKey = "stable_clusters";
        public string turnIndexKey = "turn_index";
        public string rejectReasonKey = "reject_reason";

        [Header("Hard Rules")]
        public bool requireSelectOne = true;
        [Min(0)] public int minUnknownCount = 1;
        [Min(1)] public int maxUnknownCount = 3;
        public string[] bannedTokens =
        {
            "WHY",
            "HOW",
            "REASON",
            "CAUSE",
            "EXPLAIN"
        };

        [Header("Fallback")]
        public FirstContactQuestionSet fallbackQuestionSet;
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
