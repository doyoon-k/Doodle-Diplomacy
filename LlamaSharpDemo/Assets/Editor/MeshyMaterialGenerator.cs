using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.IO;

public class MeshyMaterialGenerator : Editor
{
    private const string LogPrefix = "[Tripo/Meshy Material Generator]";

    private static readonly string[] BaseSuffixes =
    {
        "_texture",
        "_basecolor",
        "_base_color",
        "_base"
    };

    private static readonly string[] MetallicSuffixes =
    {
        "_metallic",
        "_metalic",
        "_metalness"
    };

    private static readonly string[] NormalSuffixes =
    {
        "_normal",
        "_normalgl",
        "_normal_gl"
    };

    private static readonly string[] RoughnessSuffixes =
    {
        "_roughness",
        "_rough"
    };

    private sealed class TextureSet
    {
        public string RootName;
        public string BasePath;
        public string MetallicPath;
        public string NormalPath;
        public string RoughnessPath;
    }

    [MenuItem("Assets/Tripo AI/Generate Material(s) Here", false, 20)]
    [MenuItem("Assets/Meshy AI/Generate Material(s) Here", false, 21)]
    public static void GenerateMaterialsFromSelection()
    {
        Object[] selectedObjects = Selection.GetFiltered(typeof(Object), SelectionMode.Assets);
        if (selectedObjects == null || selectedObjects.Length == 0)
        {
            Debug.LogWarning($"{LogPrefix} Please select a folder containing textures, or select texture assets directly.");
            return;
        }

        HashSet<string> processedFolders = new HashSet<string>();

        foreach (Object selectedObject in selectedObjects)
        {
            string path = AssetDatabase.GetAssetPath(selectedObject);
            string absolutePath = AssetPathToAbsolute(path);
            string directory = Directory.Exists(absolutePath) ? path : Path.GetDirectoryName(path);

            if (string.IsNullOrEmpty(directory))
            {
                continue;
            }

            directory = directory.Replace("\\", "/");
            if (processedFolders.Add(directory))
            {
                ProcessFolder(directory);
            }
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"{LogPrefix} Finished generating materials.");
    }

    private static void ProcessFolder(string folderPath)
    {
        string absoluteFolderPath = AssetPathToAbsolute(folderPath);
        if (!Directory.Exists(absoluteFolderPath))
        {
            return;
        }

        string[] allFiles = Directory.GetFiles(absoluteFolderPath, "*.*");
        Dictionary<string, TextureSet> textureSets = new Dictionary<string, TextureSet>();

        foreach (string file in allFiles)
        {
            if (file.EndsWith(".meta"))
            {
                continue;
            }

            string fileName = Path.GetFileNameWithoutExtension(file);
            string fileNameLower = fileName.ToLowerInvariant();
            string extension = Path.GetExtension(file);

            if (!IsSupportedTextureExtension(extension))
            {
                continue;
            }

            string rootName;
            string relativeAssetPath = folderPath + "/" + Path.GetFileName(file);

            if (TryExtractRootName(fileName, fileNameLower, BaseSuffixes, out rootName))
            {
                GetOrCreateSet(textureSets, rootName).BasePath = relativeAssetPath;
                continue;
            }

            if (TryExtractRootName(fileName, fileNameLower, MetallicSuffixes, out rootName))
            {
                GetOrCreateSet(textureSets, rootName).MetallicPath = relativeAssetPath;
                continue;
            }

            if (TryExtractRootName(fileName, fileNameLower, NormalSuffixes, out rootName))
            {
                GetOrCreateSet(textureSets, rootName).NormalPath = relativeAssetPath;
                continue;
            }

            if (TryExtractRootName(fileName, fileNameLower, RoughnessSuffixes, out rootName))
            {
                GetOrCreateSet(textureSets, rootName).RoughnessPath = relativeAssetPath;
            }
        }

        int createdCount = 0;
        foreach (TextureSet textureSet in textureSets.Values)
        {
            if (string.IsNullOrEmpty(textureSet.BasePath))
            {
                continue;
            }

            CreateMaterialForSet(folderPath, textureSet);
            createdCount++;
        }

        if (createdCount == 0)
        {
            Debug.LogWarning($"{LogPrefix} No base texture set found in {folderPath}. Supported base suffixes: {string.Join(", ", BaseSuffixes)}");
        }
    }

    private static TextureSet GetOrCreateSet(Dictionary<string, TextureSet> sets, string rootName)
    {
        string key = rootName.ToLowerInvariant();

        TextureSet textureSet;
        if (!sets.TryGetValue(key, out textureSet))
        {
            textureSet = new TextureSet { RootName = rootName };
            sets.Add(key, textureSet);
        }

        return textureSet;
    }

    private static bool TryExtractRootName(string originalName, string lowerName, string[] suffixes, out string rootName)
    {
        foreach (string suffix in suffixes)
        {
            if (!lowerName.EndsWith(suffix))
            {
                continue;
            }

            rootName = originalName.Substring(0, originalName.Length - suffix.Length);
            rootName = rootName.TrimEnd('_', '-', ' ');
            if (string.IsNullOrEmpty(rootName))
            {
                rootName = "Generated";
            }

            return true;
        }

        rootName = null;
        return false;
    }

    private static bool IsSupportedTextureExtension(string extension)
    {
        string ext = extension.ToLowerInvariant();
        return ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".tga" || ext == ".tif" || ext == ".tiff" || ext == ".bmp";
    }

    private static void CreateMaterialForSet(string folderPath, TextureSet textureSet)
    {
        string materialPath = folderPath + "/M_" + textureSet.RootName + ".mat";

        Texture2D baseTexture = LoadTexture(textureSet.BasePath, TextureKind.BaseColor);
        Texture2D metallicTexture = LoadTexture(textureSet.MetallicPath, TextureKind.Metallic);
        Texture2D normalTexture = LoadTexture(textureSet.NormalPath, TextureKind.Normal);
        Texture2D roughnessTexture = LoadTexture(textureSet.RoughnessPath, TextureKind.Roughness);

        Texture2D metallicSmoothnessTexture = null;
        if (metallicTexture != null && roughnessTexture != null)
        {
            string packedPath = folderPath + "/" + textureSet.RootName + "_metallicSmoothness.png";
            metallicSmoothnessTexture = CreateMetallicSmoothnessTexture(packedPath, metallicTexture, roughnessTexture);
        }
        else if (metallicTexture != null)
        {
            metallicSmoothnessTexture = metallicTexture;
        }

        Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        bool isNew = material == null;

        if (isNew)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            material = new Material(shader);
            AssetDatabase.CreateAsset(material, materialPath);
        }

        if (baseTexture != null)
        {
            if (material.HasProperty("_BaseMap"))
            {
                material.SetTexture("_BaseMap", baseTexture);
            }

            if (material.HasProperty("_MainTex"))
            {
                material.SetTexture("_MainTex", baseTexture);
            }
        }

        if (metallicSmoothnessTexture != null && material.HasProperty("_MetallicGlossMap"))
        {
            material.SetTexture("_MetallicGlossMap", metallicSmoothnessTexture);
            material.EnableKeyword("_METALLICSPECGLOSSMAP");

            if (material.HasProperty("_Metallic"))
            {
                material.SetFloat("_Metallic", 1f);
            }

            if (material.HasProperty("_SmoothnessTextureChannel"))
            {
                material.SetFloat("_SmoothnessTextureChannel", 0f);
            }
        }

        if (normalTexture != null && material.HasProperty("_BumpMap"))
        {
            material.SetTexture("_BumpMap", normalTexture);
            material.EnableKeyword("_NORMALMAP");
        }

        if (material.HasProperty("_Smoothness") && roughnessTexture != null)
        {
            material.SetFloat("_Smoothness", 1f);
        }

        EditorUtility.SetDirty(material);

        if (string.IsNullOrEmpty(textureSet.MetallicPath) || string.IsNullOrEmpty(textureSet.RoughnessPath))
        {
            Debug.LogWarning($"{LogPrefix} {textureSet.RootName}: Metallic/Roughness pair was incomplete. Smoothness packing skipped.");
        }

        Debug.Log($"{LogPrefix} Created/Updated material: {materialPath}", material);
        Selection.activeObject = material;
        EditorGUIUtility.PingObject(material);
    }

    private enum TextureKind
    {
        BaseColor,
        Metallic,
        Normal,
        Roughness
    }

    private static Texture2D LoadTexture(string path, TextureKind kind)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        ConfigureImporter(path, kind);
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    private static void ConfigureImporter(string path, TextureKind kind)
    {
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
        {
            return;
        }

        bool changed = false;

        TextureImporterType targetType = kind == TextureKind.Normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        if (importer.textureType != targetType)
        {
            importer.textureType = targetType;
            changed = true;
        }

        bool shouldUseSrgb = kind == TextureKind.BaseColor;
        if (importer.sRGBTexture != shouldUseSrgb)
        {
            importer.sRGBTexture = shouldUseSrgb;
            changed = true;
        }

        if (changed)
        {
            importer.SaveAndReimport();
        }
    }

    private static Texture2D CreateMetallicSmoothnessTexture(string targetAssetPath, Texture2D metallicTexture, Texture2D roughnessTexture)
    {
        int width = Mathf.Max(1, Mathf.Max(metallicTexture.width, roughnessTexture.width));
        int height = Mathf.Max(1, Mathf.Max(metallicTexture.height, roughnessTexture.height));

        Texture2D metallicReadable = CreateReadableLinearCopy(metallicTexture, width, height);
        Texture2D roughnessReadable = CreateReadableLinearCopy(roughnessTexture, width, height);
        Texture2D packedTexture = new Texture2D(width, height, TextureFormat.RGBA32, false, true);

        Color[] colors = new Color[width * height];
        for (int y = 0; y < height; y++)
        {
            float v = (y + 0.5f) / height;
            for (int x = 0; x < width; x++)
            {
                float u = (x + 0.5f) / width;
                Color metallic = metallicReadable.GetPixelBilinear(u, v);
                Color roughness = roughnessReadable.GetPixelBilinear(u, v);

                float smoothness = Mathf.Clamp01(1f - roughness.r);
                colors[(y * width) + x] = new Color(metallic.r, metallic.g, metallic.b, smoothness);
            }
        }

        packedTexture.SetPixels(colors);
        packedTexture.Apply(false, false);

        byte[] pngBytes = packedTexture.EncodeToPNG();
        string absolutePath = AssetPathToAbsolute(targetAssetPath);
        File.WriteAllBytes(absolutePath, pngBytes);

        AssetDatabase.ImportAsset(targetAssetPath, ImportAssetOptions.ForceUpdate);
        ConfigureImporter(targetAssetPath, TextureKind.Metallic);

        Object.DestroyImmediate(metallicReadable);
        Object.DestroyImmediate(roughnessReadable);
        Object.DestroyImmediate(packedTexture);

        return AssetDatabase.LoadAssetAtPath<Texture2D>(targetAssetPath);
    }

    private static Texture2D CreateReadableLinearCopy(Texture2D source, int width, int height)
    {
        RenderTexture renderTexture = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        Graphics.Blit(source, renderTexture);

        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = renderTexture;

        Texture2D readableTexture = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
        readableTexture.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
        readableTexture.Apply(false, false);

        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(renderTexture);
        return readableTexture;
    }

    private static string AssetPathToAbsolute(string assetPath)
    {
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        return Path.GetFullPath(Path.Combine(projectRoot, assetPath));
    }
}
