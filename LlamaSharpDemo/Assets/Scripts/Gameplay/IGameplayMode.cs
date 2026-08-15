using DoodleDiplomacy.Core;
using DoodleDiplomacy.Interaction;

namespace DoodleDiplomacy.Gameplay
{
    public enum GameplayModeExitReason
    {
        Cancelled,
        Completed,
        Replaced,
        HostDestroyed
    }

    public interface IGameplayMode
    {
        string ModeId { get; }
        GameState CurrentState { get; }

        void Enter(GameplayModeContext context);
        void Exit();
        void HandleInteraction(InteractionType type, InteractableObject source);
        void Tick(float deltaTime);
    }

    /// <summary>
    /// Optional lifecycle contract for modes that need to distinguish a normal
    /// flow handoff from cancellation or teardown. Legacy modes continue to use
    /// <see cref="IGameplayMode.Exit"/>.
    /// </summary>
    public interface IGameplayModeExitHandler
    {
        void Exit(GameplayModeExitReason reason);
    }
}
