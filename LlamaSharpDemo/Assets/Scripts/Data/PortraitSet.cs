using System;
using System.Collections.Generic;
using UnityEngine;

namespace DoodleDiplomacy.Data
{
    [Serializable]
    public class PortraitEntry
    {
        public string emotionID;
        public Texture2D texture;
    }

    [CreateAssetMenu(fileName = "PortraitSet", menuName = "DoodleDiplomacy/Portrait Set")]
    public class PortraitSet : ScriptableObject
    {
        public List<PortraitEntry> entries = new List<PortraitEntry>();

        public Texture2D GetPortrait(string emotionID)
        {
            foreach (var entry in entries)
            {
                if (string.Equals(entry.emotionID, emotionID, System.StringComparison.Ordinal))
                    return entry.texture;
            }
            Debug.LogWarning($"[PortraitSet] 알 수 없는 emotionID: '{emotionID}'", this);
            return null;
        }
    }
}
