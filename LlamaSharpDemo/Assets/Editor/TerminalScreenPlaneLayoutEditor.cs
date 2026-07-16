using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
internal static class TerminalScreenPlaneLayoutEditor
{
    private const string AlignMenuPath =
        "Tools/Terminal Layout/Align Scene View to Screen Plane %#l";
    private const string LockMenuPath =
        "Tools/Terminal Layout/Lock Scene View to Screen Plane";
    private const string LockPreferenceKey =
        "DoodleDiplomacy.TerminalLayout.LockSceneViewToScreenPlane";

    private static bool _lockToScreenPlane;

    static TerminalScreenPlaneLayoutEditor()
    {
        _lockToScreenPlane = EditorPrefs.GetBool(LockPreferenceKey, true);
        SceneView.duringSceneGui += DrawSceneViewControls;
        Selection.selectionChanged += HandleSelectionChanged;
        if (_lockToScreenPlane)
        {
            EditorApplication.delayCall += FrameLockedSelection;
        }
    }

    [MenuItem(AlignMenuPath, false, 2000)]
    private static void AlignSelectedTerminalScreen()
    {
        if (!TryGetSelectedScreenRect(out RectTransform screenRect))
        {
            return;
        }

        AlignSceneView(SceneView.lastActiveSceneView, screenRect, frameScreen: true);
    }

    [MenuItem(AlignMenuPath, true)]
    private static bool ValidateAlignSelectedTerminalScreen()
    {
        return TryGetSelectedScreenRect(out _);
    }

    [MenuItem(LockMenuPath, false, 2001)]
    private static void ToggleScreenPlaneLock()
    {
        SetScreenPlaneLock(!_lockToScreenPlane);
    }

    [MenuItem(LockMenuPath, true)]
    private static bool ValidateScreenPlaneLock()
    {
        Menu.SetChecked(LockMenuPath, _lockToScreenPlane);
        return TryGetSelectedScreenRect(out _);
    }

    private static void HandleSelectionChanged()
    {
        if (!_lockToScreenPlane)
        {
            SceneView.RepaintAll();
            return;
        }

        EditorApplication.delayCall += AlignLockedSelection;
    }

    private static void AlignLockedSelection()
    {
        if (_lockToScreenPlane &&
            TryGetSelectedScreenRect(out RectTransform screenRect))
        {
            AlignSceneView(SceneView.lastActiveSceneView, screenRect, frameScreen: false);
        }
    }

    private static void FrameLockedSelection()
    {
        if (_lockToScreenPlane &&
            TryGetSelectedScreenRect(out RectTransform screenRect))
        {
            AlignSceneView(SceneView.lastActiveSceneView, screenRect, frameScreen: true);
        }
    }

    private static void DrawSceneViewControls(SceneView sceneView)
    {
        if (!TryGetSelectedScreenRect(out RectTransform screenRect))
        {
            return;
        }

        if (_lockToScreenPlane)
        {
            ConstrainSceneViewToScreenPlane(sceneView, screenRect);
        }

        Handles.BeginGUI();
        GUILayout.BeginArea(
            new Rect(12f, 48f, 248f, 92f),
            "Terminal Screen Layout",
            EditorStyles.helpBox);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Screen Front", GUILayout.Height(24f)))
            {
                AlignSceneView(sceneView, screenRect, frameScreen: true);
            }

            bool requestedLock = GUILayout.Toggle(
                _lockToScreenPlane,
                "Lock Front",
                "Button",
                GUILayout.Height(24f));
            if (requestedLock != _lockToScreenPlane)
            {
                SetScreenPlaneLock(requestedLock);
                if (requestedLock)
                {
                    AlignSceneView(sceneView, screenRect, frameScreen: true);
                }
            }
        }

        EditorGUILayout.LabelField(
            "Rect Tool | Local axes | X/Y = screen plane",
            EditorStyles.miniLabel);
        EditorGUILayout.LabelField(
            "Shortcut: Ctrl+Shift+L",
            EditorStyles.miniLabel);
        GUILayout.EndArea();
        Handles.EndGUI();
    }

    private static void SetScreenPlaneLock(bool enabled)
    {
        _lockToScreenPlane = enabled;
        EditorPrefs.SetBool(LockPreferenceKey, enabled);
        Menu.SetChecked(LockMenuPath, enabled);
        SceneView.RepaintAll();
    }

    private static void AlignSceneView(
        SceneView sceneView,
        RectTransform screenRect,
        bool frameScreen)
    {
        if (sceneView == null || screenRect == null)
        {
            return;
        }

        Quaternion screenRotation = GetScreenViewRotation(screenRect);
        Vector3 pivot = frameScreen
            ? GetScreenWorldCenter(screenRect)
            : sceneView.pivot;
        float size = frameScreen
            ? CalculateFrameSize(sceneView, screenRect)
            : sceneView.size;

        sceneView.LookAt(pivot, screenRotation, size, true);
        Tools.current = Tool.Rect;
        Tools.pivotRotation = PivotRotation.Local;
        sceneView.Repaint();
    }

    private static void ConstrainSceneViewToScreenPlane(
        SceneView sceneView,
        RectTransform screenRect)
    {
        if (sceneView == null || screenRect == null)
        {
            return;
        }

        Quaternion screenRotation = GetScreenViewRotation(screenRect);
        if (!sceneView.orthographic ||
            Quaternion.Angle(sceneView.rotation, screenRotation) > 0.01f)
        {
            sceneView.rotation = screenRotation;
            sceneView.orthographic = true;
        }

        if (Tools.current != Tool.Rect)
        {
            Tools.current = Tool.Rect;
        }

        if (Tools.pivotRotation != PivotRotation.Local)
        {
            Tools.pivotRotation = PivotRotation.Local;
        }
    }

    private static Quaternion GetScreenViewRotation(RectTransform screenRect)
    {
        return Quaternion.LookRotation(screenRect.forward, screenRect.up);
    }

    private static Vector3 GetScreenWorldCenter(RectTransform screenRect)
    {
        return screenRect.TransformPoint(screenRect.rect.center);
    }

    private static float CalculateFrameSize(
        SceneView sceneView,
        RectTransform screenRect)
    {
        var corners = new Vector3[4];
        screenRect.GetWorldCorners(corners);

        float worldWidth = Vector3.Distance(corners[0], corners[3]);
        float worldHeight = Vector3.Distance(corners[0], corners[1]);
        float viewportAspect = sceneView.position.height > 1f
            ? sceneView.position.width / sceneView.position.height
            : 1f;
        float halfHeightForWidth = worldWidth / Mathf.Max(0.1f, viewportAspect) * 0.5f;
        float halfHeight = worldHeight * 0.5f;
        return Mathf.Max(0.01f, Mathf.Max(halfHeight, halfHeightForWidth) * 1.08f);
    }

    internal static bool TryGetScreenRect(
        Transform selectedTransform,
        out RectTransform screenRect)
    {
        screenRect = null;
        if (selectedTransform == null)
        {
            return false;
        }

        Transform current = selectedTransform;
        while (current != null)
        {
            if (current.name == "ScreenPanel" && current is RectTransform currentRect)
            {
                screenRect = currentRect;
                return true;
            }

            if (current.name == "EditableTerminalLayout" &&
                current.parent is RectTransform parentRect)
            {
                screenRect = parentRect;
                return true;
            }

            current = current.parent;
        }

        RectTransform[] descendants =
            selectedTransform.GetComponentsInChildren<RectTransform>(true);
        for (int i = 0; i < descendants.Length; i++)
        {
            RectTransform candidate = descendants[i];
            if (candidate.name == "ScreenPanel" &&
                candidate.parent != null &&
                candidate.parent.name == "TerminalCanvas")
            {
                screenRect = candidate;
                return true;
            }
        }

        return false;
    }

    private static bool TryGetSelectedScreenRect(out RectTransform screenRect)
    {
        return TryGetScreenRect(Selection.activeTransform, out screenRect);
    }
}
