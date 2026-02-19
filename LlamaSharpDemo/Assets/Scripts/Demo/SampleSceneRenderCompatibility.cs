using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

public static class SampleSceneRenderCompatibility
{
    private static Material _fallbackSpriteMaterial;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void ApplyFallbackIfNeeded()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        if (!string.Equals(activeScene.name, "SampleScene", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // Keep intended visuals only when URP + 2D renderer are actually active.
        if (IsUrp2DRendererActive())
        {
            return;
        }

        Shader spriteDefaultShader = Shader.Find("Sprites/Default");
        if (spriteDefaultShader == null)
        {
            Debug.LogWarning("[SampleSceneRenderCompatibility] Could not find 'Sprites/Default' shader for fallback.");
            return;
        }

        if (_fallbackSpriteMaterial == null)
        {
            _fallbackSpriteMaterial = new Material(spriteDefaultShader)
            {
                name = "SampleScene_SpritesDefault_Fallback",
                hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild
            };
        }

        int spriteRendererCount = 0;
        foreach (SpriteRenderer spriteRenderer in UnityEngine.Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None))
        {
            if (spriteRenderer == null)
            {
                continue;
            }

            spriteRenderer.sharedMaterial = _fallbackSpriteMaterial;
            spriteRendererCount++;
        }

        int tilemapRendererCount = 0;
        foreach (TilemapRenderer tilemapRenderer in UnityEngine.Object.FindObjectsByType<TilemapRenderer>(FindObjectsSortMode.None))
        {
            if (tilemapRenderer == null)
            {
                continue;
            }

            tilemapRenderer.sharedMaterial = _fallbackSpriteMaterial;
            tilemapRendererCount++;
        }

        Debug.LogWarning(
            $"[SampleSceneRenderCompatibility] Non-URP project detected. Applied Sprites/Default fallback to " +
            $"{spriteRendererCount} SpriteRenderer(s) and {tilemapRendererCount} TilemapRenderer(s).");
    }

    private static bool IsUrp2DRendererActive()
    {
        RenderPipelineAsset activePipeline = GraphicsSettings.currentRenderPipeline;
        if (activePipeline == null)
        {
            return false;
        }

        string typeName = activePipeline.GetType().FullName ?? string.Empty;
        if (typeName.IndexOf("UniversalRenderPipeline", StringComparison.OrdinalIgnoreCase) < 0)
        {
            return false;
        }

        object renderer = TryGetUrpRenderer(activePipeline);
        if (renderer == null)
        {
            return false;
        }

        string rendererTypeName = renderer.GetType().FullName ?? string.Empty;
        return rendererTypeName.IndexOf("Renderer2D", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static object TryGetUrpRenderer(RenderPipelineAsset pipelineAsset)
    {
        Type pipelineType = pipelineAsset.GetType();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        // Newer URP versions expose scriptableRenderer directly.
        PropertyInfo scriptableRendererProperty = pipelineType.GetProperty("scriptableRenderer", flags);
        if (scriptableRendererProperty != null)
        {
            object renderer = scriptableRendererProperty.GetValue(pipelineAsset);
            if (renderer != null)
            {
                return renderer;
            }
        }

        // Fallback path for versions exposing GetRenderer(index).
        MethodInfo getRendererMethod = pipelineType.GetMethod("GetRenderer", flags, null, new[] { typeof(int) }, null);
        if (getRendererMethod != null)
        {
            object renderer = getRendererMethod.Invoke(pipelineAsset, new object[] { 0 });
            if (renderer != null)
            {
                return renderer;
            }
        }

        return null;
    }
}
