using System;
using UnityEngine;

/// <summary>
/// Runtime-owned transparent sticker candidate generated from the current sketch guide.
/// </summary>
public sealed class DrawingStickerCandidate : IDisposable
{
    private const HideFlags RuntimeHideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;

    public DrawingStickerCandidate(
        Texture2D texture,
        RectInt placementRegion,
        string sourceImagePath,
        string promptText,
        int seed)
    {
        Texture = texture;
        PlacementRegion = placementRegion;
        SourceImagePath = sourceImagePath ?? string.Empty;
        PromptText = promptText ?? string.Empty;
        Seed = seed;

        if (Texture != null)
        {
            Texture.hideFlags = RuntimeHideFlags;
        }
    }

    public Texture2D Texture { get; private set; }
    public RectInt PlacementRegion { get; }
    public string SourceImagePath { get; }
    public string PromptText { get; }
    public int Seed { get; }

    public string GetDebugLabel()
    {
        string seedLabel = Seed > 0 ? $"seed {Seed}" : "seed n/a";
        return string.IsNullOrWhiteSpace(PromptText)
            ? seedLabel
            : $"{PromptText.Trim()} ({seedLabel})";
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

        Texture = null;
    }
}
