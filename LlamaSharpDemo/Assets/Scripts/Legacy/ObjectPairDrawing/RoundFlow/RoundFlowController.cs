using System;
using UnityEngine;

namespace DoodleDiplomacy.Core
{
    internal sealed class RoundFlowController
    {
        private readonly RoundStateMachine _stateMachine;
        private readonly Func<RoundStateEntryActions> _getEntryActions;
        private readonly Action<GameState> _onExitState;
        private readonly Action _clearSharedMonitorZoom;
        private readonly Action<GameState> _applyDrawingState;
        private readonly Action _applyInteractionPolicy;
        private readonly Action<GameState> _applyCameraMode;
        private readonly Action<GameState> _publishStateChanged;

        public RoundFlowController(
            GameState initialState,
            Func<RoundStateEntryActions> getEntryActions,
            Action<GameState> onExitState,
            Action clearSharedMonitorZoom,
            Action<GameState> applyDrawingState,
            Action applyInteractionPolicy,
            Action<GameState> applyCameraMode,
            Action<GameState> publishStateChanged)
        {
            _stateMachine = new RoundStateMachine(initialState);
            _getEntryActions = getEntryActions;
            _onExitState = onExitState;
            _clearSharedMonitorZoom = clearSharedMonitorZoom;
            _applyDrawingState = applyDrawingState;
            _applyInteractionPolicy = applyInteractionPolicy;
            _applyCameraMode = applyCameraMode;
            _publishStateChanged = publishStateChanged;
        }

        public GameState CurrentState => _stateMachine.CurrentState;

        public bool IsCurrent(GameState state, int stateVersion)
        {
            return _stateMachine.IsCurrent(state, stateVersion);
        }

        public void TryTransition(GameState required, GameState next)
        {
            if (CurrentState == required)
            {
                ChangeState(next);
            }
        }

        public void ChangeState(GameState newState)
        {
            if (!_stateMachine.CanChangeTo(newState))
            {
                return;
            }

            _clearSharedMonitorZoom?.Invoke();

            GameState oldState = CurrentState;
            _onExitState?.Invoke(oldState);

            RoundStateTransition transition = _stateMachine.MoveTo(newState);
            Debug.Log($"[RoundManager] State: {transition.OldState} -> {transition.NewState}");

            _applyDrawingState?.Invoke(transition.NewState);
            _getEntryActions?.Invoke()?.Enter(transition.NewState, transition.Version);
            if (!IsCurrent(transition.NewState, transition.Version))
            {
                return;
            }

            _publishStateChanged?.Invoke(transition.NewState);
            _applyInteractionPolicy?.Invoke();
            _applyCameraMode?.Invoke(transition.NewState);
        }
    }
}
