using UnityEngine;
using UnityEngine.EventSystems;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
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
    [SerializeField] private float scrollRadiusStep = 1f;
    [SerializeField] private bool blockPointerWhenOverUi = true;
    [SerializeField] private Rect normalizedPaintArea = new(0.40f, 0.02f, 0.58f, 0.96f);

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

    [Header("Sticker Layers")]
    [SerializeField] private bool enableStickerLayers = true;
    [SerializeField] private float stickerSurfaceOffset = 0.02f;
    [SerializeField] private float stickerDepthStep = 0.0025f;
    [SerializeField] private float minStickerScale = 0.08f;
    [SerializeField] private float maxStickerScale = 12f;
    [SerializeField] private float stickerScaleStep = 0.08f;
    [SerializeField] private float stickerRotationStep = 8f;
    [SerializeField] private float stickerOpacityStep = 0.05f;
    [SerializeField] private Color selectedStickerOutlineColor = new(0.15f, 0.90f, 1f, 0.95f);

    private DrawingCanvas _canvas;
    private DrawingCanvas _guideCanvas;
    private DrawingCanvas _displayCanvas;
    private DrawingCanvas _exportCanvas;
    private DrawingHistory _history;
    private Material _runtimeMaterial;
    private MeshCollider _runtimeDrawingSurfaceCollider;
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
    private readonly List<DrawingStickerLayer> _stickerLayers = new();
    private Transform _stickerRoot;
    private DrawingStickerLayer _selectedSticker;
    private bool _isDraggingSticker;
    private bool _useStickerMaskErase;
    private bool _isErasingStickerMask;
    private Vector3 _stickerDragOffsetBoardLocal;

    public event Action<int> BrushRadiusChanged;
    public event Action<bool, bool> HistoryStateChanged;
    public event Action<bool, bool> SketchGuideStateChanged;
    public event Action<bool, float, string> StickerSelectionChanged;

    public Texture2D CanvasTexture => _canvas?.Texture;
    public Texture2D GuideTexture => _guideCanvas?.Texture;
    public Texture2D DisplayTexture => _displayCanvas?.Texture;
    public bool HasCanvasMarks => _canvas != null && _canvas.TryGetNonBackgroundBounds(out _);
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
    public bool HasSelectedSticker => _selectedSticker != null;
    public float SelectedStickerOpacity => _selectedSticker != null ? _selectedSticker.Opacity : 1f;
    public string SelectedStickerLabel => _selectedSticker != null ? _selectedSticker.LayerName : string.Empty;
    public bool IsStickerMaskEraseEnabled => _useStickerMaskErase;
    public int StickerCount => _stickerLayers.Count;

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
        if (_canvas == null || _guideCanvas == null || _displayCanvas == null ||
            drawingSurfaceCollider == null || drawingCamera == null)
        {
            return;
        }

        if (_selectedSticker == null || _useStickerMaskErase)
        {
            UpdateBrushRadiusFromScroll();
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

    public bool TryCreateStickerFromTexture(
        Texture2D stickerTexture,
        RectInt placementRegion,
        string stickerLabel,
        out string error)
    {
        error = null;

        if (!enableStickerLayers)
        {
            error = "Sticker layers are disabled on this drawing board.";
            return false;
        }

        if (_canvas == null || _displayCanvas == null)
        {
            error = "Drawing canvases are not initialized.";
            return false;
        }

        if (stickerTexture == null)
        {
            error = "Sticker texture is null.";
            return false;
        }

        if (!TryResolveStickerPlacement(
                stickerTexture,
                placementRegion,
                out Vector3 centerLocal,
                out Vector3 sizeLocal,
                out error))
        {
            return false;
        }

        EnsureStickerRoot();

        var stickerObject = new GameObject($"StickerLayer_{_stickerLayers.Count + 1:00}");
        stickerObject.hideFlags = RuntimeHideFlags;
        stickerObject.transform.SetParent(_stickerRoot, false);
        stickerObject.transform.localPosition = new Vector3(
            centerLocal.x,
            stickerSurfaceOffset + (_stickerLayers.Count * stickerDepthStep),
            centerLocal.z);
        stickerObject.transform.localRotation = Quaternion.identity;
        stickerObject.transform.localScale = sizeLocal;

        DrawingStickerLayer stickerLayer = stickerObject.AddComponent<DrawingStickerLayer>();
        stickerLayer.Initialize(
            stickerTexture,
            placementRegion,
            stickerLabel,
            _stickerLayers.Count + 1,
            selectedStickerOutlineColor);
        _stickerLayers.Add(stickerLayer);
        SelectSticker(stickerLayer);
        ClearSketchGuide();
        return true;
    }

    public bool TryApplyStickerFromTexture(
        Texture2D stickerTexture,
        RectInt placementRegion,
        string stickerLabel,
        out string error)
    {
        if (_selectedSticker == null)
        {
            return TryCreateStickerFromTexture(
                stickerTexture,
                placementRegion,
                stickerLabel,
                out error);
        }

        error = null;
        if (!TryResolveStickerPlacement(
                stickerTexture,
                placementRegion,
                out _,
                out Vector3 sizeLocal,
                out error))
        {
            return false;
        }

        Vector3 currentPosition = _selectedSticker.transform.localPosition;
        Vector3 currentScale = _selectedSticker.transform.localScale;
        _selectedSticker.ReplaceStickerTexture(stickerTexture, placementRegion, stickerLabel);
        _selectedSticker.transform.localPosition = currentPosition;
        _selectedSticker.transform.localScale = new Vector3(
            Mathf.Sign(Mathf.Approximately(currentScale.x, 0f) ? 1f : currentScale.x) * sizeLocal.x,
            currentScale.y,
            Mathf.Sign(Mathf.Approximately(currentScale.z, 0f) ? 1f : currentScale.z) * sizeLocal.z);
        ClampStickerInsideBoard(_selectedSticker);
        SelectSticker(_selectedSticker);
        ClearSketchGuide();
        return true;
    }

    public bool DeleteSelectedSticker()
    {
        if (_selectedSticker == null)
        {
            return false;
        }

        DrawingStickerLayer stickerToRemove = _selectedSticker;
        SelectSticker(null);
        _isDraggingSticker = false;

        int index = _stickerLayers.IndexOf(stickerToRemove);
        if (index >= 0)
        {
            _stickerLayers.RemoveAt(index);
        }

        if (stickerToRemove != null)
        {
            if (Application.isPlaying)
            {
                Destroy(stickerToRemove.gameObject);
            }
            else
            {
                DestroyImmediate(stickerToRemove.gameObject);
            }
        }

        NormalizeStickerLayerDepths();
        return true;
    }

    public bool ConfirmSelectedStickerPlacement()
    {
        if (_selectedSticker == null)
        {
            NotifyStickerSelectionChanged();
            return false;
        }

        SelectSticker(null);
        _isDraggingSticker = false;
        _isErasingStickerMask = false;
        return true;
    }

    public void SetSelectedStickerOpacity(float opacity)
    {
        if (_selectedSticker == null)
        {
            NotifyStickerSelectionChanged();
            return;
        }

        _selectedSticker.SetOpacity(opacity);
        NotifyStickerSelectionChanged();
    }

    public Texture2D GetCompositeTextureForExport()
    {
        if (_canvas == null)
        {
            return null;
        }

        if (!enableStickerLayers || _stickerLayers.Count == 0)
        {
            return _canvas.Texture;
        }

        if (_exportCanvas == null ||
            _exportCanvas.Width != _canvas.Width ||
            _exportCanvas.Height != _canvas.Height)
        {
            _exportCanvas?.Dispose();
            _exportCanvas = new DrawingCanvas(_canvas.Width, _canvas.Height, backgroundColor, filterMode);
        }

        Color32[] compositePixels = _canvas.CopyPixels();
        Bounds boardMeshBounds = GetBoardMeshBounds();
        for (int i = 0; i < _stickerLayers.Count; i++)
        {
            BlendStickerIntoPixels(_stickerLayers[i], compositePixels, boardMeshBounds);
        }

        _exportCanvas.ApplyRegion(
            new RectInt(0, 0, _canvas.Width, _canvas.Height),
            compositePixels);
        return _exportCanvas.Texture;
    }

    public void SetBrushColor(Color color)
    {
        FinalizeStrokeHistory();
        SelectSticker(null);
        _isDraggingSticker = false;
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
        SelectSticker(null);
        _isDraggingSticker = false;
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
        SelectSticker(null);
        _isDraggingSticker = false;
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
        SelectSticker(null);
        _isDraggingSticker = false;
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

    public void SetStickerMaskEraseEnabled(bool enabled)
    {
        if (!enableStickerLayers || _selectedSticker == null)
        {
            enabled = false;
        }

        if (_useStickerMaskErase == enabled)
        {
            NotifyStickerSelectionChanged();
            return;
        }

        FinalizeStrokeHistory();
        _useStickerMaskErase = enabled;
        _isDrawing = false;
        _isDraggingSticker = false;
        _isErasingStickerMask = false;

        if (_useStickerMaskErase)
        {
            _useEraser = false;
            _useFillTool = false;
            _useSketchGuide = false;
            NotifySketchGuideStateChanged();
        }

        NotifyStickerSelectionChanged();
    }

    public void ToggleStickerMaskErase()
    {
        SetStickerMaskEraseEnabled(!_useStickerMaskErase);
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
            if (_brushPreviewRenderer != null)
            {
                _brushPreviewRenderer.enabled = false;
            }
        }
    }

    public DrawingToolMode GetCurrentToolMode()
    {
        if (_useStickerMaskErase)
        {
            return DrawingToolMode.StickerMaskErase;
        }

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
        ClearAllStickerLayers();
        _canvas?.Dispose();
        _canvas = null;
        _guideCanvas?.Dispose();
        _guideCanvas = null;
        _displayCanvas?.Dispose();
        _displayCanvas = null;
        _exportCanvas?.Dispose();
        _exportCanvas = null;
        _history?.Clear();
        ReleaseRuntimeMaterial();
        CleanupBrushPreview();
        CleanupRuntimeDrawingSurfaceCollider();
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

        CaptureOriginalMaterial();
        ReleaseRuntimeMaterial();
        _canvas?.Dispose();
        _guideCanvas?.Dispose();
        _displayCanvas?.Dispose();
        _exportCanvas?.Dispose();
        GetResolvedCanvasDimensions(out int resolvedWidth, out int resolvedHeight);
        _canvas = new DrawingCanvas(resolvedWidth, resolvedHeight, backgroundColor, filterMode);
        _guideCanvas = new DrawingCanvas(resolvedWidth, resolvedHeight, Color.clear, filterMode);
        _displayCanvas = new DrawingCanvas(resolvedWidth, resolvedHeight, backgroundColor, filterMode);
        _exportCanvas = new DrawingCanvas(resolvedWidth, resolvedHeight, backgroundColor, filterMode);
        _history = new DrawingHistory(maxHistoryEntries);
        EnsureStickerRoot();
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
            _isDraggingSticker = false;
            _isErasingStickerMask = false;
            FinalizeStrokeHistory();
            return;
        }

        bool pointerDown = GetPointerDownThisFrame();
        bool pointerHeld = GetPointerHeld();
        bool pointerUp = GetPointerUpThisFrame();
        bool pointerOverUi = blockPointerWhenOverUi && IsPointerOverUi();

        bool hideBrushPreview = pointerOverUi || _isDraggingSticker || (_selectedSticker != null && !_useStickerMaskErase);
        UpdateBrushPreview(hideBrushPreview);
        HandleHistoryShortcuts();
        HandleStickerKeyboardShortcuts(pointerOverUi);

        if (TryHandleStickerPointerInput(pointerDown, pointerHeld, pointerUp, pointerOverUi))
        {
            return;
        }

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

        if (_useStickerMaskErase)
        {
            return previewEraserColor;
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

        Vector2 surfaceUv;
        if (hit.collider is MeshCollider)
        {
            surfaceUv = hit.textureCoord;
        }
        else if (!TryGetSurfaceUvFromBoardLocalHit(hit.point, out surfaceUv))
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

        Ray ray = drawingCamera.ScreenPointToRay(pointerScreenPosition);
        if (!drawingSurfaceCollider.Raycast(ray, out hit, 1000f))
        {
            return false;
        }

        return true;
    }

    private bool TryGetSurfaceUvFromBoardLocalHit(Vector3 worldPoint, out Vector2 surfaceUv)
    {
        surfaceUv = default;

        Bounds bounds = GetBoardMeshBounds();
        if (bounds.size.x <= 0.0001f || bounds.size.z <= 0.0001f)
        {
            return false;
        }

        Vector3 localPoint = transform.InverseTransformPoint(worldPoint);
        surfaceUv = new Vector2(
            1f - Mathf.InverseLerp(bounds.min.x, bounds.max.x, localPoint.x),
            1f - Mathf.InverseLerp(bounds.min.z, bounds.max.z, localPoint.z));
        return true;
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
            return;
        }

        float boardAspect = ResolveCanvasWorldAspect();
        if (boardAspect <= 0.0001f)
        {
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
        SketchGuideStateChanged?.Invoke(_useSketchGuide, HasSketchGuide);
    }

    private void NotifyStickerSelectionChanged()
    {
        StickerSelectionChanged?.Invoke(
            _selectedSticker != null,
            _selectedSticker != null ? _selectedSticker.Opacity : 1f,
            _selectedSticker != null ? _selectedSticker.LayerName : string.Empty);
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

        Ray ray = drawingCamera.ScreenPointToRay(pointerScreenPosition);
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

        Ray ray = drawingCamera.ScreenPointToRay(pointerScreenPosition);
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
                    compositePixels[pixelIndex] = nonPaintColor32;
                    continue;
                }

                if (absoluteX >= dividerStart && absoluteX < dividerEnd)
                {
                    compositePixels[pixelIndex] = dividerColor32;
                }
            }
        }

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

        Vector2 previewCanvasUv;
        if (hit.collider is MeshCollider)
        {
            previewCanvasUv = SurfaceUvToCanvasUv(hit.textureCoord);
        }
        else
        {
            if (!TryGetSurfaceUvFromBoardLocalHit(hit.point, out Vector2 previewSurfaceUv))
            {
                _brushPreviewRenderer.enabled = false;
                return;
            }

            previewCanvasUv = SurfaceUvToCanvasUv(previewSurfaceUv);
        }

        if (!IsCanvasUvInPaintArea(previewCanvasUv))
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

    private void CleanupRuntimeDrawingSurfaceCollider()
    {
        if (_runtimeDrawingSurfaceCollider == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(_runtimeDrawingSurfaceCollider);
        }
        else
        {
            DestroyImmediate(_runtimeDrawingSurfaceCollider);
        }

        _runtimeDrawingSurfaceCollider = null;
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
        if (boardRenderer == null)
        {
            boardRenderer = GetComponent<Renderer>();
        }

        if (drawingCamera == null)
        {
            drawingCamera = Camera.main;
        }

        drawingSurfaceCollider = ResolveDrawingSurfaceCollider();
    }

    private void EnsureRuntimeReady()
    {
        ResolveRuntimeReferences();

        if ((_canvas == null || _guideCanvas == null || _displayCanvas == null) &&
            boardRenderer != null &&
            drawingSurfaceCollider != null)
        {
            InitializeCanvas();
        }

        if (_brushPreviewRenderer == null && boardRenderer != null)
        {
            InitializeBrushPreview();
        }
    }

    private Collider ResolveDrawingSurfaceCollider()
    {
        if (drawingSurfaceCollider is MeshCollider meshCollider && meshCollider.sharedMesh != null)
        {
            return meshCollider;
        }

        if (TryGetComponent(out MeshCollider existingMeshCollider) && existingMeshCollider.sharedMesh != null)
        {
            return existingMeshCollider;
        }

        MeshFilter meshFilter = null;
        if (boardRenderer != null)
        {
            meshFilter = boardRenderer.GetComponent<MeshFilter>();
        }

        if (meshFilter == null)
        {
            meshFilter = GetComponent<MeshFilter>();
        }

        // Drawing needs mesh UVs; a runtime MeshCollider preserves the actual board mapping.
        if (meshFilter != null && meshFilter.sharedMesh != null)
        {
            if (_runtimeDrawingSurfaceCollider == null)
            {
                _runtimeDrawingSurfaceCollider = gameObject.AddComponent<MeshCollider>();
                _runtimeDrawingSurfaceCollider.hideFlags = RuntimeHideFlags;
            }

            _runtimeDrawingSurfaceCollider.sharedMesh = meshFilter.sharedMesh;
            _runtimeDrawingSurfaceCollider.convex = false;
            return _runtimeDrawingSurfaceCollider;
        }

        return drawingSurfaceCollider != null ? drawingSurfaceCollider : GetComponent<Collider>();
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
