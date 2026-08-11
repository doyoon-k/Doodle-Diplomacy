using UnityEngine;

namespace DoodleDiplomacy.Audio
{
    /// <summary>
    /// Authoring component for scene and prefab AudioSources. Runtime-created
    /// sources should call GameAudio.Route directly after creation.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    public sealed class GameAudioSourceRoute : MonoBehaviour
    {
        [SerializeField] private GameAudioBus bus = GameAudioBus.Sfx;

        public GameAudioBus Bus => bus;

        private void Awake()
        {
            Apply();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            Apply();
        }
#endif

        private void Apply()
        {
            if (bus == GameAudioBus.Master)
            {
                bus = GameAudioBus.Sfx;
            }

            AudioSource source = GetComponent<AudioSource>();
            if (source != null)
            {
                GameAudio.Route(source, bus);
            }
        }
    }
}
