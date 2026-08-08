using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DoodleDiplomacy.Camera
{
    [InitializeOnLoad]
    internal static class CameraAnchorSceneViewEditor
    {
        private const string AnchorRootName = "Cinematics_Anchors";
        private const string AnchorNamePrefix = "SHOT_";
        private const string PreviewMenuPath =
            "Tools/Camera Anchors/선택 앵커 시점으로 씬 뷰 보기";
        private const string ApplyMenuPath =
            "Tools/Camera Anchors/씬 뷰 포즈를 선택 앵커에 적용";

        private const float DefaultFieldOfView = 60f;
        private const float DefaultAspect = 16f / 9f;
        private const float FrustumPreviewDistance = 8f;

        static CameraAnchorSceneViewEditor()
        {
            SceneView.duringSceneGui += DrawSceneViewControls;
        }

        [MenuItem(PreviewMenuPath, false, 1800)]
        private static void PreviewSelectedAnchor()
        {
            if (TryGetSelectedAnchor(out Transform anchor))
            {
                PreviewAnchor(SceneView.lastActiveSceneView, anchor);
            }
        }

        [MenuItem(PreviewMenuPath, true)]
        private static bool ValidatePreviewSelectedAnchor()
        {
            return TryGetSelectedAnchor(out _);
        }

        [MenuItem(ApplyMenuPath, false, 1801)]
        private static void ApplySceneViewPoseToSelectedAnchor()
        {
            if (TryGetSelectedAnchor(out Transform anchor))
            {
                ApplySceneViewPose(SceneView.lastActiveSceneView, anchor);
            }
        }

        [MenuItem(ApplyMenuPath, true)]
        private static bool ValidateApplySceneViewPoseToSelectedAnchor()
        {
            return TryGetSelectedAnchor(out _);
        }

        private static void DrawSceneViewControls(SceneView sceneView)
        {
            if (sceneView == null || !sceneView.drawGizmos)
            {
                return;
            }

            TryGetSelectedAnchor(out Transform selectedAnchor);
            DrawAnchorGizmos(selectedAnchor);

            if (selectedAnchor == null)
            {
                return;
            }

            DrawSelectedAnchorFrustum(selectedAnchor);
            DrawSelectedAnchorControls(sceneView, selectedAnchor);
        }

        private static void DrawAnchorGizmos(Transform selectedAnchor)
        {
            for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
            {
                Scene scene = SceneManager.GetSceneAt(sceneIndex);
                if (!scene.isLoaded)
                {
                    continue;
                }

                GameObject[] rootObjects = scene.GetRootGameObjects();
                for (int rootIndex = 0; rootIndex < rootObjects.Length; rootIndex++)
                {
                    Transform root = rootObjects[rootIndex].transform;
                    if (!string.Equals(root.name, AnchorRootName, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    Transform[] anchors = root.GetComponentsInChildren<Transform>(true);
                    for (int anchorIndex = 0; anchorIndex < anchors.Length; anchorIndex++)
                    {
                        Transform anchor = anchors[anchorIndex];
                        if (!IsCameraAnchor(anchor))
                        {
                            continue;
                        }

                        DrawAnchorGizmo(anchor, anchor == selectedAnchor);
                    }
                }
            }
        }

        private static void DrawAnchorGizmo(Transform anchor, bool isSelected)
        {
            float handleSize = HandleUtility.GetHandleSize(anchor.position);
            float arrowSize = Mathf.Max(0.35f, handleSize * 0.18f);
            Color color = isSelected
                ? new Color(1f, 0.76f, 0.16f, 1f)
                : new Color(0.16f, 0.94f, 1f, 0.9f);

            using (new Handles.DrawingScope(color))
            {
                Handles.SphereHandleCap(
                    0,
                    anchor.position,
                    Quaternion.identity,
                    arrowSize * 0.12f,
                    EventType.Repaint);
                Handles.ArrowHandleCap(
                    0,
                    anchor.position,
                    anchor.rotation,
                    arrowSize,
                    EventType.Repaint);
                Handles.Label(
                    anchor.position + (Vector3.up * (arrowSize * 0.16f)),
                    anchor.name,
                    EditorStyles.miniBoldLabel);
            }
        }

        private static void DrawSelectedAnchorFrustum(Transform anchor)
        {
            GetFrustumProjection(out float fieldOfView, out float aspect);
            float halfHeight = Mathf.Tan(fieldOfView * Mathf.Deg2Rad * 0.5f) *
                FrustumPreviewDistance;
            float halfWidth = halfHeight * aspect;

            Vector3 topLeft = new(-halfWidth, halfHeight, FrustumPreviewDistance);
            Vector3 topRight = new(halfWidth, halfHeight, FrustumPreviewDistance);
            Vector3 bottomRight = new(halfWidth, -halfHeight, FrustumPreviewDistance);
            Vector3 bottomLeft = new(-halfWidth, -halfHeight, FrustumPreviewDistance);

            using (new Handles.DrawingScope(
                       new Color(1f, 0.76f, 0.16f, 0.9f),
                       Matrix4x4.TRS(anchor.position, anchor.rotation, Vector3.one)))
            {
                Handles.DrawLine(Vector3.zero, topLeft);
                Handles.DrawLine(Vector3.zero, topRight);
                Handles.DrawLine(Vector3.zero, bottomRight);
                Handles.DrawLine(Vector3.zero, bottomLeft);
                Handles.DrawLine(topLeft, topRight);
                Handles.DrawLine(topRight, bottomRight);
                Handles.DrawLine(bottomRight, bottomLeft);
                Handles.DrawLine(bottomLeft, topLeft);
            }
        }

        private static void DrawSelectedAnchorControls(SceneView sceneView, Transform anchor)
        {
            Handles.BeginGUI();
            GUILayout.BeginArea(
                new Rect(12f, 48f, 278f, 116f),
                "Camera Anchor",
                EditorStyles.helpBox);

            EditorGUILayout.LabelField(anchor.name, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Cyan: anchors  ·  Orange: selected frustum",
                EditorStyles.miniLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("앵커 시점으로 보기", GUILayout.Height(24f)))
                {
                    PreviewAnchor(sceneView, anchor);
                }

                if (GUILayout.Button("씬 뷰 포즈 적용", GUILayout.Height(24f)))
                {
                    ApplySceneViewPose(sceneView, anchor);
                }
            }

            EditorGUILayout.LabelField(
                "Scene View를 움직인 뒤 포즈 적용을 누르세요.",
                EditorStyles.miniLabel);
            GUILayout.EndArea();
            Handles.EndGUI();
        }

        private static void PreviewAnchor(SceneView sceneView, Transform anchor)
        {
            if (anchor == null)
            {
                return;
            }

            sceneView ??= EditorWindow.GetWindow<SceneView>();
            if (sceneView == null)
            {
                return;
            }

            sceneView.orthographic = false;
            float cameraDistance = Mathf.Max(0.1f, sceneView.cameraDistance);
            sceneView.LookAtDirect(
                anchor.position + (anchor.forward * cameraDistance),
                anchor.rotation);
            sceneView.Repaint();
        }

        private static void ApplySceneViewPose(SceneView sceneView, Transform anchor)
        {
            if (sceneView == null || sceneView.camera == null || anchor == null)
            {
                return;
            }

            Undo.RecordObject(anchor, "Apply Scene View Pose To Camera Anchor");
            anchor.SetPositionAndRotation(
                sceneView.camera.transform.position,
                sceneView.camera.transform.rotation);
            EditorUtility.SetDirty(anchor);

            if (anchor.gameObject.scene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(anchor.gameObject.scene);
            }

            SceneView.RepaintAll();
        }

        private static void GetFrustumProjection(out float fieldOfView, out float aspect)
        {
            UnityEngine.Camera referenceCamera = UnityEngine.Camera.main;
            fieldOfView = referenceCamera != null
                ? referenceCamera.fieldOfView
                : DefaultFieldOfView;
            aspect = referenceCamera != null && referenceCamera.aspect > 0.01f
                ? referenceCamera.aspect
                : DefaultAspect;
        }

        private static bool TryGetSelectedAnchor(out Transform anchor)
        {
            anchor = Selection.activeTransform;
            if (IsCameraAnchor(anchor))
            {
                return true;
            }

            anchor = null;
            return false;
        }

        private static bool IsCameraAnchor(Transform transform)
        {
            if (transform == null ||
                !transform.name.StartsWith(AnchorNamePrefix, StringComparison.Ordinal))
            {
                return false;
            }

            for (Transform current = transform.parent; current != null; current = current.parent)
            {
                if (string.Equals(current.name, AnchorRootName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
