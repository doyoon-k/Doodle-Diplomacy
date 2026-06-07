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

        public string ModeId => string.IsNullOrWhiteSpace(modeId) ? DefaultModeId : modeId.Trim();
        public GameState CurrentState => _currentGameState;

        public void Enter(GameplayModeContext context)
        {
            _context = context;
            ResolveRuntimeServices();
            _context?.Drawing?.SetInteractionLocked(true);
            _context?.Drawing?.ClearRecognitionLabel();
            actionButtonPanel?.Hide();
            _terminalPresenter?.Clear();
            ApplyInteractionPolicy();
            ChangeState(FirstContactModeState.Ready);
        }

        public void Exit()
        {
            StopActiveRoutine();
            _context?.AiGateway?.CancelActiveOperations();
            _context?.Drawing?.SetInteractionLocked(true);
            _context?.Drawing?.ClearRecognitionLabel();
            actionButtonPanel?.Hide();
            _terminalPresenter?.Clear();
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

        public void Tick(float deltaTime) { }

        public void HandleInteraction(InteractionType type, InteractableObject source)
        {
            if (type == InteractionType.Tablet &&
                (_modeState == FirstContactModeState.DrawingDecodeSample ||
                 _modeState == FirstContactModeState.DrawingAnswer ||
                 _modeState == FirstContactModeState.ReviewingLabel))
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
            _terminalPresenter?.Clear();
            ChangeState(FirstContactModeState.Ready);
        }

        public void SubmitPreview()
        {
            SubmitDrawing();
        }

        public void ModifyPreview()
        {
            RedrawPending();
        }

        private IEnumerator StartFirstContactRoutine()
        {
            ResolveRuntimeServices();
            _fallbackQuestionIndex = 0;
            _runtimeWaveformSessionSeed = UnityEngine.Random.Range(1, int.MaxValue);
            _session = new FirstContactSessionContext();
            _semanticMemory = new FirstContactSemanticMemory(
                _embeddingService,
                GetSemanticSettings(),
                GetDebugSettings());
            _context?.Drawing?.SetInteractionLocked(true);
            _context?.Drawing?.ClearRecognitionLabel();
            _context?.SharedMonitorDisplay?.SetIdle();
            _terminalPresenter?.Clear();
            yield return LoadNextQuestionRoutine();
            _routine = null;
        }

        private IEnumerator LoadNextQuestionRoutine()
        {
            ChangeState(FirstContactModeState.ReceivingQuestion);
            actionButtonPanel?.Hide();
            _context?.Drawing?.SetInteractionLocked(true);
            _context?.Drawing?.ClearRecognitionLabel();
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
            actionButtonPanel?.ShowQuestionActions(question, BeginDecodeSample, BeginAnswerDrawing);
        }

        private void BeginDecodeSample(string unknownId)
        {
            if (_session?.CurrentQuestion == null || _session.CurrentQuestion.FindUnknown(unknownId) == null)
            {
                return;
            }

            StopActiveRoutine();
            _routine = StartCoroutine(BeginDrawingRoutine(
                FirstContactCardSource.DecodeSample,
                unknownId,
                $"DECODE SAMPLE: {FirstContactUnknownSlotDefinition.NormalizeUnknownId(unknownId)}"));
        }

        private void BeginAnswerDrawing()
        {
            StopActiveRoutine();
            _routine = StartCoroutine(BeginDrawingRoutine(
                FirstContactCardSource.Answer,
                string.Empty,
                "TRANSMIT ANSWER"));
        }

        private IEnumerator BeginDrawingRoutine(
            FirstContactCardSource source,
            string unknownId,
            string prompt)
        {
            _pendingCardSource = source;
            _activeUnknownId = FirstContactUnknownSlotDefinition.NormalizeUnknownId(unknownId);
            _pendingTexture = null;
            _pendingPngBytes = null;
            _pendingLabel = string.Empty;
            _pendingDisplayLabel = string.Empty;
            actionButtonPanel?.Hide();
            _context?.Drawing?.ClearCanvas();
            _context?.Drawing?.ClearRecognitionLabel();
            _context?.Drawing?.EnsureRuntimeEnabled();
            _context?.Drawing?.SetInteractionLocked(false);
            _context?.Camera?.SetMode(CameraMode.TabletView);
            if (source == FirstContactCardSource.DecodeSample)
            {
                _terminalPresenter?.ShowDecodeTarget(_session?.CurrentQuestion, _activeUnknownId);
                ChangeState(FirstContactModeState.DrawingDecodeSample);
            }
            else
            {
                ChangeState(FirstContactModeState.DrawingAnswer);
            }

            yield return null;
            actionButtonPanel?.ShowSubmit(prompt, SubmitDrawing);
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
                actionButtonPanel?.ShowSubmit("REDRAW REQUIRED", SubmitDrawing);
                return;
            }

            if (!_context.Drawing.TryExportPngBytes(out byte[] pngBytes, out string error) ||
                pngBytes == null ||
                pngBytes.Length == 0)
            {
                Debug.LogWarning($"[FirstContactTranslationMode] Failed to export drawing: {error}", this);
                actionButtonPanel?.ShowSubmit("REDRAW REQUIRED", SubmitDrawing);
                return;
            }

            Texture2D texture = CreateTextureFromPng(pngBytes);
            if (texture == null)
            {
                actionButtonPanel?.ShowSubmit("REDRAW REQUIRED", SubmitDrawing);
                return;
            }

            _pendingPngBytes = pngBytes;
            _pendingTexture = texture;
            _context.Drawing.SetInteractionLocked(true);
            actionButtonPanel?.Hide();
            StopActiveRoutine();
            _routine = StartCoroutine(AnalyzeDrawingRoutine());
        }

        private IEnumerator AnalyzeDrawingRoutine()
        {
            ChangeState(FirstContactModeState.AnalyzingDrawing);
            FirstContactPresentationSettings presentation = GetPresentationSettings();
            float startTime = Time.time;

            VisualStimulusClassificationResult classification = null;
            bool done = false;
            yield return ClassifyPendingDrawing(result =>
            {
                classification = result;
                done = true;
            });
            while (!done)
            {
                yield return null;
            }

            float elapsed = Time.time - startTime;
            if (elapsed < presentation.scanMinimumSeconds)
            {
                yield return new WaitForSeconds(presentation.scanMinimumSeconds - elapsed);
            }

            if (!IsUsableClassification(classification))
            {
                _context?.Drawing?.SetInteractionLocked(false);
                _context?.Drawing?.ClearRecognitionLabel();
                actionButtonPanel?.ShowSubmit("REDRAW REQUIRED", SubmitDrawing);
                ChangeState(_pendingCardSource == FirstContactCardSource.Answer
                    ? FirstContactModeState.DrawingAnswer
                    : FirstContactModeState.DrawingDecodeSample);
                _routine = null;
                yield break;
            }

            _pendingLabel = Day1ReactionTierEvaluator.NormalizeLabel(classification.label);
            _pendingDisplayLabel = ResolveDisplayLabel(classification, _pendingLabel);
            if (presentation.labelRevealDelay > 0f)
            {
                yield return new WaitForSeconds(presentation.labelRevealDelay);
            }

            _context?.Drawing?.ShowRecognitionLabel(_pendingDisplayLabel);
            ChangeState(FirstContactModeState.ReviewingLabel);
            string prompt = _pendingCardSource == FirstContactCardSource.Answer
                ? "TRANSMIT ANSWER"
                : $"DECODE SAMPLE: {_activeUnknownId}";
            actionButtonPanel?.ShowConfirmation(prompt, _pendingDisplayLabel, ConfirmPendingDrawing, RedrawPending);
            _routine = null;
        }

        private IEnumerator ClassifyPendingDrawing(Action<VisualStimulusClassificationResult> onComplete)
        {
            FirstContactVlmSettings vlmSettings = GetVlmSettings();
            if (vlmSettings.visualClassifierPipeline != null && GamePipelineRunner.Instance != null && _pendingTexture != null)
            {
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
            actionButtonPanel?.Hide();
            _context?.Drawing?.SetInteractionLocked(true);
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
            if (presentation.waveformLockSeconds > 0f)
            {
                yield return new WaitForSeconds(presentation.waveformLockSeconds);
            }

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
            if (GetDebugSettings().logSimilarityScores && slot != null)
            {
                Debug.Log(
                    $"[FirstContactTranslationMode] Decode score card={card.Label} slot={slot.Id} " +
                    $"stage={result.NewStage} score={result.Score:0.000}");
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
            actionButtonPanel?.ShowQuestionActions(_session.CurrentQuestion, BeginDecodeSample, BeginAnswerDrawing);
        }

        private IEnumerator TransmitAnswerCardRoutine(SemanticCardRecord card)
        {
            _session.PreviousAnswer = card;
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
            _context?.Drawing?.ClearRecognitionLabel();
            _routine = StartCoroutine(BeginDrawingRoutine(
                _pendingCardSource,
                _activeUnknownId,
                _pendingCardSource == FirstContactCardSource.Answer
                    ? "TRANSMIT ANSWER"
                    : $"DECODE SAMPLE: {_activeUnknownId}"));
        }

        private bool IsUsableClassification(VisualStimulusClassificationResult result)
        {
            if (result == null || !result.IsSuccess)
            {
                return false;
            }

            FirstContactVlmSettings vlmSettings = GetVlmSettings();
            string normalized = Day1ReactionTierEvaluator.NormalizeLabel(result.label);
            if (vlmSettings.rejectActionOrScene && Day1StimulusSubmissionPolicy.IsActionOrSceneLabel(normalized))
            {
                return false;
            }

            if (vlmSettings.rejectWrittenText && Day1StimulusSubmissionPolicy.IsWrittenTextLabel(normalized))
            {
                return false;
            }

            if (vlmSettings.rejectBlank && Day1StimulusSubmissionPolicy.IsBlockedLabel(normalized))
            {
                return false;
            }

            if (vlmSettings.rejectMultipleObjects && !Day1StimulusSubmissionPolicy.IsAllowedObjectCount(result.objectCount, normalized))
            {
                return false;
            }

            return !string.IsNullOrWhiteSpace(normalized);
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
                actionButtonPanel = GetComponent<FirstContactActionButtonPanel>() ??
                                    gameObject.AddComponent<FirstContactActionButtonPanel>();
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

        private void ChangeState(FirstContactModeState state)
        {
            _modeState = state;
            GameState gameState = state switch
            {
                FirstContactModeState.DrawingDecodeSample => GameState.Drawing,
                FirstContactModeState.DrawingAnswer => GameState.Drawing,
                FirstContactModeState.AnalyzingDrawing => GameState.PreviewAnalyzing,
                FirstContactModeState.ReviewingLabel => GameState.Preview,
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
            UpdatingTranslation,
            TransmittingAnswer,
            Completed
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
