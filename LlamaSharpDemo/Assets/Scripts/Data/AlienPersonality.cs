using UnityEngine;

namespace DoodleDiplomacy.Data
{
    [CreateAssetMenu(fileName = "AlienPersonality", menuName = "DoodleDiplomacy/Alien Personality")]
    public class AlienPersonality : ScriptableObject
    {
        [Range(-1f, 1f)]
        [Tooltip("-1 = 협력, 1 = 지배")]
        public float cooperationVsDomination;

        [Range(-1f, 1f)]
        [Tooltip("-1 = 효율, 1 = 공감")]
        public float efficiencyVsEmpathy;

        [TextArea(2, 5)]
        [Tooltip("에디터용 메모")]
        public string description;
    }
}
