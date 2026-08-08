using System.Linq;
using DoodleDiplomacy.Gameplay;
using DoodleDiplomacy.Gameplay.FirstContact;
using DoodleDiplomacy.Narrative;
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
        private const string NarrativeScenarioPath =
            "Assets/Generated/Narrative/first_contact_day1.asset";

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
            Assert.IsTrue(flow.entries[2].startSessionWithIntro, "Dr. Hwang's preflight tutorial must still run after the completed 3D intro.");

            var narrativeSettings = AssetDatabase.LoadAssetAtPath<FirstContactNarrativeSettings>(
                "Assets/Data/FirstContact/FirstContactNarrativeSettings.asset");
            Assert.IsNotNull(narrativeSettings);
            Assert.IsFalse(narrativeSettings.playPlaceholderIntroMontage, "The 3D intro replaces only the old placeholder montage.");
            Assert.IsTrue(narrativeSettings.enablePreflightTutorial, "The terminal preflight tutorial must remain enabled.");
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

        [Test]
        public void IntroFacilityContainsTranslatorBriefingConfiguration()
        {
            string sceneText = System.IO.File.ReadAllText(IntroFacilityScenePath);
            StringAssert.Contains(
                "narrativeScenario: {fileID: 11400000, guid: d0b69c3fbbd2ce942aeac9dc289e7b18, type: 2}",
                sceneText);
            StringAssert.Contains(
                "briefingWideCameraAnchor: {fileID: 1908255186}",
                sceneText);
            StringAssert.Contains(
                "briefingProjectorCameraAnchor: {fileID: 520970492}",
                sceneText);
            StringAssert.Contains(
                "projectorCloseupCameraAnchor: {fileID: 520970492}",
                sceneText);
            StringAssert.Contains("briefingPresentation: {fileID:", sceneText);
            StringAssert.Contains("m_Name: ProjectorImageSurface", sceneText);
            StringAssert.Contains("m_Name: VIEW_PresidentSeatedPreview", sceneText);
            StringAssert.Contains("m_Name: LOOK_ProjectorPresentation", sceneText);
            StringAssert.Contains("m_Name: LOOK_Hwang_Presentation", sceneText);
            StringAssert.Contains("m_Name: LOOK_Hwang_QA", sceneText);
            StringAssert.Contains("m_Name: LOOK_Director", sceneText);
            FirstContactBriefingSlideDeck slideDeck =
                AssetDatabase.LoadAssetAtPath<FirstContactBriefingSlideDeck>(
                    "Assets/Art/FirstContact/Briefing/FirstContactBriefingSlides.asset");
            Assert.IsNotNull(slideDeck);
            foreach (FirstContactBriefingSlideId slideId in
                     System.Enum.GetValues(typeof(FirstContactBriefingSlideId)))
            {
                Assert.IsNotNull(
                    slideDeck.GetSlide(slideId),
                    $"Briefing slide {slideId} must have artwork assigned.");
            }

            EditorSceneManager.OpenScene(IntroFacilityScenePath);
            FirstContactBriefingPresentation presentation =
                Object.FindFirstObjectByType<FirstContactBriefingPresentation>(
                    FindObjectsInactive.Include);
            Assert.IsNotNull(presentation);
            Transform projectorCloseupCameraAnchor = new SerializedObject(presentation)
                .FindProperty("projectorCloseupCameraAnchor")
                .objectReferenceValue as Transform;
            Assert.IsNotNull(projectorCloseupCameraAnchor);
            Assert.AreEqual("SHOT_Projector_Closeup", projectorCloseupCameraAnchor.name);
            Transform directorLookTarget = new SerializedObject(presentation)
                .FindProperty("directorLookTarget")
                .objectReferenceValue as Transform;
            Assert.IsNotNull(directorLookTarget);
            Assert.AreEqual("LOOK_Director", directorLookTarget.name);
            GameObject directorPrefabRoot = directorLookTarget.root.gameObject;
            Assert.AreEqual(
                "Adjutant",
                directorPrefabRoot.name);
            Assert.AreEqual(
                "Assets/Prefabs/Adjutant.prefab",
                PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                    directorPrefabRoot));

            FirstContactIntroGuideController adjutantGuide =
                Object.FindObjectsByType<FirstContactIntroGuideController>(
                        FindObjectsInactive.Include,
                        FindObjectsSortMode.None)
                    .First(item => item.gameObject.name == "Adjutant");
            Assert.AreEqual(10, adjutantGuide.PathPoints.Count);
            Assert.IsTrue(adjutantGuide.PathPoints.All(point => point != null));
            SerializedProperty holdPointIndices = new SerializedObject(adjutantGuide)
                .FindProperty("manualHoldPointIndices");
            Assert.AreEqual(1, holdPointIndices.arraySize);
            Assert.AreEqual(
                4,
                holdPointIndices.GetArrayElementAtIndex(0).intValue);

            NarrativeScenarioAsset scenario =
                AssetDatabase.LoadAssetAtPath<NarrativeScenarioAsset>(
                    NarrativeScenarioPath);
            Assert.IsNotNull(scenario);
            Assert.IsTrue(
                scenario.TryGetBeat(
                    "facility_corridor_discovery_0073",
                    out NarrativeBeat corridorBeat));
            Assert.AreEqual("intro.facility.corridor", corridorBeat.triggerEvent);
            Assert.IsTrue(
                scenario.TryGetBeat(
                    "briefing_move_when_ready_0130",
                    out NarrativeBeat finalBriefingBeat));
            Assert.AreEqual(
                "intro.facility.briefing",
                finalBriefingBeat.triggerEvent);
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
