using UnityEngine;

namespace DoodleDiplomacy.Data
{
    [CreateAssetMenu(fileName = "CharacterProfile", menuName = "DoodleDiplomacy/Character Profile")]
    public class CharacterProfile : ScriptableObject
    {
        [Tooltip("Stable character id referenced by dialogue lines.")]
        public string characterID;
        [Tooltip("Localized or display-facing character name.")]
        public string displayName;
        [Tooltip("3D 모델 프리팹 참조")]
        public GameObject modelPrefab;
        [Tooltip("Portrait set used for this character's expression ids.")]
        public PortraitSet portraitSet;
    }
}
