using DoodleDiplomacy.Dialogue;

namespace DoodleDiplomacy.Core
{
    public sealed class RoundHintPresenter
    {
        private readonly SubtitleDisplay _subtitleDisplay;

        public RoundHintPresenter(SubtitleDisplay subtitleDisplay)
        {
            _subtitleDisplay = subtitleDisplay;
        }

        public void Show(string speaker, string text)
        {
            _subtitleDisplay?.Show(speaker, text);
        }

        public void Hide()
        {
            _subtitleDisplay?.Hide();
        }
    }
}