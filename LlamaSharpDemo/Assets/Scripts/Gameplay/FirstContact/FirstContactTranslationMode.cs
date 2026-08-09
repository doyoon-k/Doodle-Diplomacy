using System;
using System.Collections;
using System.Collections.Generic;
using DoodleDiplomacy.Camera;
using DoodleDiplomacy.Core;
using DoodleDiplomacy.Devices;
using DoodleDiplomacy.Interaction;
using DoodleDiplomacy.Localization;
using DoodleDiplomacy.Narrative;
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
        private const string DoctorHwangSpeakerKey = "speaker.doctor_hwang";
        private const int MaxProbeLabelLength = 32;
        private const int MaxPatternMeaningLength = 24;

        [Header("Mode")]
        [SerializeField] private string modeId = DefaultModeId;
        [SerializeField] private FirstContactModeConfig config;

        public event Action<GameState> StateChanged;

        private GameplayModeContext _context;
        private FirstContactInteractionPolicy _interactionPolicy;
        private FirstContactTerminalPresenter _terminalPresenter;
        private FirstContactEmbeddingService _embeddingService;
        private FirstContactProbeProcessor _probeProcessor;
        private FirstContactProbeCaptureService _probeCaptureService;
        private TerminalTextEntrySession _terminalTextEntry;
        private FirstContactSemanticMemory _semanticMemory;
        private FirstContactBootstrapMapBuilder _bootstrapMapBuilder;
        private FirstContactEncounterDirector _encounterDirector;
        private FirstContactMeetingArrivalController _meetingArrival;
        private TabletPhysicalControlsController _tabletControls;
        private FirstContactSessionContext _session;
        private Coroutine _routine;
        private GameState _currentGameState = GameState.Title;
        private FirstContactModeState _modeState = FirstContactModeState.Inactive;
        private int _runtimeWaveformSessionSeed;

        private readonly FirstContactProbeWorkingState _workingProbe = new();
        private string _currentProbeLabelInput;
        private bool _terminalChoiceInputEnabled;
        private FirstContactTerminalChoiceMode _terminalChoiceMode = FirstContactTerminalChoiceMode.None;
        private int _selectedTerminalChoiceIndex;
        private string _currentRejectedInputReason = string.Empty;
        private FirstContactTechnicalRetryAction _technicalRetryAction = FirstContactTechnicalRetryAction.None;
        private string _technicalFailureStatus = string.Empty;
        private bool _terminalContinueRequested;
        private bool _hasShownBootstrapDuplicateGuidanceLine;
        private bool _guidanceLineVisible;
        private bool _guidanceLineAwaitingDismissal;
        private int _guidanceLineShownFrame = -1;
        private int _lastSubmitInputFrame = -1;
        private string _terminalProbeLabelStatus = string.Empty;
        private string _currentPatternMeaningInput = string.Empty;
        private string _terminalPatternMeaningStatus = string.Empty;
        private SemanticClusterRecord _pendingMeaningCluster;
        private FirstContactSemanticMapSnapshot _pendingMeaningMapSnapshot;
        private bool _patternMeaningSubmitted;
        private FirstContactBootstrapSession _bootstrapSession;
        private bool _startWithIntro;
        private bool _isPreflightTutorial;

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
            ApplyInteractionPolicy();
            ChangeState(FirstContactModeState.Ready);
        }

        public void Exit()
        {
            StopActiveRoutine();
            _meetingArrival?.StopPresentation();
            _encounterDirector?.StopPresentation();
            _tabletControls?.ClearTutorialHighlight();
            // A preflight probe validates the real authoring path but must never seed
            // semantic memory, response traces, or bootstrap category progress.
            _isPreflightTutorial = false;
            EndTerminalProbeLabelInput();
            GamePipelineRunner.Instance?.StopGeneration();
            HideGuidanceLine(force: true);
            _context?.Drawing?.SetInteractionLocked(true);
            _context?.Drawing?.ClearRecognitionLabel();
            _context?.Drawing?.ClearInstructionLabel();
            _terminalPresenter?.Clear();
            DisableTerminalChoices();
            ClearTechnicalFailureState();
            _context = null;
            ChangeState(FirstContactModeState.Inactive);
        }

        private void OnDestroy()
        {
            EndTerminalProbeLabelInput();
            _terminalTextEntry?.Dispose();
            _probeCaptureService?.Dispose();
        }

        private void OnEnable()
        {
            _terminalTextEntry?.Enable();
        }

        private void OnDisable()
        {
            EndTerminalProbeLabelInput();
            _terminalTextEntry?.Disable();
            HideGuidanceLine(force: true);
        }

        public void Tick(float deltaTime)
        {
            if (_encounterDirector?.IsBlocking == true)
            {
                return;
            }

            if (HandleGuidanceLineDismissInput())
            {
                return;
            }

            if (HandleTerminalTextInput())
            {
                return;
            }

            HandleTerminalChoiceInput();
            HandleDrawingSubmitInput();
        }

        public void HandleInteraction(InteractionType type, InteractableObject source)
        {
            if (_encounterDirector?.IsBlocking == true)
            {
                return;
            }

            if (type == InteractionType.Tablet &&
                _modeState == FirstContactModeState.DrawingBootstrapProbe)
            {
                HideGuidanceLine();
                _context?.Camera?.SetMode(CameraMode.TabletView);
            }
            else if (type == InteractionType.Terminal)
            {
                HideGuidanceLine(force: true);
                _context?.Camera?.SetMode(CameraMode.TerminalZoom);
            }
        }

        public void StartGame(bool isFirstPlay = true)
        {
            StopActiveRoutine();
            HideGuidanceLine(force: true);
            _startWithIntro = isFirstPlay;
            _routine = StartCoroutine(StartFirstContactRoutine());
        }

        public void ChangeToTitle()
        {
            StopActiveRoutine();
            _encounterDirector?.StopPresentation();
            _tabletControls?.ClearTutorialHighlight();
            _isPreflightTutorial = false;
            HideGuidanceLine(force: true);
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
                SubmitProbeLabel();
                return;
            }

            if (_modeState == FirstContactModeState.AssigningPatternMeaning)
            {
                SubmitPatternMeaning();
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
            ClearTechnicalFailureState();
            if (_meetingArrival != null &&
                _meetingArrival.ShouldPlay(_context, _startWithIntro))
            {
                yield return _meetingArrival.PlayRoutine(
                    _context,
                    config != null && config.narrativeSettings != null
                        ? config.narrativeSettings.narrativeScenario
                        : null);
            }

            _runtimeWaveformSessionSeed = UnityEngine.Random.Range(1, int.MaxValue);
            _bootstrapMapBuilder.Reset(_runtimeWaveformSessionSeed);
            _session = new FirstContactSessionContext();
            _hasShownBootstrapDuplicateGuidanceLine = false;
            ClearPendingPatternMeaning();
            InitializeBootstrapSession();
            _semanticMemory = new FirstContactSemanticMemory(
                _embeddingService,
                GetSemanticSettings(),
                GetDebugSettings(),
                config.bootstrapCategories);
            _context?.Drawing?.SetInteractionLocked(true);
            _context?.Drawing?.ClearRecognitionLabel();
            _context?.Drawing?.ClearInstructionLabel();
            _context?.SharedMonitorDisplay?.SetIdle();
            _terminalPresenter?.Clear();
            DisableTerminalChoices();
            _encounterDirector?.BeginSession();
            if (_encounterDirector != null)
            {
                yield return _encounterDirector.PlayOpeningPreludeRoutine(_startWithIntro);
            }

            yield return PrepareBootstrapCategoryDescriptorsRoutine();
            if (_encounterDirector?.ShouldRunPreflightTutorial(_startWithIntro) == true)
            {
                yield return StartPreflightTutorialRoutine();
                _routine = null;
                yield break;
            }

            if (_encounterDirector != null)
            {
                yield return _encounterDirector.PlayDelegationArrivalRoutine(_startWithIntro);
            }

            yield return StartBootstrapProbeSequenceRoutine();
            _routine = null;
        }

        private IEnumerator StartPreflightTutorialRoutine()
        {
            _isPreflightTutorial = true;

            DisableTerminalChoices();
            EndTerminalProbeLabelInput();
            _context?.Drawing?.EnsureRuntimeEnabled();
            _context?.Drawing?.SetInteractionLocked(true);
            _context?.Drawing?.ClearCanvas();
            _context?.Drawing?.ClearRecognitionLabel();
            _context?.Drawing?.ClearInstructionLabel();
            _context?.Camera?.SetMode(CameraMode.TabletView);
            ChangeState(FirstContactModeState.PreflightControls);
            _terminalPresenter?.ShowPreflightReady(instant: false);

            if (_encounterDirector != null)
            {
                yield return _encounterDirector.PlayPreflightIntroRoutine();
            }

            _tabletControls?.SetTutorialHighlight(TabletTutorialHighlightGroup.Tools);
            if (_encounterDirector != null)
            {
                yield return _encounterDirector.PlayTabletToolsRoutine();
            }

            _tabletControls?.SetTutorialHighlight(TabletTutorialHighlightGroup.Style);
            if (_encounterDirector != null)
            {
                yield return _encounterDirector.PlayTabletStyleRoutine();
            }

            _tabletControls?.SetTutorialHighlight(TabletTutorialHighlightGroup.HistoryAndSend);
            if (_encounterDirector != null)
            {
                yield return _encounterDirector.PlayTabletHistoryAndSendRoutine();
            }

            _tabletControls?.ClearTutorialHighlight();
            yield return BeginDrawingRoutine();
        }

        private void InitializeBootstrapSession()
        {
            if (config == null)
            {
                throw new InvalidOperationException(
                    "[FirstContactTranslationMode] Bootstrap CATEGORY configuration is missing.");
            }

            if (!config.TryGetBootstrapCategories(out IReadOnlyList<FirstContactBootstrapCategoryDefinition> categories, out string error))
            {
                throw new InvalidOperationException(
                    $"[FirstContactTranslationMode] Bootstrap CATEGORY configuration is invalid: {error}");
            }

            _bootstrapSession = new FirstContactBootstrapSession(
                categories,
                GetBootstrapRequiredTraceCount());
        }

        private IEnumerator PrepareBootstrapCategoryDescriptorsRoutine()
        {
            IReadOnlyList<FirstContactBootstrapCategoryState> categories = _bootstrapSession?.Categories;
            if (_embeddingService == null || categories == null || categories.Count == 0)
            {
                yield break;
            }

            var descriptors = new string[categories.Count];
            for (int i = 0; i < categories.Count; i++)
            {
                descriptors[i] = categories[i].LocalizedDescriptorText;
            }

            IReadOnlyList<EmbeddingResult> results = null;
            yield return _embeddingService.EmbedLabels(descriptors, value => results = value);

            for (int i = 0; i < categories.Count; i++)
            {
                EmbeddingResult result = results != null && i < results.Count ? results[i] : default;
                if (result.IsValid)
                {
                    categories[i].SetDescriptorEmbedding(result.Vector);
                    continue;
                }

                Debug.LogWarning(
                    $"[FirstContactTranslationMode] Bootstrap category descriptor embedding failed. " +
                    $"category={categories[i].Id} locale='{L10n.CurrentLocale}' " +
                    $"descriptor='{categories[i].LocalizedDescriptorText}' error='{result.Error}'",
                    this);
            }
        }

        private int GetBootstrapRequiredTraceCount()
        {
            return Math.Max(2, GetSemanticSettings().bootstrapMinTraceCount);
        }

        private FirstContactBootstrapCategoryState GetActiveBootstrapCategory()
        {
            return _bootstrapSession?.ActiveCategory;
        }

        private FirstContactBootstrapCategoryState GetCurrentProbeCategory()
        {
            return _isPreflightTutorial ? null : GetActiveBootstrapCategory();
        }

        private IEnumerator StartBootstrapProbeSequenceRoutine()
        {
            FirstContactBootstrapCategoryState category = GetActiveBootstrapCategory();
            if (category == null)
            {
                ChangeState(FirstContactModeState.BootstrapComplete);
                _terminalPresenter?.ShowBootstrapComplete(instant: false);
                if (_encounterDirector != null)
                {
                    yield return _encounterDirector.PlayBootstrapCalibratedRoutine();
                }

                yield return WaitForTerminalContinueRoutine();

                FirstContactTranslationResult translation = _encounterDirector != null
                    ? _encounterDirector.BuildTranslationDemonstration()
                    : default;
                if (translation.HasTranslation)
                {
                    ChangeState(FirstContactModeState.TranslatorDemonstration);
                    yield return _encounterDirector.PlayIncomingTranslationSignalRoutine(translation);
                    _terminalPresenter?.ShowTranslationDemonstration(
                        translation.RawSignal,
                        translation.RenderedMeaning,
                        translation.UnknownSegmentCount,
                        instant: false);
                    yield return _encounterDirector.PlayTranslationSucceededRoutine();
                    yield return WaitForTerminalContinueRoutine();
                }

                ChangeState(FirstContactModeState.Completed);
                yield break;
            }

            ChangeState(FirstContactModeState.BootstrapProbeSequence);
            DisableTerminalChoices();
            _context?.Drawing?.SetInteractionLocked(true);
            _context?.Drawing?.ClearRecognitionLabel();
            _context?.Drawing?.ClearInstructionLabel();
            _context?.Camera?.SetMode(CameraMode.TerminalZoom);
            _terminalPresenter?.ShowBootstrapProbeSequence(
                category.Id,
                category.DisplayName,
                category.TraceCount,
                category.RequiredTraceCount,
                category.IsStable,
                _selectedTerminalChoiceIndex,
                instant: false);
            if (_encounterDirector != null)
            {
                yield return _encounterDirector.PlayCategoryOpenedRoutine(
                    category.Id,
                    category.DisplayName,
                    category.TraceCount);
            }

            EnableBootstrapProbeChoice();
        }

        private IEnumerator BeginDrawingRoutine(bool preserveCanvas = false)
        {
            _workingProbe.Reset(_isPreflightTutorial
                ? FirstContactCardSource.PreflightProbe
                : FirstContactCardSource.BootstrapProbe);
            if (!preserveCanvas)
            {
                _currentProbeLabelInput = string.Empty;
            }

            DisableTerminalChoices();
            _context?.Drawing?.EnsureRuntimeEnabled();
            _context?.Drawing?.SetInteractionLocked(true);
            if (!preserveCanvas)
            {
                _context?.Drawing?.ClearCanvas();
            }

            _context?.Drawing?.ClearRecognitionLabel();
            _context?.Drawing?.ShowInstructionLabel(GetDrawingInstructionText());
            _context?.Camera?.SetMode(CameraMode.TabletView);
            ChangeState(FirstContactModeState.DrawingBootstrapProbe);

            FirstContactBootstrapCategoryState category = GetCurrentProbeCategory();
            if (_encounterDirector != null)
            {
                if (_isPreflightTutorial)
                {
                    yield return _encounterDirector.PlayPreflightDrawingRoutine();
                }
                else
                {
                    yield return _encounterDirector.PlayDrawingOpenedRoutine(
                        category?.Id ?? string.Empty,
                        category?.DisplayName ?? string.Empty);
                }
            }

            _context?.Drawing?.SetInteractionLocked(false);
            _routine = null;
        }

        private void SubmitDrawing()
        {
            if (_modeState != FirstContactModeState.DrawingBootstrapProbe)
            {
                return;
            }

            HideGuidanceLine();
            if (_context?.Drawing == null || !_context.Drawing.HasVisibleDrawing)
            {
                StopActiveRoutine();
                _routine = StartCoroutine(ShowContentRedrawPromptRoutine(
                    "DRAW SOMETHING",
                    "first_contact.doctor_hwang.probe_blank"));
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
                HideGuidanceLine(force: true);
                _currentProbeLabelInput = labelInput ?? string.Empty;
                _terminalProbeLabelStatus = L10n.T("first_contact.terminal.line.label_required", "LABEL REQUIRED");
                RefreshTerminalProbeLabelEntry(instant: false);
                return;
            }

            _workingProbe.TrySetSubmittedLabel(canonicalLabel, displayLabel);
            EndTerminalProbeLabelInput();
            _currentProbeLabelInput = displayLabel;
            _terminalProbeLabelStatus = string.Empty;
            HideGuidanceLine(force: true);
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

            displayLabel = FirstContactProbeProcessor.NormalizePlayerLabelText(input);
            canonicalLabel = FirstContactProbeProcessor.NormalizeProbeLabel(displayLabel);
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
                if (FirstContactProbeFeedback.TryGetBlankDrawingRedrawPrompt(
                        captureError,
                        out string blankRedrawPrompt,
                        out string blankGuidanceLineKey))
                {
                    yield return ShowContentRedrawPromptRoutine(
                        blankRedrawPrompt,
                        blankGuidanceLineKey);
                    yield break;
                }

                if (_isPreflightTutorial)
                {
                    yield return CompletePreflightRoutine(
                        technicalOverride: true,
                        diagnostic: $"Drawing capture failed during preflight: {captureError}");
                    _routine = null;
                    yield break;
                }

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
            if (_encounterDirector != null)
            {
                yield return _encounterDirector.PlayLabelOpenedRoutine();
            }

            BeginTerminalProbeLabelInput();
            _routine = null;
        }

        private IEnumerator AnalyzeDrawingRoutine()
        {
            ChangeState(FirstContactModeState.AnalyzingDrawing);
            DisableTerminalChoices();
            _context?.Camera?.SetMode(CameraMode.TerminalZoom);
            _terminalPresenter?.ShowProbeDispatching(
                _workingProbe.Source,
                string.Empty,
                _workingProbe.DisplayLabel,
                GetPendingProbeDispatchCategory(),
                _workingProbe.Texture,
                BuildProbeDispatchSignalProfile(),
                BuildProbeDispatchStreamSeed());
            FirstContactPresentationSettings presentation = GetPresentationSettings();
            float startTime = Time.time;

            FirstContactProbeLabelResult labelResult = null;
            yield return PreparePendingProbeLabelRoutine(result => labelResult = result);
            if (labelResult == null || !labelResult.IsSuccess)
            {
                if (_isPreflightTutorial)
                {
                    yield return CompletePreflightRoutine(
                        technicalOverride: true,
                        diagnostic: $"Probe label analysis failed during preflight: {labelResult?.Error ?? "No result."}");
                    _routine = null;
                    yield break;
                }

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
                if (FirstContactProbeFeedback.TryGetValidationErrorRedrawPrompt(
                        validation,
                        out string errorRedrawPrompt,
                        out string errorGuidanceLineKey))
                {
                    ShowContentRedrawPrompt(errorRedrawPrompt, errorGuidanceLineKey);
                    _routine = null;
                    yield break;
                }

                if (_isPreflightTutorial)
                {
                    yield return CompletePreflightRoutine(
                        technicalOverride: true,
                        diagnostic: $"Probe validator failed during preflight: {validationError}");
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

            if (FirstContactProbeFeedback.TryGetContentRedrawPrompt(
                    validation,
                    GetVlmSettings(),
                    out string redrawPrompt,
                    out string guidanceLineKey))
            {
                ShowContentRedrawPrompt(redrawPrompt, guidanceLineKey);
                _routine = null;
                yield break;
            }

            if (validation.IsLabelMismatch)
            {
                ShowProbeLabelUnsuitablePrompt(new FirstContactProbeLabelResult
                {
                    NormalizedLabel = _workingProbe.DisplayLabel,
                    LabelIssue = FirstContactProbeLabelIssue.LabelMismatch,
                    IsSuitable = false,
                    Reason = "The visual sample and probe label disagree."
                });
                _routine = null;
                yield break;
            }

            if (_isPreflightTutorial)
            {
                yield return CompletePreflightRoutine(technicalOverride: false);
                _routine = null;
                yield break;
            }

            if (presentation.labelRevealDelay > 0f)
            {
                _terminalPresenter?.ShowProbeDispatchAccepted(
                    _workingProbe.Source,
                    string.Empty,
                    _workingProbe.DisplayLabel,
                    GetPendingProbeDispatchCategory(),
                    _workingProbe.Texture,
                    BuildProbeDispatchSignalProfile(),
                    BuildProbeDispatchStreamSeed(),
                    instant: false);
                yield return new WaitForSeconds(presentation.labelRevealDelay);
            }

            if (_encounterDirector != null)
            {
                yield return _encounterDirector.PlayProbeTransmissionRoutine(
                    _workingProbe.Texture,
                    GetActiveBootstrapCategory()?.TraceCount ?? 0);
            }

            yield return ConfirmPendingDrawingRoutine();
            _routine = null;
        }

        private IEnumerator CompletePreflightRoutine(
            bool technicalOverride,
            string diagnostic = null)
        {
            if (!string.IsNullOrWhiteSpace(diagnostic))
            {
                Debug.LogWarning($"[FirstContactTranslationMode] {diagnostic}", this);
            }

            DisableTerminalChoices();
            EndTerminalProbeLabelInput();
            _context?.Drawing?.SetInteractionLocked(true);
            _context?.Drawing?.ClearRecognitionLabel();
            _context?.Drawing?.ClearInstructionLabel();
            _context?.Camera?.SetMode(CameraMode.TerminalZoom);
            ChangeState(FirstContactModeState.PreflightComplete);

            _terminalPresenter?.ShowPreflightComplete(
                _workingProbe.DisplayLabel,
                technicalOverride,
                instant: false);

            if (_encounterDirector != null)
            {
                yield return _encounterDirector.PlayPreflightPassedRoutine();
            }

            _tabletControls?.ClearTutorialHighlight();
            _isPreflightTutorial = false;
            _currentProbeLabelInput = string.Empty;
            _terminalProbeLabelStatus = string.Empty;
            _workingProbe.Reset(FirstContactCardSource.BootstrapProbe);

            if (_encounterDirector != null)
            {
                yield return _encounterDirector.PlayDelegationArrivalRoutine(_startWithIntro);
            }

            yield return StartBootstrapProbeSequenceRoutine();
        }

        private IEnumerator CapturePendingDrawingWithRetries(Action<bool, string> onComplete)
        {
            if (_probeCaptureService == null)
            {
                onComplete?.Invoke(false, "Probe capture service is unavailable.");
                yield break;
            }

            yield return _probeCaptureService.Capture(_context?.Drawing, result =>
            {
                bool applied = _workingProbe.TryApplyCapture(result);
                onComplete?.Invoke(applied, result.Error);
            });
        }

        private IEnumerator ValidatePendingProbeWithRetries(
            Action<FirstContactProbeValidationResult, string> onComplete)
        {
            if (_probeProcessor == null)
            {
                onComplete?.Invoke(
                    FirstContactProbeValidationResult.Failed("Probe processor is unavailable."),
                    "Probe processor is unavailable.");
                yield break;
            }

            yield return _probeProcessor.ValidateWithRetries(
                _workingProbe.CreateDraft(),
                onComplete);
        }

        private IEnumerator EvaluateBootstrapCategoryFitRoutine(
            SemanticCardRecord card,
            FirstContactBootstrapCategoryState category,
            Action<FirstContactBootstrapCategoryFitResult> onComplete)
        {
            if (_probeProcessor == null)
            {
                onComplete?.Invoke(FirstContactBootstrapCategoryFitResult.Failed(
                    "Probe processor is unavailable."));
                yield break;
            }

            yield return _probeProcessor.EvaluateCategoryFit(
                card,
                category,
                L10n.CurrentLocale,
                onComplete);
        }

        private IEnumerator PreparePendingProbeLabelRoutine(
            Action<FirstContactProbeLabelResult> onComplete)
        {
            if (_probeProcessor == null)
            {
                onComplete?.Invoke(FirstContactProbeLabelResult.Failed(
                    "Probe processor is unavailable."));
                yield break;
            }

            yield return _probeProcessor.PrepareLabel(
                _workingProbe.Texture,
                _workingProbe.PreferredLabel,
                L10n.CurrentLocale,
                result =>
                {
                    _workingProbe.TryApplyLabelAnalysis(result);
                    onComplete?.Invoke(result);
                });
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
            ChangeState(FirstContactModeState.StoringBootstrapProbe);
            _context?.Camera?.SetMode(CameraMode.TerminalZoom);

            EmbeddingResult embedding = default;
            string authoritativeLabel = _workingProbe.PreferredLabel;
            yield return _embeddingService.EmbedLabel(authoritativeLabel, result => embedding = result);
            if (!embedding.IsValid)
            {
                ShowTechnicalFailurePrompt(
                    FirstContactTechnicalRetryAction.ConfirmDrawing,
                    "signal_encoding_failed",
                    "SIGNAL ENCODING FAILED",
                    $"Embedding failed: {embedding.Error}");
                _routine = null;
                yield break;
            }

            FirstContactBootstrapCategoryState category = GetActiveBootstrapCategory();
            var card = new SemanticCardRecord
            {
                Texture = _workingProbe.Texture,
                PngBytes = _workingProbe.PngBytes,
                OriginalLabel = authoritativeLabel,
                NormalizedLabel = _workingProbe.NormalizedLabel,
                Embedding = embedding.Vector,
                Source = _workingProbe.Source,
                BootstrapCategoryId = category?.Id ?? string.Empty,
                BootstrapCategoryDisplayName = category?.DisplayName ?? string.Empty,
                ProbeIndex = _session != null ? _session.ProbeIndex++ : 0
            };

            _semanticMemory.TryCreateWaveformProfile(card, _runtimeWaveformSessionSeed, out var waveform);
            card.WaveformProfile = waveform;
            SemanticCardRecord duplicateCard = null;
            yield return FindBootstrapDuplicateCardRoutine(card, result => duplicateCard = result);
            if (duplicateCard != null)
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
            yield return EvaluateBootstrapCategoryFitRoutine(card, category, result => categoryFitResult = result);
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

            card.BootstrapCategoryEvaluated = true;
            card.BootstrapCategoryAccepted = categoryFitResult.FitsCategory;

            IReadOnlyList<FirstContactClusterTransitionSnapshot> beforeClusterStates =
                FirstContactClusterFormationTracker.Capture(_semanticMemory?.Clusters);
            _semanticMemory.AddCard(card);
            _session?.RecentCards.Add(card);
            SemanticClusterRecord cluster = _semanticMemory.FindCluster(card.ClusterId);
            FirstContactClusterFormationEvent clusterFormation =
                FirstContactClusterFormationTracker.BuildFormation(
                card,
                cluster,
                beforeClusterStates,
                _semanticMemory?.LastFormationEdges);
            yield return StoreBootstrapProbeCardRoutine(card, cluster, clusterFormation, categoryFitResult);
            _routine = null;
        }

        private IEnumerator StoreBootstrapDuplicateProbeCardRoutine(
            SemanticCardRecord card,
            SemanticCardRecord duplicateCard)
        {
            FirstContactBootstrapCategoryState category = GetActiveBootstrapCategory();
            if (category == null)
            {
                yield break;
            }

            if (!_hasShownBootstrapDuplicateGuidanceLine)
            {
                _hasShownBootstrapDuplicateGuidanceLine = true;
                ShowDoctorHwangLine("first_contact.doctor_hwang.bootstrap_duplicate_probe");
            }

            int traceCount = category.TraceCount;
            bool stable = category.IsStable;
            FirstContactSemanticMapSnapshot mapSnapshot = BuildBootstrapSemanticMapSnapshot(
                card,
                category,
                includeActiveCard: true);
            _terminalPresenter?.ShowBootstrapSignalCapture(
                card,
                category.Id,
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
            FirstContactBootstrapCategoryState category = GetActiveBootstrapCategory();
            if (category == null)
            {
                yield break;
            }

            FirstContactBootstrapProbeFit fit = category.EvaluateCandidate(card, _embeddingService);
            int previousTraceCount = category.TraceCount;
            bool wasStable = category.IsStable;
            FirstContactSemanticMapSnapshot beforeMapSnapshot = BuildBootstrapSemanticMapSnapshot(
                card,
                category,
                includeActiveCard: false);
            bool categoryAccepted = categoryFitResult != null && categoryFitResult.FitsCategory;
            bool accepted = category.RecordProbe(card, fit, categoryAccepted);
            RefreshSessionStableClusters();
            bool stable = category.IsStable;
            if (GetDebugSettings().logSimilarityScores)
            {
                Debug.Log(
                    $"[FirstContactTranslationMode] Bootstrap category={category.Id} " +
                    $"traces={category.TraceCount}/{category.RequiredTraceCount} " +
                    $"accepted={accepted} categoryFit={fit.CategoryDescriptorFit:0.000} " +
                    $"categoryJudge={categoryAccepted} " +
                    $"decision='{categoryFitResult?.Decision ?? string.Empty}' " +
                    $"reason='{categoryFitResult?.Reason ?? string.Empty}' " +
                    $"stable={stable}");
            }

            if (!accepted)
            {
                string categoryGuidanceLineKey =
                    FirstContactProbeFeedback.ResolveCategoryGuidanceLine(categoryFitResult);
                ShowDoctorHwangLine(
                    categoryGuidanceLineKey,
                    L10n.Arg("category", category.LocalizedDisplayName));
            }

            FirstContactSemanticMapSnapshot mapSnapshot = BuildBootstrapSemanticMapSnapshot(
                card,
                category,
                includeActiveCard: true);
            FirstContactClusterFormationEvent semanticFormation = FirstContactBootstrapMapBuilder.ShouldShowFormation(
                category,
                cluster,
                clusterFormation)
                ? clusterFormation
                : default;
            bool requiresMeaningAssignment =
                semanticFormation.BecameStable &&
                semanticFormation.IsStable &&
                cluster?.RequiresMeaningAssignment == true;
            _terminalPresenter?.ShowBootstrapSignalCapture(
                card,
                category.Id,
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
                requiresMeaningAssignment,
                instant: false);
            if (_encounterDirector != null)
            {
                yield return _encounterDirector.PlayTraceRecordedRoutine(
                    category.Id,
                    category.DisplayName,
                    category.TraceCount,
                    category.RequiredTraceCount,
                    accepted);
            }

            if (requiresMeaningAssignment)
            {
                ShowDoctorHwangLine("first_contact.doctor_hwang.pattern_meaning_requested");
            }

            yield return WaitForTerminalContinueRoutine();

            if (requiresMeaningAssignment)
            {
                yield return AssignPatternMeaningRoutine(card, category, cluster);
            }

            if (!stable)
            {
                yield return StartBootstrapProbeSequenceRoutine();
                yield break;
            }

            _terminalPresenter?.ShowBootstrapClusterTrace(
                category.Id,
                category.DisplayName,
                category.TraceCount,
                category.RequiredTraceCount,
                mapSnapshot,
                GetSemanticSettings(),
                instant: false);
            if (_encounterDirector != null)
            {
                yield return _encounterDirector.PlayCategoryCalibratedRoutine(
                    category.Id,
                    category.DisplayName);
            }

            yield return WaitForTerminalContinueRoutine();

            _bootstrapSession?.AdvanceCategory();
            yield return StartBootstrapProbeSequenceRoutine();
        }

        private IEnumerator FindBootstrapDuplicateCardRoutine(
            SemanticCardRecord card,
            Action<SemanticCardRecord> onComplete)
        {
            FirstContactSemanticSettings settings = GetSemanticSettings();
            if (FirstContactProbeDuplicateDetector.TryFindDuplicate(
                    card,
                    _session?.RecentCards,
                    _embeddingService,
                    settings,
                    out SemanticCardRecord certainDuplicate,
                    out FirstContactProbeDuplicateDetector.MatchEvidence evidence))
            {
                if (GetDebugSettings().logSimilarityScores)
                {
                    Debug.Log(
                        $"[FirstContactTranslationMode] Duplicate confirmed without LLM. " +
                        $"kind={evidence.Kind} similarity={evidence.SemanticSimilarity:0.000} " +
                        $"candidate='{card?.OriginalLabel}' recorded='{certainDuplicate?.OriginalLabel}'.",
                        this);
                }

                onComplete?.Invoke(certainDuplicate);
                yield break;
            }

            IReadOnlyList<FirstContactProbeDuplicateDetector.ReviewCandidate> reviewCandidates =
                FirstContactProbeDuplicateDetector.FindReviewCandidates(
                    card,
                    _session?.RecentCards,
                    _embeddingService,
                    settings);
            if (_probeProcessor == null || reviewCandidates.Count == 0)
            {
                onComplete?.Invoke(null);
                yield break;
            }

            for (int i = 0; i < reviewCandidates.Count; i++)
            {
                FirstContactProbeDuplicateDetector.ReviewCandidate candidate = reviewCandidates[i];
                FirstContactSemanticDuplicateReviewResult review = null;
                yield return _probeProcessor.EvaluateSemanticDuplicate(
                    card,
                    candidate.Card,
                    result => review = result);

                if (review?.ConfirmsDuplicate == true)
                {
                    if (GetDebugSettings().logSimilarityScores)
                    {
                        Debug.Log(
                            $"[FirstContactTranslationMode] Duplicate confirmed by gray-zone review. " +
                            $"similarity={candidate.SemanticSimilarity:0.000} " +
                            $"candidate='{card?.OriginalLabel}' recorded='{candidate.Card?.OriginalLabel}' " +
                            $"reason='{review.Reason}'.",
                            this);
                    }

                    onComplete?.Invoke(candidate.Card);
                    yield break;
                }

                if (review != null && !review.IsSuccess)
                {
                    Debug.LogWarning(
                        $"[FirstContactTranslationMode] Optional semantic duplicate review failed: {review.Error}",
                        this);
                }
            }

            onComplete?.Invoke(null);
        }

        private void RedrawPending()
        {
            ClearTechnicalFailureState();
            HideGuidanceLine();
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
            if (_isPreflightTutorial)
            {
                return string.Empty;
            }

            return GetCurrentProbeCategory()?.DisplayName ?? string.Empty;
        }

        private BrainwaveSemanticProfile BuildProbeDispatchSignalProfile()
        {
            if (_semanticMemory == null)
            {
                return BrainwaveSemanticProfile.Invalid;
            }

            string label = _workingProbe.PreferredLabel;
            if (string.IsNullOrWhiteSpace(label))
            {
                return BrainwaveSemanticProfile.Invalid;
            }

            return _semanticMemory.TryCreateTokenWaveformProfile(
                label,
                Mathf.Max(1, ((_session?.ProbeIndex ?? 0) * 37) + 1),
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
                hash = (hash * 31) + (int)_workingProbe.Source;
                string label = _workingProbe.PreferredLabel;
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
            FirstContactBootstrapCategoryState category = GetCurrentProbeCategory();
            if (_isPreflightTutorial)
            {
                _terminalPresenter?.ShowPreflightReady(instant: false);
            }
            else
            {
                _terminalPresenter?.ShowBootstrapProbeChannelOpen(
                    category?.Id ?? string.Empty,
                    category?.DisplayName ?? string.Empty,
                    category?.TraceCount ?? 0,
                    category?.RequiredTraceCount ?? GetBootstrapRequiredTraceCount(),
                    instant: false);
            }

            float holdSeconds = GetPresentationSettings().tabletLinkOpenHoldSeconds;
            if (holdSeconds > 0f)
            {
                yield return new WaitForSeconds(holdSeconds);
            }

            yield return BeginDrawingRoutine(preserveCanvas: true);
            _routine = null;
        }

        private void EnableBootstrapProbeChoice()
        {
            _selectedTerminalChoiceIndex = 0;
            _terminalChoiceInputEnabled = true;
            _terminalChoiceMode = FirstContactTerminalChoiceMode.BootstrapProbe;
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
            _terminalProbeLabelStatus = string.Empty;
            StartTerminalProbeLabelSession();
            RefreshTerminalProbeLabelEntry(instant: false);
        }

        private void StartTerminalProbeLabelSession()
        {
            _terminalTextEntry?.Begin(
                _currentProbeLabelInput,
                MaxProbeLabelLength,
                OnTerminalProbeLabelChanged,
                SubmitProbeLabel,
                CancelTerminalProbeLabelInput);
        }

        private IEnumerator AssignPatternMeaningRoutine(
            SemanticCardRecord card,
            FirstContactBootstrapCategoryState category,
            SemanticClusterRecord cluster)
        {
            if (cluster == null || !cluster.RequiresMeaningAssignment)
            {
                yield break;
            }

            _pendingMeaningCluster = cluster;
            _pendingMeaningMapSnapshot = BuildBootstrapSemanticMapSnapshot(
                card,
                category,
                includeActiveCard: true);
            _currentPatternMeaningInput = string.Empty;
            _terminalPatternMeaningStatus = string.Empty;
            _patternMeaningSubmitted = false;
            ChangeState(FirstContactModeState.AssigningPatternMeaning);
            BeginTerminalPatternMeaningInput();

            while (!_patternMeaningSubmitted &&
                   _modeState == FirstContactModeState.AssigningPatternMeaning)
            {
                yield return null;
            }

            if (!_patternMeaningSubmitted)
            {
                yield break;
            }

            EndTerminalProbeLabelInput();
            RefreshSessionStableClusters();
            FirstContactSemanticMapSnapshot registeredMap = BuildBootstrapSemanticMapSnapshot(
                card,
                category,
                includeActiveCard: true);
            ChangeState(FirstContactModeState.PatternMeaningRegistered);
            _terminalPresenter?.ShowPatternMeaningRegistered(
                cluster,
                registeredMap,
                GetSemanticSettings(),
                instant: false);
            yield return WaitForTerminalContinueRoutine();
            ClearPendingPatternMeaning();
        }

        private void BeginTerminalPatternMeaningInput()
        {
            DisableTerminalChoices();
            StartTerminalPatternMeaningSession();
            RefreshTerminalPatternMeaningEntry(instant: false);
        }

        private void StartTerminalPatternMeaningSession()
        {
            _terminalTextEntry?.Begin(
                _currentPatternMeaningInput,
                MaxPatternMeaningLength,
                OnTerminalPatternMeaningChanged,
                SubmitPatternMeaning,
                CancelTerminalPatternMeaningInput);
        }

        private void RefreshTerminalPatternMeaningEntry(bool instant)
        {
            _terminalPresenter?.ShowPatternMeaningEntry(
                _pendingMeaningCluster,
                _pendingMeaningMapSnapshot,
                GetSemanticSettings(),
                _terminalTextEntry?.RenderedValue ?? _currentPatternMeaningInput,
                _terminalPatternMeaningStatus,
                instant);

            if (_terminalTextEntry?.IsActive == true && !instant)
            {
                _terminalTextEntry.AttachTerminalInput(
                    FirstContactTerminalPresenter.PatternMeaningInputPrefix,
                    visible: false);
            }
        }

        private void SubmitPatternMeaning()
        {
            SubmitPatternMeaning(
                _context?.TerminalDisplay != null && _context.TerminalDisplay.IsTextInputActive
                    ? _context.TerminalDisplay.TextInputValue
                    : _currentPatternMeaningInput);
        }

        private void SubmitPatternMeaning(string meaningInput)
        {
            if (_modeState != FirstContactModeState.AssigningPatternMeaning)
            {
                return;
            }

            string meaning = FirstContactProbeProcessor.NormalizePlayerLabelText(meaningInput);
            if (string.IsNullOrWhiteSpace(meaning))
            {
                _currentPatternMeaningInput = meaningInput ?? string.Empty;
                _terminalPatternMeaningStatus = L10n.T(
                    "first_contact.terminal.line.meaning_required",
                    "MEANING REQUIRED");
                RefreshTerminalPatternMeaningEntry(instant: false);
                return;
            }

            if (_semanticMemory?.TryAssignMeaning(_pendingMeaningCluster?.Id, meaning) != true)
            {
                _terminalPatternMeaningStatus = L10n.T(
                    "first_contact.terminal.status.pattern_unavailable",
                    "PATTERN UNAVAILABLE");
                RefreshTerminalPatternMeaningEntry(instant: false);
                return;
            }

            _currentPatternMeaningInput = meaning;
            _terminalPatternMeaningStatus = string.Empty;
            EndTerminalProbeLabelInput();
            _patternMeaningSubmitted = true;
        }

        private void OnTerminalPatternMeaningChanged(string value)
        {
            HideGuidanceLine();
            _currentPatternMeaningInput = value ?? string.Empty;
            if (!string.IsNullOrEmpty(_terminalPatternMeaningStatus))
            {
                _terminalPatternMeaningStatus = string.Empty;
            }

            RefreshTerminalPatternMeaningEntry(instant: true);
        }

        private void CancelTerminalPatternMeaningInput()
        {
            _currentPatternMeaningInput = string.Empty;
            _terminalPatternMeaningStatus = L10n.T(
                "first_contact.terminal.line.meaning_required",
                "MEANING REQUIRED");
            StartTerminalPatternMeaningSession();
            RefreshTerminalPatternMeaningEntry(instant: false);
        }

        private void ClearPendingPatternMeaning()
        {
            _pendingMeaningCluster = null;
            _pendingMeaningMapSnapshot = null;
            _currentPatternMeaningInput = string.Empty;
            _terminalPatternMeaningStatus = string.Empty;
            _patternMeaningSubmitted = false;
        }

        private void RefreshTerminalProbeLabelEntry(bool instant)
        {
            _terminalPresenter?.ShowProbeLabelEntry(
                _workingProbe.Source,
                string.Empty,
                _workingProbe.Texture,
                _terminalTextEntry?.RenderedValue ?? _currentProbeLabelInput,
                _terminalProbeLabelStatus,
                instant);

            if (_terminalTextEntry?.IsActive == true && !instant)
            {
                _terminalTextEntry.AttachTerminalInput(
                    FirstContactTerminalPresenter.ProbeLabelInputPrefix,
                    visible: false);
            }
        }

        private bool HandleTerminalTextInput()
        {
            return (_modeState == FirstContactModeState.ReviewingLabel ||
                    _modeState == FirstContactModeState.AssigningPatternMeaning) &&
                   _terminalTextEntry?.Tick() == true;
        }

        private void OnTerminalProbeLabelChanged(string value)
        {
            _currentProbeLabelInput = value ?? string.Empty;
            RefreshTerminalProbeLabelEntry(instant: true);
        }

        private void CancelTerminalProbeLabelInput()
        {
            _terminalProbeLabelStatus = string.Empty;
            RedrawPending();
        }

        private void EndTerminalProbeLabelInput()
        {
            _terminalTextEntry?.End();
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

            if (TerminalKeyboardInput.WasPressed(KeyCode.UpArrow) ||
                TerminalKeyboardInput.WasPressed(KeyCode.W))
            {
                MoveTerminalChoiceSelection(-1, choiceCount);
                return;
            }

            if (TerminalKeyboardInput.WasPressed(KeyCode.DownArrow) ||
                TerminalKeyboardInput.WasPressed(KeyCode.S))
            {
                MoveTerminalChoiceSelection(1, choiceCount);
                return;
            }

            if (TerminalKeyboardInput.WasSubmitPressedThisFrame())
            {
                SelectTerminalChoice(_selectedTerminalChoiceIndex);
                return;
            }
        }

        private void HandleDrawingSubmitInput()
        {
            if (_modeState != FirstContactModeState.DrawingBootstrapProbe)
            {
                return;
            }

            if (TerminalKeyboardInput.WasSubmitPressedThisFrame())
            {
                TrySubmitDrawingFromInput();
            }
        }

        private bool TrySubmitDrawingFromInput()
        {
            if (_modeState != FirstContactModeState.DrawingBootstrapProbe)
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
                FirstContactTerminalChoiceMode.RejectedInput => 1,
                FirstContactTerminalChoiceMode.TechnicalRetry => 2,
                FirstContactTerminalChoiceMode.BootstrapProbe => 1,
                FirstContactTerminalChoiceMode.Continue => 1,
                _ => 0
            };
        }

        private void SelectTerminalChoice(int choiceIndex)
        {
            HideGuidanceLine();
            switch (_terminalChoiceMode)
            {
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
            _routine = StartCoroutine(ConfirmTerminalChoiceRoutine());
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
                case FirstContactTerminalChoiceMode.RejectedInput:
                    _terminalPresenter?.ShowInputRejected(_currentRejectedInputReason, _selectedTerminalChoiceIndex);
                    break;
                case FirstContactTerminalChoiceMode.TechnicalRetry:
                    _terminalPresenter?.ShowAnalysisError(_technicalFailureStatus, _selectedTerminalChoiceIndex);
                    break;
                case FirstContactTerminalChoiceMode.BootstrapProbe:
            FirstContactBootstrapCategoryState category = GetActiveBootstrapCategory();
                    if (category != null)
                    {
                        _terminalPresenter?.ShowBootstrapProbeSequence(
                            category.Id,
                            category.DisplayName,
                            category.TraceCount,
                            category.RequiredTraceCount,
                            category.IsStable,
                            _selectedTerminalChoiceIndex);
                    }
                    break;
            }
        }

        private IEnumerator ConfirmTerminalChoiceRoutine()
        {
            FirstContactBootstrapCategoryState category = GetActiveBootstrapCategory();
            _terminalPresenter?.ShowBootstrapProbeChannelOpen(
                category?.Id ?? string.Empty,
                category?.DisplayName ?? string.Empty,
                category?.TraceCount ?? 0,
                category?.RequiredTraceCount ?? GetBootstrapRequiredTraceCount(),
                instant: false);
            float linkHoldSeconds = GetPresentationSettings().tabletLinkOpenHoldSeconds;
            yield return WaitForTerminalPresentationRoutine(linkHoldSeconds, true);

            yield return BeginDrawingRoutine();
            _routine = null;
        }

        private void ShowContentRedrawPrompt(string prompt, string guidanceLineKey = null)
        {
            string safePrompt = string.IsNullOrWhiteSpace(prompt) ? "DRAW ONE OBJECT" : prompt.Trim();
            string focusKey = FirstContactProbeFeedback.GetRedrawPromptLocalizationKey(safePrompt);
            PrepareContentRedrawPrompt(safePrompt);
            CompleteContentRedrawPrompt(guidanceLineKey);
            UiCopyTrace.Focus("first_contact.terminal.input_rejected", "terminal", focusKey, "validation");
        }

        private IEnumerator ShowContentRedrawPromptRoutine(string prompt, string guidanceLineKey = null)
        {
            string safePrompt = string.IsNullOrWhiteSpace(prompt) ? "DRAW ONE OBJECT" : prompt.Trim();
            string focusKey = FirstContactProbeFeedback.GetRedrawPromptLocalizationKey(safePrompt);
            PrepareContentRedrawPrompt(safePrompt);
            yield return WaitForCameraTransitionRoutine();
            CompleteContentRedrawPrompt(guidanceLineKey);
            UiCopyTrace.Focus("first_contact.terminal.input_rejected", "terminal", focusKey, "validation");
            _routine = null;
        }

        private void PrepareContentRedrawPrompt(string safePrompt)
        {
            _currentRejectedInputReason = FirstContactProbeFeedback.LocalizeRedrawPrompt(safePrompt);
            _context?.Drawing?.SetInteractionLocked(true);
            _context?.Drawing?.ClearRecognitionLabel();
            _context?.Drawing?.ClearInstructionLabel();
            _context?.Camera?.SetMode(CameraMode.TerminalZoom);
        }

        private void CompleteContentRedrawPrompt(string guidanceLineKey)
        {
            ChangeState(FirstContactModeState.ReviewingRejectedInput);
            EnableRejectedInputChoice(_currentRejectedInputReason);
            ShowRepeatableDoctorHwangLine(guidanceLineKey);
        }

        private void ShowProbeLabelUnsuitablePrompt(FirstContactProbeLabelResult result)
        {
            _currentProbeLabelInput = string.IsNullOrWhiteSpace(_workingProbe.DisplayLabel)
                ? _currentProbeLabelInput
                : _workingProbe.DisplayLabel;
            StartTerminalProbeLabelSession();
            FirstContactProbeLabelFeedback feedback = FirstContactProbeFeedback.ResolveLabelIssue(
                result?.LabelIssue ?? FirstContactProbeLabelIssue.ActionOrAbstract);
            _terminalProbeLabelStatus = L10n.T(feedback.StatusKey, feedback.StatusFallback);
            _context?.Drawing?.SetInteractionLocked(true);
            _context?.Drawing?.ClearRecognitionLabel();
            _context?.Drawing?.ClearInstructionLabel();
            _context?.Camera?.SetMode(CameraMode.TerminalZoom);
            ChangeState(FirstContactModeState.ReviewingLabel);
            RefreshTerminalProbeLabelEntry(instant: false);
            UiCopyTrace.Focus(
                "first_contact.terminal.probe_review",
                "terminal",
                feedback.StatusKey,
                "validation");
            ShowRepeatableDoctorHwangLine(feedback.GuidanceLineKey);

            if (GetDebugSettings().logSimilarityScores)
            {
                Debug.Log(
                    "[FirstContactTranslationMode] Probe label rejected as unsuitable. " +
                    $"OriginalLabel='{_workingProbe.OriginalLabel}' NormalizedLabel='{result?.NormalizedLabel}' " +
                    $"HasClassificationClaim={result?.HasClassificationClaim} " +
                    $"ClassificationClaimText='{result?.ClassificationClaimText}' " +
                    $"NeutralSubjectLabel='{result?.NeutralSubjectLabel}' " +
                    $"Reason='{result?.Reason}'",
                    this);
            }
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
            UiCopyTrace.Focus(
                "first_contact.terminal.analysis_error",
                "terminal",
                $"first_contact.terminal.status.{statusKey}",
                "error");
        }

        private void ClearTechnicalFailureState()
        {
            _technicalRetryAction = FirstContactTechnicalRetryAction.None;
            _technicalFailureStatus = string.Empty;
        }

        private string GetDrawingInstructionText()
        {
            if (_isPreflightTutorial)
            {
                return L10n.T(
                    "first_contact.terminal.prompt.check_drawing",
                    "PRESS ENTER TO CHECK");
            }

            return L10n.T("first_contact.terminal.prompt.send_drawing", "PRESS ENTER TO SEND");
        }


        private void ResolveRuntimeServices()
        {
            TerminalDisplay terminalDisplay =
                _context?.TerminalDisplay ?? FindFirstObjectByType<TerminalDisplay>();
            _terminalTextEntry?.Dispose();
            _terminalTextEntry = new TerminalTextEntrySession(terminalDisplay);
            if (isActiveAndEnabled)
            {
                _terminalTextEntry.Enable();
            }

            _terminalPresenter = new FirstContactTerminalPresenter(
                terminalDisplay,
                GetDebugSettings(),
                GetPresentationSettings());
            _tabletControls = FindFirstObjectByType<TabletPhysicalControlsController>(FindObjectsInactive.Include);
            IEmbeddingService embeddingRuntime =
                (GamePipelineRunner.Instance?.RuntimeService as IEmbeddingService) ??
                (LlmServiceLocator.Current as IEmbeddingService);
            _embeddingService = new FirstContactEmbeddingService(embeddingRuntime, GetSemanticSettings());
            _probeCaptureService?.Dispose();
            _probeCaptureService = new FirstContactProbeCaptureService(GetVlmSettings());
            _probeProcessor = new FirstContactProbeProcessor(
                GamePipelineRunner.Instance,
                GetVlmSettings());
            _bootstrapMapBuilder = new FirstContactBootstrapMapBuilder(_embeddingService);
            _interactionPolicy = new FirstContactInteractionPolicy();
            _session ??= new FirstContactSessionContext();
            _encounterDirector = GetComponent<FirstContactEncounterDirector>();
            if (_encounterDirector == null)
            {
                _encounterDirector = gameObject.AddComponent<FirstContactEncounterDirector>();
            }

            _meetingArrival = FindFirstObjectByType<FirstContactMeetingArrivalController>(
                FindObjectsInactive.Include);

            _encounterDirector.Configure(_context, config != null ? config.narrativeSettings : null);
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


        private bool HandleGuidanceLineDismissInput()
        {
            if (!_guidanceLineVisible || !_guidanceLineAwaitingDismissal)
            {
                return false;
            }

            if (Time.frameCount <= _guidanceLineShownFrame)
            {
                return false;
            }

            if (!TerminalKeyboardInput.WasPressed(KeyCode.Space) &&
                _context?.Subtitles?.ConsumeAdvanceRequest() != true)
            {
                return false;
            }

            HideGuidanceLine(force: true);
            return true;
        }

        private void ShowDoctorHwangLine(string localizationKey, params L10nArg[] arguments)
        {
            ShowDoctorHwangLineInternal(
                localizationKey,
                firstOccurrenceOnly: true,
                showAdvancePrompt: true,
                arguments);
        }

        private void ShowRepeatableDoctorHwangLine(
            string localizationKey,
            params L10nArg[] arguments)
        {
            ShowDoctorHwangLineInternal(
                localizationKey,
                firstOccurrenceOnly: false,
                showAdvancePrompt: false,
                arguments);
        }

        private void ShowDoctorHwangLineInternal(
            string localizationKey,
            bool firstOccurrenceOnly,
            bool showAdvancePrompt,
            params L10nArg[] arguments)
        {
            if (string.IsNullOrWhiteSpace(localizationKey))
            {
                return;
            }

            if (firstOccurrenceOnly &&
                _encounterDirector != null &&
                !_encounterDirector.ShouldShowGuidance(localizationKey))
            {
                return;
            }

            string line = L10n.T(localizationKey, string.Empty, arguments);
            if (string.IsNullOrWhiteSpace(line))
            {
                return;
            }

            string speaker = L10n.T(DoctorHwangSpeakerKey, "Dr. Hwang");
            NarrativeTrace.Emit("first_contact_day1", localizationKey, "reactive_line");
            _context?.Subtitles?.Show(speaker, line);
            _context?.Subtitles?.SetAdvancePromptVisible(showAdvancePrompt);
            _guidanceLineVisible = true;
            _guidanceLineAwaitingDismissal = showAdvancePrompt;
            _guidanceLineShownFrame = Time.frameCount;
        }

        private void HideGuidanceLine(bool force = false)
        {
            if (!force && _guidanceLineAwaitingDismissal)
            {
                return;
            }

            if (!force && !_guidanceLineVisible)
            {
                return;
            }

            _context?.Subtitles?.SetAdvancePromptVisible(false);
            _context?.Subtitles?.Hide();
            _guidanceLineVisible = false;
            _guidanceLineAwaitingDismissal = false;
            _guidanceLineShownFrame = -1;
        }

        private FirstContactSemanticMapSnapshot BuildBootstrapSemanticMapSnapshot(
            SemanticCardRecord activeCard,
            FirstContactBootstrapCategoryState category,
            bool includeActiveCard)
        {
            return _bootstrapMapBuilder.Build(
                _semanticMemory?.Cards,
                _semanticMemory?.Clusters,
                activeCard,
                _bootstrapSession?.Categories,
                category,
                includeActiveCard,
                GetSemanticSettings());
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

            HideGuidanceLine(force: true);
        }

        private void ChangeState(FirstContactModeState state)
        {
            if (_modeState != state)
            {
                HideGuidanceLine();
            }

            _modeState = state;
            GameState gameState = state switch
            {
                FirstContactModeState.PreflightControls => GameState.Drawing,
                FirstContactModeState.DrawingBootstrapProbe => GameState.Drawing,
                FirstContactModeState.AnalyzingDrawing => GameState.PreviewAnalyzing,
                FirstContactModeState.ReviewingLabel => GameState.Interpreter,
                FirstContactModeState.AssigningPatternMeaning => GameState.Interpreter,
                FirstContactModeState.PatternMeaningRegistered => GameState.Interpreter,
                FirstContactModeState.ReviewingRejectedInput => GameState.Interpreter,
                FirstContactModeState.ReviewingTechnicalFailure => GameState.Interpreter,
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
            _meetingArrival?.StopPresentation();
            if (_routine != null)
            {
                StopCoroutine(_routine);
                _routine = null;
            }
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
            PreflightControls,
            PreflightComplete,
            BootstrapProbeSequence,
            DrawingBootstrapProbe,
            AnalyzingDrawing,
            ReviewingLabel,
            ReviewingRejectedInput,
            ReviewingTechnicalFailure,
            StoringBootstrapProbe,
            AssigningPatternMeaning,
            PatternMeaningRegistered,
            BootstrapComplete,
            TranslatorDemonstration,
            Completed
        }

        private enum FirstContactTerminalChoiceMode
        {
            None,
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
