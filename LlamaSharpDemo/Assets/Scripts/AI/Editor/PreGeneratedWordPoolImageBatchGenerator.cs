#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using DoodleDiplomacy.Data;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DoodleDiplomacy.AI.Editor
{
    public static class PreGeneratedWordPoolImageBatchGenerator
    {
        private const string MenuPath = "Tools/AI/Stable Diffusion CPP/Generate Word Pool Pre-Generated Catalog";
        private const string DefaultCatalogAssetPath = "Assets/ScriptableObjects/StableDiffusion/PreGeneratedObjectImageCatalog.asset";
        private const string OutputDirectoryProjectRelative = "Assets/Images/GeneratedImages/WordPool";

        private static bool s_IsRunning;

        private sealed class BridgeGenerationConfig
        {
            public AIPipelineBridge bridge;
            public SerializedObject serializedBridge;
            public SerializedProperty usePreGeneratedCatalogProperty;
            public SerializedProperty preGeneratedCatalogProperty;
            public StableDiffusionCppSettings sdSettings;
            public StableDiffusionCppModelProfile sdModelProfileOverride;
            public WordPairPool wordPairPool;
            public string objectPromptA;
            public string objectPromptB;
            public string sdNegativePrompt;
            public string selectedKeywordPromptTemplate;
            public PreGeneratedObjectImageCatalog catalog;
        }

        private sealed class PromptSpec
        {
            public string prompt;
            public string key;
            public string fileName;
        }

        [MenuItem(MenuPath, true)]
        public static bool ValidateGenerateWordPoolCatalogMenu()
        {
            return !s_IsRunning;
        }

        [MenuItem(MenuPath)]
        public static async void GenerateWordPoolCatalogMenu()
        {
            if (s_IsRunning)
            {
                Debug.LogWarning("[PreGeneratedWordPoolImageBatchGenerator] A generation job is already running.");
                return;
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog(
                    "Word Pool Pre-Generation",
                    "Stop play mode before running the batch generator.",
                    "OK");
                return;
            }

            AIPipelineBridge bridge = FindActiveSceneBridge();
            if (bridge == null)
            {
                EditorUtility.DisplayDialog(
                    "Word Pool Pre-Generation",
                    "No AIPipelineBridge was found in open scenes.",
                    "OK");
                return;
            }

            s_IsRunning = true;
            try
            {
                await GenerateWordPoolCatalogAsync(bridge);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PreGeneratedWordPoolImageBatchGenerator] Batch generation failed:\n{ex}");
                EditorUtility.DisplayDialog(
                    "Word Pool Pre-Generation",
                    $"Batch generation failed.\n\n{ex.Message}",
                    "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                s_IsRunning = false;
            }
        }

        private static async Task GenerateWordPoolCatalogAsync(AIPipelineBridge bridge)
        {
            if (!TryReadBridgeConfig(bridge, out BridgeGenerationConfig config, out string configError))
            {
                throw new InvalidOperationException(configError);
            }

            List<PromptSpec> promptSpecs = BuildPromptSpecs(config);
            if (promptSpecs.Count == 0)
            {
                throw new InvalidOperationException("No valid prompts were found in WordPairPool.");
            }

            string outputDirectoryAbsolute = ResolveProjectRelativeAbsolutePath(OutputDirectoryProjectRelative);
            Directory.CreateDirectory(outputDirectoryAbsolute);

            var expectedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < promptSpecs.Count; i++)
            {
                PromptSpec promptSpec = promptSpecs[i];
                expectedFileNames.Add(promptSpec.fileName);

                EditorUtility.DisplayProgressBar(
                    "Word Pool Pre-Generation",
                    $"Generating {i + 1}/{promptSpecs.Count}: {promptSpec.key.Substring(0, 12)}",
                    (float)i / promptSpecs.Count);

                StableDiffusionCppGenerationRequest request = BuildGenerationRequest(config, promptSpec.prompt, promptSpec.key);
                StableDiffusionCppGenerationResult result =
                    await StableDiffusionCppRuntime.GenerateTxt2ImgAsync(config.sdSettings, request);

                if (!result.Success)
                {
                    throw new InvalidOperationException(
                        $"Generation failed for key '{promptSpec.key}': {result.ErrorMessage}");
                }

                if (result.OutputFiles == null || result.OutputFiles.Count == 0)
                {
                    throw new InvalidOperationException(
                        $"Generation succeeded for key '{promptSpec.key}' but no output file was returned.");
                }

                string sourcePath = result.OutputFiles[0];
                if (!File.Exists(sourcePath))
                {
                    throw new FileNotFoundException(
                        $"Generated file does not exist for key '{promptSpec.key}'.",
                        sourcePath);
                }

                string destinationPath = Path.Combine(outputDirectoryAbsolute, promptSpec.fileName);
                File.Copy(sourcePath, destinationPath, overwrite: true);
            }

            CleanupStaleGeneratedFiles(outputDirectoryAbsolute, expectedFileNames);
            AssetDatabase.Refresh();

            List<PromptSpec> finalSpecs = new List<PromptSpec>(promptSpecs.Count);
            for (int i = 0; i < promptSpecs.Count; i++)
            {
                PromptSpec spec = promptSpecs[i];
                string assetPath = MakeProjectRelativePath(Path.Combine(outputDirectoryAbsolute, spec.fileName));
                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                if (texture == null)
                {
                    throw new InvalidOperationException(
                        $"Generated texture import failed for '{assetPath}' (key={spec.key}).");
                }

                finalSpecs.Add(spec);
            }

            UpdateCatalogEntries(config.catalog, outputDirectoryAbsolute, finalSpecs);
            config.catalog.MarkLookupDirty();
            EditorUtility.SetDirty(config.catalog);

            config.usePreGeneratedCatalogProperty.boolValue = true;
            config.preGeneratedCatalogProperty.objectReferenceValue = config.catalog;
            config.serializedBridge.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(config.bridge);

            if (config.bridge.gameObject.scene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(config.bridge.gameObject.scene);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(
                $"[PreGeneratedWordPoolImageBatchGenerator] Completed {finalSpecs.Count} prompt generations. " +
                $"Catalog='{AssetDatabase.GetAssetPath(config.catalog)}', Output='{OutputDirectoryProjectRelative}'.");
        }

        private static AIPipelineBridge FindActiveSceneBridge()
        {
            AIPipelineBridge[] bridges = UnityEngine.Object.FindObjectsOfType<AIPipelineBridge>();
            for (int i = 0; i < bridges.Length; i++)
            {
                AIPipelineBridge bridge = bridges[i];
                if (bridge == null || EditorUtility.IsPersistent(bridge))
                {
                    continue;
                }

                if (!bridge.gameObject.scene.IsValid())
                {
                    continue;
                }

                return bridge;
            }

            return null;
        }

        private static bool TryReadBridgeConfig(
            AIPipelineBridge bridge,
            out BridgeGenerationConfig config,
            out string error)
        {
            config = null;
            error = string.Empty;

            if (bridge == null)
            {
                error = "AIPipelineBridge is null.";
                return false;
            }

            var serializedBridge = new SerializedObject(bridge);
            SerializedProperty useCatalogProperty = serializedBridge.FindProperty("usePreGeneratedCatalog");
            SerializedProperty catalogProperty = serializedBridge.FindProperty("preGeneratedCatalog");
            SerializedProperty settingsProperty = serializedBridge.FindProperty("sdSettings");
            SerializedProperty profileProperty = serializedBridge.FindProperty("sdModelProfile");
            SerializedProperty poolProperty = serializedBridge.FindProperty("wordPairPool");
            SerializedProperty objectPromptAProperty = serializedBridge.FindProperty("objectPromptA");
            SerializedProperty objectPromptBProperty = serializedBridge.FindProperty("objectPromptB");
            SerializedProperty negativePromptProperty = serializedBridge.FindProperty("sdNegativePrompt");
            SerializedProperty promptTemplateProperty = serializedBridge.FindProperty("selectedKeywordPromptTemplate");

            if (useCatalogProperty == null || catalogProperty == null)
            {
                error = "AIPipelineBridge does not contain pre-generated catalog fields. Recompile scripts first.";
                return false;
            }

            StableDiffusionCppSettings settings = settingsProperty != null
                ? settingsProperty.objectReferenceValue as StableDiffusionCppSettings
                : null;
            if (settings == null)
            {
                error = "AIPipelineBridge.sdSettings is not assigned.";
                return false;
            }

            WordPairPool pool = poolProperty != null ? poolProperty.objectReferenceValue as WordPairPool : null;
            if (pool == null)
            {
                error = "AIPipelineBridge.wordPairPool is not assigned.";
                return false;
            }

            if (pool.PairCount <= 0)
            {
                error = "AIPipelineBridge.wordPairPool is empty.";
                return false;
            }

            PreGeneratedObjectImageCatalog catalog = catalogProperty.objectReferenceValue as PreGeneratedObjectImageCatalog;
            if (catalog == null)
            {
                catalog = EnsureCatalogAsset(DefaultCatalogAssetPath);
                catalogProperty.objectReferenceValue = catalog;
                serializedBridge.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(bridge);
            }

            if (catalog == null)
            {
                error = "Failed to create or load the pre-generated catalog asset.";
                return false;
            }

            config = new BridgeGenerationConfig
            {
                bridge = bridge,
                serializedBridge = serializedBridge,
                usePreGeneratedCatalogProperty = useCatalogProperty,
                preGeneratedCatalogProperty = catalogProperty,
                sdSettings = settings,
                sdModelProfileOverride = profileProperty != null
                    ? profileProperty.objectReferenceValue as StableDiffusionCppModelProfile
                    : null,
                wordPairPool = pool,
                objectPromptA = objectPromptAProperty?.stringValue ?? string.Empty,
                objectPromptB = objectPromptBProperty?.stringValue ?? string.Empty,
                sdNegativePrompt = negativePromptProperty?.stringValue ?? string.Empty,
                selectedKeywordPromptTemplate = promptTemplateProperty?.stringValue ?? string.Empty,
                catalog = catalog
            };

            return true;
        }

        private static PreGeneratedObjectImageCatalog EnsureCatalogAsset(string assetPath)
        {
            PreGeneratedObjectImageCatalog existing = AssetDatabase.LoadAssetAtPath<PreGeneratedObjectImageCatalog>(assetPath);
            if (existing != null)
            {
                return existing;
            }

            string directory = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            if (!string.IsNullOrWhiteSpace(directory))
            {
                EnsureAssetFolderExists(directory);
            }

            var created = ScriptableObject.CreateInstance<PreGeneratedObjectImageCatalog>();
            AssetDatabase.CreateAsset(created, assetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return created;
        }

        private static void EnsureAssetFolderExists(string projectRelativeFolderPath)
        {
            if (string.IsNullOrWhiteSpace(projectRelativeFolderPath) || projectRelativeFolderPath == "Assets")
            {
                return;
            }

            string normalized = projectRelativeFolderPath.Replace('\\', '/').Trim('/');
            string[] parts = normalized.Split('/');
            if (parts.Length == 0 || parts[0] != "Assets")
            {
                throw new ArgumentException($"Path must be under Assets: {projectRelativeFolderPath}");
            }

            string current = "Assets";
            for (int i = 1; i < parts.Length; i++)
            {
                string next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }

        private static List<PromptSpec> BuildPromptSpecs(BridgeGenerationConfig config)
        {
            var specs = new List<PromptSpec>();
            var seenKeys = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < config.wordPairPool.PairCount; i++)
            {
                if (!config.wordPairPool.TryGetPairAt(i, out string wordA, out string wordB, out _, out _))
                {
                    continue;
                }

                AddPromptSpec(
                    specs,
                    seenKeys,
                    BuildObjectPromptFromKeyword(wordA, config.objectPromptA, config.selectedKeywordPromptTemplate));
                AddPromptSpec(
                    specs,
                    seenKeys,
                    BuildObjectPromptFromKeyword(wordB, config.objectPromptB, config.selectedKeywordPromptTemplate));
            }

            specs.Sort((left, right) => string.CompareOrdinal(left.key, right.key));
            return specs;
        }

        private static void AddPromptSpec(List<PromptSpec> specs, HashSet<string> seenKeys, string prompt)
        {
            string key = PreGeneratedObjectImageKeyUtility.ComputePromptKey(prompt);
            if (string.IsNullOrWhiteSpace(key) || !seenKeys.Add(key))
            {
                return;
            }

            specs.Add(new PromptSpec
            {
                prompt = prompt,
                key = key,
                fileName = $"wordpool_{key}.png"
            });
        }

        private static string BuildObjectPromptFromKeyword(string keyword, string fallbackPrompt, string promptTemplate)
        {
            string normalizedKeyword = keyword?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(normalizedKeyword))
            {
                return fallbackPrompt ?? string.Empty;
            }

            string template = string.IsNullOrWhiteSpace(promptTemplate)
                ? "{0}"
                : promptTemplate.Trim();
            return template.Contains("{0}", StringComparison.Ordinal)
                ? template.Replace("{0}", normalizedKeyword)
                : $"{template} {normalizedKeyword}".Trim();
        }

        private static StableDiffusionCppGenerationRequest BuildGenerationRequest(
            BridgeGenerationConfig config,
            string prompt,
            string promptKey)
        {
            var request = new StableDiffusionCppGenerationRequest
            {
                prompt = prompt,
                offloadToCpu = config.sdSettings.defaultOffloadToCpu,
                clipOnCpu = config.sdSettings.defaultClipOnCpu,
                vaeTiling = config.sdSettings.defaultVaeTiling,
                diffusionFlashAttention = config.sdSettings.defaultDiffusionFlashAttention,
                useCacheMode = config.sdSettings.defaultUseCacheMode,
                cacheMode = config.sdSettings.defaultCacheMode,
                cacheOption = config.sdSettings.defaultCacheOption,
                cachePreset = config.sdSettings.defaultCachePreset,
                persistOutputToRequestedDirectory = false,
                outputFileName = $"wordpool_{promptKey}",
                outputFormat = "png"
            };

            StableDiffusionCppModelProfile selectedProfile = config.sdModelProfileOverride ?? config.sdSettings.GetActiveModelProfile();
            if (selectedProfile != null)
            {
                selectedProfile.ApplyDefaultsTo(request);
                request.prompt = prompt;
                request.modelPathOverride = selectedProfile.modelPath ?? string.Empty;
                request.controlNetPathOverride = selectedProfile.controlNetPath ?? string.Empty;
            }
            else
            {
                config.sdSettings.TryApplyActiveProfileDefaults(request);
                request.prompt = prompt;
            }

            if (!string.IsNullOrWhiteSpace(config.sdNegativePrompt))
            {
                request.negativePrompt = config.sdNegativePrompt;
            }

            return request;
        }

        private static void CleanupStaleGeneratedFiles(string outputDirectoryAbsolute, HashSet<string> expectedFileNames)
        {
            if (!Directory.Exists(outputDirectoryAbsolute))
            {
                return;
            }

            string[] files = Directory.GetFiles(outputDirectoryAbsolute, "*.png", SearchOption.TopDirectoryOnly);
            for (int i = 0; i < files.Length; i++)
            {
                string filePath = files[i];
                string fileName = Path.GetFileName(filePath);
                if (expectedFileNames.Contains(fileName))
                {
                    continue;
                }

                File.Delete(filePath);
                string metaPath = filePath + ".meta";
                if (File.Exists(metaPath))
                {
                    File.Delete(metaPath);
                }
            }
        }

        private static void UpdateCatalogEntries(
            PreGeneratedObjectImageCatalog catalog,
            string outputDirectoryAbsolute,
            List<PromptSpec> specs)
        {
            var serializedCatalog = new SerializedObject(catalog);
            SerializedProperty entriesProperty = serializedCatalog.FindProperty("entries");
            if (entriesProperty == null)
            {
                throw new InvalidOperationException("Catalog asset does not contain an 'entries' property.");
            }

            entriesProperty.arraySize = specs.Count;
            for (int i = 0; i < specs.Count; i++)
            {
                PromptSpec spec = specs[i];
                SerializedProperty element = entriesProperty.GetArrayElementAtIndex(i);
                SerializedProperty promptProperty = element.FindPropertyRelative("prompt");
                SerializedProperty keyProperty = element.FindPropertyRelative("promptKey");
                SerializedProperty textureProperty = element.FindPropertyRelative("texture");

                if (promptProperty == null || keyProperty == null || textureProperty == null)
                {
                    throw new InvalidOperationException(
                        "Catalog entry schema mismatch. Expected fields: prompt, promptKey, texture.");
                }

                string assetPath = MakeProjectRelativePath(Path.Combine(outputDirectoryAbsolute, spec.fileName));
                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                if (texture == null)
                {
                    throw new InvalidOperationException($"Failed to load generated texture asset: {assetPath}");
                }

                promptProperty.stringValue = spec.prompt;
                keyProperty.stringValue = spec.key;
                textureProperty.objectReferenceValue = texture;
            }

            serializedCatalog.ApplyModifiedPropertiesWithoutUndo();
        }

        private static string ResolveProjectRelativeAbsolutePath(string projectRelativePath)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string normalized = projectRelativePath
                .Replace('\\', Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar);
            return Path.Combine(projectRoot, normalized);
        }

        private static string MakeProjectRelativePath(string absolutePath)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string normalizedRoot = projectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                                    + Path.DirectorySeparatorChar;
            string normalizedPath = Path.GetFullPath(absolutePath);

            if (!normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Path is outside project root: {absolutePath}");
            }

            string relative = normalizedPath.Substring(normalizedRoot.Length);
            return relative.Replace(Path.DirectorySeparatorChar, '/');
        }
    }
}
#endif
