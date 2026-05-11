using DoodleDiplomacy.Data;
using UnityEngine;

namespace DoodleDiplomacy.Core
{
    public sealed class RoundOpeningStateEntryActions
    {
        private readonly IRoundOpeningStateEntryContext _context;

        internal RoundOpeningStateEntryActions(IRoundOpeningStateEntryContext context)
        {
            _context = context;
        }

        public void EnterTitle()
        {
            _context.HintPresenter?.Hide();
            _context.TitleScreenController?.ShowTitle();
        }

        public void EnterIntro()
        {
            _context.HintPresenter?.Hide();
            if (_context.IntroSequence == null)
            {
                return;
            }

            _context.RebuildRuntimeIntroSequence();
            _context.DialogueSystem?.PlaySequence(
                _context.RuntimeIntroSequence != null ? _context.RuntimeIntroSequence : _context.IntroSequence);
        }

        public void EnterEnding()
        {
            _context.HintPresenter?.Hide();
            EndingType ending = _context.ScoreManager?.GetEndingType() ?? EndingType.Diplomacy;
            Debug.Log($"[RoundManager] Ending: {ending}");
            _context.EndingController?.ShowEnding(ending);
        }
    }
}
