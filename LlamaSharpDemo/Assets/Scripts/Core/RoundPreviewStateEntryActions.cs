using DoodleDiplomacy.Data;

namespace DoodleDiplomacy.Core
{
    public sealed class RoundPreviewStateEntryActions
    {
        private const string DefaultPreviewAnalyzingMessage = "The alien is trying to understand your drawing...";
        private const string DefaultPreviewReadyToInspectMessage = "First-pass analysis is complete. Click the terminal to inspect the result.";
        private const string DefaultPreviewReadyHintMessage = "Click the alien to get a first-pass read, or click the tablet to keep drawing.";
        private const string DefaultSubmittingHintMessage = "Submitting the drawing to the alien delegation...";

        private readonly IRoundPreviewStateEntryContext _context;

        internal RoundPreviewStateEntryActions(IRoundPreviewStateEntryContext context)
        {
            _context = context;
        }

        public void EnterPreviewReady()
        {
            _context.ShowHint(
                "System",
                _context.GetConfiguredText(
                    table => table.previewReadyHintMessage,
                    DefaultPreviewReadyHintMessage));
            UnityEngine.Debug.Log("[RoundManager] Drawing marked complete. Waiting for alien first-pass review.");
        }

        public void EnterPreviewAnalyzing(int stateVersion)
        {
            _context.ResetPreviewInspectionState();
            _context.TerminalDisplay?.Clear();
            _context.ShowHint(
                "System",
                _context.GetConfiguredText(
                    table => table.previewAnalyzingMessage,
                    DefaultPreviewAnalyzingMessage));

            IRoundAiGateway aiGateway = _context.AiGateway;
            if (aiGateway != null && aiGateway.IsAvailable)
            {
                aiGateway.GetPreview(analysis =>
                {
                    if (!_context.IsStateCurrent(GameState.PreviewAnalyzing, stateVersion))
                    {
                        return;
                    }

                    _context.CachePreviewResult(analysis);
                    _context.ChangeStateFromEntryAction(GameState.Preview);
                });
                return;
            }

            _context.CachePreviewResult("(AI analysis unavailable)");
            _context.ChangeStateFromEntryAction(GameState.Preview);
        }

        public void EnterPreview()
        {
            _context.IsPreviewTerminalOpen = false;
            _context.ShowHint(
                "System",
                _context.GetConfiguredText(
                    table => table.previewReadyToInspectMessage,
                    DefaultPreviewReadyToInspectMessage));
            UnityEngine.Debug.Log("[RoundManager] Preview analysis complete. Waiting for submit or modify.");
        }

        public void EnterSubmitting(int stateVersion)
        {
            _context.ShowHint(
                "System",
                _context.GetConfiguredText(
                    table => table.submittingHintMessage,
                    DefaultSubmittingHintMessage));

            IRoundAiGateway aiGateway = _context.AiGateway;
            if (aiGateway != null && aiGateway.IsAvailable)
            {
                aiGateway.GetJudgment(satisfaction =>
                {
                    if (!_context.IsStateCurrent(GameState.Submitting, stateVersion))
                    {
                        return;
                    }

                    _context.LastSatisfaction = satisfaction;
                    _context.ScoreManager?.RecordRound(satisfaction);
                    _context.OnSubmitComplete();
                });
                return;
            }

            _context.LastSatisfaction = SatisfactionLevel.Neutral;
            _context.ScoreManager?.RecordRound(SatisfactionLevel.Neutral);
            _context.ResetTelepathyState();
            _context.OnSubmitComplete();
        }
    }
}
