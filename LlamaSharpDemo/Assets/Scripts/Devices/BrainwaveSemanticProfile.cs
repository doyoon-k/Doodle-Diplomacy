using System;
using UnityEngine;

namespace DoodleDiplomacy.Devices
{
    public readonly struct BrainwaveSemanticProfile
    {
        public static readonly BrainwaveSemanticProfile Invalid = default;

        public readonly bool IsValid;
        public readonly int TextureSeed;
        public readonly float BaseFrequency;
        public readonly float HarmonicRatio;
        public readonly float HarmonicWeight;
        public readonly float NoiseScale;
        public readonly float SpikeDensityScale;
        public readonly float ChannelSync;
        public readonly Vector3 ChannelPhaseOffsets;
        public readonly Vector3 ChannelGainScales;
        public readonly Vector3 ChannelFrequencyOffsets;
        public readonly Vector3 ChannelSpikeScales;

        public BrainwaveSemanticProfile(
            int textureSeed,
            float baseFrequency,
            float harmonicRatio,
            float harmonicWeight,
            float noiseScale,
            float spikeDensityScale,
            float channelSync,
            Vector3 channelPhaseOffsets,
            Vector3 channelGainScales,
            Vector3 channelFrequencyOffsets,
            Vector3 channelSpikeScales)
        {
            IsValid = true;
            TextureSeed = textureSeed;
            BaseFrequency = baseFrequency;
            HarmonicRatio = harmonicRatio;
            HarmonicWeight = harmonicWeight;
            NoiseScale = noiseScale;
            SpikeDensityScale = spikeDensityScale;
            ChannelSync = channelSync;
            ChannelPhaseOffsets = channelPhaseOffsets;
            ChannelGainScales = channelGainScales;
            ChannelFrequencyOffsets = channelFrequencyOffsets;
            ChannelSpikeScales = channelSpikeScales;
        }
    }

    public static class BrainwaveEmbeddingProfileMapper
    {
        private const int MinimumFeatureCount = 8;
        private const int MaximumFeatureCount = 32;
        private const float NeutralBaseFrequency = 1.8f;
        private const float NeutralHarmonicRatio = 2.73f;
        private const float NeutralHarmonicWeight = 0.32f;
        private const float NeutralNoiseScale = 1f;
        private const float NeutralSpikeDensityScale = 1f;
        private const float NeutralChannelSync = 0.92f;

        public static bool TryCreate(
            float[] embedding,
            string label,
            int sampleIndex,
            int sessionSeed,
            int projectionSeed,
            int featureCount,
            float semanticInfluence,
            float sessionJitter,
            out BrainwaveSemanticProfile profile)
        {
            profile = BrainwaveSemanticProfile.Invalid;

            if (embedding == null || embedding.Length == 0)
            {
                return false;
            }

            double normSquared = 0d;
            for (int i = 0; i < embedding.Length; i++)
            {
                normSquared += embedding[i] * embedding[i];
            }

            if (normSquared <= double.Epsilon)
            {
                return false;
            }

            int clampedFeatureCount = Mathf.Clamp(featureCount, MinimumFeatureCount, MaximumFeatureCount);
            float[] features = new float[clampedFeatureCount];
            float inverseNorm = (float)(1d / Math.Sqrt(normSquared));
            for (int i = 0; i < features.Length; i++)
            {
                features[i] = ProjectFeature(embedding, inverseNorm, i, projectionSeed);
            }

            float influence = Mathf.Clamp01(semanticInfluence);
            float jitter = Mathf.Clamp(sessionJitter, 0f, 0.25f);
            int jitterSeed = StableHash(label ?? string.Empty, sampleIndex, sessionSeed, projectionSeed);

            float baseFrequency = Blend(
                NeutralBaseFrequency,
                Mathf.Lerp(0.85f, 3.35f, Unit(features[0])) + Jitter(jitterSeed, 11, 0.08f * jitter),
                influence);

            float harmonicRatio = Blend(
                NeutralHarmonicRatio,
                Mathf.Lerp(1.65f, 3.65f, Unit(features[1])) + Jitter(jitterSeed, 17, 0.06f * jitter),
                influence);

            float harmonicWeight = Blend(
                NeutralHarmonicWeight,
                Mathf.Lerp(0.16f, 0.48f, Unit(features[2])) + Jitter(jitterSeed, 23, 0.02f * jitter),
                influence);

            float noiseScale = Blend(
                NeutralNoiseScale,
                Mathf.Lerp(0.65f, 1.45f, Unit(features[3])) + Jitter(jitterSeed, 29, 0.04f * jitter),
                influence);

            float spikeDensityScale = Blend(
                NeutralSpikeDensityScale,
                Mathf.Lerp(0.75f, 1.35f, Unit(features[4])) + Jitter(jitterSeed, 31, 0.04f * jitter),
                influence);

            float channelSync = Blend(
                NeutralChannelSync,
                Mathf.Lerp(0.82f, 0.98f, Unit(features[5])),
                influence);

            float phaseRange = Mathf.Lerp(0.05f, 0.28f, 1f - channelSync);
            Vector3 phaseOffsets = new(
                Feature(features, 6) * phaseRange,
                Feature(features, 7) * phaseRange,
                Feature(features, 8) * phaseRange);

            Vector3 gainScales = new(
                SemanticScale(features, 9, influence, jitterSeed, 37, 0.09f, jitter),
                SemanticScale(features, 10, influence, jitterSeed, 41, 0.09f, jitter),
                SemanticScale(features, 11, influence, jitterSeed, 43, 0.09f, jitter));

            Vector3 frequencyOffsets = new(
                SemanticOffset(features, 12, influence, jitterSeed, 47, 0.13f, jitter),
                SemanticOffset(features, 13, influence, jitterSeed, 53, 0.13f, jitter),
                SemanticOffset(features, 14, influence, jitterSeed, 59, 0.13f, jitter));

            Vector3 spikeScales = new(
                SemanticScale(features, 15, influence, jitterSeed, 61, 0.18f, jitter),
                SemanticScale(features, 16, influence, jitterSeed, 67, 0.18f, jitter),
                SemanticScale(features, 17, influence, jitterSeed, 71, 0.18f, jitter));

            int textureSeed = BuildTextureSeed(features, projectionSeed);
            profile = new BrainwaveSemanticProfile(
                textureSeed,
                Mathf.Max(0.1f, baseFrequency),
                Mathf.Max(0.1f, harmonicRatio),
                Mathf.Clamp(harmonicWeight, 0f, 0.75f),
                Mathf.Max(0.1f, noiseScale),
                Mathf.Max(0.1f, spikeDensityScale),
                Mathf.Clamp01(channelSync),
                phaseOffsets,
                ClampVector(gainScales, 0.82f, 1.18f),
                ClampVector(frequencyOffsets, -0.2f, 0.2f),
                ClampVector(spikeScales, 0.65f, 1.35f));

            return true;
        }

        private static float ProjectFeature(float[] embedding, float inverseNorm, int featureIndex, int projectionSeed)
        {
            double projected = 0d;
            for (int i = 0; i < embedding.Length; i++)
            {
                projected += embedding[i] * ProjectionWeight(featureIndex, i, projectionSeed);
            }

            float normalized = (float)(projected * inverseNorm * Math.Sqrt(3d));
            return Mathf.Clamp((float)Math.Tanh(normalized * 1.35f), -1f, 1f);
        }

        private static float ProjectionWeight(int featureIndex, int dimension, int projectionSeed)
        {
            uint hash = Mix((uint)projectionSeed, (uint)featureIndex, (uint)dimension);
            return ((hash & 0x00FFFFFFu) / 8388607.5f) - 1f;
        }

        private static float Feature(float[] features, int index)
        {
            return features[index % features.Length];
        }

        private static float Unit(float value)
        {
            return Mathf.Clamp01((value * 0.5f) + 0.5f);
        }

        private static float Blend(float neutral, float semantic, float influence)
        {
            return Mathf.Lerp(neutral, semantic, influence);
        }

        private static float SemanticScale(float[] features, int index, float influence, int seed, int salt, float amount, float jitter)
        {
            return 1f + (Feature(features, index) * amount * influence) + Jitter(seed, salt, 0.02f * jitter);
        }

        private static float SemanticOffset(float[] features, int index, float influence, int seed, int salt, float amount, float jitter)
        {
            return (Feature(features, index) * amount * influence) + Jitter(seed, salt, 0.03f * jitter);
        }

        private static float Jitter(int seed, int salt, float amount)
        {
            return ((StableRandom01(seed, salt) * 2f) - 1f) * amount;
        }

        private static Vector3 ClampVector(Vector3 value, float min, float max)
        {
            return new Vector3(
                Mathf.Clamp(value.x, min, max),
                Mathf.Clamp(value.y, min, max),
                Mathf.Clamp(value.z, min, max));
        }

        private static int BuildTextureSeed(float[] features, int projectionSeed)
        {
            unchecked
            {
                int hash = projectionSeed == 0 ? 0x45D9F3B : projectionSeed;
                int count = Mathf.Min(features.Length, 8);
                for (int i = 0; i < count; i++)
                {
                    int bucket = Mathf.RoundToInt(Unit(features[i]) * 16f);
                    hash = (hash * 397) ^ bucket;
                }

                return hash == 0 ? 1 : hash;
            }
        }

        private static int StableHash(string label, int sampleIndex, int sessionSeed, int projectionSeed)
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + sampleIndex;
                hash = (hash * 31) + sessionSeed;
                hash = (hash * 31) + projectionSeed;
                for (int i = 0; i < label.Length; i++)
                {
                    hash = (hash * 31) + label[i];
                }

                return hash;
            }
        }

        private static float StableRandom01(int seed, int salt)
        {
            uint hash = Mix((uint)seed, (uint)salt, 0x9E3779B9u);
            return (hash & 0x00FFFFFFu) / 16777215f;
        }

        private static uint Mix(uint a, uint b, uint c)
        {
            unchecked
            {
                uint x = a + 0x9E3779B9u;
                x ^= b + 0x7F4A7C15u + (x << 6) + (x >> 2);
                x ^= c + 0x94D049BBu + (x << 6) + (x >> 2);
                x ^= x >> 16;
                x *= 0x7FEB352Du;
                x ^= x >> 15;
                x *= 0x846CA68Bu;
                x ^= x >> 16;
                return x;
            }
        }
    }
}
