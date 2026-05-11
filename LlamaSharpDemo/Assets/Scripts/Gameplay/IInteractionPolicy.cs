using DoodleDiplomacy.Core;
using DoodleDiplomacy.Interaction;

namespace DoodleDiplomacy.Gameplay
{
    public interface IInteractionPolicy
    {
        bool IsAllowed(InteractionStateContext context, InteractionType interactionType);
    }
}