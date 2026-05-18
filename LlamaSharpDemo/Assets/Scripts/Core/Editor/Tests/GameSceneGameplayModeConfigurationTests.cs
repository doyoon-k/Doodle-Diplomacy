using DoodleDiplomacy.Gameplay;
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
        private const string GameFlowPath = "Assets/Data/FirstContactGameFlow.asset";

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
            Assert.IsInstanceOf<Day1CalibrationMode>(defaultModeObject);
            Assert.AreEqual("day1-calibration", ((IGameplayMode)defaultModeObject).ModeId);

            RoundManager roundManager = Object.FindFirstObjectByType<RoundManager>();
            Assert.IsNotNull(roundManager, "Object-pair RoundManager should remain available in GameScene.");
            Assert.AreEqual("object-pair-drawing", roundManager.ModeId);
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
            Assert.IsInstanceOf<Day1CalibrationMode>(hub.GetDefaultModeBehaviour());
        }

        [Test]
        public void FirstContactFlowStartsWithDay1AndKeepsObjectPairEntry()
        {
            var flow = AssetDatabase.LoadAssetAtPath<DoodleDiplomacy.Data.GameFlowAsset>(GameFlowPath);
            Assert.IsNotNull(flow, "FirstContactGameFlow asset must exist.");
            Assert.GreaterOrEqual(flow.entries.Length, 2, "Flow should keep Day1 and object-pair entries.");
            Assert.AreEqual("day1-calibration", flow.entries[0].entryTag);
            Assert.AreEqual("object-pair-drawing", flow.entries[1].entryTag);
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
        public void GameplayFlowScenesAreEnabledInBuildSettings()
        {
            AssertSceneEnabled(MainMenuScenePath);
            AssertSceneEnabled(GameRootScenePath);
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
