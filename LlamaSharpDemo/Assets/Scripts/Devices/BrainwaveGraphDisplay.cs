using System.Collections.Generic;
using DoodleDiplomacy.Core;
using UnityEngine;
using UnityEngine.UI;

namespace DoodleDiplomacy.Devices
{
    public enum BrainwaveSignalRole
    {
        Drawing,
        AlienToken,
        UnknownToken
    }

    [DisallowMultipleComponent]
    public sealed class BrainwaveGraphDisplay : MaskableGraphic
    {
        [Header("Graph")]
        [Tooltip("Number of samples used to draw each waveform line. Higher values look smoother but cost more UI mesh vertices.")]
        [SerializeField, Min(32)] private int sampleCount = 180;
        [Tooltip("Thickness of each waveform line in UI units.")]
        [SerializeField, Min(0.25f)] private float lineThickness = 4.6f;
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
        [Tooltip("Color of parsed alien token signals in single-signal mode.")]
        [SerializeField] private Color alienSignalColor = new(1f, 0.68f, 0.22f, 0.95f);
        [Tooltip("Color of hidden unresolved token signals in single-signal and semantic comparison modes.")]
        [SerializeField] private Color unknownSignalColor = new(1f, 0.28f, 0.08f, 1f);
        [Tooltip("Color of the player's drawing signal in single-signal and semantic comparison modes.")]
        [SerializeField] private Color drawingSignalColor = new(0.35f, 0.9f, 1f, 0.95f);

        [Header("Comparison Capture")]
        [Tooltip("Seconds used to record the player's visual probe trace over the pre-existing unknown trace.")]
        [SerializeField, Min(0.1f)] private float comparisonCaptureDuration = 1.25f;

        [Header("Lock Alignment")]
        [Tooltip("Vertical distance from graph center to the upper/lower waveform while the terminal is still searching.")]
        [SerializeField, Range(0f, 0.45f)] private float searchingChannelSpread = 0.24f;
        [Tooltip("Vertical distance from graph center to the upper/lower waveform after the reaction trace is locked. Lower values make all waveforms converge more tightly, independent of reaction tier.")]
        [SerializeField, Range(0f, 0.45f)] private float lockedChannelSpread = 0.1f;

        [Header("Receiver Stream")]
        [Tooltip("How many receiver samples are pushed into the ring buffer each second.")]
        [SerializeField, Min(8f)] private float receiverSamplesPerSecond = 72f;
        [Tooltip("Amplitude of the always-on idle receiver noise.")]
        [SerializeField, Range(0f, 0.2f)] private float receiverIdleAmplitude = 0.026f;
        [Tooltip("Amplitude of injected token bursts in the receiver stream.")]
        [SerializeField, Range(0f, 0.4f)] private float receiverSignalAmplitude = 0.17f;
        [Tooltip("How strongly the last received token signal keeps looping after its first burst.")]
        [SerializeField, Range(0f, 1f)] private float receiverLoopSignalScale = 0.42f;
        [Tooltip("Minimum duration in seconds before the looped receiver signal wraps back to its start.")]
        [SerializeField, Min(0.1f)] private float receiverMinimumLoopSeconds = 0.8f;
        [Tooltip("Color of the low-level receiver noise between token bursts.")]
        [SerializeField] private Color receiverIdleColor = new(0.08f, 0.42f, 0.14f, 0.55f);
        [Tooltip("Color of the live write head at the newest receiver sample.")]
        [SerializeField] private Color receiverWriteHeadColor = new(0.55f, 1f, 0.42f, 0.88f);
        [Tooltip("Width of the live receiver write head.")]
        [SerializeField, Min(0.5f)] private float receiverWriteHeadThickness = 4.2f;
        [Tooltip("Extra line thickness added while a token burst is being captured.")]
        [SerializeField, Range(0f, 4f)] private float receiverBurstThicknessBoost = 1.5f;

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
        private bool _isComparisonMode;
        private bool _isComparisonCaptureMode;
        private bool _isReceiverStreamMode;
        private bool _receiverLoopPlaybackMode;
        private BrainwaveSemanticProfile _comparisonUnknownProfile;
        private BrainwaveSemanticProfile _comparisonDrawingProfile;
        private float _comparisonCaptureElapsed;
        private float _comparisonCaptureDuration;
        private Color _singleSignalColor;
        private float[] _receiverSamples;
        private Color[] _receiverSampleColors;
        private int _receiverWriteIndex;
        private int _receiverStreamSeed;
        private float _receiverSampleAccumulator;
        private float _receiverSignalTime;
        private BrainwaveSemanticProfile _receiverBurstProfile;
        private Color _receiverBurstColor;
        private float _receiverBurstElapsed;
        private float _receiverBurstDuration;
        private float _receiverBurstIntensity;
        private float _receiverVisualPulseElapsed;
        private float _receiverVisualPulseDuration;
        private float _receiverVisualPulseIntensity;
        private Color _receiverVisualPulseColor;
        private readonly List<ReceiverLoopSegment> _receiverLoopSegments = new();
        private int _receiverLoopSegmentIndex;
        private float _receiverLoopSegmentElapsed;
        private int _seed;
        private float _amplitude = 0.1f;
        private float _noise = 0.04f;
        private float _frequency = 1.8f;
        private float _harmonicRatio = 2.73f;
        private float _harmonicWeight = 0.32f;
        private float _noiseScale = 1f;
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
        private float _startHarmonicRatio;
        private float _startHarmonicWeight;
        private float _startNoiseScale;
        private float _startSpikeChance;
        private float _startSpikeAmplitude;
        private float _startSpikeDensity;
        private float _startChannelSpread;
        private float _targetAmplitude;
        private float _targetNoise;
        private float _targetFrequency;
        private float _targetHarmonicRatio;
        private float _targetHarmonicWeight;
        private float _targetNoiseScale;
        private float _targetSpikeChance;
        private float _targetSpikeAmplitude;
        private float _targetSpikeDensity;
        private float _targetChannelSpread;

        private readonly struct ReceiverLoopSegment
        {
            public ReceiverLoopSegment(
                BrainwaveSemanticProfile profile,
                Color color,
                float duration,
                float intensity)
            {
                Profile = profile;
                Color = color;
                Duration = duration;
                Intensity = intensity;
            }

            public BrainwaveSemanticProfile Profile { get; }
            public Color Color { get; }
            public float Duration { get; }
            public float Intensity { get; }
        }

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
            _singleSignalColor = drawingSignalColor;
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

            if (_isReceiverStreamMode)
            {
                UpdateReceiverStream(Time.deltaTime);
                SetVerticesDirty();
                return;
            }

            _timeOffset += Time.deltaTime * scrollSpeed;
            if (_isComparisonCaptureMode)
            {
                _comparisonCaptureElapsed += Time.deltaTime;
                if (_comparisonCaptureElapsed >= _comparisonCaptureDuration)
                {
                    _isComparisonCaptureMode = false;
                    _comparisonCaptureElapsed = _comparisonCaptureDuration;
                }
            }

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
            _isComparisonMode = false;
            _isReceiverStreamMode = false;
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
            BeginTraceLock(tier, label, sampleIndex, sessionSeed, lockDuration, BrainwaveSemanticProfile.Invalid);
        }

        public void BeginTraceLock(
            ReactionTier tier,
            string label,
            int sampleIndex,
            int sessionSeed,
            float lockDuration,
            BrainwaveSemanticProfile semanticProfile)
        {
            if (!_hasSignal)
            {
                GenerateSearchingProfile(label, sampleIndex, sessionSeed);
                _hasSignal = true;
            }

            _isComparisonMode = false;
            _isReceiverStreamMode = false;
            CaptureCurrentAsLockStart();
            GenerateLockedProfile(tier, label, sampleIndex, sessionSeed, writeToTarget: true, semanticProfile);
            _lockElapsed = 0f;
            _lockDuration = Mathf.Max(0.05f, lockDuration);
            _running = true;
            _isLocking = true;
            SetVerticesDirty();
        }

        public void PlayLocked(ReactionTier tier, string label, int sampleIndex, int sessionSeed)
        {
            PlayLocked(tier, label, sampleIndex, sessionSeed, BrainwaveSemanticProfile.Invalid);
        }

        public void PlayLocked(
            ReactionTier tier,
            string label,
            int sampleIndex,
            int sessionSeed,
            BrainwaveSemanticProfile semanticProfile)
        {
            _isComparisonMode = false;
            _isReceiverStreamMode = false;
            GenerateProfile(tier, label, sampleIndex, sessionSeed, semanticProfile);
            _timeOffset = 0f;
            _hasSignal = true;
            _running = true;
            _isLocking = false;
            SetVerticesDirty();
        }

        public void PlaySignal(BrainwaveSemanticProfile signalProfile)
        {
            PlaySignal(signalProfile, BrainwaveSignalRole.Drawing);
        }

        public void PlaySignal(BrainwaveSemanticProfile signalProfile, BrainwaveSignalRole role)
        {
            _singleSignalColor = GetSignalColor(role);
            PlayComparison(BrainwaveSemanticProfile.Invalid, signalProfile);
        }

        public void PlayComparison(
            BrainwaveSemanticProfile unknownSignalProfile,
            BrainwaveSemanticProfile drawingSignalProfile)
        {
            if (_singleSignalColor.a <= 0f)
            {
                _singleSignalColor = drawingSignalColor;
            }

            _comparisonUnknownProfile = unknownSignalProfile;
            _comparisonDrawingProfile = drawingSignalProfile;
            _isComparisonMode = true;
            _isComparisonCaptureMode = false;
            _isReceiverStreamMode = false;
            _timeOffset = 0f;
            _hasSignal = unknownSignalProfile.IsValid || drawingSignalProfile.IsValid;
            _running = _hasSignal;
            _isLocking = false;
            SetVerticesDirty();
        }

        public void PlayComparisonCapture(
            BrainwaveSemanticProfile unknownSignalProfile,
            BrainwaveSemanticProfile drawingSignalProfile)
        {
            if (_singleSignalColor.a <= 0f)
            {
                _singleSignalColor = drawingSignalColor;
            }

            _comparisonUnknownProfile = unknownSignalProfile;
            _comparisonDrawingProfile = drawingSignalProfile;
            _isComparisonMode = true;
            _isComparisonCaptureMode = drawingSignalProfile.IsValid;
            _isReceiverStreamMode = false;
            _receiverLoopPlaybackMode = false;
            _comparisonCaptureElapsed = 0f;
            _comparisonCaptureDuration = Mathf.Max(0.1f, comparisonCaptureDuration);
            _timeOffset = 0f;
            _hasSignal = unknownSignalProfile.IsValid || drawingSignalProfile.IsValid;
            _running = _hasSignal;
            _isLocking = false;
            SetVerticesDirty();
        }

        public void BeginReceiverStream(int streamSeed)
        {
            _isComparisonMode = false;
            _isReceiverStreamMode = true;
            _receiverLoopPlaybackMode = false;
            _isLocking = false;
            _receiverStreamSeed = streamSeed == 0 ? 1 : streamSeed;
            _receiverSampleAccumulator = 0f;
            _receiverSignalTime = 0f;
            _receiverBurstProfile = BrainwaveSemanticProfile.Invalid;
            _receiverBurstDuration = 0f;
            _receiverBurstElapsed = 0f;
            _receiverBurstIntensity = 0f;
            _receiverVisualPulseElapsed = 0f;
            _receiverVisualPulseDuration = 0f;
            _receiverVisualPulseIntensity = 0f;
            _receiverVisualPulseColor = receiverWriteHeadColor;
            _receiverLoopSegments.Clear();
            _receiverLoopSegmentIndex = 0;
            _receiverLoopSegmentElapsed = 0f;
            EnsureReceiverBuffers();
            FillReceiverIdleSamples();
            _hasSignal = true;
            _running = true;
            SetVerticesDirty();
        }

        public void InjectReceiverSignal(
            BrainwaveSemanticProfile signalProfile,
            BrainwaveSignalRole role,
            float duration,
            float intensity)
        {
            if (!_isReceiverStreamMode)
            {
                BeginReceiverStream(signalProfile.IsValid ? signalProfile.TextureSeed : _seed);
            }

            if (!signalProfile.IsValid)
            {
                return;
            }

            _receiverBurstProfile = signalProfile;
            _receiverLoopPlaybackMode = false;
            _receiverBurstColor = GetSignalColor(role);
            _receiverBurstElapsed = 0f;
            _receiverBurstDuration = Mathf.Max(0.08f, duration);
            _receiverBurstIntensity = Mathf.Max(0.05f, intensity);
            _receiverVisualPulseElapsed = 0f;
            _receiverVisualPulseDuration = Mathf.Clamp(_receiverBurstDuration * 0.45f, 0.18f, 0.65f);
            _receiverVisualPulseIntensity = Mathf.Max(0.05f, intensity);
            _receiverVisualPulseColor = _receiverBurstColor;
            _receiverLoopSegments.Add(new ReceiverLoopSegment(
                signalProfile,
                _receiverBurstColor,
                Mathf.Max(receiverMinimumLoopSeconds, duration),
                Mathf.Max(0.05f, intensity) * Mathf.Clamp01(receiverLoopSignalScale)));
            _hasSignal = true;
            _running = true;
        }

        public void CompleteReceiverSequenceLoop()
        {
            if (!_isReceiverStreamMode || _receiverLoopSegments.Count == 0)
            {
                return;
            }

            _receiverBurstProfile = BrainwaveSemanticProfile.Invalid;
            _receiverBurstElapsed = 0f;
            _receiverBurstDuration = 0f;
            _receiverBurstIntensity = 0f;
            _receiverVisualPulseElapsed = _receiverVisualPulseDuration;
            _receiverVisualPulseIntensity = 0f;
            _receiverLoopPlaybackMode = true;
            _receiverLoopSegmentIndex = 0;
            _receiverLoopSegmentElapsed = 0f;
            _hasSignal = true;
            _running = true;
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
            _isComparisonMode = false;
            _isComparisonCaptureMode = false;
            _isReceiverStreamMode = false;
            _receiverLoopPlaybackMode = false;
            _hasSignal = false;
            _receiverLoopSegments.Clear();
            _receiverLoopSegmentIndex = 0;
            _receiverLoopSegmentElapsed = 0f;
            _receiverVisualPulseElapsed = 0f;
            _receiverVisualPulseDuration = 0f;
            _receiverVisualPulseIntensity = 0f;
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

            if (_isComparisonMode)
            {
                if (_comparisonUnknownProfile.IsValid)
                {
                    DrawComparisonTrace(vh, rect, _comparisonUnknownProfile, unknownSignalColor, lineThickness * 1.25f);
                }

                if (_comparisonDrawingProfile.IsValid)
                {
                    Color drawingColor = _comparisonUnknownProfile.IsValid ? drawingSignalColor : _singleSignalColor;
                    if (_isComparisonCaptureMode)
                    {
                        float captureProgress = Mathf.Clamp01(_comparisonCaptureElapsed / _comparisonCaptureDuration);
                        DrawComparisonCaptureTrace(vh, rect, _comparisonDrawingProfile, drawingColor, captureProgress);
                    }
                    else
                    {
                        DrawComparisonTrace(vh, rect, _comparisonDrawingProfile, drawingColor, lineThickness);
                    }
                }

                return;
            }

            if (_isReceiverStreamMode)
            {
                DrawReceiverStream(vh, rect);
                return;
            }

            DrawChannel(vh, rect, GetChannelCenterYNormalized(0), 0, channelAColor);
            DrawChannel(vh, rect, GetChannelCenterYNormalized(1), 1, channelBColor);
            DrawChannel(vh, rect, GetChannelCenterYNormalized(2), 2, channelCColor);
        }

        private void GenerateProfile(
            ReactionTier tier,
            string label,
            int sampleIndex,
            int sessionSeed,
            BrainwaveSemanticProfile semanticProfile)
        {
            GenerateLockedProfile(tier, label, sampleIndex, sessionSeed, writeToTarget: false, semanticProfile);
        }

        private void GenerateSearchingProfile(string label, int sampleIndex, int sessionSeed)
        {
            _seed = StableHash(label, sampleIndex, sessionSeed) ^ 0x2A6B9651;
            var rng = new System.Random(_seed);

            _amplitude = Jitter(rng, 0.025f, 0.065f);
            _noise = Jitter(rng, 0.035f, 0.085f);
            _frequency = Jitter(rng, 0.75f, 1.55f);
            _harmonicRatio = Jitter(rng, 2f, 3.2f);
            _harmonicWeight = Jitter(rng, 0.18f, 0.38f);
            _noiseScale = Jitter(rng, 0.85f, 1.35f);
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
            bool writeToTarget,
            BrainwaveSemanticProfile semanticProfile)
        {
            _seed = StableHash(label, sampleIndex, sessionSeed);
            var rng = new System.Random(_seed);
            float amplitude;
            float noise;
            float frequency;
            float harmonicRatio = 2.73f;
            float harmonicWeight = 0.32f;
            float noiseScale = 1f;
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

            if (semanticProfile.IsValid)
            {
                _seed = semanticProfile.TextureSeed;
                rng = new System.Random(_seed);
                frequency = Mathf.Lerp(frequency, semanticProfile.BaseFrequency, 0.82f);
                harmonicRatio = semanticProfile.HarmonicRatio;
                harmonicWeight = semanticProfile.HarmonicWeight;
                noiseScale = semanticProfile.NoiseScale;
                noise *= semanticProfile.NoiseScale;
                spikeDensity *= semanticProfile.SpikeDensityScale;
            }

            float sharedPhase = Jitter(rng, 0f, Mathf.PI * 2f);
            SetChannelSpreadValue(GetLockedChannelSpread(), writeToTarget);
            for (int i = 0; i < 3; i++)
            {
                if (semanticProfile.IsValid)
                {
                    SetChannelValue(
                        i,
                        sharedPhase + GetVectorComponent(semanticProfile.ChannelPhaseOffsets, i) + Jitter(rng, -0.012f, 0.012f),
                        GetVectorComponent(semanticProfile.ChannelGainScales, i) * Jitter(rng, 0.97f, 1.03f),
                        GetVectorComponent(semanticProfile.ChannelFrequencyOffsets, i) + Jitter(rng, -0.015f, 0.015f),
                        GetVectorComponent(semanticProfile.ChannelSpikeScales, i) * Jitter(rng, 0.97f, 1.03f),
                        writeToTarget);
                }
                else
                {
                    SetChannelValue(
                        i,
                        sharedPhase + Jitter(rng, -0.05f, 0.05f),
                        Jitter(rng, 0.9f, 1.12f),
                        Jitter(rng, -0.035f, 0.035f),
                        Jitter(rng, 0.9f, 1.18f),
                        writeToTarget);
                }
            }

            SetProfileValues(
                amplitude,
                noise,
                frequency,
                harmonicRatio,
                harmonicWeight,
                noiseScale,
                spikeChance,
                spikeAmplitude,
                spikeDensity,
                writeToTarget);
        }

        private void CaptureCurrentAsLockStart()
        {
            _startAmplitude = _amplitude;
            _startNoise = _noise;
            _startFrequency = _frequency;
            _startHarmonicRatio = _harmonicRatio;
            _startHarmonicWeight = _harmonicWeight;
            _startNoiseScale = _noiseScale;
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
            _harmonicRatio = Mathf.Lerp(_startHarmonicRatio, _targetHarmonicRatio, eased);
            _harmonicWeight = Mathf.Lerp(_startHarmonicWeight, _targetHarmonicWeight, eased);
            _noiseScale = Mathf.Lerp(_startNoiseScale, _targetNoiseScale, eased);
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
            float harmonicRatio,
            float harmonicWeight,
            float noiseScale,
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
                _targetHarmonicRatio = harmonicRatio;
                _targetHarmonicWeight = harmonicWeight;
                _targetNoiseScale = noiseScale;
                _targetSpikeChance = spikeChance;
                _targetSpikeAmplitude = spikeAmplitude;
                _targetSpikeDensity = spikeDensity;
                return;
            }

            _amplitude = amplitude;
            _noise = noise;
            _frequency = frequency;
            _harmonicRatio = harmonicRatio;
            _harmonicWeight = harmonicWeight;
            _noiseScale = noiseScale;
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

        private void DrawComparisonTrace(
            VertexHelper vh,
            Rect rect,
            BrainwaveSemanticProfile profile,
            Color traceColor,
            float thickness)
        {
            if (!profile.IsValid)
            {
                return;
            }

            int count = Mathf.Max(2, sampleCount);
            Vector2 previous = SampleComparisonPoint(rect, profile, 0, count);
            for (int i = 1; i < count; i++)
            {
                Vector2 next = SampleComparisonPoint(rect, profile, i, count);
                AddLine(vh, previous, next, thickness, traceColor);
                previous = next;
            }
        }

        private void DrawComparisonCaptureTrace(
            VertexHelper vh,
            Rect rect,
            BrainwaveSemanticProfile profile,
            Color traceColor,
            float progress)
        {
            if (!profile.IsValid || progress <= 0f)
            {
                return;
            }

            int count = Mathf.Max(2, sampleCount);
            int visibleCount = Mathf.Clamp(Mathf.CeilToInt((count - 1) * progress) + 1, 2, count);
            Vector2 previous = SampleComparisonPoint(rect, profile, 0, count);
            for (int i = 1; i < visibleCount; i++)
            {
                Vector2 next = SampleComparisonPoint(rect, profile, i, count);
                float recency = visibleCount <= 2 ? 1f : i / (float)(visibleCount - 1);
                float afterimage = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.68f, 1f, recency));
                float thickness = lineThickness * Mathf.Lerp(0.95f, 1.22f, recency);

                if (afterimage > 0.001f)
                {
                    Color glowColor = traceColor;
                    glowColor.a *= 0.45f * afterimage;
                    AddLine(vh, previous, next, thickness + lineThickness * 1.15f, glowColor);
                }

                AddLine(vh, previous, next, thickness, traceColor);
                previous = next;
            }

            DrawComparisonCaptureHead(vh, rect, profile, traceColor, visibleCount - 1, count);
        }

        private void DrawComparisonCaptureHead(
            VertexHelper vh,
            Rect rect,
            BrainwaveSemanticProfile profile,
            Color traceColor,
            int index,
            int count)
        {
            Vector2 head = SampleComparisonPoint(rect, profile, Mathf.Clamp(index, 0, count - 1), count);
            Color haloColor = traceColor;
            haloColor.a *= 0.32f;
            float pointSize = Mathf.Max(lineThickness * 2.2f, 5.5f);
            AddFilledRect(vh, head, pointSize * 1.8f, haloColor);
            AddFilledRect(vh, head, pointSize, traceColor);

            Color scanColor = traceColor;
            scanColor.a *= 0.5f;
            AddLine(
                vh,
                new Vector2(head.x, rect.yMin),
                new Vector2(head.x, rect.yMax),
                Mathf.Max(receiverWriteHeadThickness, lineThickness * 0.9f),
                scanColor);
        }

        private void DrawReceiverStream(VertexHelper vh, Rect rect)
        {
            EnsureReceiverBuffers();
            int count = _receiverSamples?.Length ?? 0;
            if (count < 2)
            {
                return;
            }

            bool liveCapture = !_receiverLoopPlaybackMode;
            Vector2 previous = ReceiverPoint(rect, 0, count);
            Color previousColor = ReceiverColor(0, count);
            for (int i = 1; i < count; i++)
            {
                Vector2 next = ReceiverPoint(rect, i, count);
                Color nextColor = ReceiverColor(i, count);
                Color segmentColor = Color.Lerp(previousColor, nextColor, 0.5f);
                float pulse = liveCapture ? GetReceiverVisualPulse() : 0f;
                float thickness = lineThickness * (1.08f + (receiverBurstThicknessBoost * 0.12f * pulse));

                AddLine(vh, previous, next, thickness, segmentColor);
                previous = next;
                previousColor = nextColor;
            }

            if (liveCapture)
            {
                DrawReceiverWriteHead(vh, rect, count);
            }
        }

        private void DrawReceiverWriteHead(VertexHelper vh, Rect rect, int count)
        {
            float pulse = GetReceiverVisualPulse();
            Color headColor = GetReceiverPulseColor();
            headColor.a = Mathf.Clamp01(Mathf.Lerp(0.52f, 0.96f, pulse));

            float x = rect.xMax;
            float headThickness = receiverWriteHeadThickness + (lineThickness * receiverBurstThicknessBoost * pulse);
            AddLine(vh, new Vector2(x, rect.yMin), new Vector2(x, rect.yMax), headThickness, headColor);

            Vector2 latest = ReceiverPoint(rect, count - 1, count);
            float pointSize = Mathf.Max(lineThickness * 2.2f, 5.5f) + (lineThickness * pulse * 1.6f);
            AddFilledRect(vh, latest, pointSize, headColor);
        }

        private float GetReceiverVisualPulse()
        {
            float pulse = 0f;
            if (_receiverVisualPulseDuration > 0.001f && _receiverVisualPulseElapsed < _receiverVisualPulseDuration)
            {
                float normalized = Mathf.Clamp01(_receiverVisualPulseElapsed / _receiverVisualPulseDuration);
                pulse = Mathf.Pow(1f - normalized, 0.62f) * Mathf.Clamp01(_receiverVisualPulseIntensity);
            }

            if (_receiverBurstProfile.IsValid && _receiverBurstDuration > 0.001f && _receiverBurstElapsed < _receiverBurstDuration)
            {
                float normalized = Mathf.Clamp01(_receiverBurstElapsed / _receiverBurstDuration);
                float attack = Mathf.Clamp01(normalized * 8f);
                float decay = Mathf.Pow(1f - normalized, 0.52f);
                pulse = Mathf.Max(pulse, attack * decay * Mathf.Clamp01(_receiverBurstIntensity));
            }

            return Mathf.Clamp01(pulse);
        }

        private Color GetReceiverPulseColor()
        {
            float pulse = GetReceiverVisualPulse();
            return Color.Lerp(receiverWriteHeadColor, _receiverVisualPulseColor, pulse * 0.85f);
        }

        private Vector2 SampleComparisonPoint(
            Rect rect,
            BrainwaveSemanticProfile profile,
            int index,
            int count)
        {
            float normalizedX = index / (float)(count - 1);
            float x = Mathf.Lerp(rect.xMin, rect.xMax, normalizedX);
            float movingX = normalizedX + _timeOffset;
            const int profileChannel = 1;
            float frequency = Mathf.Max(
                0.05f,
                profile.BaseFrequency + GetVectorComponent(profile.ChannelFrequencyOffsets, profileChannel));
            float phase = GetVectorComponent(profile.ChannelPhaseOffsets, profileChannel);
            float amplitude = 0.14f * GetVectorComponent(profile.ChannelGainScales, profileChannel);
            float spikeDensity = 8.5f * profile.SpikeDensityScale;
            float spikeGain = GetVectorComponent(profile.ChannelSpikeScales, profileChannel);

            float wave =
                Mathf.Sin((movingX * frequency * Mathf.PI * 2f) + phase) * amplitude +
                Mathf.Sin((movingX * frequency * profile.HarmonicRatio * Mathf.PI * 2f) + phase * 0.57f) *
                amplitude * profile.HarmonicWeight +
                SampleComparisonNoise(movingX, profile.TextureSeed, profile.NoiseScale) * 0.035f +
                SampleComparisonSpike(movingX, profile.TextureSeed, spikeDensity, spikeGain);

            float centerY = Mathf.Lerp(rect.yMin, rect.yMax, 0.5f);
            float y = centerY + wave * rect.height;
            return new Vector2(x, y);
        }

        private Vector2 ReceiverPoint(Rect rect, int index, int count)
        {
            int bufferIndex = ReceiverBufferIndex(index, count);
            float normalizedX = index / (float)(count - 1);
            float x = Mathf.Lerp(rect.xMin, rect.xMax, normalizedX);
            float centerY = Mathf.Lerp(rect.yMin, rect.yMax, 0.5f);
            float y = centerY + _receiverSamples[bufferIndex] * rect.height;
            return new Vector2(x, y);
        }

        private Color ReceiverColor(int index, int count)
        {
            int bufferIndex = ReceiverBufferIndex(index, count);
            Color sampleColor = _receiverSampleColors[bufferIndex];
            sampleColor.a = Mathf.Clamp01(sampleColor.a);
            return sampleColor;
        }

        private int ReceiverBufferIndex(int index, int count)
        {
            return (_receiverWriteIndex + index) % count;
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
                Mathf.Sin((movingX * frequency * _harmonicRatio * Mathf.PI * 2f) + phase * 0.57f) * _amplitude * _harmonicWeight +
                SampleNoise(movingX, channel) * _noise +
                SampleSpike(movingX, channel);

            wave *= _channelGain[channel];

            float centerY = Mathf.Lerp(rect.yMin, rect.yMax, centerYNormalized);
            float y = centerY + wave * rect.height;
            return new Vector2(x, y);
        }

        private void UpdateReceiverStream(float deltaTime)
        {
            EnsureReceiverBuffers();
            float interval = 1f / Mathf.Max(8f, receiverSamplesPerSecond);
            _receiverSampleAccumulator += Mathf.Max(0f, deltaTime);
            int pushed = 0;
            while (_receiverSampleAccumulator >= interval && pushed < 16)
            {
                PushReceiverSample(interval);
                _receiverSampleAccumulator -= interval;
                pushed++;
            }

            if (_receiverVisualPulseDuration > 0.001f && _receiverVisualPulseElapsed < _receiverVisualPulseDuration)
            {
                _receiverVisualPulseElapsed += Mathf.Max(0f, deltaTime);
            }
        }

        private void PushReceiverSample(float interval)
        {
            float sample = SampleReceiverIdle(_receiverSignalTime);
            Color sampleColor = receiverIdleColor;
            bool burstActive = _receiverBurstProfile.IsValid && _receiverBurstElapsed < _receiverBurstDuration;

            if (burstActive)
            {
                float normalizedElapsed = Mathf.Clamp01(_receiverBurstElapsed / _receiverBurstDuration);
                float attack = Mathf.Clamp01(normalizedElapsed * 8f);
                float decay = Mathf.Pow(1f - normalizedElapsed, 0.52f);
                float envelope = attack * decay;
                float burstTime = WrapReceiverSignalTime(_receiverBurstElapsed, _receiverBurstDuration);
                float signal = SampleReceiverSignal(_receiverBurstProfile, burstTime) *
                               receiverSignalAmplitude *
                               _receiverBurstIntensity *
                               envelope;
                sample += signal;
                float colorStrength = Mathf.Clamp01(envelope * 1.25f);
                sampleColor = Color.Lerp(receiverIdleColor, _receiverBurstColor, colorStrength);
                sampleColor.a = Mathf.Lerp(receiverIdleColor.a, _receiverBurstColor.a, colorStrength);
                _receiverBurstElapsed += interval;
            }
            else if (_receiverLoopSegments.Count > 0)
            {
                ReceiverLoopSegment segment = _receiverLoopSegments[
                    Mathf.Clamp(_receiverLoopSegmentIndex, 0, _receiverLoopSegments.Count - 1)];
                if (segment.Profile.IsValid && segment.Intensity > 0f)
                {
                    float loopTime = WrapReceiverSignalTime(_receiverLoopSegmentElapsed, segment.Duration);
                    float signal = SampleReceiverSignal(segment.Profile, loopTime) *
                                   receiverSignalAmplitude *
                                   segment.Intensity;
                    sample += signal;
                    sampleColor = Color.Lerp(receiverIdleColor, segment.Color, 0.82f);
                    sampleColor.a = Mathf.Lerp(receiverIdleColor.a, segment.Color.a, 0.78f);
                }

                AdvanceReceiverLoop(interval);
            }

            _receiverSamples[_receiverWriteIndex] = Mathf.Clamp(sample, -0.44f, 0.44f);
            _receiverSampleColors[_receiverWriteIndex] = sampleColor;
            _receiverWriteIndex = (_receiverWriteIndex + 1) % _receiverSamples.Length;
            _receiverSignalTime += interval;
        }

        private void AdvanceReceiverLoop(float interval)
        {
            if (_receiverLoopSegments.Count == 0)
            {
                return;
            }

            ReceiverLoopSegment segment = _receiverLoopSegments[
                Mathf.Clamp(_receiverLoopSegmentIndex, 0, _receiverLoopSegments.Count - 1)];
            _receiverLoopSegmentElapsed += Mathf.Max(0f, interval);
            float duration = Mathf.Max(receiverMinimumLoopSeconds, segment.Duration);
            while (_receiverLoopSegmentElapsed >= duration && _receiverLoopSegments.Count > 0)
            {
                _receiverLoopSegmentElapsed -= duration;
                _receiverLoopSegmentIndex = (_receiverLoopSegmentIndex + 1) % _receiverLoopSegments.Count;
                segment = _receiverLoopSegments[_receiverLoopSegmentIndex];
                duration = Mathf.Max(receiverMinimumLoopSeconds, segment.Duration);
            }
        }

        private float SampleReceiverIdle(float signalTime)
        {
            float seedOffset = Mathf.Abs(_receiverStreamSeed % 10000) * 0.017f;
            float slow = Mathf.Sin((signalTime * 1.9f + seedOffset) * Mathf.PI * 2f) * 0.35f;
            float noise = Mathf.PerlinNoise(signalTime * 8.1f + seedOffset, seedOffset * 0.29f) * 2f - 1f;
            return (slow + noise) * receiverIdleAmplitude;
        }

        private float SampleReceiverSignal(BrainwaveSemanticProfile profile, float signalTime)
        {
            const int profileChannel = 1;
            float frequency = Mathf.Max(
                0.05f,
                profile.BaseFrequency + GetVectorComponent(profile.ChannelFrequencyOffsets, profileChannel));
            float phase = GetVectorComponent(profile.ChannelPhaseOffsets, profileChannel);
            float gain = GetVectorComponent(profile.ChannelGainScales, profileChannel);
            float spikeDensity = 8.5f * profile.SpikeDensityScale;
            float spikeGain = GetVectorComponent(profile.ChannelSpikeScales, profileChannel);

            float wave =
                Mathf.Sin((signalTime * frequency * Mathf.PI * 2f) + phase) +
                Mathf.Sin((signalTime * frequency * profile.HarmonicRatio * Mathf.PI * 2f) + phase * 0.57f) *
                profile.HarmonicWeight +
                SampleComparisonNoise(signalTime, profile.TextureSeed, profile.NoiseScale) * 0.24f +
                SampleReceiverSpike(signalTime, profile.TextureSeed, spikeDensity, spikeGain);

            return Mathf.Clamp(wave * gain, -1.4f, 1.4f);
        }

        private static float WrapReceiverSignalTime(float signalTime, float loopDuration)
        {
            float duration = Mathf.Max(0.1f, loopDuration);
            return Mathf.Repeat(signalTime, duration);
        }

        private float SampleReceiverSpike(float signalTime, int seed, float spikeDensity, float spikeGain)
        {
            float spikePosition = signalTime * Mathf.Max(0.1f, spikeDensity);
            int cell = Mathf.FloorToInt(spikePosition);
            float local = spikePosition - cell;
            float chance = 0.05f * Mathf.Max(0.1f, spikeGain);

            if (StableRandom01(cell, 0, seed) > chance)
            {
                return 0f;
            }

            float center = Mathf.Lerp(0.18f, 0.82f, StableRandom01(cell + 17, 0, seed));
            float width = Mathf.Lerp(0.025f, 0.065f, StableRandom01(cell + 31, 0, seed));
            float sign = StableRandom01(cell + 47, 0, seed) > 0.5f ? 1f : -1f;
            float distance = (local - center) / width;
            float envelope = Mathf.Exp(-(distance * distance));
            return sign * envelope * 0.55f;
        }

        private void EnsureReceiverBuffers()
        {
            int count = Mathf.Max(32, sampleCount);
            if (_receiverSamples != null && _receiverSamples.Length == count &&
                _receiverSampleColors != null && _receiverSampleColors.Length == count)
            {
                return;
            }

            _receiverSamples = new float[count];
            _receiverSampleColors = new Color[count];
            _receiverWriteIndex = 0;
            FillReceiverIdleSamples();
        }

        private void FillReceiverIdleSamples()
        {
            if (_receiverSamples == null || _receiverSampleColors == null)
            {
                return;
            }

            float interval = 1f / Mathf.Max(8f, receiverSamplesPerSecond);
            float startTime = 0f;
            for (int i = 0; i < _receiverSamples.Length; i++)
            {
                _receiverSamples[i] = Mathf.Clamp(SampleReceiverIdle(startTime + (i * interval)), -0.44f, 0.44f);
                _receiverSampleColors[i] = receiverIdleColor;
            }

            _receiverWriteIndex = 0;
            _receiverSignalTime = _receiverSamples.Length * interval;
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

        private static float GetVectorComponent(Vector3 value, int index)
        {
            switch (index)
            {
                case 0:
                    return value.x;
                case 1:
                    return value.y;
                default:
                    return value.z;
            }
        }

        private float SampleNoise(float movingX, int channel)
        {
            float seedOffset = Mathf.Abs(_seed % 10000) * 0.013f;
            float scale = Mathf.Max(0.1f, _noiseScale);
            float noise = Mathf.PerlinNoise(
                movingX * 18.7f * scale + seedOffset,
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

        private float SampleComparisonNoise(float movingX, int seed, float noiseScale)
        {
            float seedOffset = Mathf.Abs(seed % 10000) * 0.013f;
            float scale = Mathf.Max(0.1f, noiseScale);
            float noise = Mathf.PerlinNoise(
                movingX * 18.7f * scale + seedOffset,
                seedOffset * 0.37f);
            return noise * 2f - 1f;
        }

        private float SampleComparisonSpike(float movingX, int seed, float spikeDensity, float spikeGain)
        {
            float spikePosition = movingX * Mathf.Max(0.1f, spikeDensity);
            int cell = Mathf.FloorToInt(spikePosition);
            float local = spikePosition - cell;
            float chance = 0.022f * Mathf.Max(0.1f, spikeGain);

            if (StableRandom01(cell, 0, seed) > chance)
            {
                return 0f;
            }

            float center = Mathf.Lerp(0.18f, 0.82f, StableRandom01(cell + 17, 0, seed));
            float width = Mathf.Lerp(0.025f, 0.07f, StableRandom01(cell + 31, 0, seed));
            float sign = StableRandom01(cell + 47, 0, seed) > 0.5f ? 1f : -1f;
            float distance = (local - center) / width;
            float envelope = Mathf.Exp(-(distance * distance));
            return sign * envelope * 0.09f;
        }

        private static void AddFilledRect(VertexHelper vh, Vector2 center, float size, Color rectColor)
        {
            float half = Mathf.Max(0.1f, size) * 0.5f;
            int vertexStart = vh.currentVertCount;

            vh.AddVert(new Vector2(center.x - half, center.y - half), rectColor, Vector2.zero);
            vh.AddVert(new Vector2(center.x - half, center.y + half), rectColor, Vector2.zero);
            vh.AddVert(new Vector2(center.x + half, center.y + half), rectColor, Vector2.zero);
            vh.AddVert(new Vector2(center.x + half, center.y - half), rectColor, Vector2.zero);

            vh.AddTriangle(vertexStart, vertexStart + 1, vertexStart + 2);
            vh.AddTriangle(vertexStart, vertexStart + 2, vertexStart + 3);
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

        private Color GetSignalColor(BrainwaveSignalRole role)
        {
            return role switch
            {
                BrainwaveSignalRole.AlienToken => alienSignalColor,
                BrainwaveSignalRole.UnknownToken => unknownSignalColor,
                _ => drawingSignalColor
            };
        }
    }
}
