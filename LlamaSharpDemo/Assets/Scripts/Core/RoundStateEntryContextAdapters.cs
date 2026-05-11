using DoodleDiplomacy.Character;
using DoodleDiplomacy.Data;
using DoodleDiplomacy.Devices;
using DoodleDiplomacy.Dialogue;
using DoodleDiplomacy.Ending;
using DoodleDiplomacy.UI;

namespace DoodleDiplomacy.Core
{
    internal sealed class RoundOpeningStateEntryContextAdapter : IRoundOpeningStateEntryContext
    {
        private readonly IRoundStateEntryContext _context;

        public RoundOpeningStateEntryContextAdapter(IRoundStateEntryContext context)
        {
            _context = context;
        }

        public ScoreManager ScoreManager => _context.ScoreManager;
        public DialogueSystem DialogueSystem => _context.DialogueSystem;
        public DialogueSequence IntroSequence => _context.IntroSequence;
        public DialogueSequence RuntimeIntroSequence => _context.RuntimeIntroSequence;
        public EndingController EndingController => _context.EndingController;
        public TitleScreenController TitleScreenController => _context.TitleScreenController;
        public RoundHintPresenter HintPresenter => _context.HintPresenter;
        public void RebuildRuntimeIntroSequence() => _context.RebuildRuntimeIntroSequence();
    }

    internal sealed class RoundPreparationStateEntryContextAdapter : IRoundPreparationStateEntryContext
    {
        private readonly IRoundStateEntryContext _context;

        public RoundPreparationStateEntryContextAdapter(IRoundStateEntryContext context)
        {
            _context = context;
        }

        public IRoundAiGateway AiGateway => _context.AiGateway;
        public ScoreConfig ScoreConfig => _context.ScoreConfig;
        public RoundHintPresenter HintPresenter => _context.HintPresenter;
        public RoundDrawingInteractionGate DrawingInteractionGate => _context.DrawingInteractionGate;
        public int CurrentRound { get => _context.CurrentRound; set => _context.CurrentRound = value; }
        public bool PreserveRoundIndexOnNextWaitingState
        {
            get => _context.PreserveRoundIndexOnNextWaitingState;
            set => _context.PreserveRoundIndexOnNextWaitingState = value;
        }
        public bool HasOpenedInterpreterThisRound
        {
            get => _context.HasOpenedInterpreterThisRound;
            set => _context.HasOpenedInterpreterThisRound = value;
        }

        public bool IsStateCurrent(GameState state, int stateVersion) => _context.IsStateCurrent(state, stateVersion);
        public void ResetPreviewInspectionState() => _context.ResetPreviewInspectionState();
        public void ResetTelepathyState(bool clearCachedText = true) => _context.ResetTelepathyState(clearCachedText);
        public void ReturnToWaitingForRoundAfterPresentingFailure() => _context.ReturnToWaitingForRoundAfterPresentingFailure();
        public void OnPresentingComplete() => _context.OnPresentingComplete();
        public void ShowHint(string speaker, string text) => _context.ShowHint(speaker, text);
        public string GetConfiguredText(System.Func<IngameTextTable, string> selector, string fallback) =>
            _context.GetConfiguredText(selector, fallback);
        public string GetDrawingReadyHintMessage() => _context.GetDrawingReadyHintMessage();
        public string BuildObjectGenerationFailureHint(string objectGenerationError) =>
            _context.BuildObjectGenerationFailureHint(objectGenerationError);
    }

    internal sealed class RoundPreviewStateEntryContextAdapter : IRoundPreviewStateEntryContext
    {
        private readonly IRoundStateEntryContext _context;

        public RoundPreviewStateEntryContextAdapter(IRoundStateEntryContext context)
        {
            _context = context;
        }

        public IRoundAiGateway AiGateway => _context.AiGateway;
        public ScoreManager ScoreManager => _context.ScoreManager;
        public TerminalDisplay TerminalDisplay => _context.TerminalDisplay;
        public bool IsPreviewTerminalOpen
        {
            get => _context.IsPreviewTerminalOpen;
            set => _context.IsPreviewTerminalOpen = value;
        }
        public SatisfactionLevel LastSatisfaction
        {
            get => _context.LastSatisfaction;
            set => _context.LastSatisfaction = value;
        }

        public bool IsStateCurrent(GameState state, int stateVersion) => _context.IsStateCurrent(state, stateVersion);
        public void ResetPreviewInspectionState() => _context.ResetPreviewInspectionState();
        public void ResetTelepathyState(bool clearCachedText = true) => _context.ResetTelepathyState(clearCachedText);
        public void CachePreviewResult(string analysis) => _context.CachePreviewResult(analysis);
        public void ChangeStateFromEntryAction(GameState state) => _context.ChangeStateFromEntryAction(state);
        public void OnSubmitComplete() => _context.OnSubmitComplete();
        public void ShowHint(string speaker, string text) => _context.ShowHint(speaker, text);
        public string GetConfiguredText(System.Func<IngameTextTable, string> selector, string fallback) =>
            _context.GetConfiguredText(selector, fallback);
    }

    internal sealed class RoundInterpreterStateEntryContextAdapter : IRoundInterpreterStateEntryContext
    {
        private readonly IRoundStateEntryContext _context;

        public RoundInterpreterStateEntryContextAdapter(IRoundStateEntryContext context)
        {
            _context = context;
        }

        public IRoundAiGateway AiGateway => _context.AiGateway;
        public TerminalDisplay TerminalDisplay => _context.TerminalDisplay;
        public AlienReactionController AlienReactionController => _context.AlienReactionController;
        public RoundHintPresenter HintPresenter => _context.HintPresenter;
        public bool HasOpenedInterpreterThisRound
        {
            get => _context.HasOpenedInterpreterThisRound;
            set => _context.HasOpenedInterpreterThisRound = value;
        }
        public SatisfactionLevel LastSatisfaction
        {
            get => _context.LastSatisfaction;
            set => _context.LastSatisfaction = value;
        }

        public void OnReactionComplete() => _context.OnReactionComplete();
        public void ShowHint(string speaker, string text) => _context.ShowHint(speaker, text);
        public string GetConfiguredText(System.Func<IngameTextTable, string> selector, string fallback) =>
            _context.GetConfiguredText(selector, fallback);
    }
}
