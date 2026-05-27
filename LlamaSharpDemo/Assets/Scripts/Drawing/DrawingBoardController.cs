using UnityEngine;
using System;
using DoodleDiplomacy.Core;
using DoodleDiplomacy.Gameplay;
using TMPro;

public enum DrawingToolMode
{
    Brush = 0,
    Eraser = 1,
    Fill = 2
}

/// <summary>
/// Receives pointer input on a collider-backed drawing surface and paints into a runtime texture.
/// </summary>
public class DrawingBoardController : MonoBehaviour
{
    private const HideFlags RuntimeHideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;

    [Header("Board")]
    [Tooltip("Renderer that displays the runtime drawing texture on the tablet/board surface.")]
    [SerializeField] private Renderer boardRenderer;
    [Tooltip("Collider used to raycast pointer input onto the drawable surface.")]
    [SerializeField] private Collider drawingSurfaceCollider;
    [Tooltip("Camera used to convert pointer screen position into drawing-surface rays.")]
    [SerializeField] private Camera drawingCamera;
    [Tooltip("Base canvas texture width before optional board-aspect matching.")]
    [SerializeField] private int textureWidth = 512;
    [Tooltip("Base canvas texture height before optional board-aspect matching.")]
    [SerializeField] private int textureHeight = 512;
    [Tooltip("Resize the runtime canvas to match the physical drawing board aspect ratio.")]
    [SerializeField] private bool autoMatchCanvasResolutionToBoardAspect = true;
    [Tooltip("Filter mode applied to runtime drawing textures.")]
    [SerializeField] private FilterMode filterMode = FilterMode.Bilinear;
    [Tooltip("Material texture property used for the drawing texture. URP usually uses _BaseMap.")]
    [SerializeField] private string texturePropertyName = "_BaseMap";
    [Tooltip("Optional material template used to create the runtime drawing material.")]
    [SerializeField] private Material boardMaterialTemplate;
    [Tooltip("Scale applied when mapping the runtime canvas texture onto the board material.")]
    [SerializeField] private Vector2 boardTextureScale = new(-1f, -1f);
    [Tooltip("Offset applied when mapping the runtime canvas texture onto the board material.")]
    [SerializeField] private Vector2 boardTextureOffset = new(1f, 1f);

    [Header("Export")]
    [Tooltip("Flip exported drawing pixels horizontally before sending to AI or monitors.")]
    [SerializeField] private bool flipExportHorizontally;
    [Tooltip("Flip exported drawing pixels vertically before sending to AI or monitors.")]
    [SerializeField] private bool flipExportVertically;

    [Header("Brush")]
    [Tooltip("Color used when clearing the canvas and when erasing.")]
    [SerializeField] private Color backgroundColor = Color.white;
    [Tooltip("Display color for the non-paintable area outside Normalized Paint Area.")]
    [SerializeField] private Color nonPaintAreaDisplayColor = new(0.88f, 0.90f, 0.94f, 1f);
    [Tooltip("Display color for the divider line around the paintable area.")]
    [SerializeField] private Color paintAreaDividerColor = new(0.73f, 0.77f, 0.84f, 1f);
    [Tooltip("Divider width around the paintable area, expressed as a fraction of canvas size.")]
    [SerializeField] [Range(0f, 0.02f)] private float paintAreaDividerWidthNormalized = 0.003f;
    [Tooltip("Current brush color used by the brush and fill tools.")]
    [SerializeField] private Color brushColor = Color.black;
    [Tooltip("Current brush radius in canvas pixels.")]
    [SerializeField] private int brushRadius = 6;
    [Tooltip("Minimum brush radius selectable by UI or scripts.")]
    [SerializeField] private int minBrushRadius = 1;
    [Tooltip("Maximum brush radius selectable by UI or scripts.")]
    [SerializeField] private int maxBrushRadius = 24;
    [Tooltip("Prevent drawing when the pointer is over Unity UI.")]
    [SerializeField] private bool blockPointerWhenOverUi = true;
    [Tooltip("Paintable area inside the canvas in normalized coordinates: x, y, width, height.")]
    [SerializeField] private Rect normalizedPaintArea = new(0.40f, 0.02f, 0.58f, 0.96f);

    [Header("Preview")]
    [Tooltip("Show a brush-size preview on the drawing surface while hovering.")]
    [SerializeField] private bool showBrushPreview = true;
    [Tooltip("Renderer helper used to draw the brush or eraser preview on the board surface.")]
    [SerializeField] private DrawingBrushPreview brushPreview;
    [Tooltip("World-space offset that lifts the preview slightly off the drawing surface to avoid z-fighting.")]
    [SerializeField] private float previewSurfaceOffset = 0.01f;
    [Tooltip("Number of segments used for the circular preview mesh.")]
    [SerializeField] private int previewSegments = 48;
    [Tooltip("Color used for the filled brush preview.")]
    [SerializeField] private Color previewBrushColor = new(0f, 0f, 0f, 0.9f);
    [Tooltip("Color reserved for eraser preview styling.")]
    [SerializeField] private Color previewEraserColor = new(0.15f, 0.55f, 1f, 0.95f);

    [Header("Recognition Label")]
    [Tooltip("Show a small recognized-object label on the tablet screen during confirmation.")]
    [SerializeField] private bool recognitionLabelEnabled = true;
    [Tooltip("Pre-placed TextMeshPro label on the tablet surface. Assign a prefab or scene object here to tune position, rotation, size, font, and color directly in the Inspector.")]
    [SerializeField] private TextMeshPro recognitionLabelText;
    [Tooltip("Create a fallback label at runtime if Recognition Label Text is not assigned.")]
    [SerializeField] private bool autoCreateRecognitionLabelIfMissing;
    [Tooltip("Text color used only by the runtime fallback recognition label.")]
    [SerializeField] private Color recognitionLabelColor = new(0.02f, 0.04f, 0.05f, 0.82f);
    [Tooltip("Fallback recognition label width as a fraction of the tablet screen width.")]
    [SerializeField] [Range(0.05f, 0.8f)] private float recognitionLabelWidthNormalized = 0.32f;
    [Tooltip("Fallback recognition label text height as a fraction of the tablet screen height.")]
    [SerializeField] [Range(0.01f, 0.15f)] private float recognitionLabelHeightNormalized = 0.045f;
    [Tooltip("Bottom-right inset for the fallback recognition label, in normalized tablet screen units.")]
    [SerializeField] private Vector2 recognitionLabelInsetNormalized = new(0.025f, 0.025f);
    [Tooltip("World-space offset that lifts the fallback recognition label slightly off the tablet screen.")]
    [SerializeField] private float recognitionLabelSurfaceOffset = 0.012f;

    [Header("History")]
    [Tooltip("Maximum undo history entries retained for drawing edits.")]
    [SerializeField] private int maxHistoryEntries = 24;

    private DrawingCanvas _canvas;
    private DrawingCanvas _displayCanvas;
    private DrawingCanvas _exportCanvas;
    private DrawingHistory _history;
    private Material _runtimeMaterial;
    private Material _originalSharedMaterial;
    private readonly DrawingSurfaceTextureSampler _surfaceTextureSampler = new();
    private bool _isDrawing;
    private bool _useEraser;
    private bool _useFillTool;
    private bool _isInteractionLocked;
    private bool _brushPreviewConfigured;
    private bool _missingBrushPreviewLogged;
    private bool _missingDrawingCameraLogged;
    private Vector2Int _lastPixel;
    private readonly DrawingStrokeHistory _strokeHistory = new();
    private TextMeshPro _runtimeRecognitionLabelText;

    public event Action<int> BrushRadiusChanged;
    public event Action<bool, bool> HistoryStateChanged;

    public Texture2D CanvasTexture => _canvas?.Texture;
    public Texture2D DisplayTexture => _displayCanvas?.Texture;
    public Material RuntimeBoardMaterial => _runtimeMaterial;
    public bool HasCanvasMarks => _canvas != null && _canvas.TryGetNonBackgroundBounds(out _);
    public int BrushRadius => brushRadius;
    public bool IsEraserEnabled => _useEraser;
    public bool IsFillToolEnabled => _useFillTool;
    public bool IsInteractionLocked => _isInteractionLocked;
    public Color BrushColor => brushColor;
    public Color BackgroundColor => backgroundColor;
    public Color ActiveDrawColor => GetActiveDrawColor();
    public bool CanUndo => _history != null && _history.CanUndo;
    public bool CanRedo => _history != null && _history.CanRedo;

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
        OrientPixelsForSurfaceExport(compositePixels, _canvas.Width, _canvas.Height);

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
        _isDrawing = false;
    }

    public void SetToolMode(DrawingToolMode mode)
    {
        FinalizeStrokeHistory();
        _isDrawing = false;
        _useEraser = mode == DrawingToolMode.Eraser;
        _useFillTool = mode == DrawingToolMode.Fill;
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
            FinalizeStrokeHistory();
            HideBrushPreview();
        }
    }

    public void ShowRecognitionLabel(string label)
    {
        if (!recognitionLabelEnabled || string.IsNullOrWhiteSpace(label))
        {
            ClearRecognitionLabel();
            return;
        }

        EnsureRuntimeReady();
        TextMeshPro labelText = GetRecognitionLabelText(createIfMissing: true);
        if (labelText == null)
        {
            return;
        }

        labelText.text = label.Trim();
        labelText.gameObject.SetActive(true);
        if (labelText == _runtimeRecognitionLabelText)
        {
            labelText.color = recognitionLabelColor;
            PositionRuntimeRecognitionLabel();
        }
    }

    public void ClearRecognitionLabel()
    {
        TextMeshPro labelText = GetRecognitionLabelText(createIfMissing: false);
        if (labelText != null)
        {
            labelText.gameObject.SetActive(false);
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
        CleanupRecognitionLabel();
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

        _runtimeMaterial = DrawingBoardMaterialBinding.CreateRuntimeMaterial(
            _originalSharedMaterial,
            name,
            _displayCanvas.Texture,
            texturePropertyName,
            boardTextureScale,
            boardTextureOffset,
            RuntimeHideFlags);
        DrawingBoardMaterialBinding.EnsureBinding(
            boardRenderer,
            _runtimeMaterial,
            _displayCanvas.Texture,
            texturePropertyName,
            boardTextureScale,
            boardTextureOffset);
        NotifyHistoryStateChanged();
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
        return DrawingInputReader.IsPointerOverUi();
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

        if (drawingCamera == null)
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

    private bool TryGetSurfaceUvFromHit(RaycastHit hit, out Vector2 surfaceUv)
    {
        return DrawingSurfaceMapper.TryGetSurfaceUvFromHit(hit, drawingSurfaceCollider, out surfaceUv);
    }

    private static bool TryGetSurfaceUvFromBoxColliderHit(
        Vector3 worldPoint,
        BoxCollider boxCollider,
        out Vector2 surfaceUv)
    {
        return DrawingSurfaceMapper.TryGetSurfaceUvFromBoxColliderHit(worldPoint, boxCollider, out surfaceUv);
    }

    private static bool TryResolveBoxPaintAxes(
        BoxCollider boxCollider,
        Vector3 axisWorldSizes,
        out int uAxis,
        out int vAxis)
    {
        return DrawingSurfaceMapper.TryResolveBoxPaintAxes(boxCollider, axisWorldSizes, out uAxis, out vAxis);
    }

    private static Vector3 GetBoxAxisWorldSizes(BoxCollider boxCollider)
    {
        return DrawingSurfaceMapper.GetBoxAxisWorldSizes(boxCollider);
    }

    private static float GetAxis(Vector3 value, int axis)
    {
        return DrawingSurfaceMapper.GetAxis(value, axis);
    }

    private static Vector3 GetAxisDirection(Transform targetTransform, int axis)
    {
        return DrawingSurfaceMapper.GetAxisDirection(targetTransform, axis);
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
        return DrawingSurfaceMapper.TryGetBoxColliderWorldSurfaceSize(boxCollider, out worldWidth, out worldHeight);
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
        _strokeHistory.Begin(_canvas);
    }

    private void FinalizeStrokeHistory()
    {
        if (_history == null ||
            !_strokeHistory.TryFinalize(
                _canvas,
                out RectInt region,
                out Color32[] beforePixels,
                out Color32[] afterPixels))
        {
            return;
        }

        RecordHistory(region, beforePixels, afterPixels);
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
        _strokeHistory.CaptureSegmentBeforeChange(_canvas, from, to, brushRadius);
    }

    private void ResetStrokeHistory()
    {
        _strokeHistory.Reset();
    }

    private void NotifyHistoryStateChanged()
    {
        HistoryStateChanged?.Invoke(CanUndo, CanRedo);
    }

    private void OrientPixelsForSurfaceExport(Color32[] pixels, int width, int height)
    {
        if (flipExportHorizontally)
        {
            FlipPixelsHorizontally(pixels, width, height);
        }

        if (flipExportVertically)
        {
            FlipPixelsVertically(pixels, width, height);
        }
    }

    private static void FlipPixelsHorizontally(Color32[] pixels, int width, int height)
    {
        if (pixels == null || width <= 1 || height <= 0)
        {
            return;
        }

        int halfColumns = width / 2;
        for (int y = 0; y < height; y++)
        {
            int rowOffset = y * width;
            for (int x = 0; x < halfColumns; x++)
            {
                int leftIndex = rowOffset + x;
                int rightIndex = rowOffset + (width - 1 - x);
                (pixels[leftIndex], pixels[rightIndex]) = (pixels[rightIndex], pixels[leftIndex]);
            }
        }
    }

    private static void FlipPixelsVertically(Color32[] pixels, int width, int height)
    {
        if (pixels == null || width <= 0 || height <= 1)
        {
            return;
        }

        int halfRows = height / 2;
        for (int y = 0; y < halfRows; y++)
        {
            int oppositeY = height - 1 - y;
            int topOffset = y * width;
            int bottomOffset = oppositeY * width;
            for (int x = 0; x < width; x++)
            {
                int topIndex = topOffset + x;
                int bottomIndex = bottomOffset + x;
                (pixels[topIndex], pixels[bottomIndex]) = (pixels[bottomIndex], pixels[topIndex]);
            }
        }
    }

    private void RefreshDisplayFullCanvas()
    {
        DrawingDisplayComposer.RefreshFullCanvas(
            _canvas,
            _displayCanvas,
            normalizedPaintArea,
            paintAreaDividerWidthNormalized,
            nonPaintAreaDisplayColor,
            paintAreaDividerColor,
            _surfaceTextureSampler);
    }

    private void RefreshDisplayRegion(RectInt region)
    {
        DrawingDisplayComposer.RefreshRegion(
            _canvas,
            _displayCanvas,
            region,
            normalizedPaintArea,
            paintAreaDividerWidthNormalized,
            nonPaintAreaDisplayColor,
            paintAreaDividerColor,
            _surfaceTextureSampler);
    }

    private void InitializeBrushPreview()
    {
        if (!showBrushPreview)
        {
            return;
        }

        if (_brushPreviewConfigured)
        {
            return;
        }

        if (brushPreview == null)
        {
            if (!_missingBrushPreviewLogged)
            {
                Debug.LogError("[DrawingBoardController] Brush preview must be assigned in the Inspector.", this);
                _missingBrushPreviewLogged = true;
            }

            return;
        }

        brushPreview.ConfigureFromBoardRenderer(boardRenderer, previewSegments);
        _brushPreviewConfigured = brushPreview.HasRequiredReferences;
    }

    private void SyncBrushPreviewRendererSettings()
    {
        // Renderer references are configured once from Inspector-assigned scene objects.
    }

    private void UpdateBrushPreview(bool pointerOverUi)
    {
        if (!showBrushPreview || brushPreview == null || !brushPreview.HasRequiredReferences)
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

        if (drawingCamera != null)
        {
            Vector3 cameraToHit = (hit.point - drawingCamera.transform.position).normalized;
            // Keep preview offset on the camera-facing side of the surface.
            if (Vector3.Dot(normal, cameraToHit) > 0f)
            {
                normal = -normal;
            }
        }

        float outlineWidth = GetPreviewOutlineWorldWidth(previewRadiusU, previewRadiusV);
        float resolvedSurfaceOffset = Mathf.Max(previewSurfaceOffset, (outlineWidth * 0.5f) + 0.001f);
        Vector3 center = hit.point + (normal * resolvedSurfaceOffset);
        int segmentCount = previewSegments;
        if (_useEraser)
        {
            brushPreview.ShowOutline(
                center,
                previewAxisU,
                previewAxisV,
                previewRadiusU,
                previewRadiusV,
                outlineWidth,
                segmentCount,
                Color.black);
            return;
        }

        brushPreview.ShowFill(center, normal, previewAxisV, previewRadiusU, previewRadiusV, previewBrushColor);
    }

    private void HideBrushPreview()
    {
        brushPreview?.Hide();
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
        brushPreview?.Hide();
    }

    private void SetOriginalMaterialSource(Material sourceMaterial)
    {
        if (sourceMaterial == null)
        {
            return;
        }

        _originalSharedMaterial = sourceMaterial;
        _surfaceTextureSampler.Configure(
            _originalSharedMaterial,
            texturePropertyName,
            boardTextureScale,
            boardTextureOffset);
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
            if (!_missingDrawingCameraLogged)
            {
                Debug.LogError("[DrawingBoardController] Drawing camera must be assigned in the Inspector.", this);
                _missingDrawingCameraLogged = true;
            }
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

        if (boardRenderer != null)
        {
            InitializeBrushPreview();
        }

        SyncBrushPreviewRendererSettings();
        PositionRuntimeRecognitionLabel();
    }

    private TextMeshPro GetRecognitionLabelText(bool createIfMissing)
    {
        if (recognitionLabelText != null)
        {
            return recognitionLabelText;
        }

        if (_runtimeRecognitionLabelText != null)
        {
            return _runtimeRecognitionLabelText;
        }

        if (createIfMissing && autoCreateRecognitionLabelIfMissing)
        {
            EnsureRuntimeRecognitionLabel();
        }

        return _runtimeRecognitionLabelText;
    }

    private void EnsureRuntimeRecognitionLabel()
    {
        if (_runtimeRecognitionLabelText != null)
        {
            return;
        }

        Transform parent = drawingSurfaceCollider != null ? drawingSurfaceCollider.transform : transform;
        var labelObject = new GameObject("RecognitionLabel", typeof(RectTransform), typeof(TextMeshPro))
        {
            hideFlags = RuntimeHideFlags
        };
        labelObject.transform.SetParent(parent, false);
        labelObject.layer = boardRenderer != null ? boardRenderer.gameObject.layer : gameObject.layer;

        _runtimeRecognitionLabelText = labelObject.GetComponent<TextMeshPro>();
        _runtimeRecognitionLabelText.alignment = TextAlignmentOptions.BottomRight;
        _runtimeRecognitionLabelText.enableWordWrapping = false;
        _runtimeRecognitionLabelText.overflowMode = TextOverflowModes.Ellipsis;
        _runtimeRecognitionLabelText.fontStyle = FontStyles.Bold;
        _runtimeRecognitionLabelText.richText = false;
        _runtimeRecognitionLabelText.color = recognitionLabelColor;
        _runtimeRecognitionLabelText.gameObject.SetActive(false);
    }

    private void PositionRuntimeRecognitionLabel()
    {
        if (_runtimeRecognitionLabelText == null || !_runtimeRecognitionLabelText.gameObject.activeSelf)
        {
            return;
        }

        if (!TryGetRecognitionLabelPlacement(
                out Vector3 position,
                out Quaternion rotation,
                out Vector2 rectSize,
                out float fontSize))
        {
            return;
        }

        RectTransform rectTransform = _runtimeRecognitionLabelText.rectTransform;
        rectTransform.pivot = new Vector2(1f, 0f);
        rectTransform.sizeDelta = rectSize;
        rectTransform.SetPositionAndRotation(position, rotation);
        _runtimeRecognitionLabelText.fontSize = fontSize;
    }

    private bool TryGetRecognitionLabelPlacement(
        out Vector3 position,
        out Quaternion rotation,
        out Vector2 rectSize,
        out float fontSize)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;
        rectSize = Vector2.zero;
        fontSize = 0f;

        if (drawingSurfaceCollider is not BoxCollider boxCollider)
        {
            return false;
        }

        Vector3 axisWorldSizes = GetBoxAxisWorldSizes(boxCollider);
        if (!TryResolveBoxPaintAxes(boxCollider, axisWorldSizes, out int uAxis, out int vAxis))
        {
            return false;
        }

        int normalAxis = GetRemainingAxis(uAxis, vAxis);
        Vector3 boxSize = boxCollider.size;
        float halfU = Mathf.Abs(GetAxis(boxSize, uAxis)) * 0.5f;
        float halfV = Mathf.Abs(GetAxis(boxSize, vAxis)) * 0.5f;
        float halfNormal = Mathf.Abs(GetAxis(boxSize, normalAxis)) * 0.5f;
        if (halfU <= 0.0001f || halfV <= 0.0001f)
        {
            return false;
        }

        float insetX = Mathf.Clamp01(recognitionLabelInsetNormalized.x);
        float insetY = Mathf.Clamp01(recognitionLabelInsetNormalized.y);
        float canvasU = 1f - insetX;
        float canvasV = insetY;
        float surfaceU = CanvasAxisToSurfaceUv(canvasU, boardTextureScale.x, boardTextureOffset.x);
        float surfaceV = CanvasAxisToSurfaceUv(canvasV, boardTextureScale.y, boardTextureOffset.y);

        Vector3 localPoint = boxCollider.center;
        SetAxis(ref localPoint, uAxis, Mathf.Lerp(-halfU, halfU, 1f - surfaceU));
        SetAxis(ref localPoint, vAxis, Mathf.Lerp(-halfV, halfV, 1f - surfaceV));

        Vector3 positiveNormal = GetAxisDirection(boxCollider.transform, normalAxis);
        Vector3 boxCenterWorld = boxCollider.transform.TransformPoint(boxCollider.center);
        Vector3 viewDirection = GetRecognitionLabelViewDirection(boxCenterWorld);
        float normalSign = Vector3.Dot(positiveNormal, viewDirection) >= 0f ? 1f : -1f;
        SetAxis(ref localPoint, normalAxis, normalSign * halfNormal);

        Vector3 normal = positiveNormal * normalSign;
        Vector3 canvasUp = GetCanvasAxisWorldDirection(boxCollider.transform, vAxis, boardTextureScale.y);
        canvasUp = Vector3.ProjectOnPlane(canvasUp, normal);
        if (canvasUp.sqrMagnitude <= 0.0001f)
        {
            canvasUp = Vector3.ProjectOnPlane(transform.up, normal);
        }

        if (canvasUp.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        canvasUp.Normalize();
        float worldWidth = Mathf.Abs(GetAxis(axisWorldSizes, uAxis)) / Mathf.Max(0.0001f, Mathf.Abs(boardTextureScale.x));
        float worldHeight = Mathf.Abs(GetAxis(axisWorldSizes, vAxis)) / Mathf.Max(0.0001f, Mathf.Abs(boardTextureScale.y));
        fontSize = Mathf.Max(0.01f, worldHeight * recognitionLabelHeightNormalized);
        rectSize = new Vector2(
            Mathf.Max(fontSize * 2f, worldWidth * recognitionLabelWidthNormalized),
            Mathf.Max(fontSize * 1.4f, fontSize));

        position = boxCollider.transform.TransformPoint(localPoint) +
                   normal * Mathf.Max(0.0005f, recognitionLabelSurfaceOffset);
        rotation = Quaternion.LookRotation(-normal, canvasUp);
        return true;
    }

    private Vector3 GetRecognitionLabelViewDirection(Vector3 surfaceCenterWorld)
    {
        if (drawingCamera != null)
        {
            return drawingCamera.transform.position - surfaceCenterWorld;
        }

        UnityEngine.Camera mainCamera = UnityEngine.Camera.main;
        if (mainCamera != null)
        {
            return mainCamera.transform.position - surfaceCenterWorld;
        }

        return transform.forward;
    }

    private static float CanvasAxisToSurfaceUv(float canvasAxis, float textureScale, float textureOffset)
    {
        if (Mathf.Abs(textureScale) <= 0.0001f)
        {
            return Mathf.Clamp01(canvasAxis);
        }

        return Mathf.Clamp01((canvasAxis - textureOffset) / textureScale);
    }

    private static Vector3 GetCanvasAxisWorldDirection(Transform targetTransform, int axis, float textureScale)
    {
        float scaleSign = textureScale < 0f ? -1f : 1f;
        return -GetAxisDirection(targetTransform, axis) * scaleSign;
    }

    private static int GetRemainingAxis(int firstAxis, int secondAxis)
    {
        for (int axis = 0; axis < 3; axis++)
        {
            if (axis != firstAxis && axis != secondAxis)
            {
                return axis;
            }
        }

        return 1;
    }

    private static void SetAxis(ref Vector3 value, int axis, float axisValue)
    {
        switch (axis)
        {
            case 0:
                value.x = axisValue;
                break;
            case 1:
                value.y = axisValue;
                break;
            default:
                value.z = axisValue;
                break;
        }
    }

    private void CleanupRecognitionLabel()
    {
        if (_runtimeRecognitionLabelText == null)
        {
            return;
        }

        GameObject labelObject = _runtimeRecognitionLabelText.gameObject;
        _runtimeRecognitionLabelText = null;
        if (Application.isPlaying)
        {
            Destroy(labelObject);
        }
        else
        {
            DestroyImmediate(labelObject);
        }
    }

    private void EnsureBoardMaterialBinding()
    {
        if (boardRenderer == null || _runtimeMaterial == null || _displayCanvas == null)
        {
            return;
        }

        DrawingBoardMaterialBinding.EnsureBinding(
            boardRenderer,
            _runtimeMaterial,
            _displayCanvas.Texture,
            texturePropertyName,
            boardTextureScale,
            boardTextureOffset);
    }

    private Vector2 SurfaceUvToCanvasUv(Vector2 surfaceUv)
    {
        return new Vector2(
            (surfaceUv.x * boardTextureScale.x) + boardTextureOffset.x,
            (surfaceUv.y * boardTextureScale.y) + boardTextureOffset.y);
    }

    private static bool IsDrawingPhaseActive()
    {
        GameplayModeHost host = GameplayModeHost.Instance;
        return host == null || host.CurrentState == GameState.Drawing;
    }

    private bool TryGetPointerScreenPosition(out Vector2 screenPosition)
    {
        return DrawingInputReader.TryGetPointerScreenPosition(drawingCamera, out screenPosition);
    }

    private static bool GetPointerDownThisFrame()
    {
        return DrawingInputReader.GetPointerDownThisFrame();
    }

    private static bool GetPointerHeld()
    {
        return DrawingInputReader.GetPointerHeld();
    }

    private static bool GetPointerUpThisFrame()
    {
        return DrawingInputReader.GetPointerUpThisFrame();
    }

    private static bool GetUndoShortcutPressed()
    {
        return DrawingInputReader.GetUndoShortcutPressed();
    }

    private static bool GetRedoShortcutPressed()
    {
        return DrawingInputReader.GetRedoShortcutPressed();
    }

}
