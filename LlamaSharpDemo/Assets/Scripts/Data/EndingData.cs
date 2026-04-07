using UnityEngine;

namespace DoodleDiplomacy.Data
{
    [CreateAssetMenu(fileName = "EndingData", menuName = "DoodleDiplomacy/Ending Data")]
    public class EndingData : ScriptableObject
    {
        public EndingType endingType;
        public string title;
        [TextArea(3, 8)]
        public string description;
        public Sprite backgroundImage;
    }
}
