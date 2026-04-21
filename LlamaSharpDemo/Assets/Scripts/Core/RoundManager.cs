using System;
using System.Collections;
using System.Collections.Generic;
using DoodleDiplomacy.AI;
using DoodleDiplomacy.Camera;
using DoodleDiplomacy.Character;
using DoodleDiplomacy.Data;
using DoodleDiplomacy.Devices;
using DoodleDiplomacy.Dialogue;
using DoodleDiplomacy.Ending;
using DoodleDiplomacy.Interaction;
using DoodleDiplomacy.UI;
using UnityEngine;
using UnityEngine.Events;

namespace DoodleDiplomacy.Core
{
    [Serializable]
    public class GameStateUnityEvent : UnityEvent<GameState> { }

    public class RoundManager : MonoBehaviour
    {
        private const string PreviewFallbackText = "(analysis unavailable)";
        private const string PreviewTerminalHeader = "[ALIEN FIRST PASS]";
        private const string DefaultPreviewAnalyzingMessage = "The alien is trying to understand your drawing...";
        private const string DefaultPreviewReadyToInspectMessage = "First-pass analysis is complete. Click the terminal to inspect the result.";
        private const string DefaultTerminalSignalReadyMessage = "A signal has reached the terminal. Click the terminal to inspect it.";
        private const string DefaultNoSignalMessage = "No readable signal was recovered. Open the terminal to continue.";
        private const string DefaultRegeneratingReferencesMessage = "Regenerating the object references with the same prompts...";
        private const string DefaultOpenTerminalFirstMessage = "Open the terminal first.";
        private const string DefaultAdjutantDisabledMessage = "The adjutant can no longer review drawings. Click the alien for first-pass review.";
        private const string DefaultGeneratingAlienObjectsMessage = "Generating the alien objects...";
        private const string DefaultObjectGeneratorMissingMessage = "Object generator is missing. Assign AIPipelineBridge before starting the round.";
        private const string DefaultObjectGenerationFailedRetryMessage = "Object generation failed. Click the alien to retry.";
        private const string DefaultObjectGenerationFailedPrefix = "Object generation failed: ";
        private const string DefaultObjectPresentedHintMessage = "Click the tablet to start drawing, or click the alien to regenerate the references.";
        private const string DefaultDrawingReadyHintTemplate = "Press {0} when the drawing is ready to submit.";
        private const string DefaultPreviewReadyHintMessage = "Click the alien to get a first-pass read, or click the tablet to keep drawing.";
        private const string DefaultSubmittingHintMessage = "Submitting the drawing to the alien delegation...";
        private const float TerminalCloseGuardSeconds = 0.15f;
        private const float DefaultFirstRoundPrefetchTimeoutSeconds = 180f;

        public static RoundManager Instance { get; private set; }

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

        private GameState _currentState = GameState.Title;
        private int _currentRound;
        private int _stateVersion;
        private SatisfactionLevel _lastSatisfaction = SatisfactionLevel.Neutral;
        private bool _preserveRoundIndexOnNextWaitingState;
        private float _interpreterOpenedAt;
        private bool _isSharedMonitorZoomActive;
        private bool _hasOpenedInterpreterThisRound;
        private bool _monitorClickConsumedUntilRelease;
        private bool _isPreviewTerminalOpen;
        private bool _hasShownPreviewTerminalOnce;
        private string _cachedPreviewTerminalOutput = string.Empty;
        private Coroutine _startGameRoutine;
        private DialogueSequence _runtimeIntroSequence;

        public GameState CurrentState => _currentState;
        public int CurrentRound => _currentRound;
        public bool HasOpenedInterpreterThisRound => _hasOpenedInterpreterThisRound;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        private void Start()
        {
            drawingBoard ??= FindFirstObjectByType<DrawingBoardController>();
            UpdateDrawingBoardInteractionForState(_currentState);
            BindInspectorInteractions();
            SubscribeToBridgeEvents();
            interactionManager?.SetInteractablesForState(_currentState);
            ApplyCameraMode(_currentState);
        }

        private void OnDestroy()
        {
            StopStartGameRoutine();
            UnbindInspectorInteractions();
            UnsubscribeFromBridgeEvents();
            ReleaseRuntimeIntroSequence();

            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Update()
        {
            RefreshMonitorClickLatch();

            if (_isSharedMonitorZoomActive && WasKeyPressed(exitMonitorZoomKey))
            {
                ExitSharedMonitorZoom();
                return;
            }

            if (_currentState == GameState.Drawing && WasKeyPressed(exitDrawingKey))
            {
                OnDrawingComplete();
            }

            if (_currentState == GameState.Interpreter && WasKeyPressed(exitInterpreterKey))
            {
                OnInterpreterClose();
            }
        }

        public void StartGame(bool isFirstPlay = true)
        {
            StopStartGameRoutine();
            CancelActiveAiOperations();

            _currentRound = 0;
            _lastSatisfaction = SatisfactionLevel.Neutral;
            _preserveRoundIndexOnNextWaitingState = false;
            _hasOpenedInterpreterThisRound = false;
            ResetPreviewInspectionState();

            scoreManager?.Reset();
            aipipelineBridge?.ResetRound();
            ResetTelepathyState();

            if (aipipelineBridge == null)
            {
                ChangeState(isFirstPlay ? GameState.Intro : GameState.WaitingForRound);
                return;
            }

            _startGameRoutine = StartCoroutine(StartGameAfterFirstRoundPrefetchRoutine(isFirstPlay));
        }

        public void ChangeToTitle()
        {
            StopStartGameRoutine();
            CancelActiveAiOperations();
            ResetTelepathyState();
            ResetPreviewInspectionState();
            _hasOpenedInterpreterThisRound = false;
            ChangeState(GameState.Title);
        }

        private IEnumerator StartGameAfterFirstRoundPrefetchRoutine(bool isFirstPlay)
        {
            try
            {
                if (aipipelineBridge == null)
                {
                    ChangeState(isFirstPlay ? GameState.Intro : GameState.WaitingForRound);
                    yield break;
                }

                Debug.Log("[RoundManager] Preparing first-round objects and LLM runtime before starting gameplay.");
                aipipelineBridge.EnsureObjectGenerationPreparation(
                    forceRetry: aipipelineBridge.HasObjectGenerationPreparationFailed);
                aipipelineBridge.EnsureLlmPreparation(forceRetry: aipipelineBridge.HasLlmPreparationFailed);
                aipipelineBridge.StartNextRoundPrefetch();

                float timeout = Mathf.Max(0f, firstRoundPrefetchTimeoutSeconds);
                float elapsed = 0f;
                while (aipipelineBridge.IsNextRoundPrefetchRunning)
                {
                    aipipelineBridge.RefreshLlmPreparationStatus();
                    if (timeout > 0f && elapsed >= timeout)
                    {
                        break;
                    }

                    elapsed += Time.unscaledDeltaTime;
                    yield return null;
                }

                aipipelineBridge.RefreshLlmPreparationStatus();

                if (aipipelineBridge.IsNextRoundPrefetchReady)
                {
                    Debug.Log("[RoundManager] First-round prefetch finished. Starting gameplay.");
                }
                else if (aipipelineBridge.HasNextRoundPrefetchFailed)
                {
                    Debug.LogWarning(
                        $"[RoundManager] First-round prefetch failed before start: {aipipelineBridge.NextRoundPrefetchError}. " +
                        "Will fall back to normal presenting generation.");
                }
                else if (aipipelineBridge.IsNextRoundPrefetchRunning)
                {
                    Debug.LogWarning(
                        $"[RoundManager] First-round prefetch timed out after {timeout:0.##}s. " +
                        "Will fall back to normal presenting generation.");
                }

                if (aipipelineBridge.IsLlmPreparationReady)
                {
                    Debug.Log("[RoundManager] LLM preload finished. Starting gameplay.");
                }
                else if (aipipelineBridge.HasLlmPreparationFailed)
                {
                    Debug.LogWarning(
                        $"[RoundManager] LLM preload failed before start: {aipipelineBridge.LastLlmPreparationError}. " +
                        "Gameplay will continue and LLM calls may fall back or stall.");
                }
                else if (aipipelineBridge.IsLlmPreparationRunning)
                {
                    Debug.Log(
                        "[RoundManager] LLM preload is still running in background. " +
                        "Gameplay starts now and first LLM call may still warm up.");
                }

                ChangeState(isFirstPlay ? GameState.Intro : GameState.WaitingForRound);
            }
            finally
            {
                _startGameRoutine = null;
            }
        }

        public void OnAlienClicked()
        {
            if (_currentState == GameState.WaitingForRound)
            {
                if (aipipelineBridge != null && !aipipelineBridge.IsRoundStartReady)
                {
                    aipipelineBridge.EnsureObjectGenerationPreparation(
                        forceRetry: aipipelineBridge.HasObjectGenerationPreparationFailed);
                    aipipelineBridge.PrepareRoundKeywords(forceRefresh: false);
                    ShowHint("System", aipipelineBridge.GetRoundStartAvailabilityMessage());
                    interactionManager?.SetInteractablesForState(_currentState);
                    return;
                }

                ChangeState(GameState.Presenting);
                return;
            }

            if (_currentState == GameState.ObjectPresented)
            {
                ShowHint(
                    "System",
                    GetConfiguredText(
                        table => table.regeneratingReferencesMessage,
                        DefaultRegeneratingReferencesMessage));
                ChangeState(GameState.Presenting);
                return;
            }

            if (_currentState == GameState.PreviewReady)
            {
                ChangeState(GameState.PreviewAnalyzing);
                return;
            }

            if (_currentState == GameState.InterpreterReady)
            {
                if (!_hasOpenedInterpreterThisRound)
                {
                    ShowHint(
                        "System",
                        GetConfiguredText(
                            table => table.openTerminalFirstMessage,
                            DefaultOpenTerminalFirstMessage));
                    return;
                }

                bool isLastRound = scoreConfig != null && _currentRound >= scoreConfig.totalRounds;
                ChangeState(isLastRound ? GameState.Ending : GameState.WaitingForRound);
            }

        }

        public void OnTabletClicked()
        {
            if (_currentState == GameState.ObjectPresented || _currentState == GameState.PreviewReady)
            {
                if (_currentState == GameState.PreviewReady)
                {
                    ResetCachedInterpretationForRedraw();
                }

                ChangeState(GameState.Drawing);
            }
        }

        public void OnAdjutantClicked()
        {
            if (_currentState == GameState.PreviewReady)
            {
                ShowHint(
                    "System",
                    GetConfiguredText(
                        table => table.adjutantDisabledMessage,
                        DefaultAdjutantDisabledMessage));
            }
        }
        public void OnTerminalClicked()
        {
            if (_currentState == GameState.Preview)
            {
                if (_isPreviewTerminalOpen)
                {
                    if (Time.unscaledTime - _interpreterOpenedAt < TerminalCloseGuardSeconds)
                    {
                        return;
                    }

                    _isPreviewTerminalOpen = false;
                    ApplyCameraMode(_currentState);
                    return;
                }

                _interpreterOpenedAt = Time.unscaledTime;
                _isPreviewTerminalOpen = true;
                cameraController?.SetMode(CameraMode.TerminalZoom);

                string previewOutput = string.IsNullOrWhiteSpace(_cachedPreviewTerminalOutput)
                    ? BuildPreviewTerminalOutput(PreviewFallbackText)
                    : _cachedPreviewTerminalOutput;
                bool instant = _hasShownPreviewTerminalOnce;
                terminalDisplay?.ShowText(previewOutput, instant);
                _hasShownPreviewTerminalOnce = true;
                subtitleDisplay?.Hide();
                return;
            }

            if (_currentState == GameState.InterpreterReady)
            {
                _interpreterOpenedAt = Time.unscaledTime;
                ChangeState(GameState.Interpreter);
            }
            else if (_currentState == GameState.Interpreter)
            {
                if (Time.unscaledTime - _interpreterOpenedAt < TerminalCloseGuardSeconds)
                {
                    return;
                }

                OnInterpreterClose();
            }
        }

        public void OnSharedMonitorClicked()
        {
            if (_monitorClickConsumedUntilRelease)
            {
                return;
            }

            _monitorClickConsumedUntilRelease = true;

            if (!(_currentState == GameState.ObjectPresented || _currentState == GameState.PreviewReady))
            {
                return;
            }

            if (!CanUseSharedMonitorZoom())
            {
                return;
            }

            if (_isSharedMonitorZoomActive)
            {
                ExitSharedMonitorZoom();
            }
            else
            {
                EnterSharedMonitorZoom();
            }
        }

        private void RefreshMonitorClickLatch()
        {
            if (!_monitorClickConsumedUntilRelease)
            {
                return;
            }

            if (!IsPrimaryPointerHeld())
            {
                _monitorClickConsumedUntilRelease = false;
            }
        }

        private static bool IsPrimaryPointerHeld()
        {
#if ENABLE_INPUT_SYSTEM
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse != null)
            {
                return mouse.leftButton.isPressed;
            }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetMouseButton(0);
#else
            return false;
#endif
        }

        public void OnIntroComplete() => TryTransition(GameState.Intro, GameState.WaitingForRound);
        public void OnPresentingComplete() => TryTransition(GameState.Presenting, GameState.ObjectPresented);
        public void OnDrawingComplete() => TryTransition(GameState.Drawing, GameState.PreviewReady);
        public void OnPreviewSubmit() => TryTransition(GameState.Preview, GameState.Submitting);
        public void OnPreviewModify()
        {
            if (_currentState != GameState.Preview)
            {
                return;
            }

            ResetCachedInterpretationForRedraw();
            ChangeState(GameState.Drawing);
        }
        public void OnSubmitComplete() => TryTransition(GameState.Submitting, GameState.AlienReaction);
        public void OnReactionComplete() => TryTransition(GameState.AlienReaction, GameState.InterpreterReady);
        public void OnInterpreterClose() => TryTransition(GameState.Interpreter, GameState.InterpreterReady);

        private void BindInspectorInteractions()
        {
            BindTabletInteractions();

            if (sharedMonitorInteractable != null)
            {
                sharedMonitorInteractable.interactionType = InteractionType.Monitor;
                sharedMonitorInteractable.OnInteracted.RemoveListener(OnTabletClicked);
                sharedMonitorInteractable.OnInteracted.RemoveListener(OnSharedMonitorClicked);
                sharedMonitorInteractable.OnInteracted.AddListener(OnSharedMonitorClicked);
            }
            else
            {
                Debug.LogWarning("[RoundManager] sharedMonitorInteractable is not assigned. Monitor click zoom will be disabled.");
            }

            if (terminalInteractables == null || terminalInteractables.Length == 0)
            {
                Debug.LogWarning("[RoundManager] terminalInteractables is empty. Terminal click interaction must be wired in inspector.");
                return;
            }

            for (int i = 0; i < terminalInteractables.Length; i++)
            {
                InteractableObject terminalInteractable = terminalInteractables[i];
                if (terminalInteractable == null)
                {
                    continue;
                }

                terminalInteractable.interactionType = InteractionType.Terminal;
                terminalInteractable.OnInteracted.RemoveListener(OnTabletClicked);
                terminalInteractable.OnInteracted.RemoveListener(OnTerminalClicked);
                terminalInteractable.OnInteracted.AddListener(OnTerminalClicked);
            }
        }

        private void UnbindInspectorInteractions()
        {
            UnbindTabletInteractions();

            if (sharedMonitorInteractable != null)
            {
                sharedMonitorInteractable.OnInteracted.RemoveListener(OnSharedMonitorClicked);
            }

            if (terminalInteractables == null)
            {
                return;
            }

            for (int i = 0; i < terminalInteractables.Length; i++)
            {
                if (terminalInteractables[i] == null)
                {
                    continue;
                }

                terminalInteractables[i].OnInteracted.RemoveListener(OnTerminalClicked);
            }
        }

        private void BindTabletInteractions()
        {
            InteractableObject[] interactables = FindObjectsByType<InteractableObject>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (int i = 0; i < interactables.Length; i++)
            {
                InteractableObject interactable = interactables[i];
                if (interactable == null || interactable.interactionType != InteractionType.Tablet)
                {
                    continue;
                }

                interactable.OnInteracted.RemoveListener(OnTabletClicked);
                interactable.OnInteracted.AddListener(OnTabletClicked);
            }
        }

        private void UnbindTabletInteractions()
        {
            InteractableObject[] interactables = FindObjectsByType<InteractableObject>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (int i = 0; i < interactables.Length; i++)
            {
                InteractableObject interactable = interactables[i];
                if (interactable == null || interactable.interactionType != InteractionType.Tablet)
                {
                    continue;
                }

                interactable.OnInteracted.RemoveListener(OnTabletClicked);
            }
        }

        private bool CanUseSharedMonitorZoom()
        {
            return sharedMonitorDisplay != null && sharedMonitorDisplay.HasInspectableImage;
        }

        private void EnterSharedMonitorZoom()
        {
            if (cameraController == null)
            {
                return;
            }

            if (!cameraController.HasValidPreset(CameraMode.SharedMonitorZoom))
            {
                Debug.LogWarning("[RoundManager] Shared monitor zoom preset is not configured on CameraController.");
                return;
            }

            _isSharedMonitorZoomActive = true;
            cameraController.SetMode(CameraMode.SharedMonitorZoom);
        }

        private void ExitSharedMonitorZoom()
        {
            if (!_isSharedMonitorZoomActive)
            {
                return;
            }

            _isSharedMonitorZoomActive = false;
            if (cameraController != null)
            {
                ApplyCameraMode(_currentState);
            }
        }

        [ContextMenu("Debug: Advance State")]
        public void Debug_AdvanceState()
        {
            switch (_currentState)
            {
                case GameState.Intro:
                    ChangeState(GameState.WaitingForRound);
                    break;
                case GameState.Presenting:
                    OnPresentingComplete();
                    break;
                case GameState.Drawing:
                    OnDrawingComplete();
                    break;
                case GameState.PreviewReady:
                    ChangeState(GameState.PreviewAnalyzing);
                    break;
                case GameState.PreviewAnalyzing:
                    ChangeState(GameState.Preview);
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
                    Debug.Log($"[RoundManager] Debug_AdvanceState ignored in state '{_currentState}'.");
                    break;
            }
        }

        public void Debug_JumpToState(GameState target)
        {
            Debug.Log($"[RoundManager] Debug_JumpToState: {_currentState} -> {target}");
            ChangeState(target);
        }

        private bool WasKeyPressed(KeyCode keyCode)
        {
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard == null)
            {
                return false;
            }

            var keyControl = GetInputSystemKeyControl(keyboard, keyCode);
            return keyControl != null && keyControl.wasPressedThisFrame;
        }

        private static UnityEngine.InputSystem.Controls.KeyControl GetInputSystemKeyControl(
            UnityEngine.InputSystem.Keyboard keyboard,
            KeyCode keyCode)
        {
            return keyCode switch
            {
                KeyCode.Escape => keyboard.escapeKey,
                KeyCode.Space => keyboard.spaceKey,
                KeyCode.Return => keyboard.enterKey,
                KeyCode.KeypadEnter => keyboard.numpadEnterKey,
                KeyCode.Tab => keyboard.tabKey,
                KeyCode.Backspace => keyboard.backspaceKey,
                KeyCode.Delete => keyboard.deleteKey,
                KeyCode.UpArrow => keyboard.upArrowKey,
                KeyCode.DownArrow => keyboard.downArrowKey,
                KeyCode.LeftArrow => keyboard.leftArrowKey,
                KeyCode.RightArrow => keyboard.rightArrowKey,
                KeyCode.A => keyboard.aKey,
                KeyCode.B => keyboard.bKey,
                KeyCode.C => keyboard.cKey,
                KeyCode.D => keyboard.dKey,
                KeyCode.E => keyboard.eKey,
                KeyCode.F => keyboard.fKey,
                KeyCode.G => keyboard.gKey,
                KeyCode.H => keyboard.hKey,
                KeyCode.I => keyboard.iKey,
                KeyCode.J => keyboard.jKey,
                KeyCode.K => keyboard.kKey,
                KeyCode.L => keyboard.lKey,
                KeyCode.M => keyboard.mKey,
                KeyCode.N => keyboard.nKey,
                KeyCode.O => keyboard.oKey,
                KeyCode.P => keyboard.pKey,
                KeyCode.Q => keyboard.qKey,
                KeyCode.R => keyboard.rKey,
                KeyCode.S => keyboard.sKey,
                KeyCode.T => keyboard.tKey,
                KeyCode.U => keyboard.uKey,
                KeyCode.V => keyboard.vKey,
                KeyCode.W => keyboard.wKey,
                KeyCode.X => keyboard.xKey,
                KeyCode.Y => keyboard.yKey,
                KeyCode.Z => keyboard.zKey,
                KeyCode.Alpha0 => keyboard.digit0Key,
                KeyCode.Alpha1 => keyboard.digit1Key,
                KeyCode.Alpha2 => keyboard.digit2Key,
                KeyCode.Alpha3 => keyboard.digit3Key,
                KeyCode.Alpha4 => keyboard.digit4Key,
                KeyCode.Alpha5 => keyboard.digit5Key,
                KeyCode.Alpha6 => keyboard.digit6Key,
                KeyCode.Alpha7 => keyboard.digit7Key,
                KeyCode.Alpha8 => keyboard.digit8Key,
                KeyCode.Alpha9 => keyboard.digit9Key,
                _ => keyboard.escapeKey
            };
        }

        private void TryTransition(GameState required, GameState next)
        {
            if (_currentState == required)
            {
                ChangeState(next);
            }
        }

        private void ChangeState(GameState newState)
        {
            if (_currentState == newState)
            {
                return;
            }

            if (_isSharedMonitorZoomActive)
            {
                _isSharedMonitorZoomActive = false;
            }

            GameState oldState = _currentState;
            int newStateVersion = _stateVersion + 1;

            ExitState(oldState);

            Debug.Log($"[RoundManager] State: {oldState} -> {newState}");
            _currentState = newState;
            _stateVersion = newStateVersion;
            UpdateDrawingBoardInteractionForState(newState);

            EnterState(newState, newStateVersion);
            if (!IsStateCurrent(newState, newStateVersion))
            {
                return;
            }

            OnStateChanged?.Invoke(newState);
            interactionManager?.SetInteractablesForState(newState);
            ApplyCameraMode(newState);
        }

        private void ExitState(GameState state)
        {
            switch (state)
            {
                case GameState.Interpreter:
                    break;
            }
        }

        private void EnterState(GameState state, int stateVersion)
        {
            switch (state)
            {
                case GameState.Intro:
                    subtitleDisplay?.Hide();
                    if (introSequence != null)
                    {
                        RebuildRuntimeIntroSequence();
                        dialogueSystem?.PlaySequence(_runtimeIntroSequence != null ? _runtimeIntroSequence : introSequence);
                    }
                    break;

                case GameState.WaitingForRound:
                    subtitleDisplay?.Hide();
                    _hasOpenedInterpreterThisRound = false;
                    ResetPreviewInspectionState();
                    if (_preserveRoundIndexOnNextWaitingState)
                    {
                        _preserveRoundIndexOnNextWaitingState = false;
                    }
                    else
                    {
                        _currentRound++;
                    }

                    ResetTelepathyState();
                    aipipelineBridge?.ResetRound();
                    aipipelineBridge?.EnsureObjectGenerationPreparation();
                    bool adoptedPrefetch = aipipelineBridge != null && aipipelineBridge.TryAdoptPrefetchedRound();
                    if (!adoptedPrefetch)
                    {
                        aipipelineBridge?.PrepareRoundKeywords();
                    }
                    Debug.Log($"[RoundManager] Waiting for round {_currentRound} / {scoreConfig?.totalRounds}.");
                    ShowHint(
                        "System",
                        aipipelineBridge != null
                            ? aipipelineBridge.GetRoundStartAvailabilityMessage()
                            : GetConfiguredText(
                                table => table.objectGeneratorMissingMessage,
                                DefaultObjectGeneratorMissingMessage));
                    break;

                case GameState.Presenting:
                    ShowHint(
                        "System",
                        GetConfiguredText(
                            table => table.generatingAlienObjectsMessage,
                            DefaultGeneratingAlienObjectsMessage));
                    if (aipipelineBridge != null)
                    {
                        aipipelineBridge.GenerateObjects(success =>
                        {
                            if (!IsStateCurrent(GameState.Presenting, stateVersion))
                            {
                                return;
                            }

                            if (!success)
                            {
                                Debug.LogWarning(
                                    $"[RoundManager] Object generation failed: {aipipelineBridge.LastObjectGenerationError}");
                                ShowHint(
                                    "System",
                                    BuildObjectGenerationFailureHint(aipipelineBridge.LastObjectGenerationError));
                                ReturnToWaitingForRoundAfterPresentingFailure();
                                return;
                            }

                            OnPresentingComplete();
                        });
                    }
                    else
                    {
                        Debug.LogWarning("[RoundManager] AIPipelineBridge is missing. Cannot generate alien objects.");
                        ShowHint(
                            "System",
                            GetConfiguredText(
                                table => table.objectGeneratorMissingMessage,
                                DefaultObjectGeneratorMissingMessage));
                        ReturnToWaitingForRoundAfterPresentingFailure();
                    }
                    break;

                case GameState.ObjectPresented:
                    ShowHint(
                        "System",
                        GetConfiguredText(
                            table => table.objectPresentedHintMessage,
                            DefaultObjectPresentedHintMessage));
                    Debug.Log("[RoundManager] Objects are ready. Waiting for tablet interaction.");
                    aipipelineBridge?.StartNextRoundPrefetch();
                    break;

                case GameState.Drawing:
                    EnsureDrawingBoardReadyForEditing();
                    ShowHint("System", GetDrawingReadyHintMessage());
                    break;

                case GameState.PreviewReady:
                    ShowHint(
                        "System",
                        GetConfiguredText(
                            table => table.previewReadyHintMessage,
                            DefaultPreviewReadyHintMessage));
                    Debug.Log("[RoundManager] Drawing marked complete. Waiting for alien first-pass review.");
                    break;

                case GameState.PreviewAnalyzing:
                    _isPreviewTerminalOpen = false;
                    _hasShownPreviewTerminalOnce = false;
                    _cachedPreviewTerminalOutput = string.Empty;
                    terminalDisplay?.Clear();
                    ShowHint(
                        "System",
                        GetConfiguredText(
                            table => table.previewAnalyzingMessage,
                            DefaultPreviewAnalyzingMessage));
                    if (aipipelineBridge != null)
                    {
                        aipipelineBridge.GetPreview(analysis =>
                        {
                            if (!IsStateCurrent(GameState.PreviewAnalyzing, stateVersion))
                            {
                                return;
                            }

                            CachePreviewResult(analysis);
                            ChangeState(GameState.Preview);
                        });
                    }
                    else
                    {
                        CachePreviewResult("(AI analysis unavailable)");
                        ChangeState(GameState.Preview);
                    }
                    break;

                case GameState.Preview:
                    _isPreviewTerminalOpen = false;
                    ShowHint(
                        "System",
                        GetConfiguredText(
                            table => table.previewReadyToInspectMessage,
                            DefaultPreviewReadyToInspectMessage));
                    Debug.Log("[RoundManager] Preview analysis complete. Waiting for submit or modify.");
                    break;

                case GameState.Submitting:
                    ShowHint(
                        "System",
                        GetConfiguredText(
                            table => table.submittingHintMessage,
                            DefaultSubmittingHintMessage));
                    if (aipipelineBridge != null)
                    {
                        aipipelineBridge.GetJudgment(satisfaction =>
                        {
                            if (!IsStateCurrent(GameState.Submitting, stateVersion))
                            {
                                return;
                            }

                            _lastSatisfaction = satisfaction;
                            scoreManager?.RecordRound(satisfaction);
                            OnSubmitComplete();
                        });
                    }
                    else
                    {
                        _lastSatisfaction = SatisfactionLevel.Neutral;
                        scoreManager?.RecordRound(SatisfactionLevel.Neutral);
                        ResetTelepathyState();
                        OnSubmitComplete();
                    }
                    break;

                case GameState.AlienReaction:
                    subtitleDisplay?.Hide();
                    if (alienReactionController != null)
                    {
                        alienReactionController.OnReactionComplete.RemoveListener(OnReactionComplete);
                        alienReactionController.OnReactionComplete.AddListener(OnReactionComplete);
                        alienReactionController.PlayReaction(_lastSatisfaction);
                    }
                    else
                    {
                        Debug.LogWarning("[RoundManager] AlienReactionController is missing. Skipping reaction.");
                        OnReactionComplete();
                    }
                    break;

                case GameState.InterpreterReady:
                    if (aipipelineBridge != null && aipipelineBridge.HasTelepathyResult)
                    {
                        ShowHint(
                            "System",
                            GetConfiguredText(
                                table => table.terminalSignalReadyMessage,
                                DefaultTerminalSignalReadyMessage));
                    }
                    else
                    {
                        ShowHint(
                            "System",
                            GetConfiguredText(
                                table => table.noSignalMessage,
                                DefaultNoSignalMessage));
                    }

                    Debug.Log("[RoundManager] Interpreter is ready.");
                    break;

                case GameState.Interpreter:
                    subtitleDisplay?.Hide();
                    bool instantTerminalDisplay = _hasOpenedInterpreterThisRound;
                    if (aipipelineBridge != null && aipipelineBridge.HasTelepathyResult)
                    {
                        terminalDisplay?.ShowText(aipipelineBridge.LastTelepathy, instantTerminalDisplay);
                    }
                    else if (terminalDisplay != null)
                    {
                        terminalDisplay.ShowText(
                            "[TRANSLATOR v1.0]\n> No captured alien signal.\n> _",
                            instantTerminalDisplay);
                    }

                    _hasOpenedInterpreterThisRound = true;
                    break;

                case GameState.Ending:
                    subtitleDisplay?.Hide();
                    EndingType ending = scoreManager?.GetEndingType() ?? EndingType.Diplomacy;
                    Debug.Log($"[RoundManager] Ending: {ending}");
                    endingController?.ShowEnding(ending);
                    break;

                case GameState.Title:
                    subtitleDisplay?.Hide();
                    titleScreenController?.ShowTitle();
                    break;
            }
        }

        private bool IsStateCurrent(GameState state, int stateVersion)
        {
            return _currentState == state && _stateVersion == stateVersion;
        }

        private void SubscribeToBridgeEvents()
        {
            if (aipipelineBridge != null)
            {
                aipipelineBridge.RoundStartReadinessChanged -= HandleRoundStartReadinessChanged;
                aipipelineBridge.RoundStartReadinessChanged += HandleRoundStartReadinessChanged;
            }
        }

        private void UnsubscribeFromBridgeEvents()
        {
            if (aipipelineBridge != null)
            {
                aipipelineBridge.RoundStartReadinessChanged -= HandleRoundStartReadinessChanged;
            }
        }

        private void HandleRoundStartReadinessChanged(bool isReady)
        {
            interactionManager?.SetInteractablesForState(_currentState);

            if (_currentState == GameState.WaitingForRound && aipipelineBridge != null)
            {
                ShowHint("System", aipipelineBridge.GetRoundStartAvailabilityMessage());
            }
        }

        private void ReturnToWaitingForRoundAfterPresentingFailure()
        {
            _preserveRoundIndexOnNextWaitingState = true;
            ChangeState(GameState.WaitingForRound);
        }

        private void CancelActiveAiOperations()
        {
            aipipelineBridge?.CancelActiveOperations();
        }

        private void StopStartGameRoutine()
        {
            if (_startGameRoutine == null)
            {
                return;
            }

            StopCoroutine(_startGameRoutine);
            _startGameRoutine = null;
        }

        private void ResetTelepathyState(bool clearCachedText = true)
        {
            if (clearCachedText)
            {
                aipipelineBridge?.ResetRound();
            }
        }

        private void ResetCachedInterpretationForRedraw()
        {
            ResetTelepathyState();
            ResetPreviewInspectionState();
            terminalDisplay?.Clear();
        }

        private void CachePreviewResult(string analysis)
        {
            string resolvedAnalysis = string.IsNullOrWhiteSpace(analysis)
                ? PreviewFallbackText
                : analysis.Trim();
            _cachedPreviewTerminalOutput = BuildPreviewTerminalOutput(resolvedAnalysis);
        }

        private static string BuildPreviewTerminalOutput(string previewLine)
        {
            string line = string.IsNullOrWhiteSpace(previewLine)
                ? PreviewFallbackText
                : previewLine.Trim();
            return $"{PreviewTerminalHeader}\n> {line}\n> _";
        }

        private void ResetPreviewInspectionState()
        {
            _isPreviewTerminalOpen = false;
            _hasShownPreviewTerminalOnce = false;
            _cachedPreviewTerminalOutput = string.Empty;
        }

        private void ShowHint(string speaker, string text)
        {
            if (subtitleDisplay == null)
            {
                return;
            }

            subtitleDisplay.Show(speaker, text);
        }

        private string GetDrawingReadyHintMessage()
        {
            string template = GetConfiguredText(
                table => table.drawingReadyHintTemplate,
                DefaultDrawingReadyHintTemplate);
            if (string.IsNullOrWhiteSpace(template))
            {
                template = DefaultDrawingReadyHintTemplate;
            }

            try
            {
                return string.Format(template, exitDrawingKey);
            }
            catch (FormatException)
            {
                Debug.LogWarning(
                    $"[RoundManager] drawingReadyHintTemplate is invalid ('{template}'). Falling back to default template.");
                return string.Format(DefaultDrawingReadyHintTemplate, exitDrawingKey);
            }
        }

        private string GetConfiguredText(Func<IngameTextTable, string> selector, string fallback)
        {
            IngameTextTable table = ResolveIngameTextTable();
            if (table == null)
            {
                return fallback;
            }

            string configured = selector(table);
            return string.IsNullOrWhiteSpace(configured) ? fallback : configured;
        }

        private string BuildObjectGenerationFailureHint(string objectGenerationError)
        {
            if (string.IsNullOrWhiteSpace(objectGenerationError))
            {
                return GetConfiguredText(
                    table => table.objectGenerationFailedRetryMessage,
                    DefaultObjectGenerationFailedRetryMessage);
            }

            string prefix = GetConfiguredText(
                table => table.objectGenerationFailedPrefix,
                DefaultObjectGenerationFailedPrefix);
            return $"{prefix}{objectGenerationError}";
        }

        private IngameTextTable ResolveIngameTextTable()
        {
            return ingameTextTable != null ? ingameTextTable : IngameTextTable.LoadDefault();
        }

        private void RebuildRuntimeIntroSequence()
        {
            ReleaseRuntimeIntroSequence();

            if (introSequence == null)
            {
                return;
            }

            IngameTextTable table = ResolveIngameTextTable();
            if (table == null)
            {
                return;
            }

            _runtimeIntroSequence = Instantiate(introSequence);
            _runtimeIntroSequence.name = $"{introSequence.name} (Runtime)";

            ApplyIntroSpeakerOverride(_runtimeIntroSequence, table.introAdjutantSpeaker);
            ApplyIntroLineOverride(_runtimeIntroSequence, 0, table.introAdjutantLine1);
            ApplyIntroLineOverride(_runtimeIntroSequence, 1, table.introAdjutantLine2);
            ApplyIntroLineOverride(_runtimeIntroSequence, 2, table.introAdjutantLine3);
        }

        private void ReleaseRuntimeIntroSequence()
        {
            if (_runtimeIntroSequence == null)
            {
                return;
            }

            Destroy(_runtimeIntroSequence);
            _runtimeIntroSequence = null;
        }

        private static void ApplyIntroSpeakerOverride(DialogueSequence sequence, string speaker)
        {
            if (string.IsNullOrWhiteSpace(speaker))
            {
                return;
            }

            ApplyIntroSpeakerOverride(sequence, 0, speaker);
            ApplyIntroSpeakerOverride(sequence, 1, speaker);
            ApplyIntroSpeakerOverride(sequence, 2, speaker);
        }

        private static void ApplyIntroSpeakerOverride(DialogueSequence sequence, int index, string speaker)
        {
            if (!TryGetDialogueLine(sequence, index, out DialogueLineData line))
            {
                return;
            }

            line.characterID = speaker;
        }

        private static void ApplyIntroLineOverride(DialogueSequence sequence, int index, string overrideText)
        {
            if (string.IsNullOrWhiteSpace(overrideText))
            {
                return;
            }

            if (!TryGetDialogueLine(sequence, index, out DialogueLineData line))
            {
                return;
            }

            line.text = overrideText;
        }

        private static bool TryGetDialogueLine(DialogueSequence sequence, int index, out DialogueLineData line)
        {
            line = null;
            if (sequence == null || sequence.lines == null || index < 0 || index >= sequence.lines.Count)
            {
                return false;
            }

            line = sequence.lines[index];
            return line != null;
        }

        private void EnsureDrawingBoardReadyForEditing()
        {
            drawingBoard ??= FindFirstObjectByType<DrawingBoardController>();
            if (drawingBoard == null)
            {
                return;
            }

            drawingBoard.enabled = true;
            drawingBoard.SetInteractionLocked(false);
        }

        private void UpdateDrawingBoardInteractionForState(GameState state)
        {
            drawingBoard ??= FindFirstObjectByType<DrawingBoardController>();
            if (drawingBoard == null)
            {
                return;
            }

            drawingBoard.enabled = true;
            drawingBoard.SetInteractionLocked(state != GameState.Drawing);
        }

        private void ApplyCameraMode(GameState state)
        {
            if (cameraController == null)
            {
                return;
            }

            CameraMode mode = state switch
            {
                GameState.ObjectPresented => CameraMode.Default,
                GameState.Drawing => CameraMode.TabletView,
                GameState.PreviewReady => CameraMode.FreeLook,
                GameState.PreviewAnalyzing => CameraMode.AlienReaction,
                GameState.Preview => CameraMode.FreeLook,
                GameState.AlienReaction => CameraMode.AlienReaction,
                GameState.InterpreterReady => CameraMode.FreeLook,
                GameState.Interpreter => CameraMode.TerminalZoom,
                _ => CameraMode.Default
            };

            cameraController.SetMode(mode);
        }
    }
}
