using DoodleDiplomacy.Interaction;

namespace DoodleDiplomacy.Gameplay
{
    public interface IInteractionStateService
    {
        void ApplyState(InteractionStateContext context);
    }
}
