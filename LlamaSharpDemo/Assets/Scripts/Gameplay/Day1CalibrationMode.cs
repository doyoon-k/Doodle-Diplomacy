using System;
using System.Collections;
using System.Collections.Generic;
using DoodleDiplomacy.Camera;
using DoodleDiplomacy.Character;
using DoodleDiplomacy.Core;
using DoodleDiplomacy.Data;
using DoodleDiplomacy.Devices;
using DoodleDiplomacy.Dialogue;
using DoodleDiplomacy.Interaction;
using DoodleDiplomacy.Localization;
using DoodleDiplomacy.UI;
using UnityEngine;

namespace DoodleDiplomacy.Gameplay
{
    public sealed class Day1CalibrationMode : MonoBehaviour,
        IGameplayMode,
        IGameplaySessionController,
        IGameplayStateObservable
    {
        private const string Day1ModeId = "day1-calibration";
        private const int RequiredStimulusCount = 5;
        private const string ScienceOfficer = "Science Officer";
        private const string Adjutant = "Adjutant";

        [Header("Mode")]
        [SerializeField] private string modeId = Day1ModeId;

        [Header("References")]
        [SerializeField] private Day1StimulusLibrary stimulusLibrary;
        [SerializeField] private Day1StimulusButtonPanel stimulusButtonPanel;
        [SerializeField] private TerminalDisplay terminalDisplay;
        [SerializeField] private SharedMonitorDisplay sharedMonitorDisplay;
        [SerializeField] private AlienReactionController alienReactionController;

        [Header("Dialogue Sequences")]
        [SerializeField] private DialogueSequence openingSequence;
        [SerializeField] private DialogueSequence tabletBlankSequence;
        [SerializeField] private DialogueSequence scanningSequence;
        [SerializeField] private DialogueSequence classificationUnstableSequence;
        [SerializeField] private DialogueSequence actionOrSceneDetectedSequence;
        [SerializeField] private DialogueSequence writtenTextDetectedSequence;
        [SerializeField] private DialogueSequence nonStimulusDetectedSequence;
        [SerializeField] private DialogueSequence multipleObjectsDetectedSequence;
        [SerializeField] private DialogueSequence identifiesDrawingSequence;
        [SerializeField] private DialogueSequence previousImageNotTransmittedSequence;
        [SerializeField] private DialogueSequence transmissionLabelConfirmedSequence;
        [SerializeField] private DialogueSequence reactionEvaluationUnstableSequence;
        [SerializeField] private DialogueSequence responsePatternLoggedSequence;
        [SerializeField] private DialogueSequence reactionNoneSequence;
        [SerializeField] private DialogueSequence reactionSubtleSequence;
        [SerializeField] private DialogueSequence reactionModerateSequence;
        [SerializeField] private DialogueSequence reactionStrongSequence;
        [SerializeField] private DialogueSequence completeSequence;

        [Header("Timing")]
        [SerializeField, Min(0f)] private float transmissionHoldSeconds = 0.6f;

        [Header("Reaction Evaluation")]
        [SerializeField, Min(1)] private int reactionEvaluationMaxAttempts = 3;
        [SerializeField, Min(0f)] private float reactionEvaluationRetryDelaySeconds = 0.4f;

        public event Action<GameState> StateChanged;

        private readonly List<Texture2D> _ownedTextures = new();
        private GameplayModeContext _context;
        private Day1CalibrationInteractionPolicy _interactionPolicy;
        private Coroutine _routine;
        private GameState _currentState = GameState.Title;
        private int _slot = 1;
        private int _scanVersion;
        private Texture2D _pendingTexture;
        private byte[] _pendingPngBytes;
        private string _pendingLabel;
        private string _pendingDisplayLabel;
        private bool _entered;

        public string ModeId => string.IsNullOrWhiteSpace(modeId) ? Day1ModeId : modeId;
        public GameState CurrentState => _currentState;

        public void Enter(GameplayModeContext context)
        {
            _entered = true;
            _context = context;
            _interactionPolicy = new Day1CalibrationInteractionPolicy();
            ResolveReferences();
            stimulusButtonPanel?.Hide();
            _context?.Drawing?.SetInteractionLocked(true);
            ApplyCameraMode(GameState.Title);
            ChangeState(GameState.Title);
        }

        public void Exit()
        {
            StopActiveRoutine();
            _context?.AiGateway?.CancelActiveOperations();
            _context?.DialogueSystem?.StopSequence();
            stimulusButtonPanel?.Hide();
            _context?.Drawing?.SetInteractionLocked(true);
            _context = null;
            _entered = false;
        }

        private void OnDestroy()
        {
            foreach (Texture2D texture in _ownedTextures)
            {
                if (texture != null)
                {
                    Destroy(texture);
                }
            }

            _ownedTextures.Clear();
        }

        public void Tick(float deltaTime) { }

        public void HandleInteraction(InteractionType type, InteractableObject source)
        {
            if (type == InteractionType.Tablet && _currentState == GameState.Drawing)
            {
                _context?.Camera?.SetMode(CameraMode.TabletView);
            }
        }

        public void StartGame(bool isFirstPlay = true)
        {
            if (!_entered)
            {
                return;
            }

            StopActiveRoutine();
            _routine = StartCoroutine(StartDayRoutine());
        }

        public void ChangeToTitle()
        {
            StopActiveRoutine();
            _context?.DialogueSystem?.StopSequence();
            stimulusButtonPanel?.Hide();
            _context?.Drawing?.SetInteractionLocked(true);
            ChangeState(GameState.Title);
        }

        public void SubmitPreview()
        {
            if (_currentState == GameState.Drawing)
            {
                SubmitDrawing();
            }
        }

        public void ModifyPreview()
        {
            if (_currentState == GameState.Preview)
            {
                RedrawCandidate();
            }
        }

        private IEnumerator StartDayRoutine()
        {
            _slot = 1;
            _pendingTexture = null;
            _pendingPngBytes = null;
            _pendingLabel = null;
            _pendingDisplayLabel = null;
            _scanVersion = 0;

            stimulusLibrary?.BeginSession(clearExisting: true);
            terminalDisplay?.Clear();
            sharedMonitorDisplay?.SetIdle();
            stimulusButtonPanel?.Hide();
            _context?.AiGateway?.EnsureLlmPreparation();

            ChangeState(GameState.Intro);
            yield return PlayDialogueSequence(Day1DialogueSequence.Opening);

            BeginDrawing(clearCanvas: true);
            _routine = null;
        }

        private void BeginDrawing(bool clearCanvas)
        {
            _pendingTexture = null;
            _pendingPngBytes = null;
            _pendingLabel = null;
            _pendingDisplayLabel = null;

            if (clearCanvas)
            {
                _context?.Drawing?.ClearCanvas();
            }

            _context?.Drawing?.EnsureRuntimeEnabled();
            _context?.Drawing?.SetInteractionLocked(false);
            ApplyCameraMode(GameState.Drawing);
            ChangeState(GameState.Drawing);
            stimulusButtonPanel?.ShowSubmit(SubmitDrawing);
        }

        private void SubmitDrawing()
        {
            if (_currentState != GameState.Drawing)
            {
                return;
            }

            if (_context?.Drawing == null || !_context.Drawing.HasVisibleDrawing)
            {
                StopActiveRoutine();
                _routine = StartCoroutine(BlankSubmitRoutine());
                return;
            }

            if (!_context.Drawing.TryExportPngBytes(out byte[] pngBytes, out string error) ||
                pngBytes == null ||
                pngBytes.Length == 0)
            {
                Debug.LogWarning($"[Day1CalibrationMode] Failed to export drawing PNG: {error}", this);
                StopActiveRoutine();
                _routine = StartCoroutine(ClassificationFailedRoutine());
                return;
            }

            Texture2D texture = CreateTextureFromPng(pngBytes);
            if (texture == null)
            {
                StopActiveRoutine();
                _routine = StartCoroutine(ClassificationFailedRoutine());
                return;
            }

            _pendingPngBytes = pngBytes;
            _pendingTexture = texture;
            _context.Drawing.SetInteractionLocked(true);
            stimulusButtonPanel?.Hide();
            ApplyCameraMode(GameState.PreviewAnalyzing);
            ChangeState(GameState.PreviewAnalyzing);

            int version = ++_scanVersion;
            StopActiveRoutine();
            _routine = StartCoroutine(ScanRoutine(version));
        }

        private IEnumerator BlankSubmitRoutine()
        {
            _context?.Drawing?.SetInteractionLocked(true);
            stimulusButtonPanel?.Hide();
            yield return PlayDialogueSequence(Day1DialogueSequence.TabletBlank);
            BeginDrawing(clearCanvas: false);
            _routine = null;
        }

        private IEnumerator ScanRoutine(int version)
        {
            bool done = false;
            VisualStimulusClassificationResult result = null;
            _context?.AiGateway?.ClassifyVisualStimulus(classification =>
            {
                result = classification;
                done = true;
            });

            if (_context?.AiGateway == null)
            {
                done = true;
                result = VisualStimulusClassificationResult.Failed("AI gateway is missing.");
            }

            PlayDialogueSequenceNonBlocking(Day1DialogueSequence.Scanning);
            yield return null;
            yield return new WaitUntil(() => done);
            if (version != _scanVersion)
            {
                yield break;
            }

            if (result == null || !result.IsSuccess)
            {
                yield return PlayDialogueSequence(Day1DialogueSequence.ClassificationUnstable);
                BeginDrawing(clearCanvas: true);
                _routine = null;
                yield break;
            }

            string normalizedResultLabel = Day1ReactionTierEvaluator.NormalizeLabel(result.label);
            if (Day1StimulusSubmissionPolicy.IsActionOrSceneLabel(normalizedResultLabel))
            {
                yield return PlayDialogueSequence(
                    Day1DialogueSequence.ActionOrSceneDetected,
                    L10n.Arg("label", GetDisplayLabel(result, normalizedResultLabel)));
                BeginDrawing(clearCanvas: true);
                _routine = null;
                yield break;
            }

            if (Day1StimulusSubmissionPolicy.IsWrittenTextLabel(normalizedResultLabel))
            {
                yield return PlayDialogueSequence(Day1DialogueSequence.WrittenTextDetected);
                BeginDrawing(clearCanvas: true);
                _routine = null;
                yield break;
            }

            if (result.objectCount <= 0 || Day1StimulusSubmissionPolicy.IsBlockedLabel(normalizedResultLabel))
            {
                yield return PlayDialogueSequence(Day1DialogueSequence.NonStimulusDetected);
                BeginDrawing(clearCanvas: true);
                _routine = null;
                yield break;
            }

            if (!Day1StimulusSubmissionPolicy.IsAllowedObjectCount(result.objectCount, normalizedResultLabel))
            {
                string detectedSummary = string.IsNullOrWhiteSpace(normalizedResultLabel)
                    ? L10n.Label("multiple objects")
                    : GetDisplayLabel(result, normalizedResultLabel);
                yield return PlayDialogueSequence(
                    Day1DialogueSequence.MultipleObjectsDetected,
                    L10n.Arg("label", detectedSummary));
                BeginDrawing(clearCanvas: true);
                _routine = null;
                yield break;
            }

            string label = normalizedResultLabel;
            if (string.IsNullOrWhiteSpace(label))
            {
                yield return PlayDialogueSequence(Day1DialogueSequence.ClassificationUnstable);
                BeginDrawing(clearCanvas: true);
                _routine = null;
                yield break;
            }

            _pendingLabel = label;
            _pendingDisplayLabel = GetDisplayLabel(result, label);
            ChangeState(GameState.Preview);
            yield return PlayDialogueSequence(
                Day1DialogueSequence.IdentifiesDrawing,
                L10n.Arg("label", _pendingDisplayLabel));
            stimulusButtonPanel?.ShowConfirmation(ConfirmLabel, RedrawCandidate);
            _routine = null;
        }

        private IEnumerator ClassificationFailedRoutine()
        {
            yield return PlayDialogueSequence(Day1DialogueSequence.ClassificationUnstable);
            BeginDrawing(clearCanvas: true);
            _routine = null;
        }

        private void RedrawCandidate()
        {
            if (_currentState != GameState.Preview)
            {
                return;
            }

            StopActiveRoutine();
            stimulusButtonPanel?.Hide();
            _routine = StartCoroutine(RedrawRoutine());
        }

        private IEnumerator RedrawRoutine()
        {
            yield return PlayDialogueSequence(Day1DialogueSequence.PreviousImageNotTransmitted);
            BeginDrawing(clearCanvas: true);
            _routine = null;
        }

        private void ConfirmLabel()
        {
            if (_currentState != GameState.Preview)
            {
                return;
            }

            string normalizedLabel = Day1ReactionTierEvaluator.NormalizeLabel(_pendingLabel);
            if (string.IsNullOrWhiteSpace(normalizedLabel))
            {
                RedrawCandidate();
                return;
            }

            StopActiveRoutine();
            stimulusButtonPanel?.Hide();
            _routine = StartCoroutine(ConfirmLabelRoutine(normalizedLabel));
        }

        private IEnumerator ConfirmLabelRoutine(string label)
        {
            string displayLabel = GetPendingDisplayLabel(label);
            _context?.Drawing?.SetInteractionLocked(true);
            ChangeState(GameState.Submitting);
            ApplyCameraMode(GameState.Submitting);

            sharedMonitorDisplay?.ShowSubmission(_pendingTexture);
            terminalDisplay?.ShowText(L10n.T(
                    "day1.terminal.transmission_active",
                    "TRANSMISSION ACTIVE\nDRAWING: {label}\nRESPONSE MONITORING ONLINE",
                    L10n.Arg("label", displayLabel)),
                instant: true);

            yield return PlayDialogueSequence(
                Day1DialogueSequence.TransmissionLabelConfirmed,
                L10n.Arg("label", displayLabel));
            if (transmissionHoldSeconds > 0f)
            {
                yield return new WaitForSeconds(transmissionHoldSeconds);
            }

            ReactionTier reactionTier = ReactionTier.Subtle;
            bool evaluationSucceeded = false;
            string evaluationError = string.Empty;
            yield return EvaluateReactionTierWithRetries(
                label,
                tier =>
                {
                    reactionTier = tier;
                    evaluationSucceeded = true;
                },
                error => evaluationError = error);

            if (!evaluationSucceeded)
            {
                Debug.LogWarning(
                    $"[Day1CalibrationMode] Day1 reaction tier evaluation failed for '{label}': {evaluationError}",
                    this);
                ChangeState(GameState.Preview);
                ApplyCameraMode(GameState.Preview);
                yield return PlayDialogueSequence(Day1DialogueSequence.ReactionEvaluationUnstable);
                stimulusButtonPanel?.ShowConfirmation(ConfirmLabel, RedrawCandidate);
                _routine = null;
                yield break;
            }

            ChangeState(GameState.AlienReaction);
            ApplyCameraMode(GameState.AlienReaction);
            yield return PlayReactionRoutine(reactionTier);

            if (stimulusLibrary != null)
            {
                stimulusLibrary.SaveApprovedStimulus(_slot, label, reactionTier, _pendingPngBytes);
            }

            if (_slot >= RequiredStimulusCount)
            {
                yield return CompleteDayRoutine();
                yield break;
            }

            yield return PlayDialogueSequence(Day1DialogueSequence.ResponsePatternLogged);
            _slot++;
            BeginDrawing(clearCanvas: true);
            _routine = null;
        }

        private IEnumerator EvaluateReactionTierWithRetries(
            string label,
            Action<ReactionTier> onSuccess,
            Action<string> onFailure)
        {
            IRoundAiGateway aiGateway = _context?.AiGateway;
            if (aiGateway == null || !aiGateway.IsAvailable)
            {
                onFailure?.Invoke("AI gateway is unavailable.");
                yield break;
            }

            int attempts = Mathf.Max(1, reactionEvaluationMaxAttempts);
            string lastError = string.Empty;
            for (int attempt = 1; attempt <= attempts; attempt++)
            {
                bool done = false;
                Day1ReactionEvaluationResult evaluation = null;
                aiGateway.EvaluateDay1ReactionTier(label, result =>
                {
                    evaluation = result;
                    done = true;
                });

                yield return new WaitUntil(() => done);

                if (evaluation != null && evaluation.IsSuccess)
                {
                    onSuccess?.Invoke(evaluation.reactionTier);
                    yield break;
                }

                lastError = evaluation?.error ?? "Reaction evaluator returned no result.";
                Debug.LogWarning(
                    $"[Day1CalibrationMode] Day1 reaction tier attempt {attempt}/{attempts} failed for '{label}': {lastError}",
                    this);

                if (attempt < attempts && reactionEvaluationRetryDelaySeconds > 0f)
                {
                    yield return new WaitForSeconds(reactionEvaluationRetryDelaySeconds);
                }
            }

            onFailure?.Invoke(lastError);
        }

        private IEnumerator PlayReactionRoutine(ReactionTier reactionTier)
        {
            Day1DialogueSequence reactionSequence = GetReactionSequence(reactionTier);
            string reactionComment = ResolveFirstDialogueLine(reactionSequence);

            if (alienReactionController == null)
            {
                yield return PlayDialogueSequence(reactionSequence);
                yield break;
            }

            bool complete = false;
            UnityEngine.Events.UnityAction handler = () => complete = true;
            alienReactionController.OnReactionComplete.AddListener(handler);
            alienReactionController.PlayReaction(
                MapReactionTierToSatisfaction(reactionTier),
                LocalizeSpeaker(ScienceOfficer),
                string.Empty,
                reactionComment);
            yield return new WaitUntil(() => complete);
            alienReactionController.OnReactionComplete.RemoveListener(handler);
        }

        private IEnumerator CompleteDayRoutine()
        {
            string payloadPath = stimulusLibrary != null ? stimulusLibrary.WriteProfilePayload() : string.Empty;
            if (!string.IsNullOrWhiteSpace(payloadPath))
            {
                Debug.Log($"[Day1CalibrationMode] Day1 profile payload written: {payloadPath}", this);
            }

            _context?.Drawing?.SetInteractionLocked(true);
            stimulusButtonPanel?.Hide();
            ChangeState(GameState.Ending);
            ApplyCameraMode(GameState.Ending);

            yield return PlayDialogueSequence(Day1DialogueSequence.Complete);
            _routine = null;
        }

        private string GetDisplayLabel(VisualStimulusClassificationResult result, string fallbackLabel)
        {
            return !string.IsNullOrWhiteSpace(result?.localizedLabel)
                ? result.localizedLabel.Trim()
                : L10n.Label(fallbackLabel);
        }

        private string GetPendingDisplayLabel(string label)
        {
            string normalizedLabel = Day1ReactionTierEvaluator.NormalizeLabel(label);
            string normalizedPendingLabel = Day1ReactionTierEvaluator.NormalizeLabel(_pendingLabel);
            return !string.IsNullOrWhiteSpace(_pendingDisplayLabel) &&
                   string.Equals(normalizedLabel, normalizedPendingLabel, StringComparison.Ordinal)
                ? _pendingDisplayLabel.Trim()
                : L10n.Label(label);
        }

        private IEnumerator PlayDialogueSequence(Day1DialogueSequence sequenceId, params L10nArg[] args)
        {
            DialogueSequence sequence = GetDialogueSequence(sequenceId);
            DialogueSystem dialogueSystem = _context?.DialogueSystem;
            if (sequence == null || dialogueSystem == null)
            {
                Debug.LogWarning($"[Day1CalibrationMode] Missing dialogue sequence or DialogueSystem for '{sequenceId}'.", this);
                yield break;
            }

            yield return dialogueSystem.PlaySequenceAndWait(sequence, args);
        }

        private void PlayDialogueSequenceNonBlocking(Day1DialogueSequence sequenceId, params L10nArg[] args)
        {
            DialogueSequence sequence = GetDialogueSequence(sequenceId);
            DialogueSystem dialogueSystem = _context?.DialogueSystem;
            if (sequence == null || dialogueSystem == null)
            {
                Debug.LogWarning($"[Day1CalibrationMode] Missing dialogue sequence or DialogueSystem for '{sequenceId}'.", this);
                return;
            }

            dialogueSystem.PlaySequence(sequence, args);
        }

        private string ResolveFirstDialogueLine(Day1DialogueSequence sequenceId, params L10nArg[] args)
        {
            DialogueSequence sequence = GetDialogueSequence(sequenceId);
            if (sequence == null || sequence.lines == null || sequence.lines.Count == 0)
            {
                return string.Empty;
            }

            return DialogueSystem.ResolveLocalizedText(sequence.lines[0], args);
        }

        private DialogueSequence GetDialogueSequence(Day1DialogueSequence sequenceId)
        {
            DialogueSequence assigned = sequenceId switch
            {
                Day1DialogueSequence.Opening => openingSequence,
                Day1DialogueSequence.TabletBlank => tabletBlankSequence,
                Day1DialogueSequence.Scanning => scanningSequence,
                Day1DialogueSequence.ClassificationUnstable => classificationUnstableSequence,
                Day1DialogueSequence.ActionOrSceneDetected => actionOrSceneDetectedSequence,
                Day1DialogueSequence.WrittenTextDetected => writtenTextDetectedSequence,
                Day1DialogueSequence.NonStimulusDetected => nonStimulusDetectedSequence,
                Day1DialogueSequence.MultipleObjectsDetected => multipleObjectsDetectedSequence,
                Day1DialogueSequence.IdentifiesDrawing => identifiesDrawingSequence,
                Day1DialogueSequence.PreviousImageNotTransmitted => previousImageNotTransmittedSequence,
                Day1DialogueSequence.TransmissionLabelConfirmed => transmissionLabelConfirmedSequence,
                Day1DialogueSequence.ReactionEvaluationUnstable => reactionEvaluationUnstableSequence,
                Day1DialogueSequence.ResponsePatternLogged => responsePatternLoggedSequence,
                Day1DialogueSequence.ReactionNone => reactionNoneSequence,
                Day1DialogueSequence.ReactionSubtle => reactionSubtleSequence,
                Day1DialogueSequence.ReactionModerate => reactionModerateSequence,
                Day1DialogueSequence.ReactionStrong => reactionStrongSequence,
                Day1DialogueSequence.Complete => completeSequence,
                _ => null
            };

            return assigned != null
                ? assigned
                : Resources.Load<DialogueSequence>($"Dialogue/Day1/{GetDialogueResourceName(sequenceId)}");
        }

        private static string GetDialogueResourceName(Day1DialogueSequence sequenceId)
        {
            return sequenceId switch
            {
                Day1DialogueSequence.Opening => "Day1Opening",
                Day1DialogueSequence.TabletBlank => "Day1TabletBlank",
                Day1DialogueSequence.Scanning => "Day1Scanning",
                Day1DialogueSequence.ClassificationUnstable => "Day1ClassificationUnstable",
                Day1DialogueSequence.ActionOrSceneDetected => "Day1ActionOrSceneDetected",
                Day1DialogueSequence.WrittenTextDetected => "Day1WrittenTextDetected",
                Day1DialogueSequence.NonStimulusDetected => "Day1NonStimulusDetected",
                Day1DialogueSequence.MultipleObjectsDetected => "Day1MultipleObjectsDetected",
                Day1DialogueSequence.IdentifiesDrawing => "Day1IdentifiesDrawing",
                Day1DialogueSequence.PreviousImageNotTransmitted => "Day1PreviousImageNotTransmitted",
                Day1DialogueSequence.TransmissionLabelConfirmed => "Day1TransmissionLabelConfirmed",
                Day1DialogueSequence.ReactionEvaluationUnstable => "Day1ReactionEvaluationUnstable",
                Day1DialogueSequence.ResponsePatternLogged => "Day1ResponsePatternLogged",
                Day1DialogueSequence.ReactionNone => "Day1ReactionNone",
                Day1DialogueSequence.ReactionSubtle => "Day1ReactionSubtle",
                Day1DialogueSequence.ReactionModerate => "Day1ReactionModerate",
                Day1DialogueSequence.ReactionStrong => "Day1ReactionStrong",
                Day1DialogueSequence.Complete => "Day1Complete",
                _ => string.Empty
            };
        }

        private void ResolveReferences()
        {
            if (_context?.SceneReferences != null)
            {
                stimulusLibrary ??= _context.SceneReferences.Day1StimulusLibrary;
                terminalDisplay ??= _context.SceneReferences.TerminalDisplay;
                sharedMonitorDisplay ??= _context.SceneReferences.SharedMonitorDisplay;
                alienReactionController ??= _context.SceneReferences.AlienReactionController;
            }

            if (stimulusLibrary == null)
            {
                stimulusLibrary = GetComponent<Day1StimulusLibrary>() ?? gameObject.AddComponent<Day1StimulusLibrary>();
            }

            if (stimulusButtonPanel == null)
            {
                stimulusButtonPanel = GetComponent<Day1StimulusButtonPanel>() ?? gameObject.AddComponent<Day1StimulusButtonPanel>();
            }

            terminalDisplay ??= FindFirstObjectByType<TerminalDisplay>();
            sharedMonitorDisplay ??= FindFirstObjectByType<SharedMonitorDisplay>();
            alienReactionController ??= FindFirstObjectByType<AlienReactionController>();
        }

        private void ChangeState(GameState state)
        {
            _currentState = state;
            ApplyInteractionPolicy();
            StateChanged?.Invoke(state);
        }

        private void ApplyInteractionPolicy()
        {
            if (_context?.InteractionManager == null || _interactionPolicy == null)
            {
                return;
            }

            _context.InteractionManager.ConfigureInteractionPolicy(_interactionPolicy);
            _context.InteractionManager.ApplyStatePolicy(new InteractionStateContext(
                _currentState,
                roundStartReady: true,
                interpreterInspectionCompleted: true));
        }

        private void ApplyCameraMode(GameState state)
        {
            if (_context?.Camera == null)
            {
                return;
            }

            CameraMode mode = state switch
            {
                GameState.Drawing or GameState.PreviewAnalyzing or GameState.Preview => CameraMode.TabletView,
                GameState.Submitting or GameState.AlienReaction => CameraMode.AlienReaction,
                GameState.Ending => CameraMode.Default,
                _ => CameraMode.Default
            };
            _context.Camera.SetMode(mode);
        }

        private void StopActiveRoutine()
        {
            if (_routine != null)
            {
                StopCoroutine(_routine);
                _routine = null;
            }
        }

        private Texture2D CreateTextureFromPng(byte[] pngBytes)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            {
                name = $"Day1Stimulus_{_slot:00}",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            if (!texture.LoadImage(pngBytes, markNonReadable: false))
            {
                Destroy(texture);
                return null;
            }

            _ownedTextures.Add(texture);
            return texture;
        }

        private static SatisfactionLevel MapReactionTierToSatisfaction(ReactionTier reactionTier)
        {
            return reactionTier switch
            {
                ReactionTier.None => SatisfactionLevel.Neutral,
                ReactionTier.Subtle => SatisfactionLevel.Neutral,
                ReactionTier.Moderate => SatisfactionLevel.Satisfied,
                ReactionTier.Strong => SatisfactionLevel.VerySatisfied,
                _ => SatisfactionLevel.Neutral
            };
        }

        private static Day1DialogueSequence GetReactionSequence(ReactionTier reactionTier)
        {
            return reactionTier switch
            {
                ReactionTier.None => Day1DialogueSequence.ReactionNone,
                ReactionTier.Subtle => Day1DialogueSequence.ReactionSubtle,
                ReactionTier.Moderate => Day1DialogueSequence.ReactionModerate,
                ReactionTier.Strong => Day1DialogueSequence.ReactionStrong,
                _ => Day1DialogueSequence.ReactionSubtle
            };
        }

        private static string LocalizeSpeaker(string speaker)
        {
            return speaker switch
            {
                ScienceOfficer => L10n.T("speaker.science_officer", ScienceOfficer),
                Adjutant => L10n.T("speaker.adjutant", Adjutant),
                _ => speaker
            };
        }

        private enum Day1DialogueSequence
        {
            Opening,
            TabletBlank,
            Scanning,
            ClassificationUnstable,
            ActionOrSceneDetected,
            WrittenTextDetected,
            NonStimulusDetected,
            MultipleObjectsDetected,
            IdentifiesDrawing,
            PreviousImageNotTransmitted,
            TransmissionLabelConfirmed,
            ReactionEvaluationUnstable,
            ResponsePatternLogged,
            ReactionNone,
            ReactionSubtle,
            ReactionModerate,
            ReactionStrong,
            Complete
        }
    }

}
