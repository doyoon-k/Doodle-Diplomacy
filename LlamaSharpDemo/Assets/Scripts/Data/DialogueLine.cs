using System;
using UnityEngine;

namespace DoodleDiplomacy.Data
{
    public enum DisplayMode { WorldSpace, Subtitle }
    public enum AdvanceType { Click, Auto, Wait }

    [Serializable]
    public class DialogueLineData
    {
        [Tooltip("Character id used to resolve speaker name, portrait, or world-space anchor.")]
        public string characterID;
        [Tooltip("비어 있으면 characterID를 그대로 표시합니다.")]
        public string speakerLocalizationKey;
        [TextArea(2, 5)]
        [Tooltip("Source dialogue text used when no localization key is resolved.")]
        public string text;
        [Tooltip("비어 있으면 text를 그대로 표시합니다.")]
        public string localizationKey;
        [Tooltip("Where this line is displayed: world-space bubble or subtitle UI.")]
        public DisplayMode displayMode;
        [Tooltip("비어 있으면 표정 변경 없음")]
        public string portraitID;
        [Tooltip("How the line advances: click, automatic delay, or wait.")]
        public AdvanceType advanceType;
        [Tooltip("AdvanceType이 Wait일 때 대기 시간(초)")]
        public float autoDelay = 2f;
    }
}
