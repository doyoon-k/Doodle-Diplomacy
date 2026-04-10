using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;

public class MeshyMaterialGenerator : Editor
{
    // Creates a right-click menu option in the Project view under "Assets/Meshy AI"
    [MenuItem("Assets/Meshy AI/Generate Material(s) Here", false, 20)]
    public static void GenerateMaterialsFromSelection()
    {
        Object[] selectedObjects = Selection.GetFiltered(typeof(Object), SelectionMode.Assets);
        if (selectedObjects == null || selectedObjects.Length == 0)
        {
            Debug.LogWarning("[Meshy Material Generator] Please select a folder containing the textures, or the textures themselves.");
            return;
        }

        HashSet<string> processedFolders = new HashSet<string>();

        foreach (Object obj in selectedObjects)
        {
            string path = AssetDatabase.GetAssetPath(obj);
            string absolutePath = Path.Combine(Directory.GetCurrentDirectory(), path);

            // Determine if the selected object is a folder or a file
            string directory = Directory.Exists(absolutePath) ? path : Path.GetDirectoryName(path);
            
            // Reformat path to use Unity's forward slashes
            directory = directory.Replace("\\", "/");

            if (processedFolders.Add(directory))
            {
                ProcessFolder(directory);
            }
        }
        
        Debug.Log("[Meshy Material Generator] Finished generating materials!");
    }

    private static void ProcessFolder(string folderPath)
    {
        List<string> baseTexturePaths = new List<string>();

        // Get all files in the directory
        string absoluteFolderPath = Path.Combine(Directory.GetCurrentDirectory(), folderPath);
        if (!Directory.Exists(absoluteFolderPath)) return;

        string[] allFiles = Directory.GetFiles(absoluteFolderPath, "*.*");
        foreach (string file in allFiles)
        {
            if (file.EndsWith(".meta")) continue; // Skip meta files

            string fileName = Path.GetFileNameWithoutExtension(file);

            // Identify the base texture (Meshy names the base color texture ending with "_texture")
            if (fileName.EndsWith("_texture"))
            {
                // Convert back to relative Unity path
                string relativePath = folderPath + "/" + Path.GetFileName(file);
                baseTexturePaths.Add(relativePath);
            }
        }

        if (baseTexturePaths.Count == 0)
        {
            Debug.LogWarning($"[Meshy Material Generator] No base textures (ending with '_texture') found in {folderPath}");
            return;
        }

        // Process each identified base texture to generate a unique material
        foreach (string basePath in baseTexturePaths)
        {
            CreateMaterialForBase(folderPath, basePath);
        }
    }

    private static void CreateMaterialForBase(string folderPath, string basePath)
    {
        string extension = Path.GetExtension(basePath);
        string baseName = Path.GetFileNameWithoutExtension(basePath);
        
        // Construct expected paths for the other maps
        string metallicPath = folderPath + "/" + baseName + "_metallic" + extension;
        string normalPath = folderPath + "/" + baseName + "_normal" + extension;
        
        // Define Material Name
        string matNamePart = baseName;
        if (matNamePart.EndsWith("_texture"))
        {
            matNamePart = matNamePart.Substring(0, matNamePart.Length - "_texture".Length); // Remove "_texture" suffix
        }
        string matPath = folderPath + "/M_" + matNamePart + ".mat";

        // Load Textures
        Texture2D baseTex = AssetDatabase.LoadAssetAtPath<Texture2D>(basePath);
        Texture2D metallicTex = AssetDatabase.LoadAssetAtPath<Texture2D>(metallicPath);
        Texture2D normalTex = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);

        // Auto-fix normal map import settings
        if (normalTex != null)
        {
            TextureImporter normalImporter = AssetImporter.GetAtPath(normalPath) as TextureImporter;
            if (normalImporter != null && normalImporter.textureType != TextureImporterType.NormalMap)
            {
                normalImporter.textureType = TextureImporterType.NormalMap;
                normalImporter.SaveAndReimport();
            }
        }

        // Check if material already exists, otherwise create
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        bool isNew = false;
        
        if (mat == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            
            mat = new Material(shader);
            isNew = true;
        }

        // Assign textures
        if (mat.shader.name.Contains("Universal Render Pipeline"))
        {
            if (baseTex != null) mat.SetTexture("_BaseMap", baseTex);
            if (metallicTex != null) mat.SetTexture("_MetallicGlossMap", metallicTex);
            if (normalTex != null) mat.SetTexture("_BumpMap", normalTex);
        }
        else
        {
            if (baseTex != null) mat.SetTexture("_MainTex", baseTex);
            if (metallicTex != null) mat.SetTexture("_MetallicGlossMap", metallicTex);
            if (normalTex != null) mat.SetTexture("_BumpMap", normalTex);
        }

        // Save
        if (isNew)
        {
            AssetDatabase.CreateAsset(mat, matPath);
        }
        else
        {
            EditorUtility.SetDirty(mat);
        }

        AssetDatabase.SaveAssets();

        Debug.Log($"[Meshy Material Generator] Created Material: {matPath}", mat);
        
        // Highlight the newly created material
        Selection.activeObject = mat;
        EditorGUIUtility.PingObject(mat);
    }
}
