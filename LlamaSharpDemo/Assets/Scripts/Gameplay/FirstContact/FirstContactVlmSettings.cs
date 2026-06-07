using UnityEngine;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    [CreateAssetMenu(
        fileName = "FirstContactVlmSettings",
        menuName = "DoodleDiplomacy/First Contact/VLM Settings")]
    public sealed class FirstContactVlmSettings : ScriptableObject
    {
        [Tooltip("Optional first-contact classifier pipeline. If empty, the mode reuses IRoundAiGateway.ClassifyVisualStimulus.")]
        public PromptPipelineAsset visualClassifierPipeline;
        public string imageStateKey = "reference_image";
        public bool useLocalizedLabelForDisplay = true;
        public bool rejectBlank = true;
        public bool rejectWrittenText = true;
        public bool rejectActionOrScene = true;
        public bool rejectMultipleObjects = true;
    }
}
