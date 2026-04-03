using System;
using System.IO;
using UnityEngine;

public static class StableDiffusionCppImageIO
{
    public static bool TryWriteTextureToUniqueTempPng(
        Texture2D source,
        string directory,
        string filePrefix,
        out string absolutePath,
        out string error)
    {
        absolutePath = string.Empty;
        error = null;

        if (string.IsNullOrWhiteSpace(directory))
        {
            error = "Temporary image directory is empty.";
            return false;
        }

        string prefix = SanitizeFileToken(string.IsNullOrWhiteSpace(filePrefix) ? "image" : filePrefix.Trim());
        string fileName =
            prefix + "_" +
            DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "_" +
            Guid.NewGuid().ToString("N").Substring(0, 8) +
            ".png";
        absolutePath = Path.Combine(directory, fileName);
        return TryWriteTextureToPng(source, absolutePath, out error);
    }

    public static bool TryWriteTextureToPng(Texture2D source, string absolutePath, out string error)
    {
        error = null;
        if (source == null)
        {
            error = "Texture is null.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(absolutePath))
        {
            error = "Target image path is empty.";
            return false;
        }

        bool ownsReadable = false;
        if (!TryGetReadableTexture(source, out Texture2D readable, out ownsReadable, out error))
        {
            return false;
        }

        try
        {
            byte[] pngBytes = readable.EncodeToPNG();
            if (pngBytes == null || pngBytes.Length == 0)
            {
                error = "Texture.EncodeToPNG returned no data.";
                return false;
            }

            string parent = Path.GetDirectoryName(absolutePath);
            if (!string.IsNullOrWhiteSpace(parent))
            {
                Directory.CreateDirectory(parent);
            }

            File.WriteAllBytes(absolutePath, pngBytes);
            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to save PNG '{absolutePath}': {ex.Message}";
            return false;
        }
        finally
        {
            if (ownsReadable)
            {
                SafeDestroy(readable);
            }
        }
    }

    public static bool TryLoadTextureFromFile(string absolutePath, out Texture2D texture, out string error)
    {
        texture = null;
        error = null;

        if (string.IsNullOrWhiteSpace(absolutePath))
        {
            error = "Image path is empty.";
            return false;
        }

        if (!File.Exists(absolutePath))
        {
            error = $"Image file not found: {absolutePath}";
            return false;
        }

        try
        {
            byte[] data = File.ReadAllBytes(absolutePath);
            texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            {
                name = Path.GetFileNameWithoutExtension(absolutePath)
            };

            if (!texture.LoadImage(data))
            {
                SafeDestroy(texture);
                texture = null;
                error = $"Texture.LoadImage failed for '{absolutePath}'.";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            SafeDestroy(texture);
            texture = null;
            error = $"Failed to load image '{absolutePath}': {ex.Message}";
            return false;
        }
    }

    public static bool TryGetImageSizeFromFile(string absolutePath, out Vector2Int size, out string error)
    {
        size = default;
        error = null;

        if (!TryLoadTextureFromFile(absolutePath, out Texture2D texture, out error))
        {
            return false;
        }

        try
        {
            size = new Vector2Int(texture.width, texture.height);
            return true;
        }
        finally
        {
            SafeDestroy(texture);
        }
    }

    public static bool TryResizeTexture(
        Texture2D source,
        int targetWidth,
        int targetHeight,
        out Texture2D resized,
        out string error)
    {
        resized = null;
        error = null;

        if (source == null)
        {
            error = "Texture is null.";
            return false;
        }

        int width = Mathf.Max(1, targetWidth);
        int height = Mathf.Max(1, targetHeight);
        return TryBlitToReadableTexture(source, width, height, out resized, out error);
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
            error = "Texture is null.";
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

    private static bool TryBlitToReadableTexture(
        Texture source,
        int targetWidth,
        int targetHeight,
        out Texture2D readable,
        out string error)
    {
        readable = null;
        error = null;

        RenderTexture temp = RenderTexture.GetTemporary(targetWidth, targetHeight, 0, RenderTextureFormat.ARGB32);
        RenderTexture previous = RenderTexture.active;

        try
        {
            Graphics.Blit(source, temp);
            RenderTexture.active = temp;

            readable = new Texture2D(targetWidth, targetHeight, TextureFormat.RGBA32, false)
            {
                name = $"{source.name}_ReadableCopy"
            };
            readable.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
            readable.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            return true;
        }
        catch (Exception ex)
        {
            SafeDestroy(readable);
            readable = null;
            error = $"Failed to create readable texture copy: {ex.Message}";
            return false;
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(temp);
        }
    }

    private static string SanitizeFileToken(string value)
    {
        string sanitized = value;
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            sanitized = sanitized.Replace(invalid, '_');
        }

        return string.IsNullOrWhiteSpace(sanitized) ? "image" : sanitized;
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
