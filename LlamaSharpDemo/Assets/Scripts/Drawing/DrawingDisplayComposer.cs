using UnityEngine;

internal static class DrawingDisplayComposer
{
    public static void RefreshFullCanvas(
        DrawingCanvas sourceCanvas,
        DrawingCanvas displayCanvas,
        Rect normalizedPaintArea,
        float paintAreaDividerWidthNormalized,
        Color nonPaintAreaDisplayColor,
        Color paintAreaDividerColor,
        DrawingSurfaceTextureSampler surfaceTextureSampler)
    {
        if (sourceCanvas == null)
        {
            return;
        }

        RefreshRegion(
            sourceCanvas,
            displayCanvas,
            new RectInt(0, 0, sourceCanvas.Width, sourceCanvas.Height),
            normalizedPaintArea,
            paintAreaDividerWidthNormalized,
            nonPaintAreaDisplayColor,
            paintAreaDividerColor,
            surfaceTextureSampler);
    }

    public static void RefreshRegion(
        DrawingCanvas sourceCanvas,
        DrawingCanvas displayCanvas,
        RectInt region,
        Rect normalizedPaintArea,
        float paintAreaDividerWidthNormalized,
        Color nonPaintAreaDisplayColor,
        Color paintAreaDividerColor,
        DrawingSurfaceTextureSampler surfaceTextureSampler)
    {
        if (sourceCanvas == null || displayCanvas == null || region.width <= 0 || region.height <= 0)
        {
            return;
        }

        Color32[] compositePixels = sourceCanvas.CopyRegion(region);
        if (compositePixels.Length != region.width * region.height)
        {
            return;
        }

        Rect paintArea = GetClampedPaintArea(normalizedPaintArea);
        int dividerPixelWidth = Mathf.Max(1, Mathf.RoundToInt(sourceCanvas.Width * paintAreaDividerWidthNormalized));
        int dividerStart = Mathf.Clamp(Mathf.RoundToInt(paintArea.xMin * sourceCanvas.Width) - dividerPixelWidth, 0, sourceCanvas.Width);
        int dividerEnd = Mathf.Clamp(Mathf.RoundToInt(paintArea.xMin * sourceCanvas.Width) + dividerPixelWidth, 0, sourceCanvas.Width);
        Color32 nonPaintColor32 = nonPaintAreaDisplayColor;
        Color32 dividerColor32 = paintAreaDividerColor;

        for (int localY = 0; localY < region.height; localY++)
        {
            int absoluteY = region.y + localY;
            float canvasV = (absoluteY + 0.5f) / sourceCanvas.Height;
            for (int localX = 0; localX < region.width; localX++)
            {
                int absoluteX = region.x + localX;
                int pixelIndex = (localY * region.width) + localX;
                float canvasU = (absoluteX + 0.5f) / sourceCanvas.Width;

                if (!paintArea.Contains(new Vector2(canvasU, canvasV)))
                {
                    compositePixels[pixelIndex] = surfaceTextureSampler != null &&
                                                  surfaceTextureSampler.TrySample(canvasU, canvasV, out Color32 originalColor)
                        ? originalColor
                        : nonPaintColor32;
                    continue;
                }

                if (absoluteX >= dividerStart && absoluteX < dividerEnd)
                {
                    compositePixels[pixelIndex] = dividerColor32;
                }
            }
        }

        displayCanvas.ApplyRegion(region, compositePixels);
    }

    private static Rect GetClampedPaintArea(Rect normalizedPaintArea)
    {
        float x = Mathf.Clamp01(normalizedPaintArea.x);
        float y = Mathf.Clamp01(normalizedPaintArea.y);
        float width = Mathf.Clamp(normalizedPaintArea.width, 0.01f, 1f - x);
        float height = Mathf.Clamp(normalizedPaintArea.height, 0.01f, 1f - y);
        return new Rect(x, y, width, height);
    }
}
