using DoodleDiplomacy.Interaction;

namespace DoodleDiplomacy.Gameplay
{
    public sealed class ObjectPairDrawingInteractionPolicy : IInteractionPolicy
    {
        public bool IsAllowed(InteractionStateContext context, InteractionType interactionType)
        {
            return InteractionStatePolicy.IsAllowed(context, interactionType);
        }
    }
}
