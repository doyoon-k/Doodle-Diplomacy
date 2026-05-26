using DoodleDiplomacy.Core;
using UnityEngine;
using UnityEngine.UI;

namespace DoodleDiplomacy.Devices
{
    [DisallowMultipleComponent]
    public sealed class BrainwaveGraphDisplay : MaskableGraphic
    {
        [Header("Graph")]
        [Tooltip("Number of samples used to draw each waveform line. Higher values look smoother but cost more UI mesh vertices.")]
        [SerializeField, Min(32)] private int sampleCount = 180;
        [Tooltip("Thickness of each waveform line in UI units.")]
        [SerializeField, Min(0.25f)] private float lineThickness = 2f;
        [Tooltip("Horizontal movement speed of the waveform animation.")]
        [SerializeField, Min(0f)] private float scrollSpeed = 0.18f;
        [Tooltip("Default convergence duration used when BeginTraceLock is called without an explicit duration.")]
        [SerializeField, Min(0.05f)] private float defaultLockDuration = 0.9f;
        [Tooltip("Draw a faint grid behind the waveform lines.")]
        [SerializeField] private bool drawGrid = true;
        [Tooltip("Color and opacity of the graph grid lines.")]
        [SerializeField] private Color gridColor = new(0.08f, 0.32f, 0.12f, 0.45f);

        [Header("Channels")]
        [Tooltip("Color of the upper composite brainwave trace.")]
        [SerializeField] private Color channelAColor = new(0.35f, 1f, 0.5f, 0.95f);
        [Tooltip("Color of the middle composite brainwave trace.")]
        [SerializeField] private Color channelBColor = new(0.35f, 0.9f, 1f, 0.9f);
        [Tooltip("Color of the lower composite brainwave trace.")]
        [SerializeField] private Color channelCColor = new(1f, 0.8f, 0.35f, 0.9f);

        [Header("Lock Alignment")]
        [Tooltip("Vertical distance from graph center to the upper/lower waveform while the terminal is still searching.")]
        [SerializeField, Range(0f, 0.45f)] private float searchingChannelSpread = 0.24f;
        [Tooltip("Vertical distance from graph center to the upper/lower waveform after the reaction trace is locked. Lower values make all waveforms converge more tightly, independent of reaction tier.")]
        [SerializeField, Range(0f, 0.45f)] private float lockedChannelSpread = 0.1f;

        private readonly float[] _channelPhase = new float[3];
        private readonly float[] _channelGain = new float[3];
        private readonly float[] _channelFrequencyOffset = new float[3];
        private readonly float[] _channelSpikeGain = new float[3];
        private readonly float[] _startChannelPhase = new float[3];
        private readonly float[] _startChannelGain = new float[3];
        private readonly float[] _startChannelFrequencyOffset = new float[3];
        private readonly float[] _startChannelSpikeGain = new float[3];
        private readonly float[] _targetChannelPhase = new float[3];
        private readonly float[] _targetChannelGain = new float[3];
        private readonly float[] _targetChannelFrequencyOffset = new float[3];
        private readonly float[] _targetChannelSpikeGain = new float[3];

        private bool _hasSignal;
        private bool _running;
        private bool _isLocking;
        private int _seed;
        private float _amplitude = 0.1f;
        private float _noise = 0.04f;
        private float _frequency = 1.8f;
        private float _spikeChance = 0.02f;
        private float _spikeAmplitude = 0.12f;
        private float _spikeDensity = 8f;
        private float _channelSpread = 0.24f;
        private float _timeOffset;
        private float _lockElapsed;
        private float _lockDuration;
        private float _startAmplitude;
        private float _startNoise;
        private float _startFrequency;
        private float _startSpikeChance;
        private float _startSpikeAmplitude;
        private float _startSpikeDensity;
        private float _startChannelSpread;
        private float _targetAmplitude;
        private float _targetNoise;
        private float _targetFrequency;
        private float _targetSpikeChance;
        private float _targetSpikeAmplitude;
        private float _targetSpikeDensity;
        private float _targetChannelSpread;

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

        protected override void OnValidate()
        {
            base.OnValidate();
            searchingChannelSpread = Mathf.Clamp(searchingChannelSpread, 0f, 0.45f);
            lockedChannelSpread = Mathf.Clamp(lockedChannelSpread, 0f, 0.45f);
            if (!_hasSignal)
            {
                _channelSpread = GetSearchingChannelSpread();
            }

            SetVerticesDirty();
        }

        private void Update()
        {
            if (!_running)
            {
                return;
            }

            _timeOffset += Time.deltaTime * scrollSpeed;
            if (_isLocking)
            {
                UpdateTraceLock(Time.deltaTime);
            }

            SetVerticesDirty();
        }

        public void Play(ReactionTier tier, string label, int sampleIndex, int sessionSeed)
        {
            PlayLocked(tier, label, sampleIndex, sessionSeed);
        }

        public void PlaySearching(string label, int sampleIndex, int sessionSeed)
        {
            GenerateSearchingProfile(label, sampleIndex, sessionSeed);
            _timeOffset = 0f;
            _hasSignal = true;
            _running = true;
            _isLocking = false;
            SetVerticesDirty();
        }

        public void BeginTraceLock(ReactionTier tier, string label, int sampleIndex, int sessionSeed)
        {
            BeginTraceLock(tier, label, sampleIndex, sessionSeed, defaultLockDuration);
        }

        public void BeginTraceLock(
            ReactionTier tier,
            string label,
            int sampleIndex,
            int sessionSeed,
            float lockDuration)
        {
            if (!_hasSignal)
            {
                GenerateSearchingProfile(label, sampleIndex, sessionSeed);
                _hasSignal = true;
            }

            CaptureCurrentAsLockStart();
            GenerateLockedProfile(tier, label, sampleIndex, sessionSeed, writeToTarget: true);
            _lockElapsed = 0f;
            _lockDuration = Mathf.Max(0.05f, lockDuration);
            _running = true;
            _isLocking = true;
            SetVerticesDirty();
        }

        public void PlayLocked(ReactionTier tier, string label, int sampleIndex, int sessionSeed)
        {
            GenerateProfile(tier, label, sampleIndex, sessionSeed);
            _timeOffset = 0f;
            _hasSignal = true;
            _running = true;
            _isLocking = false;
            SetVerticesDirty();
        }

        public void Stop()
        {
            _running = false;
        }

        public void ConfigureChannelSpread(float searchingSpread, float lockedSpread)
        {
            searchingChannelSpread = Mathf.Clamp(searchingSpread, 0f, 0.45f);
            lockedChannelSpread = Mathf.Clamp(lockedSpread, 0f, 0.45f);
            if (!_hasSignal)
            {
                _channelSpread = GetSearchingChannelSpread();
            }

            SetVerticesDirty();
        }

        public void Clear()
        {
            _running = false;
            _isLocking = false;
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

            DrawChannel(vh, rect, GetChannelCenterYNormalized(0), 0, channelAColor);
            DrawChannel(vh, rect, GetChannelCenterYNormalized(1), 1, channelBColor);
            DrawChannel(vh, rect, GetChannelCenterYNormalized(2), 2, channelCColor);
        }

        private void GenerateProfile(ReactionTier tier, string label, int sampleIndex, int sessionSeed)
        {
            GenerateLockedProfile(tier, label, sampleIndex, sessionSeed, writeToTarget: false);
        }

        private void GenerateSearchingProfile(string label, int sampleIndex, int sessionSeed)
        {
            _seed = StableHash(label, sampleIndex, sessionSeed) ^ 0x2A6B9651;
            var rng = new System.Random(_seed);

            _amplitude = Jitter(rng, 0.025f, 0.065f);
            _noise = Jitter(rng, 0.035f, 0.085f);
            _frequency = Jitter(rng, 0.75f, 1.55f);
            _spikeChance = Jitter(rng, 0.004f, 0.014f);
            _spikeAmplitude = Jitter(rng, 0.025f, 0.075f);
            _spikeDensity = Jitter(rng, 4.5f, 8.5f);
            _channelSpread = GetSearchingChannelSpread();

            for (int i = 0; i < 3; i++)
            {
                _channelPhase[i] = Jitter(rng, 0f, Mathf.PI * 2f);
                _channelGain[i] = Jitter(rng, 0.62f, 1.1f);
                _channelFrequencyOffset[i] = Jitter(rng, -0.55f, 0.8f);
                _channelSpikeGain[i] = Jitter(rng, 0.35f, 0.9f);
            }
        }

        private void GenerateLockedProfile(
            ReactionTier tier,
            string label,
            int sampleIndex,
            int sessionSeed,
            bool writeToTarget)
        {
            _seed = StableHash(label, sampleIndex, sessionSeed);
            var rng = new System.Random(_seed);
            float amplitude;
            float noise;
            float frequency;
            float spikeChance;
            float spikeAmplitude;
            float spikeDensity;

            switch (tier)
            {
                case ReactionTier.None:
                    amplitude = Jitter(rng, 0.025f, 0.055f);
                    noise = Jitter(rng, 0.012f, 0.035f);
                    frequency = Jitter(rng, 0.8f, 1.35f);
                    spikeChance = Jitter(rng, 0.004f, 0.012f);
                    spikeAmplitude = Jitter(rng, 0.03f, 0.07f);
                    spikeDensity = Jitter(rng, 5f, 8f);
                    break;
                case ReactionTier.Subtle:
                    amplitude = Jitter(rng, 0.08f, 0.15f);
                    noise = Jitter(rng, 0.025f, 0.065f);
                    frequency = Jitter(rng, 1.35f, 2.1f);
                    spikeChance = Jitter(rng, 0.012f, 0.03f);
                    spikeAmplitude = Jitter(rng, 0.07f, 0.14f);
                    spikeDensity = Jitter(rng, 6f, 10f);
                    break;
                case ReactionTier.Moderate:
                    amplitude = Jitter(rng, 0.17f, 0.28f);
                    noise = Jitter(rng, 0.045f, 0.095f);
                    frequency = Jitter(rng, 2.0f, 3.1f);
                    spikeChance = Jitter(rng, 0.025f, 0.055f);
                    spikeAmplitude = Jitter(rng, 0.12f, 0.23f);
                    spikeDensity = Jitter(rng, 8f, 13f);
                    break;
                case ReactionTier.Strong:
                    amplitude = Jitter(rng, 0.29f, 0.42f);
                    noise = Jitter(rng, 0.09f, 0.16f);
                    frequency = Jitter(rng, 2.8f, 4.2f);
                    spikeChance = Jitter(rng, 0.045f, 0.09f);
                    spikeAmplitude = Jitter(rng, 0.2f, 0.36f);
                    spikeDensity = Jitter(rng, 10f, 16f);
                    break;
                default:
                    amplitude = 0.1f;
                    noise = 0.04f;
                    frequency = 1.8f;
                    spikeChance = 0.02f;
                    spikeAmplitude = 0.12f;
                    spikeDensity = 8f;
                    break;
            }

            float sharedPhase = Jitter(rng, 0f, Mathf.PI * 2f);
            SetChannelSpreadValue(GetLockedChannelSpread(), writeToTarget);
            for (int i = 0; i < 3; i++)
            {
                SetChannelValue(
                    i,
                    sharedPhase + Jitter(rng, -0.05f, 0.05f),
                    Jitter(rng, 0.9f, 1.12f),
                    Jitter(rng, -0.035f, 0.035f),
                    Jitter(rng, 0.9f, 1.18f),
                    writeToTarget);
            }

            SetProfileValues(amplitude, noise, frequency, spikeChance, spikeAmplitude, spikeDensity, writeToTarget);
        }

        private void CaptureCurrentAsLockStart()
        {
            _startAmplitude = _amplitude;
            _startNoise = _noise;
            _startFrequency = _frequency;
            _startSpikeChance = _spikeChance;
            _startSpikeAmplitude = _spikeAmplitude;
            _startSpikeDensity = _spikeDensity;
            _startChannelSpread = _channelSpread;

            for (int i = 0; i < 3; i++)
            {
                _startChannelPhase[i] = _channelPhase[i];
                _startChannelGain[i] = _channelGain[i];
                _startChannelFrequencyOffset[i] = _channelFrequencyOffset[i];
                _startChannelSpikeGain[i] = _channelSpikeGain[i];
            }
        }

        private void UpdateTraceLock(float deltaTime)
        {
            _lockElapsed += deltaTime;
            float t = Mathf.Clamp01(_lockElapsed / _lockDuration);
            float eased = Mathf.SmoothStep(0f, 1f, t);

            _amplitude = Mathf.Lerp(_startAmplitude, _targetAmplitude, eased);
            _noise = Mathf.Lerp(_startNoise, _targetNoise, eased);
            _frequency = Mathf.Lerp(_startFrequency, _targetFrequency, eased);
            _spikeChance = Mathf.Lerp(_startSpikeChance, _targetSpikeChance, eased);
            _spikeAmplitude = Mathf.Lerp(_startSpikeAmplitude, _targetSpikeAmplitude, eased);
            _spikeDensity = Mathf.Lerp(_startSpikeDensity, _targetSpikeDensity, eased);
            _channelSpread = Mathf.Lerp(_startChannelSpread, _targetChannelSpread, eased);

            for (int i = 0; i < 3; i++)
            {
                _channelPhase[i] = LerpRadians(_startChannelPhase[i], _targetChannelPhase[i], eased);
                _channelGain[i] = Mathf.Lerp(_startChannelGain[i], _targetChannelGain[i], eased);
                _channelFrequencyOffset[i] = Mathf.Lerp(
                    _startChannelFrequencyOffset[i],
                    _targetChannelFrequencyOffset[i],
                    eased);
                _channelSpikeGain[i] = Mathf.Lerp(_startChannelSpikeGain[i], _targetChannelSpikeGain[i], eased);
            }

            if (t >= 1f)
            {
                _isLocking = false;
            }
        }

        private void SetProfileValues(
            float amplitude,
            float noise,
            float frequency,
            float spikeChance,
            float spikeAmplitude,
            float spikeDensity,
            bool writeToTarget)
        {
            if (writeToTarget)
            {
                _targetAmplitude = amplitude;
                _targetNoise = noise;
                _targetFrequency = frequency;
                _targetSpikeChance = spikeChance;
                _targetSpikeAmplitude = spikeAmplitude;
                _targetSpikeDensity = spikeDensity;
                return;
            }

            _amplitude = amplitude;
            _noise = noise;
            _frequency = frequency;
            _spikeChance = spikeChance;
            _spikeAmplitude = spikeAmplitude;
            _spikeDensity = spikeDensity;
        }

        private void SetChannelSpreadValue(float channelSpread, bool writeToTarget)
        {
            if (writeToTarget)
            {
                _targetChannelSpread = channelSpread;
                return;
            }

            _channelSpread = channelSpread;
        }

        private void SetChannelValue(
            int channel,
            float phase,
            float gain,
            float frequencyOffset,
            float spikeGain,
            bool writeToTarget)
        {
            if (writeToTarget)
            {
                _targetChannelPhase[channel] = phase;
                _targetChannelGain[channel] = gain;
                _targetChannelFrequencyOffset[channel] = frequencyOffset;
                _targetChannelSpikeGain[channel] = spikeGain;
                return;
            }

            _channelPhase[channel] = phase;
            _channelGain[channel] = gain;
            _channelFrequencyOffset[channel] = frequencyOffset;
            _channelSpikeGain[channel] = spikeGain;
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

        private float GetChannelCenterYNormalized(int channel)
        {
            float spread = Mathf.Clamp(_channelSpread, 0f, 0.45f);
            switch (channel)
            {
                case 0:
                    return 0.5f + spread;
                case 2:
                    return 0.5f - spread;
                default:
                    return 0.5f;
            }
        }

        private float GetSearchingChannelSpread()
        {
            return Mathf.Clamp(searchingChannelSpread, 0f, 0.45f);
        }

        private float GetLockedChannelSpread()
        {
            return Mathf.Clamp(lockedChannelSpread, 0f, 0.45f);
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

        private static float LerpRadians(float from, float to, float t)
        {
            float fromDegrees = from * Mathf.Rad2Deg;
            float toDegrees = to * Mathf.Rad2Deg;
            return Mathf.LerpAngle(fromDegrees, toDegrees, t) * Mathf.Deg2Rad;
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
