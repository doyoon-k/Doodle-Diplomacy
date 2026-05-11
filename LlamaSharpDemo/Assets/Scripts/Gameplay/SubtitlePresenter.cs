using DoodleDiplomacy.Dialogue;

namespace DoodleDiplomacy.Gameplay
{
    public sealed class SubtitlePresenter : ISubtitlePresenter
    {
        private readonly SubtitleDisplay _subtitleDisplay;

        public SubtitlePresenter(SubtitleDisplay subtitleDisplay)
        {
            _subtitleDisplay = subtitleDisplay;
        }

        public void Show(string characterName, string text) => _subtitleDisplay?.Show(characterName, text);
        public void SetText(string text) => _subtitleDisplay?.SetText(text);
        public void Hide() => _subtitleDisplay?.Hide();
    }
}