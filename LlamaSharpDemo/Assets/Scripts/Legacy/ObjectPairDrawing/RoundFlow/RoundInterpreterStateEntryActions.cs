using DoodleDiplomacy.Data;
using DoodleDiplomacy.Localization;
using UnityEngine;

namespace DoodleDiplomacy.Core
{
    public sealed class RoundInterpreterStateEntryActions
    {
        private const string DefaultTerminalSignalReadyMessage = "A signal has reached the terminal. Click the terminal to inspect it.";
        private const string DefaultNoSignalMessage = "No readable signal was recovered. Open the terminal to continue.";

        private readonly IRoundInterpreterStateEntryContext _context;

        internal RoundInterpreterStateEntryActions(IRoundInterpreterStateEntryContext context)
        {
            _context = context;
        }

        public void EnterAlienReaction()
        {
            _context.HintPresenter?.Hide();
            if (_context.AlienReactionController != null)
            {
                _context.AlienReactionController.OnReactionComplete.RemoveListener(_context.OnReactionComplete);
                _context.AlienReactionController.OnReactionComplete.AddListener(_context.OnReactionComplete);
                _context.AlienReactionController.PlayReaction(_context.LastSatisfaction);
                return;
            }

            Debug.LogWarning("[RoundManager] AlienReactionController is missing. Skipping reaction.");
            _context.OnReactionComplete();
        }

        public void EnterInterpreterReady()
        {
            IRoundAiGateway aiGateway = _context.AiGateway;
            if (aiGateway != null && aiGateway.HasTelepathyResult)
            {
                _context.ShowHint(
                    "System",
                    L10n.T("round.interpreter.signal_ready", DefaultTerminalSignalReadyMessage));
            }
            else
            {
                _context.ShowHint(
                    "System",
                    L10n.T("round.interpreter.no_signal", DefaultNoSignalMessage));
            }

            Debug.Log("[RoundManager] Interpreter is ready.");
        }

        public void EnterInterpreter()
        {
            _context.HintPresenter?.Hide();
            bool instantTerminalDisplay = _context.HasOpenedInterpreterThisRound;
            IRoundAiGateway aiGateway = _context.AiGateway;
            if (aiGateway != null && aiGateway.HasTelepathyResult)
            {
                _context.TerminalDisplay?.ShowText(aiGateway.LastTelepathy, instantTerminalDisplay);
            }
            else if (_context.TerminalDisplay != null)
            {
                _context.TerminalDisplay.ShowText(
                    L10n.T(
                        "round.interpreter.terminal.no_captured_signal",
                        "[TRANSLATOR v1.0]\n> No captured alien signal.\n> _"),
                    instantTerminalDisplay);
            }

            _context.HasOpenedInterpreterThisRound = true;
        }
    }
}
