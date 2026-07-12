using System;
using UnityEngine;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    [CreateAssetMenu(
        fileName = "FirstContactVlmSettings",
        menuName = "DoodleDiplomacy/First Contact/VLM Settings")]
    public sealed class FirstContactVlmSettings : ScriptableObject
    {
        [Tooltip("플레이어가 입력한 라벨을 내부 영어 의미 라벨로 정규화하고, 단일 사물 라벨인지 검사하는 파이프라인입니다.")]
        public PromptPipelineAsset probeLabelPipeline;
        [Tooltip("First Contact 전용 시각 표본 검수 파이프라인입니다. 의미 라벨을 만들지 않고 규약/라벨 일치 여부만 확인합니다.")]
        public PromptPipelineAsset probeValidationPipeline;
        [Tooltip("부트스트랩 CATEGORY에 표본 라벨이 명백히 맞지 않는지 판단하는 파이프라인입니다. 임베딩 점수는 보조 신호로만 사용합니다.")]
        public PromptPipelineAsset bootstrapCategoryFitPipeline;
        [Tooltip("VLM 파이프라인에 그림 이미지를 넣을 때 사용할 state key입니다.")]
        public string imageStateKey = "reference_image";
        [Tooltip("PNG 캡처가 일시적으로 실패했을 때 내부적으로 다시 시도할 횟수입니다. 플레이어에게는 표시하지 않습니다.")]
        [Min(0)] public int captureRetryCount = 2;
        [Tooltip("VLM validator가 일시적으로 실패했을 때 내부적으로 다시 시도할 횟수입니다. 플레이어에게는 표시하지 않습니다.")]
        [Min(0)] public int validatorRetryCount = 1;
        [Tooltip("기술적 재시도 사이에 기다릴 시간입니다.")]
        [Min(0f)] public float technicalRetryDelaySeconds = 0.15f;
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
            validatorRetryCount = Math.Max(0, validatorRetryCount);
            technicalRetryDelaySeconds = Math.Max(0f, technicalRetryDelaySeconds);
        }
    }
}
