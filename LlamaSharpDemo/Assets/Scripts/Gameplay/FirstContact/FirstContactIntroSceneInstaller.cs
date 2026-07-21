using UnityEngine;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    [DisallowMultipleComponent]
    public sealed class FirstContactIntroSceneInstaller : MonoBehaviour, IGameplaySceneInstaller
    {
        [SerializeField] private string sceneId = "first-contact-intro";
        [SerializeField] private FirstContactIntroMode defaultModeBehaviour;

        public string SceneId => string.IsNullOrWhiteSpace(sceneId) ? gameObject.scene.name : sceneId;

        public void Configure(string id, FirstContactIntroMode mode)
        {
            sceneId = id;
            defaultModeBehaviour = mode;
        }

        public GameplayModeContext CreateContext(GameplayModeHost host)
        {
            return new GameplayModeContext(
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null);
        }

        public MonoBehaviour GetDefaultModeBehaviour()
        {
            return defaultModeBehaviour;
        }
    }
}
