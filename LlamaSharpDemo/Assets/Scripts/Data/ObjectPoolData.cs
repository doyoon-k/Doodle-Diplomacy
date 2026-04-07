using System;
using System.Collections.Generic;
using UnityEngine;

namespace DoodleDiplomacy.Data
{
    [Serializable]
    public class ObjectEntry
    {
        public string objectName;
        [TextArea(2, 4)]
        [Tooltip("Stable Diffusion 생성용 프롬프트")]
        public string prompt;
    }

    [Serializable]
    public class ObjectPair
    {
        [Tooltip("entries 리스트의 인덱스 A")]
        public int entryA;
        [Tooltip("entries 리스트의 인덱스 B")]
        public int entryB;
    }

    [CreateAssetMenu(fileName = "ObjectPoolData", menuName = "DoodleDiplomacy/Object Pool Data")]
    public class ObjectPoolData : ScriptableObject
    {
        public List<ObjectEntry> entries = new List<ObjectEntry>();
        [Tooltip("선택적 프리셋 조합")]
        public List<ObjectPair> presetPairs = new List<ObjectPair>();
    }
}
