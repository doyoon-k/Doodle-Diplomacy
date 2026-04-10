using UnityEngine;

namespace DoodleDiplomacy.Data
{
    [CreateAssetMenu(fileName = "AlienPersonality", menuName = "DoodleDiplomacy/Alien Personality")]
    public class AlienPersonality : ScriptableObject
    {
        [Tooltip("Archetype label injected into the LLM prompt (e.g. Conqueror, Guardian).")]
        public string label;

        [TextArea(2, 3)]
        [Tooltip("Short phrase describing core values. Injected into the LLM prompt.")]
        public string coreValues;

        [TextArea(2, 3)]
        [Tooltip("What this archetype likes or values. Injected into the LLM prompt.")]
        public string likes;

        [TextArea(2, 3)]
        [Tooltip("What this archetype dislikes. Injected into the LLM prompt.")]
        public string dislikes;

        [TextArea(2, 3)]
        [Tooltip("One-sentence priority reminder used as a fallback judgment line.")]
        public string fallbackJudgmentPriority;

        [TextArea(2, 5)]
        [Tooltip("Optional designer note appended at the end of the LLM prompt.")]
        public string description;
    }
}
