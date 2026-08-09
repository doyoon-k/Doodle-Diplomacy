using DoodleDiplomacy.Gameplay.FirstContact;
using UnityEditor;
using UnityEngine;

namespace DoodleDiplomacy.Editor
{
    [CustomEditor(typeof(FirstContactMeetingLookTarget))]
    public sealed class FirstContactMeetingLookTargetEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.HelpBox(
                "이 오브젝트는 CM_Default의 위치를 옮기지 않고 대통령의 시선만 돌리는 목표입니다.",
                MessageType.Info);

            if (GUILayout.Button("기존 게임플레이 좌석 시점에서 보기"))
            {
                PreviewTarget((FirstContactMeetingLookTarget)target);
            }
        }

        private static void PreviewTarget(FirstContactMeetingLookTarget lookTarget)
        {
            FirstContactMeetingArrivalController controller = FindController();
            Transform origin = controller?.SeatedViewPreview;
            if (origin == null || lookTarget == null)
            {
                Debug.LogWarning(
                    "[MeetingLookTarget] Facility 회담실의 CM_Default 기준점을 찾지 못했습니다.");
                return;
            }

            Vector3 direction = lookTarget.transform.position - origin.position;
            if (direction.sqrMagnitude < 0.0001f)
            {
                return;
            }

            SceneView sceneView = SceneView.lastActiveSceneView ??
                                  EditorWindow.GetWindow<SceneView>();
            sceneView.orthographic = false;
            sceneView.LookAtDirect(
                origin.position + direction.normalized *
                Mathf.Max(0.1f, sceneView.cameraDistance),
                Quaternion.LookRotation(direction.normalized, Vector3.up));
            sceneView.Repaint();
        }

        private static FirstContactMeetingArrivalController FindController()
        {
            return Object.FindFirstObjectByType<FirstContactMeetingArrivalController>(
                FindObjectsInactive.Include);
        }
    }

    [InitializeOnLoad]
    internal static class FirstContactMeetingLookTargetSceneOverlay
    {
        static FirstContactMeetingLookTargetSceneOverlay()
        {
            SceneView.duringSceneGui += DrawLookTargets;
        }

        private static void DrawLookTargets(SceneView sceneView)
        {
            FirstContactMeetingArrivalController controller =
                Object.FindFirstObjectByType<FirstContactMeetingArrivalController>(
                    FindObjectsInactive.Include);
            Transform origin = controller?.SeatedViewPreview;

            foreach (FirstContactMeetingLookTarget lookTarget in
                     Resources.FindObjectsOfTypeAll<FirstContactMeetingLookTarget>())
            {
                if (lookTarget == null || !lookTarget.gameObject.scene.IsValid())
                {
                    continue;
                }

                Vector3 position = lookTarget.transform.position;
                if (!TryGetHandleSize(sceneView, position, out float size))
                {
                    continue;
                }

                Handles.color = lookTarget.SceneViewColor;

                if (Handles.Button(
                        position,
                        Quaternion.identity,
                        size,
                        size,
                        Handles.SphereHandleCap))
                {
                    Selection.activeGameObject = lookTarget.gameObject;
                }

                if (origin != null &&
                    IsDrawableFromSceneView(sceneView, origin.position))
                {
                    Handles.DrawDottedLine(origin.position, position, 5f);
                }

                Handles.Label(
                    position + Vector3.up * size * 1.5f,
                    lookTarget.name,
                    EditorStyles.miniBoldLabel);
            }
        }

        private static bool TryGetHandleSize(
            SceneView sceneView,
            Vector3 position,
            out float size)
        {
            size = 0f;
            if (!IsDrawableFromSceneView(sceneView, position))
            {
                return false;
            }

            float handleSize = HandleUtility.GetHandleSize(position);
            if (!float.IsFinite(handleSize))
            {
                return false;
            }

            size = Mathf.Max(0.035f, handleSize * 0.08f);
            return true;
        }

        private static bool IsDrawableFromSceneView(
            SceneView sceneView,
            Vector3 position)
        {
            UnityEngine.Camera camera = sceneView != null
                ? sceneView.camera
                : null;
            if (camera == null ||
                !float.IsFinite(position.x) ||
                !float.IsFinite(position.y) ||
                !float.IsFinite(position.z))
            {
                return false;
            }

            Vector3 cameraPosition = camera.transform.position;
            Vector3 cameraForward = camera.transform.forward;
            if (!float.IsFinite(cameraPosition.x) ||
                !float.IsFinite(cameraPosition.y) ||
                !float.IsFinite(cameraPosition.z) ||
                !float.IsFinite(cameraForward.x) ||
                !float.IsFinite(cameraForward.y) ||
                !float.IsFinite(cameraForward.z))
            {
                return false;
            }

            float depth = Vector3.Dot(
                cameraForward,
                position - cameraPosition);
            return float.IsFinite(depth) &&
                   depth > Mathf.Max(0.001f, camera.nearClipPlane) &&
                   depth < camera.farClipPlane;
        }
    }
}
