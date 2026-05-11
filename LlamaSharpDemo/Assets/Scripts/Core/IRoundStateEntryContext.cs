using System;
using DoodleDiplomacy.Character;
using DoodleDiplomacy.Data;
using DoodleDiplomacy.Devices;
using DoodleDiplomacy.Dialogue;
using DoodleDiplomacy.Ending;
using DoodleDiplomacy.UI;

namespace DoodleDiplomacy.Core
{
    internal interface IRoundStateEntryContext
    {
        IRoundAiGateway AiGateway { get; }
        ScoreManager ScoreManager { get; }
        ScoreConfig ScoreConfig { get; }
        DialogueSystem DialogueSystem { get; }
        DialogueSequence IntroSequence { get; }
        DialogueSequence RuntimeIntroSequence { get; }
        TerminalDisplay TerminalDisplay { get; }
        AlienReactionController AlienReactionController { get; }
        EndingController EndingController { get; }
        TitleScreenController TitleScreenController { get; }
        RoundHintPresenter HintPresenter { get; }
        RoundDrawingInteractionGate DrawingInteractionGate { get; }

        int CurrentRound { get; set; }
        bool PreserveRoundIndexOnNextWaitingState { get; set; }
        bool HasOpenedInterpreterThisRound { get; set; }
        bool IsPreviewTerminalOpen { get; set; }
        SatisfactionLevel LastSatisfaction { get; set; }

        bool IsStateCurrent(GameState state, int stateVersion);
        void RebuildRuntimeIntroSequence();
        void ResetPreviewInspectionState();
        void ResetTelepathyState(bool clearCachedText = true);
        void ReturnToWaitingForRoundAfterPresentingFailure();
        void CachePreviewResult(string analysis);
        void ChangeStateFromEntryAction(GameState state);
        void OnPresentingComplete();
        void OnSubmitComplete();
        void OnReactionComplete();
        void ShowHint(string speaker, string text);
        string GetConfiguredText(Func<IngameTextTable, string> selector, string fallback);
        string GetDrawingReadyHintMessage();
        string BuildObjectGenerationFailureHint(string objectGenerationError);
    }

    internal interface IRoundOpeningStateEntryContext
    {
        ScoreManager ScoreManager { get; }
        DialogueSystem DialogueSystem { get; }
        DialogueSequence IntroSequence { get; }
        DialogueSequence RuntimeIntroSequence { get; }
        EndingController EndingController { get; }
        TitleScreenController TitleScreenController { get; }
        RoundHintPresenter HintPresenter { get; }

        void RebuildRuntimeIntroSequence();
    }

    internal interface IRoundPreparationStateEntryContext
    {
        IRoundAiGateway AiGateway { get; }
        ScoreConfig ScoreConfig { get; }
        RoundHintPresenter HintPresenter { get; }
        RoundDrawingInteractionGate DrawingInteractionGate { get; }

        int CurrentRound { get; set; }
        bool PreserveRoundIndexOnNextWaitingState { get; set; }
        bool HasOpenedInterpreterThisRound { get; set; }

        bool IsStateCurrent(GameState state, int stateVersion);
        void ResetPreviewInspectionState();
        void ResetTelepathyState(bool clearCachedText = true);
        void ReturnToWaitingForRoundAfterPresentingFailure();
        void OnPresentingComplete();
        void ShowHint(string speaker, string text);
        string GetConfiguredText(Func<IngameTextTable, string> selector, string fallback);
        string GetDrawingReadyHintMessage();
        string BuildObjectGenerationFailureHint(string objectGenerationError);
    }

    internal interface IRoundPreviewStateEntryContext
    {
        IRoundAiGateway AiGateway { get; }
        ScoreManager ScoreManager { get; }
        TerminalDisplay TerminalDisplay { get; }

        bool IsPreviewTerminalOpen { get; set; }
        SatisfactionLevel LastSatisfaction { get; set; }

        bool IsStateCurrent(GameState state, int stateVersion);
        void ResetPreviewInspectionState();
        void ResetTelepathyState(bool clearCachedText = true);
        void CachePreviewResult(string analysis);
        void ChangeStateFromEntryAction(GameState state);
        void OnSubmitComplete();
        void ShowHint(string speaker, string text);
        string GetConfiguredText(Func<IngameTextTable, string> selector, string fallback);
    }

    internal interface IRoundInterpreterStateEntryContext
    {
        IRoundAiGateway AiGateway { get; }
        TerminalDisplay TerminalDisplay { get; }
        AlienReactionController AlienReactionController { get; }
        RoundHintPresenter HintPresenter { get; }

        bool HasOpenedInterpreterThisRound { get; set; }
        SatisfactionLevel LastSatisfaction { get; set; }

        void OnReactionComplete();
        void ShowHint(string speaker, string text);
        string GetConfiguredText(Func<IngameTextTable, string> selector, string fallback);
    }
}
