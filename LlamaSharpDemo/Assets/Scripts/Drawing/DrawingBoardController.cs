using UnityEngine;
using UnityEngine.EventSystems;
using System;
using System.Collections;
using System.Threading;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public enum DrawingToolMode
{
    Brush = 0,
    Eraser = 1,
    Fill = 2,
    SketchGuide = 3
}

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
    [SerializeField] private int textureWidth = 512;
    [SerializeField] private int textureHeight = 512;
    [SerializeField] private FilterMode filterMode = FilterMode.Bilinear;
    [SerializeField] private string texturePropertyName = "_BaseMap";
    [SerializeField] private Vector2 boardTextureScale = new(-1f, -1f);
    [SerializeField] private Vector2 boardTextureOffset = new(1f, 1f);

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

    [Header("Sketch Guide")]
    [SerializeField] private Color sketchGuideStrokeColor = new(0f, 0f, 0f, 1f);
    [SerializeField] private Color sketchGuideOverlayColor = new(0.05f, 0.8f, 1f, 0.85f);
    [SerializeField] private int sketchGuideRegionPadding = 24;
    [SerializeField] private bool applyFullSketchResult = true;
    [SerializeField] private int sketchResultApplyRowsPerFrame = 32;

    [Header("History")]
    [SerializeField] private int maxHistoryEntries = 24;

    private DrawingCanvas _canvas;
    private DrawingCanvas _guideCanvas;
    private DrawingCanvas _displayCanvas;
    private DrawingHistory _history;
    private Material _runtimeMaterial;
    private LineRenderer _brushPreviewRenderer;
    private Material _brushPreviewMaterial;
    private Material _originalSharedMaterial;
    private bool _isDrawing;
    private bool _useEraser;
    private bool _useFillTool;
    private bool _useSketchGuide;
    private bool _hasCapturedOriginalMaterial;
    private bool _isInteractionLocked;
    private Vector2Int _lastPixel;
    private readonly StrokeHistoryCapture _strokeHistory = new();

    public event Action<int> BrushRadiusChanged;
    public event Action<bool, bool> HistoryStateChanged;
    public event Action<bool, bool> SketchGuideStateChanged;

    public Texture2D CanvasTexture => _canvas?.Texture;
    public Texture2D GuideTexture => _guideCanvas?.Texture;
    public Texture2D DisplayTexture => _displayCanvas?.Texture;
    public int BrushRadius => brushRadius;
    public bool IsEraserEnabled => _useEraser;
    public bool IsFillToolEnabled => _useFillTool;
    public bool IsSketchGuideEnabled => _useSketchGuide;
    public bool IsInteractionLocked => _isInteractionLocked;
    public Color BrushColor => brushColor;
    public Color BackgroundColor => backgroundColor;
    public Color ActiveDrawColor => GetActiveDrawColor();
    public bool CanUndo => _history != null && _history.CanUndo;
    public bool CanRedo => _history != null && _history.CanRedo;
    public int SketchGuideRegionPadding => Mathf.Max(0, sketchGuideRegionPadding);
    public bool HasSketchGuide => _guideCanvas != null && _guideCanvas.TryGetNonBackgroundBounds(out _);

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
        if (_canvas == null || _guideCanvas == null || _displayCanvas == null ||
            drawingSurfaceCollider == null || drawingCamera == null)
        {
            return;
        }

        UpdateBrushRadiusFromScroll();
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
        if (_guideCanvas == null || _displayCanvas == null)
        {
            return;
        }

        _isDrawing = false;
        if (_useSketchGuide)
        {
            FinalizeStrokeHistory();
        }

        if (!_guideCanvas.TryGetNonBackgroundBounds(out RectInt dirtyRegion))
        {
            NotifySketchGuideStateChanged();
            return;
        }

        _guideCanvas.Clear();
        RefreshDisplayRegion(dirtyRegion);
        NotifySketchGuideStateChanged();
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
        FinalizeStrokeHistory();
        _useEraser = enabled;
        if (enabled)
        {
            _useFillTool = false;
            _useSketchGuide = false;
        }

        _isDrawing = false;
        NotifySketchGuideStateChanged();
    }

    public void ToggleEraser()
    {
        SetEraserEnabled(!_useEraser);
    }

    public void SetFillToolEnabled(bool enabled)
    {
        FinalizeStrokeHistory();
        _useFillTool = enabled;
        if (enabled)
        {
            _useEraser = false;
            _useSketchGuide = false;
        }

        _isDrawing = false;
        NotifySketchGuideStateChanged();
    }

    public void ToggleFillTool()
    {
        SetFillToolEnabled(!_useFillTool);
    }

    public void SetSketchGuideEnabled(bool enabled)
    {
        FinalizeStrokeHistory();
        _useSketchGuide = enabled;
        if (enabled)
        {
            _useFillTool = false;
            _useEraser = false;
        }

        _isDrawing = false;
        NotifySketchGuideStateChanged();
    }

    public void ToggleSketchGuide()
    {
        SetSketchGuideEnabled(!_useSketchGuide);
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
            if (_brushPreviewRenderer != null)
            {
                _brushPreviewRenderer.enabled = false;
            }
        }
    }

    public DrawingToolMode GetCurrentToolMode()
    {
        if (_useSketchGuide)
        {
            return DrawingToolMode.SketchGuide;
        }

        if (_useFillTool)
        {
            return DrawingToolMode.Fill;
        }

        return _useEraser ? DrawingToolMode.Eraser : DrawingToolMode.Brush;
    }

    public bool TryGetSketchGuideBounds(out RectInt guideRegion)
    {
        if (_guideCanvas == null)
        {
            guideRegion = default;
            return false;
        }

        return _guideCanvas.TryGetNonBackgroundBounds(out guideRegion);
    }

    public bool TryBuildSketchGuideControlTexture(
        out Texture2D controlTexture,
        out RectInt guideRegion,
        out string error)
    {
        controlTexture = null;
        guideRegion = default;
        error = null;

        if (_guideCanvas == null)
        {
            error = "Sketch guide canvas is not initialized.";
            return false;
        }

        if (!_guideCanvas.TryGetNonBackgroundBounds(out guideRegion))
        {
            error = "Draw sketch guide strokes first.";
            return false;
        }

        Color32[] guidePixels = _guideCanvas.CopyPixels();
        Color32[] controlPixels = new Color32[guidePixels.Length];
        Color32 white = Color.white;
        Color32 black = Color.black;
        int width = _guideCanvas.Width;
        int height = _guideCanvas.Height;
        for (int y = 0; y < height; y++)
        {
            int rowStart = y * width;
            for (int x = 0; x < width; x++)
            {
                int pixelIndex = rowStart + x;
                controlPixels[pixelIndex] = guidePixels[pixelIndex].a > 0 ? black : white;
            }
        }

        controlTexture = new Texture2D(_guideCanvas.Width, _guideCanvas.Height, TextureFormat.RGBA32, false)
        {
            name = $"{name}_SketchGuideControlTexture",
            filterMode = filterMode,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = RuntimeHideFlags
        };
        controlTexture.SetPixels32(controlPixels);
        controlTexture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
        return true;
    }

    public bool TryApplySketchGuideResult(
        Texture2D generatedTexture,
        RectInt guideRegion,
        int regionPadding,
        out RectInt appliedRegion,
        out string error)
    {
        appliedRegion = default;
        error = null;

        if (_canvas == null || _guideCanvas == null || _displayCanvas == null)
        {
            error = "Drawing canvases are not initialized.";
            return false;
        }

        if (generatedTexture == null)
        {
            error = "Generated texture is null.";
            return false;
        }

        if (guideRegion.width <= 0 || guideRegion.height <= 0)
        {
            if (!_guideCanvas.TryGetNonBackgroundBounds(out guideRegion))
            {
                error = "Sketch guide bounds are empty.";
                return false;
            }
        }

        appliedRegion = applyFullSketchResult
            ? new RectInt(0, 0, _canvas.Width, _canvas.Height)
            : ExpandRegion(guideRegion, Mathf.Max(0, regionPadding), _canvas.Width, _canvas.Height);
        if (appliedRegion.width <= 0 || appliedRegion.height <= 0)
        {
            error = "Resolved sketch guide region is empty.";
            return false;
        }

        Texture2D sampledTexture = generatedTexture;
        Texture2D resizedTexture = null;
        if (generatedTexture.width != _canvas.Width || generatedTexture.height != _canvas.Height)
        {
            if (!StableDiffusionCppImageIO.TryResizeTexture(
                    generatedTexture,
                    _canvas.Width,
                    _canvas.Height,
                    out resizedTexture,
                    out error))
            {
                return false;
            }

            sampledTexture = resizedTexture;
        }

        try
        {
            Color32[] beforePixels = _canvas.CopyRegion(appliedRegion);
            Color32[] generatedPixels = CopyTextureRegionPixels(
                sampledTexture.GetPixels32(),
                sampledTexture.width,
                appliedRegion);

            if (generatedPixels == null || generatedPixels.Length != appliedRegion.width * appliedRegion.height)
            {
                error = "Failed to read generated sketch guide pixels.";
                return false;
            }

            _canvas.ApplyRegion(appliedRegion, generatedPixels);
            RecordHistory(appliedRegion, beforePixels, generatedPixels);
            _guideCanvas.Clear();
            RefreshDisplayRegion(appliedRegion);
            NotifySketchGuideStateChanged();
            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to apply sketch guide result: {ex.Message}";
            return false;
        }
        finally
        {
            if (resizedTexture != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(resizedTexture);
                }
                else
                {
                    DestroyImmediate(resizedTexture);
                }
            }
        }
    }

    public IEnumerator ApplySketchGuideResultCoroutine(
        Texture2D generatedTexture,
        RectInt guideRegion,
        int regionPadding,
        CancellationToken cancellationToken,
        Action<bool, RectInt, string> onComplete)
    {
        RectInt appliedRegion = default;
        string error = null;

        if (_canvas == null || _guideCanvas == null || _displayCanvas == null)
        {
            onComplete?.Invoke(false, appliedRegion, "Drawing canvases are not initialized.");
            yield break;
        }

        if (generatedTexture == null)
        {
            onComplete?.Invoke(false, appliedRegion, "Generated texture is null.");
            yield break;
        }

        if (guideRegion.width <= 0 || guideRegion.height <= 0)
        {
            if (!_guideCanvas.TryGetNonBackgroundBounds(out guideRegion))
            {
                onComplete?.Invoke(false, appliedRegion, "Sketch guide bounds are empty.");
                yield break;
            }
        }

        appliedRegion = applyFullSketchResult
            ? new RectInt(0, 0, _canvas.Width, _canvas.Height)
            : ExpandRegion(guideRegion, Mathf.Max(0, regionPadding), _canvas.Width, _canvas.Height);
        if (appliedRegion.width <= 0 || appliedRegion.height <= 0)
        {
            onComplete?.Invoke(false, appliedRegion, "Resolved sketch guide region is empty.");
            yield break;
        }

        Texture2D sampledTexture = generatedTexture;
        Texture2D resizedTexture = null;
        if (generatedTexture.width != _canvas.Width || generatedTexture.height != _canvas.Height)
        {
            if (!StableDiffusionCppImageIO.TryResizeTexture(
                    generatedTexture,
                    _canvas.Width,
                    _canvas.Height,
                    out resizedTexture,
                    out error))
            {
                onComplete?.Invoke(false, appliedRegion, error);
                yield break;
            }

            sampledTexture = resizedTexture;
        }

        Color32[] beforePixels = _canvas.CopyRegion(appliedRegion);
        Color32[] sourcePixels = sampledTexture.GetPixels32();
        Color32[] afterPixels = new Color32[appliedRegion.width * appliedRegion.height];
        int rowsPerFrame = Mathf.Clamp(sketchResultApplyRowsPerFrame, 1, appliedRegion.height);

        try
        {
            for (int localY = 0; localY < appliedRegion.height; localY += rowsPerFrame)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    onComplete?.Invoke(false, appliedRegion, "Sketch guide generation cancelled.");
                    yield break;
                }

                int chunkHeight = Mathf.Min(rowsPerFrame, appliedRegion.height - localY);
                var chunkRegion = new RectInt(
                    appliedRegion.x,
                    appliedRegion.y + localY,
                    appliedRegion.width,
                    chunkHeight);
                Color32[] chunkPixels = CopyTextureRegionPixels(
                    sourcePixels,
                    sampledTexture.width,
                    chunkRegion);

                if (chunkPixels == null || chunkPixels.Length != chunkRegion.width * chunkRegion.height)
                {
                    onComplete?.Invoke(false, appliedRegion, "Failed to read generated sketch guide pixels.");
                    yield break;
                }

                _canvas.ApplyRegion(chunkRegion, chunkPixels);
                RefreshDisplayRegion(chunkRegion);
                Array.Copy(
                    chunkPixels,
                    0,
                    afterPixels,
                    localY * appliedRegion.width,
                    chunkPixels.Length);
                yield return null;
            }

            RecordHistory(appliedRegion, beforePixels, afterPixels);
            _guideCanvas.Clear();
            RefreshDisplayRegion(appliedRegion);
            NotifySketchGuideStateChanged();
            onComplete?.Invoke(true, appliedRegion, null);
        }
        finally
        {
            if (resizedTexture != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(resizedTexture);
                }
                else
                {
                    DestroyImmediate(resizedTexture);
                }
            }
        }
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

    private void OnDestroy()
    {
        ResetStrokeHistory();
        _canvas?.Dispose();
        _canvas = null;
        _guideCanvas?.Dispose();
        _guideCanvas = null;
        _displayCanvas?.Dispose();
        _displayCanvas = null;
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
        _guideCanvas?.Dispose();
        _displayCanvas?.Dispose();
        _canvas = new DrawingCanvas(textureWidth, textureHeight, backgroundColor, filterMode);
        _guideCanvas = new DrawingCanvas(textureWidth, textureHeight, Color.clear, filterMode);
        _displayCanvas = new DrawingCanvas(textureWidth, textureHeight, backgroundColor, filterMode);
        _history = new DrawingHistory(maxHistoryEntries);
        ResetStrokeHistory();
        RefreshDisplayFullCanvas();

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
        AssignTexture(_runtimeMaterial, _displayCanvas.Texture, texturePropertyName);
        boardRenderer.sharedMaterial = _runtimeMaterial;
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
            if (_useSketchGuide)
            {
                _isDrawing = true;
                _lastPixel = startPixel;
                PaintSketchGuideLine(startPixel, startPixel);
                return;
            }

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
                if (_useSketchGuide)
                {
                    PaintSketchGuideLine(_lastPixel, currentPixel);
                    _lastPixel = currentPixel;
                    return;
                }

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
        if (_useSketchGuide)
        {
            return sketchGuideOverlayColor;
        }

        return _useEraser ? backgroundColor : brushColor;
    }

    private Color GetPreviewColor()
    {
        if (_useSketchGuide)
        {
            return sketchGuideOverlayColor;
        }

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

        Vector2 canvasUv = SurfaceUvToCanvasUv(hit.textureCoord);
        int x = Mathf.Clamp(Mathf.FloorToInt(canvasUv.x * _canvas.Width), 0, _canvas.Width - 1);
        int y = Mathf.Clamp(Mathf.FloorToInt(canvasUv.y * _canvas.Height), 0, _canvas.Height - 1);
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
        SketchGuideStateChanged?.Invoke(_useSketchGuide, HasSketchGuide);
    }

    private void PaintSketchGuideLine(Vector2Int from, Vector2Int to)
    {
        if (_guideCanvas == null || _displayCanvas == null)
        {
            return;
        }

        if (_guideCanvas.DrawLine(from, to, sketchGuideStrokeColor, brushRadius, out RectInt dirtyRegion))
        {
            RefreshDisplayRegion(dirtyRegion);
            NotifySketchGuideStateChanged();
        }
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
        if (_guideCanvas != null)
        {
            Color32[] guidePixels = _guideCanvas.CopyRegion(region);
            int pixelCount = Mathf.Min(compositePixels.Length, guidePixels.Length);
            Color guideColor = sketchGuideOverlayColor;
            for (int i = 0; i < pixelCount; i++)
            {
                float alpha = guidePixels[i].a / 255f;
                if (alpha <= 0f)
                {
                    continue;
                }

                compositePixels[i] = Color32.Lerp(compositePixels[i], guideColor, alpha * guideColor.a);
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

        if (_useFillTool || pointerOverUi || _isInteractionLocked || !TryGetPointerHit(out RaycastHit hit))
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

    private Vector2 SurfaceUvToCanvasUv(Vector2 surfaceUv)
    {
        return new Vector2(
            (surfaceUv.x * boardTextureScale.x) + boardTextureOffset.x,
            (surfaceUv.y * boardTextureScale.y) + boardTextureOffset.y);
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
