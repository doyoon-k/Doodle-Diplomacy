namespace DoodleDiplomacy.Core
{
    public static class RoundDebugTransitionPolicy
    {
        public static bool TryGetNextState(GameState currentState, out GameState nextState)
        {
            switch (currentState)
            {
                case GameState.Intro:
                    nextState = GameState.WaitingForRound;
                    return true;
                case GameState.PreviewReady:
                    nextState = GameState.PreviewAnalyzing;
                    return true;
                case GameState.PreviewAnalyzing:
                    nextState = GameState.Preview;
                    return true;
                default:
                    nextState = currentState;
                    return false;
            }
        }
    }
}
