using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Editor-only helper that resolves declared read/write keys for custom link types.
/// </summary>
internal static class CustomLinkStateResolver
{
    public static bool TryResolve(PromptPipelineStep step, out List<string> writes)
    {
        writes = new List<string>();

        if (step == null || string.IsNullOrWhiteSpace(step.customLinkTypeName))
        {
            return false;
        }

        try
        {
            // Use the asset's instantiation logic so we handle parameters/assets correctly.
            if (PromptPipelineAsset.InstantiateCustomLink(step) is not ICustomLinkStateProvider provider)
            {
                return false;
            }

            writes = Normalize(provider.GetWrites());
            return writes.Count > 0;
        }
        catch (Exception ex)
        {
            // It's common for instantiation to fail if params aren't fully set up yet in editor.
            // Just log a warning or ignore.
            // Debug.LogWarning($"CustomLinkStateResolver: Failed to resolve state for '{step.customLinkTypeName}': {ex.Message}");
            return false;
        }
    }

    private static List<string> Normalize(IEnumerable<string> source)
    {
        if (source == null)
        {
            return new List<string>();
        }

        return source
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();
    }
}
