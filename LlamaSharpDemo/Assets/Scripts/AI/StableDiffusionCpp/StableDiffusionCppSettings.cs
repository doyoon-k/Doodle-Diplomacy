using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[CreateAssetMenu(fileName = "StableDiffusionCppSettings", menuName = "AI/Stable Diffusion CPP Settings")]
public class StableDiffusionCppSettings : ScriptableObject
{
    [Header("Runtime Packaging")]
    [Tooltip("Version token used for persistent runtime install path. Bump to force re-install.")]
    public string runtimeVersion = "v0.0.1";

    [Tooltip("Per-platform runtime package locations under StreamingAssets.")]
    public List<StableDiffusionCppRuntimePackage> runtimePackages = new List<StableDiffusionCppRuntimePackage>
    {
        new StableDiffusionCppRuntimePackage
        {
            platform = StableDiffusionCppPlatformId.WinX64,
            streamingAssetsRuntimeFolder = "SDCpp/win-x64",
            executableFileName = "sd-cli.exe"
        }
    };

    [Header("Model Profiles")]
    [Tooltip("Model profiles (SD-1.5, SD-Turbo, etc.) managed as ScriptableObjects.")]
    public List<StableDiffusionCppModelProfile> modelProfiles = new List<StableDiffusionCppModelProfile>();

    [Tooltip("Active model profile used by runtime/editor.")]
    public StableDiffusionCppModelProfile activeModelProfile;

    [Header("Model")]
    [Tooltip("Fallback model path used when no active model profile is assigned.")]
    public string modelPath = "SDModels/v1-5-pruned-emaonly.safetensors";

    [Tooltip("Fallback VAE path used when no active model profile is assigned.")]
    public string vaePath = string.Empty;

    [Tooltip("Copy model into persistentDataPath before inference. Recommended off for very large files.")]
    public bool copyModelToPersistentDataPath;

    [Header("Defaults")]
    [Min(64)]
    public int defaultWidth = 512;
    [Min(64)]
    public int defaultHeight = 512;
    [Min(1)]
    public int defaultSteps = 20;
    [Min(0.1f)]
    public float defaultCfgScale = 7.0f;
    public int defaultSeed = 42;
    public string defaultSampler = "euler_a";
    public string defaultNegativePrompt = string.Empty;

    [Header("Performance Defaults")]
    [Tooltip("Add --offload-to-cpu by default.")]
    public bool defaultOffloadToCpu;
    [Tooltip("Add --clip-on-cpu by default.")]
    public bool defaultClipOnCpu;
    [Tooltip("Add --vae-tiling by default.")]
    public bool defaultVaeTiling;
    [Tooltip("Add --diffusion-fa by default.")]
    public bool defaultDiffusionFlashAttention;
    [Tooltip("Add --cache-mode by default.")]
    public bool defaultUseCacheMode;
    [Tooltip("Default cache mode when cache mode is enabled.")]
    public string defaultCacheMode = "easycache";
    [Tooltip("Optional default value for --cache-option.")]
    public string defaultCacheOption = string.Empty;
    [Tooltip("Optional default value for --cache-preset.")]
    public string defaultCachePreset = string.Empty;

    [Header("Runtime Execution")]
    [Min(30)]
    public int processTimeoutSeconds = 600;

    [Tooltip("Additional raw CLI arguments appended to every command.")]
    public string globalAdditionalArguments = string.Empty;

    [Tooltip("Subfolder under persistentDataPath where generated images are stored by default.")]
    public string runtimeOutputSubfolder = "generated/sdcpp";

    [Header("GPU Telemetry")]
    [Tooltip("If enabled, runtime samples nvidia-smi while generating and reports peak GPU memory/use in logs.")]
    public bool enableGpuTelemetry = true;
    [Range(100, 2000)]
    [Tooltip("Sampling interval for nvidia-smi polling in milliseconds.")]
    public int gpuTelemetryPollIntervalMs = 500;

    [Header("Editor UX")]
    [Tooltip("Project-relative output folder used by the editor test window.")]
    public string editorOutputProjectRelativePath = "Assets/Generated/StableDiffusion";

    public StableDiffusionCppRuntimePackage GetPackageForCurrentPlatform()
    {
        return GetPackageForPlatform(StableDiffusionCppPlatformResolver.GetCurrentPlatform());
    }

    public StableDiffusionCppRuntimePackage GetPackageForPlatform(StableDiffusionCppPlatformId platform)
    {
        if (runtimePackages == null)
        {
            return null;
        }

        for (int i = 0; i < runtimePackages.Count; i++)
        {
            if (runtimePackages[i] != null && runtimePackages[i].platform == platform)
            {
                return runtimePackages[i];
            }
        }

        return null;
    }

    public string ResolvePathFromStreamingAssetsOrAbsolute(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string trimmed = value.Trim();
        if (Path.IsPathRooted(trimmed))
        {
            return trimmed;
        }

        return Path.Combine(Application.streamingAssetsPath, NormalizeRelativePath(trimmed));
    }

    public string ResolveModelPath()
    {
        StableDiffusionCppModelProfile profile = GetActiveModelProfile();
        string path = profile != null ? profile.modelPath : modelPath;
        return ResolvePathFromStreamingAssetsOrAbsolute(path);
    }

    public string ResolveVaePath()
    {
        StableDiffusionCppModelProfile profile = GetActiveModelProfile();
        string path = profile != null && !string.IsNullOrWhiteSpace(profile.vaePath)
            ? profile.vaePath
            : vaePath;
        return ResolvePathFromStreamingAssetsOrAbsolute(path);
    }

    public StableDiffusionCppModelProfile GetActiveModelProfile()
    {
        if (activeModelProfile != null)
        {
            return activeModelProfile;
        }

        if (modelProfiles == null)
        {
            return null;
        }

        for (int i = 0; i < modelProfiles.Count; i++)
        {
            if (modelProfiles[i] != null)
            {
                return modelProfiles[i];
            }
        }

        return null;
    }

    public bool TryApplyActiveProfileDefaults(StableDiffusionCppGenerationRequest request)
    {
        if (request == null)
        {
            return false;
        }

        StableDiffusionCppModelProfile profile = GetActiveModelProfile();
        if (profile == null)
        {
            return false;
        }

        profile.ApplyDefaultsTo(request);
        return true;
    }

    public string ResolvePersistentOutputDirectory()
    {
        return Path.Combine(Application.persistentDataPath, NormalizeRelativePath(runtimeOutputSubfolder));
    }

    public string ResolveEditorOutputDirectoryAbsolute()
    {
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        return Path.Combine(projectRoot, NormalizeRelativePath(editorOutputProjectRelativePath));
    }

    private static string NormalizeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return path
            .Trim()
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);
    }

    private static string NormalizeTokenOrFallback(string token, string fallback)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return fallback;
        }

        return token.Trim();
    }

    private void OnValidate()
    {
        modelProfiles ??= new List<StableDiffusionCppModelProfile>();
        if (activeModelProfile == null)
        {
            for (int i = 0; i < modelProfiles.Count; i++)
            {
                if (modelProfiles[i] != null)
                {
                    activeModelProfile = modelProfiles[i];
                    break;
                }
            }
        }
        else if (!modelProfiles.Contains(activeModelProfile))
        {
            modelProfiles.Add(activeModelProfile);
        }

        runtimeVersion = string.IsNullOrWhiteSpace(runtimeVersion) ? "v0.0.1" : runtimeVersion.Trim();
        runtimeOutputSubfolder = string.IsNullOrWhiteSpace(runtimeOutputSubfolder)
            ? "generated/sdcpp"
            : runtimeOutputSubfolder.Trim();
        editorOutputProjectRelativePath = string.IsNullOrWhiteSpace(editorOutputProjectRelativePath)
            ? "Assets/Generated/StableDiffusion"
            : editorOutputProjectRelativePath.Trim();
        defaultCacheMode = NormalizeTokenOrFallback(defaultCacheMode, "easycache");
        defaultCacheOption ??= string.Empty;
        defaultCachePreset ??= string.Empty;
    }
}

[CreateAssetMenu(fileName = "StableDiffusionModelProfile", menuName = "AI/Stable Diffusion CPP Model Profile")]
public class StableDiffusionCppModelProfile : ScriptableObject
{
    [Header("Identity")]
    [Tooltip("User-facing profile label.")]
    public string profileName = "SD 1.5";

    [Header("Model")]
    [Tooltip("Model path relative to StreamingAssets, or absolute path.")]
    public string modelPath = "SDModels/v1-5-pruned-emaonly-q4_K.gguf";

    [Tooltip("Optional VAE path relative to StreamingAssets, or absolute path.")]
    public string vaePath = string.Empty;

    [Header("Generation Defaults")]
    [Min(64)]
    public int defaultWidth = 512;
    [Min(64)]
    public int defaultHeight = 512;
    [Min(1)]
    public int defaultSteps = 20;
    [Min(0.1f)]
    public float defaultCfgScale = 7.0f;
    public int defaultSeed = 42;
    public string defaultSampler = "euler_a";
    [TextArea(2, 6)]
    public string defaultNegativePrompt = string.Empty;

    public string DisplayName => string.IsNullOrWhiteSpace(profileName) ? name : profileName.Trim();

    public void ApplyDefaultsTo(StableDiffusionCppGenerationRequest request)
    {
        if (request == null)
        {
            return;
        }

        request.width = defaultWidth;
        request.height = defaultHeight;
        request.steps = defaultSteps;
        request.cfgScale = defaultCfgScale;
        request.seed = defaultSeed;
        request.sampler = string.IsNullOrWhiteSpace(defaultSampler) ? "euler_a" : defaultSampler.Trim();
        request.negativePrompt = defaultNegativePrompt ?? string.Empty;
    }

    private void OnValidate()
    {
        profileName = string.IsNullOrWhiteSpace(profileName) ? "Stable Diffusion Profile" : profileName.Trim();
        defaultWidth = Mathf.Max(64, defaultWidth);
        defaultHeight = Mathf.Max(64, defaultHeight);
        defaultSteps = Mathf.Max(1, defaultSteps);
        defaultCfgScale = Mathf.Max(0.1f, defaultCfgScale);
        defaultSampler = string.IsNullOrWhiteSpace(defaultSampler) ? "euler_a" : defaultSampler.Trim();
        defaultNegativePrompt ??= string.Empty;
    }
}

[Serializable]
public class StableDiffusionCppRuntimePackage
{
    public StableDiffusionCppPlatformId platform = StableDiffusionCppPlatformId.WinX64;
    [Tooltip("Runtime folder path relative to StreamingAssets.")]
    public string streamingAssetsRuntimeFolder = "SDCpp/win-x64";
    [Tooltip("Executable file name inside the runtime folder.")]
    public string executableFileName = "sd-cli.exe";
}

public enum StableDiffusionCppPlatformId
{
    Unknown = 0,
    WinX64 = 1,
    LinuxX64 = 2,
    MacOSX64 = 3,
    MacOSArm64 = 4
}

public static class StableDiffusionCppPlatformResolver
{
    public static StableDiffusionCppPlatformId GetCurrentPlatform()
    {
        RuntimePlatform platform = Application.platform;
        switch (platform)
        {
            case RuntimePlatform.WindowsEditor:
            case RuntimePlatform.WindowsPlayer:
                return StableDiffusionCppPlatformId.WinX64;
            case RuntimePlatform.LinuxEditor:
            case RuntimePlatform.LinuxPlayer:
                return StableDiffusionCppPlatformId.LinuxX64;
            case RuntimePlatform.OSXEditor:
            case RuntimePlatform.OSXPlayer:
                return IsLikelyAppleSilicon()
                    ? StableDiffusionCppPlatformId.MacOSArm64
                    : StableDiffusionCppPlatformId.MacOSX64;
            default:
                return StableDiffusionCppPlatformId.Unknown;
        }
    }

    private static bool IsLikelyAppleSilicon()
    {
        string cpu = SystemInfo.processorType ?? string.Empty;
        return cpu.IndexOf("apple", StringComparison.OrdinalIgnoreCase) >= 0
               || cpu.IndexOf("arm", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
