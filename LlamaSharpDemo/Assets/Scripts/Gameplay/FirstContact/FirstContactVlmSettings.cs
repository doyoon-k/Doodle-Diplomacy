using System;
using UnityEngine;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    [CreateAssetMenu(
        fileName = "FirstContactVlmSettings",
        menuName = "DoodleDiplomacy/First Contact/VLM Settings")]
    public sealed class FirstContactVlmSettings : ScriptableObject
    {
        [Tooltip("First Contact 전용 그림 라벨링 파이프라인입니다. 비어 있으면 Day1에서 쓰는 IRoundAiGateway.ClassifyVisualStimulus 경로를 재사용합니다.")]
        public PromptPipelineAsset visualClassifierPipeline;
        [Tooltip("VLM 파이프라인에 그림 이미지를 넣을 때 사용할 state key입니다.")]
        public string imageStateKey = "reference_image";
        [Tooltip("PNG 캡처가 일시적으로 실패했을 때 내부적으로 다시 시도할 횟수입니다. 플레이어에게는 표시하지 않습니다.")]
        [Min(0)] public int captureRetryCount = 2;
        [Tooltip("VLM/classifier가 일시적으로 실패했을 때 내부적으로 다시 시도할 횟수입니다. 플레이어에게는 표시하지 않습니다.")]
        [Min(0)] public int classifierRetryCount = 2;
        [Tooltip("기술적 재시도 사이에 기다릴 시간입니다.")]
        [Min(0f)] public float technicalRetryDelaySeconds = 0.15f;
        [Tooltip("켜면 VLM 결과의 localizedLabel을 화면 표시용 라벨로 우선 사용합니다.")]
        public bool useLocalizedLabelForDisplay = true;
        [Tooltip("켜면 빈 그림이나 알아볼 수 없는 그림 라벨을 거절하고 다시 그리게 합니다.")]
        public bool rejectBlank = true;
        [Tooltip("켜면 글자나 문장처럼 텍스트를 그린 결과를 거절합니다.")]
        public bool rejectWrittenText = true;
        [Tooltip("켜면 행동, 장면, 추상 설명처럼 사물 하나가 아닌 라벨을 거절합니다.")]
        public bool rejectActionOrScene = true;
        [Tooltip("켜면 여러 사물이 함께 그려진 결과를 거절하고 하나의 사물만 그리게 합니다.")]
        public bool rejectMultipleObjects = true;

        private void OnValidate()
        {
            captureRetryCount = Math.Max(0, captureRetryCount);
            classifierRetryCount = Math.Max(0, classifierRetryCount);
            technicalRetryDelaySeconds = Math.Max(0f, technicalRetryDelaySeconds);
        }
    }
}
