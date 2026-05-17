using System;
using System.Collections;
using System.Collections.Generic;
using DoodleDiplomacy.Camera;
using DoodleDiplomacy.Character;
using DoodleDiplomacy.Core;
using DoodleDiplomacy.Devices;
using DoodleDiplomacy.Interaction;
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

        private static readonly string[] OpeningLines =
        {
            "Mr. President, draw one simple picture on the tablet.",
            "The delegation will only see the completed image. Not the drawing process.",
            "The apparatus will classify the image before transmission. Once the label is confirmed, we will show it to them and record the response."
        };

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
            foreach (string line in OpeningLines)
            {
                yield return Speak(ScienceOfficer, line);
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
            yield return Speak(ScienceOfficer, "The tablet is blank, Mr. President. Please draw one picture by itself.");
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

            yield return Speak(ScienceOfficer, "Scanning drawing. Hold transmission.");
            yield return new WaitUntil(() => done);
            if (version != _scanVersion)
            {
                yield break;
            }

            if (result == null || !result.IsSuccess)
            {
                yield return Speak(ScienceOfficer, "Classification unstable. The response data would be contaminated. Please redraw.");
                BeginDrawing(clearCanvas: true);
                _routine = null;
                yield break;
            }

            string normalizedResultLabel = Day1ReactionTierEvaluator.NormalizeLabel(result.label);
            if (VisualStimulusClassificationResult.LabelIndicatesWrittenText(normalizedResultLabel))
            {
                yield return Speak(ScienceOfficer, "Written text detected. The apparatus cannot calibrate language input this way. Please draw one picture instead.");
                BeginDrawing(clearCanvas: true);
                _routine = null;
                yield break;
            }

            if (result.objectCount != 1)
            {
                string detectedSummary = string.IsNullOrWhiteSpace(normalizedResultLabel)
                    ? "multiple objects"
                    : normalizedResultLabel;
                yield return Speak(ScienceOfficer, $"Multiple objects detected: {detectedSummary}. We cannot calibrate the response this way. Please draw one thing by itself.");
                BeginDrawing(clearCanvas: true);
                _routine = null;
                yield break;
            }

            string label = normalizedResultLabel;
            if (string.IsNullOrWhiteSpace(label))
            {
                yield return Speak(ScienceOfficer, "Classification unstable. The response data would be contaminated. Please redraw.");
                BeginDrawing(clearCanvas: true);
                _routine = null;
                yield break;
            }

            _pendingLabel = label;
            ChangeState(GameState.Preview);
            yield return Speak(ScienceOfficer, $"The apparatus identifies this drawing as: {label}. Confirm the transmission label.");
            stimulusButtonPanel?.ShowConfirmation(ConfirmLabel, RedrawCandidate);
            _routine = null;
        }

        private IEnumerator ClassificationFailedRoutine()
        {
            yield return Speak(ScienceOfficer, "Classification unstable. The response data would be contaminated. Please redraw.");
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
            yield return Speak(ScienceOfficer, "Understood. The previous image will not be transmitted.");
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
            ReactionTier reactionTier = Day1ReactionTierEvaluator.Evaluate(label);
            _context?.Drawing?.SetInteractionLocked(true);
            ChangeState(GameState.Submitting);
            ApplyCameraMode(GameState.Submitting);

            sharedMonitorDisplay?.ShowSubmission(_pendingTexture);
            terminalDisplay?.ShowText(
                $"TRANSMISSION ACTIVE\nDRAWING: {label}\nRESPONSE MONITORING ONLINE",
                instant: true);

            yield return Speak(ScienceOfficer, $"Transmission label confirmed: {label}. Presenting drawing to the delegation.");
            if (transmissionHoldSeconds > 0f)
            {
                yield return new WaitForSeconds(transmissionHoldSeconds);
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

            yield return Speak(ScienceOfficer, "Response pattern logged. Next drawing, Mr. President.");
            _slot++;
            BeginDrawing(clearCanvas: true);
            _routine = null;
        }

        private IEnumerator PlayReactionRoutine(ReactionTier reactionTier)
        {
            if (alienReactionController == null)
            {
                yield return Speak(ScienceOfficer, GetReactionComment(reactionTier));
                yield break;
            }

            bool complete = false;
            UnityEngine.Events.UnityAction handler = () => complete = true;
            alienReactionController.OnReactionComplete.AddListener(handler);
            alienReactionController.PlayReaction(
                MapReactionTierToSatisfaction(reactionTier),
                ScienceOfficer,
                string.Empty,
                GetReactionComment(reactionTier));
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

            yield return Speak(ScienceOfficer, "Calibration set complete. The interpreter now has a preliminary visual-response map.");
            yield return Speak(ScienceOfficer, "These images will remain in the drawing library. We may need them again tomorrow.");
            yield return Speak(Adjutant, "We have enough data for the interpreter.");
            yield return Speak(Adjutant, "I should warn you, Mr. President: they did not simply look at the images. They reacted to your choices.");
            _routine = null;
        }

        private IEnumerator Speak(string speaker, string text)
        {
            _context?.Subtitles?.Show(speaker, text);

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
                ReactionTier.Subtle => SatisfactionLevel.Satisfied,
                ReactionTier.Moderate => SatisfactionLevel.VerySatisfied,
                ReactionTier.Strong => SatisfactionLevel.VerySatisfied,
                _ => SatisfactionLevel.Satisfied
            };
        }

        private static string GetReactionComment(ReactionTier reactionTier)
        {
            return reactionTier switch
            {
                ReactionTier.None => "Minimal response. Logging baseline pattern.",
                ReactionTier.Subtle => "Subtle response from the delegation. Logging association pattern.",
                ReactionTier.Moderate => "Moderate cross-delegate activity. The drawing registered clearly.",
                ReactionTier.Strong => "Strong response. All three delegates reacted to the drawing.",
                _ => "Subtle response from the delegation. Logging association pattern."
            };
        }
    }

}
