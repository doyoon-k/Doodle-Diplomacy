using UnityEngine;

namespace DoodleDiplomacy.Data
{
    [CreateAssetMenu(fileName = "EndingData", menuName = "DoodleDiplomacy/Ending Data")]
    public class EndingData : ScriptableObject
    {
        [Tooltip("Ending category selected by ScoreConfig thresholds.")]
        public EndingType endingType;
        [Tooltip("Title displayed on the ending screen.")]
        public string title;
        [TextArea(3, 8)]
        [Tooltip("Description text displayed on the ending screen.")]
        public string description;
        [Tooltip("Background sprite displayed for this ending.")]
        public Sprite backgroundImage;
    }
}
