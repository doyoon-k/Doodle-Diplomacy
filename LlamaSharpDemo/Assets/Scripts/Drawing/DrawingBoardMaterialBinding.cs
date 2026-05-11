using UnityEngine;
using UnityEngine.Rendering;

internal static class DrawingBoardMaterialBinding
{
    public static Material CreateRuntimeMaterial(
        Material sourceMaterial,
        string ownerName,
        Texture2D displayTexture,
        string texturePropertyName,
        Vector2 textureScale,
        Vector2 textureOffset,
        HideFlags hideFlags)
    {
        if (sourceMaterial == null)
        {
            return null;
        }

        Material runtimeMaterial = new(sourceMaterial)
        {
            name = $"{ownerName}_DrawingBoardMaterial",
            hideFlags = hideFlags
        };

        ConfigureDisplayMaterial(runtimeMaterial, displayTexture, texturePropertyName, textureScale, textureOffset);
        return runtimeMaterial;
    }

    public static void EnsureBinding(
        Renderer renderer,
        Material runtimeMaterial,
        Texture2D displayTexture,
        string texturePropertyName,
        Vector2 textureScale,
        Vector2 textureOffset)
    {
        if (renderer == null || runtimeMaterial == null || displayTexture == null)
        {
            return;
        }

        if (renderer.sharedMaterial != runtimeMaterial)
        {
            renderer.sharedMaterial = runtimeMaterial;
        }

        if (runtimeMaterial.mainTexture != displayTexture)
        {
            ConfigureDisplayMaterial(runtimeMaterial, displayTexture, texturePropertyName, textureScale, textureOffset);
        }

        ConfigureRenderer(renderer);
    }

    private static void ConfigureDisplayMaterial(
        Material material,
        Texture2D texture,
        string texturePropertyName,
        Vector2 textureScale,
        Vector2 textureOffset)
    {
        if (material == null || texture == null)
        {
            return;
        }

        AssignTexture(material, texture, texturePropertyName, textureScale, textureOffset);

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", Color.white);
        }

        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", Color.white);
        }

        if (material.HasProperty("_Metallic"))
        {
            material.SetFloat("_Metallic", 0f);
        }

        if (material.HasProperty("_Smoothness"))
        {
            material.SetFloat("_Smoothness", 0f);
        }

        if (material.HasProperty("_EmissionMap"))
        {
            material.SetTexture("_EmissionMap", texture);
            material.SetTextureScale("_EmissionMap", textureScale);
            material.SetTextureOffset("_EmissionMap", textureOffset);
            material.EnableKeyword("_EMISSION");
        }

        if (material.HasProperty("_EmissionColor"))
        {
            material.SetColor("_EmissionColor", Color.white * 2.2f);
        }
    }

    private static void AssignTexture(
        Material material,
        Texture2D texture,
        string texturePropertyName,
        Vector2 textureScale,
        Vector2 textureOffset)
    {
        material.mainTexture = texture;
        material.mainTextureScale = textureScale;
        material.mainTextureOffset = textureOffset;

        if (!string.IsNullOrWhiteSpace(texturePropertyName) && material.HasProperty(texturePropertyName))
        {
            material.SetTexture(texturePropertyName, texture);
            material.SetTextureScale(texturePropertyName, textureScale);
            material.SetTextureOffset(texturePropertyName, textureOffset);
        }
    }

    private static void ConfigureRenderer(Renderer renderer)
    {
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
    }
}
