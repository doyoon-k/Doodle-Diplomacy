using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runtime pixel buffer backing the drawing board texture.
/// </summary>
public sealed class DrawingCanvas : IDisposable
{
    private const HideFlags RuntimeHideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
    private readonly Color32[] _pixels;

    public DrawingCanvas(int width, int height, Color backgroundColor, FilterMode filterMode)
    {
        Width = Mathf.Max(1, width);
        Height = Mathf.Max(1, height);
        BackgroundColor = (Color32)backgroundColor;

        Texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false)
        {
            name = "DrawingCanvasTexture",
            filterMode = filterMode,
            wrapMode = TextureWrapMode.Clamp
        };
        Texture.hideFlags = RuntimeHideFlags;

        _pixels = new Color32[Width * Height];
        FillAll(BackgroundColor);
        ApplyDirtyRect(0, 0, Width - 1, Height - 1);
    }

    public int Width { get; }
    public int Height { get; }
    public Color32 BackgroundColor { get; }
    public Texture2D Texture { get; }

    public Color32[] CopyPixels()
    {
        var copy = new Color32[_pixels.Length];
        Array.Copy(_pixels, copy, _pixels.Length);
        return copy;
    }

    public Color32[] CopyRegion(RectInt region)
    {
        if (!TryClampRegion(region, out RectInt clampedRegion))
        {
            return Array.Empty<Color32>();
        }

        var copy = new Color32[clampedRegion.width * clampedRegion.height];
        for (int y = 0; y < clampedRegion.height; y++)
        {
            int sourceIndex = ((clampedRegion.y + y) * Width) + clampedRegion.x;
            int destinationIndex = y * clampedRegion.width;
            Array.Copy(_pixels, sourceIndex, copy, destinationIndex, clampedRegion.width);
        }

        return copy;
    }

    public RectInt GetLineBounds(Vector2Int from, Vector2Int to, int radius)
    {
        int brushRadius = Mathf.Max(1, radius);
        int minX = Mathf.Clamp(Mathf.Min(from.x, to.x) - brushRadius, 0, Width - 1);
        int minY = Mathf.Clamp(Mathf.Min(from.y, to.y) - brushRadius, 0, Height - 1);
        int maxX = Mathf.Clamp(Mathf.Max(from.x, to.x) + brushRadius, 0, Width - 1);
        int maxY = Mathf.Clamp(Mathf.Max(from.y, to.y) + brushRadius, 0, Height - 1);
        return new RectInt(minX, minY, (maxX - minX) + 1, (maxY - minY) + 1);
    }

    public bool TryGetNonBackgroundBounds(out RectInt region)
    {
        int minX = Width;
        int minY = Height;
        int maxX = -1;
        int maxY = -1;

        for (int y = 0; y < Height; y++)
        {
            int rowOffset = y * Width;
            for (int x = 0; x < Width; x++)
            {
                if (ColorsEqual(_pixels[rowOffset + x], BackgroundColor))
                {
                    continue;
                }

                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
            }
        }

        if (maxX < minX || maxY < minY)
        {
            region = default;
            return false;
        }

        region = new RectInt(minX, minY, (maxX - minX) + 1, (maxY - minY) + 1);
        return true;
    }

    public void Clear()
    {
        FillAll(BackgroundColor);
        ApplyDirtyRect(0, 0, Width - 1, Height - 1);
    }

    public bool DrawLine(Vector2Int from, Vector2Int to, Color color, int radius, out RectInt dirtyRegion)
    {
        int brushRadius = Mathf.Max(1, radius);
        Color32 brushColor = (Color32)color;

        int minX = Width;
        int minY = Height;
        int maxX = -1;
        int maxY = -1;
        bool changed = false;

        float distance = Vector2Int.Distance(from, to);
        int steps = Mathf.Max(1, Mathf.CeilToInt(distance));

        for (int i = 0; i <= steps; i++)
        {
            float t = steps == 0 ? 0f : i / (float)steps;
            int x = Mathf.RoundToInt(Mathf.Lerp(from.x, to.x, t));
            int y = Mathf.RoundToInt(Mathf.Lerp(from.y, to.y, t));
            changed |= DrawDisc(x, y, brushRadius, brushColor, ref minX, ref minY, ref maxX, ref maxY);
        }

        if (!changed)
        {
            dirtyRegion = default;
            return false;
        }

        dirtyRegion = new RectInt(minX, minY, (maxX - minX) + 1, (maxY - minY) + 1);
        ApplyDirtyRect(minX, minY, maxX, maxY);
        return true;
    }

    public bool FloodFill(
        Vector2Int startPixel,
        Color color,
        out RectInt dirtyRegion,
        out Color32[] beforePixels,
        out Color32[] afterPixels,
        RectInt? fillBounds = null)
    {
        dirtyRegion = default;
        beforePixels = null;
        afterPixels = null;

        int fillMinX = 0;
        int fillMinY = 0;
        int fillMaxX = Width - 1;
        int fillMaxY = Height - 1;
        if (fillBounds.HasValue)
        {
            if (!TryClampRegion(fillBounds.Value, out RectInt clampedBounds))
            {
                return false;
            }

            fillMinX = clampedBounds.xMin;
            fillMinY = clampedBounds.yMin;
            fillMaxX = clampedBounds.xMax - 1;
            fillMaxY = clampedBounds.yMax - 1;
        }

        int startX = Mathf.Clamp(startPixel.x, fillMinX, fillMaxX);
        int startY = Mathf.Clamp(startPixel.y, fillMinY, fillMaxY);
        if (startPixel.x < fillMinX || startPixel.x > fillMaxX ||
            startPixel.y < fillMinY || startPixel.y > fillMaxY)
        {
            return false;
        }

        int startIndex = (startY * Width) + startX;

        Color32 targetColor = _pixels[startIndex];
        Color32 fillColor = (Color32)color;
        if (ColorsEqual(targetColor, fillColor))
        {
            return false;
        }

        int minX = startX;
        int minY = startY;
        int maxX = startX;
        int maxY = startY;
        bool changed = false;
        var pixelsToVisit = new Stack<int>();
        var changedIndices = new List<int>();
        pixelsToVisit.Push(startIndex);

        while (pixelsToVisit.Count > 0)
        {
            int index = pixelsToVisit.Pop();
            if (!ColorsEqual(_pixels[index], targetColor))
            {
                continue;
            }

            int x = index % Width;
            int y = index / Width;

            _pixels[index] = fillColor;
            changed = true;
            changedIndices.Add(index);

            if (x < minX) minX = x;
            if (y < minY) minY = y;
            if (x > maxX) maxX = x;
            if (y > maxY) maxY = y;

            if (x > fillMinX)
            {
                pixelsToVisit.Push(index - 1);
            }

            if (x < fillMaxX)
            {
                pixelsToVisit.Push(index + 1);
            }

            if (y > fillMinY)
            {
                pixelsToVisit.Push(index - Width);
            }

            if (y < fillMaxY)
            {
                pixelsToVisit.Push(index + Width);
            }
        }

        if (!changed)
        {
            return false;
        }

        dirtyRegion = new RectInt(minX, minY, (maxX - minX) + 1, (maxY - minY) + 1);
        afterPixels = CopyRegion(dirtyRegion);
        beforePixels = new Color32[afterPixels.Length];
        Array.Copy(afterPixels, beforePixels, afterPixels.Length);

        for (int i = 0; i < changedIndices.Count; i++)
        {
            int index = changedIndices[i];
            int x = index % Width;
            int y = index / Width;
            int localIndex = ((y - dirtyRegion.y) * dirtyRegion.width) + (x - dirtyRegion.x);
            beforePixels[localIndex] = targetColor;
        }

        ApplyDirtyRect(minX, minY, maxX, maxY);
        return true;
    }

    public void Dispose()
    {
        if (Texture == null)
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

    public void ApplyRegion(RectInt region, Color32[] colors)
    {
        if (colors == null)
        {
            return;
        }

        if (region.width <= 0 || region.height <= 0)
        {
            return;
        }

        if (colors.Length != region.width * region.height)
        {
            Debug.LogWarning("[DrawingCanvas] Region pixel count does not match region size.");
            return;
        }

        int clampedX = Mathf.Clamp(region.xMin, 0, Width - 1);
        int clampedY = Mathf.Clamp(region.yMin, 0, Height - 1);
        int maxX = Mathf.Clamp(region.xMax, 0, Width);
        int maxY = Mathf.Clamp(region.yMax, 0, Height);
        int clampedWidth = maxX - clampedX;
        int clampedHeight = maxY - clampedY;

        if (clampedWidth <= 0 || clampedHeight <= 0)
        {
            return;
        }

        for (int y = 0; y < clampedHeight; y++)
        {
            int sourceIndex = y * region.width;
            int destinationIndex = ((clampedY + y) * Width) + clampedX;
            Array.Copy(colors, sourceIndex, _pixels, destinationIndex, clampedWidth);
        }

        ApplyDirtyRect(clampedX, clampedY, clampedX + clampedWidth - 1, clampedY + clampedHeight - 1);
    }

    private void FillAll(Color32 color)
    {
        for (int i = 0; i < _pixels.Length; i++)
        {
            _pixels[i] = color;
        }
    }

    private bool TryClampRegion(RectInt region, out RectInt clampedRegion)
    {
        clampedRegion = default;
        if (region.width <= 0 || region.height <= 0)
        {
            return false;
        }

        int minX = Mathf.Clamp(region.xMin, 0, Width - 1);
        int minY = Mathf.Clamp(region.yMin, 0, Height - 1);
        int maxX = Mathf.Clamp(region.xMax, 0, Width);
        int maxY = Mathf.Clamp(region.yMax, 0, Height);
        int clampedWidth = maxX - minX;
        int clampedHeight = maxY - minY;

        if (clampedWidth <= 0 || clampedHeight <= 0)
        {
            return false;
        }

        clampedRegion = new RectInt(minX, minY, clampedWidth, clampedHeight);
        return true;
    }

    private bool DrawDisc(
        int centerX,
        int centerY,
        int radius,
        Color32 brushColor,
        ref int minX,
        ref int minY,
        ref int maxX,
        ref int maxY)
    {
        int sqrRadius = radius * radius;
        bool changed = false;

        int startX = Mathf.Max(0, centerX - radius);
        int endX = Mathf.Min(Width - 1, centerX + radius);
        int startY = Mathf.Max(0, centerY - radius);
        int endY = Mathf.Min(Height - 1, centerY + radius);

        for (int y = startY; y <= endY; y++)
        {
            int dy = y - centerY;
            for (int x = startX; x <= endX; x++)
            {
                int dx = x - centerX;
                if ((dx * dx) + (dy * dy) > sqrRadius)
                {
                    continue;
                }

                int index = (y * Width) + x;
                if (ColorsEqual(_pixels[index], brushColor))
                {
                    continue;
                }

                _pixels[index] = brushColor;
                changed = true;

                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
            }
        }

        return changed;
    }

    private void ApplyDirtyRect(int minX, int minY, int maxX, int maxY)
    {
        int width = maxX - minX + 1;
        int height = maxY - minY + 1;
        var colors = new Color32[width * height];

        for (int y = 0; y < height; y++)
        {
            int sourceIndex = ((minY + y) * Width) + minX;
            int destinationIndex = y * width;
            Array.Copy(_pixels, sourceIndex, colors, destinationIndex, width);
        }

        Texture.SetPixels32(minX, minY, width, height, colors);
        Texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
    }

    private static bool ColorsEqual(Color32 left, Color32 right)
    {
        return left.r == right.r &&
               left.g == right.g &&
               left.b == right.b &&
               left.a == right.a;
    }
}
