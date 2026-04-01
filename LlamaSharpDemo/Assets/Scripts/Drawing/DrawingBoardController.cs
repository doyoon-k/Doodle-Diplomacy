using UnityEngine;
using UnityEngine.EventSystems;
using System;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Receives pointer input on a collider-backed drawing surface and paints into a runtime texture.
/// </summary>
public class DrawingBoardController : MonoBehaviour
{
    private const HideFlags RuntimeHideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
    private const HideFlags RuntimeHierarchyHideFlags = HideFlags.HideInHierarchy | HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;

    private sealed class StrokeHistoryCapture
    {
        public bool IsActive;
        public RectInt Region;
        public Color32[] BeforePixels;
    }

    [Header("Board")]
    [SerializeField] private Renderer boardRenderer;
    [SerializeField] private Collider drawingSurfaceCollider;
    [SerializeField] private Camera drawingCamera;
    [SerializeField] private int textureWidth = 1024;
    [SerializeField] private int textureHeight = 1024;
    [SerializeField] private FilterMode filterMode = FilterMode.Bilinear;
    [SerializeField] private string texturePropertyName = "_BaseMap";

    [Header("Brush")]
    [SerializeField] private Color backgroundColor = Color.white;
    [SerializeField] private Color brushColor = Color.black;
    [SerializeField] private int brushRadius = 6;
    [SerializeField] private int minBrushRadius = 1;
    [SerializeField] private int maxBrushRadius = 24;
    [SerializeField] private float scrollRadiusStep = 1f;
    [SerializeField] private bool blockPointerWhenOverUi = true;

    [Header("Preview")]
    [SerializeField] private bool showBrushPreview = true;
    [SerializeField] private float previewSurfaceOffset = 0.01f;
    [SerializeField] private float previewLineWidth = 0.01f;
    [SerializeField] private int previewSegments = 48;
    [SerializeField] private Color previewBrushColor = new(0f, 0f, 0f, 0.9f);
    [SerializeField] private Color previewEraserColor = new(0.15f, 0.55f, 1f, 0.95f);

    [Header("History")]
    [SerializeField] private int maxHistoryEntries = 24;

    private DrawingCanvas _canvas;
    private DrawingHistory _history;
    private Material _runtimeMaterial;
    private LineRenderer _brushPreviewRenderer;
    private Material _brushPreviewMaterial;
    private Material _originalSharedMaterial;
    private bool _isDrawing;
    private bool _useEraser;
    private bool _useFillTool;
    private bool _hasCapturedOriginalMaterial;
    private Vector2Int _lastPixel;
    private readonly StrokeHistoryCapture _strokeHistory = new();

    public event Action<int> BrushRadiusChanged;
    public event Action<bool, bool> HistoryStateChanged;

    public Texture2D CanvasTexture => _canvas?.Texture;
    public int BrushRadius => brushRadius;
    public bool IsEraserEnabled => _useEraser;
    public bool IsFillToolEnabled => _useFillTool;
    public Color BrushColor => brushColor;
    public Color BackgroundColor => backgroundColor;
    public Color ActiveDrawColor => GetActiveDrawColor();
    public bool CanUndo => _history != null && _history.CanUndo;
    public bool CanRedo => _history != null && _history.CanRedo;

    private void Awake()
    {
        if (boardRenderer == null)
        {
            boardRenderer = GetComponent<Renderer>();
        }

        if (drawingSurfaceCollider == null)
        {
            drawingSurfaceCollider = GetComponent<Collider>();
        }

        if (drawingCamera == null)
        {
            drawingCamera = Camera.main;
        }
    }

    private void Start()
    {
        InitializeCanvas();
        InitializeBrushPreview();
    }

    private void Update()
    {
        if (_canvas == null || drawingSurfaceCollider == null || drawingCamera == null)
        {
            return;
        }

        UpdateBrushRadiusFromScroll();
        HandlePointerInput();
    }

    [ContextMenu("Clear Canvas")]
    public void ClearCanvas()
    {
        if (_canvas == null)
        {
            return;
        }

        FinalizeStrokeHistory();
        if (!_canvas.TryGetNonBackgroundBounds(out RectInt dirtyRegion))
        {
            return;
        }

        Color32[] beforePixels = _canvas.CopyRegion(dirtyRegion);
        _canvas.Clear();
        Color32[] afterPixels = _canvas.CopyRegion(dirtyRegion);
        RecordHistory(dirtyRegion, beforePixels, afterPixels);
    }

    public void SetBrushColor(Color color)
    {
        brushColor = color;
        _useEraser = false;
    }

    public void SetBrushRadius(float radius)
    {
        int newRadius = Mathf.Clamp(Mathf.RoundToInt(radius), minBrushRadius, maxBrushRadius);
        if (newRadius == brushRadius)
        {
            return;
        }

        brushRadius = newRadius;
        BrushRadiusChanged?.Invoke(brushRadius);
    }

    public void SetEraserEnabled(bool enabled)
    {
        _useEraser = enabled;
        if (enabled)
        {
            _useFillTool = false;
        }
    }

    public void ToggleEraser()
    {
        _useEraser = !_useEraser;
        if (_useEraser)
        {
            _useFillTool = false;
        }
    }

    public void SetFillToolEnabled(bool enabled)
    {
        _useFillTool = enabled;
        if (enabled)
        {
            _useEraser = false;
        }
    }

    public void ToggleFillTool()
    {
        _useFillTool = !_useFillTool;
        if (_useFillTool)
        {
            _useEraser = false;
        }
    }

    public bool Undo()
    {
        FinalizeStrokeHistory();
        if (_history == null || !_history.Undo(_canvas))
        {
            return false;
        }

        NotifyHistoryStateChanged();
        return true;
    }

    public bool Redo()
    {
        FinalizeStrokeHistory();
        if (_history == null || !_history.Redo(_canvas))
        {
            return false;
        }

        NotifyHistoryStateChanged();
        return true;
    }

    private void OnDestroy()
    {
        ResetStrokeHistory();
        _canvas?.Dispose();
        _canvas = null;
        _history?.Clear();
        ReleaseRuntimeMaterial();
        CleanupBrushPreview();
    }

    private void InitializeCanvas()
    {
        if (boardRenderer == null)
        {
            Debug.LogError("[DrawingBoardController] Board renderer is missing.");
            return;
        }

        if (drawingSurfaceCollider == null)
        {
            Debug.LogError("[DrawingBoardController] Drawing surface collider is missing.");
            return;
        }

        CaptureOriginalMaterial();
        ReleaseRuntimeMaterial();
        _canvas?.Dispose();
        _canvas = new DrawingCanvas(textureWidth, textureHeight, backgroundColor, filterMode);
        _history = new DrawingHistory(maxHistoryEntries);
        ResetStrokeHistory();

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
        {
            shader = Shader.Find("Unlit/Texture");
        }

        if (shader == null && boardRenderer.sharedMaterial != null)
        {
            shader = boardRenderer.sharedMaterial.shader;
        }

        _runtimeMaterial = new Material(shader);
        _runtimeMaterial.name = $"{name}_DrawingBoardMaterial";
        _runtimeMaterial.hideFlags = RuntimeHideFlags;
        ConfigureRuntimeMaterial(_runtimeMaterial);
        AssignTexture(_runtimeMaterial, _canvas.Texture, texturePropertyName);
        boardRenderer.sharedMaterial = _runtimeMaterial;
        NotifyHistoryStateChanged();
    }

    private void HandlePointerInput()
    {
        bool pointerDown = GetPointerDownThisFrame();
        bool pointerHeld = GetPointerHeld();
        bool pointerUp = GetPointerUpThisFrame();
        bool pointerOverUi = blockPointerWhenOverUi && IsPointerOverUi();

        UpdateBrushPreview(pointerOverUi);
        HandleHistoryShortcuts();

        if (pointerOverUi)
        {
            _isDrawing = false;
        }

        if (!pointerOverUi && pointerDown && TryGetPointerPixel(out Vector2Int startPixel))
        {
            if (_useFillTool)
            {
                ApplyFill(startPixel);
                _isDrawing = false;
                return;
            }

            BeginStrokeHistory();
            _isDrawing = true;
            _lastPixel = startPixel;
            CaptureStrokeSegmentBeforeChange(startPixel, startPixel);
            _canvas.DrawLine(startPixel, startPixel, GetActiveDrawColor(), brushRadius, out _);
        }

        if (!pointerOverUi && _isDrawing && pointerHeld && TryGetPointerPixel(out Vector2Int currentPixel))
        {
            if (currentPixel != _lastPixel)
            {
                CaptureStrokeSegmentBeforeChange(_lastPixel, currentPixel);
                _canvas.DrawLine(_lastPixel, currentPixel, GetActiveDrawColor(), brushRadius, out _);
                _lastPixel = currentPixel;
            }
        }

        if (pointerUp || (_isDrawing && !pointerHeld))
        {
            _isDrawing = false;
            FinalizeStrokeHistory();
        }
    }

    private Color GetActiveDrawColor()
    {
        return _useEraser ? backgroundColor : brushColor;
    }

    private Color GetPreviewColor()
    {
        return _useEraser ? previewEraserColor : previewBrushColor;
    }

    private static bool IsPointerOverUi()
    {
        return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    }

    private bool TryGetPointerPixel(out Vector2Int pixel)
    {
        pixel = default;

        if (!TryGetPointerHit(out RaycastHit hit))
        {
            return false;
        }

        Vector2 uv = hit.textureCoord;
        int x = Mathf.Clamp(Mathf.FloorToInt(uv.x * _canvas.Width), 0, _canvas.Width - 1);
        int y = Mathf.Clamp(Mathf.FloorToInt(uv.y * _canvas.Height), 0, _canvas.Height - 1);
        pixel = new Vector2Int(x, y);
        return true;
    }

    private bool TryGetPointerHit(out RaycastHit hit)
    {
        hit = default;

        if (!TryGetPointerScreenPosition(out Vector2 pointerScreenPosition))
        {
            return false;
        }

        Ray ray = drawingCamera.ScreenPointToRay(pointerScreenPosition);
        if (!drawingSurfaceCollider.Raycast(ray, out hit, 1000f))
        {
            return false;
        }

        return true;
    }

    private void UpdateBrushRadiusFromScroll()
    {
        float scrollDelta = GetScrollDelta();
        if (Mathf.Abs(scrollDelta) < 0.01f)
        {
            return;
        }

        SetBrushRadius(brushRadius + Mathf.Sign(scrollDelta) * scrollRadiusStep);
    }

    private void HandleHistoryShortcuts()
    {
        if (GetUndoShortcutPressed())
        {
            Undo();
            return;
        }

        if (GetRedoShortcutPressed())
        {
            Redo();
        }
    }

    private bool ApplyFill(Vector2Int pixel)
    {
        if (_canvas == null)
        {
            return false;
        }

        bool filled = _canvas.FloodFill(
            pixel,
            GetActiveDrawColor(),
            out RectInt dirtyRegion,
            out Color32[] beforePixels,
            out Color32[] afterPixels);
        if (filled)
        {
            RecordHistory(dirtyRegion, beforePixels, afterPixels);
        }

        return filled;
    }

    private void BeginStrokeHistory()
    {
        if (_canvas == null)
        {
            return;
        }

        _strokeHistory.IsActive = true;
        _strokeHistory.Region = default;
        _strokeHistory.BeforePixels = null;
    }

    private void FinalizeStrokeHistory()
    {
        if (!_strokeHistory.IsActive || _strokeHistory.BeforePixels == null || _canvas == null || _history == null)
        {
            ResetStrokeHistory();
            return;
        }

        Color32[] afterPixels = _canvas.CopyRegion(_strokeHistory.Region);
        RecordHistory(_strokeHistory.Region, _strokeHistory.BeforePixels, afterPixels);
        ResetStrokeHistory();
    }

    private void RecordHistory(RectInt region, Color32[] beforePixels, Color32[] afterPixels)
    {
        if (_history == null || beforePixels == null || afterPixels == null)
        {
            return;
        }

        bool recorded = _history.Record(region, beforePixels, afterPixels);
        if (recorded)
        {
            NotifyHistoryStateChanged();
        }
    }

    private void CaptureStrokeSegmentBeforeChange(Vector2Int from, Vector2Int to)
    {
        if (_canvas == null || !_strokeHistory.IsActive)
        {
            return;
        }

        RectInt segmentRegion = _canvas.GetLineBounds(from, to, brushRadius);
        if (segmentRegion.width <= 0 || segmentRegion.height <= 0)
        {
            return;
        }

        if (_strokeHistory.BeforePixels == null)
        {
            _strokeHistory.Region = segmentRegion;
            _strokeHistory.BeforePixels = _canvas.CopyRegion(segmentRegion);
            return;
        }

        RectInt expandedRegion = UnionRegions(_strokeHistory.Region, segmentRegion);
        if (expandedRegion.Equals(_strokeHistory.Region))
        {
            return;
        }

        Color32[] expandedBeforePixels = _canvas.CopyRegion(expandedRegion);
        CopyRegionPixels(_strokeHistory.BeforePixels, _strokeHistory.Region, expandedBeforePixels, expandedRegion);
        _strokeHistory.Region = expandedRegion;
        _strokeHistory.BeforePixels = expandedBeforePixels;
    }

    private void ResetStrokeHistory()
    {
        _strokeHistory.IsActive = false;
        _strokeHistory.Region = default;
        _strokeHistory.BeforePixels = null;
    }

    private void NotifyHistoryStateChanged()
    {
        HistoryStateChanged?.Invoke(CanUndo, CanRedo);
    }

    private void InitializeBrushPreview()
    {
        if (!showBrushPreview || _brushPreviewRenderer != null)
        {
            return;
        }

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
        {
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        }

        _brushPreviewMaterial = shader != null ? new Material(shader) : null;
        if (_brushPreviewMaterial != null)
        {
            _brushPreviewMaterial.name = $"{name}_BrushPreviewMaterial";
            _brushPreviewMaterial.hideFlags = RuntimeHideFlags;
            if (_brushPreviewMaterial.HasProperty("_BaseColor"))
            {
                _brushPreviewMaterial.SetColor("_BaseColor", GetPreviewColor());
            }

            if (_brushPreviewMaterial.HasProperty("_Color"))
            {
                _brushPreviewMaterial.SetColor("_Color", GetPreviewColor());
            }
        }

        var previewObject = new GameObject("BrushPreview");
        previewObject.hideFlags = RuntimeHierarchyHideFlags;
        previewObject.transform.SetParent(transform, false);
        _brushPreviewRenderer = previewObject.AddComponent<LineRenderer>();
        _brushPreviewRenderer.hideFlags = RuntimeHideFlags;
        _brushPreviewRenderer.loop = true;
        _brushPreviewRenderer.useWorldSpace = true;
        _brushPreviewRenderer.positionCount = Mathf.Max(16, previewSegments);
        _brushPreviewRenderer.widthMultiplier = previewLineWidth;
        _brushPreviewRenderer.textureMode = LineTextureMode.Stretch;
        _brushPreviewRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _brushPreviewRenderer.receiveShadows = false;
        _brushPreviewRenderer.alignment = LineAlignment.View;
        _brushPreviewRenderer.enabled = false;

        if (_brushPreviewMaterial != null)
        {
            _brushPreviewRenderer.sharedMaterial = _brushPreviewMaterial;
        }
    }

    private void UpdateBrushPreview(bool pointerOverUi)
    {
        if (!showBrushPreview || _brushPreviewRenderer == null)
        {
            return;
        }

        if (_useFillTool || pointerOverUi || !TryGetPointerHit(out RaycastHit hit))
        {
            _brushPreviewRenderer.enabled = false;
            return;
        }

        float radius = GetPreviewWorldRadius(hit);
        Vector3 normal = hit.normal.normalized;
        Vector3 tangent = Vector3.Cross(normal, Vector3.up);
        if (tangent.sqrMagnitude < 0.0001f)
        {
            tangent = Vector3.Cross(normal, Vector3.right);
        }

        tangent.Normalize();
        Vector3 bitangent = Vector3.Cross(normal, tangent).normalized;
        Vector3 center = hit.point + (normal * previewSurfaceOffset);

        int segmentCount = _brushPreviewRenderer.positionCount;
        float step = Mathf.PI * 2f / segmentCount;
        for (int i = 0; i < segmentCount; i++)
        {
            float angle = i * step;
            Vector3 offset = (tangent * Mathf.Cos(angle) + bitangent * Mathf.Sin(angle)) * radius;
            _brushPreviewRenderer.SetPosition(i, center + offset);
        }

        _brushPreviewRenderer.widthMultiplier = previewLineWidth;
        _brushPreviewRenderer.startColor = GetPreviewColor();
        _brushPreviewRenderer.endColor = GetPreviewColor();
        _brushPreviewRenderer.enabled = true;

        if (_brushPreviewMaterial != null)
        {
            if (_brushPreviewMaterial.HasProperty("_BaseColor"))
            {
                _brushPreviewMaterial.SetColor("_BaseColor", GetPreviewColor());
            }

            if (_brushPreviewMaterial.HasProperty("_Color"))
            {
                _brushPreviewMaterial.SetColor("_Color", GetPreviewColor());
            }
        }
    }

    private float GetPreviewWorldRadius(RaycastHit hit)
    {
        Bounds bounds = drawingSurfaceCollider.bounds;
        float xRadius = (brushRadius / (float)_canvas.Width) * bounds.size.x;
        float yRadius = (brushRadius / (float)_canvas.Height) * bounds.size.y;
        float zRadius = (brushRadius / (float)_canvas.Height) * bounds.size.z;

        float secondaryRadius = zRadius > 0.0001f ? zRadius : yRadius;
        float averageRadius = (xRadius + secondaryRadius) * 0.5f;
        return Mathf.Max(0.01f, averageRadius);
    }

    private void CleanupBrushPreview()
    {
        if (_brushPreviewRenderer != null)
        {
            if (Application.isPlaying)
            {
                Destroy(_brushPreviewRenderer.gameObject);
            }
            else
            {
                DestroyImmediate(_brushPreviewRenderer.gameObject);
            }

            _brushPreviewRenderer = null;
        }

        if (_brushPreviewMaterial != null)
        {
            if (Application.isPlaying)
            {
                Destroy(_brushPreviewMaterial);
            }
            else
            {
                DestroyImmediate(_brushPreviewMaterial);
            }

            _brushPreviewMaterial = null;
        }
    }

    private void CaptureOriginalMaterial()
    {
        if (_hasCapturedOriginalMaterial || boardRenderer == null)
        {
            return;
        }

        _originalSharedMaterial = boardRenderer.sharedMaterial;
        _hasCapturedOriginalMaterial = true;
    }

    private void ReleaseRuntimeMaterial()
    {
        if (_runtimeMaterial == null)
        {
            return;
        }

        RestoreOriginalMaterial();

        if (Application.isPlaying)
        {
            Destroy(_runtimeMaterial);
        }
        else
        {
            DestroyImmediate(_runtimeMaterial);
        }

        _runtimeMaterial = null;
    }

    private void RestoreOriginalMaterial()
    {
        if (!_hasCapturedOriginalMaterial || boardRenderer == null)
        {
            return;
        }

        if (boardRenderer.sharedMaterial == _runtimeMaterial)
        {
            boardRenderer.sharedMaterial = _originalSharedMaterial;
        }
    }

    private static RectInt UnionRegions(RectInt left, RectInt right)
    {
        int minX = Mathf.Min(left.xMin, right.xMin);
        int minY = Mathf.Min(left.yMin, right.yMin);
        int maxX = Mathf.Max(left.xMax, right.xMax);
        int maxY = Mathf.Max(left.yMax, right.yMax);
        return new RectInt(minX, minY, maxX - minX, maxY - minY);
    }

    private static void CopyRegionPixels(Color32[] sourcePixels, RectInt sourceRegion, Color32[] destinationPixels, RectInt destinationRegion)
    {
        int offsetX = sourceRegion.xMin - destinationRegion.xMin;
        int offsetY = sourceRegion.yMin - destinationRegion.yMin;

        for (int y = 0; y < sourceRegion.height; y++)
        {
            int sourceIndex = y * sourceRegion.width;
            int destinationIndex = ((offsetY + y) * destinationRegion.width) + offsetX;
            Array.Copy(sourcePixels, sourceIndex, destinationPixels, destinationIndex, sourceRegion.width);
        }
    }

    private static void AssignTexture(Material material, Texture2D texture, string texturePropertyName)
    {
        if (material == null)
        {
            return;
        }

        material.mainTexture = texture;

        if (!string.IsNullOrWhiteSpace(texturePropertyName) && material.HasProperty(texturePropertyName))
        {
            material.SetTexture(texturePropertyName, texture);
        }
    }

    private static void ConfigureRuntimeMaterial(Material material)
    {
        if (material == null)
        {
            return;
        }

        material.color = Color.white;

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", Color.white);
        }

        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", Color.white);
        }
    }

    private static bool TryGetPointerScreenPosition(out Vector2 screenPosition)
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null)
        {
            screenPosition = Mouse.current.position.ReadValue();
            return true;
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        screenPosition = Input.mousePosition;
        return true;
#else
        screenPosition = default;
        return false;
#endif
    }

    private static bool GetPointerDownThisFrame()
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            return true;
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetMouseButtonDown(0);
#else
        return false;
#endif
    }

    private static bool GetPointerHeld()
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null && Mouse.current.leftButton.isPressed)
        {
            return true;
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetMouseButton(0);
#else
        return false;
#endif
    }

    private static bool GetPointerUpThisFrame()
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null && Mouse.current.leftButton.wasReleasedThisFrame)
        {
            return true;
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetMouseButtonUp(0);
#else
        return false;
#endif
    }

    private static float GetScrollDelta()
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null)
        {
            return Mouse.current.scroll.ReadValue().y;
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.mouseScrollDelta.y;
#else
        return 0f;
#endif
    }

    private static bool GetUndoShortcutPressed()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
        {
            bool controlPressed = Keyboard.current.leftCtrlKey.isPressed || Keyboard.current.rightCtrlKey.isPressed;
            if (controlPressed && Keyboard.current.zKey.wasPressedThisFrame)
            {
                return true;
            }
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        bool controlPressed = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        return controlPressed && Input.GetKeyDown(KeyCode.Z);
#else
        return false;
#endif
    }

    private static bool GetRedoShortcutPressed()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
        {
            bool controlPressed = Keyboard.current.leftCtrlKey.isPressed || Keyboard.current.rightCtrlKey.isPressed;
            bool shiftPressed = Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed;
            if (controlPressed && Keyboard.current.yKey.wasPressedThisFrame)
            {
                return true;
            }

            if (controlPressed && shiftPressed && Keyboard.current.zKey.wasPressedThisFrame)
            {
                return true;
            }
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        bool controlPressed = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        bool shiftPressed = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        return (controlPressed && Input.GetKeyDown(KeyCode.Y)) ||
               (controlPressed && shiftPressed && Input.GetKeyDown(KeyCode.Z));
#else
        return false;
#endif
    }
}
