using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
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
        private const int BootstrapRequiredTraceCount = 3;
        private const int MaxProbeLabelLength = 32;

        [Header("Mode")]
        [SerializeField] private string modeId = DefaultModeId;
        [SerializeField] private FirstContactModeConfig config;

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
        private bool _pendingTranslationAvailable;
        private string _currentProbeLabelInput;
        private FirstContactCardSource _pendingCardSource;
        private string _activeUnknownId;
        private bool _terminalChoiceInputEnabled;
        private FirstContactTerminalChoiceMode _terminalChoiceMode = FirstContactTerminalChoiceMode.None;
        private int _selectedTerminalChoiceIndex;
        private string _currentFallbackReason = string.Empty;
        private string _currentRejectedInputReason = string.Empty;
        private FirstContactTechnicalRetryAction _technicalRetryAction = FirstContactTechnicalRetryAction.None;
        private string _technicalFailureStatus = string.Empty;
        private bool _terminalContinueRequested;
        private bool _incomingTransmissionChoicesActive;
        private bool _hasShownBootstrapDuplicateOfficerLine;
        private bool _officerLineVisible;
        private bool _officerLineAwaitingDismissal;
        private int _officerLineShownFrame = -1;
        private int _lastSubmitInputFrame = -1;
        private bool _terminalProbeLabelInputActive;
        private string _terminalProbeLabelStatus = string.Empty;
        private string _queuedTerminalProbeLabelText = string.Empty;
        private string _currentProbeLabelComposition = string.Empty;
        private int _lastTerminalProbeLabelTextInputFrame = -1;
        private bool _hasPreviousImeCompositionMode;
        private IMECompositionMode _previousImeCompositionMode;
#if ENABLE_INPUT_SYSTEM
        private UnityEngine.InputSystem.Keyboard _subscribedTextInputKeyboard;
        private string _inputSystemProbeLabelComposition = string.Empty;
#endif
        private readonly List<BootstrapCategoryState> _bootstrapCategories = new();
        private int _bootstrapCategoryIndex;

        public string ModeId => string.IsNullOrWhiteSpace(modeId) ? DefaultModeId : modeId.Trim();
        public GameState CurrentState => _currentGameState;

        public void Enter(GameplayModeContext context)
        {
            _context = context;
            ResolveRuntimeServices();
            _context?.Drawing?.SetInteractionLocked(true);
            _context?.Drawing?.ClearRecognitionLabel();
            _context?.Drawing?.ClearInstructionLabel();
            _terminalPresenter?.Clear();
            DisableTerminalChoices();
            ClearTechnicalFailureState();
            _incomingTransmissionChoicesActive = false;
            ApplyInteractionPolicy();
            ChangeState(FirstContactModeState.Ready);
        }

        public void Exit()
        {
            StopActiveRoutine();
            EndTerminalProbeLabelInput();
            GamePipelineRunner.Instance?.StopGeneration();
            HideOfficerLine(force: true);
            _context?.Drawing?.SetInteractionLocked(true);
            _context?.Drawing?.ClearRecognitionLabel();
            _context?.Drawing?.ClearInstructionLabel();
            _terminalPresenter?.Clear();
            DisableTerminalChoices();
            ClearTechnicalFailureState();
            _incomingTransmissionChoicesActive = false;
            _context = null;
            ChangeState(FirstContactModeState.Inactive);
        }

        private void OnDestroy()
        {
            EndTerminalProbeLabelInput();
#if ENABLE_INPUT_SYSTEM
            UnsubscribeTerminalProbeLabelTextInput();
#endif
            for (int i = 0; i < _ownedTextures.Count; i++)
            {
                if (_ownedTextures[i] != null)
                {
                    Destroy(_ownedTextures[i]);
                }
            }

            _ownedTextures.Clear();
        }

        private void OnEnable()
        {
#if ENABLE_INPUT_SYSTEM
            SubscribeTerminalProbeLabelTextInput();
#endif
        }

        private void OnDisable()
        {
            EndTerminalProbeLabelInput();
#if ENABLE_INPUT_SYSTEM
            UnsubscribeTerminalProbeLabelTextInput();
#endif
            HideOfficerLine(force: true);
        }

        public void Tick(float deltaTime)
        {
            if (HandleOfficerLineDismissInput())
            {
                return;
            }

            if (HandleTerminalProbeLabelInput())
            {
                return;
            }

            HandleTerminalChoiceInput();
            HandleDrawingSubmitInput();
        }

        public void HandleInteraction(InteractionType type, InteractableObject source)
        {
            if (type == InteractionType.Tablet &&
                (_modeState == FirstContactModeState.DrawingDecodeSample ||
                 _modeState == FirstContactModeState.DrawingAnswer ||
                 _modeState == FirstContactModeState.DrawingBootstrapProbe))
            {
                HideOfficerLine();
                _context?.Camera?.SetMode(CameraMode.TabletView);
            }
            else if (type == InteractionType.Terminal)
            {
                HideOfficerLine(force: true);
                _context?.Camera?.SetMode(CameraMode.TerminalZoom);
            }
        }

        public void StartGame(bool isFirstPlay = true)
        {
            StopActiveRoutine();
            HideOfficerLine(force: true);
            _routine = StartCoroutine(StartFirstContactRoutine());
        }

        public void ChangeToTitle()
        {
            StopActiveRoutine();
            HideOfficerLine(force: true);
            _context?.Drawing?.SetInteractionLocked(true);
            _context?.Drawing?.ClearRecognitionLabel();
            _context?.Drawing?.ClearInstructionLabel();
            _terminalPresenter?.Clear();
            DisableTerminalChoices();
            _incomingTransmissionChoicesActive = false;
            ChangeState(FirstContactModeState.Ready);
        }

        public void SubmitPreview()
        {
            if (_modeState == FirstContactModeState.ReviewingLabel)
            {
                SubmitProbeLabel();
                return;
            }

            if (_modeState == FirstContactModeState.ReviewingRejectedInput)
            {
                RedrawPending();
                return;
            }

            if (_modeState == FirstContactModeState.ReviewingTechnicalFailure)
            {
                SelectTechnicalRetryChoice(_selectedTerminalChoiceIndex);
                return;
            }

            SubmitDrawing();
        }

        public void ModifyPreview()
        {
            if (_modeState == FirstContactModeState.ReviewingLabel ||
                _modeState == FirstContactModeState.ReviewingRejectedInput ||
                _modeState == FirstContactModeState.ReviewingTechnicalFailure)
            {
                RedrawPending();
            }
        }

        private IEnumerator StartFirstContactRoutine()
        {
            ResolveRuntimeServices();
            _fallbackQuestionIndex = 0;
            ClearTechnicalFailureState();
            _runtimeWaveformSessionSeed = UnityEngine.Random.Range(1, int.MaxValue);
            _semanticMapLayout.Reset(_runtimeWaveformSessionSeed);
            _session = new FirstContactSessionContext();
            _hasShownBootstrapDuplicateOfficerLine = false;
            InitializeBootstrapCategories();
            _semanticMemory = new FirstContactSemanticMemory(
                _embeddingService,
                GetSemanticSettings(),
                GetDebugSettings());
            yield return PrepareBootstrapCategoryDescriptorsRoutine();
            _context?.Drawing?.SetInteractionLocked(true);
            _context?.Drawing?.ClearRecognitionLabel();
            _context?.Drawing?.ClearInstructionLabel();
            _context?.SharedMonitorDisplay?.SetIdle();
            _terminalPresenter?.Clear();
            DisableTerminalChoices();
            _incomingTransmissionChoicesActive = false;
            yield return StartBootstrapProbeSequenceRoutine();
            _routine = null;
        }

        private void InitializeBootstrapCategories()
        {
            _bootstrapCategories.Clear();
            _bootstrapCategoryIndex = 0;
            AddBootstrapCategory(
                "danger",
                "DANGER",
                "[DANGER?]",
                "concrete visible subjects whose ordinary identity is a source or instrument of harm, threat, injury, poisoning, fire, explosion, attack, or other direct hazard");
            AddBootstrapCategory(
                "protection",
                "PROTECTION",
                "[PROTECTION?]",
                "concrete visible subjects whose ordinary identity is protection, defense, shelter, guarding, or blocking harm");
            AddBootstrapCategory(
                "food",
                "FOOD",
                "[FOOD?]",
                "concrete visible subjects whose ordinary identity is something eaten, drunk, prepared as a meal, or used as food");
            AddBootstrapCategory(
                "tool",
                "TOOL",
                "[TOOL?]",
                "concrete visible subjects whose ordinary identity is a tool, instrument, device, or implement for doing work");
        }

        private void AddBootstrapCategory(string id, string displayName, string meaning, string descriptor)
        {
            _bootstrapCategories.Add(new BootstrapCategoryState(
                id,
                displayName,
                meaning,
                descriptor,
                GetBootstrapRequiredTraceCount()));
        }

        private IEnumerator PrepareBootstrapCategoryDescriptorsRoutine()
        {
            if (_embeddingService == null || _bootstrapCategories.Count == 0)
            {
                yield break;
            }

            var descriptors = new string[_bootstrapCategories.Count];
            for (int i = 0; i < _bootstrapCategories.Count; i++)
            {
                descriptors[i] = _bootstrapCategories[i].DescriptorText;
            }

            IReadOnlyList<EmbeddingResult> results = null;
            yield return _embeddingService.EmbedLabels(descriptors, value => results = value);

            for (int i = 0; i < _bootstrapCategories.Count; i++)
            {
                EmbeddingResult result = results != null && i < results.Count ? results[i] : default;
                if (result.IsValid)
                {
                    _bootstrapCategories[i].SetDescriptorEmbedding(result.Vector);
                    continue;
                }

                Debug.LogWarning(
                    $"[FirstContactTranslationMode] Bootstrap category descriptor embedding failed. " +
                    $"category={_bootstrapCategories[i].Id} descriptor='{_bootstrapCategories[i].DescriptorText}' error='{result.Error}'",
                    this);
            }
        }

        private int GetBootstrapRequiredTraceCount()
        {
            return Math.Max(2, GetSemanticSettings().bootstrapMinTraceCount);
        }

        private BootstrapCategoryState GetActiveBootstrapCategory()
        {
            return _bootstrapCategoryIndex >= 0 && _bootstrapCategoryIndex < _bootstrapCategories.Count
                ? _bootstrapCategories[_bootstrapCategoryIndex]
                : null;
        }

        private IEnumerator StartBootstrapProbeSequenceRoutine()
        {
            BootstrapCategoryState category = GetActiveBootstrapCategory();
            if (category == null)
            {
                ChangeState(FirstContactModeState.BootstrapComplete);
                _terminalPresenter?.ShowBootstrapComplete(instant: false);
                yield return WaitForTerminalContinueRoutine();
                ChangeState(FirstContactModeState.Completed);
                yield break;
            }

            ChangeState(FirstContactModeState.BootstrapProbeSequence);
            DisableTerminalChoices();
            _pendingCardSource = FirstContactCardSource.BootstrapProbe;
            _activeUnknownId = string.Empty;
            _context?.Drawing?.SetInteractionLocked(true);
            _context?.Drawing?.ClearRecognitionLabel();
            _context?.Drawing?.ClearInstructionLabel();
            _context?.Camera?.SetMode(CameraMode.TerminalZoom);
            _terminalPresenter?.ShowBootstrapProbeSequence(
                category.DisplayName,
                category.TraceCount,
                category.RequiredTraceCount,
                category.IsStable,
                _selectedTerminalChoiceIndex,
                instant: false);
            EnableBootstrapProbeChoice();
        }

        private IEnumerator LoadNextQuestionRoutine()
        {
            ChangeState(FirstContactModeState.ReceivingQuestion);
            DisableTerminalChoices();
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
            yield return PlayIncomingTransmissionRoutine(question, fallbackReason);
            ShowOfficerLine(question.DialogueKeys?.initial);
            yield return WaitForTerminalPresentationRoutine(presentation.questionReadHoldSeconds, presentation.waitForTerminalTypingBeforeActions);

            bool autoChanged = _unknownResolver.ApplyAutomaticClusterHints(question, _semanticMemory);
            if (autoChanged)
            {
                if (presentation.tokenUpdateDelay > 0f)
                {
                    yield return new WaitForSeconds(presentation.tokenUpdateDelay);
                }

                _terminalPresenter.ShowQuestion(question, instant: false, fallbackReason: fallbackReason);
                yield return WaitForTerminalPresentationRoutine(presentation.updatedQuestionReadHoldSeconds, false);
            }

            ChangeState(FirstContactModeState.InspectingQuestion);
            EnableTerminalChoices(question);
        }

        private IEnumerator PlayIncomingTransmissionRoutine(AlienQuestion question, string fallbackReason)
        {
            FirstContactPresentationSettings presentation = GetPresentationSettings();
            if (!presentation.showIncomingTokenStream ||
                question == null ||
                question.PrimitiveTokens == null ||
                question.PrimitiveTokens.Count == 0)
            {
                _terminalPresenter.ShowQuestion(question, instant: false, fallbackReason: fallbackReason);
                _incomingTransmissionChoicesActive = false;
                yield break;
            }

            _incomingTransmissionChoicesActive = true;
            _terminalPresenter?.BeginIncomingTransmissionStream(BuildIncomingStreamSeed(question));
            for (int i = 0; i < question.PrimitiveTokens.Count; i++)
            {
                string token = question.PrimitiveTokens[i];
                UnknownSlot slot = question.FindUnknown(token);
                bool isUnknownToken = slot != null;
                BrainwaveSemanticProfile signalProfile = isUnknownToken
                    ? BuildUnknownSignalProfile(slot, null)
                    : BuildAlienTokenSignalProfile(token, i);
                float holdSeconds = isUnknownToken
                    ? presentation.incomingUnknownTokenHoldSeconds
                    : presentation.incomingTokenHoldSeconds;
                float signalDuration = Mathf.Max(
                    0.12f,
                    holdSeconds * (isUnknownToken ? 1.35f : 0.85f));
                float signalIntensity = isUnknownToken ? 1.35f : 0.72f;
                _terminalPresenter?.ShowIncomingTransmissionToken(
                    question,
                    i,
                    isUnknownToken,
                    signalProfile,
                    signalDuration,
                    signalIntensity,
                    instant: false);

                if (holdSeconds > 0f)
                {
                    yield return new WaitForSeconds(holdSeconds);
                }
            }

            _terminalPresenter?.CompleteIncomingTransmissionStream();
        }

        private IEnumerator BeginDrawingRoutine(
            FirstContactCardSource source,
            string unknownId,
            bool preserveCanvas = false)
        {
            _pendingCardSource = source;
            _activeUnknownId = FirstContactUnknownSlotDefinition.NormalizeUnknownId(unknownId);
            _pendingTexture = null;
            _pendingPngBytes = null;
            _pendingLabel = string.Empty;
            _pendingDisplayLabel = string.Empty;
            _pendingTranslationAvailable = false;
            if (!preserveCanvas)
            {
                _currentProbeLabelInput = string.Empty;
            }

            DisableTerminalChoices();
            _context?.Drawing?.EnsureRuntimeEnabled();
            _context?.Drawing?.SetInteractionLocked(false);
            if (!preserveCanvas)
            {
                _context?.Drawing?.ClearCanvas();
            }

            _context?.Drawing?.ClearRecognitionLabel();
            _context?.Drawing?.ShowInstructionLabel(GetDrawingInstructionText(source));
            _context?.Camera?.SetMode(CameraMode.TabletView);
            if (source == FirstContactCardSource.BootstrapProbe)
            {
                ChangeState(FirstContactModeState.DrawingBootstrapProbe);
            }
            else if (source == FirstContactCardSource.DecodeSample)
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
                _modeState != FirstContactModeState.DrawingAnswer &&
                _modeState != FirstContactModeState.DrawingBootstrapProbe)
            {
                return;
            }

            HideOfficerLine();
            if (_context?.Drawing == null || !_context.Drawing.HasVisibleDrawing)
            {
                StopActiveRoutine();
                _routine = StartCoroutine(ShowContentRedrawPromptRoutine(
                    "DRAW SOMETHING",
                    "first_contact.officer.probe_blank"));
                return;
            }

            _context.Drawing.SetInteractionLocked(true);
            _context.Drawing.ClearInstructionLabel();
            StopActiveRoutine();
            _routine = StartCoroutine(OpenProbeLabelEntryRoutine());
        }

        private void SubmitProbeLabel()
        {
            SubmitProbeLabel(_context?.TerminalDisplay != null && _context.TerminalDisplay.IsTextInputActive
                ? _context.TerminalDisplay.TextInputValue
                : _currentProbeLabelInput);
        }

        private void SubmitProbeLabel(string labelInput)
        {
            if (_modeState != FirstContactModeState.ReviewingLabel)
            {
                return;
            }

            if (!TryPreparePlayerProbeLabel(labelInput, out string canonicalLabel, out string displayLabel))
            {
                _currentProbeLabelInput = labelInput ?? string.Empty;
                _terminalProbeLabelStatus = L10n.T("first_contact.terminal.line.label_required", "LABEL REQUIRED");
                RefreshTerminalProbeLabelEntry(instant: false);
                return;
            }

            _pendingLabel = canonicalLabel;
            _pendingDisplayLabel = displayLabel;
            EndTerminalProbeLabelInput();
            _currentProbeLabelInput = displayLabel;
            _terminalProbeLabelStatus = string.Empty;
            HideOfficerLine();
            StopActiveRoutine();
            _routine = StartCoroutine(AnalyzeDrawingRoutine());
        }

        private static bool TryPreparePlayerProbeLabel(
            string input,
            out string canonicalLabel,
            out string displayLabel)
        {
            canonicalLabel = string.Empty;
            displayLabel = string.Empty;
            if (string.IsNullOrWhiteSpace(input))
            {
                return false;
            }

            displayLabel = NormalizePlayerLabelText(input);
            canonicalLabel = NormalizeProbeLabel(displayLabel);
            return !string.IsNullOrWhiteSpace(canonicalLabel);
        }

        private IEnumerator OpenProbeLabelEntryRoutine()
        {
            ChangeState(FirstContactModeState.AnalyzingDrawing);
            DisableTerminalChoices();
            _context?.Camera?.SetMode(CameraMode.TerminalZoom);
            yield return WaitForCameraTransitionRoutine();

            bool captureSucceeded = false;
            string captureError = string.Empty;
            yield return CapturePendingDrawingWithRetries((succeeded, error) =>
            {
                captureSucceeded = succeeded;
                captureError = error;
            });

            if (!captureSucceeded)
            {
                ShowTechnicalFailurePrompt(
                    FirstContactTechnicalRetryAction.CaptureDrawing,
                    "image_capture_failed",
                    "IMAGE CAPTURE FAILED",
                    $"Drawing capture failed after retries: {captureError}");
                _routine = null;
                yield break;
            }

            _context?.Drawing?.ClearRecognitionLabel();
            _context?.Drawing?.ClearInstructionLabel();
            _context?.Camera?.SetMode(CameraMode.TerminalZoom);
            yield return WaitForCameraTransitionRoutine();
            ChangeState(FirstContactModeState.ReviewingLabel);
            BeginTerminalProbeLabelInput();
            _routine = null;
        }

        private IEnumerator AnalyzeDrawingRoutine()
        {
            ChangeState(FirstContactModeState.AnalyzingDrawing);
            DisableTerminalChoices();
            _context?.Camera?.SetMode(CameraMode.TerminalZoom);
            _terminalPresenter?.ShowProbeDispatching(
                _pendingCardSource,
                _activeUnknownId,
                _pendingDisplayLabel,
                GetPendingProbeDispatchCategory(),
                _pendingTexture,
                BuildProbeDispatchSignalProfile(),
                BuildProbeDispatchStreamSeed());
            FirstContactPresentationSettings presentation = GetPresentationSettings();
            float startTime = Time.time;

            FirstContactProbeLabelResult labelResult = null;
            yield return PreparePendingProbeLabelRoutine(result => labelResult = result);
            if (labelResult == null || !labelResult.IsSuccess)
            {
                ShowTechnicalFailurePrompt(
                    FirstContactTechnicalRetryAction.AnalyzeDrawing,
                    "label_analysis_failed",
                    "LABEL ANALYSIS FAILED",
                    $"Probe label analysis failed: {labelResult?.Error ?? "No result."}");
                _routine = null;
                yield break;
            }

            if (labelResult != null && labelResult.IsSuccess && !labelResult.IsSuitable)
            {
                ShowProbeLabelUnsuitablePrompt(labelResult);
                _routine = null;
                yield break;
            }

            FirstContactProbeValidationResult validation = null;
            string validationError = string.Empty;
            yield return ValidatePendingProbeWithRetries((result, error) =>
            {
                validation = result;
                validationError = error;
            });

            float elapsed = Time.time - startTime;
            if (elapsed < presentation.scanMinimumSeconds)
            {
                yield return new WaitForSeconds(presentation.scanMinimumSeconds - elapsed);
            }

            if (validation == null || !validation.IsSuccess)
            {
                if (TryGetValidationErrorRedrawPrompt(
                        validation,
                        out string errorRedrawPrompt,
                        out string errorOfficerLineKey))
                {
                    ShowContentRedrawPrompt(errorRedrawPrompt, errorOfficerLineKey);
                    _routine = null;
                    yield break;
                }

                ShowTechnicalFailurePrompt(
                    FirstContactTechnicalRetryAction.AnalyzeDrawing,
                    "image_validation_failed",
                    "IMAGE VALIDATION FAILED",
                    $"Probe validator failed after retries: {validationError}");
                _routine = null;
                yield break;
            }

            if (TryGetContentRedrawPrompt(validation, out string redrawPrompt, out string officerLineKey))
            {
                ShowContentRedrawPrompt(redrawPrompt, officerLineKey);
                _routine = null;
                yield break;
            }

            if (presentation.labelRevealDelay > 0f)
            {
                _terminalPresenter?.ShowProbeDispatchAccepted(
                    _pendingCardSource,
                    _activeUnknownId,
                    _pendingDisplayLabel,
                    GetPendingProbeDispatchCategory(),
                    _pendingTexture,
                    BuildProbeDispatchSignalProfile(),
                    BuildProbeDispatchStreamSeed(),
                    instant: false);
                yield return new WaitForSeconds(presentation.labelRevealDelay);
            }

            yield return ConfirmPendingDrawingRoutine();
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

        private IEnumerator ValidatePendingProbeWithRetries(
            Action<FirstContactProbeValidationResult, string> onComplete)
        {
            FirstContactVlmSettings vlmSettings = GetVlmSettings();
            int attempts = Mathf.Max(1, vlmSettings.validatorRetryCount + 1);
            FirstContactProbeValidationResult lastResult = null;
            string lastError = string.Empty;

            for (int attempt = 1; attempt <= attempts; attempt++)
            {
                FirstContactProbeValidationResult result = null;
                bool done = false;
                yield return ValidatePendingProbe(value =>
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
                lastError = string.IsNullOrWhiteSpace(result?.Error)
                    ? "Validator returned no result."
                    : result.Error.Trim();

                if (IsFatalValidationFailure(lastError))
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

        private IEnumerator ValidatePendingProbe(Action<FirstContactProbeValidationResult> onComplete)
        {
            FirstContactVlmSettings vlmSettings = GetVlmSettings();
            if (_pendingTexture == null)
            {
                onComplete?.Invoke(FirstContactProbeValidationResult.Failed("Drawing texture is unavailable."));
                yield break;
            }

            if (vlmSettings.probeValidationPipeline == null)
            {
                onComplete?.Invoke(FirstContactProbeValidationResult.Failed(
                    "Probe validation pipeline is not assigned."));
                yield break;
            }

            if (GamePipelineRunner.Instance == null)
            {
                onComplete?.Invoke(FirstContactProbeValidationResult.Failed("GamePipelineRunner is missing."));
                yield break;
            }

            var state = new PipelineState();
            state.SetImage(
                string.IsNullOrWhiteSpace(vlmSettings.imageStateKey) ? "reference_image" : vlmSettings.imageStateKey,
                _pendingTexture);
            state.SetString("probe_label", _pendingLabel ?? string.Empty);
            state.SetString("probe_display_label", _pendingDisplayLabel ?? _pendingLabel ?? string.Empty);
            state.SetString(
                FirstContactBestEffortTranslationChainLink.TranslationAvailableKey,
                _pendingTranslationAvailable ? "true" : "false");
            state.SetString(PromptPipelineConstants.SourceLocaleKey, L10n.CurrentLocale);

            bool done = false;
            PipelineState finalState = null;
            GamePipelineRunner.Instance.RunPipeline(vlmSettings.probeValidationPipeline, state, result =>
            {
                finalState = result;
                done = true;
            });
            yield return new WaitUntil(() => done);

            if (FirstContactProbeValidationResult.TryFromPipelineState(
                    finalState,
                    out FirstContactProbeValidationResult validation))
            {
                onComplete?.Invoke(validation);
                yield break;
            }

            onComplete?.Invoke(validation ?? FirstContactProbeValidationResult.Failed("Probe validation unstable."));
        }

        private IEnumerator EvaluateBootstrapCategoryFitRoutine(
            SemanticCardRecord card,
            BootstrapCategoryState category,
            Action<FirstContactBootstrapCategoryFitResult> onComplete)
        {
            if (category == null)
            {
                onComplete?.Invoke(FirstContactBootstrapCategoryFitResult.Accepted("No active category."));
                yield break;
            }

            FirstContactVlmSettings vlmSettings = GetVlmSettings();
            if (vlmSettings.bootstrapCategoryFitPipeline == null)
            {
                onComplete?.Invoke(FirstContactBootstrapCategoryFitResult.Failed(
                    "Bootstrap category fit pipeline is not assigned."));
                yield break;
            }

            if (GamePipelineRunner.Instance == null)
            {
                onComplete?.Invoke(FirstContactBootstrapCategoryFitResult.Failed(
                    "GamePipelineRunner is missing."));
                yield break;
            }

            var state = new PipelineState();
            state.SetString("category_id", category.Id);
            state.SetString("category_display_name", category.DisplayName);
            state.SetString("category_definition", category.DescriptorText);
            state.SetString("probe_label", card?.CanonicalLabel ?? _pendingLabel ?? string.Empty);
            state.SetString("probe_display_label", card?.LocalizedLabel ?? _pendingDisplayLabel ?? _pendingLabel ?? string.Empty);
            state.SetString(
                FirstContactBestEffortTranslationChainLink.TranslationAvailableKey,
                card?.TranslationAvailable == true || _pendingTranslationAvailable ? "true" : "false");
            state.SetString(PromptPipelineConstants.SourceLocaleKey, L10n.CurrentLocale);

            bool done = false;
            PipelineState finalState = null;
            GamePipelineRunner.Instance.RunPipeline(vlmSettings.bootstrapCategoryFitPipeline, state, result =>
            {
                finalState = result;
                done = true;
            });
            yield return new WaitUntil(() => done);

            if (FirstContactBootstrapCategoryFitResult.TryFromPipelineState(
                    finalState,
                    out FirstContactBootstrapCategoryFitResult fitResult))
            {
                onComplete?.Invoke(fitResult);
                yield break;
            }

            Debug.LogWarning(
                $"[FirstContactTranslationMode] Bootstrap category fit pipeline returned an unstable result. " +
                $"Error='{fitResult?.Error ?? string.Empty}'",
                this);
            onComplete?.Invoke(fitResult ?? FirstContactBootstrapCategoryFitResult.Failed(
                "Category fit pipeline unstable."));
        }

        private IEnumerator PreparePendingProbeLabelRoutine(Action<FirstContactProbeLabelResult> onComplete)
        {
            string displayLabel = string.IsNullOrWhiteSpace(_pendingDisplayLabel)
                ? _pendingLabel
                : _pendingDisplayLabel;
            FirstContactVlmSettings vlmSettings = GetVlmSettings();
            if (vlmSettings.probeLabelPipeline == null)
            {
                onComplete?.Invoke(FirstContactProbeLabelResult.Failed(
                    "Probe label pipeline is not assigned."));
                yield break;
            }

            if (GamePipelineRunner.Instance == null)
            {
                onComplete?.Invoke(FirstContactProbeLabelResult.Failed(
                    "GamePipelineRunner is missing."));
                yield break;
            }

            var state = new PipelineState();
            string normalizedDisplayLabel = NormalizePlayerLabelText(displayLabel);
            if (_pendingTexture == null)
            {
                onComplete?.Invoke(FirstContactProbeLabelResult.Failed(
                    "Drawing texture is unavailable for label analysis."));
                yield break;
            }

            state.SetString("probe_display_label", normalizedDisplayLabel);
            state.SetString("probe_display_label_json", JsonSerializer.Serialize(normalizedDisplayLabel));
            state.SetString("probe_label", normalizedDisplayLabel);
            state.SetString("canonical_label", normalizedDisplayLabel);
            state.SetString(
                FirstContactBestEffortTranslationChainLink.TranslationAvailableKey,
                "false");
            state.SetString(PromptPipelineConstants.SourceLocaleKey, L10n.CurrentLocale);
            state.SetString(PromptPipelineConstants.TargetLocaleKey, "en-US");
            state.SetString(PromptPipelineConstants.TargetLanguageKey, "English");
            state.SetString(PromptPipelineConstants.TargetLanguageNativeNameKey, "English");

            bool done = false;
            PipelineState finalState = null;
            GamePipelineRunner.Instance.RunPipeline(vlmSettings.probeLabelPipeline, state, result =>
            {
                finalState = result;
                done = true;
            });
            yield return new WaitUntil(() => done);

            if (!FirstContactProbeLabelResult.TryFromPipelineState(
                    finalState,
                    out FirstContactProbeLabelResult labelResult))
            {
                Debug.LogWarning(
                    "[FirstContactTranslationMode] Probe label pipeline failed. " +
                    $"DisplayLabel='{displayLabel}' Error='{labelResult?.Error}'",
                    this);
                onComplete?.Invoke(labelResult ?? FirstContactProbeLabelResult.Failed(
                    "Probe label pipeline failed."));
                yield break;
            }

            string canonicalLabel = NormalizeProbeLabel(labelResult.CanonicalLabel);
            if (string.IsNullOrWhiteSpace(canonicalLabel))
            {
                Debug.LogWarning(
                    "[FirstContactTranslationMode] Probe label pipeline returned an empty canonical label. " +
                    $"DisplayLabel='{displayLabel}'",
                    this);
                onComplete?.Invoke(FirstContactProbeLabelResult.Failed(
                    "Canonical label is empty."));
                yield break;
            }

            labelResult.CanonicalLabel = canonicalLabel;
            _pendingLabel = canonicalLabel;
            _pendingTranslationAvailable = labelResult.TranslationAvailable;
            onComplete?.Invoke(labelResult);
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
            _context?.Drawing?.SetInteractionLocked(true);
            _context?.Drawing?.ClearInstructionLabel();
            ChangeState(_pendingCardSource switch
            {
                FirstContactCardSource.Answer => FirstContactModeState.TransmittingAnswer,
                FirstContactCardSource.BootstrapProbe => FirstContactModeState.StoringBootstrapProbe,
                _ => FirstContactModeState.UpdatingTranslation
            });
            _context?.Camera?.SetMode(CameraMode.TerminalZoom);

            EmbeddingResult embedding = default;
            string authoritativeLabel = string.IsNullOrWhiteSpace(_pendingDisplayLabel)
                ? _pendingLabel
                : _pendingDisplayLabel;
            yield return _embeddingService.EmbedLabel(authoritativeLabel, result => embedding = result);
            if (!embedding.IsValid &&
                (_pendingCardSource == FirstContactCardSource.BootstrapProbe ||
                 GetSemanticSettings().failCardWhenEmbeddingMissing))
            {
                ShowTechnicalFailurePrompt(
                    FirstContactTechnicalRetryAction.ConfirmDrawing,
                    "signal_encoding_failed",
                    "SIGNAL ENCODING FAILED",
                    $"Embedding failed: {embedding.Error}");
                _routine = null;
                yield break;
            }

            var card = new SemanticCardRecord
            {
                Texture = _pendingTexture,
                PngBytes = _pendingPngBytes,
                Label = authoritativeLabel,
                CanonicalLabel = _pendingLabel,
                LocalizedLabel = _pendingDisplayLabel,
                TranslationAvailable = _pendingTranslationAvailable,
                Embedding = embedding.Vector,
                Source = _pendingCardSource,
                TargetUnknownId = _pendingCardSource == FirstContactCardSource.DecodeSample ? _activeUnknownId : string.Empty,
                BootstrapCategoryId = GetActiveBootstrapCategory()?.Id ?? string.Empty,
                BootstrapCategoryDisplayName = GetActiveBootstrapCategory()?.DisplayName ?? string.Empty,
                QuestionId = _pendingCardSource == FirstContactCardSource.BootstrapProbe
                    ? (GetActiveBootstrapCategory()?.Id ?? "bootstrap-probe")
                    : (_session?.CurrentQuestion?.Id ?? string.Empty),
                TurnIndex = _session?.TurnIndex ?? 0
            };

            _semanticMemory.TryCreateWaveformProfile(card, _runtimeWaveformSessionSeed, out var waveform);
            card.WaveformProfile = waveform;
            if (_pendingCardSource == FirstContactCardSource.BootstrapProbe &&
                TryFindBootstrapDuplicateCard(card, out SemanticCardRecord duplicateCard))
            {
                card.Id = duplicateCard.Id;
                card.ClusterId = duplicateCard.ClusterId;
                card.BootstrapCategoryEvaluated = true;
                card.BootstrapCategoryAccepted = false;
                card.BootstrapCategoryDuplicate = true;
                card.DuplicateOfCardId = duplicateCard.Id;
                yield return StoreBootstrapDuplicateProbeCardRoutine(card, duplicateCard);
                _routine = null;
                yield break;
            }

            FirstContactBootstrapCategoryFitResult categoryFitResult = null;
            if (_pendingCardSource == FirstContactCardSource.BootstrapProbe)
            {
                yield return EvaluateBootstrapCategoryFitRoutine(
                    card,
                    GetActiveBootstrapCategory(),
                    result => categoryFitResult = result);
                if (categoryFitResult == null || !categoryFitResult.IsSuccess)
                {
                    ShowTechnicalFailurePrompt(
                        FirstContactTechnicalRetryAction.ConfirmDrawing,
                        "category_analysis_failed",
                        "CATEGORY ANALYSIS FAILED",
                        $"Bootstrap category analysis failed: {categoryFitResult?.Error ?? "No result."}");
                    _routine = null;
                    yield break;
                }
            }

            List<SemanticClusterTransitionState> beforeClusterStates = CaptureSemanticClusterStates();
            FirstContactSemanticMapSnapshot beforeMapSnapshot = null;
            if (_pendingCardSource == FirstContactCardSource.DecodeSample &&
                GetSemanticSettings().showSemanticMapFeedback)
            {
                beforeMapSnapshot = BuildSemanticMapSnapshot(null, _activeUnknownId);
            }

            // Every valid probe contributes to the general semantic map. A bootstrap
            // category rejection only prevents the card from advancing that category's
            // TRACE; it does not discard the observed meaning or its possible cluster.
            _semanticMemory.AddCard(card);
            _session?.RecentCards.Add(card);
            SemanticClusterRecord cluster = _semanticMemory.FindCluster(card.ClusterId);
            FirstContactClusterFormationEvent clusterFormation = BuildClusterFormationEvent(
                card,
                cluster,
                beforeClusterStates,
                _semanticMemory?.LastFormationEdges);

            if (_pendingCardSource == FirstContactCardSource.BootstrapProbe)
            {
                yield return StoreBootstrapProbeCardRoutine(
                    card,
                    cluster,
                    clusterFormation,
                    categoryFitResult);
            }
            else if (_pendingCardSource == FirstContactCardSource.DecodeSample)
            {
                yield return ResolveDecodeCardRoutine(
                    card,
                    cluster,
                    beforeMapSnapshot,
                    clusterFormation);
            }
            else
            {
                yield return TransmitAnswerCardRoutine(card);
            }

            _routine = null;
        }

        private IEnumerator StoreBootstrapDuplicateProbeCardRoutine(
            SemanticCardRecord card,
            SemanticCardRecord duplicateCard)
        {
            BootstrapCategoryState category = GetActiveBootstrapCategory();
            if (category == null)
            {
                yield break;
            }

            if (!_hasShownBootstrapDuplicateOfficerLine)
            {
                _hasShownBootstrapDuplicateOfficerLine = true;
                ShowOfficerLine("first_contact.officer.bootstrap_duplicate_probe");
            }

            int traceCount = category.TraceCount;
            bool stable = category.IsStable;
            FirstContactSemanticMapSnapshot mapSnapshot = BuildBootstrapSemanticMapSnapshot(
                card,
                category,
                includeActiveCard: true);
            _terminalPresenter?.ShowBootstrapSignalCapture(
                card,
                category.DisplayName,
                traceCount,
                traceCount,
                category.RequiredTraceCount,
                mapSnapshot,
                mapSnapshot,
                GetSemanticSettings(),
                accepted: false,
                becameStable: false,
                stable,
                duplicate: true,
                instant: false);
            yield return WaitForTerminalContinueRoutine();

            yield return StartBootstrapProbeSequenceRoutine();
        }

        private IEnumerator StoreBootstrapProbeCardRoutine(
            SemanticCardRecord card,
            SemanticClusterRecord cluster,
            FirstContactClusterFormationEvent clusterFormation,
            FirstContactBootstrapCategoryFitResult categoryFitResult)
        {
            BootstrapCategoryState category = GetActiveBootstrapCategory();
            if (category == null)
            {
                yield break;
            }

            BootstrapProbeFit fit = category.EvaluateCandidate(card, _embeddingService);
            int previousTraceCount = category.TraceCount;
            bool wasStable = category.IsStable;
            FirstContactSemanticMapSnapshot beforeMapSnapshot = BuildBootstrapSemanticMapSnapshot(
                card,
                category,
                includeActiveCard: false);
            bool categoryAccepted = categoryFitResult != null && categoryFitResult.FitsCategory;
            bool accepted = category.TryAcceptCard(
                card,
                _embeddingService,
                GetSemanticSettings(),
                fit,
                categoryAccepted);
            RefreshSessionStableClusters();
            bool stable = category.IsStable;
            if (GetDebugSettings().logSimilarityScores)
            {
                Debug.Log(
                    $"[FirstContactTranslationMode] Bootstrap category={category.Id} " +
                    $"traces={category.TraceCount}/{category.RequiredTraceCount} " +
                    $"accepted={accepted} categoryFit={fit.CategoryDescriptorFit:0.000} " +
                    $"categoryJudge={categoryAccepted} " +
                    $"evidence='{categoryFitResult?.EvidenceType ?? string.Empty}' " +
                    $"reason='{categoryFitResult?.Reason ?? string.Empty}' " +
                    $"stable={stable}");
            }

            if (!accepted)
            {
                ResolveCategoryRejectionFeedback(
                    categoryFitResult,
                    out string categoryOfficerLineKey);
                ShowOfficerLine(categoryOfficerLineKey);
            }

            FirstContactSemanticMapSnapshot mapSnapshot = BuildBootstrapSemanticMapSnapshot(
                card,
                category,
                includeActiveCard: true);
            FirstContactClusterFormationEvent semanticFormation = ShouldShowBootstrapSemanticFormation(
                category,
                cluster,
                clusterFormation)
                ? clusterFormation
                : default;
            _terminalPresenter?.ShowBootstrapSignalCapture(
                card,
                category.DisplayName,
                previousTraceCount,
                category.TraceCount,
                category.RequiredTraceCount,
                beforeMapSnapshot,
                mapSnapshot,
                GetSemanticSettings(),
                accepted,
                stable && !wasStable,
                stable,
                semanticFormation,
                instant: false);
            yield return WaitForTerminalContinueRoutine();

            if (!stable)
            {
                yield return StartBootstrapProbeSequenceRoutine();
                yield break;
            }

            _terminalPresenter?.ShowBootstrapClusterTrace(
                category.DisplayName,
                category.TraceCount,
                category.RequiredTraceCount,
                category.Meaning,
                mapSnapshot,
                GetSemanticSettings(),
                instant: false);
            yield return WaitForTerminalContinueRoutine();

            _bootstrapCategoryIndex++;
            yield return StartBootstrapProbeSequenceRoutine();
        }

        private static void ResolveCategoryRejectionFeedback(
            FirstContactBootstrapCategoryFitResult result,
            out string officerLineKey)
        {
            string evidenceType = result?.EvidenceType ?? string.Empty;
            switch (evidenceType)
            {
                case FirstContactBootstrapCategoryFitResult.SymbolicOrContextualEvidence:
                    officerLineKey = "first_contact.officer.bootstrap_category_contextual";
                    break;
                case FirstContactBootstrapCategoryFitResult.NeutralOrGenericEvidence:
                    officerLineKey = "first_contact.officer.bootstrap_category_generic";
                    break;
                case FirstContactBootstrapCategoryFitResult.UncertainEvidence:
                    officerLineKey = "first_contact.officer.bootstrap_category_uncertain";
                    break;
                default:
                    officerLineKey = "first_contact.officer.bootstrap_category_mismatch";
                    break;
            }
        }

        private bool TryFindBootstrapDuplicateCard(
            SemanticCardRecord card,
            out SemanticCardRecord duplicateCard)
        {
            return FirstContactProbeDuplicateDetector.TryFindDuplicate(
                card,
                _session?.RecentCards,
                _embeddingService,
                GetSemanticSettings(),
                out duplicateCard);
        }

        private IEnumerator ResolveDecodeCardRoutine(
            SemanticCardRecord card,
            SemanticClusterRecord cluster,
            FirstContactSemanticMapSnapshot beforeMapSnapshot,
            FirstContactClusterFormationEvent clusterFormation)
        {
            UnknownSlot slot = _session?.CurrentQuestion?.FindUnknown(_activeUnknownId);
            FirstContactResolutionResult result = _unknownResolver.EvaluateCard(card, slot);
            IReadOnlyList<FirstContactSlotScore> slotScores = BuildSlotScores(card, _activeUnknownId);
            FirstContactSemanticMapSnapshot mapSnapshot = BuildSemanticMapSnapshot(card, _activeUnknownId);
            BrainwaveSemanticProfile unknownSignalProfile = BuildUnknownSignalProfile(slot, card);
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
                    GetSemanticSettings(),
                    unknownSignalProfile,
                    beforeMapSnapshot,
                    clusterFormation);
                yield return WaitForTerminalContinueRoutine();
            }

            RefreshSessionStableClusters();
            _unknownResolver.ApplyAutomaticClusterHints(_session.CurrentQuestion, _semanticMemory);
            _incomingTransmissionChoicesActive = false;
            ChangeState(FirstContactModeState.InspectingQuestion);
            _context?.Camera?.SetMode(CameraMode.TerminalZoom);
            EnableTerminalChoices(_session.CurrentQuestion);
        }

        private IEnumerator TransmitAnswerCardRoutine(SemanticCardRecord card)
        {
            _session.PreviousAnswer = card;
            _context?.SharedMonitorDisplay?.ShowSubmission(card.Texture);
            _terminalPresenter?.ShowAnswerTransmitted(card, instant: false);
            ShowOfficerLine(_session?.CurrentQuestion?.DialogueKeys?.answerSent);
            FirstContactPresentationSettings presentation = GetPresentationSettings();
            yield return WaitForTerminalPresentationRoutine(0f, true);
            if (presentation.answerTransmitHoldSeconds > 0f)
            {
                yield return new WaitForSeconds(presentation.answerTransmitHoldSeconds);
            }

            if (HasConsumedFallbackQuestions())
            {
                ChangeState(FirstContactModeState.Completed);
                yield break;
            }

            if (presentation.nextQuestionDelay > 0f)
            {
                yield return new WaitForSeconds(presentation.nextQuestionDelay);
            }

            yield return LoadNextQuestionRoutine();
        }

        private BrainwaveSemanticProfile BuildUnknownSignalProfile(UnknownSlot slot, SemanticCardRecord card)
        {
            if (slot?.TargetEmbedding == null ||
                !slot.TargetEmbedding.IsValid ||
                _semanticMemory == null)
            {
                return BrainwaveSemanticProfile.Invalid;
            }

            return _semanticMemory.TryCreateWaveformProfile(
                slot.TargetEmbedding.Vector,
                slot.Id,
                card != null ? Mathf.Max(1, card.TurnIndex + 1) : Mathf.Max(1, _session?.TurnIndex + 1 ?? 1),
                _runtimeWaveformSessionSeed,
                out BrainwaveSemanticProfile profile)
                ? profile
                : BrainwaveSemanticProfile.Invalid;
        }

        private int BuildIncomingStreamSeed(AlienQuestion question)
        {
            unchecked
            {
                int hash = _runtimeWaveformSessionSeed == 0 ? 17 : _runtimeWaveformSessionSeed;
                string id = question?.Id ?? string.Empty;
                for (int i = 0; i < id.Length; i++)
                {
                    hash = (hash * 31) + char.ToLowerInvariant(id[i]);
                }

                return hash == 0 ? 1 : hash;
            }
        }

        private BrainwaveSemanticProfile BuildAlienTokenSignalProfile(string token, int tokenIndex)
        {
            if (_semanticMemory == null || string.IsNullOrWhiteSpace(token))
            {
                return BrainwaveSemanticProfile.Invalid;
            }

            return _semanticMemory.TryCreateTokenWaveformProfile(
                token,
                Mathf.Max(1, ((_session?.TurnIndex ?? 0) * 31) + tokenIndex + 1),
                _runtimeWaveformSessionSeed,
                out BrainwaveSemanticProfile profile)
                ? profile
                : BrainwaveSemanticProfile.Invalid;
        }

        private void RedrawPending()
        {
            ClearTechnicalFailureState();
            HideOfficerLine();
            StopActiveRoutine();
            DisableTerminalChoices();
            EndTerminalProbeLabelInput();
            _terminalProbeLabelStatus = string.Empty;
            _context?.Drawing?.ClearRecognitionLabel();
            _context?.Drawing?.ClearInstructionLabel();
            _routine = StartCoroutine(RedrawPendingRoutine());
        }

        private string GetPendingProbeDispatchCategory()
        {
            return _pendingCardSource == FirstContactCardSource.BootstrapProbe
                ? GetActiveBootstrapCategory()?.DisplayName ?? string.Empty
                : string.Empty;
        }

        private BrainwaveSemanticProfile BuildProbeDispatchSignalProfile()
        {
            if (_semanticMemory == null)
            {
                return BrainwaveSemanticProfile.Invalid;
            }

            string label = !string.IsNullOrWhiteSpace(_pendingDisplayLabel)
                ? _pendingDisplayLabel
                : _pendingLabel;
            if (string.IsNullOrWhiteSpace(label))
            {
                return BrainwaveSemanticProfile.Invalid;
            }

            return _semanticMemory.TryCreateTokenWaveformProfile(
                label,
                Mathf.Max(1, ((_session?.TurnIndex ?? 0) * 37) + 1),
                _runtimeWaveformSessionSeed,
                out BrainwaveSemanticProfile profile)
                ? profile
                : BrainwaveSemanticProfile.Invalid;
        }

        private int BuildProbeDispatchStreamSeed()
        {
            unchecked
            {
                int hash = _runtimeWaveformSessionSeed == 0 ? 23 : _runtimeWaveformSessionSeed;
                hash = (hash * 31) + (int)_pendingCardSource;
                string label = _pendingDisplayLabel ?? _pendingLabel ?? string.Empty;
                for (int i = 0; i < label.Length; i++)
                {
                    hash = (hash * 31) + char.ToLowerInvariant(label[i]);
                }

                return hash == 0 ? 1 : hash;
            }
        }

        private IEnumerator RedrawPendingRoutine()
        {
            _context?.Drawing?.SetInteractionLocked(true);
            _context?.Drawing?.ClearInstructionLabel();
            _context?.Camera?.SetMode(CameraMode.TerminalZoom);
            if (_pendingCardSource == FirstContactCardSource.BootstrapProbe)
            {
                BootstrapCategoryState category = GetActiveBootstrapCategory();
                _terminalPresenter?.ShowBootstrapProbeChannelOpen(
                    category?.DisplayName ?? string.Empty,
                    category?.TraceCount ?? 0,
                    category?.RequiredTraceCount ?? BootstrapRequiredTraceCount,
                    instant: false);
            }
            else
            {
                _terminalPresenter?.ShowTabletLinkOpen(
                    _session?.CurrentQuestion,
                    _pendingCardSource,
                    _activeUnknownId,
                    instant: false);
            }

            float holdSeconds = GetPresentationSettings().tabletLinkOpenHoldSeconds;
            if (holdSeconds > 0f)
            {
                yield return new WaitForSeconds(holdSeconds);
            }

            yield return BeginDrawingRoutine(_pendingCardSource, _activeUnknownId, preserveCanvas: true);
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
            ShowActiveQuestionChoices(instant: false);
        }

        private void EnableBootstrapProbeChoice()
        {
            _selectedTerminalChoiceIndex = 0;
            _terminalChoiceInputEnabled = true;
            _terminalChoiceMode = FirstContactTerminalChoiceMode.BootstrapProbe;
        }

        private void ShowActiveQuestionChoices(bool instant)
        {
            AlienQuestion question = _session?.CurrentQuestion;
            if (question == null)
            {
                return;
            }

            string initialProbeUnknownId = GetInitialProbeUnknownId(question);
            if (_incomingTransmissionChoicesActive && !string.IsNullOrWhiteSpace(initialProbeUnknownId))
            {
                _terminalPresenter?.ShowIncomingTransmissionChoices(
                    question,
                    _selectedTerminalChoiceIndex,
                    initialProbeUnknownId,
                    instant);
                return;
            }

            _terminalPresenter?.ShowQuestionChoices(
                question,
                _selectedTerminalChoiceIndex,
                BuildSemanticMapSnapshot(GetMostRecentCard(), string.Empty),
                GetSemanticSettings(),
                instant,
                _currentFallbackReason,
                initialProbeUnknownId);
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
                GetSemanticSettings(),
                instant: false);
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
                _selectedTerminalChoiceIndex,
                instant: false);
        }

        private void EnableRejectedInputChoice(string reason)
        {
            _selectedTerminalChoiceIndex = 0;
            _terminalChoiceInputEnabled = true;
            _terminalChoiceMode = FirstContactTerminalChoiceMode.RejectedInput;
            _terminalPresenter?.ShowInputRejected(reason, _selectedTerminalChoiceIndex, instant: false);
        }

        private void EnableTechnicalRetryChoice()
        {
            _selectedTerminalChoiceIndex = 0;
            _terminalChoiceInputEnabled = true;
            _terminalChoiceMode = FirstContactTerminalChoiceMode.TechnicalRetry;
            _terminalPresenter?.ShowAnalysisError(
                _technicalFailureStatus,
                _selectedTerminalChoiceIndex,
                instant: false);
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

        private void BeginTerminalProbeLabelInput()
        {
            DisableTerminalChoices();
            _terminalProbeLabelInputActive = true;
            _terminalProbeLabelStatus = string.Empty;
            EnableImeComposition();
            RefreshTerminalProbeLabelEntry(instant: false);
        }

        private void RefreshTerminalProbeLabelEntry(bool instant)
        {
            _terminalPresenter?.ShowProbeLabelEntry(
                _pendingCardSource,
                _activeUnknownId,
                _pendingTexture,
                GetRenderedProbeLabelInput(),
                _terminalProbeLabelStatus,
                instant);

            if (_terminalProbeLabelInputActive && !instant && UsesHiddenTerminalTextInput())
            {
                BeginTerminalTextInput();
            }
        }

        private bool HandleTerminalProbeLabelInput()
        {
            if (!_terminalProbeLabelInputActive || _modeState != FirstContactModeState.ReviewingLabel)
            {
                return false;
            }

            if (WasKeyPressed(KeyCode.Escape))
            {
                _terminalProbeLabelStatus = string.Empty;
                RedrawPending();
                return true;
            }

            TerminalDisplay terminal = _context?.TerminalDisplay;
            if (terminal != null && terminal.IsTextInputActive)
            {
                if (terminal.IsTyping())
                {
                    return true;
                }

                if (SyncTerminalTextInputState(terminal))
                {
                    return true;
                }

                terminal.FocusTextInput();
                return true;
            }

            if (terminal != null && terminal.IsTyping())
            {
                QueueLegacyTerminalProbeLabelTextInput();
                return true;
            }

            bool changed = false;
            bool consumedBackspace = false;
            string textInput = ConsumeTerminalProbeLabelTextInput();
            for (int i = 0; i < textInput.Length; i++)
            {
                char character = textInput[i];
                if (character == '\b')
                {
                    consumedBackspace = true;
                    changed |= RemoveLastProbeLabelCharacter();
                    continue;
                }

                if (character == '\n' || character == '\r' || character == '\t' || char.IsControl(character))
                {
                    continue;
                }

                if ((_currentProbeLabelInput?.Length ?? 0) >= MaxProbeLabelLength)
                {
                    continue;
                }

                _currentProbeLabelInput = (_currentProbeLabelInput ?? string.Empty) + character;
                changed = true;
            }

            if (!consumedBackspace && WasKeyPressed(KeyCode.Backspace))
            {
                changed |= RemoveLastProbeLabelCharacter();
            }

            string composition = GetCurrentImeCompositionString();
            if (!string.Equals(_currentProbeLabelComposition, composition, StringComparison.Ordinal))
            {
                _currentProbeLabelComposition = composition;
                changed = true;
            }

            if (changed)
            {
                HideOfficerLine();
                if (!string.IsNullOrEmpty(_terminalProbeLabelStatus))
                {
                    _terminalProbeLabelStatus = string.Empty;
                }

                RefreshTerminalProbeLabelEntry(instant: true);
            }

            return true;
        }

        private void BeginTerminalTextInput()
        {
            TerminalDisplay terminal = _context?.TerminalDisplay;
            if (terminal == null)
            {
                return;
            }

            EnableImeComposition();
            terminal.BeginTextInput(
                FirstContactTerminalPresenter.ProbeLabelInputPrefix,
                _currentProbeLabelInput,
                MaxProbeLabelLength,
                SubmitProbeLabel,
                OnTerminalProbeLabelChanged,
                visible: false);
        }

        private static bool UsesHiddenTerminalTextInput()
        {
            return true;
        }

        private void OnTerminalProbeLabelChanged(string value)
        {
            HideOfficerLine();
            if (_context?.TerminalDisplay != null && SyncTerminalTextInputState(_context.TerminalDisplay))
            {
                return;
            }

            _currentProbeLabelInput = value ?? string.Empty;
            _currentProbeLabelComposition = string.Empty;
            if (!string.IsNullOrEmpty(_terminalProbeLabelStatus))
            {
                _terminalProbeLabelStatus = string.Empty;
            }
            RefreshTerminalProbeLabelEntry(instant: true);
        }

        private bool SyncTerminalTextInputState(TerminalDisplay terminal)
        {
            if (terminal == null)
            {
                return false;
            }

            string value = terminal.TextInputValue ?? string.Empty;
            string displayValue = terminal.TextInputDisplayValue ?? value;
            SplitCommittedAndDisplayProbeLabel(value, displayValue, out string committed, out string composition);
            bool changed =
                !string.Equals(_currentProbeLabelInput, committed, StringComparison.Ordinal) ||
                !string.Equals(_currentProbeLabelComposition, composition, StringComparison.Ordinal);
            if (!changed)
            {
                return false;
            }

            HideOfficerLine();
            _currentProbeLabelInput = committed;
            _currentProbeLabelComposition = composition;
            if (!string.IsNullOrEmpty(_terminalProbeLabelStatus))
            {
                _terminalProbeLabelStatus = string.Empty;
            }

            RefreshTerminalProbeLabelEntry(instant: true);
            return true;
        }

        private static void SplitCommittedAndDisplayProbeLabel(
            string committedValue,
            string displayValue,
            out string committed,
            out string composition)
        {
            committed = committedValue ?? string.Empty;
            string display = displayValue ?? committed;
            if (display.StartsWith(committed, StringComparison.Ordinal))
            {
                composition = display.Substring(committed.Length);
                return;
            }

            committed = display;
            composition = string.Empty;
        }

        private string ConsumeTerminalProbeLabelTextInput()
        {
            string textInput = string.Empty;
#if ENABLE_INPUT_SYSTEM
            SubscribeTerminalProbeLabelTextInput();
            textInput = _queuedTerminalProbeLabelText ?? string.Empty;
            _queuedTerminalProbeLabelText = string.Empty;
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
            if (string.IsNullOrEmpty(textInput))
            {
                textInput = Input.inputString ?? string.Empty;
            }
#endif

            return textInput;
        }

        private void QueueLegacyTerminalProbeLabelTextInput()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
#if ENABLE_INPUT_SYSTEM
            if (_lastTerminalProbeLabelTextInputFrame == Time.frameCount)
            {
                return;
            }
#endif

            string textInput = Input.inputString ?? string.Empty;
            if (!string.IsNullOrEmpty(textInput))
            {
                _queuedTerminalProbeLabelText += textInput;
                _lastTerminalProbeLabelTextInputFrame = Time.frameCount;
            }
#endif
        }

        private bool RemoveLastProbeLabelCharacter()
        {
            if (string.IsNullOrEmpty(_currentProbeLabelInput))
            {
                return false;
            }

            _currentProbeLabelInput = _currentProbeLabelInput.Substring(0, _currentProbeLabelInput.Length - 1);
            return true;
        }

        private string GetRenderedProbeLabelInput()
        {
            return (_currentProbeLabelInput ?? string.Empty) + (_currentProbeLabelComposition ?? string.Empty);
        }

        private string GetCurrentImeCompositionString()
        {
#if ENABLE_INPUT_SYSTEM
            return _inputSystemProbeLabelComposition ?? string.Empty;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.compositionString ?? string.Empty;
#else
            return string.Empty;
#endif
        }

        private void EnableImeComposition()
        {
#if ENABLE_INPUT_SYSTEM
            SubscribeTerminalProbeLabelTextInput();
            UnityEngine.InputSystem.Keyboard.current?.SetIMEEnabled(true);
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            if (!_hasPreviousImeCompositionMode)
            {
                _previousImeCompositionMode = Input.imeCompositionMode;
                _hasPreviousImeCompositionMode = true;
            }

            Input.imeCompositionMode = IMECompositionMode.On;
#endif
        }

        private void RestoreImeComposition()
        {
#if ENABLE_INPUT_SYSTEM
            UnityEngine.InputSystem.Keyboard.current?.SetIMEEnabled(false);
            _inputSystemProbeLabelComposition = string.Empty;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            if (!_hasPreviousImeCompositionMode)
            {
                return;
            }

            Input.imeCompositionMode = _previousImeCompositionMode;
            _hasPreviousImeCompositionMode = false;
#endif
        }

        private void EndTerminalProbeLabelInput()
        {
            _terminalProbeLabelInputActive = false;
            _queuedTerminalProbeLabelText = string.Empty;
            _currentProbeLabelComposition = string.Empty;
            RestoreImeComposition();
            _context?.TerminalDisplay?.HideTextInput();
        }

        private void HandleTerminalChoiceInput()
        {
            if (!_terminalChoiceInputEnabled)
            {
                return;
            }

            if (_context?.TerminalDisplay != null && _context.TerminalDisplay.IsTyping())
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

            if (WasSubmitPressedThisFrame())
            {
                SelectTerminalChoice(_selectedTerminalChoiceIndex);
                return;
            }
        }

        private void HandleDrawingSubmitInput()
        {
            if (_modeState != FirstContactModeState.DrawingDecodeSample &&
                _modeState != FirstContactModeState.DrawingAnswer &&
                _modeState != FirstContactModeState.DrawingBootstrapProbe)
            {
                return;
            }

            if (WasSubmitPressedThisFrame())
            {
                TrySubmitDrawingFromInput();
            }
        }

        private bool TrySubmitDrawingFromInput()
        {
            if (_modeState != FirstContactModeState.DrawingDecodeSample &&
                _modeState != FirstContactModeState.DrawingAnswer &&
                _modeState != FirstContactModeState.DrawingBootstrapProbe)
            {
                return false;
            }

            if (_lastSubmitInputFrame == Time.frameCount)
            {
                return true;
            }

            _lastSubmitInputFrame = Time.frameCount;
            SubmitDrawing();
            return true;
        }

        private static bool WasSubmitPressedThisFrame()
        {
            bool pressed = false;
#if ENABLE_INPUT_SYSTEM
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null)
            {
                pressed |= keyboard.enterKey != null && keyboard.enterKey.wasPressedThisFrame;
                pressed |= keyboard.numpadEnterKey != null && keyboard.numpadEnterKey.wasPressedThisFrame;
                pressed |= keyboard[UnityEngine.InputSystem.Key.Enter].wasPressedThisFrame;
                pressed |= keyboard[UnityEngine.InputSystem.Key.NumpadEnter].wasPressedThisFrame;
            }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
            pressed |= Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
#endif
            return pressed;
        }

        private static bool WasKeyPressed(KeyCode keyCode)
        {
            bool pressed = false;
#if ENABLE_INPUT_SYSTEM
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null)
            {
                var keyControl = GetInputSystemKeyControl(keyboard, keyCode);
                pressed |= keyControl != null && keyControl.wasPressedThisFrame;
            }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
            pressed |= keyCode != KeyCode.None && Input.GetKeyDown(keyCode);
#endif
            return pressed;
        }

#if ENABLE_INPUT_SYSTEM
        private void SubscribeTerminalProbeLabelTextInput()
        {
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (_subscribedTextInputKeyboard == keyboard)
            {
                return;
            }

            UnsubscribeTerminalProbeLabelTextInput();
            if (keyboard == null)
            {
                return;
            }

            _subscribedTextInputKeyboard = keyboard;
            _subscribedTextInputKeyboard.onTextInput += HandleTerminalProbeLabelTextInput;
            _subscribedTextInputKeyboard.onIMECompositionChange += HandleTerminalProbeLabelImeCompositionChanged;
        }

        private void UnsubscribeTerminalProbeLabelTextInput()
        {
            if (_subscribedTextInputKeyboard == null)
            {
                return;
            }

            _subscribedTextInputKeyboard.onTextInput -= HandleTerminalProbeLabelTextInput;
            _subscribedTextInputKeyboard.onIMECompositionChange -= HandleTerminalProbeLabelImeCompositionChanged;
            _subscribedTextInputKeyboard = null;
        }

        private void HandleTerminalProbeLabelTextInput(char character)
        {
            if (!_terminalProbeLabelInputActive || _modeState != FirstContactModeState.ReviewingLabel)
            {
                return;
            }

            _queuedTerminalProbeLabelText += character;
            _inputSystemProbeLabelComposition = string.Empty;
            _lastTerminalProbeLabelTextInputFrame = Time.frameCount;
        }

        private void HandleTerminalProbeLabelImeCompositionChanged(
            UnityEngine.InputSystem.LowLevel.IMECompositionString composition)
        {
            if (!_terminalProbeLabelInputActive || _modeState != FirstContactModeState.ReviewingLabel)
            {
                return;
            }

            _inputSystemProbeLabelComposition = composition.Count == 0
                ? string.Empty
                : composition.ToString();
        }
#endif

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
                KeyCode.Space => keyboard.spaceKey,
                KeyCode.Backspace => keyboard.backspaceKey,
                KeyCode.Escape => keyboard.escapeKey,
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
                FirstContactTerminalChoiceMode.TechnicalRetry => 2,
                FirstContactTerminalChoiceMode.BootstrapProbe => 1,
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
                HasTranslationCardsInSession())
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

        private bool HasTranslationCardsInSession()
        {
            if (_session?.RecentCards == null)
            {
                return false;
            }

            for (int i = 0; i < _session.RecentCards.Count; i++)
            {
                SemanticCardRecord card = _session.RecentCards[i];
                if (card != null)
                {
                    return true;
                }
            }

            return false;
        }

        private void SelectTerminalChoice(int choiceIndex)
        {
            HideOfficerLine();
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
                case FirstContactTerminalChoiceMode.TechnicalRetry:
                    SelectTechnicalRetryChoice(choiceIndex);
                    break;
                case FirstContactTerminalChoiceMode.BootstrapProbe:
                    SelectBootstrapProbeChoice(choiceIndex);
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

        private void SelectTechnicalRetryChoice(int choiceIndex)
        {
            if (_modeState != FirstContactModeState.ReviewingTechnicalFailure ||
                choiceIndex < 0 || choiceIndex > 1)
            {
                return;
            }

            if (choiceIndex == 0)
            {
                ClearTechnicalFailureState();
                RedrawPending();
                return;
            }

            FirstContactTechnicalRetryAction retryAction = _technicalRetryAction;
            ClearTechnicalFailureState();
            DisableTerminalChoices();
            StopActiveRoutine();
            _routine = retryAction switch
            {
                FirstContactTechnicalRetryAction.CaptureDrawing => StartCoroutine(OpenProbeLabelEntryRoutine()),
                FirstContactTechnicalRetryAction.ConfirmDrawing => StartCoroutine(ConfirmPendingDrawingRoutine()),
                _ => StartCoroutine(AnalyzeDrawingRoutine())
            };
        }

        private void SelectBootstrapProbeChoice(int choiceIndex)
        {
            if (_modeState != FirstContactModeState.BootstrapProbeSequence || choiceIndex != 0)
            {
                return;
            }

            DisableTerminalChoices();
            StopActiveRoutine();
            _routine = StartCoroutine(ConfirmTerminalChoiceRoutine(
                FirstContactCardSource.BootstrapProbe,
                string.Empty));
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
                    ShowActiveQuestionChoices(instant: true);
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
                case FirstContactTerminalChoiceMode.TechnicalRetry:
                    _terminalPresenter?.ShowAnalysisError(
                        _technicalFailureStatus,
                        _selectedTerminalChoiceIndex);
                    break;
                case FirstContactTerminalChoiceMode.BootstrapProbe:
                    {
                        BootstrapCategoryState category = GetActiveBootstrapCategory();
                        if (category != null)
                        {
                            _terminalPresenter?.ShowBootstrapProbeSequence(
                                category.DisplayName,
                                category.TraceCount,
                                category.RequiredTraceCount,
                                category.IsStable,
                                _selectedTerminalChoiceIndex);
                        }
                    }
                    break;
                case FirstContactTerminalChoiceMode.Continue:
                    break;
            }
        }

        private IEnumerator ConfirmTerminalChoiceRoutine(
            FirstContactCardSource source,
            string unknownId)
        {
            _incomingTransmissionChoicesActive = false;
            if (source == FirstContactCardSource.BootstrapProbe)
            {
                BootstrapCategoryState category = GetActiveBootstrapCategory();
                _terminalPresenter?.ShowBootstrapProbeChannelOpen(
                    category?.DisplayName ?? string.Empty,
                    category?.TraceCount ?? 0,
                    category?.RequiredTraceCount ?? BootstrapRequiredTraceCount,
                    instant: false);
            }
            else
            {
                _terminalPresenter?.ShowProbeChannelOpen(source, unknownId, instant: false);
            }
            float linkHoldSeconds = GetPresentationSettings().tabletLinkOpenHoldSeconds;
            yield return WaitForTerminalPresentationRoutine(linkHoldSeconds, true);

            yield return BeginDrawingRoutine(source, unknownId);
            _routine = null;
        }

        private void ShowContentRedrawPrompt(string prompt, string officerLineKey = null)
        {
            string safePrompt = string.IsNullOrWhiteSpace(prompt) ? "DRAW ONE OBJECT" : prompt.Trim();
            PrepareContentRedrawPrompt(safePrompt);
            CompleteContentRedrawPrompt(officerLineKey);
        }

        private IEnumerator ShowContentRedrawPromptRoutine(string prompt, string officerLineKey = null)
        {
            string safePrompt = string.IsNullOrWhiteSpace(prompt) ? "DRAW ONE OBJECT" : prompt.Trim();
            PrepareContentRedrawPrompt(safePrompt);
            yield return WaitForCameraTransitionRoutine();
            CompleteContentRedrawPrompt(officerLineKey);
            _routine = null;
        }

        private void PrepareContentRedrawPrompt(string safePrompt)
        {
            _currentRejectedInputReason = LocalizeRedrawPrompt(safePrompt);
            _context?.Drawing?.SetInteractionLocked(true);
            _context?.Drawing?.ClearRecognitionLabel();
            _context?.Drawing?.ClearInstructionLabel();
            _context?.Camera?.SetMode(CameraMode.TerminalZoom);
        }

        private void CompleteContentRedrawPrompt(string officerLineKey)
        {
            ChangeState(FirstContactModeState.ReviewingRejectedInput);
            EnableRejectedInputChoice(_currentRejectedInputReason);
            ShowOfficerLine(officerLineKey);
        }

        private void ShowProbeLabelUnsuitablePrompt(FirstContactProbeLabelResult result)
        {
            _currentProbeLabelInput = string.IsNullOrWhiteSpace(_pendingDisplayLabel)
                ? _currentProbeLabelInput
                : _pendingDisplayLabel;
            _terminalProbeLabelInputActive = true;
            ResolveLabelIssueFeedback(
                result?.LabelIssue ?? FirstContactProbeLabelIssue.NotConcrete,
                out string statusKey,
                out string statusFallback,
                out string officerLineKey);
            _terminalProbeLabelStatus = L10n.T(statusKey, statusFallback);
            _context?.Drawing?.SetInteractionLocked(true);
            _context?.Drawing?.ClearRecognitionLabel();
            _context?.Drawing?.ClearInstructionLabel();
            _context?.Camera?.SetMode(CameraMode.TerminalZoom);
            ChangeState(FirstContactModeState.ReviewingLabel);
            RefreshTerminalProbeLabelEntry(instant: false);
            ShowOfficerLine(officerLineKey);

            if (GetDebugSettings().logSimilarityScores)
            {
                Debug.Log(
                    "[FirstContactTranslationMode] Probe label rejected as unsuitable. " +
                    $"DisplayLabel='{_pendingDisplayLabel}' CanonicalLabel='{result?.CanonicalLabel}' " +
                    $"HasClassificationClaim={result?.HasClassificationClaim} " +
                    $"ClassificationClaimText='{result?.ClassificationClaimText}' " +
                    $"NeutralSubjectLabel='{result?.NeutralSubjectLabel}' " +
                    $"Reason='{result?.Reason}'",
                    this);
            }
        }

        private bool TryGetContentRedrawPrompt(
            FirstContactProbeValidationResult result,
            out string prompt,
            out string officerLineKey)
        {
            prompt = string.Empty;
            officerLineKey = string.Empty;
            FirstContactVlmSettings vlmSettings = GetVlmSettings();
            if (result == null)
            {
                prompt = "OBJECT NOT CLEAR";
                officerLineKey = "first_contact.officer.probe_unresolved_object";
                return true;
            }

            IReadOnlyList<FirstContactProbeVisualIssue> issues =
                result.CollectRejectedVisualIssues(vlmSettings);
            bool isBlank = issues.Count == 1 && issues[0] == FirstContactProbeVisualIssue.Blank;
            if (issues.Count == 0 && !result.IsLabelMismatch)
            {
                return false;
            }

            var promptBuilder = new StringBuilder();
            for (int i = 0; i < issues.Count; i++)
            {
                ResolveVisualIssueFeedback(
                    issues[i],
                    out string issuePrompt,
                    out string issueOfficerLineKey);
                if (promptBuilder.Length > 0)
                {
                    promptBuilder.Append('\n');
                }

                promptBuilder.Append(issuePrompt);
                if (string.IsNullOrWhiteSpace(officerLineKey))
                {
                    officerLineKey = issueOfficerLineKey;
                }
            }

            if (result.IsLabelMismatch && !isBlank)
            {
                if (promptBuilder.Length > 0)
                {
                    promptBuilder.Append('\n');
                }

                promptBuilder.Append("LABEL MISMATCH");
                if (string.IsNullOrWhiteSpace(officerLineKey))
                {
                    officerLineKey = "first_contact.officer.probe_label_mismatch";
                }
            }

            prompt = promptBuilder.ToString();
            return prompt.Length > 0;
        }

        private static void ResolveVisualIssueFeedback(
            FirstContactProbeVisualIssue issue,
            out string prompt,
            out string officerLineKey)
        {
            switch (issue)
            {
                case FirstContactProbeVisualIssue.Blank:
                    prompt = "DRAW SOMETHING";
                    officerLineKey = "first_contact.officer.probe_blank";
                    break;
                case FirstContactProbeVisualIssue.TextOrSymbol:
                    prompt = "TEXT OR SYMBOL DETECTED";
                    officerLineKey = "first_contact.officer.probe_text_or_symbol";
                    break;
                case FirstContactProbeVisualIssue.SceneOrAction:
                    prompt = "SCENE OR ACTION DETECTED";
                    officerLineKey = "first_contact.officer.probe_scene_or_action";
                    break;
                case FirstContactProbeVisualIssue.MultipleObjects:
                    prompt = "DRAW ONE OBJECT ONLY";
                    officerLineKey = "first_contact.officer.probe_multiple_objects";
                    break;
                default:
                    prompt = "OBJECT NOT CLEAR";
                    officerLineKey = "first_contact.officer.probe_unresolved_object";
                    break;
            }
        }

        private static void ResolveLabelIssueFeedback(
            FirstContactProbeLabelIssue issue,
            out string statusKey,
            out string statusFallback,
            out string officerLineKey)
        {
            switch (issue)
            {
                case FirstContactProbeLabelIssue.ActionOrAbstract:
                    statusKey = "first_contact.terminal.status.label_action_or_abstract";
                    statusFallback = "LABEL: ACTION OR ABSTRACT";
                    officerLineKey = "first_contact.officer.probe_label_action_or_abstract";
                    break;
                case FirstContactProbeLabelIssue.BroadCategory:
                    statusKey = "first_contact.terminal.status.label_broad_category";
                    statusFallback = "LABEL: CATEGORY TOO BROAD";
                    officerLineKey = "first_contact.officer.probe_label_broad_category";
                    break;
                case FirstContactProbeLabelIssue.MultipleSubjects:
                    statusKey = "first_contact.terminal.status.label_multiple_subjects";
                    statusFallback = "LABEL: MULTIPLE SUBJECTS";
                    officerLineKey = "first_contact.officer.probe_label_multiple_subjects";
                    break;
                case FirstContactProbeLabelIssue.ClassificationClaim:
                    statusKey = "first_contact.terminal.status.label_classification_claim";
                    statusFallback = "LABEL: REMOVE CLAIM";
                    officerLineKey = "first_contact.officer.probe_label_classification_claim";
                    break;
                default:
                    statusKey = "first_contact.terminal.status.label_not_object";
                    statusFallback = "LABEL: OBJECT ONLY";
                    officerLineKey = "first_contact.officer.probe_label_not_object";
                    break;
            }
        }

        private static bool TryGetValidationErrorRedrawPrompt(
            FirstContactProbeValidationResult result,
            out string prompt,
            out string officerLineKey)
        {
            prompt = string.Empty;
            officerLineKey = string.Empty;
            if (result == null || result.IsSuccess || string.IsNullOrWhiteSpace(result.Error))
            {
                return false;
            }

            if (result.Error.Trim().Equals("Drawing is blank.", StringComparison.OrdinalIgnoreCase))
            {
                prompt = "DRAW SOMETHING";
                officerLineKey = "first_contact.officer.probe_blank";
                return true;
            }

            return false;
        }

        private static bool IsFatalValidationFailure(string error)
        {
            if (string.IsNullOrWhiteSpace(error))
            {
                return false;
            }

            string normalized = error.Trim().ToLowerInvariant();
            return normalized.Contains("validator pipeline is not assigned") ||
                   normalized.Contains("probe validation pipeline is not assigned") ||
                   normalized.Contains("gamepipelinerunner is missing") ||
                   normalized.Contains("drawing texture is unavailable");
        }

        private void LogFatalTechnicalFailure(string message)
        {
            Debug.LogError($"[FirstContactTranslationMode] {message}", this);
        }

        private void ShowTechnicalFailurePrompt(
            FirstContactTechnicalRetryAction retryAction,
            string statusKey,
            string statusFallback,
            string diagnostic)
        {
            LogFatalTechnicalFailure(diagnostic);
            _technicalRetryAction = retryAction;
            _technicalFailureStatus = L10n.T(
                $"first_contact.terminal.status.{statusKey}",
                statusFallback);
            _context?.Drawing?.SetInteractionLocked(true);
            _context?.Drawing?.ClearRecognitionLabel();
            _context?.Drawing?.ClearInstructionLabel();
            _context?.Camera?.SetMode(CameraMode.TerminalZoom);
            ChangeState(FirstContactModeState.ReviewingTechnicalFailure);
            EnableTechnicalRetryChoice();
        }

        private void ClearTechnicalFailureState()
        {
            _technicalRetryAction = FirstContactTechnicalRetryAction.None;
            _technicalFailureStatus = string.Empty;
        }

        private static string ResolveDynamicLabelFallback(string fallbackLabel)
        {
            if (LlmLocalizationSettings.IsEnglishLocale(L10n.CurrentLocale))
            {
                return string.IsNullOrWhiteSpace(fallbackLabel) ? "UNKNOWN" : fallbackLabel.Trim();
            }

            return L10n.T("first_contact.terminal.fallback.unknown", "UNKNOWN");
        }

        private static string GetDrawingInstructionText(FirstContactCardSource source)
        {
            return L10n.T("first_contact.terminal.prompt.send_drawing", "PRESS ENTER TO SEND");
        }

        private static string LocalizeRedrawPrompt(string prompt)
        {
            if (string.IsNullOrWhiteSpace(prompt))
            {
                return L10n.T("first_contact.terminal.reason.draw_one_object", "DRAW ONE OBJECT");
            }

            string normalizedPrompt = prompt.Replace("\r\n", "\n").Replace('\r', '\n');
            if (normalizedPrompt.Contains('\n'))
            {
                string[] lines = normalizedPrompt.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < lines.Length; i++)
                {
                    lines[i] = LocalizeRedrawPrompt(lines[i]);
                }

                return string.Join("\n", lines);
            }

            string normalized = normalizedPrompt.Trim().ToUpperInvariant();
            return normalized switch
            {
                "DRAW SOMETHING" => L10n.T("first_contact.terminal.reason.draw_something", "DRAW SOMETHING"),
                "DRAW ONE OBJECT" => L10n.T("first_contact.terminal.reason.draw_one_object", "DRAW ONE OBJECT"),
                "DRAW ONE OBJECT ONLY" => L10n.T("first_contact.terminal.reason.draw_one_object_only", "DRAW ONE OBJECT ONLY"),
                "TEXT OR SYMBOL DETECTED" => L10n.T("first_contact.terminal.reason.text_or_symbol_detected", "TEXT OR SYMBOL DETECTED"),
                "SCENE OR ACTION DETECTED" => L10n.T("first_contact.terminal.reason.scene_or_action_detected", "SCENE OR ACTION DETECTED"),
                "OBJECT NOT CLEAR" => L10n.T("first_contact.terminal.reason.object_not_clear", "OBJECT NOT CLEAR"),
                "LABEL MISMATCH" => L10n.T("first_contact.terminal.reason.label_mismatch", "LABEL MISMATCH"),
                _ => prompt.Trim()
            };
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
            _terminalPresenter = new FirstContactTerminalPresenter(
                _context?.TerminalDisplay ?? FindFirstObjectByType<TerminalDisplay>(),
                GetDebugSettings(),
                GetPresentationSettings());
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

        private List<SemanticClusterTransitionState> CaptureSemanticClusterStates()
        {
            var states = new List<SemanticClusterTransitionState>();
            IReadOnlyList<SemanticClusterRecord> clusters = _semanticMemory?.Clusters;
            if (clusters == null)
            {
                return states;
            }

            for (int i = 0; i < clusters.Count; i++)
            {
                SemanticClusterRecord cluster = clusters[i];
                if (cluster == null || string.IsNullOrWhiteSpace(cluster.Id))
                {
                    continue;
                }

                states.Add(new SemanticClusterTransitionState(
                    cluster.Id,
                    cluster.IsStable,
                    cluster.Members.Count));
            }

            return states;
        }

        private static FirstContactClusterFormationEvent BuildClusterFormationEvent(
            SemanticCardRecord card,
            SemanticClusterRecord cluster,
            IReadOnlyList<SemanticClusterTransitionState> beforeClusterStates,
            IReadOnlyList<FirstContactClusterFormationEdge> formationEdges)
        {
            if (card == null || cluster == null)
            {
                return default;
            }

            bool hadCluster = TryFindClusterTransitionState(
                beforeClusterStates,
                cluster.Id,
                out SemanticClusterTransitionState beforeState);
            bool becameStable = cluster.IsStable && (!hadCluster || !beforeState.IsStable);
            bool isNewCluster = !hadCluster || (beforeState.MemberCount <= 1 && cluster.Members.Count > 1);
            FirstContactClusterFormationEdge[] edges = CopyFormationEdges(formationEdges);
            string[] memberNodeIds = BuildClusterMemberNodeIds(cluster);
            return new FirstContactClusterFormationEvent(
                FirstContactSemanticMapLayout.BuildCardNodeId(card),
                cluster.IsStable ? FirstContactSemanticMapLayout.BuildClusterNodeId(cluster) : string.Empty,
                cluster.DisplayName,
                hasCluster: true,
                isNewCluster,
                becameStable,
                cluster.IsStable,
                cluster.Members.Count,
                edges,
                memberNodeIds);
        }

        private static FirstContactClusterFormationEdge[] CopyFormationEdges(
            IReadOnlyList<FirstContactClusterFormationEdge> edges)
        {
            if (edges == null || edges.Count == 0)
            {
                return Array.Empty<FirstContactClusterFormationEdge>();
            }

            var copy = new FirstContactClusterFormationEdge[edges.Count];
            for (int i = 0; i < edges.Count; i++)
            {
                copy[i] = edges[i];
            }

            return copy;
        }

        private static string[] BuildClusterMemberNodeIds(SemanticClusterRecord cluster)
        {
            if (cluster == null || cluster.Members.Count == 0)
            {
                return Array.Empty<string>();
            }

            var nodeIds = new string[cluster.Members.Count];
            for (int i = 0; i < cluster.Members.Count; i++)
            {
                nodeIds[i] = FirstContactSemanticMapLayout.BuildCardNodeId(cluster.Members[i]);
            }

            return nodeIds;
        }

        private static bool TryFindClusterTransitionState(
            IReadOnlyList<SemanticClusterTransitionState> states,
            string clusterId,
            out SemanticClusterTransitionState state)
        {
            if (states != null && !string.IsNullOrWhiteSpace(clusterId))
            {
                for (int i = 0; i < states.Count; i++)
                {
                    if (string.Equals(states[i].Id, clusterId, StringComparison.OrdinalIgnoreCase))
                    {
                        state = states[i];
                        return true;
                    }
                }
            }

            state = default;
            return false;
        }

        private bool HandleOfficerLineDismissInput()
        {
            if (!_officerLineVisible || !_officerLineAwaitingDismissal)
            {
                return false;
            }

            if (Time.frameCount <= _officerLineShownFrame)
            {
                return false;
            }

            if (!WasKeyPressed(KeyCode.Space))
            {
                return false;
            }

            HideOfficerLine(force: true);
            return true;
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
            _officerLineVisible = true;
            _officerLineAwaitingDismissal = true;
            _officerLineShownFrame = Time.frameCount;
        }

        private void HideOfficerLine(bool force = false)
        {
            if (!force && _officerLineAwaitingDismissal)
            {
                return;
            }

            if (!force && !_officerLineVisible)
            {
                return;
            }

            _context?.Subtitles?.Hide();
            _officerLineVisible = false;
            _officerLineAwaitingDismissal = false;
            _officerLineShownFrame = -1;
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

        private FirstContactSemanticMapSnapshot BuildBootstrapSemanticMapSnapshot(
            SemanticCardRecord activeCard,
            BootstrapCategoryState category,
            bool includeActiveCard)
        {
            FirstContactSemanticMapSnapshot snapshot = BuildSemanticMapSnapshot(activeCard, string.Empty);
            string activeCardNodeId = FirstContactSemanticMapLayout.BuildCardNodeId(activeCard);
            HashSet<string> relevantClusterNodeIds = BuildBootstrapRelevantClusterNodeIds(category);
            PruneBootstrapSnapshot(
                snapshot,
                category,
                activeCardNodeId,
                includeActiveCard,
                relevantClusterNodeIds);
            snapshot.Links.Clear();
            AddBootstrapCategoryNode(snapshot, category);
            ShapeBootstrapCategoryNodes(snapshot, category, activeCardNodeId);
            return snapshot;
        }

        private HashSet<string> BuildBootstrapRelevantClusterNodeIds(BootstrapCategoryState category)
        {
            var clusterNodeIds = new HashSet<string>(StringComparer.Ordinal);
            IReadOnlyList<SemanticClusterRecord> clusters = _semanticMemory?.Clusters;
            if (category == null || clusters == null)
            {
                return clusterNodeIds;
            }

            for (int i = 0; i < clusters.Count; i++)
            {
                SemanticClusterRecord cluster = clusters[i];
                if (cluster == null || !cluster.IsStable)
                {
                    continue;
                }

                if (HasDetachedBootstrapMember(cluster, category))
                {
                    clusterNodeIds.Add(FirstContactSemanticMapLayout.BuildClusterNodeId(cluster));
                }
            }

            return clusterNodeIds;
        }

        private static bool ShouldShowBootstrapSemanticFormation(
            BootstrapCategoryState category,
            SemanticClusterRecord cluster,
            FirstContactClusterFormationEvent formation)
        {
            if (!formation.ShouldAnimate || category == null || cluster == null)
            {
                return false;
            }

            return HasDetachedBootstrapMember(cluster, category);
        }

        private static bool HasDetachedBootstrapMember(
            SemanticClusterRecord cluster,
            BootstrapCategoryState category)
        {
            if (cluster == null || category == null)
            {
                return false;
            }

            for (int i = 0; i < cluster.Members.Count; i++)
            {
                SemanticCardRecord member = cluster.Members[i];
                if (member != null &&
                    member.BootstrapCategoryEvaluated &&
                    !member.BootstrapCategoryAccepted &&
                    string.Equals(member.BootstrapCategoryId, category.Id, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static void PruneBootstrapSnapshot(
            FirstContactSemanticMapSnapshot snapshot,
            BootstrapCategoryState category,
            string activeCardNodeId,
            bool includeActiveCard,
            ISet<string> relevantClusterNodeIds)
        {
            if (snapshot == null || category == null)
            {
                return;
            }

            for (int i = snapshot.Nodes.Count - 1; i >= 0; i--)
            {
                FirstContactSemanticMapNode node = snapshot.Nodes[i];
                bool keepCurrentCategoryCard =
                    node != null &&
                    node.Kind == FirstContactSemanticMapNodeKind.Card &&
                    string.Equals(node.BootstrapCategoryId, category.Id, StringComparison.Ordinal);
                bool keepRelevantCluster =
                    node != null &&
                    node.Kind == FirstContactSemanticMapNodeKind.StableCluster &&
                    relevantClusterNodeIds != null &&
                    relevantClusterNodeIds.Contains(node.Id);
                bool keep = keepCurrentCategoryCard || keepRelevantCluster;
                if (keepCurrentCategoryCard &&
                    !includeActiveCard &&
                    string.Equals(node.Id, activeCardNodeId, StringComparison.Ordinal))
                {
                    keep = false;
                }

                if (!keep)
                {
                    snapshot.Nodes.RemoveAt(i);
                }
            }
        }

        private void AddBootstrapCategoryNode(
            FirstContactSemanticMapSnapshot snapshot,
            BootstrapCategoryState category)
        {
            if (snapshot == null || category == null)
            {
                return;
            }

            string categoryNodeId = BuildBootstrapCategoryNodeId(category);
            if (snapshot.FindNode(categoryNodeId) != null)
            {
                return;
            }

            category.TryBuildCentroid(_embeddingService, out float[] centroid);
            var node = new FirstContactSemanticMapNode
            {
                Id = categoryNodeId,
                Label = FirstContactTerminalLocalization.LocalizeBootstrapCategory(category.DisplayName).ToUpperInvariant(),
                SecondaryLabel = FirstContactTerminalLocalization.LocalizeMeaning(category.Meaning).ToUpperInvariant(),
                Kind = FirstContactSemanticMapNodeKind.BootstrapCategory,
                Position = ResolveBootstrapCategoryPosition(snapshot, category),
                Embedding = centroid,
                IsActive = true,
                Marker = '*',
                BootstrapCategoryId = category.Id,
                TraceCount = category.TraceCount,
                RequiredTraceCount = category.RequiredTraceCount,
                IsBootstrapCategoryStable = category.IsStable
            };
            snapshot.Nodes.Add(node);
        }

        private Vector2 ResolveBootstrapCategoryPosition(
            FirstContactSemanticMapSnapshot snapshot,
            BootstrapCategoryState category)
        {
            return new Vector2(0.36f, 0.06f);
        }

        private void ShapeBootstrapCategoryNodes(
            FirstContactSemanticMapSnapshot snapshot,
            BootstrapCategoryState category,
            string activeCardNodeId)
        {
            if (snapshot == null || category == null)
            {
                return;
            }

            FirstContactSemanticMapNode categoryNode = snapshot.FindNode(BuildBootstrapCategoryNodeId(category));
            if (categoryNode == null)
            {
                return;
            }

            var acceptedNodes = new List<FirstContactSemanticMapNode>();
            var detachedNodes = new List<FirstContactSemanticMapNode>();
            var clusterNodes = new List<FirstContactSemanticMapNode>();
            for (int i = 0; i < snapshot.Nodes.Count; i++)
            {
                FirstContactSemanticMapNode node = snapshot.Nodes[i];
                if (node == null)
                {
                    continue;
                }

                if (node.Kind == FirstContactSemanticMapNodeKind.StableCluster)
                {
                    clusterNodes.Add(node);
                    continue;
                }

                if (node.Kind != FirstContactSemanticMapNodeKind.Card ||
                    !string.Equals(node.BootstrapCategoryId, category.Id, StringComparison.Ordinal))
                {
                    continue;
                }

                if (node.IsBootstrapDetached)
                {
                    detachedNodes.Add(node);
                    continue;
                }

                acceptedNodes.Add(node);
            }

            for (int i = 0; i < clusterNodes.Count; i++)
            {
                clusterNodes[i].Position = ResolveBootstrapDetachedClusterPosition(i);
            }

            int acceptedIndex = 0;
            for (int i = 0; i < acceptedNodes.Count; i++)
            {
                FirstContactSemanticMapNode node = acceptedNodes[i];
                float angleOffset = ResolveBootstrapAcceptedAngle(acceptedIndex);
                Vector2 orbit = new(Mathf.Cos(angleOffset), Mathf.Sin(angleOffset));
                float orbitDistance = Mathf.Lerp(0.42f, 0.5f, Mathf.Clamp01((acceptedNodes.Count - 1) / 4f));
                node.Position = ClampSemanticMapPosition(categoryNode.Position + orbit * orbitDistance);
                AddSemanticMapLinkIfMissing(snapshot, node.Id, categoryNode.Id, 0.72f);
                acceptedIndex++;
            }

            var clusterMemberIndices = new Dictionary<string, int>(StringComparer.Ordinal);
            int looseDetachedIndex = 0;
            for (int i = 0; i < detachedNodes.Count; i++)
            {
                FirstContactSemanticMapNode node = detachedNodes[i];
                bool active = string.Equals(node.Id, activeCardNodeId, StringComparison.Ordinal);
                string clusterNodeId = FirstContactSemanticMapLayout.BuildClusterNodeId(node.SecondaryLabel);
                FirstContactSemanticMapNode clusterNode = snapshot.FindNode(clusterNodeId);
                if (clusterNode != null)
                {
                    clusterMemberIndices.TryGetValue(clusterNodeId, out int clusterMemberIndex);
                    node.Position = ResolveBootstrapDetachedClusterMemberPosition(
                        clusterNode.Position,
                        clusterMemberIndex,
                        active);
                    AddSemanticMapLinkIfMissing(snapshot, node.Id, clusterNode.Id, active ? 0.92f : 0.74f);
                    clusterMemberIndices[clusterNodeId] = clusterMemberIndex + 1;
                    continue;
                }

                node.Position = ResolveBootstrapDetachedPosition(looseDetachedIndex, active);
                looseDetachedIndex++;
            }
        }

        private static float ResolveBootstrapAcceptedAngle(int index)
        {
            float degree = index switch
            {
                0 => 140f,
                1 => -145f,
                2 => 74f,
                3 => -58f,
                4 => 188f,
                5 => 14f,
                _ => 140f + index * 137.5f
            };
            return degree * Mathf.Deg2Rad;
        }

        private static Vector2 ResolveBootstrapDetachedClusterPosition(int index)
        {
            int safeIndex = Mathf.Max(0, index);
            int column = safeIndex % 2;
            int row = safeIndex / 2;
            Vector2 basePosition = new(-0.54f, 0.08f);
            Vector2 offset = new(column * 0.46f, -row * 0.46f);
            return ClampSemanticMapPosition(basePosition + offset);
        }

        private static Vector2 ResolveBootstrapDetachedClusterMemberPosition(
            Vector2 clusterPosition,
            int index,
            bool active)
        {
            float degree = index switch
            {
                0 => 180f,
                1 => 122f,
                2 => -132f,
                3 => -72f,
                4 => 68f,
                5 => 8f,
                _ => 180f + index * 137.5f
            };
            float radius = active ? 0.32f : 0.26f;
            Vector2 orbit = new(Mathf.Cos(degree * Mathf.Deg2Rad), Mathf.Sin(degree * Mathf.Deg2Rad));
            return ClampSemanticMapPosition(clusterPosition + orbit * radius);
        }

        private static Vector2 ResolveBootstrapDetachedPosition(int index, bool active)
        {
            int safeIndex = Mathf.Max(0, index);
            int column = safeIndex % 3;
            int row = safeIndex / 3;
            Vector2 basePosition = active
                ? new Vector2(-0.58f, -0.22f)
                : new Vector2(-0.72f, -0.42f);
            Vector2 offset = new(column * 0.24f, -row * 0.22f);
            return ClampSemanticMapPosition(basePosition + offset);
        }

        private static void RemoveSemanticMapNode(
            FirstContactSemanticMapSnapshot snapshot,
            string nodeId)
        {
            if (snapshot == null || string.IsNullOrWhiteSpace(nodeId))
            {
                return;
            }

            for (int i = snapshot.Nodes.Count - 1; i >= 0; i--)
            {
                if (string.Equals(snapshot.Nodes[i]?.Id, nodeId, StringComparison.Ordinal))
                {
                    snapshot.Nodes.RemoveAt(i);
                }
            }

            for (int i = snapshot.Links.Count - 1; i >= 0; i--)
            {
                FirstContactSemanticMapLink link = snapshot.Links[i];
                if (string.Equals(link.FromId, nodeId, StringComparison.Ordinal) ||
                    string.Equals(link.ToId, nodeId, StringComparison.Ordinal))
                {
                    snapshot.Links.RemoveAt(i);
                }
            }
        }

        private static void AddSemanticMapLinkIfMissing(
            FirstContactSemanticMapSnapshot snapshot,
            string fromId,
            string toId,
            float strength)
        {
            if (snapshot == null || string.IsNullOrWhiteSpace(fromId) || string.IsNullOrWhiteSpace(toId))
            {
                return;
            }

            for (int i = 0; i < snapshot.Links.Count; i++)
            {
                FirstContactSemanticMapLink existing = snapshot.Links[i];
                bool sameDirection = string.Equals(existing.FromId, fromId, StringComparison.Ordinal) &&
                                     string.Equals(existing.ToId, toId, StringComparison.Ordinal);
                bool reverseDirection = string.Equals(existing.FromId, toId, StringComparison.Ordinal) &&
                                        string.Equals(existing.ToId, fromId, StringComparison.Ordinal);
                if (sameDirection || reverseDirection)
                {
                    if (strength > existing.Strength)
                    {
                        snapshot.Links[i] = new FirstContactSemanticMapLink(fromId, toId, strength);
                    }

                    return;
                }
            }

            snapshot.Links.Add(new FirstContactSemanticMapLink(fromId, toId, strength));
        }

        private static string BuildBootstrapCategoryNodeId(BootstrapCategoryState category)
        {
            return category == null || string.IsNullOrWhiteSpace(category.Id)
                ? string.Empty
                : $"B:{category.Id}";
        }

        private static Vector2 DeterministicSemanticMapDirection(string key)
        {
            uint hash = HashSemanticMapKey(key ?? string.Empty);
            float angle = (hash / (float)uint.MaxValue) * Mathf.PI * 2f;
            return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        }

        private static Vector2 ClampSemanticMapPosition(Vector2 value)
        {
            return new Vector2(
                Mathf.Clamp(value.x, -0.92f, 0.92f),
                Mathf.Clamp(value.y, -0.9f, 0.9f));
        }

        private static uint HashSemanticMapKey(string value)
        {
            unchecked
            {
                uint hash = 2166136261u;
                for (int i = 0; i < value.Length; i++)
                {
                    hash ^= value[i];
                    hash *= 16777619u;
                }

                return hash;
            }
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

        private IEnumerator WaitForCameraTransitionRoutine()
        {
            if (_context?.Camera == null)
            {
                yield break;
            }

            yield return null;
            while (_context.Camera.IsTransitioning)
            {
                yield return null;
            }
        }

        private IEnumerator WaitForTerminalContinueRoutine()
        {
            TerminalDisplay terminal = _context?.TerminalDisplay;
            if (terminal != null)
            {
                yield return null;
                while (terminal.IsTyping())
                {
                    yield return null;
                }
            }

            EnableContinueChoice();
            while (!_terminalContinueRequested)
            {
                yield return null;
            }

            HideOfficerLine(force: true);
        }

        private void ChangeState(FirstContactModeState state)
        {
            if (_modeState != state)
            {
                HideOfficerLine();
            }

            _modeState = state;
            GameState gameState = state switch
            {
                FirstContactModeState.DrawingBootstrapProbe => GameState.Drawing,
                FirstContactModeState.DrawingDecodeSample => GameState.Drawing,
                FirstContactModeState.DrawingAnswer => GameState.Drawing,
                FirstContactModeState.AnalyzingDrawing => GameState.PreviewAnalyzing,
                FirstContactModeState.ReviewingLabel => GameState.Interpreter,
                FirstContactModeState.ReviewingRejectedInput => GameState.Interpreter,
                FirstContactModeState.ReviewingTechnicalFailure => GameState.Interpreter,
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

        private readonly struct SemanticClusterTransitionState
        {
            public readonly string Id;
            public readonly bool IsStable;
            public readonly int MemberCount;

            public SemanticClusterTransitionState(string id, bool isStable, int memberCount)
            {
                Id = id ?? string.Empty;
                IsStable = isStable;
                MemberCount = Mathf.Max(0, memberCount);
            }
        }

        private readonly struct BootstrapProbeFit
        {
            public readonly float CategoryDescriptorFit;
            public readonly bool HasUsableSignal;
            public readonly bool HasCategoryDescriptor;

            public BootstrapProbeFit(
                float categoryDescriptorFit,
                bool hasUsableSignal,
                bool hasCategoryDescriptor)
            {
                CategoryDescriptorFit = categoryDescriptorFit;
                HasUsableSignal = hasUsableSignal;
                HasCategoryDescriptor = hasCategoryDescriptor;
            }
        }

        private sealed class BootstrapCategoryState
        {
            public readonly string Id;
            public readonly string DisplayName;
            public readonly string Meaning;
            public readonly string DescriptorText;
            public readonly int RequiredTraceCount;
            public readonly List<SemanticCardRecord> Cards = new();
            public readonly List<SemanticCardRecord> DetachedCards = new();
            public bool IsStable { get; private set; }
            private float[] _descriptorEmbedding;

            public BootstrapCategoryState(
                string id,
                string displayName,
                string meaning,
                string descriptorText,
                int requiredTraceCount)
            {
                Id = id ?? string.Empty;
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? "UNKNOWN" : displayName.Trim().ToUpperInvariant();
                Meaning = string.IsNullOrWhiteSpace(meaning) ? "[MEANING?]" : meaning.Trim().ToUpperInvariant();
                DescriptorText = string.IsNullOrWhiteSpace(descriptorText) ? DisplayName : descriptorText.Trim();
                RequiredTraceCount = Mathf.Max(1, requiredTraceCount);
            }

            public int TraceCount => Cards.Count;
            public bool HasDescriptorEmbedding => _descriptorEmbedding != null && _descriptorEmbedding.Length > 0;

            public void SetDescriptorEmbedding(float[] embedding)
            {
                _descriptorEmbedding = embedding;
            }

            public bool TryFindRecordedCardByLabel(string label, out SemanticCardRecord card)
            {
                card = null;
                string normalized = NormalizeCardLabel(label);
                if (string.IsNullOrWhiteSpace(normalized))
                {
                    return false;
                }

                if (TryFindRecordedCardByLabel(Cards, normalized, out card) ||
                    TryFindRecordedCardByLabel(DetachedCards, normalized, out card))
                {
                    return true;
                }

                return false;
            }

            public bool TryBuildCentroid(
                FirstContactEmbeddingService embeddingService,
                out float[] centroid)
            {
                return TryBuildCentroid(Cards, embeddingService, out centroid);
            }

            public BootstrapProbeFit EvaluateCandidate(
                SemanticCardRecord card,
                FirstContactEmbeddingService embeddingService)
            {
                bool hasUsableSignal =
                    card?.Embedding != null &&
                    card.Embedding.Length > 0;
                if (!hasUsableSignal)
                {
                    return new BootstrapProbeFit(0f, false, HasDescriptorEmbedding);
                }

                CalculateCategoryDescriptorFit(
                    card,
                    embeddingService,
                    out float categoryFit,
                    out bool hasCategoryDescriptor);

                return new BootstrapProbeFit(
                    categoryFit,
                    true,
                    hasCategoryDescriptor);
            }

            private void CalculateCategoryDescriptorFit(
                SemanticCardRecord card,
                FirstContactEmbeddingService embeddingService,
                out float categoryFit,
                out bool hasCategoryDescriptor)
            {
                categoryFit = 1f;
                hasCategoryDescriptor = false;
                if (card?.Embedding == null || embeddingService == null || !HasDescriptorEmbedding)
                {
                    return;
                }

                categoryFit = embeddingService.Similarity(card.Embedding, _descriptorEmbedding);
                hasCategoryDescriptor = true;
            }

            public bool TryAcceptCard(
                SemanticCardRecord card,
                FirstContactEmbeddingService embeddingService,
                FirstContactSemanticSettings settings,
                BootstrapProbeFit fit,
                bool categoryAccepted)
            {
                settings ??= ScriptableObject.CreateInstance<FirstContactSemanticSettings>();
                bool accepted = fit.HasUsableSignal &&
                                categoryAccepted;

                if (accepted)
                {
                    Cards.Add(card);
                }
                else
                {
                    DetachedCards.Add(card);
                }

                if (card != null)
                {
                    card.BootstrapCategoryEvaluated = true;
                    card.BootstrapCategoryAccepted = accepted;
                }

                RecalculateStability(embeddingService, settings);
                return accepted;
            }

            public void RecalculateStability(
                FirstContactEmbeddingService embeddingService,
                FirstContactSemanticSettings settings)
            {
                settings ??= ScriptableObject.CreateInstance<FirstContactSemanticSettings>();
                IsStable = TraceCount >= Mathf.Max(2, RequiredTraceCount);
            }

            private static bool TryBuildCentroid(
                IReadOnlyList<SemanticCardRecord> cards,
                FirstContactEmbeddingService embeddingService,
                out float[] centroid)
            {
                centroid = null;
                if (cards == null || embeddingService == null)
                {
                    return false;
                }

                var vectors = new List<float[]>();
                for (int i = 0; i < cards.Count; i++)
                {
                    float[] embedding = cards[i]?.Embedding;
                    if (embedding != null && embedding.Length > 0)
                    {
                        vectors.Add(embedding);
                    }
                }

                return embeddingService.TryBuildCentroid(vectors, out centroid);
            }

            private static bool TryFindRecordedCardByLabel(
                IReadOnlyList<SemanticCardRecord> cards,
                string normalizedLabel,
                out SemanticCardRecord card)
            {
                card = null;
                if (cards == null || string.IsNullOrWhiteSpace(normalizedLabel))
                {
                    return false;
                }

                for (int i = 0; i < cards.Count; i++)
                {
                    SemanticCardRecord candidate = cards[i];
                    if (candidate != null &&
                        string.Equals(NormalizeCardLabel(candidate.Label), normalizedLabel, StringComparison.Ordinal))
                    {
                        card = candidate;
                        return true;
                    }
                }

                return false;
            }

            private static string NormalizeCardLabel(string label)
            {
                return string.IsNullOrWhiteSpace(label)
                    ? string.Empty
                    : NormalizeProbeLabel(label);
            }
        }

        private static string NormalizeProbeLabel(string label)
        {
            return string.IsNullOrWhiteSpace(label)
                ? string.Empty
                : label.Trim().ToLowerInvariant();
        }

        private static string NormalizePlayerLabelText(string label)
        {
            string trimmed = label?.Trim() ?? string.Empty;
            if (trimmed.Length == 0)
            {
                return string.Empty;
            }

            try
            {
                return trimmed.Normalize(NormalizationForm.FormC);
            }
            catch (ArgumentException)
            {
                return trimmed;
            }
        }

        private enum FirstContactModeState
        {
            Inactive,
            Ready,
            BootstrapProbeSequence,
            DrawingBootstrapProbe,
            ReceivingQuestion,
            InspectingQuestion,
            DrawingDecodeSample,
            DrawingAnswer,
            AnalyzingDrawing,
            ReviewingLabel,
            ReviewingRejectedInput,
            ReviewingTechnicalFailure,
            StoringBootstrapProbe,
            UpdatingTranslation,
            TransmittingAnswer,
            BootstrapComplete,
            Completed
        }

        private enum FirstContactTerminalChoiceMode
        {
            None,
            QuestionActions,
            SemanticMap,
            LabelReview,
            RejectedInput,
            TechnicalRetry,
            BootstrapProbe,
            Continue
        }

        private enum FirstContactTechnicalRetryAction
        {
            None,
            CaptureDrawing,
            AnalyzeDrawing,
            ConfirmDrawing
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
