using DoodleDiplomacy.Interaction;

namespace DoodleDiplomacy.Gameplay
{
    public sealed class LegacyRoundInteractionPolicy : IInteractionPolicy
    {
        public bool IsAllowed(InteractionStateContext context, InteractionType interactionType)
        {
            return InteractionStatePolicy.IsAllowed(context, interactionType);
        }
    }
}