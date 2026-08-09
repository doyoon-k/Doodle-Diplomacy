using System.Linq;
using DoodleDiplomacy.Camera;
using DoodleDiplomacy.Devices;
using DoodleDiplomacy.Gameplay;
using DoodleDiplomacy.Gameplay.FirstContact;
using DoodleDiplomacy.Narrative;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

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
        public void FacilityEmbedsTranslationGameplayMode()
        {
            Scene facilityScene = EditorSceneManager.OpenScene(IntroFacilityScenePath);

            FirstContactIntroSceneInstaller installer =
                FindComponentInScene<FirstContactIntroSceneInstaller>(facilityScene);
            Assert.IsNotNull(installer, "Facility must contain its intro scene installer.");

            var serializedInstaller = new SerializedObject(installer);
            SceneReferenceHub hub = serializedInstaller
                .FindProperty("embeddedGameplayReferences")
                .objectReferenceValue as SceneReferenceHub;
            GameObject gameplayRoot = serializedInstaller
                .FindProperty("embeddedGameplayRoot")
                .objectReferenceValue as GameObject;
            Assert.IsNotNull(hub, "Facility installer must reference the embedded meeting gameplay.");
            Assert.IsNotNull(gameplayRoot, "Facility installer must reference the embedded gameplay root.");
            Assert.AreEqual("MeetingGameplaySystems", gameplayRoot.name);
            Assert.IsFalse(
                gameplayRoot.activeSelf,
                "Meeting gameplay systems must remain dormant until the player takes the seat.");

            Assert.IsNull(
                gameplayRoot.GetComponentInChildren<GameplayModeHost>(true),
                "Facility must use the single authoritative GameplayModeHost owned by GameRoot.");
            Object defaultModeObject = hub.GetDefaultModeBehaviour();
            Assert.IsNotNull(defaultModeObject, "SceneReferenceHub.defaultModeBehaviour must be assigned.");
            Assert.IsInstanceOf<MonoBehaviour>(defaultModeObject);
            Assert.IsInstanceOf<IGameplayMode>(defaultModeObject);
            Assert.IsInstanceOf<IGameplaySessionController>(defaultModeObject);
            Assert.IsInstanceOf<FirstContactTranslationMode>(defaultModeObject);
            Assert.AreEqual("first-contact-translation", ((IGameplayMode)defaultModeObject).ModeId);

            var flow = AssetDatabase.LoadAssetAtPath<DoodleDiplomacy.Data.GameFlowAsset>(GameFlowPath);
            Assert.IsInstanceOf<FirstContactTranslationMode>(
                installer.GetModeBehaviour(flow.entries[^1]));
        }

        [Test]
        public void FacilityTabletReceivesAuthoritativeHostThroughSceneReferenceHub()
        {
            Scene facilityScene = EditorSceneManager.OpenScene(IntroFacilityScenePath);
            SceneReferenceHub hub =
                FindComponentInScene<SceneReferenceHub>(facilityScene);
            TabletPhysicalControlsController tablet =
                FindComponentInScene<TabletPhysicalControlsController>(facilityScene);

            Assert.IsNotNull(hub);
            Assert.IsNotNull(tablet);
            Assert.AreSame(tablet, hub.TabletPhysicalControls);

            var hostObject = new GameObject("AuthoritativeGameplayModeHost");
            try
            {
                GameplayModeHost authoritativeHost =
                    hostObject.AddComponent<GameplayModeHost>();
                hub.ConfigureRuntime(authoritativeHost);

                Assert.AreSame(authoritativeHost, tablet.GameplayModeHost);
            }
            finally
            {
                Object.DestroyImmediate(hostObject);
            }
        }

        [Test]
        public void DrawingBrushPreviewRecreatesTransientPropertyBlockAtPointOfUse()
        {
            var root = new GameObject("DrawingBrushPreviewTestRoot");
            root.SetActive(false);

            try
            {
                DrawingBrushPreview preview = root.AddComponent<DrawingBrushPreview>();
                MeshFilter outlineFilter = CreatePreviewRenderer(
                    root.transform,
                    "Outline",
                    out MeshRenderer outlineRenderer);
                MeshFilter fillFilter = CreatePreviewRenderer(
                    root.transform,
                    "Fill",
                    out MeshRenderer fillRenderer);

                var serializedPreview = new SerializedObject(preview);
                serializedPreview.FindProperty("outlineMeshFilter").objectReferenceValue = outlineFilter;
                serializedPreview.FindProperty("outlineRenderer").objectReferenceValue = outlineRenderer;
                serializedPreview.FindProperty("fillMeshFilter").objectReferenceValue = fillFilter;
                serializedPreview.FindProperty("fillRenderer").objectReferenceValue = fillRenderer;
                serializedPreview.ApplyModifiedPropertiesWithoutUndo();

                System.Reflection.FieldInfo propertyBlockField = typeof(DrawingBrushPreview).GetField(
                    "_propertyBlock",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic);
                Assert.IsNotNull(propertyBlockField);
                propertyBlockField.SetValue(preview, null);

                Assert.DoesNotThrow(() => preview.ShowFill(
                    Vector3.zero,
                    Vector3.forward,
                    Vector3.up,
                    1f,
                    1f,
                    Color.white));
                Assert.IsNotNull(propertyBlockField.GetValue(preview));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void FacilityEmbeddedReferenceHubCanInstallGameplayScene()
        {
            Scene facilityScene = EditorSceneManager.OpenScene(IntroFacilityScenePath);

            SceneReferenceHub hub = FindComponentInScene<SceneReferenceHub>(facilityScene);
            Assert.IsNotNull(hub, "Facility must contain the embedded SceneReferenceHub.");
            Assert.IsInstanceOf<IGameplaySceneInstaller>(hub);
            Assert.IsNotNull(hub.GetDefaultModeBehaviour(), "SceneReferenceHub must resolve a default gameplay mode.");
            Assert.IsInstanceOf<IGameplayMode>(hub.GetDefaultModeBehaviour());
            Assert.IsInstanceOf<FirstContactTranslationMode>(hub.GetDefaultModeBehaviour());
            Assert.IsTrue(hub.ValidateReferences(logErrors: false));

            var flow = AssetDatabase.LoadAssetAtPath<DoodleDiplomacy.Data.GameFlowAsset>(GameFlowPath);
            Assert.IsInstanceOf<IGameplaySceneModeResolver>(hub);
            var resolver = (IGameplaySceneModeResolver)hub;
            Assert.IsInstanceOf<FirstContactTranslationMode>(resolver.GetModeBehaviour(flow.entries[^1]));
        }

        [Test]
        public void FacilityMeetingSequenceStartsFromExistingGameplaySeat()
        {
            Scene facilityScene = EditorSceneManager.OpenScene(IntroFacilityScenePath);

            FirstContactMeetingArrivalController meeting =
                FindComponentInScene<FirstContactMeetingArrivalController>(facilityScene);
            Assert.IsNotNull(meeting, "Facility must contain the authored meeting arrival sequence.");
            Assert.IsTrue(meeting.ValidateConfiguration(logErrors: false));

            CameraController cameraController =
                FindComponentInScene<CameraController>(facilityScene);
            Assert.IsNotNull(cameraController);
            Assert.AreSame(
                cameraController.DefaultViewCamera,
                meeting.SeatedCamera,
                "The meeting sequence must end on the existing CM_Default gameplay pose.");
            Assert.AreEqual("CM_Default", meeting.SeatedCamera.name);

            SerializedProperty serializedTargets = new SerializedObject(meeting)
                .FindProperty("lookTargets");
            FirstContactMeetingLookTarget[] targets = Enumerable
                .Range(0, serializedTargets.arraySize)
                .Select(index => serializedTargets
                    .GetArrayElementAtIndex(index)
                    .objectReferenceValue as FirstContactMeetingLookTarget)
                .Where(item => item != null)
                .ToArray();
            MeetingLookTarget[] expectedTargets =
            {
                MeetingLookTarget.Obama,
                MeetingLookTarget.Director,
                MeetingLookTarget.Hwang,
                MeetingLookTarget.Door,
                MeetingLookTarget.Coffee,
                MeetingLookTarget.Terminal
            };
            foreach (MeetingLookTarget expected in expectedTargets)
            {
                Assert.AreEqual(
                    1,
                    targets.Count(item => item.Target == expected),
                    $"Facility must contain exactly one authored {expected} meeting look target.");
            }

            FirstContactMeetingLookTarget directorTarget =
                targets.Single(item => item.Target == MeetingLookTarget.Director);
            Assert.AreEqual("LOOK_Director", directorTarget.name);
            GameObject directorPrefabRoot =
                PrefabUtility.GetNearestPrefabInstanceRoot(directorTarget.gameObject);
            Assert.IsNotNull(directorPrefabRoot);
            Assert.AreEqual("Adjutant", directorPrefabRoot.name);
            Assert.AreEqual(
                "Assets/Prefabs/Adjutant.prefab",
                PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                    directorPrefabRoot));

            FirstContactIntroSequenceController introSequence =
                FindComponentInScene<FirstContactIntroSequenceController>(facilityScene);
            Assert.IsNotNull(introSequence);
            Assert.IsNotNull(introSequence.Guide);
            Assert.AreSame(
                introSequence.Guide.transform,
                meeting.DirectorActor,
                "Meeting staging must reuse the director who guided the player through Facility.");
            Assert.AreSame(
                introSequence.Guide.gameObject,
                directorPrefabRoot,
                "The Director look target and actor must belong to the same carried-through prefab instance.");

            FirstContactMeetingLookTarget hwangTarget =
                targets.Single(item => item.Target == MeetingLookTarget.Hwang);
            Assert.IsNotNull(introSequence.DoctorHwangActor);
            Assert.AreEqual(
                "DoctorHwang_Placeholder",
                introSequence.DoctorHwangActor.name);
            Assert.AreSame(
                introSequence.DoctorHwangActor,
                meeting.DoctorHwangActor,
                "Meeting staging must reuse Doctor Hwang from the Facility briefing.");
            Assert.IsTrue(
                hwangTarget.transform.IsChildOf(introSequence.DoctorHwangActor),
                "The meeting Hwang look target must move with the carried briefing actor.");
            Assert.IsNull(
                FindTransformInScene(facilityScene, "DoctorHwang_Meeting_Placeholder"),
                "Facility must not retain a second Doctor Hwang inside the meeting room.");

            FirstContactIntroGuideController[] directorGuides = facilityScene
                .GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<FirstContactIntroGuideController>(true))
                .ToArray();
            Assert.AreEqual(
                1,
                directorGuides.Length,
                "Facility must not contain a second Director prefab inside the integrated meeting room.");

            Transform meetingDirectorPose = new SerializedObject(introSequence)
                .FindProperty("meetingDirectorPose")
                .objectReferenceValue as Transform;
            Assert.IsNotNull(meetingDirectorPose);
            Assert.AreEqual("POSE_DirectorMeeting", meetingDirectorPose.name);

            var serializedSequence = new SerializedObject(introSequence);
            Transform meetingHwangPose = serializedSequence
                .FindProperty("meetingHwangPose")
                .objectReferenceValue as Transform;
            Assert.IsNotNull(meetingHwangPose);
            Assert.AreEqual("POSE_HwangMeeting", meetingHwangPose.name);

            SerializedProperty hwangPath = serializedSequence
                .FindProperty("meetingHwangApproachPath");
            Assert.AreEqual(3, hwangPath.arraySize);
            Assert.AreEqual(
                "ROUTE_Hwang_07_BriefingExit",
                hwangPath.GetArrayElementAtIndex(0).objectReferenceValue.name);
            Assert.AreEqual(
                "ROUTE_Hwang_08_MeetingConnector",
                hwangPath.GetArrayElementAtIndex(1).objectReferenceValue.name);
            Assert.AreEqual(
                "ROUTE_Hwang_09_MeetingAirlock",
                hwangPath.GetArrayElementAtIndex(2).objectReferenceValue.name);
        }

        [Test]
        public void FacilityContainsConnectedMeetingRoomAndSeat()
        {
            Scene facilityScene = EditorSceneManager.OpenScene(IntroFacilityScenePath);
            Transform meetingRoomRoot = FindTransformInScene(
                facilityScene,
                "MeetingRoom_Integrated");
            Assert.IsNotNull(
                meetingRoomRoot,
                "The meeting room must be authored directly inside Facility.");

            Transform room = meetingRoomRoot.Find("Room");
            Assert.IsNotNull(room, "The original authored Room must remain under the integrated root.");
            Transform table = meetingRoomRoot.Find("Table");
            Assert.IsNotNull(
                table,
                "The original authored Table must move with the integrated meeting room.");
            Assert.IsTrue(
                table.gameObject.activeInHierarchy,
                "The meeting table must remain visible while the dormant gameplay systems are disabled.");
            Assert.AreEqual(
                "Assets/Models/WoodenTable.fbx",
                PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(table.gameObject),
                "The integrated room must retain the original authored table prefab.");

            Bounds roomBounds = CalculateRendererBounds(room);
            Bounds tableBounds = CalculateRendererBounds(table);
            Assert.That(
                roomBounds.size.y,
                Is.InRange(2.9f, 3.2f),
                "The meeting room ceiling must match the three-metre Facility corridor scale.");
            Assert.That(
                tableBounds.size.y,
                Is.InRange(0.68f, 0.82f),
                "The restored meeting table must remain at a normal seated-table height.");

            Transform interactables = meetingRoomRoot.Find("Interactables");
            Assert.IsNotNull(interactables);
            Transform sharedMonitor = interactables.Find("SharedMonitor");
            Assert.IsNotNull(sharedMonitor);
            Assert.That(
                CalculateRendererBounds(sharedMonitor).size.y,
                Is.LessThan(2.5f),
                "The shared monitor must fit beneath the normalized meeting-room ceiling.");
            foreach (string alienName in new[] { "Alien", "Alien (1)", "Alien (2)" })
            {
                Transform alien = interactables.Find(alienName);
                Assert.IsNotNull(alien);
                Assert.That(
                    CalculateRendererBounds(alien).size.y,
                    Is.InRange(1.5f, 1.8f),
                    $"{alienName} must use the same human-scale proportions as the meeting cast.");
            }
            Transform entranceWall = room.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(candidate => candidate.name == "A_Wall (12)");
            Assert.IsNotNull(entranceWall, "The original entrance wall must remain authored in the room.");
            Assert.IsFalse(
                entranceWall.gameObject.activeSelf,
                "The wall behind the Facility airlock must stay disabled so the player can walk through.");
            Vector3 roomEntrance = room.TransformPoint(new Vector3(-4f, -6f, -4f));

            FirstContactMeetingArrivalController meeting =
                FindComponentInScene<FirstContactMeetingArrivalController>(facilityScene);
            Assert.IsNotNull(meeting);
            Assert.IsNotNull(meeting.SeatedViewPreview);

            Transform airlock = FindTransformInScene(facilityScene, "MeetingAirlock");
            Assert.IsNotNull(airlock);
            Assert.That(
                Vector2.Distance(
                    new Vector2(roomEntrance.x, roomEntrance.z),
                    new Vector2(airlock.position.x, airlock.position.z)),
                Is.LessThan(0.02f),
                "The integrated room opening must begin directly behind the Facility airlock.");

            Transform seatedShot = FindTransformInScene(
                facilityScene,
                "SHOT_Meeting_Seated");
            Assert.IsNotNull(seatedShot);
            Assert.That(
                Vector3.Distance(
                    seatedShot.position,
                    meeting.SeatedViewPreview.position),
                Is.LessThan(0.001f),
                "The walk-in seat must hand off to the exact meeting seated camera position.");
            Assert.That(
                Quaternion.Angle(
                    seatedShot.rotation,
                    meeting.SeatedViewPreview.rotation),
                Is.LessThan(0.01f),
                "The walk-in seat and meeting seated view rotations must match.");

            Transform gameplayRoot = FindTransformInScene(
                facilityScene,
                "MeetingGameplaySystems");
            Assert.IsNotNull(gameplayRoot);
            Assert.IsFalse(
                gameplayRoot.gameObject.activeSelf,
                "Meeting cameras, UI, and gameplay systems must stay off during the walk-in.");

            FirstContactIntroInteractable roomInteraction =
                FindComponentInScene<FirstContactIntroInteractable>(
                    facilityScene,
                    "INT_MeetingRoomButton");
            Assert.IsNotNull(roomInteraction);
            Assert.AreEqual(
                FirstContactIntroInteractionAction.EnterMeetingRoom,
                roomInteraction.Action);
            Assert.AreSame(airlock, roomInteraction.Target);
            Assert.AreEqual("POSE_MeetingAirlock_Open", roomInteraction.SecondaryTarget.name);

            FirstContactIntroInteractable seatInteraction =
                FindComponentInScene<FirstContactIntroInteractable>(
                    facilityScene,
                    "INT_PresidentMeetingChair");
            Assert.IsNotNull(seatInteraction);
            Assert.AreEqual(
                FirstContactIntroInteractionAction.TakeMeetingSeat,
                seatInteraction.Action);
            Assert.AreEqual("POSE_PresidentMeetingSeat", seatInteraction.Target.name);
            Assert.AreSame(seatedShot, seatInteraction.SecondaryTarget);
            Assert.IsFalse(
                seatInteraction.IsAvailable,
                "The chair becomes available only after the meeting-room door opens.");

            FirstContactIntroSequenceController sequence =
                FindComponentInScene<FirstContactIntroSequenceController>(facilityScene);
            Assert.IsNotNull(sequence);
            var serializedSequence = new SerializedObject(sequence);
            Assert.AreSame(
                roomInteraction,
                serializedSequence.FindProperty("meetingRoomInteraction").objectReferenceValue);
            Assert.AreSame(
                seatInteraction,
                serializedSequence.FindProperty("meetingSeatInteraction").objectReferenceValue);
        }

        [Test]
        public void FacilityInstallerSwitchesFromWalkInToEmbeddedMeeting()
        {
            Scene facilityScene = EditorSceneManager.OpenScene(IntroFacilityScenePath);
            FirstContactIntroSceneInstaller installer =
                FindComponentInScene<FirstContactIntroSceneInstaller>(facilityScene);
            Transform gameplayRoot = FindTransformInScene(
                facilityScene,
                "MeetingGameplaySystems");
            Transform introHud = FindTransformInScene(facilityScene, "IntroHUD");
            Transform playerRig = FindTransformInScene(facilityScene, "FC_PlayerRig");
            var flow = AssetDatabase.LoadAssetAtPath<DoodleDiplomacy.Data.GameFlowAsset>(GameFlowPath);

            Assert.IsNotNull(installer);
            Assert.IsNotNull(gameplayRoot);
            Assert.IsNotNull(introHud);
            Assert.IsNotNull(playerRig);
            Assert.IsNotNull(flow);

            try
            {
                installer.PrepareEntry(flow.entries[^1]);
                Assert.IsTrue(gameplayRoot.gameObject.activeSelf);
                Assert.IsFalse(introHud.gameObject.activeSelf);
                Assert.IsFalse(playerRig.gameObject.activeSelf);
                Assert.IsInstanceOf<FirstContactTranslationMode>(
                    installer.GetModeBehaviour(flow.entries[^1]));
            }
            finally
            {
                installer.PrepareEntry(flow.entries[1]);
            }

            Assert.IsFalse(gameplayRoot.gameObject.activeSelf);
            Assert.IsTrue(introHud.gameObject.activeSelf);
            Assert.IsTrue(playerRig.gameObject.activeSelf);
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
            Assert.AreEqual("FC_Intro_Facility", flow.entries[1].sceneName);
            Assert.AreEqual(
                flow.entries[1].sceneName,
                flow.entries[2].sceneName,
                "Taking the meeting seat must transition modes without loading another scene.");
            Assert.IsFalse(
                flow.entries[2].unloadPreviousScene,
                "The in-scene meeting transition must not request a scene unload.");
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
        public void RetiredGameSceneContainsNoRuntimeRoots()
        {
            Scene gameScene = EditorSceneManager.OpenScene(GameScenePath);
            Assert.AreEqual(
                0,
                gameScene.GetRootGameObjects().Length,
                "Retired GameScene must not retain a second copy of the meeting room or gameplay systems.");
        }

        [Test]
        public void GameplayFlowScenesAreEnabledInBuildSettings()
        {
            AssertSceneEnabled(MainMenuScenePath);
            AssertSceneEnabled(GameRootScenePath);
            AssertSceneEnabled(IntroSurfaceScenePath);
            AssertSceneEnabled(IntroFacilityScenePath);
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
            StringAssert.Contains(
                "propertyPath: pathPoints.Array.size\n      value: 10",
                sceneText.Replace("\r\n", "\n"));
            StringAssert.Contains("briefingPresentation: {fileID:", sceneText);
            StringAssert.Contains("m_Name: ProjectorImageSurface", sceneText);
            StringAssert.Contains("m_Name: VIEW_PresidentSeatedPreview", sceneText);
            StringAssert.Contains("m_Name: LOOK_ProjectorPresentation", sceneText);
            StringAssert.Contains("m_Name: LOOK_Hwang_Presentation", sceneText);
            StringAssert.Contains("m_Name: LOOK_Hwang_QA", sceneText);
            StringAssert.Contains("m_Name: MeetingRoom_Integrated", sceneText);
            StringAssert.Contains("m_Name: MeetingGameplaySystems", sceneText);
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
            FirstContactIntroSceneInstaller installer =
                Object.FindFirstObjectByType<FirstContactIntroSceneInstaller>(
                    FindObjectsInactive.Include);
            Assert.IsNotNull(installer);
            var serializedInstaller = new SerializedObject(installer);
            Assert.IsNotNull(
                serializedInstaller.FindProperty("embeddedGameplayReferences")
                    .objectReferenceValue);
            GameObject embeddedGameplayRoot = serializedInstaller
                .FindProperty("embeddedGameplayRoot")
                .objectReferenceValue as GameObject;
            Assert.IsNotNull(embeddedGameplayRoot);
            Assert.AreEqual("MeetingGameplaySystems", embeddedGameplayRoot.name);
            Assert.IsFalse(embeddedGameplayRoot.activeSelf);

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

            FirstContactIntroSequenceController introSequence =
                Object.FindFirstObjectByType<FirstContactIntroSequenceController>(
                    FindObjectsInactive.Include);
            Assert.IsNotNull(introSequence);
            var serializedIntroSequence = new SerializedObject(introSequence);
            Assert.IsTrue(
                serializedIntroSequence.FindProperty("enableDebugShortcuts").boolValue);
            Assert.AreEqual(
                (int)UnityEngine.InputSystem.Key.F8,
                serializedIntroSequence.FindProperty("skipToVehicleExitKey").intValue);
            Assert.IsNotNull(
                typeof(FirstContactIntroSequenceController).GetMethod("DebugSkipBriefing"),
                "Facility must expose the F8 briefing-skip debug transition.");
            FirstContactIntroGuideController adjutantGuide = introSequence.Guide;
            Assert.IsNotNull(adjutantGuide);
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

        private static T FindComponentInScene<T>(Scene scene, string objectName = null)
            where T : Component
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (T component in root.GetComponentsInChildren<T>(true))
                {
                    if (string.IsNullOrEmpty(objectName) || component.name == objectName)
                    {
                        return component;
                    }
                }
            }

            return null;
        }

        private static MeshFilter CreatePreviewRenderer(
            Transform parent,
            string objectName,
            out MeshRenderer meshRenderer)
        {
            var previewObject = new GameObject(objectName);
            previewObject.transform.SetParent(parent, false);
            MeshFilter meshFilter = previewObject.AddComponent<MeshFilter>();
            meshRenderer = previewObject.AddComponent<MeshRenderer>();
            return meshFilter;
        }

        private static Transform FindTransformInScene(Scene scene, string objectName)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
                {
                    if (candidate.name == objectName)
                    {
                        return candidate;
                    }
                }
            }

            return null;
        }

        private static Bounds CalculateRendererBounds(Transform root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            Assert.IsNotEmpty(renderers, $"{root.name} must contain authored renderers.");
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return bounds;
        }

    }
}
