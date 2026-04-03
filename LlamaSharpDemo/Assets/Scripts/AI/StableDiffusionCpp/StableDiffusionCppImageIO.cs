using System;
using System.IO;
using UnityEngine;

public static class StableDiffusionCppImageIO
{
    public static bool TryLoadImageFileAsRawBytes(
        string absolutePath,
        int targetWidth,
        int targetHeight,
        int channelCount,
        out byte[] bytes,
        out int width,
        out int height,
        out string error)
    {
        bytes = null;
        width = 0;
        height = 0;
        error = null;

        if (channelCount != 1 && channelCount != 3 && channelCount != 4)
        {
            error = $"Unsupported channel count: {channelCount}";
            return false;
        }

        if (!TryLoadTextureFromFile(absolutePath, out Texture2D source, out error))
        {
            return false;
        }

        Texture2D readable = source;
        bool ownsReadable = false;

        try
        {
            int resolvedWidth = Mathf.Max(1, targetWidth > 0 ? targetWidth : source.width);
            int resolvedHeight = Mathf.Max(1, targetHeight > 0 ? targetHeight : source.height);

            if (resolvedWidth != source.width || resolvedHeight != source.height)
            {
                if (!TryResizeTexture(source, resolvedWidth, resolvedHeight, out readable, out error))
                {
                    return false;
                }

                ownsReadable = true;
            }

            Color32[] pixels = readable.GetPixels32();
            // Unity's Texture2D pixel buffers are laid out bottom-up, while stable-diffusion.cpp
            // expects top-down image rows. Flip rows here so ControlNet/init/mask inputs keep
            // the same visual orientation as the source texture.
            bytes = ConvertPixelsToTopDownRawBytes(pixels, readable.width, readable.height, channelCount);
            width = readable.width;
            height = readable.height;
            return true;
        }
        catch (Exception ex)
        {
            bytes = null;
            width = 0;
            height = 0;
            error = $"Failed to read image '{absolutePath}' as raw bytes: {ex.Message}";
            return false;
        }
        finally
        {
            if (ownsReadable)
            {
                SafeDestroy(readable);
            }

            SafeDestroy(source);
        }
    }

    public static bool TryWriteRawBytesToImageFile(
        byte[] bytes,
        int width,
        int height,
        int channelCount,
        string absolutePath,
        string outputFormat,
        out string error)
    {
        error = null;

        if (bytes == null || bytes.Length == 0)
        {
            error = "Image byte buffer is empty.";
            return false;
        }

        if (width <= 0 || height <= 0)
        {
            error = $"Invalid image dimensions: {width}x{height}";
            return false;
        }

        if (string.IsNullOrWhiteSpace(absolutePath))
        {
            error = "Target image path is empty.";
            return false;
        }

        TextureFormat textureFormat = ResolveTextureFormat(channelCount, out int bytesPerPixel);
        if (textureFormat == TextureFormat.RGBA32 && channelCount != 4)
        {
            error = $"Unsupported channel count: {channelCount}";
            return false;
        }

        int expectedLength = width * height * bytesPerPixel;
        if (bytes.Length < expectedLength)
        {
            error = $"Image byte buffer is too small. Expected at least {expectedLength} bytes, got {bytes.Length}.";
            return false;
        }

        Texture2D texture = null;
        try
        {
            // stable-diffusion.cpp returns top-down rows, but Texture2D.LoadRawTextureData expects
            // Unity's bottom-up texture memory layout.
            byte[] unityBytes = ConvertTopDownRawBytesToUnityRawBytes(bytes, width, height, bytesPerPixel);

            texture = new Texture2D(width, height, textureFormat, false);
            texture.LoadRawTextureData(unityBytes);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);

            byte[] encoded = string.Equals(outputFormat, "jpg", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(outputFormat, "jpeg", StringComparison.OrdinalIgnoreCase)
                ? texture.EncodeToJPG()
                : texture.EncodeToPNG();
            if (encoded == null || encoded.Length == 0)
            {
                error = $"Failed to encode {outputFormat} image.";
                return false;
            }

            string parent = Path.GetDirectoryName(absolutePath);
            if (!string.IsNullOrWhiteSpace(parent))
            {
                Directory.CreateDirectory(parent);
            }

            File.WriteAllBytes(absolutePath, encoded);
            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to save image '{absolutePath}': {ex.Message}";
            return false;
        }
        finally
        {
            SafeDestroy(texture);
        }
    }
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

    private static byte[] ConvertPixelsToTopDownRawBytes(
        Color32[] pixels,
        int width,
        int height,
        int channelCount)
    {
        if (pixels == null || pixels.Length == 0 || width <= 0 || height <= 0)
        {
            return Array.Empty<byte>();
        }

        var bytes = new byte[pixels.Length * channelCount];
        for (int y = 0; y < height; y++)
        {
            int sourceRow = height - 1 - y;
            int sourceRowOffset = sourceRow * width;
            int targetRowOffset = y * width * channelCount;
            for (int x = 0; x < width; x++)
            {
                Color32 pixel = pixels[sourceRowOffset + x];
                int targetOffset = targetRowOffset + x * channelCount;

                if (channelCount == 1)
                {
                    bytes[targetOffset] = (byte)Mathf.RoundToInt((pixel.r + pixel.g + pixel.b) / 3f);
                    continue;
                }

                bytes[targetOffset] = pixel.r;
                bytes[targetOffset + 1] = pixel.g;
                bytes[targetOffset + 2] = pixel.b;
                if (channelCount == 4)
                {
                    bytes[targetOffset + 3] = pixel.a;
                }
            }
        }

        return bytes;
    }

    private static byte[] ConvertTopDownRawBytesToUnityRawBytes(
        byte[] bytes,
        int width,
        int height,
        int bytesPerPixel)
    {
        if (bytes == null || bytes.Length == 0 || width <= 0 || height <= 0 || bytesPerPixel <= 0)
        {
            return Array.Empty<byte>();
        }

        int rowLength = width * bytesPerPixel;
        var unityBytes = new byte[rowLength * height];
        for (int y = 0; y < height; y++)
        {
            int sourceRowOffset = y * rowLength;
            int targetRowOffset = (height - 1 - y) * rowLength;
            Buffer.BlockCopy(bytes, sourceRowOffset, unityBytes, targetRowOffset, rowLength);
        }

        return unityBytes;
    }

    private static TextureFormat ResolveTextureFormat(int channelCount, out int bytesPerPixel)
    {
        switch (channelCount)
        {
            case 1:
                bytesPerPixel = 1;
                return TextureFormat.R8;
            case 3:
                bytesPerPixel = 3;
                return TextureFormat.RGB24;
            case 4:
                bytesPerPixel = 4;
                return TextureFormat.RGBA32;
            default:
                bytesPerPixel = 0;
                return TextureFormat.RGBA32;
        }
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
