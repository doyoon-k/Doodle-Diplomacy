using UnityEngine;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    [CreateAssetMenu(
        fileName = "FirstContactModeConfig",
        menuName = "DoodleDiplomacy/First Contact/Mode Config")]
    public sealed class FirstContactModeConfig : ScriptableObject
    {
        [Tooltip("터미널 표시, 카드 공개, 분석 대기 등 UX 연출 시간 설정입니다.")]
        public FirstContactPresentationSettings presentationSettings;
        [Tooltip("임베딩, UNKNOWN 해석 임계값, 군집, 파형 생성 설정입니다.")]
        public FirstContactSemanticSettings semanticSettings;
        [Tooltip("그림 라벨링 파이프라인과 라벨 거절 규칙 설정입니다.")]
        public FirstContactVlmSettings vlmSettings;
        [Tooltip("외계 질문 생성 파이프라인, 검증 규칙, fallback 질문 설정입니다.")]
        public FirstContactQuestionSettings questionSettings;
        [Tooltip("First Contact 모드의 디버그 로그와 터미널 디버그 표시 설정입니다.")]
        public FirstContactDebugSettings debugSettings;
    }
}
