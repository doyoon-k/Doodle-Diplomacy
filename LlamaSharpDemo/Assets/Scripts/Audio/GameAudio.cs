using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;

namespace DoodleDiplomacy.Audio
{
    public enum GameAudioBus
    {
        Master,
        Music,
        Ambience,
        Sfx,
        Ui,
        Voice
    }

    /// <summary>
    /// Project-wide audio routing and user-volume policy. Every AudioSource should
    /// be assigned to one of these buses before playback. Unassigned authored
    /// sources are routed to SFX when their scene loads as a safe fallback.
    /// </summary>
    public static class GameAudio
    {
        private const string MixerResourcePath = "Audio/GameAudioMixer";
        private const string PreferencePrefix = "audio.volume.";
        private const float MinimumDecibels = -80f;
        private const float MinimumLinearVolume = 0.0001f;

        private static readonly Dictionary<GameAudioBus, AudioMixerGroup> Groups =
            new();
        private static readonly Dictionary<GameAudioBus, float> PendingVolumes =
            new();

        private static AudioMixer _mixer;
        private static bool _canApplyMixerVolumes;
        private static bool _reportedMissingMixer;

        public static AudioMixer Mixer
        {
            get
            {
                EnsureMixerLoaded();
                return _mixer;
            }
        }

        public static bool Route(AudioSource source, GameAudioBus bus)
        {
            if (source == null)
            {
                return false;
            }

            AudioMixerGroup group = GetGroup(bus);
            if (group == null)
            {
                return false;
            }

            source.outputAudioMixerGroup = group;
            return true;
        }

        public static AudioMixerGroup GetGroup(GameAudioBus bus)
        {
            if (Groups.TryGetValue(bus, out AudioMixerGroup cached) &&
                cached != null)
            {
                return cached;
            }

            if (!EnsureMixerLoaded())
            {
                return null;
            }

            string groupName = GetGroupName(bus);
            AudioMixerGroup[] matches = _mixer.FindMatchingGroups(groupName);
            for (int i = 0; i < matches.Length; i++)
            {
                if (matches[i] != null && matches[i].name == groupName)
                {
                    Groups[bus] = matches[i];
                    return matches[i];
                }
            }

            Debug.LogError(
                $"Game audio mixer is missing the '{groupName}' group.",
                _mixer);
            return null;
        }

        public static void SetVolume(
            GameAudioBus bus,
            float linearVolume,
            bool remember = true)
        {
            float clamped = Mathf.Clamp01(linearVolume);
            if (remember)
            {
                PlayerPrefs.SetFloat(GetPreferenceKey(bus), clamped);
            }

            float decibels = LinearToDecibels(clamped);
            if (!_canApplyMixerVolumes || !TrySetMixerVolume(bus, decibels))
            {
                PendingVolumes[bus] = decibels;
            }
        }

        public static float GetVolume(GameAudioBus bus)
        {
            return Mathf.Clamp01(
                PlayerPrefs.GetFloat(GetPreferenceKey(bus), 1f));
        }

        public static void SaveVolumes()
        {
            PlayerPrefs.Save();
        }

        internal static void EnableRuntimeVolumeControl()
        {
            _canApplyMixerVolumes = true;

            GameAudioBus[] buses = (GameAudioBus[])System.Enum.GetValues(
                typeof(GameAudioBus));
            for (int i = 0; i < buses.Length; i++)
            {
                GameAudioBus bus = buses[i];
                float decibels = PendingVolumes.TryGetValue(bus, out float pending)
                    ? pending
                    : LinearToDecibels(GetVolume(bus));
                TrySetMixerVolume(bus, decibels);
            }

            PendingVolumes.Clear();
        }

        internal static int RouteUnassignedSources(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return 0;
            }

            int routedCount = 0;
            GameObject[] roots = scene.GetRootGameObjects();
            for (int rootIndex = 0; rootIndex < roots.Length; rootIndex++)
            {
                AudioSource[] sources = roots[rootIndex]
                    .GetComponentsInChildren<AudioSource>(true);
                for (int sourceIndex = 0; sourceIndex < sources.Length; sourceIndex++)
                {
                    AudioSource source = sources[sourceIndex];
                    if (source.outputAudioMixerGroup == null &&
                        Route(source, GameAudioBus.Sfx))
                    {
                        routedCount++;
                    }
                }
            }

            return routedCount;
        }

        internal static float LinearToDecibels(float linearVolume)
        {
            if (linearVolume <= MinimumLinearVolume)
            {
                return MinimumDecibels;
            }

            return Mathf.Log10(Mathf.Clamp01(linearVolume)) * 20f;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Groups.Clear();
            PendingVolumes.Clear();
            _mixer = null;
            _canApplyMixerVolumes = false;
            _reportedMissingMixer = false;
        }

        private static bool EnsureMixerLoaded()
        {
            if (_mixer != null)
            {
                return true;
            }

            _mixer = Resources.Load<AudioMixer>(MixerResourcePath);
            if (_mixer == null && !_reportedMissingMixer)
            {
                _reportedMissingMixer = true;
                Debug.LogError(
                    $"Game audio mixer was not found at Resources/{MixerResourcePath}.mixer.");
            }

            return _mixer != null;
        }

        private static bool TrySetMixerVolume(
            GameAudioBus bus,
            float decibels)
        {
            return EnsureMixerLoaded() &&
                   _mixer.SetFloat(GetParameterName(bus), decibels);
        }

        private static string GetGroupName(GameAudioBus bus)
        {
            return bus switch
            {
                GameAudioBus.Master => "Master",
                GameAudioBus.Music => "Music",
                GameAudioBus.Ambience => "Ambience",
                GameAudioBus.Sfx => "SFX",
                GameAudioBus.Ui => "UI",
                GameAudioBus.Voice => "Voice",
                _ => "SFX"
            };
        }

        private static string GetParameterName(GameAudioBus bus)
        {
            return GetGroupName(bus) + "Volume";
        }

        private static string GetPreferenceKey(GameAudioBus bus)
        {
            return PreferencePrefix + bus.ToString().ToLowerInvariant();
        }
    }

    [DefaultExecutionOrder(-10000)]
    internal sealed class GameAudioRuntime : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void CreateRuntime()
        {
            if (FindAnyObjectByType<GameAudioRuntime>() != null)
            {
                return;
            }

            var runtimeObject = new GameObject("Game Audio");
            runtimeObject.hideFlags = HideFlags.HideInHierarchy;
            DontDestroyOnLoad(runtimeObject);
            runtimeObject.AddComponent<GameAudioRuntime>();
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private void Start()
        {
            GameAudio.EnableRuntimeVolumeControl();
            RouteAllLoadedScenes();
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
        }

        private static void HandleSceneLoaded(Scene scene, LoadSceneMode loadMode)
        {
            GameAudio.RouteUnassignedSources(scene);
        }

        private static void RouteAllLoadedScenes()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                GameAudio.RouteUnassignedSources(SceneManager.GetSceneAt(i));
            }
        }
    }
}
