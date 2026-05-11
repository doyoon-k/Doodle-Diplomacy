using DoodleDiplomacy.Core;
using DoodleDiplomacy.Interaction;

namespace DoodleDiplomacy.Gameplay
{
    public interface IGameplayMode
    {
        string ModeId { get; }
        GameState CurrentState { get; }

        void Enter(GameplayModeContext context);
        void Exit();
        void HandleInteraction(InteractionType type, InteractableObject source);
        void Tick(float deltaTime);
    }
}