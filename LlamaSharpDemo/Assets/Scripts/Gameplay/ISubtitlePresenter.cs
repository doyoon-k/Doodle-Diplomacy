namespace DoodleDiplomacy.Gameplay
{
    public interface ISubtitlePresenter
    {
        void Show(string characterName, string text);
        void SetText(string text);
        void Hide();
    }
}