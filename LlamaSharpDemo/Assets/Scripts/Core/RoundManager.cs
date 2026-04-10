using System;
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
        private const string TerminalSignalReadyMessage = "A signal has reached the terminal. Click the terminal to inspect it.";
        private const string NoSignalMessage = "No readable signal was recovered. Click the alien to continue.";
        private const float TerminalCloseGuardSeconds = 0.15f;

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

        [Header("Interaction Targets (Inspector Wiring)")]
        [SerializeField] private InteractableObject sharedMonitorInteractable;
        [SerializeField] private InteractableObject[] terminalInteractables = Array.Empty<InteractableObject>();

        [Header("UI")]
        [SerializeField] private SubtitleDisplay subtitleDisplay;
        [SerializeField] private EndingController endingController;
        [SerializeField] private TitleScreenController titleScreenController;

        [Header("Sequences")]
        [SerializeField] private DialogueSequence introSequence;

        [Header("Input")]
        [Tooltip("Input used to mark the drawing as complete and unlock the adjutant preview.")]
        [SerializeField] private KeyCode exitDrawingKey = KeyCode.Escape;
        [Tooltip("Input used to leave the terminal view after reading the translation.")]
        [SerializeField] private KeyCode exitInterpreterKey = KeyCode.Escape;
        [Tooltip("Input used to leave the monitor zoom view after inspecting generated objects.")]
        [SerializeField] private KeyCode exitMonitorZoomKey = KeyCode.Escape;

        [Header("Monitor Zoom")]
        [Range(20f, 70f)]
        [SerializeField] private float monitorZoomFieldOfView = 38f;
        [Range(1f, 1.5f)]
        [SerializeField] private float monitorZoomFramingPadding = 1.08f;
        [SerializeField] private float monitorZoomVerticalOffset = 0.05f;
        [SerializeField] private float monitorZoomMinDistance = 0.8f;
        [SerializeField] private float monitorZoomMaxDistance = 6f;

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

        public GameState CurrentState => _currentState;
        public int CurrentRound => _currentRound;

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
            BindInspectorInteractions();
            SubscribeToBridgeEvents();
            interactionManager?.SetInteractablesForState(_currentState);
            ApplyCameraMode(_currentState);
        }

        private void OnDestroy()
        {
            UnbindInspectorInteractions();
            UnsubscribeFromBridgeEvents();

            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Update()
        {
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
            CancelActiveAiOperations();

            _currentRound = 0;
            _lastSatisfaction = SatisfactionLevel.Neutral;
            _preserveRoundIndexOnNextWaitingState = false;
            _hasOpenedInterpreterThisRound = false;

            scoreManager?.Reset();
            aipipelineBridge?.ResetRound();
            ResetTelepathyState();
            ChangeState(isFirstPlay ? GameState.Intro : GameState.WaitingForRound);
        }

        public void ChangeToTitle()
        {
            CancelActiveAiOperations();
            ResetTelepathyState();
            _hasOpenedInterpreterThisRound = false;
            ChangeState(GameState.Title);
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

            if (_currentState == GameState.InterpreterReady)
            {
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

        public void OnAdjutantClicked() => TryTransition(GameState.PreviewReady, GameState.PreviewAnalyzing);
        public void OnTerminalClicked()
        {
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
            if (sharedMonitorInteractable != null)
            {
                sharedMonitorInteractable.interactionType = InteractionType.Monitor;
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
                terminalInteractable.OnInteracted.RemoveListener(OnTerminalClicked);
                terminalInteractable.OnInteracted.AddListener(OnTerminalClicked);
            }
        }

        private void UnbindInspectorInteractions()
        {
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

            if (!TryGetSharedMonitorZoomPose(out Vector3 zoomPosition, out Quaternion zoomRotation))
            {
                return;
            }

            _isSharedMonitorZoomActive = true;
            cameraController.SetCustomView(zoomPosition, zoomRotation, monitorZoomFieldOfView);
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

        private bool TryGetSharedMonitorZoomPose(out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;

            if (sharedMonitorDisplay == null)
            {
                return false;
            }

            Transform monitorTransform = sharedMonitorDisplay.transform;
            Bounds monitorBounds = new Bounds(monitorTransform.position, Vector3.one);
            bool hasBounds = false;

            if (sharedMonitorDisplay.TryGetComponent(out Renderer renderer))
            {
                monitorBounds = renderer.bounds;
                hasBounds = true;
            }
            else if (sharedMonitorDisplay.TryGetComponent(out Collider collider))
            {
                monitorBounds = collider.bounds;
                hasBounds = true;
            }

            Vector3 focusPoint = hasBounds ? monitorBounds.center : monitorTransform.position;
            focusPoint += monitorTransform.up * monitorZoomVerticalOffset;

            float distance = 2.2f;
            if (hasBounds)
            {
                float cameraAspect = cameraController != null && cameraController.TargetCamera != null
                    ? Mathf.Max(0.1f, cameraController.TargetCamera.aspect)
                    : 16f / 9f;
                float halfHeight = Mathf.Max(0.12f, monitorBounds.extents.y);
                float halfWidthAsHeight = Mathf.Max(0.12f, monitorBounds.extents.x / cameraAspect);
                float halfFrame = Mathf.Max(halfHeight, halfWidthAsHeight);
                float halfFovRad = Mathf.Deg2Rad * Mathf.Clamp(monitorZoomFieldOfView, 10f, 120f) * 0.5f;
                float tanHalfFov = Mathf.Max(0.01f, Mathf.Tan(halfFovRad));
                distance = halfFrame / tanHalfFov * Mathf.Max(1f, monitorZoomFramingPadding);
            }

            distance = Mathf.Clamp(distance, monitorZoomMinDistance, monitorZoomMaxDistance);

            Vector3 normal = monitorTransform.forward.normalized;
            Vector3 candidateA = focusPoint + normal * distance;
            Vector3 candidateB = focusPoint - normal * distance;

            Vector3 currentCameraPosition = cameraController != null && cameraController.TargetCamera != null
                ? cameraController.TargetCamera.transform.position
                : candidateB;
            position = (currentCameraPosition - candidateA).sqrMagnitude <= (currentCameraPosition - candidateB).sqrMagnitude
                ? candidateA
                : candidateB;

            Vector3 lookDirection = focusPoint - position;
            if (lookDirection.sqrMagnitude < 0.0001f)
            {
                return false;
            }

            rotation = Quaternion.LookRotation(lookDirection.normalized, Vector3.up);
            return true;
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
                    OnAdjutantClicked();
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
                        dialogueSystem?.PlaySequence(introSequence);
                    }
                    break;

                case GameState.WaitingForRound:
                    subtitleDisplay?.Hide();
                    _hasOpenedInterpreterThisRound = false;
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
                    aipipelineBridge?.PrepareRoundKeywords();
                    Debug.Log($"[RoundManager] Waiting for round {_currentRound} / {scoreConfig?.totalRounds}.");
                    ShowHint(
                        "System",
                        aipipelineBridge != null
                            ? aipipelineBridge.GetRoundStartAvailabilityMessage()
                            : "Object generator is missing. Assign AIPipelineBridge before starting the round.");
                    break;

                case GameState.Presenting:
                    ShowHint("System", "Generating the alien objects...");
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
                                    string.IsNullOrWhiteSpace(aipipelineBridge.LastObjectGenerationError)
                                        ? "Object generation failed. Click the alien to retry."
                                        : $"Object generation failed: {aipipelineBridge.LastObjectGenerationError}");
                                ReturnToWaitingForRoundAfterPresentingFailure();
                                return;
                            }

                            OnPresentingComplete();
                        });
                    }
                    else
                    {
                        Debug.LogWarning("[RoundManager] AIPipelineBridge is missing. Cannot generate alien objects.");
                        ShowHint("System", "Object generator is missing. Assign AIPipelineBridge before starting the round.");
                        ReturnToWaitingForRoundAfterPresentingFailure();
                    }
                    break;

                case GameState.ObjectPresented:
                    ShowHint("System", "Click the tablet to start drawing.");
                    Debug.Log("[RoundManager] Objects are ready. Waiting for tablet interaction.");
                    break;

                case GameState.Drawing:
                    ShowHint("System", $"Press {exitDrawingKey} when the drawing is ready.");
                    break;

                case GameState.PreviewReady:
                    ShowHint("System", "Click the adjutant for a preview, or click the tablet to keep drawing.");
                    Debug.Log("[RoundManager] Drawing marked complete. Waiting for adjutant preview.");
                    break;

                case GameState.PreviewAnalyzing:
                    subtitleDisplay?.Show("Adjutant", "Let me review the drawing.");
                    if (aipipelineBridge != null)
                    {
                        aipipelineBridge.GetPreview(analysis =>
                        {
                            if (!IsStateCurrent(GameState.PreviewAnalyzing, stateVersion))
                            {
                                return;
                            }

                            ShowPreviewResult(analysis);
                            ChangeState(GameState.Preview);
                        });
                    }
                    else
                    {
                        ShowPreviewResult("(AI analysis unavailable)");
                        ChangeState(GameState.Preview);
                    }
                    break;

                case GameState.Preview:
                    ShowPreviewResult(aipipelineBridge != null ? aipipelineBridge.LastPreviewDialogue : PreviewFallbackText);
                    Debug.Log("[RoundManager] Preview analysis complete. Waiting for submit or modify.");
                    break;

                case GameState.Submitting:
                    ShowHint("System", "Submitting the drawing for judgment...");
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
                        ShowHint("System", TerminalSignalReadyMessage);
                    }
                    else
                    {
                        ShowHint("System", NoSignalMessage);
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
            terminalDisplay?.Clear();
        }

        private void ShowPreviewResult(string analysis)
        {
            string resolvedAnalysis = string.IsNullOrWhiteSpace(analysis)
                ? PreviewFallbackText
                : analysis.Trim();

            subtitleDisplay?.Show("Adjutant", resolvedAnalysis);
        }

        private void ShowHint(string speaker, string text)
        {
            if (subtitleDisplay == null)
            {
                return;
            }

            subtitleDisplay.Show(speaker, text);
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
                GameState.PreviewAnalyzing => CameraMode.FreeLook,
                GameState.Preview => CameraMode.FreeLook,
                GameState.InterpreterReady => CameraMode.FreeLook,
                GameState.Interpreter => CameraMode.TerminalZoom,
                _ => CameraMode.Default
            };

            cameraController.SetMode(mode);
        }
    }
}
