using System.Collections.Generic;
using DoodleDiplomacy.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace DoodleDiplomacy.Devices
{
    [DisallowMultipleComponent]
    public sealed class TabletDrawingHudController : MonoBehaviour
    {
        private static readonly Color[] DefaultPaletteColors =
        {
            new(0.07f, 0.07f, 0.07f, 1f),
            new(0.36f, 0.36f, 0.36f, 1f),
            new(1f, 1f, 1f, 1f),
            new(0.91f, 0.19f, 0.18f, 1f),
            new(0.98f, 0.53f, 0.15f, 1f),
            new(0.97f, 0.84f, 0.18f, 1f),
            new(0.22f, 0.69f, 0.30f, 1f),
            new(0.16f, 0.56f, 0.92f, 1f)
        };

        private sealed class PaletteSwatch
        {
            public int SlotIndex;
            public bool IsCustomSlot;
            public bool IsAssigned;
            public Color32 SwatchColor;
            public Button Button;
            public Image BorderImage;
            public Image FillImage;
            public Text LabelText;
        }

        private const int CustomSlotCount = 4;
        private const float CanvasWidth = 1320f;
        private const float CanvasHeight = 820f;
        private const float PanelWidth = 360f;
        private const float PanelHeight = 432f;
        private const float CustomPanelWidth = 292f;
        private const float CustomPanelHeight = 224f;
        private const float SurfaceMarginX = 0.94f;
        private const float SurfaceMarginY = 0.88f;
        private const float SurfaceOffset = 0.012f;

        [Header("References")]
        [SerializeField] private DrawingBoardController drawingBoard;
        [SerializeField] private RoundManager roundManager;
        [SerializeField] private UnityEngine.Camera uiCamera;

        [Header("Placement")]
        [SerializeField] private bool autoFitCanvasToSurface = true;
        [SerializeField] private bool preserveSquareCanvasPixels = true;
        [SerializeField] private bool applyPlacementToSceneAuthoredCanvas = false;

        [Header("Theme")]
        [SerializeField] private Color panelColor = new(0.09f, 0.11f, 0.14f, 0.94f);
        [SerializeField] private Color sectionColor = new(0.16f, 0.18f, 0.23f, 1f);
        [SerializeField] private Color activeButtonColor = new(0.15f, 0.59f, 0.49f, 1f);
        [SerializeField] private Color neutralButtonColor = new(0.22f, 0.24f, 0.29f, 1f);
        [SerializeField] private Color warningButtonColor = new(0.80f, 0.36f, 0.18f, 1f);
        [SerializeField] private Color primaryButtonColor = new(0.18f, 0.46f, 0.78f, 1f);
        [SerializeField] private Color disabledButtonColor = new(0.16f, 0.17f, 0.20f, 0.75f);
        [SerializeField] private Color swatchBorderColor = new(0.52f, 0.56f, 0.63f, 1f);
        [SerializeField] private Color swatchSelectedColor = new(0.92f, 0.95f, 0.98f, 1f);
        [SerializeField] private Color emptySwatchColor = new(1f, 1f, 1f, 0.08f);

        private readonly List<PaletteSwatch> _paletteSwatches = new();
        private readonly Color32[] _customSlotColors = new Color32[CustomSlotCount];
        private readonly bool[] _customSlotAssigned = new bool[CustomSlotCount];

        private GameObject _canvasRootObject;
        private Canvas _canvas;
        private RectTransform _mainPanel;
        private RectTransform _customPanel;
        private Image _activeColorPreview;
        private Text _toolStatusText;
        private Button _eraserButton;
        private Text _eraserButtonText;
        private Image _eraserButtonImage;
        private Button _fillButton;
        private Text _fillButtonText;
        private Image _fillButtonImage;
        private Button _customButton;
        private Image _customButtonImage;
        private Image _customButtonPreview;
        private Button _undoButton;
        private Image _undoButtonImage;
        private Button _redoButton;
        private Image _redoButtonImage;
        private Button _clearButton;
        private Image _clearButtonImage;
        private Button _doneButton;
        private TabletBrushSizeSlider _brushSizeSlider;
        private Text _brushSizeValueText;
        private Image _customColorPreview;
        private Text _customSlotLabelText;
        private DrawingColorPickerController _customColorPicker;

        private bool _uiBuilt;
        private bool _isVisible;
        private bool _customPanelVisible;
        private bool _ownsRuntimeUi;
        private int _selectedCustomSlotIndex = -1;
        private Color _customColor = Color.black;

        public void Initialize(DrawingBoardController targetDrawingBoard)
        {
            if (targetDrawingBoard != null)
            {
                drawingBoard = targetDrawingBoard;
            }

            roundManager ??= RoundManager.Instance ?? FindFirstObjectByType<RoundManager>();
            uiCamera ??= UnityEngine.Camera.main ?? FindFirstObjectByType<UnityEngine.Camera>();
            EnsureUiBuilt();
            SyncCustomColorFromBrush();
            SyncUi();
            SetHudVisible(false);
        }

        private void Awake()
        {
            if (drawingBoard == null)
            {
                drawingBoard = GetComponent<DrawingBoardController>();
            }

            roundManager ??= RoundManager.Instance ?? FindFirstObjectByType<RoundManager>();
            uiCamera ??= UnityEngine.Camera.main ?? FindFirstObjectByType<UnityEngine.Camera>();
            EnsureUiBuilt();
            SyncCustomColorFromBrush();
            SyncUi();
            SetHudVisible(false);
        }

        private void OnEnable()
        {
            SubscribeToDrawingBoard();
        }

        private void OnDisable()
        {
            UnsubscribeFromDrawingBoard();
        }

        private void OnDestroy()
        {
            UnsubscribeFromDrawingBoard();

            if (_canvasRootObject == null || !_ownsRuntimeUi)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(_canvasRootObject);
            }
            else
            {
                DestroyImmediate(_canvasRootObject);
            }
        }

        public void OnGameStateChanged(GameState state)
        {
            bool shouldShow = state == GameState.Drawing;
            if (!shouldShow)
            {
                SetCustomPanelVisible(false);
            }

            SetHudVisible(shouldShow);
            SyncUi();
        }

        private void SubscribeToDrawingBoard()
        {
            if (drawingBoard == null)
            {
                return;
            }

            drawingBoard.BrushRadiusChanged -= OnBrushRadiusChanged;
            drawingBoard.BrushRadiusChanged += OnBrushRadiusChanged;
            drawingBoard.HistoryStateChanged -= OnHistoryStateChanged;
            drawingBoard.HistoryStateChanged += OnHistoryStateChanged;
            drawingBoard.SketchGuideStateChanged -= OnToolStateChanged;
            drawingBoard.SketchGuideStateChanged += OnToolStateChanged;
        }

        private void UnsubscribeFromDrawingBoard()
        {
            if (drawingBoard == null)
            {
                return;
            }

            drawingBoard.BrushRadiusChanged -= OnBrushRadiusChanged;
            drawingBoard.HistoryStateChanged -= OnHistoryStateChanged;
            drawingBoard.SketchGuideStateChanged -= OnToolStateChanged;
        }

        private void EnsureUiBuilt()
        {
            if (_uiBuilt)
            {
                RefreshCanvasPlacement();
                RefreshCanvasCamera();
                return;
            }

            EnsureEventSystem();
            if (!TryBindExistingUi())
            {
                BuildCanvas();
                BuildMainPanel();
                BuildCustomPanel();
            }

            BuildPaletteGrid();
            BindControls();
            RefreshCanvasPlacement();
            RefreshCanvasCamera();
            _uiBuilt = true;
        }

        private void BuildCanvas()
        {
            _ownsRuntimeUi = true;
            _canvasRootObject = new GameObject(
                "TabletDrawingHudCanvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));

            _canvasRootObject.transform.SetParent(transform, false);

            _canvas = _canvasRootObject.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            _canvas.overrideSorting = true;
            _canvas.sortingOrder = 400;
            _canvas.worldCamera = uiCamera;

            CanvasScaler scaler = _canvasRootObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

            RectTransform canvasRect = (RectTransform)_canvas.transform;
            canvasRect.anchorMin = new Vector2(0.5f, 0.5f);
            canvasRect.anchorMax = new Vector2(0.5f, 0.5f);
            canvasRect.pivot = new Vector2(0.5f, 0.5f);
            canvasRect.sizeDelta = new Vector2(CanvasWidth, CanvasHeight);
            canvasRect.localRotation = Quaternion.Euler(90f, 0f, 0f);
        }

        private bool TryBindExistingUi()
        {
            Transform existingRoot = transform.Find("TabletDrawingHudCanvas");
            if (existingRoot == null)
            {
                return false;
            }

            _canvasRootObject = existingRoot.gameObject;
            _canvas = existingRoot.GetComponent<Canvas>();
            _mainPanel = existingRoot.Find("DrawingHudPanel") as RectTransform;
            _customPanel = existingRoot.Find("CustomColorPanel") as RectTransform;

            if (_canvas == null || _mainPanel == null || _customPanel == null)
            {
                return false;
            }

            _activeColorPreview = FindNamedComponent<Image>(existingRoot, "ActiveColorPreview");
            _toolStatusText = FindNamedComponent<Text>(existingRoot, "ToolStatusText");
            _eraserButton = FindNamedComponent<Button>(existingRoot, "EraserButton");
            _eraserButtonText = FindNamedComponent<Text>(existingRoot, "EraserButtonText");
            _eraserButtonImage = _eraserButton != null ? _eraserButton.GetComponent<Image>() : null;
            _fillButton = FindNamedComponent<Button>(existingRoot, "FillButton");
            _fillButtonText = FindNamedComponent<Text>(existingRoot, "FillButtonText");
            _fillButtonImage = _fillButton != null ? _fillButton.GetComponent<Image>() : null;
            _customButton = FindNamedComponent<Button>(existingRoot, "CustomButton");
            _customButtonImage = _customButton != null ? _customButton.GetComponent<Image>() : null;
            _customButtonPreview = FindNamedComponent<Image>(existingRoot, "CustomButtonPreview");
            _undoButton = FindNamedComponent<Button>(existingRoot, "UndoButton");
            _undoButtonImage = _undoButton != null ? _undoButton.GetComponent<Image>() : null;
            _redoButton = FindNamedComponent<Button>(existingRoot, "RedoButton");
            _redoButtonImage = _redoButton != null ? _redoButton.GetComponent<Image>() : null;
            _clearButton = FindNamedComponent<Button>(existingRoot, "ClearButton");
            _clearButtonImage = _clearButton != null ? _clearButton.GetComponent<Image>() : null;
            _doneButton = FindNamedComponent<Button>(existingRoot, "DoneButton");
            RectTransform brushSizeSliderRoot = FindNamedComponent<RectTransform>(existingRoot, "BrushSizeSlider");
            _brushSizeSlider = ResolveBrushSizeSlider(brushSizeSliderRoot);
            _brushSizeValueText = FindNamedComponent<Text>(existingRoot, "BrushSizeValue");
            _customColorPreview = FindNamedComponent<Image>(existingRoot, "CustomColorPreview");
            _customSlotLabelText = FindNamedComponent<Text>(existingRoot, "CustomSlotLabel");
            _customColorPicker = _customPanel.GetComponent<DrawingColorPickerController>();
            _ownsRuntimeUi = false;
            return true;
        }

        private void BuildMainPanel()
        {
            _mainPanel = CreatePanel("DrawingHudPanel", (RectTransform)_canvas.transform, panelColor, new Vector2(20f, -20f), new Vector2(PanelWidth, PanelHeight));

            CreateText("HudTitleText", _mainPanel, "Tablet Paint", 20, FontStyle.Bold, TextAnchor.UpperLeft, new Vector2(16f, -16f), new Vector2(PanelWidth - 32f, 26f));
            CreateText("HudHintText", _mainPanel, "Pick a color, draw on the tablet, and use Fill for one tap only.", 13, FontStyle.Normal, TextAnchor.UpperLeft, new Vector2(16f, -44f), new Vector2(PanelWidth - 32f, 40f));

            RectTransform previewCard = CreatePanel("PreviewCard", _mainPanel, sectionColor, new Vector2(16f, -92f), new Vector2(98f, 98f));
            _activeColorPreview = CreateImage("ActiveColorPreview", previewCard, Color.black, new Vector2(21f, -14f), new Vector2(56f, 56f));
            _toolStatusText = CreateText("ToolStatusText", previewCard, "Brush", 13, FontStyle.Bold, TextAnchor.UpperCenter, new Vector2(8f, -72f), new Vector2(82f, 18f));

            CreateText("PaletteLabel", _mainPanel, "Palette + Custom", 13, FontStyle.Bold, TextAnchor.UpperLeft, new Vector2(126f, -92f), new Vector2(218f, 18f));
            RectTransform paletteGrid = CreateRect("PaletteGrid", _mainPanel, new Vector2(126f, -116f), new Vector2(218f, 72f));
            GridLayoutGroup grid = paletteGrid.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(32f, 32f);
            grid.spacing = new Vector2(4f, 4f);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 6;

            CreateText("BrushSizeLabel", _mainPanel, "Brush Size", 13, FontStyle.Bold, TextAnchor.UpperLeft, new Vector2(16f, -198f), new Vector2(120f, 18f));
            _brushSizeSlider = CreateSlider("BrushSizeSlider", _mainPanel, new Vector2(16f, -222f), new Vector2(188f, 20f), 1f, 24f);
            _brushSizeValueText = CreateText("BrushSizeValue", _mainPanel, "6", 15, FontStyle.Bold, TextAnchor.MiddleLeft, new Vector2(212f, -224f), new Vector2(28f, 20f));

            _eraserButton = CreateButton("EraserButton", _mainPanel, "Eraser", neutralButtonColor, new Vector2(16f, -262f), new Vector2(100f, 36f), out _eraserButtonText, out _eraserButtonImage);
            _fillButton = CreateButton("FillButton", _mainPanel, "Fill", neutralButtonColor, new Vector2(130f, -262f), new Vector2(100f, 36f), out _fillButtonText, out _fillButtonImage);
            _customButton = CreateButton("CustomButton", _mainPanel, "Custom", neutralButtonColor, new Vector2(244f, -262f), new Vector2(100f, 36f), out _, out _customButtonImage);
            _customButtonPreview = CreateImage("CustomButtonPreview", _customButton.transform as RectTransform, _customColor, new Vector2(8f, -8f), new Vector2(18f, 18f));

            _undoButton = CreateButton("UndoButton", _mainPanel, "Undo", neutralButtonColor, new Vector2(16f, -308f), new Vector2(100f, 36f), out _, out _undoButtonImage);
            _redoButton = CreateButton("RedoButton", _mainPanel, "Redo", neutralButtonColor, new Vector2(130f, -308f), new Vector2(100f, 36f), out _, out _redoButtonImage);
            _clearButton = CreateButton("ClearButton", _mainPanel, "Clear", warningButtonColor, new Vector2(244f, -308f), new Vector2(100f, 36f), out _, out _clearButtonImage);
            _doneButton = CreateButton("DoneButton", _mainPanel, "Done", primaryButtonColor, new Vector2(16f, -360f), new Vector2(328f, 42f), out _, out _);
        }

        private void BuildPaletteGrid()
        {
            Transform paletteGrid = _mainPanel.Find("PaletteGrid");
            if (paletteGrid == null)
            {
                return;
            }

            // Scene-authored palette entries can remain from older prefabs/layouts.
            // Clear the container first so runtime generation is deterministic.
            int childCount = paletteGrid.childCount;
            var existingChildren = new List<Transform>(childCount);
            for (int i = 0; i < childCount; i++)
            {
                existingChildren.Add(paletteGrid.GetChild(i));
            }

            for (int i = 0; i < existingChildren.Count; i++)
            {
                Transform child = existingChildren[i];
                if (child == null)
                {
                    continue;
                }

                child.SetParent(null, false);
                if (Application.isPlaying)
                {
                    Destroy(child.gameObject);
                }
                else
                {
                    DestroyImmediate(child.gameObject);
                }
            }

            _paletteSwatches.Clear();

            for (int i = 0; i < DefaultPaletteColors.Length; i++)
            {
                _paletteSwatches.Add(CreatePaletteSwatch(paletteGrid as RectTransform, i, false, true, DefaultPaletteColors[i]));
            }

            for (int i = 0; i < CustomSlotCount; i++)
            {
                _paletteSwatches.Add(CreatePaletteSwatch(paletteGrid as RectTransform, i, true, _customSlotAssigned[i], _customSlotAssigned[i] ? _customSlotColors[i] : emptySwatchColor));
            }
        }

        private void BuildCustomPanel()
        {
            _customPanel = CreatePanel("CustomColorPanel", (RectTransform)_canvas.transform, panelColor, new Vector2(20f, -472f), new Vector2(308f, 274f));

            CreateText("CustomPanelTitle", _customPanel, "Pick Any Color", 18, FontStyle.Bold, TextAnchor.UpperLeft, new Vector2(16f, -16f), new Vector2(220f, 24f));
            _customColorPreview = CreateImage("CustomColorPreview", _customPanel, _customColor, new Vector2(16f, -48f), new Vector2(48f, 48f));
            CreateText("CustomPanelHint", _customPanel, "Drag on the color pad, then save it if you want to keep it.", 12, FontStyle.Normal, TextAnchor.UpperLeft, new Vector2(76f, -50f), new Vector2(214f, 34f));
            _customSlotLabelText = CreateText("CustomSlotLabel", _customPanel, "Saving to first empty slot", 12, FontStyle.Normal, TextAnchor.UpperLeft, new Vector2(76f, -84f), new Vector2(214f, 20f));

            RectTransform pickerFrame = CreatePanel("ColorPickerFrame", _customPanel, sectionColor, new Vector2(16f, -112f), new Vector2(276f, 94f));
            RawImage colorField = CreateRawImage("ColorFieldImage", pickerFrame, new Vector2(10f, -8f), new Vector2(218f, 78f));
            RawImage valueSlider = CreateRawImage("ValueSliderImage", pickerFrame, new Vector2(236f, -8f), new Vector2(24f, 78f));

            RectTransform colorCursor = CreateCursor("ColorFieldCursor", colorField.rectTransform, new Vector2(14f, 14f), new Color(1f, 1f, 1f, 0.10f));
            colorCursor.anchorMin = Vector2.zero;
            colorCursor.anchorMax = Vector2.zero;
            colorCursor.pivot = new Vector2(0.5f, 0.5f);

            RectTransform valueCursor = CreateCursor("ValueSliderCursor", valueSlider.rectTransform, new Vector2(30f, 10f), new Color(1f, 1f, 1f, 0.18f));
            valueCursor.anchorMin = new Vector2(0.5f, 0f);
            valueCursor.anchorMax = new Vector2(0.5f, 0f);
            valueCursor.pivot = new Vector2(0.5f, 0.5f);

            CreateButton("SaveCustomSlotButton", _customPanel, "Save Slot", activeButtonColor, new Vector2(16f, -220f), new Vector2(126f, 34f), out _, out _);
            CreateButton("CloseCustomButton", _customPanel, "Close", neutralButtonColor, new Vector2(166f, -220f), new Vector2(126f, 34f), out _, out _);

            _customColorPicker = _customPanel.gameObject.AddComponent<DrawingColorPickerController>();
            var colorFieldZone = colorField.gameObject.AddComponent<DrawingColorPickerInteractionZone>();
            colorFieldZone.Configure(_customColorPicker, DrawingColorPickerInteractionZone.ZoneKind.ColorField);
            var valueSliderZone = valueSlider.gameObject.AddComponent<DrawingColorPickerInteractionZone>();
            valueSliderZone.Configure(_customColorPicker, DrawingColorPickerInteractionZone.ZoneKind.ValueSlider);

            _customPanel.gameObject.SetActive(false);
        }

        private void BindControls()
        {
            if (_brushSizeSlider != null)
            {
                _brushSizeSlider.OnValueChanged.RemoveAllListeners();
                _brushSizeSlider.OnValueChanged.AddListener(OnBrushSizeChanged);
            }

            if (_eraserButton != null)
            {
                _eraserButton.onClick.RemoveAllListeners();
                _eraserButton.onClick.AddListener(OnEraserClicked);
            }

            if (_fillButton != null)
            {
                _fillButton.onClick.RemoveAllListeners();
                _fillButton.onClick.AddListener(OnFillClicked);
            }

            if (_customButton != null)
            {
                _customButton.onClick.RemoveAllListeners();
                _customButton.onClick.AddListener(OnCustomClicked);
            }

            if (_undoButton != null)
            {
                _undoButton.onClick.RemoveAllListeners();
                _undoButton.onClick.AddListener(OnUndoClicked);
            }

            if (_redoButton != null)
            {
                _redoButton.onClick.RemoveAllListeners();
                _redoButton.onClick.AddListener(OnRedoClicked);
            }

            if (_clearButton != null)
            {
                _clearButton.onClick.RemoveAllListeners();
                _clearButton.onClick.AddListener(OnClearClicked);
            }

            if (_doneButton != null)
            {
                _doneButton.onClick.RemoveAllListeners();
                _doneButton.onClick.AddListener(OnDoneClicked);
            }

            Button saveButton = _customPanel != null ? _customPanel.Find("SaveCustomSlotButton")?.GetComponent<Button>() : null;
            if (saveButton != null)
            {
                saveButton.onClick.RemoveAllListeners();
                saveButton.onClick.AddListener(OnSaveCustomSlotClicked);
            }

            Button closeButton = _customPanel != null ? _customPanel.Find("CloseCustomButton")?.GetComponent<Button>() : null;
            if (closeButton != null)
            {
                closeButton.onClick.RemoveAllListeners();
                closeButton.onClick.AddListener(() => SetCustomPanelVisible(false));
            }

            if (_customColorPicker != null)
            {
                _customColorPicker.ColorChanged -= OnCustomPickerChanged;
                _customColorPicker.ColorChanged += OnCustomPickerChanged;
            }
        }

        private PaletteSwatch CreatePaletteSwatch(RectTransform parent, int slotIndex, bool isCustomSlot, bool assigned, Color color)
        {
            var swatch = new PaletteSwatch
            {
                SlotIndex = slotIndex,
                IsCustomSlot = isCustomSlot,
                IsAssigned = assigned,
                SwatchColor = (Color32)color
            };

            RectTransform root = CreateRect(isCustomSlot ? $"CustomSwatch_{slotIndex}" : $"PaletteSwatch_{slotIndex}", parent, Vector2.zero, new Vector2(32f, 32f));

            Image borderImage = root.gameObject.AddComponent<Image>();
            borderImage.color = swatchBorderColor;

            Button button = root.gameObject.AddComponent<Button>();
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.95f, 0.95f, 0.95f, 1f);
            colors.pressedColor = new Color(0.80f, 0.80f, 0.80f, 1f);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = new Color(0.7f, 0.7f, 0.7f, 0.5f);
            button.colors = colors;

            Image fillImage = CreateImage("Fill", root, assigned ? color : emptySwatchColor, new Vector2(4f, -4f), new Vector2(24f, 24f));
            Text labelText = CreateText("Label", root, assigned ? string.Empty : "+", 14, FontStyle.Bold, TextAnchor.MiddleCenter, Vector2.zero, new Vector2(32f, 32f));

            swatch.Button = button;
            swatch.BorderImage = borderImage;
            swatch.FillImage = fillImage;
            swatch.LabelText = labelText;

            button.onClick.AddListener(() => OnPaletteSwatchClicked(swatch));
            return swatch;
        }

        private void OnPaletteSwatchClicked(PaletteSwatch swatch)
        {
            if (swatch == null || drawingBoard == null)
            {
                return;
            }

            if (!swatch.IsCustomSlot)
            {
                _selectedCustomSlotIndex = -1;
                drawingBoard.SetBrushColor(swatch.SwatchColor);
                SyncCustomColorFromBrush();
                SyncUi();
                return;
            }

            _selectedCustomSlotIndex = swatch.SlotIndex;
            if (swatch.IsAssigned)
            {
                _customColor = swatch.SwatchColor;
                _customColorPicker?.SetColor(_customColor, notify: false);
                drawingBoard.SetBrushColor(swatch.SwatchColor);
                SyncUi();
                return;
            }

            SyncCustomColorFromBrush();
            SetCustomPanelVisible(true);
            SyncUi();
        }

        private void OnBrushSizeChanged(float newValue)
        {
            drawingBoard?.SetBrushRadius(newValue);
            SyncUi();
        }

        private void OnEraserClicked()
        {
            drawingBoard?.ToggleEraser();
            SyncUi();
        }

        private void OnFillClicked()
        {
            if (drawingBoard == null)
            {
                return;
            }

            drawingBoard.SetFillToolEnabled(!drawingBoard.IsFillToolEnabled);
            SyncUi();
        }

        private void OnCustomClicked()
        {
            SyncCustomColorFromBrush();
            _customColorPicker?.SetColor(_customColor, notify: false);
            SetCustomPanelVisible(!_customPanelVisible);
            SyncUi();
        }

        private void OnUndoClicked()
        {
            if (drawingBoard != null && drawingBoard.Undo())
            {
                SyncUi();
            }
        }

        private void OnRedoClicked()
        {
            if (drawingBoard != null && drawingBoard.Redo())
            {
                SyncUi();
            }
        }

        private void OnClearClicked()
        {
            drawingBoard?.ClearCanvas();
            SyncUi();
        }

        private void OnDoneClicked()
        {
            (roundManager ?? RoundManager.Instance ?? FindFirstObjectByType<RoundManager>())?.OnDrawingComplete();
        }

        private void OnSaveCustomSlotClicked()
        {
            if (drawingBoard == null)
            {
                return;
            }

            int targetIndex = ResolveCustomSlotIndexForSave();
            _selectedCustomSlotIndex = targetIndex;
            _customSlotAssigned[targetIndex] = true;
            _customSlotColors[targetIndex] = (Color32)_customColor;

            foreach (PaletteSwatch swatch in _paletteSwatches)
            {
                if (!swatch.IsCustomSlot || swatch.SlotIndex != targetIndex)
                {
                    continue;
                }

                swatch.IsAssigned = true;
                swatch.SwatchColor = (Color32)_customColor;
                break;
            }

            drawingBoard.SetBrushColor(_customColor);
            SetCustomPanelVisible(false);
            SyncUi();
        }

        private void OnCustomPickerChanged(Color color)
        {
            if (drawingBoard == null)
            {
                return;
            }

            _customColor = color;
            drawingBoard.SetBrushColor(color);
            SyncUi();
        }

        private void OnBrushRadiusChanged(int _)
        {
            SyncUi();
        }

        private void OnHistoryStateChanged(bool _, bool __)
        {
            SyncUi();
        }

        private void OnToolStateChanged(bool _, bool __)
        {
            SyncUi();
        }

        private void SyncUi()
        {
            if (!_uiBuilt || drawingBoard == null)
            {
                return;
            }

            RefreshCanvasPlacement();
            RefreshCanvasCamera();
            if (_brushSizeSlider != null)
            {
                _brushSizeSlider.SetValueWithoutNotify(drawingBoard.BrushRadius);
            }

            if (_brushSizeValueText != null)
            {
                _brushSizeValueText.text = drawingBoard.BrushRadius.ToString();
            }

            if (_activeColorPreview != null)
            {
                _activeColorPreview.color = drawingBoard.ActiveDrawColor;
            }

            if (_toolStatusText != null)
            {
                _toolStatusText.text = drawingBoard.GetCurrentToolMode() switch
                {
                    DrawingToolMode.Eraser => "Eraser",
                    DrawingToolMode.Fill => "Fill Armed",
                    DrawingToolMode.SketchGuide => "Sketch",
                    _ => "Brush"
                };
            }

            RefreshToolButtons();
            RefreshHistoryButtons();
            RefreshPaletteSelection();
            RefreshCustomPanel();
        }

        private void RefreshToolButtons()
        {
            if (_eraserButtonImage != null)
            {
                _eraserButtonImage.color = drawingBoard.IsEraserEnabled ? warningButtonColor : neutralButtonColor;
            }

            if (_eraserButtonText != null)
            {
                _eraserButtonText.text = drawingBoard.IsEraserEnabled ? "Eraser On" : "Eraser";
            }

            if (_fillButtonImage != null)
            {
                _fillButtonImage.color = drawingBoard.IsFillToolEnabled ? activeButtonColor : neutralButtonColor;
            }

            if (_fillButtonText != null)
            {
                _fillButtonText.text = drawingBoard.IsFillToolEnabled ? "Fill Armed" : "Fill";
            }

            if (_customButtonImage != null)
            {
                bool currentColorIsCustom = !MatchesAnyPaletteColor((Color32)drawingBoard.BrushColor);
                _customButtonImage.color = _customPanelVisible || currentColorIsCustom ? activeButtonColor : neutralButtonColor;
            }

            if (_customButtonPreview != null)
            {
                _customButtonPreview.color = _customColor;
            }

            if (_clearButtonImage != null)
            {
                _clearButtonImage.color = warningButtonColor;
            }
        }

        private void RefreshHistoryButtons()
        {
            SetButtonState(_undoButton, _undoButtonImage, drawingBoard.CanUndo, neutralButtonColor, disabledButtonColor);
            SetButtonState(_redoButton, _redoButtonImage, drawingBoard.CanRedo, neutralButtonColor, disabledButtonColor);
        }

        private void RefreshPaletteSelection()
        {
            Color32 currentBrushColor = (Color32)drawingBoard.BrushColor;
            foreach (PaletteSwatch swatch in _paletteSwatches)
            {
                if (swatch.IsCustomSlot)
                {
                    swatch.IsAssigned = _customSlotAssigned[swatch.SlotIndex];
                    swatch.SwatchColor = _customSlotAssigned[swatch.SlotIndex]
                        ? _customSlotColors[swatch.SlotIndex]
                        : (Color32)emptySwatchColor;
                }

                bool isSelected = swatch.IsAssigned &&
                                  !drawingBoard.IsEraserEnabled &&
                                  ColorsEqual(swatch.SwatchColor, currentBrushColor);
                if (!swatch.IsAssigned && swatch.IsCustomSlot && swatch.SlotIndex == _selectedCustomSlotIndex && _customPanelVisible)
                {
                    isSelected = true;
                }

                if (swatch.BorderImage != null)
                {
                    swatch.BorderImage.color = isSelected ? swatchSelectedColor : swatchBorderColor;
                }

                if (swatch.FillImage != null)
                {
                    swatch.FillImage.color = swatch.IsAssigned ? swatch.SwatchColor : emptySwatchColor;
                }

                if (swatch.LabelText != null)
                {
                    swatch.LabelText.text = swatch.IsAssigned ? string.Empty : "+";
                    swatch.LabelText.color = isSelected ? swatchSelectedColor : swatchBorderColor;
                }
            }
        }

        private void RefreshCustomPanel()
        {
            if (_customColorPreview != null)
            {
                _customColorPreview.color = _customColor;
            }

            if (_customSlotLabelText != null)
            {
                _customSlotLabelText.text = _selectedCustomSlotIndex >= 0
                    ? $"Saving to slot {_selectedCustomSlotIndex + 1}"
                    : "Saving to first empty slot";
            }
        }

        private void SetHudVisible(bool visible)
        {
            _isVisible = visible;
            if (_canvasRootObject != null)
            {
                _canvasRootObject.SetActive(visible);
            }

            if (!visible)
            {
                _customPanelVisible = false;
            }

            if (_customPanel != null)
            {
                _customPanel.gameObject.SetActive(visible && _customPanelVisible);
            }
        }

        private void SetCustomPanelVisible(bool visible)
        {
            _customPanelVisible = visible;
            if (_customPanel != null)
            {
                _customPanel.gameObject.SetActive(visible && _isVisible);
            }
        }

        private void SyncCustomColorFromBrush()
        {
            if (drawingBoard == null)
            {
                return;
            }

            _customColor = drawingBoard.BrushColor;
            _customColorPicker?.SetColor(_customColor, notify: false);
        }

        private void RefreshCanvasPlacement()
        {
            if (_canvas == null)
            {
                return;
            }

            if (!_ownsRuntimeUi && !applyPlacementToSceneAuthoredCanvas)
            {
                return;
            }

            RectTransform canvasRect = (RectTransform)_canvas.transform;
            if (autoFitCanvasToSurface)
            {
                Bounds surfaceBounds = ResolveSurfaceLocalBounds();
                float widthUnits = Mathf.Max(0.2f, surfaceBounds.size.x * SurfaceMarginX);
                float heightUnits = Mathf.Max(0.2f, surfaceBounds.size.z * SurfaceMarginY);

                canvasRect.localPosition = new Vector3(
                    surfaceBounds.center.x,
                    surfaceBounds.max.y + SurfaceOffset,
                    surfaceBounds.center.z);
                canvasRect.localRotation = Quaternion.Euler(90f, 0f, 0f);
                canvasRect.localScale = new Vector3(widthUnits / CanvasWidth, heightUnits / CanvasHeight, 1f);
            }

            RefreshCanvasPixelAspect(canvasRect);
        }

        private void RefreshCanvasCamera()
        {
            if (_canvas == null)
            {
                return;
            }

            uiCamera ??= UnityEngine.Camera.main ?? FindFirstObjectByType<UnityEngine.Camera>();
            _canvas.worldCamera = uiCamera;
        }

        private Bounds ResolveSurfaceLocalBounds()
        {
            if (TryGetComponent(out MeshFilter meshFilter) && meshFilter.sharedMesh != null)
            {
                return meshFilter.sharedMesh.bounds;
            }

            if (TryGetComponent(out Renderer renderer))
            {
                Bounds worldBounds = renderer.bounds;
                Vector3 center = transform.InverseTransformPoint(worldBounds.center);
                Vector3 size = transform.InverseTransformVector(worldBounds.size);
                size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));
                return new Bounds(center, size);
            }

            return new Bounds(Vector3.zero, new Vector3(1f, 0.1f, 0.7f));
        }

        private void RefreshCanvasPixelAspect(RectTransform canvasRect)
        {
            if (!preserveSquareCanvasPixels || canvasRect == null)
            {
                return;
            }

            Vector3 parentLossyScale = transform.lossyScale;
            float worldScaleX = Mathf.Abs(parentLossyScale.x);
            float worldScaleY = Mathf.Abs(parentLossyScale.z);
            if (worldScaleX < 0.0001f || worldScaleY < 0.0001f)
            {
                return;
            }

            Vector3 localScale = canvasRect.localScale;
            float xSign = localScale.x < 0f ? -1f : 1f;
            localScale.x = xSign * Mathf.Abs(localScale.y) * (worldScaleY / worldScaleX);
            canvasRect.localScale = localScale;
        }

        private int ResolveCustomSlotIndexForSave()
        {
            if (_selectedCustomSlotIndex >= 0 && _selectedCustomSlotIndex < CustomSlotCount)
            {
                return _selectedCustomSlotIndex;
            }

            for (int i = 0; i < CustomSlotCount; i++)
            {
                if (!_customSlotAssigned[i])
                {
                    return i;
                }
            }

            return 0;
        }

        private bool MatchesAnyPaletteColor(Color32 color)
        {
            for (int i = 0; i < DefaultPaletteColors.Length; i++)
            {
                if (ColorsEqual((Color32)DefaultPaletteColors[i], color))
                {
                    return true;
                }
            }

            for (int i = 0; i < CustomSlotCount; i++)
            {
                if (_customSlotAssigned[i] && ColorsEqual(_customSlotColors[i], color))
                {
                    return true;
                }
            }

            return false;
        }

        private static void SetButtonState(Button button, Image buttonImage, bool interactable, Color enabledColor, Color disabledColorOverride)
        {
            if (button != null)
            {
                button.interactable = interactable;
            }

            if (buttonImage != null)
            {
                buttonImage.color = interactable ? enabledColor : disabledColorOverride;
            }
        }

        private static bool ColorsEqual(Color32 left, Color32 right)
        {
            return left.r == right.r &&
                   left.g == right.g &&
                   left.b == right.b &&
                   left.a == right.a;
        }

        private static T FindNamedComponent<T>(Transform root, string objectName) where T : Component
        {
            if (root == null)
            {
                return null;
            }

            T[] components = root.GetComponentsInChildren<T>(true);
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i].name == objectName)
                {
                    return components[i];
                }
            }

            return null;
        }

        private static TabletBrushSizeSlider ResolveBrushSizeSlider(RectTransform root)
        {
            if (root == null)
            {
                return null;
            }

            RectTransform fillRect = root.Find("Fill Area/Fill") as RectTransform;
            RectTransform handleRect = root.Find("Handle Slide Area/Handle") as RectTransform;
            var slider = root.GetComponent<TabletBrushSizeSlider>();
            if (slider == null)
            {
                slider = root.gameObject.AddComponent<TabletBrushSizeSlider>();
            }

            slider.Configure(fillRect, handleRect, 1f, 24f, useWholeNumbers: true);
            return slider;
        }

        private static void EnsureEventSystem()
        {
            EventSystem eventSystem = FindFirstObjectByType<EventSystem>();
            if (eventSystem == null)
            {
                eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule)).GetComponent<EventSystem>();
            }

            if (eventSystem != null && eventSystem.GetComponent<InputSystemUIInputModule>() == null)
            {
                eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
            }
        }

        private static RectTransform CreateRect(string name, RectTransform parent, Vector2 anchoredPosition, Vector2 size)
        {
            var gameObject = new GameObject(name, typeof(RectTransform));
            gameObject.transform.SetParent(parent, false);
            RectTransform rect = gameObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
            return rect;
        }

        private static RectTransform CreatePanel(string name, RectTransform parent, Color color, Vector2 anchoredPosition, Vector2 size)
        {
            RectTransform rect = CreateRect(name, parent, anchoredPosition, size);
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            return rect;
        }

        private static Image CreateImage(string name, RectTransform parent, Color color, Vector2 anchoredPosition, Vector2 size)
        {
            RectTransform rect = CreateRect(name, parent, anchoredPosition, size);
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            return image;
        }

        private static RawImage CreateRawImage(string name, RectTransform parent, Vector2 anchoredPosition, Vector2 size)
        {
            RectTransform rect = CreateRect(name, parent, anchoredPosition, size);
            RawImage image = rect.gameObject.AddComponent<RawImage>();
            image.color = Color.white;
            return image;
        }

        private static RectTransform CreateCursor(string name, RectTransform parent, Vector2 size, Color color)
        {
            RectTransform rect = CreateRect(name, parent, Vector2.zero, size);
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            Outline outline = rect.gameObject.AddComponent<Outline>();
            outline.effectColor = Color.black;
            outline.effectDistance = new Vector2(1f, -1f);
            return rect;
        }

        private static Text CreateText(string name, RectTransform parent, string content, int fontSize, FontStyle fontStyle, TextAnchor anchor, Vector2 anchoredPosition, Vector2 size)
        {
            RectTransform rect = CreateRect(name, parent, anchoredPosition, size);
            Text text = rect.gameObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = content;
            text.fontSize = fontSize;
            text.fontStyle = fontStyle;
            text.alignment = anchor;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }

        private static Button CreateButton(string name, RectTransform parent, string label, Color color, Vector2 anchoredPosition, Vector2 size, out Text labelText, out Image image)
        {
            RectTransform rect = CreateRect(name, parent, anchoredPosition, size);
            image = rect.gameObject.AddComponent<Image>();
            image.color = color;

            Button button = rect.gameObject.AddComponent<Button>();
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.94f, 0.94f, 0.94f, 1f);
            colors.pressedColor = new Color(0.80f, 0.80f, 0.80f, 1f);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = new Color(0.7f, 0.7f, 0.7f, 0.5f);
            button.colors = colors;

            labelText = CreateText($"{name}Text", rect, label, 13, FontStyle.Bold, TextAnchor.MiddleCenter, Vector2.zero, size);
            return button;
        }

        private static TabletBrushSizeSlider CreateSlider(string name, RectTransform parent, Vector2 anchoredPosition, Vector2 size, float minValue, float maxValue)
        {
            RectTransform root = CreateRect(name, parent, anchoredPosition, size);
            Image background = root.gameObject.AddComponent<Image>();
            background.color = new Color(0.12f, 0.13f, 0.17f, 1f);

            RectTransform fillArea = CreateRect("Fill Area", root, Vector2.zero, size);
            fillArea.anchorMin = Vector2.zero;
            fillArea.anchorMax = Vector2.one;
            fillArea.offsetMin = new Vector2(8f, 4f);
            fillArea.offsetMax = new Vector2(-18f, -4f);
            fillArea.pivot = new Vector2(0.5f, 0.5f);

            Image fill = CreateImage("Fill", fillArea, new Color(0.23f, 0.68f, 0.92f, 1f), Vector2.zero, Vector2.zero);
            RectTransform fillRect = fill.rectTransform;
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            fillRect.pivot = new Vector2(0f, 0.5f);

            RectTransform handleArea = CreateRect("Handle Slide Area", root, Vector2.zero, size);
            handleArea.anchorMin = Vector2.zero;
            handleArea.anchorMax = Vector2.one;
            handleArea.offsetMin = new Vector2(8f, 0f);
            handleArea.offsetMax = new Vector2(-8f, 0f);
            handleArea.pivot = new Vector2(0.5f, 0.5f);

            Image handle = CreateImage("Handle", handleArea, Color.white, Vector2.zero, new Vector2(12f, size.y));
            RectTransform handleRect = handle.rectTransform;
            handleRect.anchorMin = new Vector2(0f, 0f);
            handleRect.anchorMax = new Vector2(0f, 1f);
            handleRect.pivot = new Vector2(0.5f, 0.5f);
            handleRect.anchoredPosition = Vector2.zero;
            handleRect.sizeDelta = new Vector2(12f, -8f);
            handleRect.offsetMin = new Vector2(-6f, 4f);
            handleRect.offsetMax = new Vector2(6f, -4f);

            var slider = root.gameObject.AddComponent<TabletBrushSizeSlider>();
            slider.Configure(fillRect, handleRect, minValue, maxValue, useWholeNumbers: true);
            return slider;
        }
    }
}
