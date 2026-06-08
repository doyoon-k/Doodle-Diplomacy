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
        public LlmEmbeddingProfile embeddingProfile;
        public bool cacheEmbeddings = true;
        public bool normalizeVectorsInMemory = true;
        [Min(1)] public int maxBatchSize = 32;
        public bool failCardWhenEmbeddingMissing;

        [Header("Unknown Resolution")]
        [Range(-1f, 1f)] public float hintThreshold = 0.46f;
        [Range(-1f, 1f)] public float partialThreshold = 0.58f;
        [Range(-1f, 1f)] public float solvedThreshold = 0.73f;
        [Range(0f, 1f)] public float centroidWeight = 0.92f;

        [Header("Clusters")]
        [Range(-1f, 1f)] public float clusterJoinThreshold = 0.62f;
        [Range(-1f, 1f)] public float clusterAutoPartialThreshold = 0.58f;
        [Min(2)] public int minClusterMembers = 3;
        [Range(-1f, 1f)] public float minClusterCohesion = 0.55f;

        [Header("Waveform")]
        [Range(0f, 1f)] public float waveformSemanticInfluence = 0.9f;
        [Range(0f, 0.25f)] public float waveformSessionJitter = 0.04f;
        [Min(8)] public int waveformFeatureCount = 16;
        public int waveformProjectionSeed = 17331;

        private void OnValidate()
        {
            maxBatchSize = Math.Max(1, maxBatchSize);
            minClusterMembers = Math.Max(2, minClusterMembers);
            waveformFeatureCount = Math.Max(8, waveformFeatureCount);
            centroidWeight = Mathf.Clamp01(centroidWeight);
        }
    }
}
