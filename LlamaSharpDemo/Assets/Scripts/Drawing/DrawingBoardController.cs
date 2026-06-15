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

    [Header("Instruction Label")]
    [Tooltip("Show a small control hint on the tablet surface while drawing.")]
    [SerializeField] private bool instructionLabelEnabled = true;
    [Tooltip("Optional TextMeshPro label on the tablet surface. If unset, a runtime world-space label is created.")]
    [SerializeField] private TextMeshPro instructionLabelText;
    [Tooltip("Create a fallback TextMeshPro instruction label at runtime if Instruction Label Text is not assigned.")]
    [SerializeField] private bool autoCreateInstructionLabelIfMissing = true;
    [Tooltip("Text color used only by the runtime fallback instruction label.")]
    [SerializeField] private Color instructionLabelColor = new(0.02f, 0.04f, 0.05f, 0.92f);
    [Tooltip("Instruction label width as a fraction of the tablet screen width.")]
    [SerializeField] [Range(0.05f, 0.8f)] private float instructionLabelWidthNormalized = 0.34f;
    [Tooltip("Instruction label text height as a fraction of the tablet screen height.")]
    [SerializeField] [Range(0.01f, 0.15f)] private float instructionLabelHeightNormalized = 0.038f;
    [Tooltip("Instruction label position in normalized tablet screen coordinates.")]
    [SerializeField] private Vector2 instructionLabelAnchorNormalized = new(0.5f, 0.035f);
    [Tooltip("World-space offset that lifts the fallback instruction label slightly off the tablet screen.")]
    [SerializeField] private float instructionLabelSurfaceOffset = 0.014f;

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
    private TextMeshPro _runtimeInstructionLabelText;

    private DrawingBoardCoordinateMapper CoordinateMapper =>
        new(boardTextureScale, boardTextureOffset, flipExportHorizontally, flipExportVertically);

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
        CoordinateMapper.ApplyExportOrientation(compositePixels, _canvas.Width, _canvas.Height);

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

    public void ShowInstructionLabel(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            ClearInstructionLabel();
            return;
        }

        if (!instructionLabelEnabled && instructionLabelText != null)
        {
            ClearInstructionLabel();
            return;
        }

        EnsureRuntimeReady();
        TextMeshPro labelText = GetInstructionLabelText(createIfMissing: true);
        if (labelText == null)
        {
            return;
        }

        labelText.text = label.Trim();
        labelText.gameObject.SetActive(true);
        if (labelText == _runtimeInstructionLabelText)
        {
            ApplyRuntimeInstructionLabelStyle(labelText);
            PositionRuntimeInstructionLabel();
        }
    }

    public void ClearInstructionLabel()
    {
        TextMeshPro labelText = GetInstructionLabelText(createIfMissing: false);
        if (labelText != null)
        {
            labelText.text = string.Empty;
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
        CleanupInstructionLabel();
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

        pixel = CoordinateMapper.CanvasUvToPixel(canvasUv, _canvas.Width, _canvas.Height);
        return true;
    }

    private bool TryGetPointerCanvasUv(out Vector2 canvasUv)
    {
        canvasUv = default;

        if (_canvas == null || !TryGetPointerHit(out RaycastHit hit))
        {
            return false;
        }

        if (!CoordinateMapper.TryGetCanvasUvFromHit(hit, drawingSurfaceCollider, out canvasUv))
        {
            return false;
        }

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
        if (!TryGetBoardWorldSurfaceSize(out float boardWorldWidth, out float boardWorldHeight))
        {
            return 1f;
        }

        DrawingBoardCoordinateMapper mapper = CoordinateMapper;
        float worldWidth = boardWorldWidth / mapper.TextureScaleX;
        float worldHeight = boardWorldHeight / mapper.TextureScaleY;
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

    private void RefreshDisplayFullCanvas()
    {
        RefreshDisplayFullCanvasBase();
    }

    private void RefreshDisplayRegion(RectInt region)
    {
        RefreshDisplayRegionBase(region);
    }

    private void RefreshDisplayFullCanvasBase()
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

    private void RefreshDisplayRegionBase(RectInt region)
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

        Vector2 previewCanvasUv = CoordinateMapper.SurfaceUvToCanvasUv(previewSurfaceUv);

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

        DrawingBoardCoordinateMapper mapper = CoordinateMapper;
        radiusU = brushRadius * (worldUSize / (_canvas.Width * mapper.TextureScaleX));
        radiusV = brushRadius * (worldVSize / (_canvas.Height * mapper.TextureScaleY));
        radiusU = Mathf.Max(0.0005f, radiusU);
        radiusV = Mathf.Max(0.0005f, radiusV);

        axisU = mapper.GetCanvasUAxisWorldDirection(boxCollider.transform, uAxis);
        axisV = mapper.GetCanvasVAxisWorldDirection(boxCollider.transform, vAxis);

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

        DrawingBoardCoordinateMapper mapper = CoordinateMapper;
        if (drawingSurfaceCollider is BoxCollider boxCollider &&
            TryGetBoxColliderWorldSurfaceSize(boxCollider, out float worldWidth, out float worldHeight))
        {
            float radiusU = brushRadius * (worldWidth / (_canvas.Width * mapper.TextureScaleX));
            float radiusV = brushRadius * (worldHeight / (_canvas.Height * mapper.TextureScaleY));
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
        PositionRuntimeInstructionLabel();
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
        _runtimeRecognitionLabelText.textWrappingMode = TextWrappingModes.NoWrap;
        _runtimeRecognitionLabelText.overflowMode = TextOverflowModes.Ellipsis;
        _runtimeRecognitionLabelText.fontStyle = FontStyles.Bold;
        _runtimeRecognitionLabelText.richText = false;
        _runtimeRecognitionLabelText.color = recognitionLabelColor;
        _runtimeRecognitionLabelText.gameObject.SetActive(false);
    }

    private TextMeshPro GetInstructionLabelText(bool createIfMissing)
    {
        if (instructionLabelText != null)
        {
            return instructionLabelText;
        }

        if (_runtimeInstructionLabelText != null)
        {
            return _runtimeInstructionLabelText;
        }

        if (createIfMissing && (autoCreateInstructionLabelIfMissing || instructionLabelText == null))
        {
            EnsureRuntimeInstructionLabel();
        }

        return _runtimeInstructionLabelText;
    }

    private void EnsureRuntimeInstructionLabel()
    {
        if (_runtimeInstructionLabelText != null)
        {
            return;
        }

        Transform parent = drawingSurfaceCollider != null ? drawingSurfaceCollider.transform : transform;
        var labelObject = new GameObject("InstructionLabel", typeof(RectTransform), typeof(TextMeshPro))
        {
            hideFlags = RuntimeHideFlags
        };
        labelObject.transform.SetParent(parent, false);
        labelObject.layer = boardRenderer != null ? boardRenderer.gameObject.layer : gameObject.layer;

        _runtimeInstructionLabelText = labelObject.GetComponent<TextMeshPro>();
        ApplyRuntimeInstructionLabelStyle(_runtimeInstructionLabelText);
        _runtimeInstructionLabelText.gameObject.SetActive(false);
    }

    private void ApplyRuntimeInstructionLabelStyle(TextMeshPro labelText)
    {
        if (labelText == null)
        {
            return;
        }

        TextMeshPro styleSource = recognitionLabelText != null ? recognitionLabelText : _runtimeRecognitionLabelText;
        if (styleSource != null && styleSource.font != null)
        {
            labelText.font = styleSource.font;
            if (styleSource.fontSharedMaterial != null)
            {
                labelText.fontSharedMaterial = styleSource.fontSharedMaterial;
            }
        }

        labelText.alignment = TextAlignmentOptions.Bottom;
        labelText.textWrappingMode = TextWrappingModes.NoWrap;
        labelText.overflowMode = TextOverflowModes.Ellipsis;
        labelText.enableAutoSizing = true;
        labelText.fontSizeMin = 0.08f;
        labelText.fontSizeMax = ResolveInstructionLabelFontSize(labelText.fontSize);
        labelText.fontStyle = FontStyles.Bold;
        labelText.richText = false;
        labelText.color = instructionLabelColor;
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

    private void PositionRuntimeInstructionLabel()
    {
        if (_runtimeInstructionLabelText == null || !_runtimeInstructionLabelText.gameObject.activeSelf)
        {
            return;
        }

        if (!TryGetInstructionLabelPlacement(
                out Vector3 position,
                out Quaternion rotation,
                out Vector2 rectSize,
                out float fontSize))
        {
            return;
        }

        RectTransform rectTransform = _runtimeInstructionLabelText.rectTransform;
        float resolvedFontSize = ResolveInstructionLabelFontSize(fontSize);
        rectSize = new Vector2(
            Mathf.Max(rectSize.x, 0.72f),
            Mathf.Max(rectSize.y, resolvedFontSize * 1.4f));
        rectTransform.pivot = new Vector2(0.5f, 0f);
        rectTransform.sizeDelta = rectSize;
        rectTransform.SetPositionAndRotation(position, rotation);
        _runtimeInstructionLabelText.fontSizeMin = Mathf.Min(0.08f, resolvedFontSize * 0.5f);
        _runtimeInstructionLabelText.fontSizeMax = resolvedFontSize;
        _runtimeInstructionLabelText.fontSize = resolvedFontSize;
    }

    private bool TryGetRecognitionLabelPlacement(
        out Vector3 position,
        out Quaternion rotation,
        out Vector2 rectSize,
        out float fontSize)
    {
        if (drawingSurfaceCollider is not BoxCollider boxCollider)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            rectSize = Vector2.zero;
            fontSize = 0f;
            return false;
        }

        Vector2 anchor = new(
            1f - Mathf.Clamp01(recognitionLabelInsetNormalized.x),
            Mathf.Clamp01(recognitionLabelInsetNormalized.y));
        return CoordinateMapper.TryGetSurfaceLabelPlacement(
            boxCollider,
            drawingCamera,
            transform,
            anchor,
            recognitionLabelWidthNormalized,
            recognitionLabelHeightNormalized,
            recognitionLabelSurfaceOffset,
            out position,
            out rotation,
            out rectSize,
            out fontSize);
    }

    private bool TryGetInstructionLabelPlacement(
        out Vector3 position,
        out Quaternion rotation,
        out Vector2 rectSize,
        out float fontSize)
    {
        if (drawingSurfaceCollider is not BoxCollider boxCollider)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            rectSize = Vector2.zero;
            fontSize = 0f;
            return false;
        }

        Vector2 anchor = new(
            Mathf.Clamp01(instructionLabelAnchorNormalized.x),
            Mathf.Clamp01(instructionLabelAnchorNormalized.y));
        float widthNormalized = Mathf.Clamp(Mathf.Max(instructionLabelWidthNormalized, 0.58f), 0.05f, 0.9f);
        float heightNormalized = Mathf.Clamp(Mathf.Max(instructionLabelHeightNormalized, 0.06f), 0.01f, 0.15f);
        return CoordinateMapper.TryGetSurfaceLabelPlacement(
            boxCollider,
            drawingCamera,
            transform,
            anchor,
            widthNormalized,
            heightNormalized,
            instructionLabelSurfaceOffset,
            out position,
            out rotation,
            out rectSize,
            out fontSize);
    }

    private float ResolveInstructionLabelFontSize(float calculatedFontSize)
    {
        TextMeshPro styleSource = recognitionLabelText != null ? recognitionLabelText : _runtimeRecognitionLabelText;
        float sourceFontSize = styleSource != null && styleSource.fontSize > 0f
            ? styleSource.fontSize * 0.65f
            : 0.32f;
        return Mathf.Clamp(Mathf.Max(calculatedFontSize, sourceFontSize), 0.18f, 0.55f);
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

    private void CleanupInstructionLabel()
    {
        if (_runtimeInstructionLabelText == null)
        {
            return;
        }

        GameObject labelObject = _runtimeInstructionLabelText.gameObject;
        _runtimeInstructionLabelText = null;
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
