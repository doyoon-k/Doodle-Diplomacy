using System;
using DoodleDiplomacy.Core;
using DoodleDiplomacy.Interaction;
using UnityEngine;

namespace DoodleDiplomacy.Gameplay
{
    public class LegacyRoundModeAdapter : MonoBehaviour, IGameplayMode, IGameplayStateObservable
    {
        [SerializeField] private string modeId = "legacy-round";
        [SerializeField] private RoundManager roundManager;

        public string ModeId => string.IsNullOrWhiteSpace(modeId) ? "legacy-round" : modeId;
        public GameState CurrentState => roundManager != null ? roundManager.CurrentState : GameState.Title;
        public event Action<GameState> StateChanged;

        public void Enter(GameplayModeContext context)
        {
            roundManager = roundManager != null ? roundManager : context?.RoundManager;
            if (roundManager == null)
            {
                Debug.LogError("[LegacyRoundModeAdapter] RoundManager reference is missing.", this);
                return;
            }

            roundManager.ConfigureDrawingFeature(context?.Drawing);
            roundManager.ConfigureCameraModeService(context?.Camera);
            roundManager.ConfigureInteractionStateService(context?.InteractionState);
            roundManager.OnStateChanged.AddListener(HandleRoundStateChanged);
            StateChanged?.Invoke(roundManager.CurrentState);
        }

        public void Exit()
        {
            if (roundManager != null)
            {
                roundManager.OnStateChanged.RemoveListener(HandleRoundStateChanged);
            }
        }

        public void HandleInteraction(InteractionType type, InteractableObject source)
        {
            if (roundManager == null)
            {
                Debug.LogError("[LegacyRoundModeAdapter] Cannot handle interaction because RoundManager is missing.", this);
                return;
            }

            switch (type)
            {
                case InteractionType.Alien:
                    roundManager.OnAlienClicked();
                    break;
                case InteractionType.Tablet:
                    roundManager.OnTabletClicked();
                    break;
                case InteractionType.Adjutant:
                    roundManager.OnAdjutantClicked();
                    break;
                case InteractionType.Terminal:
                    roundManager.OnTerminalClicked();
                    break;
                case InteractionType.Monitor:
                    roundManager.OnSharedMonitorClicked();
                    break;
                default:
                    Debug.LogWarning($"[LegacyRoundModeAdapter] Unhandled interaction type: {type}.", source);
                    break;
            }
        }

        public void Tick(float deltaTime)
        {
        }

        private void OnDisable()
        {
            Exit();
        }

        private void HandleRoundStateChanged(GameState state)
        {
            StateChanged?.Invoke(state);
        }
    }
}
