using System;
using System.Collections.Generic;
using System.Linq;
using DoodleDiplomacy.Camera;
using DoodleDiplomacy.Devices;
using DoodleDiplomacy.Gameplay.FirstContact;
using DoodleDiplomacy.Narrative;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DoodleDiplomacy.EditorTools
{
    public static class FirstContactMeetingRoomSetupEditor
    {
        private const string FacilityScenePath =
            "Assets/Scenes/FirstContact/FC_Intro_Facility.unity";
        private const string RootName = "MeetingArrival";
        private const string IntegratedRoomRootName = "MeetingRoom_Integrated";
        private const string GameplaySystemsRootName = "MeetingGameplaySystems";
        private const string MeetingTableName = "Table";
        private const string DirectorPrefabPath = "Assets/Prefabs/Adjutant.prefab";
        private const string HwangMaterialPath =
            "Assets/Materials/FirstContact/Graybox/Graybox_Doctor.mat";
        private const string ObamaMaterialPath =
            "Assets/Materials/FirstContact/Graybox/Graybox_Character.mat";
        private const string PropMaterialPath =
            "Assets/Materials/FirstContact/Graybox/Graybox_Prop.mat";

        [MenuItem("Tools/First Contact/Setup Meeting Room Arrival", priority = 140)]
        public static void SetupFromMenu()
        {
            SetupAndSave(selectRoot: true);
        }

        public static bool SetupAndSave(bool selectRoot)
        {
            Scene scene = FindLoadedScene(FacilityScenePath);
            bool openedForSetup = !scene.IsValid();
            if (openedForSetup)
            {
                scene = EditorSceneManager.OpenScene(FacilityScenePath, OpenSceneMode.Additive);
            }

            Scene previousActiveScene = SceneManager.GetActiveScene();
            try
            {
                SceneManager.SetActiveScene(scene);
                Transform root = FindRoot(scene, RootName);
                bool createdRoot = root == null;
                if (createdRoot)
                {
                    root = new GameObject(RootName).transform;
                }

                CameraController cameraController = FindInScene<CameraController>(scene);
                CinemachineCamera seatedCamera = cameraController != null
                    ? cameraController.DefaultViewCamera
                    : FindNamedComponent<CinemachineCamera>(scene, "CM_Default");
                if (cameraController == null || seatedCamera == null)
                {
                    Debug.LogError(
                        "[MeetingArrivalSetup] Facility의 통합 CameraController/CM_Default를 찾지 못했습니다.");
                    return false;
                }

                if (!RepairImportedTableHierarchy(scene))
                {
                    return false;
                }

                BuildOrWire(scene, root, cameraController, seatedCamera);
                EditorSceneManager.MarkSceneDirty(scene);
                bool saved = EditorSceneManager.SaveScene(scene);
                if (!saved)
                {
                    Debug.LogError("[MeetingArrivalSetup] Facility 저장에 실패했습니다.");
                    return false;
                }

                if (selectRoot && !openedForSetup)
                {
                    Selection.activeTransform = root;
                    EditorGUIUtility.PingObject(root.gameObject);
                }

                Debug.Log(
                    createdRoot
                        ? "[MeetingArrivalSetup] Facility에 회담장 도착 연출 앵커를 저장했습니다."
                        : "[MeetingArrivalSetup] 기존 회담장 도착 연출 앵커 연결을 갱신했습니다.");
                return true;
            }
            finally
            {
                if (previousActiveScene.IsValid() && previousActiveScene.isLoaded)
                {
                    SceneManager.SetActiveScene(previousActiveScene);
                }

                if (openedForSetup && scene.IsValid() && scene.isLoaded)
                {
                    EditorSceneManager.CloseScene(scene, removeScene: true);
                }
            }
        }

        private static bool RepairImportedTableHierarchy(Scene scene)
        {
            Transform integratedRoom = FindRoot(scene, IntegratedRoomRootName);
            Transform gameplaySystems = FindRoot(scene, GameplaySystemsRootName);
            Transform table = FindNamedTransform(scene, MeetingTableName);
            if (integratedRoom == null || gameplaySystems == null || table == null)
            {
                Debug.LogError(
                    "[MeetingArrivalSetup] 통합 회담장 루트, 게임플레이 시스템 또는 원본 Table을 찾지 못했습니다.");
                return false;
            }

            if (table.parent == integratedRoom)
            {
                return true;
            }

            if (table.parent != gameplaySystems)
            {
                Debug.LogWarning(
                    $"[MeetingArrivalSetup] Table이 예상하지 않은 부모 '{table.parent?.name}' 아래에 있어 " +
                    "사용자가 조정한 배치를 보존했습니다.",
                    table);
                return true;
            }

            Vector3 localPosition = table.localPosition;
            Quaternion localRotation = table.localRotation;
            Vector3 localScale = table.localScale;

            Undo.SetTransformParent(
                table,
                integratedRoom,
                "Restore Meeting Room Table Hierarchy");
            Undo.RecordObject(table, "Restore Meeting Room Table Transform");
            table.localPosition = localPosition;
            table.localRotation = localRotation;
            table.localScale = localScale;
            EditorUtility.SetDirty(table);

            Debug.Log(
                "[MeetingArrivalSetup] 원본 Table을 MeetingRoom_Integrated 아래로 복원했습니다.",
                table);
            return true;
        }

        private static void BuildOrWire(
            Scene scene,
            Transform root,
            CameraController cameraController,
            CinemachineCamera seatedCamera)
        {
            Vector3 seatedPosition = seatedCamera.transform.position;
            Quaternion seatedRotation = seatedCamera.transform.rotation;
            Vector3 forward = Vector3.ProjectOnPlane(
                seatedCamera.transform.forward,
                Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.001f)
            {
                forward = Vector3.forward;
            }

            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            float floorY = 0f;

            Transform cameras = GetOrCreateChild(scene, root, "Cameras");
            Transform arrivalTransform = GetOrCreateChild(
                scene,
                cameras,
                "CM_MeetingArrival",
                out bool arrivalCreated);
            CinemachineCamera arrivalCamera =
                GetOrAddComponent<CinemachineCamera>(arrivalTransform.gameObject);
            GetOrAddComponent<CameraAnchorGizmo>(arrivalTransform.gameObject);
            if (arrivalCreated)
            {
                arrivalTransform.SetPositionAndRotation(
                    seatedPosition - forward * 1.4f + Vector3.up * 0.35f,
                    seatedRotation);
                arrivalCamera.Lens = seatedCamera.Lens;
            }
            arrivalCamera.enabled = false;

            Transform interaction = GetOrCreateChild(scene, root, "Interaction");
            Transform seatInteraction = GetOrCreateChild(
                scene,
                interaction,
                "INT_PresidentChair",
                out bool seatInteractionCreated);
            if (seatInteractionCreated)
            {
                seatInteraction.SetPositionAndRotation(
                    seatedPosition - forward * 0.45f - Vector3.up * 1.15f,
                    seatedRotation);
            }

            Transform actors = GetOrCreateChild(scene, root, "Actors");
            Transform hwang = GetOrCreatePlaceholder(
                scene,
                actors,
                "DoctorHwang_Meeting_Placeholder",
                HwangMaterialPath,
                seatedPosition - right * 2.55f + forward * 4.1f,
                floorY,
                forward);
            Transform obama = GetOrCreatePlaceholder(
                scene,
                actors,
                "Obama_Meeting_Placeholder",
                ObamaMaterialPath,
                seatedPosition + right * 1.25f + forward * 4.25f,
                floorY,
                forward);

            Transform director = FindDirector(scene);
            if (director == null)
            {
                Debug.LogError(
                    "[MeetingArrivalSetup] Facility 회담실의 실제 Adjutant 프리팹 인스턴스를 찾지 못했습니다.");
            }

            Transform stage = GetOrCreateChild(scene, root, "Stage");
            Transform obamaStart = GetOrCreatePose(
                scene,
                stage,
                "POSE_Obama_Start",
                seatedPosition + right * 5.1f + forward * 5f,
                floorY,
                -right);
            Transform obamaCoffee = GetOrCreatePose(
                scene,
                stage,
                "POSE_Obama_Coffee",
                seatedPosition + right * 1.25f + forward * 4.25f,
                floorY,
                -forward);
            Transform coffeeDrop = GetOrCreatePose(
                scene,
                stage,
                "CoffeeDropPoint",
                obamaCoffee.position - right * 0.35f - forward * 0.3f,
                1.05f,
                forward);

            Transform props = GetOrCreateChild(scene, root, "Props");
            Transform coffee = GetOrCreatePrimitive(
                scene,
                props,
                "Coffee_Cup_Placeholder",
                PrimitiveType.Cylinder,
                PropMaterialPath,
                out bool coffeeCreated);
            if (coffeeCreated)
            {
                coffee.SetPositionAndRotation(coffeeDrop.position, coffeeDrop.rotation);
                coffee.localScale = new Vector3(0.09f, 0.08f, 0.09f);
            }

            Transform lookTargetsRoot = GetOrCreateChild(scene, root, "LookTargets");
            FirstContactMeetingLookTarget hwangTarget = GetOrCreateActorLookTarget(
                scene,
                hwang,
                "LOOK_Hwang",
                MeetingLookTarget.Hwang,
                new Vector3(0f, 1.55f, 0f));
            FirstContactMeetingLookTarget obamaTarget = GetOrCreateActorLookTarget(
                scene,
                obama,
                "LOOK_Obama",
                MeetingLookTarget.Obama,
                new Vector3(0f, 1.55f, 0f));
            FirstContactMeetingLookTarget directorTarget =
                FindOrCreateDirectorLookTarget(scene, director);
            FirstContactMeetingLookTarget doorTarget = GetOrCreateStaticLookTarget(
                scene,
                lookTargetsRoot,
                "LOOK_Door",
                MeetingLookTarget.Door,
                obamaStart.position + Vector3.up * 1.45f);
            FirstContactMeetingLookTarget coffeeTarget = GetOrCreateActorLookTarget(
                scene,
                coffee,
                "LOOK_Coffee",
                MeetingLookTarget.Coffee,
                new Vector3(0f, 0.12f, 0f));

            TerminalDisplay terminal = FindInScene<TerminalDisplay>(scene);
            Vector3 terminalPosition = terminal != null
                ? terminal.transform.position
                : seatedPosition - right * 3.8f + forward * 4.5f + Vector3.up * 1.2f;
            FirstContactMeetingLookTarget terminalTarget = GetOrCreateStaticLookTarget(
                scene,
                lookTargetsRoot,
                "LOOK_Terminal",
                MeetingLookTarget.Terminal,
                terminalPosition);

            coffee.gameObject.SetActive(false);
            obama.gameObject.SetActive(false);

            FirstContactMeetingLookTarget[] targets =
            {
                obamaTarget,
                directorTarget,
                hwangTarget,
                doorTarget,
                coffeeTarget,
                terminalTarget
            };

            FirstContactMeetingArrivalController controller =
                GetOrAddComponent<FirstContactMeetingArrivalController>(root.gameObject);
            controller.Configure(
                cameraController,
                arrivalCamera,
                seatedCamera,
                hwang,
                director,
                obama,
                obamaStart,
                obamaCoffee,
                coffee,
                coffeeDrop,
                targets.Where(item => item != null).ToArray());

            EditorUtility.SetDirty(controller);
            EditorUtility.SetDirty(arrivalCamera);
            EditorUtility.SetDirty(root.gameObject);
        }

        private static Transform FindDirector(Scene scene)
        {
            FirstContactMeetingLookTarget markedDirector =
                FindAllInScene<FirstContactMeetingLookTarget>(scene)
                    .FirstOrDefault(item => item.Target == MeetingLookTarget.Director);
            if (markedDirector != null)
            {
                GameObject markedPrefabRoot =
                    PrefabUtility.GetNearestPrefabInstanceRoot(markedDirector.gameObject);
                return markedPrefabRoot != null
                    ? markedPrefabRoot.transform
                    : markedDirector.transform;
            }

            foreach (Transform candidate in FindAllInScene<Transform>(scene))
            {
                GameObject nearestRoot = PrefabUtility.GetNearestPrefabInstanceRoot(
                    candidate.gameObject);
                if (nearestRoot != candidate.gameObject)
                {
                    continue;
                }

                if (string.Equals(
                        PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(nearestRoot),
                        DirectorPrefabPath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return nearestRoot.transform;
                }
            }

            return FindNamedTransform(scene, "Adjutant");
        }

        private static FirstContactMeetingLookTarget FindOrCreateDirectorLookTarget(
            Scene scene,
            Transform director)
        {
            if (director == null)
            {
                return null;
            }

            FirstContactMeetingLookTarget existing = director
                .GetComponentsInChildren<FirstContactMeetingLookTarget>(true)
                .FirstOrDefault(item => item.Target == MeetingLookTarget.Director);
            if (existing != null)
            {
                return existing;
            }

            Transform look = FindNamedTransformInHierarchy(director, "LOOK_Director") ??
                             GetOrCreateChild(scene, director, "LOOK_Director");
            FirstContactMeetingLookTarget target =
                GetOrAddComponent<FirstContactMeetingLookTarget>(look.gameObject);
            target.Configure(MeetingLookTarget.Director);
            return target;
        }

        private static FirstContactMeetingLookTarget GetOrCreateActorLookTarget(
            Scene scene,
            Transform parent,
            string name,
            MeetingLookTarget targetType,
            Vector3 localPosition)
        {
            Transform look = GetOrCreateChild(scene, parent, name, out bool created);
            if (created)
            {
                look.localPosition = localPosition;
                look.localRotation = Quaternion.identity;
            }

            FirstContactMeetingLookTarget target =
                GetOrAddComponent<FirstContactMeetingLookTarget>(look.gameObject);
            target.Configure(targetType);
            return target;
        }

        private static FirstContactMeetingLookTarget GetOrCreateStaticLookTarget(
            Scene scene,
            Transform parent,
            string name,
            MeetingLookTarget targetType,
            Vector3 worldPosition)
        {
            Transform look = GetOrCreateChild(scene, parent, name, out bool created);
            if (created)
            {
                look.position = worldPosition;
            }

            FirstContactMeetingLookTarget target =
                GetOrAddComponent<FirstContactMeetingLookTarget>(look.gameObject);
            target.Configure(targetType);
            return target;
        }

        private static Transform GetOrCreatePlaceholder(
            Scene scene,
            Transform parent,
            string name,
            string materialPath,
            Vector3 horizontalPosition,
            float floorY,
            Vector3 faceDirection)
        {
            Transform actor = GetOrCreatePrimitive(
                scene,
                parent,
                name,
                PrimitiveType.Capsule,
                materialPath,
                out bool created);
            if (created)
            {
                actor.position = new Vector3(
                    horizontalPosition.x,
                    floorY + 0.85f,
                    horizontalPosition.z);
                actor.rotation = Quaternion.LookRotation(-faceDirection, Vector3.up);
                actor.localScale = new Vector3(0.45f, 0.85f, 0.45f);
            }

            return actor;
        }

        private static Transform GetOrCreatePose(
            Scene scene,
            Transform parent,
            string name,
            Vector3 horizontalPosition,
            float worldY,
            Vector3 faceDirection)
        {
            Transform pose = GetOrCreateChild(scene, parent, name, out bool created);
            if (created)
            {
                pose.position = new Vector3(horizontalPosition.x, worldY, horizontalPosition.z);
                Vector3 facing = Vector3.ProjectOnPlane(faceDirection, Vector3.up).normalized;
                pose.rotation = facing.sqrMagnitude > 0.001f
                    ? Quaternion.LookRotation(facing, Vector3.up)
                    : Quaternion.identity;
            }

            return pose;
        }

        private static Transform GetOrCreatePrimitive(
            Scene scene,
            Transform parent,
            string name,
            PrimitiveType primitiveType,
            string materialPath,
            out bool created)
        {
            Transform existing = parent.Find(name);
            if (existing != null)
            {
                created = false;
                return existing;
            }

            GameObject instance = GameObject.CreatePrimitive(primitiveType);
            instance.name = name;
            instance.transform.SetParent(parent, worldPositionStays: false);
            Collider collider = instance.GetComponent<Collider>();
            if (collider != null)
            {
                UnityEngine.Object.DestroyImmediate(collider);
            }

            Renderer renderer = instance.GetComponent<Renderer>();
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (renderer != null && material != null)
            {
                renderer.sharedMaterial = material;
            }

            created = true;
            return instance.transform;
        }

        private static Transform GetOrCreateChild(
            Scene scene,
            Transform parent,
            string name)
        {
            return GetOrCreateChild(scene, parent, name, out _);
        }

        private static Transform GetOrCreateChild(
            Scene scene,
            Transform parent,
            string name,
            out bool created)
        {
            Transform child = parent.Find(name);
            if (child != null)
            {
                created = false;
                return child;
            }

            child = new GameObject(name).transform;
            child.SetParent(parent, worldPositionStays: false);
            created = true;
            return child;
        }

        private static T GetOrAddComponent<T>(GameObject gameObject)
            where T : Component
        {
            T component = gameObject.GetComponent<T>();
            return component != null ? component : gameObject.AddComponent<T>();
        }

        private static Scene FindLoadedScene(string path)
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (string.Equals(scene.path, path, StringComparison.OrdinalIgnoreCase))
                {
                    return scene;
                }
            }

            return default;
        }

        private static Transform FindRoot(Scene scene, string name)
        {
            return FindNamedTransform(scene, name);
        }

        private static T FindInScene<T>(Scene scene) where T : Component
        {
            return FindAllInScene<T>(scene).FirstOrDefault();
        }

        private static IEnumerable<T> FindAllInScene<T>(Scene scene) where T : Component
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (T component in root.GetComponentsInChildren<T>(true))
                {
                    yield return component;
                }
            }
        }

        private static T FindNamedComponent<T>(Scene scene, string name)
            where T : Component
        {
            return FindAllInScene<T>(scene)
                .FirstOrDefault(item => item.name == name);
        }

        private static Transform FindNamedTransform(Scene scene, string name)
        {
            return FindAllInScene<Transform>(scene)
                .FirstOrDefault(item => item.name == name);
        }

        private static Transform FindNamedTransformInHierarchy(
            Transform root,
            string name)
        {
            return root.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(item => item.name == name);
        }
    }

}
