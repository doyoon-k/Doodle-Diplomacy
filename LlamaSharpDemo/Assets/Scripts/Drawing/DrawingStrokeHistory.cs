using System;
using UnityEngine;

public sealed class DrawingStrokeHistory
{
    private bool _isActive;
    private RectInt _region;
    private Color32[] _beforePixels;

    public void Begin(DrawingCanvas canvas)
    {
        if (canvas == null)
        {
            Reset();
            return;
        }

        _isActive = true;
        _region = default;
        _beforePixels = null;
    }

    public void CaptureSegmentBeforeChange(DrawingCanvas canvas, Vector2Int from, Vector2Int to, int brushRadius)
    {
        if (canvas == null || !_isActive)
        {
            return;
        }

        RectInt segmentRegion = canvas.GetLineBounds(from, to, brushRadius);
        if (segmentRegion.width <= 0 || segmentRegion.height <= 0)
        {
            return;
        }

        if (_beforePixels == null)
        {
            _region = segmentRegion;
            _beforePixels = canvas.CopyRegion(segmentRegion);
            return;
        }

        RectInt expandedRegion = UnionRegions(_region, segmentRegion);
        if (expandedRegion.Equals(_region))
        {
            return;
        }

        Color32[] expandedBeforePixels = canvas.CopyRegion(expandedRegion);
        CopyRegionPixels(_beforePixels, _region, expandedBeforePixels, expandedRegion);
        _region = expandedRegion;
        _beforePixels = expandedBeforePixels;
    }

    public bool TryFinalize(DrawingCanvas canvas, out RectInt region, out Color32[] beforePixels, out Color32[] afterPixels)
    {
        region = default;
        beforePixels = null;
        afterPixels = null;

        if (!_isActive || _beforePixels == null || canvas == null)
        {
            Reset();
            return false;
        }

        region = _region;
        beforePixels = _beforePixels;
        afterPixels = canvas.CopyRegion(_region);
        Reset();
        return true;
    }

    public void Reset()
    {
        _isActive = false;
        _region = default;
        _beforePixels = null;
    }

    private static RectInt UnionRegions(RectInt left, RectInt right)
    {
        int minX = Mathf.Min(left.xMin, right.xMin);
        int minY = Mathf.Min(left.yMin, right.yMin);
        int maxX = Mathf.Max(left.xMax, right.xMax);
        int maxY = Mathf.Max(left.yMax, right.yMax);
        return new RectInt(minX, minY, maxX - minX, maxY - minY);
    }

    private static void CopyRegionPixels(
        Color32[] sourcePixels,
        RectInt sourceRegion,
        Color32[] destinationPixels,
        RectInt destinationRegion)
    {
        int offsetX = sourceRegion.xMin - destinationRegion.xMin;
        int offsetY = sourceRegion.yMin - destinationRegion.yMin;

        for (int y = 0; y < sourceRegion.height; y++)
        {
            int sourceIndex = y * sourceRegion.width;
            int destinationIndex = ((offsetY + y) * destinationRegion.width) + offsetX;
            Array.Copy(sourcePixels, sourceIndex, destinationPixels, destinationIndex, sourceRegion.width);
        }
    }
}
