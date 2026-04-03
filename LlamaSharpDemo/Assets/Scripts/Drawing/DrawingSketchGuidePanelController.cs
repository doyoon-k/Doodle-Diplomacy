using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Lightweight runtime UGUI panel for Sketch Guide mode and Stable Diffusion generation.
/// </summary>
public class DrawingSketchGuidePanelController : MonoBehaviour
{
    private const HideFlags RuntimeHideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;

    [Header("References")]
    [SerializeField] private DrawingBoardController drawingBoard;
    [SerializeField] private DrawingSketchGuideGenerator sketchGuideGenerator;
    [SerializeField] private RectTransform controlPanel;
    [SerializeField] private RectTransform analysisPanel;

    [Header("Layout")]
    [SerializeField] private Vector2 panelAnchoredPosition = new(20f, -608f);
    [SerializeField] private Vector2 panelSize = new(430f, 314f);
    [SerializeField] private float panelVerticalSpacing = 12f;

    [Header("Theme")]
    [SerializeField] private Color panelColor = new(0.11f, 0.12f, 0.15f, 0.96f);
    [SerializeField] private Color fieldColor = new(0.18f, 0.20f, 0.25f, 1f);
    [SerializeField] private Color buttonOffColor = new(0.22f, 0.24f, 0.29f, 1f);
    [SerializeField] private Color guideOnColor = new(0.08f, 0.66f, 0.82f, 1f);
    [SerializeField] private Color generateColor = new(0.16f, 0.52f, 0.28f, 1f);
    [SerializeField] private Color cancelColor = new(0.72f, 0.30f, 0.22f, 1f);
    [SerializeField] private Color disabledColor = new(0.16f, 0.17f, 0.20f, 0.8f);

    private RectTransform _panelRoot;
    private Button _guideButton;
    private Image _guideButtonImage;
    private Text _guideButtonText;
    private Button _clearGuideButton;
    private Button _generateButton;
    private Button _cancelButton;
    private Dropdown _modelProfileDropdown;
    private InputField _promptInput;
    private Slider _controlStrengthSlider;
    private Text _controlStrengthValueText;
    private Text _statusText;
    private readonly List<string> _modelProfileOptions = new();

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
        }

        if (sketchGuideGenerator != null)
        {
            sketchGuideGenerator.GenerationStateChanged += OnGenerationStateChanged;
        }

        RefreshVisualState();
    }

    private void OnDisable()
    {
        if (drawingBoard != null)
        {
            drawingBoard.SketchGuideStateChanged -= OnSketchGuideStateChanged;
            drawingBoard.HistoryStateChanged -= OnHistoryStateChanged;
        }

        if (sketchGuideGenerator != null)
        {
            sketchGuideGenerator.GenerationStateChanged -= OnGenerationStateChanged;
        }
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
        if (controlPanel.parent != null)
        {
            rootObject.transform.SetSiblingIndex(controlPanel.GetSiblingIndex() + 1);
        }
        MarkRuntimeObject(rootObject);

        _panelRoot = rootObject.GetComponent<RectTransform>();
        _panelRoot.anchorMin = new Vector2(0f, 1f);
        _panelRoot.anchorMax = new Vector2(0f, 1f);
        _panelRoot.pivot = new Vector2(0f, 1f);
        ApplyPanelLayout();
        rootObject.GetComponent<Image>().color = panelColor;

        CreateLabel(_panelRoot, "SketchGuideTitle", "Sketch Guide", new Vector2(12f, -10f), new Vector2(296f, 24f), font, 16, FontStyle.Bold);
        (_guideButton, _guideButtonImage, _guideButtonText) = CreateButton(
            _panelRoot,
            "SketchGuideModeButton",
            "Sketch Guide",
            new Vector2(12f, -40f),
            new Vector2(142f, 30f),
            buttonOffColor,
            font);
        (_clearGuideButton, _, _) = CreateButton(
            _panelRoot,
            "ClearSketchGuideButton",
            "Clear Guide",
            new Vector2(166f, -40f),
            new Vector2(142f, 30f),
            buttonOffColor,
            font);

        CreateLabel(_panelRoot, "SketchModelLabel", "Model", new Vector2(12f, -80f), new Vector2(296f, 20f), font, 12, FontStyle.Bold);
        _modelProfileDropdown = CreateDropdown(
            _panelRoot,
            "SketchModelDropdown",
            "No SD Profile",
            new Vector2(12f, -102f),
            new Vector2(296f, 28f),
            font);

        CreateLabel(_panelRoot, "SketchPromptLabel", "Object", new Vector2(12f, -140f), new Vector2(296f, 20f), font, 12, FontStyle.Bold);
        _promptInput = CreateInputField(
            _panelRoot,
            "SketchPromptInput",
            "Describe the object to generate. Example: a red apple",
            new Vector2(12f, -162f),
            new Vector2(296f, 54f),
            font);

        CreateSliderRow(
            _panelRoot,
            "Control",
            new Vector2(12f, -236f),
            font,
            0f,
            2f,
            0.95f,
            out _controlStrengthSlider,
            out _controlStrengthValueText);

        (_generateButton, _, _) = CreateButton(
            _panelRoot,
            "GenerateSketchGuideButton",
            "Generate",
            new Vector2(12f, -264f),
            new Vector2(142f, 22f),
            generateColor,
            font);
        (_cancelButton, _, _) = CreateButton(
            _panelRoot,
            "CancelSketchGuideButton",
            "Cancel",
            new Vector2(166f, -264f),
            new Vector2(142f, 22f),
            cancelColor,
            font);
        _statusText = CreateLabel(
            _panelRoot,
            "SketchGuideStatusText",
            "Sketch guide generator is ready.",
            new Vector2(12f, -288f),
            new Vector2(296f, 34f),
            font,
            11,
            FontStyle.Normal);
        _statusText.alignment = TextAnchor.UpperLeft;
        _statusText.horizontalOverflow = HorizontalWrapMode.Wrap;
        _statusText.verticalOverflow = VerticalWrapMode.Truncate;
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

        if (_modelProfileDropdown != null)
        {
            _modelProfileDropdown.onValueChanged.RemoveListener(OnModelProfileChanged);
            _modelProfileDropdown.onValueChanged.AddListener(OnModelProfileChanged);
        }
    }

    private void SyncControlsFromGenerator()
    {
        if (sketchGuideGenerator == null)
        {
            return;
        }

        RefreshModelProfileDropdown();

        if (_promptInput != null)
        {
            _promptInput.SetTextWithoutNotify(sketchGuideGenerator.Prompt ?? string.Empty);
        }

        if (_controlStrengthSlider != null)
        {
            _controlStrengthSlider.SetValueWithoutNotify(sketchGuideGenerator.ControlStrength);
        }

        UpdateSliderValueLabel(_controlStrengthValueText, _controlStrengthSlider != null ? _controlStrengthSlider.value : sketchGuideGenerator.ControlStrength);
        if (_statusText != null)
        {
            _statusText.text = sketchGuideGenerator.StatusMessage;
        }
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
    }

    private void RefreshVisualState()
    {
        bool sketchGuideEnabled = drawingBoard != null && drawingBoard.IsSketchGuideEnabled;
        bool hasGuide = drawingBoard != null && drawingBoard.HasSketchGuide;
        bool isGenerating = sketchGuideGenerator != null && sketchGuideGenerator.IsGenerating;
        bool boardLocked = drawingBoard != null && drawingBoard.IsInteractionLocked;

        if (_guideButtonImage != null)
        {
            _guideButtonImage.color = sketchGuideEnabled ? guideOnColor : buttonOffColor;
        }

        if (_guideButtonText != null)
        {
            _guideButtonText.text = sketchGuideEnabled ? "Guide On" : "Sketch Guide";
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

        if (_statusText != null && sketchGuideGenerator != null)
        {
            _statusText.text = sketchGuideGenerator.StatusMessage;
        }

        if (_panelRoot != null)
        {
            ApplyPanelLayout();
        }
    }

    private Vector2 ResolvePanelAnchoredPosition()
    {
        if (controlPanel != null)
        {
            float x = controlPanel.anchoredPosition.x;
            float y = controlPanel.anchoredPosition.y - controlPanel.sizeDelta.y - Mathf.Max(0f, panelVerticalSpacing);
            return new Vector2(x, y);
        }

        if (analysisPanel != null)
        {
            float x = analysisPanel.anchoredPosition.x;
            float y = analysisPanel.anchoredPosition.y - analysisPanel.sizeDelta.y - Mathf.Max(0f, panelVerticalSpacing);
            return new Vector2(x, y);
        }

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
        out Text valueText)
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
        valueText = CreateLabel(parent, $"{label}ValueText", defaultValue.ToString("0.00"), new Vector2(254f, anchoredPosition.y), new Vector2(54f, 20f), font, 12, FontStyle.Bold);
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

    private static void UpdateSliderValueLabel(Text valueText, float value)
    {
        if (valueText != null)
        {
            valueText.text = value.ToString("0.00");
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
}
