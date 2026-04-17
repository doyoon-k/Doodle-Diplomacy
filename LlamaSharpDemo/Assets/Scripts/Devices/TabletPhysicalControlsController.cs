using DoodleDiplomacy.Core;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace DoodleDiplomacy.Devices
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-250)]
    public sealed class TabletPhysicalControlsController : MonoBehaviour
    {
        private enum ControlTarget
        {
            None = 0,
            Brush = 1,
            Fill = 2,
            Eraser = 3,
            SizeSmall = 4,
            SizeMedium = 5,
            SizeLarge = 6,
            Undo = 7,
            Redo = 8,
            Clear = 9,
            Spectrum = 10,
            Knob = 11
        }

        private const int SmallBrushSize = 3;
        private const int MediumBrushSize = 6;
        private const int LargeBrushSize = 12;
        private const int SpectrumSnapCount = 32;

        [Header("References")]
        [SerializeField] private DrawingBoardController drawingBoard;
        [SerializeField] private UnityEngine.Camera inputCamera;
        [SerializeField] private RoundManager roundManager;
        [SerializeField] private GameObject controlsRoot;

        [Header("Input")]
        [SerializeField] private LayerMask controlLayerMask = ~0;
        [SerializeField] private float controlRaycastDistance = 100f;

        [Header("Tool Buttons")]
        [SerializeField] private Collider brushButtonCollider;
        [SerializeField] private Collider fillButtonCollider;
        [SerializeField] private Collider eraserButtonCollider;
        [SerializeField] private Renderer brushButtonRenderer;
        [SerializeField] private Renderer fillButtonRenderer;
        [SerializeField] private Renderer eraserButtonRenderer;

        [Header("Brush Size Buttons")]
        [SerializeField] private Collider sizeSmallCollider;
        [SerializeField] private Collider sizeMediumCollider;
        [SerializeField] private Collider sizeLargeCollider;
        [SerializeField] private Renderer sizeSmallRenderer;
        [SerializeField] private Renderer sizeMediumRenderer;
        [SerializeField] private Renderer sizeLargeRenderer;

        [Header("History Buttons")]
        [SerializeField] private Collider undoButtonCollider;
        [SerializeField] private Collider redoButtonCollider;
        [SerializeField] private Collider clearButtonCollider;
        [SerializeField] private Renderer undoButtonRenderer;
        [SerializeField] private Renderer redoButtonRenderer;
        [SerializeField] private Renderer clearButtonRenderer;

        [Header("Spectrum")]
        [SerializeField] private Collider spectrumBarCollider;
        [SerializeField] private Collider spectrumKnobCollider;
        [SerializeField] private Transform spectrumKnob;
        [SerializeField] private Transform spectrumTrackStart;
        [SerializeField] private Transform spectrumTrackEnd;
        [SerializeField] private Renderer spectrumRenderer;
        [SerializeField] private Renderer spectrumKnobRenderer;
        [SerializeField] private string spectrumTextureProperty = "_BaseMap";
        [SerializeField] private Texture2D spectrumColorTexture;

        [Header("Visuals")]
        [SerializeField] private Color neutralButtonColor = new(0.25f, 0.25f, 0.28f, 1f);
        [SerializeField] private Color activeToolColor = new(0.15f, 0.59f, 0.49f, 1f);
        [SerializeField] private Color activeEraserColor = new(0.80f, 0.36f, 0.18f, 1f);
        [SerializeField] private Color activeSizeColor = new(0.18f, 0.46f, 0.78f, 1f);
        [SerializeField] private Color disabledButtonColor = new(0.16f, 0.17f, 0.20f, 0.75f);
        [SerializeField] private Color clearButtonColor = new(0.80f, 0.36f, 0.18f, 1f);

        private readonly int[] _brushSizePresets = { SmallBrushSize, MediumBrushSize, LargeBrushSize };
        private MaterialPropertyBlock _rendererPropertyBlock;
        private Texture2D _spectrumTexture;
        private Texture2D _boundSpectrumSource;
        private bool _controlsActive;
        private bool _isDraggingSpectrum;
        private bool _suppressBoardUntilRelease;
        private float _spectrumPositionNormalized;
        private Color _selectedColor = Color.black;

        public void Initialize(DrawingBoardController targetDrawingBoard)
        {
            if (targetDrawingBoard != null)
            {
                drawingBoard = targetDrawingBoard;
            }

            roundManager ??= RoundManager.Instance ?? FindFirstObjectByType<RoundManager>();
            inputCamera ??= UnityEngine.Camera.main ?? FindFirstObjectByType<UnityEngine.Camera>();

            EnsureSpectrumTexture();
            SubscribeDrawingBoard();
            SyncFromDrawingBoard();
            RefreshVisuals();
        }

        private void Awake()
        {
            TabletGlowController glowController = GetComponent<TabletGlowController>();
            if (glowController == null)
            {
                glowController = gameObject.AddComponent<TabletGlowController>();
            }
            glowController.EnsureNow();

            if (drawingBoard == null)
            {
                drawingBoard = GetComponent<DrawingBoardController>();
            }

            Initialize(drawingBoard);
        }

        private void OnEnable()
        {
            SubscribeDrawingBoard();
            SyncFromDrawingBoard();
            RefreshStateFromRoundManager();
            RefreshVisuals();
        }

        private void OnDisable()
        {
            UnsubscribeDrawingBoard();
            EndPointerCapture();
            SetControlsVisible(false);
        }

        private void OnDestroy()
        {
            if (_spectrumTexture == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(_spectrumTexture);
            }
            else
            {
                DestroyImmediate(_spectrumTexture);
            }

            _boundSpectrumSource = null;
        }

        private void Update()
        {
            RefreshStateFromRoundManager();
            if (!_controlsActive || drawingBoard == null)
            {
                return;
            }

            if (inputCamera == null)
            {
                inputCamera = UnityEngine.Camera.main ?? FindFirstObjectByType<UnityEngine.Camera>();
            }

            if (!ReadMouseState(
                    inputCamera,
                    out Vector2 pointerScreenPos,
                    out bool pointerDown,
                    out bool pointerHeld,
                    out bool pointerUp))
            {
                return;
            }

            if (pointerDown && TryRaycastControl(pointerScreenPos, out ControlTarget target))
            {
                HandleControlPressed(target, pointerScreenPos);
            }

            if (_isDraggingSpectrum && pointerHeld)
            {
                UpdateSpectrumFromPointer(pointerScreenPos);
            }

            if (pointerUp)
            {
                EndPointerCapture();
            }

            SetBoardInteractionLocked(_controlsActive && (_isDraggingSpectrum || _suppressBoardUntilRelease));
        }

        public void OnGameStateChanged(GameState state)
        {
            bool shouldEnable = state == GameState.Drawing;
            if (_controlsActive == shouldEnable)
            {
                RefreshVisuals();
                return;
            }

            _controlsActive = shouldEnable;
            SetControlsVisible(_controlsActive);
            if (!_controlsActive)
            {
                EndPointerCapture();
                SetBoardInteractionLocked(true);
            }
            else
            {
                SetBoardInteractionLocked(false);
            }

            RefreshVisuals();
        }

        private void RefreshStateFromRoundManager()
        {
            roundManager ??= RoundManager.Instance ?? FindFirstObjectByType<RoundManager>();
            GameState state = roundManager != null ? roundManager.CurrentState : GameState.Title;
            OnGameStateChanged(state);
        }

        private void HandleControlPressed(ControlTarget target, Vector2 pointerScreenPos)
        {
            if (drawingBoard == null)
            {
                return;
            }

            switch (target)
            {
                case ControlTarget.Brush:
                    drawingBoard.SetToolMode(DrawingToolMode.Brush);
                    _suppressBoardUntilRelease = true;
                    break;

                case ControlTarget.Fill:
                    drawingBoard.SetToolMode(DrawingToolMode.Fill);
                    _suppressBoardUntilRelease = true;
                    break;

                case ControlTarget.Eraser:
                    drawingBoard.SetToolMode(DrawingToolMode.Eraser);
                    _suppressBoardUntilRelease = true;
                    break;

                case ControlTarget.SizeSmall:
                    drawingBoard.SetBrushRadius(SmallBrushSize);
                    _suppressBoardUntilRelease = true;
                    break;

                case ControlTarget.SizeMedium:
                    drawingBoard.SetBrushRadius(MediumBrushSize);
                    _suppressBoardUntilRelease = true;
                    break;

                case ControlTarget.SizeLarge:
                    drawingBoard.SetBrushRadius(LargeBrushSize);
                    _suppressBoardUntilRelease = true;
                    break;

                case ControlTarget.Undo:
                    drawingBoard.Undo();
                    _suppressBoardUntilRelease = true;
                    break;

                case ControlTarget.Redo:
                    drawingBoard.Redo();
                    _suppressBoardUntilRelease = true;
                    break;

                case ControlTarget.Clear:
                    drawingBoard.ClearCanvas();
                    _suppressBoardUntilRelease = true;
                    break;

                case ControlTarget.Knob:
                case ControlTarget.Spectrum:
                    _isDraggingSpectrum = true;
                    _suppressBoardUntilRelease = true;
                    UpdateSpectrumFromPointer(pointerScreenPos);
                    break;
            }

            RefreshVisuals();
        }

        private bool TryRaycastControl(Vector2 pointerScreenPos, out ControlTarget target)
        {
            target = ControlTarget.None;
            if (inputCamera == null)
            {
                inputCamera = UnityEngine.Camera.main ?? FindFirstObjectByType<UnityEngine.Camera>();
                if (inputCamera == null)
                {
                    return false;
                }
            }

            Ray ray = inputCamera.ScreenPointToRay(pointerScreenPos);
            if (!Physics.Raycast(ray, out RaycastHit hit, controlRaycastDistance, controlLayerMask))
            {
                return false;
            }

            target = ResolveControlTarget(hit.collider);
            return target != ControlTarget.None;
        }

        private ControlTarget ResolveControlTarget(Collider hitCollider)
        {
            if (MatchesCollider(hitCollider, brushButtonCollider))
            {
                return ControlTarget.Brush;
            }

            if (MatchesCollider(hitCollider, fillButtonCollider))
            {
                return ControlTarget.Fill;
            }

            if (MatchesCollider(hitCollider, eraserButtonCollider))
            {
                return ControlTarget.Eraser;
            }

            if (MatchesCollider(hitCollider, sizeSmallCollider))
            {
                return ControlTarget.SizeSmall;
            }

            if (MatchesCollider(hitCollider, sizeMediumCollider))
            {
                return ControlTarget.SizeMedium;
            }

            if (MatchesCollider(hitCollider, sizeLargeCollider))
            {
                return ControlTarget.SizeLarge;
            }

            if (MatchesCollider(hitCollider, undoButtonCollider))
            {
                return ControlTarget.Undo;
            }

            if (MatchesCollider(hitCollider, redoButtonCollider))
            {
                return ControlTarget.Redo;
            }

            if (MatchesCollider(hitCollider, clearButtonCollider))
            {
                return ControlTarget.Clear;
            }

            if (MatchesCollider(hitCollider, spectrumKnobCollider))
            {
                return ControlTarget.Knob;
            }

            if (MatchesCollider(hitCollider, spectrumBarCollider))
            {
                return ControlTarget.Spectrum;
            }

            return ControlTarget.None;
        }

        private void UpdateSpectrumFromPointer(Vector2 pointerScreenPos)
        {
            if (drawingBoard == null || spectrumTrackStart == null || spectrumTrackEnd == null)
            {
                return;
            }

            Vector3 sampledWorldPoint;
            Ray ray = inputCamera.ScreenPointToRay(pointerScreenPos);
            if (spectrumBarCollider != null && spectrumBarCollider.Raycast(ray, out RaycastHit hit, controlRaycastDistance))
            {
                sampledWorldPoint = hit.point;
            }
            else
            {
                sampledWorldPoint = ClosestPointOnSegmentToRay(
                    spectrumTrackStart.position,
                    spectrumTrackEnd.position,
                    ray);
            }

            float normalizedPosition = EvaluateTrackPositionNormalized(sampledWorldPoint);
            ApplySpectrumPosition(normalizedPosition, applyToDrawingBoard: true);
        }

        private void ApplySpectrumPosition(float normalizedPosition, bool applyToDrawingBoard)
        {
            _spectrumPositionNormalized = GetSnappedNormalized(normalizedPosition);
            Color spectrumColor = EvaluateSpectrumColor(_spectrumPositionNormalized);
            _selectedColor = spectrumColor;
            UpdateKnobPose();

            if (applyToDrawingBoard && drawingBoard != null)
            {
                DrawingToolMode modeBeforeChange = drawingBoard.GetCurrentToolMode();
                drawingBoard.SetBrushColor(spectrumColor);
                if (modeBeforeChange == DrawingToolMode.Eraser)
                {
                    drawingBoard.SetToolMode(DrawingToolMode.Eraser);
                }
            }

            RefreshVisuals();
        }

        private void SyncFromDrawingBoard()
        {
            if (drawingBoard == null)
            {
                return;
            }

            _selectedColor = drawingBoard.BrushColor;
            _spectrumPositionNormalized = EvaluateSpectrumPositionFromColor(_selectedColor);
            UpdateKnobPose();
        }

        private void UpdateKnobPose()
        {
            if (spectrumKnob == null || spectrumTrackStart == null || spectrumTrackEnd == null)
            {
                return;
            }

            spectrumKnob.position = Vector3.Lerp(
                spectrumTrackStart.position,
                spectrumTrackEnd.position,
                _spectrumPositionNormalized);
        }

        private float EvaluateTrackPositionNormalized(Vector3 worldPoint)
        {
            if (spectrumTrackStart == null || spectrumTrackEnd == null)
            {
                return 0f;
            }

            Vector3 start = spectrumTrackStart.position;
            Vector3 end = spectrumTrackEnd.position;
            Vector3 direction = end - start;
            float directionLengthSquared = direction.sqrMagnitude;
            if (directionLengthSquared < 0.000001f)
            {
                return 0f;
            }

            float projection = Vector3.Dot(worldPoint - start, direction) / directionLengthSquared;
            return Mathf.Clamp01(projection);
        }

        private float EvaluateSpectrumPositionFromColor(Color color)
        {
            int bestIndex = 0;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < SpectrumSnapCount; i++)
            {
                Color candidate = GetSpectrumColorByIndex(i);
                float distance = EvaluateColorDistanceSquared(color, candidate);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestIndex = i;
                }
            }

            return GetSnappedNormalized(bestIndex);
        }

        private Color EvaluateSpectrumColor(float normalizedPosition)
        {
            int snappedIndex = GetSnappedIndex(normalizedPosition);
            return GetSpectrumColorByIndex(snappedIndex);
        }

        private void SetControlsVisible(bool visible)
        {
            if (controlsRoot != null)
            {
                controlsRoot.SetActive(visible);
                return;
            }

            SetColliderEnabled(brushButtonCollider, visible);
            SetColliderEnabled(fillButtonCollider, visible);
            SetColliderEnabled(eraserButtonCollider, visible);
            SetColliderEnabled(sizeSmallCollider, visible);
            SetColliderEnabled(sizeMediumCollider, visible);
            SetColliderEnabled(sizeLargeCollider, visible);
            SetColliderEnabled(undoButtonCollider, visible);
            SetColliderEnabled(redoButtonCollider, visible);
            SetColliderEnabled(clearButtonCollider, visible);
            SetColliderEnabled(spectrumBarCollider, visible);
            SetColliderEnabled(spectrumKnobCollider, visible);
        }

        private void EnsureSpectrumTexture()
        {
            if (spectrumRenderer == null || spectrumColorTexture == null)
            {
                return;
            }

            SetRendererTexture(spectrumRenderer, spectrumTextureProperty, spectrumColorTexture);

            bool canReuseCache =
                _spectrumTexture != null &&
                _spectrumTexture.width == spectrumColorTexture.width &&
                _spectrumTexture.height == spectrumColorTexture.height;

            if (!canReuseCache)
            {
                if (_spectrumTexture != null)
                {
                    if (Application.isPlaying)
                    {
                        Destroy(_spectrumTexture);
                    }
                    else
                    {
                        DestroyImmediate(_spectrumTexture);
                    }
                }

                _spectrumTexture = new Texture2D(
                    spectrumColorTexture.width,
                    spectrumColorTexture.height,
                    TextureFormat.RGBA32,
                    false)
                {
                    name = "TabletSpectrumRuntime",
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                };
            }

            if (spectrumColorTexture.isReadable)
            {
                _spectrumTexture.SetPixels32(spectrumColorTexture.GetPixels32());
            }
            else
            {
                RenderTexture previous = RenderTexture.active;
                RenderTexture temporary = RenderTexture.GetTemporary(
                    spectrumColorTexture.width,
                    spectrumColorTexture.height,
                    0,
                    RenderTextureFormat.ARGB32);

                Graphics.Blit(spectrumColorTexture, temporary);
                RenderTexture.active = temporary;
                _spectrumTexture.ReadPixels(
                    new Rect(0, 0, temporary.width, temporary.height),
                    0,
                    0,
                    false);
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(temporary);
            }

            _spectrumTexture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            _boundSpectrumSource = spectrumColorTexture;
        }

        private void RefreshVisuals()
        {
            if (drawingBoard == null)
            {
                return;
            }

            DrawingToolMode mode = drawingBoard.GetCurrentToolMode();
            SetRendererColor(brushButtonRenderer, mode == DrawingToolMode.Brush ? activeToolColor : neutralButtonColor);
            SetRendererColor(fillButtonRenderer, mode == DrawingToolMode.Fill ? activeToolColor : neutralButtonColor);
            SetRendererColor(eraserButtonRenderer, mode == DrawingToolMode.Eraser ? activeEraserColor : neutralButtonColor);

            int nearestPreset = ResolveNearestBrushPreset(drawingBoard.BrushRadius);
            SetRendererColor(sizeSmallRenderer, nearestPreset == SmallBrushSize ? activeSizeColor : neutralButtonColor);
            SetRendererColor(sizeMediumRenderer, nearestPreset == MediumBrushSize ? activeSizeColor : neutralButtonColor);
            SetRendererColor(sizeLargeRenderer, nearestPreset == LargeBrushSize ? activeSizeColor : neutralButtonColor);

            SetRendererColor(undoButtonRenderer, drawingBoard.CanUndo ? neutralButtonColor : disabledButtonColor);
            SetRendererColor(redoButtonRenderer, drawingBoard.CanRedo ? neutralButtonColor : disabledButtonColor);
            SetRendererColor(clearButtonRenderer, clearButtonColor);
            SetRendererColor(spectrumKnobRenderer, _selectedColor);
        }

        private int ResolveNearestBrushPreset(int brushSize)
        {
            int selected = _brushSizePresets[0];
            int smallestDistance = int.MaxValue;
            for (int i = 0; i < _brushSizePresets.Length; i++)
            {
                int preset = _brushSizePresets[i];
                int distance = Mathf.Abs(brushSize - preset);
                if (distance < smallestDistance)
                {
                    smallestDistance = distance;
                    selected = preset;
                }
            }

            return selected;
        }

        private void SubscribeDrawingBoard()
        {
            if (drawingBoard == null)
            {
                return;
            }

            drawingBoard.BrushRadiusChanged -= OnDrawingBoardChanged;
            drawingBoard.BrushRadiusChanged += OnDrawingBoardChanged;
            drawingBoard.HistoryStateChanged -= OnDrawingBoardHistoryChanged;
            drawingBoard.HistoryStateChanged += OnDrawingBoardHistoryChanged;
            drawingBoard.SketchGuideStateChanged -= OnDrawingBoardSketchGuideChanged;
            drawingBoard.SketchGuideStateChanged += OnDrawingBoardSketchGuideChanged;
        }

        private void UnsubscribeDrawingBoard()
        {
            if (drawingBoard == null)
            {
                return;
            }

            drawingBoard.BrushRadiusChanged -= OnDrawingBoardChanged;
            drawingBoard.HistoryStateChanged -= OnDrawingBoardHistoryChanged;
            drawingBoard.SketchGuideStateChanged -= OnDrawingBoardSketchGuideChanged;
        }

        private void OnDrawingBoardChanged(int _)
        {
            RefreshVisuals();
        }

        private void OnDrawingBoardHistoryChanged(bool _, bool __)
        {
            RefreshVisuals();
        }

        private void OnDrawingBoardSketchGuideChanged(bool _, bool __)
        {
            RefreshVisuals();
        }

        private void EndPointerCapture()
        {
            _isDraggingSpectrum = false;
            _suppressBoardUntilRelease = false;
        }

        private void SetBoardInteractionLocked(bool locked)
        {
            drawingBoard?.SetInteractionLocked(locked);
        }

        private static void SetColliderEnabled(Collider collider, bool enabled)
        {
            if (collider != null)
            {
                collider.enabled = enabled;
            }
        }

        private bool MatchesCollider(Collider hitCollider, Collider targetCollider)
        {
            if (hitCollider == null || targetCollider == null)
            {
                return false;
            }

            return hitCollider == targetCollider || hitCollider.transform.IsChildOf(targetCollider.transform);
        }

        private void SetRendererColor(Renderer targetRenderer, Color color)
        {
            if (targetRenderer == null)
            {
                return;
            }

            _rendererPropertyBlock ??= new MaterialPropertyBlock();
            targetRenderer.GetPropertyBlock(_rendererPropertyBlock);
            _rendererPropertyBlock.SetColor("_BaseColor", color);
            _rendererPropertyBlock.SetColor("_Color", color);
            targetRenderer.SetPropertyBlock(_rendererPropertyBlock);
        }

        private void SetRendererTexture(Renderer targetRenderer, string texturePropertyName, Texture texture)
        {
            if (targetRenderer == null || string.IsNullOrEmpty(texturePropertyName))
            {
                return;
            }

            _rendererPropertyBlock ??= new MaterialPropertyBlock();
            targetRenderer.GetPropertyBlock(_rendererPropertyBlock);
            _rendererPropertyBlock.SetTexture(texturePropertyName, texture);
            targetRenderer.SetPropertyBlock(_rendererPropertyBlock);
        }

        private static float EvaluateColorDistanceSquared(Color a, Color b)
        {
            float dr = a.r - b.r;
            float dg = a.g - b.g;
            float db = a.b - b.b;
            return (dr * dr) + (dg * dg) + (db * db);
        }

        private static int GetSnappedIndex(float normalizedPosition)
        {
            int maxIndex = SpectrumSnapCount - 1;
            if (maxIndex <= 0)
            {
                return 0;
            }

            float t = Mathf.Clamp01(normalizedPosition);
            return Mathf.Clamp(Mathf.RoundToInt(t * maxIndex), 0, maxIndex);
        }

        private static float GetSnappedNormalized(float normalizedPosition)
        {
            return GetSnappedNormalized(GetSnappedIndex(normalizedPosition));
        }

        private static float GetSnappedNormalized(int snappedIndex)
        {
            int maxIndex = SpectrumSnapCount - 1;
            if (maxIndex <= 0)
            {
                return 0f;
            }

            int clampedIndex = Mathf.Clamp(snappedIndex, 0, maxIndex);
            return clampedIndex / (float)maxIndex;
        }

        private Color GetSpectrumColorByIndex(int snappedIndex)
        {
            Texture2D sampleTexture = _spectrumTexture;
            if (sampleTexture == null || sampleTexture.width <= 0 || sampleTexture.height <= 0)
            {
                return Color.black;
            }

            float normalized = GetSnappedNormalized(snappedIndex);
            int x = Mathf.Clamp(Mathf.RoundToInt(normalized * (sampleTexture.width - 1)), 0, sampleTexture.width - 1);
            int y = Mathf.Clamp(sampleTexture.height / 2, 0, sampleTexture.height - 1);
            return sampleTexture.GetPixel(x, y);
        }

        private static Vector3 ClosestPointOnSegmentToRay(Vector3 segmentStart, Vector3 segmentEnd, Ray ray)
        {
            Vector3 segmentDirection = segmentEnd - segmentStart;
            Vector3 rayDirection = ray.direction;
            Vector3 w0 = segmentStart - ray.origin;

            float a = Vector3.Dot(segmentDirection, segmentDirection);
            float b = Vector3.Dot(segmentDirection, rayDirection);
            float c = Vector3.Dot(rayDirection, rayDirection);
            float d = Vector3.Dot(segmentDirection, w0);
            float e = Vector3.Dot(rayDirection, w0);

            float denominator = (a * c) - (b * b);
            float segmentT = denominator > 0.00001f
                ? Mathf.Clamp01((b * e - c * d) / denominator)
                : 0f;

            return segmentStart + (segmentDirection * segmentT);
        }

        private static bool ReadMouseState(
            UnityEngine.Camera referenceCamera,
            out Vector2 pointerScreenPos,
            out bool pointerDown,
            out bool pointerHeld,
            out bool pointerUp)
        {
            pointerScreenPos = default;
            pointerDown = false;
            pointerHeld = false;
            pointerUp = false;

#if ENABLE_INPUT_SYSTEM
            Mouse mouse = Mouse.current;
            if (mouse != null)
            {
                pointerScreenPos = mouse.position.ReadValue();
                pointerDown = mouse.leftButton.wasPressedThisFrame;
                pointerHeld = mouse.leftButton.isPressed;
                pointerUp = mouse.leftButton.wasReleasedThisFrame;
                return IsPointerWithinCameraBounds(referenceCamera, pointerScreenPos);
            }
#endif

            pointerScreenPos = Input.mousePosition;
            pointerDown = Input.GetMouseButtonDown(0);
            pointerHeld = Input.GetMouseButton(0);
            pointerUp = Input.GetMouseButtonUp(0);

            return IsPointerWithinCameraBounds(referenceCamera, pointerScreenPos);
        }

        private static bool IsPointerWithinCameraBounds(UnityEngine.Camera referenceCamera, Vector2 pointerScreenPos)
        {
            if (float.IsNaN(pointerScreenPos.x) || float.IsNaN(pointerScreenPos.y))
            {
                return false;
            }

            if (Screen.width <= 0 || Screen.height <= 0)
            {
                return false;
            }

            if (pointerScreenPos.x < 0f || pointerScreenPos.x > Screen.width ||
                pointerScreenPos.y < 0f || pointerScreenPos.y > Screen.height)
            {
                return false;
            }

            Rect pixelRect = referenceCamera != null && referenceCamera.pixelRect.width > 0f && referenceCamera.pixelRect.height > 0f
                ? referenceCamera.pixelRect
                : new Rect(0f, 0f, Screen.width, Screen.height);
            return pointerScreenPos.x >= pixelRect.xMin &&
                   pointerScreenPos.x <= pixelRect.xMax &&
                   pointerScreenPos.y >= pixelRect.yMin &&
                   pointerScreenPos.y <= pixelRect.yMax;
        }
    }
}
