using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Converts generated images into transparent sticker textures by removing border-connected background pixels.
/// </summary>
public static class DrawingStickerExtractor
{
    private const HideFlags RuntimeHideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;

    public static bool TryExtractTransparentSticker(
        Texture2D sourceTexture,
        RectInt preferredRegion,
        int regionPadding,
        float backgroundTolerance,
        int trimPadding,
        FilterMode filterMode,
        out Texture2D stickerTexture,
        out RectInt placementRegion,
        out string error)
    {
        stickerTexture = null;
        placementRegion = default;
        error = null;

        if (sourceTexture == null)
        {
            error = "Generated texture is null.";
            return false;
        }

        RectInt sourceRegion = ResolveSourceRegion(sourceTexture, preferredRegion, regionPadding);
        if (sourceRegion.width <= 0 || sourceRegion.height <= 0)
        {
            error = "Sticker extraction region is empty.";
            return false;
        }

        Color32[] sourcePixels;
        try
        {
            sourcePixels = sourceTexture.GetPixels32();
        }
        catch (Exception ex)
        {
            error = $"Failed to read generated pixels: {ex.Message}";
            return false;
        }

        Color32[] regionPixels = CopyRegion(sourcePixels, sourceTexture.width, sourceRegion);
        if (regionPixels.Length != sourceRegion.width * sourceRegion.height)
        {
            error = "Failed to copy sticker extraction region.";
            return false;
        }

        Color backgroundColor = EstimateBorderBackground(regionPixels, sourceRegion.width, sourceRegion.height);
        RemoveConnectedBackground(
            regionPixels,
            sourceRegion.width,
            sourceRegion.height,
            backgroundColor,
            Mathf.Clamp01(backgroundTolerance),
            out RectInt opaqueBounds);

        if (opaqueBounds.width <= 0 || opaqueBounds.height <= 0)
        {
            error = "Object extraction removed the full candidate image.";
            return false;
        }

        RectInt trimmedBounds = ExpandRegion(
            opaqueBounds,
            Mathf.Max(0, trimPadding),
            sourceRegion.width,
            sourceRegion.height);
        Color32[] trimmedPixels = CopyRegion(regionPixels, sourceRegion.width, trimmedBounds);
        if (trimmedPixels.Length != trimmedBounds.width * trimmedBounds.height)
        {
            error = "Failed to trim sticker pixels.";
            return false;
        }

        stickerTexture = new Texture2D(trimmedBounds.width, trimmedBounds.height, TextureFormat.RGBA32, false)
        {
            name = $"{sourceTexture.name}_Sticker",
            filterMode = filterMode,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = RuntimeHideFlags
        };
        stickerTexture.SetPixels32(trimmedPixels);
        stickerTexture.Apply(updateMipmaps: false, makeNoLongerReadable: false);

        placementRegion = new RectInt(
            sourceRegion.x + trimmedBounds.x,
            sourceRegion.y + trimmedBounds.y,
            trimmedBounds.width,
            trimmedBounds.height);
        return true;
    }

    private static RectInt ResolveSourceRegion(Texture2D sourceTexture, RectInt preferredRegion, int regionPadding)
    {
        if (preferredRegion.width <= 0 || preferredRegion.height <= 0)
        {
            return new RectInt(0, 0, sourceTexture.width, sourceTexture.height);
        }

        return ExpandRegion(
            preferredRegion,
            Mathf.Max(0, regionPadding),
            sourceTexture.width,
            sourceTexture.height);
    }

    private static void RemoveConnectedBackground(
        Color32[] pixels,
        int width,
        int height,
        Color backgroundColor,
        float tolerance,
        out RectInt opaqueBounds)
    {
        float toleranceSqr = tolerance * tolerance * 3f;
        var visited = new bool[pixels.Length];
        var queue = new Queue<int>(width + height);

        EnqueueBorderBackgroundPixels(pixels, width, height, backgroundColor, toleranceSqr, visited, queue);
        while (queue.Count > 0)
        {
            int index = queue.Dequeue();
            int x = index % width;
            int y = index / width;

            pixels[index] = new Color32(255, 255, 255, 0);

            TryVisitNeighbour(index - 1, x > 0);
            TryVisitNeighbour(index + 1, x < width - 1);
            TryVisitNeighbour(index - width, y > 0);
            TryVisitNeighbour(index + width, y < height - 1);
        }

        int minX = width;
        int minY = height;
        int maxX = -1;
        int maxY = -1;
        for (int y = 0; y < height; y++)
        {
            int rowOffset = y * width;
            for (int x = 0; x < width; x++)
            {
                if (pixels[rowOffset + x].a == 0)
                {
                    continue;
                }

                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
            }
        }

        opaqueBounds = maxX < minX || maxY < minY
            ? default
            : new RectInt(minX, minY, (maxX - minX) + 1, (maxY - minY) + 1);
        return;

        void TryVisitNeighbour(int neighbourIndex, bool inBounds)
        {
            if (!inBounds || visited[neighbourIndex])
            {
                return;
            }

            visited[neighbourIndex] = true;
            if (ColorDistanceSqr(pixels[neighbourIndex], backgroundColor) <= toleranceSqr)
            {
                queue.Enqueue(neighbourIndex);
            }
        }
    }

    private static void EnqueueBorderBackgroundPixels(
        Color32[] pixels,
        int width,
        int height,
        Color backgroundColor,
        float toleranceSqr,
        bool[] visited,
        Queue<int> queue)
    {
        for (int x = 0; x < width; x++)
        {
            TryEnqueueBorderIndex(x);
            TryEnqueueBorderIndex(((height - 1) * width) + x);
        }

        for (int y = 1; y < height - 1; y++)
        {
            TryEnqueueBorderIndex(y * width);
            TryEnqueueBorderIndex((y * width) + width - 1);
        }

        return;

        void TryEnqueueBorderIndex(int index)
        {
            if (index < 0 || index >= pixels.Length || visited[index])
            {
                return;
            }

            visited[index] = true;
            if (ColorDistanceSqr(pixels[index], backgroundColor) <= toleranceSqr)
            {
                queue.Enqueue(index);
            }
        }
    }

    private static Color EstimateBorderBackground(Color32[] pixels, int width, int height)
    {
        if (pixels == null || pixels.Length == 0 || width <= 0 || height <= 0)
        {
            return Color.white;
        }

        double r = 0d;
        double g = 0d;
        double b = 0d;
        int count = 0;

        for (int x = 0; x < width; x++)
        {
            Accumulate(pixels[x]);
            Accumulate(pixels[((height - 1) * width) + x]);
        }

        for (int y = 1; y < height - 1; y++)
        {
            Accumulate(pixels[y * width]);
            Accumulate(pixels[(y * width) + width - 1]);
        }

        if (count == 0)
        {
            return Color.white;
        }

        float scale = 1f / (count * 255f);
        return new Color(
            (float)r * scale,
            (float)g * scale,
            (float)b * scale,
            1f);

        void Accumulate(Color32 color)
        {
            r += color.r;
            g += color.g;
            b += color.b;
            count++;
        }
    }

    private static RectInt ExpandRegion(RectInt region, int padding, int width, int height)
    {
        int minX = Mathf.Clamp(region.xMin - padding, 0, Mathf.Max(0, width - 1));
        int minY = Mathf.Clamp(region.yMin - padding, 0, Mathf.Max(0, height - 1));
        int maxX = Mathf.Clamp(region.xMax + padding, 0, width);
        int maxY = Mathf.Clamp(region.yMax + padding, 0, height);
        return new RectInt(minX, minY, Mathf.Max(0, maxX - minX), Mathf.Max(0, maxY - minY));
    }

    private static Color32[] CopyRegion(Color32[] sourcePixels, int sourceWidth, RectInt region)
    {
        if (sourcePixels == null || sourceWidth <= 0 || region.width <= 0 || region.height <= 0)
        {
            return Array.Empty<Color32>();
        }

        var copy = new Color32[region.width * region.height];
        for (int y = 0; y < region.height; y++)
        {
            int sourceIndex = ((region.y + y) * sourceWidth) + region.x;
            int destinationIndex = y * region.width;
            Array.Copy(sourcePixels, sourceIndex, copy, destinationIndex, region.width);
        }

        return copy;
    }

    private static float ColorDistanceSqr(Color32 left, Color backgroundColor)
    {
        float dr = (left.r / 255f) - backgroundColor.r;
        float dg = (left.g / 255f) - backgroundColor.g;
        float db = (left.b / 255f) - backgroundColor.b;
        return (dr * dr) + (dg * dg) + (db * db);
    }
}
