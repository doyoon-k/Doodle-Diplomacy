using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Stores undo/redo entries using the smallest changed pixel region for each action.
/// </summary>
public sealed class DrawingHistory
{
    private readonly struct HistoryEntry
    {
        public HistoryEntry(RectInt region, Color32[] beforePixels, Color32[] afterPixels)
        {
            Region = region;
            BeforePixels = beforePixels;
            AfterPixels = afterPixels;
        }

        public RectInt Region { get; }
        public Color32[] BeforePixels { get; }
        public Color32[] AfterPixels { get; }
    }

    private readonly int _maxEntries;
    private readonly List<HistoryEntry> _undoEntries = new();
    private readonly List<HistoryEntry> _redoEntries = new();

    public DrawingHistory(int maxEntries)
    {
        _maxEntries = Mathf.Max(1, maxEntries);
    }

    public bool CanUndo => _undoEntries.Count > 0;
    public bool CanRedo => _redoEntries.Count > 0;

    public void Clear()
    {
        _undoEntries.Clear();
        _redoEntries.Clear();
    }

    public bool Record(RectInt region, Color32[] beforePixels, Color32[] afterPixels)
    {
        if (region.width <= 0 || region.height <= 0)
        {
            return false;
        }

        int expectedLength = region.width * region.height;
        if (beforePixels == null ||
            afterPixels == null ||
            beforePixels.Length != expectedLength ||
            afterPixels.Length != expectedLength)
        {
            return false;
        }

        if (PixelsMatch(beforePixels, afterPixels))
        {
            return false;
        }

        _undoEntries.Add(new HistoryEntry(region, beforePixels, afterPixels));
        _redoEntries.Clear();

        if (_undoEntries.Count > _maxEntries)
        {
            _undoEntries.RemoveAt(0);
        }

        return true;
    }

    public bool Undo(DrawingCanvas canvas)
    {
        if (!CanUndo || canvas == null)
        {
            return false;
        }

        int lastIndex = _undoEntries.Count - 1;
        HistoryEntry entry = _undoEntries[lastIndex];
        _undoEntries.RemoveAt(lastIndex);

        canvas.ApplyRegion(entry.Region, entry.BeforePixels);
        _redoEntries.Add(entry);
        return true;
    }

    public bool Redo(DrawingCanvas canvas)
    {
        if (!CanRedo || canvas == null)
        {
            return false;
        }

        int lastIndex = _redoEntries.Count - 1;
        HistoryEntry entry = _redoEntries[lastIndex];
        _redoEntries.RemoveAt(lastIndex);

        canvas.ApplyRegion(entry.Region, entry.AfterPixels);
        _undoEntries.Add(entry);
        return true;
    }

    private static bool PixelsMatch(Color32[] beforePixels, Color32[] afterPixels)
    {
        for (int i = 0; i < beforePixels.Length; i++)
        {
            if (!ColorsEqual(beforePixels[i], afterPixels[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ColorsEqual(Color32 left, Color32 right)
    {
        return left.r == right.r &&
               left.g == right.g &&
               left.b == right.b &&
               left.a == right.a;
    }
}
