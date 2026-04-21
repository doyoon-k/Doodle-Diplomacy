using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DoodleDiplomacy.Core;
using DoodleDiplomacy.Data;
using DoodleDiplomacy.Devices;
using UnityEngine;

namespace DoodleDiplomacy.AI
{
    public class AIPipelineBridge : MonoBehaviour
    {
        private const float SdTimeoutSeconds = 120f;
        private const int LeftObjectSlot = 0;
        private const int RightObjectSlot = 1;
        private const string TelepathyHeader = "[TRANSLATOR v1.0]";
        private const string DefaultSatisfactionStateKey = "satisfaction";
        private const string PreviewSceneReadingStateKey = "preview_scene_reading";
        private const string PreviewVisibleRelationsStateKey = "preview_visible_relations";
        private const string PreviewUncertaintyStateKey = "preview_uncertainty";
        private const string PreviewObjectAPresenceStateKey = "preview_object_a_presence";
        private const string PreviewObjectBPresenceStateKey = "preview_object_b_presence";
        private const string DefaultClickAlienToBeginRoundMessage = "Click the alien to begin the round.";
        private const string DefaultPreparingSdServerMessage = "Preparing the bundled SD server. Wait until the alien becomes interactable.";
        private const string DefaultObjectGeneratorUnavailablePrefix = "Object generator unavailable: ";
        private const string DefaultObjectGeneratorNotReadyMessage = "Object generator is not ready yet.";
        private const string DefaultPreparingRoundObjectsMessage = "Preparing the round objects. Wait until the alien becomes interactable.";
        private const string DefaultStudyObjectsAndClickAlienMessage = "Study the two object images. Click the alien to begin the round.";
        private const string DefaultLlmRuntimeReadyMessage = "LLM runtime is ready.";
        private const string DefaultLlmRuntimeLoadingMessage = "Loading LLM runtime. Game will start after preload completes.";
        private const string DefaultLlmPreloadFailedPrefix = "LLM preload failed: ";
        private const string DefaultLlmRuntimeNotReadyMessage = "LLM runtime is not ready yet.";

        private enum ObjectGenerationAvailabilityState
        {
            Unknown,
            Preparing,
            Ready,
            Failed
        }

        private enum LlmPreparationAvailabilityState
        {
            Unknown,
            Preparing,
            Ready,
            Failed
        }

        private enum PrefetchedRoundState
        {
            Idle,
            Running,
            Ready,
            Failed
        }

        private sealed class PrefetchedRoundData
        {
            public PrefetchedRoundState state;
            public string keywordA = string.Empty;
            public string keywordB = string.Empty;
            public string objectPromptA = string.Empty;
            public string objectPromptB = string.Empty;
            public Texture2D objectTextureA;
            public Texture2D objectTextureB;
            public string error = string.Empty;
            public bool adopted;
        }

        private sealed class RoundKeywordSelectionData
        {
            public string keywordA = string.Empty;
            public string keywordB = string.Empty;
            public string labelA = string.Empty;
            public string labelB = string.Empty;
            public string objectPromptA = string.Empty;
            public string objectPromptB = string.Empty;
            public string warning = string.Empty;

            public bool IsValid =>
                !string.IsNullOrWhiteSpace(objectPromptA) &&
                !string.IsNullOrWhiteSpace(objectPromptB);
        }

        private enum PreviewObjectPresence
        {
            Unknown,
            Visible,
            Unclear,
            Missing
        }

        public static AIPipelineBridge Instance { get; private set; }

        [Header("SD Object Generation")]
        [SerializeField] private StableDiffusionCppSettings sdSettings;
        [SerializeField] private StableDiffusionCppModelProfile sdModelProfile;
        [TextArea(1, 3)]
        [SerializeField] private string objectPromptA = "a glowing alien artifact cube, dark background, dramatic lighting, product photo";
        [TextArea(1, 3)]
        [SerializeField] private string objectPromptB = "an alien crystal sphere, dark background, dramatic lighting, product photo";
        [TextArea(1, 2)]
        [SerializeField] private string sdNegativePrompt = "low quality, blurry, text, watermark, cropped, out of frame, partial object, close-up, zoomed in, occluded, cut off";
        [Header("Pre-generated Object Images")]
        [SerializeField] private bool usePreGeneratedCatalog;
        [SerializeField] private PreGeneratedObjectImageCatalog preGeneratedCatalog;

        [Header("LLM Pipelines")]
        [Tooltip("Alien first-pass preview pipeline. Expected output keys: preview_scene_reading, preview_visible_relations, preview_uncertainty, preview_object_a_presence, preview_object_b_presence")]
        [SerializeField] private PromptPipelineAsset previewDialoguePipeline;
        [Tooltip("Judgment pipeline. Expected output keys: satisfaction, scene_reading, judgment_reason")]
        [SerializeField] private PromptPipelineAsset judgmentPipeline;
        [Tooltip("Telepathy transcript pipeline. Expected output key: response")]
        [SerializeField] private PromptPipelineAsset telepathyPipeline;
        [Tooltip("Round keyword selection pipeline. Expected output key: words")]
        [SerializeField] private PromptPipelineAsset wordsSelectionPipeline;
        [Tooltip("Curated word pair pool. When assigned, pairs are drawn from here first and the LLM pipeline is skipped.")]
        [SerializeField] private DoodleDiplomacy.Data.WordPairPool wordPairPool;
        [Header("Pipeline State Keys")]
        [SerializeField] private string drawingImageKey = "reference_image";
        [SerializeField] private string alienPersonalityKey = "alien_personality";
        [SerializeField] private string targetObjectsKey = "target_objects";
        [SerializeField] private string judgmentSatisfactionKey = "satisfaction";
        [SerializeField] private string judgmentSceneReadingKey = "scene_reading";
        [SerializeField] private string judgmentReasonKey = "judgment_reason";
        [SerializeField] private string wordsSelectionWordsKey = "words";
        [SerializeField] private string selectedKeywordPromptTemplate = "best quality, studio product photo of a single {0}, iconic and instantly recognizable shape, centered composition, full object fully visible, isolated on a clean white background, sharp focus, even soft lighting, high-contrast silhouette, realistic texture, no extra objects, no text, no watermark";

        [Header("References")]
        [SerializeField] private SharedMonitorDisplay monitorDisplay;
        [SerializeField] private DrawingExportBridge drawingExportBridge;
        [SerializeField] private AlienPersonality[] alienPersonalityProfiles = Array.Empty<AlienPersonality>();
        [SerializeField] private AlienPersonality alienPersonality;

        [Header("Text")]
        [SerializeField] private IngameTextTable ingameTextTable;

        [Header("Telepathy Postprocess")]
        [Range(0f, 1f)]
        [SerializeField] private float telepathyCorruptedLineRatio = 0.4f;
        [Range(0f, 1f)]
        [SerializeField] private float telepathyCorruptionStrength = 0.45f;
        [Range(0, 7)]
        [SerializeField] private int telepathyMinCorruptedLines = 1;
        [Range(0, 7)]
        [SerializeField] private int telepathyMaxCorruptedLines = 3;
        [SerializeField] private string[] telepathyNoiseBursts =
        {
            "/|/",
            "##",
            "~:~",
            ".-.",
            "///",
            "|||",
            "::"
        };

        public string LastPreviewDialogue { get; private set; } = "";
        public string LastPreviewSceneReading { get; private set; } = "";
        public string LastPreviewVisibleRelations { get; private set; } = "";
        public string LastPreviewUncertainty { get; private set; } = "";
        public string LastPreviewObjectAPresence => FormatPreviewObjectPresence(_lastPreviewObjectAPresence);
        public string LastPreviewObjectBPresence => FormatPreviewObjectPresence(_lastPreviewObjectBPresence);
        public bool HasPreviewStructuredRead { get; private set; }
        public SatisfactionLevel LastSatisfaction { get; private set; } = SatisfactionLevel.Neutral;
        public string LastJudgmentSceneReading { get; private set; } = "";
        public string LastJudgmentReason { get; private set; } = "";
        public string LastTelepathy { get; private set; } = "";
        public bool LastDrawingWasBlank { get; private set; }
        public bool HasTelepathyResult => !string.IsNullOrWhiteSpace(LastTelepathy);
        public string LastObjectGenerationError { get; private set; } = "";
        public string LastLlmPreparationError { get; private set; } = "";
        public bool IsObjectGenerationReady => _objectGenerationAvailability == ObjectGenerationAvailabilityState.Ready;
        public bool IsObjectGenerationPreparing => _objectGenerationAvailability == ObjectGenerationAvailabilityState.Preparing;
        public bool HasObjectGenerationPreparationFailed => _objectGenerationAvailability == ObjectGenerationAvailabilityState.Failed;
        public bool IsLlmPreparationReady => _llmPreparationAvailability == LlmPreparationAvailabilityState.Ready;
        public bool IsLlmPreparationRunning => _llmPreparationAvailability == LlmPreparationAvailabilityState.Preparing;
        public bool HasLlmPreparationFailed => _llmPreparationAvailability == LlmPreparationAvailabilityState.Failed;
        public bool IsRoundKeywordSelectionInProgress => _isRoundKeywordSelectionInProgress;
        public bool IsRoundKeywordsReady =>
            !_isRoundKeywordSelectionInProgress &&
            !string.IsNullOrWhiteSpace(GetCurrentObjectPromptA()) &&
            !string.IsNullOrWhiteSpace(GetCurrentObjectPromptB());
        public bool IsRoundStartReady => IsObjectGenerationReady && IsRoundKeywordsReady;
        public bool IsNextRoundPrefetchRunning => _prefetchedRound.state == PrefetchedRoundState.Running;
        public bool IsNextRoundPrefetchReady => _prefetchedRound.state == PrefetchedRoundState.Ready;
        public bool HasNextRoundPrefetchFailed => _prefetchedRound.state == PrefetchedRoundState.Failed;
        public string NextRoundPrefetchError => _prefetchedRound.error;
        public string CurrentRoundKeywordA => _currentRoundKeywordA;
        public string CurrentRoundKeywordB => _currentRoundKeywordB;
        public WordPairPool CurrentWordPairPool => wordPairPool;

        public event Action<bool> ObjectGenerationReadinessChanged;
        public event Action<bool> RoundStartReadinessChanged;

        private Texture2D _lastObjTexA;
        private Texture2D _lastObjTexB;
        private Texture2D _progressObjTexA;
        private Texture2D _progressObjTexB;
        private CancellationTokenSource _sdCts;
        private CancellationTokenSource _prefetchCts;
        private Coroutine _prefetchCoroutine;
        private bool _sdPrewarmStarted;
        private Task _sdPrewarmTask;
        private ObjectGenerationAvailabilityState _objectGenerationAvailability = ObjectGenerationAvailabilityState.Unknown;
        private LlmPreparationAvailabilityState _llmPreparationAvailability = LlmPreparationAvailabilityState.Unknown;
        private readonly PrefetchedRoundData _prefetchedRound = new PrefetchedRoundData();
        private bool _isRoundKeywordSelectionInProgress;
        private string _currentObjectPromptA;
        private string _currentObjectPromptB;
        private string _currentRoundKeywordA = string.Empty;
        private string _currentRoundKeywordB = string.Empty;

        private readonly object _sdProgressLock = new object();
        private int _activeSdSlot = -1;
        private long _activeProgressSessionId = -1;
        private int _pendingPreviewSlot = -1;
        private long _pendingPreviewUpdateIndex = -1;
        private int _pendingPreviewWidth;
        private int _pendingPreviewHeight;
        private int _pendingPreviewChannels;
        private byte[] _pendingPreviewBytes;
        private PreviewObjectPresence _lastPreviewObjectAPresence = PreviewObjectPresence.Unknown;
        private PreviewObjectPresence _lastPreviewObjectBPresence = PreviewObjectPresence.Unknown;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            SelectAlienPersonalityProfile();
            _currentObjectPromptA = objectPromptA;
            _currentObjectPromptB = objectPromptB;
            _currentRoundKeywordA = SimplifyTargetObjectPrompt(_currentObjectPromptA);
            _currentRoundKeywordB = SimplifyTargetObjectPrompt(_currentObjectPromptB);
        }

        private void OnEnable()
        {
            StableDiffusionCppRuntime.ProgressChanged += HandleStableDiffusionProgress;
            EnsureObjectGenerationPreparation();
            EnsureLlmPreparation();
        }

        private void OnDisable()
        {
            StableDiffusionCppRuntime.ProgressChanged -= HandleStableDiffusionProgress;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;

            StableDiffusionCppRuntime.ProgressChanged -= HandleStableDiffusionProgress;

            _sdCts?.Cancel();
            _sdCts?.Dispose();
            _sdCts = null;
            CancelPrefetchGeneration("Bridge is being destroyed.");
            ClearPrefetchedRoundData(destroyTextures: true);

            ClearActiveGenerationSlot();
            DestroyTexture(_lastObjTexA);
            DestroyTexture(_lastObjTexB);
            DestroyTexture(_progressObjTexA);
            DestroyTexture(_progressObjTexB);
        }

        public void GenerateObjects(Action onComplete = null)
        {
            StartCoroutine(GenerateObjectsRoutine(_ => onComplete?.Invoke()));
        }

        public void GenerateObjects(Action<bool> onComplete)
        {
            StartCoroutine(GenerateObjectsRoutine(onComplete));
        }

        public void GetPreview(Action<string> onComplete = null)
        {
            StartCoroutine(GetPreviewRoutine(onComplete));
        }

        public void GetJudgment(Action<SatisfactionLevel> onComplete = null)
        {
            StartCoroutine(GetJudgmentRoutine(onComplete));
        }

        public void GetTelepathy(Action<string> onComplete = null)
        {
            StartCoroutine(GetTelepathyRoutine(onComplete));
        }

        public void CancelActiveOperations()
        {
            StopAllCoroutines();

            _sdCts?.Cancel();
            _sdCts?.Dispose();
            _sdCts = null;
            CancelPrefetchGeneration("Cancelling active AI operations.");
            ClearPrefetchedRoundData(destroyTextures: true);

            StableDiffusionCppRuntime.CancelActiveGeneration();
            GamePipelineRunner.Instance?.StopGeneration();

            ClearActiveGenerationSlot();
            ReplaceSlotProgressTexture(LeftObjectSlot, null);
            ReplaceSlotProgressTexture(RightObjectSlot, null);
            monitorDisplay?.SetIdle();
            SetRoundKeywordSelectionInProgress(false, notifyListeners: true);
        }

        public void ResetRound()
        {
            LastPreviewDialogue = "";
            LastPreviewSceneReading = "";
            LastPreviewVisibleRelations = "";
            LastPreviewUncertainty = "";
            _lastPreviewObjectAPresence = PreviewObjectPresence.Unknown;
            _lastPreviewObjectBPresence = PreviewObjectPresence.Unknown;
            HasPreviewStructuredRead = false;
            LastSatisfaction = SatisfactionLevel.Neutral;
            LastJudgmentSceneReading = "";
            LastJudgmentReason = "";
            LastTelepathy = "";
            LastDrawingWasBlank = false;
        }

        public void StartNextRoundPrefetch()
        {
            if (_prefetchedRound.state is PrefetchedRoundState.Running or PrefetchedRoundState.Ready)
            {
                return;
            }

            if (!usePreGeneratedCatalog && sdSettings == null)
            {
                Debug.LogWarning("[AIPipelineBridge] Cannot prefetch next round objects because SD settings are missing.");
                return;
            }

            if (_prefetchedRound.state != PrefetchedRoundState.Idle || _prefetchCoroutine != null || _prefetchCts != null)
            {
                CancelPrefetchGeneration("Restarting next-round prefetch.");
            }
            ClearPrefetchedRoundData(destroyTextures: true);
            _prefetchedRound.state = PrefetchedRoundState.Running;
            _prefetchedRound.error = string.Empty;
            _prefetchedRound.adopted = false;

            _prefetchCts = new CancellationTokenSource();
            _prefetchCoroutine = StartCoroutine(PrefetchNextRoundRoutine(_prefetchCts, _prefetchCts.Token));
        }

        public bool TryAdoptPrefetchedRound()
        {
            switch (_prefetchedRound.state)
            {
                case PrefetchedRoundState.Ready:
                {
                    bool oldRoundStartReady = IsRoundStartReady;
                    _currentRoundKeywordA = _prefetchedRound.keywordA;
                    _currentRoundKeywordB = _prefetchedRound.keywordB;
                    _currentObjectPromptA = _prefetchedRound.objectPromptA;
                    _currentObjectPromptB = _prefetchedRound.objectPromptB;
                    _prefetchedRound.adopted = true;
                    SetRoundKeywordSelectionInProgress(false, notifyListeners: true, previousRoundStartReady: oldRoundStartReady);
                    Debug.Log(
                        $"[AIPipelineBridge] Adopted prefetched round keywords: {_currentRoundKeywordA}, {_currentRoundKeywordB}");
                    return true;
                }
                case PrefetchedRoundState.Running:
                    CancelPrefetchGeneration("Prefetch was still running at round start. Falling back to regular generation.");
                    ClearPrefetchedRoundData(destroyTextures: true);
                    return false;
                case PrefetchedRoundState.Failed:
                    if (!string.IsNullOrWhiteSpace(_prefetchedRound.error))
                    {
                        Debug.LogWarning($"[AIPipelineBridge] Discarding failed prefetched round: {_prefetchedRound.error}");
                    }
                    ClearPrefetchedRoundData(destroyTextures: true);
                    return false;
                default:
                    return false;
            }
        }

        public string GetAlienPersonalitySummary()
        {
            if (alienPersonality == null)
            {
                return "Alien personality: no archetype assigned";
            }

            return $"Alien personality: {alienPersonality.label}";
        }

        private void SelectAlienPersonalityProfile()
        {
            if (alienPersonalityProfiles == null || alienPersonalityProfiles.Length == 0)
            {
                return;
            }

            var validProfiles = new List<AlienPersonality>();
            foreach (AlienPersonality profile in alienPersonalityProfiles)
            {
                if (profile != null)
                {
                    validProfiles.Add(profile);
                }
            }

            if (validProfiles.Count == 0)
            {
                return;
            }

            alienPersonality = validProfiles[UnityEngine.Random.Range(0, validProfiles.Count)];
        }

        public void EnsureObjectGenerationPreparation(bool forceRetry = false)
        {
            if (usePreGeneratedCatalog)
            {
                string catalogError = ValidatePreGeneratedCatalogConfiguration();
                if (string.IsNullOrWhiteSpace(catalogError))
                {
                    SetObjectGenerationAvailability(ObjectGenerationAvailabilityState.Ready, string.Empty, string.Empty);
                }
                else
                {
                    SetObjectGenerationAvailability(
                        ObjectGenerationAvailabilityState.Failed,
                        catalogError,
                        catalogError);
                }

                return;
            }

            if (_objectGenerationAvailability == ObjectGenerationAvailabilityState.Preparing && !forceRetry)
            {
                return;
            }

            if (_objectGenerationAvailability == ObjectGenerationAvailabilityState.Ready && !forceRetry)
            {
                return;
            }

            if (!TryPrepareObjectGenerationRequest(
                    out StableDiffusionCppGenerationRequest request,
                    out bool requiresPrewarm,
                    out string error))
            {
                SetObjectGenerationAvailability(
                    ObjectGenerationAvailabilityState.Failed,
                    error,
                    error);
                return;
            }

            if (!requiresPrewarm)
            {
                SetObjectGenerationAvailability(ObjectGenerationAvailabilityState.Ready, string.Empty, string.Empty);
                return;
            }

            _sdPrewarmStarted = true;
            _sdPrewarmTask = PrewarmStableDiffusionBackendAsync(request);
        }

        public void EnsureLlmPreparation(bool forceRetry = false)
        {
            RefreshLlmPreparationStatus();

            if (IsLlmPreparationRunning && !forceRetry)
            {
                return;
            }

            if (IsLlmPreparationReady && !forceRetry)
            {
                return;
            }

            RuntimeLlamaSharpService runtimeService = ResolveRuntimeLlmService();
            if (runtimeService == null)
            {
                SetLlmPreparationAvailability(
                    LlmPreparationAvailabilityState.Failed,
                    "RuntimeLlamaSharpService is missing.",
                    "Cannot preload LLM because RuntimeLlamaSharpService is missing.");
                return;
            }

            runtimeService.StartPreload();
            UpdateLlmPreparationAvailabilityFromService(runtimeService);
        }

        public void RefreshLlmPreparationStatus()
        {
            if (_llmPreparationAvailability != LlmPreparationAvailabilityState.Preparing &&
                _llmPreparationAvailability != LlmPreparationAvailabilityState.Unknown)
            {
                return;
            }

            RuntimeLlamaSharpService runtimeService = ResolveRuntimeLlmService();
            if (runtimeService == null)
            {
                if (_llmPreparationAvailability == LlmPreparationAvailabilityState.Preparing)
                {
                    SetLlmPreparationAvailability(
                        LlmPreparationAvailabilityState.Failed,
                        "RuntimeLlamaSharpService disappeared while waiting for LLM preload.",
                        "LLM preload failed because RuntimeLlamaSharpService became unavailable.");
                }

                return;
            }

            UpdateLlmPreparationAvailabilityFromService(runtimeService);
        }

        public string GetObjectGenerationAvailabilityMessage()
        {
            if (IsObjectGenerationReady)
            {
                return GetConfiguredText(
                    table => table.clickAlienToBeginRoundMessage,
                    DefaultClickAlienToBeginRoundMessage);
            }

            if (IsObjectGenerationPreparing)
            {
                return GetConfiguredText(
                    table => table.preparingSdServerMessage,
                    DefaultPreparingSdServerMessage);
            }

            if (!string.IsNullOrWhiteSpace(LastObjectGenerationError))
            {
                string prefix = GetConfiguredText(
                    table => table.objectGeneratorUnavailablePrefix,
                    DefaultObjectGeneratorUnavailablePrefix);
                return $"{prefix}{LastObjectGenerationError}";
            }

            return GetConfiguredText(
                table => table.objectGeneratorNotReadyMessage,
                DefaultObjectGeneratorNotReadyMessage);
        }

        public string GetRoundStartAvailabilityMessage()
        {
            RefreshLlmPreparationStatus();
            if (IsRoundKeywordSelectionInProgress)
            {
                return GetConfiguredText(
                    table => table.preparingRoundObjectsMessage,
                    DefaultPreparingRoundObjectsMessage);
            }

            if (!IsObjectGenerationReady)
            {
                return GetObjectGenerationAvailabilityMessage();
            }

            string keywords = GetCurrentRoundKeywordsLabel();
            if (!string.IsNullOrWhiteSpace(keywords))
            {
                return GetConfiguredText(
                    table => table.studyObjectsAndClickAlienMessage,
                    DefaultStudyObjectsAndClickAlienMessage);
            }

            return GetConfiguredText(
                table => table.clickAlienToBeginRoundMessage,
                DefaultClickAlienToBeginRoundMessage);
        }

        public string GetLlmPreparationAvailabilityMessage()
        {
            RefreshLlmPreparationStatus();

            if (IsLlmPreparationReady)
            {
                return GetConfiguredText(
                    table => table.llmRuntimeReadyMessage,
                    DefaultLlmRuntimeReadyMessage);
            }

            if (IsLlmPreparationRunning)
            {
                return GetConfiguredText(
                    table => table.llmRuntimeLoadingMessage,
                    DefaultLlmRuntimeLoadingMessage);
            }

            if (!string.IsNullOrWhiteSpace(LastLlmPreparationError))
            {
                string prefix = GetConfiguredText(
                    table => table.llmPreloadFailedPrefix,
                    DefaultLlmPreloadFailedPrefix);
                return $"{prefix}{LastLlmPreparationError}";
            }

            return GetConfiguredText(
                table => table.llmRuntimeNotReadyMessage,
                DefaultLlmRuntimeNotReadyMessage);
        }

        private string GetConfiguredText(Func<IngameTextTable, string> selector, string fallback)
        {
            IngameTextTable table = ingameTextTable != null ? ingameTextTable : IngameTextTable.LoadDefault();
            if (table == null)
            {
                return fallback;
            }

            string configured = selector(table);
            return string.IsNullOrWhiteSpace(configured) ? fallback : configured;
        }

        public string GetCurrentRoundKeywordsLabel()
        {
            if (string.IsNullOrWhiteSpace(_currentRoundKeywordA) && string.IsNullOrWhiteSpace(_currentRoundKeywordB))
            {
                return string.Empty;
            }

            if (string.IsNullOrWhiteSpace(_currentRoundKeywordA))
            {
                return _currentRoundKeywordB;
            }

            if (string.IsNullOrWhiteSpace(_currentRoundKeywordB))
            {
                return _currentRoundKeywordA;
            }

            return $"{_currentRoundKeywordA}, {_currentRoundKeywordB}";
        }

        public void PrepareRoundKeywords(bool forceRefresh = true)
        {
            if (_isRoundKeywordSelectionInProgress)
            {
                return;
            }

            if (!forceRefresh && IsRoundKeywordsReady)
            {
                return;
            }

            StartCoroutine(PrepareRoundKeywordsRoutine());
        }

        public void DebugGenerateObjectsForPair(
            string wordA,
            string wordB,
            string labelA = null,
            string labelB = null,
            Action<bool> onComplete = null)
        {
            StartCoroutine(DebugGenerateObjectsForPairRoutine(wordA, wordB, labelA, labelB, onComplete));
        }

        private IEnumerator DebugGenerateObjectsForPairRoutine(
            string wordA,
            string wordB,
            string labelA,
            string labelB,
            Action<bool> onComplete)
        {
            string normalizedWordA = wordA?.Trim() ?? string.Empty;
            string normalizedWordB = wordB?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(normalizedWordA) || string.IsNullOrWhiteSpace(normalizedWordB))
            {
                LastObjectGenerationError = "Debug pair generation requires two non-empty words.";
                onComplete?.Invoke(false);
                yield break;
            }

            StopCoroutine(nameof(PrepareRoundKeywordsRoutine));
            _sdCts?.Cancel();
            _sdCts?.Dispose();
            _sdCts = null;
            CancelPrefetchGeneration("Debug pair generation requested.");
            ClearPrefetchedRoundData(destroyTextures: true);
            StableDiffusionCppRuntime.CancelActiveGeneration();

            _currentObjectPromptA = BuildObjectPromptFromKeyword(normalizedWordA, objectPromptA);
            _currentObjectPromptB = BuildObjectPromptFromKeyword(normalizedWordB, objectPromptB);

            _currentRoundKeywordA = string.IsNullOrWhiteSpace(labelA) ? normalizedWordA : labelA.Trim();
            _currentRoundKeywordB = string.IsNullOrWhiteSpace(labelB) ? normalizedWordB : labelB.Trim();
            if (string.IsNullOrWhiteSpace(_currentRoundKeywordA))
            {
                _currentRoundKeywordA = SimplifyTargetObjectPrompt(_currentObjectPromptA);
            }

            if (string.IsNullOrWhiteSpace(_currentRoundKeywordB))
            {
                _currentRoundKeywordB = SimplifyTargetObjectPrompt(_currentObjectPromptB);
            }

            SetRoundKeywordSelectionInProgress(false, notifyListeners: true);
            Debug.Log(
                $"[AIPipelineBridge] Debug pair generation requested: " +
                $"label=({_currentRoundKeywordA}, {_currentRoundKeywordB}) sd=({normalizedWordA}, {normalizedWordB})");

            bool success = false;
            yield return GenerateObjectsRoutine(result => success = result);
            onComplete?.Invoke(success);
        }

        private IEnumerator GenerateObjectsRoutine(Action<bool> onComplete)
        {
            LastObjectGenerationError = string.Empty;

            if (TryConsumePrefetchedRoundTexturesForCurrentRound(out Texture2D prefetchedA, out Texture2D prefetchedB))
            {
                DestroyTexture(_lastObjTexA);
                DestroyTexture(_lastObjTexB);
                _lastObjTexA = prefetchedA;
                _lastObjTexB = prefetchedB;

                DestroyTexture(_progressObjTexA);
                DestroyTexture(_progressObjTexB);
                _progressObjTexA = null;
                _progressObjTexB = null;

                monitorDisplay?.ShowObjects(prefetchedA, prefetchedB);
                Debug.Log("[AIPipelineBridge] Consumed prefetched round objects for presenting.");
                onComplete?.Invoke(true);
                yield break;
            }

            if (_prefetchedRound.state == PrefetchedRoundState.Running)
            {
                CancelPrefetchGeneration("Prefetch not ready at presenting. Falling back to regular generation.");
                ClearPrefetchedRoundData(destroyTextures: true);
            }

            if (usePreGeneratedCatalog)
            {
                if (!TryCreatePreGeneratedRoundTextures(
                        GetCurrentObjectPromptA(),
                        GetCurrentObjectPromptB(),
                        out Texture2D preGeneratedA,
                        out Texture2D preGeneratedB,
                        out string preGeneratedError))
                {
                    LastObjectGenerationError = preGeneratedError;
                    Debug.LogWarning($"[AIPipelineBridge] {LastObjectGenerationError}");
                    monitorDisplay?.SetIdle();
                    onComplete?.Invoke(false);
                    yield break;
                }

                DestroyTexture(_lastObjTexA);
                DestroyTexture(_lastObjTexB);
                _lastObjTexA = preGeneratedA;
                _lastObjTexB = preGeneratedB;

                DestroyTexture(_progressObjTexA);
                DestroyTexture(_progressObjTexB);
                _progressObjTexA = null;
                _progressObjTexB = null;

                ClearActiveGenerationSlot();
                monitorDisplay?.ShowObjects(preGeneratedA, preGeneratedB);
                Debug.Log("[AIPipelineBridge] Presented pre-generated object textures.");
                onComplete?.Invoke(true);
                yield break;
            }

            monitorDisplay?.ShowGenerating(null, null);

            if (sdSettings == null)
            {
                LastObjectGenerationError = "Stable Diffusion settings are not assigned.";
                Debug.LogWarning($"[AIPipelineBridge] {LastObjectGenerationError}");
                monitorDisplay?.SetIdle();
                onComplete?.Invoke(false);
                yield break;
            }

            _sdCts?.Cancel();
            _sdCts?.Dispose();
            _sdCts = new CancellationTokenSource();
            CancellationToken token = _sdCts.Token;

            Texture2D texA = null;
            Texture2D texB = null;

            yield return GenerateObjectIntoSlotRoutine(
                LeftObjectSlot,
                GetCurrentObjectPromptA(),
                () => null,
                () => texB,
                _sdCts,
                token,
                result => texA = result,
                showOnMonitor: true,
                updateLastObjectGenerationError: true);

            if (token.IsCancellationRequested)
            {
                monitorDisplay?.SetIdle();
                onComplete?.Invoke(false);
                yield break;
            }

            if (texA == null)
            {
                DestroyTexture(texA);
                DestroyTexture(texB);
                monitorDisplay?.SetIdle();
                onComplete?.Invoke(false);
                yield break;
            }

            yield return GenerateObjectIntoSlotRoutine(
                RightObjectSlot,
                GetCurrentObjectPromptB(),
                () => texA,
                () => null,
                _sdCts,
                token,
                result => texB = result,
                showOnMonitor: true,
                updateLastObjectGenerationError: true);

            if (token.IsCancellationRequested || texB == null)
            {
                DestroyTexture(texA);
                DestroyTexture(texB);
                monitorDisplay?.SetIdle();
                onComplete?.Invoke(false);
                yield break;
            }

            DestroyTexture(_lastObjTexA);
            DestroyTexture(_lastObjTexB);
            _lastObjTexA = texA;
            _lastObjTexB = texB;

            DestroyTexture(_progressObjTexA);
            DestroyTexture(_progressObjTexB);
            _progressObjTexA = null;
            _progressObjTexB = null;

            ClearActiveGenerationSlot();
            monitorDisplay?.ShowObjects(texA, texB);
            Debug.Log($"[AIPipelineBridge] Object generation finished. texA={texA != null}, texB={texB != null}");
            onComplete?.Invoke(true);
        }

        private IEnumerator GenerateObjectIntoSlotRoutine(
            int slot,
            string prompt,
            Func<Texture2D> stableLeftProvider,
            Func<Texture2D> stableRightProvider,
            CancellationTokenSource requestCts,
            CancellationToken token,
            Action<Texture2D> onResult,
            bool showOnMonitor = true,
            bool updateLastObjectGenerationError = true,
            Action<string> onError = null)
        {
            void ReportError(string message)
            {
                if (updateLastObjectGenerationError)
                {
                    LastObjectGenerationError = message;
                }
                onError?.Invoke(message);
            }

            Debug.Log(
                $"[AIPipelineBridge] SD slot {slot} start " +
                $"promptLen={prompt?.Length ?? 0}, showOnMonitor={showOnMonitor}, " +
                $"preferSdServerBackend={sdSettings != null && sdSettings.preferSdServerBackend}, " +
                $"preferPersistentNativeWorker={sdSettings != null && sdSettings.preferPersistentNativeWorker}, " +
                $"progressPreview={sdSettings != null && sdSettings.enablePersistentWorkerProgressPreview}");

            if (showOnMonitor)
            {
                SetActiveGenerationSlot(slot);
                UpdateMonitorProgress(stableLeftProvider?.Invoke(), stableRightProvider?.Invoke());
            }

            Task<StableDiffusionCppGenerationResult> task = GenerateSingleObjectAsync(prompt, token);
            float elapsed = 0f;

            while (!task.IsCompleted)
            {
                elapsed += Time.deltaTime;
                if (showOnMonitor)
                {
                    ApplyPendingProgressPreview(stableLeftProvider?.Invoke(), stableRightProvider?.Invoke());
                }

                if (elapsed >= SdTimeoutSeconds)
                {
                    string timeoutError = $"SD object slot {slot} timed out while generating alien objects.";
                    Debug.LogWarning($"[AIPipelineBridge] {timeoutError}");
                    ReportError(timeoutError);
                    requestCts?.Cancel();
                    if (showOnMonitor)
                    {
                        ClearActiveGenerationSlot();
                    }
                    onResult?.Invoke(null);
                    yield break;
                }

                yield return null;
            }

            if (showOnMonitor)
            {
                ApplyPendingProgressPreview(stableLeftProvider?.Invoke(), stableRightProvider?.Invoke());
                ClearActiveGenerationSlot();
            }

            StableDiffusionCppGenerationResult result;
            if (task.IsCanceled || token.IsCancellationRequested)
            {
                ReportError("Alien object generation was cancelled.");
                Debug.LogWarning($"[AIPipelineBridge] SD slot {slot} was cancelled.");
                if (showOnMonitor)
                {
                    ReplaceSlotProgressTexture(slot, null);
                }
                onResult?.Invoke(null);
                if (showOnMonitor)
                {
                    UpdateMonitorProgress(stableLeftProvider?.Invoke(), stableRightProvider?.Invoke());
                }
                yield break;
            }

            if (task.IsFaulted)
            {
                Exception failure = task.Exception?.GetBaseException() ?? task.Exception;
                ReportError(failure?.Message ?? $"SD slot {slot} failed unexpectedly.");
                Debug.LogError($"[AIPipelineBridge] SD slot {slot} failed: {failure}");
                if (showOnMonitor)
                {
                    ReplaceSlotProgressTexture(slot, null);
                }
                onResult?.Invoke(null);
                if (showOnMonitor)
                {
                    UpdateMonitorProgress(stableLeftProvider?.Invoke(), stableRightProvider?.Invoke());
                }
                yield break;
            }

            result = task.Result;
            if (!result.Success)
            {
                ReportError(string.IsNullOrWhiteSpace(result.ErrorMessage)
                    ? $"SD slot {slot} failed without an error message."
                    : result.ErrorMessage);
            }

            Debug.Log(BuildSdSlotDiagnosticsLog(slot, result));

            Texture2D finalTexture = null;
            if (result.Success && result.OutputFiles?.Count > 0)
            {
                bool loaded = StableDiffusionCppImageIO.TryLoadTextureFromFile(
                    result.OutputFiles[0],
                    out finalTexture,
                    out string loadError);
                Debug.Log(
                    $"[AIPipelineBridge] Slot {slot} texture load={loaded}, " +
                    $"path={result.OutputFiles[0]}, err={loadError}");
                if (!loaded)
                {
                    ReportError(string.IsNullOrWhiteSpace(loadError)
                        ? $"Generated slot {slot} output could not be loaded."
                        : loadError);
                }
            }
            else if (result.Success)
            {
                ReportError($"Generated slot {slot} returned no image files.");
            }

            if (showOnMonitor)
            {
                ReplaceSlotProgressTexture(slot, null);
            }
            onResult?.Invoke(finalTexture);
            if (showOnMonitor)
            {
                UpdateMonitorProgress(
                    slot == LeftObjectSlot ? finalTexture : stableLeftProvider?.Invoke(),
                    slot == RightObjectSlot ? finalTexture : stableRightProvider?.Invoke());
            }
        }

        private string BuildSdSlotDiagnosticsLog(int slot, StableDiffusionCppGenerationResult result)
        {
            if (result == null)
            {
                return $"[AIPipelineBridge] SD slot {slot} diagnostics unavailable: null result.";
            }

            string backend = InferSdBackend(result.CommandLine, result.StdOut, result.StdErr);
            string commandLine = TrimForLog(result.CommandLine, 900);
            string stdOutTail = BuildLogTail(result.StdOut, maxLines: 8, maxChars: 800);
            string stdErrTail = BuildLogTail(result.StdErr, maxLines: 8, maxChars: 800);

            var sb = new StringBuilder(1400);
            sb.Append($"[AIPipelineBridge] SD slot {slot} diagnostics: ")
              .Append($"backend={backend}, success={result.Success}, cancelled={result.Cancelled}, timedOut={result.TimedOut}, ")
              .Append($"exitCode={result.ExitCode}, elapsed={result.Elapsed.TotalSeconds:0.00}s, ")
              .Append($"files={result.OutputFiles?.Count ?? 0}, outputDir={result.OutputDirectory}, ")
              .Append($"gpuTelemetry={result.GpuTelemetryAvailable}, peakVramMiB={result.PeakGpuMemoryMiB}, ")
              .Append($"peakGpuUtil={result.PeakGpuUtilizationPercent}, samples={result.GpuTelemetrySamples}");

            if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
            {
                sb.Append($", error={result.ErrorMessage}");
            }

            if (!string.IsNullOrWhiteSpace(commandLine))
            {
                sb.Append("\n  command: ").Append(commandLine);
            }

            if (!string.IsNullOrWhiteSpace(stdOutTail))
            {
                sb.Append("\n  stdout(tail):\n").Append(stdOutTail);
            }

            if (!string.IsNullOrWhiteSpace(stdErrTail))
            {
                sb.Append("\n  stderr(tail):\n").Append(stdErrTail);
            }

            return sb.ToString();
        }

        private static string InferSdBackend(string commandLine, string stdOut, string stdErr)
        {
            if (!string.IsNullOrWhiteSpace(commandLine))
            {
                if (commandLine.IndexOf("[bundled-sd-server]", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    commandLine.IndexOf("sd-server.exe", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return "sd-server";
                }

                if (commandLine.IndexOf("sd-cli.exe", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return "sd-cli";
                }
            }

            if (!string.IsNullOrWhiteSpace(stdOut) &&
                stdOut.IndexOf("[NativeWorker]", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "native-worker";
            }

            if (!string.IsNullOrWhiteSpace(stdErr) &&
                stdErr.IndexOf("[NativeWorker]", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "native-worker";
            }

            return "unknown";
        }

        private static string BuildLogTail(string raw, int maxLines, int maxChars)
        {
            if (string.IsNullOrWhiteSpace(raw) || maxLines <= 0 || maxChars <= 0)
            {
                return string.Empty;
            }

            string normalized = raw.Replace("\r\n", "\n").Replace('\r', '\n');
            string[] lines = normalized.Split('\n');
            int start = Mathf.Max(0, lines.Length - maxLines);
            var sb = new StringBuilder(Mathf.Min(maxChars, 1024));

            for (int i = start; i < lines.Length; i++)
            {
                string line = lines[i]?.TrimEnd();
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (sb.Length > 0)
                {
                    sb.Append('\n');
                }

                if (sb.Length + line.Length > maxChars)
                {
                    int remain = Mathf.Max(0, maxChars - sb.Length);
                    if (remain > 0)
                    {
                        sb.Append(line.Substring(0, remain));
                    }
                    break;
                }

                sb.Append(line);
            }

            return sb.ToString();
        }

        private static string TrimForLog(string value, int maxChars)
        {
            if (string.IsNullOrWhiteSpace(value) || maxChars <= 0)
            {
                return string.Empty;
            }

            if (value.Length <= maxChars)
            {
                return value;
            }

            return value.Substring(0, maxChars) + "...(truncated)";
        }

        private async Task<StableDiffusionCppGenerationResult> GenerateSingleObjectAsync(
            string prompt,
            CancellationToken cancellationToken)
        {
            bool isReady = await EnsureObjectGenerationReadyAsync(cancellationToken);
            if (!isReady)
            {
                string error = string.IsNullOrWhiteSpace(LastObjectGenerationError)
                    ? "Object generator is not ready."
                    : LastObjectGenerationError;
                return StableDiffusionCppGenerationResult.Failed(
                    error,
                    -1,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    TimeSpan.Zero);
            }

            StableDiffusionCppGenerationRequest request = BuildObjectGenerationRequest(prompt);
            if (request == null)
            {
                return StableDiffusionCppGenerationResult.Failed(
                    "StableDiffusionCppSettings is not assigned.",
                    -1,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    TimeSpan.Zero);
            }

            return await StableDiffusionCppRuntime.GenerateTxt2ImgAsync(sdSettings, request, cancellationToken);
        }

        private IEnumerator PrefetchNextRoundRoutine(
            CancellationTokenSource prefetchCts,
            CancellationToken token)
        {
            Debug.Log("[AIPipelineBridge] Starting next-round prefetch.");
            try
            {
                RoundKeywordSelectionData selection = null;
                yield return ResolveRoundKeywordSelectionRoutine(result => selection = result);
                if (token.IsCancellationRequested)
                {
                    yield break;
                }

                if (selection == null || !selection.IsValid)
                {
                    SetPrefetchedRoundFailed("Next-round prefetch failed during keyword selection.");
                    yield break;
                }

                _prefetchedRound.keywordA = selection.labelA;
                _prefetchedRound.keywordB = selection.labelB;
                _prefetchedRound.objectPromptA = selection.objectPromptA;
                _prefetchedRound.objectPromptB = selection.objectPromptB;
                _prefetchedRound.adopted = false;
                _prefetchedRound.error = string.Empty;

                if (!string.IsNullOrWhiteSpace(selection.warning))
                {
                    Debug.LogWarning($"[AIPipelineBridge] Prefetch keyword selection fallback: {selection.warning}");
                }

                if (usePreGeneratedCatalog)
                {
                    if (!TryCreatePreGeneratedRoundTextures(
                            selection.objectPromptA,
                            selection.objectPromptB,
                            out Texture2D cachedA,
                            out Texture2D cachedB,
                            out string preGeneratedError))
                    {
                        DestroyTexture(cachedA);
                        DestroyTexture(cachedB);
                        SetPrefetchedRoundFailed(
                            string.IsNullOrWhiteSpace(preGeneratedError)
                                ? "Next-round prefetch failed while loading pre-generated object images."
                                : preGeneratedError);
                        yield break;
                    }

                    if (token.IsCancellationRequested)
                    {
                        DestroyTexture(cachedA);
                        DestroyTexture(cachedB);
                        yield break;
                    }

                    DestroyTexture(_prefetchedRound.objectTextureA);
                    DestroyTexture(_prefetchedRound.objectTextureB);
                    _prefetchedRound.objectTextureA = cachedA;
                    _prefetchedRound.objectTextureB = cachedB;
                    _prefetchedRound.state = PrefetchedRoundState.Ready;
                    _prefetchedRound.error = string.Empty;
                    _prefetchedRound.adopted = false;

                    Debug.Log(
                        $"[AIPipelineBridge] Next-round prefetch ready from pre-generated catalog: " +
                        $"label=({_prefetchedRound.keywordA}, {_prefetchedRound.keywordB})");
                    yield break;
                }

                Texture2D texA = null;
                Texture2D texB = null;
                string prefetchError = string.Empty;

                yield return GenerateObjectIntoSlotRoutine(
                    LeftObjectSlot,
                    selection.objectPromptA,
                    () => null,
                    () => null,
                    prefetchCts,
                    token,
                    result => texA = result,
                    showOnMonitor: false,
                    updateLastObjectGenerationError: false,
                    onError: error =>
                    {
                        if (string.IsNullOrWhiteSpace(prefetchError))
                        {
                            prefetchError = error;
                        }
                    });

                if (token.IsCancellationRequested)
                {
                    DestroyTexture(texA);
                    yield break;
                }

                if (texA == null)
                {
                    DestroyTexture(texA);
                    SetPrefetchedRoundFailed(
                        string.IsNullOrWhiteSpace(prefetchError)
                            ? "Next-round prefetch failed while generating object A."
                            : prefetchError);
                    yield break;
                }

                yield return GenerateObjectIntoSlotRoutine(
                    RightObjectSlot,
                    selection.objectPromptB,
                    () => null,
                    () => null,
                    prefetchCts,
                    token,
                    result => texB = result,
                    showOnMonitor: false,
                    updateLastObjectGenerationError: false,
                    onError: error =>
                    {
                        if (string.IsNullOrWhiteSpace(prefetchError))
                        {
                            prefetchError = error;
                        }
                    });

                if (token.IsCancellationRequested)
                {
                    DestroyTexture(texA);
                    DestroyTexture(texB);
                    yield break;
                }

                if (texB == null)
                {
                    DestroyTexture(texA);
                    DestroyTexture(texB);
                    SetPrefetchedRoundFailed(
                        string.IsNullOrWhiteSpace(prefetchError)
                            ? "Next-round prefetch failed while generating object B."
                            : prefetchError);
                    yield break;
                }

                DestroyTexture(_prefetchedRound.objectTextureA);
                DestroyTexture(_prefetchedRound.objectTextureB);
                _prefetchedRound.objectTextureA = texA;
                _prefetchedRound.objectTextureB = texB;
                _prefetchedRound.state = PrefetchedRoundState.Ready;
                _prefetchedRound.error = string.Empty;
                _prefetchedRound.adopted = false;

                Debug.Log(
                    $"[AIPipelineBridge] Next-round prefetch ready: label=({_prefetchedRound.keywordA}, {_prefetchedRound.keywordB})");
            }
            finally
            {
                _prefetchCoroutine = null;
                if (ReferenceEquals(_prefetchCts, prefetchCts))
                {
                    _prefetchCts?.Dispose();
                    _prefetchCts = null;
                }
            }
        }

        private bool TryConsumePrefetchedRoundTexturesForCurrentRound(out Texture2D texA, out Texture2D texB)
        {
            texA = null;
            texB = null;

            if (_prefetchedRound.state != PrefetchedRoundState.Ready || !_prefetchedRound.adopted)
            {
                return false;
            }

            bool matchesCurrentRound =
                string.Equals(_prefetchedRound.objectPromptA, GetCurrentObjectPromptA(), StringComparison.Ordinal) &&
                string.Equals(_prefetchedRound.objectPromptB, GetCurrentObjectPromptB(), StringComparison.Ordinal);
            if (!matchesCurrentRound)
            {
                Debug.LogWarning("[AIPipelineBridge] Prefetched round prompts no longer match current round. Discarding cache.");
                ClearPrefetchedRoundData(destroyTextures: true);
                return false;
            }

            texA = _prefetchedRound.objectTextureA;
            texB = _prefetchedRound.objectTextureB;
            _prefetchedRound.objectTextureA = null;
            _prefetchedRound.objectTextureB = null;

            if (texA == null || texB == null)
            {
                DestroyTexture(texA);
                DestroyTexture(texB);
                ClearPrefetchedRoundData(destroyTextures: true);
                return false;
            }

            ClearPrefetchedRoundData(destroyTextures: false);
            return true;
        }

        private void SetPrefetchedRoundFailed(string error)
        {
            _prefetchedRound.state = PrefetchedRoundState.Failed;
            _prefetchedRound.error = error ?? "Unknown prefetch error.";
            _prefetchedRound.adopted = false;
            Debug.LogWarning($"[AIPipelineBridge] {_prefetchedRound.error}");
        }

        private void CancelPrefetchGeneration(string reason)
        {
            bool wasRunning = _prefetchedRound.state == PrefetchedRoundState.Running;
            if (_prefetchCoroutine != null)
            {
                StopCoroutine(_prefetchCoroutine);
                _prefetchCoroutine = null;
            }

            _prefetchCts?.Cancel();
            _prefetchCts?.Dispose();
            _prefetchCts = null;

            if (wasRunning)
            {
                StableDiffusionCppRuntime.CancelActiveGeneration();
            }

            if (!string.IsNullOrWhiteSpace(reason))
            {
                Debug.Log($"[AIPipelineBridge] {reason}");
            }
        }

        private void ClearPrefetchedRoundData(bool destroyTextures)
        {
            if (destroyTextures)
            {
                DestroyTexture(_prefetchedRound.objectTextureA);
                DestroyTexture(_prefetchedRound.objectTextureB);
            }

            _prefetchedRound.objectTextureA = null;
            _prefetchedRound.objectTextureB = null;
            _prefetchedRound.keywordA = string.Empty;
            _prefetchedRound.keywordB = string.Empty;
            _prefetchedRound.objectPromptA = string.Empty;
            _prefetchedRound.objectPromptB = string.Empty;
            _prefetchedRound.error = string.Empty;
            _prefetchedRound.adopted = false;
            _prefetchedRound.state = PrefetchedRoundState.Idle;
        }

        private string ValidatePreGeneratedCatalogConfiguration()
        {
            if (!usePreGeneratedCatalog)
            {
                return string.Empty;
            }

            if (preGeneratedCatalog == null)
            {
                return "Pre-generated object image catalog is not assigned.";
            }

            return string.Empty;
        }

        private bool TryCreatePreGeneratedRoundTextures(
            string promptA,
            string promptB,
            out Texture2D textureA,
            out Texture2D textureB,
            out string error)
        {
            textureA = null;
            textureB = null;
            error = ValidatePreGeneratedCatalogConfiguration();
            if (!string.IsNullOrWhiteSpace(error))
            {
                return false;
            }

            if (!TryCreatePreGeneratedTextureFromPrompt(promptA, out textureA, out string objectAError))
            {
                error = $"Object A: {objectAError}";
                return false;
            }

            if (!TryCreatePreGeneratedTextureFromPrompt(promptB, out textureB, out string objectBError))
            {
                DestroyTexture(textureA);
                textureA = null;
                error = $"Object B: {objectBError}";
                return false;
            }

            return true;
        }

        private bool TryCreatePreGeneratedTextureFromPrompt(
            string prompt,
            out Texture2D runtimeTexture,
            out string error)
        {
            runtimeTexture = null;
            error = ValidatePreGeneratedCatalogConfiguration();
            if (!string.IsNullOrWhiteSpace(error))
            {
                return false;
            }

            string promptKey = PreGeneratedObjectImageKeyUtility.ComputePromptKey(prompt);
            if (string.IsNullOrWhiteSpace(promptKey))
            {
                error = "Prompt is empty, so no pre-generated key could be resolved.";
                return false;
            }

            if (!preGeneratedCatalog.TryGetTextureByKey(promptKey, out Texture2D sourceTexture) || sourceTexture == null)
            {
                string promptText = prompt?.Trim() ?? string.Empty;
                error = $"Missing pre-generated image for prompt '{promptText}' (key='{promptKey}').";
                return false;
            }

            if (!TryCloneTextureForRuntime(sourceTexture, out runtimeTexture, out string cloneError))
            {
                error = $"Failed to clone pre-generated image for key '{promptKey}': {cloneError}";
                return false;
            }

            return true;
        }

        private static bool TryCloneTextureForRuntime(Texture2D source, out Texture2D clone, out string error)
        {
            clone = null;
            error = string.Empty;
            if (source == null)
            {
                error = "Source texture is null.";
                return false;
            }

            RenderTexture temp = null;
            RenderTexture previous = RenderTexture.active;
            try
            {
                int width = Mathf.Max(1, source.width);
                int height = Mathf.Max(1, source.height);
                temp = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(source, temp);
                RenderTexture.active = temp;

                clone = new Texture2D(width, height, TextureFormat.RGBA32, false)
                {
                    name = source.name + "_RuntimeCopy",
                    filterMode = source.filterMode,
                    wrapMode = source.wrapMode
                };
                clone.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                clone.Apply(updateMipmaps: false, makeNoLongerReadable: false);
                return true;
            }
            catch (Exception ex)
            {
                if (clone != null)
                {
                    UnityEngine.Object.Destroy(clone);
                    clone = null;
                }

                error = ex.Message;
                return false;
            }
            finally
            {
                RenderTexture.active = previous;
                if (temp != null)
                {
                    RenderTexture.ReleaseTemporary(temp);
                }
            }
        }

        private string BuildAlienPersonalityPromptContext()
        {
            if (alienPersonality == null)
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            builder.AppendLine($"archetype: {alienPersonality.label}");
            builder.AppendLine($"core values: {alienPersonality.coreValues}");
            builder.AppendLine($"likes: {alienPersonality.likes}");
            builder.AppendLine($"dislikes: {alienPersonality.dislikes}");
            builder.AppendLine("judgment note: satisfaction is not a task-completion score. It measures how pleasing or off-putting the visible scene feels to this archetype.");
            builder.AppendLine("debiasing note: do not judge with your own moral framework or generic human ethics. Judge strictly by this archetype's values, even when they conflict with ordinary moral approval or disapproval.");
            builder.Append("evidence note: stay close to visible evidence. If the image is ambiguous or requires extra assumptions, prefer neutral.");

            string designerNote = alienPersonality.description?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(designerNote))
            {
                builder.AppendLine();
                builder.Append($"designer note: {designerNote}");
            }

            return builder.ToString();
        }

        private string FormatTelepathyTerminalOutput(string rawTranscript)
        {
            List<string> lines = ExtractTelepathyTranscriptLines(rawTranscript);
            if (lines.Count == 0)
            {
                return $"{TelepathyHeader}\n> Decoding alien signal...\n> _";
            }

            int corruptionCount = GetTelepathyCorruptionCount(lines.Count);
            var corruptedLineIndices = new HashSet<int>();
            while (corruptionCount > 0 && corruptedLineIndices.Count < corruptionCount)
            {
                corruptedLineIndices.Add(UnityEngine.Random.Range(0, lines.Count));
            }

            var builder = new StringBuilder();
            builder.AppendLine(TelepathyHeader);

            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i];
                if (corruptedLineIndices.Contains(i))
                {
                    line = ApplySignalCorruption(line);
                }

                builder.Append("> ").AppendLine(line);
            }

            builder.Append("> _");
            return builder.ToString();
        }

        private string BuildFallbackJudgmentTranscript()
        {
            var lines = new List<string>();

            if (LastDrawingWasBlank)
            {
                lines.Add("No marks were detected on the submitted drawing.");
                lines.Add("No relationship between the prompt objects was communicated.");
                lines.Add("A visible action or connection is required.");
                return string.Join("\n", lines);
            }

            if (!string.IsNullOrWhiteSpace(LastJudgmentSceneReading))
            {
                lines.Add(LastJudgmentSceneReading.Trim());
            }

            if (!string.IsNullOrWhiteSpace(LastJudgmentReason))
            {
                lines.Add(LastJudgmentReason.Trim());
            }

            string personalityLine = BuildFallbackJudgmentPriorityLine();
            if (!string.IsNullOrWhiteSpace(personalityLine))
            {
                lines.Add(personalityLine);
            }

            string satisfactionLine = BuildFallbackSatisfactionLine();
            if (!string.IsNullOrWhiteSpace(satisfactionLine))
            {
                lines.Add(satisfactionLine);
            }

            if (lines.Count == 0)
            {
                lines.Add("The delegation could not infer a clear situation from the submitted drawing.");
            }

            return string.Join("\n", lines);
        }

        private IEnumerator TryGenerateTelepathyTranscriptRoutine(Action<string> onComplete)
        {
            onComplete?.Invoke(string.Empty);

            if (telepathyPipeline == null)
            {
                Debug.LogWarning("[AIPipelineBridge] Telepathy pipeline is not assigned. Falling back to formatted judgment transcript.");
                yield break;
            }

            if (GamePipelineRunner.Instance == null)
            {
                Debug.LogWarning("[AIPipelineBridge] GamePipelineRunner is missing. Falling back to formatted judgment transcript.");
                yield break;
            }

            var state = new PipelineState();
            if (!string.IsNullOrWhiteSpace(LastJudgmentSceneReading))
            {
                state.SetString(judgmentSceneReadingKey, LastJudgmentSceneReading);
            }

            if (!string.IsNullOrWhiteSpace(LastJudgmentReason))
            {
                state.SetString(judgmentReasonKey, LastJudgmentReason);
            }

            SetSatisfactionStateValue(state, LastSatisfaction);

            if (alienPersonality != null)
            {
                state.SetString(alienPersonalityKey, BuildAlienPersonalityPromptContext());
            }

            bool done = false;
            PipelineState finalState = null;
            GamePipelineRunner.Instance.RunPipeline(telepathyPipeline, state, result =>
            {
                finalState = result;
                done = true;
            });
            yield return new WaitUntil(() => done);

            string transcript = string.Empty;
            if (finalState != null &&
                finalState.TryGetString(PromptPipelineConstants.AnswerKey, out string telepathyText))
            {
                transcript = SanitizeTelepathyTranscript(telepathyText);
            }

            if (string.IsNullOrWhiteSpace(transcript))
            {
                if (finalState != null &&
                    finalState.TryGetString(PromptPipelineConstants.ErrorKey, out string pipelineError) &&
                    !string.IsNullOrWhiteSpace(pipelineError))
                {
                    Debug.LogWarning($"[AIPipelineBridge] Telepathy pipeline returned no usable transcript. Error: {pipelineError}");
                }
                else
                {
                    Debug.LogWarning("[AIPipelineBridge] Telepathy pipeline returned no usable transcript. Falling back to formatted judgment transcript.");
                }
            }

            onComplete?.Invoke(transcript);
        }

        private string BuildTargetObjectsSummary()
        {
            // Use the simple noun labels (_currentRoundKeywordA/B) for LLM context,
            // not the full SD prompt string.
            string left = _currentRoundKeywordA;
            string right = _currentRoundKeywordB;

            if (string.IsNullOrWhiteSpace(left) && string.IsNullOrWhiteSpace(right))
            {
                return string.Empty;
            }

            if (string.IsNullOrWhiteSpace(left))
            {
                return $"Object B: {right}";
            }

            if (string.IsNullOrWhiteSpace(right))
            {
                return $"Object A: {left}";
            }

            return $"Object A: {left}\nObject B: {right}";
        }

        private static string SimplifyTargetObjectPrompt(string prompt)
        {
            if (string.IsNullOrWhiteSpace(prompt))
            {
                return string.Empty;
            }

            string simplified = prompt.Trim();
            int commaIndex = simplified.IndexOf(',');
            if (commaIndex >= 0)
            {
                simplified = simplified.Substring(0, commaIndex).Trim();
            }

            string[] removablePrefixes =
            {
                "a ",
                "an ",
                "the "
            };

            foreach (string prefix in removablePrefixes)
            {
                if (simplified.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    simplified = simplified.Substring(prefix.Length).Trim();
                    break;
                }
            }

            return simplified;
        }

        private string BuildFallbackJudgmentPriorityLine()
        {
            if (alienPersonality == null)
            {
                return string.Empty;
            }

            return alienPersonality.fallbackJudgmentPriority ?? string.Empty;
        }

        private string BuildFallbackSatisfactionLine()
        {
            if (LastDrawingWasBlank)
            {
                return "The delegation cannot judge an empty submission.";
            }

            return LastSatisfaction switch
            {
                SatisfactionLevel.VerySatisfied => "This strongly satisfies the delegation.",
                SatisfactionLevel.Satisfied => "This satisfies the delegation.",
                SatisfactionLevel.Neutral => "The delegation remains unconvinced.",
                SatisfactionLevel.Dissatisfied => "This dissatisfies the delegation.",
                SatisfactionLevel.VeryDissatisfied => "This strongly dissatisfies the delegation.",
                _ => "The delegation remains unconvinced."
            };
        }

        private bool TryGetCurrentDrawingForPipeline(out Texture2D texture)
        {
            LastDrawingWasBlank = false;
            texture = null;

            if (drawingExportBridge == null)
            {
                Debug.LogWarning("[AIPipelineBridge] DrawingExportBridge is not assigned.");
                return false;
            }

            if (!drawingExportBridge.TryHasVisibleDrawing(out bool hasVisibleDrawing, out string blankCheckError))
            {
                Debug.LogWarning($"[AIPipelineBridge] Could not inspect drawing content: {blankCheckError}");
            }
            else if (!hasVisibleDrawing)
            {
                LastDrawingWasBlank = true;
                Debug.Log("[AIPipelineBridge] Drawing is blank.");
                return false;
            }

            if (!drawingExportBridge.TryGetCurrentTexture(out texture, out string err) || texture == null)
            {
                Debug.LogWarning($"[AIPipelineBridge] No drawing texture available: {err}");
                return false;
            }

            return true;
        }

        private static string BuildFallbackPreviewDialogue(bool isBlankDrawing)
        {
            if (isBlankDrawing)
            {
                return "It looks like no visible marks are on the canvas yet.";
            }

            return "It looks like the scene is still unclear from the current drawing.";
        }

        private void SetPreviewRead(
            string sceneReading,
            string visibleRelations,
            string uncertainty = null,
            string previewDialogue = null,
            PreviewObjectPresence objectAPresence = PreviewObjectPresence.Unknown,
            PreviewObjectPresence objectBPresence = PreviewObjectPresence.Unknown)
        {
            LastPreviewSceneReading = sceneReading?.Trim() ?? string.Empty;
            LastPreviewVisibleRelations = visibleRelations?.Trim() ?? string.Empty;
            LastPreviewUncertainty = uncertainty?.Trim() ?? string.Empty;
            _lastPreviewObjectAPresence = objectAPresence;
            _lastPreviewObjectBPresence = objectBPresence;
            LastPreviewDialogue = EnsurePreviewDialogueStyle(
                SanitizePreviewDialogue(
                    BuildPreviewDialogueFromRead(
                        LastPreviewSceneReading,
                        previewDialogue)));
        }

        private void SetFallbackPreviewRead(bool isBlankDrawing)
        {
            HasPreviewStructuredRead = false;

            if (isBlankDrawing)
            {
                SetPreviewRead(
                    "The submitted drawing is blank.",
                    "No visible action, interaction, or connection is shown.",
                    "There is not enough visible information to interpret the scene.",
                    BuildFallbackPreviewDialogue(true),
                    PreviewObjectPresence.Missing,
                    PreviewObjectPresence.Missing);
                return;
            }

            SetPreviewRead(
                "The drawing is hard to read.",
                "No clear interaction or cause-and-effect is visible.",
                "The main action and intent are ambiguous.",
                BuildFallbackPreviewDialogue(false),
                PreviewObjectPresence.Unclear,
                PreviewObjectPresence.Unclear);
        }

        private static string BuildPreviewDialogueFromRead(
            string sceneReading,
            string explicitPreviewDialogue = null)
        {
            if (!string.IsNullOrWhiteSpace(explicitPreviewDialogue))
            {
                return explicitPreviewDialogue;
            }

            string sentence = sceneReading?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(sentence))
            {
                return BuildFallbackPreviewDialogue(false);
            }

            sentence = sentence.TrimEnd();
            if (sentence.Length == 0)
            {
                return BuildFallbackPreviewDialogue(false);
            }

            char lastChar = sentence[sentence.Length - 1];
            if (lastChar is not '.' and not '!' and not '?')
            {
                sentence += ".";
            }

            return sentence;
        }

        private static string EnsurePreviewDialogueStyle(string dialogue)
        {
            const string fallback = "It looks like the scene is still unclear from the current drawing.";
            const string legacyQuestion = "Does that match what you intended?";

            if (string.IsNullOrWhiteSpace(dialogue))
            {
                return fallback;
            }

            string text = dialogue.Trim();
            int legacyQuestionIndex = text.IndexOf(legacyQuestion, StringComparison.OrdinalIgnoreCase);
            if (legacyQuestionIndex >= 0)
            {
                text = text.Substring(0, legacyQuestionIndex).TrimEnd(' ', '.', '!', '?');
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                return fallback;
            }

            if (text.StartsWith("It looks like", StringComparison.OrdinalIgnoreCase))
            {
                return EnsureSentenceEnding(text);
            }

            if (text.StartsWith("This looks like", StringComparison.OrdinalIgnoreCase))
            {
                text = text.Substring("This looks like".Length).TrimStart();
            }
            else if (text.StartsWith("Looks like", StringComparison.OrdinalIgnoreCase))
            {
                text = text.Substring("Looks like".Length).TrimStart();
            }

            text = text.TrimStart('-', ':', ' ');
            if (string.IsNullOrWhiteSpace(text))
            {
                return fallback;
            }

            if (text.Length > 1 && char.IsUpper(text[0]) && char.IsLower(text[1]))
            {
                text = char.ToLowerInvariant(text[0]) + text.Substring(1);
            }

            return EnsureSentenceEnding($"It looks like {text}");
        }

        private static string EnsureSentenceEnding(string sentence)
        {
            if (string.IsNullOrWhiteSpace(sentence))
            {
                return sentence;
            }

            string trimmed = sentence.Trim();
            char lastChar = trimmed[trimmed.Length - 1];
            return lastChar is '.' or '!' or '?' ? trimmed : $"{trimmed}.";
        }

        private void ApplyPreviewReadFromState(PipelineState finalState)
        {
            string sceneReading = finalState != null &&
                                  finalState.TryGetString(PreviewSceneReadingStateKey, out string sceneValue)
                ? sceneValue
                : string.Empty;

            string visibleRelations = finalState != null &&
                                      finalState.TryGetString(PreviewVisibleRelationsStateKey, out string relationValue)
                ? relationValue
                : string.Empty;

            string uncertainty = finalState != null &&
                                 finalState.TryGetString(PreviewUncertaintyStateKey, out string uncertaintyValue)
                ? uncertaintyValue
                : string.Empty;
            PreviewObjectPresence objectAPresence = finalState != null &&
                                                    finalState.TryGetString(PreviewObjectAPresenceStateKey, out string objectAValue)
                ? ParsePreviewObjectPresence(objectAValue)
                : PreviewObjectPresence.Unknown;
            PreviewObjectPresence objectBPresence = finalState != null &&
                                                    finalState.TryGetString(PreviewObjectBPresenceStateKey, out string objectBValue)
                ? ParsePreviewObjectPresence(objectBValue)
                : PreviewObjectPresence.Unknown;

            HasPreviewStructuredRead =
                !string.IsNullOrWhiteSpace(sceneReading) ||
                !string.IsNullOrWhiteSpace(visibleRelations) ||
                !string.IsNullOrWhiteSpace(uncertainty) ||
                objectAPresence != PreviewObjectPresence.Unknown ||
                objectBPresence != PreviewObjectPresence.Unknown;

            if (string.IsNullOrWhiteSpace(sceneReading))
            {
                sceneReading = "The drawing is hard to read.";
            }

            if (string.IsNullOrWhiteSpace(visibleRelations))
            {
                visibleRelations = "No clear interaction or cause-and-effect is visible.";
            }

            if (string.IsNullOrWhiteSpace(uncertainty))
            {
                uncertainty = "The main action and intent are ambiguous.";
            }

            SetPreviewRead(
                sceneReading,
                visibleRelations,
                uncertainty,
                objectAPresence: objectAPresence,
                objectBPresence: objectBPresence);
        }

        private bool HasPreviewReadContext()
        {
            return !string.IsNullOrWhiteSpace(LastPreviewSceneReading) ||
                   !string.IsNullOrWhiteSpace(LastPreviewVisibleRelations) ||
                   !string.IsNullOrWhiteSpace(LastPreviewUncertainty);
        }

        private void SetNeutralJudgmentFallback(string sceneReading, string judgmentReason)
        {
            LastSatisfaction = SatisfactionLevel.Neutral;
            LastJudgmentSceneReading = sceneReading?.Trim() ?? string.Empty;
            LastJudgmentReason = judgmentReason?.Trim() ?? string.Empty;

            string rawTranscript = BuildFallbackJudgmentTranscript();
            LastTelepathy = string.IsNullOrWhiteSpace(rawTranscript)
                ? string.Empty
                : FormatTelepathyTerminalOutput(rawTranscript);
        }

        private static string SanitizePreviewDialogue(string dialogue)
        {
            if (string.IsNullOrWhiteSpace(dialogue))
            {
                return "It looks like the scene is still unclear from the current drawing.";
            }

            string sanitized = dialogue.Trim();
            string[] blockedPrefixes =
            {
                "Here is",
                "Here's",
                "Here is an uncertain preview line",
                "Here's an uncertain preview line",
                "Uncertain preview line",
                "Preview line",
                "Adjutant:",
                "Alien:"
            };

            bool removedPrefix = true;
            while (removedPrefix)
            {
                removedPrefix = false;
                foreach (string prefix in blockedPrefixes)
                {
                    if (!sanitized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    int splitIndex = sanitized.IndexOf(':');
                    if (splitIndex >= 0 && splitIndex + 1 < sanitized.Length)
                    {
                        sanitized = sanitized.Substring(splitIndex + 1).Trim();
                    }
                    else
                    {
                        sanitized = sanitized.Substring(prefix.Length).TrimStart(' ', '-', ':');
                    }

                    removedPrefix = true;
                    break;
                }
            }

            string[] blockedQuestions =
            {
                "Do you want me to elaborate",
                "Would you like me to elaborate",
                "Want me to elaborate",
                "Should I elaborate",
                "Do you want me to explain",
                "Would you like me to explain",
                "Should I explain",
                "Would you like more detail",
                "Do you want more detail",
                "Would you like me to describe",
                "Should I describe another aspect"
            };

            foreach (string blockedQuestion in blockedQuestions)
            {
                int blockedIndex = sanitized.IndexOf(blockedQuestion, StringComparison.OrdinalIgnoreCase);
                if (blockedIndex < 0)
                {
                    continue;
                }

                sanitized = sanitized.Substring(0, blockedIndex).TrimEnd(' ', '.', '!', '?');
                if (string.IsNullOrWhiteSpace(sanitized))
                {
                    sanitized = "It looks like the scene is still unclear from the current drawing.";
                }

                break;
            }

            return string.IsNullOrWhiteSpace(sanitized)
                ? "It looks like the scene is still unclear from the current drawing."
                : sanitized;
        }

        private static string SanitizeTelepathyTranscript(string transcript)
        {
            if (string.IsNullOrWhiteSpace(transcript))
            {
                return string.Empty;
            }

            string sanitized = transcript.Trim().Replace("```", string.Empty).Trim();
            string[] blockedPrefixes =
            {
                "Here is the transcript",
                "Here is a transcript",
                "Transcript:",
                "Internal transcript:",
                "Alien transcript:",
                "Translated transcript:"
            };

            bool removedPrefix = true;
            while (removedPrefix)
            {
                removedPrefix = false;
                foreach (string prefix in blockedPrefixes)
                {
                    if (!sanitized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    int splitIndex = sanitized.IndexOf(':');
                    if (splitIndex >= 0 && splitIndex + 1 < sanitized.Length)
                    {
                        sanitized = sanitized.Substring(splitIndex + 1).Trim();
                    }
                    else
                    {
                        sanitized = sanitized.Substring(prefix.Length).TrimStart(' ', '-', ':');
                    }

                    removedPrefix = true;
                    break;
                }
            }

            return sanitized;
        }

        private static List<string> ExtractTelepathyTranscriptLines(string rawTranscript)
        {
            var lines = new List<string>();
            if (string.IsNullOrWhiteSpace(rawTranscript))
            {
                return lines;
            }

            string[] rawLines = rawTranscript.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string rawLine in rawLines)
            {
                string line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line) ||
                    line.StartsWith("[TRANSLATOR", StringComparison.OrdinalIgnoreCase) ||
                    line == "_")
                {
                    continue;
                }

                if (line.StartsWith("> ", StringComparison.Ordinal))
                {
                    line = line.Substring(2).Trim();
                }
                else if (line.StartsWith(">", StringComparison.Ordinal))
                {
                    line = line.Substring(1).Trim();
                }

                if (!string.IsNullOrWhiteSpace(line))
                {
                    lines.Add(line);
                }
            }

            return lines;
        }

        private int GetTelepathyCorruptionCount(int totalLines)
        {
            if (totalLines <= 0 || telepathyCorruptedLineRatio <= 0f || telepathyCorruptionStrength <= 0f)
            {
                return 0;
            }

            int minCorrupted = Mathf.Clamp(telepathyMinCorruptedLines, 0, totalLines);
            int maxCorrupted = Mathf.Clamp(telepathyMaxCorruptedLines, minCorrupted, totalLines);
            int desired = Mathf.RoundToInt(totalLines * telepathyCorruptedLineRatio);
            desired = Mathf.Max(desired, minCorrupted);
            desired = Mathf.Min(desired, maxCorrupted);
            return desired;
        }

        private string ApplySignalCorruption(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return "[////]";
            }

            switch (UnityEngine.Random.Range(0, 4))
            {
                case 0:
                    return RedactRandomWords(line);
                case 1:
                    return InsertStaticBurst(line);
                case 2:
                    return TruncateWithSignalDrop(line);
                default:
                    return DistortWordWithNoise(line);
            }
        }

        private string RedactRandomWords(string line)
        {
            string[] words = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0)
            {
                return "[////]";
            }

            if (words.Length == 1)
            {
                return "[////]";
            }

            int startIndex = UnityEngine.Random.Range(0, words.Length);
            int maxLength = telepathyCorruptionStrength >= 0.66f ? 3 : 2;
            int length = Mathf.Min(UnityEngine.Random.Range(1, maxLength + 1), words.Length - startIndex);
            words[startIndex] = BuildNoiseMask();

            for (int i = 1; i < length; i++)
            {
                words[startIndex + i] = string.Empty;
            }

            return string.Join(" ", words).Replace("  ", " ").Trim();
        }

        private string InsertStaticBurst(string line)
        {
            string[] words = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length < 2)
            {
                return $"{GetRandomNoiseBurst()} {line}";
            }

            int insertIndex = UnityEngine.Random.Range(1, words.Length);
            var rebuilt = new StringBuilder();
            string burst = BuildNoiseBurst();

            for (int i = 0; i < words.Length; i++)
            {
                if (i == insertIndex)
                {
                    rebuilt.Append(burst).Append(' ');
                }

                rebuilt.Append(words[i]);
                if (i < words.Length - 1)
                {
                    rebuilt.Append(' ');
                }
            }

            return rebuilt.ToString();
        }

        private string DistortWordWithNoise(string line)
        {
            string[] words = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0)
            {
                return BuildNoiseBurst();
            }

            int wordIndex = UnityEngine.Random.Range(0, words.Length);
            words[wordIndex] = InjectNoiseIntoWord(words[wordIndex]);
            return string.Join(" ", words);
        }

        private string TruncateWithSignalDrop(string line)
        {
            if (line.Length < 18)
            {
                return $"{line} ... {GetRandomNoiseBurst()}";
            }

            int minimumCutoff = Mathf.Max(6, Mathf.RoundToInt(line.Length * (0.35f + (1f - telepathyCorruptionStrength) * 0.2f)));
            int cutoff = UnityEngine.Random.Range(minimumCutoff, line.Length - 2);
            return $"{line.Substring(0, cutoff).TrimEnd()} ... {BuildNoiseBurst()}";
        }

        private string InjectNoiseIntoWord(string word)
        {
            if (string.IsNullOrWhiteSpace(word))
            {
                return GetRandomNoiseBurst();
            }

            char[] chars = word.ToCharArray();
            int replacements = Mathf.Clamp(Mathf.RoundToInt(chars.Length * telepathyCorruptionStrength), 1, Mathf.Max(1, chars.Length - 1));

            for (int i = 0; i < replacements; i++)
            {
                int index = UnityEngine.Random.Range(0, chars.Length);
                chars[index] = GetRandomNoiseCharacter();
            }

            return new string(chars);
        }

        private string BuildNoiseMask()
        {
            int length = telepathyCorruptionStrength >= 0.66f ? 6 : 4;
            return $"[{new string(GetRandomNoiseCharacter(), length)}]";
        }

        private string BuildNoiseBurst()
        {
            string burst = GetRandomNoiseBurst();
            return telepathyCorruptionStrength >= 0.66f
                ? $"{burst}{GetRandomNoiseBurst()}"
                : burst;
        }

        private string GetRandomNoiseBurst()
        {
            if (telepathyNoiseBursts == null || telepathyNoiseBursts.Length == 0)
            {
                return "/|/";
            }

            string burst = telepathyNoiseBursts[UnityEngine.Random.Range(0, telepathyNoiseBursts.Length)];
            return string.IsNullOrWhiteSpace(burst) ? "/|/" : burst.Trim();
        }

        private char GetRandomNoiseCharacter()
        {
            const string noiseChars = "/|#~:.=-";
            return noiseChars[UnityEngine.Random.Range(0, noiseChars.Length)];
        }

        private StableDiffusionCppGenerationRequest BuildObjectGenerationRequest(string prompt)
        {
            if (sdSettings == null)
            {
                return null;
            }

            var request = new StableDiffusionCppGenerationRequest
            {
                prompt = prompt,
                offloadToCpu = sdSettings.defaultOffloadToCpu,
                clipOnCpu = sdSettings.defaultClipOnCpu,
                vaeTiling = sdSettings.defaultVaeTiling,
                diffusionFlashAttention = sdSettings.defaultDiffusionFlashAttention,
                useCacheMode = sdSettings.defaultUseCacheMode,
                cacheMode = sdSettings.defaultCacheMode,
                cacheOption = sdSettings.defaultCacheOption,
                cachePreset = sdSettings.defaultCachePreset,
                persistOutputToRequestedDirectory = false
            };

            StableDiffusionCppModelProfile selectedProfile = sdModelProfile;
            if (selectedProfile == null)
                selectedProfile = sdSettings.GetActiveModelProfile();

            if (selectedProfile != null)
            {
                selectedProfile.ApplyDefaultsTo(request);
                request.prompt = prompt;
                request.modelPathOverride = selectedProfile.modelPath ?? string.Empty;
                request.controlNetPathOverride = selectedProfile.controlNetPath ?? string.Empty;
            }
            else if (sdSettings != null)
            {
                sdSettings.TryApplyActiveProfileDefaults(request);
                request.prompt = prompt;
            }

            if (!string.IsNullOrWhiteSpace(sdNegativePrompt))
                request.negativePrompt = sdNegativePrompt;

            return request;
        }

        private bool TryPrepareObjectGenerationRequest(
            out StableDiffusionCppGenerationRequest request,
            out bool requiresPrewarm,
            out string error)
        {
            request = BuildObjectGenerationRequest(GetCurrentObjectPromptA());
            requiresPrewarm = false;
            error = string.Empty;

            if (sdSettings == null)
            {
                error = "Stable Diffusion settings are not assigned.";
                return false;
            }

            if (request == null)
            {
                error = "StableDiffusionCppSettings is not assigned.";
                return false;
            }

            StableDiffusionCppPreparationResult prep = StableDiffusionCppRuntime.PrepareRuntime(
                sdSettings,
                forceReinstall: false,
                modelPathOverride: request.modelPathOverride);
            if (!prep.Success)
            {
                error = prep.ErrorMessage;
                return false;
            }

            requiresPrewarm = StableDiffusionCppSdServerWorker.CanUsePersistentServer(sdSettings, prep, request);
            return true;
        }

        private async Task PrewarmStableDiffusionBackendAsync(StableDiffusionCppGenerationRequest request)
        {
            SetObjectGenerationAvailability(
                ObjectGenerationAvailabilityState.Preparing,
                string.Empty,
                "Preparing the bundled SD server. Wait until the alien becomes interactable.");

            try
            {
                bool prewarmed = await StableDiffusionCppRuntime.PrewarmTxt2ImgAsync(sdSettings, request);
                if (prewarmed)
                {
                    Debug.Log("[AIPipelineBridge] Prewarmed bundled sd-server backend.");
                    SetObjectGenerationAvailability(ObjectGenerationAvailabilityState.Ready, string.Empty, string.Empty);
                    return;
                }

                string error = "Bundled SD server prewarm did not complete successfully.";
                Debug.LogWarning($"[AIPipelineBridge] {error}");
                SetObjectGenerationAvailability(ObjectGenerationAvailabilityState.Failed, error, error);
            }
            catch (Exception ex)
            {
                string error = $"Failed to prewarm bundled SD server backend: {ex.Message}";
                Debug.LogWarning($"[AIPipelineBridge] {error}");
                SetObjectGenerationAvailability(ObjectGenerationAvailabilityState.Failed, error, error);
            }
        }

        private async Task<bool> EnsureObjectGenerationReadyAsync(CancellationToken cancellationToken)
        {
            EnsureObjectGenerationPreparation();

            Task prewarmTask = _sdPrewarmTask;
            if (prewarmTask != null)
            {
                try
                {
                    while (!prewarmTask.IsCompleted)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        await Task.WhenAny(prewarmTask, Task.Delay(50, cancellationToken));
                    }

                    await prewarmTask;
                }
                catch (OperationCanceledException)
                {
                    LastObjectGenerationError = "Object generation preparation was cancelled.";
                    return false;
                }
                catch (Exception ex)
                {
                    LastObjectGenerationError = ex.Message;
                    return false;
                }
            }

            return IsObjectGenerationReady;
        }

        private void SetObjectGenerationAvailability(
            ObjectGenerationAvailabilityState nextState,
            string error,
            string logMessage)
        {
            bool oldReady = IsObjectGenerationReady;
            bool oldRoundStartReady = IsRoundStartReady;
            _objectGenerationAvailability = nextState;
            LastObjectGenerationError = error ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(logMessage))
            {
                Debug.Log($"[AIPipelineBridge] {logMessage}");
            }

            bool newReady = IsObjectGenerationReady;
            if (oldReady != newReady)
            {
                ObjectGenerationReadinessChanged?.Invoke(newReady);
                NotifyRoundStartReadinessChanged(oldRoundStartReady, forceNotify: true);
                return;
            }

            if (nextState is ObjectGenerationAvailabilityState.Failed or ObjectGenerationAvailabilityState.Preparing)
            {
                ObjectGenerationReadinessChanged?.Invoke(newReady);
                NotifyRoundStartReadinessChanged(oldRoundStartReady, forceNotify: true);
            }
        }

        private void SetLlmPreparationAvailability(
            LlmPreparationAvailabilityState nextState,
            string error,
            string logMessage)
        {
            _llmPreparationAvailability = nextState;
            LastLlmPreparationError = error ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(logMessage))
            {
                Debug.Log($"[AIPipelineBridge] {logMessage}");
            }
        }

        private RuntimeLlamaSharpService ResolveRuntimeLlmService()
        {
            if (GamePipelineRunner.Instance != null)
            {
                return GamePipelineRunner.Instance.RuntimeService;
            }

            return FindFirstObjectByType<RuntimeLlamaSharpService>();
        }

        private void UpdateLlmPreparationAvailabilityFromService(RuntimeLlamaSharpService runtimeService)
        {
            if (runtimeService == null)
            {
                SetLlmPreparationAvailability(
                    LlmPreparationAvailabilityState.Failed,
                    "RuntimeLlamaSharpService is missing.",
                    "LLM preload failed because RuntimeLlamaSharpService is missing.");
                return;
            }

            if (runtimeService.IsModelReady)
            {
                SetLlmPreparationAvailability(LlmPreparationAvailabilityState.Ready, string.Empty, string.Empty);
                return;
            }

            if (runtimeService.IsPreloadInProgress || !runtimeService.IsPreloadComplete)
            {
                SetLlmPreparationAvailability(LlmPreparationAvailabilityState.Preparing, string.Empty, string.Empty);
                return;
            }

            SetLlmPreparationAvailability(
                LlmPreparationAvailabilityState.Failed,
                "LLM preload completed without a ready model.",
                "LLM preload completed without a ready model.");
        }

        private IEnumerator PrepareRoundKeywordsRoutine()
        {
            bool oldRoundStartReady = IsRoundStartReady;
            _currentRoundKeywordA = string.Empty;
            _currentRoundKeywordB = string.Empty;
            _currentObjectPromptA = string.Empty;
            _currentObjectPromptB = string.Empty;
            SetRoundKeywordSelectionInProgress(true, notifyListeners: true, previousRoundStartReady: oldRoundStartReady);

            RoundKeywordSelectionData selection = null;
            yield return ResolveRoundKeywordSelectionRoutine(result => selection = result);

            if (selection == null || !selection.IsValid)
            {
                ApplyFallbackRoundKeywords("Round keyword selection failed. Using fallback prompts.");
                yield break;
            }

            _currentRoundKeywordA = selection.labelA;
            _currentRoundKeywordB = selection.labelB;
            _currentObjectPromptA = selection.objectPromptA;
            _currentObjectPromptB = selection.objectPromptB;

            if (!string.IsNullOrWhiteSpace(selection.warning))
            {
                Debug.LogWarning($"[AIPipelineBridge] {selection.warning}");
            }

            Debug.Log(
                $"[AIPipelineBridge] Round keywords selected: label=({_currentRoundKeywordA}, {_currentRoundKeywordB}) " +
                $"sd=({selection.keywordA}, {selection.keywordB})");
            SetRoundKeywordSelectionInProgress(false, notifyListeners: true, previousRoundStartReady: oldRoundStartReady);
        }

        private IEnumerator ResolveRoundKeywordSelectionRoutine(Action<RoundKeywordSelectionData> onComplete)
        {
            var selection = new RoundKeywordSelectionData();

            // Curated pool takes priority over LLM pipeline.
            if (wordPairPool != null &&
                wordPairPool.TryGetRandomPair(out string poolWordA, out string poolWordB, out string poolLabelA, out string poolLabelB))
            {
                selection.keywordA = poolWordA?.Trim() ?? string.Empty;
                selection.keywordB = poolWordB?.Trim() ?? string.Empty;
                selection.labelA = string.IsNullOrWhiteSpace(poolLabelA) ? selection.keywordA : poolLabelA.Trim();
                selection.labelB = string.IsNullOrWhiteSpace(poolLabelB) ? selection.keywordB : poolLabelB.Trim();
                selection.objectPromptA = BuildObjectPromptFromKeyword(selection.keywordA, objectPromptA);
                selection.objectPromptB = BuildObjectPromptFromKeyword(selection.keywordB, objectPromptB);
                onComplete?.Invoke(selection);
                yield break;
            }

            if (wordsSelectionPipeline == null || GamePipelineRunner.Instance == null)
            {
                selection.warning = "Round keyword selection pipeline is not assigned. Using fallback prompts.";
                selection.keywordA = SimplifyTargetObjectPrompt(objectPromptA);
                selection.keywordB = SimplifyTargetObjectPrompt(objectPromptB);
                selection.labelA = selection.keywordA;
                selection.labelB = selection.keywordB;
                selection.objectPromptA = objectPromptA;
                selection.objectPromptB = objectPromptB;
                onComplete?.Invoke(selection);
                yield break;
            }

            bool done = false;
            PipelineState finalState = null;
            var wordsState = new PipelineState();
            if (alienPersonality != null)
            {
                wordsState.SetString(alienPersonalityKey, BuildAlienPersonalityPromptContext());
            }

            GamePipelineRunner.Instance.RunPipeline(wordsSelectionPipeline, wordsState, result =>
            {
                finalState = result;
                done = true;
            });

            yield return new WaitUntil(() => done);

            string error = string.Empty;
            string wordsJson = string.Empty;
            string keywordA = string.Empty;
            string keywordB = string.Empty;
            if (finalState == null)
            {
                error = "Round keyword selection returned no state.";
            }
            else if (!finalState.TryGetString(wordsSelectionWordsKey, out wordsJson) ||
                     !TryParseRoundKeywords(wordsJson, out keywordA, out keywordB))
            {
                if (finalState.TryGetString(PromptPipelineConstants.ErrorKey, out string pipelineError) &&
                    !string.IsNullOrWhiteSpace(pipelineError))
                {
                    error = pipelineError;
                }
                else
                {
                    error = "Round keyword selection returned invalid words JSON.";
                }
            }
            else
            {
                selection.keywordA = keywordA.Trim();
                selection.keywordB = keywordB.Trim();
                selection.labelA = selection.keywordA;
                selection.labelB = selection.keywordB;
                selection.objectPromptA = BuildObjectPromptFromKeyword(selection.keywordA, objectPromptA);
                selection.objectPromptB = BuildObjectPromptFromKeyword(selection.keywordB, objectPromptB);
                onComplete?.Invoke(selection);
                yield break;
            }

            selection.warning = error;
            selection.keywordA = SimplifyTargetObjectPrompt(objectPromptA);
            selection.keywordB = SimplifyTargetObjectPrompt(objectPromptB);
            selection.labelA = selection.keywordA;
            selection.labelB = selection.keywordB;
            selection.objectPromptA = objectPromptA;
            selection.objectPromptB = objectPromptB;
            onComplete?.Invoke(selection);
        }

        private void ApplyFallbackRoundKeywords(string reason)
        {
            bool oldRoundStartReady = IsRoundStartReady;
            _currentObjectPromptA = objectPromptA;
            _currentObjectPromptB = objectPromptB;
            _currentRoundKeywordA = SimplifyTargetObjectPrompt(_currentObjectPromptA);
            _currentRoundKeywordB = SimplifyTargetObjectPrompt(_currentObjectPromptB);

            if (!string.IsNullOrWhiteSpace(reason))
            {
                Debug.LogWarning($"[AIPipelineBridge] {reason}");
            }

            SetRoundKeywordSelectionInProgress(false, notifyListeners: true, previousRoundStartReady: oldRoundStartReady);
        }

        private void SetRoundKeywordSelectionInProgress(
            bool inProgress,
            bool notifyListeners,
            bool? previousRoundStartReady = null)
        {
            bool oldRoundStartReady = previousRoundStartReady ?? IsRoundStartReady;
            _isRoundKeywordSelectionInProgress = inProgress;

            if (notifyListeners)
            {
                NotifyRoundStartReadinessChanged(oldRoundStartReady, forceNotify: true);
            }
        }

        private void NotifyRoundStartReadinessChanged(bool oldRoundStartReady, bool forceNotify = false)
        {
            bool newRoundStartReady = IsRoundStartReady;
            if (forceNotify || oldRoundStartReady != newRoundStartReady)
            {
                RoundStartReadinessChanged?.Invoke(newRoundStartReady);
            }
        }

        private string GetCurrentObjectPromptA()
        {
            return string.IsNullOrWhiteSpace(_currentObjectPromptA) ? objectPromptA : _currentObjectPromptA;
        }

        private string GetCurrentObjectPromptB()
        {
            return string.IsNullOrWhiteSpace(_currentObjectPromptB) ? objectPromptB : _currentObjectPromptB;
        }

        private string BuildObjectPromptFromKeyword(string keyword, string fallbackPrompt)
        {
            string normalizedKeyword = keyword?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(normalizedKeyword))
            {
                return fallbackPrompt;
            }

            string template = string.IsNullOrWhiteSpace(selectedKeywordPromptTemplate)
                ? "{0}"
                : selectedKeywordPromptTemplate.Trim();

            return template.Contains("{0}", StringComparison.Ordinal)
                ? template.Replace("{0}", normalizedKeyword)
                : $"{template} {normalizedKeyword}".Trim();
        }

        private static bool TryParseRoundKeywords(string rawWordsJson, out string keywordA, out string keywordB)
        {
            keywordA = string.Empty;
            keywordB = string.Empty;

            if (string.IsNullOrWhiteSpace(rawWordsJson))
            {
                return false;
            }

            try
            {
                using JsonDocument document = JsonDocument.Parse(rawWordsJson);
                if (document.RootElement.ValueKind != JsonValueKind.Array)
                {
                    return false;
                }

                List<string> keywords = new();
                foreach (JsonElement element in document.RootElement.EnumerateArray())
                {
                    if (element.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    string value = element.GetString()?.Trim() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        continue;
                    }

                    keywords.Add(value);
                    if (keywords.Count == 2)
                    {
                        break;
                    }
                }

                if (keywords.Count != 2 ||
                    string.Equals(keywords[0], keywords[1], StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                keywordA = keywords[0];
                keywordB = keywords[1];
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private IEnumerator GetPreviewRoutine(Action<string> onComplete)
        {
            bool hasDrawingTexture = TryGetCurrentDrawingForPipeline(out Texture2D drawingTexture);

            if (LastDrawingWasBlank)
            {
                SetFallbackPreviewRead(true);
                onComplete?.Invoke(LastPreviewDialogue);
                yield break;
            }

            if (previewDialoguePipeline == null || GamePipelineRunner.Instance == null)
            {
                Debug.LogWarning("[AIPipelineBridge] Preview dialogue pipeline is not assigned. Using fallback preview dialogue.");
                SetFallbackPreviewRead(false);
                onComplete?.Invoke(LastPreviewDialogue);
                yield break;
            }

            if (!hasDrawingTexture)
            {
                SetFallbackPreviewRead(false);
                onComplete?.Invoke(LastPreviewDialogue);
                yield break;
            }

            var state = new PipelineState();
            state.SetImage(drawingImageKey, drawingTexture);
            string targetObjects = BuildTargetObjectsSummary();
            if (!string.IsNullOrWhiteSpace(targetObjects))
            {
                state.SetString(targetObjectsKey, targetObjects);
            }

            bool done = false;
            PipelineState finalState = null;
            GamePipelineRunner.Instance.RunPipeline(previewDialoguePipeline, state, result =>
            {
                finalState = result;
                done = true;
            });
            yield return new WaitUntil(() => done);

            ApplyPreviewReadFromState(finalState);

            Debug.Log(
                $"[AIPipelineBridge] Preview dialogue generation finished. Length={LastPreviewDialogue.Length}, " +
                $"objectA={LastPreviewObjectAPresence}, objectB={LastPreviewObjectBPresence}");
            onComplete?.Invoke(LastPreviewDialogue);
        }

        private IEnumerator GetJudgmentRoutine(Action<SatisfactionLevel> onComplete)
        {
            if (judgmentPipeline == null || GamePipelineRunner.Instance == null)
            {
                Debug.LogWarning("[AIPipelineBridge] Judgment pipeline is not assigned. Using Neutral fallback.");
                LastSatisfaction = SatisfactionLevel.Neutral;
                LastJudgmentSceneReading = "";
                LastJudgmentReason = "";
                LastTelepathy = "";
                onComplete?.Invoke(LastSatisfaction);
                yield break;
            }

            bool hasDrawingTexture = TryGetCurrentDrawingForPipeline(out Texture2D drawingTexture);

            if (LastDrawingWasBlank)
            {
                SetNeutralJudgmentFallback(
                    "The submitted drawing is blank.",
                    "No visible action, relationship, or object combination was communicated.");
                Debug.Log("[AIPipelineBridge] Judgment skipped because the drawing is blank.");
                onComplete?.Invoke(LastSatisfaction);
                yield break;
            }

            if (!hasDrawingTexture)
            {
                SetNeutralJudgmentFallback(
                    "The submitted drawing could not be read.",
                    "No drawing texture was available for the alien evaluation.");
                Debug.LogWarning("[AIPipelineBridge] Judgment skipped because no drawing texture was available.");
                onComplete?.Invoke(LastSatisfaction);
                yield break;
            }

            if (!HasPreviewReadContext())
            {
                yield return GetPreviewRoutine(_ => { });
            }

            var state = new PipelineState();
            string targetObjects = BuildTargetObjectsSummary();
            if (!string.IsNullOrWhiteSpace(targetObjects))
            {
                state.SetString(targetObjectsKey, targetObjects);
            }

            // Judgment is text-only. Reuse the Stage 1 description instead of rereading the image.
            if (!string.IsNullOrWhiteSpace(LastPreviewSceneReading))
            {
                state.SetString(PreviewSceneReadingStateKey, LastPreviewSceneReading);
            }

            if (!string.IsNullOrWhiteSpace(LastPreviewVisibleRelations))
            {
                state.SetString(PreviewVisibleRelationsStateKey, LastPreviewVisibleRelations);
            }

            if (!string.IsNullOrWhiteSpace(LastPreviewUncertainty))
            {
                state.SetString(PreviewUncertaintyStateKey, LastPreviewUncertainty);
            }

            if (_lastPreviewObjectAPresence != PreviewObjectPresence.Unknown)
            {
                state.SetString(PreviewObjectAPresenceStateKey, LastPreviewObjectAPresence);
            }

            if (_lastPreviewObjectBPresence != PreviewObjectPresence.Unknown)
            {
                state.SetString(PreviewObjectBPresenceStateKey, LastPreviewObjectBPresence);
            }

            if (alienPersonality != null)
            {
                state.SetString(alienPersonalityKey, BuildAlienPersonalityPromptContext());
            }

            bool done = false;
            PipelineState finalState = null;
            GamePipelineRunner.Instance.RunPipeline(judgmentPipeline, state, result =>
            {
                finalState = result;
                done = true;
            });
            yield return new WaitUntil(() => done);

            LastSatisfaction = SatisfactionLevel.Neutral;
            LastJudgmentSceneReading = "";
            LastJudgmentReason = "";
            if (finalState != null)
            {
                if (TryReadSatisfactionStateValue(finalState, out SatisfactionLevel satisfaction))
                    LastSatisfaction = satisfaction;
                if (finalState.TryGetString(judgmentSceneReadingKey, out string sceneReading))
                    LastJudgmentSceneReading = sceneReading?.Trim() ?? string.Empty;
                if (finalState.TryGetString(judgmentReasonKey, out string judgmentReason))
                    LastJudgmentReason = judgmentReason?.Trim() ?? string.Empty;
            }

            string rawTranscript = string.Empty;
            yield return TryGenerateTelepathyTranscriptRoutine(result => rawTranscript = result);
            if (string.IsNullOrWhiteSpace(rawTranscript))
            {
                rawTranscript = BuildFallbackJudgmentTranscript();
            }

            LastTelepathy = string.IsNullOrWhiteSpace(rawTranscript)
                ? string.Empty
                : FormatTelepathyTerminalOutput(rawTranscript);

            Debug.Log($"[AIPipelineBridge] Judgment finished. satisfaction={LastSatisfaction}");
            onComplete?.Invoke(LastSatisfaction);
        }

        private IEnumerator GetTelepathyRoutine(Action<string> onComplete)
        {
            onComplete?.Invoke(LastTelepathy);
            yield break;
        }

        private void HandleStableDiffusionProgress(StableDiffusionCppWorkerProgressResponse progress)
        {
            if (progress == null ||
                !progress.isBusy ||
                !progress.hasProgress ||
                progress.previewImage == null ||
                !progress.previewImage.HasData ||
                string.IsNullOrEmpty(progress.previewImage.base64Data))
            {
                return;
            }

            int slot;
            lock (_sdProgressLock)
            {
                slot = _activeSdSlot;
                if (slot < 0)
                    return;

                if (_activeProgressSessionId < 0)
                    _activeProgressSessionId = progress.progressSessionId;
                else if (progress.progressSessionId != _activeProgressSessionId)
                    return;

                if (progress.previewUpdateIndex <= _pendingPreviewUpdateIndex)
                    return;
            }

            byte[] previewBytes;
            try
            {
                previewBytes = Convert.FromBase64String(progress.previewImage.base64Data);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AIPipelineBridge] Failed to decode SD preview image: {ex.Message}");
                return;
            }

            lock (_sdProgressLock)
            {
                if (slot != _activeSdSlot || progress.previewUpdateIndex <= _pendingPreviewUpdateIndex)
                    return;

                _pendingPreviewSlot = slot;
                _pendingPreviewUpdateIndex = progress.previewUpdateIndex;
                _pendingPreviewWidth = progress.previewImage.width;
                _pendingPreviewHeight = progress.previewImage.height;
                _pendingPreviewChannels = progress.previewImage.channelCount;
                _pendingPreviewBytes = previewBytes;
            }
        }

        private void SetActiveGenerationSlot(int slot)
        {
            lock (_sdProgressLock)
            {
                _activeSdSlot = slot;
                _activeProgressSessionId = -1;
                ClearPendingPreviewLocked();
            }
        }

        private void ClearActiveGenerationSlot()
        {
            lock (_sdProgressLock)
            {
                _activeSdSlot = -1;
                _activeProgressSessionId = -1;
                ClearPendingPreviewLocked();
            }
        }

        private void ApplyPendingProgressPreview(Texture2D stableLeftTexture, Texture2D stableRightTexture)
        {
            int slot;
            int width;
            int height;
            int channels;
            byte[] bytes;

            lock (_sdProgressLock)
            {
                if (_pendingPreviewSlot < 0 || _pendingPreviewBytes == null)
                    return;

                slot = _pendingPreviewSlot;
                width = _pendingPreviewWidth;
                height = _pendingPreviewHeight;
                channels = _pendingPreviewChannels;
                bytes = _pendingPreviewBytes;
                ClearPendingPreviewLocked();
            }

            if (!StableDiffusionCppImageIO.TryCreateTextureFromTopDownRawBytes(
                    bytes,
                    width,
                    height,
                    channels,
                    FilterMode.Bilinear,
                    out Texture2D previewTexture,
                    out string error))
            {
                Debug.LogWarning($"[AIPipelineBridge] Failed to create SD preview texture: {error}");
                return;
            }

            ReplaceSlotProgressTexture(slot, previewTexture);
            UpdateMonitorProgress(stableLeftTexture, stableRightTexture);
        }

        private void UpdateMonitorProgress(Texture2D stableLeftTexture, Texture2D stableRightTexture)
        {
            monitorDisplay?.ShowGenerating(
                _progressObjTexA != null ? _progressObjTexA : stableLeftTexture,
                _progressObjTexB != null ? _progressObjTexB : stableRightTexture);
        }

        private void ReplaceSlotProgressTexture(int slot, Texture2D previewTexture)
        {
            if (slot == LeftObjectSlot)
            {
                DestroyTexture(_progressObjTexA);
                _progressObjTexA = previewTexture;
                return;
            }

            if (slot == RightObjectSlot)
            {
                DestroyTexture(_progressObjTexB);
                _progressObjTexB = previewTexture;
                return;
            }

            DestroyTexture(previewTexture);
        }

        private void ClearPendingPreviewLocked()
        {
            _pendingPreviewSlot = -1;
            _pendingPreviewUpdateIndex = -1;
            _pendingPreviewWidth = 0;
            _pendingPreviewHeight = 0;
            _pendingPreviewChannels = 0;
            _pendingPreviewBytes = null;
        }

        private static SatisfactionLevel ParseSatisfaction(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return SatisfactionLevel.Neutral;

            return value.ToLowerInvariant().Trim() switch
            {
                "verydissatisfied" or "very_dissatisfied" or "-2" => SatisfactionLevel.VeryDissatisfied,
                "dissatisfied" or "-1" => SatisfactionLevel.Dissatisfied,
                "neutral" or "0" => SatisfactionLevel.Neutral,
                "satisfied" or "1" => SatisfactionLevel.Satisfied,
                "verysatisfied" or "very_satisfied" or "2" => SatisfactionLevel.VerySatisfied,
                _ => SatisfactionLevel.Neutral,
            };
        }

        private void SetSatisfactionStateValue(PipelineState state, SatisfactionLevel satisfaction)
        {
            if (state == null)
            {
                return;
            }

            string formatted = FormatSatisfactionForPrompt(satisfaction);
            state.SetString(GetSatisfactionStateKey(), formatted);
        }

        private bool TryReadSatisfactionStateValue(PipelineState state, out SatisfactionLevel satisfaction)
        {
            satisfaction = SatisfactionLevel.Neutral;
            if (state == null)
            {
                return false;
            }

            if (TryReadStateString(state, GetSatisfactionStateKey(), out string rawValue))
            {
                satisfaction = ParseSatisfaction(rawValue);
                return true;
            }

            return false;
        }

        private string GetSatisfactionStateKey()
        {
            return string.IsNullOrWhiteSpace(judgmentSatisfactionKey)
                ? DefaultSatisfactionStateKey
                : judgmentSatisfactionKey.Trim();
        }

        private static bool TryReadStateString(PipelineState state, string key, out string value)
        {
            value = string.Empty;
            if (state == null || string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            return state.TryGetString(key, out value) && !string.IsNullOrWhiteSpace(value);
        }

        private static PreviewObjectPresence ParsePreviewObjectPresence(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return PreviewObjectPresence.Unknown;
            }

            return value.Trim().ToLowerInvariant() switch
            {
                "visible" or "present" or "seen" => PreviewObjectPresence.Visible,
                "unclear" or "ambiguous" or "partial" or "maybe" => PreviewObjectPresence.Unclear,
                "missing" or "absent" or "not_visible" or "not visible" => PreviewObjectPresence.Missing,
                _ => PreviewObjectPresence.Unknown
            };
        }

        private static string FormatPreviewObjectPresence(PreviewObjectPresence value)
        {
            return value switch
            {
                PreviewObjectPresence.Visible => "visible",
                PreviewObjectPresence.Unclear => "unclear",
                PreviewObjectPresence.Missing => "missing",
                _ => "unknown"
            };
        }

        private static string FormatSatisfactionForPrompt(SatisfactionLevel value)
        {
            return value switch
            {
                SatisfactionLevel.VeryDissatisfied => "very_dissatisfied",
                SatisfactionLevel.Dissatisfied => "dissatisfied",
                SatisfactionLevel.Neutral => "neutral",
                SatisfactionLevel.Satisfied => "satisfied",
                SatisfactionLevel.VerySatisfied => "very_satisfied",
                _ => "neutral"
            };
        }

        private static void DestroyTexture(Texture2D tex)
        {
            if (tex != null)
                Destroy(tex);
        }
    }
}
