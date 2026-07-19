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

        public void Show(string characterName, string text)
        {
            if (_subtitleDisplay != null)
            {
                _subtitleDisplay.Show(characterName, text);
            }
        }

        public void SetText(string text)
        {
            if (_subtitleDisplay != null)
            {
                _subtitleDisplay.SetText(text);
            }
        }

        public void SetAdvancePromptVisible(bool visible)
        {
            if (_subtitleDisplay != null)
            {
                _subtitleDisplay.SetAdvancePromptVisible(visible);
            }
        }

        public bool ConsumeAdvanceRequest() =>
            _subtitleDisplay != null && _subtitleDisplay.ConsumeAdvanceRequest();

        public void Hide()
        {
            if (_subtitleDisplay != null)
            {
                _subtitleDisplay.Hide();
            }
        }
    }
}
