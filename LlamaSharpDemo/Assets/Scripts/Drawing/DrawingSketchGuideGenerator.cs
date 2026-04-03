using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Runs Stable Diffusion sketch + ControlNet from the current sketch-guide overlay.
/// </summary>
public class DrawingSketchGuideGenerator : MonoBehaviour
{
    private const string FallbackSubjectPrompt = "a clean illustrated object";
    private const string ReadyStatusMessage = "Sketch sticker generator is ready.";
    private const int SingleCandidatePerGeneration = 1;
    private static readonly string[] StylePresetNames =
    {
        "Clean Sticker",
        "Photo Realistic",
        "Painterly",
        "Pixel Art",
        "Ink Sketch"
    };

    private static readonly string[] StylePresetPrompts =
    {
        "clean object sticker, readable silhouette, crisp edges, game prop presentation",
        "photo-realistic object, natural materials, detailed lighting, sharp subject focus, product photo quality",
        "painterly hand-painted object, visible brush texture, rich color variation",
        "pixel-art object sprite, chunky readable silhouette, controlled palette, no dithering noise",
        "inked illustration with confident linework and light color wash"
    };

    private readonly struct SketchGuideApplyResult
    {
        public SketchGuideApplyResult(bool success, RectInt appliedRegion, string error)
        {
            Success = success;
            AppliedRegion = appliedRegion;
            Error = error;
        }

        public bool Success { get; }
        public RectInt AppliedRegion { get; }
        public string Error { get; }
    }

    [Header("References")]
    [SerializeField] private DrawingBoardController drawingBoard;
    [SerializeField] private StableDiffusionCppSettings stableDiffusionSettings;
    [SerializeField] private StableDiffusionCppModelProfile selectedModelProfile;

    [Header("Prompt")]
    [TextArea(2, 4)]
    [SerializeField] private string prompt = FallbackSubjectPrompt;
    [TextArea(2, 4)]
    [SerializeField] private string promptTemplate = "Create a clean, fully colored illustration of {0} on a plain white background. Follow the sketch guide silhouette and proportions closely.";
    [TextArea(2, 4)]
    [SerializeField] private string negativePrompt = "low quality, blurry, distorted, text, watermark";

    [Header("Generation")]
    [Range(0f, 2f)]
    [SerializeField] private float controlStrength = 0.95f;
    [Range(1, 60)]
    [SerializeField] private int steps = 20;
    [Min(0.1f)]
    [SerializeField] private float cfgScale = 6f;
    [SerializeField] private int seed = -1;
    [SerializeField] private bool randomizeSeed = true;
    [Min(0)]
    [SerializeField] private int regionPadding = 24;
    [SerializeField] private bool logGenerationSummary = true;

    [Header("Sticker Candidates")]
    [SerializeField] private int selectedStylePresetIndex;
    [SerializeField] private bool extractTransparentStickers = true;
    [Range(0.01f, 0.5f)]
    [SerializeField] private float backgroundRemovalTolerance = 0.08f;
    [Min(0)]
    [SerializeField] private int stickerTrimPadding = 4;
    [SerializeField] private FilterMode stickerFilterMode = FilterMode.Bilinear;
    [Min(1)]
    [SerializeField] private int maxStoredStickerCandidates = 24;

    private CancellationTokenSource _generationCancellation;
    private bool _isGenerating;
    private string _statusMessage = ReadyStatusMessage;
    private readonly List<DrawingStickerCandidate> _stickerCandidates = new();
    private readonly object _progressSnapshotLock = new();
    private StableDiffusionCppWorkerProgressResponse _pendingProgressSnapshot;
    private Texture2D _livePreviewTexture;
    private long _appliedProgressSessionId = -1L;
    private long _appliedPreviewUpdateIndex = -1L;
    private float _generationProgress01;
    private string _generationProgressPhase = "Idle";

    public event Action<bool, string> GenerationStateChanged;
    public event Action<float, Texture2D, string> GenerationProgressChanged;
    public event Action<IReadOnlyList<DrawingStickerCandidate>, string> StickerCandidatesChanged;

    public bool IsGenerating => _isGenerating;
    public string StatusMessage => _statusMessage;
    public float GenerationProgress01 => _generationProgress01;
    public string GenerationProgressPhase => _generationProgressPhase;
    public Texture2D LivePreviewTexture => _livePreviewTexture;
    public IReadOnlyList<DrawingStickerCandidate> StickerCandidates => _stickerCandidates;
    public StableDiffusionCppSettings StableDiffusionSettings => stableDiffusionSettings;
    public StableDiffusionCppModelProfile SelectedModelProfile => GetSelectedModelProfile();
    public string Prompt
    {
        get => prompt;
        set => prompt = value ?? string.Empty;
    }

    public float ControlStrength
    {
        get => controlStrength;
        set => controlStrength = Mathf.Clamp(value, 0f, 2f);
    }

    public float BackgroundRemovalTolerance
    {
        get => backgroundRemovalTolerance;
        set => backgroundRemovalTolerance = Mathf.Clamp(value, 0.01f, 0.5f);
    }

    public int Steps
    {
        get => steps;
        set => steps = Mathf.Clamp(value, 1, 60);
    }

    public float CfgScale
    {
        get => cfgScale;
        set => cfgScale = Mathf.Max(0.1f, value);
    }

    public int Seed
    {
        get => seed;
        set => seed = value;
    }

    public bool RandomizeSeed
    {
        get => randomizeSeed;
        set => randomizeSeed = value;
    }

    public int RegionPadding
    {
        get => regionPadding;
        set => regionPadding = Mathf.Max(0, value);
    }

    private void Awake()
    {
        if (drawingBoard == null)
        {
            drawingBoard = GetComponent<DrawingBoardController>();
        }

        if (drawingBoard == null)
        {
            drawingBoard = FindFirstObjectByType<DrawingBoardController>();
        }

        if (stableDiffusionSettings == null)
        {
            stableDiffusionSettings = FindStableDiffusionSettings();
        }

        InitializeSelectedModelProfile();

        PublishState(_isGenerating, _statusMessage);
    }

    private void OnEnable()
    {
        StableDiffusionCppRuntime.ProgressChanged += OnStableDiffusionProgressChanged;
    }

    private void OnDisable()
    {
        StableDiffusionCppRuntime.ProgressChanged -= OnStableDiffusionProgressChanged;
        CancelGeneration();
    }

    private void OnDestroy()
    {
        ClearStickerCandidates();
        SafeDestroyTexture(_livePreviewTexture);
        _livePreviewTexture = null;
    }

    private void Update()
    {
        ApplyPendingProgressSnapshot();
    }

    public void GenerateFromCurrentGuide()
    {
        if (_isGenerating)
        {
            return;
        }

        if (drawingBoard == null)
        {
            SetStatus(false, "Drawing board reference is missing.");
            return;
        }

        if (stableDiffusionSettings == null)
        {
            stableDiffusionSettings = FindStableDiffusionSettings();
        }

        if (stableDiffusionSettings == null)
        {
            SetStatus(false, "StableDiffusionCppSettings asset is missing.");
            return;
        }

        if (StableDiffusionCppRuntime.IsBusy)
        {
            SetStatus(false, "Stable Diffusion is already running.");
            return;
        }

        _generationCancellation?.Dispose();
        _generationCancellation = new CancellationTokenSource();
        ResetGenerationProgressState();
        _ = RunGenerationAsync(_generationCancellation.Token);
    }

    public void CancelGeneration()
    {
        if (_generationCancellation == null)
        {
            return;
        }

        if (!_generationCancellation.IsCancellationRequested)
        {
            _generationCancellation.Cancel();
        }

        StableDiffusionCppRuntime.CancelActiveGeneration();
    }

    public void SetStableDiffusionSettings(StableDiffusionCppSettings settings)
    {
        stableDiffusionSettings = settings;
        InitializeSelectedModelProfile();
    }

    public int GetModelProfileCount()
    {
        return stableDiffusionSettings != null && stableDiffusionSettings.modelProfiles != null
            ? stableDiffusionSettings.modelProfiles.Count
            : 0;
    }

    public int GetSelectedModelProfileIndex()
    {
        StableDiffusionCppModelProfile profile = GetSelectedModelProfile();
        if (profile == null || stableDiffusionSettings == null || stableDiffusionSettings.modelProfiles == null)
        {
            return -1;
        }

        return stableDiffusionSettings.modelProfiles.IndexOf(profile);
    }

    public void GetModelProfileDisplayNames(List<string> profileNames)
    {
        if (profileNames == null)
        {
            return;
        }

        profileNames.Clear();
        if (stableDiffusionSettings == null || stableDiffusionSettings.modelProfiles == null)
        {
            return;
        }

        for (int i = 0; i < stableDiffusionSettings.modelProfiles.Count; i++)
        {
            StableDiffusionCppModelProfile profile = stableDiffusionSettings.modelProfiles[i];
            profileNames.Add(profile != null ? profile.DisplayName : $"Missing Profile {i + 1}");
        }
    }

    public int GetStylePresetCount()
    {
        return StylePresetNames.Length;
    }

    public int GetSelectedStylePresetIndex()
    {
        return Mathf.Clamp(selectedStylePresetIndex, 0, Mathf.Max(0, StylePresetNames.Length - 1));
    }

    public void GetStylePresetDisplayNames(List<string> styleNames)
    {
        if (styleNames == null)
        {
            return;
        }

        styleNames.Clear();
        styleNames.AddRange(StylePresetNames);
    }

    public void SelectStylePresetIndex(int presetIndex)
    {
        selectedStylePresetIndex = Mathf.Clamp(presetIndex, 0, Mathf.Max(0, StylePresetNames.Length - 1));
        SetStatus(false, $"Style preset selected: {StylePresetNames[selectedStylePresetIndex]}");
    }

    public void ClearStickerCandidates()
    {
        for (int i = 0; i < _stickerCandidates.Count; i++)
        {
            _stickerCandidates[i]?.Dispose();
        }

        _stickerCandidates.Clear();
        PublishStickerCandidates("Sticker candidates cleared.");
    }

    public bool TryPlaceStickerCandidate(int candidateIndex, out string error)
    {
        error = null;

        if (drawingBoard == null)
        {
            error = "Drawing board reference is missing.";
            return false;
        }

        if (candidateIndex < 0 || candidateIndex >= _stickerCandidates.Count)
        {
            error = "Sticker candidate index is out of range.";
            return false;
        }

        DrawingStickerCandidate candidate = _stickerCandidates[candidateIndex];
        if (candidate == null || candidate.Texture == null)
        {
            error = "Selected sticker candidate is unavailable.";
            return false;
        }

        return drawingBoard.TryApplyStickerFromTexture(
            candidate.Texture,
            candidate.PlacementRegion,
            $"Sticker_{candidateIndex + 1:00}",
            out error);
    }

    public void SelectModelProfileIndex(int profileIndex)
    {
        if (_isGenerating)
        {
            return;
        }

        if (stableDiffusionSettings == null)
        {
            stableDiffusionSettings = FindStableDiffusionSettings();
        }

        if (stableDiffusionSettings == null ||
            stableDiffusionSettings.modelProfiles == null ||
            profileIndex < 0 ||
            profileIndex >= stableDiffusionSettings.modelProfiles.Count)
        {
            selectedModelProfile = null;
            SetStatus(false, "Selected SD profile is unavailable.");
            return;
        }

        StableDiffusionCppModelProfile profile = stableDiffusionSettings.modelProfiles[profileIndex];
        if (profile == null)
        {
            selectedModelProfile = null;
            SetStatus(false, "Selected SD profile is missing.");
            return;
        }

        selectedModelProfile = profile;
        ApplySelectedProfileDefaults();
        SetStatus(false, $"SD profile selected: {profile.DisplayName}");
    }

    private async Task RunGenerationAsync(CancellationToken cancellationToken)
    {
        Texture2D controlTexture = null;
        if (drawingBoard != null)
        {
            drawingBoard.SetInteractionLocked(true);
        }

        SetStatus(true, "Generating one sticker from sketch guide...");

        try
        {
            if (!drawingBoard.TryBuildSketchGuideControlTexture(
                    out controlTexture,
                    out RectInt guideRegion,
                    out string guideError))
            {
                SetStatus(false, guideError);
                return;
            }

            var request = new StableDiffusionCppGenerationRequest
            {
                mode = StableDiffusionCppGenerationMode.Sketch,
                prompt = BuildPromptText(),
                negativePrompt = BuildNegativePromptText(),
                controlStrength = Mathf.Clamp(controlStrength, 0f, 2f),
                steps = Mathf.Clamp(steps, 1, 60),
                cfgScale = Mathf.Max(0.1f, cfgScale),
                seed = randomizeSeed ? UnityEngine.Random.Range(1, int.MaxValue) : seed,
                batchCount = SingleCandidatePerGeneration,
                useInitImageDimensions = true,
                useControlNet = true,
                persistOutputToRequestedDirectory = false
            };

            StableDiffusionCppModelProfile profile = GetSelectedModelProfile();
            if (profile != null)
            {
                profile.ApplyDefaultsTo(request);
                request.modelPathOverride = profile.modelPath ?? string.Empty;
                request.controlNetPathOverride = profile.controlNetPath ?? string.Empty;
            }
            else
            {
                stableDiffusionSettings.TryApplyActiveProfileDefaults(request);
            }

            request.mode = StableDiffusionCppGenerationMode.Sketch;
            request.prompt = BuildPromptText();
            request.negativePrompt = BuildNegativePromptText();
            request.controlStrength = Mathf.Clamp(controlStrength, 0f, 2f);
            request.steps = Mathf.Clamp(steps, 1, 60);
            request.cfgScale = Mathf.Max(0.1f, cfgScale);
            request.seed = randomizeSeed ? UnityEngine.Random.Range(1, int.MaxValue) : seed;
            request.batchCount = SingleCandidatePerGeneration;
            request.width = controlTexture.width;
            request.height = controlTexture.height;
            request.useInitImageDimensions = false;
            request.useControlNet = true;
            request.persistOutputToRequestedDirectory = false;

            StableDiffusionCppGenerationResult result = await StableDiffusionCppRuntime.GenerateFromTexturesAsync(
                stableDiffusionSettings,
                request,
                initImage: null,
                maskImage: null,
                controlImage: controlTexture,
                cancellationToken: cancellationToken);

            if (cancellationToken.IsCancellationRequested || result.Cancelled)
            {
                SetStatus(false, "Sketch guide generation cancelled.");
                return;
            }

            if (!result.Success)
            {
                string failure = string.IsNullOrWhiteSpace(result.ErrorMessage)
                    ? "Sketch guide generation failed."
                    : result.ErrorMessage;
                SetStatus(false, failure);
                return;
            }

            if (result.OutputFiles == null || result.OutputFiles.Count == 0)
            {
                SetStatus(false, "Sticker candidate generation produced no image.");
                return;
            }

            if (drawingBoard == null)
            {
                SetStatus(false, "Drawing board reference was lost before building sticker candidates.");
                return;
            }

            int padding = Mathf.Max(regionPadding, drawingBoard.SketchGuideRegionPadding);
            int generatedCandidateCount = 0;
            for (int i = 0; i < result.OutputFiles.Count; i++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    SetStatus(false, "Sticker candidate generation cancelled.");
                    return;
                }

                string outputFile = result.OutputFiles[i];
                int candidateSeed = request.seed > int.MaxValue - i ? request.seed : request.seed + i;
                if (TryCreateStickerCandidateFromOutput(
                        outputFile,
                        guideRegion,
                        padding,
                        BuildPromptText(),
                        candidateSeed,
                        out DrawingStickerCandidate candidate,
                        out string candidateError))
                {
                    _stickerCandidates.Add(candidate);
                    TrimStoredStickerCandidates();
                    generatedCandidateCount++;
                }
                else if (logGenerationSummary)
                {
                    Debug.LogWarning($"[DrawingSketchGuideGenerator] Failed to build sticker candidate from '{outputFile}': {candidateError}");
                }
            }

            if (generatedCandidateCount == 0)
            {
                SetStatus(false, "Generation finished but no transparent sticker could be extracted.");
                PublishStickerCandidates(StatusMessage);
                return;
            }

            string successMessage =
                $"Generated {generatedCandidateCount} sticker from '{Prompt}' and added it to the palette ({result.Elapsed.TotalSeconds:0.0}s, {_stickerCandidates.Count} stored).";
            SetStatus(false, successMessage);
            PublishStickerCandidates(successMessage);

            if (logGenerationSummary)
            {
                Debug.Log(
                    $"[DrawingSketchGuideGenerator] {successMessage}\nOutputs: {string.Join(", ", result.OutputFiles)}\nPrompt: {request.prompt}");
            }
        }
        catch (OperationCanceledException)
        {
            SetStatus(false, "Sketch guide generation cancelled.");
        }
        catch (Exception ex)
        {
            SetStatus(false, $"Sketch guide generation failed: {ex.Message}");
            Debug.LogException(ex);
        }
        finally
        {
            if (drawingBoard != null)
            {
                drawingBoard.SetInteractionLocked(false);
            }

            SafeDestroyTexture(controlTexture);

            _generationCancellation?.Dispose();
            _generationCancellation = null;
        }
    }

    private void SetStatus(bool isGenerating, string message)
    {
        _isGenerating = isGenerating;
        if (!_isGenerating)
        {
            lock (_progressSnapshotLock)
            {
                _pendingProgressSnapshot = null;
            }

            _generationProgress01 = string.IsNullOrWhiteSpace(message) || message == ReadyStatusMessage ? 0f : 1f;
            _generationProgressPhase = "Idle";
        }

        _statusMessage = string.IsNullOrWhiteSpace(message)
            ? (_isGenerating ? "Generating one sticker from sketch guide..." : ReadyStatusMessage)
            : message;
        PublishState(_isGenerating, _statusMessage);
        if (!_isGenerating)
        {
            GenerationProgressChanged?.Invoke(_generationProgress01, _livePreviewTexture, _statusMessage);
        }
    }

    private void PublishState(bool isGenerating, string message)
    {
        GenerationStateChanged?.Invoke(isGenerating, message);
    }

    private void PublishStickerCandidates(string message)
    {
        StickerCandidatesChanged?.Invoke(_stickerCandidates, message ?? StatusMessage);
    }

    private void OnStableDiffusionProgressChanged(StableDiffusionCppWorkerProgressResponse progress)
    {
        if (progress == null || !_isGenerating)
        {
            return;
        }

        lock (_progressSnapshotLock)
        {
            _pendingProgressSnapshot = progress;
        }
    }

    private void ResetGenerationProgressState()
    {
        lock (_progressSnapshotLock)
        {
            _pendingProgressSnapshot = null;
        }

        _generationProgress01 = 0f;
        _generationProgressPhase = "Loading Model";
        _appliedProgressSessionId = -1L;
        _appliedPreviewUpdateIndex = -1L;
        SafeDestroyTexture(_livePreviewTexture);
        _livePreviewTexture = null;
        GenerationProgressChanged?.Invoke(_generationProgress01, _livePreviewTexture, "Loading Stable Diffusion model...");
    }

    private void ApplyPendingProgressSnapshot()
    {
        StableDiffusionCppWorkerProgressResponse snapshot = null;
        lock (_progressSnapshotLock)
        {
            if (_pendingProgressSnapshot != null)
            {
                snapshot = _pendingProgressSnapshot;
                _pendingProgressSnapshot = null;
            }
        }

        if (snapshot == null || !snapshot.hasProgress)
        {
            return;
        }

        _generationProgressPhase = string.IsNullOrWhiteSpace(snapshot.phase)
            ? "Sampling"
            : snapshot.phase;
        _generationProgress01 = Mathf.Clamp01(snapshot.progress01);

        if (snapshot.progressSessionId != _appliedProgressSessionId)
        {
            _appliedProgressSessionId = snapshot.progressSessionId;
            _appliedPreviewUpdateIndex = -1L;
            SafeDestroyTexture(_livePreviewTexture);
            _livePreviewTexture = null;
        }

        if (snapshot.previewImage != null &&
            snapshot.previewImage.HasData &&
            snapshot.previewUpdateIndex > _appliedPreviewUpdateIndex)
        {
            if (TryCreateLivePreviewTexture(snapshot.previewImage, out Texture2D previewTexture))
            {
                SafeDestroyTexture(_livePreviewTexture);
                _livePreviewTexture = previewTexture;
                _appliedPreviewUpdateIndex = snapshot.previewUpdateIndex;
            }
        }

        string message = BuildProgressMessage(snapshot);
        _statusMessage = message;
        GenerationStateChanged?.Invoke(_isGenerating, _statusMessage);
        GenerationProgressChanged?.Invoke(_generationProgress01, _livePreviewTexture, _statusMessage);
    }

    private static string BuildProgressMessage(StableDiffusionCppWorkerProgressResponse snapshot)
    {
        if (snapshot == null)
        {
            return "Generating sticker...";
        }

        if (!string.IsNullOrWhiteSpace(snapshot.message))
        {
            return snapshot.message;
        }

        if (snapshot.totalSteps > 0)
        {
            int step = Mathf.Clamp(snapshot.step, 0, snapshot.totalSteps);
            return $"{snapshot.phase} {step}/{snapshot.totalSteps}";
        }

        return string.IsNullOrWhiteSpace(snapshot.phase)
            ? "Generating sticker..."
            : snapshot.phase;
    }

    private static bool TryCreateLivePreviewTexture(
        StableDiffusionCppWorkerImagePayload previewPayload,
        out Texture2D previewTexture)
    {
        previewTexture = null;
        if (previewPayload == null || !previewPayload.HasData)
        {
            return false;
        }

        try
        {
            byte[] bytes = Convert.FromBase64String(previewPayload.base64Data);
            return StableDiffusionCppImageIO.TryCreateTextureFromTopDownRawBytes(
                bytes,
                previewPayload.width,
                previewPayload.height,
                previewPayload.channelCount,
                FilterMode.Bilinear,
                out previewTexture,
                out _);
        }
        catch
        {
            SafeDestroyTexture(previewTexture);
            previewTexture = null;
            return false;
        }
    }

    private Task<SketchGuideApplyResult> ApplySketchGuideResultAsync(
        Texture2D generatedTexture,
        RectInt guideRegion,
        int padding,
        CancellationToken cancellationToken)
    {
        if (drawingBoard == null)
        {
            return Task.FromResult(new SketchGuideApplyResult(
                false,
                default,
                "Drawing board reference was lost before applying the result."));
        }

        var completionSource = new TaskCompletionSource<SketchGuideApplyResult>();
        drawingBoard.StartCoroutine(drawingBoard.ApplySketchGuideResultCoroutine(
            generatedTexture,
            guideRegion,
            padding,
            cancellationToken,
            (success, appliedRegion, error) =>
            {
                completionSource.TrySetResult(new SketchGuideApplyResult(success, appliedRegion, error));
            }));

        return completionSource.Task;
    }

    private bool TryCreateStickerCandidateFromOutput(
        string outputFilePath,
        RectInt guideRegion,
        int padding,
        string promptText,
        int candidateSeed,
        out DrawingStickerCandidate candidate,
        out string error)
    {
        candidate = null;
        error = null;

        if (string.IsNullOrWhiteSpace(outputFilePath))
        {
            error = "Generated output path is empty.";
            return false;
        }

        if (!StableDiffusionCppImageIO.TryLoadTextureFromFile(outputFilePath, out Texture2D sourceTexture, out error))
        {
            return false;
        }

        Texture2D stickerTexture = null;
        RectInt placementRegion = guideRegion;
        try
        {
            if (extractTransparentStickers)
            {
                if (!DrawingStickerExtractor.TryExtractTransparentSticker(
                        sourceTexture,
                        guideRegion,
                        padding,
                        backgroundRemovalTolerance,
                        stickerTrimPadding,
                        stickerFilterMode,
                        out stickerTexture,
                        out placementRegion,
                        out error))
                {
                    return false;
                }
            }
            else
            {
                stickerTexture = DuplicateTexture(sourceTexture, stickerFilterMode);
                if (placementRegion.width <= 0 || placementRegion.height <= 0)
                {
                    placementRegion = new RectInt(0, 0, sourceTexture.width, sourceTexture.height);
                }
            }

            candidate = new DrawingStickerCandidate(
                stickerTexture,
                placementRegion,
                outputFilePath,
                promptText,
                candidateSeed);
            stickerTexture = null;
            return true;
        }
        finally
        {
            SafeDestroyTexture(sourceTexture);
            SafeDestroyTexture(stickerTexture);
        }
    }

    private void InitializeSelectedModelProfile()
    {
        if (stableDiffusionSettings == null)
        {
            selectedModelProfile = null;
            return;
        }

        if (selectedModelProfile != null &&
            stableDiffusionSettings.modelProfiles != null &&
            stableDiffusionSettings.modelProfiles.Contains(selectedModelProfile))
        {
            ApplySelectedProfileDefaults();
            return;
        }

        selectedModelProfile = stableDiffusionSettings.GetActiveModelProfile();
        ApplySelectedProfileDefaults();
    }

    private StableDiffusionCppModelProfile GetSelectedModelProfile()
    {
        if (selectedModelProfile != null)
        {
            return selectedModelProfile;
        }

        if (stableDiffusionSettings == null)
        {
            return null;
        }

        selectedModelProfile = stableDiffusionSettings.GetActiveModelProfile();
        return selectedModelProfile;
    }

    private void ApplySelectedProfileDefaults()
    {
        StableDiffusionCppModelProfile profile = GetSelectedModelProfile();
        if (profile == null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(profile.defaultNegativePrompt))
        {
            negativePrompt = profile.defaultNegativePrompt;
        }

        controlStrength = Mathf.Clamp(profile.defaultControlStrength, 0f, 2f);
        steps = Mathf.Clamp(profile.defaultSteps, 1, 60);
        cfgScale = Mathf.Max(0.1f, profile.defaultCfgScale);
        if (!randomizeSeed)
        {
            seed = profile.defaultSeed;
        }
    }

    private string BuildPromptText()
    {
        string subject = string.IsNullOrWhiteSpace(prompt)
            ? FallbackSubjectPrompt
            : prompt.Trim();
        string template = string.IsNullOrWhiteSpace(promptTemplate)
            ? "Create a clean, fully colored illustration of {0} on a plain white background. Follow the sketch guide silhouette and proportions closely."
            : promptTemplate.Trim();
        string styledPrompt = template.Contains("{0}", StringComparison.Ordinal)
            ? string.Format(template, subject)
            : $"{subject}, {template}";
        string stylePresetPrompt = GetSelectedStylePresetPrompt();
        return string.IsNullOrWhiteSpace(stylePresetPrompt)
            ? styledPrompt
            : $"{styledPrompt}, {stylePresetPrompt}";
    }

    private string BuildNegativePromptText()
    {
        return negativePrompt ?? string.Empty;
    }

    private static void SafeDestroyTexture(Texture2D texture)
    {
        if (texture == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            UnityEngine.Object.Destroy(texture);
        }
        else
        {
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }

    private static Texture2D DuplicateTexture(Texture2D sourceTexture, FilterMode filterMode)
    {
        if (sourceTexture == null)
        {
            return null;
        }

        var copy = new Texture2D(sourceTexture.width, sourceTexture.height, TextureFormat.RGBA32, false)
        {
            name = $"{sourceTexture.name}_Copy",
            filterMode = filterMode,
            wrapMode = TextureWrapMode.Clamp
        };
        copy.SetPixels32(sourceTexture.GetPixels32());
        copy.Apply(updateMipmaps: false, makeNoLongerReadable: false);
        return copy;
    }

    private string GetSelectedStylePresetPrompt()
    {
        if (StylePresetPrompts.Length == 0)
        {
            return string.Empty;
        }

        int index = Mathf.Clamp(selectedStylePresetIndex, 0, StylePresetPrompts.Length - 1);
        return StylePresetPrompts[index];
    }

    private void TrimStoredStickerCandidates()
    {
        int maxCount = Mathf.Max(1, maxStoredStickerCandidates);
        while (_stickerCandidates.Count > maxCount)
        {
            DrawingStickerCandidate oldestCandidate = _stickerCandidates[0];
            _stickerCandidates.RemoveAt(0);
            oldestCandidate?.Dispose();
        }
    }

    private static StableDiffusionCppSettings FindStableDiffusionSettings()
    {
#if UNITY_EDITOR
        const string preferredPath = "Assets/ScriptableObjects/StableDiffusion/StableDiffusionCppSettings.asset";
        StableDiffusionCppSettings preferred = AssetDatabase.LoadAssetAtPath<StableDiffusionCppSettings>(preferredPath);
        if (preferred != null)
        {
            return preferred;
        }

        string[] guids = AssetDatabase.FindAssets("t:StableDiffusionCppSettings");
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            StableDiffusionCppSettings settings = AssetDatabase.LoadAssetAtPath<StableDiffusionCppSettings>(path);
            if (settings != null)
            {
                return settings;
            }
        }
#endif

        return null;
    }
}
