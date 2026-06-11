namespace DoodleDiplomacy.Core
{
    public readonly struct RoundStateTransition
    {
        public RoundStateTransition(GameState oldState, GameState newState, int version)
        {
            OldState = oldState;
            NewState = newState;
            Version = version;
        }

        public GameState OldState { get; }
        public GameState NewState { get; }
        public int Version { get; }
    }

    public sealed class RoundStateMachine
    {
        public RoundStateMachine(GameState initialState)
        {
            CurrentState = initialState;
        }

        public GameState CurrentState { get; private set; }
        public int StateVersion { get; private set; }

        public bool CanChangeTo(GameState newState)
        {
            return CurrentState != newState;
        }

        public RoundStateTransition MoveTo(GameState newState)
        {
            GameState oldState = CurrentState;
            StateVersion++;
            CurrentState = newState;
            return new RoundStateTransition(oldState, newState, StateVersion);
        }

        public bool IsCurrent(GameState state, int version)
        {
            return CurrentState == state && StateVersion == version;
        }
    }
}