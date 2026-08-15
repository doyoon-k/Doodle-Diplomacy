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
        [Tooltip("First Contact 번역기에서 그림 라벨과 숨겨진 정답 개념을 임베딩할 때 사용할 프로필입니다.")]
        public LlmEmbeddingProfile embeddingProfile;
        [Tooltip("켜면 같은 라벨/정답 개념의 임베딩 결과를 세션 중 재사용합니다.")]
        public bool cacheEmbeddings = true;
        [Tooltip("켜면 메모리에 저장하기 전에 임베딩 벡터를 정규화합니다.")]
        public bool normalizeVectorsInMemory = true;
        [Tooltip("여러 문장을 한 번에 임베딩할 때 한 배치에 넣을 최대 개수입니다.")]
        [Min(1)] public int maxBatchSize = 32;
        [Tooltip("켜면 임베딩 생성에 실패한 그림 카드는 실패 처리하고 다시 그리게 합니다.")]
        public bool failCardWhenEmbeddingMissing;

        [Header("Unknown Resolution")]
        [Tooltip("그림과 미해석 단어의 의미 유사도가 이 값 이상이면 HINT 단계로 엽니다.")]
        [Range(-1f, 1f)] public float hintThreshold = 0.6f;
        [Tooltip("그림과 미해석 단어의 의미 유사도가 이 값 이상이면 PARTIAL 단계로 엽니다.")]
        [Range(-1f, 1f)] public float partialThreshold = 0.68f;
        [Tooltip("그림과 미해석 단어의 의미 유사도가 이 값 이상이면 SOLVED 단계로 확정합니다.")]
        [Range(-1f, 1f)] public float solvedThreshold = 0.78f;

        [Header("Clusters")]
        [Tooltip("군집이 안정화되기 위해 필요한 최소 카드 수입니다.")]
        [Min(2)] public int minClusterMembers = 3;

        // Kept serialized for existing assets and save migrations. Explicit LLM JOIN/REJECT
        // decisions supersede the old embedding-graph thresholds.
        [HideInInspector] public float clusterJoinThreshold = 0.75f;
        [HideInInspector] public float clusterAutoPartialThreshold = 0.68f;
        [HideInInspector] public int clusterNeighborCount = 2;
        [HideInInspector] public float minClusterCohesion = 0.72f;
        [HideInInspector] public float minClusterPairwiseSimilarity = 0.75f;

        [Header("LLM Group Formation")]
        [Tooltip("새 불일치 표본 하나당 임베딩으로 검색한 뒤 LLM 판정 대상으로 유지할 GROUP 수입니다.")]
        [Range(1, 3)] public int semanticGroupCandidateCount = 3;
        [Tooltip("GROUP 하나를 판정할 때 LLM에 전달할 기존 대표 MEANING의 최대 개수입니다.")]
        [Range(2, 8)] public int semanticGroupRepresentativeLimit = 6;
        [Tooltip("첫 JOIN 후보와 이 값 이내로 가까운 다음 후보도 검사하여 복수 JOIN을 감지합니다.")]
        [Range(0f, 0.25f)] public float semanticGroupJoinAmbiguityMargin = 0.04f;

        [Header("Bootstrap Category Training")]
        [Tooltip("튜토리얼 카테고리 하나가 안정화되기 위해 필요한 최소 그림 수입니다.")]
        [Min(2)] public int bootstrapMinTraceCount = 3;
        [Tooltip("튜토리얼 카테고리 설명 임베딩과의 적합도를 표시/디버그할 때 참고하는 보조 기준입니다. 카테고리 거절은 별도 판정 파이프라인이 처리합니다.")]
        [Range(-1f, 1f)] public float bootstrapMinCategoryDescriptorFit = 0.6f;
        [Tooltip("기존 직렬화 값과 검토 시작값의 상한을 유지하기 위한 기준입니다. 이 값 이상이어도 의미 중복을 자동 확정하지 않고 LLM 검토를 거칩니다.")]
        [Range(0.8f, 1f)] public float bootstrapDuplicateSemanticThreshold = 0.96f;
        [Tooltip("라벨 임베딩이 이 값 이상이면 가까운 후보로 보고 LLM 등가 명칭 확인 대상으로 보냅니다.")]
        [Range(0f, 1f)] public float bootstrapDuplicateSemanticReviewThreshold = 0.75f;
        [Tooltip("켜면 임베딩으로 찾은 가까운 라벨 후보를 LLM이 등가 명칭인지 제한적으로 검토합니다.")]
        public bool enableSemanticDuplicateLlmReview = true;
        [Tooltip("새 표본 하나당 LLM 의미 재검토 대상으로 유지할 가장 가까운 기존 카드 수입니다.")]
        [Min(1)] public int semanticDuplicateReviewMaxCandidates = 3;

        [Header("Semantic Map")]
        [Tooltip("켜면 그림 제출 후 터미널에 세션 기반 의미공간 맵과 공명 피드백을 표시합니다.")]
        public bool showSemanticMapFeedback = true;
        [Tooltip("새 노드가 추가될 때 의미공간 배치를 안정화하기 위해 수행할 반복 계산 횟수입니다.")]
        [Min(1)] public int semanticMapLayoutIterations = 28;
        [Tooltip("두 의미 노드가 이 유사도 이상 가까울 때 서로 끌어당깁니다.")]
        [Range(-1f, 1f)] public float semanticMapAttractionThreshold = 0.7f;
        [Tooltip("의미가 가까운 노드끼리 서로 끌어당기는 힘입니다.")]
        [Range(0f, 1f)] public float semanticMapAttractionStrength = 0.08f;
        [Tooltip("모든 노드가 서로 겹치지 않도록 밀어내는 힘입니다.")]
        [Range(0f, 1f)] public float semanticMapRepulsionStrength = 0.028f;
        [Tooltip("기존 배치가 갑자기 크게 흔들리지 않도록 이동 속도를 줄이는 비율입니다.")]
        [Range(0f, 1f)] public float semanticMapDamping = 0.72f;
        [Tooltip("한 번의 배치 반복에서 노드가 이동할 수 있는 최대 거리입니다.")]
        [Range(0.005f, 0.25f)] public float semanticMapMaxStep = 0.045f;

        [Header("Waveform")]
        [Tooltip("의미 임베딩이 캘리브레이션 파형 모양에 영향을 주는 비율입니다.")]
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
            clusterNeighborCount = Math.Max(1, clusterNeighborCount);
            minClusterMembers = Math.Max(2, minClusterMembers);
            semanticGroupCandidateCount = Mathf.Clamp(semanticGroupCandidateCount, 1, 3);
            semanticGroupRepresentativeLimit = Mathf.Clamp(semanticGroupRepresentativeLimit, 2, 8);
            semanticGroupJoinAmbiguityMargin = Mathf.Clamp(semanticGroupJoinAmbiguityMargin, 0f, 0.25f);
            bootstrapMinTraceCount = Math.Max(2, bootstrapMinTraceCount);
            bootstrapDuplicateSemanticThreshold = Mathf.Clamp(bootstrapDuplicateSemanticThreshold, 0.8f, 1f);
            bootstrapDuplicateSemanticReviewThreshold = Mathf.Clamp(
                bootstrapDuplicateSemanticReviewThreshold,
                0f,
                bootstrapDuplicateSemanticThreshold);
            semanticDuplicateReviewMaxCandidates = Math.Max(1, semanticDuplicateReviewMaxCandidates);
            semanticMapLayoutIterations = Math.Max(1, semanticMapLayoutIterations);
            semanticMapAttractionStrength = Mathf.Clamp01(semanticMapAttractionStrength);
            semanticMapRepulsionStrength = Mathf.Clamp01(semanticMapRepulsionStrength);
            semanticMapDamping = Mathf.Clamp01(semanticMapDamping);
            semanticMapMaxStep = Mathf.Clamp(semanticMapMaxStep, 0.005f, 0.25f);
            waveformFeatureCount = Math.Max(8, waveformFeatureCount);
        }
    }
}
