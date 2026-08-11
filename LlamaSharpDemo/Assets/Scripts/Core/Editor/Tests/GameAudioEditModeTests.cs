using DoodleDiplomacy.Audio;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

namespace DoodleDiplomacy.Core.Editor.Tests
{
    public sealed class GameAudioEditModeTests
    {
        private const string MixerResourcePath = "Audio/GameAudioMixer";

        [Test]
        public void GameAudioMixerContainsEveryPolicyBus()
        {
            AudioMixer mixer = Resources.Load<AudioMixer>(MixerResourcePath);
            Assert.IsNotNull(mixer);

            GameAudioBus[] buses =
            {
                GameAudioBus.Master,
                GameAudioBus.Music,
                GameAudioBus.Ambience,
                GameAudioBus.Sfx,
                GameAudioBus.Ui,
                GameAudioBus.Voice
            };

            for (int i = 0; i < buses.Length; i++)
            {
                Assert.IsNotNull(
                    GameAudio.GetGroup(buses[i]),
                    $"Missing mixer group for {buses[i]}.");
            }
        }

        [Test]
        public void GameAudioMixerExposesEveryBusVolume()
        {
            AudioMixer mixer = Resources.Load<AudioMixer>(MixerResourcePath);
            Assert.IsNotNull(mixer);

            string[] expectedNames =
            {
                "MasterVolume",
                "MusicVolume",
                "AmbienceVolume",
                "SFXVolume",
                "UIVolume",
                "VoiceVolume"
            };
            var serializedMixer = new SerializedObject(mixer);
            SerializedProperty parameters = serializedMixer.FindProperty(
                "m_ExposedParameters");
            Assert.IsNotNull(parameters);

            for (int expectedIndex = 0;
                 expectedIndex < expectedNames.Length;
                 expectedIndex++)
            {
                bool found = false;
                for (int parameterIndex = 0;
                     parameterIndex < parameters.arraySize;
                     parameterIndex++)
                {
                    SerializedProperty parameter =
                        parameters.GetArrayElementAtIndex(parameterIndex);
                    SerializedProperty name = parameter.FindPropertyRelative("name");
                    if (name != null && name.stringValue == expectedNames[expectedIndex])
                    {
                        found = true;
                        break;
                    }
                }

                Assert.IsTrue(
                    found,
                    $"Missing exposed mixer parameter {expectedNames[expectedIndex]}.");
            }
        }

        [Test]
        public void RouteAssignsRequestedMixerGroup()
        {
            var host = new GameObject("GameAudioRouteTest");
            try
            {
                AudioSource source = host.AddComponent<AudioSource>();
                Assert.IsTrue(GameAudio.Route(source, GameAudioBus.Music));
                Assert.AreSame(
                    GameAudio.GetGroup(GameAudioBus.Music),
                    source.outputAudioMixerGroup);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }
    }
}
