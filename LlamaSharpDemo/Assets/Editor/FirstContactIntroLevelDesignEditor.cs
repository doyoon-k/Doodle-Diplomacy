using System;
using System.Collections.Generic;
using System.Linq;
using DoodleDiplomacy.Gameplay.FirstContact;
using DoodleDiplomacy.Narrative;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEditor.SceneManagement;
using UnityEngine;

[CustomEditor(typeof(FirstContactIntroNarrativeZone))]
public sealed class FirstContactIntroNarrativeZoneEditor : Editor
{
    private readonly BoxBoundsHandle _boundsHandle = new();
    private SerializedProperty _displayName;
    private SerializedProperty _stage;
    private SerializedProperty _sequenceOrder;
    private SerializedProperty _requiredActors;
    private SerializedProperty _enterDelaySeconds;
    private SerializedProperty _dialogueEvent;
    private SerializedProperty _followupDialogueEvent;
    private SerializedProperty _guideHoldPoint;
    private SerializedProperty _oneShot;
    private SerializedProperty _gizmoColor;
    private bool _showAdvanced;

    private static string[] _eventIds;
    private static string[] _eventLabels;

    private void OnEnable()
    {
        _displayName = serializedObject.FindProperty("displayName");
        _stage = serializedObject.FindProperty("stage");
        _sequenceOrder = serializedObject.FindProperty("sequenceOrder");
        _requiredActors = serializedObject.FindProperty("requiredActors");
        _enterDelaySeconds = serializedObject.FindProperty("enterDelaySeconds");
        _dialogueEvent = serializedObject.FindProperty("dialogueEvent");
        _followupDialogueEvent = serializedObject.FindProperty("followupDialogueEvent");
        _guideHoldPoint = serializedObject.FindProperty("guideHoldPoint");
        _oneShot = serializedObject.FindProperty("oneShot");
        _gizmoColor = serializedObject.FindProperty("gizmoColor");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.Space(3f);
        EditorGUILayout.LabelField("인트로 내러티브 영역", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "씬 뷰의 박스가 실제 발동 영역입니다. 오브젝트를 이동하거나 박스 핸들을 조절하면 런타임 조건도 그대로 바뀝니다.",
            MessageType.Info);

        EditorGUILayout.PropertyField(_displayName, new GUIContent("표시 이름"));
        EditorGUILayout.PropertyField(_stage, new GUIContent("구간 역할"));
        EditorGUILayout.PropertyField(_sequenceOrder, new GUIContent("진행 순서"));
        EditorGUILayout.PropertyField(_requiredActors, new GUIContent("영역에 필요한 인물"));
        EditorGUILayout.PropertyField(_guideHoldPoint, new GUIContent("국장 정지 목표"));
        EditorGUILayout.PropertyField(_enterDelaySeconds, new GUIContent("진입 후 지연(초)"));

        DrawNarrativeEventPopup(_dialogueEvent, "재생할 대사");
        if ((FirstContactIntroNarrativeStage)_stage.enumValueIndex ==
            FirstContactIntroNarrativeStage.SecretDoorReveal)
        {
            DrawNarrativeEventPopup(_followupDialogueEvent, "연출 후 대사");
        }

        _showAdvanced = EditorGUILayout.Foldout(
            _showAdvanced,
            "고급 설정",
            true);
        if (_showAdvanced)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(_oneShot, new GUIContent("한 번만 발동"));
            EditorGUILayout.PropertyField(_gizmoColor, new GUIContent("씬 표시 색상"));
            EditorGUI.indentLevel--;
        }

        serializedObject.ApplyModifiedProperties();

        FirstContactIntroNarrativeZone zone =
            (FirstContactIntroNarrativeZone)target;
        if (Application.isPlaying)
        {
            string status = zone.HasTriggered
                ? "완료"
                : zone.IsArmed
                    ? zone.IsConditionMet ? "발동 조건 충족" : "대기 중"
                    : "아직 활성화되지 않음";
            EditorGUILayout.Space(4f);
            EditorGUILayout.HelpBox($"런타임 상태: {status}", MessageType.None);
        }

        EditorGUILayout.Space(4f);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("씬 뷰에서 보기"))
            {
                Selection.activeObject = zone.gameObject;
                SceneView.lastActiveSceneView?.FrameSelected();
            }

            if (GUILayout.Button("대사 목록 새로고침"))
            {
                _eventIds = null;
                _eventLabels = null;
            }
        }
    }

    private void OnSceneGUI()
    {
        FirstContactIntroNarrativeZone zone =
            (FirstContactIntroNarrativeZone)target;
        BoxCollider box = zone.GetComponent<BoxCollider>();
        if (box == null)
        {
            return;
        }

        Color color = zone.GizmoColor;
        color.a = 1f;
        _boundsHandle.SetColor(color);
        _boundsHandle.center = box.center;
        _boundsHandle.size = box.size;

        using (new Handles.DrawingScope(zone.transform.localToWorldMatrix))
        {
            EditorGUI.BeginChangeCheck();
            _boundsHandle.DrawHandle();
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(box, "Resize Narrative Zone");
                box.center = _boundsHandle.center;
                box.size = _boundsHandle.size;
                EditorUtility.SetDirty(box);
            }
        }

        DrawZoneLabel(zone, box);
    }

    private static void DrawZoneLabel(
        FirstContactIntroNarrativeZone zone,
        BoxCollider box)
    {
        Vector3 top = zone.transform.TransformPoint(
            box.center + Vector3.up * (box.size.y * 0.5f + 0.35f));
        string actorText = zone.RequiredActors switch
        {
            FirstContactIntroZoneActors.Player => "PLAYER",
            FirstContactIntroZoneActors.Director => "DIRECTOR",
            FirstContactIntroZoneActors.PlayerAndDirector => "PLAYER + DIRECTOR",
            _ => "UNCONFIGURED"
        };
        string label = $"{zone.SequenceOrder:00}  {zone.DisplayName}\n{actorText}";
        GUIStyle style = new(EditorStyles.helpBox)
        {
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white }
        };
        Handles.Label(top, label, style);
    }

    private static void DrawNarrativeEventPopup(
        SerializedProperty property,
        string label)
    {
        EnsureNarrativeEventCache();
        string current = property.stringValue ?? string.Empty;
        int found = Array.IndexOf(_eventIds, current);
        bool custom = found < 0 && !string.IsNullOrWhiteSpace(current);

        var ids = new List<string>(_eventIds);
        var labels = new List<string>(_eventLabels);
        if (custom)
        {
            ids.Insert(0, current);
            labels.Insert(0, $"현재 값: {current}");
            found = 0;
        }
        else
        {
            found = Mathf.Max(0, found);
        }

        int next = EditorGUILayout.Popup(label, found, labels.ToArray());
        if (next >= 0 && next < ids.Count)
        {
            property.stringValue = ids[next];
        }
    }

    private static void EnsureNarrativeEventCache()
    {
        if (_eventIds != null && _eventLabels != null)
        {
            return;
        }

        var previews = new SortedDictionary<string, string>(
            StringComparer.OrdinalIgnoreCase)
        {
            [string.Empty] = "(대사 없음)"
        };
        string[] guids = AssetDatabase.FindAssets("t:NarrativeScenarioAsset");
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            NarrativeScenarioAsset scenario =
                AssetDatabase.LoadAssetAtPath<NarrativeScenarioAsset>(path);
            if (scenario == null)
            {
                continue;
            }

            foreach (NarrativeBeat beat in scenario.Beats)
            {
                if (beat == null || string.IsNullOrWhiteSpace(beat.triggerEvent))
                {
                    continue;
                }

                if (!previews.ContainsKey(beat.triggerEvent))
                {
                    string text = beat.ResolveText() ?? string.Empty;
                    text = text.Replace('\n', ' ').Replace('\r', ' ').Trim();
                    if (text.Length > 54)
                    {
                        text = text.Substring(0, 54) + "…";
                    }

                    string speaker = beat.ResolveSpeaker();
                    previews[beat.triggerEvent] = string.IsNullOrWhiteSpace(text)
                        ? beat.triggerEvent
                        : $"{beat.triggerEvent}  |  {speaker}: {text}";
                }
            }
        }

        _eventIds = previews.Keys.ToArray();
        _eventLabels = previews.Values.ToArray();
    }
}

[CustomEditor(typeof(FirstContactIntroGuidePoint))]
public sealed class FirstContactIntroGuidePointEditor : Editor
{
    public override void OnInspectorGUI()
    {
        EditorGUILayout.LabelField("국장 이동 목표", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "이 Transform이 국장의 실제 목표 위치와 방향입니다. 빨간 포인트에서는 국장이 대사가 끝날 때까지 기다립니다.",
            MessageType.Info);
        DrawDefaultInspector();

        FirstContactIntroGuidePoint point =
            (FirstContactIntroGuidePoint)target;
        if (FirstContactIntroRouteEditing.TryFindPrimaryUsage(
                point,
                out FirstContactIntroGuideController guide,
                out int routeIndex))
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField(
                $"현재 경로 순서: {routeIndex + 1} / {guide.PathPoints.Count}",
                EditorStyles.miniBoldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("앞에 지점 추가"))
                {
                    FirstContactIntroRouteEditing.InsertRelative(
                        point,
                        insertBefore: true);
                    GUIUtility.ExitGUI();
                }

                if (GUILayout.Button("뒤에 지점 추가"))
                {
                    FirstContactIntroRouteEditing.InsertRelative(
                        point,
                        insertBefore: false);
                    GUIUtility.ExitGUI();
                }
            }

            if (GUILayout.Button("경로에서 제거", GUILayout.Height(24f)))
            {
                FirstContactIntroRouteEditing.Remove(point);
                GUIUtility.ExitGUI();
            }
        }
        else
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.HelpBox(
                "현재 국장 Path Points 배열에서 이 목표를 찾을 수 없습니다.",
                MessageType.Warning);
        }
    }

    [DrawGizmo(GizmoType.Active | GizmoType.InSelectionHierarchy |
               GizmoType.NonSelected | GizmoType.Pickable | GizmoType.Selected)]
    private static void DrawGuidePointLabel(
        FirstContactIntroGuidePoint point,
        GizmoType gizmoType)
    {
        Color color = point.PauseOnArrival
            ? new Color(1f, 0.42f, 0.18f, 1f)
            : point.RouteColor;
        string suffix = point.PauseOnArrival ? "  [WAIT]" : string.Empty;
        GUIStyle style = new(EditorStyles.miniBoldLabel)
        {
            normal = { textColor = color }
        };
        Vector3 labelPosition =
            point.transform.position + Vector3.up * 1.2f;
        if (!FirstContactSceneViewHandleSafety.IsDrawable(labelPosition))
        {
            return;
        }

        Handles.Label(
            labelPosition,
            $"{point.RouteOrder:00}  {point.DisplayName}{suffix}",
            style);
    }
}

[CustomEditor(typeof(FirstContactIntroGuideController))]
public sealed class FirstContactIntroGuideControllerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        EditorGUILayout.LabelField("국장 이동 경로", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "파란 선은 이동 경로, 빨간 포인트는 내러티브 대기 지점입니다. 목표 Transform을 옮기면 실제 이동 경로가 바로 바뀝니다.",
            MessageType.Info);
        DrawDefaultInspector();

        FirstContactIntroGuideController guide =
            (FirstContactIntroGuideController)target;
        EditorGUILayout.Space(6f);
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(
                       guide.PathPoints == null || guide.PathPoints.Count == 0))
            {
                if (GUILayout.Button("경로 끝에 지점 추가"))
                {
                    FirstContactIntroRouteEditing.AddAtEnd(guide);
                    GUIUtility.ExitGUI();
                }
            }

            if (GUILayout.Button("표시 번호 자동 정리"))
            {
                FirstContactIntroRouteEditing.Renumber(guide);
            }
        }
    }

    [DrawGizmo(GizmoType.Active | GizmoType.InSelectionHierarchy |
               GizmoType.NonSelected | GizmoType.Selected)]
    private static void DrawGuidePath(
        FirstContactIntroGuideController guide,
        GizmoType gizmoType)
    {
        IReadOnlyList<Transform> points = guide.PathPoints;
        if (points == null || points.Count < 2)
        {
            return;
        }

        Color previous = Handles.color;
        bool selected = (gizmoType & GizmoType.Selected) != 0 ||
                        (gizmoType & GizmoType.InSelectionHierarchy) != 0;
        Handles.color = selected
            ? new Color(0.15f, 0.85f, 1f, 0.95f)
            : new Color(0.15f, 0.75f, 1f, 0.4f);
        for (int i = 0; i < points.Count - 1; i++)
        {
            if (points[i] != null && points[i + 1] != null)
            {
                Vector3 start = points[i].position;
                Vector3 end = points[i + 1].position;
                if (!FirstContactSceneViewHandleSafety.IsDrawable(start) ||
                    !FirstContactSceneViewHandleSafety.IsDrawable(end))
                {
                    continue;
                }

                Handles.DrawAAPolyLine(
                    selected ? 4f : 2f,
                    start,
                    end);
            }
        }

        Handles.color = previous;
    }
}

internal static class FirstContactSceneViewHandleSafety
{
    public static bool IsDrawable(Vector3 position)
    {
        SceneView sceneView = SceneView.currentDrawingSceneView ??
                              SceneView.lastActiveSceneView;
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

internal static class FirstContactIntroRouteEditing
{
    internal readonly struct RouteUsage
    {
        public RouteUsage(
            FirstContactIntroGuideController guide,
            int index)
        {
            Guide = guide;
            Index = index;
        }

        public FirstContactIntroGuideController Guide { get; }
        public int Index { get; }
    }

    public static bool TryFindPrimaryUsage(
        FirstContactIntroGuidePoint point,
        out FirstContactIntroGuideController guide,
        out int routeIndex)
    {
        List<RouteUsage> usages = FindUsages(point);
        if (usages.Count > 0)
        {
            guide = usages[0].Guide;
            routeIndex = usages[0].Index;
            return true;
        }

        guide = null;
        routeIndex = -1;
        return false;
    }

    public static FirstContactIntroGuidePoint InsertRelative(
        FirstContactIntroGuidePoint current,
        bool insertBefore)
    {
        if (current == null)
        {
            return null;
        }

        List<RouteUsage> usages = FindUsages(current);
        if (usages.Count == 0)
        {
            EditorUtility.DisplayDialog(
                "경로 지점 추가",
                "이 목표를 사용하는 국장 경로를 찾을 수 없습니다.",
                "확인");
            return null;
        }

        RouteUsage primary = usages[0];
        int insertionIndex = primary.Index + (insertBefore ? 0 : 1);
        CalculateInsertedPose(
            primary.Guide,
            primary.Index,
            insertBefore,
            out Vector3 position,
            out Quaternion rotation);

        Transform parent = current.transform.parent;
        string requestedName = $"Route_{insertionIndex:00}_New";
        string uniqueName = GameObjectUtility.GetUniqueNameForSibling(
            parent,
            requestedName);
        GameObject created = new(uniqueName);
        Undo.RegisterCreatedObjectUndo(created, "Add director route point");
        created.transform.SetParent(parent, true);
        created.transform.SetPositionAndRotation(position, rotation);
        FirstContactIntroGuidePoint createdPoint =
            Undo.AddComponent<FirstContactIntroGuidePoint>(created);
        createdPoint.Configure("New route target", insertionIndex, pause: false);
        EditorUtility.SetDirty(createdPoint);

        foreach (RouteUsage usage in usages)
        {
            int index = usage.Index + (insertBefore ? 0 : 1);
            InsertPathReference(usage.Guide, index, created.transform);
            Renumber(usage.Guide);
        }

        MarkSceneDirty(created);
        Selection.activeGameObject = created;
        SceneView.lastActiveSceneView?.FrameSelected();
        return createdPoint;
    }

    public static FirstContactIntroGuidePoint AddAtEnd(
        FirstContactIntroGuideController guide)
    {
        if (guide == null || guide.PathPoints == null ||
            guide.PathPoints.Count == 0)
        {
            return null;
        }

        Transform last = guide.PathPoints[guide.PathPoints.Count - 1];
        FirstContactIntroGuidePoint point = last != null
            ? last.GetComponent<FirstContactIntroGuidePoint>()
            : null;
        if (point == null)
        {
            EditorUtility.DisplayDialog(
                "경로 끝에 추가",
                "마지막 경로 목표에 Guide Point 컴포넌트가 없습니다.",
                "확인");
            return null;
        }

        return InsertRelative(point, insertBefore: false);
    }

    public static bool Remove(FirstContactIntroGuidePoint point)
    {
        if (point == null)
        {
            return false;
        }

        List<RouteUsage> usages = FindUsages(point);
        if (usages.Count == 0)
        {
            EditorUtility.DisplayDialog(
                "경로에서 제거",
                "이 목표를 사용하는 국장 경로를 찾을 수 없습니다.",
                "확인");
            return false;
        }

        RouteUsage primary = usages[0];
        FirstContactIntroGuidePoint replacement = FindNeighborPoint(
            primary.Guide,
            primary.Index);
        FirstContactIntroNarrativeZone[] referencingZones =
            FindReferencingZones(point);
        if (referencingZones.Length > 0)
        {
            string names = string.Join(
                ", ",
                referencingZones.Select(zone => zone.DisplayName));
            int choice = EditorUtility.DisplayDialogComplex(
                "대사 구역이 사용 중인 목표",
                $"{names} 구역이 이 지점을 국장 정지 목표로 사용하고 있습니다. 어떻게 처리할까요?",
                replacement != null
                    ? $"{replacement.DisplayName}(으)로 대체"
                    : "참조 해제 후 제거",
                "취소",
                "참조 해제 후 제거");
            if (choice == 1)
            {
                return false;
            }

            bool useReplacement = choice == 0 && replacement != null;
            foreach (FirstContactIntroNarrativeZone zone in referencingZones)
            {
                SetZoneHoldPoint(zone, useReplacement ? replacement : null);
            }

            if (useReplacement)
            {
                Undo.RecordObject(replacement, "Promote replacement guide hold");
                replacement.Configure(
                    replacement.DisplayName,
                    replacement.RouteOrder,
                    pause: true);
                EditorUtility.SetDirty(replacement);
            }
        }
        else if (!EditorUtility.DisplayDialog(
                     "경로에서 제거",
                     $"{point.DisplayName} 지점을 경로와 씬에서 제거할까요?",
                     "제거",
                     "취소"))
        {
            return false;
        }

        foreach (RouteUsage usage in usages.OrderByDescending(item => item.Index))
        {
            RemovePathReference(usage.Guide, usage.Index);
            Renumber(usage.Guide);
        }

        GameObject pointObject = point.gameObject;
        GameObject selectionFallback = replacement != null
            ? replacement.gameObject
            : primary.Guide.gameObject;
        MarkSceneDirty(pointObject);
        Undo.DestroyObjectImmediate(pointObject);
        Selection.activeGameObject = selectionFallback;
        return true;
    }

    public static void Renumber(FirstContactIntroGuideController guide)
    {
        if (guide == null || guide.PathPoints == null)
        {
            return;
        }

        for (int i = 0; i < guide.PathPoints.Count; i++)
        {
            Transform target = guide.PathPoints[i];
            FirstContactIntroGuidePoint point = target != null
                ? target.GetComponent<FirstContactIntroGuidePoint>()
                : null;
            if (point == null)
            {
                continue;
            }

            SerializedObject pointData = new(point);
            SerializedProperty routeOrder =
                pointData.FindProperty("routeOrder");
            if (routeOrder != null && routeOrder.intValue != i)
            {
                Undo.RecordObject(point, "Renumber director route points");
                routeOrder.intValue = i;
                pointData.ApplyModifiedProperties();
                EditorUtility.SetDirty(point);
            }
        }

        MarkSceneDirty(guide.gameObject);
        SceneView.RepaintAll();
    }

    private static List<RouteUsage> FindUsages(
        FirstContactIntroGuidePoint point)
    {
        var result = new List<RouteUsage>();
        if (point == null)
        {
            return result;
        }

        FirstContactIntroGuideController[] guides =
            UnityEngine.Object.FindObjectsByType<FirstContactIntroGuideController>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
        foreach (FirstContactIntroGuideController guide in guides)
        {
            IReadOnlyList<Transform> path = guide.PathPoints;
            if (path == null)
            {
                continue;
            }

            for (int i = 0; i < path.Count; i++)
            {
                if (path[i] == point.transform)
                {
                    result.Add(new RouteUsage(guide, i));
                }
            }
        }

        return result;
    }

    private static FirstContactIntroNarrativeZone[] FindReferencingZones(
        FirstContactIntroGuidePoint point)
    {
        return UnityEngine.Object.FindObjectsByType<FirstContactIntroNarrativeZone>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None)
            .Where(zone => zone.GuideHoldPoint == point)
            .ToArray();
    }

    private static FirstContactIntroGuidePoint FindNeighborPoint(
        FirstContactIntroGuideController guide,
        int index)
    {
        if (guide == null || guide.PathPoints == null)
        {
            return null;
        }

        int neighborIndex = index + 1 < guide.PathPoints.Count
            ? index + 1
            : index - 1;
        if (neighborIndex < 0 || neighborIndex >= guide.PathPoints.Count)
        {
            return null;
        }

        Transform neighbor = guide.PathPoints[neighborIndex];
        return neighbor != null
            ? neighbor.GetComponent<FirstContactIntroGuidePoint>()
            : null;
    }

    private static void SetZoneHoldPoint(
        FirstContactIntroNarrativeZone zone,
        FirstContactIntroGuidePoint point)
    {
        if (zone == null)
        {
            return;
        }

        Undo.RecordObject(zone, "Reassign narrative guide hold");
        SerializedObject zoneData = new(zone);
        SerializedProperty hold = zoneData.FindProperty("guideHoldPoint");
        hold.objectReferenceValue = point;
        zoneData.ApplyModifiedProperties();
        EditorUtility.SetDirty(zone);
    }

    private static void InsertPathReference(
        FirstContactIntroGuideController guide,
        int index,
        Transform target)
    {
        Undo.RecordObject(guide, "Insert director route point");
        SerializedObject guideData = new(guide);
        SerializedProperty path = guideData.FindProperty("pathPoints");
        index = Mathf.Clamp(index, 0, path.arraySize);
        if (index == path.arraySize)
        {
            path.arraySize++;
        }
        else
        {
            path.InsertArrayElementAtIndex(index);
        }

        path.GetArrayElementAtIndex(index).objectReferenceValue = target;
        guideData.ApplyModifiedProperties();
        EditorUtility.SetDirty(guide);
    }

    private static void RemovePathReference(
        FirstContactIntroGuideController guide,
        int index)
    {
        SerializedObject guideData = new(guide);
        SerializedProperty path = guideData.FindProperty("pathPoints");
        if (index < 0 || index >= path.arraySize)
        {
            return;
        }

        Undo.RecordObject(guide, "Remove director route point");
        int oldSize = path.arraySize;
        path.DeleteArrayElementAtIndex(index);
        if (path.arraySize == oldSize)
        {
            path.DeleteArrayElementAtIndex(index);
        }

        guideData.ApplyModifiedProperties();
        EditorUtility.SetDirty(guide);
    }

    private static void CalculateInsertedPose(
        FirstContactIntroGuideController guide,
        int currentIndex,
        bool insertBefore,
        out Vector3 position,
        out Quaternion rotation)
    {
        IReadOnlyList<Transform> path = guide.PathPoints;
        Transform current = path[currentIndex];
        int neighborIndex = insertBefore
            ? currentIndex - 1
            : currentIndex + 1;
        Transform neighbor = neighborIndex >= 0 && neighborIndex < path.Count
            ? path[neighborIndex]
            : null;
        if (neighbor != null)
        {
            position = Vector3.Lerp(current.position, neighbor.position, 0.5f);
            rotation = Quaternion.Slerp(current.rotation, neighbor.rotation, 0.5f);
            return;
        }

        Vector3 direction = current.forward;
        int oppositeIndex = insertBefore
            ? currentIndex + 1
            : currentIndex - 1;
        Transform opposite = oppositeIndex >= 0 && oppositeIndex < path.Count
            ? path[oppositeIndex]
            : null;
        if (opposite != null)
        {
            Vector3 away = current.position - opposite.position;
            if (away.sqrMagnitude > 0.001f)
            {
                direction = away.normalized;
            }
        }

        position = current.position + direction * 1.5f;
        rotation = current.rotation;
    }

    private static void MarkSceneDirty(GameObject context)
    {
        if (context != null && context.scene.IsValid())
        {
            EditorSceneManager.MarkSceneDirty(context.scene);
        }
    }
}

internal static class FirstContactIntroRouteMenus
{
    [MenuItem("CONTEXT/FirstContactIntroGuidePoint/앞에 경로 지점 추가")]
    private static void InsertBefore(MenuCommand command)
    {
        FirstContactIntroRouteEditing.InsertRelative(
            command.context as FirstContactIntroGuidePoint,
            insertBefore: true);
    }

    [MenuItem("CONTEXT/FirstContactIntroGuidePoint/뒤에 경로 지점 추가")]
    private static void InsertAfter(MenuCommand command)
    {
        FirstContactIntroRouteEditing.InsertRelative(
            command.context as FirstContactIntroGuidePoint,
            insertBefore: false);
    }

    [MenuItem("CONTEXT/FirstContactIntroGuidePoint/경로에서 제거")]
    private static void Remove(MenuCommand command)
    {
        FirstContactIntroRouteEditing.Remove(
            command.context as FirstContactIntroGuidePoint);
    }

    [MenuItem("GameObject/First Contact Route/앞에 지점 추가", false, 20)]
    private static void InsertBeforeSelected()
    {
        FirstContactIntroRouteEditing.InsertRelative(
            Selection.activeGameObject.GetComponent<FirstContactIntroGuidePoint>(),
            insertBefore: true);
    }

    [MenuItem("GameObject/First Contact Route/앞에 지점 추가", true)]
    private static bool ValidateInsertBeforeSelected()
    {
        return HasSelectedGuidePoint();
    }

    [MenuItem("GameObject/First Contact Route/뒤에 지점 추가", false, 21)]
    private static void InsertAfterSelected()
    {
        FirstContactIntroRouteEditing.InsertRelative(
            Selection.activeGameObject.GetComponent<FirstContactIntroGuidePoint>(),
            insertBefore: false);
    }

    [MenuItem("GameObject/First Contact Route/뒤에 지점 추가", true)]
    private static bool ValidateInsertAfterSelected()
    {
        return HasSelectedGuidePoint();
    }

    [MenuItem("GameObject/First Contact Route/경로에서 제거", false, 40)]
    private static void RemoveSelected()
    {
        FirstContactIntroRouteEditing.Remove(
            Selection.activeGameObject.GetComponent<FirstContactIntroGuidePoint>());
    }

    [MenuItem("GameObject/First Contact Route/경로에서 제거", true)]
    private static bool ValidateRemoveSelected()
    {
        return HasSelectedGuidePoint();
    }

    private static bool HasSelectedGuidePoint()
    {
        return Selection.activeGameObject != null &&
               Selection.activeGameObject.GetComponent<
                   FirstContactIntroGuidePoint>() != null;
    }
}
