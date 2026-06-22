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
        [Tooltip("표본 검증이 통과된 뒤, 반응 채널 개방 상태를 짧게 보여주는 시간입니다.")]
        [Min(0f)] public float labelRevealDelay = 0.15f;

        [Header("Probe Preview")]
        [Tooltip("표본 라벨 입력 화면에서 그림 프리뷰가 차지하는 터미널 화면 앵커 최소값입니다.")]
        public Vector2 probeReviewAnchorMin = new(0.12f, 0.5f);
        [Tooltip("표본 라벨 입력 화면에서 그림 프리뷰가 차지하는 터미널 화면 앵커 최대값입니다.")]
        public Vector2 probeReviewAnchorMax = new(0.88f, 0.93f);
        [Tooltip("표본 라벨 입력 화면에서 터미널 텍스트가 프리뷰 아래로 밀리는 비율입니다.")]
        [Range(0f, 1f)] public float probeReviewTextTopInset = 0.52f;
        [Tooltip("그림 제출 후 스캔/송신 화면에서 그림 프리뷰가 차지하는 터미널 화면 앵커 최소값입니다.")]
        public Vector2 probeDispatchAnchorMin = new(0.54f, 0.36f);
        [Tooltip("그림 제출 후 스캔/송신 화면에서 그림 프리뷰가 차지하는 터미널 화면 앵커 최대값입니다.")]
        public Vector2 probeDispatchAnchorMax = new(0.93f, 0.76f);
        [Tooltip("그림 제출 후 스캔/송신 화면에서 터미널 텍스트가 프리뷰 아래로 밀리는 비율입니다. 오른쪽 배치에서는 0을 권장합니다.")]
        [Range(0f, 1f)] public float probeDispatchTextTopInset = 0f;

        [Header("Answer")]
        [Tooltip("답변 그림을 외계인에게 송신했다는 화면을 유지하는 시간입니다.")]
        [Min(0f)] public float answerTransmitHoldSeconds = 1.5f;
        [Tooltip("답변 송신 후 다음 외계 질문으로 넘어가기 전까지 기다리는 시간입니다.")]
        [Min(0f)] public float nextQuestionDelay = 1.2f;

        private void OnValidate()
        {
            ClampPreviewAnchors(ref probeReviewAnchorMin, ref probeReviewAnchorMax);
            ClampPreviewAnchors(ref probeDispatchAnchorMin, ref probeDispatchAnchorMax);
            probeReviewTextTopInset = Mathf.Clamp01(probeReviewTextTopInset);
            probeDispatchTextTopInset = Mathf.Clamp01(probeDispatchTextTopInset);
        }

        private static void ClampPreviewAnchors(ref Vector2 min, ref Vector2 max)
        {
            min = new Vector2(Mathf.Clamp01(min.x), Mathf.Clamp01(min.y));
            max = new Vector2(Mathf.Clamp01(max.x), Mathf.Clamp01(max.y));
            max.x = Mathf.Max(max.x, min.x + 0.05f);
            max.y = Mathf.Max(max.y, min.y + 0.05f);
            max = new Vector2(Mathf.Clamp01(max.x), Mathf.Clamp01(max.y));
        }
    }
}
