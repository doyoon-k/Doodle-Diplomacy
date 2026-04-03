using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[CreateAssetMenu(fileName = "StableDiffusionCppSettings", menuName = "AI/Stable Diffusion CPP Settings")]
public class StableDiffusionCppSettings : ScriptableObject
{
    [Header("Runtime Packaging")]
    [Tooltip("Runtime install version label used under persistentDataPath. Increase this value when you replace the bundled stable-diffusion.cpp binaries and want Unity to copy/install the new runtime into a fresh folder.")]
    public string runtimeVersion = "v0.0.1";

    [Tooltip("Per-platform stable-diffusion.cpp runtime packages bundled under StreamingAssets. At startup, the entry matching the current OS is selected and its executable is copied/prepared for inference.")]
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
    [Tooltip("List of available Stable Diffusion model profiles. Each profile asset owns its model path, optional VAE/ControlNet paths, and default generation parameters such as resolution, steps, sampler, scheduler, and prompt defaults.")]
    public List<StableDiffusionCppModelProfile> modelProfiles = new List<StableDiffusionCppModelProfile>();

    [Tooltip("Currently selected model profile used by the generator window and runtime. If this is empty, the first non-null entry in Model Profiles is used; if no profile exists, model preparation/generation cannot resolve a model path.")]
    public StableDiffusionCppModelProfile activeModelProfile;

    [Header("Model Storage")]
    [Tooltip("If enabled, the active profile's model file is copied from StreamingAssets (or its source location) into persistentDataPath before running inference, and the copied file is passed to sd-cli. This can reduce read issues on some platforms, but it duplicates large model files and increases disk usage, so keep it off unless you need a local writable/runtime-managed copy.")]
    public bool copyModelToPersistentDataPath;

    [Header("Performance Defaults")]
    [Tooltip("Default state for the generator window's Offload Weights To CPU option. When enabled, sd-cli receives --offload-to-cpu, which moves part of the model workload/weights to system RAM/CPU to reduce VRAM pressure, usually at the cost of slower generation.")]
    public bool defaultOffloadToCpu;

    [Tooltip("Default state for the generator window's Keep CLIP On CPU option. When enabled, sd-cli receives --clip-on-cpu, so the text encoder runs on CPU instead of GPU. This can save VRAM, but prompt encoding may become slower.")]
    public bool defaultClipOnCpu;

    [Tooltip("Default state for the generator window's Enable VAE Tiling option. When enabled, sd-cli receives --vae-tiling, which decodes/encodes latent images in tiles to reduce peak VRAM usage, especially at higher resolutions. It can be slower and may introduce subtle tile-boundary artifacts depending on the model/output.")]
    public bool defaultVaeTiling;

    [Tooltip("Default state for the generator window's Use Diffusion Flash Attention option. When enabled, sd-cli receives --diffusion-fa, using a flash-attention implementation for the diffusion U-Net/denoiser attention blocks. This can reduce memory use and improve speed on supported GPU/runtime combinations, but behavior and gains depend on the backend/model.")]
    public bool defaultDiffusionFlashAttention;

    [Tooltip("Default state for the generator window's Enable Cache Mode option. When enabled, the command includes --cache-mode plus the selected cache mode string below, allowing stable-diffusion.cpp to reuse/cache intermediate computations for supported acceleration strategies. Keep disabled if you want the baseline uncached path.")]
    public bool defaultUseCacheMode;

    [Tooltip("Default value passed to --cache-mode when cache mode is enabled in the generator. Examples include easycache, ucache, dbcache, taylorseer, and cache-dit. The exact mode controls which stable-diffusion.cpp caching strategy is used, so choose one supported by your target model/runtime.")]
    public string defaultCacheMode = "easycache";

    [Tooltip("Optional default value passed to --cache-option when cache mode is enabled. This is a raw stable-diffusion.cpp cache parameter string for advanced tuning; leave empty unless the selected cache mode requires extra options or you are intentionally overriding its behavior.")]
    public string defaultCacheOption = string.Empty;

    [Tooltip("Optional default value passed to --cache-preset when cache mode is enabled. Use this when the chosen cache mode supports named presets and you want a preset selected automatically; leave empty to let stable-diffusion.cpp use its own default behavior.")]
    public string defaultCachePreset = string.Empty;

    [Header("Runtime Execution")]
    [Tooltip("Maximum time, in seconds, to wait for one sd-cli process before the runtime treats it as timed out and terminates/cancels that generation request. Increase this for slow CPUs, large images, high step counts, or very large models.")]
    [Min(30)]
    public int processTimeoutSeconds = 600;

    [Tooltip("Extra raw command-line arguments appended to every sd-cli invocation after the generated arguments. Use this as an escape hatch for stable-diffusion.cpp options that are not exposed by dedicated UI fields. Invalid or conflicting flags can break generation, so keep it empty unless you know the exact CLI arguments you want.")]
    public string globalAdditionalArguments = string.Empty;

    [Tooltip("Relative subfolder under Application.persistentDataPath where runtime-generated images are written when no explicit output directory override is provided. Example: generated/sdcpp stores files under <persistentDataPath>/generated/sdcpp.")]
    public string runtimeOutputSubfolder = "generated/sdcpp";

    [Header("GPU Telemetry")]
    [Tooltip("If enabled, the runtime periodically queries nvidia-smi while a generation is running and logs peak GPU memory usage/utilization for that process. This is mainly for NVIDIA GPU debugging/profiling; on systems without nvidia-smi or compatible telemetry, the generation still runs but GPU telemetry may be unavailable.")]
    public bool enableGpuTelemetry = true;

    [Range(100, 2000)]
    [Tooltip("Polling interval, in milliseconds, for GPU telemetry sampling via nvidia-smi. Lower values capture shorter VRAM/utilization spikes more accurately but add more monitoring overhead; higher values are lighter but may miss brief peaks.")]
    public int gpuTelemetryPollIntervalMs = 500;

    [Header("Editor UX")]
    [Tooltip("Project-relative folder path used by the editor generator window as its default output location when saving generated images into the Assets tree. Use an Assets/... path if you want outputs to appear in the Project window after AssetDatabase refresh.")]
    public string editorOutputProjectRelativePath = "Assets/Images/GeneratedImages";

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
        return profile != null
            ? ResolvePathFromStreamingAssetsOrAbsolute(profile.modelPath)
            : string.Empty;
    }

    public string ResolveVaePath()
    {
        StableDiffusionCppModelProfile profile = GetActiveModelProfile();
        return profile != null
            ? ResolvePathFromStreamingAssetsOrAbsolute(profile.vaePath)
            : string.Empty;
    }

    public string ResolveControlNetPath(string controlNetPathOverride = null)
    {
        if (!string.IsNullOrWhiteSpace(controlNetPathOverride))
        {
            return ResolvePathFromStreamingAssetsOrAbsolute(controlNetPathOverride);
        }

        StableDiffusionCppModelProfile profile = GetActiveModelProfile();
        return profile != null
            ? ResolvePathFromStreamingAssetsOrAbsolute(profile.controlNetPath)
            : string.Empty;
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
            ? "Assets/Images/GeneratedImages"
            : editorOutputProjectRelativePath.Trim();
        defaultCacheMode = NormalizeTokenOrFallback(defaultCacheMode, "easycache");
        defaultCacheOption ??= string.Empty;
        defaultCachePreset ??= string.Empty;
    }
}

[Serializable]
public class StableDiffusionCppRuntimePackage
{
    public StableDiffusionCppPlatformId platform = StableDiffusionCppPlatformId.WinX64;
    [Tooltip("Folder path under StreamingAssets that contains the stable-diffusion.cpp runtime files for this platform entry. Example: SDCpp/win-x64 resolves to <StreamingAssets>/SDCpp/win-x64.")]
    public string streamingAssetsRuntimeFolder = "SDCpp/win-x64";

    [Tooltip("Executable file name inside the selected runtime folder that will be launched for generation. On Windows this is typically sd-cli.exe; on Linux/macOS it may be the corresponding sd-cli binary name.")]
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
