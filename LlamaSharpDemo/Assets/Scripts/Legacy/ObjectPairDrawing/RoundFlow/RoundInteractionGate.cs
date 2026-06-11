using DoodleDiplomacy.Interaction;
using DoodleDiplomacy.Gameplay;

namespace DoodleDiplomacy.Core
{
    public sealed class RoundInteractionGate
    {
        private readonly IInteractionStateService _interactionState;

        public RoundInteractionGate(IInteractionStateService interactionState)
        {
            _interactionState = interactionState;
        }

        public void Apply(GameState state, bool roundStartReady, bool interpreterInspectionCompleted)
        {
            if (_interactionState == null)
            {
                return;
            }

            var context = new InteractionStateContext(
                state,
                roundStartReady,
                interpreterInspectionCompleted);
            _interactionState.ApplyState(context);
        }
    }
}
