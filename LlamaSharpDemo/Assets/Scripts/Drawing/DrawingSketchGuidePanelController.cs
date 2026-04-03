using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Lightweight runtime UGUI panel for Sketch Guide mode and Stable Diffusion generation.
/// </summary>
public class DrawingSketchGuidePanelController : MonoBehaviour
{
    private const HideFlags RuntimeHideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
    private const float PaletteCardWidth = 124f;
    private const float PaletteCardHeight = 116f;
    private const float PaletteCardSpacing = 12f;

    private sealed class CandidateCard
    {
        public RectTransform Root;
        public Image CheckerImage;
        public Image PreviewImage;
        public Button SelectButton;
        public Text LabelText;
        public Sprite PreviewSprite;
    }

    [Header("References")]
    [SerializeField] private DrawingBoardController drawingBoard;
    [SerializeField] private DrawingSketchGuideGenerator sketchGuideGenerator;
    [SerializeField] private RectTransform controlPanel;
    [SerializeField] private RectTransform analysisPanel;

    [Header("Layout")]
    [SerializeField] private Vector2 panelAnchoredPosition = new(-20f, -20f);
    [SerializeField] private Vector2 panelSize = new(430f, 548f);
    [SerializeField] private Vector2 candidateStripAnchoredPosition = new(0f, 18f);
    [SerializeField] private Vector2 candidateStripSize = new(960f, 164f);

    [Header("Theme")]
    [SerializeField] private Color panelColor = new(0.11f, 0.12f, 0.15f, 0.96f);
    [SerializeField] private Color fieldColor = new(0.18f, 0.20f, 0.25f, 1f);
    [SerializeField] private Color buttonOffColor = new(0.22f, 0.24f, 0.29f, 1f);
    [SerializeField] private Color guideOnColor = new(0.08f, 0.66f, 0.82f, 1f);
    [SerializeField] private Color generateColor = new(0.16f, 0.52f, 0.28f, 1f);
    [SerializeField] private Color cancelColor = new(0.72f, 0.30f, 0.22f, 1f);
    [SerializeField] private Color disabledColor = new(0.16f, 0.17f, 0.20f, 0.8f);
    [SerializeField] private Color candidateStripColor = new(0.10f, 0.11f, 0.14f, 0.94f);
    [SerializeField] private Color candidateCardColor = new(0.19f, 0.21f, 0.26f, 1f);
    [SerializeField] private Color candidateCardBorderColor = new(0.42f, 0.46f, 0.54f, 1f);

    private RectTransform _panelRoot;
    private RectTransform _candidateStripRoot;
    private RectTransform _candidateContentRoot;
    private Button _guideButton;
    private Image _guideButtonImage;
    private Text _guideButtonText;
    private Button _clearGuideButton;
    private Button _generateButton;
    private Button _cancelButton;
    private Dropdown _modelProfileDropdown;
    private Dropdown _stylePresetDropdown;
    private InputField _promptInput;
    private Slider _controlStrengthSlider;
    private Text _controlStrengthValueText;
    private Slider _backgroundRemovalToleranceSlider;
    private Text _backgroundRemovalToleranceValueText;
    private Slider _stickerOpacitySlider;
    private Text _stickerOpacityValueText;
    private Button _confirmStickerButton;
    private Button _maskEraseButton;
    private Image _maskEraseButtonImage;
    private Text _maskEraseButtonText;
    private Button _deleteStickerButton;
    private Text _deleteStickerButtonText;
    private RectTransform _generationProgressRoot;
    private Image _generationProgressFillImage;
    private Text _generationProgressText;
    private RectTransform _livePreviewRoot;
    private Image _livePreviewImage;
    private Sprite _livePreviewSprite;
    private RectTransform _paletteLivePreviewRoot;
    private Image _paletteLivePreviewImage;
    private Image _paletteLivePreviewProgressFillImage;
    private Text _paletteLivePreviewLabelText;
    private Sprite _paletteLivePreviewSprite;
    private Text _statusText;
    private Text _stickerStatusText;
    private readonly List<string> _modelProfileOptions = new();
    private readonly List<string> _stylePresetOptions = new();
    private readonly List<CandidateCard> _candidateCards = new();
    private Texture2D _checkerTexture;
    private Sprite _checkerSprite;

    private void Awake()
    {
        if (drawingBoard == null)
        {
            drawingBoard = FindFirstObjectByType<DrawingBoardController>();
        }

        if (sketchGuideGenerator == null)
        {
            sketchGuideGenerator = FindFirstObjectByType<DrawingSketchGuideGenerator>();
        }

        if (sketchGuideGenerator == null && drawingBoard != null)
        {
            sketchGuideGenerator = drawingBoard.GetComponent<DrawingSketchGuideGenerator>();
            if (sketchGuideGenerator == null)
            {
                sketchGuideGenerator = drawingBoard.gameObject.AddComponent<DrawingSketchGuideGenerator>();
            }
        }

        if (controlPanel == null)
        {
            controlPanel = FindNamedComponent<RectTransform>("ControlPanel");
        }

        if (analysisPanel == null)
        {
            analysisPanel = FindNamedComponent<RectTransform>("AnalysisPanel");
        }

        BuildPanel();
        BindControls();
        SyncControlsFromGenerator();
        RefreshVisualState();
    }

    private void OnEnable()
    {
        if (drawingBoard != null)
        {
            drawingBoard.SketchGuideStateChanged += OnSketchGuideStateChanged;
            drawingBoard.HistoryStateChanged += OnHistoryStateChanged;
            drawingBoard.StickerSelectionChanged += OnStickerSelectionChanged;
        }

        if (sketchGuideGenerator != null)
        {
            sketchGuideGenerator.GenerationStateChanged += OnGenerationStateChanged;
            sketchGuideGenerator.GenerationProgressChanged += OnGenerationProgressChanged;
            sketchGuideGenerator.StickerCandidatesChanged += OnStickerCandidatesChanged;
        }

        RefreshVisualState();
    }

    private void OnDisable()
    {
        if (drawingBoard != null)
        {
            drawingBoard.SketchGuideStateChanged -= OnSketchGuideStateChanged;
            drawingBoard.HistoryStateChanged -= OnHistoryStateChanged;
            drawingBoard.StickerSelectionChanged -= OnStickerSelectionChanged;
        }

        if (sketchGuideGenerator != null)
        {
            sketchGuideGenerator.GenerationStateChanged -= OnGenerationStateChanged;
            sketchGuideGenerator.GenerationProgressChanged -= OnGenerationProgressChanged;
            sketchGuideGenerator.StickerCandidatesChanged -= OnStickerCandidatesChanged;
        }
    }

    private void OnDestroy()
    {
        ClearCandidateCards();
        SafeDestroy(_paletteLivePreviewSprite);
        SafeDestroy(_livePreviewSprite);
        SafeDestroy(_checkerSprite);
        SafeDestroy(_checkerTexture);
    }

    private void BuildPanel()
    {
        if (_panelRoot != null || controlPanel == null)
        {
            return;
        }

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var rootObject = new GameObject("SketchGuidePanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        Transform parentTransform = controlPanel.parent != null ? controlPanel.parent : controlPanel;
        rootObject.transform.SetParent(parentTransform, false);
        rootObject.transform.SetAsLastSibling();
        MarkRuntimeObject(rootObject);

        _panelRoot = rootObject.GetComponent<RectTransform>();
        _panelRoot.anchorMin = new Vector2(1f, 1f);
        _panelRoot.anchorMax = new Vector2(1f, 1f);
        _panelRoot.pivot = new Vector2(1f, 1f);
        ApplyPanelLayout();
        rootObject.GetComponent<Image>().color = panelColor;

        CreateLabel(
            _panelRoot,
            "SketchGuideTitle",
            "AI Sticker Generator",
            new Vector2(12f, -12f),
            new Vector2(390f, 24f),
            font,
            16,
            FontStyle.Bold);
        (_guideButton, _guideButtonImage, _guideButtonText) = CreateButton(
            _panelRoot,
            "SketchGuideModeButton",
            "Sketch Select",
            new Vector2(12f, -44f),
            new Vector2(192f, 30f),
            buttonOffColor,
            font);
        (_clearGuideButton, _, _) = CreateButton(
            _panelRoot,
            "ClearSketchGuideButton",
            "Clear Guide",
            new Vector2(216f, -44f),
            new Vector2(192f, 30f),
            buttonOffColor,
            font);

        CreateLabel(_panelRoot, "SketchModelLabel", "Model", new Vector2(12f, -88f), new Vector2(192f, 20f), font, 12, FontStyle.Bold);
        _modelProfileDropdown = CreateDropdown(
            _panelRoot,
            "SketchModelDropdown",
            "No SD Profile",
            new Vector2(12f, -110f),
            new Vector2(192f, 28f),
            font);

        CreateLabel(_panelRoot, "SketchStyleLabel", "Style", new Vector2(216f, -88f), new Vector2(192f, 20f), font, 12, FontStyle.Bold);
        _stylePresetDropdown = CreateDropdown(
            _panelRoot,
            "SketchStyleDropdown",
            "Clean Sticker",
            new Vector2(216f, -110f),
            new Vector2(192f, 28f),
            font);

        CreateLabel(_panelRoot, "SketchPromptLabel", "Object Prompt", new Vector2(12f, -150f), new Vector2(396f, 20f), font, 12, FontStyle.Bold);
        _promptInput = CreateInputField(
            _panelRoot,
            "SketchPromptInput",
            "Short keyword works best. Example: cat, rusty sword, flower bouquet",
            new Vector2(12f, -172f),
            new Vector2(396f, 56f),
            font);

        CreateSliderRow(
            _panelRoot,
            "Control",
            new Vector2(12f, -244f),
            font,
            0f,
            2f,
            0.95f,
            out _controlStrengthSlider,
            out _controlStrengthValueText);
        CreateSliderRow(
            _panelRoot,
            "BG Cut",
            new Vector2(12f, -274f),
            font,
            0.01f,
            0.5f,
            0.08f,
            out _backgroundRemovalToleranceSlider,
            out _backgroundRemovalToleranceValueText);

        (_generateButton, _, _) = CreateButton(
            _panelRoot,
            "GenerateSketchGuideButton",
            "Generate Sticker",
            new Vector2(12f, -308f),
            new Vector2(192f, 28f),
            generateColor,
            font);
        (_cancelButton, _, _) = CreateButton(
            _panelRoot,
            "CancelSketchGuideButton",
            "Cancel",
            new Vector2(216f, -308f),
            new Vector2(192f, 28f),
            cancelColor,
            font);
        _statusText = CreateLabel(
            _panelRoot,
            "SketchGuideStatusText",
            "Sketch sticker generator is ready.",
            new Vector2(12f, -342f),
            new Vector2(324f, 34f),
            font,
            11,
            FontStyle.Normal);
        _statusText.alignment = TextAnchor.UpperLeft;
        _statusText.horizontalOverflow = HorizontalWrapMode.Wrap;
        _statusText.verticalOverflow = VerticalWrapMode.Truncate;

        BuildLiveGenerationPreview(font);

        CreateLabel(_panelRoot, "StickerEditLabel", "Selected Sticker", new Vector2(12f, -394f), new Vector2(396f, 20f), font, 12, FontStyle.Bold);
        CreateSliderRow(
            _panelRoot,
            "Opacity",
            new Vector2(12f, -418f),
            font,
            0f,
            1f,
            1f,
            out _stickerOpacitySlider,
            out _stickerOpacityValueText);
        (_confirmStickerButton, _, _) = CreateButton(
            _panelRoot,
            "ConfirmSelectedStickerButton",
            "Confirm Place",
            new Vector2(12f, -450f),
            new Vector2(124f, 28f),
            generateColor,
            font);
        (_deleteStickerButton, _, _deleteStickerButtonText) = CreateButton(
            _panelRoot,
            "DeleteSelectedStickerButton",
            "Delete Sticker",
            new Vector2(284f, -450f),
            new Vector2(124f, 28f),
            cancelColor,
            font);
        (_maskEraseButton, _maskEraseButtonImage, _maskEraseButtonText) = CreateButton(
            _panelRoot,
            "MaskEraseStickerButton",
            "Mask Erase",
            new Vector2(148f, -450f),
            new Vector2(124f, 28f),
            buttonOffColor,
            font);
        _stickerStatusText = CreateLabel(
            _panelRoot,
            "StickerStatusText",
            "Generated stickers accumulate in the palette below. Click one to place/reuse. Drag = move, Wheel = scale, Shift+Wheel = rotate, F = flip, [ ] = opacity. Mask Erase = brush-clean edges.",
            new Vector2(12f, -484f),
            new Vector2(396f, 52f),
            font,
            11,
            FontStyle.Normal);
        _stickerStatusText.alignment = TextAnchor.UpperLeft;
        _stickerStatusText.horizontalOverflow = HorizontalWrapMode.Wrap;
        _stickerStatusText.verticalOverflow = VerticalWrapMode.Truncate;

        BuildCandidateStrip(parentTransform, font);
        BuildPaletteLiveGenerationPreview(font);
    }

    private void BindControls()
    {
        if (_guideButton != null)
        {
            _guideButton.onClick.RemoveListener(OnGuideButtonClicked);
            _guideButton.onClick.AddListener(OnGuideButtonClicked);
        }

        if (_clearGuideButton != null)
        {
            _clearGuideButton.onClick.RemoveListener(OnClearGuideClicked);
            _clearGuideButton.onClick.AddListener(OnClearGuideClicked);
        }

        if (_generateButton != null)
        {
            _generateButton.onClick.RemoveListener(OnGenerateClicked);
            _generateButton.onClick.AddListener(OnGenerateClicked);
        }

        if (_cancelButton != null)
        {
            _cancelButton.onClick.RemoveListener(OnCancelClicked);
            _cancelButton.onClick.AddListener(OnCancelClicked);
        }

        if (_promptInput != null)
        {
            _promptInput.onEndEdit.RemoveListener(OnPromptChanged);
            _promptInput.onEndEdit.AddListener(OnPromptChanged);
        }

        if (_controlStrengthSlider != null)
        {
            _controlStrengthSlider.onValueChanged.RemoveListener(OnControlStrengthChanged);
            _controlStrengthSlider.onValueChanged.AddListener(OnControlStrengthChanged);
        }

        if (_backgroundRemovalToleranceSlider != null)
        {
            _backgroundRemovalToleranceSlider.onValueChanged.RemoveListener(OnBackgroundRemovalToleranceChanged);
            _backgroundRemovalToleranceSlider.onValueChanged.AddListener(OnBackgroundRemovalToleranceChanged);
        }

        if (_modelProfileDropdown != null)
        {
            _modelProfileDropdown.onValueChanged.RemoveListener(OnModelProfileChanged);
            _modelProfileDropdown.onValueChanged.AddListener(OnModelProfileChanged);
        }

        if (_stylePresetDropdown != null)
        {
            _stylePresetDropdown.onValueChanged.RemoveListener(OnStylePresetChanged);
            _stylePresetDropdown.onValueChanged.AddListener(OnStylePresetChanged);
        }

        if (_stickerOpacitySlider != null)
        {
            _stickerOpacitySlider.onValueChanged.RemoveListener(OnStickerOpacityChanged);
            _stickerOpacitySlider.onValueChanged.AddListener(OnStickerOpacityChanged);
        }

        if (_confirmStickerButton != null)
        {
            _confirmStickerButton.onClick.RemoveListener(OnConfirmStickerClicked);
            _confirmStickerButton.onClick.AddListener(OnConfirmStickerClicked);
        }

        if (_deleteStickerButton != null)
        {
            _deleteStickerButton.onClick.RemoveListener(OnDeleteStickerClicked);
            _deleteStickerButton.onClick.AddListener(OnDeleteStickerClicked);
        }

        if (_maskEraseButton != null)
        {
            _maskEraseButton.onClick.RemoveListener(OnMaskEraseClicked);
            _maskEraseButton.onClick.AddListener(OnMaskEraseClicked);
        }
    }

    private void SyncControlsFromGenerator()
    {
        if (sketchGuideGenerator == null)
        {
            return;
        }

        RefreshModelProfileDropdown();
        RefreshStylePresetDropdown();

        if (_promptInput != null)
        {
            _promptInput.SetTextWithoutNotify(sketchGuideGenerator.Prompt ?? string.Empty);
        }

        if (_controlStrengthSlider != null)
        {
            _controlStrengthSlider.SetValueWithoutNotify(sketchGuideGenerator.ControlStrength);
        }

        if (_backgroundRemovalToleranceSlider != null)
        {
            _backgroundRemovalToleranceSlider.SetValueWithoutNotify(sketchGuideGenerator.BackgroundRemovalTolerance);
        }

        UpdateSliderValueLabel(_controlStrengthValueText, _controlStrengthSlider != null ? _controlStrengthSlider.value : sketchGuideGenerator.ControlStrength);
        UpdateSliderValueLabel(
            _backgroundRemovalToleranceValueText,
            _backgroundRemovalToleranceSlider != null ? _backgroundRemovalToleranceSlider.value : sketchGuideGenerator.BackgroundRemovalTolerance,
            "0.00");
        UpdateStickerOpacityControls();
        if (_statusText != null)
        {
            _statusText.text = sketchGuideGenerator.StatusMessage;
        }

        RefreshGenerationProgressVisuals(
            sketchGuideGenerator.IsGenerating,
            sketchGuideGenerator.GenerationProgress01,
            sketchGuideGenerator.LivePreviewTexture,
            sketchGuideGenerator.StatusMessage);

        RefreshCandidateStrip(sketchGuideGenerator.StickerCandidates);
    }

    private void OnGuideButtonClicked()
    {
        if (drawingBoard == null || drawingBoard.IsInteractionLocked)
        {
            return;
        }

        drawingBoard.ToggleSketchGuide();
        RefreshVisualState();
    }

    private void OnClearGuideClicked()
    {
        if (drawingBoard == null || drawingBoard.IsInteractionLocked)
        {
            return;
        }

        drawingBoard.ClearSketchGuide();
        RefreshVisualState();
    }

    private void OnGenerateClicked()
    {
        if (sketchGuideGenerator == null)
        {
            return;
        }

        PushInputValuesToGenerator();
        sketchGuideGenerator.GenerateFromCurrentGuide();
        RefreshVisualState();
    }

    private void OnCancelClicked()
    {
        sketchGuideGenerator?.CancelGeneration();
    }

    private void OnPromptChanged(string value)
    {
        if (sketchGuideGenerator != null)
        {
            sketchGuideGenerator.Prompt = value;
        }
    }

    private void OnControlStrengthChanged(float value)
    {
        if (sketchGuideGenerator != null)
        {
            sketchGuideGenerator.ControlStrength = value;
        }

        UpdateSliderValueLabel(_controlStrengthValueText, value);
    }

    private void OnBackgroundRemovalToleranceChanged(float value)
    {
        if (sketchGuideGenerator != null)
        {
            sketchGuideGenerator.BackgroundRemovalTolerance = value;
        }

        UpdateSliderValueLabel(_backgroundRemovalToleranceValueText, value, "0.00");
    }

    private void OnModelProfileChanged(int profileIndex)
    {
        if (sketchGuideGenerator == null)
        {
            return;
        }

        sketchGuideGenerator.SelectModelProfileIndex(profileIndex);
        SyncControlsFromGenerator();
        RefreshVisualState();
    }

    private void OnStylePresetChanged(int presetIndex)
    {
        if (sketchGuideGenerator == null)
        {
            return;
        }

        sketchGuideGenerator.SelectStylePresetIndex(presetIndex);
        SyncControlsFromGenerator();
        RefreshVisualState();
    }

    private void OnStickerOpacityChanged(float value)
    {
        if (drawingBoard != null)
        {
            drawingBoard.SetSelectedStickerOpacity(value);
        }

        UpdateSliderValueLabel(_stickerOpacityValueText, value, "0.00");
        RefreshStickerStatusText();
    }

    private void OnConfirmStickerClicked()
    {
        drawingBoard?.ConfirmSelectedStickerPlacement();
        UpdateStickerOpacityControls();
        RefreshStickerStatusText();
        RefreshVisualState();
    }

    private void OnDeleteStickerClicked()
    {
        drawingBoard?.DeleteSelectedSticker();
        UpdateStickerOpacityControls();
        RefreshStickerStatusText();
        RefreshVisualState();
    }

    private void OnMaskEraseClicked()
    {
        drawingBoard?.ToggleStickerMaskErase();
        UpdateStickerOpacityControls();
        RefreshStickerStatusText();
        RefreshVisualState();
    }

    private void OnSketchGuideStateChanged(bool sketchGuideEnabled, bool hasSketchGuide)
    {
        RefreshVisualState();
    }

    private void OnHistoryStateChanged(bool canUndo, bool canRedo)
    {
        RefreshVisualState();
    }

    private void OnGenerationStateChanged(bool isGenerating, string message)
    {
        if (_statusText != null)
        {
            _statusText.text = message;
        }

        RefreshGenerationProgressVisuals(
            isGenerating,
            sketchGuideGenerator != null ? sketchGuideGenerator.GenerationProgress01 : 0f,
            sketchGuideGenerator != null ? sketchGuideGenerator.LivePreviewTexture : null,
            message);
        RefreshVisualState();
    }

    private void OnGenerationProgressChanged(float progress01, Texture2D previewTexture, string message)
    {
        if (_statusText != null && !string.IsNullOrWhiteSpace(message))
        {
            _statusText.text = message;
        }

        RefreshGenerationProgressVisuals(
            sketchGuideGenerator != null && sketchGuideGenerator.IsGenerating,
            progress01,
            previewTexture,
            message);
    }

    private void OnStickerCandidatesChanged(IReadOnlyList<DrawingStickerCandidate> candidates, string message)
    {
        RefreshCandidateStrip(candidates);
        if (!string.IsNullOrWhiteSpace(message) && _statusText != null)
        {
            _statusText.text = message;
        }

        RefreshVisualState();
    }

    private void OnStickerSelectionChanged(bool hasSticker, float opacity, string stickerName)
    {
        UpdateStickerOpacityControls();
        RefreshStickerStatusText();
        RefreshVisualState();
    }

    private void PushInputValuesToGenerator()
    {
        if (sketchGuideGenerator == null)
        {
            return;
        }

        sketchGuideGenerator.Prompt = _promptInput != null ? _promptInput.text : sketchGuideGenerator.Prompt;
        if (_controlStrengthSlider != null)
        {
            sketchGuideGenerator.ControlStrength = _controlStrengthSlider.value;
        }

        if (_backgroundRemovalToleranceSlider != null)
        {
            sketchGuideGenerator.BackgroundRemovalTolerance = _backgroundRemovalToleranceSlider.value;
        }

        if (_stylePresetDropdown != null)
        {
            sketchGuideGenerator.SelectStylePresetIndex(_stylePresetDropdown.value);
        }
    }

    private void RefreshVisualState()
    {
        bool sketchGuideEnabled = drawingBoard != null && drawingBoard.IsSketchGuideEnabled;
        bool hasGuide = drawingBoard != null && drawingBoard.HasSketchGuide;
        bool isGenerating = sketchGuideGenerator != null && sketchGuideGenerator.IsGenerating;
        bool boardLocked = drawingBoard != null && drawingBoard.IsInteractionLocked;
        bool hasSelectedSticker = drawingBoard != null && drawingBoard.HasSelectedSticker;
        bool maskEraseEnabled = hasSelectedSticker && drawingBoard.IsStickerMaskEraseEnabled;

        if (_guideButtonImage != null)
        {
            _guideButtonImage.color = sketchGuideEnabled ? guideOnColor : buttonOffColor;
        }

        if (_guideButtonText != null)
        {
            _guideButtonText.text = sketchGuideEnabled ? "Selection On" : "Sketch Select";
        }

        SetButtonInteractable(
            _guideButton,
            _guideButtonImage,
            !isGenerating && !boardLocked,
            sketchGuideEnabled ? guideOnColor : buttonOffColor);
        SetButtonInteractable(
            _clearGuideButton,
            _clearGuideButton != null ? _clearGuideButton.GetComponent<Image>() : null,
            hasGuide && !isGenerating && !boardLocked,
            buttonOffColor);
        SetButtonInteractable(
            _generateButton,
            _generateButton != null ? _generateButton.GetComponent<Image>() : null,
            hasGuide && !isGenerating && sketchGuideGenerator != null,
            generateColor);
        SetButtonInteractable(
            _cancelButton,
            _cancelButton != null ? _cancelButton.GetComponent<Image>() : null,
            isGenerating,
            cancelColor);
        if (_cancelButton != null)
        {
            _cancelButton.gameObject.SetActive(isGenerating);
        }

        if (_modelProfileDropdown != null)
        {
            _modelProfileDropdown.interactable = !isGenerating && !boardLocked && sketchGuideGenerator != null &&
                                                 sketchGuideGenerator.GetModelProfileCount() > 1;
        }

        if (_stylePresetDropdown != null)
        {
            _stylePresetDropdown.interactable = !isGenerating && !boardLocked && sketchGuideGenerator != null &&
                                                sketchGuideGenerator.GetStylePresetCount() > 1;
        }

        if (_backgroundRemovalToleranceSlider != null)
        {
            _backgroundRemovalToleranceSlider.interactable = !isGenerating && sketchGuideGenerator != null;
        }

        UpdateStickerOpacityControls();
        SetButtonInteractable(
            _confirmStickerButton,
            _confirmStickerButton != null ? _confirmStickerButton.GetComponent<Image>() : null,
            hasSelectedSticker && !isGenerating && !boardLocked,
            generateColor);
        SetButtonInteractable(
            _deleteStickerButton,
            _deleteStickerButton != null ? _deleteStickerButton.GetComponent<Image>() : null,
            hasSelectedSticker && !isGenerating && !boardLocked,
            cancelColor);
        if (_maskEraseButtonImage != null)
        {
            _maskEraseButtonImage.color = maskEraseEnabled ? guideOnColor : buttonOffColor;
        }

        if (_maskEraseButtonText != null)
        {
            _maskEraseButtonText.text = maskEraseEnabled ? "Mask Erase On" : "Mask Erase";
        }

        SetButtonInteractable(
            _maskEraseButton,
            _maskEraseButtonImage,
            hasSelectedSticker && !isGenerating && !boardLocked,
            maskEraseEnabled ? guideOnColor : buttonOffColor);

        RefreshStickerStatusText();

        if (_statusText != null && sketchGuideGenerator != null)
        {
            _statusText.text = sketchGuideGenerator.StatusMessage;
        }

        RefreshGenerationProgressVisuals(
            isGenerating,
            sketchGuideGenerator != null ? sketchGuideGenerator.GenerationProgress01 : 0f,
            sketchGuideGenerator != null ? sketchGuideGenerator.LivePreviewTexture : null,
            sketchGuideGenerator != null ? sketchGuideGenerator.StatusMessage : string.Empty);

        if (_panelRoot != null)
        {
            ApplyPanelLayout();
        }
    }

    private Vector2 ResolvePanelAnchoredPosition()
    {
        return panelAnchoredPosition;
    }

    private void ApplyPanelLayout()
    {
        if (_panelRoot == null)
        {
            return;
        }

        _panelRoot.anchoredPosition = ResolvePanelAnchoredPosition();
        _panelRoot.sizeDelta = panelSize;

        if (_candidateStripRoot != null)
        {
            _candidateStripRoot.anchoredPosition = candidateStripAnchoredPosition;
            _candidateStripRoot.sizeDelta = candidateStripSize;
        }
    }

    private void RefreshModelProfileDropdown()
    {
        if (_modelProfileDropdown == null || sketchGuideGenerator == null)
        {
            return;
        }

        sketchGuideGenerator.GetModelProfileDisplayNames(_modelProfileOptions);
        _modelProfileDropdown.ClearOptions();

        if (_modelProfileOptions.Count == 0)
        {
            _modelProfileOptions.Add("No SD Profile");
            _modelProfileDropdown.AddOptions(_modelProfileOptions);
            _modelProfileDropdown.SetValueWithoutNotify(0);
            _modelProfileDropdown.RefreshShownValue();
            return;
        }

        _modelProfileDropdown.AddOptions(_modelProfileOptions);
        int selectedIndex = sketchGuideGenerator.GetSelectedModelProfileIndex();
        if (selectedIndex < 0 || selectedIndex >= _modelProfileOptions.Count)
        {
            selectedIndex = 0;
        }

        _modelProfileDropdown.SetValueWithoutNotify(selectedIndex);
        _modelProfileDropdown.RefreshShownValue();
    }

    private void RefreshStylePresetDropdown()
    {
        if (_stylePresetDropdown == null || sketchGuideGenerator == null)
        {
            return;
        }

        sketchGuideGenerator.GetStylePresetDisplayNames(_stylePresetOptions);
        _stylePresetDropdown.ClearOptions();

        if (_stylePresetOptions.Count == 0)
        {
            _stylePresetOptions.Add("No Style");
            _stylePresetDropdown.AddOptions(_stylePresetOptions);
            _stylePresetDropdown.SetValueWithoutNotify(0);
            _stylePresetDropdown.RefreshShownValue();
            return;
        }

        _stylePresetDropdown.AddOptions(_stylePresetOptions);
        int selectedIndex = sketchGuideGenerator.GetSelectedStylePresetIndex();
        selectedIndex = Mathf.Clamp(selectedIndex, 0, _stylePresetOptions.Count - 1);
        _stylePresetDropdown.SetValueWithoutNotify(selectedIndex);
        _stylePresetDropdown.RefreshShownValue();
    }

    private void BuildCandidateStrip(Transform parentTransform, Font font)
    {
        if (_candidateStripRoot != null || parentTransform == null)
        {
            return;
        }

        EnsureCheckerSprite();

        var stripObject = new GameObject("StickerCandidateStrip", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(ScrollRect));
        stripObject.transform.SetParent(parentTransform, false);
        stripObject.transform.SetAsLastSibling();
        MarkRuntimeObject(stripObject);

        _candidateStripRoot = stripObject.GetComponent<RectTransform>();
        _candidateStripRoot.anchorMin = new Vector2(0.5f, 0f);
        _candidateStripRoot.anchorMax = new Vector2(0.5f, 0f);
        _candidateStripRoot.pivot = new Vector2(0.5f, 0f);
        _candidateStripRoot.anchoredPosition = candidateStripAnchoredPosition;
        _candidateStripRoot.sizeDelta = candidateStripSize;
        stripObject.GetComponent<Image>().color = candidateStripColor;

        var viewportObject = new GameObject("Viewport", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Mask));
        viewportObject.transform.SetParent(stripObject.transform, false);
        MarkRuntimeObject(viewportObject);
        RectTransform viewportRect = viewportObject.GetComponent<RectTransform>();
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.offsetMin = new Vector2(12f, 8f);
        viewportRect.offsetMax = new Vector2(-12f, -32f);
        Image viewportImage = viewportObject.GetComponent<Image>();
        viewportImage.color = new Color(1f, 1f, 1f, 0.02f);
        viewportImage.raycastTarget = true;
        viewportObject.GetComponent<Mask>().showMaskGraphic = false;

        var contentObject = new GameObject("Content", typeof(RectTransform));
        contentObject.transform.SetParent(viewportObject.transform, false);
        MarkRuntimeObject(contentObject);
        _candidateContentRoot = contentObject.GetComponent<RectTransform>();
        _candidateContentRoot.anchorMin = new Vector2(0f, 1f);
        _candidateContentRoot.anchorMax = new Vector2(0f, 1f);
        _candidateContentRoot.pivot = new Vector2(0f, 1f);
        _candidateContentRoot.anchoredPosition = Vector2.zero;
        _candidateContentRoot.sizeDelta = new Vector2(candidateStripSize.x - 24f, candidateStripSize.y - 40f);

        ScrollRect scrollRect = stripObject.GetComponent<ScrollRect>();
        scrollRect.viewport = viewportRect;
        scrollRect.content = _candidateContentRoot;
        scrollRect.horizontal = true;
        scrollRect.vertical = false;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.scrollSensitivity = 24f;

        Text titleText = CreateLabel(
            _candidateStripRoot,
            "CandidateStripTitle",
            "Sticker Palette",
            new Vector2(16f, -6f),
            new Vector2(candidateStripSize.x - 32f, 20f),
            font,
            13,
            FontStyle.Bold);
        titleText.alignment = TextAnchor.MiddleLeft;
    }

    private void BuildPaletteLiveGenerationPreview(Font font)
    {
        if (_candidateContentRoot == null || _paletteLivePreviewRoot != null)
        {
            return;
        }

        EnsureCheckerSprite();

        var cardObject = new GameObject(
            "StickerPaletteLivePreviewCard",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        cardObject.transform.SetParent(_candidateContentRoot, false);
        MarkRuntimeObject(cardObject);

        _paletteLivePreviewRoot = cardObject.GetComponent<RectTransform>();
        _paletteLivePreviewRoot.anchorMin = new Vector2(0f, 1f);
        _paletteLivePreviewRoot.anchorMax = new Vector2(0f, 1f);
        _paletteLivePreviewRoot.pivot = new Vector2(0f, 1f);
        _paletteLivePreviewRoot.anchoredPosition = Vector2.zero;
        _paletteLivePreviewRoot.sizeDelta = new Vector2(PaletteCardWidth, PaletteCardHeight);
        cardObject.GetComponent<Image>().color = guideOnColor;

        var checkerObject = new GameObject("Checker", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        checkerObject.transform.SetParent(cardObject.transform, false);
        MarkRuntimeObject(checkerObject);
        RectTransform checkerRect = checkerObject.GetComponent<RectTransform>();
        checkerRect.anchorMin = new Vector2(0.5f, 1f);
        checkerRect.anchorMax = new Vector2(0.5f, 1f);
        checkerRect.pivot = new Vector2(0.5f, 1f);
        checkerRect.anchoredPosition = new Vector2(0f, -4f);
        checkerRect.sizeDelta = new Vector2(PaletteCardWidth - 8f, 84f);
        Image checkerImage = checkerObject.GetComponent<Image>();
        checkerImage.sprite = _checkerSprite;
        checkerImage.type = Image.Type.Tiled;
        checkerImage.color = Color.white;
        checkerImage.raycastTarget = false;

        var previewObject = new GameObject("Preview", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        previewObject.transform.SetParent(checkerObject.transform, false);
        MarkRuntimeObject(previewObject);
        RectTransform previewRect = previewObject.GetComponent<RectTransform>();
        previewRect.anchorMin = Vector2.zero;
        previewRect.anchorMax = Vector2.one;
        previewRect.offsetMin = new Vector2(6f, 6f);
        previewRect.offsetMax = new Vector2(-6f, -6f);
        _paletteLivePreviewImage = previewObject.GetComponent<Image>();
        _paletteLivePreviewImage.preserveAspect = true;
        _paletteLivePreviewImage.raycastTarget = false;

        var progressObject = new GameObject("Progress", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        progressObject.transform.SetParent(checkerObject.transform, false);
        MarkRuntimeObject(progressObject);
        RectTransform progressRect = progressObject.GetComponent<RectTransform>();
        progressRect.anchorMin = new Vector2(0f, 0f);
        progressRect.anchorMax = new Vector2(1f, 0f);
        progressRect.pivot = new Vector2(0.5f, 0f);
        progressRect.anchoredPosition = new Vector2(0f, 2f);
        progressRect.sizeDelta = new Vector2(-4f, 6f);
        progressObject.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.45f);

        var fillObject = new GameObject("Fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        fillObject.transform.SetParent(progressObject.transform, false);
        MarkRuntimeObject(fillObject);
        RectTransform fillRect = fillObject.GetComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
        _paletteLivePreviewProgressFillImage = fillObject.GetComponent<Image>();
        _paletteLivePreviewProgressFillImage.color = guideOnColor;
        _paletteLivePreviewProgressFillImage.type = Image.Type.Filled;
        _paletteLivePreviewProgressFillImage.fillMethod = Image.FillMethod.Horizontal;
        _paletteLivePreviewProgressFillImage.fillOrigin = 0;
        _paletteLivePreviewProgressFillImage.fillAmount = 0f;
        _paletteLivePreviewProgressFillImage.raycastTarget = false;

        _paletteLivePreviewLabelText = CreateLabel(
            _paletteLivePreviewRoot,
            "LivePreviewLabel",
            "Generating...",
            new Vector2(0f, -90f),
            new Vector2(PaletteCardWidth, 20f),
            font,
            11,
            FontStyle.Bold);
        _paletteLivePreviewLabelText.alignment = TextAnchor.MiddleCenter;
        _paletteLivePreviewLabelText.color = Color.white;

        _paletteLivePreviewRoot.gameObject.SetActive(false);
    }

    private void BuildLiveGenerationPreview(Font font)
    {
        if (_panelRoot == null || _livePreviewRoot != null)
        {
            return;
        }

        EnsureCheckerSprite();

        var previewRootObject = new GameObject("SketchGenerationLivePreview", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        previewRootObject.transform.SetParent(_panelRoot, false);
        MarkRuntimeObject(previewRootObject);
        _livePreviewRoot = previewRootObject.GetComponent<RectTransform>();
        _livePreviewRoot.anchorMin = new Vector2(0f, 1f);
        _livePreviewRoot.anchorMax = new Vector2(0f, 1f);
        _livePreviewRoot.pivot = new Vector2(0f, 1f);
        _livePreviewRoot.anchoredPosition = new Vector2(348f, -342f);
        _livePreviewRoot.sizeDelta = new Vector2(60f, 42f);
        Image previewBackground = previewRootObject.GetComponent<Image>();
        previewBackground.sprite = _checkerSprite;
        previewBackground.type = Image.Type.Tiled;
        previewBackground.color = Color.white;
        previewBackground.raycastTarget = false;

        var previewObject = new GameObject("Preview", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        previewObject.transform.SetParent(previewRootObject.transform, false);
        MarkRuntimeObject(previewObject);
        RectTransform previewRect = previewObject.GetComponent<RectTransform>();
        previewRect.anchorMin = Vector2.zero;
        previewRect.anchorMax = Vector2.one;
        previewRect.offsetMin = new Vector2(2f, 2f);
        previewRect.offsetMax = new Vector2(-2f, -2f);
        _livePreviewImage = previewObject.GetComponent<Image>();
        _livePreviewImage.preserveAspect = true;
        _livePreviewImage.raycastTarget = false;

        var progressRootObject = new GameObject("SketchGenerationProgress", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        progressRootObject.transform.SetParent(_panelRoot, false);
        MarkRuntimeObject(progressRootObject);
        _generationProgressRoot = progressRootObject.GetComponent<RectTransform>();
        _generationProgressRoot.anchorMin = new Vector2(0f, 1f);
        _generationProgressRoot.anchorMax = new Vector2(0f, 1f);
        _generationProgressRoot.pivot = new Vector2(0f, 1f);
        _generationProgressRoot.anchoredPosition = new Vector2(12f, -378f);
        _generationProgressRoot.sizeDelta = new Vector2(396f, 10f);
        progressRootObject.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.12f);

        var fillObject = new GameObject("Fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        fillObject.transform.SetParent(progressRootObject.transform, false);
        MarkRuntimeObject(fillObject);
        RectTransform fillRect = fillObject.GetComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
        _generationProgressFillImage = fillObject.GetComponent<Image>();
        _generationProgressFillImage.color = guideOnColor;
        _generationProgressFillImage.type = Image.Type.Filled;
        _generationProgressFillImage.fillMethod = Image.FillMethod.Horizontal;
        _generationProgressFillImage.fillOrigin = 0;
        _generationProgressFillImage.fillAmount = 0f;
        _generationProgressFillImage.raycastTarget = false;

        _generationProgressText = CreateLabel(
            _panelRoot,
            "SketchGenerationProgressText",
            "0%",
            new Vector2(348f, -372f),
            new Vector2(60f, 18f),
            font,
            11,
            FontStyle.Bold);
        _generationProgressText.alignment = TextAnchor.UpperRight;

        RefreshGenerationProgressVisuals(false, 0f, null, string.Empty);
    }

    private void RefreshGenerationProgressVisuals(
        bool isGenerating,
        float progress01,
        Texture2D previewTexture,
        string message)
    {
        float clampedProgress = Mathf.Clamp01(progress01);

        if (_generationProgressRoot != null)
        {
            _generationProgressRoot.gameObject.SetActive(isGenerating);
        }

        if (_generationProgressFillImage != null)
        {
            _generationProgressFillImage.fillAmount = isGenerating
                ? Mathf.Max(0.03f, clampedProgress)
                : 0f;
        }

        if (_generationProgressText != null)
        {
            _generationProgressText.gameObject.SetActive(isGenerating);
            _generationProgressText.text = $"{Mathf.RoundToInt(clampedProgress * 100f)}%";
        }

        if (_livePreviewRoot != null)
        {
            bool showPanelPreviewFallback = _paletteLivePreviewRoot == null && isGenerating && previewTexture != null;
            _livePreviewRoot.gameObject.SetActive(showPanelPreviewFallback);
        }

        if (_livePreviewImage != null && previewTexture != null && _livePreviewRoot != null && _livePreviewRoot.gameObject.activeSelf)
        {
            if (_livePreviewSprite == null || _livePreviewSprite.texture != previewTexture)
            {
                SafeDestroy(_livePreviewSprite);
                _livePreviewSprite = Sprite.Create(
                    previewTexture,
                    new Rect(0f, 0f, previewTexture.width, previewTexture.height),
                    new Vector2(0.5f, 0.5f),
                    Mathf.Max(previewTexture.width, previewTexture.height));
                _livePreviewSprite.name = $"{previewTexture.name}_LivePreviewSprite";
                _livePreviewSprite.hideFlags = RuntimeHideFlags;
            }

            _livePreviewImage.sprite = _livePreviewSprite;
        }
        else if (_livePreviewImage != null && _livePreviewRoot != null && !_livePreviewRoot.gameObject.activeSelf)
        {
            _livePreviewImage.sprite = null;
            SafeDestroy(_livePreviewSprite);
            _livePreviewSprite = null;
        }

        RefreshPaletteLiveGenerationPreviewCard(isGenerating, clampedProgress, previewTexture);

        if (_statusText != null && !string.IsNullOrWhiteSpace(message))
        {
            _statusText.text = message;
        }
    }

    private void RefreshPaletteLiveGenerationPreviewCard(
        bool isGenerating,
        float progress01,
        Texture2D previewTexture)
    {
        bool showPreviewCard = _paletteLivePreviewRoot != null && isGenerating;
        if (_paletteLivePreviewRoot != null)
        {
            _paletteLivePreviewRoot.gameObject.SetActive(showPreviewCard);
            if (showPreviewCard)
            {
                _paletteLivePreviewRoot.SetAsFirstSibling();
                _paletteLivePreviewRoot.anchoredPosition = Vector2.zero;
            }
        }

        if (_paletteLivePreviewProgressFillImage != null)
        {
            _paletteLivePreviewProgressFillImage.fillAmount = showPreviewCard
                ? Mathf.Max(0.03f, progress01)
                : 0f;
        }

        if (_paletteLivePreviewLabelText != null)
        {
            _paletteLivePreviewLabelText.text = showPreviewCard
                ? $"Generating {Mathf.RoundToInt(progress01 * 100f)}%"
                : "Generating...";
        }

        if (_paletteLivePreviewImage != null)
        {
            if (showPreviewCard && previewTexture != null)
            {
                if (_paletteLivePreviewSprite == null || _paletteLivePreviewSprite.texture != previewTexture)
                {
                    SafeDestroy(_paletteLivePreviewSprite);
                    _paletteLivePreviewSprite = Sprite.Create(
                        previewTexture,
                        new Rect(0f, 0f, previewTexture.width, previewTexture.height),
                        new Vector2(0.5f, 0.5f),
                        Mathf.Max(previewTexture.width, previewTexture.height));
                    _paletteLivePreviewSprite.name = $"{previewTexture.name}_PaletteLivePreviewSprite";
                    _paletteLivePreviewSprite.hideFlags = RuntimeHideFlags;
                }

                _paletteLivePreviewImage.sprite = _paletteLivePreviewSprite;
                _paletteLivePreviewImage.enabled = true;
            }
            else
            {
                _paletteLivePreviewImage.sprite = null;
                _paletteLivePreviewImage.enabled = false;
                SafeDestroy(_paletteLivePreviewSprite);
                _paletteLivePreviewSprite = null;
            }
        }

        RefreshCandidateStripLayout();
    }

    private void RefreshCandidateStripLayout()
    {
        if (_candidateContentRoot == null)
        {
            return;
        }

        bool reservePreviewSlot = _paletteLivePreviewRoot != null && _paletteLivePreviewRoot.gameObject.activeSelf;
        float startX = reservePreviewSlot ? PaletteCardWidth + PaletteCardSpacing : 0f;
        float stepX = PaletteCardWidth + PaletteCardSpacing;

        for (int i = 0; i < _candidateCards.Count; i++)
        {
            CandidateCard card = _candidateCards[i];
            if (card?.Root == null)
            {
                continue;
            }

            card.Root.anchoredPosition = new Vector2(startX + (i * stepX), 0f);
        }

        int slotCount = _candidateCards.Count + (reservePreviewSlot ? 1 : 0);
        float viewportWidth = _candidateStripRoot != null ? _candidateStripRoot.sizeDelta.x - 24f : 0f;
        float contentWidth = Mathf.Max(
            viewportWidth,
            slotCount > 0
                ? slotCount * PaletteCardWidth + Mathf.Max(0, slotCount - 1) * PaletteCardSpacing
                : viewportWidth);
        _candidateContentRoot.sizeDelta = new Vector2(contentWidth, PaletteCardHeight);
    }

    private void RefreshCandidateStrip(IReadOnlyList<DrawingStickerCandidate> candidates)
    {
        ClearCandidateCards();
        if (_candidateContentRoot == null)
        {
            return;
        }

        if (candidates == null || candidates.Count == 0)
        {
            RefreshCandidateStripLayout();
            return;
        }

        EnsureCheckerSprite();

        for (int i = 0; i < candidates.Count; i++)
        {
            DrawingStickerCandidate candidate = candidates[i];
            if (candidate == null || candidate.Texture == null)
            {
                continue;
            }

            CandidateCard card = CreateCandidateCard(
                _candidateContentRoot,
                candidate,
                i,
                Vector2.zero,
                new Vector2(PaletteCardWidth, PaletteCardHeight));
            _candidateCards.Add(card);
        }

        RefreshCandidateStripLayout();
    }

    private CandidateCard CreateCandidateCard(
        RectTransform parent,
        DrawingStickerCandidate candidate,
        int candidateIndex,
        Vector2 anchoredPosition,
        Vector2 size)
    {
        var cardObject = new GameObject($"StickerCandidate_{candidateIndex + 1:00}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        cardObject.transform.SetParent(parent, false);
        MarkRuntimeObject(cardObject);

        RectTransform cardRect = cardObject.GetComponent<RectTransform>();
        cardRect.anchorMin = new Vector2(0f, 1f);
        cardRect.anchorMax = new Vector2(0f, 1f);
        cardRect.pivot = new Vector2(0f, 1f);
        cardRect.anchoredPosition = anchoredPosition;
        cardRect.sizeDelta = size;

        Image borderImage = cardObject.GetComponent<Image>();
        borderImage.color = candidateCardBorderColor;

        Button button = cardObject.GetComponent<Button>();
        button.transition = Selectable.Transition.ColorTint;

        var checkerObject = new GameObject("Checker", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        checkerObject.transform.SetParent(cardObject.transform, false);
        MarkRuntimeObject(checkerObject);
        RectTransform checkerRect = checkerObject.GetComponent<RectTransform>();
        checkerRect.anchorMin = new Vector2(0.5f, 1f);
        checkerRect.anchorMax = new Vector2(0.5f, 1f);
        checkerRect.pivot = new Vector2(0.5f, 1f);
        checkerRect.anchoredPosition = new Vector2(0f, -4f);
        checkerRect.sizeDelta = new Vector2(size.x - 8f, 84f);
        Image checkerImage = checkerObject.GetComponent<Image>();
        checkerImage.sprite = _checkerSprite;
        checkerImage.type = Image.Type.Tiled;
        checkerImage.color = Color.white;
        checkerImage.raycastTarget = false;

        var previewObject = new GameObject("Preview", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        previewObject.transform.SetParent(checkerObject.transform, false);
        MarkRuntimeObject(previewObject);
        RectTransform previewRect = previewObject.GetComponent<RectTransform>();
        previewRect.anchorMin = Vector2.zero;
        previewRect.anchorMax = Vector2.one;
        previewRect.offsetMin = new Vector2(6f, 6f);
        previewRect.offsetMax = new Vector2(-6f, -6f);
        Image previewImage = previewObject.GetComponent<Image>();
        previewImage.preserveAspect = true;
        previewImage.raycastTarget = false;

        Sprite previewSprite = Sprite.Create(
            candidate.Texture,
            new Rect(0f, 0f, candidate.Texture.width, candidate.Texture.height),
            new Vector2(0.5f, 0.5f),
            Mathf.Max(candidate.Texture.width, candidate.Texture.height));
        previewSprite.name = $"{candidate.Texture.name}_PreviewSprite";
        previewSprite.hideFlags = RuntimeHideFlags;
        previewImage.sprite = previewSprite;

        Text labelText = CreateLabel(
            cardRect,
            $"StickerCandidateLabel_{candidateIndex + 1:00}",
            $"{candidateIndex + 1}",
            new Vector2(0f, -90f),
            new Vector2(size.x, 20f),
            Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),
            11,
            FontStyle.Bold);
        labelText.alignment = TextAnchor.MiddleCenter;
        labelText.color = Color.white;

        int capturedIndex = candidateIndex;
        button.onClick.AddListener(() => OnCandidateCardClicked(capturedIndex));

        return new CandidateCard
        {
            Root = cardRect,
            CheckerImage = checkerImage,
            PreviewImage = previewImage,
            SelectButton = button,
            LabelText = labelText,
            PreviewSprite = previewSprite
        };
    }

    private void ClearCandidateCards()
    {
        for (int i = _candidateCards.Count - 1; i >= 0; i--)
        {
            CandidateCard card = _candidateCards[i];
            if (card == null)
            {
                continue;
            }

            SafeDestroy(card.PreviewSprite);
            if (card.Root != null)
            {
                SafeDestroy(card.Root.gameObject);
            }
        }

        _candidateCards.Clear();
    }

    private void OnCandidateCardClicked(int candidateIndex)
    {
        if (sketchGuideGenerator == null)
        {
            return;
        }

        if (!sketchGuideGenerator.TryPlaceStickerCandidate(candidateIndex, out string error) &&
            _statusText != null)
        {
            _statusText.text = string.IsNullOrWhiteSpace(error)
                ? "Failed to place sticker candidate."
                : error;
        }

        RefreshVisualState();
    }

    private void UpdateStickerOpacityControls()
    {
        bool hasSelectedSticker = drawingBoard != null && drawingBoard.HasSelectedSticker;
        float opacity = drawingBoard != null ? drawingBoard.SelectedStickerOpacity : 1f;

        if (_stickerOpacitySlider != null)
        {
            _stickerOpacitySlider.SetValueWithoutNotify(opacity);
            _stickerOpacitySlider.interactable = hasSelectedSticker;
        }

        UpdateSliderValueLabel(_stickerOpacityValueText, opacity, "0.00");
    }

    private void RefreshStickerStatusText()
    {
        if (_stickerStatusText == null)
        {
            return;
        }

        if (drawingBoard == null || !drawingBoard.HasSelectedSticker)
        {
            _stickerStatusText.text = "Click a palette sticker to place a new layer. If one is selected, candidate click replaces it. Confirm Place pins current sticker and lets you add another.";
            return;
        }

        if (drawingBoard.IsStickerMaskEraseEnabled)
        {
            _stickerStatusText.text =
                $"Selected: {drawingBoard.SelectedStickerLabel}  Opacity {drawingBoard.SelectedStickerOpacity:0.00}\n" +
                "Mask Erase On: brush over sticker to erase alpha. Wheel = brush size. Confirm Place pins this sticker.";
            return;
        }

        _stickerStatusText.text =
            $"Selected: {drawingBoard.SelectedStickerLabel}  Opacity {drawingBoard.SelectedStickerOpacity:0.00}\n" +
            "Drag = move, Wheel = scale, Shift+Wheel = rotate, F = flip, [ ] = opacity. Confirm Place before choosing another sticker.";
    }

    private static Text CreateLabel(
        RectTransform parent,
        string objectName,
        string textValue,
        Vector2 anchoredPosition,
        Vector2 size,
        Font font,
        int fontSize,
        FontStyle fontStyle)
    {
        var textObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        textObject.transform.SetParent(parent, false);
        MarkRuntimeObject(textObject);

        RectTransform rectTransform = textObject.GetComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0f, 1f);
        rectTransform.anchorMax = new Vector2(0f, 1f);
        rectTransform.pivot = new Vector2(0f, 1f);
        rectTransform.anchoredPosition = anchoredPosition;
        rectTransform.sizeDelta = size;

        Text text = textObject.GetComponent<Text>();
        text.text = textValue;
        text.font = font;
        text.fontSize = fontSize;
        text.fontStyle = fontStyle;
        text.color = Color.white;
        text.alignment = TextAnchor.MiddleLeft;
        text.raycastTarget = false;
        return text;
    }

    private InputField CreateInputField(
        RectTransform parent,
        string objectName,
        string placeholder,
        Vector2 anchoredPosition,
        Vector2 size,
        Font font)
    {
        var root = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(InputField));
        root.transform.SetParent(parent, false);
        MarkRuntimeObject(root);

        RectTransform rootRect = root.GetComponent<RectTransform>();
        rootRect.anchorMin = new Vector2(0f, 1f);
        rootRect.anchorMax = new Vector2(0f, 1f);
        rootRect.pivot = new Vector2(0f, 1f);
        rootRect.anchoredPosition = anchoredPosition;
        rootRect.sizeDelta = size;
        root.GetComponent<Image>().color = fieldColor;

        InputField inputField = root.GetComponent<InputField>();
        inputField.lineType = size.y > 40f ? InputField.LineType.MultiLineNewline : InputField.LineType.SingleLine;
        inputField.targetGraphic = root.GetComponent<Image>();

        Text text = CreateLabel(rootRect, "Text", string.Empty, new Vector2(8f, -5f), new Vector2(size.x - 16f, size.y - 10f), font, 12, FontStyle.Normal);
        text.alignment = TextAnchor.UpperLeft;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;

        Text placeholderText = CreateLabel(
            rootRect,
            "Placeholder",
            placeholder,
            new Vector2(8f, -5f),
            new Vector2(size.x - 16f, size.y - 10f),
            font,
            12,
            FontStyle.Italic);
        placeholderText.color = new Color(1f, 1f, 1f, 0.45f);
        placeholderText.alignment = TextAnchor.UpperLeft;
        placeholderText.horizontalOverflow = HorizontalWrapMode.Wrap;
        placeholderText.verticalOverflow = VerticalWrapMode.Truncate;

        inputField.textComponent = text;
        inputField.placeholder = placeholderText;
        return inputField;
    }

    private Dropdown CreateDropdown(
        RectTransform parent,
        string objectName,
        string caption,
        Vector2 anchoredPosition,
        Vector2 size,
        Font font)
    {
        var root = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Dropdown));
        root.transform.SetParent(parent, false);
        MarkRuntimeObject(root);

        RectTransform rootRect = root.GetComponent<RectTransform>();
        rootRect.anchorMin = new Vector2(0f, 1f);
        rootRect.anchorMax = new Vector2(0f, 1f);
        rootRect.pivot = new Vector2(0f, 1f);
        rootRect.anchoredPosition = anchoredPosition;
        rootRect.sizeDelta = size;

        Image rootImage = root.GetComponent<Image>();
        rootImage.color = fieldColor;

        Text captionText = CreateLabel(
            rootRect,
            "Label",
            caption,
            new Vector2(8f, -4f),
            new Vector2(size.x - 34f, size.y - 8f),
            font,
            12,
            FontStyle.Normal);
        captionText.alignment = TextAnchor.MiddleLeft;

        Text arrowText = CreateLabel(
            rootRect,
            "Arrow",
            "v",
            new Vector2(size.x - 22f, -4f),
            new Vector2(14f, size.y - 8f),
            font,
            12,
            FontStyle.Bold);
        arrowText.alignment = TextAnchor.MiddleCenter;

        RectTransform templateRect = CreateDropdownTemplate(rootRect, size.x, font, out Text itemLabel);

        Dropdown dropdown = root.GetComponent<Dropdown>();
        dropdown.targetGraphic = rootImage;
        dropdown.captionText = captionText;
        dropdown.template = templateRect;
        dropdown.itemText = itemLabel;
        dropdown.options.Clear();
        return dropdown;
    }

    private RectTransform CreateDropdownTemplate(
        RectTransform parent,
        float width,
        Font font,
        out Text itemLabel)
    {
        var template = new GameObject("Template", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(ScrollRect));
        template.transform.SetParent(parent, false);
        MarkRuntimeObject(template);

        RectTransform templateRect = template.GetComponent<RectTransform>();
        templateRect.anchorMin = new Vector2(0f, 0f);
        templateRect.anchorMax = new Vector2(0f, 0f);
        templateRect.pivot = new Vector2(0.5f, 1f);
        templateRect.anchoredPosition = new Vector2(width * 0.5f, -2f);
        templateRect.sizeDelta = new Vector2(width, 110f);

        Image templateImage = template.GetComponent<Image>();
        templateImage.color = fieldColor;

        ScrollRect scrollRect = template.GetComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.scrollSensitivity = 18f;

        var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Mask));
        viewport.transform.SetParent(template.transform, false);
        MarkRuntimeObject(viewport);
        RectTransform viewportRect = viewport.GetComponent<RectTransform>();
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.sizeDelta = Vector2.zero;
        viewportRect.offsetMin = new Vector2(0f, 0f);
        viewportRect.offsetMax = new Vector2(0f, 0f);
        viewport.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.04f);
        viewport.GetComponent<Mask>().showMaskGraphic = false;

        var content = new GameObject("Content", typeof(RectTransform));
        content.transform.SetParent(viewport.transform, false);
        MarkRuntimeObject(content);
        RectTransform contentRect = content.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.anchoredPosition = Vector2.zero;
        contentRect.sizeDelta = new Vector2(0f, 28f);

        var item = new GameObject("Item", typeof(RectTransform), typeof(Toggle));
        item.transform.SetParent(content.transform, false);
        MarkRuntimeObject(item);
        RectTransform itemRect = item.GetComponent<RectTransform>();
        itemRect.anchorMin = new Vector2(0f, 0.5f);
        itemRect.anchorMax = new Vector2(1f, 0.5f);
        itemRect.pivot = new Vector2(0.5f, 0.5f);
        itemRect.anchoredPosition = Vector2.zero;
        itemRect.sizeDelta = new Vector2(0f, 24f);

        var itemBackground = new GameObject("Item Background", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        itemBackground.transform.SetParent(item.transform, false);
        MarkRuntimeObject(itemBackground);
        RectTransform itemBackgroundRect = itemBackground.GetComponent<RectTransform>();
        itemBackgroundRect.anchorMin = Vector2.zero;
        itemBackgroundRect.anchorMax = Vector2.one;
        itemBackgroundRect.offsetMin = Vector2.zero;
        itemBackgroundRect.offsetMax = Vector2.zero;
        Image itemBackgroundImage = itemBackground.GetComponent<Image>();
        itemBackgroundImage.color = buttonOffColor;

        var checkmark = new GameObject("Item Checkmark", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        checkmark.transform.SetParent(item.transform, false);
        MarkRuntimeObject(checkmark);
        RectTransform checkmarkRect = checkmark.GetComponent<RectTransform>();
        checkmarkRect.anchorMin = new Vector2(0f, 0.5f);
        checkmarkRect.anchorMax = new Vector2(0f, 0.5f);
        checkmarkRect.pivot = new Vector2(0.5f, 0.5f);
        checkmarkRect.anchoredPosition = new Vector2(10f, 0f);
        checkmarkRect.sizeDelta = new Vector2(8f, 8f);
        checkmark.GetComponent<Image>().color = guideOnColor;

        itemLabel = CreateLabel(
            itemRect,
            "Item Label",
            "Option",
            new Vector2(24f, 0f),
            new Vector2(width - 32f, 24f),
            font,
            12,
            FontStyle.Normal);
        RectTransform itemLabelRect = itemLabel.GetComponent<RectTransform>();
        itemLabelRect.anchorMin = new Vector2(0f, 0f);
        itemLabelRect.anchorMax = new Vector2(1f, 1f);
        itemLabelRect.offsetMin = new Vector2(24f, 0f);
        itemLabelRect.offsetMax = new Vector2(-8f, 0f);
        itemLabelRect.pivot = new Vector2(0f, 0.5f);
        itemLabel.alignment = TextAnchor.MiddleLeft;

        Toggle itemToggle = item.GetComponent<Toggle>();
        itemToggle.targetGraphic = itemBackgroundImage;
        itemToggle.graphic = checkmark.GetComponent<Image>();
        itemToggle.isOn = true;

        scrollRect.viewport = viewportRect;
        scrollRect.content = contentRect;
        template.SetActive(false);
        return templateRect;
    }

    private void CreateSliderRow(
        RectTransform parent,
        string label,
        Vector2 anchoredPosition,
        Font font,
        float min,
        float max,
        float defaultValue,
        out Slider slider,
        out Text valueText,
        string valueFormat = "0.00")
    {
        CreateLabel(parent, $"{label}Label", label, anchoredPosition, new Vector2(54f, 20f), font, 12, FontStyle.Bold);
        slider = CreateSlider(
            parent,
            $"{label}Slider",
            new Vector2(66f, anchoredPosition.y - 1f),
            new Vector2(180f, 18f),
            min,
            max,
            defaultValue);
        valueText = CreateLabel(
            parent,
            $"{label}ValueText",
            defaultValue.ToString(string.IsNullOrWhiteSpace(valueFormat) ? "0.00" : valueFormat),
            new Vector2(254f, anchoredPosition.y),
            new Vector2(54f, 20f),
            font,
            12,
            FontStyle.Bold);
        valueText.alignment = TextAnchor.MiddleRight;
    }

    private Slider CreateSlider(
        RectTransform parent,
        string objectName,
        Vector2 anchoredPosition,
        Vector2 size,
        float min,
        float max,
        float defaultValue)
    {
        var root = new GameObject(objectName, typeof(RectTransform), typeof(Slider));
        root.transform.SetParent(parent, false);
        MarkRuntimeObject(root);

        RectTransform rootRect = root.GetComponent<RectTransform>();
        rootRect.anchorMin = new Vector2(0f, 1f);
        rootRect.anchorMax = new Vector2(0f, 1f);
        rootRect.pivot = new Vector2(0f, 1f);
        rootRect.anchoredPosition = anchoredPosition;
        rootRect.sizeDelta = size;

        var background = new GameObject("Background", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        background.transform.SetParent(root.transform, false);
        MarkRuntimeObject(background);
        RectTransform backgroundRect = background.GetComponent<RectTransform>();
        backgroundRect.anchorMin = Vector2.zero;
        backgroundRect.anchorMax = Vector2.one;
        backgroundRect.offsetMin = new Vector2(0f, 6f);
        backgroundRect.offsetMax = new Vector2(0f, -6f);
        background.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.14f);

        var fillArea = new GameObject("Fill Area", typeof(RectTransform));
        fillArea.transform.SetParent(root.transform, false);
        MarkRuntimeObject(fillArea);
        RectTransform fillAreaRect = fillArea.GetComponent<RectTransform>();
        fillAreaRect.anchorMin = Vector2.zero;
        fillAreaRect.anchorMax = Vector2.one;
        fillAreaRect.offsetMin = new Vector2(0f, 6f);
        fillAreaRect.offsetMax = new Vector2(0f, -6f);

        var fill = new GameObject("Fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        fill.transform.SetParent(fillArea.transform, false);
        MarkRuntimeObject(fill);
        RectTransform fillRect = fill.GetComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
        fill.GetComponent<Image>().color = guideOnColor;

        var handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
        handleArea.transform.SetParent(root.transform, false);
        MarkRuntimeObject(handleArea);
        RectTransform handleAreaRect = handleArea.GetComponent<RectTransform>();
        handleAreaRect.anchorMin = Vector2.zero;
        handleAreaRect.anchorMax = Vector2.one;
        handleAreaRect.offsetMin = new Vector2(0f, 0f);
        handleAreaRect.offsetMax = new Vector2(0f, 0f);

        var handle = new GameObject("Handle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        handle.transform.SetParent(handleArea.transform, false);
        MarkRuntimeObject(handle);
        RectTransform handleRect = handle.GetComponent<RectTransform>();
        handleRect.sizeDelta = new Vector2(14f, 14f);
        handle.GetComponent<Image>().color = Color.white;

        Slider slider = root.GetComponent<Slider>();
        slider.minValue = min;
        slider.maxValue = max;
        slider.value = defaultValue;
        slider.direction = Slider.Direction.LeftToRight;
        slider.targetGraphic = handle.GetComponent<Image>();
        slider.fillRect = fillRect;
        slider.handleRect = handleRect;
        return slider;
    }

    private (Button button, Image image, Text text) CreateButton(
        RectTransform parent,
        string objectName,
        string label,
        Vector2 anchoredPosition,
        Vector2 size,
        Color color,
        Font font)
    {
        var buttonObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(parent, false);
        MarkRuntimeObject(buttonObject);

        RectTransform rectTransform = buttonObject.GetComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0f, 1f);
        rectTransform.anchorMax = new Vector2(0f, 1f);
        rectTransform.pivot = new Vector2(0f, 1f);
        rectTransform.anchoredPosition = anchoredPosition;
        rectTransform.sizeDelta = size;

        Image image = buttonObject.GetComponent<Image>();
        image.color = color;

        Button button = buttonObject.GetComponent<Button>();
        button.transition = Selectable.Transition.ColorTint;
        button.targetGraphic = image;

        Text text = CreateLabel(rectTransform, $"{objectName}Text", label, Vector2.zero, size, font, 12, FontStyle.Bold);
        RectTransform textRect = text.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.pivot = new Vector2(0.5f, 0.5f);
        textRect.anchoredPosition = Vector2.zero;
        textRect.sizeDelta = Vector2.zero;
        text.alignment = TextAnchor.MiddleCenter;
        return (button, image, text);
    }

    private void SetButtonInteractable(Button button, Image image, bool interactable, Color enabledColor)
    {
        if (button == null)
        {
            return;
        }

        button.interactable = interactable;
        if (image != null)
        {
            image.color = interactable ? enabledColor : disabledColor;
        }
    }

    private static void UpdateSliderValueLabel(Text valueText, float value, string format = "0.00")
    {
        if (valueText != null)
        {
            valueText.text = value.ToString(string.IsNullOrWhiteSpace(format) ? "0.00" : format);
        }
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

    private void EnsureCheckerSprite()
    {
        if (_checkerSprite != null)
        {
            return;
        }

        const int size = 16;
        _checkerTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "StickerCandidateChecker",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Repeat,
            hideFlags = RuntimeHideFlags
        };

        Color32 light = new Color32(228, 228, 228, 255);
        Color32 dark = new Color32(172, 172, 172, 255);
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool useLight = ((x / 8) + (y / 8)) % 2 == 0;
                pixels[(y * size) + x] = useLight ? light : dark;
            }
        }

        _checkerTexture.SetPixels32(pixels);
        _checkerTexture.Apply(updateMipmaps: false, makeNoLongerReadable: false);

        _checkerSprite = Sprite.Create(
            _checkerTexture,
            new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f),
            size);
        _checkerSprite.name = "StickerCandidateCheckerSprite";
        _checkerSprite.hideFlags = RuntimeHideFlags;
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
}
