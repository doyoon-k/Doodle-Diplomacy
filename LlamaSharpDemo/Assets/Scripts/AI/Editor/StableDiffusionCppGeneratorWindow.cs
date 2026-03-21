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
    private int _samplerIndex;
    private bool _autoSaveToOutputFolder;
    private bool _enableLivePreview = true;
    private int _livePreviewInterval = 1;
    private string _livePreviewPath;
    private DateTime _livePreviewLastWriteUtc = DateTime.MinValue;
    private int _livePreviewUpdateCount;
    private double _nextLivePreviewPollTime;

    [MenuItem("Tools/AI/Stable Diffusion CPP/Generator")]
    public static void ShowWindow()
    {
        var window = GetWindow<StableDiffusionCppGeneratorWindow>();
        window.titleContent = new GUIContent("SD CPP Generator");
        window.minSize = new Vector2(560f, 560f);
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
    }

    private void OnGUI()
    {
        _scroll = EditorGUILayout.BeginScrollView(_scroll);

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
        EditorGUILayout.HelpBox(_statusMessage, _statusType);

        EditorGUILayout.EndScrollView();
    }

    private void DrawSettingsSection()
    {
        EditorGUILayout.LabelField("Settings", EditorStyles.boldLabel);
        _settings = (StableDiffusionCppSettings)EditorGUILayout.ObjectField(
            "Config Asset",
            _settings,
            typeof(StableDiffusionCppSettings),
            false);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Find Config", GUILayout.Height(24f)))
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

            if (GUILayout.Button("Create Default Config", GUILayout.Height(24f)))
            {
                _settings = CreateDefaultSettingsAsset(selectAsset: true);
                ApplyDefaultsFromSettings();
            }
        }

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
        _request.prompt = EditorGUILayout.TextArea(_request.prompt, GUILayout.MinHeight(60f));
        _request.negativePrompt = EditorGUILayout.TextField("Negative Prompt", _request.negativePrompt);

        using (new EditorGUILayout.HorizontalScope())
        {
            _request.width = EditorGUILayout.IntField("Width", _request.width);
            _request.height = EditorGUILayout.IntField("Height", _request.height);
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            _request.steps = EditorGUILayout.IntField("Steps", _request.steps);
            _request.cfgScale = EditorGUILayout.FloatField("CFG Scale", _request.cfgScale);
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            _request.seed = EditorGUILayout.IntField("Seed", _request.seed);
            _request.batchCount = EditorGUILayout.IntSlider("Batch Count", _request.batchCount, 1, 4);
        }

        _samplerIndex = Mathf.Clamp(_samplerIndex, 0, SamplerOptions.Length - 1);
        _samplerIndex = EditorGUILayout.Popup("Sampler", _samplerIndex, SamplerOptions);
        _request.sampler = SamplerOptions[_samplerIndex];

        EditorGUILayout.Space(4f);
        DrawActiveProfileSummary();

        using (new EditorGUILayout.HorizontalScope())
        {
            _request.outputFormat = EditorGUILayout.TextField("Format", _request.outputFormat);
            _request.outputFileName = EditorGUILayout.TextField("Output File Name", _request.outputFileName);
        }

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

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Apply Defaults", GUILayout.Height(24f)))
            {
                ApplyDefaultsFromSettings();
                SetStatus("Applied defaults from settings.", MessageType.Info);
            }

            if (GUILayout.Button("Apply Profile Defaults", GUILayout.Height(24f)))
            {
                ApplyModelProfileDefaults();
            }

            if (GUILayout.Button("Prepare Runtime", GUILayout.Height(24f)))
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
        }
    }

    private void DrawControlSection()
    {
        EditorGUILayout.LabelField("Actions", EditorStyles.boldLabel);
        string revealPath = GetPreferredRevealPath();
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(_isGenerating))
            {
                if (GUILayout.Button("Generate", GUILayout.Height(28f)))
                {
                    StartGeneration();
                }
            }

            using (new EditorGUI.DisabledScope(!_isGenerating))
            {
                if (GUILayout.Button("Cancel", GUILayout.Height(28f)))
                {
                    CancelGeneration();
                    SetStatus("Generation cancellation requested.", MessageType.Warning);
                }
            }

            using (new EditorGUI.DisabledScope(_isGenerating || string.IsNullOrWhiteSpace(_lastGeneratedImagePath) || !File.Exists(_lastGeneratedImagePath)))
            {
                if (GUILayout.Button("Save To Output Folder", GUILayout.Height(28f)))
                {
                    SaveLatestGeneratedImage();
                }
            }

            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(revealPath) || !File.Exists(revealPath)))
            {
                if (GUILayout.Button("Reveal Output", GUILayout.Height(28f)))
                {
                    EditorUtility.RevealInFinder(revealPath);
                }
            }
        }
    }

    private void DrawResultSection()
    {
        EditorGUILayout.LabelField("Result", EditorStyles.boldLabel);
        if (_previewTexture != null)
        {
            Rect previewRect = GUILayoutUtility.GetRect(256f, 256f, GUILayout.ExpandWidth(false));
            EditorGUI.DrawPreviewTexture(previewRect, _previewTexture, null, ScaleMode.ScaleToFit);
        }
        else
        {
            EditorGUILayout.HelpBox("No preview image yet.", MessageType.None);
        }

        if (!string.IsNullOrWhiteSpace(_lastGeneratedImagePath))
        {
            EditorGUILayout.LabelField("Generated Image Path");
            EditorGUILayout.SelectableLabel(_lastGeneratedImagePath, GUILayout.Height(34f));
        }

        if (!string.IsNullOrWhiteSpace(_lastSavedOutputPath))
        {
            EditorGUILayout.LabelField("Saved Output Path");
            EditorGUILayout.SelectableLabel(_lastSavedOutputPath, GUILayout.Height(34f));
        }

        EditorGUILayout.LabelField("Execution Log", EditorStyles.boldLabel);
        EditorGUILayout.TextArea(_executionLog, GUILayout.MinHeight(180f));
    }

    private void StartGeneration()
    {
        if (_settings == null)
        {
            SetStatus("Assign StableDiffusionCppSettings before generating.", MessageType.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(_request.prompt))
        {
            SetStatus("Prompt is required.", MessageType.Warning);
            return;
        }

        _request.width = Mathf.Max(64, _request.width);
        _request.height = Mathf.Max(64, _request.height);
        _request.steps = Mathf.Max(1, _request.steps);
        _request.cfgScale = Mathf.Max(0.1f, _request.cfgScale);
        _request.batchCount = Mathf.Clamp(_request.batchCount, 1, 4);
        _request.outputFormat = NormalizeFormat(_request.outputFormat);
        _request.outputDirectory = string.IsNullOrWhiteSpace(_request.outputDirectory)
            ? _settings.editorOutputProjectRelativePath
            : _request.outputDirectory;
        _request.cacheMode = string.IsNullOrWhiteSpace(_request.cacheMode)
            ? CacheModeOptions[0]
            : _request.cacheMode.Trim();
        _request.cacheOption ??= string.Empty;
        _request.cachePreset ??= string.Empty;

        _generationCancellation?.Dispose();
        _generationCancellation = new CancellationTokenSource();

        _isGenerating = true;
        _executionLog = "Running stable-diffusion.cpp...";
        _lastSavedOutputPath = null;
        _lastRequestedOutputDirectory = ResolveOutputDirectoryAbsolute(_request.outputDirectory);
        _lastRequestedOutputFileName = _request.outputFileName;
        _lastRequestedOutputFormat = _request.outputFormat;
        SetStatus("Generation started.", MessageType.Info);

        var requestCopy = new StableDiffusionCppGenerationRequest
        {
            prompt = _request.prompt,
            negativePrompt = _request.negativePrompt,
            width = _request.width,
            height = _request.height,
            steps = _request.steps,
            cfgScale = _request.cfgScale,
            seed = _request.seed,
            batchCount = _request.batchCount,
            sampler = _request.sampler,
            modelPathOverride = string.Empty,
            outputDirectory = _request.outputDirectory,
            outputFileName = _request.outputFileName,
            outputFormat = _request.outputFormat,
            extraArgumentsRaw = _request.extraArgumentsRaw,
            offloadToCpu = _request.offloadToCpu,
            clipOnCpu = _request.clipOnCpu,
            vaeTiling = _request.vaeTiling,
            diffusionFlashAttention = _request.diffusionFlashAttention,
            useCacheMode = _request.useCacheMode,
            cacheMode = _request.cacheMode,
            cacheOption = _request.cacheOption,
            cachePreset = _request.cachePreset,
            persistOutputToRequestedDirectory = _autoSaveToOutputFolder
        };
        PrepareLivePreviewArgs(requestCopy);

        _generationTask = StableDiffusionCppRuntime.GenerateTxt2ImgAsync(
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
                    SetStatus($"Generation complete and saved in {result.Elapsed.TotalSeconds:F1}s.", MessageType.Info);
                }
                else
                {
                    SetStatus(
                        $"Generation complete in {result.Elapsed.TotalSeconds:F1}s. Use 'Save To Output Folder' to persist.",
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

        _request.width = _settings.defaultWidth;
        _request.height = _settings.defaultHeight;
        _request.steps = _settings.defaultSteps;
        _request.cfgScale = _settings.defaultCfgScale;
        _request.seed = _settings.defaultSeed;
        _request.sampler = string.IsNullOrWhiteSpace(_settings.defaultSampler) ? "euler_a" : _settings.defaultSampler.Trim();
        _request.negativePrompt = _settings.defaultNegativePrompt ?? string.Empty;
        _request.offloadToCpu = _settings.defaultOffloadToCpu;
        _request.clipOnCpu = _settings.defaultClipOnCpu;
        _request.vaeTiling = _settings.defaultVaeTiling;
        _request.diffusionFlashAttention = _settings.defaultDiffusionFlashAttention;
        _request.useCacheMode = _settings.defaultUseCacheMode;
        _request.cacheMode = string.IsNullOrWhiteSpace(_settings.defaultCacheMode) ? CacheModeOptions[0] : _settings.defaultCacheMode.Trim();
        _request.cacheOption = _settings.defaultCacheOption ?? string.Empty;
        _request.cachePreset = _settings.defaultCachePreset ?? string.Empty;
        _request.outputFormat = "png";
        _request.outputDirectory = _settings.editorOutputProjectRelativePath;
        _settings.TryApplyActiveProfileDefaults(_request);
        _samplerIndex = FindSamplerIndex(_request.sampler);
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
                "No active model profile selected in StableDiffusionCppSettings. Legacy modelPath/defaults fallback is active.",
                MessageType.Warning);
            return;
        }

        EditorGUILayout.LabelField("Active Profile", activeProfile.DisplayName);
        EditorGUILayout.LabelField("Model Path", string.IsNullOrWhiteSpace(activeProfile.modelPath) ? "(empty)" : activeProfile.modelPath);
        if (!string.IsNullOrWhiteSpace(activeProfile.vaePath))
        {
            EditorGUILayout.LabelField("VAE Path", activeProfile.vaePath);
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
        sb.AppendLine($"OutputDirectory: {result.OutputDirectory}");
        sb.AppendLine($"Command: {result.CommandLine}");
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

            ReleasePreviewTexture();
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

    private void ReleasePreviewTexture()
    {
        if (_previewTexture != null)
        {
            DestroyImmediate(_previewTexture);
            _previewTexture = null;
        }
    }
}
#endif
