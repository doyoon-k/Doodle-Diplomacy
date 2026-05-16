using System;
using System.Collections.Generic;
using DoodleDiplomacy.AI;
using DoodleDiplomacy.Camera;
using DoodleDiplomacy.Character;
using DoodleDiplomacy.Data;
using DoodleDiplomacy.Devices;
using DoodleDiplomacy.Dialogue;
using DoodleDiplomacy.Ending;
using DoodleDiplomacy.Gameplay;
using DoodleDiplomacy.Interaction;
using DoodleDiplomacy.UI;
using UnityEngine;
using UnityEngine.Events;

namespace DoodleDiplomacy.Core
{
    [Serializable]
    public class GameStateUnityEvent : UnityEvent<GameState> { }

    public class RoundManager : MonoBehaviour,
        IGameplayMode,
        IGameplayStateObservable,
        IGameplaySessionController,
        IGameplayDebugController,
        IRoundStateEntryContext,
        IRoundPlayerActionContext,
        IRoundStartupContext
    {
        private const float DefaultFirstRoundPrefetchTimeoutSeconds = 180f;

        public static RoundManager Instance { get; private set; }

        [Header("Mode")]
        [SerializeField] private string modeId = "object-pair-drawing";

        [Header("Core Dependencies")]
        [SerializeField] private ScoreManager scoreManager;
        [SerializeField] private CameraController cameraController;
        [SerializeField] private InteractionManager interactionManager;
        [SerializeField] private ScoreConfig scoreConfig;
        [SerializeField] private DialogueSystem dialogueSystem;

        [Header("AI")]
        [SerializeField] private AIPipelineBridge aipipelineBridge;

        [Header("Characters & Devices")]
        [SerializeField] private AlienReactionController alienReactionController;
        [SerializeField] private TerminalDisplay terminalDisplay;
        [SerializeField] private SharedMonitorDisplay sharedMonitorDisplay;
        [SerializeField] private DrawingBoardController drawingBoard;

        [Header("Interaction Targets (Inspector Wiring)")]
        [SerializeField] private InteractableObject[] alienInteractables = Array.Empty<InteractableObject>();
        [SerializeField] private InteractableObject[] tabletInteractables = Array.Empty<InteractableObject>();
        [SerializeField] private InteractableObject sharedMonitorInteractable;
        [SerializeField] private InteractableObject[] terminalInteractables = Array.Empty<InteractableObject>();

        [Header("UI")]
        [SerializeField] private SubtitleDisplay subtitleDisplay;
        [SerializeField] private EndingController endingController;
        [SerializeField] private TitleScreenController titleScreenController;

        [Header("Text")]
        [SerializeField] private IngameTextTable ingameTextTable;

        [Header("Sequences")]
        [SerializeField] private DialogueSequence introSequence;

        [Header("Input")]
        [Tooltip("Input used to mark the drawing as complete and return to submit/modify decisions.")]
        [SerializeField] private KeyCode exitDrawingKey = KeyCode.Escape;
        [Tooltip("Input used to leave the terminal view after reading the translation.")]
        [SerializeField] private KeyCode exitInterpreterKey = KeyCode.Escape;
        [Tooltip("Input used to leave the monitor zoom view after inspecting generated objects.")]
        [SerializeField] private KeyCode exitMonitorZoomKey = KeyCode.Escape;

        [Header("Startup")]
        [Min(0f)]
        [SerializeField] private float firstRoundPrefetchTimeoutSeconds = DefaultFirstRoundPrefetchTimeoutSeconds;

        [Header("Events")]
        public GameStateUnityEvent OnStateChanged;

        private readonly RoundRuntimeServices _services = new();
        private int _currentRound;
        private SatisfactionLevel _lastSatisfaction = SatisfactionLevel.Neutral;
        private bool _preserveRoundIndexOnNextWaitingState;
        private float _interpreterOpenedAt;
        private bool _isSharedMonitorZoomActive;
        private bool _hasOpenedInterpreterThisRound;
        private IRoundAiGateway _aiGateway;
        private IDrawingFeature _drawingFeature;
        private ICameraModeService _cameraModeService;
        private IInteractionStateService _interactionStateService;
        private bool _reportedMissingDrawingFeature;
        private bool _reportedMissingCameraModeService;
        private bool _reportedMissingInteractionStateService;
        private bool _hasBoundInspectorFallbackInteractions;
        private bool _runtimeInitialized;
        private bool _enteredByGameplayHost;

        public string ModeId => string.IsNullOrWhiteSpace(modeId) ? "object-pair-drawing" : modeId;
        public GameState CurrentState => _services.FlowController?.CurrentState ?? GameState.Title;
        public int CurrentRound => _currentRound;
        public bool HasOpenedInterpreterThisRound => _hasOpenedInterpreterThisRound;
        public event Action<GameState> StateChanged;

        IRoundAiGateway IRoundStateEntryContext.AiGateway => _aiGateway;
        ScoreManager IRoundStateEntryContext.ScoreManager => scoreManager;
        ScoreConfig IRoundStateEntryContext.ScoreConfig => scoreConfig;
        DialogueSystem IRoundStateEntryContext.DialogueSystem => dialogueSystem;
        DialogueSequence IRoundStateEntryContext.IntroSequence => introSequence;
        DialogueSequence IRoundStateEntryContext.RuntimeIntroSequence
        {
            get
            {
                EnsureRoundServices();
                return _services.IntroSequenceProvider?.RuntimeSequence;
            }
        }
        TerminalDisplay IRoundStateEntryContext.TerminalDisplay => terminalDisplay;
        AlienReactionController IRoundStateEntryContext.AlienReactionController => alienReactionController;
        EndingController IRoundStateEntryContext.EndingController => endingController;
        TitleScreenController IRoundStateEntryContext.TitleScreenController => titleScreenController;
        RoundHintPresenter IRoundStateEntryContext.HintPresenter => _services.HintPresenter;
        RoundDrawingInteractionGate IRoundStateEntryContext.DrawingInteractionGate => _services.DrawingInteractionGate;
        int IRoundStateEntryContext.CurrentRound
        {
            get => _currentRound;
            set => _currentRound = value;
        }
        bool IRoundStateEntryContext.PreserveRoundIndexOnNextWaitingState
        {
            get => _preserveRoundIndexOnNextWaitingState;
            set => _preserveRoundIndexOnNextWaitingState = value;
        }
        bool IRoundStateEntryContext.HasOpenedInterpreterThisRound
        {
            get => _hasOpenedInterpreterThisRound;
            set => _hasOpenedInterpreterThisRound = value;
        }
        bool IRoundStateEntryContext.IsPreviewTerminalOpen
        {
            get
            {
                EnsureRoundServices();
                return _services.PreviewTerminalPresenter?.IsOpen ?? false;
            }
            set
            {
                EnsureRoundServices();
                if (_services.PreviewTerminalPresenter != null)
                {
                    _services.PreviewTerminalPresenter.IsOpen = value;
                }
            }
        }
        SatisfactionLevel IRoundStateEntryContext.LastSatisfaction
        {
            get => _lastSatisfaction;
            set => _lastSatisfaction = value;
        }
        IRoundAiGateway IRoundPlayerActionContext.AiGateway => _aiGateway;
        ScoreConfig IRoundPlayerActionContext.ScoreConfig => scoreConfig;
        bool IRoundPlayerActionContext.IsPreviewTerminalOpen
        {
            get
            {
                EnsureRoundServices();
                return _services.PreviewTerminalPresenter?.IsOpen ?? false;
            }
            set
            {
                EnsureRoundServices();
                if (_services.PreviewTerminalPresenter != null)
                {
                    _services.PreviewTerminalPresenter.IsOpen = value;
                }
            }
        }
        bool IRoundPlayerActionContext.IsSharedMonitorZoomActive => _isSharedMonitorZoomActive;
        float IRoundPlayerActionContext.InterpreterOpenedAt
        {
            get => _interpreterOpenedAt;
            set => _interpreterOpenedAt = value;
        }
        IRoundAiGateway IRoundStartupContext.AiGateway => _aiGateway;
        ScoreManager IRoundStartupContext.ScoreManager => scoreManager;
        float IRoundStartupContext.FirstRoundPrefetchTimeoutSeconds => firstRoundPrefetchTimeoutSeconds;
        int IRoundStartupContext.CurrentRound
        {
            get => _currentRound;
            set => _currentRound = value;
        }
        SatisfactionLevel IRoundStartupContext.LastSatisfaction
        {
            get => _lastSatisfaction;
            set => _lastSatisfaction = value;
        }
        bool IRoundStartupContext.PreserveRoundIndexOnNextWaitingState
        {
            get => _preserveRoundIndexOnNextWaitingState;
            set => _preserveRoundIndexOnNextWaitingState = value;
        }
        bool IRoundStartupContext.HasOpenedInterpreterThisRound
        {
            get => _hasOpenedInterpreterThisRound;
            set => _hasOpenedInterpreterThisRound = value;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            ResolveAiGateway();
        }

        private void Start()
        {
            InitializeRuntime();
        }

        private void OnDestroy()
        {
            _services.StartupFlow?.Stop();
            UnbindInspectorInteractions();
            UnsubscribeFromBridgeEvents();
            _services.IntroSequenceProvider?.Release();

            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Update()
        {
            if (!_enteredByGameplayHost)
            {
                Tick(Time.deltaTime);
            }
        }

        public void Enter(GameplayModeContext context)
        {
            _enteredByGameplayHost = true;

            if (context != null)
            {
                ConfigureDrawingFeature(context.Drawing);
                ConfigureCameraModeService(context.Camera);
                ConfigureInteractionStateService(context.InteractionState);
                if (context.AiGateway != null && !ReferenceEquals(_aiGateway, context.AiGateway))
                {
                    UnsubscribeFromBridgeEvents();
                    _aiGateway = context.AiGateway;
                    SubscribeToBridgeEvents();
                }
            }

            InitializeRuntime();
            ApplyInteractionPolicyForCurrentState();
            ApplyCameraMode(CurrentState);
            StateChanged?.Invoke(CurrentState);
        }

        public void Exit()
        {
            _enteredByGameplayHost = false;
            _services.StartupFlow?.Stop();
            UnsubscribeFromBridgeEvents();
        }

        public void Tick(float deltaTime)
        {
            EnsureRoundServices();
            _services.InputRouter?.Tick();
        }

        public void HandleInteraction(InteractionType type, InteractableObject source)
        {
            switch (type)
            {
                case InteractionType.Alien:
                    OnAlienClicked();
                    break;
                case InteractionType.Tablet:
                    OnTabletClicked();
                    break;
                case InteractionType.Adjutant:
                    OnAdjutantClicked();
                    break;
                case InteractionType.Terminal:
                    OnTerminalClicked();
                    break;
                case InteractionType.Monitor:
                    OnSharedMonitorClicked();
                    break;
                default:
                    Debug.LogWarning($"[RoundManager] Unhandled interaction type: {type}.", source);
                    break;
            }
        }

        public void StartGame(bool isFirstPlay = true)
        {
            EnsureRoundServices();
            _services.StartupFlow?.StartGame(isFirstPlay);
        }

        public void ChangeToTitle()
        {
            EnsureRoundServices();
            _services.StartupFlow?.ChangeToTitle();
        }

        public void OnAlienClicked()
        {
            EnsureRoundServices();
            _services.PlayerActionHandler?.OnAlienClicked();
        }

        public void OnTabletClicked()
        {
            EnsureRoundServices();
            _services.PlayerActionHandler?.OnTabletClicked();
        }

        public void OnAdjutantClicked()
        {
            EnsureRoundServices();
            _services.PlayerActionHandler?.OnAdjutantClicked();
        }

        public void OnTerminalClicked()
        {
            EnsureRoundServices();
            _services.PlayerActionHandler?.OnTerminalClicked();
        }

        public void OnSharedMonitorClicked()
        {
            EnsureRoundServices();
            _services.PlayerActionHandler?.OnSharedMonitorClicked();
        }

        public void OnIntroComplete() => TryTransition(GameState.Intro, GameState.WaitingForRound);
        public void OnPresentingComplete() => TryTransition(GameState.Presenting, GameState.ObjectPresented);
        public void OnDrawingComplete() => TryTransition(GameState.Drawing, GameState.PreviewReady);
        public void OnPreviewSubmit() => TryTransition(GameState.Preview, GameState.Submitting);
        public void SubmitPreview() => OnPreviewSubmit();
        public void OnPreviewModify()
        {
            EnsureRoundServices();
            _services.PlayerActionHandler?.OnPreviewModify();
        }
        public void ModifyPreview() => OnPreviewModify();

        public void ConfigureDrawingFeature(IDrawingFeature drawingFeature)
        {
            _drawingFeature = drawingFeature;
            _reportedMissingDrawingFeature = false;
            _services.DrawingInteractionGate = drawingFeature != null
                ? new RoundDrawingInteractionGate(drawingFeature)
                : null;
        }

        public void ConfigureCameraModeService(ICameraModeService cameraModeService)
        {
            _cameraModeService = cameraModeService;
            _reportedMissingCameraModeService = false;
            _services.CameraModeApplier = cameraModeService != null
                ? new RoundCameraModeApplier(cameraModeService)
                : null;
        }

        public void ConfigureInteractionStateService(IInteractionStateService interactionStateService)
        {
            _interactionStateService = interactionStateService;
            _reportedMissingInteractionStateService = false;
            _services.InteractionGate = interactionStateService != null
                ? new RoundInteractionGate(interactionStateService)
                : null;
        }

        public void OnSubmitComplete() => TryTransition(GameState.Submitting, GameState.AlienReaction);
        public void OnReactionComplete() => TryTransition(GameState.AlienReaction, GameState.InterpreterReady);
        public void OnInterpreterClose() => TryTransition(GameState.Interpreter, GameState.InterpreterReady);

        private void BindInspectorInteractions()
        {
            if (HasGameplayModeHostInteractionRoute())
            {
                _hasBoundInspectorFallbackInteractions = false;
                return;
            }

            EnsureRoundServices();
            _services.InteractionBinder?.Bind(
                OnAlienClicked,
                OnTabletClicked,
                OnTerminalClicked,
                OnSharedMonitorClicked);
            _hasBoundInspectorFallbackInteractions = true;
        }

        private void UnbindInspectorInteractions()
        {
            if (!_hasBoundInspectorFallbackInteractions)
            {
                return;
            }

            _services.InteractionBinder?.Unbind(
                OnAlienClicked,
                OnTabletClicked,
                OnTerminalClicked,
                OnSharedMonitorClicked);
            _hasBoundInspectorFallbackInteractions = false;
        }

        private bool HasGameplayModeHostInteractionRoute()
        {
            return interactionManager != null && interactionManager.GameplayModeHost != null;
        }

        private bool CanUseSharedMonitorZoom()
        {
            return sharedMonitorDisplay != null && sharedMonitorDisplay.HasInspectableImage;
        }

        private void EnterSharedMonitorZoom()
        {
            EnsureRoundServices();
            if (_services.CameraModeApplier == null || !_services.CameraModeApplier.TryApplySharedMonitorZoom(this))
            {
                return;
            }

            _isSharedMonitorZoomActive = true;
        }

        private void ExitSharedMonitorZoom()
        {
            if (!_isSharedMonitorZoomActive)
            {
                return;
            }

            _isSharedMonitorZoomActive = false;
            ApplyCameraMode(CurrentState);
        }

        [ContextMenu("Debug: Advance State")]
        public void Debug_AdvanceState()
        {
            if (RoundDebugTransitionPolicy.TryGetNextState(CurrentState, out GameState nextState))
            {
                ChangeState(nextState);
                return;
            }

            switch (CurrentState)
            {
                case GameState.Presenting:
                    OnPresentingComplete();
                    break;
                case GameState.Drawing:
                    OnDrawingComplete();
                    break;
                case GameState.Preview:
                    OnPreviewSubmit();
                    break;
                case GameState.Submitting:
                    OnSubmitComplete();
                    break;
                case GameState.AlienReaction:
                    OnReactionComplete();
                    break;
                case GameState.Interpreter:
                    OnInterpreterClose();
                    break;
                case GameState.Ending:
                    ChangeToTitle();
                    break;
                default:
                    Debug.Log($"[RoundManager] Debug_AdvanceState ignored in state '{CurrentState}'.");
                    break;
            }
        }

        public void DebugAdvanceState() => Debug_AdvanceState();

        public void Debug_JumpToState(GameState target)
        {
            Debug.Log($"[RoundManager] Debug_JumpToState: {CurrentState} -> {target}");
            ChangeState(target);
        }

        public void DebugJumpToState(GameState target) => Debug_JumpToState(target);

        private void TryTransition(GameState required, GameState next)
        {
            EnsureRoundServices();
            _services.FlowController?.TryTransition(required, next);
        }

        private void ChangeState(GameState newState)
        {
            EnsureRoundServices();
            _services.FlowController?.ChangeState(newState);
        }

        private void ExitState(GameState state)
        {
            switch (state)
            {
                case GameState.Interpreter:
                    break;
            }
        }

        private void ClearSharedMonitorZoomForStateChange()
        {
            _isSharedMonitorZoomActive = false;
        }

        private void PublishStateChanged(GameState state)
        {
            OnStateChanged?.Invoke(state);
            StateChanged?.Invoke(state);
        }

        private bool IsStateCurrent(GameState state, int stateVersion)
        {
            EnsureRoundServices();
            return _services.FlowController != null && _services.FlowController.IsCurrent(state, stateVersion);
        }

        bool IRoundStateEntryContext.IsStateCurrent(GameState state, int stateVersion) => IsStateCurrent(state, stateVersion);
        void IRoundStateEntryContext.RebuildRuntimeIntroSequence()
        {
            EnsureRoundServices();
            _services.IntroSequenceProvider?.Rebuild();
        }
        void IRoundStateEntryContext.ResetPreviewInspectionState() => ResetPreviewInspectionState();
        void IRoundStateEntryContext.ResetTelepathyState(bool clearCachedText) => ResetTelepathyState(clearCachedText);
        void IRoundStateEntryContext.ReturnToWaitingForRoundAfterPresentingFailure() => ReturnToWaitingForRoundAfterPresentingFailure();
        void IRoundStateEntryContext.CachePreviewResult(string analysis) => CachePreviewResult(analysis);
        void IRoundStateEntryContext.ChangeStateFromEntryAction(GameState state) => ChangeState(state);
        void IRoundStateEntryContext.ShowHint(string speaker, string text) => ShowHint(speaker, text);
        string IRoundStateEntryContext.GetConfiguredText(Func<IngameTextTable, string> selector, string fallback) =>
            GetConfiguredText(selector, fallback);
        string IRoundStateEntryContext.GetDrawingReadyHintMessage() => GetDrawingReadyHintMessage();
        string IRoundStateEntryContext.BuildObjectGenerationFailureHint(string objectGenerationError) =>
            BuildObjectGenerationFailureHint(objectGenerationError);
        bool IRoundPlayerActionContext.TryConsumeSharedMonitorClick()
        {
            EnsureRoundServices();
            return _services.InputRouter == null || _services.InputRouter.TryConsumePrimaryClick();
        }
        bool IRoundPlayerActionContext.CanUseSharedMonitorZoom() => CanUseSharedMonitorZoom();
        void IRoundPlayerActionContext.EnterSharedMonitorZoom() => EnterSharedMonitorZoom();
        void IRoundPlayerActionContext.ExitSharedMonitorZoom() => ExitSharedMonitorZoom();
        void IRoundPlayerActionContext.ChangeStateFromPlayerAction(GameState state) => ChangeState(state);
        void IRoundPlayerActionContext.ApplyInteractionPolicyForPlayerAction() => ApplyInteractionPolicyForCurrentState();
        void IRoundPlayerActionContext.ApplyCameraModeForPlayerAction(GameState state) => ApplyCameraMode(state);
        void IRoundPlayerActionContext.ApplyCameraModeForPlayerAction(CameraMode mode)
        {
            EnsureRoundServices();
            _services.CameraModeApplier?.Apply(mode);
        }
        void IRoundPlayerActionContext.ResetCachedInterpretationForRedraw() => ResetCachedInterpretationForRedraw();
        void IRoundPlayerActionContext.ShowPreviewTerminal() => ShowPreviewTerminal();
        void IRoundPlayerActionContext.ShowHint(string speaker, string text) => ShowHint(speaker, text);
        string IRoundPlayerActionContext.GetConfiguredText(Func<IngameTextTable, string> selector, string fallback) =>
            GetConfiguredText(selector, fallback);
        Coroutine IRoundStartupContext.StartStartupCoroutine(System.Collections.IEnumerator routine) => StartCoroutine(routine);
        void IRoundStartupContext.StopStartupCoroutine(Coroutine routine)
        {
            if (routine != null)
            {
                StopCoroutine(routine);
            }
        }
        void IRoundStartupContext.ResetPreviewInspectionState() => ResetPreviewInspectionState();
        void IRoundStartupContext.ResetTelepathyState(bool clearCachedText) => ResetTelepathyState(clearCachedText);
        void IRoundStartupContext.ChangeStateFromStartup(GameState state) => ChangeState(state);

        private void SubscribeToBridgeEvents()
        {
            if (_aiGateway != null)
            {
                _aiGateway.RoundStartReadinessChanged -= HandleRoundStartReadinessChanged;
                _aiGateway.RoundStartReadinessChanged += HandleRoundStartReadinessChanged;
            }
        }

        private void UnsubscribeFromBridgeEvents()
        {
            if (_aiGateway != null)
            {
                _aiGateway.RoundStartReadinessChanged -= HandleRoundStartReadinessChanged;
            }
        }

        private void HandleRoundStartReadinessChanged(bool isReady)
        {
            ApplyInteractionPolicyForCurrentState();

            if (CurrentState == GameState.WaitingForRound && _aiGateway != null)
            {
                ShowHint("System", _aiGateway.GetRoundStartAvailabilityMessage());
            }
        }

        private void ReturnToWaitingForRoundAfterPresentingFailure()
        {
            _preserveRoundIndexOnNextWaitingState = true;
            ChangeState(GameState.WaitingForRound);
        }

        private void ResetTelepathyState(bool clearCachedText = true)
        {
            if (clearCachedText)
            {
                _aiGateway?.ResetRound();
            }
        }

        private void ResetCachedInterpretationForRedraw()
        {
            ResetTelepathyState();
            ResetPreviewInspectionState();
            _services.PreviewTerminalPresenter?.ClearTerminal();
        }

        private void ShowPreviewTerminal()
        {
            EnsureRoundServices();
            _services.PreviewTerminalPresenter?.Show();
        }

        private void CachePreviewResult(string analysis)
        {
            EnsureRoundServices();
            _services.PreviewTerminalPresenter?.CacheResult(analysis);
        }

        private void ResetPreviewInspectionState()
        {
            EnsureRoundServices();
            _services.PreviewTerminalPresenter?.Reset();
        }

        private void ShowHint(string speaker, string text)
        {
            EnsureRoundServices();
            _services.HintPresenter?.Show(speaker, text);
        }

        private string GetDrawingReadyHintMessage()
        {
            EnsureRoundServices();
            return _services.TextProvider?.GetDrawingReadyHintMessage() ?? string.Empty;
        }

        private string GetConfiguredText(Func<IngameTextTable, string> selector, string fallback)
        {
            EnsureRoundServices();
            return _services.TextProvider?.GetConfiguredText(selector, fallback) ?? fallback;
        }

        private string BuildObjectGenerationFailureHint(string objectGenerationError)
        {
            EnsureRoundServices();
            return _services.TextProvider?.BuildObjectGenerationFailureHint(objectGenerationError)
                ?? objectGenerationError;
        }

        private IngameTextTable ResolveIngameTextTable()
        {
            return ingameTextTable != null ? ingameTextTable : IngameTextTable.LoadDefault();
        }

        private void InitializeRoundServices()
        {
            _services.HintPresenter = new RoundHintPresenter(subtitleDisplay);
            _services.CameraModeApplier = CreateCameraModeApplier();
            _services.InteractionGate = CreateInteractionGate();
            _services.InteractionBinder = new RoundInteractableEventBinder(
                alienInteractables,
                tabletInteractables,
                sharedMonitorInteractable,
                terminalInteractables,
                this);
            _services.InputRouter = CreateInputRouter();
            _services.PlayerActionHandler = new RoundPlayerActionHandler(this);
            _services.StartupFlow = new RoundStartupFlow(this);
            _services.PreviewTerminalPresenter = new RoundPreviewTerminalPresenter(terminalDisplay, () => _services.HintPresenter);
            _services.TextProvider = new RoundTextProvider(ResolveIngameTextTable, () => exitDrawingKey);
            _services.IntroSequenceProvider = new RoundIntroSequenceProvider(() => introSequence, ResolveIngameTextTable);
            IDrawingFeature drawingFeature = ResolveDrawingFeature();
            _services.DrawingInteractionGate = drawingFeature != null
                ? new RoundDrawingInteractionGate(drawingFeature)
                : null;
            _services.StateEntryActions = new RoundStateEntryActions(this);
            _services.FlowController = CreateFlowController();
        }

        private void InitializeRuntime()
        {
            if (_runtimeInitialized)
            {
                return;
            }

            InitializeRoundServices();
            if (_aiGateway == null)
            {
                ResolveAiGateway();
            }
            UpdateDrawingBoardInteractionForState(CurrentState);
            BindInspectorInteractions();
            SubscribeToBridgeEvents();
            ApplyInteractionPolicyForCurrentState();
            ApplyCameraMode(CurrentState);
            _runtimeInitialized = true;
        }

        private void EnsureRoundServices()
        {
            _services.HintPresenter ??= new RoundHintPresenter(subtitleDisplay);
            _services.CameraModeApplier ??= CreateCameraModeApplier();
            _services.InteractionGate ??= CreateInteractionGate();
            _services.InteractionBinder ??= new RoundInteractableEventBinder(
                alienInteractables,
                tabletInteractables,
                sharedMonitorInteractable,
                terminalInteractables,
                this);
            _services.InputRouter ??= CreateInputRouter();
            _services.PlayerActionHandler ??= new RoundPlayerActionHandler(this);
            _services.StartupFlow ??= new RoundStartupFlow(this);
            _services.PreviewTerminalPresenter ??= new RoundPreviewTerminalPresenter(terminalDisplay, () => _services.HintPresenter);
            _services.TextProvider ??= new RoundTextProvider(ResolveIngameTextTable, () => exitDrawingKey);
            _services.IntroSequenceProvider ??= new RoundIntroSequenceProvider(() => introSequence, ResolveIngameTextTable);

            if (_services.DrawingInteractionGate == null)
            {
                IDrawingFeature drawingFeature = ResolveDrawingFeature();
                if (drawingFeature != null)
                {
                    _services.DrawingInteractionGate = new RoundDrawingInteractionGate(drawingFeature);
                }
            }

            _services.StateEntryActions ??= new RoundStateEntryActions(this);
            _services.FlowController ??= CreateFlowController();
        }

        private RoundFlowController CreateFlowController()
        {
            return new RoundFlowController(
                GameState.Title,
                () => _services.StateEntryActions,
                ExitState,
                ClearSharedMonitorZoomForStateChange,
                UpdateDrawingBoardInteractionForState,
                ApplyInteractionPolicyForCurrentState,
                ApplyCameraMode,
                PublishStateChanged);
        }

        private IDrawingFeature ResolveDrawingFeature()
        {
            if (_drawingFeature != null)
            {
                return _drawingFeature;
            }

            if (drawingBoard != null)
            {
                _drawingFeature = new DrawingFeature(drawingBoard, null);
                return _drawingFeature;
            }

            if (!_reportedMissingDrawingFeature)
            {
                Debug.LogError("[RoundManager] Drawing feature is missing. Assign DrawingBoardController in the inspector or inject IDrawingFeature from GameplayModeContext.", this);
                _reportedMissingDrawingFeature = true;
            }

            return null;
        }

        private RoundInteractionGate CreateInteractionGate()
        {
            IInteractionStateService interactionStateService = ResolveInteractionStateService();
            return interactionStateService != null ? new RoundInteractionGate(interactionStateService) : null;
        }

        private IInteractionStateService ResolveInteractionStateService()
        {
            if (_interactionStateService != null)
            {
                return _interactionStateService;
            }

            if (interactionManager != null)
            {
                _interactionStateService = new InteractionStateService(interactionManager, new ObjectPairDrawingInteractionPolicy());
                return _interactionStateService;
            }

            if (!_reportedMissingInteractionStateService)
            {
                Debug.LogError("[RoundManager] Interaction state service is missing. Assign InteractionManager in the inspector or inject IInteractionStateService from GameplayModeContext.", this);
                _reportedMissingInteractionStateService = true;
            }

            return null;
        }

        private RoundCameraModeApplier CreateCameraModeApplier()
        {
            ICameraModeService cameraModeService = ResolveCameraModeService();
            return cameraModeService != null ? new RoundCameraModeApplier(cameraModeService) : null;
        }

        private ICameraModeService ResolveCameraModeService()
        {
            if (_cameraModeService != null)
            {
                return _cameraModeService;
            }

            if (cameraController != null)
            {
                _cameraModeService = new CameraModeService(cameraController);
                return _cameraModeService;
            }

            if (!_reportedMissingCameraModeService)
            {
                Debug.LogError("[RoundManager] Camera mode service is missing. Assign CameraController in the inspector or inject ICameraModeService from GameplayModeContext.", this);
                _reportedMissingCameraModeService = true;
            }

            return null;
        }

        private RoundInputRouter CreateInputRouter()
        {
            return new RoundInputRouter(
                () => CurrentState,
                () => _isSharedMonitorZoomActive,
                exitDrawingKey,
                exitInterpreterKey,
                exitMonitorZoomKey,
                ExitSharedMonitorZoom,
                OnDrawingComplete,
                OnInterpreterClose);
        }

        private void ResolveAiGateway()
        {
            _aiGateway = new AIPipelineRoundAiGateway(aipipelineBridge);
        }

        private void ApplyInteractionPolicyForCurrentState()
        {
            EnsureRoundServices();
            bool roundStartReady = _aiGateway != null && _aiGateway.IsRoundStartReady;
            _services.InteractionGate?.Apply(
                CurrentState,
                roundStartReady,
                _hasOpenedInterpreterThisRound);
        }

        private void EnsureDrawingBoardReadyForEditing()
        {
            EnsureRoundServices();
            _services.DrawingInteractionGate?.UnlockForEditing();
        }

        private void UpdateDrawingBoardInteractionForState(GameState state)
        {
            EnsureRoundServices();
            _services.DrawingInteractionGate?.Apply(state);
        }

        private void ApplyCameraMode(GameState state)
        {
            EnsureRoundServices();
            _services.CameraModeApplier?.Apply(state);
        }
    }
}

