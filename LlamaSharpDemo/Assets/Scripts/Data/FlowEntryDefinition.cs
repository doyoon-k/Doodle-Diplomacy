using UnityEngine;

namespace DoodleDiplomacy.Data
{
    [CreateAssetMenu(fileName = "FlowEntryDefinition", menuName = "DoodleDiplomacy/Game Flow/Flow Entry")]
    public sealed class FlowEntryDefinition : ScriptableObject
    {
        [Tooltip("Stable id for this flow entry.")]
        public string entryId = "flow-entry";
        [Tooltip("High-level type of flow entry.")]
        public FlowEntryType entryType = FlowEntryType.Gameplay;
        [Tooltip("Optional story day marker. Use 0 when this entry is not tied to a calendar day.")]
        [Min(0)] public int storyDay;
        [Tooltip("Optional free-form tag for special modes such as combat, memory, dream, briefing, or experiment.")]
        public string entryTag;
        [Tooltip("Scene name loaded for this flow entry.")]
        public string sceneName;
        [Tooltip("Unload the previously loaded flow scene before or after loading this entry.")]
        public bool unloadPreviousScene = true;
        [Tooltip("Automatically enter the scene's default gameplay mode after loading.")]
        public bool autoStartSession = true;
        [Tooltip("Start the gameplay session with its intro sequence enabled.")]
        public bool startSessionWithIntro = true;
    }
}
