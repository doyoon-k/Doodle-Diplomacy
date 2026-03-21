#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

internal readonly struct StableDiffusionDownloadPreset
{
    internal readonly string Id;
    internal readonly string Label;
    internal readonly string RepoId;
    internal readonly string FilePath;
    internal readonly string Revision;
    internal readonly string ProfileAssetPath;
    internal readonly string ProfileName;
    internal readonly int Width;
    internal readonly int Height;
    internal readonly int Steps;
    internal readonly float CfgScale;
    internal readonly int Seed;
    internal readonly string Sampler;

    internal StableDiffusionDownloadPreset(
        string id,
        string label,
        string repoId,
        string filePath,
        string revision,
        string profileAssetPath,
        string profileName,
        int width,
        int height,
        int steps,
        float cfgScale,
        int seed,
        string sampler)
    {
        Id = id ?? string.Empty;
        Label = label ?? string.Empty;
        RepoId = repoId ?? string.Empty;
        FilePath = filePath ?? string.Empty;
        Revision = string.IsNullOrWhiteSpace(revision) ? "main" : revision.Trim();
        ProfileAssetPath = profileAssetPath ?? string.Empty;
        ProfileName = profileName ?? string.Empty;
        Width = Mathf.Max(64, width);
        Height = Mathf.Max(64, height);
        Steps = Mathf.Max(1, steps);
        CfgScale = Mathf.Max(0.1f, cfgScale);
        Seed = seed;
        Sampler = string.IsNullOrWhiteSpace(sampler) ? "euler_a" : sampler.Trim();
    }

    internal string FileName => Path.GetFileName(FilePath ?? string.Empty);
}

internal static class StableDiffusionCppSetupUtility
{
    internal const string SettingsAssetPath = "Assets/ScriptableObjects/StableDiffusion/StableDiffusionCppSettings.asset";
    internal const string ModelsFolderName = "SDModels";

    private const string ProfilesFolder = "Assets/ScriptableObjects/StableDiffusion/Profiles";
    private const string Sd15ProfileAssetPath = ProfilesFolder + "/SD-1.5-q4_K.asset";
    private const string SdTurboProfileAssetPath = ProfilesFolder + "/SD-Turbo-q4_K.asset";

    private static readonly StableDiffusionDownloadPreset[] DownloadPresetArray =
    {
        new StableDiffusionDownloadPreset(
            id: "sd15-q4_1",
            label: "SD 1.5 Q4_1 (Recommended)",
            repoId: "second-state/stable-diffusion-v1-5-GGUF",
            filePath: "stable-diffusion-v1-5-pruned-emaonly-Q4_1.gguf",
            revision: "main",
            profileAssetPath: Sd15ProfileAssetPath,
            profileName: "SD 1.5 Q4_1",
            width: 512,
            height: 512,
            steps: 20,
            cfgScale: 7.0f,
            seed: 42,
            sampler: "euler_a"),
        new StableDiffusionDownloadPreset(
            id: "sd-turbo-q8_0",
            label: "SD-Turbo Q8_0",
            repoId: "Green-Sky/SD-Turbo-GGUF",
            filePath: "sd_turbo-f16-q8_0.gguf",
            revision: "main",
            profileAssetPath: SdTurboProfileAssetPath,
            profileName: "SD-Turbo Q8_0",
            width: 512,
            height: 512,
            steps: 1,
            cfgScale: 1.0f,
            seed: 42,
            sampler: "euler")
    };

    internal static IReadOnlyList<StableDiffusionDownloadPreset> DownloadPresets => DownloadPresetArray;

    internal static bool TryGetPreset(int index, out StableDiffusionDownloadPreset preset)
    {
        if (index >= 0 && index < DownloadPresetArray.Length)
        {
            preset = DownloadPresetArray[index];
            return true;
        }

        preset = default;
        return false;
    }

    internal static string GetModelDestinationDirectoryAbsolute()
    {
        return Path.Combine(Application.streamingAssetsPath, ModelsFolderName);
    }

    internal static string GetStreamingAssetsRelativeModelPath(StableDiffusionDownloadPreset preset)
    {
        return NormalizeRelativePath($"{ModelsFolderName}/{preset.FileName}");
    }

    internal static void CreateOrRefreshDefaultProfiles(StableDiffusionCppSettings settings)
    {
        if (settings == null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        settings.modelProfiles ??= new List<StableDiffusionCppModelProfile>();

        StableDiffusionCppModelProfile firstProfile = null;
        for (int i = 0; i < DownloadPresetArray.Length; i++)
        {
            StableDiffusionDownloadPreset preset = DownloadPresetArray[i];
            StableDiffusionCppModelProfile profile = GetOrCreateProfileAsset(
                preset,
                GetStreamingAssetsRelativeModelPath(preset));
            AssignProfileIfMissing(settings, profile);
            firstProfile ??= profile;
        }

        if (settings.activeModelProfile == null)
        {
            settings.activeModelProfile = firstProfile;
        }

        if (string.IsNullOrWhiteSpace(settings.modelPath) && firstProfile != null)
        {
            settings.modelPath = firstProfile.modelPath;
        }

        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
    }

    internal static StableDiffusionCppModelProfile ApplyDownloadedPreset(int presetIndex, string modelPathRelativeToStreamingAssets)
    {
        if (!TryGetPreset(presetIndex, out StableDiffusionDownloadPreset preset))
        {
            throw new ArgumentOutOfRangeException(nameof(presetIndex), "Invalid Stable Diffusion preset index.");
        }

        return ApplyDownloadedPreset(preset, modelPathRelativeToStreamingAssets);
    }

    internal static StableDiffusionCppModelProfile ApplyDownloadedPreset(
        StableDiffusionDownloadPreset preset,
        string modelPathRelativeToStreamingAssets)
    {
        if (string.IsNullOrWhiteSpace(modelPathRelativeToStreamingAssets))
        {
            throw new ArgumentException("Model path must not be empty.", nameof(modelPathRelativeToStreamingAssets));
        }

        StableDiffusionCppSettings settings = EnsureSettingsAsset();
        CreateOrRefreshDefaultProfiles(settings);

        string normalizedPath = NormalizeRelativePath(modelPathRelativeToStreamingAssets);
        StableDiffusionCppModelProfile profile = GetOrCreateProfileAsset(preset, normalizedPath);
        AssignProfileIfMissing(settings, profile);

        settings.activeModelProfile = profile;
        settings.modelPath = normalizedPath;

        EditorUtility.SetDirty(profile);
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
        return profile;
    }

    internal static StableDiffusionCppSettings EnsureSettingsAsset()
    {
        StableDiffusionCppSettings settings = AssetDatabase.LoadAssetAtPath<StableDiffusionCppSettings>(SettingsAssetPath);
        if (settings != null)
        {
            return settings;
        }

        string folderPath = NormalizeRelativePath(Path.GetDirectoryName(SettingsAssetPath) ?? "Assets");
        EnsureFolderPath(folderPath);

        settings = ScriptableObject.CreateInstance<StableDiffusionCppSettings>();
        AssetDatabase.CreateAsset(settings, SettingsAssetPath);
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
        return settings;
    }

    private static StableDiffusionCppModelProfile GetOrCreateProfileAsset(
        StableDiffusionDownloadPreset preset,
        string modelPath)
    {
        string assetPath = NormalizeRelativePath(preset.ProfileAssetPath);
        string assetFolder = NormalizeRelativePath(Path.GetDirectoryName(assetPath) ?? ProfilesFolder);
        EnsureFolderPath(assetFolder);

        StableDiffusionCppModelProfile profile = AssetDatabase.LoadAssetAtPath<StableDiffusionCppModelProfile>(assetPath);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<StableDiffusionCppModelProfile>();
            AssetDatabase.CreateAsset(profile, assetPath);
        }

        profile.profileName = string.IsNullOrWhiteSpace(preset.ProfileName) ? preset.Label : preset.ProfileName;
        profile.modelPath = NormalizeRelativePath(modelPath);
        profile.vaePath = string.Empty;
        profile.defaultWidth = preset.Width;
        profile.defaultHeight = preset.Height;
        profile.defaultSteps = preset.Steps;
        profile.defaultCfgScale = preset.CfgScale;
        profile.defaultSeed = preset.Seed;
        profile.defaultSampler = preset.Sampler;
        EditorUtility.SetDirty(profile);
        return profile;
    }

    private static void AssignProfileIfMissing(StableDiffusionCppSettings settings, StableDiffusionCppModelProfile profile)
    {
        if (settings == null || profile == null)
        {
            return;
        }

        settings.modelProfiles ??= new List<StableDiffusionCppModelProfile>();
        if (!settings.modelProfiles.Contains(profile))
        {
            settings.modelProfiles.Add(profile);
        }
    }

    private static void EnsureFolderPath(string folderPath)
    {
        string normalizedFolderPath = NormalizeRelativePath(folderPath);
        if (string.IsNullOrWhiteSpace(normalizedFolderPath) || AssetDatabase.IsValidFolder(normalizedFolderPath))
        {
            return;
        }

        string[] segments = normalizedFolderPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return;
        }

        string current = segments[0];
        for (int i = 1; i < segments.Length; i++)
        {
            string next = current + "/" + segments[i];
            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, segments[i]);
            }

            current = next;
        }
    }

    private static string NormalizeRelativePath(string path)
    {
        return (path ?? string.Empty).Trim().Replace('\\', '/');
    }
}
#endif
