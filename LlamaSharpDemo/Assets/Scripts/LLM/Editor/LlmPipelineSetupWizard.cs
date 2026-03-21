#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class LlmPipelineSetupWizard : EditorWindow
{
    private const string WindowTitle = "LLM Setup Wizard";
    private const string DefaultModelFolderName = "Models";
    private const string DefaultHfRevision = "main";
    private const int HfDownloadBufferSizeBytes = 4 * 1024 * 1024; // 4 MB
    private const int HfProgressUpdateIntervalMs = 300;
    private const long HfProgressUpdateMinBytes = 4L * 1024L * 1024L; // 4 MB
    private const long HfParallelMinSizeBytes = 32L * 1024L * 1024L; // 32 MB
    private const long HfParallelMinChunkSizeBytes = 16L * 1024L * 1024L; // 16 MB
    private const int HfParallelMaxSegments = 8;
    private const int HfSegmentRetryMaxAttempts = 3;
    private const int HfSegmentRetryBaseDelayMs = 500;
    private const int HfSegmentRetryMaxDelayMs = 4000;
    private const string NewtonsoftUpmPackage = "com.unity.nuget.newtonsoft-json";
    private const string NewtonsoftUpmVersion = "3.2.2";
    private const string NuGetForUnityUpmPackage = "com.github-glitchenzo.nugetforunity";
    private const string NuGetForUnityUpmGitUrl = "https://github.com/GlitchEnzo/NuGetForUnity.git?path=/src/NuGetForUnity";
    private const string NuGetLlamaVersion = "0.26.0";
    private static readonly HttpClient HfHttpClient = CreateHttpClient();

    private enum BackendMode
    {
        Cpu,
        Cuda12,
        Vulkan,
        Metal
    }

    private static readonly HashSet<string> CoreNativeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "llama.dll",
        "libllama.dll",
        "ggml.dll",
        "libggml.dll",
        "ggml-base.dll",
        "libggml-base.dll",
        "ggml-cpu.dll",
        "mtmd.dll",
        "libmtmd.dll"
    };

    private static readonly HashSet<string> CudaNativeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "ggml-cuda.dll",
        "cudart64_12.dll",
        "cublas64_12.dll",
        "cublasLt64_12.dll"
    };

    private static readonly HashSet<string> VulkanNativeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "ggml-vulkan.dll"
    };

    private static readonly Dictionary<string, string> RequiredNuGetPackageVersions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            { "CommunityToolkit.HighPerformance", "8.4.0" },
            { "LLamaSharp", NuGetLlamaVersion },
            { "Microsoft.Bcl.AsyncInterfaces", "9.0.9" },
            { "Microsoft.Bcl.Numerics", "9.0.9" },
            { "Microsoft.Extensions.AI.Abstractions", "9.10.1" },
            { "Microsoft.Extensions.DependencyInjection.Abstractions", "10.0.2" },
            { "Microsoft.Extensions.Logging.Abstractions", "10.0.2" },
            { "System.Diagnostics.DiagnosticSource", "10.0.2" },
            { "System.IO.Pipelines", "9.0.9" },
            { "System.Linq.Async", "6.0.3" },
            { "System.Numerics.Tensors", "9.0.9" },
            { "System.Text.Encodings.Web", "9.0.9" },
            { "System.Text.Json", "9.0.9" }
        };

    private static readonly string[] NuGetManageMenuCandidates =
    {
        "NuGet/Manage NuGet Packages",
        "NuGet/Manage Packages"
    };

    private static readonly string[] NuGetRestoreMenuCandidates =
    {
        "NuGet/Restore",
        "NuGet/Restore Packages"
    };

    private static readonly BuildTarget[] StandaloneTargets =
    {
        BuildTarget.StandaloneWindows64,
        BuildTarget.StandaloneWindows,
        BuildTarget.StandaloneOSX,
        BuildTarget.StandaloneLinux64
    };

    [Serializable]
    private sealed class HfParallelResumeState
    {
        public string key;
        public long totalBytes;
        public int segmentCount;
        public bool[] completedSegments;
    }

    private enum HfDownloadApplyMode
    {
        LlmProfiles,
        StableDiffusionPreset
    }

    private sealed class HfDownloadPlan
    {
        public string AssetLabel = "model";
        public string RepoId = string.Empty;
        public string FilePath = string.Empty;
        public string Revision = DefaultHfRevision;
        public string Token = string.Empty;
        public string DestinationFolderName = DefaultModelFolderName;
        public string ExpectedExtension = ".gguf";
        public bool AutoApplyAfterDownload = true;
        public HfDownloadApplyMode ApplyMode = HfDownloadApplyMode.LlmProfiles;
        public int StableDiffusionPresetIndex = -1;

        public string FileName => Path.GetFileName(FilePath ?? string.Empty);
    }

    private Vector2 _scroll;
    private BackendMode _backendMode;
    private List<string> _modelChoices = new();
    private int _selectedModelIndex = -1;
    private string _statusMessage = "Ready.";
    private MessageType _statusType = MessageType.Info;
    private bool _forceRelativeToStreamingAssets = true;
    private string _hfRepoId = string.Empty;
    private string _hfFilePath = string.Empty;
    private string _hfRevision = DefaultHfRevision;
    private string _hfToken = string.Empty;
    private bool _hfAutoApplyAfterDownload = true;
    private int _sdSelectedPresetIndex;
    private string _sdRepoId = string.Empty;
    private string _sdFilePath = string.Empty;
    private string _sdRevision = DefaultHfRevision;
    private bool _sdAutoApplyAfterDownload = true;
    private bool _hfIsDownloading;
    private bool _hfIsWatchingBrowserDownload;
    private long _hfDownloadedBytes;
    private long _hfTotalBytes = -1;
    private string _hfDownloadStatus = "Idle";
    private HfDownloadPlan _activeHfDownloadPlan;
    private CancellationTokenSource _hfDownloadCancellation;
    private Task _hfDownloadTask;
    private CancellationTokenSource _hfBrowserWatchCancellation;
    private Task _hfBrowserWatchTask;
    private AddRequest _nugetForUnityAddRequest;
    private AddRequest _newtonsoftAddRequest;
    private readonly object _hfProgressLock = new object();

    [MenuItem("Tools/LLM Pipeline/Setup Wizard")]
    public static void ShowWindow()
    {
        var window = GetWindow<LlmPipelineSetupWizard>();
        window.titleContent = new GUIContent(WindowTitle);
        window.minSize = new Vector2(560f, 480f);
        window.Show();
    }

    private void OnEnable()
    {
        CleanupLegacyPartFilesInStreamingAssets();
        LoadStableDiffusionPresetIntoFields(_sdSelectedPresetIndex);
        RefreshState();
    }

    private void OnDisable()
    {
        CancelActiveHfOperation();
    }

    private void Update()
    {
        HandleUpmAddRequest(ref _nugetForUnityAddRequest, NuGetForUnityUpmPackage);
        HandleUpmAddRequest(ref _newtonsoftAddRequest, NewtonsoftUpmPackage);
    }

    private void OnGUI()
    {
        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        EditorGUILayout.LabelField("LLM Pipeline Setup Wizard", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "This wizard configures native backends, imports GGUF models into StreamingAssets, " +
            "and applies a model path to every LlmGenerationProfile.",
            MessageType.Info);

        DrawBackendSection();
        EditorGUILayout.Space(10f);
        DrawModelSection();
        EditorGUILayout.Space(10f);
        DrawHuggingFaceSection();
        EditorGUILayout.Space(10f);
        DrawStableDiffusionDownloadSection();
        EditorGUILayout.Space(10f);
        DrawValidationSection();
        EditorGUILayout.Space(10f);
        DrawStatusSection();

        EditorGUILayout.EndScrollView();
    }

    private void DrawBackendSection()
    {
        EditorGUILayout.LabelField("1) Backend", EditorStyles.boldLabel);

        BackendMode recommended = RecommendBackendMode();
        EditorGUILayout.HelpBox(
            $"Environment: {BuildEnvironmentSummary()}\nRecommended Backend: {recommended}",
            MessageType.Info);

        _backendMode = (BackendMode)EditorGUILayout.EnumPopup(new GUIContent("Target Backend"), _backendMode);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Use Recommended", GUILayout.Height(28f)))
            {
                _backendMode = recommended;
            }

            if (GUILayout.Button("Apply Backend Configuration", GUILayout.Height(28f)))
            {
                ApplyBackendConfiguration();
            }

            if (GUILayout.Button("Refresh", GUILayout.Width(120f), GUILayout.Height(28f)))
            {
                RefreshState();
            }
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Install/Update Dependencies", GUILayout.Height(24f)))
            {
                InstallOrUpdateDependenciesForSelectedBackend();
            }
            if (GUILayout.Button("Install NuGetForUnity", GUILayout.Height(24f)))
            {
                InstallNuGetForUnity();
            }
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Run NuGet Restore", GUILayout.Height(24f)))
            {
                if (RunNuGetRestore())
                {
                    SetStatus("NuGet restore command executed.", MessageType.Info);
                }
                else
                {
                    SetStatus("NuGet restore menu was not found. Ensure NuGetForUnity is installed.", MessageType.Warning);
                }
            }

            if (GUILayout.Button("Open NuGet Manager", GUILayout.Height(24f)))
            {
                OpenNuGetManager();
            }
        }

        EditorGUILayout.HelpBox(
            "Only one backend should be active at a time. " +
            "The wizard enforces a single active backend set.",
            MessageType.None);
    }

    private void DrawModelSection()
    {
        EditorGUILayout.LabelField("2) Model", EditorStyles.boldLabel);

        if (_modelChoices.Count == 0)
        {
            EditorGUILayout.HelpBox("No GGUF models found under Assets/StreamingAssets.", MessageType.Warning);
        }
        else
        {
            _selectedModelIndex = Mathf.Clamp(_selectedModelIndex, 0, _modelChoices.Count - 1);
            _selectedModelIndex = EditorGUILayout.Popup("StreamingAssets Model", _selectedModelIndex, _modelChoices.ToArray());
        }

        _forceRelativeToStreamingAssets = EditorGUILayout.ToggleLeft(
            "Store profile path as StreamingAssets-relative path",
            _forceRelativeToStreamingAssets);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Import GGUF...", GUILayout.Height(26f)))
            {
                ImportModelToStreamingAssets();
            }

            if (GUILayout.Button("Apply Model To All Profiles", GUILayout.Height(26f)))
            {
                ApplyModelToAllProfiles();
            }
        }
    }

    private void DrawHuggingFaceSection()
    {
        EditorGUILayout.LabelField("2-1) Download From Hugging Face", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Enter a Hugging Face repo and GGUF file path, then download directly into StreamingAssets/Models.",
            MessageType.None);

        _hfRepoId = EditorGUILayout.TextField(new GUIContent("Repo ID"), _hfRepoId);
        _hfFilePath = EditorGUILayout.TextField(new GUIContent("GGUF File Path"), _hfFilePath);
        _hfRevision = EditorGUILayout.TextField(new GUIContent("Revision"), _hfRevision);
        _hfToken = EditorGUILayout.PasswordField(new GUIContent("Access Token (optional)"), _hfToken);
        EditorGUILayout.HelpBox(
            $"Browser-assisted mode watches default Downloads folder:\n{ResolveDefaultDownloadsFolder()}",
            MessageType.None);
        if (string.IsNullOrWhiteSpace(_hfToken))
        {
            EditorGUILayout.HelpBox(
                "No token set. Anonymous Hugging Face downloads can be throttled and significantly slower.",
                MessageType.Warning);
        }
        _hfAutoApplyAfterDownload = EditorGUILayout.ToggleLeft(
            "Auto-apply downloaded model to all LlmGenerationProfile assets",
            _hfAutoApplyAfterDownload);

        if (DrawActiveTransferUiIfNeeded(HfDownloadApplyMode.LlmProfiles))
        {
            return;
        }

        bool hasRequiredInputs =
            !string.IsNullOrWhiteSpace(_hfRepoId) &&
            !string.IsNullOrWhiteSpace(_hfFilePath);

        using (new EditorGUI.DisabledScope(!hasRequiredInputs || HasAnyActiveTransfer()))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Download In Editor", GUILayout.Height(26f)))
                {
                    StartLlmHuggingFaceDownload();
                }

                if (GUILayout.Button("Open Browser (Fast) + Auto Import", GUILayout.Height(26f)))
                {
                    StartLlmBrowserAssistedDownload();
                }
            }
        }
    }

    private void DrawStableDiffusionDownloadSection()
    {
        EditorGUILayout.LabelField("2-2) Stable Diffusion Models", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Download recommended Stable Diffusion GGUF models into StreamingAssets/SDModels and auto-configure StableDiffusionCppSettings.",
            MessageType.None);

        IReadOnlyList<StableDiffusionDownloadPreset> presets = StableDiffusionCppSetupUtility.DownloadPresets;
        if (presets.Count == 0)
        {
            EditorGUILayout.HelpBox("No Stable Diffusion presets are configured.", MessageType.Warning);
            return;
        }

        _sdSelectedPresetIndex = Mathf.Clamp(_sdSelectedPresetIndex, 0, presets.Count - 1);
        string[] presetLabels = presets.Select(preset => preset.Label).ToArray();
        int newPresetIndex = EditorGUILayout.Popup("Preset", _sdSelectedPresetIndex, presetLabels);
        if (newPresetIndex != _sdSelectedPresetIndex)
        {
            _sdSelectedPresetIndex = newPresetIndex;
            LoadStableDiffusionPresetIntoFields(_sdSelectedPresetIndex);
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Use Preset Values", GUILayout.Height(24f)))
            {
                LoadStableDiffusionPresetIntoFields(_sdSelectedPresetIndex);
            }

            if (GUILayout.Button("Apply Existing Download To Settings", GUILayout.Height(24f)))
            {
                ApplyExistingStableDiffusionDownload();
            }
        }

        _sdRepoId = EditorGUILayout.TextField(new GUIContent("Repo ID"), _sdRepoId);
        _sdFilePath = EditorGUILayout.TextField(new GUIContent("GGUF File Path"), _sdFilePath);
        _sdRevision = EditorGUILayout.TextField(new GUIContent("Revision"), _sdRevision);
        _hfToken = EditorGUILayout.PasswordField(new GUIContent("Access Token (optional)"), _hfToken);
        _sdAutoApplyAfterDownload = EditorGUILayout.ToggleLeft(
            "Auto-apply downloaded model to StableDiffusionCppSettings",
            _sdAutoApplyAfterDownload);

        string destinationFolder = StableDiffusionCppSetupUtility.GetModelDestinationDirectoryAbsolute();
        EditorGUILayout.HelpBox(
            $"Destination folder:\n{destinationFolder}",
            MessageType.None);

        if (DrawActiveTransferUiIfNeeded(HfDownloadApplyMode.StableDiffusionPreset))
        {
            return;
        }

        bool hasRequiredInputs =
            !string.IsNullOrWhiteSpace(_sdRepoId) &&
            !string.IsNullOrWhiteSpace(_sdFilePath);

        using (new EditorGUI.DisabledScope(!hasRequiredInputs || HasAnyActiveTransfer()))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Download In Editor", GUILayout.Height(26f)))
                {
                    StartStableDiffusionHuggingFaceDownload();
                }

                if (GUILayout.Button("Open Browser (Fast) + Auto Import", GUILayout.Height(26f)))
                {
                    StartStableDiffusionBrowserDownload();
                }
            }
        }
    }

    private bool DrawActiveTransferUiIfNeeded(HfDownloadApplyMode applyMode)
    {
        if (!IsTransferActiveForMode(applyMode))
        {
            return false;
        }

        GetHfProgressSnapshot(out long downloaded, out long total, out string status);
        float progress = total > 0 ? Mathf.Clamp01((float)downloaded / total) : 0f;
        string progressText = total > 0
            ? $"{FormatBytes(downloaded)} / {FormatBytes(total)}"
            : $"{FormatBytes(downloaded)}";

        Rect rect = GUILayoutUtility.GetRect(18f, 18f);
        EditorGUI.ProgressBar(rect, progress, progressText);
        GUILayout.Space(4f);

        if (!string.IsNullOrWhiteSpace(status))
        {
            EditorGUILayout.HelpBox(status, MessageType.Info);
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Cancel", GUILayout.Height(24f)))
            {
                CancelActiveHfOperation();
            }
        }

        Repaint();
        return true;
    }

    private void DrawValidationSection()
    {
        EditorGUILayout.LabelField("3) Validation", EditorStyles.boldLabel);
        if (GUILayout.Button("Run Quick Validation", GUILayout.Height(26f)))
        {
            RunQuickValidation();
        }
    }

    private void DrawStatusSection()
    {
        EditorGUILayout.LabelField("Status", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(_statusMessage, _statusType);
    }

    private void RefreshState()
    {
        _modelChoices = FindStreamingAssetModels();
        _selectedModelIndex = _modelChoices.Count > 0 ? Mathf.Clamp(_selectedModelIndex, 0, _modelChoices.Count - 1) : -1;
        _backendMode = DetectBackendMode();
        SetStatus("State refreshed.", MessageType.Info);
    }

    private void StartLlmHuggingFaceDownload()
    {
        StartConfiguredHuggingFaceDownload(BuildLlmDownloadPlan(), browserAssisted: false);
    }

    private void StartLlmBrowserAssistedDownload()
    {
        StartConfiguredHuggingFaceDownload(BuildLlmDownloadPlan(), browserAssisted: true);
    }

    private void StartStableDiffusionHuggingFaceDownload()
    {
        StartConfiguredHuggingFaceDownload(BuildStableDiffusionDownloadPlan(), browserAssisted: false);
    }

    private void StartStableDiffusionBrowserDownload()
    {
        StartConfiguredHuggingFaceDownload(BuildStableDiffusionDownloadPlan(), browserAssisted: true);
    }

    private void StartConfiguredHuggingFaceDownload(HfDownloadPlan plan, bool browserAssisted)
    {
        if (plan == null || HasAnyActiveTransfer())
        {
            return;
        }

        string repoId = (plan.RepoId ?? string.Empty).Trim();
        string filePath = NormalizeModelPathForUrl(plan.FilePath);
        string revision = string.IsNullOrWhiteSpace(plan.Revision) ? DefaultHfRevision : plan.Revision.Trim();
        if (string.IsNullOrWhiteSpace(repoId) || string.IsNullOrWhiteSpace(filePath))
        {
            SetStatus($"Repo ID and {plan.ExpectedExtension} file path are required.", MessageType.Warning);
            return;
        }

        if (!string.IsNullOrWhiteSpace(plan.ExpectedExtension) &&
            !filePath.EndsWith(plan.ExpectedExtension, StringComparison.OrdinalIgnoreCase))
        {
            bool continueWithoutExpectedExtension = EditorUtility.DisplayDialog(
                "File Extension Check",
                $"The selected file path does not end with {plan.ExpectedExtension}:\n{filePath}\n\nContinue anyway?",
                "Continue",
                "Cancel");
            if (!continueWithoutExpectedExtension)
            {
                return;
            }
        }

        string fileName = Path.GetFileName(filePath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            SetStatus($"Invalid {plan.AssetLabel} file path.", MessageType.Error);
            return;
        }

        plan.RepoId = repoId;
        plan.FilePath = filePath;
        plan.Revision = revision;
        plan.Token = (plan.Token ?? string.Empty).Trim();

        string destinationDirectory = GetDestinationDirectory(plan);
        Directory.CreateDirectory(destinationDirectory);
        string destinationPath = GetDestinationPath(plan);
        string legacyPartPath = destinationPath + ".part";
        TryDeleteFileSilently(legacyPartPath);

        if (File.Exists(destinationPath))
        {
            bool replace = EditorUtility.DisplayDialog(
                "Replace Existing Model",
                $"A model with the same name already exists:\n{destinationPath}\n\nReplace it?",
                "Replace",
                "Cancel");
            if (!replace)
            {
                return;
            }
        }

        string url = BuildHuggingFaceResolveUrl(repoId, revision, filePath);
        _activeHfDownloadPlan = plan;

        if (browserAssisted)
        {
            string watchDirectory = ResolveDefaultDownloadsFolder();
            if (!Directory.Exists(watchDirectory))
            {
                try
                {
                    Directory.CreateDirectory(watchDirectory);
                }
                catch (Exception ex)
                {
                    SetStatus($"Browser download folder is invalid: {ex.Message}", MessageType.Error);
                    _activeHfDownloadPlan = null;
                    return;
                }
            }

            _hfBrowserWatchCancellation?.Dispose();
            _hfBrowserWatchCancellation = new CancellationTokenSource();
            _hfBrowserWatchTask = null;
            _hfIsWatchingBrowserDownload = true;
            DateTime startedAtUtc = DateTime.UtcNow;

            SetHfProgress(0, -1, $"Opened browser URL. Waiting for '{fileName}' in {watchDirectory}...");
            SetStatus($"Browser download started for {plan.AssetLabel}. Waiting for completion and auto-import...", MessageType.Info);
            Application.OpenURL(url);

            _hfBrowserWatchTask = WatchBrowserDownloadAndImportAsync(
                watchDirectory,
                fileName,
                startedAtUtc,
                destinationPath,
                _hfBrowserWatchCancellation.Token);
            _hfBrowserWatchTask.ContinueWith(task =>
            {
                EditorApplication.delayCall += () => CompleteBrowserAssistedDownload(task);
            }, TaskScheduler.Default);
            return;
        }

        _hfDownloadCancellation?.Dispose();
        _hfDownloadCancellation = new CancellationTokenSource();
        _hfDownloadTask = null;
        _hfIsDownloading = true;
        SetHfProgress(0, -1, $"Starting download from {repoId}...");
        SetStatus($"Downloading {plan.AssetLabel} from Hugging Face...", MessageType.Info);

        _hfDownloadTask = DownloadModelFromHuggingFaceAsync(
            url,
            destinationPath,
            plan.Token,
            _hfDownloadCancellation.Token);

        _hfDownloadTask.ContinueWith(task =>
        {
            EditorApplication.delayCall += () => CompleteHuggingFaceDownload(task);
        }, TaskScheduler.Default);
    }

    private HfDownloadPlan BuildLlmDownloadPlan()
    {
        return new HfDownloadPlan
        {
            AssetLabel = "GGUF model",
            RepoId = _hfRepoId,
            FilePath = _hfFilePath,
            Revision = _hfRevision,
            Token = _hfToken,
            DestinationFolderName = DefaultModelFolderName,
            ExpectedExtension = ".gguf",
            AutoApplyAfterDownload = _hfAutoApplyAfterDownload,
            ApplyMode = HfDownloadApplyMode.LlmProfiles
        };
    }

    private HfDownloadPlan BuildStableDiffusionDownloadPlan()
    {
        return new HfDownloadPlan
        {
            AssetLabel = "Stable Diffusion model",
            RepoId = _sdRepoId,
            FilePath = _sdFilePath,
            Revision = _sdRevision,
            Token = _hfToken,
            DestinationFolderName = StableDiffusionCppSetupUtility.ModelsFolderName,
            ExpectedExtension = ".gguf",
            AutoApplyAfterDownload = _sdAutoApplyAfterDownload,
            ApplyMode = HfDownloadApplyMode.StableDiffusionPreset,
            StableDiffusionPresetIndex = _sdSelectedPresetIndex
        };
    }

    private void LoadStableDiffusionPresetIntoFields(int presetIndex)
    {
        if (!StableDiffusionCppSetupUtility.TryGetPreset(presetIndex, out StableDiffusionDownloadPreset preset))
        {
            return;
        }

        _sdSelectedPresetIndex = presetIndex;
        _sdRepoId = preset.RepoId;
        _sdFilePath = preset.FilePath;
        _sdRevision = preset.Revision;
    }

    private void ApplyExistingStableDiffusionDownload()
    {
        if (!StableDiffusionCppSetupUtility.TryGetPreset(_sdSelectedPresetIndex, out StableDiffusionDownloadPreset preset))
        {
            SetStatus("Select a Stable Diffusion preset first.", MessageType.Warning);
            return;
        }

        string candidatePath = Path.Combine(
            StableDiffusionCppSetupUtility.GetModelDestinationDirectoryAbsolute(),
            Path.GetFileName(NormalizeModelPathForUrl(_sdFilePath)));
        if (!File.Exists(candidatePath))
        {
            SetStatus($"Model file was not found in StreamingAssets:\n{candidatePath}", MessageType.Warning);
            return;
        }

        if (!TryGetStreamingAssetsRelativePath(candidatePath, out string relativeModelPath))
        {
            SetStatus($"Model is not located under StreamingAssets:\n{candidatePath}", MessageType.Error);
            return;
        }

        try
        {
            StableDiffusionCppSetupUtility.ApplyDownloadedPreset(preset, relativeModelPath);
            SetStatus($"Stable Diffusion settings updated to use {relativeModelPath}.", MessageType.Info);
        }
        catch (Exception ex)
        {
            SetStatus($"Failed to apply Stable Diffusion preset: {ex.Message}", MessageType.Error);
        }
    }

    private bool HasAnyActiveTransfer()
    {
        return _hfIsDownloading || _hfIsWatchingBrowserDownload;
    }

    private bool IsTransferActiveForMode(HfDownloadApplyMode applyMode)
    {
        return HasAnyActiveTransfer() &&
               _activeHfDownloadPlan != null &&
               _activeHfDownloadPlan.ApplyMode == applyMode;
    }

    private static string GetDestinationDirectory(HfDownloadPlan plan)
    {
        return Path.Combine(Application.streamingAssetsPath, plan.DestinationFolderName ?? DefaultModelFolderName);
    }

    private static string GetDestinationPath(HfDownloadPlan plan)
    {
        return Path.Combine(GetDestinationDirectory(plan), plan.FileName);
    }

    private async Task WatchBrowserDownloadAndImportAsync(
        string watchDirectory,
        string expectedFileName,
        DateTime startedAtUtc,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        string sourcePath = await WaitForBrowserDownloadCompletionAsync(
            watchDirectory,
            expectedFileName,
            startedAtUtc,
            cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        string sourceFullPath = Path.GetFullPath(sourcePath);
        string destinationFullPath = Path.GetFullPath(destinationPath);
        if (string.Equals(sourceFullPath, destinationFullPath, StringComparison.OrdinalIgnoreCase))
        {
            long bytes = TryGetFileSizeSafe(destinationFullPath);
            SetHfProgress(bytes, bytes > 0 ? bytes : -1, "Model is already in StreamingAssets.");
            return;
        }

        long sourceBytes = TryGetFileSizeSafe(sourceFullPath);
        SetHfProgress(0, sourceBytes > 0 ? sourceBytes : -1, "Importing downloaded model into StreamingAssets...");
        await CopyFileWithProgressAsync(sourceFullPath, destinationFullPath, cancellationToken);
        long destinationBytes = TryGetFileSizeSafe(destinationFullPath);
        SetHfProgress(
            destinationBytes,
            sourceBytes > 0 ? sourceBytes : destinationBytes,
            "Import complete.");
    }

    private async Task<string> WaitForBrowserDownloadCompletionAsync(
        string watchDirectory,
        string expectedFileName,
        DateTime startedAtUtc,
        CancellationToken cancellationToken)
    {
        string lastCandidate = null;
        long lastSize = -1;
        int stableTicks = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string completedCandidate = FindCompletedBrowserModelCandidate(
                watchDirectory,
                expectedFileName,
                startedAtUtc);
            if (!string.IsNullOrWhiteSpace(completedCandidate))
            {
                long currentSize = TryGetFileSizeSafe(completedCandidate);
                SetHfProgress(
                    currentSize,
                    -1,
                    $"Detected '{Path.GetFileName(completedCandidate)}'. Waiting for browser to finalize...");

                bool sameCandidate = string.Equals(completedCandidate, lastCandidate, StringComparison.OrdinalIgnoreCase);
                if (sameCandidate && currentSize > 0 && currentSize == lastSize)
                {
                    stableTicks++;
                }
                else
                {
                    stableTicks = 0;
                }

                lastCandidate = completedCandidate;
                lastSize = currentSize;

                if (stableTicks >= 3 && CanOpenExclusively(completedCandidate))
                {
                    return completedCandidate;
                }
            }
            else
            {
                string tempCandidate = FindBrowserPartialCandidate(watchDirectory, expectedFileName);
                if (!string.IsNullOrWhiteSpace(tempCandidate))
                {
                    long tempBytes = TryGetFileSizeSafe(tempCandidate);
                    SetHfProgress(
                        tempBytes,
                        -1,
                        $"Browser downloading '{Path.GetFileName(tempCandidate)}'...");
                }
                else
                {
                    SetHfProgress(0, -1, $"Waiting for browser download to start in '{watchDirectory}'...");
                }

                lastCandidate = null;
                lastSize = -1;
                stableTicks = 0;
            }

            await Task.Delay(1000, cancellationToken);
        }
    }

    private async Task CopyFileWithProgressAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        if (File.Exists(destinationPath))
        {
            File.Delete(destinationPath);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? Application.streamingAssetsPath);

        using var source = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            HfDownloadBufferSizeBytes,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var destination = new FileStream(
            destinationPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            HfDownloadBufferSizeBytes,
            FileOptions.Asynchronous);

        long totalBytes = source.Length;
        var buffer = new byte[HfDownloadBufferSizeBytes];
        long copied = 0;
        long lastReportedBytes = 0;
        long lastReportMs = 0;
        var progressStopwatch = Stopwatch.StartNew();

        while (true)
        {
            int read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read <= 0)
            {
                break;
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            copied += read;
            long nowMs = progressStopwatch.ElapsedMilliseconds;
            bool dueToBytes = copied - lastReportedBytes >= HfProgressUpdateMinBytes;
            bool dueToTime = nowMs - lastReportMs >= HfProgressUpdateIntervalMs;
            bool finalChunk = totalBytes > 0 && copied >= totalBytes;
            if (dueToBytes || dueToTime || finalChunk)
            {
                SetHfProgress(copied, totalBytes, "Importing downloaded model into StreamingAssets...");
                lastReportedBytes = copied;
                lastReportMs = nowMs;
            }
        }

        await destination.FlushAsync(cancellationToken);
    }

    private void CompleteBrowserAssistedDownload(Task task)
    {
        HfDownloadPlan plan = _activeHfDownloadPlan;
        string destinationPath = plan != null ? GetDestinationPath(plan) : string.Empty;
        _hfIsWatchingBrowserDownload = false;

        if (task.IsCanceled || (_hfBrowserWatchCancellation?.IsCancellationRequested ?? false))
        {
            SetStatus("Browser-assisted import canceled.", MessageType.Warning);
            CleanupBrowserWatchHandles();
            return;
        }

        Exception error = task.Exception?.GetBaseException();
        if (task.IsFaulted || error != null)
        {
            SetStatus($"Browser-assisted import failed: {error?.Message ?? "Unknown error"}", MessageType.Error);
            CleanupBrowserWatchHandles();
            return;
        }

        AssetDatabase.Refresh();
        RefreshState();

        try
        {
            ApplyDownloadedPlan(plan, destinationPath, "Model imported from browser download");
        }
        catch (Exception ex)
        {
            SetStatus($"Browser-assisted import succeeded but post-apply failed: {ex.Message}", MessageType.Error);
        }

        CleanupBrowserWatchHandles();
    }

    private async Task DownloadModelFromHuggingFaceAsync(
        string downloadUrl,
        string destinationPath,
        string token,
        CancellationToken cancellationToken)
    {
        string tempPath = BuildExternalTempPath(destinationPath, downloadUrl);
        string resumeStatePath = BuildParallelResumeStatePath(tempPath);

        try
        {
            (long probedTotalBytes, bool supportsRanges) = await TryProbeHfDownloadMetadataAsync(
                downloadUrl,
                token,
                cancellationToken);

            long downloaded;
            long totalBytes;

            int segmentCount = CalculateParallelSegmentCount(probedTotalBytes);
            bool useParallel =
                supportsRanges &&
                probedTotalBytes >= HfParallelMinSizeBytes &&
                segmentCount > 1;

            if (useParallel)
            {
                SetHfProgress(0, probedTotalBytes, $"Downloading... ({segmentCount} connections)");
                try
                {
                    downloaded = await DownloadHfParallelAsync(
                        downloadUrl,
                        tempPath,
                        token,
                        probedTotalBytes,
                        segmentCount,
                        cancellationToken);
                    totalBytes = probedTotalBytes;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    TryDeleteFileSilently(tempPath);
                    SetHfProgress(0, probedTotalBytes, $"Parallel mode unavailable ({TrimForStatus(ex.Message)}). Retrying in single-connection mode...");
                    (downloaded, totalBytes) = await DownloadHfSingleStreamAsync(
                        downloadUrl,
                        tempPath,
                        token,
                        probedTotalBytes,
                        supportsRanges,
                        cancellationToken);
                }
            }
            else
            {
                (downloaded, totalBytes) = await DownloadHfSingleStreamAsync(
                    downloadUrl,
                    tempPath,
                    token,
                    probedTotalBytes,
                    supportsRanges,
                    cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(destinationPath))
            {
                File.Delete(destinationPath);
            }

            TryMoveFileAcrossVolumes(tempPath, destinationPath);
            TryDeleteFileSilently(resumeStatePath);
            SetHfProgress(downloaded, totalBytes, "Download complete.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Keep temp/resume files for resume on next attempt.
            throw;
        }
    }

    private static int CalculateParallelSegmentCount(long totalBytes)
    {
        if (totalBytes <= 0)
        {
            return 1;
        }

        long byChunk = (totalBytes + HfParallelMinChunkSizeBytes - 1) / HfParallelMinChunkSizeBytes;
        int chunkBasedCount = (int)Math.Max(1L, byChunk);
        return Math.Max(1, Math.Min(HfParallelMaxSegments, chunkBasedCount));
    }

    private async Task<(long totalBytes, bool supportsRanges)> TryProbeHfDownloadMetadataAsync(
        string downloadUrl,
        string token,
        CancellationToken cancellationToken)
    {
        using var request = CreateHfRequest(HttpMethod.Head, downloadUrl, token);
        using HttpResponseMessage response = await HfHttpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.MethodNotAllowed ||
            response.StatusCode == HttpStatusCode.NotImplemented)
        {
            return await TryProbeRangeSupportWithGetAsync(downloadUrl, token, cancellationToken);
        }

        ThrowForCommonHfErrors(response);
        response.EnsureSuccessStatusCode();

        long totalBytes = response.Content.Headers.ContentLength ?? -1;
        bool supportsRanges = response.Headers.AcceptRanges.Any(value =>
            string.Equals(value, "bytes", StringComparison.OrdinalIgnoreCase));
        if (!supportsRanges)
        {
            (long rangeTotalBytes, bool rangeSupported) = await TryProbeRangeSupportWithGetAsync(downloadUrl, token, cancellationToken);
            supportsRanges = rangeSupported;
            if (totalBytes <= 0 && rangeTotalBytes > 0)
            {
                totalBytes = rangeTotalBytes;
            }
        }

        return (totalBytes, supportsRanges);
    }

    private async Task<(long totalBytes, bool supportsRanges)> TryProbeRangeSupportWithGetAsync(
        string downloadUrl,
        string token,
        CancellationToken cancellationToken)
    {
        using var request = CreateHfRequest(HttpMethod.Get, downloadUrl, token);
        request.Headers.Range = new RangeHeaderValue(0, 0);

        using HttpResponseMessage response = await HfHttpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        ThrowForCommonHfErrors(response);
        if (response.StatusCode == HttpStatusCode.PartialContent)
        {
            long totalBytes = response.Content.Headers.ContentRange?.Length ?? -1;
            if (totalBytes <= 0)
            {
                totalBytes = response.Content.Headers.ContentLength ?? -1;
            }

            return (totalBytes, true);
        }

        response.EnsureSuccessStatusCode();
        long contentLength = response.Content.Headers.ContentLength ?? -1;
        return (contentLength, false);
    }

    private async Task<(long downloaded, long totalBytes)> DownloadHfSingleStreamAsync(
        string downloadUrl,
        string tempPath,
        string token,
        long probedTotalBytes,
        bool supportsRanges,
        CancellationToken cancellationToken)
    {
        long existingBytes = TryGetFileSizeSafe(tempPath);
        if (probedTotalBytes > 0 && existingBytes > probedTotalBytes)
        {
            TryDeleteFileSilently(tempPath);
            existingBytes = 0;
        }

        bool resumeAttempt = supportsRanges && existingBytes > 0;
        using var request = CreateHfRequest(HttpMethod.Get, downloadUrl, token);
        if (resumeAttempt)
        {
            request.Headers.Range = new RangeHeaderValue(existingBytes, null);
        }

        using HttpResponseMessage response = await HfHttpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        ThrowForCommonHfErrors(response);
        response.EnsureSuccessStatusCode();

        bool resumed = resumeAttempt && response.StatusCode == HttpStatusCode.PartialContent;
        if (resumeAttempt && !resumed)
        {
            existingBytes = 0;
        }

        long totalBytes = ResolveSingleStreamTotalBytes(response, probedTotalBytes, existingBytes, resumed);
        if (totalBytes > 0 && existingBytes > totalBytes)
        {
            existingBytes = 0;
        }

        SetHfProgress(
            existingBytes,
            totalBytes,
            resumed ? "Resuming download..." : "Downloading...");

        using Stream source = await response.Content.ReadAsStreamAsync();
        using var destination = new FileStream(
            tempPath,
            FileMode.OpenOrCreate,
            FileAccess.Write,
            FileShare.None,
            HfDownloadBufferSizeBytes,
            FileOptions.Asynchronous);

        if (existingBytes == 0)
        {
            if (totalBytes > 0)
            {
                destination.SetLength(totalBytes);
            }
            else
            {
                destination.SetLength(0);
            }
            destination.Position = 0;
        }
        else
        {
            if (totalBytes > 0 && destination.Length != totalBytes)
            {
                destination.SetLength(totalBytes);
            }
            destination.Position = Math.Min(existingBytes, destination.Length);
        }

        var buffer = new byte[HfDownloadBufferSizeBytes];
        long downloaded = existingBytes;
        long lastReportedBytes = existingBytes;
        var progressStopwatch = Stopwatch.StartNew();
        long lastReportMs = 0;
        while (true)
        {
            int read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read <= 0)
            {
                break;
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            downloaded += read;
            long nowMs = progressStopwatch.ElapsedMilliseconds;
            bool dueToBytes = downloaded - lastReportedBytes >= HfProgressUpdateMinBytes;
            bool dueToTime = nowMs - lastReportMs >= HfProgressUpdateIntervalMs;
            bool finalChunk = totalBytes > 0 && downloaded >= totalBytes;
            if (dueToBytes || dueToTime || finalChunk)
            {
                SetHfProgress(downloaded, totalBytes, "Downloading...");
                lastReportedBytes = downloaded;
                lastReportMs = nowMs;
            }
        }

        await destination.FlushAsync(cancellationToken);
        return (downloaded, totalBytes);
    }

    private async Task<long> DownloadHfParallelAsync(
        string downloadUrl,
        string tempPath,
        string token,
        long totalBytes,
        int segmentCount,
        CancellationToken cancellationToken)
    {
        string resumeStatePath = BuildParallelResumeStatePath(tempPath);
        string resumeKey = BuildParallelResumeKey(downloadUrl, totalBytes, segmentCount);
        bool[] completedSegments = LoadParallelResumeState(
            resumeStatePath,
            resumeKey,
            totalBytes,
            segmentCount);

        using (var initializer = new FileStream(
                   tempPath,
                   FileMode.OpenOrCreate,
                   FileAccess.Write,
                   FileShare.ReadWrite,
                   4096,
                   FileOptions.Asynchronous))
        {
            if (initializer.Length != totalBytes)
            {
                initializer.SetLength(totalBytes);
                completedSegments = new bool[segmentCount];
                SaveParallelResumeState(resumeStatePath, resumeKey, totalBytes, completedSegments);
            }
            await initializer.FlushAsync(cancellationToken);
        }

        long downloaded = 0;
        long[] segmentProgressBytes = new long[segmentCount];
        long lastReportMs = 0;
        long lastReportedBytes = 0;
        var progressStopwatch = Stopwatch.StartNew();
        object stateLock = new object();
        long segmentSize = (totalBytes + segmentCount - 1) / segmentCount;
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var tasks = new List<Task>(segmentCount);

        for (int i = 0; i < segmentCount; i++)
        {
            if (!completedSegments[i])
            {
                continue;
            }

            long completedSegmentBytes = GetSegmentLength(i, segmentSize, totalBytes);
            segmentProgressBytes[i] = completedSegmentBytes;
            downloaded += completedSegmentBytes;
        }

        lastReportedBytes = downloaded;
        SetHfProgress(downloaded, totalBytes, $"Downloading... ({segmentCount} connections)");

        void ReportSegmentBytes(int segmentIndex, int bytes)
        {
            lock (stateLock)
            {
                segmentProgressBytes[segmentIndex] += bytes;
                downloaded += bytes;

                long nowMs = progressStopwatch.ElapsedMilliseconds;
                bool dueToBytes = downloaded - lastReportedBytes >= HfProgressUpdateMinBytes;
                bool dueToTime = nowMs - lastReportMs >= HfProgressUpdateIntervalMs;
                if (dueToBytes || dueToTime)
                {
                    SetHfProgress(downloaded, totalBytes, $"Downloading... ({segmentCount} connections)");
                    lastReportedBytes = downloaded;
                    lastReportMs = nowMs;
                }
            }
        }

        void RollbackSegmentProgress(int segmentIndex)
        {
            lock (stateLock)
            {
                long rollbackBytes = segmentProgressBytes[segmentIndex];
                if (rollbackBytes <= 0)
                {
                    return;
                }

                downloaded = Math.Max(0, downloaded - rollbackBytes);
                segmentProgressBytes[segmentIndex] = 0;
                lastReportedBytes = downloaded;
                lastReportMs = progressStopwatch.ElapsedMilliseconds;
                SetHfProgress(downloaded, totalBytes, $"Retrying segment {segmentIndex + 1}/{segmentCount}...");
            }
        }

        async Task DownloadSegmentWithRetryAsync(int segmentIndex, long rangeStart, long rangeEnd)
        {
            for (int attempt = 1; attempt <= HfSegmentRetryMaxAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await DownloadHfRangeSegmentAsync(
                        downloadUrl,
                        tempPath,
                        token,
                        rangeStart,
                        rangeEnd,
                        bytes => ReportSegmentBytes(segmentIndex, bytes),
                        linkedCts.Token);

                    lock (stateLock)
                    {
                        completedSegments[segmentIndex] = true;
                        SaveParallelResumeState(resumeStatePath, resumeKey, totalBytes, completedSegments);
                    }
                    return;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex) when (attempt < HfSegmentRetryMaxAttempts)
                {
                    RollbackSegmentProgress(segmentIndex);
                    int delayMs = Math.Min(HfSegmentRetryMaxDelayMs, HfSegmentRetryBaseDelayMs * (1 << (attempt - 1)));
                    SetHfProgress(
                        downloaded,
                        totalBytes,
                        $"Segment {segmentIndex + 1}/{segmentCount} failed ({TrimForStatus(ex.Message)}). Retrying {attempt}/{HfSegmentRetryMaxAttempts - 1}...");
                    await Task.Delay(delayMs, linkedCts.Token);
                }
                catch
                {
                    RollbackSegmentProgress(segmentIndex);
                    throw;
                }
            }
        }

        for (int i = 0; i < segmentCount; i++)
        {
            if (completedSegments[i])
            {
                continue;
            }

            long rangeStart = i * segmentSize;
            if (rangeStart >= totalBytes)
            {
                break;
            }

            long rangeEnd = Math.Min(totalBytes - 1, rangeStart + segmentSize - 1);
            int segmentIndex = i;
            tasks.Add(DownloadSegmentWithRetryAsync(segmentIndex, rangeStart, rangeEnd));
        }

        if (tasks.Count == 0)
        {
            TryDeleteFileSilently(resumeStatePath);
            SetHfProgress(downloaded, totalBytes, $"Downloading... ({segmentCount} connections)");
            return downloaded;
        }

        try
        {
            await Task.WhenAll(tasks);
        }
        catch
        {
            linkedCts.Cancel();
            throw;
        }

        TryDeleteFileSilently(resumeStatePath);
        SetHfProgress(downloaded, totalBytes, $"Downloading... ({segmentCount} connections)");
        return downloaded;
    }

    private async Task DownloadHfRangeSegmentAsync(
        string downloadUrl,
        string tempPath,
        string token,
        long rangeStart,
        long rangeEnd,
        Action<int> reportBytes,
        CancellationToken cancellationToken)
    {
        using var request = CreateHfRequest(HttpMethod.Get, downloadUrl, token);
        request.Headers.Range = new RangeHeaderValue(rangeStart, rangeEnd);

        using HttpResponseMessage response = await HfHttpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        ThrowForCommonHfErrors(response);
        if (response.StatusCode != HttpStatusCode.PartialContent)
        {
            throw new InvalidOperationException("Server did not honor range requests for parallel download.");
        }

        using Stream source = await response.Content.ReadAsStreamAsync();
        using var destination = new FileStream(
            tempPath,
            FileMode.Open,
            FileAccess.Write,
            FileShare.ReadWrite,
            HfDownloadBufferSizeBytes,
            FileOptions.Asynchronous);
        destination.Seek(rangeStart, SeekOrigin.Begin);

        long remaining = rangeEnd - rangeStart + 1;
        var buffer = new byte[HfDownloadBufferSizeBytes];
        while (remaining > 0)
        {
            int toRead = (int)Math.Min(buffer.Length, remaining);
            int read = await source.ReadAsync(buffer.AsMemory(0, toRead), cancellationToken);
            if (read <= 0)
            {
                break;
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            remaining -= read;
            reportBytes(read);
        }

        if (remaining > 0)
        {
            throw new InvalidOperationException("Parallel download ended before the requested byte range was fully received.");
        }
    }

    private static HttpRequestMessage CreateHfRequest(HttpMethod method, string downloadUrl, string token)
    {
        var request = new HttpRequestMessage(method, downloadUrl);
        request.Headers.UserAgent.ParseAdd("AIFighterGame-LLMSetupWizard/1.0");
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return request;
    }

    private static void ThrowForCommonHfErrors(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
        {
            throw new InvalidOperationException("Authorization failed. Check Hugging Face access token or model access permission.");
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException("Model file not found. Verify repo ID, revision, and file path.");
        }
    }

    private static string TrimForStatus(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "unknown error";
        }

        string singleLine = message
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Trim();
        if (singleLine.Length <= 120)
        {
            return singleLine;
        }

        return singleLine.Substring(0, 120) + "...";
    }

    private static string ResolveDefaultDownloadsFolder()
    {
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(userProfile))
        {
            userProfile = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }

        if (string.IsNullOrWhiteSpace(userProfile))
        {
            return Application.dataPath;
        }

        return Path.Combine(userProfile, "Downloads");
    }

    private static string FindCompletedBrowserModelCandidate(
        string watchDirectory,
        string expectedFileName,
        DateTime startedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(watchDirectory) || !Directory.Exists(watchDirectory))
        {
            return null;
        }

        string expectedPath = Path.Combine(watchDirectory, expectedFileName);
        if (File.Exists(expectedPath) && File.GetLastWriteTimeUtc(expectedPath) >= startedAtUtc.AddSeconds(-2))
        {
            return expectedPath;
        }

        string expectedBaseName = Path.GetFileNameWithoutExtension(expectedFileName);
        string extension = Path.GetExtension(expectedFileName);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".gguf";
        }

        string[] files = Directory.GetFiles(watchDirectory, "*" + extension, SearchOption.TopDirectoryOnly);
        if (files.Length == 0)
        {
            return null;
        }

        string namedCandidate = files
            .Where(path => Path.GetFileNameWithoutExtension(path).StartsWith(expectedBaseName, StringComparison.OrdinalIgnoreCase))
            .Where(path => File.GetLastWriteTimeUtc(path) >= startedAtUtc.AddSeconds(-2))
            .OrderByDescending(path => File.GetLastWriteTimeUtc(path))
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(namedCandidate))
        {
            return namedCandidate;
        }

        return files
            .Where(path => File.GetLastWriteTimeUtc(path) >= startedAtUtc.AddSeconds(-2))
            .OrderByDescending(path => File.GetLastWriteTimeUtc(path))
            .FirstOrDefault();
    }

    private static string FindBrowserPartialCandidate(string watchDirectory, string expectedFileName)
    {
        if (string.IsNullOrWhiteSpace(watchDirectory) || !Directory.Exists(watchDirectory))
        {
            return null;
        }

        string[] candidates = Directory.GetFiles(
            watchDirectory,
            expectedFileName + "*",
            SearchOption.TopDirectoryOnly);
        return candidates
            .Where(path => IsTemporaryBrowserDownloadFile(Path.GetFileName(path)))
            .OrderByDescending(path => File.GetLastWriteTimeUtc(path))
            .FirstOrDefault();
    }

    private static bool IsTemporaryBrowserDownloadFile(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        string lowered = fileName.ToLowerInvariant();
        return lowered.EndsWith(".crdownload", StringComparison.Ordinal) ||
               lowered.EndsWith(".part", StringComparison.Ordinal) ||
               lowered.EndsWith(".download", StringComparison.Ordinal) ||
               lowered.EndsWith(".tmp", StringComparison.Ordinal);
    }

    private static bool CanOpenExclusively(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return stream.Length >= 0;
        }
        catch
        {
            return false;
        }
    }

    private static long TryGetFileSizeSafe(string path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                return new FileInfo(path).Length;
            }
        }
        catch
        {
            // Ignore file stat failures.
        }

        return 0;
    }

    private void CompleteHuggingFaceDownload(Task task)
    {
        HfDownloadPlan plan = _activeHfDownloadPlan;
        string destinationPath = plan != null ? GetDestinationPath(plan) : string.Empty;
        _hfIsDownloading = false;

        if (task.IsCanceled || (_hfDownloadCancellation?.IsCancellationRequested ?? false))
        {
            SetHfProgress(_hfDownloadedBytes, _hfTotalBytes, "Download canceled.");
            SetStatus("Hugging Face download canceled. Next Download In Editor will resume.", MessageType.Warning);
            CleanupHfDownloadHandles();
            return;
        }

        Exception error = task.Exception?.GetBaseException();
        if (task.IsFaulted || error != null)
        {
            SetStatus($"Hugging Face download failed: {error?.Message ?? "Unknown error"}", MessageType.Error);
            CleanupHfDownloadHandles();
            return;
        }

        AssetDatabase.Refresh();
        RefreshState();

        try
        {
            ApplyDownloadedPlan(plan, destinationPath, "Model downloaded");
        }
        catch (Exception ex)
        {
            SetStatus($"Download succeeded but post-apply failed: {ex.Message}", MessageType.Error);
        }

        CleanupHfDownloadHandles();
    }

    private void ApplyDownloadedPlan(HfDownloadPlan plan, string destinationPath, string defaultSuccessPrefix)
    {
        if (plan == null)
        {
            SetStatus($"{defaultSuccessPrefix}: {destinationPath}", MessageType.Info);
            return;
        }

        TryGetStreamingAssetsRelativePath(destinationPath, out string relativeModelPath);

        switch (plan.ApplyMode)
        {
            case HfDownloadApplyMode.LlmProfiles:
            {
                if (!string.IsNullOrWhiteSpace(relativeModelPath))
                {
                    _selectedModelIndex = _modelChoices.FindIndex(path =>
                        string.Equals(path, relativeModelPath, StringComparison.OrdinalIgnoreCase));
                }

                if (plan.AutoApplyAfterDownload && !string.IsNullOrWhiteSpace(relativeModelPath))
                {
                    ApplyModelToAllProfiles(relativeModelPath);
                }
                else
                {
                    SetStatus($"{defaultSuccessPrefix}: {destinationPath}", MessageType.Info);
                }

                break;
            }
            case HfDownloadApplyMode.StableDiffusionPreset:
            {
                if (string.IsNullOrWhiteSpace(relativeModelPath))
                {
                    SetStatus($"{defaultSuccessPrefix}: {destinationPath}", MessageType.Info);
                    break;
                }

                if (plan.AutoApplyAfterDownload)
                {
                    StableDiffusionCppSetupUtility.ApplyDownloadedPreset(
                        plan.StableDiffusionPresetIndex,
                        relativeModelPath);
                    SetStatus($"Stable Diffusion model ready: {relativeModelPath}", MessageType.Info);
                }
                else
                {
                    SetStatus($"{defaultSuccessPrefix}: {destinationPath}", MessageType.Info);
                }

                break;
            }
            default:
                SetStatus($"{defaultSuccessPrefix}: {destinationPath}", MessageType.Info);
                break;
        }
    }

    private void CleanupHfDownloadHandles()
    {
        _hfDownloadTask = null;
        _hfDownloadCancellation?.Dispose();
        _hfDownloadCancellation = null;
        _activeHfDownloadPlan = null;
    }

    private void CleanupBrowserWatchHandles()
    {
        _hfBrowserWatchTask = null;
        _hfBrowserWatchCancellation?.Dispose();
        _hfBrowserWatchCancellation = null;
        _activeHfDownloadPlan = null;
    }

    private void CancelActiveHfOperation()
    {
        if (_hfIsDownloading)
        {
            _hfDownloadCancellation?.Cancel();
        }

        if (_hfIsWatchingBrowserDownload)
        {
            _hfBrowserWatchCancellation?.Cancel();
        }
    }

    private void SetHfProgress(long downloadedBytes, long totalBytes, string status)
    {
        lock (_hfProgressLock)
        {
            _hfDownloadedBytes = Math.Max(0, downloadedBytes);
            _hfTotalBytes = totalBytes;
            if (!string.IsNullOrWhiteSpace(status))
            {
                _hfDownloadStatus = status;
            }
        }
    }

    private void GetHfProgressSnapshot(out long downloadedBytes, out long totalBytes, out string status)
    {
        lock (_hfProgressLock)
        {
            downloadedBytes = _hfDownloadedBytes;
            totalBytes = _hfTotalBytes;
            status = _hfDownloadStatus;
        }
    }

    private void ApplyBackendConfiguration()
    {
        var pluginPaths = DiscoverCandidatePluginAssetPaths();
        if (pluginPaths.Count == 0)
        {
            SetStatus("No candidate native plugins were found.", MessageType.Warning);
            return;
        }

        bool preferProjectPlugins = pluginPaths.Any(IsInProjectPluginFolder);
        int changedCount = 0;

        foreach (string assetPath in pluginPaths)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as PluginImporter;
            if (importer == null)
            {
                continue;
            }

            bool enabled = ShouldEnablePlugin(assetPath, _backendMode, preferProjectPlugins);
            if (ApplyPluginCompatibility(importer, enabled))
            {
                changedCount++;
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        SetStatus($"Backend applied: {_backendMode} (changed {changedCount} plugin import settings).", MessageType.Info);
    }

    private static bool ApplyPluginCompatibility(PluginImporter importer, bool enabled)
    {
        bool changed = false;

        if (importer.GetCompatibleWithAnyPlatform())
        {
            importer.SetCompatibleWithAnyPlatform(false);
            changed = true;
        }

        if (importer.GetCompatibleWithEditor() != enabled)
        {
            importer.SetCompatibleWithEditor(enabled);
            changed = true;
        }

        foreach (BuildTarget target in StandaloneTargets)
        {
            bool current = importer.GetCompatibleWithPlatform(target);
            if (current != enabled)
            {
                importer.SetCompatibleWithPlatform(target, enabled);
                changed = true;
            }
        }

        if (changed)
        {
            importer.SaveAndReimport();
        }

        return changed;
    }

    private static bool ShouldEnablePlugin(string assetPath, BackendMode mode, bool preferProjectPlugins)
    {
        string path = NormalizePath(assetPath);
        string fileName = Path.GetFileName(path);
        bool hasPackageBackend = TryGetBackendModeFromPath(path, out BackendMode packageBackend);

        if (preferProjectPlugins && IsInBackendPackageFolder(path))
        {
            return false;
        }

        if (hasPackageBackend && NormalizeModeForBackendPackage(packageBackend) != NormalizeModeForBackendPackage(mode))
        {
            return false;
        }

        if (path.IndexOf("LLamaSharp.Backend.Cpu", StringComparison.OrdinalIgnoreCase) >= 0 &&
            IsCpuVariantPath(path) &&
            path.IndexOf("/native/avx2/", StringComparison.OrdinalIgnoreCase) < 0)
        {
            return false;
        }

        if (CoreNativeNames.Contains(fileName))
        {
            return true;
        }

        if (CudaNativeNames.Contains(fileName))
        {
            return mode == BackendMode.Cuda12;
        }

        if (VulkanNativeNames.Contains(fileName))
        {
            return mode == BackendMode.Vulkan;
        }

        return false;
    }

    private void ImportModelToStreamingAssets()
    {
        string sourcePath = EditorUtility.OpenFilePanel("Import GGUF model", string.Empty, "gguf");
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return;
        }

        try
        {
            string destinationDirectory = Path.Combine(Application.streamingAssetsPath, DefaultModelFolderName);
            Directory.CreateDirectory(destinationDirectory);

            string destinationPath = Path.Combine(destinationDirectory, Path.GetFileName(sourcePath));
            string sourceFullPath = Path.GetFullPath(sourcePath);
            string destinationFullPath = Path.GetFullPath(destinationPath);

            bool sameFile = string.Equals(sourceFullPath, destinationFullPath, StringComparison.OrdinalIgnoreCase);
            if (!sameFile && File.Exists(destinationFullPath))
            {
                bool replace = EditorUtility.DisplayDialog(
                    "Replace Existing Model",
                    $"A model with the same name already exists:\n{destinationFullPath}\n\nReplace it?",
                    "Replace",
                    "Cancel");

                if (!replace)
                {
                    return;
                }
            }

            if (!sameFile)
            {
                File.Copy(sourceFullPath, destinationFullPath, true);
            }

            AssetDatabase.Refresh();
            RefreshState();
            SetStatus($"Model imported: {destinationFullPath}", MessageType.Info);
        }
        catch (Exception ex)
        {
            SetStatus($"Failed to import model: {ex.Message}", MessageType.Error);
        }
    }

    private void ApplyModelToAllProfiles()
    {
        if (_selectedModelIndex < 0 || _selectedModelIndex >= _modelChoices.Count)
        {
            SetStatus("Select a model from StreamingAssets first.", MessageType.Warning);
            return;
        }

        ApplyModelToAllProfiles(_modelChoices[_selectedModelIndex]);
    }

    private void ApplyModelToAllProfiles(string modelPath)
    {
        if (string.IsNullOrWhiteSpace(modelPath))
        {
            SetStatus("Model path is empty.", MessageType.Warning);
            return;
        }

        string[] profileGuids = AssetDatabase.FindAssets("t:LlmGenerationProfile");
        if (profileGuids.Length == 0)
        {
            SetStatus("No LlmGenerationProfile assets found.", MessageType.Warning);
            return;
        }

        int updatedCount = 0;
        foreach (string guid in profileGuids)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            var profile = AssetDatabase.LoadAssetAtPath<LlmGenerationProfile>(assetPath);
            if (profile == null)
            {
                continue;
            }

            var so = new SerializedObject(profile);
            so.Update();

            var modelProp = so.FindProperty("model");
            var runtimeProp = so.FindProperty("runtimeParams");
            var relativeProp = runtimeProp?.FindPropertyRelative("modelPathRelativeToStreamingAssets");

            bool dirty = false;
            if (modelProp != null && !string.Equals(modelProp.stringValue, modelPath, StringComparison.Ordinal))
            {
                modelProp.stringValue = modelPath;
                dirty = true;
            }

            if (relativeProp != null && relativeProp.boolValue != _forceRelativeToStreamingAssets)
            {
                relativeProp.boolValue = _forceRelativeToStreamingAssets;
                dirty = true;
            }

            if (!dirty)
            {
                continue;
            }

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(profile);
            LlmSettingsChangeNotifier.RaiseChanged(profile);
            updatedCount++;
        }

        if (updatedCount > 0)
        {
            AssetDatabase.SaveAssets();
        }

        SetStatus($"Applied model to {updatedCount} profile(s).", MessageType.Info);
    }

    private void RunQuickValidation()
    {
        var pluginPaths = DiscoverCandidatePluginAssetPaths();
        var enabled = new List<string>();

        foreach (string assetPath in pluginPaths)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as PluginImporter;
            if (importer != null && importer.GetCompatibleWithEditor())
            {
                enabled.Add(assetPath);
            }
        }

        var duplicateNames = enabled
            .Select(Path.GetFileName)
            .GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        int profileCount = AssetDatabase.FindAssets("t:LlmGenerationProfile").Length;
        int modelCount = FindStreamingAssetModels().Count;

        if (duplicateNames.Count > 0)
        {
            SetStatus(
                $"Validation failed: duplicate enabled native plugin names detected ({string.Join(", ", duplicateNames)}).",
                MessageType.Error);
            return;
        }

        if (profileCount == 0)
        {
            SetStatus("Validation warning: no LlmGenerationProfile assets found.", MessageType.Warning);
            return;
        }

        if (modelCount == 0)
        {
            SetStatus("Validation warning: no GGUF model found in StreamingAssets.", MessageType.Warning);
            return;
        }

        SetStatus($"Validation passed. Profiles={profileCount}, Models={modelCount}, EnabledPlugins={enabled.Count}", MessageType.Info);
    }

    private static BackendMode DetectBackendMode()
    {
        var pluginPaths = DiscoverCandidatePluginAssetPaths();
        foreach (string assetPath in pluginPaths)
        {
            string fileName = Path.GetFileName(assetPath);
            if (!fileName.Equals("ggml-cuda.dll", StringComparison.OrdinalIgnoreCase) &&
                !fileName.Equals("ggml-vulkan.dll", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var importer = AssetImporter.GetAtPath(assetPath) as PluginImporter;
            if (importer != null && importer.GetCompatibleWithEditor())
            {
                return fileName.Equals("ggml-vulkan.dll", StringComparison.OrdinalIgnoreCase)
                    ? BackendMode.Vulkan
                    : BackendMode.Cuda12;
            }
        }

        if (Application.platform == RuntimePlatform.OSXEditor)
        {
            return BackendMode.Metal;
        }

        return BackendMode.Cpu;
    }

    private static BackendMode RecommendBackendMode()
    {
        if (Application.platform == RuntimePlatform.OSXEditor)
        {
            return BackendMode.Metal;
        }

        string vendor = (SystemInfo.graphicsDeviceVendor ?? string.Empty).ToLowerInvariant();
        if (Application.platform == RuntimePlatform.WindowsEditor && vendor.Contains("nvidia"))
        {
            return BackendMode.Cuda12;
        }

        if (Application.platform == RuntimePlatform.WindowsEditor ||
            Application.platform == RuntimePlatform.LinuxEditor)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Vulkan)
            {
                return BackendMode.Vulkan;
            }
        }

        return BackendMode.Cpu;
    }

    private static string BuildEnvironmentSummary()
    {
        return $"Platform={Application.platform}, GPU={SystemInfo.graphicsDeviceName}, Vendor={SystemInfo.graphicsDeviceVendor}, API={SystemInfo.graphicsDeviceType}";
    }

    private static bool TryGetBackendModeFromPath(string assetPath, out BackendMode mode)
    {
        string path = NormalizePath(assetPath);
        if (path.IndexOf("LLamaSharp.Backend.Cuda12", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            mode = BackendMode.Cuda12;
            return true;
        }

        if (path.IndexOf("LLamaSharp.Backend.Vulkan", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            mode = BackendMode.Vulkan;
            return true;
        }

        if (path.IndexOf("LLamaSharp.Backend.Cpu", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            mode = BackendMode.Cpu;
            return true;
        }

        mode = BackendMode.Cpu;
        return false;
    }

    private static BackendMode NormalizeModeForBackendPackage(BackendMode mode)
    {
        return mode == BackendMode.Metal ? BackendMode.Cpu : mode;
    }

    private void InstallOrUpdateDependenciesForSelectedBackend()
    {
        bool hasNuGetForUnity = IsNuGetForUnityInstalled();
        bool startedNuGetForUnityInstall = false;
        if (!hasNuGetForUnity)
        {
            startedNuGetForUnityInstall = TryInstallNuGetForUnity();
        }

        if (!EnsureNuGetPackagesForSelectedBackend(_backendMode, out bool nugetChanged, out string nugetMessage))
        {
            SetStatus(nugetMessage, MessageType.Error);
            return;
        }

        bool upmChanged = EnsureNewtonsoftUpmDependency();
        AssetDatabase.Refresh();

        string summary = $"{nugetMessage} UPM Newtonsoft: {(upmChanged ? "added" : "already present")}.";
        if (!hasNuGetForUnity)
        {
            string nugetStatus = startedNuGetForUnityInstall
                ? "NuGetForUnity installation started via UPM."
                : "NuGetForUnity installation is still in progress.";
            SetStatus(
                $"{summary} {nugetStatus} After Unity finishes package import and script compilation, run this action again to restore NuGet packages.",
                MessageType.Warning);
            return;
        }

        if (!RunNuGetRestore())
        {
            SetStatus(
                $"{summary} Could not auto-run NuGet restore menu. Use 'Open NuGet Manager' and run Restore manually.",
                MessageType.Warning);
        }
        else if (nugetChanged || upmChanged)
        {
            SetStatus($"{summary} NuGet restore triggered.", MessageType.Info);
        }
    }

    private void HandleUpmAddRequest(ref AddRequest request, string packageId)
    {
        if (request == null || !request.IsCompleted)
        {
            return;
        }

        if (request.Status == UnityEditor.PackageManager.StatusCode.Success)
        {
            SetStatus($"UPM dependency installed: {packageId}", MessageType.Info);
        }
        else if (request.Status >= UnityEditor.PackageManager.StatusCode.Failure)
        {
            string error = request.Error?.message ?? "Unknown UPM error.";
            SetStatus($"Failed to install UPM dependency ({packageId}): {error}", MessageType.Error);
        }

        request = null;
    }

    private void InstallNuGetForUnity()
    {
        if (IsNuGetForUnityInstalled())
        {
            SetStatus("NuGetForUnity is already installed.", MessageType.Info);
            return;
        }

        if (TryInstallNuGetForUnity())
        {
            SetStatus("Started NuGetForUnity installation via UPM. Wait for import/compile, then run NuGet Restore.", MessageType.Info);
        }
        else
        {
            SetStatus("NuGetForUnity installation is already in progress.", MessageType.Info);
        }
    }

    private bool TryInstallNuGetForUnity()
    {
        if (_nugetForUnityAddRequest != null && !_nugetForUnityAddRequest.IsCompleted)
        {
            return false;
        }

        _nugetForUnityAddRequest = Client.Add(NuGetForUnityUpmGitUrl);
        return true;
    }

    private static bool IsNuGetForUnityInstalled()
    {
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string manifestPath = Path.Combine(projectRoot, "Packages", "manifest.json");

        try
        {
            if (File.Exists(manifestPath))
            {
                string manifestText = File.ReadAllText(manifestPath);
                if (manifestText.IndexOf($"\"{NuGetForUnityUpmPackage}\"", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
        }
        catch (Exception)
        {
            // Ignore manifest read failures and fall through to package folder check.
        }

        string packageFolder = Path.Combine(projectRoot, "Packages", NuGetForUnityUpmPackage);
        return Directory.Exists(packageFolder);
    }

    private static bool EnsureNuGetPackagesForSelectedBackend(BackendMode mode, out bool changed, out string message)
    {
        changed = false;

        string packagesConfigPath = Path.Combine(Application.dataPath, "packages.config");
        XDocument document;
        XElement root;

        try
        {
            if (File.Exists(packagesConfigPath))
            {
                document = XDocument.Load(packagesConfigPath, LoadOptions.PreserveWhitespace);
                root = document.Root;
            }
            else
            {
                root = new XElement("packages");
                document = new XDocument(new XDeclaration("1.0", "utf-8", null), root);
                changed = true;
            }
        }
        catch (Exception ex)
        {
            message = $"Failed to read packages.config: {ex.Message}";
            return false;
        }

        if (root == null || !string.Equals(root.Name.LocalName, "packages", StringComparison.OrdinalIgnoreCase))
        {
            message = "Invalid packages.config format.";
            return false;
        }

        string requiredBackendId = GetBackendPackageIdForMode(mode);
        if (string.IsNullOrWhiteSpace(requiredBackendId))
        {
            message = $"Unsupported backend mode: {mode}";
            return false;
        }

        foreach (var package in root.Elements("package").ToList())
        {
            string id = package.Attribute("id")?.Value;
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            if (id.StartsWith("LLamaSharp.Backend.", StringComparison.OrdinalIgnoreCase) &&
                !id.Equals(requiredBackendId, StringComparison.OrdinalIgnoreCase))
            {
                package.Remove();
                changed = true;
            }
        }

        EnsureOrUpdateNuGetEntry(root, "LLamaSharp", NuGetLlamaVersion, ref changed);
        EnsureOrUpdateNuGetEntry(root, requiredBackendId, NuGetLlamaVersion, ref changed);

        foreach (var kv in RequiredNuGetPackageVersions)
        {
            EnsureOrUpdateNuGetEntry(root, kv.Key, kv.Value, ref changed);
        }

        if (changed)
        {
            try
            {
                document.Save(packagesConfigPath);
            }
            catch (Exception ex)
            {
                message = $"Failed to write packages.config: {ex.Message}";
                return false;
            }
        }

        message = changed
            ? $"Updated packages.config for backend '{mode}' ({requiredBackendId})."
            : $"packages.config already satisfies backend '{mode}' ({requiredBackendId}).";
        return true;
    }

    private static void EnsureOrUpdateNuGetEntry(XElement root, string packageId, string version, ref bool changed)
    {
        XElement existing = root
            .Elements("package")
            .FirstOrDefault(element => string.Equals(
                element.Attribute("id")?.Value,
                packageId,
                StringComparison.OrdinalIgnoreCase));

        if (existing == null)
        {
            root.Add(new XElement(
                "package",
                new XAttribute("id", packageId),
                new XAttribute("version", version)));
            changed = true;
            return;
        }

        XAttribute versionAttribute = existing.Attribute("version");
        if (versionAttribute == null || !string.Equals(versionAttribute.Value, version, StringComparison.OrdinalIgnoreCase))
        {
            existing.SetAttributeValue("version", version);
            changed = true;
        }
    }

    private static string GetBackendPackageIdForMode(BackendMode mode)
    {
        switch (NormalizeModeForBackendPackage(mode))
        {
            case BackendMode.Cpu:
                return "LLamaSharp.Backend.Cpu";
            case BackendMode.Cuda12:
                return Application.platform == RuntimePlatform.WindowsEditor
                    ? "LLamaSharp.Backend.Cuda12.Windows"
                    : "LLamaSharp.Backend.Cuda12";
            case BackendMode.Vulkan:
                return "LLamaSharp.Backend.Vulkan";
            default:
                return null;
        }
    }

    private bool EnsureNewtonsoftUpmDependency()
    {
        if (_newtonsoftAddRequest != null && !_newtonsoftAddRequest.IsCompleted)
        {
            return false;
        }

        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string manifestPath = Path.Combine(projectRoot, "Packages", "manifest.json");
        if (!File.Exists(manifestPath))
        {
            return false;
        }

        string manifestText = File.ReadAllText(manifestPath);
        if (manifestText.IndexOf($"\"{NewtonsoftUpmPackage}\"", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return false;
        }

        _newtonsoftAddRequest = Client.Add($"{NewtonsoftUpmPackage}@{NewtonsoftUpmVersion}");
        return true;
    }

    private bool RunNuGetRestore()
    {
        return ExecuteFirstMenuItem(NuGetRestoreMenuCandidates);
    }

    private void OpenNuGetManager()
    {
        if (!ExecuteFirstMenuItem(NuGetManageMenuCandidates))
        {
            SetStatus("NuGet Manager menu was not found. Ensure NuGetForUnity is installed.", MessageType.Warning);
        }
    }

    private static bool ExecuteFirstMenuItem(IEnumerable<string> menuCandidates)
    {
        foreach (string menu in menuCandidates)
        {
            if (EditorApplication.ExecuteMenuItem(menu))
            {
                return true;
            }
        }

        return false;
    }

    private static List<string> FindStreamingAssetModels()
    {
        if (string.IsNullOrWhiteSpace(Application.streamingAssetsPath) || !Directory.Exists(Application.streamingAssetsPath))
        {
            return new List<string>();
        }

        return Directory
            .GetFiles(Application.streamingAssetsPath, "*.gguf", SearchOption.AllDirectories)
            .Select(path =>
            {
                TryGetStreamingAssetsRelativePath(path, out string relativePath);
                return relativePath;
            })
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool TryGetStreamingAssetsRelativePath(string absolutePath, out string relativePath)
    {
        relativePath = null;
        if (string.IsNullOrWhiteSpace(absolutePath) || string.IsNullOrWhiteSpace(Application.streamingAssetsPath))
        {
            return false;
        }

        string streamingRoot = Path.GetFullPath(Application.streamingAssetsPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string candidatePath = Path.GetFullPath(absolutePath);

        if (!candidatePath.StartsWith(streamingRoot, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string trimmed = candidatePath.Substring(streamingRoot.Length)
            .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        relativePath = NormalizePath(trimmed);
        return !string.IsNullOrWhiteSpace(relativePath);
    }

    private static List<string> DiscoverCandidatePluginAssetPaths()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddDllCandidatesFromFolder(Path.Combine(Application.dataPath, "Plugins"), result);
        AddDllCandidatesFromFolder(Path.Combine(Application.dataPath, "Packages"), result);

        return result.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void AddDllCandidatesFromFolder(string absoluteFolderPath, HashSet<string> output)
    {
        if (string.IsNullOrWhiteSpace(absoluteFolderPath) || !Directory.Exists(absoluteFolderPath))
        {
            return;
        }

        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string[] dllPaths = Directory.GetFiles(absoluteFolderPath, "*.dll", SearchOption.AllDirectories);
        foreach (string dllPath in dllPaths)
        {
            string fileName = Path.GetFileName(dllPath);
            bool isKnownName =
                CoreNativeNames.Contains(fileName) ||
                CudaNativeNames.Contains(fileName) ||
                VulkanNativeNames.Contains(fileName);
            if (!isKnownName)
            {
                continue;
            }

            string normalized = NormalizePath(dllPath);
            bool isLlamaRelatedPath =
                normalized.IndexOf("/Assets/Plugins/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normalized.IndexOf("/Assets/Packages/LLamaSharp.Backend.", StringComparison.OrdinalIgnoreCase) >= 0;

            if (!isLlamaRelatedPath)
            {
                continue;
            }

            string assetPath = AbsoluteToAssetPath(dllPath, projectRoot);
            if (!string.IsNullOrWhiteSpace(assetPath))
            {
                output.Add(assetPath);
            }
        }
    }

    private static string AbsoluteToAssetPath(string absolutePath, string projectRoot)
    {
        if (string.IsNullOrWhiteSpace(absolutePath) || string.IsNullOrWhiteSpace(projectRoot))
        {
            return null;
        }

        string normalizedRoot = NormalizePath(Path.GetFullPath(projectRoot)).TrimEnd('/');
        string normalizedPath = NormalizePath(Path.GetFullPath(absolutePath));
        string prefix = normalizedRoot + "/";

        if (!normalizedPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return normalizedPath.Substring(prefix.Length);
    }

    private static bool IsInProjectPluginFolder(string assetPath)
    {
        return NormalizePath(assetPath).StartsWith("Assets/Plugins/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsInBackendPackageFolder(string assetPath)
    {
        return NormalizePath(assetPath).StartsWith("Assets/Packages/LLamaSharp.Backend.", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCpuVariantPath(string assetPath)
    {
        string normalized = NormalizePath(assetPath);
        return normalized.IndexOf("/native/avx2/", StringComparison.OrdinalIgnoreCase) >= 0 ||
               normalized.IndexOf("/native/avx/", StringComparison.OrdinalIgnoreCase) >= 0 ||
               normalized.IndexOf("/native/noavx/", StringComparison.OrdinalIgnoreCase) >= 0 ||
               normalized.IndexOf("/native/avx512/", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string BuildHuggingFaceResolveUrl(string repoId, string revision, string filePath)
    {
        string cleanRepo = (repoId ?? string.Empty).Trim().Trim('/');
        string cleanRevision = string.IsNullOrWhiteSpace(revision) ? DefaultHfRevision : revision.Trim();
        string cleanFilePath = NormalizeModelPathForUrl(filePath);

        string encodedFilePath = string.Join(
            "/",
            cleanFilePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString));

        return $"https://huggingface.co/{cleanRepo}/resolve/{Uri.EscapeDataString(cleanRevision)}/{encodedFilePath}?download=true";
    }

    private static string BuildExternalTempPath(string destinationPath, string downloadUrl)
    {
        string safeName = string.IsNullOrWhiteSpace(destinationPath)
            ? "model.gguf"
            : Path.GetFileName(destinationPath);
        string keySource = $"{destinationPath}|{downloadUrl}";
        string keyHash = ComputeShortHashHex(keySource);
        string tempFileName = $"llmsetup_{keyHash}_{safeName}.part";
        return Path.Combine(Path.GetTempPath(), tempFileName);
    }

    private static string BuildParallelResumeStatePath(string tempPath)
    {
        return string.IsNullOrWhiteSpace(tempPath) ? null : tempPath + ".segments.json";
    }

    private static string BuildParallelResumeKey(string downloadUrl, long totalBytes, int segmentCount)
    {
        return $"{downloadUrl}|{totalBytes}|{segmentCount}";
    }

    private static bool[] LoadParallelResumeState(
        string statePath,
        string expectedKey,
        long totalBytes,
        int segmentCount)
    {
        if (string.IsNullOrWhiteSpace(statePath) || !File.Exists(statePath))
        {
            return new bool[segmentCount];
        }

        try
        {
            string json = File.ReadAllText(statePath);
            var state = JsonUtility.FromJson<HfParallelResumeState>(json);
            if (state == null ||
                state.completedSegments == null ||
                state.completedSegments.Length != segmentCount ||
                state.segmentCount != segmentCount ||
                state.totalBytes != totalBytes ||
                !string.Equals(state.key, expectedKey, StringComparison.Ordinal))
            {
                return new bool[segmentCount];
            }

            return (bool[])state.completedSegments.Clone();
        }
        catch
        {
            return new bool[segmentCount];
        }
    }

    private static void SaveParallelResumeState(
        string statePath,
        string key,
        long totalBytes,
        bool[] completedSegments)
    {
        if (string.IsNullOrWhiteSpace(statePath) || completedSegments == null)
        {
            return;
        }

        try
        {
            var state = new HfParallelResumeState
            {
                key = key,
                totalBytes = totalBytes,
                segmentCount = completedSegments.Length,
                completedSegments = (bool[])completedSegments.Clone()
            };
            string json = JsonUtility.ToJson(state, prettyPrint: false);
            File.WriteAllText(statePath, json);
        }
        catch
        {
            // Ignore sidecar write failures.
        }
    }

    private static long GetSegmentLength(int index, long segmentSize, long totalBytes)
    {
        long start = index * segmentSize;
        if (start >= totalBytes)
        {
            return 0;
        }

        long end = Math.Min(totalBytes - 1, start + segmentSize - 1);
        return Math.Max(0, end - start + 1);
    }

    private static long ResolveSingleStreamTotalBytes(
        HttpResponseMessage response,
        long probedTotalBytes,
        long existingBytes,
        bool resumed)
    {
        if (resumed)
        {
            long contentRangeLength = response.Content.Headers.ContentRange?.Length ?? -1;
            if (contentRangeLength > 0)
            {
                return contentRangeLength;
            }

            long remainingContentLength = response.Content.Headers.ContentLength ?? -1;
            if (remainingContentLength > 0)
            {
                return existingBytes + remainingContentLength;
            }
        }

        long directLength = response.Content.Headers.ContentLength ?? -1;
        if (directLength > 0)
        {
            return directLength;
        }

        return probedTotalBytes;
    }

    private static string ComputeShortHashHex(string value)
    {
        string input = value ?? string.Empty;
        byte[] bytes = Encoding.UTF8.GetBytes(input);
        using var sha = SHA256.Create();
        byte[] hash = sha.ComputeHash(bytes);
        var builder = new StringBuilder(16);
        for (int i = 0; i < 8 && i < hash.Length; i++)
        {
            builder.Append(hash[i].ToString("x2"));
        }

        return builder.ToString();
    }

    private static void TryMoveFileAcrossVolumes(string sourcePath, string destinationPath)
    {
        try
        {
            File.Move(sourcePath, destinationPath);
        }
        catch (IOException)
        {
            File.Copy(sourcePath, destinationPath, overwrite: true);
            TryDeleteFileSilently(sourcePath);
        }
    }

    private static void TryDeleteFileSilently(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch
        {
            // Ignore cleanup failure.
        }
    }

    private static string NormalizeModelPathForUrl(string filePath)
    {
        return (filePath ?? string.Empty)
            .Trim()
            .Replace('\\', '/')
            .TrimStart('/');
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0)
        {
            return "0 B";
        }

        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        double value = bytes;
        int index = 0;
        while (value >= 1024d && index < suffixes.Length - 1)
        {
            value /= 1024d;
            index++;
        }

        return $"{value:0.##} {suffixes[index]}";
    }

    private static HttpClient CreateHttpClient()
    {
        ServicePointManager.DefaultConnectionLimit = Math.Max(
            ServicePointManager.DefaultConnectionLimit,
            HfParallelMaxSegments * 2);
        ServicePointManager.Expect100Continue = false;

        return new HttpClient
        {
            Timeout = TimeSpan.FromHours(6)
        };
    }

    private static void CleanupLegacyPartFilesInStreamingAssets()
    {
        if (string.IsNullOrWhiteSpace(Application.streamingAssetsPath) || !Directory.Exists(Application.streamingAssetsPath))
        {
            return;
        }

        string[] partFiles = Directory.GetFiles(Application.streamingAssetsPath, "*.gguf.part", SearchOption.AllDirectories);
        if (partFiles.Length == 0)
        {
            return;
        }

        foreach (string partPath in partFiles)
        {
            TryDeleteFileSilently(partPath);
        }

        AssetDatabase.Refresh();
    }

    private static string NormalizePath(string path)
    {
        return string.IsNullOrWhiteSpace(path) ? string.Empty : path.Replace('\\', '/');
    }

    private void SetStatus(string message, MessageType type)
    {
        _statusMessage = string.IsNullOrWhiteSpace(message) ? "Ready." : message;
        _statusType = type;
        Repaint();
    }
}
#endif
