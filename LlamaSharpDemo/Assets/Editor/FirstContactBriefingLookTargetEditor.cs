using DoodleDiplomacy.Gameplay.FirstContact;
using UnityEditor;
using UnityEngine;

namespace DoodleDiplomacy.Editor
{
    [CustomEditor(typeof(FirstContactBriefingLookTarget))]
    public sealed class FirstContactBriefingLookTargetEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.HelpBox(
                "이 오브젝트는 카메라가 이동할 위치가 아니라 대통령이 바라볼 지점입니다.",
                MessageType.Info);

            if (GUILayout.Button("대통령 좌석 시점에서 보기"))
            {
                PreviewTarget((FirstContactBriefingLookTarget)target);
            }
        }

        private static void PreviewTarget(FirstContactBriefingLookTarget lookTarget)
        {
            FirstContactBriefingPresentation presentation = FindPresentation();
            Transform origin = presentation?.SeatedViewPreview;
            if (origin == null || lookTarget == null)
            {
                Debug.LogWarning(
                    "[BriefingLookTarget] 대통령 좌석 미리보기 원점을 찾지 못했습니다.");
                return;
            }

            Vector3 direction = lookTarget.transform.position - origin.position;
            if (direction.sqrMagnitude < 0.0001f)
            {
                return;
            }

            SceneView sceneView = SceneView.lastActiveSceneView ??
                                  EditorWindow.GetWindow<SceneView>();
            Quaternion rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            float distance = Mathf.Max(0.1f, sceneView.cameraDistance);
            sceneView.orthographic = false;
            sceneView.LookAtDirect(
                origin.position + direction.normalized * distance,
                rotation);
            sceneView.Repaint();
        }

        private static FirstContactBriefingPresentation FindPresentation()
        {
            return Object.FindFirstObjectByType<FirstContactBriefingPresentation>(
                FindObjectsInactive.Include);
        }
    }

    [InitializeOnLoad]
    internal static class FirstContactBriefingLookTargetSceneOverlay
    {
        static FirstContactBriefingLookTargetSceneOverlay()
        {
            SceneView.duringSceneGui += DrawLookTargets;
        }

        private static void DrawLookTargets(SceneView sceneView)
        {
            FirstContactBriefingPresentation presentation =
                Object.FindFirstObjectByType<FirstContactBriefingPresentation>(
                    FindObjectsInactive.Include);
            Transform origin = presentation?.SeatedViewPreview;

            foreach (FirstContactBriefingLookTarget lookTarget in
                     Resources.FindObjectsOfTypeAll<FirstContactBriefingLookTarget>())
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
