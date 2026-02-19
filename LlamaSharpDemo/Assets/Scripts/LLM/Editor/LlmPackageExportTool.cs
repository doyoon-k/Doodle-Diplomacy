#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class LlmPackageExportTool
{
    private enum ExportVariant
    {
        Core,
        Demo
    }

    private static readonly string[] CoreRoots =
    {
        "Assets/Scripts/LLM",
        "Assets/Scripts/AI/RuntimeLlamaSharpService.cs",
        "Assets/Scripts/AI/GamePipelineRunner.cs",
        "Assets/Scripts/AI/AIModels.cs",
        "Assets/packages.config",
        "Assets/NuGet.config",
        "Assets/Plugins/x86_64",
        "Assets/ScriptableObjects/LlmProfiles",
        "Assets/ScriptableObjects/Pipeline"
    };

    private static readonly string[] DemoRoots =
    {
        "Assets/Scripts",
        "Assets/packages.config",
        "Assets/NuGet.config",
        "Assets/Plugins/x86_64",
        "Assets/ScriptableObjects"
    };

    private static readonly string[] DemoSampleEntryAssets =
    {
        "Assets/Scenes/SampleScene.unity"
    };

    private static readonly string[] IncludedPackagePrefixes =
    {
        "Assets/Packages/CommunityToolkit.HighPerformance.",
        "Assets/Packages/LLamaSharp.",
        "Assets/Packages/Microsoft.Bcl.AsyncInterfaces.",
        "Assets/Packages/Microsoft.Bcl.Numerics.",
        "Assets/Packages/Microsoft.Extensions.AI.Abstractions.",
        "Assets/Packages/Microsoft.Extensions.DependencyInjection.Abstractions.",
        "Assets/Packages/Microsoft.Extensions.Logging.Abstractions.",
        "Assets/Packages/System.Diagnostics.DiagnosticSource.",
        "Assets/Packages/System.IO.Pipelines.",
        "Assets/Packages/System.Linq.Async.",
        "Assets/Packages/System.Numerics.Tensors.",
        "Assets/Packages/System.Text.Encodings.Web.",
        "Assets/Packages/System.Text.Json."
    };

    private static readonly string[] ExcludedPathPrefixes =
    {
        "Assets/Packages/LLamaSharp.Backend."
    };

    private static readonly string[] ExcludedExtensions =
    {
        ".gguf",
        ".part",
        ".crdownload",
        ".download",
        ".tmp"
    };

    [MenuItem("Tools/LLM Pipeline/Packaging/Export Release .unitypackage", priority = 340)]
    public static void ExportReleasePackage()
    {
        ExportPackage(ExportVariant.Demo);
    }

    [MenuItem("Tools/LLM Pipeline/Packaging/Export Core .unitypackage", priority = 341)]
    public static void ExportCorePackage()
    {
        ExportPackage(ExportVariant.Core);
    }

    [MenuItem("Tools/LLM Pipeline/Packaging/Print Export Asset Count (Release)", priority = 342)]
    public static void PrintReleaseExportAssetCount()
    {
        PrintExportAssetCount(ExportVariant.Demo);
    }

    [MenuItem("Tools/LLM Pipeline/Packaging/Print Export Asset Count (Core)", priority = 343)]
    public static void PrintCoreExportAssetCount()
    {
        PrintExportAssetCount(ExportVariant.Core);
    }

    private static void ExportPackage(ExportVariant variant)
    {
        List<string> exportAssets = CollectExportAssets(variant);
        if (exportAssets.Count == 0)
        {
            EditorUtility.DisplayDialog(
                "LLM Package Export",
                "No exportable assets were found. Check package roots and try again.",
                "OK");
            return;
        }

        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string projectName = Path.GetFileName(projectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        string variantTag = variant == ExportVariant.Demo ? "ReleaseDemo" : "Core";
        string defaultFileName = $"{projectName}_LLM_Pipeline_{variantTag}_{DateTime.Now:yyyyMMdd_HHmmss}.unitypackage";
        string savePath = EditorUtility.SaveFilePanel(
            $"Export LLM Pipeline {variantTag} Package",
            projectRoot,
            defaultFileName,
            "unitypackage");

        if (string.IsNullOrWhiteSpace(savePath))
        {
            return;
        }

        AssetDatabase.ExportPackage(exportAssets.ToArray(), savePath, ExportPackageOptions.Default);

        Debug.Log($"[LlmPackageExportTool] Exported {exportAssets.Count} assets ({variantTag}) to '{savePath}'.");
        EditorUtility.RevealInFinder(savePath);
    }

    private static void PrintExportAssetCount(ExportVariant variant)
    {
        List<string> exportAssets = CollectExportAssets(variant);
        string variantTag = variant == ExportVariant.Demo ? "ReleaseDemo" : "Core";
        Debug.Log($"[LlmPackageExportTool] Current export asset count ({variantTag}): {exportAssets.Count}");
    }

    private static List<string> CollectExportAssets(ExportVariant variant)
    {
        var assetSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string root in EnumerateExistingRoots(variant))
        {
            AddRootAssets(root, assetSet);
        }

        AddSampleDependencies(variant, assetSet);

        return assetSet
            .Select(NormalizePath)
            .Where(ShouldIncludeAsset)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IEnumerable<string> EnumerateExistingRoots(ExportVariant variant)
    {
        string[] roots = variant == ExportVariant.Demo ? DemoRoots : CoreRoots;
        foreach (string root in roots)
        {
            if (AssetDatabase.IsValidFolder(root) || AssetExists(root))
            {
                yield return NormalizePath(root);
            }
        }

        foreach (string packageFolder in EnumerateIncludedPackageFolders())
        {
            yield return packageFolder;
        }
    }

    private static IEnumerable<string> EnumerateIncludedPackageFolders()
    {
        const string packagesRoot = "Assets/Packages";
        if (!AssetDatabase.IsValidFolder(packagesRoot))
        {
            yield break;
        }

        string[] packageFolders = AssetDatabase.GetSubFolders(packagesRoot);
        foreach (string packageFolder in packageFolders)
        {
            if (ShouldIncludePackageFolder(packageFolder))
            {
                yield return NormalizePath(packageFolder);
            }
        }
    }

    private static bool ShouldIncludePackageFolder(string folderPath)
    {
        string normalized = NormalizePath(folderPath);
        if (!normalized.StartsWith("Assets/Packages/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (ExcludedPathPrefixes.Any(
                prefix => normalized.StartsWith(NormalizePath(prefix), StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return IncludedPackagePrefixes.Any(
            prefix => normalized.StartsWith(NormalizePath(prefix), StringComparison.OrdinalIgnoreCase));
    }

    private static void AddRootAssets(string rootAssetPath, HashSet<string> output)
    {
        if (AssetDatabase.IsValidFolder(rootAssetPath))
        {
            string[] guids = AssetDatabase.FindAssets(string.Empty, new[] { rootAssetPath });
            foreach (string guid in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrWhiteSpace(assetPath) || AssetDatabase.IsValidFolder(assetPath))
                {
                    continue;
                }

                output.Add(NormalizePath(assetPath));
            }

            return;
        }

        if (AssetExists(rootAssetPath))
        {
            output.Add(NormalizePath(rootAssetPath));
        }
    }

    private static void AddSampleDependencies(ExportVariant variant, HashSet<string> output)
    {
        string[] sampleEntries = variant == ExportVariant.Demo
            ? DemoSampleEntryAssets
            : Array.Empty<string>();

        foreach (string entry in sampleEntries)
        {
            string normalizedEntry = NormalizePath(entry);
            if (!AssetExists(normalizedEntry))
            {
                continue;
            }

            output.Add(normalizedEntry);

            string[] dependencies = AssetDatabase.GetDependencies(normalizedEntry, recursive: true);
            foreach (string dependency in dependencies)
            {
                if (string.IsNullOrWhiteSpace(dependency) || AssetDatabase.IsValidFolder(dependency))
                {
                    continue;
                }

                output.Add(NormalizePath(dependency));
            }
        }
    }

    private static bool AssetExists(string assetPath)
    {
        return !string.IsNullOrWhiteSpace(AssetDatabase.AssetPathToGUID(assetPath));
    }

    private static bool ShouldIncludeAsset(string assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            return false;
        }

        string normalized = NormalizePath(assetPath);

        if (!normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (ExcludedPathPrefixes.Any(
                prefix => normalized.StartsWith(NormalizePath(prefix), StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        string extension = Path.GetExtension(normalized);
        if (!string.IsNullOrWhiteSpace(extension) &&
            ExcludedExtensions.Any(excluded => extension.Equals(excluded, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return true;
    }

    private static string NormalizePath(string path)
    {
        return string.IsNullOrWhiteSpace(path) ? string.Empty : path.Replace('\\', '/');
    }
}
#endif
