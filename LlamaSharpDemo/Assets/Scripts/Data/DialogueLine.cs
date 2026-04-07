using System;
using UnityEngine;

namespace DoodleDiplomacy.Data
{
    public enum DisplayMode { WorldSpace, Subtitle }
    public enum AdvanceType { Click, Auto, Wait }

    [Serializable]
    public class DialogueLineData
    {
        public string characterID;
        [TextArea(2, 5)]
        public string text;
        public DisplayMode displayMode;
        [Tooltip("비어 있으면 표정 변경 없음")]
        public string portraitID;
        public AdvanceType advanceType;
        [Tooltip("AdvanceType이 Wait일 때 대기 시간(초)")]
        public float autoDelay = 2f;
    }
}
