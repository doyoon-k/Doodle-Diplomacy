using System;
using System.Collections;
using System.Collections.Generic;
using DoodleDiplomacy.Camera;
using DoodleDiplomacy.Core;
using DoodleDiplomacy.Devices;
using DoodleDiplomacy.Interaction;
using DoodleDiplomacy.Localization;
using UnityEngine;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    [DisallowMultipleComponent]
    public sealed class FirstContactTranslationMode : MonoBehaviour,
        IGameplayMode,
        IGameplaySessionController,
        IGameplayStateObservable
    {
        private const string DefaultModeId = "first-contact-translation";
        private const string ScienceOfficerSpeakerKey = "speaker.science_officer";
        private const string TabletDrawingInstructionText = "PRESS ENTER TO SEND";

        [Header("Mode")]
        [SerializeField] private string modeId = DefaultModeId;
        [SerializeField] private FirstContactModeConfig config;

        [Header("Runtime UI")]
        [SerializeField] private FirstContactActionButtonPanel actionButtonPanel;

        public event Action<GameState> StateChanged;

        private readonly List<Texture2D> _ownedTextures = new();
        private GameplayModeContext _context;
        private FirstContactInteractionPolicy _interactionPolicy;
        private FirstContactTerminalPresenter _terminalPresenter;
        private FirstContactQuestionProvider _questionProvider;
        private FirstContactEmbeddingService _embeddingService;
        private FirstContactUnknownResolver _unknownResolver;
        private FirstContactSemanticMemory _semanticMemory;
        private FirstContactSemanticMapLayout _semanticMapLayout = new();
        private FirstContactSessionContext _session;
        private Coroutine _routine;
        private GameState _currentGameState = GameState.Title;
        private FirstContactModeState _modeState = FirstContactModeState.Inactive;
        private int _fallbackQuestionIndex;
        private int _runtimeWaveformSessionSeed;

        private Texture2D _pendingTexture;
        private byte[] _pendingPngBytes;
        private string _pendingLabel;
        private string _pendingDisplayLabel;
        private FirstContactCardSource _pendingCardSource;
        private string _activeUnknownId;
        private bool _terminalChoiceInputEnabled;
        private FirstContactTerminalChoiceMode _terminalChoiceMode = FirstContactTerminalChoiceMode.None;
        private int _selectedTerminalChoiceIndex;
        private string _currentFallbackReason = string.Empty;
        private string _currentRejectedInputReason = string.Empty;
        private bool _terminalContinueRequested;

        public string ModeId => string.IsNullOrWhiteSpace(modeId) ? DefaultModeId : modeId.Trim();
        public GameState CurrentState => _currentGameState;

        public void Enter(GameplayModeContext context)
        {
            _context = context;
            ResolveRuntimeServices();
            _context?.Drawing?.SetInteractionLocked(true);
            _context?.Drawing?.ClearRecognitionLabel();
            _context?.Drawing?.ClearInstructionLabel();
            actionButtonPanel?.Hide();
            _terminalPresenter?.Clear();
            DisableTerminalChoices();
            ApplyInteractionPolicy();
            ChangeState(FirstContactModeState.Ready);
        }

        public void Exit()
        {
            StopActiveRoutine();
            _context?.AiGateway?.CancelActiveOperations();
            _context?.Drawing?.SetInteractionLocked(true);
            _context?.Drawing?.ClearRecognitionLabel();
            _context?.Drawing?.ClearInstructionLabel();
            actionButtonPanel?.Hide();
            _terminalPresenter?.Clear();
            DisableTerminalChoices();
            _context = null;
            ChangeState(FirstContactModeState.Inactive);
        }

        private void OnDestroy()
        {
            for (int i = 0; i < _ownedTextures.Count; i++)
            {
                if (_ownedTextures[i] != null)
                {
                    Destroy(_ownedTextures[i]);
                }
            }

            _ownedTextures.Clear();
        }

        public void Tick(float deltaTime)
        {
            HandleTerminalChoiceInput();
            HandleDrawingSubmitInput();
        }

        public void HandleInteraction(InteractionType type, InteractableObject source)
        {
            if (type == InteractionType.Tablet &&
                (_modeState == FirstContactModeState.DrawingDecodeSample ||
                 _modeState == FirstContactModeState.DrawingAnswer))
            {
                _context?.Camera?.SetMode(CameraMode.TabletView);
            }
            else if (type == InteractionType.Terminal)
            {
                _context?.Camera?.SetMode(CameraMode.TerminalZoom);
            }
        }

        public void StartGame(bool isFirstPlay = true)
        {
            StopActiveRoutine();
            _routine = StartCoroutine(StartFirstContactRoutine());
        }

        public void ChangeToTitle()
        {
            StopActiveRoutine();
            actionButtonPanel?.Hide();
            _context?.Drawing?.SetInteractionLocked(true);
            _context?.Drawing?.ClearRecognitionLabel();
            _context?.Drawing?.ClearInstructionLabel();
            _terminalPresenter?.Clear();
            DisableTerminalChoices();
            ChangeState(FirstContactModeState.Ready);
        }

        public void SubmitPreview()
        {
            if (_modeState == FirstContactModeState.ReviewingLabel)
            {
                ConfirmPendingDrawing();
                return;
            }

            if (_modeState == FirstContactModeState.ReviewingRejectedInput)
            {
                RedrawPending();
                return;
            }

            SubmitDrawing();
        }

        public void ModifyPreview()
        {
            if (_modeState == FirstContactModeState.ReviewingLabel ||
                _modeState == FirstContactModeState.ReviewingRejectedInput)
            {
                RedrawPending();
            }
        }

        private IEnumerator StartFirstContactRoutine()
        {
            ResolveRuntimeServices();
            _fallbackQuestionIndex = 0;
            _runtimeWaveformSessionSeed = UnityEngine.Random.Range(1, int.MaxValue);
            _semanticMapLayout.Reset(_runtimeWaveformSessionSeed);
            _session = new FirstContactSessionContext();
            _semanticMemory = new FirstContactSemanticMemory(
                _embeddingService,
                GetSemanticSettings(),
                GetDebugSettings());
            _context?.Drawing?.SetInteractionLocked(true);
            _context?.Drawing?.ClearRecognitionLabel();
            _context?.Drawing?.ClearInstructionLabel();
            _context?.SharedMonitorDisplay?.SetIdle();
            _terminalPresenter?.Clear();
            DisableTerminalChoices();
            yield return LoadNextQuestionRoutine();
            _routine = null;
        }

        private IEnumerator LoadNextQuestionRoutine()
        {
            ChangeState(FirstContactModeState.ReceivingQuestion);
            DisableTerminalChoices();
            actionButtonPanel?.Hide();
            _context?.Drawing?.SetInteractionLocked(true);
            _context?.Drawing?.ClearRecognitionLabel();
            _context?.Drawing?.ClearInstructionLabel();
            _context?.Camera?.SetMode(CameraMode.TerminalZoom);

            FirstContactPresentationSettings presentation = GetPresentationSettings();
            if (presentation.questionReceiveDelay > 0f)
            {
                yield return new WaitForSeconds(presentation.questionReceiveDelay);
            }

            AlienQuestion question = null;
            string fallbackReason = string.Empty;
            yield return _questionProvider.GetNextQuestion(
                _session,
                _fallbackQuestionIndex,
                (value, reason) =>
                {
                    question = value;
                    fallbackReason = reason;
                });
            _fallbackQuestionIndex++;

            if (question == null)
            {
                ChangeState(FirstContactModeState.Completed);
                yield break;
            }

            _session.CurrentQuestion = question;
            _session.TurnIndex = _fallbackQuestionIndex - 1;
            _currentFallbackReason = fallbackReason ?? string.Empty;
            yield return _unknownResolver.PrepareQuestion(question);
            RefreshSessionStableClusters();
            _terminalPresenter.ShowQuestion(question, instant: false, fallbackReason);
            ShowOfficerLine(question.DialogueKeys?.initial);
            yield return WaitForTerminalPresentationRoutine(presentation.questionReadHoldSeconds, presentation.waitForTerminalTypingBeforeActions);

            bool autoChanged = _unknownResolver.ApplyAutomaticClusterHints(question, _semanticMemory);
            if (autoChanged)
            {
                if (presentation.tokenUpdateDelay > 0f)
                {
                    yield return new WaitForSeconds(presentation.tokenUpdateDelay);
                }

                _terminalPresenter.ShowQuestion(question, instant: true, fallbackReason);
                yield return WaitForTerminalPresentationRoutine(presentation.updatedQuestionReadHoldSeconds, false);
            }

            ChangeState(FirstContactModeState.InspectingQuestion);
            EnableTerminalChoices(question);
        }

        private IEnumerator BeginDrawingRoutine(
            FirstContactCardSource source,
            string unknownId)
        {
            _pendingCardSource = source;
            _activeUnknownId = FirstContactUnknownSlotDefinition.NormalizeUnknownId(unknownId);
            _pendingTexture = null;
            _pendingPngBytes = null;
            _pendingLabel = string.Empty;
            _pendingDisplayLabel = string.Empty;
            DisableTerminalChoices();
            actionButtonPanel?.Hide();
            _context?.Drawing?.EnsureRuntimeEnabled();
            _context?.Drawing?.SetInteractionLocked(false);
            _context?.Drawing?.ClearCanvas();
            _context?.Drawing?.ClearRecognitionLabel();
            _context?.Drawing?.ShowInstructionLabel(TabletDrawingInstructionText);
            _context?.Camera?.SetMode(CameraMode.TabletView);
            if (source == FirstContactCardSource.DecodeSample)
            {
                ChangeState(FirstContactModeState.DrawingDecodeSample);
            }
            else
            {
                ChangeState(FirstContactModeState.DrawingAnswer);
            }

            yield return null;
            _routine = null;
        }

        private void SubmitDrawing()
        {
            if (_modeState != FirstContactModeState.DrawingDecodeSample &&
                _modeState != FirstContactModeState.DrawingAnswer)
            {
                return;
            }

            if (_context?.Drawing == null || !_context.Drawing.HasVisibleDrawing)
            {
                ShowContentRedrawPrompt("DRAW SOMETHING");
                return;
            }

            _context.Drawing.SetInteractionLocked(true);
            _context.Drawing.ClearInstructionLabel();
            actionButtonPanel?.Hide();
            StopActiveRoutine();
            _routine = StartCoroutine(AnalyzeDrawingRoutine());
        }

        private IEnumerator AnalyzeDrawingRoutine()
        {
            ChangeState(FirstContactModeState.AnalyzingDrawing);
            DisableTerminalChoices();
            actionButtonPanel?.Hide();
            _context?.Camera?.SetMode(CameraMode.TerminalZoom);
            _terminalPresenter?.ShowTabletImageReceived(_pendingCardSource, _activeUnknownId);
            FirstContactPresentationSettings presentation = GetPresentationSettings();
            float startTime = Time.time;

            bool captureSucceeded = false;
            string captureError = string.Empty;
            yield return CapturePendingDrawingWithRetries((succeeded, error) =>
            {
                captureSucceeded = succeeded;
                captureError = error;
            });

            if (!captureSucceeded)
            {
                LogFatalTechnicalFailure($"Drawing capture failed after retries: {captureError}");
                _routine = null;
                yield break;
            }

            VisualStimulusClassificationResult classification = null;
            string classificationError = string.Empty;
            yield return ClassifyPendingDrawingWithRetries((result, error) =>
            {
                classification = result;
                classificationError = error;
            });

            float elapsed = Time.time - startTime;
            if (elapsed < presentation.scanMinimumSeconds)
            {
                yield return new WaitForSeconds(presentation.scanMinimumSeconds - elapsed);
            }

            if (classification == null || !classification.IsSuccess)
            {
                if (TryGetClassificationErrorRedrawPrompt(classification, out string errorRedrawPrompt))
                {
                    ShowContentRedrawPrompt(errorRedrawPrompt);
                    _routine = null;
                    yield break;
                }

                LogFatalTechnicalFailure($"Drawing classifier failed after retries: {classificationError}");
                _routine = null;
                yield break;
            }

            if (TryGetContentRedrawPrompt(classification, out string redrawPrompt))
            {
                ShowContentRedrawPrompt(redrawPrompt);
                _routine = null;
                yield break;
            }

            _pendingLabel = Day1ReactionTierEvaluator.NormalizeLabel(classification.label);
            _pendingDisplayLabel = ResolveDisplayLabel(classification, _pendingLabel);
            if (presentation.labelRevealDelay > 0f)
            {
                yield return new WaitForSeconds(presentation.labelRevealDelay);
            }

            _context?.Drawing?.ClearRecognitionLabel();
            _context?.Drawing?.ClearInstructionLabel();
            _context?.Camera?.SetMode(CameraMode.TerminalZoom);
            ChangeState(FirstContactModeState.ReviewingLabel);
            EnableLabelReviewChoices();
            _routine = null;
        }

        private IEnumerator CapturePendingDrawingWithRetries(Action<bool, string> onComplete)
        {
            FirstContactVlmSettings vlmSettings = GetVlmSettings();
            int attempts = Mathf.Max(1, vlmSettings.captureRetryCount + 1);
            string lastError = string.Empty;

            for (int attempt = 1; attempt <= attempts; attempt++)
            {
                if (_context?.Drawing == null)
                {
                    onComplete?.Invoke(false, "Drawing feature is missing.");
                    yield break;
                }

                if (_context.Drawing.TryExportPngBytes(out byte[] pngBytes, out string error) &&
                    pngBytes != null &&
                    pngBytes.Length > 0)
                {
                    Texture2D texture = CreateTextureFromPng(pngBytes);
                    if (texture != null)
                    {
                        _pendingPngBytes = pngBytes;
                        _pendingTexture = texture;
                        onComplete?.Invoke(true, string.Empty);
                        yield break;
                    }

                    lastError = "Exported PNG could not be loaded into a Texture2D.";
                }
                else
                {
                    lastError = string.IsNullOrWhiteSpace(error)
                        ? "Drawing export returned no PNG bytes."
                        : error.Trim();
                }

                if (attempt < attempts && vlmSettings.technicalRetryDelaySeconds > 0f)
                {
                    yield return new WaitForSeconds(vlmSettings.technicalRetryDelaySeconds);
                }
            }

            onComplete?.Invoke(false, lastError);
        }

        private IEnumerator ClassifyPendingDrawingWithRetries(
            Action<VisualStimulusClassificationResult, string> onComplete)
        {
            FirstContactVlmSettings vlmSettings = GetVlmSettings();
            int attempts = Mathf.Max(1, vlmSettings.classifierRetryCount + 1);
            VisualStimulusClassificationResult lastResult = null;
            string lastError = string.Empty;

            for (int attempt = 1; attempt <= attempts; attempt++)
            {
                VisualStimulusClassificationResult result = null;
                bool done = false;
                yield return ClassifyPendingDrawing(value =>
                {
                    result = value;
                    done = true;
                });
                while (!done)
                {
                    yield return null;
                }

                if (result != null && result.IsSuccess)
                {
                    onComplete?.Invoke(result, string.Empty);
                    yield break;
                }

                lastResult = result;
                lastError = string.IsNullOrWhiteSpace(result?.error)
                    ? "Classifier returned no result."
                    : result.error.Trim();

                if (IsFatalClassificationFailure(lastError))
                {
                    onComplete?.Invoke(lastResult, lastError);
                    yield break;
                }

                if (attempt < attempts && vlmSettings.technicalRetryDelaySeconds > 0f)
                {
                    yield return new WaitForSeconds(vlmSettings.technicalRetryDelaySeconds);
                }
            }

            onComplete?.Invoke(lastResult, lastError);
        }

        private IEnumerator ClassifyPendingDrawing(Action<VisualStimulusClassificationResult> onComplete)
        {
            FirstContactVlmSettings vlmSettings = GetVlmSettings();
            if (_pendingTexture == null)
            {
                onComplete?.Invoke(VisualStimulusClassificationResult.Failed("Drawing texture is unavailable."));
                yield break;
            }

            if (vlmSettings.visualClassifierPipeline != null)
            {
                if (GamePipelineRunner.Instance == null)
                {
                    onComplete?.Invoke(VisualStimulusClassificationResult.Failed("GamePipelineRunner is missing."));
                    yield break;
                }

                var state = new PipelineState();
                state.SetImage(
                    string.IsNullOrWhiteSpace(vlmSettings.imageStateKey) ? "reference_image" : vlmSettings.imageStateKey,
                    _pendingTexture);

                bool done = false;
                PipelineState finalState = null;
                GamePipelineRunner.Instance.RunPipeline(vlmSettings.visualClassifierPipeline, state, result =>
                {
                    finalState = result;
                    done = true;
                });
                yield return new WaitUntil(() => done);

                if (VisualStimulusClassificationResult.TryFromPipelineState(
                        finalState,
                        out VisualStimulusClassificationResult classification))
                {
                    onComplete?.Invoke(classification);
                    yield break;
                }

                onComplete?.Invoke(classification ?? VisualStimulusClassificationResult.Failed("Classification unstable."));
                yield break;
            }

            bool gatewayDone = false;
            VisualStimulusClassificationResult gatewayResult = null;
            _context?.AiGateway?.ClassifyVisualStimulus(result =>
            {
                gatewayResult = result;
                gatewayDone = true;
            });

            if (_context?.AiGateway == null)
            {
                gatewayDone = true;
                gatewayResult = VisualStimulusClassificationResult.Failed("AI gateway is missing.");
            }

            yield return new WaitUntil(() => gatewayDone);
            onComplete?.Invoke(gatewayResult);
        }

        private void ConfirmPendingDrawing()
        {
            if (_modeState != FirstContactModeState.ReviewingLabel)
            {
                return;
            }

            StopActiveRoutine();
            _routine = StartCoroutine(ConfirmPendingDrawingRoutine());
        }

        private IEnumerator ConfirmPendingDrawingRoutine()
        {
            DisableTerminalChoices();
            actionButtonPanel?.Hide();
            _context?.Drawing?.SetInteractionLocked(true);
            _context?.Drawing?.ClearInstructionLabel();
            ChangeState(_pendingCardSource == FirstContactCardSource.Answer
                ? FirstContactModeState.TransmittingAnswer
                : FirstContactModeState.UpdatingTranslation);
            _context?.Camera?.SetMode(CameraMode.TerminalZoom);

            EmbeddingResult embedding = default;
            yield return _embeddingService.EmbedLabel(_pendingLabel, result => embedding = result);
            if (!embedding.IsValid && GetSemanticSettings().failCardWhenEmbeddingMissing)
            {
                Debug.LogWarning($"[FirstContactTranslationMode] Embedding failed: {embedding.Error}", this);
                RedrawPending();
                yield break;
            }

            var card = new SemanticCardRecord
            {
                Texture = _pendingTexture,
                PngBytes = _pendingPngBytes,
                Label = _pendingLabel,
                LocalizedLabel = _pendingDisplayLabel,
                Embedding = embedding.Vector,
                Source = _pendingCardSource,
                TargetUnknownId = _pendingCardSource == FirstContactCardSource.DecodeSample ? _activeUnknownId : string.Empty,
                QuestionId = _session?.CurrentQuestion?.Id ?? string.Empty,
                TurnIndex = _session?.TurnIndex ?? 0
            };

            _semanticMemory.TryCreateWaveformProfile(card, _runtimeWaveformSessionSeed, out var waveform);
            card.WaveformProfile = waveform;
            _semanticMemory.AddCard(card);
            _session?.RecentCards.Add(card);
            SemanticClusterRecord cluster = _semanticMemory.FindCluster(card.ClusterId);

            FirstContactPresentationSettings presentation = GetPresentationSettings();
            if (presentation.cardRevealDelay > 0f)
            {
                yield return new WaitForSeconds(presentation.cardRevealDelay);
            }

            _terminalPresenter?.ShowCard(card, cluster);
            yield return WaitForTerminalContinueRoutine();

            if (_pendingCardSource == FirstContactCardSource.DecodeSample)
            {
                yield return ResolveDecodeCardRoutine(card, cluster);
            }
            else
            {
                yield return TransmitAnswerCardRoutine(card);
            }

            _routine = null;
        }

        private IEnumerator ResolveDecodeCardRoutine(SemanticCardRecord card, SemanticClusterRecord cluster)
        {
            UnknownSlot slot = _session?.CurrentQuestion?.FindUnknown(_activeUnknownId);
            FirstContactResolutionResult result = _unknownResolver.EvaluateCard(card, slot);
            IReadOnlyList<FirstContactSlotScore> slotScores = BuildSlotScores(card, _activeUnknownId);
            FirstContactSemanticMapSnapshot mapSnapshot = BuildSemanticMapSnapshot(card, _activeUnknownId);
            if (GetDebugSettings().logSimilarityScores && slot != null)
            {
                Debug.Log(
                    $"[FirstContactTranslationMode] Decode score card={card.Label} slot={slot.Id} " +
                    $"stage={result.NewStage} score={result.Score:0.000}");
            }

            if (GetSemanticSettings().showSemanticMapFeedback)
            {
                _terminalPresenter?.ShowSemanticAnalysis(
                    card,
                    cluster,
                    slotScores,
                    result,
                    mapSnapshot,
                    GetSemanticSettings());
                yield return WaitForTerminalContinueRoutine();
            }

            if (cluster != null && cluster.IsStable)
            {
                _terminalPresenter?.ShowCluster(cluster);
                yield return new WaitForSeconds(GetPresentationSettings().tokenUpdateDelay);
            }

            if (result.Changed && GetPresentationSettings().tokenUpdateDelay > 0f)
            {
                yield return new WaitForSeconds(GetPresentationSettings().tokenUpdateDelay);
            }

            RefreshSessionStableClusters();
            _unknownResolver.ApplyAutomaticClusterHints(_session.CurrentQuestion, _semanticMemory);
            _terminalPresenter?.ShowQuestion(_session.CurrentQuestion, instant: true);
            ChangeState(FirstContactModeState.InspectingQuestion);
            _context?.Camera?.SetMode(CameraMode.TerminalZoom);
            yield return WaitForTerminalPresentationRoutine(GetPresentationSettings().updatedQuestionReadHoldSeconds, false);
            EnableTerminalChoices(_session.CurrentQuestion);
        }

        private IEnumerator TransmitAnswerCardRoutine(SemanticCardRecord card)
        {
            _session.PreviousAnswer = card;
            if (GetSemanticSettings().showSemanticMapFeedback)
            {
                FirstContactSemanticMapSnapshot mapSnapshot = BuildSemanticMapSnapshot(card, string.Empty);
                _terminalPresenter?.ShowSemanticAnalysis(
                    card,
                    _semanticMemory.FindCluster(card.ClusterId),
                    Array.Empty<FirstContactSlotScore>(),
                    null,
                    mapSnapshot,
                    GetSemanticSettings());
                yield return WaitForTerminalContinueRoutine();
            }

            _context?.SharedMonitorDisplay?.ShowSubmission(card.Texture);
            _terminalPresenter?.ShowAnswerTransmitted(card);
            ShowOfficerLine(_session?.CurrentQuestion?.DialogueKeys?.answerSent);
            FirstContactPresentationSettings presentation = GetPresentationSettings();
            if (presentation.answerTransmitHoldSeconds > 0f)
            {
                yield return new WaitForSeconds(presentation.answerTransmitHoldSeconds);
            }

            if (HasConsumedFallbackQuestions())
            {
                ChangeState(FirstContactModeState.Completed);
                actionButtonPanel?.Hide();
                yield break;
            }

            if (presentation.nextQuestionDelay > 0f)
            {
                yield return new WaitForSeconds(presentation.nextQuestionDelay);
            }

            yield return LoadNextQuestionRoutine();
        }

        private void RedrawPending()
        {
            StopActiveRoutine();
            DisableTerminalChoices();
            actionButtonPanel?.Hide();
            _context?.Drawing?.ClearRecognitionLabel();
            _context?.Drawing?.ClearInstructionLabel();
            _routine = StartCoroutine(RedrawPendingRoutine());
        }

        private IEnumerator RedrawPendingRoutine()
        {
            _context?.Drawing?.SetInteractionLocked(true);
            _context?.Drawing?.ClearInstructionLabel();
            _context?.Camera?.SetMode(CameraMode.TerminalZoom);
            _terminalPresenter?.ShowTabletLinkOpen(
                _session?.CurrentQuestion,
                _pendingCardSource,
                _activeUnknownId);

            float holdSeconds = GetPresentationSettings().tabletLinkOpenHoldSeconds;
            if (holdSeconds > 0f)
            {
                yield return new WaitForSeconds(holdSeconds);
            }

            yield return BeginDrawingRoutine(_pendingCardSource, _activeUnknownId);
            _routine = null;
        }

        private void EnableTerminalChoices(AlienQuestion question)
        {
            if (question == null)
            {
                DisableTerminalChoices();
                return;
            }

            _selectedTerminalChoiceIndex = 0;
            _terminalChoiceInputEnabled = true;
            _terminalChoiceMode = FirstContactTerminalChoiceMode.QuestionActions;
            _terminalPresenter?.ShowQuestionChoices(
                question,
                _selectedTerminalChoiceIndex,
                BuildSemanticMapSnapshot(GetMostRecentCard(), string.Empty),
                GetSemanticSettings(),
                instant: true,
                fallbackReason: _currentFallbackReason,
                initialProbeUnknownId: GetInitialProbeUnknownId(question));
        }

        private void EnableSemanticMapChoices()
        {
            _selectedTerminalChoiceIndex = 0;
            _terminalChoiceInputEnabled = true;
            _terminalChoiceMode = FirstContactTerminalChoiceMode.SemanticMap;
            _terminalPresenter?.ShowSemanticMapChoices(
                _session?.CurrentQuestion,
                _selectedTerminalChoiceIndex,
                BuildSemanticMapSnapshot(GetMostRecentCard(), string.Empty),
                GetSemanticSettings());
        }

        private void EnableLabelReviewChoices()
        {
            _selectedTerminalChoiceIndex = 0;
            _terminalChoiceInputEnabled = true;
            _terminalChoiceMode = FirstContactTerminalChoiceMode.LabelReview;
            _terminalPresenter?.ShowLabelReview(
                _pendingCardSource,
                _activeUnknownId,
                _pendingDisplayLabel,
                _selectedTerminalChoiceIndex);
        }

        private void EnableRejectedInputChoice(string reason)
        {
            _selectedTerminalChoiceIndex = 0;
            _terminalChoiceInputEnabled = true;
            _terminalChoiceMode = FirstContactTerminalChoiceMode.RejectedInput;
            _terminalPresenter?.ShowInputRejected(reason, _selectedTerminalChoiceIndex);
        }

        private void EnableContinueChoice()
        {
            _terminalContinueRequested = false;
            _selectedTerminalChoiceIndex = 0;
            _terminalChoiceInputEnabled = true;
            _terminalChoiceMode = FirstContactTerminalChoiceMode.Continue;
        }

        private void DisableTerminalChoices()
        {
            _terminalChoiceInputEnabled = false;
            _terminalChoiceMode = FirstContactTerminalChoiceMode.None;
            _selectedTerminalChoiceIndex = 0;
        }

        private void HandleTerminalChoiceInput()
        {
            if (!_terminalChoiceInputEnabled)
            {
                return;
            }

            int choiceCount = GetTerminalChoiceCount();
            if (choiceCount <= 0)
            {
                return;
            }

            if (WasKeyPressed(KeyCode.UpArrow) || WasKeyPressed(KeyCode.W))
            {
                MoveTerminalChoiceSelection(-1, choiceCount);
                return;
            }

            if (WasKeyPressed(KeyCode.DownArrow) || WasKeyPressed(KeyCode.S))
            {
                MoveTerminalChoiceSelection(1, choiceCount);
                return;
            }

            if (WasKeyPressed(KeyCode.Return) || WasKeyPressed(KeyCode.KeypadEnter))
            {
                SelectTerminalChoice(_selectedTerminalChoiceIndex);
                return;
            }

            int numericChoiceCount = Mathf.Min(choiceCount, 9);
            for (int i = 0; i < numericChoiceCount; i++)
            {
                KeyCode alphaKey = (KeyCode)((int)KeyCode.Alpha1 + i);
                KeyCode keypadKey = (KeyCode)((int)KeyCode.Keypad1 + i);
                if (WasKeyPressed(alphaKey) || WasKeyPressed(keypadKey))
                {
                    SelectTerminalChoice(i);
                    return;
                }
            }

        }

        private void HandleDrawingSubmitInput()
        {
            if (_modeState != FirstContactModeState.DrawingDecodeSample &&
                _modeState != FirstContactModeState.DrawingAnswer)
            {
                return;
            }

            if (WasKeyPressed(KeyCode.Return) ||
                WasKeyPressed(KeyCode.KeypadEnter))
            {
                SubmitDrawing();
            }
        }

        private static bool WasKeyPressed(KeyCode keyCode)
        {
#if ENABLE_INPUT_SYSTEM
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null)
            {
                var keyControl = GetInputSystemKeyControl(keyboard, keyCode);
                return keyControl != null && keyControl.wasPressedThisFrame;
            }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
            return keyCode != KeyCode.None && Input.GetKeyDown(keyCode);
#else
            return false;
#endif
        }

#if ENABLE_INPUT_SYSTEM
        private static UnityEngine.InputSystem.Controls.KeyControl GetInputSystemKeyControl(
            UnityEngine.InputSystem.Keyboard keyboard,
            KeyCode keyCode)
        {
            return keyCode switch
            {
                KeyCode.UpArrow => keyboard.upArrowKey,
                KeyCode.DownArrow => keyboard.downArrowKey,
                KeyCode.W => keyboard.wKey,
                KeyCode.S => keyboard.sKey,
                KeyCode.Return => keyboard.enterKey,
                KeyCode.KeypadEnter => keyboard.numpadEnterKey,
                KeyCode.Alpha1 => keyboard.digit1Key,
                KeyCode.Alpha2 => keyboard.digit2Key,
                KeyCode.Alpha3 => keyboard.digit3Key,
                KeyCode.Alpha4 => keyboard.digit4Key,
                KeyCode.Alpha5 => keyboard.digit5Key,
                KeyCode.Alpha6 => keyboard.digit6Key,
                KeyCode.Alpha7 => keyboard.digit7Key,
                KeyCode.Alpha8 => keyboard.digit8Key,
                KeyCode.Alpha9 => keyboard.digit9Key,
                KeyCode.Keypad1 => keyboard.numpad1Key,
                KeyCode.Keypad2 => keyboard.numpad2Key,
                KeyCode.Keypad3 => keyboard.numpad3Key,
                KeyCode.Keypad4 => keyboard.numpad4Key,
                KeyCode.Keypad5 => keyboard.numpad5Key,
                KeyCode.Keypad6 => keyboard.numpad6Key,
                KeyCode.Keypad7 => keyboard.numpad7Key,
                KeyCode.Keypad8 => keyboard.numpad8Key,
                KeyCode.Keypad9 => keyboard.numpad9Key,
                _ => null
            };
        }
#endif

        private void MoveTerminalChoiceSelection(int direction, int choiceCount)
        {
            _selectedTerminalChoiceIndex =
                (_selectedTerminalChoiceIndex + direction + choiceCount) % choiceCount;
            RefreshActiveTerminalChoices();
        }

        private int GetTerminalChoiceCount()
        {
            return _terminalChoiceMode switch
            {
                FirstContactTerminalChoiceMode.QuestionActions => GetQuestionActionChoiceCount(),
                FirstContactTerminalChoiceMode.SemanticMap => (_session?.CurrentQuestion?.UnknownSlots.Count ?? 0) + 2,
                FirstContactTerminalChoiceMode.LabelReview => 2,
                FirstContactTerminalChoiceMode.RejectedInput => 1,
                FirstContactTerminalChoiceMode.Continue => 1,
                _ => 0
            };
        }

        private int GetQuestionActionChoiceCount()
        {
            AlienQuestion question = _session?.CurrentQuestion;
            if (!string.IsNullOrWhiteSpace(GetInitialProbeUnknownId(question)))
            {
                return 1;
            }

            return (question?.UnknownSlots.Count ?? 0) + 2;
        }

        private string GetInitialProbeUnknownId(AlienQuestion question)
        {
            if (question == null ||
                _session?.RecentCards == null ||
                _session.RecentCards.Count > 0)
            {
                return string.Empty;
            }

            for (int i = 0; i < question.UnknownSlots.Count; i++)
            {
                UnknownSlot slot = question.UnknownSlots[i];
                if (slot != null && slot.Stage != FirstContactTranslationStage.Solved)
                {
                    return slot.Id;
                }
            }

            return string.Empty;
        }

        private void SelectTerminalChoice(int choiceIndex)
        {
            switch (_terminalChoiceMode)
            {
                case FirstContactTerminalChoiceMode.QuestionActions:
                    SelectQuestionChoice(choiceIndex);
                    break;
                case FirstContactTerminalChoiceMode.SemanticMap:
                    SelectSemanticMapChoice(choiceIndex);
                    break;
                case FirstContactTerminalChoiceMode.LabelReview:
                    SelectLabelReviewChoice(choiceIndex);
                    break;
                case FirstContactTerminalChoiceMode.RejectedInput:
                    SelectRejectedInputChoice(choiceIndex);
                    break;
                case FirstContactTerminalChoiceMode.Continue:
                    SelectContinueChoice(choiceIndex);
                    break;
            }
        }

        private void SelectQuestionChoice(int choiceIndex)
        {
            AlienQuestion question = _session?.CurrentQuestion;
            int choiceCount = GetTerminalChoiceCount();
            if (question == null || choiceIndex < 0 || choiceIndex >= choiceCount)
            {
                return;
            }

            string initialProbeUnknownId = GetInitialProbeUnknownId(question);
            if (!string.IsNullOrWhiteSpace(initialProbeUnknownId))
            {
                DisableTerminalChoices();
                StopActiveRoutine();
                _routine = StartCoroutine(ConfirmTerminalChoiceRoutine(
                    FirstContactCardSource.DecodeSample,
                    initialProbeUnknownId));
                return;
            }

            bool isAnswerChoice = choiceIndex == choiceCount - 1;
            bool isMapChoice = choiceIndex == choiceCount - 2;
            if (isMapChoice)
            {
                EnableSemanticMapChoices();
                return;
            }

            string unknownId = isAnswerChoice
                ? string.Empty
                : question.UnknownSlots[choiceIndex].Id;

            DisableTerminalChoices();
            StopActiveRoutine();
            _routine = StartCoroutine(ConfirmTerminalChoiceRoutine(
                isAnswerChoice ? FirstContactCardSource.Answer : FirstContactCardSource.DecodeSample,
                unknownId));
        }

        private void SelectSemanticMapChoice(int choiceIndex)
        {
            AlienQuestion question = _session?.CurrentQuestion;
            int choiceCount = GetTerminalChoiceCount();
            if (question == null || choiceIndex < 0 || choiceIndex >= choiceCount)
            {
                return;
            }

            if (choiceIndex == 0)
            {
                EnableTerminalChoices(question);
                return;
            }

            bool isAnswerChoice = choiceIndex == choiceCount - 1;
            int unknownIndex = choiceIndex - 1;
            string unknownId = isAnswerChoice
                ? string.Empty
                : question.UnknownSlots[unknownIndex].Id;

            DisableTerminalChoices();
            StopActiveRoutine();
            _routine = StartCoroutine(ConfirmTerminalChoiceRoutine(
                isAnswerChoice ? FirstContactCardSource.Answer : FirstContactCardSource.DecodeSample,
                unknownId));
        }

        private void SelectLabelReviewChoice(int choiceIndex)
        {
            if (_modeState != FirstContactModeState.ReviewingLabel)
            {
                return;
            }

            if (choiceIndex == 0)
            {
                ConfirmPendingDrawing();
            }
            else if (choiceIndex == 1)
            {
                RedrawPending();
            }
        }

        private void SelectRejectedInputChoice(int choiceIndex)
        {
            if (_modeState != FirstContactModeState.ReviewingRejectedInput || choiceIndex != 0)
            {
                return;
            }

            RedrawPending();
        }

        private void SelectContinueChoice(int choiceIndex)
        {
            if (choiceIndex != 0)
            {
                return;
            }

            _terminalContinueRequested = true;
            DisableTerminalChoices();
        }

        private void RefreshActiveTerminalChoices()
        {
            switch (_terminalChoiceMode)
            {
                case FirstContactTerminalChoiceMode.QuestionActions:
                    _terminalPresenter?.ShowQuestionChoices(
                        _session?.CurrentQuestion,
                        _selectedTerminalChoiceIndex,
                        BuildSemanticMapSnapshot(GetMostRecentCard(), string.Empty),
                        GetSemanticSettings(),
                        instant: true,
                        fallbackReason: _currentFallbackReason,
                        initialProbeUnknownId: GetInitialProbeUnknownId(_session?.CurrentQuestion));
                    break;
                case FirstContactTerminalChoiceMode.SemanticMap:
                    _terminalPresenter?.ShowSemanticMapChoices(
                        _session?.CurrentQuestion,
                        _selectedTerminalChoiceIndex,
                        BuildSemanticMapSnapshot(GetMostRecentCard(), string.Empty),
                        GetSemanticSettings());
                    break;
                case FirstContactTerminalChoiceMode.LabelReview:
                    _terminalPresenter?.ShowLabelReview(
                        _pendingCardSource,
                        _activeUnknownId,
                        _pendingDisplayLabel,
                        _selectedTerminalChoiceIndex);
                    break;
                case FirstContactTerminalChoiceMode.RejectedInput:
                    _terminalPresenter?.ShowInputRejected(
                        _currentRejectedInputReason,
                        _selectedTerminalChoiceIndex);
                    break;
                case FirstContactTerminalChoiceMode.Continue:
                    break;
            }
        }

        private IEnumerator ConfirmTerminalChoiceRoutine(
            FirstContactCardSource source,
            string unknownId)
        {
            actionButtonPanel?.Hide();
            string choiceLabel = source == FirstContactCardSource.Answer
                ? FirstContactTerminalPresenter.AnswerActionLabel
                : FirstContactTerminalPresenter.BuildProbeActionLabel(unknownId);
            _terminalPresenter?.ShowQuestionChoiceEcho(_session?.CurrentQuestion, choiceLabel);

            float echoSeconds = GetPresentationSettings().choiceConfirmEchoSeconds;
            if (echoSeconds > 0f)
            {
                yield return new WaitForSeconds(echoSeconds);
            }

            _terminalPresenter?.ShowTabletLinkOpen(_session?.CurrentQuestion, source, unknownId);

            float linkHoldSeconds = GetPresentationSettings().tabletLinkOpenHoldSeconds;
            if (linkHoldSeconds > 0f)
            {
                yield return new WaitForSeconds(linkHoldSeconds);
            }

            yield return BeginDrawingRoutine(source, unknownId);
            _routine = null;
        }

        private void ShowContentRedrawPrompt(string prompt)
        {
            string safePrompt = string.IsNullOrWhiteSpace(prompt) ? "DRAW ONE OBJECT" : prompt.Trim();
            _currentRejectedInputReason = safePrompt;
            _context?.Drawing?.SetInteractionLocked(true);
            _context?.Drawing?.ClearRecognitionLabel();
            _context?.Drawing?.ClearInstructionLabel();
            actionButtonPanel?.Hide();
            _context?.Camera?.SetMode(CameraMode.TerminalZoom);
            ChangeState(FirstContactModeState.ReviewingRejectedInput);
            EnableRejectedInputChoice(safePrompt);
        }

        private bool TryGetContentRedrawPrompt(VisualStimulusClassificationResult result, out string prompt)
        {
            prompt = string.Empty;
            FirstContactVlmSettings vlmSettings = GetVlmSettings();
            string normalized = Day1ReactionTierEvaluator.NormalizeLabel(result?.label);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                prompt = "DRAW ONE OBJECT";
                return true;
            }

            if (vlmSettings.rejectWrittenText && Day1StimulusSubmissionPolicy.IsWrittenTextLabel(normalized))
            {
                prompt = "TEXT OR SYMBOL DETECTED";
                return true;
            }

            if (vlmSettings.rejectActionOrScene && Day1StimulusSubmissionPolicy.IsActionOrSceneLabel(normalized))
            {
                prompt = "DRAW ONE OBJECT";
                return true;
            }

            if (vlmSettings.rejectBlank && Day1StimulusSubmissionPolicy.IsBlockedLabel(normalized))
            {
                prompt = "DRAW ONE OBJECT";
                return true;
            }

            if (vlmSettings.rejectMultipleObjects && !Day1StimulusSubmissionPolicy.IsAllowedObjectCount(result.objectCount, normalized))
            {
                prompt = result.objectCount <= 0 ? "DRAW ONE OBJECT" : "DRAW ONE OBJECT ONLY";
                return true;
            }

            return false;
        }

        private static bool TryGetClassificationErrorRedrawPrompt(
            VisualStimulusClassificationResult result,
            out string prompt)
        {
            prompt = string.Empty;
            if (result == null || result.IsSuccess || string.IsNullOrWhiteSpace(result.error))
            {
                return false;
            }

            if (result.error.Trim().Equals("Drawing is blank.", StringComparison.OrdinalIgnoreCase))
            {
                prompt = "DRAW SOMETHING";
                return true;
            }

            return false;
        }

        private static bool IsFatalClassificationFailure(string error)
        {
            if (string.IsNullOrWhiteSpace(error))
            {
                return false;
            }

            string normalized = error.Trim().ToLowerInvariant();
            return normalized.Contains("ai gateway is missing") ||
                   normalized.Contains("ai bridge is missing") ||
                   normalized.Contains("classifier pipeline is not assigned") ||
                   normalized.Contains("visual classifier pipeline is not assigned") ||
                   normalized.Contains("gamepipelinerunner is missing") ||
                   normalized.Contains("drawing texture is unavailable");
        }

        private void LogFatalTechnicalFailure(string message)
        {
            Debug.LogError($"[FirstContactTranslationMode] {message}", this);
        }

        private string ResolveDisplayLabel(VisualStimulusClassificationResult result, string fallbackLabel)
        {
            FirstContactVlmSettings vlmSettings = GetVlmSettings();
            if (vlmSettings.useLocalizedLabelForDisplay && !string.IsNullOrWhiteSpace(result?.localizedLabel))
            {
                return result.localizedLabel.Trim();
            }

            return L10n.Label(fallbackLabel);
        }

        private Texture2D CreateTextureFromPng(byte[] pngBytes)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            {
                name = $"FirstContactDrawing_{_ownedTextures.Count + 1:000}",
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

        private void ResolveRuntimeServices()
        {
            if (actionButtonPanel == null)
            {
                actionButtonPanel = GetComponent<FirstContactActionButtonPanel>();
            }

            _terminalPresenter = new FirstContactTerminalPresenter(
                _context?.TerminalDisplay ?? FindFirstObjectByType<TerminalDisplay>(),
                GetDebugSettings());
            IEmbeddingService embeddingRuntime =
                (GamePipelineRunner.Instance?.RuntimeService as IEmbeddingService) ??
                (LlmServiceLocator.Current as IEmbeddingService);
            _embeddingService = new FirstContactEmbeddingService(embeddingRuntime, GetSemanticSettings());
            _unknownResolver = new FirstContactUnknownResolver(_embeddingService, GetSemanticSettings());
            _questionProvider = new FirstContactQuestionProvider(GetQuestionSettings(), GetDebugSettings());
            _interactionPolicy = new FirstContactInteractionPolicy();
            _session ??= new FirstContactSessionContext();
            _semanticMapLayout ??= new FirstContactSemanticMapLayout();
        }

        private void RefreshSessionStableClusters()
        {
            if (_session == null || _semanticMemory == null)
            {
                return;
            }

            _session.StableClusters.Clear();
            IReadOnlyList<SemanticClusterRecord> stable = _semanticMemory.StableClusters;
            for (int i = 0; i < stable.Count; i++)
            {
                _session.StableClusters.Add(stable[i]);
            }
        }

        private void ShowOfficerLine(string localizationKey)
        {
            if (string.IsNullOrWhiteSpace(localizationKey))
            {
                return;
            }

            string line = L10n.T(localizationKey, string.Empty);
            if (string.IsNullOrWhiteSpace(line))
            {
                return;
            }

            string speaker = L10n.T(ScienceOfficerSpeakerKey, "Science Officer");
            _context?.Subtitles?.Show(speaker, line);
        }

        private IReadOnlyList<FirstContactSlotScore> BuildSlotScores(
            SemanticCardRecord card,
            string activeUnknownId)
        {
            var scores = new List<FirstContactSlotScore>();
            AlienQuestion question = _session?.CurrentQuestion;
            if (question == null || _unknownResolver == null)
            {
                return scores;
            }

            string normalizedActiveId = FirstContactUnknownSlotDefinition.NormalizeUnknownId(activeUnknownId);
            for (int i = 0; i < question.UnknownSlots.Count; i++)
            {
                UnknownSlot slot = question.UnknownSlots[i];
                float score = _unknownResolver.ScoreCardAgainstSlot(card, slot);
                scores.Add(new FirstContactSlotScore(
                    slot,
                    score,
                    _unknownResolver.DetermineStageForScore(score),
                    string.Equals(slot?.Id, normalizedActiveId, StringComparison.OrdinalIgnoreCase)));
            }

            return scores;
        }

        private FirstContactSemanticMapSnapshot BuildSemanticMapSnapshot(
            SemanticCardRecord activeCard,
            string activeUnknownId)
        {
            _semanticMapLayout ??= new FirstContactSemanticMapLayout();
            return _semanticMapLayout.BuildSnapshot(
                _session?.CurrentQuestion,
                _semanticMemory?.Cards,
                _semanticMemory?.Clusters,
                activeCard,
                activeUnknownId,
                GetSemanticSettings());
        }

        private SemanticCardRecord GetMostRecentCard()
        {
            if (_session?.RecentCards == null || _session.RecentCards.Count == 0)
            {
                return null;
            }

            return _session.RecentCards[_session.RecentCards.Count - 1];
        }

        private IEnumerator WaitForTerminalPresentationRoutine(float holdSeconds, bool waitForTyping)
        {
            TerminalDisplay terminal = _context?.TerminalDisplay;
            if (waitForTyping && terminal != null)
            {
                yield return null;
                while (terminal.IsTyping())
                {
                    yield return null;
                }
            }

            if (holdSeconds > 0f)
            {
                yield return new WaitForSeconds(holdSeconds);
            }
        }

        private IEnumerator WaitForTerminalContinueRoutine()
        {
            EnableContinueChoice();
            while (!_terminalContinueRequested)
            {
                yield return null;
            }
        }

        private void ChangeState(FirstContactModeState state)
        {
            _modeState = state;
            GameState gameState = state switch
            {
                FirstContactModeState.DrawingDecodeSample => GameState.Drawing,
                FirstContactModeState.DrawingAnswer => GameState.Drawing,
                FirstContactModeState.AnalyzingDrawing => GameState.PreviewAnalyzing,
                FirstContactModeState.ReviewingLabel => GameState.Interpreter,
                FirstContactModeState.ReviewingRejectedInput => GameState.Interpreter,
                FirstContactModeState.TransmittingAnswer => GameState.Submitting,
                FirstContactModeState.Completed => GameState.Ending,
                FirstContactModeState.Ready => GameState.Title,
                _ => GameState.Interpreter
            };

            if (_currentGameState != gameState)
            {
                _currentGameState = gameState;
                ApplyInteractionPolicy();
                StateChanged?.Invoke(gameState);
            }
        }

        private void ApplyInteractionPolicy()
        {
            if (_context?.InteractionManager == null || _interactionPolicy == null)
            {
                return;
            }

            _context.InteractionManager.ConfigureInteractionPolicy(_interactionPolicy);
            _context.InteractionManager.ApplyStatePolicy(new InteractionStateContext(
                _currentGameState,
                roundStartReady: true,
                interpreterInspectionCompleted: true));
        }

        private void StopActiveRoutine()
        {
            if (_routine != null)
            {
                StopCoroutine(_routine);
                _routine = null;
            }
        }

        private bool HasConsumedFallbackQuestions()
        {
            FirstContactQuestionSettings settings = GetQuestionSettings();
            if (settings.loopFallbackQuestions)
            {
                return false;
            }

            int fallbackCount = settings.fallbackQuestionSet != null &&
                                settings.fallbackQuestionSet.questions != null &&
                                settings.fallbackQuestionSet.questions.Length > 0
                ? settings.fallbackQuestionSet.questions.Length
                : FirstContactRuntimeFallbackQuestions.Count;
            return _fallbackQuestionIndex >= fallbackCount;
        }

        private FirstContactPresentationSettings GetPresentationSettings()
        {
            return config != null && config.presentationSettings != null
                ? config.presentationSettings
                : ScriptableObject.CreateInstance<FirstContactPresentationSettings>();
        }

        private FirstContactSemanticSettings GetSemanticSettings()
        {
            return config != null && config.semanticSettings != null
                ? config.semanticSettings
                : ScriptableObject.CreateInstance<FirstContactSemanticSettings>();
        }

        private FirstContactVlmSettings GetVlmSettings()
        {
            return config != null && config.vlmSettings != null
                ? config.vlmSettings
                : ScriptableObject.CreateInstance<FirstContactVlmSettings>();
        }

        private FirstContactQuestionSettings GetQuestionSettings()
        {
            return config != null && config.questionSettings != null
                ? config.questionSettings
                : ScriptableObject.CreateInstance<FirstContactQuestionSettings>();
        }

        private FirstContactDebugSettings GetDebugSettings()
        {
            return config != null && config.debugSettings != null
                ? config.debugSettings
                : ScriptableObject.CreateInstance<FirstContactDebugSettings>();
        }

        private enum FirstContactModeState
        {
            Inactive,
            Ready,
            ReceivingQuestion,
            InspectingQuestion,
            DrawingDecodeSample,
            DrawingAnswer,
            AnalyzingDrawing,
            ReviewingLabel,
            ReviewingRejectedInput,
            UpdatingTranslation,
            TransmittingAnswer,
            Completed
        }

        private enum FirstContactTerminalChoiceMode
        {
            None,
            QuestionActions,
            SemanticMap,
            LabelReview,
            RejectedInput,
            Continue
        }
    }

    public sealed class FirstContactInteractionPolicy : IInteractionPolicy
    {
        public bool IsAllowed(InteractionStateContext context, InteractionType interactionType)
        {
            return context.State switch
            {
                GameState.Drawing => interactionType == InteractionType.Tablet,
                GameState.Preview => interactionType == InteractionType.Tablet,
                GameState.Interpreter => interactionType == InteractionType.Terminal ||
                                         interactionType == InteractionType.Tablet,
                GameState.Submitting => false,
                GameState.Ending => false,
                _ => false
            };
        }
    }
}
