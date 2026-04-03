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

    private CancellationTokenSource _generationCancellation;
    private bool _isGenerating;
    private string _statusMessage = "Sketch guide generator is ready.";

    public event Action<bool, string> GenerationStateChanged;

    public bool IsGenerating => _isGenerating;
    public string StatusMessage => _statusMessage;
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

    private void OnDisable()
    {
        CancelGeneration();
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
        Texture2D generatedTexture = null;
        if (drawingBoard != null)
        {
            drawingBoard.SetInteractionLocked(true);
        }

        SetStatus(true, "Generating from sketch guide...");

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
                SetStatus(false, "Sketch guide generation produced no image.");
                return;
            }

            if (!StableDiffusionCppImageIO.TryLoadTextureFromFile(result.OutputFiles[0], out generatedTexture, out string loadError))
            {
                SetStatus(false, loadError);
                return;
            }

            if (drawingBoard == null)
            {
                SetStatus(false, "Drawing board reference was lost before applying the result.");
                return;
            }

            int padding = Mathf.Max(regionPadding, drawingBoard.SketchGuideRegionPadding);
            SketchGuideApplyResult applyResult = await ApplySketchGuideResultAsync(
                generatedTexture,
                guideRegion,
                padding,
                cancellationToken);
            if (!applyResult.Success)
            {
                SetStatus(false, applyResult.Error);
                return;
            }

            string successMessage =
                $"Sketch guide applied ({applyResult.AppliedRegion.width}x{applyResult.AppliedRegion.height}, seed {request.seed}, {result.Elapsed.TotalSeconds:0.0}s).";
            SetStatus(false, successMessage);

            if (logGenerationSummary)
            {
                Debug.Log(
                    $"[DrawingSketchGuideGenerator] {successMessage}\nOutput: {result.OutputFiles[0]}\nPrompt: {request.prompt}");
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
            SafeDestroyTexture(generatedTexture);

            _generationCancellation?.Dispose();
            _generationCancellation = null;
        }
    }

    private void SetStatus(bool isGenerating, string message)
    {
        _isGenerating = isGenerating;
        _statusMessage = string.IsNullOrWhiteSpace(message)
            ? (_isGenerating ? "Generating from sketch guide..." : "Sketch guide generator is ready.")
            : message;
        PublishState(_isGenerating, _statusMessage);
    }

    private void PublishState(bool isGenerating, string message)
    {
        GenerationStateChanged?.Invoke(isGenerating, message);
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
        return template.Contains("{0}", StringComparison.Ordinal)
            ? string.Format(template, subject)
            : $"{template} {subject}";
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
