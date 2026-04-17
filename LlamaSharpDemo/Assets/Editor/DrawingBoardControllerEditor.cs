using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(DrawingBoardController))]
public class DrawingBoardControllerEditor : Editor
{
    private SerializedProperty _boardRendererProp;
    private SerializedProperty _drawingSurfaceColliderProp;
    private SerializedProperty _normalizedPaintAreaProp;
    private Transform _proxyTransform;

    private void OnEnable()
    {
        _boardRendererProp = serializedObject.FindProperty("boardRenderer");
        _drawingSurfaceColliderProp = serializedObject.FindProperty("drawingSurfaceCollider");
        _normalizedPaintAreaProp = serializedObject.FindProperty("normalizedPaintArea");

        var board = (DrawingBoardController)target;
        if (board != null && _proxyTransform == null)
        {
            _proxyTransform = board.transform.Find("PaintAreaProxy");
        }
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawDefaultInspector();

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("Proxy Drawing Surface", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Use a proxy cube as the drawing input surface. Legacy paint-area handle/bake workflow was removed.",
            MessageType.Info);

        var board = (DrawingBoardController)target;
        _proxyTransform = (Transform)EditorGUILayout.ObjectField("Proxy", _proxyTransform, typeof(Transform), true);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Create + Assign Proxy"))
            {
                _proxyTransform = CreateProxyCube(board);
                AssignProxyAsDrawingSurface(board, _proxyTransform);
            }

            if (GUILayout.Button("Assign Selected Proxy"))
            {
                AssignProxyAsDrawingSurface(board, _proxyTransform);
            }
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Select Proxy") && _proxyTransform != null)
            {
                Selection.activeTransform = _proxyTransform;
            }

            if (GUILayout.Button("Delete Proxy") && _proxyTransform != null)
            {
                Undo.DestroyObjectImmediate(_proxyTransform.gameObject);
                _proxyTransform = null;
                SceneView.RepaintAll();
            }
        }

        serializedObject.ApplyModifiedProperties();
    }

    private static Transform CreateProxyCube(DrawingBoardController board)
    {
        if (board == null)
        {
            return null;
        }

        Transform existing = board.transform.Find("PaintAreaProxy");
        if (existing != null)
        {
            EnsureProxyCollider(existing);
            return existing;
        }

        GameObject proxy = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Undo.RegisterCreatedObjectUndo(proxy, "Create Paint Area Proxy");
        proxy.name = "PaintAreaProxy";
        proxy.transform.SetParent(board.transform, true);

        Renderer boardRenderer = board.GetComponent<Renderer>();
        if (boardRenderer != null)
        {
            Bounds bounds = boardRenderer.bounds;
            proxy.transform.position = bounds.center + (board.transform.forward * 0.01f);
            proxy.transform.rotation = board.transform.rotation;
            proxy.transform.localScale = new Vector3(
                Mathf.Max(0.05f, bounds.size.x * 0.8f),
                Mathf.Max(0.005f, bounds.size.y * 0.05f),
                Mathf.Max(0.05f, bounds.size.z * 0.8f));
        }
        else
        {
            proxy.transform.localPosition = Vector3.zero;
            proxy.transform.localRotation = Quaternion.identity;
            proxy.transform.localScale = new Vector3(0.4f, 0.01f, 0.4f);
        }

        EnsureProxyCollider(proxy.transform);
        var proxyRenderer = proxy.GetComponent<Renderer>();
        if (proxyRenderer != null)
        {
            proxyRenderer.enabled = true;
        }

        return proxy.transform;
    }

    private static Collider EnsureProxyCollider(Transform proxy)
    {
        if (proxy == null)
        {
            return null;
        }

        Collider collider = proxy.GetComponent<Collider>();
        if (collider == null)
        {
            collider = Undo.AddComponent<BoxCollider>(proxy.gameObject);
        }

        if (collider is BoxCollider boxCollider)
        {
            boxCollider.isTrigger = true;
        }

        return collider;
    }

    private void AssignProxyAsDrawingSurface(DrawingBoardController board, Transform proxy)
    {
        if (board == null || proxy == null)
        {
            return;
        }

        Collider proxyCollider = EnsureProxyCollider(proxy);
        if (proxyCollider == null)
        {
            return;
        }

        serializedObject.Update();
        Undo.RecordObject(target, "Assign Proxy Drawing Surface");
        if (_boardRendererProp != null)
        {
            _boardRendererProp.objectReferenceValue = proxy.GetComponent<Renderer>();
        }

        _drawingSurfaceColliderProp.objectReferenceValue = proxyCollider;
        _normalizedPaintAreaProp.rectValue = new Rect(0f, 0f, 1f, 1f);
        serializedObject.ApplyModifiedProperties();
        EditorUtility.SetDirty(target);
        SceneView.RepaintAll();
    }
}
