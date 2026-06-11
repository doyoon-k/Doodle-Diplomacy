using DoodleDiplomacy.Gameplay;
using DoodleDiplomacy.Gameplay.FirstContact;
using DoodleDiplomacy.Dialogue;
using DoodleDiplomacy.Interaction;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;

namespace DoodleDiplomacy.Core.Editor.Tests
{
    public sealed class GameSceneGameplayModeConfigurationTests
    {
        private const string MainMenuScenePath = "Assets/Scenes/MainMenuScene.unity";
        private const string GameRootScenePath = "Assets/Scenes/GameRoot.unity";
        private const string GameScenePath = "Assets/Scenes/GameScene.unity";
        private const string GameFlowPath = "Assets/Data/FirstContact/FirstContactGameFlow.asset";
        private const string LegacyDay1FlowEntryPath = "Assets/Data/Legacy/Day1Calibration/FlowEntry_Day1Calibration.asset";
        private const string LegacyObjectPairFlowEntryPath = "Assets/Data/Legacy/ObjectPairDrawing/FlowEntry_CurrentObjectPairDrawing.asset";

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
            Assert.IsInstanceOf<FirstContactTranslationMode>(hub.GetDefaultModeBehaviour());

            var flow = AssetDatabase.LoadAssetAtPath<DoodleDiplomacy.Data.GameFlowAsset>(GameFlowPath);
            Assert.IsInstanceOf<IGameplaySceneModeResolver>(hub);
            var resolver = (IGameplaySceneModeResolver)hub;
            Assert.IsInstanceOf<FirstContactTranslationMode>(resolver.GetModeBehaviour(flow.entries[0]));

            var legacyDay1 = AssetDatabase.LoadAssetAtPath<DoodleDiplomacy.Data.FlowEntryDefinition>(LegacyDay1FlowEntryPath);
            var legacyObjectPair = AssetDatabase.LoadAssetAtPath<DoodleDiplomacy.Data.FlowEntryDefinition>(LegacyObjectPairFlowEntryPath);
            Assert.IsInstanceOf<Day1CalibrationMode>(resolver.GetModeBehaviour(legacyDay1));
            Assert.IsInstanceOf<RoundManager>(resolver.GetModeBehaviour(legacyObjectPair));
        }

        [Test]
        public void FirstContactFlowContainsOnlyActiveFirstContactEntry()
        {
            var flow = AssetDatabase.LoadAssetAtPath<DoodleDiplomacy.Data.GameFlowAsset>(GameFlowPath);
            Assert.IsNotNull(flow, "FirstContactGameFlow asset must exist.");
            Assert.AreEqual(1, flow.entries.Length, "Active flow should exclude legacy Day1/object-pair prototype entries.");
            Assert.AreEqual("first-contact-translation", flow.entries[0].entryTag);
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
        public void GameSceneDoesNotRouteSharedSceneEventsDirectlyToRoundManager()
        {
            EditorSceneManager.OpenScene(GameScenePath);

            RoundManager roundManager = Object.FindFirstObjectByType<RoundManager>();
            Assert.IsNotNull(roundManager, "GameScene should still contain the object-pair RoundManager mode.");

            DialogueSystem dialogueSystem = Object.FindFirstObjectByType<DialogueSystem>();
            Assert.IsNotNull(dialogueSystem, "GameScene must contain a DialogueSystem.");
            AssertNoPersistentTarget(
                dialogueSystem.OnSequenceComplete,
                roundManager,
                "DialogueSystem.OnSequenceComplete");

            foreach (InteractableObject interactable in Object.FindObjectsByType<InteractableObject>(
                         FindObjectsInactive.Include,
                         FindObjectsSortMode.None))
            {
                AssertNoPersistentTarget(
                    interactable.OnInteracted,
                    roundManager,
                    $"{interactable.name}.OnInteracted");
            }
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

        private static void AssertNoPersistentTarget(UnityEventBase unityEvent, Object disallowedTarget, string owner)
        {
            Assert.IsNotNull(unityEvent, $"{owner} must not be null.");
            for (int i = 0; i < unityEvent.GetPersistentEventCount(); i++)
            {
                Assert.AreNotEqual(
                    disallowedTarget,
                    unityEvent.GetPersistentTarget(i),
                    $"{owner} must not call RoundManager directly; route through GameplayModeHost/active mode ownership instead.");
            }
        }
    }
}
