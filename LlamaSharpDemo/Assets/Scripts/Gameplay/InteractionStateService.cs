using DoodleDiplomacy.Interaction;

namespace DoodleDiplomacy.Gameplay
{
    public sealed class InteractionStateService : IInteractionStateService
    {
        private readonly InteractionManager _interactionManager;
        private readonly IInteractionPolicy _interactionPolicy;

        public InteractionStateService(InteractionManager interactionManager, IInteractionPolicy interactionPolicy)
        {
            _interactionManager = interactionManager;
            _interactionPolicy = interactionPolicy;
        }

        public void ApplyState(InteractionStateContext context)
        {
            _interactionManager?.ConfigureInteractionPolicy(_interactionPolicy);
            _interactionManager?.ApplyStatePolicy(context);
        }
    }
}
