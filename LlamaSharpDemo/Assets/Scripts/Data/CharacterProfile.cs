using UnityEngine;

namespace DoodleDiplomacy.Data
{
    [CreateAssetMenu(fileName = "CharacterProfile", menuName = "DoodleDiplomacy/Character Profile")]
    public class CharacterProfile : ScriptableObject
    {
        public string characterID;
        public string displayName;
        [Tooltip("3D 모델 프리팹 참조")]
        public GameObject modelPrefab;
        public PortraitSet portraitSet;
    }
}
