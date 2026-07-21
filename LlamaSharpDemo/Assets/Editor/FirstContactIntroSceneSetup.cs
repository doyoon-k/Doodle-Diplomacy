using System.Collections.Generic;
using System.Linq;
using DoodleDiplomacy.Data;
using DoodleDiplomacy.Gameplay.FirstContact;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class FirstContactIntroSceneSetup
{
    private const string SceneFolder = "Assets/Scenes/FirstContact";
    private const string SurfaceScenePath = SceneFolder + "/FC_Intro_Surface.unity";
    private const string FacilityScenePath = SceneFolder + "/FC_Intro_Facility.unity";
    private const string MaterialFolder = "Assets/Materials/FirstContact/Graybox";
    private const string DataFolder = "Assets/Data/FirstContact";
    private const string SurfaceEntryPath = DataFolder + "/FlowEntry_FirstContactIntroSurface.asset";
    private const string FacilityEntryPath = DataFolder + "/FlowEntry_FirstContactIntroFacility.asset";
    private const string FullFlowPath = DataFolder + "/FirstContactGameFlow.asset";
    private const string TranslationAfterIntroEntryPath = DataFolder + "/FlowEntry_FirstContactTranslationAfterIntro.asset";

    [MenuItem("Tools/First Contact/Build Intro Graybox Scenes")]
    public static void RebuildAllFromMenu()
    {
        BuildAll(true);
        EditorUtility.DisplayDialog(
            "First Contact Intro",
            "Surface and facility graybox scenes were rebuilt.",
            "OK");
    }

    public static void BuildAllFromCommandLine()
    {
        BuildAll(true);
    }

    [MenuItem("Tools/First Contact/Validate Intro Graybox Scenes")]
    public static void ValidateAll()
    {
        List<string> errors = new();
        ValidateScene(SurfaceScenePath, FirstContactIntroSegment.Surface, 6, 4, 2, errors);
        ValidateScene(FacilityScenePath, FirstContactIntroSegment.Facility, 10, 5, 2, errors);

        HashSet<string> buildScenePaths = EditorBuildSettings.scenes
            .Where(scene => scene.enabled)
            .Select(scene => scene.path)
            .ToHashSet();
        if (!buildScenePaths.Contains(SurfaceScenePath))
        {
            errors.Add($"Build Settings is missing {SurfaceScenePath}.");
        }

        if (!buildScenePaths.Contains(FacilityScenePath))
        {
            errors.Add($"Build Settings is missing {FacilityScenePath}.");
        }

        GameFlowAsset fullFlow = AssetDatabase.LoadAssetAtPath<GameFlowAsset>(FullFlowPath);
        if (fullFlow == null || fullFlow.entries == null || fullFlow.entries.Length != 3)
        {
            errors.Add("FirstContactGameFlow must contain surface, facility, and translation entries.");
        }

        if (errors.Count > 0)
        {
            foreach (string error in errors)
            {
                Debug.LogError("[FirstContactIntroSceneSetup] " + error);
            }

            throw new System.InvalidOperationException(
                $"First Contact intro graybox validation failed with {errors.Count} error(s).");
        }

        Debug.Log("[FirstContactIntroSceneSetup] Validation passed: 2 scenes, route anchors, triggers, cameras, flow assets, and Build Settings are valid.");
    }

    private static void BuildAll(bool overwrite)
    {
        EnsureFolder("Assets/Scenes", "FirstContact");
        EnsureFolder("Assets/Materials", "FirstContact");
        EnsureFolder("Assets/Materials/FirstContact", "Graybox");

        GrayboxMaterials materials = CreateMaterials();
        if (overwrite || AssetDatabase.LoadAssetAtPath<SceneAsset>(SurfaceScenePath) == null)
        {
            BuildSurfaceScene(materials);
        }

        if (overwrite || AssetDatabase.LoadAssetAtPath<SceneAsset>(FacilityScenePath) == null)
        {
            BuildFacilityScene(materials);
        }

        CreateFlowAssets();
        UpdateBuildSettings();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[FirstContactIntroSceneSetup] Intro graybox scenes and basic flow assets are ready.");
    }

    private static void ValidateScene(
        string path,
        FirstContactIntroSegment expectedSegment,
        int minimumRoutePoints,
        int minimumTriggers,
        int minimumInteractions,
        ICollection<string> errors)
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null)
        {
            errors.Add($"Scene asset is missing: {path}.");
            return;
        }

        Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        try
        {
            GameObject[] roots = scene.GetRootGameObjects();
            FirstContactIntroSceneReferences references = roots
                .SelectMany(root => root.GetComponentsInChildren<FirstContactIntroSceneReferences>(true))
                .FirstOrDefault();
            FirstContactIntroSceneInstaller installer = roots
                .SelectMany(root => root.GetComponentsInChildren<FirstContactIntroSceneInstaller>(true))
                .FirstOrDefault();
            int cameraCount = roots.Sum(root => root.GetComponentsInChildren<Camera>(true).Length);
            int triggerCount = roots.Sum(root => root.GetComponentsInChildren<FirstContactIntroTriggerMarker>(true).Length);
            int interactionCount = roots.Sum(root => root.GetComponentsInChildren<FirstContactIntroInteractable>(true).Length);
            int playerCount = roots.Sum(root => root.GetComponentsInChildren<FirstContactIntroPlayerController>(true).Length);
            int guideCount = roots.Sum(root => root.GetComponentsInChildren<FirstContactIntroGuideController>(true).Length);
            int sequenceCount = roots.Sum(root => root.GetComponentsInChildren<FirstContactIntroSequenceController>(true).Length);
            int hudCount = roots.Sum(root => root.GetComponentsInChildren<FirstContactIntroHud>(true).Length);
            int missingScriptCount = roots
                .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .Sum(transform => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject));

            if (references == null)
            {
                errors.Add($"{path} has no FirstContactIntroSceneReferences component.");
            }
            else
            {
                if (references.Segment != expectedSegment)
                {
                    errors.Add($"{path} has the wrong intro segment value.");
                }

                if (references.PlayerSpawn == null || references.EntryPoint == null || references.ExitPoint == null)
                {
                    errors.Add($"{path} is missing a spawn, entry, or exit anchor.");
                }

                if (references.RoutePoints == null || references.RoutePoints.Length < minimumRoutePoints)
                {
                    errors.Add($"{path} requires at least {minimumRoutePoints} route points.");
                }
                else
                {
                    ValidateRouteClearance(path, references.RoutePoints, errors);
                }

                if (references.EnvironmentRoot == null || references.CharacterRoot == null ||
                    references.CinematicRoot == null || references.TriggerRoot == null || references.AudioRoot == null)
                {
                    errors.Add($"{path} has an incomplete root reference set.");
                }
            }

            if (installer == null || installer.GetDefaultModeBehaviour() == null)
            {
                errors.Add($"{path} has no configured intro scene installer/default mode.");
            }

            if (cameraCount != 1)
            {
                errors.Add($"{path} must contain exactly one camera; found {cameraCount}.");
            }

            if (triggerCount < minimumTriggers)
            {
                errors.Add($"{path} requires at least {minimumTriggers} progression triggers; found {triggerCount}.");
            }

            if (interactionCount < minimumInteractions)
            {
                errors.Add($"{path} requires at least {minimumInteractions} intro interactions; found {interactionCount}.");
            }

            if (playerCount != 1 || guideCount != 1 || sequenceCount != 1 || hudCount != 1)
            {
                errors.Add($"{path} requires exactly one player, guide, sequence controller, and HUD.");
            }

            if (missingScriptCount > 0)
            {
                errors.Add($"{path} contains {missingScriptCount} missing script reference(s).");
            }
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static void ValidateRouteClearance(
        string scenePath,
        Transform[] routePoints,
        ICollection<string> errors)
    {
        const float sampleSpacing = 0.25f;
        const float capsuleRadius = 0.28f;

        for (int segmentIndex = 0; segmentIndex < routePoints.Length - 1; segmentIndex++)
        {
            Transform from = routePoints[segmentIndex];
            Transform to = routePoints[segmentIndex + 1];
            if (from == null || to == null)
            {
                continue;
            }

            float distance = Vector3.Distance(from.position, to.position);
            int sampleCount = Mathf.Max(1, Mathf.CeilToInt(distance / sampleSpacing));
            for (int sampleIndex = 0; sampleIndex <= sampleCount; sampleIndex++)
            {
                Vector3 position = Vector3.Lerp(
                    from.position,
                    to.position,
                    sampleIndex / (float)sampleCount);
                Vector3 lower = position + Vector3.up * 0.55f;
                Vector3 upper = position + Vector3.up * 1.55f;
                Collider blockingCollider = Physics.OverlapCapsule(
                        lower,
                        upper,
                        capsuleRadius,
                        ~0,
                        QueryTriggerInteraction.Ignore)
                    .FirstOrDefault(collider =>
                    {
                        if (collider.gameObject.scene != from.gameObject.scene)
                        {
                            return false;
                        }

                        FirstContactIntroInteractable interactable =
                            collider.GetComponentInParent<FirstContactIntroInteractable>();
                        return interactable == null ||
                               interactable.Action != FirstContactIntroInteractionAction.TakeBriefingSeat;
                    });
                if (blockingCollider == null)
                {
                    continue;
                }

                errors.Add(
                    $"{scenePath} route segment {segmentIndex}->{segmentIndex + 1} is blocked by " +
                    $"'{blockingCollider.name}' near {position}.");
                break;
            }
        }
    }

    private static GrayboxMaterials CreateMaterials()
    {
        return new GrayboxMaterials
        {
            Exterior = CreateMaterial("Graybox_Exterior", new Color(0.23f, 0.27f, 0.31f)),
            WarmWall = CreateMaterial("Graybox_PizzaWall", new Color(0.58f, 0.30f, 0.20f)),
            WarmFloor = CreateMaterial("Graybox_PizzaFloor", new Color(0.29f, 0.18f, 0.14f)),
            FacilityWall = CreateMaterial("Graybox_FacilityWall", new Color(0.24f, 0.34f, 0.38f)),
            FacilityFloor = CreateMaterial("Graybox_FacilityFloor", new Color(0.09f, 0.13f, 0.15f)),
            Prop = CreateMaterial("Graybox_Prop", new Color(0.38f, 0.40f, 0.42f)),
            Accent = CreateMaterial("Graybox_Accent", new Color(0.96f, 0.28f, 0.10f), true),
            Route = CreateMaterial("Graybox_Route", new Color(0.12f, 0.68f, 0.86f), true),
            Character = CreateMaterial("Graybox_Character", new Color(0.32f, 0.50f, 0.80f)),
            Crowd = CreateMaterial("Graybox_Crowd", new Color(0.68f, 0.62f, 0.48f)),
            Doctor = CreateMaterial("Graybox_Doctor", new Color(0.78f, 0.82f, 0.86f)),
            Screen = CreateMaterial("Graybox_Screen", new Color(0.18f, 0.72f, 0.80f), true)
        };
    }

    private static Material CreateMaterial(string name, Color color, bool emissive = false)
    {
        string path = MaterialFolder + "/" + name + ".mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            material = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(material, path);
        }

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }
        else
        {
            material.color = color;
        }

        if (emissive && material.HasProperty("_EmissionColor"))
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color * 2f);
        }

        EditorUtility.SetDirty(material);
        return material;
    }

    private static void BuildSurfaceScene(GrayboxMaterials materials)
    {
        Scene previousActiveScene = SceneManager.GetActiveScene();
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        CloseLoadedGeneratedScene(SurfaceScenePath, scene);
        SceneManager.SetActiveScene(scene);
        scene.name = "FC_Intro_Surface";

        GameObject setup = new("SceneSetup");
        GameObject environment = new("Environment");
        GameObject characters = new("Characters_Placeholders");
        GameObject cinematics = new("Cinematics_Anchors");
        GameObject triggers = new("ProgressionTriggers");
        GameObject audio = new("Audio");
        GameObject route = new("NavigationRoute");

        FirstContactIntroSceneReferences references = setup.AddComponent<FirstContactIntroSceneReferences>();
        FirstContactIntroMode mode = setup.AddComponent<FirstContactIntroMode>();
        FirstContactIntroSceneInstaller installer = setup.AddComponent<FirstContactIntroSceneInstaller>();
        FirstContactIntroSequenceController sequence = setup.AddComponent<FirstContactIntroSequenceController>();

        ConfigureSurfaceLighting();
        BuildCarStage(environment.transform, materials);
        BuildPizzaExterior(environment.transform, materials);
        BuildPizzaInterior(environment.transform, materials);

        Transform playerSpawn = CreateAnchor("PlayerSpawn_Car", setup.transform, new Vector3(-22f, 0.1f, -1.4f));
        Transform entry = CreateAnchor("SurfaceEntry_Parking", route.transform, new Vector3(0f, 0.1f, 0f));
        Transform[] routePoints =
        {
            entry,
            CreateAnchor("Route_01_PizzaDoor", route.transform, new Vector3(0f, 0.1f, 6.5f)),
            CreateAnchor("Route_02_Dining", route.transform, new Vector3(0f, 0.1f, 13f)),
            CreateAnchor("Route_03_Kitchen", route.transform, new Vector3(0f, 0.1f, 22f)),
            CreateAnchor("Route_04_Storage", route.transform, new Vector3(0f, 0.1f, 29f)),
            CreateAnchor("Route_05_Elevator", route.transform, new Vector3(0f, 0.1f, 34f))
        };
        Transform exit = CreateAnchor("SurfaceExit_Elevator", route.transform, new Vector3(0f, 0.1f, 35.2f));

        FirstContactIntroGuideController guide = BuildSurfaceCharacters(
            characters.transform,
            materials,
            routePoints);
        FirstContactIntroHud hud = CreateIntroHud();
        FirstContactIntroPlayerController player = CreatePlayerRig(
            playerSpawn,
            new Vector3(-22f, 1.35f, 1.2f),
            hud);

        GameObject carDoorHandle = CreateCube(
            "INT_CarDoorHandle",
            environment.transform,
            new Vector3(-20.99f, 1.05f, -0.65f),
            new Vector3(0.18f, 0.18f, 0.58f),
            materials.Accent);
        FirstContactIntroInteractable exitVehicle = carDoorHandle.AddComponent<FirstContactIntroInteractable>();

        GameObject elevatorButton = CreateCube(
            "INT_ElevatorButton",
            environment.transform,
            new Vector3(1.78f, 1.2f, 33.1f),
            new Vector3(0.12f, 0.28f, 0.28f),
            materials.Accent);
        FirstContactIntroInteractable useElevator = elevatorButton.AddComponent<FirstContactIntroInteractable>();

        CreateSurfaceShotAnchors(cinematics.transform);
        CreateTrigger("TRG_PizzaEntry", "pizza-entry", triggers.transform, new Vector3(0f, 1.25f, 7f), new Vector3(3f, 2.5f, 1f));
        CreateTrigger("TRG_CitizenEncounter", "citizen-encounter", triggers.transform, new Vector3(0f, 1.25f, 12f), new Vector3(5f, 2.5f, 1f));
        CreateTrigger("TRG_SecretDoorReveal", "secret-door-reveal", triggers.transform, new Vector3(0f, 1.25f, 31f), new Vector3(4f, 2.5f, 1f));
        CreateTrigger("TRG_ElevatorBoard", "elevator-board", triggers.transform, new Vector3(0f, 1.25f, 34f), new Vector3(3f, 2.5f, 2f));

        references.Configure(
            "first-contact-intro-surface",
            FirstContactIntroSegment.Surface,
            environment.transform,
            characters.transform,
            cinematics.transform,
            triggers.transform,
            audio.transform,
            playerSpawn,
            entry,
            exit,
            routePoints);
        sequence.Configure(
            FirstContactIntroSegment.Surface,
            mode,
            player,
            hud,
            guide,
            exitVehicle,
            useElevator,
            null,
            null);
        exitVehicle.Configure(
            FirstContactIntroInteractionAction.ExitVehicle,
            "first_contact.intro.prompt.exit_vehicle",
            "[ E ]  EXIT VEHICLE",
            entry,
            null,
            sequence,
            true);
        useElevator.Configure(
            FirstContactIntroInteractionAction.UseElevator,
            "first_contact.intro.prompt.use_elevator",
            "[ E ]  USE ELEVATOR",
            exit,
            null,
            sequence,
            false);
        mode.Configure("first-contact-intro-surface", references, sequence);
        installer.Configure("first-contact-intro-surface", mode);

        if (!EditorSceneManager.SaveScene(scene, SurfaceScenePath))
        {
            throw new System.InvalidOperationException($"Could not save generated scene at {SurfaceScenePath}.");
        }

        EditorSceneManager.CloseScene(scene, true);
        RestoreActiveScene(previousActiveScene);
    }

    private static void BuildFacilityScene(GrayboxMaterials materials)
    {
        Scene previousActiveScene = SceneManager.GetActiveScene();
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        CloseLoadedGeneratedScene(FacilityScenePath, scene);
        SceneManager.SetActiveScene(scene);
        scene.name = "FC_Intro_Facility";

        GameObject setup = new("SceneSetup");
        GameObject environment = new("Environment");
        GameObject characters = new("Characters_Placeholders");
        GameObject cinematics = new("Cinematics_Anchors");
        GameObject triggers = new("ProgressionTriggers");
        GameObject audio = new("Audio");
        GameObject route = new("NavigationRoute");

        FirstContactIntroSceneReferences references = setup.AddComponent<FirstContactIntroSceneReferences>();
        FirstContactIntroMode mode = setup.AddComponent<FirstContactIntroMode>();
        FirstContactIntroSceneInstaller installer = setup.AddComponent<FirstContactIntroSceneInstaller>();
        FirstContactIntroSequenceController sequence = setup.AddComponent<FirstContactIntroSequenceController>();

        ConfigureFacilityLighting();
        BuildFacilityEnvironment(environment.transform, materials);

        Transform playerSpawn = CreateAnchor("PlayerSpawn_Elevator", setup.transform, new Vector3(0f, 0.1f, 0f));
        Transform entry = CreateAnchor("FacilityEntry_Elevator", route.transform, new Vector3(0f, 0.1f, 1.5f));
        Transform[] routePoints =
        {
            entry,
            CreateAnchor("Route_01_MainCorridor", route.transform, new Vector3(0f, 0.1f, 10f)),
            CreateAnchor("Route_02_CorridorCorner", route.transform, new Vector3(0f, 0.1f, 20f)),
            CreateAnchor("Route_03_BriefingDoor", route.transform, new Vector3(6.5f, 0.1f, 20f)),
            CreateAnchor("Route_04_BriefingEntry", route.transform, new Vector3(8.5f, 0.1f, 20f)),
            CreateAnchor("Route_05_BriefingSouth", route.transform, new Vector3(8.5f, 0.1f, 17f)),
            CreateAnchor("Route_06_BriefingEast", route.transform, new Vector3(17.5f, 0.1f, 17f)),
            CreateAnchor("Route_07_BriefingNorthExit", route.transform, new Vector3(17.5f, 0.1f, 20f)),
            CreateAnchor("Route_08_MeetingConnector", route.transform, new Vector3(19f, 0.1f, 20f)),
            CreateAnchor("Route_09_MeetingAirlock", route.transform, new Vector3(25f, 0.1f, 20f))
        };
        Transform exit = CreateAnchor("FacilityExit_MeetingRoom", route.transform, new Vector3(26.5f, 0.1f, 20f));

        Transform seatTarget = CreateAnchor("SeatTarget_President", route.transform, new Vector3(12f, 0.1f, 16.8f));
        seatTarget.LookAt(new Vector3(12f, 1.2f, 20f));
        Transform seatExit = CreateAnchor("SeatExit_President", route.transform, new Vector3(9.4f, 0.1f, 17f));
        seatExit.LookAt(new Vector3(12f, 1.2f, 20f));

        FirstContactIntroGuideController guide = BuildFacilityCharacters(
            characters.transform,
            materials,
            routePoints);
        FirstContactIntroHud hud = CreateIntroHud();
        FirstContactIntroPlayerController player = CreatePlayerRig(
            playerSpawn,
            new Vector3(0f, 1.5f, 6f),
            hud);

        GameObject briefingChair = CreateCube(
            "INT_PresidentBriefingChair",
            environment.transform,
            new Vector3(12f, 0.5f, 16.8f),
            new Vector3(0.9f, 1f, 0.9f),
            materials.Accent);
        FirstContactIntroInteractable takeSeat = briefingChair.AddComponent<FirstContactIntroInteractable>();

        GameObject meetingButton = CreateCube(
            "INT_MeetingRoomButton",
            environment.transform,
            new Vector3(25.72f, 1.2f, 19.1f),
            new Vector3(0.12f, 0.28f, 0.28f),
            materials.Accent);
        FirstContactIntroInteractable enterMeeting = meetingButton.AddComponent<FirstContactIntroInteractable>();

        CreateFacilityShotAnchors(cinematics.transform);
        CreateTrigger("TRG_ElevatorExit", "elevator-exit", triggers.transform, new Vector3(0f, 1.25f, 2f), new Vector3(2.5f, 2.5f, 1f));
        CreateTrigger("TRG_CorridorCorner", "corridor-corner", triggers.transform, new Vector3(0f, 1.25f, 19f), new Vector3(2.5f, 2.5f, 2f));
        CreateTrigger("TRG_BriefingEnter", "briefing-enter", triggers.transform, new Vector3(7f, 1.25f, 20f), new Vector3(1f, 2.5f, 3f));
        CreateTrigger("TRG_BriefingSeat", "briefing-seat", triggers.transform, new Vector3(12f, 1.25f, 18.5f), new Vector3(2f, 2.5f, 2f));
        CreateTrigger("TRG_MeetingTransition", "meeting-transition", triggers.transform, new Vector3(25f, 1.25f, 20f), new Vector3(1f, 2.5f, 3f));

        references.Configure(
            "first-contact-intro-facility",
            FirstContactIntroSegment.Facility,
            environment.transform,
            characters.transform,
            cinematics.transform,
            triggers.transform,
            audio.transform,
            playerSpawn,
            entry,
            exit,
            routePoints);
        sequence.Configure(
            FirstContactIntroSegment.Facility,
            mode,
            player,
            hud,
            guide,
            null,
            null,
            takeSeat,
            enterMeeting);
        takeSeat.Configure(
            FirstContactIntroInteractionAction.TakeBriefingSeat,
            "first_contact.intro.prompt.sit",
            "[ E ]  SIT",
            seatTarget,
            seatExit,
            sequence,
            false);
        enterMeeting.Configure(
            FirstContactIntroInteractionAction.EnterMeetingRoom,
            "first_contact.intro.prompt.enter_meeting",
            "[ E ]  ENTER MEETING ROOM",
            exit,
            null,
            sequence,
            false);
        mode.Configure("first-contact-intro-facility", references, sequence);
        installer.Configure("first-contact-intro-facility", mode);

        if (!EditorSceneManager.SaveScene(scene, FacilityScenePath))
        {
            throw new System.InvalidOperationException($"Could not save generated scene at {FacilityScenePath}.");
        }

        EditorSceneManager.CloseScene(scene, true);
        RestoreActiveScene(previousActiveScene);
    }

    private static void CloseLoadedGeneratedScene(string scenePath, Scene replacementScene)
    {
        Scene loadedScene = SceneManager.GetSceneByPath(scenePath);
        if (loadedScene.IsValid() && loadedScene.isLoaded && loadedScene != replacementScene)
        {
            EditorSceneManager.CloseScene(loadedScene, true);
        }
    }

    private static void BuildCarStage(Transform root, GrayboxMaterials materials)
    {
        GameObject car = new("CarInterior_Graybox");
        car.transform.SetParent(root);
        CreateCube("Floor", car.transform, new Vector3(-22f, 0f, 0f), new Vector3(2.4f, 0.2f, 5f), materials.Exterior);
        CreateCube("LeftPanel", car.transform, new Vector3(-23.2f, 1.1f, 0f), new Vector3(0.15f, 2.2f, 5f), materials.Exterior);
        CreateCube("RightPanel", car.transform, new Vector3(-20.8f, 1.1f, 0f), new Vector3(0.15f, 2.2f, 5f), materials.Exterior);
        CreateCube("RightExitDoorInset", car.transform, new Vector3(-20.91f, 1.1f, -0.65f), new Vector3(0.05f, 1.85f, 1.65f), materials.Prop);
        CreateCube("RightExitDoorMarker", car.transform, new Vector3(-20.96f, 1.62f, -0.65f), new Vector3(0.05f, 0.09f, 1.05f), materials.Accent);
        CreateCube("RearSeat", car.transform, new Vector3(-22f, 0.55f, -1.55f), new Vector3(2.1f, 0.9f, 0.8f), materials.Prop);
        CreateCube("FrontSeatBack_L", car.transform, new Vector3(-22.55f, 1f, 1.2f), new Vector3(0.85f, 1.5f, 0.35f), materials.Prop);
        CreateCube("FrontSeatBack_R", car.transform, new Vector3(-21.45f, 1f, 1.2f), new Vector3(0.85f, 1.5f, 0.35f), materials.Prop);
        CreateCube("NewsMonitor", car.transform, new Vector3(-22f, 1.35f, 1.05f), new Vector3(1.15f, 0.65f, 0.08f), materials.Screen);
    }

    private static void BuildPizzaExterior(Transform root, GrayboxMaterials materials)
    {
        GameObject exterior = new("PizzaExterior_Graybox");
        exterior.transform.SetParent(root);
        CreateCube("ParkingLot", exterior.transform, new Vector3(0f, -0.12f, 1.5f), new Vector3(22f, 0.2f, 9f), materials.Exterior);
        CreateCube("Facade_Left", exterior.transform, new Vector3(-4f, 1.5f, 6f), new Vector3(6f, 3f, 0.25f), materials.WarmWall);
        CreateCube("Facade_Right", exterior.transform, new Vector3(4f, 1.5f, 6f), new Vector3(6f, 3f, 0.25f), materials.WarmWall);
        CreateCube("DoorHeader", exterior.transform, new Vector3(0f, 2.65f, 6f), new Vector3(2f, 0.7f, 0.25f), materials.WarmWall);
        CreateCube("SignBacking", exterior.transform, new Vector3(0f, 3.75f, 6.05f), new Vector3(8f, 1.2f, 0.25f), materials.Accent);
        CreateWorldLabel("Zaucer Pizza", exterior.transform, new Vector3(0f, 3.75f, 5.88f), Quaternion.Euler(0f, 180f, 0f), 0.55f);
    }

    private static void BuildPizzaInterior(Transform root, GrayboxMaterials materials)
    {
        GameObject interior = new("PizzaInterior_Graybox");
        interior.transform.SetParent(root);

        CreateCube("DiningFloor", interior.transform, new Vector3(0f, -0.1f, 12f), new Vector3(14f, 0.2f, 12f), materials.WarmFloor);
        CreateCube("DiningWall_L", interior.transform, new Vector3(-7f, 1.5f, 12f), new Vector3(0.2f, 3f, 12f), materials.WarmWall);
        CreateCube("DiningWall_R", interior.transform, new Vector3(7f, 1.5f, 12f), new Vector3(0.2f, 3f, 12f), materials.WarmWall);

        CreateCube("KitchenFloor", interior.transform, new Vector3(0f, -0.1f, 22f), new Vector3(10f, 0.2f, 8f), materials.WarmFloor);
        CreateCube("KitchenWall_L", interior.transform, new Vector3(-5f, 1.5f, 22f), new Vector3(0.2f, 3f, 8f), materials.WarmWall);
        CreateCube("KitchenWall_R", interior.transform, new Vector3(5f, 1.5f, 22f), new Vector3(0.2f, 3f, 8f), materials.WarmWall);
        CreateCube("KitchenCounter_L", interior.transform, new Vector3(-3.8f, 0.55f, 22f), new Vector3(1.2f, 1.1f, 6f), materials.Prop);
        CreateCube("KitchenCounter_R", interior.transform, new Vector3(3.8f, 0.55f, 22f), new Vector3(1.2f, 1.1f, 6f), materials.Prop);

        CreateCube("StorageFloor", interior.transform, new Vector3(0f, -0.1f, 29f), new Vector3(7f, 0.2f, 6f), materials.WarmFloor);
        CreateCube("StorageWall_L", interior.transform, new Vector3(-3.5f, 1.5f, 29f), new Vector3(0.2f, 3f, 6f), materials.WarmWall);
        CreateCube("StorageWall_R", interior.transform, new Vector3(3.5f, 1.5f, 29f), new Vector3(0.2f, 3f, 6f), materials.WarmWall);
        CreateCube("StorageShelf_L", interior.transform, new Vector3(-2.6f, 1f, 29f), new Vector3(0.8f, 2f, 4.5f), materials.Prop);
        CreateCube("SecretShelf_Open_L", interior.transform, new Vector3(-2.5f, 1.2f, 31.7f), new Vector3(1.6f, 2.4f, 0.6f), materials.Prop);
        CreateCube("SecretShelf_Open_R", interior.transform, new Vector3(2.5f, 1.2f, 31.7f), new Vector3(1.6f, 2.4f, 0.6f), materials.Prop);

        CreateCube("ElevatorFloor", interior.transform, new Vector3(0f, -0.1f, 34f), new Vector3(4f, 0.2f, 4f), materials.FacilityFloor);
        CreateCube("ElevatorWall_L", interior.transform, new Vector3(-2f, 1.5f, 34f), new Vector3(0.2f, 3f, 4f), materials.FacilityWall);
        CreateCube("ElevatorWall_R", interior.transform, new Vector3(2f, 1.5f, 34f), new Vector3(0.2f, 3f, 4f), materials.FacilityWall);
        CreateCube("ElevatorBack", interior.transform, new Vector3(0f, 1.5f, 36f), new Vector3(4f, 3f, 0.2f), materials.FacilityWall);

        for (int i = 0; i < 4; i++)
        {
            float z = 9.5f + i * 2.3f;
            CreateCube($"DiningTable_{i + 1:00}", interior.transform, new Vector3(i % 2 == 0 ? -3.5f : 3.5f, 0.7f, z), new Vector3(2.4f, 0.15f, 1.4f), materials.Prop);
        }
    }

    private static void BuildFacilityEnvironment(Transform root, GrayboxMaterials materials)
    {
        GameObject facility = new("UndergroundFacility_Graybox");
        facility.transform.SetParent(root);

        CreateCube("ElevatorFloor", facility.transform, new Vector3(0f, -0.1f, 0f), new Vector3(4f, 0.2f, 4f), materials.FacilityFloor);
        CreateCube("ElevatorWall_L", facility.transform, new Vector3(-2f, 1.5f, 0f), new Vector3(0.2f, 3f, 4f), materials.FacilityWall);
        CreateCube("ElevatorWall_R", facility.transform, new Vector3(2f, 1.5f, 0f), new Vector3(0.2f, 3f, 4f), materials.FacilityWall);
        CreateCube("ElevatorBack", facility.transform, new Vector3(0f, 1.5f, -2f), new Vector3(4f, 3f, 0.2f), materials.FacilityWall);

        CreateCube("MainCorridorFloor", facility.transform, new Vector3(0f, -0.1f, 11f), new Vector3(3f, 0.2f, 22f), materials.FacilityFloor);
        CreateCube("MainCorridorWall_L", facility.transform, new Vector3(-1.5f, 1.5f, 11f), new Vector3(0.2f, 3f, 22f), materials.FacilityWall);
        CreateCube("MainCorridorWall_R_Lower", facility.transform, new Vector3(1.5f, 1.5f, 8.75f), new Vector3(0.2f, 3f, 17.5f), materials.FacilityWall);
        CreateCube("TurnCorridorFloor", facility.transform, new Vector3(4f, -0.1f, 20f), new Vector3(8f, 0.2f, 3f), materials.FacilityFloor);
        CreateCube("TurnCorridorWall_N", facility.transform, new Vector3(4f, 1.5f, 21.5f), new Vector3(8f, 3f, 0.2f), materials.FacilityWall);
        CreateCube("TurnCorridorWall_S", facility.transform, new Vector3(4.8f, 1.5f, 18.5f), new Vector3(6.4f, 3f, 0.2f), materials.FacilityWall);

        CreateCube("BriefingFloor", facility.transform, new Vector3(12f, -0.1f, 20f), new Vector3(12f, 0.2f, 10f), materials.FacilityFloor);
        CreateCube("BriefingWall_N", facility.transform, new Vector3(12f, 1.5f, 25f), new Vector3(12f, 3f, 0.2f), materials.FacilityWall);
        CreateCube("BriefingWall_S", facility.transform, new Vector3(12f, 1.5f, 15f), new Vector3(12f, 3f, 0.2f), materials.FacilityWall);
        CreateCube("BriefingTable", facility.transform, new Vector3(12f, 0.75f, 20f), new Vector3(6f, 0.2f, 3f), materials.Prop);
        CreateCube("ProjectorScreen", facility.transform, new Vector3(12f, 1.65f, 24.82f), new Vector3(5.5f, 2.5f, 0.08f), materials.Screen);
        CreateWorldLabel("TRANSLATOR BRIEFING", facility.transform, new Vector3(12f, 1.7f, 24.7f), Quaternion.Euler(0f, 180f, 0f), 0.25f);

        CreateCube("MeetingConnectorFloor", facility.transform, new Vector3(22f, -0.1f, 20f), new Vector3(8f, 0.2f, 3f), materials.FacilityFloor);
        CreateCube("MeetingConnectorWall_N", facility.transform, new Vector3(22f, 1.5f, 21.5f), new Vector3(8f, 3f, 0.2f), materials.FacilityWall);
        CreateCube("MeetingConnectorWall_S", facility.transform, new Vector3(22f, 1.5f, 18.5f), new Vector3(8f, 3f, 0.2f), materials.FacilityWall);
        CreateCube("MeetingAirlock", facility.transform, new Vector3(26f, 1.5f, 20f), new Vector3(0.25f, 3f, 3f), materials.Accent);

        for (int i = 0; i < 5; i++)
        {
            float z = 5f + i * 3.5f;
            CreateCube($"EvidenceFrame_{i + 1:00}", facility.transform, new Vector3(-1.37f, 1.65f, z), new Vector3(0.08f, 1.1f, 1.5f), materials.Screen);
        }
    }

    private static FirstContactIntroGuideController BuildSurfaceCharacters(
        Transform root,
        GrayboxMaterials materials,
        Transform[] routePoints)
    {
        GameObject director = CreateCharacter("Director_Placeholder", root, new Vector3(1f, 1f, 2.5f), materials.Character);
        director.GetComponent<CapsuleCollider>().isTrigger = true;
        FirstContactIntroGuideController guide = director.AddComponent<FirstContactIntroGuideController>();
        guide.Configure(routePoints, System.Array.Empty<int>());
        CreateCharacter("Citizen_01_Placeholder", root, new Vector3(-3.5f, 1f, 10f), materials.Crowd);
        CreateCharacter("Citizen_02_Placeholder", root, new Vector3(3.5f, 1f, 12f), materials.Crowd);
        CreateCharacter("Citizen_03_Placeholder", root, new Vector3(-3.5f, 1f, 14f), materials.Crowd);
        CreateCharacter("Citizen_04_Placeholder", root, new Vector3(3.5f, 1f, 16f), materials.Crowd);
        return guide;
    }

    private static FirstContactIntroGuideController BuildFacilityCharacters(
        Transform root,
        GrayboxMaterials materials,
        Transform[] routePoints)
    {
        GameObject director = CreateCharacter("Director_Placeholder", root, new Vector3(0.8f, 1f, 5f), materials.Character);
        director.GetComponent<CapsuleCollider>().isTrigger = true;
        FirstContactIntroGuideController guide = director.AddComponent<FirstContactIntroGuideController>();
        guide.Configure(routePoints, new[] { 3 });
        CreateCharacter("DoctorHwang_Placeholder", root, new Vector3(15.5f, 1f, 22.5f), materials.Doctor);
        CreateCharacter("Obama_Placeholder", root, new Vector3(24f, 1f, 20.8f), materials.Character);

        Vector3[] staffPositions =
        {
            new(8.5f, 1f, 23.2f), new(11.5f, 1f, 23.2f), new(14.5f, 1f, 23.2f),
            new(8.5f, 1f, 16.2f), new(15.5f, 1f, 16.2f), new(16.5f, 1f, 20f)
        };
        for (int i = 0; i < staffPositions.Length; i++)
        {
            GameObject staff = CreateCharacter($"Staff_{i + 1:00}_Placeholder", root, staffPositions[i], materials.Crowd);
            staff.GetComponent<CapsuleCollider>().isTrigger = true;
        }

        return guide;
    }

    private static void ConfigureSurfaceLighting()
    {
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.40f, 0.45f, 0.52f);
        RenderSettings.ambientEquatorColor = new Color(0.25f, 0.22f, 0.20f);
        RenderSettings.ambientGroundColor = new Color(0.08f, 0.08f, 0.10f);

        GameObject sun = new("Directional Light");
        Light light = sun.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.1f;
        light.color = new Color(1f, 0.82f, 0.68f);
        sun.transform.rotation = Quaternion.Euler(38f, -28f, 0f);

        CreatePointLight("PizzaInterior_WarmLight", new Vector3(0f, 2.6f, 14f), new Color(1f, 0.48f, 0.25f), 8f, 2.2f);
        CreatePointLight("Kitchen_WarmLight", new Vector3(0f, 2.6f, 23f), new Color(1f, 0.68f, 0.45f), 7f, 1.8f);
    }

    private static void ConfigureFacilityLighting()
    {
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.08f, 0.12f, 0.14f);
        CreatePointLight("Elevator_CoolLight", new Vector3(0f, 2.5f, 0f), new Color(0.45f, 0.78f, 1f), 5f, 2f);
        CreatePointLight("Corridor_CoolLight_01", new Vector3(0f, 2.5f, 8f), new Color(0.40f, 0.72f, 1f), 7f, 1.8f);
        CreatePointLight("Corridor_CoolLight_02", new Vector3(0f, 2.5f, 17f), new Color(0.40f, 0.72f, 1f), 7f, 1.8f);
        CreatePointLight("Briefing_NeutralLight", new Vector3(12f, 2.5f, 20f), new Color(0.82f, 0.90f, 1f), 10f, 2.4f);
    }

    private static void CreateSurfaceShotAnchors(Transform root)
    {
        CreateShotAnchor("SHOT_Car_TV_Closeup", root, new Vector3(-22f, 1.35f, -0.1f), new Vector3(-22f, 1.35f, 1.1f));
        CreateShotAnchor("SHOT_Car_PresidentPOV", root, new Vector3(-22f, 1.25f, -1.4f), new Vector3(-22f, 1.25f, 1.2f));
        CreateShotAnchor("SHOT_Pizza_Sign", root, new Vector3(0f, 2.7f, 0f), new Vector3(0f, 3.7f, 6f));
        CreateShotAnchor("SHOT_Restaurant_Crowd", root, new Vector3(0f, 1.7f, 8f), new Vector3(0f, 1.2f, 13f));
        CreateShotAnchor("SHOT_SecretDoor", root, new Vector3(0f, 1.7f, 27.5f), new Vector3(0f, 1.3f, 31.7f));
        CreateShotAnchor("SHOT_Elevator", root, new Vector3(0f, 1.7f, 31f), new Vector3(0f, 1.2f, 34f));
    }

    private static void CreateFacilityShotAnchors(Transform root)
    {
        CreateShotAnchor("SHOT_Elevator_Exit", root, new Vector3(0f, 1.7f, -0.5f), new Vector3(0f, 1.4f, 5f));
        CreateShotAnchor("SHOT_Corridor", root, new Vector3(0f, 1.7f, 8f), new Vector3(0f, 1.4f, 17f));
        CreateShotAnchor("SHOT_Briefing_Wide", root, new Vector3(7f, 2.2f, 20f), new Vector3(13f, 1.2f, 20f));
        CreateShotAnchor("SHOT_Projector_Closeup", root, new Vector3(12f, 1.7f, 18f), new Vector3(12f, 1.7f, 24.8f));
        CreateShotAnchor("SHOT_MeetingDoor", root, new Vector3(22f, 1.7f, 20f), new Vector3(26f, 1.4f, 20f));
    }

    private static void CreateFlowAssets()
    {
        FlowEntryDefinition surface = CreateOrUpdateFlowEntry(
            SurfaceEntryPath,
            "first-contact-intro-surface",
            SurfaceScenePath,
            "first-contact-intro-surface",
            FlowEntryType.InteractiveCutscene,
            0,
            true);
        FlowEntryDefinition facility = CreateOrUpdateFlowEntry(
            FacilityEntryPath,
            "first-contact-intro-facility",
            FacilityScenePath,
            "first-contact-intro-facility",
            FlowEntryType.InteractiveCutscene,
            0,
            true);
        FlowEntryDefinition translation = CreateOrUpdateFlowEntry(
            TranslationAfterIntroEntryPath,
            "first-contact-translation-after-intro",
            "Assets/Scenes/GameScene.unity",
            "first-contact-translation",
            FlowEntryType.Gameplay,
            1,
            false);

        GameFlowAsset fullFlow = AssetDatabase.LoadAssetAtPath<GameFlowAsset>(FullFlowPath);
        if (fullFlow == null)
        {
            fullFlow = ScriptableObject.CreateInstance<GameFlowAsset>();
            fullFlow.name = "FirstContactGameFlow";
            AssetDatabase.CreateAsset(fullFlow, FullFlowPath);
        }

        fullFlow.entries = new[] { surface, facility, translation };
        EditorUtility.SetDirty(fullFlow);
    }

    private static FlowEntryDefinition CreateOrUpdateFlowEntry(
        string path,
        string id,
        string scenePath,
        string tag,
        FlowEntryType entryType,
        int storyDay,
        bool startWithIntro)
    {
        FlowEntryDefinition entry = AssetDatabase.LoadAssetAtPath<FlowEntryDefinition>(path);
        if (entry == null)
        {
            entry = ScriptableObject.CreateInstance<FlowEntryDefinition>();
            entry.name = System.IO.Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(entry, path);
        }

        entry.entryId = id;
        entry.entryType = entryType;
        entry.storyDay = storyDay;
        entry.entryTag = tag;
        entry.sceneName = System.IO.Path.GetFileNameWithoutExtension(scenePath);
        entry.unloadPreviousScene = true;
        entry.autoStartSession = true;
        entry.startSessionWithIntro = startWithIntro;
        EditorUtility.SetDirty(entry);
        return entry;
    }

    private static void UpdateBuildSettings()
    {
        List<EditorBuildSettingsScene> scenes = EditorBuildSettings.scenes.ToList();
        AddBuildSceneIfMissing(scenes, SurfaceScenePath);
        AddBuildSceneIfMissing(scenes, FacilityScenePath);

        EditorBuildSettingsScene surface = scenes.First(scene => scene.path == SurfaceScenePath);
        EditorBuildSettingsScene facility = scenes.First(scene => scene.path == FacilityScenePath);
        scenes.Remove(surface);
        scenes.Remove(facility);

        int gameSceneIndex = scenes.FindIndex(scene => scene.path == "Assets/Scenes/GameScene.unity");
        int insertIndex = gameSceneIndex >= 0 ? gameSceneIndex : scenes.Count;
        scenes.Insert(insertIndex, surface);
        scenes.Insert(insertIndex + 1, facility);
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    private static void AddBuildSceneIfMissing(List<EditorBuildSettingsScene> scenes, string path)
    {
        if (scenes.Any(scene => scene.path == path))
        {
            return;
        }

        scenes.Add(new EditorBuildSettingsScene(path, true));
    }

    private static GameObject CreateCube(string name, Transform parent, Vector3 position, Vector3 scale, Material material)
    {
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = name;
        cube.transform.SetParent(parent, true);
        cube.transform.position = position;
        cube.transform.localScale = scale;
        cube.GetComponent<MeshRenderer>().sharedMaterial = material;
        return cube;
    }

    private static GameObject CreateCharacter(string name, Transform parent, Vector3 position, Material material)
    {
        GameObject character = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        character.name = name;
        character.transform.SetParent(parent, true);
        character.transform.position = position;
        character.transform.localScale = new Vector3(0.48f, 1f, 0.48f);
        character.GetComponent<MeshRenderer>().sharedMaterial = material;
        return character;
    }

    private static Transform CreateAnchor(string name, Transform parent, Vector3 position)
    {
        GameObject anchor = new(name);
        anchor.transform.SetParent(parent, true);
        anchor.transform.position = position;
        return anchor.transform;
    }

    private static void CreateShotAnchor(string name, Transform parent, Vector3 position, Vector3 lookAt)
    {
        Transform anchor = CreateAnchor(name, parent, position);
        anchor.LookAt(lookAt);
    }

    private static void CreateTrigger(string name, string id, Transform parent, Vector3 position, Vector3 size)
    {
        GameObject trigger = new(name);
        trigger.transform.SetParent(parent, true);
        trigger.transform.position = position;
        BoxCollider collider = trigger.AddComponent<BoxCollider>();
        collider.isTrigger = true;
        collider.size = size;
        FirstContactIntroTriggerMarker marker = trigger.AddComponent<FirstContactIntroTriggerMarker>();
        marker.Configure(id, new Color(1f, 0.65f, 0.1f, 0.28f));
    }

    private static FirstContactIntroPlayerController CreatePlayerRig(
        Transform spawn,
        Vector3 lookAt,
        FirstContactIntroHud hud)
    {
        GameObject rig = new("PlayerRig_Placeholder");
        rig.transform.position = spawn.position;
        CharacterController characterController = rig.AddComponent<CharacterController>();
        characterController.height = 1.8f;
        characterController.radius = 0.3f;
        characterController.center = new Vector3(0f, 0.9f, 0f);
        characterController.stepOffset = 0.3f;
        characterController.slopeLimit = 50f;

        GameObject cameraObject = new("Main Camera");
        cameraObject.tag = "MainCamera";
        cameraObject.transform.SetParent(rig.transform, false);
        cameraObject.transform.localPosition = new Vector3(0f, 1.65f, 0f);
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.nearClipPlane = 0.05f;
        camera.farClipPlane = 250f;
        camera.clearFlags = CameraClearFlags.Skybox;
        cameraObject.AddComponent<AudioListener>();

        Vector3 eyePosition = rig.transform.position + Vector3.up * 1.65f;
        Vector3 lookDirection = (lookAt - eyePosition).normalized;
        Quaternion lookRotation = Quaternion.LookRotation(lookDirection, Vector3.up);
        Vector3 lookEuler = lookRotation.eulerAngles;
        rig.transform.rotation = Quaternion.Euler(0f, lookEuler.y, 0f);
        cameraObject.transform.localRotation = Quaternion.Euler(lookEuler.x, 0f, 0f);

        FirstContactIntroPlayerController player = rig.AddComponent<FirstContactIntroPlayerController>();
        player.Configure(camera, hud);
        return player;
    }

    private static FirstContactIntroHud CreateIntroHud()
    {
        GameObject canvasObject = new(
            "IntroHUD",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster),
            typeof(FirstContactIntroHud));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 80;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        TextMeshProUGUI objective = CreateHudText(
            "Objective",
            canvasObject.transform,
            28f,
            TextAlignmentOptions.TopLeft);
        RectTransform objectiveRect = objective.rectTransform;
        objectiveRect.anchorMin = new Vector2(0f, 1f);
        objectiveRect.anchorMax = new Vector2(0f, 1f);
        objectiveRect.pivot = new Vector2(0f, 1f);
        objectiveRect.anchoredPosition = new Vector2(46f, -42f);
        objectiveRect.sizeDelta = new Vector2(900f, 80f);

        TextMeshProUGUI prompt = CreateHudText(
            "InteractionPrompt",
            canvasObject.transform,
            30f,
            TextAlignmentOptions.Center);
        RectTransform promptRect = prompt.rectTransform;
        promptRect.anchorMin = new Vector2(0.5f, 0f);
        promptRect.anchorMax = new Vector2(0.5f, 0f);
        promptRect.pivot = new Vector2(0.5f, 0f);
        promptRect.anchoredPosition = new Vector2(0f, 76f);
        promptRect.sizeDelta = new Vector2(900f, 70f);

        TextMeshProUGUI crosshair = CreateHudText(
            "Crosshair",
            canvasObject.transform,
            24f,
            TextAlignmentOptions.Center);
        crosshair.text = "+";
        crosshair.color = new Color(1f, 1f, 1f, 0.8f);
        RectTransform crosshairRect = crosshair.rectTransform;
        crosshairRect.anchorMin = new Vector2(0.5f, 0.5f);
        crosshairRect.anchorMax = new Vector2(0.5f, 0.5f);
        crosshairRect.pivot = new Vector2(0.5f, 0.5f);
        crosshairRect.anchoredPosition = Vector2.zero;
        crosshairRect.sizeDelta = new Vector2(32f, 32f);

        FirstContactIntroHud hud = canvasObject.GetComponent<FirstContactIntroHud>();
        hud.Configure(objective, prompt, crosshair);
        hud.ClearObjective();
        hud.ClearPrompt();
        return hud;
    }

    private static TextMeshProUGUI CreateHudText(
        string name,
        Transform parent,
        float fontSize,
        TextAlignmentOptions alignment)
    {
        GameObject textObject = new(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(parent, false);
        TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
        text.fontSize = fontSize;
        text.fontStyle = FontStyles.Bold;
        text.alignment = alignment;
        text.color = Color.white;
        text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        return text;
    }

    private static void CreatePointLight(string name, Vector3 position, Color color, float range, float intensity)
    {
        GameObject lightObject = new(name);
        lightObject.transform.position = position;
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        light.range = range;
        light.intensity = intensity;
    }

    private static void CreateWorldLabel(string text, Transform parent, Vector3 position, Quaternion rotation, float scale)
    {
        GameObject labelObject = new("Label_" + text.Replace(" ", string.Empty));
        labelObject.transform.SetParent(parent, true);
        labelObject.transform.position = position;
        labelObject.transform.rotation = rotation;
        labelObject.transform.localScale = Vector3.one * scale;
        TextMeshPro label = labelObject.AddComponent<TextMeshPro>();
        label.text = text;
        label.alignment = TextAlignmentOptions.Center;
        label.fontSize = 6f;
        label.color = Color.white;
        label.rectTransform.sizeDelta = new Vector2(12f, 2f);
    }

    private static void EnsureFolder(string parent, string child)
    {
        string path = parent + "/" + child;
        if (!AssetDatabase.IsValidFolder(path))
        {
            AssetDatabase.CreateFolder(parent, child);
        }
    }

    private static void RestoreActiveScene(Scene previousActiveScene)
    {
        if (previousActiveScene.IsValid() && previousActiveScene.isLoaded)
        {
            SceneManager.SetActiveScene(previousActiveScene);
        }
    }

    private sealed class GrayboxMaterials
    {
        public Material Exterior;
        public Material WarmWall;
        public Material WarmFloor;
        public Material FacilityWall;
        public Material FacilityFloor;
        public Material Prop;
        public Material Accent;
        public Material Route;
        public Material Character;
        public Material Crowd;
        public Material Doctor;
        public Material Screen;
    }
}
