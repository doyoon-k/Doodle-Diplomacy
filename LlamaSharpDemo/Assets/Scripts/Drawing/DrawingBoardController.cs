using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using DoodleDiplomacy.Core;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public enum DrawingToolMode
{
    Brush = 0,
    Eraser = 1,
    Fill = 2,
    SketchGuide = 3,
    StickerMaskErase = 4
}

/// <summary>
/// Receives pointer input on a collider-backed drawing surface and paints into a runtime texture.
/// </summary>
public class DrawingBoardController : MonoBehaviour
{
    private const HideFlags RuntimeHideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
    private const HideFlags RuntimeHierarchyHideFlags = HideFlags.HideInHierarchy | HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
    private const int MinBrushPreviewCircleSegments = 24;

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
    [SerializeField] private int textureWidth = 512;
    [SerializeField] private int textureHeight = 512;
    [SerializeField] private bool autoMatchCanvasResolutionToBoardAspect = true;
    [SerializeField] private FilterMode filterMode = FilterMode.Bilinear;
    [SerializeField] private string texturePropertyName = "_BaseMap";
    [SerializeField] private Material boardMaterialTemplate;
    [SerializeField] private Vector2 boardTextureScale = new(-1f, -1f);
    [SerializeField] private Vector2 boardTextureOffset = new(1f, 1f);

    [Header("Brush")]
    [SerializeField] private Color backgroundColor = Color.white;
    [SerializeField] private Color nonPaintAreaDisplayColor = new(0.88f, 0.90f, 0.94f, 1f);
    [SerializeField] private Color paintAreaDividerColor = new(0.73f, 0.77f, 0.84f, 1f);
    [SerializeField] [Range(0f, 0.02f)] private float paintAreaDividerWidthNormalized = 0.003f;
    [SerializeField] private Color brushColor = Color.black;
    [SerializeField] private int brushRadius = 6;
    [SerializeField] private int minBrushRadius = 1;
    [SerializeField] private int maxBrushRadius = 24;
    [SerializeField] private bool blockPointerWhenOverUi = true;
    [SerializeField] private Rect normalizedPaintArea = new(0.40f, 0.02f, 0.58f, 0.96f);

    [Header("Preview")]
    [SerializeField] private bool showBrushPreview = true;
    [SerializeField] private float previewSurfaceOffset = 0.01f;
    [SerializeField] private int previewSegments = 48;
    [SerializeField] private Color previewBrushColor = new(0f, 0f, 0f, 0.9f);
    [SerializeField] private Color previewEraserColor = new(0.15f, 0.55f, 1f, 0.95f);

    [Header("History")]
    [SerializeField] private int maxHistoryEntries = 24;
    // Sketch-guide and sticker-layer inspector parameters were removed.
    private readonly Color sketchGuideStrokeColor = new(0f, 0f, 0f, 1f);
    private readonly Color sketchGuideOverlayColor = new(0.05f, 0.8f, 1f, 0.85f);
    private const int sketchGuideRegionPadding = 24;
    private const bool applyFullSketchResult = true;
    private const int sketchResultApplyRowsPerFrame = 32;
    private bool enableStickerLayers;
    private const float stickerSurfaceOffset = 0.02f;
    private const float stickerDepthStep = 0.0025f;
    private const float minStickerScale = 0.08f;
    private const float maxStickerScale = 12f;
    private const float stickerScaleStep = 0.08f;
    private const float stickerRotationStep = 8f;
    private const float stickerOpacityStep = 0.05f;
    private readonly Color selectedStickerOutlineColor = new(0.15f, 0.90f, 1f, 0.95f);

    private DrawingCanvas _canvas;
    private DrawingCanvas _displayCanvas;
    private DrawingCanvas _exportCanvas;
    private DrawingHistory _history;
    private Material _runtimeMaterial;
    private LineRenderer _brushPreviewRenderer;
    private Material _brushPreviewMaterial;
    private MeshRenderer _brushPreviewFillRenderer;
    private Material _brushPreviewFillMaterial;
    private Mesh _brushPreviewFillMesh;
    private Material _originalSharedMaterial;
    private bool _isDrawing;
    private bool _useEraser;
    private bool _useFillTool;
    private bool _useSketchGuide;
    private bool _isInteractionLocked;
    private Vector2Int _lastPixel;
    private readonly StrokeHistoryCapture _strokeHistory = new();
    private readonly List<DrawingStickerLayer> _stickerLayers = new();
    private Transform _stickerRoot;
    private DrawingStickerLayer _selectedSticker;
    private bool _isDraggingSticker;
    private bool _useStickerMaskErase;
    private bool _isErasingStickerMask;
    private Vector3 _stickerDragOffsetBoardLocal;
    private Color32[] _originalSurfacePixels;
    private int _originalSurfaceWidth;
    private int _originalSurfaceHeight;
    private Vector2 _originalSurfaceTextureScale = Vector2.one;
    private Vector2 _originalSurfaceTextureOffset = Vector2.zero;
    private TextureWrapMode _originalSurfaceWrapMode = TextureWrapMode.Clamp;
    private bool _hasOriginalSurfaceTextureData;

    public event Action<int> BrushRadiusChanged;
    public event Action<bool, bool> HistoryStateChanged;
    public event Action<bool, bool> SketchGuideStateChanged;
    public event Action<bool, float, string> StickerSelectionChanged;

    public Texture2D CanvasTexture => _canvas?.Texture;
    public Texture2D GuideTexture => null;
    public Texture2D DisplayTexture => _displayCanvas?.Texture;
    public Material RuntimeBoardMaterial => _runtimeMaterial;
    public bool HasCanvasMarks => _canvas != null && _canvas.TryGetNonBackgroundBounds(out _);
    public int BrushRadius => brushRadius;
    public bool IsEraserEnabled => _useEraser;
    public bool IsFillToolEnabled => _useFillTool;
    public bool IsSketchGuideEnabled => false;
    public bool IsInteractionLocked => _isInteractionLocked;
    public Color BrushColor => brushColor;
    public Color BackgroundColor => backgroundColor;
    public Color ActiveDrawColor => GetActiveDrawColor();
    public bool CanUndo => _history != null && _history.CanUndo;
    public bool CanRedo => _history != null && _history.CanRedo;
    public int SketchGuideRegionPadding => Mathf.Max(0, sketchGuideRegionPadding);
    public bool HasSketchGuide => false;
    public bool HasSelectedSticker => false;
    public float SelectedStickerOpacity => 1f;
    public string SelectedStickerLabel => string.Empty;
    public bool IsStickerMaskEraseEnabled => false;
    public int StickerCount => 0;

    private void Awake()
    {
        ResolveRuntimeReferences();
    }

    private void OnEnable()
    {
        EnsureRuntimeReady();
    }

    private void Start()
    {
        EnsureRuntimeReady();
    }

    private void Update()
    {
        EnsureRuntimeReady();
        if (_canvas == null || _displayCanvas == null ||
            drawingSurfaceCollider == null || drawingCamera == null)
        {
            return;
        }

        if (!IsDrawingPhaseActive())
        {
            _isDrawing = false;
            HideBrushPreview();

            return;
        }

        HandlePointerInput();
    }

    [ContextMenu("Clear Canvas")]
    public void ClearCanvas()
    {
        if (_canvas == null || _displayCanvas == null || _isInteractionLocked)
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
        RefreshDisplayRegion(dirtyRegion);
    }

    [ContextMenu("Clear Sketch Guide")]
    public void ClearSketchGuide()
    {
        _useSketchGuide = false;
        NotifySketchGuideStateChanged();
    }

    public bool TryCreateStickerFromTexture(
        Texture2D stickerTexture,
        RectInt placementRegion,
        string stickerLabel,
        out string error)
    {
        error = "Sticker layers were removed from DrawingBoardController.";
        NotifyStickerSelectionChanged();
        return false;
    }

    public bool TryApplyStickerFromTexture(
        Texture2D stickerTexture,
        RectInt placementRegion,
        string stickerLabel,
        out string error)
    {
        error = "Sticker layers were removed from DrawingBoardController.";
        NotifyStickerSelectionChanged();
        return false;
    }

    public bool DeleteSelectedSticker()
    {
        NotifyStickerSelectionChanged();
        return false;
    }

    public bool ConfirmSelectedStickerPlacement()
    {
        NotifyStickerSelectionChanged();
        return false;
    }

    public void SetSelectedStickerOpacity(float opacity)
    {
        NotifyStickerSelectionChanged();
    }

    public Texture2D GetCompositeTextureForExport()
    {
        if (_canvas == null)
        {
            return null;
        }

        if (_exportCanvas == null ||
            _exportCanvas.Width != _canvas.Width ||
            _exportCanvas.Height != _canvas.Height)
        {
            _exportCanvas?.Dispose();
            _exportCanvas = new DrawingCanvas(_canvas.Width, _canvas.Height, backgroundColor, filterMode);
        }

        Color32[] compositePixels = _canvas.CopyPixels();

        _exportCanvas.ApplyRegion(
            new RectInt(0, 0, _canvas.Width, _canvas.Height),
            compositePixels);
        return _exportCanvas.Texture;
    }

    public void SetBrushColor(Color color)
    {
        FinalizeStrokeHistory();
        brushColor = color;
        _useEraser = false;
        _useSketchGuide = false;
        _isDrawing = false;
        NotifySketchGuideStateChanged();
    }

    public void SetToolMode(DrawingToolMode mode)
    {
        FinalizeStrokeHistory();
        _isDrawing = false;
        _isDraggingSticker = false;
        _isErasingStickerMask = false;
        _useEraser = mode == DrawingToolMode.Eraser;
        _useFillTool = mode == DrawingToolMode.Fill;
        _useSketchGuide = false;
        _useStickerMaskErase = false;

        NotifySketchGuideStateChanged();
        NotifyStickerSelectionChanged();
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
        SetToolMode(enabled ? DrawingToolMode.Eraser : DrawingToolMode.Brush);
    }

    public void ToggleEraser()
    {
        SetEraserEnabled(!_useEraser);
    }

    public void SetFillToolEnabled(bool enabled)
    {
        SetToolMode(enabled ? DrawingToolMode.Fill : DrawingToolMode.Brush);
    }

    public void ToggleFillTool()
    {
        SetFillToolEnabled(!_useFillTool);
    }

    public void SetSketchGuideEnabled(bool enabled)
    {
        SetToolMode(DrawingToolMode.Brush);
    }

    public void ToggleSketchGuide()
    {
        SetSketchGuideEnabled(false);
    }

    public void SetStickerMaskEraseEnabled(bool enabled)
    {
        _useStickerMaskErase = false;
        _isDraggingSticker = false;
        _isErasingStickerMask = false;
        NotifyStickerSelectionChanged();
    }

    public void ToggleStickerMaskErase()
    {
        SetStickerMaskEraseEnabled(false);
    }

    public void SetInteractionLocked(bool locked)
    {
        if (_isInteractionLocked == locked)
        {
            return;
        }

        _isInteractionLocked = locked;
        if (locked)
        {
            _isDrawing = false;
            _isDraggingSticker = false;
            _isErasingStickerMask = false;
            FinalizeStrokeHistory();
            HideBrushPreview();
        }
    }

    public DrawingToolMode GetCurrentToolMode()
    {
        if (_useFillTool)
        {
            return DrawingToolMode.Fill;
        }

        return _useEraser ? DrawingToolMode.Eraser : DrawingToolMode.Brush;
    }

    public bool TryGetSketchGuideBounds(out RectInt guideRegion)
    {
        guideRegion = default;
        return false;
    }

    public bool TryBuildSketchGuideControlTexture(
        out Texture2D controlTexture,
        out RectInt guideRegion,
        out string error)
    {
        controlTexture = null;
        guideRegion = default;
        error = "Sketch guide logic was removed from DrawingBoardController.";
        return false;
    }

    public bool TryApplySketchGuideResult(
        Texture2D generatedTexture,
        RectInt guideRegion,
        int regionPadding,
        out RectInt appliedRegion,
        out string error)
    {
        appliedRegion = default;
        error = "Sketch guide logic was removed from DrawingBoardController.";
        return false;
    }

    public IEnumerator ApplySketchGuideResultCoroutine(
        Texture2D generatedTexture,
        RectInt guideRegion,
        int regionPadding,
        CancellationToken cancellationToken,
        Action<bool, RectInt, string> onComplete)
    {
        onComplete?.Invoke(false, default, "Sketch guide logic was removed from DrawingBoardController.");
        yield break;
    }

    public bool Undo()
    {
        if (_isInteractionLocked)
        {
            return false;
        }

        FinalizeStrokeHistory();
        if (_history == null || !_history.Undo(_canvas))
        {
            return false;
        }

        RefreshDisplayFullCanvas();
        NotifyHistoryStateChanged();
        return true;
    }

    public bool Redo()
    {
        if (_isInteractionLocked)
        {
            return false;
        }

        FinalizeStrokeHistory();
        if (_history == null || !_history.Redo(_canvas))
        {
            return false;
        }

        RefreshDisplayFullCanvas();
        NotifyHistoryStateChanged();
        return true;
    }

    public void SetBoardMaterialTemplate(Material template, bool reinitializeIfReady = true)
    {
        if (template == null)
        {
            return;
        }

        boardMaterialTemplate = template;
        if (!reinitializeIfReady)
        {
            return;
        }

        bool isRuntimeReady =
            _canvas != null &&
            _displayCanvas != null &&
            boardRenderer != null &&
            drawingSurfaceCollider != null;
        if (!isRuntimeReady)
        {
            return;
        }

        InitializeCanvas();
    }

    private void OnDestroy()
    {
        ResetStrokeHistory();
        _canvas?.Dispose();
        _canvas = null;
        _displayCanvas?.Dispose();
        _displayCanvas = null;
        _exportCanvas?.Dispose();
        _exportCanvas = null;
        _history?.Clear();
        ReleaseRuntimeMaterial();
        CleanupBrushPreview();
    }

    private void InitializeCanvas()
    {
        ResolveRuntimeReferences();
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

        Material sourceMaterial = boardMaterialTemplate != null
            ? boardMaterialTemplate
            : boardRenderer.sharedMaterial;
        if (sourceMaterial == null)
        {
            Debug.LogError("[DrawingBoardController] Board material template is missing.");
            return;
        }

        SetOriginalMaterialSource(sourceMaterial);
        ReleaseRuntimeMaterial();
        _canvas?.Dispose();
        _displayCanvas?.Dispose();
        _exportCanvas?.Dispose();
        GetResolvedCanvasDimensions(out int resolvedWidth, out int resolvedHeight);
        _canvas = new DrawingCanvas(resolvedWidth, resolvedHeight, backgroundColor, filterMode);
        _displayCanvas = new DrawingCanvas(resolvedWidth, resolvedHeight, backgroundColor, filterMode);
        _exportCanvas = new DrawingCanvas(resolvedWidth, resolvedHeight, backgroundColor, filterMode);
        _history = new DrawingHistory(maxHistoryEntries);
        ResetStrokeHistory();
        RefreshDisplayFullCanvas();

        _runtimeMaterial = new Material(_originalSharedMaterial);
        _runtimeMaterial.name = $"{name}_DrawingBoardMaterial";
        _runtimeMaterial.hideFlags = RuntimeHideFlags;
        AssignTexture(_runtimeMaterial, _displayCanvas.Texture, texturePropertyName);
        ConfigureDisplayMaterial(_runtimeMaterial, _displayCanvas.Texture, texturePropertyName);
        boardRenderer.sharedMaterial = _runtimeMaterial;
        ConfigureBoardRendererForDisplay();
        NotifyHistoryStateChanged();
        NotifySketchGuideStateChanged();
    }

    private void HandlePointerInput()
    {
        if (_isInteractionLocked)
        {
            UpdateBrushPreview(pointerOverUi: true);
            _isDrawing = false;
            FinalizeStrokeHistory();
            return;
        }

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
            if (_canvas.DrawLine(startPixel, startPixel, GetActiveDrawColor(), brushRadius, out RectInt dirtyRegion))
            {
                RefreshDisplayRegion(dirtyRegion);
            }
        }

        if (!pointerOverUi && _isDrawing && pointerHeld && TryGetPointerPixel(out Vector2Int currentPixel))
        {
            if (currentPixel != _lastPixel)
            {
                CaptureStrokeSegmentBeforeChange(_lastPixel, currentPixel);
                if (_canvas.DrawLine(_lastPixel, currentPixel, GetActiveDrawColor(), brushRadius, out RectInt dirtyRegion))
                {
                    RefreshDisplayRegion(dirtyRegion);
                }

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
        return brushColor;
    }

    private static bool IsPointerOverUi()
    {
        if (EventSystem.current == null)
        {
            return false;
        }

#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null && EventSystem.current.IsPointerOverGameObject(Mouse.current.deviceId))
        {
            return true;
        }
#endif

        return EventSystem.current.IsPointerOverGameObject();
    }

    private bool TryGetPointerPixel(out Vector2Int pixel)
    {
        pixel = default;

        if (!TryGetPointerCanvasUv(out Vector2 canvasUv))
        {
            return false;
        }

        int x = Mathf.Clamp(Mathf.FloorToInt(canvasUv.x * _canvas.Width), 0, _canvas.Width - 1);
        int y = Mathf.Clamp(Mathf.FloorToInt(canvasUv.y * _canvas.Height), 0, _canvas.Height - 1);
        pixel = new Vector2Int(x, y);
        return true;
    }

    private bool TryGetPointerCanvasUv(out Vector2 canvasUv)
    {
        canvasUv = default;

        if (_canvas == null || !TryGetPointerHit(out RaycastHit hit))
        {
            return false;
        }

        if (!TryGetSurfaceUvFromHit(hit, out Vector2 surfaceUv))
        {
            return false;
        }

        canvasUv = SurfaceUvToCanvasUv(surfaceUv);
        if (!IsCanvasUvInPaintArea(canvasUv))
        {
            return false;
        }

        return true;
    }

    private bool TryGetPointerHit(out RaycastHit hit)
    {
        hit = default;

        if (!TryGetPointerScreenPosition(out Vector2 pointerScreenPosition))
        {
            return false;
        }

        Camera activeCamera = drawingCamera != null ? drawingCamera : Camera.main;
        if (activeCamera == null)
        {
            return false;
        }

        Ray ray = activeCamera.ScreenPointToRay(pointerScreenPosition);
        if (!drawingSurfaceCollider.Raycast(ray, out hit, 1000f))
        {
            return false;
        }

        return true;
    }

    private bool TryGetSurfaceUvFromHit(RaycastHit hit, out Vector2 surfaceUv)
    {
        surfaceUv = default;

        if (hit.collider is MeshCollider)
        {
            surfaceUv = hit.textureCoord;
            return true;
        }

        if (hit.collider is BoxCollider hitBoxCollider &&
            TryGetSurfaceUvFromBoxColliderHit(
                hit.point,
                hitBoxCollider,
                out surfaceUv))
        {
            return true;
        }

        if (drawingSurfaceCollider is BoxCollider configuredBoxCollider &&
            TryGetSurfaceUvFromBoxColliderHit(
                hit.point,
                configuredBoxCollider,
                out surfaceUv))
        {
            return true;
        }

        return false;
    }

    private static bool TryGetSurfaceUvFromBoxColliderHit(
        Vector3 worldPoint,
        BoxCollider boxCollider,
        out Vector2 surfaceUv)
    {
        surfaceUv = default;
        if (boxCollider == null)
        {
            return false;
        }

        Vector3 size = boxCollider.size;
        Vector3 axisWorldSizes = GetBoxAxisWorldSizes(boxCollider);
        if (!TryResolveBoxPaintAxes(boxCollider, axisWorldSizes, out int uAxis, out int vAxis))
        {
            return false;
        }

        Vector3 localPoint = boxCollider.transform.InverseTransformPoint(worldPoint) - boxCollider.center;
        float halfU = Mathf.Abs(GetAxis(size, uAxis)) * 0.5f;
        float halfV = Mathf.Abs(GetAxis(size, vAxis)) * 0.5f;
        if (halfU <= 0.0001f || halfV <= 0.0001f)
        {
            return false;
        }

        float u = Mathf.InverseLerp(-halfU, halfU, GetAxis(localPoint, uAxis));
        float v = Mathf.InverseLerp(-halfV, halfV, GetAxis(localPoint, vAxis));
        u = 1f - u;
        v = 1f - v;

        surfaceUv = new Vector2(Mathf.Clamp01(u), Mathf.Clamp01(v));
        return true;
    }

    private static bool TryResolveBoxPaintAxes(
        BoxCollider boxCollider,
        Vector3 axisWorldSizes,
        out int uAxis,
        out int vAxis)
    {
        uAxis = 0;
        vAxis = 2;
        if (boxCollider == null)
        {
            return false;
        }

        float x = Mathf.Abs(GetAxis(axisWorldSizes, 0));
        float y = Mathf.Abs(GetAxis(axisWorldSizes, 1));
        float z = Mathf.Abs(GetAxis(axisWorldSizes, 2));
        int normalAxis;

        if (x <= y && x <= z)
        {
            normalAxis = 0;
        }
        else if (y <= x && y <= z)
        {
            normalAxis = 1;
        }
        else
        {
            normalAxis = 2;
        }

        // Keep UV orientation deterministic based on collider's thinnest axis.
        switch (normalAxis)
        {
            case 0:
                uAxis = 2;
                vAxis = 1;
                break;
            case 1:
                uAxis = 0;
                vAxis = 2;
                break;
            default:
                uAxis = 0;
                vAxis = 1;
                break;
        }

        return Mathf.Abs(GetAxis(axisWorldSizes, uAxis)) > 0.0001f &&
               Mathf.Abs(GetAxis(axisWorldSizes, vAxis)) > 0.0001f;
    }

    private static Vector3 GetBoxAxisWorldSizes(BoxCollider boxCollider)
    {
        if (boxCollider == null)
        {
            return Vector3.zero;
        }

        Vector3 size = boxCollider.size;
        return new Vector3(
            boxCollider.transform.TransformVector(new Vector3(size.x, 0f, 0f)).magnitude,
            boxCollider.transform.TransformVector(new Vector3(0f, size.y, 0f)).magnitude,
            boxCollider.transform.TransformVector(new Vector3(0f, 0f, size.z)).magnitude);
    }

    private static float GetAxis(Vector3 value, int axis)
    {
        switch (axis)
        {
            case 0:
                return value.x;
            case 1:
                return value.y;
            default:
                return value.z;
        }
    }

    private static Vector3 GetAxisDirection(Transform targetTransform, int axis)
    {
        if (targetTransform == null)
        {
            return Vector3.zero;
        }

        Vector3 axisVector;
        switch (axis)
        {
            case 0:
                axisVector = new Vector3(1f, 0f, 0f);
                break;
            case 1:
                axisVector = new Vector3(0f, 1f, 0f);
                break;
            default:
                axisVector = new Vector3(0f, 0f, 1f);
                break;
        }

        return targetTransform.TransformVector(axisVector).normalized;
    }

    private bool IsCanvasUvInPaintArea(Vector2 canvasUv)
    {
        Rect clampedArea = GetClampedPaintArea();
        return clampedArea.Contains(canvasUv);
    }

    private Rect GetClampedPaintArea()
    {
        float x = Mathf.Clamp01(normalizedPaintArea.x);
        float y = Mathf.Clamp01(normalizedPaintArea.y);
        float width = Mathf.Clamp(normalizedPaintArea.width, 0.01f, 1f - x);
        float height = Mathf.Clamp(normalizedPaintArea.height, 0.01f, 1f - y);
        return new Rect(x, y, width, height);
    }

    private RectInt GetPaintAreaPixelRect()
    {
        if (_canvas == null)
        {
            return default;
        }

        Rect paintArea = GetClampedPaintArea();
        int minX = Mathf.Clamp(Mathf.FloorToInt(paintArea.xMin * _canvas.Width), 0, _canvas.Width - 1);
        int minY = Mathf.Clamp(Mathf.FloorToInt(paintArea.yMin * _canvas.Height), 0, _canvas.Height - 1);
        int maxX = Mathf.Clamp(Mathf.CeilToInt(paintArea.xMax * _canvas.Width), minX + 1, _canvas.Width);
        int maxY = Mathf.Clamp(Mathf.CeilToInt(paintArea.yMax * _canvas.Height), minY + 1, _canvas.Height);
        return new RectInt(minX, minY, maxX - minX, maxY - minY);
    }

    private void GetResolvedCanvasDimensions(out int resolvedWidth, out int resolvedHeight)
    {
        resolvedWidth = Mathf.Max(1, textureWidth);
        resolvedHeight = Mathf.Max(1, textureHeight);

        if (!autoMatchCanvasResolutionToBoardAspect)
        {
            ClampCanvasResolutionToHardwareLimit(ref resolvedWidth, ref resolvedHeight);
            return;
        }

        float boardAspect = ResolveCanvasWorldAspect();
        if (boardAspect <= 0.0001f)
        {
            ClampCanvasResolutionToHardwareLimit(ref resolvedWidth, ref resolvedHeight);
            return;
        }

        int referenceResolution = Mathf.Max(resolvedWidth, resolvedHeight);
        if (boardAspect >= 1f)
        {
            resolvedWidth = Mathf.Max(1, Mathf.RoundToInt(referenceResolution * boardAspect));
            resolvedHeight = referenceResolution;
        }
        else
        {
            resolvedWidth = referenceResolution;
            resolvedHeight = Mathf.Max(1, Mathf.RoundToInt(referenceResolution / boardAspect));
        }

        ClampCanvasResolutionToHardwareLimit(ref resolvedWidth, ref resolvedHeight);
    }

    private static void ClampCanvasResolutionToHardwareLimit(ref int width, ref int height)
    {
        int maxTextureSize = Mathf.Max(1, SystemInfo.maxTextureSize);
        if (width <= maxTextureSize && height <= maxTextureSize)
        {
            return;
        }

        float scale = Mathf.Min(
            maxTextureSize / Mathf.Max(1f, width),
            maxTextureSize / Mathf.Max(1f, height));
        width = Mathf.Max(1, Mathf.FloorToInt(width * scale));
        height = Mathf.Max(1, Mathf.FloorToInt(height * scale));
    }

    private float ResolveCanvasWorldAspect()
    {
        float scaleX = Mathf.Max(0.0001f, Mathf.Abs(boardTextureScale.x));
        float scaleY = Mathf.Max(0.0001f, Mathf.Abs(boardTextureScale.y));
        if (!TryGetBoardWorldSurfaceSize(out float boardWorldWidth, out float boardWorldHeight))
        {
            return 1f;
        }

        float worldWidth = boardWorldWidth / scaleX;
        float worldHeight = boardWorldHeight / scaleY;
        if (worldHeight <= 0.0001f)
        {
            return 1f;
        }

        return Mathf.Max(0.01f, worldWidth / worldHeight);
    }

    private bool TryGetBoardWorldSurfaceSize(out float worldWidth, out float worldHeight)
    {
        worldWidth = 1f;
        worldHeight = 1f;

        if (drawingSurfaceCollider is BoxCollider boxCollider &&
            TryGetBoxColliderWorldSurfaceSize(boxCollider, out worldWidth, out worldHeight))
        {
            return true;
        }

        Bounds localBounds = GetBoardMeshBounds();
        if (localBounds.size.x <= 0.0001f || localBounds.size.z <= 0.0001f)
        {
            return false;
        }

        Transform referenceTransform = boardRenderer != null ? boardRenderer.transform : transform;
        worldWidth = referenceTransform.TransformVector(new Vector3(localBounds.size.x, 0f, 0f)).magnitude;
        worldHeight = referenceTransform.TransformVector(new Vector3(0f, 0f, localBounds.size.z)).magnitude;
        return worldWidth > 0.0001f && worldHeight > 0.0001f;
    }

    private static bool TryGetBoxColliderWorldSurfaceSize(BoxCollider boxCollider, out float worldWidth, out float worldHeight)
    {
        worldWidth = 1f;
        worldHeight = 1f;
        if (boxCollider == null)
        {
            return false;
        }

        Vector3 axisWorldSizes = GetBoxAxisWorldSizes(boxCollider);
        if (!TryResolveBoxPaintAxes(boxCollider, axisWorldSizes, out int uAxis, out int vAxis))
        {
            return false;
        }

        worldWidth = GetAxis(axisWorldSizes, uAxis);
        worldHeight = GetAxis(axisWorldSizes, vAxis);
        return worldWidth > 0.0001f && worldHeight > 0.0001f;
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

        RectInt fillBounds = GetPaintAreaPixelRect();
        if (fillBounds.width <= 0 || fillBounds.height <= 0)
        {
            return false;
        }

        bool filled = _canvas.FloodFill(
            pixel,
            GetActiveDrawColor(),
            out RectInt dirtyRegion,
            out Color32[] beforePixels,
            out Color32[] afterPixels,
            fillBounds);
        if (filled)
        {
            RecordHistory(dirtyRegion, beforePixels, afterPixels);
            RefreshDisplayRegion(dirtyRegion);
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

    private void NotifySketchGuideStateChanged()
    {
        SketchGuideStateChanged?.Invoke(false, false);
    }

    private void NotifyStickerSelectionChanged()
    {
        StickerSelectionChanged?.Invoke(false, 1f, string.Empty);
    }

    private void EnsureStickerRoot()
    {
        if (!enableStickerLayers || _stickerRoot != null)
        {
            return;
        }

        Transform existingRoot = transform.Find("StickerRoot");
        if (existingRoot != null)
        {
            _stickerRoot = existingRoot;
            return;
        }

        var stickerRootObject = new GameObject("StickerRoot");
        stickerRootObject.hideFlags = RuntimeHideFlags;
        stickerRootObject.transform.SetParent(transform, false);
        stickerRootObject.transform.localPosition = Vector3.zero;
        stickerRootObject.transform.localRotation = Quaternion.identity;
        stickerRootObject.transform.localScale = Vector3.one;
        _stickerRoot = stickerRootObject.transform;
    }

    private void ClearAllStickerLayers()
    {
        SelectSticker(null);
        _isDraggingSticker = false;

        for (int i = _stickerLayers.Count - 1; i >= 0; i--)
        {
            DrawingStickerLayer stickerLayer = _stickerLayers[i];
            if (stickerLayer == null)
            {
                continue;
            }

            if (Application.isPlaying)
            {
                Destroy(stickerLayer.gameObject);
            }
            else
            {
                DestroyImmediate(stickerLayer.gameObject);
            }
        }

        _stickerLayers.Clear();
        if (_stickerRoot != null)
        {
            if (Application.isPlaying)
            {
                Destroy(_stickerRoot.gameObject);
            }
            else
            {
                DestroyImmediate(_stickerRoot.gameObject);
            }

            _stickerRoot = null;
        }
    }

    private void SelectSticker(DrawingStickerLayer stickerLayer)
    {
        if (_selectedSticker == stickerLayer)
        {
            if (_selectedSticker == null)
            {
                _useStickerMaskErase = false;
                _isErasingStickerMask = false;
            }

            NotifyStickerSelectionChanged();
            return;
        }

        if (_selectedSticker != null)
        {
            _selectedSticker.SetSelected(false);
        }

        _selectedSticker = stickerLayer;
        if (_selectedSticker != null)
        {
            _selectedSticker.SetSelected(true);
        }
        else
        {
            _useStickerMaskErase = false;
            _isErasingStickerMask = false;
        }

        NotifyStickerSelectionChanged();
    }

    private bool TryHandleStickerPointerInput(
        bool pointerDown,
        bool pointerHeld,
        bool pointerUp,
        bool pointerOverUi)
    {
        if (!enableStickerLayers)
        {
            _isDraggingSticker = false;
            _isErasingStickerMask = false;
            _useStickerMaskErase = false;
            return false;
        }

        if (pointerOverUi)
        {
            if (pointerUp || !pointerHeld)
            {
                _isDraggingSticker = false;
                _isErasingStickerMask = false;
            }

            return _isDraggingSticker || _isErasingStickerMask;
        }

        if (_useStickerMaskErase)
        {
            return TryHandleStickerMaskErasePointerInput(pointerDown, pointerHeld, pointerUp);
        }

        if (_isDraggingSticker)
        {
            if (_selectedSticker != null && pointerHeld && TryGetPointerBoardLocalPoint(out Vector3 boardLocalPoint))
            {
                SetStickerBoardLocalPosition(_selectedSticker, boardLocalPoint + _stickerDragOffsetBoardLocal);
            }

            if (pointerUp || !pointerHeld)
            {
                _isDraggingSticker = false;
            }

            return true;
        }

        if (pointerDown)
        {
            if (TryPickStickerFromPointer(out DrawingStickerLayer stickerLayer, out Vector3 stickerHitBoardLocal))
            {
                FinalizeStrokeHistory();
                _isDrawing = false;
                SelectSticker(stickerLayer);
                _stickerDragOffsetBoardLocal = stickerLayer.transform.localPosition - stickerHitBoardLocal;
                _isDraggingSticker = true;
                return true;
            }

            if (_selectedSticker != null)
            {
                _isDrawing = false;
                SelectSticker(null);
                return true;
            }
        }

        return _selectedSticker != null;
    }

    private bool TryHandleStickerMaskErasePointerInput(
        bool pointerDown,
        bool pointerHeld,
        bool pointerUp)
    {
        if (_selectedSticker == null)
        {
            _isErasingStickerMask = false;
            _useStickerMaskErase = false;
            return false;
        }

        if (_isErasingStickerMask)
        {
            if (pointerHeld)
            {
                TryEraseSelectedStickerMaskAtPointer();
            }

            if (pointerUp || !pointerHeld)
            {
                _isErasingStickerMask = false;
            }

            return true;
        }

        if (pointerDown)
        {
            if (TryPickStickerFromPointer(out DrawingStickerLayer stickerLayer, out _))
            {
                FinalizeStrokeHistory();
                _isDrawing = false;
                SelectSticker(stickerLayer);
                _isDraggingSticker = false;
                _isErasingStickerMask = TryEraseSelectedStickerMaskAtPointer();
                return true;
            }

            _isDrawing = false;
            SelectSticker(null);
            return true;
        }

        return true;
    }

    private bool TryEraseSelectedStickerMaskAtPointer()
    {
        if (_selectedSticker == null || _canvas == null || !TryGetPointerScreenPosition(out Vector2 pointerScreenPosition))
        {
            return false;
        }

        Camera activeCamera = drawingCamera != null ? drawingCamera : Camera.main;
        if (activeCamera == null)
        {
            return false;
        }

        Ray ray = activeCamera.ScreenPointToRay(pointerScreenPosition);
        if (!_selectedSticker.TryRaycast(ray, 1000f, out RaycastHit stickerHit))
        {
            return false;
        }

        return _selectedSticker.TryEraseAlphaAtWorldPoint(stickerHit.point, GetPreviewWorldRadius(stickerHit));
    }

    private void HandleStickerKeyboardShortcuts(bool pointerOverUi)
    {
        if (!enableStickerLayers || _selectedSticker == null || pointerOverUi)
        {
            return;
        }

        float scrollDelta = GetScrollDelta();
        if (!_useStickerMaskErase && Mathf.Abs(scrollDelta) >= 0.01f)
        {
            if (GetStickerRotateModifierPressed())
            {
                RotateSelectedSticker(Mathf.Sign(scrollDelta) * stickerRotationStep);
            }
            else
            {
                ScaleSelectedSticker(Mathf.Sign(scrollDelta) * stickerScaleStep);
            }
        }

        if (GetStickerFlipShortcutPressed())
        {
            _selectedSticker.FlipHorizontal();
            ClampStickerInsideBoard(_selectedSticker);
        }

        if (GetStickerOpacityIncreaseShortcutPressed())
        {
            SetSelectedStickerOpacity(_selectedSticker.Opacity + stickerOpacityStep);
        }

        if (GetStickerOpacityDecreaseShortcutPressed())
        {
            SetSelectedStickerOpacity(_selectedSticker.Opacity - stickerOpacityStep);
        }

        if (GetStickerDeleteShortcutPressed())
        {
            DeleteSelectedSticker();
        }
    }

    private bool TryPickStickerFromPointer(out DrawingStickerLayer stickerLayer, out Vector3 stickerHitBoardLocal)
    {
        stickerLayer = null;
        stickerHitBoardLocal = default;

        if (_stickerLayers.Count == 0 || !TryGetPointerScreenPosition(out Vector2 pointerScreenPosition))
        {
            return false;
        }

        Camera activeCamera = drawingCamera != null ? drawingCamera : Camera.main;
        if (activeCamera == null)
        {
            return false;
        }

        Ray ray = activeCamera.ScreenPointToRay(pointerScreenPosition);
        for (int i = _stickerLayers.Count - 1; i >= 0; i--)
        {
            DrawingStickerLayer candidate = _stickerLayers[i];
            if (candidate == null)
            {
                continue;
            }

            if (!candidate.TryRaycast(ray, 1000f, out RaycastHit stickerHit))
            {
                continue;
            }

            stickerLayer = candidate;
            stickerHitBoardLocal = transform.InverseTransformPoint(stickerHit.point);
            return true;
        }

        return false;
    }

    private bool TryGetPointerBoardLocalPoint(out Vector3 boardLocalPoint)
    {
        boardLocalPoint = default;
        if (!TryGetPointerHit(out RaycastHit hit))
        {
            return false;
        }

        boardLocalPoint = transform.InverseTransformPoint(hit.point);
        return true;
    }

    private void SetStickerBoardLocalPosition(DrawingStickerLayer stickerLayer, Vector3 boardLocalPosition)
    {
        if (stickerLayer == null)
        {
            return;
        }

        Vector3 currentPosition = stickerLayer.transform.localPosition;
        stickerLayer.transform.localPosition = new Vector3(
            boardLocalPosition.x,
            currentPosition.y,
            boardLocalPosition.z);
        ClampStickerInsideBoard(stickerLayer);
    }

    private void ScaleSelectedSticker(float scaleDelta)
    {
        if (_selectedSticker == null)
        {
            return;
        }

        Vector3 scale = _selectedSticker.transform.localScale;
        float width = Mathf.Clamp(Mathf.Abs(scale.x) * (1f + scaleDelta), minStickerScale, maxStickerScale);
        float depth = Mathf.Clamp(Mathf.Abs(scale.z) * (1f + scaleDelta), minStickerScale, maxStickerScale);
        scale.x = Mathf.Sign(scale.x == 0f ? 1f : scale.x) * width;
        scale.z = Mathf.Sign(scale.z == 0f ? 1f : scale.z) * depth;
        _selectedSticker.transform.localScale = scale;
        ClampStickerInsideBoard(_selectedSticker);
    }

    private void RotateSelectedSticker(float rotationDelta)
    {
        if (_selectedSticker == null)
        {
            return;
        }

        Vector3 euler = _selectedSticker.transform.localEulerAngles;
        euler.y += rotationDelta;
        _selectedSticker.transform.localEulerAngles = euler;
        ClampStickerInsideBoard(_selectedSticker);
    }

    private void ClampStickerInsideBoard(DrawingStickerLayer stickerLayer)
    {
        if (stickerLayer == null)
        {
            return;
        }

        Bounds boardBounds = GetBoardMeshBounds();
        Rect stickerRect = stickerLayer.GetBoardLocalRect();
        float halfWidth = stickerRect.width * 0.5f;
        float halfHeight = stickerRect.height * 0.5f;
        Vector3 position = stickerLayer.transform.localPosition;
        float xMin = boardBounds.min.x + halfWidth;
        float xMax = boardBounds.max.x - halfWidth;
        float zMin = boardBounds.min.z + halfHeight;
        float zMax = boardBounds.max.z - halfHeight;

        position.x = xMin <= xMax
            ? Mathf.Clamp(position.x, xMin, xMax)
            : boardBounds.center.x;
        position.z = zMin <= zMax
            ? Mathf.Clamp(position.z, zMin, zMax)
            : boardBounds.center.z;
        stickerLayer.transform.localPosition = position;
    }

    private void NormalizeStickerLayerDepths()
    {
        for (int i = 0; i < _stickerLayers.Count; i++)
        {
            DrawingStickerLayer stickerLayer = _stickerLayers[i];
            if (stickerLayer == null)
            {
                continue;
            }

            Vector3 position = stickerLayer.transform.localPosition;
            position.y = stickerSurfaceOffset + (i * stickerDepthStep);
            stickerLayer.transform.localPosition = position;
        }
    }

    private void PaintSketchGuideLine(Vector2Int from, Vector2Int to)
    {
        // Sketch guide drawing was removed.
    }

    private void RefreshDisplayFullCanvas()
    {
        if (_canvas == null || _displayCanvas == null)
        {
            return;
        }

        RefreshDisplayRegion(new RectInt(0, 0, _canvas.Width, _canvas.Height));
    }

    private void RefreshDisplayRegion(RectInt region)
    {
        if (_canvas == null || _displayCanvas == null || region.width <= 0 || region.height <= 0)
        {
            return;
        }

        Color32[] compositePixels = _canvas.CopyRegion(region);
        Rect paintArea = GetClampedPaintArea();
        int dividerPixelWidth = Mathf.Max(1, Mathf.RoundToInt(_canvas.Width * paintAreaDividerWidthNormalized));
        int dividerStart = Mathf.Clamp(Mathf.RoundToInt(paintArea.xMin * _canvas.Width) - dividerPixelWidth, 0, _canvas.Width);
        int dividerEnd = Mathf.Clamp(Mathf.RoundToInt(paintArea.xMin * _canvas.Width) + dividerPixelWidth, 0, _canvas.Width);
        Color32 nonPaintColor32 = nonPaintAreaDisplayColor;
        Color32 dividerColor32 = paintAreaDividerColor;

        for (int localY = 0; localY < region.height; localY++)
        {
            int absoluteY = region.y + localY;
            float canvasV = (absoluteY + 0.5f) / _canvas.Height;
            for (int localX = 0; localX < region.width; localX++)
            {
                int absoluteX = region.x + localX;
                int pixelIndex = (localY * region.width) + localX;
                float canvasU = (absoluteX + 0.5f) / _canvas.Width;

                if (!paintArea.Contains(new Vector2(canvasU, canvasV)))
                {
                    if (TrySampleOriginalSurfaceColor(canvasU, canvasV, out Color32 originalColor))
                    {
                        compositePixels[pixelIndex] = originalColor;
                    }
                    else
                    {
                        compositePixels[pixelIndex] = nonPaintColor32;
                    }

                    continue;
                }

                if (absoluteX >= dividerStart && absoluteX < dividerEnd)
                {
                    compositePixels[pixelIndex] = dividerColor32;
                }
            }
        }

        _displayCanvas.ApplyRegion(region, compositePixels);
    }

    private void InitializeBrushPreview()
    {
        if (!showBrushPreview || _brushPreviewRenderer != null)
        {
            return;
        }

        Shader previewShader = Shader.Find("Hidden/Internal-Colored");
        if (previewShader == null)
        {
            previewShader = Shader.Find("Sprites/Default");
        }

        if (previewShader == null)
        {
            previewShader = Shader.Find("Universal Render Pipeline/Unlit");
        }

        _brushPreviewMaterial = previewShader != null ? new Material(previewShader) : null;
        if (_brushPreviewMaterial != null)
        {
            _brushPreviewMaterial.name = $"{name}_BrushPreviewMaterial";
            _brushPreviewMaterial.hideFlags = RuntimeHideFlags;
            ApplyPreviewMaterialRenderSettings(_brushPreviewMaterial);
            UpdatePreviewMaterialColor(_brushPreviewMaterial, Color.black);
        }

        _brushPreviewFillMaterial = previewShader != null ? new Material(previewShader) : null;
        if (_brushPreviewFillMaterial != null)
        {
            _brushPreviewFillMaterial.name = $"{name}_BrushPreviewFillMaterial";
            _brushPreviewFillMaterial.hideFlags = RuntimeHideFlags;
            ApplyPreviewMaterialRenderSettings(_brushPreviewFillMaterial);
            UpdatePreviewMaterialColor(_brushPreviewFillMaterial, brushColor);
        }

        var previewObject = new GameObject("BrushPreview");
        previewObject.hideFlags = RuntimeHierarchyHideFlags;
        previewObject.transform.SetParent(transform, false);
        previewObject.layer = ResolveBrushPreviewLayer();
        _brushPreviewRenderer = previewObject.AddComponent<LineRenderer>();
        _brushPreviewRenderer.hideFlags = RuntimeHideFlags;
        _brushPreviewRenderer.loop = true;
        _brushPreviewRenderer.useWorldSpace = true;
        _brushPreviewRenderer.positionCount = Mathf.Max(MinBrushPreviewCircleSegments, previewSegments);
        _brushPreviewRenderer.widthMultiplier = 0.01f;
        _brushPreviewRenderer.textureMode = LineTextureMode.Stretch;
        _brushPreviewRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _brushPreviewRenderer.receiveShadows = false;
        _brushPreviewRenderer.alignment = LineAlignment.View;
        _brushPreviewRenderer.allowOcclusionWhenDynamic = false;
        _brushPreviewRenderer.sortingOrder = short.MaxValue;
        _brushPreviewRenderer.enabled = false;

        if (_brushPreviewMaterial != null)
        {
            _brushPreviewRenderer.sharedMaterial = _brushPreviewMaterial;
        }

        var fillObject = new GameObject("BrushPreviewFill");
        fillObject.hideFlags = RuntimeHierarchyHideFlags;
        fillObject.transform.SetParent(previewObject.transform, false);
        fillObject.layer = previewObject.layer;
        MeshFilter fillMeshFilter = fillObject.AddComponent<MeshFilter>();
        _brushPreviewFillRenderer = fillObject.AddComponent<MeshRenderer>();
        _brushPreviewFillRenderer.hideFlags = RuntimeHideFlags;
        _brushPreviewFillRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _brushPreviewFillRenderer.receiveShadows = false;
        _brushPreviewFillRenderer.allowOcclusionWhenDynamic = false;
        _brushPreviewFillRenderer.sortingOrder = short.MaxValue;
        _brushPreviewFillRenderer.enabled = false;

        if (_brushPreviewFillMaterial != null)
        {
            _brushPreviewFillRenderer.sharedMaterial = _brushPreviewFillMaterial;
        }

        _brushPreviewFillMesh = BuildUnitDiscMesh(Mathf.Max(MinBrushPreviewCircleSegments, previewSegments));
        _brushPreviewFillMesh.name = $"{name}_BrushPreviewFillMesh";
        _brushPreviewFillMesh.hideFlags = RuntimeHideFlags;
        fillMeshFilter.sharedMesh = _brushPreviewFillMesh;

        SyncBrushPreviewRendererSettings();
        HideBrushPreview();
    }

    private int ResolveBrushPreviewLayer()
    {
        if (boardRenderer != null)
        {
            return boardRenderer.gameObject.layer;
        }

        return gameObject.layer;
    }

    private void SyncBrushPreviewRendererSettings()
    {
        int targetLayer = ResolveBrushPreviewLayer();
        if (_brushPreviewRenderer != null && _brushPreviewRenderer.gameObject.layer != targetLayer)
        {
            _brushPreviewRenderer.gameObject.layer = targetLayer;
        }

        if (_brushPreviewFillRenderer != null && _brushPreviewFillRenderer.gameObject.layer != targetLayer)
        {
            _brushPreviewFillRenderer.gameObject.layer = targetLayer;
        }

        if (boardRenderer != null && _brushPreviewRenderer != null)
        {
            _brushPreviewRenderer.sortingLayerID = boardRenderer.sortingLayerID;
            _brushPreviewRenderer.sortingOrder = Mathf.Max(
                _brushPreviewRenderer.sortingOrder,
                boardRenderer.sortingOrder + 1000);
        }

        if (boardRenderer != null && _brushPreviewFillRenderer != null)
        {
            _brushPreviewFillRenderer.sortingLayerID = boardRenderer.sortingLayerID;
            _brushPreviewFillRenderer.sortingOrder = Mathf.Max(
                _brushPreviewFillRenderer.sortingOrder,
                boardRenderer.sortingOrder + 1000);
        }
    }

    private void UpdateBrushPreview(bool pointerOverUi)
    {
        if (!showBrushPreview || _brushPreviewRenderer == null)
        {
            HideBrushPreview();
            return;
        }

        if (_useFillTool || pointerOverUi || _isInteractionLocked || !TryGetPointerHit(out RaycastHit hit))
        {
            HideBrushPreview();
            return;
        }

        if (!TryGetSurfaceUvFromHit(hit, out Vector2 previewSurfaceUv))
        {
            HideBrushPreview();
            return;
        }

        Vector2 previewCanvasUv = SurfaceUvToCanvasUv(previewSurfaceUv);

        if (!IsCanvasUvInPaintArea(previewCanvasUv))
        {
            HideBrushPreview();
            return;
        }

        Vector3 normal = hit.normal.normalized;
        Vector3 tangent = Vector3.Cross(normal, Vector3.up);
        if (tangent.sqrMagnitude < 0.0001f)
        {
            tangent = Vector3.Cross(normal, Vector3.right);
        }

        tangent.Normalize();
        Vector3 bitangent = Vector3.Cross(normal, tangent).normalized;
        Vector3 previewAxisU;
        Vector3 previewAxisV;
        float previewRadiusU;
        float previewRadiusV;
        if (!TryGetPreviewWorldRadiiAndAxes(
                hit,
                normal,
                out previewAxisU,
                out previewAxisV,
                out previewRadiusU,
                out previewRadiusV))
        {
            float fallbackRadius = GetPreviewWorldRadius(hit);
            previewAxisU = tangent;
            previewAxisV = bitangent;
            previewRadiusU = fallbackRadius;
            previewRadiusV = fallbackRadius;
        }

        Camera activeCamera = drawingCamera != null ? drawingCamera : Camera.main;
        if (activeCamera != null)
        {
            Vector3 cameraToHit = (hit.point - activeCamera.transform.position).normalized;
            // Keep preview offset on the camera-facing side of the surface.
            if (Vector3.Dot(normal, cameraToHit) > 0f)
            {
                normal = -normal;
            }
        }

        float outlineWidth = GetPreviewOutlineWorldWidth(previewRadiusU, previewRadiusV);
        float resolvedSurfaceOffset = Mathf.Max(previewSurfaceOffset, (outlineWidth * 0.5f) + 0.001f);
        Vector3 center = hit.point + (normal * resolvedSurfaceOffset);
        int segmentCount = Mathf.Max(MinBrushPreviewCircleSegments, previewSegments);
        if (_brushPreviewRenderer.positionCount != segmentCount)
        {
            _brushPreviewRenderer.positionCount = segmentCount;
        }

        float step = Mathf.PI * 2f / segmentCount;
        for (int i = 0; i < segmentCount; i++)
        {
            float angle = i * step;
            Vector3 offset =
                (previewAxisU * Mathf.Cos(angle) * previewRadiusU) +
                (previewAxisV * Mathf.Sin(angle) * previewRadiusV);
            _brushPreviewRenderer.SetPosition(i, center + offset);
        }

        if (_useEraser)
        {
            _brushPreviewRenderer.widthMultiplier = outlineWidth;
            _brushPreviewRenderer.startColor = Color.black;
            _brushPreviewRenderer.endColor = Color.black;
            _brushPreviewRenderer.enabled = true;

            if (_brushPreviewFillRenderer != null)
            {
                _brushPreviewFillRenderer.enabled = false;
            }

            UpdatePreviewMaterialColor(_brushPreviewMaterial, Color.black);
            return;
        }

        _brushPreviewRenderer.enabled = false;
        if (_brushPreviewFillRenderer != null)
        {
            Transform fillTransform = _brushPreviewFillRenderer.transform;
            fillTransform.position = center;
            fillTransform.rotation = Quaternion.LookRotation(normal, previewAxisV);
            fillTransform.localScale = new Vector3(previewRadiusU, previewRadiusV, 1f);
            _brushPreviewFillRenderer.enabled = true;
        }

        UpdatePreviewMaterialColor(_brushPreviewFillMaterial, brushColor);
    }

    private void HideBrushPreview()
    {
        if (_brushPreviewRenderer != null)
        {
            _brushPreviewRenderer.enabled = false;
        }

        if (_brushPreviewFillRenderer != null)
        {
            _brushPreviewFillRenderer.enabled = false;
        }
    }

    private static void UpdatePreviewMaterialColor(Material material, Color color)
    {
        if (material == null)
        {
            return;
        }

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }

        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }
    }

    private static void ApplyPreviewMaterialRenderSettings(Material material)
    {
        if (material == null)
        {
            return;
        }

        // Render preview as transparent overlay and bypass depth occlusion.
        if (material.HasProperty("_SrcBlend"))
        {
            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        }

        if (material.HasProperty("_DstBlend"))
        {
            material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        }

        if (material.HasProperty("_Cull"))
        {
            material.SetInt("_Cull", (int)CullMode.Off);
        }

        if (material.HasProperty("_ZTest"))
        {
            material.SetInt("_ZTest", (int)CompareFunction.Always);
        }

        if (material.HasProperty("_ZWrite"))
        {
            material.SetInt("_ZWrite", 0);
        }

        material.renderQueue = (int)RenderQueue.Overlay + 100;
    }

    private static Mesh BuildUnitDiscMesh(int requestedSegmentCount)
    {
        int segmentCount = Mathf.Max(MinBrushPreviewCircleSegments, requestedSegmentCount);
        var mesh = new Mesh();
        var vertices = new Vector3[segmentCount + 1];
        var triangles = new int[segmentCount * 3];
        var uv = new Vector2[vertices.Length];

        vertices[0] = Vector3.zero;
        uv[0] = new Vector2(0.5f, 0.5f);

        for (int i = 0; i < segmentCount; i++)
        {
            float angle = (i / (float)segmentCount) * Mathf.PI * 2f;
            float x = Mathf.Cos(angle);
            float y = Mathf.Sin(angle);
            int vertexIndex = i + 1;
            vertices[vertexIndex] = new Vector3(x, y, 0f);
            uv[vertexIndex] = new Vector2((x + 1f) * 0.5f, (y + 1f) * 0.5f);

            int triangleIndex = i * 3;
            triangles[triangleIndex] = 0;
            triangles[triangleIndex + 1] = vertexIndex;
            triangles[triangleIndex + 2] = (i == segmentCount - 1) ? 1 : vertexIndex + 1;
        }

        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.uv = uv;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private bool TryGetPreviewWorldRadiiAndAxes(
        RaycastHit hit,
        Vector3 surfaceNormal,
        out Vector3 axisU,
        out Vector3 axisV,
        out float radiusU,
        out float radiusV)
    {
        axisU = Vector3.zero;
        axisV = Vector3.zero;
        radiusU = 0f;
        radiusV = 0f;

        if (_canvas == null || brushRadius <= 0)
        {
            return false;
        }

        BoxCollider boxCollider = hit.collider as BoxCollider ?? drawingSurfaceCollider as BoxCollider;
        if (boxCollider == null)
        {
            return false;
        }

        Vector3 axisWorldSizes = GetBoxAxisWorldSizes(boxCollider);
        if (!TryResolveBoxPaintAxes(boxCollider, axisWorldSizes, out int uAxis, out int vAxis))
        {
            return false;
        }

        float worldUSize = Mathf.Abs(GetAxis(axisWorldSizes, uAxis));
        float worldVSize = Mathf.Abs(GetAxis(axisWorldSizes, vAxis));
        if (worldUSize <= 0.0001f || worldVSize <= 0.0001f)
        {
            return false;
        }

        float scaleX = Mathf.Max(0.0001f, Mathf.Abs(boardTextureScale.x));
        float scaleY = Mathf.Max(0.0001f, Mathf.Abs(boardTextureScale.y));
        radiusU = brushRadius * (worldUSize / (_canvas.Width * scaleX));
        radiusV = brushRadius * (worldVSize / (_canvas.Height * scaleY));
        radiusU = Mathf.Max(0.0005f, radiusU);
        radiusV = Mathf.Max(0.0005f, radiusV);

        // UV mapping in TryGetSurfaceUvFromBoxColliderHit flips both axes (u = 1-u, v = 1-v).
        axisU = -GetAxisDirection(boxCollider.transform, uAxis);
        axisV = -GetAxisDirection(boxCollider.transform, vAxis);

        axisU = Vector3.ProjectOnPlane(axisU, surfaceNormal);
        axisV = Vector3.ProjectOnPlane(axisV, surfaceNormal);
        if (axisU.sqrMagnitude <= 0.0001f || axisV.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        axisU.Normalize();
        axisV = Vector3.ProjectOnPlane(axisV, axisU);
        if (axisV.sqrMagnitude <= 0.0001f)
        {
            axisV = Vector3.Cross(surfaceNormal, axisU);
        }

        if (axisV.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        axisV.Normalize();
        if (Vector3.Dot(Vector3.Cross(axisU, axisV), surfaceNormal) < 0f)
        {
            axisV = -axisV;
        }

        return true;
    }

    private float GetPreviewWorldRadius(RaycastHit hit)
    {
        if (_canvas == null || drawingSurfaceCollider == null)
        {
            return 0.01f;
        }

        float scaleX = Mathf.Max(0.0001f, Mathf.Abs(boardTextureScale.x));
        float scaleY = Mathf.Max(0.0001f, Mathf.Abs(boardTextureScale.y));
        if (drawingSurfaceCollider is BoxCollider boxCollider &&
            TryGetBoxColliderWorldSurfaceSize(boxCollider, out float worldWidth, out float worldHeight))
        {
            float radiusU = brushRadius * (worldWidth / (_canvas.Width * scaleX));
            float radiusV = brushRadius * (worldHeight / (_canvas.Height * scaleY));
            return Mathf.Max(0.001f, (radiusU + radiusV) * 0.5f);
        }

        Bounds bounds = drawingSurfaceCollider.bounds;
        float dominantExtent = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
        float dominantPixels = Mathf.Max(_canvas.Width, _canvas.Height);
        return Mathf.Max(0.001f, brushRadius * (dominantExtent / Mathf.Max(1f, dominantPixels)));
    }

    private static float GetPreviewOutlineWorldWidth(float radiusU, float radiusV)
    {
        return Mathf.Max(Mathf.Min(radiusU, radiusV) * 0.15f, 0.0015f);
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
        _brushPreviewFillRenderer = null;

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

        if (_brushPreviewFillMaterial != null)
        {
            if (Application.isPlaying)
            {
                Destroy(_brushPreviewFillMaterial);
            }
            else
            {
                DestroyImmediate(_brushPreviewFillMaterial);
            }

            _brushPreviewFillMaterial = null;
        }

        if (_brushPreviewFillMesh != null)
        {
            if (Application.isPlaying)
            {
                Destroy(_brushPreviewFillMesh);
            }
            else
            {
                DestroyImmediate(_brushPreviewFillMesh);
            }

            _brushPreviewFillMesh = null;
        }
    }

    private void SetOriginalMaterialSource(Material sourceMaterial)
    {
        if (sourceMaterial == null)
        {
            return;
        }

        _originalSharedMaterial = sourceMaterial;
        CacheOriginalSurfaceTextureData(_originalSharedMaterial);
    }

    private void ReleaseRuntimeMaterial()
    {
        if (_runtimeMaterial == null)
        {
            RestoreOriginalMaterial();
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
        if (_originalSharedMaterial == null || boardRenderer == null)
        {
            return;
        }

        boardRenderer.sharedMaterial = _originalSharedMaterial;
    }

    private bool TryResolveStickerPlacement(
        Texture2D stickerTexture,
        RectInt placementRegion,
        out Vector3 centerLocal,
        out Vector3 sizeLocal,
        out string error)
    {
        centerLocal = Vector3.zero;
        sizeLocal = Vector3.one;
        error = null;

        if (_canvas == null || stickerTexture == null)
        {
            error = "Sticker placement failed because the board canvas or sticker texture is unavailable.";
            return false;
        }

        RectInt resolvedRegion = placementRegion;
        if (resolvedRegion.width <= 0 || resolvedRegion.height <= 0)
        {
            int fallbackWidth = Mathf.Clamp(stickerTexture.width, 1, _canvas.Width);
            int fallbackHeight = Mathf.Clamp(stickerTexture.height, 1, _canvas.Height);
            resolvedRegion = new RectInt(
                Mathf.Max(0, (_canvas.Width - fallbackWidth) / 2),
                Mathf.Max(0, (_canvas.Height - fallbackHeight) / 2),
                fallbackWidth,
                fallbackHeight);
        }

        Bounds boardBounds = GetBoardMeshBounds();
        Vector2 canvasCenterUv = new(
            (resolvedRegion.x + (resolvedRegion.width * 0.5f)) / _canvas.Width,
            (resolvedRegion.y + (resolvedRegion.height * 0.5f)) / _canvas.Height);
        Vector2 surfaceCenterUv = CanvasUvToSurfaceUv(canvasCenterUv);

        centerLocal = new Vector3(
            Mathf.Lerp(boardBounds.min.x, boardBounds.max.x, surfaceCenterUv.x),
            stickerSurfaceOffset + (_stickerLayers.Count * stickerDepthStep),
            Mathf.Lerp(boardBounds.min.z, boardBounds.max.z, surfaceCenterUv.y));

        float scaleX = Mathf.Max(0.0001f, Mathf.Abs(boardTextureScale.x));
        float scaleY = Mathf.Max(0.0001f, Mathf.Abs(boardTextureScale.y));
        float widthLocal = (resolvedRegion.width / (_canvas.Width * scaleX)) * boardBounds.size.x;
        float depthLocal = (resolvedRegion.height / (_canvas.Height * scaleY)) * boardBounds.size.z;
        float aspect = stickerTexture.height > 0
            ? stickerTexture.width / (float)stickerTexture.height
            : 1f;

        if (widthLocal <= 0.0001f && depthLocal > 0.0001f)
        {
            widthLocal = depthLocal * aspect;
        }
        else if (depthLocal <= 0.0001f && widthLocal > 0.0001f)
        {
            depthLocal = Mathf.Approximately(aspect, 0f) ? widthLocal : widthLocal / aspect;
        }

        float maxDimension = Mathf.Max(widthLocal, depthLocal);
        if (maxDimension > maxStickerScale && maxDimension > 0.0001f)
        {
            float scaleFactor = maxStickerScale / maxDimension;
            widthLocal *= scaleFactor;
            depthLocal *= scaleFactor;
        }

        float minDimension = Mathf.Min(widthLocal, depthLocal);
        if (minDimension < minStickerScale && minDimension > 0.0001f)
        {
            float scaleFactor = minStickerScale / minDimension;
            widthLocal *= scaleFactor;
            depthLocal *= scaleFactor;
        }

        sizeLocal = new Vector3(
            Mathf.Max(minStickerScale, widthLocal),
            1f,
            Mathf.Max(minStickerScale, depthLocal));
        return true;
    }

    private void BlendStickerIntoPixels(
        DrawingStickerLayer stickerLayer,
        Color32[] compositePixels,
        Bounds boardBounds)
    {
        if (stickerLayer == null ||
            stickerLayer.Texture == null ||
            compositePixels == null ||
            _canvas == null ||
            stickerLayer.Opacity <= 0.001f)
        {
            return;
        }

        Color32[] stickerPixels = stickerLayer.Texture.GetPixels32();
        if (stickerPixels == null || stickerPixels.Length == 0)
        {
            return;
        }

        int stickerWidth = stickerLayer.Texture.width;
        int stickerHeight = stickerLayer.Texture.height;
        if (stickerWidth <= 0 || stickerHeight <= 0)
        {
            return;
        }

        Matrix4x4 boardLocalToStickerLocal =
            stickerLayer.transform.worldToLocalMatrix * transform.localToWorldMatrix;
        float stickerPlaneY = stickerLayer.transform.localPosition.y;

        for (int y = 0; y < _canvas.Height; y++)
        {
            float canvasV = (y + 0.5f) / _canvas.Height;
            float surfaceV = CanvasUvToSurfaceUv(new Vector2(0.5f, canvasV)).y;
            float boardLocalZ = Mathf.Lerp(boardBounds.min.z, boardBounds.max.z, surfaceV);

            for (int x = 0; x < _canvas.Width; x++)
            {
                float canvasU = (x + 0.5f) / _canvas.Width;
                float surfaceU = CanvasUvToSurfaceUv(new Vector2(canvasU, canvasV)).x;
                float boardLocalX = Mathf.Lerp(boardBounds.min.x, boardBounds.max.x, surfaceU);
                Vector3 stickerLocalPoint = boardLocalToStickerLocal.MultiplyPoint3x4(
                    new Vector3(boardLocalX, stickerPlaneY, boardLocalZ));

                float stickerU = stickerLocalPoint.x + 0.5f;
                float stickerV = stickerLocalPoint.z + 0.5f;
                if (stickerU < 0f || stickerU > 1f || stickerV < 0f || stickerV > 1f)
                {
                    continue;
                }

                Color sampledColor = SampleTextureBilinear(
                    stickerPixels,
                    stickerWidth,
                    stickerHeight,
                    stickerU,
                    stickerV);
                sampledColor.a *= stickerLayer.Opacity;
                if (sampledColor.a <= 0.001f)
                {
                    continue;
                }

                int canvasIndex = (y * _canvas.Width) + x;
                compositePixels[canvasIndex] = BlendColorOver(sampledColor, compositePixels[canvasIndex]);
            }
        }
    }

    private Bounds GetBoardMeshBounds()
    {
        if (boardRenderer != null)
        {
            MeshFilter meshFilter = boardRenderer.GetComponent<MeshFilter>();
            if (meshFilter != null && meshFilter.sharedMesh != null)
            {
                return meshFilter.sharedMesh.bounds;
            }
        }

        return new Bounds(Vector3.zero, new Vector3(10f, 0f, 10f));
    }

    private void ResolveRuntimeReferences()
    {
        if (drawingCamera == null)
        {
            drawingCamera = Camera.main ?? FindFirstObjectByType<Camera>();
        }
    }

    private void EnsureRuntimeReady()
    {
        ResolveRuntimeReferences();

        if ((_canvas == null || _displayCanvas == null) &&
            boardRenderer != null &&
            drawingSurfaceCollider != null)
        {
            InitializeCanvas();
        }

        EnsureBoardMaterialBinding();

        if (_brushPreviewRenderer == null && boardRenderer != null)
        {
            InitializeBrushPreview();
        }

        SyncBrushPreviewRendererSettings();
    }

    private void EnsureBoardMaterialBinding()
    {
        if (boardRenderer == null || _runtimeMaterial == null || _displayCanvas == null)
        {
            return;
        }

        if (boardRenderer.sharedMaterial != _runtimeMaterial)
        {
            boardRenderer.sharedMaterial = _runtimeMaterial;
        }

        if (_runtimeMaterial.mainTexture != _displayCanvas.Texture)
        {
            AssignTexture(_runtimeMaterial, _displayCanvas.Texture, texturePropertyName);
            ConfigureDisplayMaterial(_runtimeMaterial, _displayCanvas.Texture, texturePropertyName);
        }

        ConfigureBoardRendererForDisplay();
    }

    private void CacheOriginalSurfaceTextureData(Material sourceMaterial)
    {
        _hasOriginalSurfaceTextureData = false;
        _originalSurfacePixels = null;
        _originalSurfaceWidth = 0;
        _originalSurfaceHeight = 0;
        _originalSurfaceTextureScale = Vector2.one;
        _originalSurfaceTextureOffset = Vector2.zero;
        _originalSurfaceWrapMode = TextureWrapMode.Clamp;

        if (sourceMaterial == null)
        {
            return;
        }

        string targetTextureProperty = !string.IsNullOrWhiteSpace(texturePropertyName) &&
                                       sourceMaterial.HasProperty(texturePropertyName)
            ? texturePropertyName
            : null;

        Texture sourceTexture;
        if (!string.IsNullOrWhiteSpace(targetTextureProperty))
        {
            sourceTexture = sourceMaterial.GetTexture(targetTextureProperty);
            _originalSurfaceTextureScale = sourceMaterial.GetTextureScale(targetTextureProperty);
            _originalSurfaceTextureOffset = sourceMaterial.GetTextureOffset(targetTextureProperty);
        }
        else
        {
            sourceTexture = sourceMaterial.mainTexture;
            _originalSurfaceTextureScale = sourceMaterial.mainTextureScale;
            _originalSurfaceTextureOffset = sourceMaterial.mainTextureOffset;
        }

        if (Mathf.Abs(_originalSurfaceTextureScale.x) <= 0.0001f)
        {
            _originalSurfaceTextureScale.x = 1f;
        }

        if (Mathf.Abs(_originalSurfaceTextureScale.y) <= 0.0001f)
        {
            _originalSurfaceTextureScale.y = 1f;
        }

        Texture2D sourceTexture2D = sourceTexture as Texture2D;
        if (sourceTexture2D == null)
        {
            return;
        }

        _originalSurfaceWrapMode = sourceTexture2D.wrapMode;
        if (!TryExtractTexturePixels(
                sourceTexture2D,
                out _originalSurfacePixels,
                out _originalSurfaceWidth,
                out _originalSurfaceHeight))
        {
            return;
        }

        _hasOriginalSurfaceTextureData = _originalSurfacePixels != null &&
                                         _originalSurfacePixels.Length > 0 &&
                                         _originalSurfaceWidth > 0 &&
                                         _originalSurfaceHeight > 0;
    }

    private bool TrySampleOriginalSurfaceColor(float canvasU, float canvasV, out Color32 color)
    {
        color = default;
        if (!_hasOriginalSurfaceTextureData ||
            _originalSurfacePixels == null ||
            _originalSurfaceWidth <= 0 ||
            _originalSurfaceHeight <= 0)
        {
            return false;
        }

        Vector2 surfaceUv = CanvasUvToSurfaceUv(new Vector2(canvasU, canvasV));
        float sourceU = (surfaceUv.x * _originalSurfaceTextureScale.x) + _originalSurfaceTextureOffset.x;
        float sourceV = (surfaceUv.y * _originalSurfaceTextureScale.y) + _originalSurfaceTextureOffset.y;
        sourceU = WrapSampleUv(sourceU, _originalSurfaceWrapMode);
        sourceV = WrapSampleUv(sourceV, _originalSurfaceWrapMode);

        color = SampleTextureBilinear(
            _originalSurfacePixels,
            _originalSurfaceWidth,
            _originalSurfaceHeight,
            sourceU,
            sourceV);
        return true;
    }

    private static float WrapSampleUv(float value, TextureWrapMode wrapMode)
    {
        switch (wrapMode)
        {
            case TextureWrapMode.Repeat:
                return Mathf.Repeat(value, 1f);
            case TextureWrapMode.Mirror:
                return Mathf.PingPong(value, 1f);
            case TextureWrapMode.MirrorOnce:
                return Mathf.Clamp01(Mathf.PingPong(value, 2f));
            case TextureWrapMode.Clamp:
            default:
                return Mathf.Clamp01(value);
        }
    }

    private static bool TryExtractTexturePixels(
        Texture2D texture,
        out Color32[] pixels,
        out int width,
        out int height)
    {
        pixels = null;
        width = 0;
        height = 0;
        if (texture == null)
        {
            return false;
        }

        width = texture.width;
        height = texture.height;
        if (width <= 0 || height <= 0)
        {
            return false;
        }

        try
        {
            pixels = texture.GetPixels32();
            return pixels != null && pixels.Length > 0;
        }
        catch
        {
            RenderTexture temporary = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            RenderTexture previous = RenderTexture.active;
            Texture2D readable = null;

            try
            {
                Graphics.Blit(texture, temporary);
                RenderTexture.active = temporary;
                readable = new Texture2D(width, height, TextureFormat.RGBA32, false);
                readable.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, false);
                readable.Apply(false, false);
                pixels = readable.GetPixels32();
                return pixels != null && pixels.Length > 0;
            }
            catch
            {
                pixels = null;
                return false;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(temporary);
                if (readable != null)
                {
                    if (Application.isPlaying)
                    {
                        Destroy(readable);
                    }
                    else
                    {
                        DestroyImmediate(readable);
                    }
                }
            }
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

    private static RectInt ExpandRegion(RectInt region, int padding, int width, int height)
    {
        int minX = Mathf.Clamp(region.xMin - padding, 0, Mathf.Max(0, width - 1));
        int minY = Mathf.Clamp(region.yMin - padding, 0, Mathf.Max(0, height - 1));
        int maxX = Mathf.Clamp(region.xMax + padding, 0, width);
        int maxY = Mathf.Clamp(region.yMax + padding, 0, height);
        return new RectInt(minX, minY, Mathf.Max(0, maxX - minX), Mathf.Max(0, maxY - minY));
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

    private static Color32[] CopyTextureRegionPixels(
        Color32[] sourcePixels,
        int sourceWidth,
        RectInt region)
    {
        if (sourcePixels == null || sourceWidth <= 0 || region.width <= 0 || region.height <= 0)
        {
            return Array.Empty<Color32>();
        }

        Color32[] copy = new Color32[region.width * region.height];
        for (int y = 0; y < region.height; y++)
        {
            int sourceY = region.y + y;
            int sourceIndex = (sourceY * sourceWidth) + region.x;
            int destinationIndex = y * region.width;
            Array.Copy(sourcePixels, sourceIndex, copy, destinationIndex, region.width);
        }

        return copy;
    }

    private void AssignTexture(Material material, Texture2D texture, string texturePropertyName)
    {
        if (material == null)
        {
            return;
        }

        material.mainTexture = texture;
        material.mainTextureScale = boardTextureScale;
        material.mainTextureOffset = boardTextureOffset;

        if (!string.IsNullOrWhiteSpace(texturePropertyName) && material.HasProperty(texturePropertyName))
        {
            material.SetTexture(texturePropertyName, texture);
            material.SetTextureScale(texturePropertyName, boardTextureScale);
            material.SetTextureOffset(texturePropertyName, boardTextureOffset);
        }
    }

    private void ConfigureDisplayMaterial(Material material, Texture2D texture, string texturePropertyName)
    {
        if (material == null || texture == null)
        {
            return;
        }

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", Color.white);
        }

        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", Color.white);
        }

        if (material.HasProperty("_Metallic"))
        {
            material.SetFloat("_Metallic", 0f);
        }

        if (material.HasProperty("_Smoothness"))
        {
            material.SetFloat("_Smoothness", 0f);
        }

        if (material.HasProperty("_EmissionMap"))
        {
            material.SetTexture("_EmissionMap", texture);
            material.SetTextureScale("_EmissionMap", boardTextureScale);
            material.SetTextureOffset("_EmissionMap", boardTextureOffset);
            material.EnableKeyword("_EMISSION");
        }

        if (material.HasProperty("_EmissionColor"))
        {
            material.SetColor("_EmissionColor", Color.white * 2.2f);
        }

        if (!string.IsNullOrWhiteSpace(texturePropertyName) && material.HasProperty(texturePropertyName))
        {
            material.SetTexture(texturePropertyName, texture);
            material.SetTextureScale(texturePropertyName, boardTextureScale);
            material.SetTextureOffset(texturePropertyName, boardTextureOffset);
        }
    }

    private void ConfigureBoardRendererForDisplay()
    {
        if (boardRenderer == null)
        {
            return;
        }

        boardRenderer.shadowCastingMode = ShadowCastingMode.Off;
        boardRenderer.receiveShadows = false;
        boardRenderer.lightProbeUsage = LightProbeUsage.Off;
        boardRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
    }

    private Vector2 SurfaceUvToCanvasUv(Vector2 surfaceUv)
    {
        return new Vector2(
            (surfaceUv.x * boardTextureScale.x) + boardTextureOffset.x,
            (surfaceUv.y * boardTextureScale.y) + boardTextureOffset.y);
    }

    private Vector2 CanvasUvToSurfaceUv(Vector2 canvasUv)
    {
        float scaleX = Mathf.Abs(boardTextureScale.x) > 0.0001f ? boardTextureScale.x : 1f;
        float scaleY = Mathf.Abs(boardTextureScale.y) > 0.0001f ? boardTextureScale.y : 1f;
        return new Vector2(
            (canvasUv.x - boardTextureOffset.x) / scaleX,
            (canvasUv.y - boardTextureOffset.y) / scaleY);
    }

    private static Color SampleTextureBilinear(
        Color32[] pixels,
        int width,
        int height,
        float u,
        float v)
    {
        if (pixels == null || pixels.Length == 0 || width <= 0 || height <= 0)
        {
            return Color.clear;
        }

        float x = Mathf.Clamp01(u) * (width - 1);
        float y = Mathf.Clamp01(v) * (height - 1);
        int x0 = Mathf.FloorToInt(x);
        int y0 = Mathf.FloorToInt(y);
        int x1 = Mathf.Min(x0 + 1, width - 1);
        int y1 = Mathf.Min(y0 + 1, height - 1);
        float tx = x - x0;
        float ty = y - y0;

        Color c00 = pixels[(y0 * width) + x0];
        Color c10 = pixels[(y0 * width) + x1];
        Color c01 = pixels[(y1 * width) + x0];
        Color c11 = pixels[(y1 * width) + x1];
        Color bottom = Color.Lerp(c00, c10, tx);
        Color top = Color.Lerp(c01, c11, tx);
        return Color.Lerp(bottom, top, ty);
    }

    private static Color32 BlendColorOver(Color source, Color32 destination)
    {
        float srcA = Mathf.Clamp01(source.a);
        if (srcA <= 0f)
        {
            return destination;
        }

        Color dst = destination;
        float dstA = Mathf.Clamp01(dst.a);
        float outA = srcA + (dstA * (1f - srcA));
        if (outA <= 0.0001f)
        {
            return new Color32(0, 0, 0, 0);
        }

        float outR = ((source.r * srcA) + (dst.r * dstA * (1f - srcA))) / outA;
        float outG = ((source.g * srcA) + (dst.g * dstA * (1f - srcA))) / outA;
        float outB = ((source.b * srcA) + (dst.b * dstA * (1f - srcA))) / outA;
        return (Color32)new Color(outR, outG, outB, outA);
    }

    private static bool IsDrawingPhaseActive()
    {
        RoundManager manager = RoundManager.Instance;
        return manager == null || manager.CurrentState == GameState.Drawing;
    }

    private bool TryGetPointerScreenPosition(out Vector2 screenPosition)
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null)
        {
            screenPosition = Mouse.current.position.ReadValue();
        }
        else
        {
            screenPosition = default;
            return false;
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        if (
#if ENABLE_INPUT_SYSTEM
            Mouse.current == null &&
#endif
            true)
        {
            screenPosition = Input.mousePosition;
        }
#else
        if (
#if ENABLE_INPUT_SYSTEM
            Mouse.current == null
#else
            true
#endif
            )
        {
            screenPosition = default;
            return false;
        }
#endif

        if (float.IsNaN(screenPosition.x) || float.IsNaN(screenPosition.y))
        {
            return false;
        }

        Camera activeCamera = drawingCamera != null ? drawingCamera : Camera.main;
        Rect pixelRect = activeCamera != null && activeCamera.pixelRect.width > 0f && activeCamera.pixelRect.height > 0f
            ? activeCamera.pixelRect
            : new Rect(0f, 0f, Mathf.Max(1f, Screen.width), Mathf.Max(1f, Screen.height));

        if (pixelRect.width <= 0f || pixelRect.height <= 0f)
        {
            return false;
        }

        return screenPosition.x >= pixelRect.xMin &&
               screenPosition.x <= pixelRect.xMax &&
               screenPosition.y >= pixelRect.yMin &&
               screenPosition.y <= pixelRect.yMax;
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
            bool shiftPressed = Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed;
#if UNITY_EDITOR
            if (Application.isEditor)
            {
                return !controlPressed && !shiftPressed && Keyboard.current.zKey.wasPressedThisFrame;
            }
#endif
            if (controlPressed && Keyboard.current.zKey.wasPressedThisFrame)
            {
                return true;
            }
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        bool controlPressed = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        bool shiftPressed = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
#if UNITY_EDITOR
        if (Application.isEditor)
        {
            return !controlPressed && !shiftPressed && Input.GetKeyDown(KeyCode.Z);
        }
#endif
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
#if UNITY_EDITOR
            if (Application.isEditor)
            {
                if (!controlPressed && Keyboard.current.yKey.wasPressedThisFrame)
                {
                    return true;
                }

                if (!controlPressed && shiftPressed && Keyboard.current.zKey.wasPressedThisFrame)
                {
                    return true;
                }

                return false;
            }
#endif
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
#if UNITY_EDITOR
        if (Application.isEditor)
        {
            return (!controlPressed && Input.GetKeyDown(KeyCode.Y)) ||
                   (!controlPressed && shiftPressed && Input.GetKeyDown(KeyCode.Z));
        }
#endif
        return (controlPressed && Input.GetKeyDown(KeyCode.Y)) ||
               (controlPressed && shiftPressed && Input.GetKeyDown(KeyCode.Z));
#else
        return false;
#endif
    }

    private static bool GetStickerRotateModifierPressed()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null &&
            (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed))
        {
            return true;
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
#else
        return false;
#endif
    }

    private static bool GetStickerFlipShortcutPressed()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame)
        {
            return true;
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKeyDown(KeyCode.F);
#else
        return false;
#endif
    }

    private static bool GetStickerDeleteShortcutPressed()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null &&
            (Keyboard.current.deleteKey.wasPressedThisFrame || Keyboard.current.backspaceKey.wasPressedThisFrame))
        {
            return true;
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKeyDown(KeyCode.Delete) || Input.GetKeyDown(KeyCode.Backspace);
#else
        return false;
#endif
    }

    private static bool GetStickerOpacityIncreaseShortcutPressed()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.rightBracketKey.wasPressedThisFrame)
        {
            return true;
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKeyDown(KeyCode.RightBracket);
#else
        return false;
#endif
    }

    private static bool GetStickerOpacityDecreaseShortcutPressed()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.leftBracketKey.wasPressedThisFrame)
        {
            return true;
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKeyDown(KeyCode.LeftBracket);
#else
        return false;
#endif
    }
}
