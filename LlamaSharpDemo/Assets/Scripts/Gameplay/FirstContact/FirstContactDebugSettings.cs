using UnityEngine;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    [CreateAssetMenu(
        fileName = "FirstContactDebugSettings",
        menuName = "DoodleDiplomacy/First Contact/Debug Settings")]
    public sealed class FirstContactDebugSettings : ScriptableObject
    {
        public bool logQuestionProvider = true;
        public bool logSimilarityScores = true;
        public bool logClusterUpdates = true;
        public bool showScoresOnTerminal;
        public bool showQuestionFallbackReason;
    }
}
