using DoodleDiplomacy.Camera;
using DoodleDiplomacy.Localization;
using UnityEngine;

namespace DoodleDiplomacy.Core
{
    public sealed class RoundPlayerActionHandler
    {
        private const string DefaultRegeneratingReferencesMessage = "Regenerating the object references with the same prompts...";
        private const string DefaultOpenTerminalFirstMessage = "Open the terminal first.";
        private const string DefaultAdjutantDisabledMessage = "The adjutant can no longer review drawings. Click the alien for first-pass review.";
        private const float TerminalCloseGuardSeconds = 0.15f;

        private readonly IRoundPlayerActionContext _context;

        internal RoundPlayerActionHandler(IRoundPlayerActionContext context)
        {
            _context = context;
        }

        public void OnAlienClicked()
        {
            if (_context.CurrentState == GameState.WaitingForRound)
            {
                IRoundAiGateway aiGateway = _context.AiGateway;
                if (aiGateway != null && aiGateway.IsAvailable && !aiGateway.IsRoundStartReady)
                {
                    aiGateway.EnsureObjectGenerationPreparation(
                        forceRetry: aiGateway.HasObjectGenerationPreparationFailed);
                    aiGateway.PrepareRoundKeywords(forceRefresh: false);
                    _context.ShowHint("System", aiGateway.GetRoundStartAvailabilityMessage());
                    _context.ApplyInteractionPolicyForPlayerAction();
                    return;
                }

                _context.ChangeStateFromPlayerAction(GameState.Presenting);
                return;
            }

            if (_context.CurrentState == GameState.ObjectPresented)
            {
                _context.ShowHint(
                    "System",
                    L10n.T("round.references.regenerating", DefaultRegeneratingReferencesMessage));
                _context.ChangeStateFromPlayerAction(GameState.Presenting);
                return;
            }

            if (_context.CurrentState == GameState.PreviewReady)
            {
                _context.ChangeStateFromPlayerAction(GameState.PreviewAnalyzing);
                return;
            }

            if (_context.CurrentState != GameState.InterpreterReady)
            {
                return;
            }

            if (!_context.HasOpenedInterpreterThisRound)
            {
                _context.ShowHint(
                    "System",
                    L10n.T("round.terminal.open_first", DefaultOpenTerminalFirstMessage));
                return;
            }

            bool isLastRound = _context.ScoreConfig != null && _context.CurrentRound >= _context.ScoreConfig.totalRounds;
            _context.ChangeStateFromPlayerAction(isLastRound ? GameState.Ending : GameState.WaitingForRound);
        }

        public void OnTabletClicked()
        {
            if (_context.CurrentState != GameState.ObjectPresented && _context.CurrentState != GameState.PreviewReady)
            {
                return;
            }

            if (_context.CurrentState == GameState.PreviewReady)
            {
                _context.ResetCachedInterpretationForRedraw();
            }

            _context.ChangeStateFromPlayerAction(GameState.Drawing);
        }

        public void OnAdjutantClicked()
        {
            if (_context.CurrentState != GameState.PreviewReady)
            {
                return;
            }

            _context.ShowHint(
                "System",
                L10n.T("round.adjutant.disabled", DefaultAdjutantDisabledMessage));
        }

        public void OnTerminalClicked()
        {
            if (_context.CurrentState == GameState.Preview)
            {
                TogglePreviewTerminal();
                return;
            }

            if (_context.CurrentState == GameState.InterpreterReady)
            {
                _context.InterpreterOpenedAt = Time.unscaledTime;
                _context.ChangeStateFromPlayerAction(GameState.Interpreter);
                return;
            }

            if (_context.CurrentState != GameState.Interpreter)
            {
                return;
            }

            if (Time.unscaledTime - _context.InterpreterOpenedAt < TerminalCloseGuardSeconds)
            {
                return;
            }

            _context.OnInterpreterClose();
        }

        public void OnSharedMonitorClicked()
        {
            if (!_context.TryConsumeSharedMonitorClick())
            {
                return;
            }

            if (_context.CurrentState != GameState.ObjectPresented && _context.CurrentState != GameState.PreviewReady)
            {
                return;
            }

            if (!_context.CanUseSharedMonitorZoom())
            {
                return;
            }

            if (_context.IsSharedMonitorZoomActive)
            {
                _context.ExitSharedMonitorZoom();
            }
            else
            {
                _context.EnterSharedMonitorZoom();
            }
        }

        public void OnPreviewModify()
        {
            if (_context.CurrentState != GameState.Preview)
            {
                return;
            }

            _context.ResetCachedInterpretationForRedraw();
            _context.ChangeStateFromPlayerAction(GameState.Drawing);
        }

        private void TogglePreviewTerminal()
        {
            if (_context.IsPreviewTerminalOpen)
            {
                if (Time.unscaledTime - _context.InterpreterOpenedAt < TerminalCloseGuardSeconds)
                {
                    return;
                }

                _context.IsPreviewTerminalOpen = false;
                _context.ApplyCameraModeForPlayerAction(_context.CurrentState);
                return;
            }

            _context.InterpreterOpenedAt = Time.unscaledTime;
            _context.IsPreviewTerminalOpen = true;
            _context.ApplyCameraModeForPlayerAction(CameraMode.TerminalZoom);
            _context.ShowPreviewTerminal();
        }
    }
}
