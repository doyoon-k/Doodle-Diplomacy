using DoodleDiplomacy.Core;

namespace DoodleDiplomacy.Interaction
{
    public readonly struct InteractionStateContext
    {
        public InteractionStateContext(GameState state, bool roundStartReady, bool interpreterInspectionCompleted)
        {
            State = state;
            RoundStartReady = roundStartReady;
            InterpreterInspectionCompleted = interpreterInspectionCompleted;
        }

        public GameState State { get; }
        public bool RoundStartReady { get; }
        public bool InterpreterInspectionCompleted { get; }
    }
}
