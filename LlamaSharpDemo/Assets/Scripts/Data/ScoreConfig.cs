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
        [Tooltip("이 값 이하면 멸망")]
        public int destructionMax = -9;
        [Tooltip("이 값 이하면 추방 (destructionMax 초과)")]
        public int exileMax = 0;
        [Tooltip("이 값 이하면 수교 (exileMax 초과), 초과하면 동맹")]
        public int diplomacyMax = 12;

        public EndingType EvaluateEnding(int score)
        {
            if (score <= destructionMax) return EndingType.Destruction;
            if (score <= exileMax)      return EndingType.Exile;
            if (score <= diplomacyMax)  return EndingType.Diplomacy;
            return EndingType.Alliance;
        }
    }
}
