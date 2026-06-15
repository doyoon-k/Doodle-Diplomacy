using UnityEngine;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    [CreateAssetMenu(
        fileName = "FirstContactPresentationSettings",
        menuName = "DoodleDiplomacy/First Contact/Presentation Settings")]
    public sealed class FirstContactPresentationSettings : ScriptableObject
    {
        [Header("Terminal")]
        [Tooltip("새 외계 질문 수신이 시작된 뒤, 터미널에 질문을 표시하기 전까지 기다리는 시간입니다.")]
        [Min(0f)] public float questionReceiveDelay = 0.35f;
        [Tooltip("켜면 외계 질문을 단어별 수신 스트림으로 먼저 보여줍니다.")]
        public bool showIncomingTokenStream = true;
        [Tooltip("일반 외계어 토큰이 수신 화면에 머무는 시간입니다.")]
        [Min(0f)] public float incomingTokenHoldSeconds = 0.45f;
        [Tooltip("미해석 UNKNOWN 토큰이 수신 화면에 머무는 시간입니다.")]
        [Min(0f)] public float incomingUnknownTokenHoldSeconds = 1.35f;
        [Tooltip("켜면 터미널 타이핑 연출이 끝난 뒤에만 플레이어 선택 버튼을 표시합니다.")]
        public bool waitForTerminalTypingBeforeActions = true;
        [Tooltip("질문이 처음 표시된 뒤, 플레이어가 읽을 수 있도록 선택 버튼을 띄우기 전 추가로 유지하는 시간입니다.")]
        [Min(0f)] public float questionReadHoldSeconds = 0.6f;
        [Tooltip("자동 해석이나 그림 해석으로 토큰이 갱신된 뒤, 갱신된 문장을 읽게 하는 시간입니다.")]
        [Min(0f)] public float updatedQuestionReadHoldSeconds = 0.75f;
        [Tooltip("토큰 갱신, 군집 표시 같은 번역기 상태 변화 사이에 넣는 짧은 간격입니다.")]
        [Min(0f)] public float tokenUpdateDelay = 0.25f;
        [Tooltip("터미널 선택지를 키보드로 고른 뒤, 입력 echo를 보여주고 태블릿 링크 메시지로 넘어가기 전까지 기다리는 시간입니다.")]
        [Min(0f)] public float choiceConfirmEchoSeconds = 0.2f;
        [Tooltip("태블릿 링크가 열렸다는 터미널 메시지를 보여준 뒤, 태블릿 카메라로 넘어가기 전까지 기다리는 시간입니다.")]
        [Min(0f)] public float tabletLinkOpenHoldSeconds = 0.45f;
        [Tooltip("그림을 확정한 뒤, 의미 카드가 터미널에 나타나기 전까지 기다리는 시간입니다.")]
        [Min(0f)] public float cardRevealDelay = 0.35f;

        [Header("Drawing")]
        [Tooltip("VLM 라벨링이 빨리 끝나도 최소한 분석 중 상태로 유지하는 시간입니다.")]
        [Min(0f)] public float scanMinimumSeconds = 1.2f;
        [Tooltip("분석이 끝난 뒤, 터미널에 인식 라벨 확인 화면을 보여주기 전까지 기다리는 시간입니다.")]
        [Min(0f)] public float labelRevealDelay = 0.15f;

        [Header("Answer")]
        [Tooltip("답변 그림을 외계인에게 송신했다는 화면을 유지하는 시간입니다.")]
        [Min(0f)] public float answerTransmitHoldSeconds = 1.5f;
        [Tooltip("답변 송신 후 다음 외계 질문으로 넘어가기 전까지 기다리는 시간입니다.")]
        [Min(0f)] public float nextQuestionDelay = 1.2f;
    }
}
