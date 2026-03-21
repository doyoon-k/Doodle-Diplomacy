using System;
using UnityEngine;

/// <summary>
/// Helpers for turning pipeline image values into readable Texture2D instances for VLM requests.
/// </summary>
public static class PipelineImageUtility
{
    public sealed class ImageNormalizationResult : IDisposable
    {
        public ImageNormalizationResult(Texture2D texture, bool ownsTexture, string sourceSummary)
        {
            Texture = texture;
            OwnsTexture = ownsTexture;
            SourceSummary = sourceSummary ?? string.Empty;
        }

        public Texture2D Texture { get; }
        public bool OwnsTexture { get; }
        public string SourceSummary { get; }

        public void Dispose()
        {
            if (!OwnsTexture || Texture == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(Texture);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(Texture);
            }
        }
    }

    public static bool TryResolveFromState(
        PipelineState state,
        string key,
        int resizeLongestSide,
        out ImageNormalizationResult result,
        out string error)
    {
        result = null;
        error = null;

        if (state == null)
        {
            error = "PipelineState is null.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            error = "Image state key is empty.";
            return false;
        }

        if (!state.TryGetImageSource(key, out UnityEngine.Object imageSource) || imageSource == null)
        {
            error = $"State key '{key}' does not contain a Texture2D or Sprite.";
            return false;
        }

        return TryNormalize(imageSource, resizeLongestSide, out result, out error);
    }

    public static bool TryNormalize(
        UnityEngine.Object imageSource,
        int resizeLongestSide,
        out ImageNormalizationResult result,
        out string error)
    {
        result = null;
        error = null;

        if (imageSource == null)
        {
            error = "Image source is null.";
            return false;
        }

        return imageSource switch
        {
            Texture2D texture => TryNormalizeTexture(texture, resizeLongestSide, out result, out error),
            Sprite sprite => TryNormalizeSprite(sprite, resizeLongestSide, out result, out error),
            _ => Fail($"Unsupported image source type: {imageSource.GetType().Name}", out result, out error)
        };
    }

    public static bool TryEncodeToPng(Texture2D texture, out byte[] pngBytes, out string error)
    {
        pngBytes = null;
        error = null;

        if (texture == null)
        {
            error = "Texture is null.";
            return false;
        }

        try
        {
            pngBytes = texture.EncodeToPNG();
            if (pngBytes == null || pngBytes.Length == 0)
            {
                error = "Texture.EncodeToPNG returned no data.";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to encode texture to PNG: {ex.Message}";
            return false;
        }
    }

    private static bool TryNormalizeTexture(
        Texture2D source,
        int resizeLongestSide,
        out ImageNormalizationResult result,
        out string error)
    {
        result = null;
        error = null;

        if (source == null)
        {
            error = "Texture source is null.";
            return false;
        }

        bool ownsReadable = false;
        if (!TryGetReadableTexture(source, out Texture2D readable, out ownsReadable, out error))
        {
            return false;
        }

        Texture2D finalTexture = readable;
        bool ownsFinal = ownsReadable;

        if (resizeLongestSide > 0 && NeedsResize(readable, resizeLongestSide))
        {
            if (!TryResizeTexture(readable, resizeLongestSide, out Texture2D resized, out error))
            {
                if (ownsReadable)
                {
                    SafeDestroy(readable);
                }

                return false;
            }

            if (ownsReadable)
            {
                SafeDestroy(readable);
            }

            finalTexture = resized;
            ownsFinal = true;
        }

        result = new ImageNormalizationResult(
            finalTexture,
            ownsFinal,
            $"{source.name} ({source.width}x{source.height})");
        return true;
    }

    private static bool TryNormalizeSprite(
        Sprite sprite,
        int resizeLongestSide,
        out ImageNormalizationResult result,
        out string error)
    {
        result = null;
        error = null;

        if (sprite == null)
        {
            error = "Sprite source is null.";
            return false;
        }

        if (sprite.texture == null)
        {
            error = $"Sprite '{sprite.name}' has no backing texture.";
            return false;
        }

        bool ownsReadable = false;
        if (!TryGetReadableTexture(sprite.texture, out Texture2D readable, out ownsReadable, out error))
        {
            return false;
        }

        if (!TryCropSprite(readable, sprite, out Texture2D cropped, out error))
        {
            if (ownsReadable)
            {
                SafeDestroy(readable);
            }

            return false;
        }

        if (ownsReadable)
        {
            SafeDestroy(readable);
        }

        Texture2D finalTexture = cropped;
        if (resizeLongestSide > 0 && NeedsResize(cropped, resizeLongestSide))
        {
            if (!TryResizeTexture(cropped, resizeLongestSide, out Texture2D resized, out error))
            {
                SafeDestroy(cropped);
                return false;
            }

            SafeDestroy(cropped);
            finalTexture = resized;
        }

        Rect rect = sprite.rect;
        result = new ImageNormalizationResult(
            finalTexture,
            true,
            $"{sprite.name} ({rect.width}x{rect.height})");
        return true;
    }

    private static bool TryGetReadableTexture(
        Texture2D source,
        out Texture2D readable,
        out bool ownsTexture,
        out string error)
    {
        readable = null;
        ownsTexture = false;
        error = null;

        if (source == null)
        {
            error = "Texture source is null.";
            return false;
        }

        if (source.isReadable)
        {
            readable = source;
            return true;
        }

        if (!TryBlitToReadableTexture(source, source.width, source.height, out readable, out error))
        {
            return false;
        }

        ownsTexture = true;
        return true;
    }

    private static bool TryCropSprite(
        Texture2D readableTexture,
        Sprite sprite,
        out Texture2D cropped,
        out string error)
    {
        cropped = null;
        error = null;

        try
        {
            Rect rect = sprite.rect;
            int x = Mathf.RoundToInt(rect.x);
            int y = Mathf.RoundToInt(rect.y);
            int width = Mathf.RoundToInt(rect.width);
            int height = Mathf.RoundToInt(rect.height);

            Color[] pixels = readableTexture.GetPixels(x, y, width, height);
            cropped = new Texture2D(width, height, TextureFormat.RGBA32, false);
            cropped.SetPixels(pixels);
            cropped.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            cropped.name = $"{sprite.name}_PipelineCrop";
            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to crop sprite '{sprite.name}': {ex.Message}";
            return false;
        }
    }

    private static bool TryResizeTexture(
        Texture2D source,
        int maxLongestSide,
        out Texture2D resized,
        out string error)
    {
        resized = null;
        error = null;

        int width = source.width;
        int height = source.height;
        float scale = maxLongestSide / (float)Math.Max(width, height);
        int targetWidth = Math.Max(1, Mathf.RoundToInt(width * scale));
        int targetHeight = Math.Max(1, Mathf.RoundToInt(height * scale));

        return TryBlitToReadableTexture(source, targetWidth, targetHeight, out resized, out error, $"{source.name}_PipelineResize");
    }

    private static bool TryBlitToReadableTexture(
        Texture source,
        int targetWidth,
        int targetHeight,
        out Texture2D texture,
        out string error,
        string textureName = null)
    {
        texture = null;
        error = null;

        RenderTexture temp = RenderTexture.GetTemporary(targetWidth, targetHeight, 0, RenderTextureFormat.ARGB32);
        RenderTexture previous = RenderTexture.active;

        try
        {
            Graphics.Blit(source, temp);
            RenderTexture.active = temp;

            texture = new Texture2D(targetWidth, targetHeight, TextureFormat.RGBA32, false);
            texture.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            texture.name = string.IsNullOrWhiteSpace(textureName) ? $"{source.name}_ReadableCopy" : textureName;
            return true;
        }
        catch (Exception ex)
        {
            if (texture != null)
            {
                SafeDestroy(texture);
                texture = null;
            }

            error = $"Failed to create readable texture copy: {ex.Message}";
            return false;
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(temp);
        }
    }

    private static bool NeedsResize(Texture2D texture, int maxLongestSide)
    {
        return Math.Max(texture.width, texture.height) > maxLongestSide;
    }

    private static bool Fail(string message, out ImageNormalizationResult result, out string error)
    {
        result = null;
        error = message;
        return false;
    }

    private static void SafeDestroy(UnityEngine.Object obj)
    {
        if (obj == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            UnityEngine.Object.Destroy(obj);
        }
        else
        {
            UnityEngine.Object.DestroyImmediate(obj);
        }
    }
}
