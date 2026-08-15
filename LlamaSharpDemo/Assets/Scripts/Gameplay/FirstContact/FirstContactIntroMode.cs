using System;
using System.Collections;
using DoodleDiplomacy.Core;
using DoodleDiplomacy.Interaction;
using UnityEngine;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    [DisallowMultipleComponent]
    public sealed class FirstContactIntroMode : MonoBehaviour,
        IGameplayMode,
        IGameplayModeExitHandler,
        IGameplaySessionController,
        IGameplayStateObservable
    {
        [SerializeField] private string modeId = "first-contact-intro";
        [SerializeField] private FirstContactIntroSceneReferences sceneReferences;
        [SerializeField] private FirstContactIntroSequenceController sequenceController;

        private GameplayModeContext _context;
        private GameState _currentState = GameState.Title;
        private FirstContactIntroSceneInstaller _sceneInstaller;
        private FirstContactTranslationMode _briefingPracticeMode;
        private bool _briefingPracticeRunning;
        private bool _briefingPracticePausedForBriefing;
        private bool _startWithBriefingPractice = true;
        private bool _briefingPracticeCompletedSuccessfully;

        public event Action<GameState> StateChanged;

        public string ModeId => string.IsNullOrWhiteSpace(modeId) ? "first-contact-intro" : modeId.Trim();
        public GameState CurrentState => _currentState;
        public FirstContactIntroSceneReferences SceneReferences => sceneReferences;
        public FirstContactIntroSequenceController SequenceController => sequenceController;
        public bool EnteredFromPreloadedScene { get; private set; }
        public bool BriefingPracticeCompletedSuccessfully =>
            _briefingPracticeCompletedSuccessfully;
        public bool BriefingPracticeInterludeReady =>
            _briefingPracticePausedForBriefing &&
            _briefingPracticeMode?.IsBriefingPracticeInterludeReady == true;

        public void Configure(
            string id,
            FirstContactIntroSceneReferences references,
            FirstContactIntroSequenceController sequence)
        {
            modeId = id;
            sceneReferences = references;
            sequenceController = sequence;
        }

        public void Enter(GameplayModeContext context)
        {
            _context = context;
            EnteredFromPreloadedScene =
                context?.Services.TryGet(out GameplaySceneTransitionContext transition) == true &&
                transition.WasPreloaded;
            ChangeState(GameState.Intro);
        }

        public void Exit()
        {
            Exit(GameplayModeExitReason.Cancelled);
        }

        public void Exit(GameplayModeExitReason reason)
        {
            CancelBriefingFoodPractice();
            sequenceController?.Stop(
                reason == GameplayModeExitReason.Completed
                    ? FirstContactSequenceExitDisposition.CommitForGameplayHandoff
                    : FirstContactSequenceExitDisposition.ResetToEntryState);
            _context = null;
            EnteredFromPreloadedScene = false;
            ChangeState(GameState.Title);
        }

        public void Tick(float deltaTime)
        {
            if (_briefingPracticeRunning &&
                !_briefingPracticePausedForBriefing &&
                _briefingPracticeMode != null)
            {
                _briefingPracticeMode.Tick(deltaTime);
            }
        }

        public void HandleInteraction(InteractionType type, InteractableObject source)
        {
            if (_briefingPracticeRunning && !_briefingPracticePausedForBriefing)
            {
                _briefingPracticeMode?.HandleInteraction(type, source);
            }
        }

        public void StartGame(bool isFirstPlay = true)
        {
            _startWithBriefingPractice = isFirstPlay;
            ChangeState(GameState.Intro);
            sequenceController?.Begin();
        }

        public void CompleteSegment()
        {
            if (_context?.Services.TryGet(out IGameFlowController flow) == true)
            {
                flow.CompleteCurrentEntry();
            }
#if UNITY_EDITOR
            else if (GetComponent<FirstContactIntroSceneInstaller>()
                     ?.TryStartEmbeddedGameplayForDirectPreview() == true)
            {
                return;
            }
#endif
            else
            {
                Debug.LogWarning("[FirstContactIntroMode] No game flow controller is available.", this);
            }
        }

        public void PreloadNextSegment()
        {
            if (_context?.Services.TryGet(out IGameFlowPreloader preloader) == true)
            {
                preloader.PreloadNextEntry();
            }
        }

        public void ChangeToTitle()
        {
            CancelBriefingFoodPractice();
            sequenceController?.Stop();
            ChangeState(GameState.Title);
        }

        public void SubmitPreview()
        {
            if (_briefingPracticeRunning && !_briefingPracticePausedForBriefing)
            {
                _briefingPracticeMode?.SubmitPreview();
            }
        }

        public void ModifyPreview()
        {
            if (_briefingPracticeRunning && !_briefingPracticePausedForBriefing)
            {
                _briefingPracticeMode?.ModifyPreview();
            }
        }

        public bool ShouldRunBriefingFoodPractice()
        {
            ResolveBriefingPracticeReferences();
            return _startWithBriefingPractice &&
                   _briefingPracticeMode != null &&
                   _briefingPracticeMode.BriefingFoodPracticeEnabled;
        }

        public IEnumerator PlayBriefingFoodPracticeFirstProbeRoutine()
        {
            _briefingPracticeCompletedSuccessfully = false;
            _briefingPracticePausedForBriefing = false;
            ResolveBriefingPracticeReferences();
            if (_sceneInstaller == null || _briefingPracticeMode == null)
            {
                Debug.LogError(
                    "[FirstContactIntroMode] Briefing FOOD practice references are unavailable.",
                    this);
                yield break;
            }

            if (!_briefingPracticeMode.BriefingFoodPracticeEnabled)
            {
                yield break;
            }

            if (!_sceneInstaller.TryBeginBriefingPractice(
                    out GameplayModeContext practiceContext,
                    out FirstContactTranslationMode translationMode))
            {
                yield break;
            }

            _briefingPracticeMode = translationMode;
            _briefingPracticeRunning = true;
            _briefingPracticeMode.StateChanged += HandleBriefingPracticeStateChanged;

            // Give the embedded runtime, cameras, and LLM pipeline one frame to
            // finish OnEnable before the shared probe session resolves them.
            yield return null;
            if (!_briefingPracticeMode.BeginBriefingFoodPractice(practiceContext))
            {
                CompleteBriefingPracticeSession();
                yield break;
            }

            while (_briefingPracticeMode.IsBriefingPracticeActive &&
                   !_briefingPracticeMode.IsBriefingPracticeInterludeReady)
            {
                yield return null;
            }

            if (_briefingPracticeMode.IsBriefingPracticeInterludeReady)
            {
                if (!_sceneInstaller.SuspendBriefingPracticePresentation())
                {
                    Debug.LogError(
                        "[FirstContactIntroMode] Failed to suspend briefing practice presentation.",
                        this);
                    CompleteBriefingPracticeSession();
                    yield break;
                }

                _briefingPracticePausedForBriefing = true;
                if (_context != null)
                {
                    ChangeState(GameState.Intro);
                }

                yield break;
            }

            _briefingPracticeCompletedSuccessfully =
                _briefingPracticeMode.IsBriefingPracticeComplete;
            CompleteBriefingPracticeSession();
        }

        public IEnumerator ResumeBriefingFoodPracticeRoutine()
        {
            if (!_briefingPracticeRunning ||
                !_briefingPracticePausedForBriefing ||
                _sceneInstaller == null ||
                _briefingPracticeMode == null)
            {
                Debug.LogError(
                    "[FirstContactIntroMode] Briefing FOOD practice is not ready to resume.",
                    this);
                yield break;
            }

            if (!_sceneInstaller.ResumeBriefingPracticePresentation())
            {
                Debug.LogError(
                    "[FirstContactIntroMode] Failed to restore briefing practice presentation.",
                    this);
                CompleteBriefingPracticeSession();
                yield break;
            }

            _briefingPracticePausedForBriefing = false;
            if (!_briefingPracticeMode.ResumeBriefingFoodPractice())
            {
                Debug.LogError(
                    "[FirstContactIntroMode] Briefing FOOD practice pipeline could not resume.",
                    this);
                CompleteBriefingPracticeSession();
                yield break;
            }

            while (_briefingPracticeMode.IsBriefingPracticeActive)
            {
                yield return null;
            }

            _briefingPracticeCompletedSuccessfully =
                _briefingPracticeMode.IsBriefingPracticeComplete;
            CompleteBriefingPracticeSession();
        }

        private void CancelBriefingFoodPractice()
        {
            CompleteBriefingPracticeSession();
        }

        private void CompleteBriefingPracticeSession()
        {
            if (_briefingPracticeMode != null)
            {
                _briefingPracticeMode.StateChanged -= HandleBriefingPracticeStateChanged;
                _briefingPracticeMode.EndBriefingFoodPractice();
            }

            _sceneInstaller?.EndBriefingPractice();
            _briefingPracticeRunning = false;
            _briefingPracticePausedForBriefing = false;
            if (_context != null)
            {
                ChangeState(GameState.Intro);
            }
        }

        private void ResolveBriefingPracticeReferences()
        {
            _sceneInstaller = _sceneInstaller != null
                ? _sceneInstaller
                : GetComponent<FirstContactIntroSceneInstaller>();
            _briefingPracticeMode = _briefingPracticeMode != null
                ? _briefingPracticeMode
                : _sceneInstaller?.EmbeddedTranslationMode;
        }

        private void HandleBriefingPracticeStateChanged(GameState state)
        {
            if (_briefingPracticeRunning)
            {
                ChangeState(state);
            }
        }

        private void ChangeState(GameState state)
        {
            if (_currentState == state)
            {
                return;
            }

            _currentState = state;
            StateChanged?.Invoke(state);
        }
    }
}
