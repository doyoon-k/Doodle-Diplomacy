using System.Collections.Generic;
using UnityEngine;

namespace DoodleDiplomacy.Data
{
    [CreateAssetMenu(fileName = "DialogueSequence", menuName = "DoodleDiplomacy/Dialogue Sequence")]
    public class DialogueSequence : ScriptableObject
    {
        [Tooltip("Stable id used to identify this dialogue sequence.")]
        public string sequenceID;
        [Tooltip("Designer note describing when or why this sequence plays.")]
        [TextArea(2, 5)] public string contextNote;
        [Tooltip("Ordered dialogue lines played by this sequence.")]
        public List<DialogueLineData> lines = new List<DialogueLineData>();
    }
}
