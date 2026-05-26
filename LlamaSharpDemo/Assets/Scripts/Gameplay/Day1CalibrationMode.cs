using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
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
        [Tooltip("Stable gameplay mode id used by GameplayModeHost when this Day 1 calibration mode is active.")]
        [SerializeField] private string modeId = Day1ModeId;

        [Header("References")]
        [Tooltip("Stores accepted Day 1 stimulus samples and writes the final calibration payload.")]
        [SerializeField] private Day1StimulusLibrary stimulusLibrary;
        [Tooltip("Submit/confirm/redraw button presenter used during Day 1 drawing and preview states.")]
        [SerializeField] private Day1StimulusButtonPanel stimulusButtonPanel;
        [Tooltip("Terminal text display used for scan, lock, and captured brainwave readouts.")]
        [SerializeField] private TerminalDisplay terminalDisplay;
        [Tooltip("Shared monitor display used to show the player's submitted drawing to the aliens.")]
        [SerializeField] private SharedMonitorDisplay sharedMonitorDisplay;
        [Tooltip("Alien reaction controller that plays the response animation and reaction subtitles.")]
        [SerializeField] private AlienReactionController alienReactionController;

        [Header("Brainwave Signal")]
        [Tooltip("Seed for repeatable Day 1 brainwave waveforms. Use 0 to generate a fresh random seed each session.")]
        [SerializeField] private int brainwaveSessionSeed;

        [Header("Dialogue Sequences")]
        [Tooltip("Dialogue played when Day 1 begins, before the first drawing prompt.")]
        [SerializeField] private DialogueSequence openingSequence;
        [Tooltip("Dialogue played when the player submits an empty tablet.")]
        [SerializeField] private DialogueSequence tabletBlankSequence;
        [Tooltip("Dialogue played while the submitted drawing is being visually classified.")]
        [SerializeField] private DialogueSequence scanningSequence;
        [Tooltip("Dialogue played when visual classification fails or returns an unusable result.")]
        [SerializeField] private DialogueSequence classificationUnstableSequence;
        [Tooltip("Dialogue played when the drawing is classified as an action or scene instead of a single object.")]
        [SerializeField] private DialogueSequence actionOrSceneDetectedSequence;
        [Tooltip("Dialogue played when the classifier detects written text instead of a usable object.")]
        [SerializeField] private DialogueSequence writtenTextDetectedSequence;
        [Tooltip("Dialogue played when no usable stimulus object is detected.")]
        [SerializeField] private DialogueSequence nonStimulusDetectedSequence;
        [Tooltip("Dialogue played when too many objects are detected for Day 1 calibration.")]
        [SerializeField] private DialogueSequence multipleObjectsDetectedSequence;
        [Tooltip("Dialogue played when the system identifies a valid drawing and asks for confirmation.")]
        [SerializeField] private DialogueSequence identifiesDrawingSequence;
        [Tooltip("Dialogue played when the player rejects the identified label and redraws.")]
        [SerializeField] private DialogueSequence previousImageNotTransmittedSequence;
        [Tooltip("Dialogue played after the player confirms the drawing label and transmits it.")]
        [SerializeField] private DialogueSequence transmissionLabelConfirmedSequence;
        [Tooltip("Dialogue played when the alien reaction tier evaluation cannot be resolved.")]
        [SerializeField] private DialogueSequence reactionEvaluationUnstableSequence;
        [Tooltip("Dialogue played after a successful response pattern is logged and the next sample begins.")]
        [SerializeField] private DialogueSequence responsePatternLoggedSequence;
        [Tooltip("Dialogue used during the alien reaction cutaway for a no-response tier.")]
        [SerializeField] private DialogueSequence reactionNoneSequence;
        [Tooltip("Dialogue used during the alien reaction cutaway for a subtle-response tier.")]
        [SerializeField] private DialogueSequence reactionSubtleSequence;
        [Tooltip("Dialogue used during the alien reaction cutaway for a moderate-response tier.")]
        [SerializeField] private DialogueSequence reactionModerateSequence;
        [Tooltip("Dialogue used during the alien reaction cutaway for a strong-response tier.")]
        [SerializeField] private DialogueSequence reactionStrongSequence;
        [Tooltip("Dialogue played after all required Day 1 stimulus samples have been captured.")]
        [SerializeField] private DialogueSequence completeSequence;

        [Header("Timing")]
        [Tooltip("Seconds to hold on the terminal after transmission is confirmed before reaction evaluation begins.")]
        [SerializeField, Min(0f)] private float transmissionHoldSeconds = 0.6f;
        [Tooltip("Seconds for the terminal waveform to converge from searching noise into the locked reaction-tier pattern.")]
        [SerializeField, Min(0.05f)] private float brainwaveLockDuration = 0.9f;
        [Tooltip("Seconds to show the captured waveform and terminal readout after the alien reaction cutaway.")]
        [SerializeField, Min(0f)] private float brainwaveResultHoldSeconds = 1.25f;

        [Header("Reaction Evaluation")]
        [Tooltip("Maximum number of attempts to ask the AI pipeline for the Day 1 reaction tier before failing this sample.")]
        [SerializeField, Min(1)] private int reactionEvaluationMaxAttempts = 3;
        [Tooltip("Delay in seconds between Day 1 reaction-tier evaluation retries.")]
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
        private int _runtimeBrainwaveSessionSeed;
        private TerminalBrainwaveDisplay _brainwaveTerminal;

        public string ModeId => string.IsNullOrWhiteSpace(modeId) ? Day1ModeId : modeId;
        public GameState CurrentState => _currentState;

        public void Enter(GameplayModeContext context)
        {
            _entered = true;
            _context = context;
            _interactionPolicy = new Day1CalibrationInteractionPolicy();
            ResolveReferences();
            stimulusButtonPanel?.Hide();
            _context?.Drawing?.ClearRecognitionLabel();
            _context?.Drawing?.SetInteractionLocked(true);
            ApplyCameraMode(GameState.Title);
            ChangeState(GameState.Title);
        }

        public void Exit()
        {
            StopActiveRoutine();
            _context?.AiGateway?.CancelActiveOperations();
            _context?.DialogueSystem?.StopSequence();
            _brainwaveTerminal?.Clear();
            stimulusButtonPanel?.Hide();
            _context?.Drawing?.ClearRecognitionLabel();
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
            _context?.Drawing?.ClearRecognitionLabel();
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
            _runtimeBrainwaveSessionSeed = brainwaveSessionSeed != 0
                ? brainwaveSessionSeed
                : UnityEngine.Random.Range(1, int.MaxValue);

            stimulusLibrary?.BeginSession(clearExisting: true);
            terminalDisplay?.Clear();
            EnsureBrainwaveTerminal();
            _brainwaveTerminal?.Clear();
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
            _context?.Drawing?.ClearRecognitionLabel();

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
            _context?.Drawing?.ShowRecognitionLabel(_pendingDisplayLabel);
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
            _context?.Drawing?.ClearRecognitionLabel();
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
            _context?.Drawing?.ClearRecognitionLabel();
            _routine = StartCoroutine(ConfirmLabelRoutine(normalizedLabel));
        }

        private IEnumerator ConfirmLabelRoutine(string label)
        {
            string displayLabel = GetPendingDisplayLabel(label);
            _context?.Drawing?.SetInteractionLocked(true);
            ChangeState(GameState.Submitting);
            ApplyCameraMode(GameState.Submitting);

            sharedMonitorDisplay?.ShowSubmission(_pendingTexture);
            EnsureBrainwaveTerminal();
            _brainwaveTerminal?.PlaySearching(label, _slot, _runtimeBrainwaveSessionSeed);
            terminalDisplay?.ShowText(BuildBrainwaveSearchingText(label, displayLabel), instant: true);

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
                evaluation =>
                {
                    reactionTier = evaluation.reactionTier;
                    evaluationSucceeded = true;
                },
                error => evaluationError = error);

            if (!evaluationSucceeded)
            {
                Debug.LogWarning(
                    $"[Day1CalibrationMode] Day1 reaction tier evaluation failed for '{label}': {evaluationError}",
                    this);
                _brainwaveTerminal?.BeginTraceLock(
                    ReactionTier.None,
                    label,
                    _slot,
                    _runtimeBrainwaveSessionSeed,
                    brainwaveLockDuration);
                terminalDisplay?.ShowText(BuildBrainwaveUnstableText(label, displayLabel), instant: true);
                if (brainwaveLockDuration > 0f)
                {
                    yield return new WaitForSeconds(brainwaveLockDuration);
                }

                ChangeState(GameState.Preview);
                ApplyCameraMode(GameState.Preview);
                yield return PlayDialogueSequence(Day1DialogueSequence.ReactionEvaluationUnstable);
                stimulusButtonPanel?.ShowConfirmation(ConfirmLabel, RedrawCandidate);
                _routine = null;
                yield break;
            }

            _brainwaveTerminal?.BeginTraceLock(
                reactionTier,
                label,
                _slot,
                _runtimeBrainwaveSessionSeed,
                brainwaveLockDuration);
            terminalDisplay?.ShowText(BuildBrainwaveLockingText(label, displayLabel), instant: true);
            if (brainwaveLockDuration > 0f)
            {
                yield return new WaitForSeconds(brainwaveLockDuration);
            }

            ChangeState(GameState.AlienReaction);
            ApplyCameraMode(GameState.AlienReaction);
            yield return PlayReactionRoutine(reactionTier);

            ApplyCameraMode(GameState.Submitting);
            _brainwaveTerminal?.PlayLocked(reactionTier, label, _slot, _runtimeBrainwaveSessionSeed);
            terminalDisplay?.ShowText(BuildBrainwaveCapturedText(label, displayLabel, reactionTier), instant: true);
            if (brainwaveResultHoldSeconds > 0f)
            {
                yield return new WaitForSeconds(brainwaveResultHoldSeconds);
            }

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
            Action<Day1ReactionEvaluationResult> onSuccess,
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
                    onSuccess?.Invoke(evaluation);
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
            _context?.Drawing?.ClearRecognitionLabel();
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
            EnsureBrainwaveTerminal();
        }

        private void EnsureBrainwaveTerminal()
        {
            if (_brainwaveTerminal != null)
            {
                return;
            }

            if (terminalDisplay != null)
            {
                _brainwaveTerminal =
                    terminalDisplay.GetComponent<TerminalBrainwaveDisplay>() ??
                    terminalDisplay.GetComponentInChildren<TerminalBrainwaveDisplay>(true);
            }

            if (_brainwaveTerminal == null)
            {
                _brainwaveTerminal = FindFirstObjectByType<TerminalBrainwaveDisplay>();
            }
        }

        private string BuildBrainwaveSearchingText(string label, string displayLabel)
        {
            var builder = new StringBuilder();
            builder.AppendLine("[CONTACT TRACE ARRAY]");
            builder.Append("DRAWING: ").AppendLine(displayLabel);
            builder.AppendLine();
            builder.AppendLine("DRAWING FEED: LIVE");
            builder.AppendLine("FIELD: OPEN");
            builder.AppendLine("TRACE BANDS: SEPARATING");
            builder.AppendLine("BASELINE: SEEKING");
            builder.AppendLine("NOISE GATE: ACTIVE");
            builder.AppendLine("SHARED TRACE: NONE");
            builder.Append("> MEMORY WRITE: ARMED");
            return builder.ToString();
        }

        private string BuildBrainwaveLockingText(string label, string displayLabel)
        {
            var builder = new StringBuilder();
            builder.AppendLine("[CONTACT TRACE ARRAY]");
            builder.Append("DRAWING: ").AppendLine(displayLabel);
            builder.AppendLine();
            builder.AppendLine("SHARED TRACE: FOUND");
            builder.AppendLine("TRACE WINDOW: CLOSED");
            builder.AppendLine();
            builder.AppendLine("LOW BAND:    ALIGNING");
            builder.AppendLine("MID BAND:    ALIGNING");
            builder.AppendLine("HIGH BAND:   ALIGNING");
            builder.AppendLine();
            builder.AppendLine("FIELD SHAPE: CONVERGING");
            builder.Append("> MEMORY WRITE: LOCKING");
            return builder.ToString();
        }

        private string BuildBrainwaveCapturedText(
            string label,
            string displayLabel,
            ReactionTier reactionTier)
        {
            var builder = new StringBuilder();
            builder.Append("[TRACE MEMORY / ENTRY ").Append(_slot.ToString("00")).AppendLine("]");
            builder.Append("DRAWING: ").AppendLine(displayLabel);
            builder.AppendLine();
            AppendTraceBandLine(builder, "LOW BAND", label, reactionTier, 11);
            AppendTraceBandLine(builder, "MID BAND", label, reactionTier, 23);
            AppendTraceBandLine(builder, "HIGH BAND", label, reactionTier, 37);
            builder.AppendLine();
            builder.Append("SHARED TRACE: ").AppendLine(GetSharedTraceClass(reactionTier));
            builder.Append("REFERENCE MARK: ").AppendLine(GetReferenceMarkState(reactionTier));
            builder.Append("TRANSLATOR SEED: ").Append(GetBrainwaveTokenId(label));
            return builder.ToString();
        }

        private string BuildBrainwaveUnstableText(string label, string displayLabel)
        {
            var builder = new StringBuilder();
            builder.AppendLine("[CONTACT TRACE ARRAY]");
            builder.Append("DRAWING: ").AppendLine(displayLabel);
            builder.AppendLine();
            builder.AppendLine("DRAWING FEED: HELD");
            builder.AppendLine("SHARED TRACE: LOST");
            builder.AppendLine();
            builder.AppendLine("LOW BAND:    NOISE");
            builder.AppendLine("MID BAND:    NOISE");
            builder.AppendLine("HIGH BAND:   NOISE");
            builder.AppendLine();
            builder.AppendLine("BUFFER: PURGED");
            builder.AppendLine("REFERENCE MARK: REJECTED");
            builder.Append("> REDRAW REQUIRED");
            return builder.ToString();
        }

        private void AppendTraceBandLine(
            StringBuilder builder,
            string band,
            string label,
            ReactionTier reactionTier,
            int salt)
        {
            float amplitude = GetBrainwaveAmplitude(label, reactionTier, salt);
            builder
                .Append(band.PadRight(10))
                .Append(" ")
                .Append(amplitude.ToString("00.0"))
                .Append("uV  ")
                .AppendLine(GetTraceBandState(reactionTier));
        }

        private float GetBrainwaveAmplitude(string label, ReactionTier reactionTier, int salt)
        {
            (float min, float max) = reactionTier switch
            {
                ReactionTier.None => (1.4f, 3.8f),
                ReactionTier.Subtle => (4.6f, 8.8f),
                ReactionTier.Moderate => (9.5f, 17.5f),
                ReactionTier.Strong => (18.5f, 32.0f),
                _ => (4.6f, 8.8f)
            };

            return Mathf.Lerp(min, max, StableBrainwave01(label, salt));
        }

        private string GetSharedTraceClass(ReactionTier reactionTier)
        {
            return reactionTier switch
            {
                ReactionTier.None => "FAINT",
                ReactionTier.Subtle => "THIN",
                ReactionTier.Moderate => "STABLE",
                ReactionTier.Strong => "OVERBRIGHT",
                _ => "THIN"
            };
        }

        private string GetReferenceMarkState(ReactionTier reactionTier)
        {
            return reactionTier switch
            {
                ReactionTier.None => "LOW CONFIDENCE",
                ReactionTier.Subtle => "STORED",
                ReactionTier.Moderate => "STORED",
                ReactionTier.Strong => "HIGH VALUE",
                _ => "STORED"
            };
        }

        private string GetTraceBandState(ReactionTier reactionTier)
        {
            return reactionTier switch
            {
                ReactionTier.None => "THIN",
                ReactionTier.Subtle => "LOCK",
                ReactionTier.Moderate => "LOCK",
                ReactionTier.Strong => "LOCK",
                _ => "LOCK"
            };
        }

        private string GetBrainwaveTokenId(string label)
        {
            int hash = StableBrainwaveHash(label, 503) & 0x0FFF;
            return $"SAMPLE-{_slot:00}-{hash:X3}";
        }

        private float StableBrainwave01(string label, int salt)
        {
            int hash = StableBrainwaveHash(label, salt);
            return (hash & 0x00FFFFFF) / 16777215f;
        }

        private int StableBrainwaveHash(string label, int salt)
        {
            unchecked
            {
                int hash = 23;
                string normalizedLabel = label ?? string.Empty;
                for (int i = 0; i < normalizedLabel.Length; i++)
                {
                    hash = (hash * 31) + char.ToLowerInvariant(normalizedLabel[i]);
                }

                hash = (hash * 31) + _slot;
                hash = (hash * 31) + _runtimeBrainwaveSessionSeed;
                hash = (hash * 31) + salt;
                return hash == int.MinValue ? 0 : hash;
            }
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
                GameState.Submitting => CameraMode.TerminalZoom,
                GameState.AlienReaction => CameraMode.AlienReaction,
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
