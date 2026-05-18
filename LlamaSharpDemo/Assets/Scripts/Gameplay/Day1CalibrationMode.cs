using System;
using System.Collections;
using System.Collections.Generic;
using DoodleDiplomacy.Camera;
using DoodleDiplomacy.Character;
using DoodleDiplomacy.Core;
using DoodleDiplomacy.Devices;
using DoodleDiplomacy.Interaction;
using DoodleDiplomacy.Localization;
using DoodleDiplomacy.UI;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

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

        private static readonly LocalizedLine[] OpeningLines =
        {
            new("day1.opening.1", "Mr. President, draw one simple picture on the tablet."),
            new("day1.opening.2", "The delegation will only see the completed image. Not the drawing process."),
            new("day1.opening.3", "The apparatus will classify the image before transmission. Once the label is confirmed, we will show it to them and record the response.")
        };

        private readonly struct LocalizedLine
        {
            public LocalizedLine(string key, string fallback)
            {
                Key = key;
                Fallback = fallback;
            }

            public string Key { get; }
            public string Fallback { get; }
        }

        [Header("Mode")]
        [SerializeField] private string modeId = Day1ModeId;

        [Header("References")]
        [SerializeField] private Day1StimulusLibrary stimulusLibrary;
        [SerializeField] private Day1StimulusButtonPanel stimulusButtonPanel;
        [SerializeField] private TerminalDisplay terminalDisplay;
        [SerializeField] private SharedMonitorDisplay sharedMonitorDisplay;
        [SerializeField] private AlienReactionController alienReactionController;

        [Header("Timing")]
        [SerializeField, Min(0f)] private float minimumDialogueAdvanceSeconds = 0.15f;
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
            _scanVersion = 0;

            stimulusLibrary?.BeginSession(clearExisting: true);
            terminalDisplay?.Clear();
            sharedMonitorDisplay?.SetIdle();
            stimulusButtonPanel?.Hide();
            _context?.AiGateway?.EnsureLlmPreparation();

            ChangeState(GameState.Intro);
            foreach (LocalizedLine line in OpeningLines)
            {
                yield return Speak(ScienceOfficer, L10n.T(line.Key, line.Fallback));
            }

            BeginDrawing(clearCanvas: true);
            _routine = null;
        }

        private void BeginDrawing(bool clearCanvas)
        {
            _pendingTexture = null;
            _pendingPngBytes = null;
            _pendingLabel = null;

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
            yield return Speak(ScienceOfficer, L10n.T(
                "day1.tablet_blank",
                "The tablet is blank, Mr. President. Please draw one picture by itself."));
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

            yield return Speak(ScienceOfficer, L10n.T(
                "day1.scanning",
                "Scanning drawing. Hold transmission."));
            yield return new WaitUntil(() => done);
            if (version != _scanVersion)
            {
                yield break;
            }

            if (result == null || !result.IsSuccess)
            {
                yield return Speak(ScienceOfficer, L10n.T(
                    "day1.classification_unstable",
                    "Classification unstable. The response data would be contaminated. Please redraw."));
                BeginDrawing(clearCanvas: true);
                _routine = null;
                yield break;
            }

            string normalizedResultLabel = Day1ReactionTierEvaluator.NormalizeLabel(result.label);
            if (result.objectCount <= 0 || Day1StimulusSubmissionPolicy.IsBlockedLabel(normalizedResultLabel))
            {
                yield return Speak(ScienceOfficer, L10n.T(
                    "day1.non_stimulus_detected",
                    "The apparatus cannot calibrate blank marks, simple lines, dots, islands, or basic geometric shapes. Please draw one recognizable object."));
                BeginDrawing(clearCanvas: true);
                _routine = null;
                yield break;
            }

            if (VisualStimulusClassificationResult.LabelIndicatesWrittenText(normalizedResultLabel))
            {
                yield return Speak(ScienceOfficer, L10n.T(
                    "day1.written_text_detected",
                    "Written text detected. The apparatus cannot calibrate language input this way. Please draw one picture instead."));
                BeginDrawing(clearCanvas: true);
                _routine = null;
                yield break;
            }

            if (result.objectCount != 1)
            {
                string detectedSummary = string.IsNullOrWhiteSpace(normalizedResultLabel)
                    ? L10n.Label("multiple objects")
                    : L10n.Label(normalizedResultLabel);
                yield return Speak(ScienceOfficer, L10n.T(
                    "day1.multiple_objects_detected",
                    "Multiple objects detected: {label}. We cannot calibrate the response this way. Please draw one thing by itself.",
                    L10n.Arg("label", detectedSummary)));
                BeginDrawing(clearCanvas: true);
                _routine = null;
                yield break;
            }

            string label = normalizedResultLabel;
            if (string.IsNullOrWhiteSpace(label))
            {
                yield return Speak(ScienceOfficer, L10n.T(
                    "day1.classification_unstable",
                    "Classification unstable. The response data would be contaminated. Please redraw."));
                BeginDrawing(clearCanvas: true);
                _routine = null;
                yield break;
            }

            _pendingLabel = label;
            ChangeState(GameState.Preview);
            yield return Speak(ScienceOfficer, L10n.T(
                "day1.identifies_drawing",
                "The apparatus identifies this drawing as: {label}. Confirm the transmission label.",
                L10n.Arg("label", L10n.Label(label))));
            stimulusButtonPanel?.ShowConfirmation(ConfirmLabel, RedrawCandidate);
            _routine = null;
        }

        private IEnumerator ClassificationFailedRoutine()
        {
            yield return Speak(ScienceOfficer, L10n.T(
                "day1.classification_unstable",
                "Classification unstable. The response data would be contaminated. Please redraw."));
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
            yield return Speak(ScienceOfficer, L10n.T(
                "day1.previous_image_not_transmitted",
                "Understood. The previous image will not be transmitted."));
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
            string displayLabel = L10n.Label(label);
            _context?.Drawing?.SetInteractionLocked(true);
            ChangeState(GameState.Submitting);
            ApplyCameraMode(GameState.Submitting);

            sharedMonitorDisplay?.ShowSubmission(_pendingTexture);
            terminalDisplay?.ShowText(L10n.T(
                    "day1.terminal.transmission_active",
                    "TRANSMISSION ACTIVE\nDRAWING: {label}\nRESPONSE MONITORING ONLINE",
                    L10n.Arg("label", displayLabel)),
                instant: true);

            yield return Speak(ScienceOfficer, L10n.T(
                "day1.transmission_label_confirmed",
                "Transmission label confirmed: {label}. Presenting drawing to the delegation.",
                L10n.Arg("label", displayLabel)));
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
                yield return Speak(ScienceOfficer, L10n.T(
                    "day1.reaction_evaluation_unstable",
                    "Response monitor unstable. Try the transmission again."));
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

            yield return Speak(ScienceOfficer, L10n.T(
                "day1.response_pattern_logged",
                "Response pattern logged. Next drawing, Mr. President."));
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
            string reactionComment = GetReactionComment(reactionTier);

            if (alienReactionController == null)
            {
                yield return Speak(ScienceOfficer, reactionComment);
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

            yield return Speak(ScienceOfficer, L10n.T(
                "day1.complete.1",
                "Calibration set complete. The interpreter now has a preliminary visual-response map."));
            yield return Speak(ScienceOfficer, L10n.T(
                "day1.complete.2",
                "These images will remain in the drawing library. We may need them again tomorrow."));
            yield return Speak(Adjutant, L10n.T(
                "day1.complete.3",
                "We have enough data for the interpreter."));
            yield return Speak(Adjutant, L10n.T(
                "day1.complete.4",
                "I should warn you, Mr. President: they did not simply look at the images. They reacted to your choices."));
            _routine = null;
        }

        private IEnumerator Speak(string speaker, string text)
        {
            _context?.Subtitles?.Show(LocalizeSpeaker(speaker), text);

            yield return null;

            while (IsDialogueAdvancePressed())
            {
                yield return null;
            }

            float elapsed = 0f;
            while (elapsed < minimumDialogueAdvanceSeconds)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            while (!GetDialogueAdvanceThisFrame())
            {
                yield return null;
            }
        }

        private static bool GetDialogueAdvanceThisFrame()
        {
#if ENABLE_INPUT_SYSTEM
            bool mousePressed = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
            bool keyboardPressed = Keyboard.current != null &&
                                   (Keyboard.current.spaceKey.wasPressedThisFrame ||
                                    Keyboard.current.enterKey.wasPressedThisFrame);
            return mousePressed || keyboardPressed;
#else
            return Input.GetMouseButtonDown(0) ||
                   Input.GetKeyDown(KeyCode.Space) ||
                   Input.GetKeyDown(KeyCode.Return);
#endif
        }

        private static bool IsDialogueAdvancePressed()
        {
#if ENABLE_INPUT_SYSTEM
            bool mousePressed = Mouse.current != null && Mouse.current.leftButton.isPressed;
            bool keyboardPressed = Keyboard.current != null &&
                                   (Keyboard.current.spaceKey.isPressed ||
                                    Keyboard.current.enterKey.isPressed);
            return mousePressed || keyboardPressed;
#else
            return Input.GetMouseButton(0) ||
                   Input.GetKey(KeyCode.Space) ||
                   Input.GetKey(KeyCode.Return);
#endif
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

        private static string GetReactionComment(ReactionTier reactionTier)
        {
            return reactionTier switch
            {
                ReactionTier.None => L10n.T(
                    "day1.reaction.none",
                    "Minimal response. Logging baseline pattern."),
                ReactionTier.Subtle => L10n.T(
                    "day1.reaction.subtle",
                    "Subtle response from the delegation. Logging association pattern."),
                ReactionTier.Moderate => L10n.T(
                    "day1.reaction.moderate",
                    "Moderate cross-delegate activity. The drawing registered clearly."),
                ReactionTier.Strong => L10n.T(
                    "day1.reaction.strong",
                    "Strong response. All three delegates reacted to the drawing."),
                _ => L10n.T(
                    "day1.reaction.subtle",
                    "Subtle response from the delegation. Logging association pattern.")
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
    }

}
