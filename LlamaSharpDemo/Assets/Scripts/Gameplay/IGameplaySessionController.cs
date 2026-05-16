namespace DoodleDiplomacy.Gameplay
{
    public interface IGameplaySessionController
    {
        void StartGame(bool isFirstPlay = true);
        void ChangeToTitle();
        void SubmitPreview();
        void ModifyPreview();
    }
}
