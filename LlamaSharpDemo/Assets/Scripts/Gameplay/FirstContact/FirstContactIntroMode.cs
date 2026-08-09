using System;
using DoodleDiplomacy.Core;
using DoodleDiplomacy.Interaction;
using UnityEngine;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    [DisallowMultipleComponent]
    public sealed class FirstContactIntroMode : MonoBehaviour,
        IGameplayMode,
        IGameplaySessionController,
        IGameplayStateObservable
    {
        [SerializeField] private string modeId = "first-contact-intro";
        [SerializeField] private FirstContactIntroSceneReferences sceneReferences;
        [SerializeField] private FirstContactIntroSequenceController sequenceController;

        private GameplayModeContext _context;
        private GameState _currentState = GameState.Title;

        public event Action<GameState> StateChanged;

        public string ModeId => string.IsNullOrWhiteSpace(modeId) ? "first-contact-intro" : modeId.Trim();
        public GameState CurrentState => _currentState;
        public FirstContactIntroSceneReferences SceneReferences => sceneReferences;
        public FirstContactIntroSequenceController SequenceController => sequenceController;
        public bool EnteredFromPreloadedScene { get; private set; }

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
            sequenceController?.Stop();
            _context = null;
            EnteredFromPreloadedScene = false;
            ChangeState(GameState.Title);
        }

        public void Tick(float deltaTime)
        {
        }

        public void HandleInteraction(InteractionType type, InteractableObject source)
        {
        }

        public void StartGame(bool isFirstPlay = true)
        {
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
            sequenceController?.Stop();
            ChangeState(GameState.Title);
        }

        public void SubmitPreview()
        {
        }

        public void ModifyPreview()
        {
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
