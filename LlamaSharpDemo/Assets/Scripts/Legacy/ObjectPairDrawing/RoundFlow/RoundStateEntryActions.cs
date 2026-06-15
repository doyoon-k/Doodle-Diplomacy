namespace DoodleDiplomacy.Core
{
    public sealed class RoundStateEntryActions
    {
        private readonly RoundOpeningStateEntryActions _openingActions;
        private readonly RoundPreparationStateEntryActions _preparationActions;
        private readonly RoundPreviewStateEntryActions _previewActions;
        private readonly RoundInterpreterStateEntryActions _interpreterActions;

        internal RoundStateEntryActions(IRoundStateEntryContext context)
        {
            _openingActions = new RoundOpeningStateEntryActions(new RoundOpeningStateEntryContextAdapter(context));
            _preparationActions = new RoundPreparationStateEntryActions(new RoundPreparationStateEntryContextAdapter(context));
            _previewActions = new RoundPreviewStateEntryActions(new RoundPreviewStateEntryContextAdapter(context));
            _interpreterActions = new RoundInterpreterStateEntryActions(new RoundInterpreterStateEntryContextAdapter(context));
        }

        public void Enter(GameState state, int stateVersion)
        {
            switch (state)
            {
                case GameState.Title:
                    _openingActions.EnterTitle();
                    break;
                case GameState.Intro:
                    _openingActions.EnterIntro();
                    break;
                case GameState.Ending:
                    _openingActions.EnterEnding();
                    break;
                case GameState.WaitingForRound:
                    _preparationActions.EnterWaitingForRound();
                    break;
                case GameState.Presenting:
                    _preparationActions.EnterPresenting(stateVersion);
                    break;
                case GameState.ObjectPresented:
                    _preparationActions.EnterObjectPresented();
                    break;
                case GameState.Drawing:
                    _preparationActions.EnterDrawing();
                    break;
                case GameState.PreviewReady:
                    _previewActions.EnterPreviewReady();
                    break;
                case GameState.PreviewAnalyzing:
                    _previewActions.EnterPreviewAnalyzing(stateVersion);
                    break;
                case GameState.Preview:
                    _previewActions.EnterPreview();
                    break;
                case GameState.Submitting:
                    _previewActions.EnterSubmitting(stateVersion);
                    break;
                case GameState.AlienReaction:
                    _interpreterActions.EnterAlienReaction();
                    break;
                case GameState.InterpreterReady:
                    _interpreterActions.EnterInterpreterReady();
                    break;
                case GameState.Interpreter:
                    _interpreterActions.EnterInterpreter();
                    break;
            }
        }
    }
}
