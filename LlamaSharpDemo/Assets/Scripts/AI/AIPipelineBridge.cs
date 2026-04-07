using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
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

        private enum ObjectGenerationAvailabilityState
        {
            Unknown,
            Preparing,
            Ready,
            Failed
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
        [SerializeField] private string sdNegativePrompt = "low quality, blurry, text, watermark, humans, faces";

        [Header("LLM Pipelines")]
        [Tooltip("Adjutant preview dialogue pipeline. Expected output key: response")]
        [SerializeField] private PromptPipelineAsset previewDialoguePipeline;
        [Tooltip("Judgment pipeline. Expected output keys: axis1, axis2, scene_reading, judgment_reason")]
        [SerializeField] private PromptPipelineAsset judgmentPipeline;
        [Tooltip("Telepathy transcript pipeline. Expected output key: response")]
        [SerializeField] private PromptPipelineAsset telepathyPipeline;
        [Header("Pipeline State Keys")]
        [SerializeField] private string drawingImageKey = "reference_image";
        [SerializeField] private string alienPersonalityKey = "alien_personality";
        [SerializeField] private string targetObjectsKey = "target_objects";
        [SerializeField] private string judgmentAxis1Key = "axis1";
        [SerializeField] private string judgmentAxis2Key = "axis2";
        [SerializeField] private string judgmentSceneReadingKey = "scene_reading";
        [SerializeField] private string judgmentReasonKey = "judgment_reason";

        [Header("References")]
        [SerializeField] private SharedMonitorDisplay monitorDisplay;
        [SerializeField] private DrawingExportBridge drawingExportBridge;
        [SerializeField] private AlienPersonality alienPersonality;

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
        public SatisfactionLevel LastAxis1 { get; private set; } = SatisfactionLevel.Neutral;
        public SatisfactionLevel LastAxis2 { get; private set; } = SatisfactionLevel.Neutral;
        public string LastJudgmentSceneReading { get; private set; } = "";
        public string LastJudgmentReason { get; private set; } = "";
        public string LastTelepathy { get; private set; } = "";
        public bool LastDrawingWasBlank { get; private set; }
        public bool HasTelepathyResult => !string.IsNullOrWhiteSpace(LastTelepathy);
        public string LastObjectGenerationError { get; private set; } = "";
        public bool IsObjectGenerationReady => _objectGenerationAvailability == ObjectGenerationAvailabilityState.Ready;
        public bool IsObjectGenerationPreparing => _objectGenerationAvailability == ObjectGenerationAvailabilityState.Preparing;
        public bool HasObjectGenerationPreparationFailed => _objectGenerationAvailability == ObjectGenerationAvailabilityState.Failed;

        public event Action<bool> ObjectGenerationReadinessChanged;

        private Texture2D _lastObjTexA;
        private Texture2D _lastObjTexB;
        private Texture2D _progressObjTexA;
        private Texture2D _progressObjTexB;
        private CancellationTokenSource _sdCts;
        private bool _sdPrewarmStarted;
        private Task _sdPrewarmTask;
        private ObjectGenerationAvailabilityState _objectGenerationAvailability = ObjectGenerationAvailabilityState.Unknown;

        private readonly object _sdProgressLock = new object();
        private int _activeSdSlot = -1;
        private long _activeProgressSessionId = -1;
        private int _pendingPreviewSlot = -1;
        private long _pendingPreviewUpdateIndex = -1;
        private int _pendingPreviewWidth;
        private int _pendingPreviewHeight;
        private int _pendingPreviewChannels;
        private byte[] _pendingPreviewBytes;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        private void OnEnable()
        {
            StableDiffusionCppRuntime.ProgressChanged += HandleStableDiffusionProgress;
            EnsureObjectGenerationPreparation();
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

        public void GetJudgment(Action<SatisfactionLevel, SatisfactionLevel> onComplete = null)
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

            StableDiffusionCppRuntime.CancelActiveGeneration();
            GamePipelineRunner.Instance?.StopGeneration();

            ClearActiveGenerationSlot();
            ReplaceSlotProgressTexture(LeftObjectSlot, null);
            ReplaceSlotProgressTexture(RightObjectSlot, null);
            monitorDisplay?.SetIdle();
        }

        public void ResetRound()
        {
            LastPreviewDialogue = "";
            LastAxis1 = SatisfactionLevel.Neutral;
            LastAxis2 = SatisfactionLevel.Neutral;
            LastJudgmentSceneReading = "";
            LastJudgmentReason = "";
            LastTelepathy = "";
            LastDrawingWasBlank = false;
        }

        public string GetAlienPersonalitySummary()
        {
            if (alienPersonality == null)
            {
                return "Alien personality: no profile assigned";
            }

            return $"Alien personality: {BuildAlienPersonalityLabel()}";
        }

        public void EnsureObjectGenerationPreparation(bool forceRetry = false)
        {
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

        public string GetObjectGenerationAvailabilityMessage()
        {
            if (IsObjectGenerationReady)
            {
                return "Click the alien to begin the round.";
            }

            if (IsObjectGenerationPreparing)
            {
                return "Preparing the bundled SD server. Wait until the alien becomes interactable.";
            }

            if (!string.IsNullOrWhiteSpace(LastObjectGenerationError))
            {
                return $"Object generator unavailable: {LastObjectGenerationError}";
            }

            return "Object generator is not ready yet.";
        }

        private IEnumerator GenerateObjectsRoutine(Action<bool> onComplete)
        {
            LastObjectGenerationError = string.Empty;
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
                objectPromptA,
                () => null,
                () => texB,
                token,
                result => texA = result);

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
                objectPromptB,
                () => texA,
                () => null,
                token,
                result => texB = result);

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
            CancellationToken token,
            Action<Texture2D> onResult)
        {
            SetActiveGenerationSlot(slot);
            UpdateMonitorProgress(stableLeftProvider?.Invoke(), stableRightProvider?.Invoke());

            Task<StableDiffusionCppGenerationResult> task = GenerateSingleObjectAsync(prompt, token);
            float elapsed = 0f;

            while (!task.IsCompleted)
            {
                elapsed += Time.deltaTime;
                ApplyPendingProgressPreview(stableLeftProvider?.Invoke(), stableRightProvider?.Invoke());

                if (elapsed >= SdTimeoutSeconds)
                {
                    string timeoutError = $"SD object slot {slot} timed out while generating alien objects.";
                    Debug.LogWarning($"[AIPipelineBridge] {timeoutError}");
                    LastObjectGenerationError = timeoutError;
                    _sdCts?.Cancel();
                    ClearActiveGenerationSlot();
                    onResult?.Invoke(null);
                    yield break;
                }

                yield return null;
            }

            ApplyPendingProgressPreview(stableLeftProvider?.Invoke(), stableRightProvider?.Invoke());
            ClearActiveGenerationSlot();

            StableDiffusionCppGenerationResult result;
            if (task.IsCanceled || token.IsCancellationRequested)
            {
                LastObjectGenerationError = "Alien object generation was cancelled.";
                Debug.LogWarning($"[AIPipelineBridge] SD slot {slot} was cancelled.");
                ReplaceSlotProgressTexture(slot, null);
                onResult?.Invoke(null);
                UpdateMonitorProgress(stableLeftProvider?.Invoke(), stableRightProvider?.Invoke());
                yield break;
            }

            if (task.IsFaulted)
            {
                Exception failure = task.Exception?.GetBaseException() ?? task.Exception;
                LastObjectGenerationError = failure?.Message ?? $"SD slot {slot} failed unexpectedly.";
                Debug.LogError($"[AIPipelineBridge] SD slot {slot} failed: {failure}");
                ReplaceSlotProgressTexture(slot, null);
                onResult?.Invoke(null);
                UpdateMonitorProgress(stableLeftProvider?.Invoke(), stableRightProvider?.Invoke());
                yield break;
            }

            result = task.Result;
            if (!result.Success)
            {
                LastObjectGenerationError = string.IsNullOrWhiteSpace(result.ErrorMessage)
                    ? $"SD slot {slot} failed without an error message."
                    : result.ErrorMessage;
            }

            Debug.Log(
                $"[AIPipelineBridge] SD slot {slot} result Success={result.Success}, " +
                $"Files={result.OutputFiles?.Count ?? 0}, Error={result.ErrorMessage}");

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
                    LastObjectGenerationError = string.IsNullOrWhiteSpace(loadError)
                        ? $"Generated slot {slot} output could not be loaded."
                        : loadError;
                }
            }
            else if (result.Success)
            {
                LastObjectGenerationError = $"Generated slot {slot} returned no image files.";
            }

            ReplaceSlotProgressTexture(slot, null);
            onResult?.Invoke(finalTexture);
            UpdateMonitorProgress(
                slot == LeftObjectSlot ? finalTexture : stableLeftProvider?.Invoke(),
                slot == RightObjectSlot ? finalTexture : stableRightProvider?.Invoke());
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

        private string BuildAlienPersonalityLabel()
        {
            return $"{DescribeCooperationVsDomination(alienPersonality.cooperationVsDomination)}, " +
                   $"{DescribeEfficiencyVsEmpathy(alienPersonality.efficiencyVsEmpathy)}";
        }

        private string BuildAlienPersonalityPromptContext()
        {
            if (alienPersonality == null)
            {
                return string.Empty;
            }

            string axis1 = DescribeCooperationVsDomination(alienPersonality.cooperationVsDomination);
            string axis2 = DescribeEfficiencyVsEmpathy(alienPersonality.efficiencyVsEmpathy);

            string axis1Guide = axis1 == "dominant"
                ? "axis1 (dominant): values power, pressure, command, pursuit, threat, coercion, attack, and imposing control over others; dislikes passive retreat, yielding, equal footing, and loss of control."
                : "axis1 (cooperative): values coordination, mutual benefit, shared action, restraint, and non-coercive alignment; dislikes needless force, domination, intimidation, and unilateral control.";

            string axis2Guide = axis2 == "efficiency-driven"
                ? "axis2 (efficiency-driven): values direct cause and effect, low waste, speed, utility, and reliable outcomes; dislikes delay, waste, decorative noise, and muddled results."
                : "axis2 (empathetic): values protection, comfort, emotional clarity, reassurance, and visible care; dislikes cruelty, indifference, avoidable harm, and cold optimization.";

            return $"{axis1Guide}\n{axis2Guide}\ninterpretation notes: dominance is about power over others or forcing outcomes, not just moving fast. retreat can be efficient without being dominant. strange or biological methods are acceptable if they are controlled and effective.";
        }

        private static string DescribeCooperationVsDomination(float value)
        {
            return value < 0f ? "cooperative" : "dominant";
        }

        private static string DescribeEfficiencyVsEmpathy(float value)
        {
            return value < 0f ? "efficiency-driven" : "empathetic";
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
            string left = SimplifyTargetObjectPrompt(objectPromptA);
            string right = SimplifyTargetObjectPrompt(objectPromptB);

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

            string personalityLabel = BuildAlienPersonalityLabel();
            return personalityLabel switch
            {
                "cooperative, efficiency-driven" => "Coordination and clear results matter more than intimidation.",
                "cooperative, empathetic" => "Mutual care and emotional clarity matter more than force.",
                "dominant, efficiency-driven" => "Control over others and obvious results matter most.",
                "dominant, empathetic" => "Protective authority, recognition, and emotional weight matter most.",
                _ => string.Empty
            };
        }

        private string BuildFallbackSatisfactionLine()
        {
            if (LastDrawingWasBlank)
            {
                return "The delegation cannot judge an empty submission.";
            }

            int combinedScore = ToAxisScore(LastAxis1) + ToAxisScore(LastAxis2);
            if (combinedScore >= 3)
            {
                return "This satisfies the delegation.";
            }

            if (combinedScore <= -3)
            {
                return "This does not satisfy the delegation.";
            }

            return "The delegation remains unconvinced.";
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
                return "I cannot read any marks on the canvas yet. Is the drawing still unfinished?";
            }

            return "It is hard to read this drawing clearly. Is that what you intended?";
        }

        private void SetNeutralJudgmentFallback(string sceneReading, string judgmentReason)
        {
            LastAxis1 = SatisfactionLevel.Neutral;
            LastAxis2 = SatisfactionLevel.Neutral;
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
                return "It is hard to read this drawing. Is that what you intended?";
            }

            string sanitized = dialogue.Trim();
            string fallbackQuestion = "Does that match what you intended?";
            string[] blockedPrefixes =
            {
                "Here is",
                "Here's",
                "Here is an uncertain preview line",
                "Here's an uncertain preview line",
                "Uncertain preview line",
                "Preview line",
                "Adjutant:"
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
                    sanitized = fallbackQuestion;
                }
                else
                {
                    sanitized = $"{sanitized}. {fallbackQuestion}";
                }

                break;
            }

            return string.IsNullOrWhiteSpace(sanitized)
                ? "It is hard to read this drawing. Is that what you intended?"
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
            request = BuildObjectGenerationRequest(objectPromptA);
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
                return;
            }

            if (nextState is ObjectGenerationAvailabilityState.Failed or ObjectGenerationAvailabilityState.Preparing)
            {
                ObjectGenerationReadinessChanged?.Invoke(newReady);
            }
        }

        private IEnumerator GetPreviewRoutine(Action<string> onComplete)
        {
            bool hasDrawingTexture = TryGetCurrentDrawingForPipeline(out Texture2D drawingTexture);

            if (LastDrawingWasBlank)
            {
                LastPreviewDialogue = BuildFallbackPreviewDialogue(true);
                onComplete?.Invoke(LastPreviewDialogue);
                yield break;
            }

            if (previewDialoguePipeline == null || GamePipelineRunner.Instance == null)
            {
                Debug.LogWarning("[AIPipelineBridge] Preview dialogue pipeline is not assigned. Using fallback preview dialogue.");
                LastPreviewDialogue = BuildFallbackPreviewDialogue(false);
                onComplete?.Invoke(LastPreviewDialogue);
                yield break;
            }

            if (!hasDrawingTexture)
            {
                LastPreviewDialogue = BuildFallbackPreviewDialogue(false);
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

            LastPreviewDialogue = finalState != null &&
                                  finalState.TryGetString(PromptPipelineConstants.AnswerKey, out string previewDialogue)
                ? previewDialogue
                : BuildFallbackPreviewDialogue(false);

            LastPreviewDialogue = SanitizePreviewDialogue(LastPreviewDialogue);

            Debug.Log($"[AIPipelineBridge] Preview dialogue generation finished. Length={LastPreviewDialogue.Length}");
            onComplete?.Invoke(LastPreviewDialogue);
        }

        private IEnumerator GetJudgmentRoutine(Action<SatisfactionLevel, SatisfactionLevel> onComplete)
        {
            if (judgmentPipeline == null || GamePipelineRunner.Instance == null)
            {
                Debug.LogWarning("[AIPipelineBridge] Judgment pipeline is not assigned. Using Neutral fallback.");
                LastAxis1 = SatisfactionLevel.Neutral;
                LastAxis2 = SatisfactionLevel.Neutral;
                LastJudgmentSceneReading = "";
                LastJudgmentReason = "";
                LastTelepathy = "";
                onComplete?.Invoke(LastAxis1, LastAxis2);
                yield break;
            }

            bool hasDrawingTexture = TryGetCurrentDrawingForPipeline(out Texture2D drawingTexture);

            if (LastDrawingWasBlank)
            {
                SetNeutralJudgmentFallback(
                    "The submitted drawing is blank.",
                    "No visible action, relationship, or object combination was communicated.");
                Debug.Log("[AIPipelineBridge] Judgment skipped because the drawing is blank.");
                onComplete?.Invoke(LastAxis1, LastAxis2);
                yield break;
            }

            if (!hasDrawingTexture)
            {
                SetNeutralJudgmentFallback(
                    "The submitted drawing could not be read.",
                    "No drawing texture was available for the alien evaluation.");
                Debug.LogWarning("[AIPipelineBridge] Judgment skipped because no drawing texture was available.");
                onComplete?.Invoke(LastAxis1, LastAxis2);
                yield break;
            }

            var state = new PipelineState();
            state.SetImage(drawingImageKey, drawingTexture);
            string targetObjects = BuildTargetObjectsSummary();
            if (!string.IsNullOrWhiteSpace(targetObjects))
            {
                state.SetString(targetObjectsKey, targetObjects);
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

            LastAxis1 = SatisfactionLevel.Neutral;
            LastAxis2 = SatisfactionLevel.Neutral;
            LastJudgmentSceneReading = "";
            LastJudgmentReason = "";
            if (finalState != null)
            {
                if (finalState.TryGetString(judgmentAxis1Key, out string a1))
                    LastAxis1 = ParseSatisfaction(a1);
                if (finalState.TryGetString(judgmentAxis2Key, out string a2))
                    LastAxis2 = ParseSatisfaction(a2);
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

            Debug.Log($"[AIPipelineBridge] Judgment finished. axis1={LastAxis1}, axis2={LastAxis2}");
            onComplete?.Invoke(LastAxis1, LastAxis2);
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

        private static int ToAxisScore(SatisfactionLevel value)
        {
            return value switch
            {
                SatisfactionLevel.VeryDissatisfied => -2,
                SatisfactionLevel.Dissatisfied => -1,
                SatisfactionLevel.Neutral => 0,
                SatisfactionLevel.Satisfied => 1,
                SatisfactionLevel.VerySatisfied => 2,
                _ => 0
            };
        }

        private static void DestroyTexture(Texture2D tex)
        {
            if (tex != null)
                Destroy(tex);
        }
    }
}
