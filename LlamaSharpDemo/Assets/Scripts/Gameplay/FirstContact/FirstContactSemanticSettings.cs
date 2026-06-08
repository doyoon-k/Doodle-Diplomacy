using System;
using UnityEngine;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    [CreateAssetMenu(
        fileName = "FirstContactSemanticSettings",
        menuName = "DoodleDiplomacy/First Contact/Semantic Settings")]
    public sealed class FirstContactSemanticSettings : ScriptableObject
    {
        [Header("Embedding")]
        [Tooltip("First Contact 번역기에서 라벨과 앵커 문장을 임베딩할 때 사용할 프로필입니다.")]
        public LlmEmbeddingProfile embeddingProfile;
        [Tooltip("켜면 같은 라벨/앵커의 임베딩 결과를 세션 중 재사용합니다.")]
        public bool cacheEmbeddings = true;
        [Tooltip("켜면 메모리에 저장하기 전에 임베딩 벡터를 정규화합니다.")]
        public bool normalizeVectorsInMemory = true;
        [Tooltip("여러 문장을 한 번에 임베딩할 때 한 배치에 넣을 최대 개수입니다.")]
        [Min(1)] public int maxBatchSize = 32;
        [Tooltip("켜면 임베딩 생성에 실패한 그림 카드는 실패 처리하고 다시 그리게 합니다.")]
        public bool failCardWhenEmbeddingMissing;

        [Header("Unknown Resolution")]
        [Tooltip("그림과 미해석 단어의 의미 유사도가 이 값 이상이면 HINT 단계로 엽니다.")]
        [Range(-1f, 1f)] public float hintThreshold = 0.46f;
        [Tooltip("그림과 미해석 단어의 의미 유사도가 이 값 이상이면 PARTIAL 단계로 엽니다.")]
        [Range(-1f, 1f)] public float partialThreshold = 0.58f;
        [Tooltip("그림과 미해석 단어의 의미 유사도가 이 값 이상이면 SOLVED 단계로 확정합니다.")]
        [Range(-1f, 1f)] public float solvedThreshold = 0.73f;
        [Tooltip("개별 앵커와의 점수 대신 앵커 중심점 점수를 사용할 때 적용하는 가중치입니다.")]
        [Range(0f, 1f)] public float centroidWeight = 0.92f;

        [Header("Clusters")]
        [Tooltip("새 그림 카드가 기존 군집에 합류하려면 기존 군집 중심과 이 값 이상의 유사도가 필요합니다.")]
        [Range(-1f, 1f)] public float clusterJoinThreshold = 0.62f;
        [Tooltip("안정화된 군집이 새 질문의 미해석 단어와 이 값 이상 가까우면 자동으로 PARTIAL 단계까지 엽니다.")]
        [Range(-1f, 1f)] public float clusterAutoPartialThreshold = 0.58f;
        [Tooltip("군집이 안정화되기 위해 필요한 최소 카드 수입니다.")]
        [Min(2)] public int minClusterMembers = 3;
        [Tooltip("군집이 안정화되기 위해 필요한 최소 응집도입니다.")]
        [Range(-1f, 1f)] public float minClusterCohesion = 0.55f;

        [Header("Semantic Map")]
        [Tooltip("켜면 그림 제출 후 터미널에 세션 기반 의미공간 맵과 공명 피드백을 표시합니다.")]
        public bool showSemanticMapFeedback = true;
        [Tooltip("의미공간 맵에 표시할 최근 그림 카드의 최대 개수입니다.")]
        [Min(1)] public int semanticMapMaxCards = 18;
        [Tooltip("새 노드가 추가될 때 의미공간 배치를 안정화하기 위해 수행할 반복 계산 횟수입니다.")]
        [Min(1)] public int semanticMapLayoutIterations = 28;
        [Tooltip("두 의미 노드가 이 유사도 이상 가까울 때 서로 끌어당깁니다.")]
        [Range(-1f, 1f)] public float semanticMapAttractionThreshold = 0.36f;
        [Tooltip("의미가 가까운 노드끼리 서로 끌어당기는 힘입니다.")]
        [Range(0f, 1f)] public float semanticMapAttractionStrength = 0.08f;
        [Tooltip("모든 노드가 서로 겹치지 않도록 밀어내는 힘입니다.")]
        [Range(0f, 1f)] public float semanticMapRepulsionStrength = 0.028f;
        [Tooltip("기존 배치가 갑자기 크게 흔들리지 않도록 이동 속도를 줄이는 비율입니다.")]
        [Range(0f, 1f)] public float semanticMapDamping = 0.72f;
        [Tooltip("한 번의 배치 반복에서 노드가 이동할 수 있는 최대 거리입니다.")]
        [Range(0.005f, 0.25f)] public float semanticMapMaxStep = 0.045f;

        [Header("Waveform")]
        [Tooltip("의미 임베딩이 Day1 스타일 파형 모양에 영향을 주는 비율입니다.")]
        [Range(0f, 1f)] public float waveformSemanticInfluence = 0.9f;
        [Tooltip("같은 의미라도 세션마다 파형이 완전히 같지 않도록 넣는 작은 흔들림입니다.")]
        [Range(0f, 0.25f)] public float waveformSessionJitter = 0.04f;
        [Tooltip("파형 생성에 사용할 임베딩 특징 개수입니다.")]
        [Min(8)] public int waveformFeatureCount = 16;
        [Tooltip("임베딩을 파형 특징으로 투영할 때 사용하는 고정 시드입니다.")]
        public int waveformProjectionSeed = 17331;

        private void OnValidate()
        {
            maxBatchSize = Math.Max(1, maxBatchSize);
            minClusterMembers = Math.Max(2, minClusterMembers);
            semanticMapMaxCards = Math.Max(1, semanticMapMaxCards);
            semanticMapLayoutIterations = Math.Max(1, semanticMapLayoutIterations);
            semanticMapAttractionStrength = Mathf.Clamp01(semanticMapAttractionStrength);
            semanticMapRepulsionStrength = Mathf.Clamp01(semanticMapRepulsionStrength);
            semanticMapDamping = Mathf.Clamp01(semanticMapDamping);
            semanticMapMaxStep = Mathf.Clamp(semanticMapMaxStep, 0.005f, 0.25f);
            waveformFeatureCount = Math.Max(8, waveformFeatureCount);
            centroidWeight = Mathf.Clamp01(centroidWeight);
        }
    }
}
