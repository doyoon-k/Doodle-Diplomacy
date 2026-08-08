using DoodleDiplomacy.Gameplay.FirstContact;
using UnityEditor;
using UnityEngine;

namespace DoodleDiplomacy.Editor
{
    [CustomEditor(typeof(FirstContactBriefingSlideDeck))]
    [CanEditMultipleObjects]
    public sealed class FirstContactBriefingSlideDeckEditor : UnityEditor.Editor
    {
        private const float PreviewWidth = 180f;
        private const float PreviewHeight = 120f;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.HelpBox(
                "각 칸에 그림을 드래그하세요. 현재 임시 그림은 3:2 비율입니다. " +
                "같은 덱 안에서는 그림 비율을 통일하는 것이 좋습니다.",
                MessageType.Info);

            bool hasMissingSlide = false;
            float referenceAspect = 0f;
            foreach (FirstContactBriefingSlideId slideId in
                     System.Enum.GetValues(typeof(FirstContactBriefingSlideId)))
            {
                string fieldName = FirstContactBriefingSlideDeck.GetSerializedFieldName(slideId);
                SerializedProperty property = serializedObject.FindProperty(fieldName);
                if (property == null)
                {
                    continue;
                }

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField(
                    FirstContactBriefingSlideDeck.GetDisplayName(slideId),
                    EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(property, GUIContent.none);

                Texture2D texture = property.objectReferenceValue as Texture2D;
                hasMissingSlide |= texture == null;
                if (texture != null)
                {
                    float aspect = texture.height > 0
                        ? texture.width / (float)texture.height
                        : 0f;
                    if (referenceAspect <= 0f)
                    {
                        referenceAspect = aspect;
                    }

                    Rect previewRect = GUILayoutUtility.GetRect(
                        PreviewWidth,
                        PreviewHeight,
                        GUILayout.ExpandWidth(false));
                    EditorGUI.DrawPreviewTexture(
                        previewRect,
                        texture,
                        null,
                        ScaleMode.ScaleToFit);
                    EditorGUILayout.LabelField(
                        $"{texture.width} × {texture.height}",
                        EditorStyles.miniLabel);

                    if (referenceAspect > 0f && Mathf.Abs(referenceAspect - aspect) > 0.02f)
                    {
                        EditorGUILayout.HelpBox(
                            "다른 슬라이드와 화면 비율이 다릅니다.",
                            MessageType.Warning);
                    }
                }

                using (new EditorGUI.DisabledScope(
                           targets.Length != 1 || texture == null))
                {
                    if (GUILayout.Button("프로젝터에서 미리보기"))
                    {
                        PreviewOnOpenProjector(slideId);
                    }
                }

                EditorGUILayout.EndVertical();
            }

            serializedObject.ApplyModifiedProperties();

            if (hasMissingSlide)
            {
                EditorGUILayout.HelpBox(
                    "비어 있는 슬라이드가 있습니다. 실행 중 해당 화면은 꺼진 상태로 표시됩니다.",
                    MessageType.Warning);
            }

            using (new EditorGUI.DisabledScope(targets.Length != 1))
            {
                if (GUILayout.Button("프로젝터 끄기"))
                {
                    FindOpenProjector()?.PowerOff();
                    SceneView.RepaintAll();
                }
            }
        }

        private static void PreviewOnOpenProjector(FirstContactBriefingSlideId slideId)
        {
            FirstContactBriefingProjector projector = FindOpenProjector();
            if (projector == null)
            {
                Debug.LogWarning(
                    "[BriefingSlideDeck] 열린 장면에서 브리핑 프로젝터를 찾지 못했습니다.");
                return;
            }

            projector.ShowSlide(slideId);
            SceneView.RepaintAll();
        }

        private static FirstContactBriefingProjector FindOpenProjector()
        {
            return Object.FindFirstObjectByType<FirstContactBriefingProjector>(
                FindObjectsInactive.Include);
        }
    }
}
