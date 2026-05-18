using DoodleDiplomacy.Data;
using DoodleDiplomacy.Localization;
using UnityEngine;

namespace DoodleDiplomacy.Core
{
    public sealed class RoundPreparationStateEntryActions
    {
        private const string DefaultGeneratingAlienObjectsMessage = "Generating the alien objects...";
        private const string DefaultObjectGeneratorMissingMessage = "Object generator is missing. Assign AIPipelineBridge before starting the round.";
        private const string DefaultObjectPresentedHintMessage = "Click the tablet to start drawing, or click the alien to regenerate the references.";

        private readonly IRoundPreparationStateEntryContext _context;

        internal RoundPreparationStateEntryActions(IRoundPreparationStateEntryContext context)
        {
            _context = context;
        }

        public void EnterWaitingForRound()
        {
            _context.HintPresenter?.Hide();
            _context.HasOpenedInterpreterThisRound = false;
            _context.ResetPreviewInspectionState();
            if (_context.PreserveRoundIndexOnNextWaitingState)
            {
                _context.PreserveRoundIndexOnNextWaitingState = false;
            }
            else
            {
                _context.CurrentRound++;
            }

            IRoundAiGateway aiGateway = _context.AiGateway;
            _context.ResetTelepathyState();
            aiGateway?.ResetRound();
            aiGateway?.EnsureObjectGenerationPreparation();
            bool adoptedPrefetch = aiGateway != null && aiGateway.TryAdoptPrefetchedRound();
            if (!adoptedPrefetch)
            {
                aiGateway?.PrepareRoundKeywords();
            }

            Debug.Log($"[RoundManager] Waiting for round {_context.CurrentRound} / {_context.ScoreConfig?.totalRounds}.");
            _context.ShowHint(
                "System",
                aiGateway != null && aiGateway.IsAvailable
                    ? aiGateway.GetRoundStartAvailabilityMessage()
                    : L10n.T("round.objects.generator_missing", DefaultObjectGeneratorMissingMessage));
        }

        public void EnterPresenting(int stateVersion)
        {
            _context.ShowHint(
                "System",
                L10n.T("round.objects.generating", DefaultGeneratingAlienObjectsMessage));

            IRoundAiGateway aiGateway = _context.AiGateway;
            if (aiGateway != null && aiGateway.IsAvailable)
            {
                aiGateway.GenerateObjects(success =>
                {
                    if (!_context.IsStateCurrent(GameState.Presenting, stateVersion))
                    {
                        return;
                    }

                    if (!success)
                    {
                        Debug.LogWarning($"[RoundManager] Object generation failed: {aiGateway.LastObjectGenerationError}");
                        _context.ShowHint("System", _context.BuildObjectGenerationFailureHint(aiGateway.LastObjectGenerationError));
                        _context.ReturnToWaitingForRoundAfterPresentingFailure();
                        return;
                    }

                    _context.OnPresentingComplete();
                });
                return;
            }

            Debug.LogWarning("[RoundManager] AIPipelineBridge is missing. Cannot generate alien objects.");
            _context.ShowHint(
                "System",
                L10n.T("round.objects.generator_missing", DefaultObjectGeneratorMissingMessage));
            _context.ReturnToWaitingForRoundAfterPresentingFailure();
        }

        public void EnterObjectPresented()
        {
            _context.ShowHint(
                "System",
                L10n.T("round.objects.presented_hint", DefaultObjectPresentedHintMessage));
            Debug.Log("[RoundManager] Objects are ready. Waiting for tablet interaction.");
            _context.AiGateway?.StartNextRoundPrefetch();
        }

        public void EnterDrawing()
        {
            _context.DrawingInteractionGate?.UnlockForEditing();
            _context.ShowHint("System", _context.GetDrawingReadyHintMessage());
        }
    }
}
