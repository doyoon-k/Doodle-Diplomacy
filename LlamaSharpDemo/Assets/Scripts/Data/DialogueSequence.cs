using System.Collections.Generic;
using UnityEngine;

namespace DoodleDiplomacy.Data
{
    [CreateAssetMenu(fileName = "DialogueSequence", menuName = "DoodleDiplomacy/Dialogue Sequence")]
    public class DialogueSequence : ScriptableObject
    {
        public string sequenceID;
        public List<DialogueLineData> lines = new List<DialogueLineData>();
    }
}
