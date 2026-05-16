using DoodleDiplomacy.Core;

namespace DoodleDiplomacy.Gameplay
{
    public interface IGameplayDebugController
    {
        void DebugAdvanceState();
        void DebugJumpToState(GameState target);
    }
}
