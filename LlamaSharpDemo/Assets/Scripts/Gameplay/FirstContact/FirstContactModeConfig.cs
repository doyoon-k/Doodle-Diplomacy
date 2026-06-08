using UnityEngine;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    [CreateAssetMenu(
        fileName = "FirstContactModeConfig",
        menuName = "DoodleDiplomacy/First Contact/Mode Config")]
    public sealed class FirstContactModeConfig : ScriptableObject
    {
        public FirstContactPresentationSettings presentationSettings;
        public FirstContactSemanticSettings semanticSettings;
        public FirstContactVlmSettings vlmSettings;
        public FirstContactQuestionSettings questionSettings;
        public FirstContactDebugSettings debugSettings;
    }
}
