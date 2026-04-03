using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Paint-style control panel for the drawing board prototype.
/// </summary>
public class DrawingUIController : MonoBehaviour
{
    private const int PaletteColumnCount = 10;
    private const int CustomSlotCount = 10;
    private const HideFlags RuntimeHideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;

    private static readonly Color[] DefaultPaletteColors =
    {
        new(0.08f, 0.08f, 0.08f, 1f),
        new(0.27f, 0.27f, 0.27f, 1f),
        new(0.41f, 0.11f, 0.14f, 1f),
        new(0.68f, 0.08f, 0.17f, 1f),
        new(0.96f, 0.18f, 0.18f, 1f),
        new(0.98f, 0.45f, 0.12f, 1f),
        new(0.99f, 0.83f, 0.16f, 1f),
        new(0.22f, 0.68f, 0.29f, 1f),
        new(0.12f, 0.60f, 0.84f, 1f),
        new(0.52f, 0.31f, 0.74f, 1f),

        new(1f, 1f, 1f, 1f),
        new(0.82f, 0.82f, 0.82f, 1f),
        new(0.71f, 0.46f, 0.28f, 1f),
        new(0.96f, 0.62f, 0.75f, 1f),
        new(0.99f, 0.76f, 0.17f, 1f),
        new(0.98f, 0.92f, 0.46f, 1f),
        new(0.72f, 0.92f, 0.10f, 1f),
        new(0.58f, 0.82f, 0.90f, 1f),
        new(0.63f, 0.71f, 0.96f, 1f),
        new(0.79f, 0.74f, 0.84f, 1f)
    };

    private sealed class PaletteSwatch
    {
        public int SlotIndex;
        public bool IsCustomSlot;
        public bool IsAssigned;
        public Color32 SwatchColor;
        public Image BorderImage;
        public Image FillImage;
        public Button Button;
    }

    [Header("References")]
    [SerializeField] private DrawingBoardController drawingBoard;
    [SerializeField] private RectTransform controlPanel;
    [SerializeField] private RectTransform paletteGrid;
    [SerializeField] private Text paletteCaptionText;
    [SerializeField] private Image activeColorPreview;
    [SerializeField] private Text activeColorStatusText;
    [SerializeField] private Slider brushSizeSlider;
    [SerializeField] private Text brushSizeValueText;
    [SerializeField] private Button eraserButton;
    [SerializeField] private Text eraserButtonText;
    [SerializeField] private Image eraserButtonImage;
    [SerializeField] private Button fillButton;
    [SerializeField] private Text fillButtonText;
    [SerializeField] private Image fillButtonImage;
    [SerializeField] private Button customColorButton;
    [SerializeField] private Text customColorButtonText;
    [SerializeField] private Image customColorButtonImage;
    [SerializeField] private Image customColorButtonPreview;
    [SerializeField] private Button undoButton;
    [SerializeField] private Text undoButtonText;
    [SerializeField] private Image undoButtonImage;
    [SerializeField] private Button redoButton;
    [SerializeField] private Text redoButtonText;
    [SerializeField] private Image redoButtonImage;
    [SerializeField] private RectTransform customColorPanel;
    [SerializeField] private DrawingColorPickerController customColorPicker;
    [SerializeField] private Button applyCustomColorButton;

    [Header("Theme")]
    [SerializeField] private Color eraserOffColor = new(0.22f, 0.24f, 0.29f, 1f);
    [SerializeField] private Color eraserOnColor = new(0.86f, 0.55f, 0.20f, 1f);
    [SerializeField] private Color fillOffColor = new(0.22f, 0.24f, 0.29f, 1f);
    [SerializeField] private Color fillOnColor = new(0.13f, 0.58f, 0.49f, 1f);
    [SerializeField] private Color swatchBorderColor = new(0.70f, 0.72f, 0.76f, 1f);
    [SerializeField] private Color swatchSelectedColor = new(0.05f, 0.05f, 0.05f, 1f);
    [SerializeField] private Color swatchEmptyFillColor = new(1f, 1f, 1f, 0.06f);
    [SerializeField] private Color swatchEmptySelectedColor = new(0.13f, 0.58f, 0.49f, 1f);
    [SerializeField] private Color customButtonColor = new(0.20f, 0.22f, 0.28f, 1f);
    [SerializeField] private Color customButtonOpenColor = new(0.22f, 0.45f, 0.78f, 1f);
    [SerializeField] private Color customButtonSelectedColor = new(0.13f, 0.58f, 0.49f, 1f);
    [SerializeField] private Color historyButtonEnabledColor = new(0.20f, 0.22f, 0.28f, 1f);
    [SerializeField] private Color historyButtonDisabledColor = new(0.16f, 0.17f, 0.20f, 0.75f);
    [SerializeField] private Color[] paletteColors = DefaultPaletteColors;

    private readonly List<PaletteSwatch> _defaultSwatches = new();
    private readonly List<PaletteSwatch> _customSwatches = new();
    private readonly List<PaletteSwatch> _allSwatches = new();
    private readonly Color32[] _customSlotColors = new Color32[CustomSlotCount];
    private readonly bool[] _customSlotAssigned = new bool[CustomSlotCount];

    private Texture2D _circleTexture;
    private Sprite _circleSprite;
    private Color _customColor = Color.red;
    private bool _customPanelVisible;
    private bool _isBrushColorInDefaultPalette;
    private int _selectedCustomSlotIndex = -1;

    private bool _layoutBaselineCaptured;
    private float _paletteBaseHeight;
    private float _controlPanelBaseHeight;
    private Vector2 _paletteCaptionBasePosition;
    private Vector2 _brushSizeLabelBasePosition;
    private Vector2 _brushSizeSliderBasePosition;
    private Vector2 _brushSizeValueBasePosition;
    private Vector2 _customColorButtonBasePosition;
    private Vector2 _fillButtonBasePosition;
    private Vector2 _eraserButtonBasePosition;
    private Vector2 _undoButtonBasePosition;
    private Vector2 _redoButtonBasePosition;
    private Vector2 _customColorPanelBasePosition;
    private RectTransform _brushSizeLabelRect;
    private Transform _controlPanelParent;
    private int _controlPanelBaseSiblingIndex = -1;
    private bool _controlPanelRaisedForCustomPanel;

    private void Awake()
    {
        if (drawingBoard == null)
        {
            drawingBoard = FindFirstObjectByType<DrawingBoardController>();
        }

        CacheReferences();
        EnsureToolButtons();
        EnsureHistoryButtons();
        CacheReferences();
        CacheControlPanelSortingState();
        CaptureLayoutBaseline();
        EnsureCircleSprite();
        BindControls();
        BuildPalette();
        SyncUi();
    }

    private void OnEnable()
    {
        if (drawingBoard != null)
        {
            drawingBoard.BrushRadiusChanged += OnBrushRadiusChangedExternally;
            drawingBoard.HistoryStateChanged += OnHistoryStateChanged;
        }
    }

    private void OnDisable()
    {
        if (drawingBoard != null)
        {
            drawingBoard.BrushRadiusChanged -= OnBrushRadiusChangedExternally;
            drawingBoard.HistoryStateChanged -= OnHistoryStateChanged;
        }
    }

    private void OnDestroy()
    {
        if (customColorPicker != null)
        {
            customColorPicker.ColorChanged -= OnCustomPickerChanged;
        }

        ClearCircleSpriteReferences();
        SafeDestroy(_circleSprite);
        SafeDestroy(_circleTexture);
    }

    private void CacheReferences()
    {
        if (controlPanel == null)
        {
            controlPanel = FindNamedComponent<RectTransform>("ControlPanel");
        }

        if (paletteGrid == null)
        {
            paletteGrid = FindNamedComponent<RectTransform>("PaletteGrid");
        }

        if (paletteCaptionText == null)
        {
            paletteCaptionText = FindNamedComponent<Text>("PaletteCaptionText");
        }

        if (activeColorPreview == null)
        {
            activeColorPreview = FindNamedComponent<Image>("ActiveColorPreview");
        }

        if (activeColorStatusText == null)
        {
            activeColorStatusText = FindNamedComponent<Text>("ActiveColorStatusText");
        }

        if (brushSizeSlider == null)
        {
            brushSizeSlider = FindNamedComponent<Slider>("BrushSizeSlider");
        }

        if (brushSizeValueText == null)
        {
            brushSizeValueText = FindNamedComponent<Text>("BrushSizeValueText");
        }

        if (_brushSizeLabelRect == null)
        {
            _brushSizeLabelRect = FindNamedComponent<RectTransform>("BrushSizeLabel");
        }

        if (eraserButton == null)
        {
            eraserButton = FindNamedComponent<Button>("EraserButton");
        }

        if (eraserButtonText == null)
        {
            eraserButtonText = FindNamedComponent<Text>("EraserButtonText");
        }

        if (eraserButtonImage == null && eraserButton != null)
        {
            eraserButtonImage = eraserButton.GetComponent<Image>();
        }

        if (fillButton == null)
        {
            fillButton = FindNamedComponent<Button>("FillButton");
        }

        if (fillButtonText == null)
        {
            fillButtonText = FindNamedComponent<Text>("FillButtonText");
        }

        if (fillButtonImage == null && fillButton != null)
        {
            fillButtonImage = fillButton.GetComponent<Image>();
        }

        if (customColorButton == null)
        {
            customColorButton = FindNamedComponent<Button>("CustomColorButton");
        }

        if (customColorButtonText == null)
        {
            customColorButtonText = FindNamedComponent<Text>("CustomColorButtonText");
        }

        if (customColorButtonImage == null && customColorButton != null)
        {
            customColorButtonImage = customColorButton.GetComponent<Image>();
        }

        if (customColorButtonPreview == null)
        {
            customColorButtonPreview = FindNamedComponent<Image>("CustomColorButtonPreview");
        }

        if (undoButton == null)
        {
            undoButton = FindNamedComponent<Button>("UndoButton");
        }

        if (undoButtonText == null)
        {
            undoButtonText = FindNamedComponent<Text>("UndoButtonText");
        }

        if (undoButtonImage == null && undoButton != null)
        {
            undoButtonImage = undoButton.GetComponent<Image>();
        }

        if (redoButton == null)
        {
            redoButton = FindNamedComponent<Button>("RedoButton");
        }

        if (redoButtonText == null)
        {
            redoButtonText = FindNamedComponent<Text>("RedoButtonText");
        }

        if (redoButtonImage == null && redoButton != null)
        {
            redoButtonImage = redoButton.GetComponent<Image>();
        }

        if (customColorPanel == null)
        {
            customColorPanel = FindNamedComponent<RectTransform>("CustomColorPanel");
        }

        if (customColorPicker == null)
        {
            customColorPicker = FindNamedComponent<DrawingColorPickerController>("CustomColorPicker");
        }

        if (applyCustomColorButton == null)
        {
            applyCustomColorButton = FindNamedComponent<Button>("ApplyCustomColorButton");
        }
    }

    private void CaptureLayoutBaseline()
    {
        if (_layoutBaselineCaptured || paletteGrid == null || controlPanel == null)
        {
            return;
        }

        _paletteBaseHeight = paletteGrid.sizeDelta.y;
        _controlPanelBaseHeight = controlPanel.sizeDelta.y;
        _paletteCaptionBasePosition = GetAnchoredPosition(paletteCaptionText != null ? paletteCaptionText.rectTransform : null);
        _brushSizeLabelBasePosition = GetAnchoredPosition(_brushSizeLabelRect);
        _brushSizeSliderBasePosition = GetAnchoredPosition(brushSizeSlider != null ? brushSizeSlider.GetComponent<RectTransform>() : null);
        _brushSizeValueBasePosition = GetAnchoredPosition(brushSizeValueText != null ? brushSizeValueText.rectTransform : null);
        _customColorButtonBasePosition = GetAnchoredPosition(customColorButton != null ? customColorButton.GetComponent<RectTransform>() : null);
        _fillButtonBasePosition = GetAnchoredPosition(fillButton != null ? fillButton.GetComponent<RectTransform>() : null);
        _eraserButtonBasePosition = GetAnchoredPosition(eraserButton != null ? eraserButton.GetComponent<RectTransform>() : null);
        _undoButtonBasePosition = GetAnchoredPosition(undoButton != null ? undoButton.GetComponent<RectTransform>() : null);
        _redoButtonBasePosition = GetAnchoredPosition(redoButton != null ? redoButton.GetComponent<RectTransform>() : null);
        _customColorPanelBasePosition = GetAnchoredPosition(customColorPanel);
        _layoutBaselineCaptured = true;
    }

    private void CacheControlPanelSortingState()
    {
        if (controlPanel == null)
        {
            _controlPanelParent = null;
            _controlPanelBaseSiblingIndex = -1;
            _controlPanelRaisedForCustomPanel = false;
            return;
        }

        _controlPanelParent = controlPanel.parent;
        _controlPanelBaseSiblingIndex = controlPanel.GetSiblingIndex();
        _controlPanelRaisedForCustomPanel = false;
    }

    private void BindControls()
    {
        if (brushSizeSlider != null)
        {
            brushSizeSlider.onValueChanged.RemoveListener(OnBrushSizeChanged);
            brushSizeSlider.onValueChanged.AddListener(OnBrushSizeChanged);
        }

        if (eraserButton != null)
        {
            eraserButton.onClick.RemoveListener(OnEraserButtonClicked);
            eraserButton.onClick.AddListener(OnEraserButtonClicked);
        }

        if (fillButton != null)
        {
            fillButton.onClick.RemoveListener(OnFillButtonClicked);
            fillButton.onClick.AddListener(OnFillButtonClicked);
        }

        if (customColorButton != null)
        {
            customColorButton.onClick.RemoveListener(OnCustomColorButtonClicked);
            customColorButton.onClick.AddListener(OnCustomColorButtonClicked);
        }

        if (applyCustomColorButton != null)
        {
            applyCustomColorButton.onClick.RemoveListener(OnApplyCustomColorClicked);
            applyCustomColorButton.onClick.AddListener(OnApplyCustomColorClicked);
        }

        if (undoButton != null)
        {
            undoButton.onClick.RemoveListener(OnUndoButtonClicked);
            undoButton.onClick.AddListener(OnUndoButtonClicked);
        }

        if (redoButton != null)
        {
            redoButton.onClick.RemoveListener(OnRedoButtonClicked);
            redoButton.onClick.AddListener(OnRedoButtonClicked);
        }

        if (customColorPicker != null)
        {
            customColorPicker.ColorChanged -= OnCustomPickerChanged;
            customColorPicker.ColorChanged += OnCustomPickerChanged;
        }
    }

    private void BuildPalette()
    {
        if (paletteGrid == null)
        {
            return;
        }

        ClearPaletteChildren();
        _defaultSwatches.Clear();
        _customSwatches.Clear();
        _allSwatches.Clear();

        if (paletteColors == null || paletteColors.Length == 0)
        {
            paletteColors = DefaultPaletteColors;
        }

        for (int i = 0; i < paletteColors.Length; i++)
        {
            var swatch = new PaletteSwatch
            {
                SlotIndex = i,
                IsCustomSlot = false,
                IsAssigned = true,
                SwatchColor = (Color32)paletteColors[i]
            };

            CreateSwatchObject(swatch);
            _defaultSwatches.Add(swatch);
            _allSwatches.Add(swatch);
        }

        for (int i = 0; i < CustomSlotCount; i++)
        {
            var swatch = new PaletteSwatch
            {
                SlotIndex = i,
                IsCustomSlot = true,
                IsAssigned = _customSlotAssigned[i],
                SwatchColor = _customSlotAssigned[i] ? _customSlotColors[i] : (Color32)Color.white
            };

            CreateSwatchObject(swatch);
            _customSwatches.Add(swatch);
            _allSwatches.Add(swatch);
        }

        ApplyPaletteLayout();
    }

    private void CreateSwatchObject(PaletteSwatch swatch)
    {
        string objectName = swatch.IsCustomSlot
            ? $"CustomSwatch_{swatch.SlotIndex:00}"
            : $"Swatch_{swatch.SlotIndex:00}";

        var root = new GameObject(objectName, typeof(RectTransform), typeof(Image), typeof(Button));
        root.transform.SetParent(paletteGrid, false);
        MarkRuntimeObject(root);

        Image borderImage = root.GetComponent<Image>();
        borderImage.sprite = _circleSprite;
        borderImage.raycastTarget = true;

        Button button = root.GetComponent<Button>();
        button.transition = Selectable.Transition.ColorTint;
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1f, 1f, 1f, 0.96f);
        colors.pressedColor = new Color(0.92f, 0.92f, 0.92f, 1f);
        colors.selectedColor = Color.white;
        colors.disabledColor = new Color(0.7f, 0.7f, 0.7f, 0.5f);
        button.colors = colors;
        button.targetGraphic = borderImage;

        var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fill.transform.SetParent(root.transform, false);
        MarkRuntimeObject(fill);

        RectTransform fillRect = fill.GetComponent<RectTransform>();
        fillRect.anchorMin = new Vector2(0.5f, 0.5f);
        fillRect.anchorMax = new Vector2(0.5f, 0.5f);
        fillRect.pivot = new Vector2(0.5f, 0.5f);
        fillRect.sizeDelta = new Vector2(18f, 18f);
        fillRect.anchoredPosition = Vector2.zero;

        Image fillImage = fill.GetComponent<Image>();
        fillImage.sprite = _circleSprite;
        fillImage.raycastTarget = false;

        swatch.BorderImage = borderImage;
        swatch.FillImage = fillImage;
        swatch.Button = button;

        button.onClick.AddListener(() => OnSwatchClicked(swatch));
        RefreshSwatchVisual(swatch, false);
    }

    private void ApplyPaletteLayout()
    {
        if (!_layoutBaselineCaptured || paletteGrid == null)
        {
            return;
        }

        GridLayoutGroup grid = paletteGrid.GetComponent<GridLayoutGroup>();
        float cellHeight = 24f;
        float spacingY = 6f;
        if (grid != null)
        {
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = PaletteColumnCount;
            cellHeight = grid.cellSize.y;
            spacingY = grid.spacing.y;
        }

        int rowCount = Mathf.CeilToInt(_allSwatches.Count / (float)PaletteColumnCount);
        float targetGridHeight = (rowCount * cellHeight) + (Mathf.Max(0, rowCount - 1) * spacingY);
        float extraHeight = targetGridHeight - _paletteBaseHeight;

        SetHeight(paletteGrid, targetGridHeight);
        SetHeight(controlPanel, _controlPanelBaseHeight + extraHeight);
        SetAnchoredPosition(paletteCaptionText != null ? paletteCaptionText.rectTransform : null, _paletteCaptionBasePosition, extraHeight);
        SetAnchoredPosition(_brushSizeLabelRect, _brushSizeLabelBasePosition, extraHeight);
        SetAnchoredPosition(brushSizeSlider != null ? brushSizeSlider.GetComponent<RectTransform>() : null, _brushSizeSliderBasePosition, extraHeight);
        SetAnchoredPosition(brushSizeValueText != null ? brushSizeValueText.rectTransform : null, _brushSizeValueBasePosition, extraHeight);
        SetAnchoredPosition(customColorButton != null ? customColorButton.GetComponent<RectTransform>() : null, _customColorButtonBasePosition, extraHeight);
        SetAnchoredPosition(fillButton != null ? fillButton.GetComponent<RectTransform>() : null, _fillButtonBasePosition, extraHeight);
        SetAnchoredPosition(eraserButton != null ? eraserButton.GetComponent<RectTransform>() : null, _eraserButtonBasePosition, extraHeight);
        SetAnchoredPosition(undoButton != null ? undoButton.GetComponent<RectTransform>() : null, _undoButtonBasePosition, extraHeight);
        SetAnchoredPosition(redoButton != null ? redoButton.GetComponent<RectTransform>() : null, _redoButtonBasePosition, extraHeight);
        SetAnchoredPosition(customColorPanel, _customColorPanelBasePosition, extraHeight);

        if (paletteCaptionText != null)
        {
            paletteCaptionText.text = "Palette + Custom";
        }
    }

    private void SyncUi()
    {
        if (drawingBoard == null)
        {
            Debug.LogWarning("[DrawingUIController] DrawingBoardController not found.");
            return;
        }

        if (brushSizeSlider != null)
        {
            brushSizeSlider.SetValueWithoutNotify(drawingBoard.BrushRadius);
        }

        if (customColorPicker != null)
        {
            customColorPicker.SetColor(GetPickerSeedColor(_customColor), notify: false);
        }

        SetCustomPanelVisible(false);
        UpdateBrushSizeLabel(drawingBoard.BrushRadius);
        RefreshPaletteSelection();
        RefreshActiveColorPreview();
        RefreshFillVisual();
        RefreshEraserVisual();
        RefreshCustomColorButtonVisual();
        RefreshHistoryButtons();
    }

    private void OnSwatchClicked(PaletteSwatch swatch)
    {
        if (swatch == null || drawingBoard == null)
        {
            return;
        }

        if (!swatch.IsCustomSlot)
        {
            _selectedCustomSlotIndex = -1;
            drawingBoard.SetBrushColor(swatch.SwatchColor);
            RefreshPaletteSelection();
            RefreshActiveColorPreview();
            RefreshEraserVisual();
            RefreshCustomColorButtonVisual();
            return;
        }

        _selectedCustomSlotIndex = swatch.SlotIndex;
        if (swatch.IsAssigned)
        {
            _customColor = swatch.SwatchColor;
            drawingBoard.SetBrushColor(swatch.SwatchColor);
            if (customColorPicker != null)
            {
                customColorPicker.SetColor(GetPickerSeedColor(_customColor), notify: false);
            }

            RefreshPaletteSelection();
            RefreshActiveColorPreview();
            RefreshEraserVisual();
            RefreshCustomColorButtonVisual();
            return;
        }

        if (customColorPicker != null)
        {
            customColorPicker.SetColor(GetPickerSeedColor(_customColor), notify: false);
        }

        SetCustomPanelVisible(true);
        RefreshPaletteSelection();
        RefreshCustomColorButtonVisual();
    }

    private void OnBrushSizeChanged(float value)
    {
        if (drawingBoard == null)
        {
            return;
        }

        drawingBoard.SetBrushRadius(value);
        UpdateBrushSizeLabel(drawingBoard.BrushRadius);
    }

    private void OnBrushRadiusChangedExternally(int brushSize)
    {
        if (brushSizeSlider != null)
        {
            brushSizeSlider.SetValueWithoutNotify(brushSize);
        }

        UpdateBrushSizeLabel(brushSize);
    }

    private void OnEraserButtonClicked()
    {
        if (drawingBoard == null)
        {
            return;
        }

        drawingBoard.ToggleEraser();
        RefreshActiveColorPreview();
        RefreshFillVisual();
        RefreshEraserVisual();
        RefreshCustomColorButtonVisual();
    }

    private void OnFillButtonClicked()
    {
        if (drawingBoard == null)
        {
            return;
        }

        drawingBoard.ToggleFillTool();
        RefreshActiveColorPreview();
        RefreshFillVisual();
        RefreshEraserVisual();
        RefreshCustomColorButtonVisual();
    }

    private void OnCustomColorButtonClicked()
    {
        if (customColorPicker != null)
        {
            customColorPicker.SetColor(GetPickerSeedColor(GetEditingSeedColor()), notify: false);
        }

        SetCustomPanelVisible(!_customPanelVisible);
        RefreshPaletteSelection();
        RefreshCustomColorButtonVisual();
    }

    private void OnApplyCustomColorClicked()
    {
        if (drawingBoard == null)
        {
            return;
        }

        int targetSlotIndex = ResolveTargetCustomSlotIndex();
        _selectedCustomSlotIndex = targetSlotIndex;
        AssignCustomSlotColor(targetSlotIndex, _customColor);
        drawingBoard.SetBrushColor(_customColor);
        SetCustomPanelVisible(false);
        RefreshPaletteSelection();
        RefreshActiveColorPreview();
        RefreshFillVisual();
        RefreshEraserVisual();
        RefreshCustomColorButtonVisual();
    }

    private void OnCustomPickerChanged(Color color)
    {
        _customColor = color;
        RefreshCustomColorButtonVisual();
    }

    private void OnUndoButtonClicked()
    {
        if (drawingBoard == null)
        {
            return;
        }

        if (drawingBoard.Undo())
        {
            RefreshPaletteSelection();
            RefreshActiveColorPreview();
            RefreshFillVisual();
            RefreshEraserVisual();
            RefreshCustomColorButtonVisual();
        }
    }

    private void OnRedoButtonClicked()
    {
        if (drawingBoard == null)
        {
            return;
        }

        if (drawingBoard.Redo())
        {
            RefreshPaletteSelection();
            RefreshActiveColorPreview();
            RefreshFillVisual();
            RefreshEraserVisual();
            RefreshCustomColorButtonVisual();
        }
    }

    private void AssignCustomSlotColor(int slotIndex, Color color)
    {
        if (slotIndex < 0 || slotIndex >= _customSlotColors.Length)
        {
            return;
        }

        _customSlotAssigned[slotIndex] = true;
        _customSlotColors[slotIndex] = (Color32)color;

        PaletteSwatch swatch = _customSwatches[slotIndex];
        swatch.IsAssigned = true;
        swatch.SwatchColor = (Color32)color;
        RefreshSwatchVisual(swatch, false);
    }

    private int ResolveTargetCustomSlotIndex()
    {
        if (_selectedCustomSlotIndex >= 0 && _selectedCustomSlotIndex < _customSwatches.Count)
        {
            return _selectedCustomSlotIndex;
        }

        for (int i = 0; i < _customSlotAssigned.Length; i++)
        {
            if (!_customSlotAssigned[i])
            {
                return i;
            }
        }

        return 0;
    }

    private Color GetEditingSeedColor()
    {
        if (_selectedCustomSlotIndex >= 0 &&
            _selectedCustomSlotIndex < _customSlotAssigned.Length &&
            _customSlotAssigned[_selectedCustomSlotIndex])
        {
            return _customSlotColors[_selectedCustomSlotIndex];
        }

        return _customColor;
    }

    private static Color GetPickerSeedColor(Color sourceColor)
    {
        Color.RGBToHSV(sourceColor, out float hue, out float saturation, out float value);
        if (value <= 0.01f)
        {
            return Color.HSVToRGB(hue, saturation, 1f);
        }

        return sourceColor;
    }

    private void UpdateBrushSizeLabel(int brushSize)
    {
        if (brushSizeValueText != null)
        {
            brushSizeValueText.text = brushSize.ToString();
        }
    }

    private void RefreshPaletteSelection()
    {
        if (drawingBoard == null)
        {
            return;
        }

        Color32 currentBrushColor = (Color32)drawingBoard.BrushColor;
        _isBrushColorInDefaultPalette = false;

        foreach (PaletteSwatch swatch in _defaultSwatches)
        {
            bool selected = ColorsEqual(swatch.SwatchColor, currentBrushColor);
            if (selected)
            {
                _isBrushColorInDefaultPalette = true;
            }

            RefreshSwatchVisual(swatch, selected);
        }

        foreach (PaletteSwatch swatch in _customSwatches)
        {
            bool selectedColor = swatch.IsAssigned && ColorsEqual(swatch.SwatchColor, currentBrushColor);
            bool selectedEmptySlot = !swatch.IsAssigned && swatch.SlotIndex == _selectedCustomSlotIndex;
            RefreshSwatchVisual(swatch, selectedColor || selectedEmptySlot);
        }
    }

    private void RefreshSwatchVisual(PaletteSwatch swatch, bool selected)
    {
        if (swatch.BorderImage != null)
        {
            if (selected)
            {
                swatch.BorderImage.color = swatch.IsCustomSlot && !swatch.IsAssigned
                    ? swatchEmptySelectedColor
                    : swatchSelectedColor;
            }
            else
            {
                swatch.BorderImage.color = swatchBorderColor;
            }
        }

        if (swatch.FillImage != null)
        {
            swatch.FillImage.sprite = _circleSprite;
            swatch.FillImage.color = swatch.IsAssigned ? swatch.SwatchColor : swatchEmptyFillColor;
        }
    }

    private void RefreshActiveColorPreview()
    {
        if (drawingBoard == null)
        {
            return;
        }

        if (activeColorPreview != null)
        {
            activeColorPreview.sprite = _circleSprite;
            activeColorPreview.color = drawingBoard.ActiveDrawColor;
        }

        if (activeColorStatusText != null)
        {
            if (drawingBoard.IsEraserEnabled)
            {
                activeColorStatusText.text = "Current: Eraser";
            }
            else if (drawingBoard.IsFillToolEnabled)
            {
                activeColorStatusText.text = "Current: Fill";
            }
            else
            {
                activeColorStatusText.text = "Current: Brush";
            }
        }
    }

    private void RefreshEraserVisual()
    {
        bool eraserOn = drawingBoard != null && drawingBoard.IsEraserEnabled;

        if (eraserButtonText != null)
        {
            eraserButtonText.text = "Eraser";
        }

        if (eraserButtonImage != null)
        {
            eraserButtonImage.color = eraserOn ? eraserOnColor : eraserOffColor;
        }
    }

    private void RefreshFillVisual()
    {
        bool fillOn = drawingBoard != null && drawingBoard.IsFillToolEnabled;

        if (fillButtonText != null)
        {
            fillButtonText.text = "Fill";
        }

        if (fillButtonImage != null)
        {
            fillButtonImage.color = fillOn ? fillOnColor : fillOffColor;
        }
    }

    private void RefreshCustomColorButtonVisual()
    {
        if (customColorButtonText != null)
        {
            customColorButtonText.text = _customPanelVisible ? "Close Custom" : "Custom Color";
        }

        bool usingCustomColor =
            drawingBoard != null &&
            !drawingBoard.IsEraserEnabled &&
            !_isBrushColorInDefaultPalette;

        if (customColorButtonImage != null)
        {
            if (_customPanelVisible)
            {
                customColorButtonImage.color = customButtonOpenColor;
            }
            else if (usingCustomColor || _selectedCustomSlotIndex >= 0)
            {
                customColorButtonImage.color = customButtonSelectedColor;
            }
            else
            {
                customColorButtonImage.color = customButtonColor;
            }
        }

        if (customColorButtonPreview != null)
        {
            customColorButtonPreview.sprite = _circleSprite;
            customColorButtonPreview.color = _customColor;
        }
    }

    private void RefreshHistoryButtons()
    {
        bool canUndo = drawingBoard != null && drawingBoard.CanUndo;
        bool canRedo = drawingBoard != null && drawingBoard.CanRedo;

        if (undoButton != null)
        {
            undoButton.interactable = canUndo;
        }

        if (redoButton != null)
        {
            redoButton.interactable = canRedo;
        }

        if (undoButtonImage != null)
        {
            undoButtonImage.color = canUndo ? historyButtonEnabledColor : historyButtonDisabledColor;
        }

        if (redoButtonImage != null)
        {
            redoButtonImage.color = canRedo ? historyButtonEnabledColor : historyButtonDisabledColor;
        }
    }

    private void SetCustomPanelVisible(bool visible)
    {
        _customPanelVisible = visible;

        if (customColorPanel != null)
        {
            customColorPanel.gameObject.SetActive(visible);
        }

        RefreshControlPanelOverlayOrder();
    }

    private void RefreshControlPanelOverlayOrder()
    {
        if (controlPanel == null)
        {
            return;
        }

        Transform parent = controlPanel.parent;
        if (parent == null)
        {
            return;
        }

        if (_controlPanelParent != parent || _controlPanelBaseSiblingIndex < 0)
        {
            CacheControlPanelSortingState();
        }

        if (_customPanelVisible)
        {
            if (!_controlPanelRaisedForCustomPanel)
            {
                _controlPanelBaseSiblingIndex = controlPanel.GetSiblingIndex();
            }

            controlPanel.SetAsLastSibling();
            if (customColorPanel != null)
            {
                customColorPanel.SetAsLastSibling();
            }

            _controlPanelRaisedForCustomPanel = true;
            return;
        }

        if (!_controlPanelRaisedForCustomPanel || _controlPanelParent != parent)
        {
            return;
        }

        controlPanel.SetSiblingIndex(Mathf.Clamp(_controlPanelBaseSiblingIndex, 0, parent.childCount - 1));
        _controlPanelRaisedForCustomPanel = false;
    }

    private void OnHistoryStateChanged(bool canUndo, bool canRedo)
    {
        RefreshHistoryButtons();
    }

    private void EnsureToolButtons()
    {
        if (controlPanel == null)
        {
            return;
        }

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (fillButton == null)
        {
            fillButton = CreatePanelButton("FillButton", "Fill", new Vector2(270f, -248f), new Vector2(68f, 32f), font);
            fillButtonText = FindNamedComponent<Text>("FillButtonText");
            fillButtonImage = fillButton != null ? fillButton.GetComponent<Image>() : null;
        }

        NormalizePanelButtonLayout(fillButton, new Vector2(270f, -248f), new Vector2(68f, 32f));
        NormalizePanelButtonLayout(eraserButton, new Vector2(342f, -248f), new Vector2(68f, 32f));
    }

    private void EnsureHistoryButtons()
    {
        if (controlPanel == null)
        {
            return;
        }

        if (undoButton != null && redoButton != null)
        {
            return;
        }

        if (controlPanel.sizeDelta.y < 304f)
        {
            controlPanel.sizeDelta = new Vector2(controlPanel.sizeDelta.x, 304f);
        }

        if (customColorPanel != null && customColorPanel.anchoredPosition.y > -314f)
        {
            customColorPanel.anchoredPosition = new Vector2(customColorPanel.anchoredPosition.x, -314f);
        }

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (undoButton == null)
        {
            undoButton = CreatePanelButton("UndoButton", "Undo", new Vector2(270f, -264f), new Vector2(68f, 32f), font);
            undoButtonText = FindNamedComponent<Text>("UndoButtonText");
            undoButtonImage = undoButton != null ? undoButton.GetComponent<Image>() : null;
        }

        if (redoButton == null)
        {
            redoButton = CreatePanelButton("RedoButton", "Redo", new Vector2(342f, -264f), new Vector2(68f, 32f), font);
            redoButtonText = FindNamedComponent<Text>("RedoButtonText");
            redoButtonImage = redoButton != null ? redoButton.GetComponent<Image>() : null;
        }
    }

    private static void NormalizePanelButtonLayout(Button button, Vector2 anchoredPosition, Vector2 size)
    {
        if (button == null)
        {
            return;
        }

        RectTransform rectTransform = button.GetComponent<RectTransform>();
        if (rectTransform == null)
        {
            return;
        }

        rectTransform.anchorMin = new Vector2(0f, 1f);
        rectTransform.anchorMax = new Vector2(0f, 1f);
        rectTransform.pivot = new Vector2(0f, 1f);
        rectTransform.anchoredPosition = anchoredPosition;
        rectTransform.sizeDelta = size;
    }

    private Button CreatePanelButton(string buttonName, string label, Vector2 anchoredPosition, Vector2 size, Font font)
    {
        var buttonObject = new GameObject(buttonName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(controlPanel, false);
        MarkRuntimeObject(buttonObject);

        RectTransform rectTransform = buttonObject.GetComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0f, 1f);
        rectTransform.anchorMax = new Vector2(0f, 1f);
        rectTransform.pivot = new Vector2(0f, 1f);
        rectTransform.anchoredPosition = anchoredPosition;
        rectTransform.sizeDelta = size;

        Image image = buttonObject.GetComponent<Image>();
        image.color = historyButtonDisabledColor;

        Button button = buttonObject.GetComponent<Button>();
        button.transition = Selectable.Transition.ColorTint;

        var textObject = new GameObject($"{buttonName}Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        textObject.transform.SetParent(buttonObject.transform, false);
        MarkRuntimeObject(textObject);

        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        Text text = textObject.GetComponent<Text>();
        text.text = label;
        text.font = font;
        text.fontSize = 13;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.raycastTarget = false;

        return button;
    }

    private void ClearPaletteChildren()
    {
        for (int i = paletteGrid.childCount - 1; i >= 0; i--)
        {
            SafeDestroy(paletteGrid.GetChild(i).gameObject);
        }
    }

    private void EnsureCircleSprite()
    {
        if (_circleSprite != null)
        {
            return;
        }

        const int size = 64;
        _circleTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "DrawingPaletteCircle",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        _circleTexture.hideFlags = RuntimeHideFlags;

        float center = (size - 1) * 0.5f;
        float radius = (size - 3) * 0.5f;
        float feather = 1.4f;
        var pixels = new Color32[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - center;
                float dy = y - center;
                float distance = Mathf.Sqrt((dx * dx) + (dy * dy));
                float alpha = Mathf.Clamp01((radius - distance) / feather);
                pixels[(y * size) + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        _circleTexture.SetPixels32(pixels);
        _circleTexture.Apply(updateMipmaps: false, makeNoLongerReadable: false);

        _circleSprite = Sprite.Create(
            _circleTexture,
            new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f),
            size);
        _circleSprite.name = "DrawingPaletteCircleSprite";
        _circleSprite.hideFlags = RuntimeHideFlags;
    }

    private T FindNamedComponent<T>(string objectName) where T : Component
    {
        T[] components = GetComponentsInChildren<T>(true);
        for (int i = 0; i < components.Length; i++)
        {
            if (components[i].name == objectName)
            {
                return components[i];
            }
        }

        return null;
    }

    private static Vector2 GetAnchoredPosition(RectTransform rectTransform)
    {
        return rectTransform != null ? rectTransform.anchoredPosition : Vector2.zero;
    }

    private static void SetHeight(RectTransform rectTransform, float height)
    {
        if (rectTransform == null)
        {
            return;
        }

        rectTransform.sizeDelta = new Vector2(rectTransform.sizeDelta.x, height);
    }

    private static void SetAnchoredPosition(RectTransform rectTransform, Vector2 basePosition, float extraHeight)
    {
        if (rectTransform == null)
        {
            return;
        }

        rectTransform.anchoredPosition = new Vector2(basePosition.x, basePosition.y - extraHeight);
    }

    private static void SafeDestroy(Object target)
    {
        if (target == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(target);
        }
        else
        {
            DestroyImmediate(target);
        }
    }

    private void ClearCircleSpriteReferences()
    {
        ClearImageSprite(activeColorPreview);
        ClearImageSprite(customColorButtonPreview);

        foreach (PaletteSwatch swatch in _allSwatches)
        {
            ClearImageSprite(swatch.BorderImage);
            ClearImageSprite(swatch.FillImage);
        }
    }

    private void ClearImageSprite(Image image)
    {
        if (image == null || image.sprite != _circleSprite)
        {
            return;
        }

        image.sprite = null;
    }

    private static void MarkRuntimeObject(GameObject target)
    {
        if (target == null)
        {
            return;
        }

        target.hideFlags = RuntimeHideFlags;
        Component[] components = target.GetComponents<Component>();
        for (int i = 0; i < components.Length; i++)
        {
            if (components[i] != null)
            {
                components[i].hideFlags = RuntimeHideFlags;
            }
        }
    }

    private static bool ColorsEqual(Color32 left, Color32 right)
    {
        return left.r == right.r &&
               left.g == right.g &&
               left.b == right.b &&
               left.a == right.a;
    }
}
