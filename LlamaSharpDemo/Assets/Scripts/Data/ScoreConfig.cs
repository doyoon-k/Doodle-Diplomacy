using UnityEngine;

namespace DoodleDiplomacy.Data
{
    public enum EndingType { Destruction, Exile, Diplomacy, Alliance }

    [CreateAssetMenu(fileName = "ScoreConfig", menuName = "DoodleDiplomacy/Score Config")]
    public class ScoreConfig : ScriptableObject
    {
        public int totalRounds = 5;
        public float lastRoundMultiplier = 1.5f;

        [Header("Ending Thresholds")]
        [Tooltip("Inclusive upper bound for Destruction.")]
        public int destructionMax = -5;
        [Tooltip("Inclusive upper bound for Exile (above destructionMax).")]
        public int exileMax = 0;
        [Tooltip("Inclusive upper bound for Diplomacy (above exileMax). Alliance is anything higher.")]
        public int diplomacyMax = 6;

        public EndingType EvaluateEnding(int score)
        {
            if (score <= destructionMax) return EndingType.Destruction;
            if (score <= exileMax) return EndingType.Exile;
            if (score <= diplomacyMax) return EndingType.Diplomacy;
            return EndingType.Alliance;
        }
    }
}
