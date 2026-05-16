using UnityEngine;

namespace DoodleDiplomacy.Data
{
    [CreateAssetMenu(fileName = "FlowEntryDefinition", menuName = "DoodleDiplomacy/Game Flow/Flow Entry")]
    public sealed class FlowEntryDefinition : ScriptableObject
    {
        public string entryId = "flow-entry";
        public FlowEntryType entryType = FlowEntryType.Gameplay;
        [Tooltip("Optional story day marker. Use 0 when this entry is not tied to a calendar day.")]
        [Min(0)] public int storyDay;
        [Tooltip("Optional free-form tag for special modes such as combat, memory, dream, briefing, or experiment.")]
        public string entryTag;
        public string sceneName;
        public bool unloadPreviousScene = true;
        public bool autoStartSession = true;
        public bool startSessionWithIntro = true;
    }
}
