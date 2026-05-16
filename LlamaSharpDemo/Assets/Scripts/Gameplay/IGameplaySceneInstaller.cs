using UnityEngine;

namespace DoodleDiplomacy.Gameplay
{
    public interface IGameplaySceneInstaller
    {
        string SceneId { get; }

        GameplayModeContext CreateContext(GameplayModeHost host);
        MonoBehaviour GetDefaultModeBehaviour();
    }
}
