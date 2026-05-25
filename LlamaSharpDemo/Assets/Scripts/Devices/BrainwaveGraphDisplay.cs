using DoodleDiplomacy.Core;
using UnityEngine;
using UnityEngine.UI;

namespace DoodleDiplomacy.Devices
{
    [DisallowMultipleComponent]
    public sealed class BrainwaveGraphDisplay : MaskableGraphic
    {
        [Header("Graph")]
        [SerializeField, Min(32)] private int sampleCount = 180;
        [SerializeField, Min(0.25f)] private float lineThickness = 2f;
        [SerializeField, Min(0f)] private float scrollSpeed = 0.18f;
        [SerializeField] private bool drawGrid = true;
        [SerializeField] private Color gridColor = new(0.08f, 0.32f, 0.12f, 0.45f);

        [Header("Channels")]
        [SerializeField] private Color channelAColor = new(0.35f, 1f, 0.5f, 0.95f);
        [SerializeField] private Color channelBColor = new(0.35f, 0.9f, 1f, 0.9f);
        [SerializeField] private Color channelCColor = new(1f, 0.8f, 0.35f, 0.9f);

        private readonly float[] _channelPhase = new float[3];
        private readonly float[] _channelGain = new float[3];
        private readonly float[] _channelFrequencyOffset = new float[3];
        private readonly float[] _channelSpikeGain = new float[3];

        private bool _hasSignal;
        private bool _running;
        private int _seed;
        private float _amplitude = 0.1f;
        private float _noise = 0.04f;
        private float _frequency = 1.8f;
        private float _spikeChance = 0.02f;
        private float _spikeAmplitude = 0.12f;
        private float _spikeDensity = 8f;
        private float _timeOffset;

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            SetVerticesDirty();
        }

        private void Update()
        {
            if (!_running)
            {
                return;
            }

            _timeOffset += Time.deltaTime * scrollSpeed;
            SetVerticesDirty();
        }

        public void Play(ReactionTier tier, string label, int sampleIndex, int sessionSeed)
        {
            GenerateProfile(tier, label, sampleIndex, sessionSeed);
            _timeOffset = 0f;
            _hasSignal = true;
            _running = true;
            SetVerticesDirty();
        }

        public void Stop()
        {
            _running = false;
        }

        public void Clear()
        {
            _running = false;
            _hasSignal = false;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            if (!_hasSignal)
            {
                return;
            }

            Rect rect = rectTransform.rect;
            if (rect.width <= 1f || rect.height <= 1f)
            {
                return;
            }

            if (drawGrid)
            {
                DrawGrid(vh, rect);
            }

            DrawChannel(vh, rect, 0.74f, 0, channelAColor);
            DrawChannel(vh, rect, 0.5f, 1, channelBColor);
            DrawChannel(vh, rect, 0.26f, 2, channelCColor);
        }

        private void GenerateProfile(ReactionTier tier, string label, int sampleIndex, int sessionSeed)
        {
            _seed = StableHash(label, sampleIndex, sessionSeed);
            var rng = new System.Random(_seed);

            switch (tier)
            {
                case ReactionTier.None:
                    _amplitude = Jitter(rng, 0.025f, 0.055f);
                    _noise = Jitter(rng, 0.012f, 0.035f);
                    _frequency = Jitter(rng, 0.8f, 1.35f);
                    _spikeChance = Jitter(rng, 0.004f, 0.012f);
                    _spikeAmplitude = Jitter(rng, 0.03f, 0.07f);
                    _spikeDensity = Jitter(rng, 5f, 8f);
                    break;
                case ReactionTier.Subtle:
                    _amplitude = Jitter(rng, 0.08f, 0.15f);
                    _noise = Jitter(rng, 0.025f, 0.065f);
                    _frequency = Jitter(rng, 1.35f, 2.1f);
                    _spikeChance = Jitter(rng, 0.012f, 0.03f);
                    _spikeAmplitude = Jitter(rng, 0.07f, 0.14f);
                    _spikeDensity = Jitter(rng, 6f, 10f);
                    break;
                case ReactionTier.Moderate:
                    _amplitude = Jitter(rng, 0.17f, 0.28f);
                    _noise = Jitter(rng, 0.045f, 0.095f);
                    _frequency = Jitter(rng, 2.0f, 3.1f);
                    _spikeChance = Jitter(rng, 0.025f, 0.055f);
                    _spikeAmplitude = Jitter(rng, 0.12f, 0.23f);
                    _spikeDensity = Jitter(rng, 8f, 13f);
                    break;
                case ReactionTier.Strong:
                    _amplitude = Jitter(rng, 0.29f, 0.42f);
                    _noise = Jitter(rng, 0.09f, 0.16f);
                    _frequency = Jitter(rng, 2.8f, 4.2f);
                    _spikeChance = Jitter(rng, 0.045f, 0.09f);
                    _spikeAmplitude = Jitter(rng, 0.2f, 0.36f);
                    _spikeDensity = Jitter(rng, 10f, 16f);
                    break;
                default:
                    _amplitude = 0.1f;
                    _noise = 0.04f;
                    _frequency = 1.8f;
                    _spikeChance = 0.02f;
                    _spikeAmplitude = 0.12f;
                    _spikeDensity = 8f;
                    break;
            }

            for (int i = 0; i < 3; i++)
            {
                _channelPhase[i] = Jitter(rng, 0f, Mathf.PI * 2f);
                _channelGain[i] = Jitter(rng, 0.76f, 1.22f);
                _channelFrequencyOffset[i] = Jitter(rng, -0.18f, 0.24f);
                _channelSpikeGain[i] = Jitter(rng, 0.65f, 1.4f);
            }
        }

        private void DrawGrid(VertexHelper vh, Rect rect)
        {
            const int verticalLines = 10;
            const int horizontalLines = 5;
            float gridThickness = Mathf.Max(0.5f, lineThickness * 0.35f);

            for (int i = 0; i <= verticalLines; i++)
            {
                float x = Mathf.Lerp(rect.xMin, rect.xMax, i / (float)verticalLines);
                AddLine(vh, new Vector2(x, rect.yMin), new Vector2(x, rect.yMax), gridThickness, gridColor);
            }

            for (int i = 0; i <= horizontalLines; i++)
            {
                float y = Mathf.Lerp(rect.yMin, rect.yMax, i / (float)horizontalLines);
                AddLine(vh, new Vector2(rect.xMin, y), new Vector2(rect.xMax, y), gridThickness, gridColor);
            }
        }

        private void DrawChannel(VertexHelper vh, Rect rect, float centerYNormalized, int channel, Color channelColor)
        {
            int count = Mathf.Max(2, sampleCount);
            Vector2 previous = SamplePoint(rect, 0, count, centerYNormalized, channel);

            for (int i = 1; i < count; i++)
            {
                Vector2 next = SamplePoint(rect, i, count, centerYNormalized, channel);
                AddLine(vh, previous, next, lineThickness, channelColor);
                previous = next;
            }
        }

        private Vector2 SamplePoint(Rect rect, int index, int count, float centerYNormalized, int channel)
        {
            float normalizedX = index / (float)(count - 1);
            float x = Mathf.Lerp(rect.xMin, rect.xMax, normalizedX);
            float movingX = normalizedX + _timeOffset;
            float frequency = Mathf.Max(0.05f, _frequency + _channelFrequencyOffset[channel]);
            float phase = _channelPhase[channel];

            float wave =
                Mathf.Sin((movingX * frequency * Mathf.PI * 2f) + phase) * _amplitude +
                Mathf.Sin((movingX * frequency * 2.73f * Mathf.PI * 2f) + phase * 0.57f) * _amplitude * 0.32f +
                SampleNoise(movingX, channel) * _noise +
                SampleSpike(movingX, channel);

            wave *= _channelGain[channel];

            float centerY = Mathf.Lerp(rect.yMin, rect.yMax, centerYNormalized);
            float y = centerY + wave * rect.height;
            return new Vector2(x, y);
        }

        private float SampleNoise(float movingX, int channel)
        {
            float seedOffset = Mathf.Abs(_seed % 10000) * 0.013f;
            float noise = Mathf.PerlinNoise(
                movingX * 18.7f + seedOffset,
                channel * 11.31f + seedOffset * 0.37f);
            return noise * 2f - 1f;
        }

        private float SampleSpike(float movingX, int channel)
        {
            float spikePosition = movingX * _spikeDensity;
            int cell = Mathf.FloorToInt(spikePosition);
            float local = spikePosition - cell;
            float chance = _spikeChance * _channelSpikeGain[channel];

            if (StableRandom01(cell, channel, _seed) > chance)
            {
                return 0f;
            }

            float center = Mathf.Lerp(0.18f, 0.82f, StableRandom01(cell + 17, channel, _seed));
            float width = Mathf.Lerp(0.025f, 0.07f, StableRandom01(cell + 31, channel, _seed));
            float sign = StableRandom01(cell + 47, channel, _seed) > 0.5f ? 1f : -1f;
            float distance = (local - center) / width;
            float envelope = Mathf.Exp(-(distance * distance));
            return sign * envelope * _spikeAmplitude;
        }

        private static void AddLine(VertexHelper vh, Vector2 start, Vector2 end, float thickness, Color lineColor)
        {
            Vector2 direction = end - start;
            if (direction.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            direction.Normalize();
            Vector2 normal = new(-direction.y, direction.x);
            Vector2 offset = normal * Mathf.Max(0.1f, thickness) * 0.5f;
            int vertexStart = vh.currentVertCount;

            vh.AddVert(start - offset, lineColor, Vector2.zero);
            vh.AddVert(start + offset, lineColor, Vector2.zero);
            vh.AddVert(end + offset, lineColor, Vector2.zero);
            vh.AddVert(end - offset, lineColor, Vector2.zero);

            vh.AddTriangle(vertexStart, vertexStart + 1, vertexStart + 2);
            vh.AddTriangle(vertexStart, vertexStart + 2, vertexStart + 3);
        }

        private static float Jitter(System.Random rng, float min, float max)
        {
            return Mathf.Lerp(min, max, (float)rng.NextDouble());
        }

        private static int StableHash(string label, int sampleIndex, int sessionSeed)
        {
            unchecked
            {
                int hash = 17;
                string normalizedLabel = label ?? string.Empty;
                for (int i = 0; i < normalizedLabel.Length; i++)
                {
                    hash = (hash * 31) + char.ToLowerInvariant(normalizedLabel[i]);
                }

                hash = (hash * 31) + sampleIndex;
                hash = (hash * 31) + sessionSeed;
                return hash == int.MinValue ? 0 : hash;
            }
        }

        private static float StableRandom01(int a, int b, int c)
        {
            unchecked
            {
                uint hash = 2166136261u;
                hash = (hash ^ (uint)a) * 16777619u;
                hash = (hash ^ (uint)b) * 16777619u;
                hash = (hash ^ (uint)c) * 16777619u;
                return (hash & 0x00FFFFFF) / 16777215f;
            }
        }
    }
}
