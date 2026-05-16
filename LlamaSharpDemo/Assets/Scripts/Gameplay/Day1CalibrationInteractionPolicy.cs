using DoodleDiplomacy.Core;
using DoodleDiplomacy.Interaction;

namespace DoodleDiplomacy.Gameplay
{
    public sealed class Day1CalibrationInteractionPolicy : IInteractionPolicy
    {
        public bool IsAllowed(InteractionStateContext context, InteractionType interactionType)
        {
            return context.State switch
            {
                GameState.Drawing => interactionType == InteractionType.Tablet,
                GameState.Preview => interactionType == InteractionType.Tablet,
                GameState.Submitting => false,
                GameState.AlienReaction => false,
                GameState.Ending => false,
                _ => false
            };
        }
    }
}
