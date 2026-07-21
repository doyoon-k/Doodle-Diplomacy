using DoodleDiplomacy.Gameplay;
using DoodleDiplomacy.Gameplay.FirstContact;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DoodleDiplomacy.Core.Editor.Tests
{
    public sealed class GameSceneGameplayModeConfigurationTests
    {
        private const string MainMenuScenePath = "Assets/Scenes/MainMenuScene.unity";
        private const string GameRootScenePath = "Assets/Scenes/GameRoot.unity";
        private const string GameScenePath = "Assets/Scenes/GameScene.unity";
        private const string IntroSurfaceScenePath = "Assets/Scenes/FirstContact/FC_Intro_Surface.unity";
        private const string IntroFacilityScenePath = "Assets/Scenes/FirstContact/FC_Intro_Facility.unity";
        private const string GameFlowPath = "Assets/Data/FirstContact/FirstContactGameFlow.asset";

        [Test]
        public void GameSceneHostUsesDirectGameplayMode()
        {
            EditorSceneManager.OpenScene(GameScenePath);

            GameplayModeHost host = Object.FindFirstObjectByType<GameplayModeHost>();
            Assert.IsNotNull(host, "GameScene must contain a GameplayModeHost.");

            var serializedHost = new SerializedObject(host);
            Object defaultModeObject = serializedHost.FindProperty("defaultModeBehaviour").objectReferenceValue;
            Assert.IsNotNull(defaultModeObject, "GameplayModeHost.defaultModeBehaviour must be assigned.");
            Assert.IsInstanceOf<MonoBehaviour>(defaultModeObject);
            Assert.IsInstanceOf<IGameplayMode>(defaultModeObject);
            Assert.IsInstanceOf<IGameplaySessionController>(defaultModeObject);
            Assert.IsInstanceOf<FirstContactTranslationMode>(defaultModeObject);
            Assert.AreEqual("first-contact-translation", ((IGameplayMode)defaultModeObject).ModeId);
        }

        [Test]
        public void GameSceneReferenceHubCanInstallGameplayScene()
        {
            EditorSceneManager.OpenScene(GameScenePath);

            SceneReferenceHub hub = Object.FindFirstObjectByType<SceneReferenceHub>();
            Assert.IsNotNull(hub, "GameScene must contain a SceneReferenceHub.");
            Assert.IsInstanceOf<IGameplaySceneInstaller>(hub);
            Assert.IsNotNull(hub.GetDefaultModeBehaviour(), "SceneReferenceHub must resolve a default gameplay mode.");
            Assert.IsInstanceOf<IGameplayMode>(hub.GetDefaultModeBehaviour());
            Assert.IsInstanceOf<FirstContactTranslationMode>(hub.GetDefaultModeBehaviour());

            var flow = AssetDatabase.LoadAssetAtPath<DoodleDiplomacy.Data.GameFlowAsset>(GameFlowPath);
            Assert.IsInstanceOf<IGameplaySceneModeResolver>(hub);
            var resolver = (IGameplaySceneModeResolver)hub;
            Assert.IsInstanceOf<FirstContactTranslationMode>(resolver.GetModeBehaviour(flow.entries[^1]));
        }

        [Test]
        public void FirstContactFlowContainsIntroAndTranslationEntries()
        {
            var flow = AssetDatabase.LoadAssetAtPath<DoodleDiplomacy.Data.GameFlowAsset>(GameFlowPath);
            Assert.IsNotNull(flow, "FirstContactGameFlow asset must exist.");
            Assert.AreEqual(3, flow.entries.Length, "Active flow should contain surface intro, facility intro, and translation gameplay.");
            Assert.AreEqual("first-contact-intro-surface", flow.entries[0].entryTag);
            Assert.AreEqual("first-contact-intro-facility", flow.entries[1].entryTag);
            Assert.AreEqual("first-contact-translation", flow.entries[2].entryTag);
            Assert.IsFalse(flow.entries[2].startSessionWithIntro, "The completed 3D intro replaces the translation mode's placeholder intro cards.");
        }

        [Test]
        public void GameRootBootstrapsFirstContactFlow()
        {
            EditorSceneManager.OpenScene(GameRootScenePath);

            GameplayModeHost host = Object.FindFirstObjectByType<GameplayModeHost>();
            Assert.IsNotNull(host, "GameRoot must contain the persistent GameplayModeHost.");

            GameFlowDirector director = Object.FindFirstObjectByType<GameFlowDirector>();
            Assert.IsNotNull(director, "GameRoot must contain a GameFlowDirector.");

            var serializedDirector = new SerializedObject(director);
            Object gameFlow = serializedDirector.FindProperty("gameFlow").objectReferenceValue;
            Assert.IsNotNull(gameFlow, "GameFlowDirector.gameFlow must be assigned.");
            Assert.AreEqual(
                AssetDatabase.LoadAssetAtPath<Object>(GameFlowPath),
                gameFlow,
                "GameRoot should load the FirstContactGameFlow asset.");

            Object referencedHost = serializedDirector.FindProperty("gameplayModeHost").objectReferenceValue;
            Assert.AreEqual(host, referencedHost, "GameFlowDirector must drive the root GameplayModeHost.");
        }

        [Test]
        public void GameSceneDoesNotContainLegacyGameplayModeReferences()
        {
            string sceneText = System.IO.File.ReadAllText(GameScenePath);
            string[] removedTypeMarkers =
            {
                "DoodleDiplomacy.Core.RoundManager",
                "DoodleDiplomacy.UI.PreviewButtonPanel",
                "DoodleDiplomacy.Ending.EndingController",
                "DoodleDiplomacy.Gameplay.Day1CalibrationMode",
                "DoodleDiplomacy.Gameplay.Day1StimulusLibrary",
                "DoodleDiplomacy.UI.Day1StimulusButtonPanel",
                "object-pair-drawing",
                "day1-calibration"
            };

            foreach (string marker in removedTypeMarkers)
            {
                StringAssert.DoesNotContain(marker, sceneText, $"GameScene must not retain legacy gameplay marker '{marker}'.");
            }
        }

        [Test]
        public void GameplayFlowScenesAreEnabledInBuildSettings()
        {
            AssertSceneEnabled(MainMenuScenePath);
            AssertSceneEnabled(GameRootScenePath);
            AssertSceneEnabled(IntroSurfaceScenePath);
            AssertSceneEnabled(IntroFacilityScenePath);
            AssertSceneEnabled(GameScenePath);
        }

        [Test]
        public void MainMenuIsBuildStartScene()
        {
            Assert.Greater(EditorBuildSettings.scenes.Length, 0, "Build Settings must contain at least one scene.");
            Assert.AreEqual(MainMenuScenePath, EditorBuildSettings.scenes[0].path);
            Assert.IsTrue(EditorBuildSettings.scenes[0].enabled, "MainMenuScene must be the enabled build start scene.");
        }

        private static void AssertSceneEnabled(string scenePath)
        {
            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
            {
                if (scene.path == scenePath)
                {
                    Assert.IsTrue(scene.enabled, $"{scenePath} must be enabled in Build Settings.");
                    return;
                }
            }

            Assert.Fail($"{scenePath} is missing from Build Settings.");
        }

    }
}
