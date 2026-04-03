#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

public sealed class StableDiffusionCppGeneratorWindow : EditorWindow
{
    private enum MaskInputSource
    {
        File = 0,
        Paint = 1
    }

    private const float MinWindowWidth = 420f;
    private const float MaxContentWidth = 760f;
    private const float HorizontalPaddingEstimate = 36f;
    private const int MaxSketchBrushSize = 128;

    private static readonly string[] SamplerOptions =
    {
        "euler_a",
        "euler",
        "heun",
        "dpm2",
        "dpm++2s_a",
        "dpm++2m",
        "dpm++2mv2",
        "ipndm",
        "ipndm_v",
        "lcm",
        "ddim",
        "tcd"
    };
    private static readonly string[] SchedulerOptions =
    {
        "discrete",
        "karras",
        "exponential",
        "ays",
        "gits",
        "smoothstep",
        "sgm_uniform",
        "simple",
        "kl_optimal",
        "lcm",
        "bong_tangent"
    };
    private static readonly string[] CacheModeOptions =
    {
        "easycache",
        "ucache",
        "dbcache",
        "taylorseer",
        "cache-dit"
    };

    private StableDiffusionCppSettings _settings;
    private StableDiffusionCppGenerationRequest _request;
    private Vector2 _scroll;
    private Vector2 _statusScroll;
    private Vector2 _executionLogScroll;
    private string _statusMessage = "Ready.";
    private MessageType _statusType = MessageType.Info;
    private string _lastOutputPath;
    private string _lastGeneratedImagePath;
    private string _lastSavedOutputPath;
    private string _lastRequestedOutputDirectory;
    private string _lastRequestedOutputFileName;
    private string _lastRequestedOutputFormat = "png";
    private string _executionLog = string.Empty;
    private bool _isGenerating;
    private Task<StableDiffusionCppGenerationResult> _generationTask;
    private CancellationTokenSource _generationCancellation;
    private Texture2D _previewTexture;
    private Texture2D _initPreviewTexture;
    private Texture2D _maskPreviewTexture;
    private string _initPreviewResolvedPath;
    private string _maskPreviewResolvedPath;
    private long _initPreviewLastWriteTicks;
    private long _maskPreviewLastWriteTicks;
    private Vector2Int _initPreviewSize;
    private Vector2Int _maskPreviewSize;
    private int _samplerIndex;
    private int _schedulerIndex;
    private bool _autoSaveToOutputFolder;
    private bool _snapResolutionToModelGrid = true;
    private bool _enableLivePreview = true;
    private MaskInputSource _maskInputSource = MaskInputSource.File;
    private Texture2D _paintMaskTexture;
    private int _paintMaskBrushSize = 24;
    private bool _paintMaskEraseMode;
    private bool _showPaintMaskOverlay = true;
    private Texture2D _sketchTexture;
    private int _sketchBrushSize = 12;
    private bool _sketchEraseMode;
    private int _livePreviewInterval = 1;
    private string _livePreviewPath;
    private DateTime _livePreviewLastWriteUtc = DateTime.MinValue;
    private int _livePreviewUpdateCount;
    private double _nextLivePreviewPollTime;
    private GUIStyle _wrappedSelectableLabelStyle;
    private GUIStyle _wrappedTextAreaStyle;

    [MenuItem("Tools/AI/Stable Diffusion CPP/Generator")]
    public static void ShowWindow()
    {
        var window = GetWindow<StableDiffusionCppGeneratorWindow>();
        window.titleContent = new GUIContent("SD CPP Generator");
        window.minSize = new Vector2(MinWindowWidth, 560f);
        window.Show();
    }

    [MenuItem("Tools/AI/Stable Diffusion CPP/Create Default Settings Asset")]
    public static void CreateDefaultSettingsAssetMenu()
    {
        CreateDefaultSettingsAsset(selectAsset: true);
    }

    private void OnEnable()
    {
        _request = new StableDiffusionCppGenerationRequest();
        _settings = FindFirstSettingsAsset();
        ApplyDefaultsFromSettings();
        EditorApplication.update += PollGenerationTask;
    }

    private void OnDisable()
    {
        EditorApplication.update -= PollGenerationTask;
        CancelGeneration();
        ReleasePreviewTexture();
        ReleaseConditioningPreviewTextures();
        ReleaseTexture(ref _paintMaskTexture);
        ReleaseTexture(ref _sketchTexture);
    }

    private void OnGUI()
    {
        float previousLabelWidth = EditorGUIUtility.labelWidth;
        bool previousWideMode = EditorGUIUtility.wideMode;
        EditorGUIUtility.labelWidth = GetAdaptiveLabelWidth();
        EditorGUIUtility.wideMode = ShouldUseWideMode();

        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.FlexibleSpace();
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(GetContentWidth())))
            {
                EditorGUILayout.LabelField("Stable Diffusion CPP Generator", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(
                    "Runs stable-diffusion.cpp locally and writes generated images for runtime use.\n" +
                    "Place runtime files under StreamingAssets and configure StableDiffusionCppSettings.",
                    MessageType.Info);

                DrawSettingsSection();
                EditorGUILayout.Space(8f);

                using (new EditorGUI.DisabledScope(_settings == null))
                {
                    DrawGenerationSection();
                    EditorGUILayout.Space(8f);
                    DrawControlSection();
                    EditorGUILayout.Space(8f);
                    DrawResultSection();
                }

                EditorGUILayout.Space(8f);
                EditorGUILayout.LabelField("Status", EditorStyles.boldLabel);
                DrawWrappedStatusMessage();
            }
            GUILayout.FlexibleSpace();
        }

        EditorGUILayout.EndScrollView();

        EditorGUIUtility.labelWidth = previousLabelWidth;
        EditorGUIUtility.wideMode = previousWideMode;
    }

    private void DrawSettingsSection()
    {
        EditorGUILayout.LabelField("Settings", EditorStyles.boldLabel);
        _settings = (StableDiffusionCppSettings)EditorGUILayout.ObjectField(
            "Config Asset",
            _settings,
            typeof(StableDiffusionCppSettings),
            false);

        DrawResponsiveGrid(
            190f,
            () =>
            {
                if (GUILayout.Button("Find Config", GUILayout.Height(24f), GUILayout.ExpandWidth(true)))
                {
                    _settings = FindFirstSettingsAsset();
                    if (_settings != null)
                    {
                        SetStatus($"Found settings: {AssetDatabase.GetAssetPath(_settings)}", MessageType.Info);
                        ApplyDefaultsFromSettings();
                    }
                    else
                    {
                        SetStatus("No StableDiffusionCppSettings asset found.", MessageType.Warning);
                    }
                }
            },
            () =>
            {
                if (GUILayout.Button("Create Default Config", GUILayout.Height(24f), GUILayout.ExpandWidth(true)))
                {
                    _settings = CreateDefaultSettingsAsset(selectAsset: true);
                    ApplyDefaultsFromSettings();
                }
            });

        if (_settings == null)
        {
            EditorGUILayout.HelpBox(
                "Create or assign a StableDiffusionCppSettings asset first.",
                MessageType.Warning);
        }
    }

    private void DrawGenerationSection()
    {
        EditorGUILayout.LabelField("Generation Parameters", EditorStyles.boldLabel);
        _request.mode = (StableDiffusionCppGenerationMode)EditorGUILayout.EnumPopup("Mode", _request.mode);
        DrawModeHelpBox();

        _request.prompt = EditorGUILayout.TextArea(_request.prompt, GUILayout.MinHeight(60f));
        _request.negativePrompt = EditorGUILayout.TextField("Negative Prompt", _request.negativePrompt);

        if (_request.mode == StableDiffusionCppGenerationMode.Sketch)
        {
            ReleaseConditioningPreviewTextures();
            ReleaseTexture(ref _paintMaskTexture);
            DrawSketchConditioningSection();
        }
        else if (_request.RequiresInitImage)
        {
            _request.useControlNet = false;
            EditorGUILayout.Space(4f);
            ReleaseTexture(ref _sketchTexture);
            DrawImageConditioningSection();
        }
        else
        {
            _request.useControlNet = false;
            ReleaseConditioningPreviewTextures();
            ReleaseTexture(ref _paintMaskTexture);
            ReleaseTexture(ref _sketchTexture);
        }

        bool dimensionsFollowInitImage = _request.RequiresInitImage && _request.useInitImageDimensions;
        using (new EditorGUI.DisabledScope(dimensionsFollowInitImage))
        {
            DrawResponsiveGrid(
                220f,
                () => _request.width = EditorGUILayout.IntField("Width", _request.width),
                () => _request.height = EditorGUILayout.IntField("Height", _request.height));
        }

        if (dimensionsFollowInitImage)
        {
            EditorGUILayout.HelpBox(
                "Width and Height will be taken from the init image when generation starts.",
                MessageType.Info);
        }

        DrawResolutionSafetySection();

        DrawResponsiveGrid(
            220f,
            () => _request.steps = EditorGUILayout.IntField("Steps", _request.steps),
            () => _request.cfgScale = EditorGUILayout.FloatField("CFG Scale", _request.cfgScale));

        DrawResponsiveGrid(
            240f,
            () => _request.seed = EditorGUILayout.IntField("Seed", _request.seed),
            () => _request.batchCount = EditorGUILayout.IntSlider("Batch Count", _request.batchCount, 1, 4));

        DrawResponsiveGrid(
            220f,
            () =>
            {
                _samplerIndex = Mathf.Clamp(_samplerIndex, 0, SamplerOptions.Length - 1);
                _samplerIndex = EditorGUILayout.Popup("Sampler", _samplerIndex, SamplerOptions);
                _request.sampler = SamplerOptions[_samplerIndex];
            },
            () =>
            {
                _schedulerIndex = Mathf.Clamp(_schedulerIndex, 0, SchedulerOptions.Length - 1);
                _schedulerIndex = EditorGUILayout.Popup("Scheduler", _schedulerIndex, SchedulerOptions);
                _request.scheduler = SchedulerOptions[_schedulerIndex];
            });

        EditorGUILayout.Space(4f);
        DrawActiveProfileSummary();

        DrawResponsiveGrid(
            260f,
            () => _request.outputFormat = EditorGUILayout.TextField("Format", _request.outputFormat),
            () => _request.outputFileName = EditorGUILayout.TextField("Output File Name", _request.outputFileName));

        string defaultOutput = _settings.ResolveEditorOutputDirectoryAbsolute();
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string defaultRelative = MakeProjectRelativePath(defaultOutput, projectRoot);
        _request.outputDirectory = EditorGUILayout.TextField("Output Folder", string.IsNullOrWhiteSpace(_request.outputDirectory) ? defaultRelative : _request.outputDirectory);
        _autoSaveToOutputFolder = EditorGUILayout.ToggleLeft(
            "Auto-save generated image to Output Folder",
            _autoSaveToOutputFolder);
        _request.extraArgumentsRaw = EditorGUILayout.TextField("Extra CLI Args", _request.extraArgumentsRaw);

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("Performance / VRAM", EditorStyles.boldLabel);
        _request.offloadToCpu = EditorGUILayout.ToggleLeft(
            "Offload Weights To CPU (--offload-to-cpu)",
            _request.offloadToCpu);
        _request.clipOnCpu = EditorGUILayout.ToggleLeft(
            "Keep CLIP On CPU (--clip-on-cpu)",
            _request.clipOnCpu);
        _request.vaeTiling = EditorGUILayout.ToggleLeft(
            "Enable VAE Tiling (--vae-tiling)",
            _request.vaeTiling);
        _request.diffusionFlashAttention = EditorGUILayout.ToggleLeft(
            "Use Diffusion Flash Attention (--diffusion-fa)",
            _request.diffusionFlashAttention);
        _request.useCacheMode = EditorGUILayout.ToggleLeft(
            "Enable Cache Mode (--cache-mode)",
            _request.useCacheMode);
        using (new EditorGUI.DisabledScope(!_request.useCacheMode))
        {
            int cacheModeIndex = FindOptionIndex(CacheModeOptions, _request.cacheMode);
            cacheModeIndex = EditorGUILayout.Popup("Cache Mode", cacheModeIndex, CacheModeOptions);
            _request.cacheMode = CacheModeOptions[cacheModeIndex];
            _request.cacheOption = EditorGUILayout.TextField("Cache Option", _request.cacheOption);
            _request.cachePreset = EditorGUILayout.TextField("Cache Preset", _request.cachePreset);
        }

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("Live Preview", EditorStyles.boldLabel);
        _enableLivePreview = EditorGUILayout.ToggleLeft(
            "Update preview image while generating",
            _enableLivePreview);
        using (new EditorGUI.DisabledScope(!_enableLivePreview))
        {
            _livePreviewInterval = EditorGUILayout.IntSlider("Preview Interval (steps)", _livePreviewInterval, 1, 10);
        }

        DrawResponsiveGrid(
            170f,
            () =>
            {
                if (GUILayout.Button("Apply Defaults", GUILayout.Height(24f), GUILayout.ExpandWidth(true)))
                {
                    ApplyDefaultsFromSettings();
                    SetStatus("Applied defaults from settings.", MessageType.Info);
                }
            },
            () =>
            {
                if (GUILayout.Button("Apply Profile Defaults", GUILayout.Height(24f), GUILayout.ExpandWidth(true)))
                {
                    ApplyModelProfileDefaults();
                }
            },
            () =>
            {
                if (GUILayout.Button("Prepare Runtime", GUILayout.Height(24f), GUILayout.ExpandWidth(true)))
                {
                    StableDiffusionCppPreparationResult prep = StableDiffusionCppRuntime.PrepareRuntime(
                        _settings,
                        forceReinstall: false);
                    if (prep.Success)
                    {
                        SetStatus(
                            $"Runtime prepared.\nExecutable: {prep.ExecutablePath}\nModel: {prep.ModelPath}",
                            MessageType.Info);
                    }
                    else
                    {
                        SetStatus(prep.ErrorMessage, MessageType.Error);
                    }
                }
            });
    }

    private void DrawModeHelpBox()
    {
        switch (_request.mode)
        {
            case StableDiffusionCppGenerationMode.Img2Img:
                EditorGUILayout.HelpBox(
                    "Img2Img uses an init image as a starting point and follows the prompt while preserving the overall composition.",
                    MessageType.Info);
                break;
            case StableDiffusionCppGenerationMode.Inpaint:
                EditorGUILayout.HelpBox(
                    "Inpaint uses an init image plus a same-size mask image. Only the masked region is regenerated.",
                    MessageType.Info);
                break;
            case StableDiffusionCppGenerationMode.Sketch:
                EditorGUILayout.HelpBox(
                    "Sketch uses a ControlNet scribble model and the black-on-white canvas below as --control-image. The prompt still defines the rendered content/style.",
                    MessageType.Info);
                break;
            default:
                EditorGUILayout.HelpBox(
                    "Txt2Img creates a new image from the prompt without requiring any source image.",
                    MessageType.None);
                break;
        }
    }

    private void DrawImageConditioningSection()
    {
        EditorGUILayout.LabelField("Image Conditioning", EditorStyles.boldLabel);
        DrawImagePathField("Init Image", ref _request.initImagePath, "Select Init Image");

        DrawResponsiveGrid(
            220f,
            () =>
            {
                _request.useInitImageDimensions = EditorGUILayout.ToggleLeft(
                    "Use Init Image Dimensions",
                    _request.useInitImageDimensions);
            },
            () =>
            {
                using (new EditorGUI.DisabledScope(!TryResolveEditorImagePath(_request.initImagePath, out _)))
                {
                    if (GUILayout.Button("Copy Init Image Size", GUILayout.Height(22f), GUILayout.ExpandWidth(true)))
                    {
                        TryApplyInitImageDimensionsToRequest(showStatusOnSuccess: true);
                    }
                }
            },
            () => _snapResolutionToModelGrid = EditorGUILayout.ToggleLeft(
                "Snap Output To 64-Multiple",
                _snapResolutionToModelGrid));

        _request.strength = EditorGUILayout.Slider("Strength", _request.strength, 0.01f, 1f);
        _request.overrideImageCfgScale = EditorGUILayout.ToggleLeft(
            "Override Image CFG Scale (--img-cfg-scale)",
            _request.overrideImageCfgScale);
        using (new EditorGUI.DisabledScope(!_request.overrideImageCfgScale))
        {
            _request.imageCfgScale = EditorGUILayout.FloatField("Image CFG Scale", _request.imageCfgScale);
        }

        if (_request.RequiresMaskImage)
        {
            _maskInputSource = (MaskInputSource)EditorGUILayout.EnumPopup("Mask Source", _maskInputSource);
            if (_maskInputSource == MaskInputSource.File)
            {
                DrawImagePathField("Mask Image", ref _request.maskImagePath, "Select Mask Image");
                EditorGUILayout.HelpBox(
                    "Mask images should match the init image dimensions exactly.",
                    MessageType.None);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "Paint the inpaint mask directly in this window. Painted regions will be exported as a white-on-black mask at generation time.",
                    MessageType.Info);
            }
        }
        else
        {
            _maskInputSource = MaskInputSource.File;
        }

        DrawSourcePreviewSection();

        if (_request.RequiresMaskImage && _maskInputSource == MaskInputSource.Paint)
        {
            DrawMaskPainterSection();
        }
    }

    private void DrawSketchConditioningSection()
    {
        EditorGUILayout.LabelField("Sketch ControlNet", EditorStyles.boldLabel);
        _request.useControlNet = true;

        DrawControlNetPathField();
        _request.controlStrength = EditorGUILayout.Slider("Control Strength", _request.controlStrength, 0f, 2f);
        _snapResolutionToModelGrid = EditorGUILayout.ToggleLeft(
            "Snap Output To 64-Multiple",
            _snapResolutionToModelGrid);

        EnsureSketchTextureMatchesRequestResolution();

        DrawResponsiveGrid(
            180f,
            () => _sketchBrushSize = EditorGUILayout.IntSlider("Brush Size", _sketchBrushSize, 1, MaxSketchBrushSize),
            () => _sketchEraseMode = EditorGUILayout.ToggleLeft("Erase Mode", _sketchEraseMode));

        DrawResponsiveGrid(
            160f,
            () =>
            {
                if (GUILayout.Button("Clear Sketch", GUILayout.Height(22f), GUILayout.ExpandWidth(true)))
                {
                    ClearSketchTexture();
                }
            },
            () =>
            {
                if (GUILayout.Button("Reset ControlNet Path", GUILayout.Height(22f), GUILayout.ExpandWidth(true)))
                {
                    _request.controlNetPathOverride = GetDefaultControlNetPathFromSettings();
                }
            });

        DrawSketchCanvasSection();
    }

    private void DrawControlNetPathField()
    {
        EditorGUILayout.LabelField("ControlNet Model Path");
        if (CanFitResponsiveColumns(1, 260f, 124f))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                _request.controlNetPathOverride = EditorGUILayout.TextField(GUIContent.none, _request.controlNetPathOverride);
                if (GUILayout.Button("Browse", GUILayout.Width(64f)))
                {
                    _request.controlNetPathOverride = BrowseForControlNetPath(_request.controlNetPathOverride);
                }

                if (GUILayout.Button("Clear", GUILayout.Width(52f)))
                {
                    _request.controlNetPathOverride = string.Empty;
                }
            }
        }
        else
        {
            _request.controlNetPathOverride = EditorGUILayout.TextField(GUIContent.none, _request.controlNetPathOverride);
            if (GUILayout.Button("Browse", GUILayout.Height(22f), GUILayout.ExpandWidth(true)))
            {
                _request.controlNetPathOverride = BrowseForControlNetPath(_request.controlNetPathOverride);
            }

            if (GUILayout.Button("Clear", GUILayout.Height(22f), GUILayout.ExpandWidth(true)))
            {
                _request.controlNetPathOverride = string.Empty;
            }
        }

        if (TryResolveEditorModelPath(_request.controlNetPathOverride, out string resolvedPath))
        {
            DrawWrappedSelectableValue("Resolved Path", resolvedPath);
        }
        else
        {
            EditorGUILayout.HelpBox(
                "Assign a SD1.5 scribble ControlNet .gguf/.safetensors/.ckpt path under StreamingAssets or an absolute path.",
                MessageType.Warning);
        }
    }

    private void DrawSketchCanvasSection()
    {
        if (_sketchTexture == null)
        {
            EditorGUILayout.HelpBox("Sketch canvas is unavailable.", MessageType.Warning);
            return;
        }

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("Sketch Canvas", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Canvas Size", $"{_sketchTexture.width}x{_sketchTexture.height}");

        float maxWidth = Mathf.Clamp(GetContentWidth(), 180f, 520f);
        float aspect = _sketchTexture.height > 0 ? _sketchTexture.width / (float)_sketchTexture.height : 1f;
        float previewWidth = Mathf.Clamp(maxWidth, 180f, 520f);
        float previewHeight = Mathf.Clamp(previewWidth / Mathf.Max(0.01f, aspect), 180f, 520f);
        Rect canvasRect = GUILayoutUtility.GetRect(previewWidth, previewHeight, GUILayout.ExpandWidth(false));
        DrawSketchCanvas(canvasRect);

        EditorGUILayout.HelpBox(
            "Left-drag draws black scribbles on a white background. Toggle Erase Mode or hold Alt to erase back to white.",
            MessageType.None);
    }

    private void DrawSketchCanvas(Rect canvasRect)
    {
        if (_sketchTexture == null)
        {
            return;
        }

        EditorGUI.DrawPreviewTexture(canvasRect, _sketchTexture, null, ScaleMode.ScaleToFit);
        EditorGUI.DrawRect(new Rect(canvasRect.xMin, canvasRect.yMin, canvasRect.width, 1f), new Color(0f, 0f, 0f, 0.4f));
        EditorGUI.DrawRect(new Rect(canvasRect.xMin, canvasRect.yMax - 1f, canvasRect.width, 1f), new Color(0f, 0f, 0f, 0.4f));
        EditorGUI.DrawRect(new Rect(canvasRect.xMin, canvasRect.yMin, 1f, canvasRect.height), new Color(0f, 0f, 0f, 0.4f));
        EditorGUI.DrawRect(new Rect(canvasRect.xMax - 1f, canvasRect.yMin, 1f, canvasRect.height), new Color(0f, 0f, 0f, 0.4f));

        HandleSketchCanvasInput(canvasRect);
    }

    private void HandleSketchCanvasInput(Rect canvasRect)
    {
        Event current = Event.current;
        if (current == null)
        {
            return;
        }

        bool isPaintEvent =
            current.button == 0 &&
            (current.type == EventType.MouseDown || current.type == EventType.MouseDrag);
        if (!isPaintEvent || !canvasRect.Contains(current.mousePosition))
        {
            return;
        }

        if (!TryMapCanvasPositionToTexturePixel(
                current.mousePosition,
                canvasRect,
                _sketchTexture.width,
                _sketchTexture.height,
                out Vector2Int pixel))
        {
            return;
        }

        bool erase = _sketchEraseMode || current.alt;
        PaintSketchDisc(pixel, _sketchBrushSize, erase);
        current.Use();
        Repaint();
    }

    private void EnsureSketchTextureMatchesRequestResolution()
    {
        Vector2Int targetSize = GetDisplayedOutputResolution();
        if (_snapResolutionToModelGrid)
        {
            targetSize = SnapResolutionToModelGrid(targetSize);
        }

        int targetWidth = Mathf.Max(64, targetSize.x);
        int targetHeight = Mathf.Max(64, targetSize.y);
        if (_sketchTexture != null &&
            _sketchTexture.width == targetWidth &&
            _sketchTexture.height == targetHeight)
        {
            return;
        }

        ReleaseTexture(ref _sketchTexture);
        _sketchTexture = CreateSketchTexture(targetWidth, targetHeight);
        ClearSketchTexture();
    }

    private static Texture2D CreateSketchTexture(int width, int height)
    {
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            name = "StableDiffusionSketchCanvas",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        return texture;
    }

    private void ClearSketchTexture()
    {
        if (_sketchTexture == null)
        {
            return;
        }

        Color32[] pixels = new Color32[_sketchTexture.width * _sketchTexture.height];
        var white = new Color32(255, 255, 255, 255);
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = white;
        }

        _sketchTexture.SetPixels32(pixels);
        _sketchTexture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
    }

    private void PaintSketchDisc(Vector2Int center, int radius, bool erase)
    {
        if (_sketchTexture == null)
        {
            return;
        }

        int brushRadius = Mathf.Max(1, radius);
        int sqrRadius = brushRadius * brushRadius;
        int startX = Mathf.Max(0, center.x - brushRadius);
        int endX = Mathf.Min(_sketchTexture.width - 1, center.x + brushRadius);
        int startY = Mathf.Max(0, center.y - brushRadius);
        int endY = Mathf.Min(_sketchTexture.height - 1, center.y + brushRadius);
        Color32 drawColor = erase
            ? new Color32(255, 255, 255, 255)
            : new Color32(0, 0, 0, 255);

        for (int y = startY; y <= endY; y++)
        {
            int dy = y - center.y;
            for (int x = startX; x <= endX; x++)
            {
                int dx = x - center.x;
                if ((dx * dx) + (dy * dy) > sqrRadius)
                {
                    continue;
                }

                _sketchTexture.SetPixel(x, y, drawColor);
            }
        }

        _sketchTexture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
    }

    private void DrawSourcePreviewSection()
    {
        UpdateConditioningPreviewTextures();

        bool hasInitPreview = _initPreviewTexture != null;
        bool hasMaskPreview = _request.RequiresMaskImage && _maskPreviewTexture != null;
        if (!hasInitPreview && !hasMaskPreview)
        {
            return;
        }

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("Source Preview", EditorStyles.boldLabel);

        float previewWidth = Mathf.Clamp((GetContentWidth() - 12f) * 0.5f, 120f, 220f);
        if (hasInitPreview && hasMaskPreview && CanFitResponsiveColumns(2, previewWidth))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawPreviewCard("Init Image", _initPreviewTexture, _initPreviewSize, _initPreviewResolvedPath, previewWidth);
                DrawPreviewCard("Mask Image", _maskPreviewTexture, _maskPreviewSize, _maskPreviewResolvedPath, previewWidth);
            }

            return;
        }

        if (hasInitPreview)
        {
            DrawPreviewCard("Init Image", _initPreviewTexture, _initPreviewSize, _initPreviewResolvedPath, previewWidth);
        }

        if (hasMaskPreview)
        {
            DrawPreviewCard("Mask Image", _maskPreviewTexture, _maskPreviewSize, _maskPreviewResolvedPath, previewWidth);
        }
    }

    private void DrawMaskPainterSection()
    {
        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("Paint Mask", EditorStyles.boldLabel);

        if (_initPreviewTexture == null || _initPreviewSize.x <= 0 || _initPreviewSize.y <= 0)
        {
            EditorGUILayout.HelpBox(
                "Select a valid init image before painting a mask.",
                MessageType.Warning);
            ReleaseTexture(ref _paintMaskTexture);
            return;
        }

        EnsurePaintMaskTextureMatchesInitPreview();

        DrawResponsiveGrid(
            180f,
            () => _paintMaskBrushSize = EditorGUILayout.IntSlider("Brush Size", _paintMaskBrushSize, 1, 128),
            () => _paintMaskEraseMode = EditorGUILayout.ToggleLeft("Erase Mode", _paintMaskEraseMode),
            () => _showPaintMaskOverlay = EditorGUILayout.ToggleLeft("Show Overlay", _showPaintMaskOverlay));

        DrawResponsiveGrid(
            160f,
            () =>
            {
                if (GUILayout.Button("Clear Mask", GUILayout.Height(22f), GUILayout.ExpandWidth(true)))
                {
                    ClearPaintMask(fillMasked: false);
                }
            },
            () =>
            {
                if (GUILayout.Button("Fill Mask", GUILayout.Height(22f), GUILayout.ExpandWidth(true)))
                {
                    ClearPaintMask(fillMasked: true);
                }
            });

        float maxWidth = Mathf.Clamp(GetContentWidth(), 180f, 520f);
        float aspect = _initPreviewSize.y > 0 ? _initPreviewSize.x / (float)_initPreviewSize.y : 1f;
        float previewWidth = Mathf.Clamp(maxWidth, 180f, 520f);
        float previewHeight = Mathf.Clamp(previewWidth / Mathf.Max(0.01f, aspect), 180f, 520f);
        Rect canvasRect = GUILayoutUtility.GetRect(previewWidth, previewHeight, GUILayout.ExpandWidth(false));
        DrawMaskPainterCanvas(canvasRect);

        EditorGUILayout.HelpBox(
            "Left-drag paints masked regions. Toggle Erase Mode to remove mask. The mask is exported as white-on-black when generating.",
            MessageType.None);
    }

    private void DrawMaskPainterCanvas(Rect canvasRect)
    {
        if (_initPreviewTexture == null || _paintMaskTexture == null)
        {
            return;
        }

        EditorGUI.DrawPreviewTexture(canvasRect, _initPreviewTexture, null, ScaleMode.ScaleToFit);

        if (_showPaintMaskOverlay)
        {
            Color previousColor = GUI.color;
            GUI.color = new Color(1f, 0.15f, 0.15f, 0.45f);
            GUI.DrawTexture(canvasRect, _paintMaskTexture, ScaleMode.ScaleToFit, true);
            GUI.color = previousColor;
        }

        EditorGUI.DrawRect(
            new Rect(canvasRect.xMin, canvasRect.yMin, canvasRect.width, 1f),
            new Color(1f, 1f, 1f, 0.25f));
        EditorGUI.DrawRect(
            new Rect(canvasRect.xMin, canvasRect.yMax - 1f, canvasRect.width, 1f),
            new Color(1f, 1f, 1f, 0.25f));
        EditorGUI.DrawRect(
            new Rect(canvasRect.xMin, canvasRect.yMin, 1f, canvasRect.height),
            new Color(1f, 1f, 1f, 0.25f));
        EditorGUI.DrawRect(
            new Rect(canvasRect.xMax - 1f, canvasRect.yMin, 1f, canvasRect.height),
            new Color(1f, 1f, 1f, 0.25f));

        HandleMaskPainterInput(canvasRect);
    }

    private void HandleMaskPainterInput(Rect canvasRect)
    {
        Event current = Event.current;
        if (current == null)
        {
            return;
        }

        bool isPaintEvent =
            current.button == 0 &&
            (current.type == EventType.MouseDown || current.type == EventType.MouseDrag);
        if (!isPaintEvent || !canvasRect.Contains(current.mousePosition))
        {
            return;
        }

        if (!TryMapCanvasPositionToMaskPixel(current.mousePosition, canvasRect, out Vector2Int pixel))
        {
            return;
        }

        bool erase = _paintMaskEraseMode || current.alt;
        PaintMaskDisc(pixel, _paintMaskBrushSize, erase);
        current.Use();
        Repaint();
    }

    private bool TryMapCanvasPositionToMaskPixel(Vector2 mousePosition, Rect canvasRect, out Vector2Int pixel)
    {
        if (_paintMaskTexture == null || _paintMaskTexture.width <= 0 || _paintMaskTexture.height <= 0)
        {
            pixel = default;
            return false;
        }

        return TryMapCanvasPositionToTexturePixel(
            mousePosition,
            canvasRect,
            _paintMaskTexture.width,
            _paintMaskTexture.height,
            out pixel);
    }

    private static bool TryMapCanvasPositionToTexturePixel(
        Vector2 mousePosition,
        Rect canvasRect,
        int textureWidth,
        int textureHeight,
        out Vector2Int pixel)
    {
        pixel = default;
        if (textureWidth <= 0 || textureHeight <= 0)
        {
            return false;
        }

        float normalizedX = Mathf.InverseLerp(canvasRect.xMin, canvasRect.xMax, mousePosition.x);
        float normalizedY = Mathf.InverseLerp(canvasRect.yMin, canvasRect.yMax, mousePosition.y);
        int x = Mathf.Clamp(Mathf.RoundToInt(normalizedX * (textureWidth - 1)), 0, textureWidth - 1);
        int y = Mathf.Clamp(
            Mathf.RoundToInt((1f - normalizedY) * (textureHeight - 1)),
            0,
            textureHeight - 1);
        pixel = new Vector2Int(x, y);
        return true;
    }

    private void EnsurePaintMaskTextureMatchesInitPreview()
    {
        if (_initPreviewSize.x <= 0 || _initPreviewSize.y <= 0)
        {
            return;
        }

        if (_paintMaskTexture != null &&
            _paintMaskTexture.width == _initPreviewSize.x &&
            _paintMaskTexture.height == _initPreviewSize.y)
        {
            return;
        }

        ReleaseTexture(ref _paintMaskTexture);
        _paintMaskTexture = CreateMaskTexture(_initPreviewSize.x, _initPreviewSize.y);
        ClearPaintMask(fillMasked: false);
    }

    private static Texture2D CreateMaskTexture(int width, int height)
    {
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            name = "StableDiffusionMaskPainter",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        return texture;
    }

    private void ClearPaintMask(bool fillMasked)
    {
        if (_paintMaskTexture == null)
        {
            return;
        }

        Color32 fillColor = fillMasked
            ? new Color32(255, 255, 255, 255)
            : new Color32(255, 255, 255, 0);
        Color32[] colors = new Color32[_paintMaskTexture.width * _paintMaskTexture.height];
        for (int i = 0; i < colors.Length; i++)
        {
            colors[i] = fillColor;
        }

        _paintMaskTexture.SetPixels32(colors);
        _paintMaskTexture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
    }

    private void PaintMaskDisc(Vector2Int center, int radius, bool erase)
    {
        if (_paintMaskTexture == null)
        {
            return;
        }

        int brushRadius = Mathf.Max(1, radius);
        int sqrRadius = brushRadius * brushRadius;
        int startX = Mathf.Max(0, center.x - brushRadius);
        int endX = Mathf.Min(_paintMaskTexture.width - 1, center.x + brushRadius);
        int startY = Mathf.Max(0, center.y - brushRadius);
        int endY = Mathf.Min(_paintMaskTexture.height - 1, center.y + brushRadius);
        Color32 brushColor = erase
            ? new Color32(255, 255, 255, 0)
            : new Color32(255, 255, 255, 255);

        for (int y = startY; y <= endY; y++)
        {
            int dy = y - center.y;
            for (int x = startX; x <= endX; x++)
            {
                int dx = x - center.x;
                if ((dx * dx) + (dy * dy) > sqrRadius)
                {
                    continue;
                }

                _paintMaskTexture.SetPixel(x, y, brushColor);
            }
        }

        _paintMaskTexture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
    }

    private void DrawResolutionSafetySection()
    {
        Vector2Int effectiveResolution = GetDisplayedOutputResolution();
        EditorGUILayout.LabelField("Effective Output Resolution", $"{effectiveResolution.x}x{effectiveResolution.y}");

        Vector2Int snappedResolution = SnapResolutionToModelGrid(effectiveResolution);
        bool alreadyAligned = effectiveResolution == snappedResolution;
        if (alreadyAligned)
        {
            return;
        }

        string guidance = _snapResolutionToModelGrid
            ? $"The current resolution is not aligned to Stable Diffusion's 64-pixel grid. It will be snapped to {snappedResolution.x}x{snappedResolution.y} on Generate."
            : $"The current resolution is not aligned to Stable Diffusion's 64-pixel grid. This can cause native crashes. Recommended: {snappedResolution.x}x{snappedResolution.y}.";
        MessageType type = _snapResolutionToModelGrid ? MessageType.Info : MessageType.Warning;
        EditorGUILayout.HelpBox(guidance, type);

        if (GUILayout.Button($"Apply Recommended {snappedResolution.x}x{snappedResolution.y}", GUILayout.Height(22f), GUILayout.ExpandWidth(true)))
        {
            _request.width = snappedResolution.x;
            _request.height = snappedResolution.y;
            _request.useInitImageDimensions = false;
            SetStatus($"Applied safe resolution {snappedResolution.x}x{snappedResolution.y}.", MessageType.Info);
        }
    }

    private void DrawPreviewCard(
        string label,
        Texture2D texture,
        Vector2Int size,
        string resolvedPath,
        float previewWidth)
    {
        if (texture == null)
        {
            return;
        }

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Size", $"{size.x}x{size.y}");

            Rect previewRect = GUILayoutUtility.GetRect(previewWidth, previewWidth, GUILayout.ExpandWidth(false));
            EditorGUI.DrawPreviewTexture(previewRect, texture, null, ScaleMode.ScaleToFit);

            DrawWrappedSelectableValue("Path", resolvedPath);
        }
    }

    private void DrawImagePathField(string label, ref string path, string dialogTitle)
    {
        Texture2D currentAsset = LoadTextureAssetFromPath(path);
        Texture2D newAsset = (Texture2D)EditorGUILayout.ObjectField(
            $"{label} Asset",
            currentAsset,
            typeof(Texture2D),
            false);
        if (newAsset != currentAsset)
        {
            path = newAsset != null ? AssetDatabase.GetAssetPath(newAsset) : string.Empty;
        }

        EditorGUILayout.LabelField($"{label} Path");
        if (CanFitResponsiveColumns(1, 260f, 124f))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                path = EditorGUILayout.TextField(GUIContent.none, path);
                if (GUILayout.Button("Browse", GUILayout.Width(64f)))
                {
                    path = BrowseForImagePath(path, dialogTitle);
                }

                if (GUILayout.Button("Clear", GUILayout.Width(52f)))
                {
                    path = ClearImagePath();
                }
            }
        }
        else
        {
            path = EditorGUILayout.TextField(GUIContent.none, path);
            if (GUILayout.Button("Browse", GUILayout.Height(22f), GUILayout.ExpandWidth(true)))
            {
                path = BrowseForImagePath(path, dialogTitle);
            }

            if (GUILayout.Button("Clear", GUILayout.Height(22f), GUILayout.ExpandWidth(true)))
            {
                path = ClearImagePath();
            }
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (TryResolveEditorImagePath(path, out string resolvedPath))
        {
            DrawWrappedSelectableValue("Resolved Path", resolvedPath);
            return;
        }

        EditorGUILayout.HelpBox(
            "The selected image path does not resolve to an existing file yet.",
            MessageType.Warning);
    }

    private void DrawControlSection()
    {
        EditorGUILayout.LabelField("Actions", EditorStyles.boldLabel);
        string revealPath = GetPreferredRevealPath();
        DrawResponsiveGrid(
            150f,
            () =>
            {
                using (new EditorGUI.DisabledScope(_isGenerating))
                {
                    if (GUILayout.Button("Generate", GUILayout.Height(28f), GUILayout.ExpandWidth(true)))
                    {
                        StartGeneration();
                    }
                }
            },
            () =>
            {
                using (new EditorGUI.DisabledScope(!_isGenerating))
                {
                    if (GUILayout.Button("Cancel", GUILayout.Height(28f), GUILayout.ExpandWidth(true)))
                    {
                        CancelGeneration();
                        SetStatus("Generation cancellation requested.", MessageType.Warning);
                    }
                }
            },
            () =>
            {
                using (new EditorGUI.DisabledScope(_isGenerating || string.IsNullOrWhiteSpace(_lastGeneratedImagePath) || !File.Exists(_lastGeneratedImagePath)))
                {
                    if (GUILayout.Button("Save To Output Folder", GUILayout.Height(28f), GUILayout.ExpandWidth(true)))
                    {
                        SaveLatestGeneratedImage();
                    }
                }
            },
            () =>
            {
                using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(revealPath) || !File.Exists(revealPath)))
                {
                    if (GUILayout.Button("Reveal Output", GUILayout.Height(28f), GUILayout.ExpandWidth(true)))
                    {
                        EditorUtility.RevealInFinder(revealPath);
                    }
                }
            });
    }

    private void DrawResultSection()
    {
        EditorGUILayout.LabelField("Result", EditorStyles.boldLabel);
        if (_previewTexture != null)
        {
            float previewSize = Mathf.Clamp(GetContentWidth(), 96f, 256f);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                Rect previewRect = GUILayoutUtility.GetRect(previewSize, previewSize, GUILayout.ExpandWidth(false));
                EditorGUI.DrawPreviewTexture(previewRect, _previewTexture, null, ScaleMode.ScaleToFit);
                GUILayout.FlexibleSpace();
            }
        }
        else
        {
            EditorGUILayout.HelpBox("No preview image yet.", MessageType.None);
        }

        if (!string.IsNullOrWhiteSpace(_lastGeneratedImagePath))
        {
            DrawWrappedSelectableValue("Generated Image Path", _lastGeneratedImagePath);
        }

        if (!string.IsNullOrWhiteSpace(_lastSavedOutputPath))
        {
            DrawWrappedSelectableValue("Saved Output Path", _lastSavedOutputPath);
        }

        EditorGUILayout.LabelField("Execution Log", EditorStyles.boldLabel);
        DrawWrappedScrollableText(ref _executionLogScroll, _executionLog, 180f);
    }

    private float GetContentWidth()
    {
        float availableWidth = Mathf.Max(120f, position.width - HorizontalPaddingEstimate);
        return Mathf.Min(availableWidth, MaxContentWidth);
    }

    private float GetAdaptiveLabelWidth()
    {
        if (!ShouldUseWideMode())
        {
            return 0f;
        }

        return Mathf.Clamp(GetContentWidth() * 0.3f, 100f, 150f);
    }

    private bool ShouldUseWideMode()
    {
        return GetContentWidth() >= 480f;
    }

    private bool CanFitResponsiveColumns(int itemCount, float minItemWidth, float reservedWidth = 0f)
    {
        return Mathf.Max(0f, GetContentWidth() - reservedWidth) >= itemCount * Mathf.Max(1f, minItemWidth);
    }

    private void DrawResponsiveGrid(float minItemWidth, params Action[] drawItems)
    {
        if (drawItems == null || drawItems.Length == 0)
        {
            return;
        }

        int columnCount = Mathf.Clamp(
            Mathf.FloorToInt(GetContentWidth() / Mathf.Max(1f, minItemWidth)),
            1,
            drawItems.Length);

        for (int itemIndex = 0; itemIndex < drawItems.Length; itemIndex += columnCount)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                int rowEnd = Mathf.Min(itemIndex + columnCount, drawItems.Length);
                for (int rowIndex = itemIndex; rowIndex < rowEnd; rowIndex++)
                {
                    using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true)))
                    {
                        drawItems[rowIndex]?.Invoke();
                    }
                }
            }
        }
    }

    private void DrawWrappedSelectableValue(string label, string value)
    {
        EditorGUILayout.LabelField(label);
        EditorGUILayout.SelectableLabel(
            string.IsNullOrWhiteSpace(value) ? string.Empty : value,
            GetWrappedSelectableLabelStyle(),
            GUILayout.Height(GetWrappedSelectableLabelHeight(value)));
    }

    private void DrawWrappedStatusMessage()
    {
        string statusPrefix = _statusType switch
        {
            MessageType.Warning => "Warning: ",
            MessageType.Error => "Error: ",
            _ => string.Empty
        };

        DrawWrappedScrollableText(ref _statusScroll, statusPrefix + (_statusMessage ?? string.Empty), 64f);
    }

    private void DrawWrappedScrollableText(ref Vector2 scrollPosition, string text, float height)
    {
        scrollPosition = EditorGUILayout.BeginScrollView(
            scrollPosition,
            alwaysShowHorizontal: false,
            alwaysShowVertical: true,
            GUILayout.Height(height),
            GUILayout.ExpandWidth(true));
        GUILayout.Label(
            string.IsNullOrWhiteSpace(text) ? "(empty)" : text,
            GetWrappedTextAreaStyle(),
            GUILayout.ExpandWidth(true));
        EditorGUILayout.EndScrollView();
    }

    private string BrowseForImagePath(string currentPath, string dialogTitle)
    {
        string selectedPath = EditorUtility.OpenFilePanelWithFilters(
            dialogTitle,
            GetInitialImageBrowseDirectory(currentPath),
            new[] { "Image Files", "png,jpg,jpeg", "All Files", "*" });
        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            GUI.FocusControl(null);
            return selectedPath;
        }

        return currentPath;
    }

    private string BrowseForControlNetPath(string currentPath)
    {
        string selectedPath = EditorUtility.OpenFilePanelWithFilters(
            "Select ControlNet Model",
            GetInitialImageBrowseDirectory(currentPath),
            new[] { "Stable Diffusion Model Files", "gguf,safetensors,ckpt,pth", "All Files", "*" });
        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            GUI.FocusControl(null);
            return selectedPath;
        }

        return currentPath;
    }

    private static string ClearImagePath()
    {
        GUI.FocusControl(null);
        return string.Empty;
    }

    private float GetWrappedSelectableLabelHeight(string value)
    {
        GUIStyle style = GetWrappedSelectableLabelStyle();
        float width = Mathf.Max(120f, GetContentWidth() - 8f);
        float height = style.CalcHeight(new GUIContent(value ?? string.Empty), width);
        return Mathf.Max(EditorGUIUtility.singleLineHeight * 2f, height + 6f);
    }

    private GUIStyle GetWrappedSelectableLabelStyle()
    {
        if (_wrappedSelectableLabelStyle == null)
        {
            _wrappedSelectableLabelStyle = new GUIStyle(EditorStyles.textArea)
            {
                wordWrap = true
            };
        }

        return _wrappedSelectableLabelStyle;
    }

    private GUIStyle GetWrappedTextAreaStyle()
    {
        if (_wrappedTextAreaStyle == null)
        {
            _wrappedTextAreaStyle = new GUIStyle(EditorStyles.textArea)
            {
                wordWrap = true,
                stretchWidth = true,
                clipping = TextClipping.Clip
            };
        }

        return _wrappedTextAreaStyle;
    }

    private void UpdateConditioningPreviewTextures()
    {
        UpdatePreviewTextureForPath(
            _request != null ? _request.initImagePath : null,
            ref _initPreviewTexture,
            ref _initPreviewResolvedPath,
            ref _initPreviewLastWriteTicks,
            ref _initPreviewSize);

        UpdatePreviewTextureForPath(
            _request != null && _request.RequiresMaskImage && _maskInputSource == MaskInputSource.File ? _request.maskImagePath : null,
            ref _maskPreviewTexture,
            ref _maskPreviewResolvedPath,
            ref _maskPreviewLastWriteTicks,
            ref _maskPreviewSize);
    }

    private void UpdatePreviewTextureForPath(
        string rawPath,
        ref Texture2D previewTexture,
        ref string resolvedPathCache,
        ref long lastWriteTicksCache,
        ref Vector2Int previewSize)
    {
        if (!TryResolveEditorImagePath(rawPath, out string resolvedPath))
        {
            ReleaseTexture(ref previewTexture);
            resolvedPathCache = string.Empty;
            lastWriteTicksCache = 0L;
            previewSize = default;
            return;
        }

        long writeTicks = File.GetLastWriteTimeUtc(resolvedPath).Ticks;
        if (previewTexture != null &&
            string.Equals(resolvedPathCache, resolvedPath, StringComparison.OrdinalIgnoreCase) &&
            lastWriteTicksCache == writeTicks)
        {
            return;
        }

        if (!StableDiffusionCppImageIO.TryLoadTextureFromFile(resolvedPath, out Texture2D loadedTexture, out string error))
        {
            Debug.LogWarning($"[StableDiffusionCppGeneratorWindow] Failed to load source preview '{resolvedPath}': {error}");
            ReleaseTexture(ref previewTexture);
            resolvedPathCache = resolvedPath;
            lastWriteTicksCache = writeTicks;
            previewSize = default;
            return;
        }

        ReleaseTexture(ref previewTexture);
        previewTexture = loadedTexture;
        resolvedPathCache = resolvedPath;
        lastWriteTicksCache = writeTicks;
        previewSize = new Vector2Int(loadedTexture.width, loadedTexture.height);
    }

    private Vector2Int GetDisplayedOutputResolution()
    {
        if (_request != null &&
            _request.RequiresInitImage &&
            _request.useInitImageDimensions &&
            _initPreviewSize.x > 0 &&
            _initPreviewSize.y > 0)
        {
            return _initPreviewSize;
        }

        return new Vector2Int(
            Mathf.Max(64, _request != null ? _request.width : 512),
            Mathf.Max(64, _request != null ? _request.height : 512));
    }

    private static Vector2Int SnapResolutionToModelGrid(Vector2Int resolution)
    {
        return new Vector2Int(
            SnapDimensionToModelGrid(resolution.x),
            SnapDimensionToModelGrid(resolution.y));
    }

    private static int SnapDimensionToModelGrid(int value)
    {
        int normalized = Mathf.Max(64, value);
        return Mathf.Max(64, Mathf.RoundToInt(normalized / 64f) * 64);
    }

    private void StartGeneration()
    {
        if (_settings == null)
        {
            SetStatus("Assign StableDiffusionCppSettings before generating.", MessageType.Warning);
            return;
        }

        if (!TryPrepareEditorRequestForGeneration(
                out StableDiffusionCppGenerationRequest requestCopy,
                out string error,
                out string preparationNote))
        {
            SetStatus(error, MessageType.Warning);
            return;
        }

        _generationCancellation?.Dispose();
        _generationCancellation = new CancellationTokenSource();

        _isGenerating = true;
        _executionLog = "Running stable-diffusion.cpp...";
        _lastSavedOutputPath = null;
        _lastRequestedOutputDirectory = ResolveOutputDirectoryAbsolute(requestCopy.outputDirectory);
        _lastRequestedOutputFileName = requestCopy.outputFileName;
        _lastRequestedOutputFormat = requestCopy.outputFormat;
        string startMessage = string.IsNullOrWhiteSpace(preparationNote)
            ? $"{GetModeLabel(requestCopy.mode)} generation started."
            : $"{GetModeLabel(requestCopy.mode)} generation started. {preparationNote}";
        SetStatus(startMessage, MessageType.Info);

        PrepareLivePreviewArgs(requestCopy);

        _generationTask = StableDiffusionCppRuntime.GenerateAsync(
            _settings,
            requestCopy,
            _generationCancellation.Token);
    }

    private void PollGenerationTask()
    {
        if (_isGenerating)
        {
            UpdateLivePreviewDuringGeneration();
        }

        if (_generationTask == null || !_generationTask.IsCompleted)
        {
            return;
        }

        _isGenerating = false;

        if (_generationTask.IsFaulted)
        {
            string exception = _generationTask.Exception != null
                ? _generationTask.Exception.ToString()
                : "Unknown exception.";
            _executionLog = exception;
            SetStatus("Generation task failed with exception. See log.", MessageType.Error);
        }
        else
        {
            StableDiffusionCppGenerationResult result = _generationTask.Result;
            _executionLog = BuildExecutionLog(result);
            LogResultToUnityConsole(result);

            if (result.Success)
            {
                if (result.OutputFiles.Count > 0)
                {
                    _lastGeneratedImagePath = result.OutputFiles[0];
                    _lastOutputPath = _lastGeneratedImagePath;
                    if (_autoSaveToOutputFolder)
                    {
                        _lastSavedOutputPath = _lastGeneratedImagePath;
                    }

                    TryLoadPreviewTexture(_lastGeneratedImagePath);
                    TryRefreshAssetDatabaseForPath(_lastGeneratedImagePath);
                }

                if (_autoSaveToOutputFolder)
                {
                    SetStatus(
                        $"{GetModeLabel(_request.mode)} generation complete and saved in {result.Elapsed.TotalSeconds:F1}s.",
                        MessageType.Info);
                }
                else
                {
                    SetStatus(
                        $"{GetModeLabel(_request.mode)} generation complete in {result.Elapsed.TotalSeconds:F1}s. Use 'Save To Output Folder' to persist.",
                        MessageType.Info);
                }
            }
            else
            {
                MessageType type = result.Cancelled ? MessageType.Warning : MessageType.Error;
                SetStatus(result.ErrorMessage, type);
            }
        }

        _generationTask = null;
        _generationCancellation?.Dispose();
        _generationCancellation = null;
        ResetLivePreviewState();
        Repaint();
    }

    private void CancelGeneration()
    {
        _generationCancellation?.Cancel();
        StableDiffusionCppRuntime.CancelActiveGeneration();
    }

    private void SaveLatestGeneratedImage()
    {
        if (string.IsNullOrWhiteSpace(_lastGeneratedImagePath) || !File.Exists(_lastGeneratedImagePath))
        {
            SetStatus("No generated image available to save.", MessageType.Warning);
            return;
        }

        string destinationDirectory = !string.IsNullOrWhiteSpace(_lastRequestedOutputDirectory)
            ? _lastRequestedOutputDirectory
            : ResolveOutputDirectoryAbsolute(_request != null ? _request.outputDirectory : string.Empty);
        if (string.IsNullOrWhiteSpace(destinationDirectory))
        {
            SetStatus("Output folder is not configured.", MessageType.Warning);
            return;
        }

        try
        {
            Directory.CreateDirectory(destinationDirectory);
            string destinationPath = BuildManualSavePath(
                _lastGeneratedImagePath,
                destinationDirectory,
                _lastRequestedOutputFileName,
                _lastRequestedOutputFormat);

            File.Copy(_lastGeneratedImagePath, destinationPath, overwrite: false);
            _lastSavedOutputPath = destinationPath;
            _lastOutputPath = destinationPath;
            TryRefreshAssetDatabaseForPath(destinationPath);
            SetStatus($"Saved image: {destinationPath}", MessageType.Info);
        }
        catch (Exception ex)
        {
            SetStatus($"Failed to save generated image: {ex.Message}", MessageType.Error);
        }
    }

    private bool TryPrepareEditorRequestForGeneration(
        out StableDiffusionCppGenerationRequest requestCopy,
        out string error,
        out string preparationNote)
    {
        requestCopy = null;
        error = null;
        preparationNote = string.Empty;

        if (_request == null)
        {
            error = "Generation request is null.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(_request.prompt))
        {
            error = "Prompt is required.";
            return false;
        }

        requestCopy = _request.Clone();
        requestCopy.width = Mathf.Max(64, requestCopy.width);
        requestCopy.height = Mathf.Max(64, requestCopy.height);
        requestCopy.steps = Mathf.Max(1, requestCopy.steps);
        requestCopy.cfgScale = Mathf.Max(0.1f, requestCopy.cfgScale);
        requestCopy.strength = Mathf.Clamp(requestCopy.strength, 0.01f, 1f);
        requestCopy.imageCfgScale = Mathf.Max(0.1f, requestCopy.imageCfgScale);
        requestCopy.controlStrength = Mathf.Clamp(requestCopy.controlStrength, 0f, 2f);
        requestCopy.batchCount = Mathf.Clamp(requestCopy.batchCount, 1, 4);
        requestCopy.outputFormat = NormalizeFormat(requestCopy.outputFormat);
        requestCopy.outputDirectory = string.IsNullOrWhiteSpace(requestCopy.outputDirectory)
            ? _settings.editorOutputProjectRelativePath
            : requestCopy.outputDirectory;
        requestCopy.persistOutputToRequestedDirectory = _autoSaveToOutputFolder;
        requestCopy.cacheMode = string.IsNullOrWhiteSpace(requestCopy.cacheMode)
            ? CacheModeOptions[0]
            : requestCopy.cacheMode.Trim();
        requestCopy.cacheOption ??= string.Empty;
        requestCopy.cachePreset ??= string.Empty;
        requestCopy.useControlNet = requestCopy.mode == StableDiffusionCppGenerationMode.Sketch;
        var notes = new StringBuilder(128);
        string resolvedInitImagePath = string.Empty;

        if (requestCopy.RequiresInitImage)
        {
            if (!TryResolveEditorImagePath(requestCopy.initImagePath, out resolvedInitImagePath))
            {
                error = "Init image is required and must point to an existing file.";
                return false;
            }

            requestCopy.initImagePath = resolvedInitImagePath;
            if (requestCopy.useInitImageDimensions)
            {
                if (!StableDiffusionCppImageIO.TryGetImageSizeFromFile(
                        resolvedInitImagePath,
                        out Vector2Int initSize,
                        out string sizeError))
                {
                    error = $"Failed to read init image dimensions: {sizeError}";
                    return false;
                }

                requestCopy.width = initSize.x;
                requestCopy.height = initSize.y;
                AppendPreparationNote(
                    notes,
                    $"Using init image dimensions {initSize.x}x{initSize.y}.");
            }
        }

        if (_snapResolutionToModelGrid)
        {
            Vector2Int originalResolution = new Vector2Int(requestCopy.width, requestCopy.height);
            Vector2Int snappedResolution = SnapResolutionToModelGrid(originalResolution);
            if (snappedResolution != originalResolution)
            {
                requestCopy.width = snappedResolution.x;
                requestCopy.height = snappedResolution.y;
                AppendPreparationNote(
                    notes,
                    $"Resolution snapped to {snappedResolution.x}x{snappedResolution.y} for model safety.");
            }
        }

        if (requestCopy.RequiresInitImage)
        {
            if (!TryPrepareConditioningImageFile(
                    requestCopy.initImagePath,
                    requestCopy.width,
                    requestCopy.height,
                    "init",
                    out string preparedInitImagePath,
                    out string initPrepareNote,
                    out error))
            {
                return false;
            }

            requestCopy.initImagePath = preparedInitImagePath;
            AppendPreparationNote(notes, initPrepareNote);
        }

        if (requestCopy.RequiresMaskImage)
        {
            if (_maskInputSource == MaskInputSource.Paint)
            {
                if (!TryCreatePaintedMaskFile(
                        requestCopy.width,
                        requestCopy.height,
                        out string paintedMaskPath,
                        out string paintedMaskNote,
                        out error))
                {
                    return false;
                }

                requestCopy.maskImagePath = paintedMaskPath;
                AppendPreparationNote(notes, paintedMaskNote);
            }
            else
            {
                if (!TryResolveEditorImagePath(requestCopy.maskImagePath, out string resolvedMaskImagePath))
                {
                    error = "Mask image is required for inpainting and must point to an existing file.";
                    return false;
                }

                if (!TryValidateEditorMaskDimensions(resolvedInitImagePath, resolvedMaskImagePath, out error))
                {
                    return false;
                }

                if (!TryPrepareConditioningImageFile(
                        resolvedMaskImagePath,
                        requestCopy.width,
                        requestCopy.height,
                        "mask",
                        out string preparedMaskImagePath,
                        out string maskPrepareNote,
                        out error))
                {
                    return false;
                }

                requestCopy.maskImagePath = preparedMaskImagePath;
                AppendPreparationNote(notes, maskPrepareNote);
            }
        }

        if (requestCopy.mode == StableDiffusionCppGenerationMode.Sketch)
        {
            string rawControlNetPath = string.IsNullOrWhiteSpace(requestCopy.controlNetPathOverride)
                ? GetDefaultControlNetPathFromSettings()
                : requestCopy.controlNetPathOverride;
            if (!TryResolveEditorModelPath(rawControlNetPath, out string resolvedControlNetPath))
            {
                error = "ControlNet model is required for Sketch mode and must point to an existing file.";
                return false;
            }

            requestCopy.controlNetPathOverride = resolvedControlNetPath;
            if (!TryCreateSketchControlImageFile(
                    requestCopy.width,
                    requestCopy.height,
                    out string sketchImagePath,
                    out string sketchPrepareNote,
                    out error))
            {
                return false;
            }

            requestCopy.controlImagePath = sketchImagePath;
            AppendPreparationNote(notes, sketchPrepareNote);
        }

        preparationNote = notes.ToString();
        return true;
    }

    private bool TryPrepareConditioningImageFile(
        string sourcePath,
        int targetWidth,
        int targetHeight,
        string tempPrefix,
        out string preparedPath,
        out string note,
        out string error)
    {
        preparedPath = string.Empty;
        note = string.Empty;
        error = null;

        if (!StableDiffusionCppImageIO.TryLoadTextureFromFile(sourcePath, out Texture2D sourceTexture, out error))
        {
            return false;
        }

        Texture2D workingTexture = sourceTexture;
        bool ownsWorkingTexture = true;
        try
        {
            if (workingTexture.width != targetWidth || workingTexture.height != targetHeight)
            {
                if (!StableDiffusionCppImageIO.TryResizeTexture(
                        workingTexture,
                        targetWidth,
                        targetHeight,
                        out Texture2D resizedTexture,
                        out error))
                {
                    return false;
                }

                ReleaseTexture(ref workingTexture);
                workingTexture = resizedTexture;
                ownsWorkingTexture = true;
                note = $"{tempPrefix} image resized to {targetWidth}x{targetHeight}.";
            }

            if (!TryWriteEditorTempTexture(workingTexture, tempPrefix, out preparedPath, out error))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(note))
            {
                note = $"{tempPrefix} image prepared for generation.";
            }

            return true;
        }
        finally
        {
            if (ownsWorkingTexture)
            {
                ReleaseTexture(ref workingTexture);
            }
        }
    }

    private bool TryCreatePaintedMaskFile(
        int targetWidth,
        int targetHeight,
        out string preparedPath,
        out string note,
        out string error)
    {
        preparedPath = string.Empty;
        note = string.Empty;
        error = null;

        EnsurePaintMaskTextureMatchesInitPreview();
        if (_paintMaskTexture == null)
        {
            error = "Painted mask is not available yet.";
            return false;
        }

        Texture2D exportTexture = CreateOpaqueExportMaskTexture(_paintMaskTexture);
        Texture2D workingTexture = exportTexture;
        try
        {
            if (workingTexture.width != targetWidth || workingTexture.height != targetHeight)
            {
                if (!StableDiffusionCppImageIO.TryResizeTexture(
                        workingTexture,
                        targetWidth,
                        targetHeight,
                        out Texture2D resizedTexture,
                        out error))
                {
                    return false;
                }

                ReleaseTexture(ref workingTexture);
                workingTexture = resizedTexture;
                note = $"Painted mask resized to {targetWidth}x{targetHeight}.";
            }

            if (!TryWriteEditorTempTexture(workingTexture, "paint_mask", out preparedPath, out error))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(note))
            {
                note = "Painted mask prepared for generation.";
            }

            return true;
        }
        finally
        {
            ReleaseTexture(ref workingTexture);
        }
    }

    private bool TryCreateSketchControlImageFile(
        int targetWidth,
        int targetHeight,
        out string preparedPath,
        out string note,
        out string error)
    {
        preparedPath = string.Empty;
        note = string.Empty;
        error = null;

        EnsureSketchTextureMatchesRequestResolution();
        if (_sketchTexture == null)
        {
            error = "Sketch canvas is not available yet.";
            return false;
        }

        if (!HasVisibleSketchStroke(_sketchTexture))
        {
            error = "Sketch canvas is empty. Draw a black sketch stroke before generating.";
            return false;
        }

        Texture2D workingTexture = _sketchTexture;
        bool ownsWorkingTexture = false;
        try
        {
            if (workingTexture.width != targetWidth || workingTexture.height != targetHeight)
            {
                if (!StableDiffusionCppImageIO.TryResizeTexture(
                        workingTexture,
                        targetWidth,
                        targetHeight,
                        out Texture2D resizedTexture,
                        out error))
                {
                    return false;
                }

                workingTexture = resizedTexture;
                ownsWorkingTexture = true;
                note = $"Sketch control image resized to {targetWidth}x{targetHeight}.";
            }

            if (!TryWriteEditorTempTexture(workingTexture, "sketch_control", out preparedPath, out error))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(note))
            {
                note = "Sketch control image prepared for generation.";
            }

            return true;
        }
        finally
        {
            if (ownsWorkingTexture)
            {
                ReleaseTexture(ref workingTexture);
            }
        }
    }

    private static bool HasVisibleSketchStroke(Texture2D sketchTexture)
    {
        if (sketchTexture == null)
        {
            return false;
        }

        Color32[] pixels = sketchTexture.GetPixels32();
        for (int i = 0; i < pixels.Length; i++)
        {
            Color32 pixel = pixels[i];
            if (pixel.r < 245 || pixel.g < 245 || pixel.b < 245)
            {
                return true;
            }
        }

        return false;
    }

    private Texture2D CreateOpaqueExportMaskTexture(Texture2D sourceMask)
    {
        var exportTexture = new Texture2D(sourceMask.width, sourceMask.height, TextureFormat.RGBA32, false)
        {
            name = "StableDiffusionPaintMaskExport"
        };

        Color32[] sourcePixels = sourceMask.GetPixels32();
        Color32[] exportPixels = new Color32[sourcePixels.Length];
        for (int i = 0; i < sourcePixels.Length; i++)
        {
            bool masked = sourcePixels[i].a >= 128;
            exportPixels[i] = masked
                ? new Color32(255, 255, 255, 255)
                : new Color32(0, 0, 0, 255);
        }

        exportTexture.SetPixels32(exportPixels);
        exportTexture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
        return exportTexture;
    }

    private bool TryWriteEditorTempTexture(
        Texture2D texture,
        string prefix,
        out string absolutePath,
        out string error)
    {
        string directory = Path.Combine(Application.temporaryCachePath, "sdcpp_editor_inputs");
        Directory.CreateDirectory(directory);
        return StableDiffusionCppImageIO.TryWriteTextureToUniqueTempPng(
            texture,
            directory,
            prefix,
            out absolutePath,
            out error);
    }

    private static void AppendPreparationNote(StringBuilder notes, string note)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            return;
        }

        if (notes.Length > 0)
        {
            notes.Append(' ');
        }

        notes.Append(note.Trim());
    }

    private bool TryApplyInitImageDimensionsToRequest(bool showStatusOnSuccess)
    {
        if (!TryResolveEditorImagePath(_request.initImagePath, out string resolvedInitImagePath))
        {
            SetStatus("Init image file not found.", MessageType.Warning);
            return false;
        }

        if (!StableDiffusionCppImageIO.TryGetImageSizeFromFile(
                resolvedInitImagePath,
                out Vector2Int initSize,
                out string sizeError))
        {
            SetStatus($"Failed to read init image dimensions: {sizeError}", MessageType.Error);
            return false;
        }

        _request.width = initSize.x;
        _request.height = initSize.y;
        if (showStatusOnSuccess)
        {
            SetStatus($"Applied init image dimensions: {initSize.x}x{initSize.y}", MessageType.Info);
        }

        return true;
    }

    private bool TryResolveEditorImagePath(string rawPath, out string resolvedPath)
    {
        resolvedPath = string.Empty;
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return false;
        }

        string trimmed = rawPath.Trim();
        if (Path.IsPathRooted(trimmed))
        {
            resolvedPath = trimmed;
            return File.Exists(resolvedPath);
        }

        string normalized = trimmed
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);

        string streamingAssetsCandidate = Path.Combine(Application.streamingAssetsPath, normalized);
        if (File.Exists(streamingAssetsCandidate))
        {
            resolvedPath = streamingAssetsCandidate;
            return true;
        }

        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string projectCandidate = Path.Combine(projectRoot, normalized);
        resolvedPath = projectCandidate;
        return File.Exists(projectCandidate);
    }

    private bool TryResolveEditorModelPath(string rawPath, out string resolvedPath)
    {
        return TryResolveEditorImagePath(rawPath, out resolvedPath);
    }

    private bool TryValidateEditorMaskDimensions(string initImagePath, string maskImagePath, out string error)
    {
        error = null;
        if (!StableDiffusionCppImageIO.TryGetImageSizeFromFile(initImagePath, out Vector2Int initSize, out string initError))
        {
            error = $"Failed to read init image dimensions: {initError}";
            return false;
        }

        if (!StableDiffusionCppImageIO.TryGetImageSizeFromFile(maskImagePath, out Vector2Int maskSize, out string maskError))
        {
            error = $"Failed to read mask image dimensions: {maskError}";
            return false;
        }

        if (initSize != maskSize)
        {
            error =
                $"Init image and mask image must have matching dimensions. Init={initSize.x}x{initSize.y}, Mask={maskSize.x}x{maskSize.y}.";
            return false;
        }

        return true;
    }

    private Texture2D LoadTextureAssetFromPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string normalized = path.Trim().Replace('\\', '/');
        return AssetDatabase.LoadAssetAtPath<Texture2D>(normalized);
    }

    private string GetInitialImageBrowseDirectory(string path)
    {
        if (TryResolveEditorImagePath(path, out string resolvedPath))
        {
            if (File.Exists(resolvedPath))
            {
                return Path.GetDirectoryName(resolvedPath) ?? resolvedPath;
            }
        }

        return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    }

    private static string GetModeLabel(StableDiffusionCppGenerationMode mode)
    {
        switch (mode)
        {
            case StableDiffusionCppGenerationMode.Img2Img:
                return "Img2Img";
            case StableDiffusionCppGenerationMode.Inpaint:
                return "Inpaint";
            case StableDiffusionCppGenerationMode.Sketch:
                return "Sketch";
            default:
                return "Txt2Img";
        }
    }

    private string GetPreferredRevealPath()
    {
        if (!string.IsNullOrWhiteSpace(_lastSavedOutputPath) && File.Exists(_lastSavedOutputPath))
        {
            return _lastSavedOutputPath;
        }

        if (!string.IsNullOrWhiteSpace(_lastGeneratedImagePath) && File.Exists(_lastGeneratedImagePath))
        {
            return _lastGeneratedImagePath;
        }

        if (!string.IsNullOrWhiteSpace(_lastOutputPath) && File.Exists(_lastOutputPath))
        {
            return _lastOutputPath;
        }

        return string.Empty;
    }

    private string ResolveOutputDirectoryAbsolute(string outputDirectory)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            return _settings != null ? _settings.ResolveEditorOutputDirectoryAbsolute() : string.Empty;
        }

        if (Path.IsPathRooted(outputDirectory))
        {
            return outputDirectory;
        }

        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        return Path.Combine(
            projectRoot,
            outputDirectory.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar));
    }

    private static string BuildManualSavePath(
        string sourcePath,
        string destinationDirectory,
        string requestedFileName,
        string requestedFormat)
    {
        string baseName = string.IsNullOrWhiteSpace(requestedFileName)
            ? Path.GetFileNameWithoutExtension(sourcePath)
            : Path.GetFileNameWithoutExtension(requestedFileName.Trim());
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = "sd_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
        }

        string extension = NormalizeFormat(requestedFormat);
        string candidate = Path.Combine(destinationDirectory, baseName + "." + extension);
        return MakeUniqueFilePath(candidate);
    }

    private static string MakeUniqueFilePath(string path)
    {
        if (!File.Exists(path))
        {
            return path;
        }

        string directory = Path.GetDirectoryName(path) ?? string.Empty;
        string name = Path.GetFileNameWithoutExtension(path);
        string extension = Path.GetExtension(path);
        for (int i = 1; i < 10000; i++)
        {
            string candidate = Path.Combine(directory, $"{name}_{i:000}{extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(directory, $"{name}_{Guid.NewGuid():N}{extension}");
    }

    private void PrepareLivePreviewArgs(StableDiffusionCppGenerationRequest request)
    {
        ResetLivePreviewState();

        if (!_enableLivePreview || request == null)
        {
            return;
        }

        string previewDir = Path.Combine(Application.persistentDataPath, "sdcpp", "live_preview");
        try
        {
            Directory.CreateDirectory(previewDir);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[StableDiffusionCppGeneratorWindow] Failed to create live preview directory: {ex.Message}");
            return;
        }

        _livePreviewPath = Path.Combine(
            previewDir,
            "preview_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".png");
        _livePreviewLastWriteUtc = DateTime.MinValue;
        _livePreviewUpdateCount = 0;
        _nextLivePreviewPollTime = 0d;

        int previewInterval = Mathf.Clamp(_livePreviewInterval, 1, 10);
        string previewPathForCli = _livePreviewPath.Replace('\\', '/');
        string livePreviewArgs = $"--preview vae --preview-interval {previewInterval} --preview-path \"{previewPathForCli}\"";
        request.extraArgumentsRaw = AppendRawArgs(request.extraArgumentsRaw, livePreviewArgs);
    }

    private void UpdateLivePreviewDuringGeneration()
    {
        if (!_enableLivePreview || string.IsNullOrWhiteSpace(_livePreviewPath))
        {
            return;
        }

        double now = EditorApplication.timeSinceStartup;
        if (now < _nextLivePreviewPollTime)
        {
            return;
        }

        _nextLivePreviewPollTime = now + 0.15d;
        if (!File.Exists(_livePreviewPath))
        {
            return;
        }

        DateTime writeUtc = File.GetLastWriteTimeUtc(_livePreviewPath);
        if (writeUtc <= _livePreviewLastWriteUtc)
        {
            return;
        }

        _livePreviewLastWriteUtc = writeUtc;
        _livePreviewUpdateCount++;
        TryLoadPreviewTexture(_livePreviewPath);
        SetStatus(
            $"Generating... live preview update {_livePreviewUpdateCount} (interval {_livePreviewInterval} step).",
            MessageType.Info);
        Repaint();
    }

    private void ResetLivePreviewState()
    {
        _livePreviewPath = null;
        _livePreviewLastWriteUtc = DateTime.MinValue;
        _livePreviewUpdateCount = 0;
        _nextLivePreviewPollTime = 0d;
    }

    private static string AppendRawArgs(string existing, string toAppend)
    {
        if (string.IsNullOrWhiteSpace(existing))
        {
            return toAppend ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(toAppend))
        {
            return existing;
        }

        return existing.Trim() + " " + toAppend.Trim();
    }

    private static StableDiffusionCppSettings FindFirstSettingsAsset()
    {
        const string preferredPath = "Assets/ScriptableObjects/StableDiffusion/StableDiffusionCppSettings.asset";
        StableDiffusionCppSettings preferred = AssetDatabase.LoadAssetAtPath<StableDiffusionCppSettings>(preferredPath);
        if (preferred != null)
        {
            return preferred;
        }

        string[] guids = AssetDatabase.FindAssets("t:StableDiffusionCppSettings");
        if (guids == null || guids.Length == 0)
        {
            return null;
        }

        string path = AssetDatabase.GUIDToAssetPath(guids[0]);
        return AssetDatabase.LoadAssetAtPath<StableDiffusionCppSettings>(path);
    }

    private static StableDiffusionCppSettings CreateDefaultSettingsAsset(bool selectAsset)
    {
        const string folder = "Assets/ScriptableObjects/StableDiffusion";
        const string assetPath = folder + "/StableDiffusionCppSettings.asset";

        if (!AssetDatabase.IsValidFolder("Assets/ScriptableObjects"))
        {
            AssetDatabase.CreateFolder("Assets", "ScriptableObjects");
        }

        if (!AssetDatabase.IsValidFolder(folder))
        {
            AssetDatabase.CreateFolder("Assets/ScriptableObjects", "StableDiffusion");
        }

        StableDiffusionCppSettings existing = AssetDatabase.LoadAssetAtPath<StableDiffusionCppSettings>(assetPath);
        if (existing != null)
        {
            if (selectAsset)
            {
                Selection.activeObject = existing;
                EditorGUIUtility.PingObject(existing);
            }
            return existing;
        }

        var created = CreateInstance<StableDiffusionCppSettings>();
        AssetDatabase.CreateAsset(created, assetPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (selectAsset)
        {
            Selection.activeObject = created;
            EditorGUIUtility.PingObject(created);
        }

        return created;
    }

    private void ApplyDefaultsFromSettings()
    {
        if (_settings == null || _request == null)
        {
            return;
        }

        StableDiffusionCppGenerationMode currentMode = _request.mode;
        string initImagePath = _request.initImagePath;
        string maskImagePath = _request.maskImagePath;
        string controlImagePath = _request.controlImagePath;

        _request = new StableDiffusionCppGenerationRequest
        {
            mode = currentMode,
            initImagePath = initImagePath,
            maskImagePath = maskImagePath,
            controlImagePath = controlImagePath,
            offloadToCpu = _settings.defaultOffloadToCpu,
            clipOnCpu = _settings.defaultClipOnCpu,
            vaeTiling = _settings.defaultVaeTiling,
            diffusionFlashAttention = _settings.defaultDiffusionFlashAttention,
            useCacheMode = _settings.defaultUseCacheMode,
            cacheMode = string.IsNullOrWhiteSpace(_settings.defaultCacheMode)
                ? CacheModeOptions[0]
                : _settings.defaultCacheMode.Trim(),
            cacheOption = _settings.defaultCacheOption ?? string.Empty,
            cachePreset = _settings.defaultCachePreset ?? string.Empty,
            outputFormat = "png",
            outputDirectory = _settings.editorOutputProjectRelativePath
        };

        _settings.TryApplyActiveProfileDefaults(_request);
        _request.controlNetPathOverride = GetDefaultControlNetPathFromSettings();
        _samplerIndex = FindSamplerIndex(_request.sampler);
        _schedulerIndex = FindSchedulerIndex(_request.scheduler);
    }

    private string GetDefaultControlNetPathFromSettings()
    {
        if (_settings == null)
        {
            return string.Empty;
        }

        StableDiffusionCppModelProfile profile = _settings.GetActiveModelProfile();
        if (profile != null && !string.IsNullOrWhiteSpace(profile.controlNetPath))
        {
            return profile.controlNetPath.Trim();
        }

        return string.Empty;
    }

    private void ApplyModelProfileDefaults()
    {
        if (_settings == null || _request == null)
        {
            return;
        }

        if (_settings.TryApplyActiveProfileDefaults(_request))
        {
            _samplerIndex = FindSamplerIndex(_request.sampler);
            _schedulerIndex = FindSchedulerIndex(_request.scheduler);
            _request.controlNetPathOverride = GetDefaultControlNetPathFromSettings();
            StableDiffusionCppModelProfile profile = _settings.GetActiveModelProfile();
            string profileName = profile != null ? profile.DisplayName : "Active Profile";
            SetStatus($"Applied '{profileName}' defaults.", MessageType.Info);
            return;
        }
        
        SetStatus(
            "No active model profile selected. Set one in StableDiffusionCppSettings.",
            MessageType.Warning);
    }

    private void DrawActiveProfileSummary()
    {
        EditorGUILayout.LabelField("Model Profile", EditorStyles.boldLabel);
        StableDiffusionCppModelProfile activeProfile = _settings != null ? _settings.GetActiveModelProfile() : null;
        if (activeProfile == null)
        {
            EditorGUILayout.HelpBox(
                "No active model profile selected in StableDiffusionCppSettings. Select a profile before generating.",
                MessageType.Warning);
            return;
        }

        EditorGUILayout.LabelField("Active Profile", activeProfile.DisplayName);
        DrawWrappedSelectableValue(
            "Model Path",
            string.IsNullOrWhiteSpace(activeProfile.modelPath) ? "(empty)" : activeProfile.modelPath);
        if (!string.IsNullOrWhiteSpace(activeProfile.vaePath))
        {
            DrawWrappedSelectableValue("VAE Path", activeProfile.vaePath);
        }
        if (!string.IsNullOrWhiteSpace(activeProfile.controlNetPath))
        {
            DrawWrappedSelectableValue("ControlNet Path", activeProfile.controlNetPath);
        }

        if (GUILayout.Button("Select Active Profile Asset", GUILayout.Height(20f)))
        {
            Selection.activeObject = activeProfile;
            EditorGUIUtility.PingObject(activeProfile);
        }
    }

    private static int FindSamplerIndex(string sampler)
    {
        for (int i = 0; i < SamplerOptions.Length; i++)
        {
            if (string.Equals(SamplerOptions[i], sampler, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return 0;
    }

    private static int FindSchedulerIndex(string scheduler)
    {
        for (int i = 0; i < SchedulerOptions.Length; i++)
        {
            if (string.Equals(SchedulerOptions[i], scheduler, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return 0;
    }

    private static int FindOptionIndex(string[] options, string value)
    {
        if (options == null || options.Length == 0)
        {
            return 0;
        }

        string normalized = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        for (int i = 0; i < options.Length; i++)
        {
            if (string.Equals(options[i], normalized, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return 0;
    }

    private static string NormalizeFormat(string format)
    {
        if (string.Equals(format, "jpg", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(format, "jpeg", StringComparison.OrdinalIgnoreCase))
        {
            return "jpg";
        }

        return "png";
    }

    private static string BuildExecutionLog(StableDiffusionCppGenerationResult result)
    {
        var sb = new StringBuilder(1024);
        sb.AppendLine($"Success: {result.Success}");
        sb.AppendLine($"Cancelled: {result.Cancelled}");
        sb.AppendLine($"TimedOut: {result.TimedOut}");
        sb.AppendLine($"ExitCode: {result.ExitCode}");
        sb.AppendLine($"Elapsed: {result.Elapsed.TotalSeconds:F1}s");
        sb.AppendLine("OutputDirectory:");
        sb.AppendLine(result.OutputDirectory);
        sb.AppendLine("Command:");
        sb.AppendLine(result.CommandLine);
        sb.AppendLine($"GpuTelemetryAvailable: {result.GpuTelemetryAvailable}");
        if (result.GpuTelemetryAvailable)
        {
            sb.AppendLine($"PeakProcessGpuMemoryMiB: {result.PeakGpuMemoryMiB}");
            sb.AppendLine($"GpuTelemetrySamples: {result.GpuTelemetrySamples}");
        }

        if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
        {
            sb.AppendLine();
            sb.AppendLine("Error:");
            sb.AppendLine(result.ErrorMessage);
        }

        if (!string.IsNullOrWhiteSpace(result.StdOut))
        {
            sb.AppendLine();
            sb.AppendLine("StdOut:");
            sb.AppendLine(result.StdOut);
        }

        if (!string.IsNullOrWhiteSpace(result.StdErr))
        {
            sb.AppendLine();
            sb.AppendLine("StdErr:");
            sb.AppendLine(result.StdErr);
        }

        if (result.OutputFiles != null && result.OutputFiles.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Output Files:");
            for (int i = 0; i < result.OutputFiles.Count; i++)
            {
                sb.AppendLine(result.OutputFiles[i]);
            }
        }

        return sb.ToString();
    }

    private static void LogResultToUnityConsole(StableDiffusionCppGenerationResult result)
    {
        if (result == null)
        {
            return;
        }

        string gpuText = result.GpuTelemetryAvailable
            ? $"PeakProcessVRAM={result.PeakGpuMemoryMiB}MiB (samples={result.GpuTelemetrySamples})"
            : "PeakProcessVRAM=unavailable";
        string firstOutput = result.OutputFiles != null && result.OutputFiles.Count > 0
            ? result.OutputFiles[0]
            : "(none)";

        string summary =
            $"[StableDiffusionCpp] Success={result.Success}, ExitCode={result.ExitCode}, Elapsed={result.Elapsed.TotalSeconds:F1}s, {gpuText}, Output={firstOutput}";

        if (result.Success)
        {
            Debug.Log(summary);
            return;
        }

        if (result.Cancelled)
        {
            Debug.LogWarning(summary + $"\nReason: {result.ErrorMessage}");
            return;
        }

        Debug.LogError(summary + $"\nReason: {result.ErrorMessage}\nCommand: {result.CommandLine}");
    }

    private void TryLoadPreviewTexture(string absolutePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(absolutePath) || !File.Exists(absolutePath))
            {
                return;
            }

            byte[] data = File.ReadAllBytes(absolutePath);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(data))
            {
                DestroyImmediate(tex);
                return;
            }

            ReleaseTexture(ref _previewTexture);
            _previewTexture = tex;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[StableDiffusionCppGeneratorWindow] Failed to load preview: {ex.Message}");
        }
    }

    private void TryRefreshAssetDatabaseForPath(string absolutePath)
    {
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        if (absolutePath.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
        {
            AssetDatabase.Refresh();
        }
    }

    private static string MakeProjectRelativePath(string absolutePath, string projectRoot)
    {
        if (string.IsNullOrWhiteSpace(absolutePath) || string.IsNullOrWhiteSpace(projectRoot))
        {
            return absolutePath;
        }

        string normalizedRoot = projectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                                 + Path.DirectorySeparatorChar;
        if (!absolutePath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            return absolutePath;
        }

        string relative = absolutePath.Substring(normalizedRoot.Length);
        return relative.Replace(Path.DirectorySeparatorChar, '/');
    }

    private void SetStatus(string message, MessageType type)
    {
        _statusMessage = string.IsNullOrWhiteSpace(message) ? "Ready." : message;
        _statusType = type;
    }

    private void ReleaseConditioningPreviewTextures()
    {
        ReleaseTexture(ref _initPreviewTexture);
        ReleaseTexture(ref _maskPreviewTexture);
        _initPreviewResolvedPath = string.Empty;
        _maskPreviewResolvedPath = string.Empty;
        _initPreviewLastWriteTicks = 0L;
        _maskPreviewLastWriteTicks = 0L;
        _initPreviewSize = default;
        _maskPreviewSize = default;
    }

    private void ReleasePreviewTexture()
    {
        ReleaseTexture(ref _previewTexture);
    }

    private static void ReleaseTexture(ref Texture2D texture)
    {
        if (texture == null)
        {
            return;
        }

        DestroyImmediate(texture);
        texture = null;
    }
}
#endif
