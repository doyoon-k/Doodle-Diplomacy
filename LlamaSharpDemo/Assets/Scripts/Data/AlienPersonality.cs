using UnityEngine;

namespace DoodleDiplomacy.Data
{
    public enum AlienPersonalityArchetype
    {
        Conqueror,
        Guardian,
        Harmonizer,
        Perfectionist,
        Trickster,
        Nurturer
    }

    [CreateAssetMenu(fileName = "AlienPersonality", menuName = "DoodleDiplomacy/Alien Personality")]
    public class AlienPersonality : ScriptableObject
    {
        [Tooltip("Alien archetype used for judgment and flavor.")]
        public AlienPersonalityArchetype archetype;

        [TextArea(2, 5)]
        [Tooltip("Designer notes.")]
        public string description;
    }
}
