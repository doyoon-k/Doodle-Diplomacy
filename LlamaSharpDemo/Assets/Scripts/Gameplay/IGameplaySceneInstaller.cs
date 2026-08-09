using DoodleDiplomacy.Data;
using UnityEngine;

namespace DoodleDiplomacy.Gameplay
{
    public interface IGameplaySceneInstaller
    {
        string SceneId { get; }

        GameplayModeContext CreateContext(GameplayModeHost host);
        MonoBehaviour GetDefaultModeBehaviour();
    }

    public interface IGameplaySceneModeResolver
    {
        MonoBehaviour GetModeBehaviour(FlowEntryDefinition entry);
    }

    public interface IGameplaySceneEntryPreparer
    {
        void PrepareEntry(FlowEntryDefinition entry);
    }
}
