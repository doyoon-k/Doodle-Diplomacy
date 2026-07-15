using UnityEngine;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    [CreateAssetMenu(
        fileName = "FirstContactPresentationSettings",
        menuName = "DoodleDiplomacy/First Contact/Presentation Settings")]
    public sealed class FirstContactPresentationSettings : ScriptableObject
    {
        [Header("Terminal")]
        [Tooltip("태블릿 링크를 연 뒤 드로잉 화면으로 이동하기 전까지의 대기 시간입니다.")]
        [Min(0f)] public float tabletLinkOpenHoldSeconds = 0.45f;
        [Tooltip("표본 검증이 통과된 뒤 수신 채널 개방 상태를 유지하는 시간입니다.")]
        [Min(0f)] public float cardRevealDelay = 0.35f;

        [Header("Drawing")]
        [Tooltip("분석이 너무 빨리 끝나더라도 ANALYZING 상태를 유지하는 최소 시간입니다.")]
        [Min(0f)] public float scanMinimumSeconds = 1.2f;
        [Tooltip("라벨 검증이 통과된 뒤 결과를 보이기 전의 짧은 대기 시간입니다.")]
        [Min(0f)] public float labelRevealDelay = 0.15f;

        [Header("Semantic Map")]
        [Tooltip("의미 공간 맵의 레이아웃, 라벨, 색상, 노드와 링크 표현을 조정하는 스타일 에셋입니다.")]
        public FirstContactSemanticMapStyle semanticMapStyle;

        [Header("Probe Preview")]
        public Vector2 probeReviewAnchorMin = new(0.12f, 0.5f);
        public Vector2 probeReviewAnchorMax = new(0.88f, 0.93f);
        [Range(0f, 1f)] public float probeReviewTextTopInset = 0.52f;
        public Vector2 probeDispatchAnchorMin = new(0.54f, 0.36f);
        public Vector2 probeDispatchAnchorMax = new(0.93f, 0.76f);
        [Range(0f, 1f)] public float probeDispatchTextTopInset = 0f;

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
