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
        [Tooltip("비어 있으면 characterID를 그대로 표시합니다.")]
        public string speakerLocalizationKey;
        [TextArea(2, 5)]
        public string text;
        [Tooltip("비어 있으면 text를 그대로 표시합니다.")]
        public string localizationKey;
        public DisplayMode displayMode;
        [Tooltip("비어 있으면 표정 변경 없음")]
        public string portraitID;
        public AdvanceType advanceType;
        [Tooltip("AdvanceType이 Wait일 때 대기 시간(초)")]
        public float autoDelay = 2f;
    }
}
