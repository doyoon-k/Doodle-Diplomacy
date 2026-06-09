using UnityEngine;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    [CreateAssetMenu(
        fileName = "FirstContactDebugSettings",
        menuName = "DoodleDiplomacy/First Contact/Debug Settings")]
    public sealed class FirstContactDebugSettings : ScriptableObject
    {
        [Tooltip("켜면 질문 파이프라인 실패/성공 및 fallback 사용 이유를 콘솔에 출력합니다.")]
        public bool logQuestionProvider = true;
        [Tooltip("켜면 그림 카드와 미해석 단어 사이의 유사도 점수를 콘솔에 출력합니다.")]
        public bool logSimilarityScores = true;
        [Tooltip("켜면 카드가 어떤 군집에 합류했는지와 군집 안정화 상태를 콘솔에 출력합니다.")]
        public bool logClusterUpdates = true;
        [Tooltip("켜면 디버그용 점수 정보를 터미널 표시에도 포함합니다.")]
        public bool showScoresOnTerminal;
        [Tooltip("켜면 파이프라인 질문 대신 fallback 질문을 사용한 이유를 터미널에 표시합니다.")]
        public bool showQuestionFallbackReason;
    }
}
