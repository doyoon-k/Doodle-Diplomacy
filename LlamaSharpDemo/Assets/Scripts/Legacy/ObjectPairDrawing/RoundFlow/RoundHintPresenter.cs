using DoodleDiplomacy.Dialogue;
using DoodleDiplomacy.Localization;

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
            _subtitleDisplay?.Show(LocalizeSpeaker(speaker), text);
        }

        public void Hide()
        {
            _subtitleDisplay?.Hide();
        }

        private static string LocalizeSpeaker(string speaker)
        {
            return string.Equals(speaker, "System", System.StringComparison.OrdinalIgnoreCase)
                ? L10n.T("speaker.system", "System")
                : speaker;
        }
    }
}
